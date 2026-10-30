using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WorldWeaver
{
    /// <summary>Native Ollama adapter with schema-constrained output and inference statistics.</summary>
    public sealed class LocalProvider : ILLMProvider
    {
        private readonly ModelProfile profile;
        private readonly JsonTransport transport = new JsonTransport();
        public LocalProvider(ModelProfile profile) { profile.Validate(); this.profile = profile; }
        public async Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken token)
        {
            var messages = new JArray();
            foreach (var message in request.Messages) messages.Add(new JObject { ["role"] = message.Role, ["content"] = message.Content });
            var body = new JObject { ["model"] = profile.Model, ["messages"] = messages, ["stream"] = false, ["think"] = false,
                ["format"] = request.Schema != null ? (JToken)request.Schema : new JValue("json"), ["keep_alive"] = "10m",
                ["options"] = new JObject { ["temperature"] = profile.Temperature, ["num_predict"] = profile.MaxTokens, ["num_ctx"] = 4096 } };
            var watch = Stopwatch.StartNew();
            var result = await transport.SendAsync(profile.Endpoint.TrimEnd('/') + "/api/chat", body, "", profile.TimeoutSeconds, profile.Retries, token);
            var content = (string)result["message"]?["content"];
            if (string.IsNullOrWhiteSpace(content)) throw new ProviderException("Ollama returned no text.");
            var duration = (double?)result["eval_duration"] ?? 0;
            return new CompletionResult { Text = content, Model = profile.Model, Provider = "Ollama", ElapsedMs = watch.ElapsedMilliseconds,
                InputTokens = (int?)result["prompt_eval_count"] ?? 0, OutputTokens = (int?)result["eval_count"] ?? 0,
                TokensPerSecond = duration > 0 ? ((int?)result["eval_count"] ?? 0) * 1e9 / duration : 0 };
        }
        public async Task<bool> HealthAsync(CancellationToken token)
        {
            var response = await transport.SendAsync(profile.Endpoint.TrimEnd('/') + "/api/tags", null, "", 10, 0, token);
            foreach (var model in response["models"] ?? new JArray()) if ((string)model["name"] == profile.Model || (string)model["name"] == profile.Model + ":latest") return true;
            return false;
        }
        public void Dispose() { transport.Dispose(); }
    }

    public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
    {
        private readonly string endpoint, model;
        private readonly JsonTransport transport = new JsonTransport();
        public string Identity => endpoint + "/" + model;
        public OllamaEmbeddingProvider(string endpoint, string model)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "http" || !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("Local embeddings require loopback HTTP.");
            this.endpoint = endpoint.TrimEnd('/'); this.model = model;
        }
        public async Task<float[]> EmbedAsync(string text, bool query, CancellationToken token)
        {
            string prefix = model.StartsWith("embeddinggemma", StringComparison.Ordinal)
                ? (query ? "task: search result | query: " : "title: Knowledge | text: ") : "";
            var result = await transport.SendAsync(endpoint + "/api/embed", new JObject { ["model"] = model, ["input"] = prefix + text, ["truncate"] = false, ["keep_alive"] = "10m" }, "", 60, 1, token);
            var vector = result["embeddings"]?[0]?.ToObject<float[]>();
            if (vector == null || vector.Length == 0) throw new ProviderException("No embedding returned.");
            foreach (float number in vector) if (float.IsNaN(number) || float.IsInfinity(number)) throw new ProviderException("Non-finite embedding.");
            return vector;
        }
        public void Dispose() { transport.Dispose(); }
    }
}
