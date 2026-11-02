using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldWeaver.Editor.Assistant
{
    public enum AssistantPower { Fast, Balanced, Deep }
    public enum AssistantMode { Ask, Plan, Execute }

    [Serializable]
    public sealed class ChatSettings
    {
        public string ModelKey = "ollama:qwen3.5:9b";
        public AssistantPower Power = AssistantPower.Balanced;
        public bool AutoApply = true;
        public AssistantMode Mode = AssistantMode.Execute;
        public bool UseSkills = true;
        public bool AutoVerify = true;
        public bool PreferGeneratedScripts = true;
        public bool UseNativeFunctions = true;
        public bool AllowEditorCode;
        public int SkillContextBudget = 4000;
        public List<string> SkillFolders = new List<string>();
    }

    [Serializable]
    public sealed class AvailableModel
    {
        public string Key, Name, Description, Model;
        public ProviderKind Provider;
        public string Endpoint;
        public bool Available;
        public bool SupportsVision;
        public bool Loaded, AutoDiscovered;
        public string ConnectionId, ApiKeyEnvironmentVariable;
        public OpenAIResponseMode ResponseMode;
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
        public string outcome = "";
        public List<string> blockers = new List<string>();
        public List<EditorCommand> actions = new List<EditorCommand>();

        public static EditorPlan Parse(string json)
        {
            var value = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (value.Properties().Any(p => p.Name != "reply" && p.Name != "complete" && p.Name != "actions" && p.Name != "outcome" && p.Name != "blockers") ||
                value["reply"]?.Type != JTokenType.String || value["complete"]?.Type != JTokenType.Boolean || value["actions"]?.Type != JTokenType.Array)
                throw new ArgumentException("Ответ должен содержать reply, complete и actions.");
            if (((JArray)value["actions"]).Count > 24) throw new ArgumentException("Не более 24 инструментов в одном шаге.");
            int index=0;
            foreach (var action in (JArray)value["actions"])
            {
                if (!(action is JObject obj))throw new ArgumentException("actions["+index+"] должен быть объектом {tool,name,target,args}.");
                foreach(var property in obj.Properties())
                    if(property.Name!="tool"&&property.Name!="name"&&property.Name!="target"&&property.Name!="args")throw new ArgumentException("Неизвестное поле actions["+index+"]."+property.Name+". Разрешены только tool,name,target,args.");
                foreach(string key in new[]{"tool","name","target","args"})
                    if(obj[key]?.Type!=(key=="args"?JTokenType.Object:JTokenType.String))throw new ArgumentException("Поле actions["+index+"]."+key+" обязательно и должно быть "+(key=="args"?"объектом":"строкой")+". Даже в команде чтения/правки указывай name:\"\" и target:\"\" явно, если они пустые.");
                index++;
            }
            var result = value.ToObject<EditorPlan>();
            if(value["outcome"]!=null&&(value["outcome"].Type!=JTokenType.String||!new[]{"","completed","blocked","partial"}.Contains(result.outcome)))throw new ArgumentException("Unknown terminal outcome.");
            if(value["blockers"]!=null&&(!(value["blockers"] is JArray reasons)||reasons.Count>8||reasons.Any(r=>r.Type!=JTokenType.String||((string)r).Length>1000)))throw new ArgumentException("Blockers must be at most eight bounded strings.");
            if(result.outcome=="blocked"||result.outcome=="partial")
                if(!result.complete||result.actions.Count!=0||result.blockers==null||result.blockers.Count==0||result.blockers.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("A blocked/partial terminal result needs explicit blockers and no actions.");
            if (result.reply.Length > 12000 || result.actions.Any(a => a.tool.Length > 64 || a.name.Length > 160 || a.target.Length > 200 || a.args.ToString().Length > 40000))
                throw new ArgumentException("Превышен размер ответа инструмента.");
            return result;
        }

        public static JObject Schema(bool typed = true, IEnumerable<string> allowedTools = null, bool nativeFunctions = false)
        {
            var command = new JObject {
                ["type"] = "object", ["additionalProperties"] = false,
                ["properties"] = new JObject {
                    ["tool"] = new JObject { ["type"] = "string" }, ["name"] = new JObject { ["type"] = "string" },
                    ["target"] = new JObject { ["type"] = "string" }, ["args"] = new JObject { ["type"] = "object" }
                }, ["required"] = new JArray("tool", "name", "target", "args")
            };
            if (allowedTools != null) command = EditorToolSchema.Restrict(allowedTools, typed);
            else if (typed) command = EditorToolSchema.DecoderCommandSchema();
            if(nativeFunctions)foreach(var branch in (command["anyOf"] as JArray??new JArray()).OfType<JObject>())
                branch["x-worldweaver-read-only"]=SceneToolExecutor.IsReadOnlyTool(EditorToolSchema.ToolName(branch))&&!WorldMaterialTools.IsAsync(EditorToolSchema.ToolName(branch));
            var schema = new JObject {
                ["type"] = "object", ["additionalProperties"] = false,
                ["properties"] = new JObject {
                    ["reply"] = new JObject { ["type"] = "string" }, ["complete"] = new JObject { ["type"] = "boolean" },
                    ["actions"] = new JObject { ["type"] = "array", ["items"] = command },
                    ["outcome"] = new JObject { ["type"]="string", ["enum"]=new JArray("","completed","blocked","partial") },
                    ["blockers"] = new JObject { ["type"]="array",["maxItems"]=8,["items"]=new JObject{["type"]="string",["maxLength"]=1000} }
                }, ["required"] = new JArray("reply", "complete", "actions")
            };
            if(!nativeFunctions){((JObject)schema["properties"]).Remove("outcome");((JObject)schema["properties"]).Remove("blockers");}
            return schema;
        }
    }

    [Serializable]
    public sealed class EditorCommandResult
    {
        public bool Success;
        public string Tool, Summary, Target, Error;
        public List<string> CreatedAssets = new List<string>();
        public Dictionary<string,string> SourceHashes = new Dictionary<string,string>();
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
        // Response remains the raw provider text for compatibility with older traces.
        // NormalizedResponse is recorded only for an explicit, narrowly scoped adapter repair.
        public string Response, RawResponse, WireResponse, NormalizedResponse, RepairKind, Error, SchemaHash, Model, FinishReason;
        public long ProviderElapsedMs;
        public List<ChatMessage> Messages = new List<ChatMessage>();
        public bool Accepted, OutputTruncated;
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
        public List<EditorResultReview> EditorReviews = new List<EditorResultReview>();
        public EditorPlan PendingPlan;
        public bool IsError;
        public AgentTaskState Task;
        public List<ChatAttachment> Attachments = new List<ChatAttachment>();
        public List<string> Skills = new List<string>();
        public long FirstTokenMs;
    }

    [Serializable]
    public sealed class ChatSession
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "Новый чат";
        public string UpdatedUtc = DateTime.UtcNow.ToString("o");
        public ChatSettings Settings = new ChatSettings();
        public List<AssistantMessage> Messages = new List<AssistantMessage>();
        public List<DesignNote> DesignMemory = new List<DesignNote>();
    }
}
