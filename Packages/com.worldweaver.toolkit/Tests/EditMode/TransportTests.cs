using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;
using WorldWeaver.Editor.Assistant;
namespace WorldWeaver.Tests
{
    public sealed class TransportTests
    {
        [UnityTest] public IEnumerator ClassicChatUsesTextInstanceAndOneSystemMessage() => Await(CheckClassicEnvelope());
        private static async Task CheckClassicEnvelope()
        {
            using (var server = new LocalServer(n => 200, "{\"model\":\"fixture\",\"choices\":[{\"message\":{\"content\":\"{\\\"reply\\\":\\\"ok\\\",\\\"complete\\\":true,\\\"actions\\\":[]}\"}}]}"))
            {
                var profile = ScriptableObject.CreateInstance<ModelProfile>();
                try
                {
                    profile.Provider = ProviderKind.Pollinations; profile.Endpoint = server.Url.TrimEnd('/');
                    profile.Model = "openai-fast"; profile.StrictSchema = false; profile.MaxTokens = 6000;
                    var request = new CompletionRequest { Schema = EditorPlan.Schema(false) };
                    request.Messages.Add(new ChatMessage("system", "First instruction"));
                    request.Messages.Add(new ChatMessage("system", "Second instruction"));
                    request.Messages.Add(new ChatMessage("user", "Read-only question"));
                    using (var provider = new OpenAICompatibleProvider(profile))
                    {
                        var result = await provider.CompleteAsync(request, CancellationToken.None);
                        Assert.That(EditorPlan.Parse(result.Text).actions.Count, Is.Zero);
                    }
                    string wire = server.LastRequest;
                    var body = JObject.Parse(wire.Substring(wire.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4));
                    Assert.That(body["response_format"], Is.Null, "Do not invoke the broken Classic JSON-object formatter.");
                    Assert.That((int)body["max_tokens"], Is.EqualTo(1500));
                    var messages = (JArray)body["messages"];
                    Assert.That(messages.Count, Is.EqualTo(2)); Assert.That((string)messages[0]["role"], Is.EqualTo("system"));
                    string system = (string)messages[0]["content"];
                    StringAssert.Contains("EditorPlan INSTANCE", system); StringAssert.Contains("First instruction", system);
                    StringAssert.Contains("Second instruction", system); StringAssert.DoesNotContain("\"type\":\"object\"", system);
                }
                finally { UnityEngine.Object.DestroyImmediate(profile); }
            }
        }
        [UnityTest] public IEnumerator RetryRateLimit() => Await(CheckRetry());
        private static async Task CheckRetry()
        {
            using (var server = new LocalServer(n => n == 1 ? 429 : 200, "{}"))
            using (var transport = new JsonTransport())
            { var r = await transport.SendAsync(server.Url, new JObject(), "", 5, 1, CancellationToken.None); Assert.AreEqual(2, server.Count); Assert.IsNotNull(r); }
        }
        [UnityTest] public IEnumerator ClientErrorNotRetried() => Await(CheckClientError());
        [UnityTest] public IEnumerator FreeLimitBodyCanRetryWithoutHeader() => Await(CheckFreeLimit());
        private static async Task CheckFreeLimit()
        {
            using (var server = new LocalServer(n => n == 1 ? 402 : 200,
                "{\"error\":{\"code\":\"FREE_LIMIT\",\"message\":\"Retry after 0s\"}}"))
            using (var transport = new JsonTransport())
            { var result = await transport.SendAsync(server.Url, new JObject(), "", 5, 1, CancellationToken.None); Assert.AreEqual(2, server.Count); Assert.IsNotNull(result); }
        }
        [UnityTest] public IEnumerator OrdinaryPaymentErrorIsNotRetried() => Await(CheckPaymentError());
        private static async Task CheckPaymentError()
        {
            using (var server = new LocalServer(n => 402, "{\"error\":{\"code\":\"CREDIT_EXHAUSTED\"}}"))
            using (var transport = new JsonTransport())
            {
                ProviderException error = null;
                try { await transport.SendAsync(server.Url, new JObject(), "", 5, 2, CancellationToken.None); }
                catch (ProviderException value) { error = value; }
                Assert.IsNotNull(error); Assert.AreEqual(402, error.Status); Assert.IsFalse(error.Transient);
                StringAssert.Contains("CREDIT_EXHAUSTED", error.Message); Assert.AreEqual(1, server.Count);
            }
        }
        private static async Task CheckClientError()
        {
            using (var server = new LocalServer(n => 400, "{}"))
            using (var transport = new JsonTransport())
            { ProviderException error = null; try { await transport.SendAsync(server.Url, new JObject(), "", 5, 2, CancellationToken.None); } catch (ProviderException e) { error = e; } Assert.AreEqual(400, error.Status); Assert.AreEqual(1, server.Count); }
        }
        [UnityTest] public IEnumerator MalformedProviderResponse() => Await(CheckMalformed());
        private static async Task CheckMalformed()
        {
            using (var server = new LocalServer(n => 200, "not-json"))
            using (var transport = new JsonTransport())
            { bool rejected = false; try { await transport.SendAsync(server.Url, new JObject(), "", 5, 0, CancellationToken.None); } catch (ProviderException) { rejected = true; } Assert.IsTrue(rejected); }
        }
        [UnityTest] public IEnumerator RequestCancellation() => Await(CheckCancellation());
        private static async Task CheckCancellation()
        {
            using (var server = new LocalServer(n => 200, "{}", 2000))
            using (var transport = new JsonTransport())
            using (var cancel = new CancellationTokenSource(100))
            { bool cancelled = false; try { await transport.SendAsync(server.Url, new JObject(), "", 5, 0, cancel.Token); } catch (OperationCanceledException) { cancelled = true; } Assert.IsTrue(cancelled); }
        }
        [UnityTest] public IEnumerator RequestTimeout() => Await(CheckTimeout());
        private static async Task CheckTimeout()
        {
            using (var server = new LocalServer(n => 200, "{}", 2500))
            using (var transport = new JsonTransport())
            { ProviderException error = null; try { await transport.SendAsync(server.Url, new JObject(), "", 1, 0, CancellationToken.None); } catch (ProviderException e) { error = e; } Assert.AreEqual(408, error.Status); }
        }
        private static IEnumerator Await(Task task) { while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult(); }
    }
    internal sealed class LocalServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        public int Count;
        public string LastRequest;
        public string Url { get; }
        public LocalServer(Func<int, int> status, string content, int delay = 0)
        {
            listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/";
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        using (var client = await listener.AcceptTcpClientAsync())
                        using (var stream = client.GetStream())
                        {
                            var buffer = new byte[8192];
                            using (var input = new System.IO.MemoryStream())
                            {
                                while (input.Length < 65536)
                                {
                                    int received = await stream.ReadAsync(buffer, 0, buffer.Length, stop.Token);
                                    if (received == 0) break;
                                    input.Write(buffer, 0, received);
                                    string text = Encoding.UTF8.GetString(input.ToArray());
                                    int boundary = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                                    if (boundary < 0) continue;
                                    var length = System.Text.RegularExpressions.Regex.Match(text.Substring(0, boundary), @"Content-Length:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    int expected = length.Success ? int.Parse(length.Groups[1].Value) : 0;
                                    if (input.Length >= Encoding.UTF8.GetByteCount(text.Substring(0, boundary + 4)) + expected) break;
                                }
                                LastRequest = Encoding.UTF8.GetString(input.ToArray());
                            }
                            int code = status(Interlocked.Increment(ref Count)); if (delay > 0) await Task.Delay(delay, stop.Token);
                            byte[] body = Encoding.UTF8.GetBytes(content); byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + " Test\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                            await stream.WriteAsync(header, 0, header.Length, stop.Token); await stream.WriteAsync(body, 0, body.Length, stop.Token);
                        }
                    }
                }
                catch (Exception) when (stop.IsCancellationRequested) { }
            });
        }
        public void Dispose() { stop.Cancel(); listener.Stop(); }
    }
}
