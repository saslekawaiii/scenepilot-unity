using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldWeaver.Editor.Assistant
{
    public enum AssistantPower { Fast, Balanced, Deep }

    [Serializable]
    public sealed class ChatSettings
    {
        public string ModelKey = "ollama:qwen2.5-coder:latest";
        public AssistantPower Power = AssistantPower.Balanced;
        public bool AutoApply = true;
    }

    [Serializable]
    public sealed class AvailableModel
    {
        public string Key, Name, Description, Model;
        public ProviderKind Provider;
        public string Endpoint;
        public bool Available;
        public override string ToString() => Name;
    }

    [Serializable]
    public sealed class EditorCommand
    {
        public string tool = "";
        public string name = "";
        public string target = "";
        public JObject args = new JObject();
    }

    [Serializable]
    public sealed class EditorPlan
    {
        public string reply = "";
        public bool complete = true;
        public List<EditorCommand> actions = new List<EditorCommand>();

        public static EditorPlan Parse(string json)
        {
            var value = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (value.Properties().Any(p => p.Name != "reply" && p.Name != "complete" && p.Name != "actions") ||
                value["reply"]?.Type != JTokenType.String || value["complete"]?.Type != JTokenType.Boolean || value["actions"]?.Type != JTokenType.Array)
                throw new ArgumentException("Ответ должен содержать reply, complete и actions.");
            if (((JArray)value["actions"]).Count > 24) throw new ArgumentException("Не более 24 инструментов в одном шаге.");
            foreach (var action in (JArray)value["actions"])
            {
                if (!(action is JObject obj) || obj.Properties().Any(p => p.Name != "tool" && p.Name != "name" && p.Name != "target" && p.Name != "args") ||
                    obj["tool"]?.Type != JTokenType.String || obj["name"]?.Type != JTokenType.String ||
                    obj["target"]?.Type != JTokenType.String || obj["args"]?.Type != JTokenType.Object)
                    throw new ArgumentException("Неверный контракт инструмента.");
            }
            var result = value.ToObject<EditorPlan>();
            if (result.reply.Length > 12000 || result.actions.Any(a => a.tool.Length > 64 || a.name.Length > 160 || a.target.Length > 200 || a.args.ToString().Length > 40000))
                throw new ArgumentException("Превышен размер ответа инструмента.");
            return result;
        }

        public static JObject Schema(bool typed = true)
        {
            var command = new JObject {
                ["type"] = "object", ["additionalProperties"] = false,
                ["properties"] = new JObject {
                    ["tool"] = new JObject { ["type"] = "string" }, ["name"] = new JObject { ["type"] = "string" },
                    ["target"] = new JObject { ["type"] = "string" }, ["args"] = new JObject { ["type"] = "object" }
                }, ["required"] = new JArray("tool", "name", "target", "args")
            };
            if (typed) command = EditorToolSchema.DecoderCommandSchema();
            return new JObject {
                ["type"] = "object", ["additionalProperties"] = false,
                ["properties"] = new JObject {
                    ["reply"] = new JObject { ["type"] = "string" }, ["complete"] = new JObject { ["type"] = "boolean" },
                    ["actions"] = new JObject { ["type"] = "array", ["items"] = command }
                }, ["required"] = new JArray("reply", "complete", "actions")
            };
        }
    }

    [Serializable]
    public sealed class EditorCommandResult
    {
        public bool Success;
        public string Tool, Summary, Target, Error;
        public List<string> CreatedAssets = new List<string>();
    }

    [Serializable]
    public sealed class EditorBatchResult
    {
        public bool Success;
        public int UndoGroup = -1;
        public string Summary, Error;
        public List<EditorCommandResult> Results = new List<EditorCommandResult>();
    }

    [Serializable]
    public sealed class EditorPlanAttempt
    {
        public int Round;
        public string Response, Error, SchemaHash, Model;
        public long ProviderElapsedMs;
        public List<ChatMessage> Messages = new List<ChatMessage>();
        public bool Accepted;
    }

    [Serializable]
    public sealed class AssistantMessage
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Role = "assistant", Text = "", Status = "", Model = "";
        public string CreatedUtc = DateTime.UtcNow.ToString("o");
        public long ElapsedMs;
        public int InputTokens, OutputTokens;
        public List<EditorCommandResult> Steps = new List<EditorCommandResult>();
        public List<EditorPlanAttempt> Attempts = new List<EditorPlanAttempt>();
        public EditorPlan PendingPlan;
        public bool IsError;
    }

    [Serializable]
    public sealed class ChatSession
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "Новый чат";
        public string UpdatedUtc = DateTime.UtcNow.ToString("o");
        public ChatSettings Settings = new ChatSettings();
        public List<AssistantMessage> Messages = new List<AssistantMessage>();
    }
}
