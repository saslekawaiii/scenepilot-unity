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
            if (request.UseToolCalls) messages.Add(new JObject { ["role"] = "system", ["content"] = AgentFunctionProtocol.Instructions });
            if (!request.UseToolCalls && cloud && request.Schema != null)
                messages.Add(new JObject { ["role"] = "system", ["content"] = "Return ONLY one JSON INSTANCE matching this exact output contract, never Markdown or a schema. Every required field must be present. Contract: " + request.Schema.ToString(Newtonsoft.Json.Formatting.None) });
            int imageCount = 0, imageBytes = 0;
            foreach (var message in request.Messages)
            {
                var item = new JObject { ["role"] = message.Role, ["content"] = message.Content };
                if(message.ToolCalls!=null)item["tool_calls"]=message.ToolCalls.DeepClone();
                if(message.ToolName!=null)item["tool_name"]=message.ToolName;
                if(message.ToolCallId!=null)item["tool_call_id"]=message.ToolCallId;
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
            if (request.UseToolCalls) messages.Add(new JObject { ["role"] = "system", ["content"] = AgentFunctionProtocol.Instructions });
            var body = new JObject { ["model"] = profile.Model, ["messages"] = messages, ["stream"] = request.Stream, ["think"] = false,
                ["keep_alive"] = "10m",
                ["options"] = new JObject { ["temperature"] = profile.Temperature, ["num_predict"] = profile.MaxTokens, ["num_ctx"] = profile.ContextWindow } };
            // Cloud reasoning models use different think value types. Omitting the
            // override allows their native default, rather than disabling reasoning.
            if(cloud&&profile.EnableCloudReasoning)body.Remove("think");
            if (request.UseToolCalls) body["tools"] = AgentFunctionProtocol.Definitions(request.Schema);
            else if (!cloud) body["format"] = request.Schema != null ? (JToken)OllamaDecoderSchema.FromHost(request.Schema) : new JValue("json");
            var watch = Stopwatch.StartNew();
            JObject result; string content; long firstToken = 0;
            if (request.Stream)
            {
                var text = new StringBuilder();
                var calls = new JArray();
                request.OnActivity?.Invoke("Ожидаю ответ локальной модели…");
                for (int attempt = 0; ; attempt++)
                {
                    bool receivedFrame = false;
                    try
                    {
                        result = await streaming.SendAsync(profile.Endpoint.TrimEnd('/') + "/api/chat", body, profile.TimeoutSeconds, frame =>
                        {
                            receivedFrame = true;
                            token.ThrowIfCancellationRequested();
                            if (frame["message"]?["tool_calls"] is JArray frameCalls) foreach (var call in frameCalls) calls.Add(call.DeepClone());
                            string delta = (string)frame["message"]?["content"] ?? "";
                            if (delta.Length > 0)
                            {
                                if (text.Length == 0) firstToken = Math.Max(1, watch.ElapsedMilliseconds);
                                text.Append(delta); request.OnContentDelta?.Invoke(delta);
                            }
                            else if (frame["message"]?["thinking"]?.Type == JTokenType.String)
                                request.OnActivity?.Invoke("Локальная модель обрабатывает запрос…");
                        }, token);
                        break;
                    }
                    catch (ProviderException error) when (error.Transient && !receivedFrame && attempt < profile.Retries)
                    {
                        request.OnActivity?.Invoke("Соединение оборвалось до ответа. Повторяю запрос…");
                        await Task.Delay(500 * (1 << attempt), token);
                    }
                }
                if (request.UseToolCalls)
                {
                    result["message"] = new JObject { ["role"] = "assistant", ["content"] = text.ToString(), ["tool_calls"] = calls };
                    content = CompletionResult.IsOutputLimit((string)result["done_reason"]) ? text.ToString() : AgentFunctionProtocol.Normalize((JObject)result["message"], request.Schema);
                }
                else content = text.ToString();
            }
            else
            {
                result = await transport.SendAsync(profile.Endpoint.TrimEnd('/') + "/api/chat", body, "", profile.TimeoutSeconds, profile.Retries, token);
                content = request.UseToolCalls && !CompletionResult.IsOutputLimit((string)result["done_reason"])
                    ? AgentFunctionProtocol.Normalize((JObject)result["message"], request.Schema) : (string)result["message"]?["content"];
            }
            token.ThrowIfCancellationRequested();
            string finishReason = (string)result["done_reason"];
            bool truncated = CompletionResult.IsOutputLimit(finishReason);
            if (string.IsNullOrWhiteSpace(content) && !truncated) throw new ProviderException("Ollama returned no text.");
            int input = Counter(result, "prompt_eval_count"), output = Counter(result, "eval_count");
            double duration = Duration(result);
            return new CompletionResult { Text = content ?? "", Model = profile.Model, Provider = "Ollama", ElapsedMs = watch.ElapsedMilliseconds,
                WireResponse = request.UseToolCalls ? result["message"]?.ToString(Newtonsoft.Json.Formatting.None) : null,
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
            if(profile.Model.EndsWith(":cloud",StringComparison.OrdinalIgnoreCase))
            {
                var details=await transport.SendAsync(profile.Endpoint.TrimEnd('/')+"/api/show",new JObject{["model"]=profile.Model},"",10,0,token);
                foreach(string capability in details["capabilities"]??new JArray())if(capability=="completion")return true;
            }
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
