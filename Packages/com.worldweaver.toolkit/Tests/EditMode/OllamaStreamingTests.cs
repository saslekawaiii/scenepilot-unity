using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorldWeaver.Tests
{
    public sealed class OllamaStreamingTests
    {
        private const string First = "{\"message\":{\"role\":\"assistant\",\"content\":\"При\"},\"done\":false}\n";
        private const string Last = "{\"message\":{\"role\":\"assistant\",\"content\":\"вет\"},\"done\":true,\"prompt_eval_count\":13,\"eval_count\":2,\"eval_duration\":200000000}\n";
        [UnityTest] public IEnumerator ContentArrivesBeforeTerminalFrameAndStatisticsComeFromDone() => Await(CheckProgress());
        private static async Task CheckProgress()
        {
            var release = new TaskCompletionSource<bool>(); var first = new TaskCompletionSource<bool>();
            using (var server = new NdjsonServer(First, Last, () => release.Task))
            {
                var profile = Profile(server.Url); var deltas = new List<string>();
                try
                {
                    using (var provider = new LocalProvider(profile))
                    {
                        var request = new CompletionRequest { Stream = true, OnContentDelta = delta => { deltas.Add(delta); first.TrySetResult(true); } };
                        request.Messages.Add(new ChatMessage("user", "Привет"));
                        var task = provider.CompleteAsync(request, CancellationToken.None);
                        Assert.That(await Task.WhenAny(first.Task, Task.Delay(4000)), Is.SameAs(first.Task), "No incremental callback before the terminal frame.");
                        Assert.That(task.IsCompleted, Is.False); release.TrySetResult(true);
                        var result = await task;
                        Assert.That(result.Text, Is.EqualTo("Привет")); Assert.That(deltas, Is.EqualTo(new[] { "При", "вет" }));
                        Assert.That(result.InputTokens, Is.EqualTo(13)); Assert.That(result.OutputTokens, Is.EqualTo(2));
                        Assert.That(result.TokensPerSecond, Is.EqualTo(10).Within(.001)); Assert.That(result.FirstTokenMs, Is.GreaterThan(0));
                        Assert.That(result.FirstTokenMs, Is.LessThanOrEqualTo(result.ElapsedMs));
                        Assert.AreEqual(2,result.Diagnostics.Frames);Assert.Greater(result.Diagnostics.FirstEventMs,0);Assert.GreaterOrEqual(result.Diagnostics.LastEventMs,result.Diagnostics.FirstEventMs);
                    }
                    Assert.That((bool)server.RequestBody["stream"], Is.True);
                }
                finally { release.TrySetResult(true); UnityEngine.Object.DestroyImmediate(profile); }
            }
        }
        [UnityTest] public IEnumerator NativeContinuationStateIsAccumulatedWithoutBecomingVisibleReplyText()=>Await(CheckNativeState());
        private static async Task CheckNativeState()
        {
            var first=new JObject{["message"]=new JObject{["role"]="assistant",["thinking"]="OPAQUE_"},["done"]=false}.ToString(Newtonsoft.Json.Formatting.None)+"\n";
            var last=new JObject{["message"]=new JObject{["role"]="assistant",["thinking"]="STATE",["tool_calls"]=new JArray(new JObject{["function"]=new JObject{["name"]="inspect_object",["arguments"]=new JObject{["target"]="owned:fixture"}}})},["done"]=true}.ToString(Newtonsoft.Json.Formatting.None)+"\n";
            using(var server=new NdjsonServer(first,last))
            {
                var profile=Profile(server.Url);var deltas=new List<string>();
                try
                {
                    using(var provider=new LocalProvider(profile))
                    {
                        var request=new CompletionRequest{Stream=true,UseToolCalls=true,Schema=WorldWeaver.Editor.Assistant.EditorPlan.Schema(nativeFunctions:true),OnContentDelta=deltas.Add};request.Messages.Add(new ChatMessage("user","Inspect the actual target"));
                        var result=await provider.CompleteAsync(request,CancellationToken.None);
                        Assert.AreEqual("OPAQUE_STATE",(string)JObject.Parse(result.WireResponse)["thinking"]);Assert.IsEmpty(deltas);Assert.That(result.Text,Does.Not.Contain("OPAQUE_STATE"));Assert.That(result.Text,Does.Contain("inspect_object"));
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(profile);}
            }
        }
        [UnityTest] public IEnumerator CancelAfterFirstDeltaPreventsLaterCallbacks() => Await(CheckCancel(false));
        [UnityTest] public IEnumerator DisposeAfterFirstDeltaPreventsLaterCallbacks() => Await(CheckCancel(true));
        private static async Task CheckCancel(bool dispose)
        {
            using (var server = new NdjsonServer(First, Last, () => Task.Delay(250)))
            using (var cancellation = new CancellationTokenSource())
            {
                var profile = Profile(server.Url); int deltas = 0;
                try
                {
                    using (var provider = new LocalProvider(profile))
                    {
                        var request = new CompletionRequest { Stream = true };
                        request.OnContentDelta = delta => { deltas++; if (dispose) provider.Dispose(); else cancellation.Cancel(); };
                        request.Messages.Add(new ChatMessage("user", "Test"));
                        bool stopped = false;
                        try { await provider.CompleteAsync(request, cancellation.Token); } catch (OperationCanceledException) { stopped = true; }
                        Assert.That(stopped, Is.True); await Task.Delay(400); Assert.That(deltas, Is.EqualTo(1));
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(profile); }
            }
        }
        [UnityTest] public IEnumerator ImagesAreMappedToNativeMessagesAndNonStreamingIsPreserved() => Await(CheckImages());
        private static async Task CheckImages()
        {
            const string image = "iVBORw0KGgo=";
            using (var server = new NdjsonServer("{\"message\":{\"content\":\"ok\"},\"done\":true}", ""))
            {
                var profile = Profile(server.Url);
                try
                {
                    using (var provider = new LocalProvider(profile))
                    {
                        var request = new CompletionRequest(); request.Messages.Add(new ChatMessage("user", "Look", new[] { image }));
                        Assert.That((await provider.CompleteAsync(request, CancellationToken.None)).Text, Is.EqualTo("ok"));
                    }
                    var body = server.RequestBody;
                    Assert.That((bool)body["stream"], Is.False); Assert.That((string)body["messages"][0]["images"][0], Is.EqualTo(image));
                }
                finally { UnityEngine.Object.DestroyImmediate(profile); }
            }
        }
        [UnityTest] public IEnumerator ImageCountBase64AndSizeAreValidatedBeforeHttp() => Await(CheckInvalidImages());
        private static async Task CheckInvalidImages()
        {
            using (var server = new NdjsonServer("{}", ""))
            {
                var profile = Profile(server.Url);
                try
                {
                    foreach (var images in new[] { new[] { "data:image/png;base64,AAAA" }, Enumerable.Repeat("AAAA", 5).ToArray(), new[] { new string('A', 2796208) } })
                    using (var provider = new LocalProvider(profile))
                    {
                        var request = new CompletionRequest { Stream = true }; request.Messages.Add(new ChatMessage("user", "Look", images));
                        bool rejected = false; try { await provider.CompleteAsync(request, CancellationToken.None); } catch (ProviderException) { rejected = true; }
                        Assert.That(rejected, Is.True);
                    }
                    Assert.That(server.Count, Is.Zero);
                }
                finally { UnityEngine.Object.DestroyImmediate(profile); }
            }
        }
        [UnityTest] public IEnumerator MissingDoneMalformedDuplicateAndTrailingFramesAreRejected() => Await(CheckInvalidFrames());
        private static async Task CheckInvalidFrames()
        {
            foreach (string body in new[] { First, "not-json\n", "{\"done\":false,\"done\":true}\n", "{\"done\":\"true\"}\n", Last + First,
                "{\"done\":true,\"message\":{\"content\":7}}\n", "{'done':true}\n", "{done:true}\n", "{\"done\":true,}\n", "{\"done\":true}//comment\n", "{\"done\":true,\"extra\":NaN}\n" })
            using (var server = new NdjsonServer(body, ""))
            using (var transport = new OllamaStreamingTransport())
            {
                bool rejected = false;
                try { await transport.SendAsync(server.Url, new JObject(), 5, null, CancellationToken.None); } catch (ProviderException) { rejected = true; }
                Assert.That(rejected, Is.True, "Accepted invalid stream: " + body);
            }
        }
        [UnityTest] public IEnumerator ErrorFramesWithoutDoneAreClassifiedWithoutEchoingTheirText() => Await(CheckErrorFrames());
        static async Task CheckErrorFrames()
        {
            foreach(var item in new[]{("runner timeout private-fixture-token",408,true),("upstream unavailable private-fixture-token",502,true),("quota exhausted private-fixture-token",402,false),("unauthorized private-fixture-token",401,false)})
            using(var server=new NdjsonServer(new JObject{["error"]=item.Item1}.ToString(Newtonsoft.Json.Formatting.None)+"\n",""))
            using(var transport=new OllamaStreamingTransport())
            {
                ProviderException failure=null;try{await transport.SendAsync(server.Url,new JObject(),5,null,CancellationToken.None);}catch(ProviderException error){failure=error;}
                Assert.IsNotNull(failure);Assert.AreEqual(item.Item2,failure.Status);Assert.AreEqual(item.Item3,failure.Transient);Assert.That(failure.Message,Does.Not.Contain("private-fixture-token"));
            }
        }
        [UnityTest] public IEnumerator TerminalStatisticsRejectNegativeAndWrongTypes() => Await(CheckInvalidStatistics());
        private static async Task CheckInvalidStatistics()
        {
            foreach (string stats in new[] { "\"eval_count\":-1", "\"prompt_eval_count\":\"13\"", "\"eval_duration\":1.5" })
            using (var server = new NdjsonServer("{\"done\":true,\"message\":{\"content\":\"ok\"}," + stats + "}\n", ""))
            {
                var profile = Profile(server.Url);
                try
                {
                    using (var provider = new LocalProvider(profile))
                    {
                        bool rejected = false;
                        try { await provider.CompleteAsync(new CompletionRequest { Stream = true }, CancellationToken.None); } catch (ProviderException) { rejected = true; }
                        Assert.That(rejected, Is.True);
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(profile); }
            }
        }
        [UnityTest] public IEnumerator BodyTimeoutRemainsActiveAfterHeadersAndFirstFrame() => Await(CheckBodyTimeout());
        private static async Task CheckBodyTimeout()
        {
            using (var server = new NdjsonServer(First, Last, () => Task.Delay(2500)))
            using (var transport = new OllamaStreamingTransport())
            {
                ProviderException failure = null;
                try { await transport.SendAsync(server.Url, new JObject(), 1, null, CancellationToken.None); } catch (ProviderException ex) { failure = ex; }
                Assert.That(failure, Is.Not.Null); Assert.That(failure.Status, Is.EqualTo(408));
            }
        }
        [UnityTest] public IEnumerator LineAndTotalResponseLimitsAreEnforced() => Await(CheckLimits());
        private static async Task CheckLimits()
        {
            foreach (int length in new[] { OllamaStreamingTransport.MaximumLineBytes + 1, OllamaStreamingTransport.MaximumResponseBytes + 1 })
            using (var server = new NdjsonServer(new string('x', length), ""))
            using (var transport = new OllamaStreamingTransport())
            {
                bool rejected = false;
                try { await transport.SendAsync(server.Url, new JObject(), 5, null, CancellationToken.None); } catch (ProviderException) { rejected = true; }
                Assert.That(rejected, Is.True);
            }
        }
        [UnityTest] public IEnumerator MalformedUtf8AndHttpFailureDoNotProduceSuccessfulCompletion() => Await(CheckWireErrors());
        private static async Task CheckWireErrors()
        {
            using (var server = new NdjsonServer("", "", rawFirst: new byte[] { 0xff, 0xfe, 0x0a }))
            using (var transport = new OllamaStreamingTransport())
            {
                bool rejected = false; try { await transport.SendAsync(server.Url, new JObject(), 5, null, CancellationToken.None); } catch (ProviderException) { rejected = true; }
                Assert.That(rejected, Is.True);
            }
            using (var server = new NdjsonServer("{}", "", status: 500))
            using (var transport = new OllamaStreamingTransport())
            {
                ProviderException failure = null; try { await transport.SendAsync(server.Url, new JObject(), 5, null, CancellationToken.None); } catch (ProviderException ex) { failure = ex; }
                Assert.That(failure, Is.Not.Null); Assert.That(failure.Status, Is.EqualTo(500)); Assert.That(server.Count, Is.EqualTo(1));
            }
        }
        private static ModelProfile Profile(string endpoint)
        { var p = ScriptableObject.CreateInstance<ModelProfile>(); p.Provider = ProviderKind.Ollama; p.Endpoint = endpoint.TrimEnd('/'); p.Model = "fixture"; p.TimeoutSeconds = 5; p.Retries = 3; return p; }
        private static IEnumerator Await(Task task) { while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult(); }
    }

    /// <summary>Two independently flushed body fragments; tests control when the terminal frame is sent.</summary>
    internal sealed class NdjsonServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        public int Count;
        public JObject RequestBody { get; private set; }
        public string Url { get; }
        public NdjsonServer(string first, string tail, Func<Task> beforeTail = null, int status = 200, byte[] rawFirst = null)
        {
            listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/";
            _ = Task.Run(async () =>
            {
                try
                {
                    using (var client = await listener.AcceptTcpClientAsync())
                    using (var stream = client.GetStream())
                    using (var input = new MemoryStream())
                    {
                        Interlocked.Increment(ref Count); var buffer = new byte[8192]; int boundary = -1, expected = 0;
                        while (input.Length < 128 * 1024)
                        {
                            int received = await stream.ReadAsync(buffer, 0, buffer.Length, stop.Token); if (received == 0) break;
                            input.Write(buffer, 0, received); string wire = Encoding.UTF8.GetString(input.ToArray());
                            boundary = wire.IndexOf("\r\n\r\n", StringComparison.Ordinal); if (boundary < 0) continue;
                            var match = System.Text.RegularExpressions.Regex.Match(wire.Substring(0, boundary), @"Content-Length:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            expected = match.Success ? int.Parse(match.Groups[1].Value) : 0;
                            if (input.Length >= boundary + 4 + expected) { RequestBody = JObject.Parse(wire.Substring(boundary + 4)); break; }
                        }
                        byte[] a = rawFirst ?? Encoding.UTF8.GetBytes(first), b = Encoding.UTF8.GetBytes(tail);
                        byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " Test\r\nContent-Type: application/x-ndjson\r\nContent-Length: " + (a.Length + b.Length) + "\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(header, 0, header.Length, stop.Token);
                        await stream.WriteAsync(a, 0, a.Length, stop.Token); await stream.FlushAsync(stop.Token);
                        if (beforeTail != null) await beforeTail();
                        if (!stop.IsCancellationRequested) { await stream.WriteAsync(b, 0, b.Length, stop.Token); await stream.FlushAsync(stop.Token); }
                    }
                }
                catch (Exception ex) when (stop.IsCancellationRequested || ex is IOException || ex is SocketException || ex is ObjectDisposedException) { }
            });
        }
        public void Dispose() { stop.Cancel(); listener.Stop(); }
    }
}
