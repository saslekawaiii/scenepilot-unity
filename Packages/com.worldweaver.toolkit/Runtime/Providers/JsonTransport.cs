using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldWeaver
{
    /// <summary>Bounded HTTP requests; redirects are disabled to keep credentials on the configured host.</summary>
    public sealed class JsonTransport : IDisposable
    {
        private readonly HttpClient client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = Timeout.InfiniteTimeSpan };

        public async Task<JObject> SendAsync(string uri, JObject body, string key, int seconds, int retries, CancellationToken token)
        {
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                using (var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, uri))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
                    if (!string.IsNullOrEmpty(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    if (body != null) request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                    try
                    {
                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                        {
                            int status = (int)response.StatusCode;
                            bool retry = status == 429 || status == 408 || status >= 500 || status == 402 && response.Headers.RetryAfter != null;
                            if (!response.IsSuccessStatusCode)
                            {
                                // Classic reports its free quota as HTTP 402 with FREE_LIMIT,
                                // including deployments that omit Retry-After. Never treat an
                                // ordinary payment/credit error or HTTP 400 as retryable.
                                string errorCode = "";
                                TimeSpan? freeDelay = null;
                                string errorBody = await ReadErrorBody(response, timeout.Token);
                                try
                                {
                                    var errorRoot = JObject.Parse(errorBody);
                                    var error = errorRoot["error"] as JObject ?? errorRoot;
                                    if (error["code"]?.Type == JTokenType.String)
                                    {
                                        string candidate = (string)error["code"];
                                        if (System.Text.RegularExpressions.Regex.IsMatch(candidate, @"^[A-Za-z0-9_-]{1,64}$")) errorCode = candidate;
                                    }
                                    if (status == 402 && errorCode == "FREE_LIMIT")
                                    {
                                        retry = true; freeDelay = TimeSpan.FromSeconds(46);
                                        string message = error["message"]?.Type == JTokenType.String ? (string)error["message"] : "";
                                        var match = System.Text.RegularExpressions.Regex.Match(message, @"Retry after (\d{1,3})s", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                        if (match.Success && int.TryParse(match.Groups[1].Value, out int wait)) freeDelay = TimeSpan.FromSeconds(Math.Min(60, wait));
                                    }
                                }
                                catch (JsonException) { }
                                if (retry && attempt < retries)
                                {
                                    var delay = response.Headers.RetryAfter?.Delta ??
                                        (response.Headers.RetryAfter?.Date.HasValue == true ? response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow : (TimeSpan?)null) ??
                                        freeDelay ?? TimeSpan.FromMilliseconds(500 * (1 << attempt));
                                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, Math.Max(.1, delay.TotalSeconds))), token);
                                    continue;
                                }
                                throw new ProviderException("Provider HTTP " + status + (errorCode.Length > 0 ? " (" + errorCode + ")" : "") + ". Check model, credentials and endpoint.", status, retry);
                            }
                            // Bound the body while streaming and keep timeout/cancellation active during reads.
                            using (var stream = await response.Content.ReadAsStreamAsync())
                            using (var memory = new System.IO.MemoryStream())
                            {
                                var buffer = new byte[8192]; int length;
                                while ((length = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) > 0)
                                {
                                    if (memory.Length + length > 2 * 1024 * 1024) throw new ProviderException("Provider response too large.");
                                    memory.Write(buffer, 0, length);
                                }
                                var root = JToken.Parse(Encoding.UTF8.GetString(memory.ToArray()), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                                if (root is JObject obj) return obj;
                                if (root is JArray array) return new JObject { ["data"] = array };
                                throw new JsonException("Expected a JSON object or array.");
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        if (attempt >= retries) throw new ProviderException("Provider timeout.", 408, true);
                    }
                    catch (HttpRequestException)
                    {
                        if (attempt >= retries) throw new ProviderException("Provider connection failed.", 0, true);
                    }
                    catch (JsonException) { throw new ProviderException("Malformed provider JSON."); }
                }
                await Task.Delay(500 * (1 << attempt), token);
            }
        }
        private static async Task<string> ReadErrorBody(HttpResponseMessage response, CancellationToken token)
        {
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var memory = new System.IO.MemoryStream())
            {
                var buffer = new byte[4096]; int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    if (memory.Length + read > 32768) return "";
                    memory.Write(buffer, 0, read);
                }
                return Encoding.UTF8.GetString(memory.ToArray());
            }
        }
        public void Dispose() { client.Dispose(); }
    }
}
