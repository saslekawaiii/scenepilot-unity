"""Keep upstream smolagents orchestration with native Ollama tool/result memory.

ActionStep's default textual ``Calling tools: [python repr]`` memory is not the
native protocol of reasoning models. Record only outputs actually yielded by
upstream execution; never synthesize a successful observation.
"""
import json
from smolagents import ToolCallingAgent
from smolagents.agents import ToolOutput


class NativeOllamaToolCallingAgent(ToolCallingAgent):
    def process_tool_calls(self, chat_message, memory_step):
        self.model.record_native_turn(chat_message)
        pending = {call.id: call.function.name for call in chat_message.tool_calls}
        try:
            for event in super().process_tool_calls(chat_message, memory_step):
                if isinstance(event, ToolOutput):
                    self.model.record_native_result(event.id, event.tool_call.name, event.observation)
                    pending.pop(event.id, None)
                yield event
        except Exception as error:
            for call_id, name in pending.items():
                # Execution may have started before failing: do not claim that
                # it never ran or invent a result for an unreported parallel call.
                self.model.record_native_result(call_id, name, json.dumps({
                    "success": False, "resultUnavailable": True,
                    "error": type(error).__name__ + ": " + str(error)[:1200]}))
            raise
