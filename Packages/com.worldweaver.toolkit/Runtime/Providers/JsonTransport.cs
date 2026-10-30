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
                            bool retry = status == 429 || status == 408 || status >= 500;
                            if (!response.IsSuccessStatusCode)
                            {
                                if (retry && attempt < retries)
                                {
                                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(500 * (1 << attempt));
                                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, Math.Max(.1, delay.TotalSeconds))), token);
                                    continue;
                                }
                                throw new ProviderException("Provider HTTP " + status + ". Check model, credentials and endpoint.", status, retry);
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
                                return JObject.Parse(Encoding.UTF8.GetString(memory.ToArray()), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
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
        public void Dispose() { client.Dispose(); }
    }
}
