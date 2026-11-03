"""Native Ollama adapter for the upstream smolagents Model interface.

ToolCallingAgent's text stop markers can truncate compatible endpoints before
their function call. Native /api/chat has structured calls and needs no markers.
Only provider-supplied function names/arguments are passed to the framework.
"""
import json
import requests
from smolagents.models import Model, ChatMessage, ChatMessageToolCall, ChatMessageToolCallFunction, TokenUsage


class OllamaToolModel(Model):
    def __init__(self, model_id):
        super().__init__(model_id=model_id, flatten_messages_as_text=True)
        self.session = requests.Session()
        self.session.trust_env = False
        self.sequence = 0
        self.truncated_calls = 0

    def generate(self, messages, stop_sequences=None, tools_to_call_from=None, **kwargs):
        prepared = self._prepare_completion_kwargs(messages=messages, tools_to_call_from=tools_to_call_from, stop_sequences=None)
        body = {"model": self.model_id, "messages": prepared["messages"], "stream": False,
                "options": {"temperature": 0.1, "num_predict": 8192 if self.model_id.endswith(":cloud") else 3000, "num_ctx": 32768}}
        if not self.model_id.endswith(":cloud"):
            body["think"] = False
        if tools_to_call_from:
            body["tools"] = prepared["tools"]
        response = self.session.post("http://127.0.0.1:11434/api/chat", json=body, timeout=120)
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
        return ChatMessage(role="assistant", content=message.get("content", ""), tool_calls=calls or None, raw=raw,
                           token_usage=TokenUsage(input_tokens=raw.get("prompt_eval_count", 0), output_tokens=raw.get("eval_count", 0)))
