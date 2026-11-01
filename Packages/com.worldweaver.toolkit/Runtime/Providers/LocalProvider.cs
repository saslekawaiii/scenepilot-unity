using System;
using System.Diagnostics;
using System.Text;
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
        private readonly OllamaStreamingTransport streaming = new OllamaStreamingTransport();
        public LocalProvider(ModelProfile profile) { profile.Validate(); this.profile = profile; }
        public async Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken token)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            token.ThrowIfCancellationRequested();
            var messages = new JArray();
            // Ollama's :cloud models do not support schema-constrained decoding.
            // Ground them in the exact contract, then keep the caller's unchanged
            // strict parser/validators. Never send the unsupported format object.
            bool cloud = profile.Model.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase);
            if (cloud && request.Schema != null)
                messages.Add(new JObject { ["role"] = "system", ["content"] = "Return ONLY one JSON INSTANCE matching this exact output contract, never Markdown or a schema. Every required field must be present. Contract: " + request.Schema.ToString(Newtonsoft.Json.Formatting.None) });
            int imageCount = 0, imageBytes = 0;
            foreach (var message in request.Messages)
            {
                var item = new JObject { ["role"] = message.Role, ["content"] = message.Content };
                if (message.Images != null && message.Images.Count > 0)
                {
                    var images = new JArray();
                    foreach (string encoded in message.Images)
                    {
                        if (++imageCount > 4 || string.IsNullOrWhiteSpace(encoded) || encoded.Length > 2796204) throw new ProviderException("Ollama accepts at most four bounded base64 images per request.");
                        byte[] decoded;
                        try { decoded = Convert.FromBase64String(encoded); }
                        catch (FormatException) { throw new ProviderException("Ollama image is not valid base64."); }
                        imageBytes += decoded.Length;
                        if (decoded.Length == 0 || decoded.Length > 2 * 1024 * 1024 || imageBytes > 6 * 1024 * 1024) throw new ProviderException("Ollama image payload is too large.");
                        images.Add(encoded);
                    }
                    item["images"] = images;
                }
                messages.Add(item);
            }
            var body = new JObject { ["model"] = profile.Model, ["messages"] = messages, ["stream"] = request.Stream, ["think"] = false,
                ["keep_alive"] = "10m",
                ["options"] = new JObject { ["temperature"] = profile.Temperature, ["num_predict"] = profile.MaxTokens, ["num_ctx"] = profile.ContextWindow } };
            if (!cloud) body["format"] = request.Schema != null ? (JToken)OllamaDecoderSchema.FromHost(request.Schema) : new JValue("json");
            var watch = Stopwatch.StartNew();
            JObject result; string content; long firstToken = 0;
            if (request.Stream)
            {
                var text = new StringBuilder();
                request.OnActivity?.Invoke("Ожидаю ответ локальной модели…");
                result = await streaming.SendAsync(profile.Endpoint.TrimEnd('/') + "/api/chat", body, profile.TimeoutSeconds, frame =>
                {
                    token.ThrowIfCancellationRequested();
                    string delta = (string)frame["message"]?["content"] ?? "";
                    if (delta.Length > 0)
                    {
                        if (text.Length == 0) firstToken = Math.Max(1, watch.ElapsedMilliseconds);
                        text.Append(delta); request.OnContentDelta?.Invoke(delta);
                    }
                    else if (frame["message"]?["thinking"]?.Type == JTokenType.String)
                        request.OnActivity?.Invoke("Локальная модель обрабатывает запрос…");
                }, token);
                content = text.ToString();
            }
            else
            {
                result = await transport.SendAsync(profile.Endpoint.TrimEnd('/') + "/api/chat", body, "", profile.TimeoutSeconds, profile.Retries, token);
                content = (string)result["message"]?["content"];
            }
            token.ThrowIfCancellationRequested();
            string finishReason = (string)result["done_reason"];
            bool truncated = CompletionResult.IsOutputLimit(finishReason);
            if (string.IsNullOrWhiteSpace(content) && !truncated) throw new ProviderException("Ollama returned no text.");
            int input = Counter(result, "prompt_eval_count"), output = Counter(result, "eval_count");
            double duration = Duration(result);
            return new CompletionResult { Text = content ?? "", Model = profile.Model, Provider = "Ollama", ElapsedMs = watch.ElapsedMilliseconds,
                FinishReason = finishReason, OutputTruncated = truncated,
                FirstTokenMs = firstToken, InputTokens = input, OutputTokens = output,
                TokensPerSecond = duration > 0 ? output * 1e9 / duration : 0 };
        }
        private static int Counter(JObject result, string name)
        {
            if (result[name] == null) return 0;
            if (result[name].Type != JTokenType.Integer || !int.TryParse(result[name].ToString(), out int value) || value < 0) throw new ProviderException("Malformed Ollama token statistics.");
            return value;
        }
        private static double Duration(JObject result)
        {
            if (result["eval_duration"] == null) return 0;
            if (result["eval_duration"].Type != JTokenType.Integer || !long.TryParse(result["eval_duration"].ToString(), out long value) || value < 0) throw new ProviderException("Malformed Ollama duration statistics.");
            return value;
        }
        public async Task<bool> HealthAsync(CancellationToken token)
        {
            var response = await transport.SendAsync(profile.Endpoint.TrimEnd('/') + "/api/tags", null, "", 10, 0, token);
            foreach (var model in response["models"] ?? new JArray()) if ((string)model["name"] == profile.Model || (string)model["name"] == profile.Model + ":latest") return true;
            return false;
        }
        public void Dispose() { streaming.Dispose(); transport.Dispose(); }
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
