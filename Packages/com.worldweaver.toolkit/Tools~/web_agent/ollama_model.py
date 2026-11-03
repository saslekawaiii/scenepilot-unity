"""Native Ollama adapter for the upstream smolagents Model interface.

ToolCallingAgent's text stop markers can truncate compatible endpoints before
their function call. Native /api/chat has structured calls and needs no markers.
Only provider-supplied function names/arguments are passed to the framework.
"""
import json
import requests
from smolagents.models import Model, ChatMessage, ChatMessageToolCall, ChatMessageToolCallFunction, TokenUsage, get_tool_call_from_text


class OllamaToolModel(Model):
    def __init__(self, model_id):
        super().__init__(model_id=model_id, flatten_messages_as_text=True)
        self.session = requests.Session()
        self.session.trust_env = False
        self.sequence = 0
        self.truncated_calls = 0
        self.recovered_calls = 0
        self.initial_messages = None
        self.native_turns = []
        self.unprocessed_response = None

    def record_native_turn(self, message):
        calls = [{"id": call.id, "type": "function", "function": {
            "name": call.function.name, "arguments": call.function.arguments}}
                 for call in message.tool_calls]
        native = {"role": "assistant", "content": message.content or "", "tool_calls": calls}
        thinking = (message.raw or {}).get("message", {}).get("thinking")
        if isinstance(thinking, str) and thinking:
            native["thinking"] = thinking
        self.native_turns.append([native])
        self.unprocessed_response = None

    def record_native_result(self, call_id, name, content):
        self.native_turns[-1].append({"role": "tool", "tool_call_id": call_id,
                                    "tool_name": name, "content": content})

    def transport_messages(self, prepared):
        instructions = [m for m in prepared if m["role"] in ("system", "developer")]
        dialogue = [m for m in prepared if m["role"] not in ("system", "developer")]
        if self.initial_messages is None:
            self.initial_messages = instructions + dialogue
        if not self.native_turns:
            return instructions + dialogue
        if self.unprocessed_response is not None:
            message = self.unprocessed_response
            native = {"role": "assistant", "content": message.content or ""}
            thinking = (message.raw or {}).get("message", {}).get("thinking")
            if isinstance(thinking, str) and thinking:
                native["thinking"] = thinking
            self.native_turns.append([native, {"role": "user", "content":
                "The preceding response did not yield a valid executable tool call. No tool result was produced. Continue with an advertised native function."}])
            self.unprocessed_response = None
        # Drop whole old exchanges, never half a call/result or a prefix of its
        # continuation state. Upstream ActionStep audit remains unchanged.
        while len(self.native_turns) > 2 and len(json.dumps(self.native_turns)) > 90000:
            self.native_turns.pop(0)
        return self.initial_messages + [m for turn in self.native_turns for m in turn]

    def generate(self, messages, stop_sequences=None, tools_to_call_from=None, **kwargs):
        prepared = self._prepare_completion_kwargs(messages=messages, tools_to_call_from=tools_to_call_from, stop_sequences=None)
        body = {"model": self.model_id, "messages": self.transport_messages(prepared["messages"]), "stream": False,
                "options": {"temperature": 0.1, "num_predict": 8192 if self.model_id.endswith(":cloud") else 3000, "num_ctx": 32768}}
        if not self.model_id.endswith(":cloud"):
            body["think"] = False
        if tools_to_call_from:
            body["tools"] = prepared["tools"]
        response = self.session.post("http://127.0.0.1:11434/api/chat", json=body, timeout=(10, 240 if self.model_id.endswith(":cloud") else 120))
        response.raise_for_status()
        raw = response.json()
        if raw.get("done_reason") in ("length", "max_tokens", "max_output_tokens"):
            self.truncated_calls += 1
            raise ValueError("Inference reached its reasoning/output budget before a complete response. No tool from this response executed; narrow the next research step.")
        message = raw["message"]
        calls = []
        self.sequence += 1
        for index, call in enumerate(message.get("tool_calls", [])):
            function = call["function"]
            arguments = function["arguments"]
            if isinstance(arguments, str):
                arguments = json.loads(arguments)
            if not isinstance(arguments, dict):
                raise ValueError("Ollama tool arguments must be an object")
            calls.append(ChatMessageToolCall(id=call.get("id") or f"ollama-{self.sequence}-{index}", type="function",
                         function=ChatMessageToolCallFunction(name=function["name"], arguments=arguments)))
        # Compatible servers sometimes put the same function JSON in content
        # with model protocol suffixes. Use the upstream smolagents parser, then
        # accept only a currently advertised tool and an actual object payload.
        # Preserve raw unchanged for audit; never invent a name or arguments.
        if not calls and message.get("content") and tools_to_call_from:
            try:
                recovered = get_tool_call_from_text(message["content"], "name", "arguments")
                allowed = {tool["function"]["name"] for tool in prepared.get("tools", [])}
                if recovered.function.name not in allowed or not isinstance(recovered.function.arguments, dict):
                    raise ValueError("Text function is not an advertised object call")
                calls.append(recovered)
                self.recovered_calls += 1
            except (ValueError, KeyError, TypeError, AttributeError):
                pass
        result = ChatMessage(role="assistant", content=message.get("content", ""), tool_calls=calls or None, raw=raw,
                             token_usage=TokenUsage(input_tokens=raw.get("prompt_eval_count", 0), output_tokens=raw.get("eval_count", 0)))
        self.unprocessed_response = result
        return result
