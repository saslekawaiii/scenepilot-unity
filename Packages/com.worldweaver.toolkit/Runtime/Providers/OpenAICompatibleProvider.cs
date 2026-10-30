using System;
using System.Diagnostics;
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
            var messages = new JArray();
            foreach (var message in request.Messages) messages.Add(new JObject { ["role"] = message.Role, ["content"] = message.Content });
            var body = new JObject { ["model"] = profile.Model, ["messages"] = messages, ["temperature"] = profile.Temperature,
                [profile.Provider == ProviderKind.Groq ? "max_completion_tokens" : "max_tokens"] = profile.MaxTokens };
            body["response_format"] = profile.StrictSchema && request.Schema != null
                ? new JObject { ["type"] = "json_schema", ["json_schema"] = new JObject { ["name"] = "worldweaver", ["strict"] = true, ["schema"] = request.Schema } }
                : new JObject { ["type"] = "json_object" };
            if (profile.Provider == ProviderKind.Groq && profile.Model.StartsWith("openai/gpt-oss", StringComparison.Ordinal)) body["reasoning_effort"] = "low";
            var watch = Stopwatch.StartNew();
            var response = await transport.SendAsync(profile.Endpoint.TrimEnd('/') + "/chat/completions", body, Key, profile.TimeoutSeconds, profile.Retries, cancellationToken);
            var content = (string)response["choices"]?[0]?["message"]?["content"];
            if (string.IsNullOrWhiteSpace(content)) throw new ProviderException("Provider returned no text.");
            return new CompletionResult { Text = content, Provider = profile.Provider.ToString(), Model = profile.Model, ElapsedMs = watch.ElapsedMilliseconds,
                InputTokens = (int?)response["usage"]?["prompt_tokens"] ?? 0, OutputTokens = (int?)response["usage"]?["completion_tokens"] ?? 0 };
        }
        public async Task<bool> HealthAsync(CancellationToken token)
        {
            var response = await transport.SendAsync(profile.Endpoint.TrimEnd('/') + "/models", null, Key, profile.TimeoutSeconds, 0, token);
            foreach (var model in response["data"] ?? new JArray()) if ((string)model["id"] == profile.Model) return true;
            return false;
        }
        public void Dispose() { transport.Dispose(); }
    }
}
