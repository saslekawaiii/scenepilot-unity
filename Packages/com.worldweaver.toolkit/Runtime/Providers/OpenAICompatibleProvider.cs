using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WorldWeaver
{
    /// <summary>Groq and explicitly configured OpenAI-compatible chat endpoints.</summary>
    public sealed class OpenAICompatibleProvider : ILLMProvider
    {
        private readonly ModelProfile profile;
        private readonly JsonTransport transport = new JsonTransport();
        private static readonly SemaphoreSlim freeGate = new SemaphoreSlim(1, 1);
        private static DateTime freeNext = DateTime.MinValue;
        public OpenAICompatibleProvider(ModelProfile profile) { profile.Validate(); this.profile = profile; }
        private string Key
        {
            get
            {
                if (string.IsNullOrEmpty(profile.ApiKeyEnvironmentVariable)) return "";
                var value = Environment.GetEnvironmentVariable(profile.ApiKeyEnvironmentVariable);
                if (string.IsNullOrWhiteSpace(value)) throw new ProviderException("Required environment variable is missing: " + profile.ApiKeyEnvironmentVariable);
                return value;
            }
        }
        public async Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken)
        {
            if (profile.Provider != ProviderKind.Pollinations) return await CompleteCoreAsync(request, cancellationToken);
            await freeGate.WaitAsync(cancellationToken);
            try
            {
                var delay = freeNext - DateTime.UtcNow;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
                freeNext = DateTime.UtcNow.AddSeconds(46);
                return await CompleteCoreAsync(request, cancellationToken);
            }
            finally { freeGate.Release(); }
        }
        private async Task<CompletionResult> CompleteCoreAsync(CompletionRequest request, CancellationToken cancellationToken)
        {
            var messages = new JArray();
            bool classicText = profile.Provider == ProviderKind.Pollinations && !profile.StrictSchema;
            if (classicText)
            {
                // Classic's JSON-object formatter can corrupt the first property of a
                // valid model answer. Its native grammar also rejects propertyNames.
                // Use ordinary text with an explicit instance contract, then apply
                // the unchanged strict parser and tool validator in the caller.
                string contract = "Return ONLY one valid JSON INSTANCE, never a JSON Schema or Markdown. Follow the JSON contract requested below, with every required field. ";
                if (request.Schema?["properties"]?["reply"] != null && request.Schema?["properties"]?["actions"] != null)
                    contract += "EditorPlan INSTANCE: {\"reply\":\"Russian response\",\"complete\":true,\"actions\":[{\"tool\":\"tool_name\",\"name\":\"object name or empty\",\"target\":\"existing target or empty\",\"args\":{}}]}. Use actions:[] for a finished non-editing answer. Each action requires tool,name,target,args. Use the tools and typed arguments described in these instructions. ";
                else if (request.Schema != null)
                    contract += "Required output fields: " + string.Join(", ", request.Schema["required"] ?? new JArray()) +
                        ". Fill an instance of this contract: " + request.Schema.ToString(Newtonsoft.Json.Formatting.None) + ". ";
                messages.Add(new JObject { ["role"] = "system", ["content"] = contract + "\n" + string.Join("\n\n", request.Messages.Where(message => message.Role == "system").Select(message => message.Content)) });
                foreach (var message in request.Messages.Where(message => message.Role != "system"))
                    messages.Add(new JObject { ["role"] = message.Role, ["content"] = message.Content });
            }
            else
            {
                if (!profile.StrictSchema && request.Schema != null)
                    messages.Add(new JObject { ["role"] = "system", ["content"] = "Return one JSON object matching exactly this schema, including every required field: " + request.Schema.ToString(Newtonsoft.Json.Formatting.None) });
                foreach (var message in request.Messages) messages.Add(new JObject { ["role"] = message.Role, ["content"] = message.Content });
            }
            var body = new JObject { ["model"] = profile.Model, ["messages"] = messages, ["temperature"] = profile.Temperature,
                [profile.Provider == ProviderKind.Groq ? "max_completion_tokens" : "max_tokens"] = profile.Provider == ProviderKind.Pollinations ? Math.Min(profile.MaxTokens, 1500) : profile.MaxTokens };
            if (!classicText)
                body["response_format"] = profile.StrictSchema && request.Schema != null
                    ? new JObject { ["type"] = "json_schema", ["json_schema"] = new JObject { ["name"] = "worldweaver", ["strict"] = true, ["schema"] = request.Schema } }
                    : new JObject { ["type"] = "json_object" };
            if (profile.Provider == ProviderKind.Groq && profile.Model.StartsWith("openai/gpt-oss", StringComparison.Ordinal)) body["reasoning_effort"] = "low";
            var watch = Stopwatch.StartNew();
            var suffix = profile.Provider == ProviderKind.Pollinations ? "/openai" : "/chat/completions";
            var response = await transport.SendAsync(profile.Endpoint.TrimEnd('/') + suffix, body, profile.Provider == ProviderKind.Pollinations ? "" : Key, profile.TimeoutSeconds, profile.Retries, cancellationToken);
            var content = (string)response["choices"]?[0]?["message"]?["content"];
            if (string.IsNullOrWhiteSpace(content)) throw new ProviderException("Provider returned no text.");
            return new CompletionResult { Text = content, Provider = profile.Provider.ToString(), Model = (string)response["model"] ?? profile.Model, ElapsedMs = watch.ElapsedMilliseconds,
                InputTokens = (int?)response["usage"]?["prompt_tokens"] ?? 0, OutputTokens = (int?)response["usage"]?["completion_tokens"] ?? 0 };
        }
        public async Task<bool> HealthAsync(CancellationToken token)
        {
            var response = await transport.SendAsync(profile.Endpoint.TrimEnd('/') + "/models", null, Key, profile.TimeoutSeconds, 0, token);
            foreach (var model in response["data"] ?? new JArray()) if ((string)model["id"] == profile.Model || (string)model["name"] == profile.Model) return true;
            return false;
        }
        public void Dispose() { transport.Dispose(); }
    }
}
