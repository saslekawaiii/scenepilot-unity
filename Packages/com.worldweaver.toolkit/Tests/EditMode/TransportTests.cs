using System;
using System.Collections;
using System.Linq;
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
        [UnityTest] public IEnumerator NativeStreamingRetriesOnlyBeforeAnyDeliveredFrame() => Await(CheckStreamRetry());
        static async Task CheckStreamRetry()
        {
            foreach(bool partial in new[]{false,true})
            {
                string line=new JObject{["message"]=new JObject{["role"]="assistant",["content"]="piece"},["done"]=!partial}.ToString(Newtonsoft.Json.Formatting.None)+"\n";
                using(var server=new LocalServer(n=>!partial&&n==1?500:200,line))
                {
                    var profile=ScriptableObject.CreateInstance<ModelProfile>();
                    try
                    {
                        profile.Endpoint=server.Url.TrimEnd('/');profile.Model="fixture";profile.Retries=1;
                        var received=new StringBuilder();ProviderException failure=null;CompletionResult result=null;
                        using(var provider=new LocalProvider(profile))
                            try{result=await provider.CompleteAsync(new CompletionRequest{Stream=true,OnContentDelta=d=>received.Append(d)},CancellationToken.None);}catch(ProviderException e){failure=e;}
                        Assert.That(server.Count,Is.EqualTo(partial?1:2));Assert.That(received.ToString(),Is.EqualTo("piece"));
                        if(partial)Assert.That(failure,Is.Not.Null,"A partially delivered stream must fail rather than replay.");
                        else{Assert.That(failure,Is.Null);Assert.That(result.Text,Is.EqualTo("piece"));}
                    }
                    finally{UnityEngine.Object.DestroyImmediate(profile);}
                }
            }
        }
        [UnityTest] public IEnumerator OllamaCloudReceivesExactContractWithoutUnsupportedFormat() => Await(CheckCloudContract());
        static async Task CheckCloudContract()
        {
            using(var server=new LocalServer(_=>200,"{\"message\":{\"content\":\"{\\\"reply\\\":\\\"ok\\\",\\\"complete\\\":true,\\\"actions\\\":[]}\"},\"done\":true}"))
            {
                var profile=ScriptableObject.CreateInstance<ModelProfile>();
                try
                {
                    profile.Endpoint=server.Url.TrimEnd('/');profile.Model="example:cloud";profile.Retries=0;
                    var schema=EditorPlan.Schema(true,new[]{"create_mesh"});var original=schema.DeepClone();
                    using(var provider=new LocalProvider(profile))await provider.CompleteAsync(new CompletionRequest{Schema=schema},CancellationToken.None);
                    var body=JObject.Parse(server.LastRequest.Substring(server.LastRequest.IndexOf("\r\n\r\n",StringComparison.Ordinal)+4));
                    Assert.That(body["format"],Is.Null);Assert.That((bool)body["think"],Is.False);
                    Assert.IsTrue(((JArray)body["messages"]).Any(m=>(string)m["role"]=="system"&&((string)m["content"]??"").Contains(schema.ToString(Newtonsoft.Json.Formatting.None))),"The exact schema must be transmitted as a trusted instruction.");
                    Assert.That(JToken.DeepEquals(schema,original),Is.True,"Cloud compatibility must not weaken host bounds or mutate the source schema.");
                }
                finally{UnityEngine.Object.DestroyImmediate(profile);}
            }
        }
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
                    var request = new CompletionRequest { Schema = EditorPlan.Schema(false,new[]{"create_mesh","configure_physics"}) };
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
                    StringAssert.Contains("Second instruction", system);
                    StringAssert.Contains(request.Schema.ToString(Newtonsoft.Json.Formatting.None),system,"A text endpoint must receive exact selected argument contracts, not just tool names.");
                    StringAssert.Contains("vertices",system);StringAssert.Contains("mass",system);StringAssert.DoesNotContain("material_import",system);
                }
                finally { UnityEngine.Object.DestroyImmediate(profile); }
            }
        }
        [UnityTest] public IEnumerator OpenAiFinishReasonRetainsPartialEmptyAndBalancedTokenLimitedOutput() => Await(CheckOpenAiFinishReason());
        static async Task CheckOpenAiFinishReason()
        {
            const string partial="{\"reply\":\"\",\"complete\":false,\"actions\":[{\"tool\":\"draft_script\",\"args\":{\"code\":\"using UnityEngine;";
            const string balanced="{\"reply\":\"looks complete but provider hit a limit\",\"complete\":true,\"actions\":[]}";
            foreach(var sample in new[]{Tuple.Create("length",partial,true),Tuple.Create("length","",true),Tuple.Create("length",balanced,true),Tuple.Create("stop",balanced,false),Tuple.Create((string)null,balanced,false)})
            {
                var choice=new JObject {["message"]=new JObject {["content"]=sample.Item2}};if(sample.Item1!=null)choice["finish_reason"]=sample.Item1;
                string envelope=new JObject {["model"]="fixture",["choices"]=new JArray(choice),["usage"]=new JObject {["prompt_tokens"]=13,["completion_tokens"]=1500}}.ToString();
                using(var server=new LocalServer(_=>200,envelope))
                {
                    var profile=ScriptableObject.CreateInstance<ModelProfile>();
                    try
                    {
                        profile.Provider=ProviderKind.OpenAICompatible;profile.Endpoint=server.Url.TrimEnd('/');profile.Model="fixture";profile.Retries=0;
                        using(var provider=new OpenAICompatibleProvider(profile))
                        {
                            var result=await provider.CompleteAsync(new CompletionRequest(),CancellationToken.None);
                            Assert.AreEqual(sample.Item2,result.Text,"Never append delimiters or silently repair partial model source.");Assert.AreEqual(sample.Item1,result.FinishReason);Assert.AreEqual(sample.Item3,result.OutputTruncated);
                            Assert.AreEqual(13,result.InputTokens);Assert.AreEqual(1500,result.OutputTokens);Assert.AreEqual(1,server.Count,"A token limit is metadata, not a hidden transport retry.");
                            if(sample.Item2==partial)Assert.Throws<Newtonsoft.Json.JsonReaderException>(()=>EditorPlan.Parse(result.Text));
                        }
                    }
                    finally {UnityEngine.Object.DestroyImmediate(profile);}
                }
            }
        }
        [UnityTest] public IEnumerator OllamaNonStreamingDoneReasonRetainsExactLimitedOutput() => Await(CheckOllamaFinishReason());
        static async Task CheckOllamaFinishReason()
        {
            foreach(var sample in new[]{Tuple.Create("length","{\"reply\":\"cut",true),Tuple.Create("length","",true),Tuple.Create("stop","{\"reply\":\"ok\",\"complete\":true,\"actions\":[]}",false),Tuple.Create((string)null,"legacy response",false)})
            {
                var envelope=new JObject {["message"]=new JObject {["content"]=sample.Item2},["done"]=true,["prompt_eval_count"]=11,["eval_count"]=42};if(sample.Item1!=null)envelope["done_reason"]=sample.Item1;
                using(var server=new LocalServer(_=>200,envelope.ToString()))
                {
                    var profile=ScriptableObject.CreateInstance<ModelProfile>();
                    try
                    {
                        profile.Endpoint=server.Url.TrimEnd('/');profile.Model="fixture";profile.Retries=0;
                        using(var provider=new LocalProvider(profile))
                        {
                            var result=await provider.CompleteAsync(new CompletionRequest(),CancellationToken.None);
                            Assert.AreEqual(sample.Item2,result.Text);Assert.AreEqual(sample.Item1,result.FinishReason);Assert.AreEqual(sample.Item3,result.OutputTruncated);
                            Assert.AreEqual(11,result.InputTokens);Assert.AreEqual(42,result.OutputTokens);Assert.AreEqual(1,server.Count);
                        }
                    }
                    finally {UnityEngine.Object.DestroyImmediate(profile);}
                }
            }
        }
        [UnityTest] public IEnumerator OllamaStreamingTerminalLimitKeepsExactlyTheAlreadyEmittedPartial() => Await(CheckOllamaStreamLimit());
        static async Task CheckOllamaStreamLimit()
        {
            const string partial="{\"reply\":\"draft\",\"actions\":[{\"tool\":\"draft_script\",\"args\":{\"code\":\"void Update() {";
            string body=new JObject {["done"]=false,["message"]=new JObject {["role"]="assistant",["content"]=partial}}.ToString(Newtonsoft.Json.Formatting.None)+"\n"+
                new JObject {["done"]=true,["done_reason"]="length",["prompt_eval_count"]=11,["eval_count"]=42,["eval_duration"]=1000000000}.ToString(Newtonsoft.Json.Formatting.None)+"\n";
            using(var server=new LocalServer(_=>200,body))
            {
                var profile=ScriptableObject.CreateInstance<ModelProfile>();
                try
                {
                    profile.Endpoint=server.Url.TrimEnd('/');profile.Model="fixture";profile.Retries=0;string emitted="";
                    using(var provider=new LocalProvider(profile))
                    {
                        var result=await provider.CompleteAsync(new CompletionRequest {Stream=true,OnContentDelta=chunk=>emitted+=chunk},CancellationToken.None);
                        Assert.AreEqual(partial,emitted);Assert.AreEqual(partial,result.Text);Assert.AreEqual("length",result.FinishReason);Assert.IsTrue(result.OutputTruncated);
                        Assert.AreEqual(42,result.OutputTokens);Assert.AreEqual(1,server.Count,"A partially emitted stream must never be replayed by this metadata handling.");
                    }
                }
                finally {UnityEngine.Object.DestroyImmediate(profile);}
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
