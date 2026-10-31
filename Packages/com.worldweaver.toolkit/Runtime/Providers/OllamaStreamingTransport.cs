using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldWeaver
{
    /// <summary>Native /api/chat NDJSON reader. Never replays a partially delivered stream.</summary>
    public sealed class OllamaStreamingTransport : IDisposable
    {
        public const int MaximumResponseBytes = 2 * 1024 * 1024;
        public const int MaximumLineBytes = 256 * 1024;
        private readonly HttpClient client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool disposed;

        /// <returns>The single terminal done:true frame, including provider statistics.</returns>
        public async Task<JObject> SendAsync(string uri, JObject body, int seconds, Action<JObject> onChunk, CancellationToken token)
        {
            if (disposed) throw new ObjectDisposedException(nameof(OllamaStreamingTransport));
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (seconds < 1 || seconds > 300) throw new ArgumentOutOfRangeException(nameof(seconds));
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            using (var request = new HttpRequestMessage(HttpMethod.Post, uri))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
                request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                try
                {
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                    {
                        int status = (int)response.StatusCode;
                        if (!response.IsSuccessStatusCode) throw new ProviderException("Ollama stream HTTP " + status + ".", status, status == 408 || status == 429 || status >= 500);
                        if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new ProviderException("Ollama stream response too large.");
                        // Disposing the response also interrupts handlers whose body ReadAsync ignores its token.
                        using (timeout.Token.Register(response.Dispose))
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var line = new MemoryStream())
                        {
                            var buffer = new byte[8192]; int total = 0; JObject terminal = null;
                            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
                            {
                                // A stalled line cannot keep a request alive indefinitely; total timeout still wins.
                                idle.CancelAfter(TimeSpan.FromSeconds(Math.Min(seconds, 120)));
                                using (idle.Token.Register(response.Dispose))
                                {
                                    while (true)
                                    {
                                        idle.Token.ThrowIfCancellationRequested();
                                        int received;
                                        try { received = await stream.ReadAsync(buffer, 0, buffer.Length, idle.Token); }
                                        catch (Exception) when (idle.IsCancellationRequested) { throw new OperationCanceledException(idle.Token); }
                                        idle.Token.ThrowIfCancellationRequested();
                                        if (received == 0) break;
                                        total += received;
                                        if (total > MaximumResponseBytes) throw new ProviderException("Ollama stream response too large.");
                                        for (int index = 0; index < received; index++)
                                        {
                                            if (buffer[index] == (byte)'\n')
                                            {
                                                ProcessLine(line, ref terminal, onChunk, idle.Token); line.SetLength(0);
                                                idle.CancelAfter(TimeSpan.FromSeconds(Math.Min(seconds, 120)));
                                            }
                                            else
                                            {
                                                if (line.Length >= MaximumLineBytes) throw new ProviderException("Ollama stream line too large.");
                                                line.WriteByte(buffer[index]);
                                            }
                                        }
                                    }
                                    if (line.Length > 0) ProcessLine(line, ref terminal, onChunk, idle.Token);
                                }
                                idle.Token.ThrowIfCancellationRequested();
                            }
                            timeout.Token.ThrowIfCancellationRequested();
                            if (terminal == null) throw new ProviderException("Ollama stream ended without done:true.");
                            return terminal;
                        }
                    }
                }
                catch (Exception) when (token.IsCancellationRequested || lifetime.IsCancellationRequested)
                { throw new OperationCanceledException(token.IsCancellationRequested ? token : lifetime.Token); }
                catch (OperationCanceledException) { throw new ProviderException("Ollama stream timeout.", 408, true); }
                catch (ObjectDisposedException) { throw new ProviderException("Ollama stream timeout or closed response.", 408, true); }
                catch (HttpRequestException) { throw new ProviderException("Ollama stream connection failed.", 0, true); }
                catch (IOException) { throw new ProviderException("Ollama stream interrupted.", 0, true); }
                catch (JsonException) { throw new ProviderException("Malformed Ollama stream JSON."); }
                catch (DecoderFallbackException) { throw new ProviderException("Malformed Ollama stream UTF-8."); }
            }
        }

        private static void ProcessLine(MemoryStream line, ref JObject terminal, Action<JObject> onChunk, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string text = new UTF8Encoding(false, true).GetString(line.ToArray()).Trim();
            if (text.Length == 0) return;
            if (terminal != null) throw new ProviderException("Ollama stream contains data after done:true.");
            RejectNonJsonExtensions(text);
            var frame = JToken.Parse(text, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }) as JObject;
            if (frame == null || frame["done"]?.Type != JTokenType.Boolean) throw new ProviderException("Malformed Ollama stream frame.");
            if (frame["error"] != null) throw new ProviderException("Ollama reported a stream error.");
            if (frame["message"] != null && !(frame["message"] is JObject)) throw new ProviderException("Malformed Ollama stream message.");
            var message = frame["message"] as JObject;
            if (message?["content"] != null && message["content"].Type != JTokenType.String) throw new ProviderException("Malformed Ollama stream content.");
            if (message?["role"] != null && (message["role"].Type != JTokenType.String || (string)message["role"] != "assistant")) throw new ProviderException("Unexpected Ollama stream role.");
            if ((bool)frame["done"]) terminal = frame;
            token.ThrowIfCancellationRequested();
            onChunk?.Invoke(frame);
        }

        private static void RejectNonJsonExtensions(string text)
        {
            // Json.NET accepts JavaScript extensions. Native NDJSON must use JSON:
            // no comments, single-quoted/unquoted keys, NaN or trailing commas.
            var outside = new StringBuilder(text.Length); bool quoted = false, escaped = false;
            foreach (char value in text)
            {
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (value == '\\') escaped = true;
                    else if (value == '"') { quoted = false; outside.Append('"'); }
                }
                else if (value == '"') { quoted = true; outside.Append('"'); }
                else
                {
                    if (value == '\'' || value == '/') throw new ProviderException("Malformed Ollama stream JSON syntax.");
                    outside.Append(value);
                }
            }
            string skeleton = outside.ToString();
            if (quoted || Regex.IsMatch(skeleton, @",\s*[}\]]|[{,]\s*[A-Za-z_$][A-Za-z0-9_$]*\s*:|\b(undefined|NaN|Infinity)\b|[:\[,]\s*\+|[:\[,]\s*-?0[0-9xX]"))
                throw new ProviderException("Malformed Ollama stream JSON syntax.");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; lifetime.Cancel(); client.Dispose();
        }
    }
}
