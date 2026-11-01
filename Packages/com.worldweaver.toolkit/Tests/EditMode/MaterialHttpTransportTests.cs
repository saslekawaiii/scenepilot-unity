using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class MaterialHttpTransportTests
    {
        // Unity must pump its synchronization context while async handlers complete.
        // NUnit's blocking async assertions can deadlock the Editor main thread.
        static IEnumerator AwaitCase(Func<Task> run)
        {
            Task task=run();double deadline=EditorApplication.timeSinceStartup+20;
            while(!task.IsCompleted)
            {
                if(EditorApplication.timeSinceStartup>deadline)Assert.Fail("Material HTTP fixture exceeded its 20-second deadline.");
                yield return null;
            }
            task.GetAwaiter().GetResult();
        }
        static async Task ExpectFailure<T>(Func<Task> action)where T:Exception
        {
            try{await action();}
            catch(Exception error){Assert.That(error,Is.InstanceOf<T>());return;}
            Assert.Fail("Expected "+typeof(T).Name+" from an invalid/cancelled material transfer.");
        }
        static readonly Uri Texture=new Uri("https://dl.polyhaven.org/file/ph-assets/Textures/jpg/1k/fixture/fixture_diff_1k.jpg");
        sealed class Handler:HttpMessageHandler
        {
            readonly Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> reply;
            public Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> reply){this.reply=reply;}
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>reply(request,token);
        }
        sealed class Progress:IProgress<MaterialTransferProgress>
        {
            public readonly ConcurrentBag<MaterialTransferProgress> Values=new ConcurrentBag<MaterialTransferProgress>();
            public void Report(MaterialTransferProgress value)=>Values.Add(value);
        }
        sealed class UnknownLengthContent:HttpContent
        {
            readonly byte[] bytes;
            public UnknownLengthContent(byte[] bytes){this.bytes=bytes;Headers.ContentLength=null;}
            protected override bool TryComputeLength(out long length){length=0;return false;}
            protected override Task SerializeToStreamAsync(Stream stream,TransportContext context)=>stream.WriteAsync(bytes,0,bytes.Length);
        }
        static byte[] Bytes(int count)=>Enumerable.Range(0,count).Select(i=>(byte)((i*131+i/257)%251)).ToArray();
        static HttpResponseMessage Whole(byte[] data)=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(data)};
        static HttpResponseMessage Part(HttpRequestMessage request,byte[] data)
        {
            var range=request.Headers.Range.Ranges.Single();long start=range.From.Value,end=Math.Min(range.To.Value,data.Length-1);
            var response=new HttpResponseMessage(HttpStatusCode.PartialContent){Content=new ByteArrayContent(data.Skip((int)start).Take((int)(end-start+1)).ToArray())};
            response.Content.Headers.ContentRange=new ContentRangeHeaderValue(start,end,data.Length);response.Headers.ETag=new EntityTagHeaderValue("\"fixture-v1\"");return response;
        }
        [UnityTest] public IEnumerator RussianWeatheredWoodSearchSendsEnglishQueryThroughActualHttpTransport()=>AwaitCase(RussianMaterialQueryCase);
        static async Task RussianMaterialQueryCase()
        {
            var requests=new ConcurrentQueue<Uri>();
            var transport=new MaterialHttpTransport(()=>new Handler(async(request,token)=>
            {
                requests.Enqueue(request.RequestUri);await Task.Delay(1,token);
                string body;
                if(request.RequestUri.AbsolutePath=="/search")
                {
                    string effective=Uri.UnescapeDataString(request.RequestUri.Query.Substring("?q=".Length).Split('&')[0]);
                    // The old untranslated request takes the intentionally wrong leaves branch.
                    string slug=effective=="weathered wood"?"weathered_planks_fixture":"forest_leaves_fixture";
                    body=new JObject{["results"]=new JArray(new JObject{["slug"]=slug})}.ToString();
                }
                else body=new JObject{["type"]=1,["name"]="Fixture Material",["tags"]=new JArray("wood")}.ToString();
                return Whole(Encoding.UTF8.GetBytes(body));
            }));
            var client=new PolyHavenMaterialClient(transport);var found=await client.SearchAsync("выветренный дерево",2);
            Assert.That(found.Single().Id,Is.EqualTo("polyhaven:weathered_planks_fixture"));
            Assert.That(requests.First().AbsoluteUri,Is.EqualTo("https://api.polyhaven.com/search?q=weathered%20wood&type=textures&limit=2"));
            Assert.That(requests.All(uri=>uri.Scheme=="https"&&uri.Host=="api.polyhaven.com"),Is.True);
            Assert.That(await client.SearchAsync("Weathered  WOOD",2),Is.SameAs(found));Assert.That(requests.Count,Is.EqualTo(2));
            using(var stop=new CancellationTokenSource())
            {
                stop.Cancel();await ExpectFailure<OperationCanceledException>(async()=>await client.SearchAsync("weathered wood",2,stop.Token));
                Assert.That(requests.Count,Is.EqualTo(2));
            }
        }
        [UnityTest] public IEnumerator SearchBoundsAreValidatedBeforeActualHttpHandlerRuns()=>AwaitCase(MaterialQueryBoundsCase);
        static async Task MaterialQueryBoundsCase()
        {
            int requests=0;var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
            {Interlocked.Increment(ref requests);return Task.FromResult(Whole(Encoding.UTF8.GetBytes("{\"results\":[]}")));}));
            var client=new PolyHavenMaterialClient(transport);
            await ExpectFailure<ArgumentException>(async()=>await client.SearchAsync(new string('x',101),1));
            await ExpectFailure<ArgumentException>(async()=>await client.SearchAsync(string.Join(" ",Enumerable.Repeat("рябь",16)),1));
            using(var stop=new CancellationTokenSource())
            {
                stop.Cancel();await ExpectFailure<OperationCanceledException>(async()=>await client.SearchAsync("дерево",1,stop.Token));
            }
            Assert.That(requests,Is.Zero);
        }
        [UnityTest] public IEnumerator HealthyTextureUsesOneNormalRequestWithoutFragmenting()=>AwaitCase(HealthyCase);
        static async Task HealthyCase()
        {
            var data=Bytes(70000);int requests=0;
            var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
            {Interlocked.Increment(ref requests);Assert.That(request.Headers.Range,Is.Null);return Task.FromResult(Whole(data));}));
            CollectionAssert.AreEqual(data,await transport.GetAsync(Texture,1024*1024,CancellationToken.None));Assert.That(requests,Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator StalledNormalTransferFallsBackToExactBoundedConcurrentFragments()=>AwaitCase(StalledCase);
        static async Task StalledCase()
        {
            var data=Bytes(110000);int requests=0,active=0,peak=0;var progress=new Progress();
            var transport=new MaterialHttpTransport(()=>new Handler(async(request,token)=>
            {
                Interlocked.Increment(ref requests);if(request.Headers.Range==null)throw new TaskCanceledException("simulated stalled normal GET");
                int now=Interlocked.Increment(ref active);int old;do{old=peak;if(old>=now)break;}while(Interlocked.CompareExchange(ref peak,now,old)!=old);
                try
                {
                    Assert.That(request.Headers.ConnectionClose,Is.True);var range=request.Headers.Range.Ranges.Single();Assert.That(range.To-range.From+1,Is.LessThanOrEqualTo(MaterialHttpTransport.RangeChunkBytes));
                    await Task.Delay(2,token);return Part(request,data);
                }
                finally{Interlocked.Decrement(ref active);}
            }),progress);
            CollectionAssert.AreEqual(data,await transport.GetAsync(Texture,1024*1024,CancellationToken.None));
            Assert.That(requests,Is.EqualTo(1+(int)Math.Ceiling(data.Length/(double)MaterialHttpTransport.RangeChunkBytes)));
            Assert.That(peak,Is.GreaterThanOrEqualTo(2));Assert.That(peak,Is.LessThanOrEqualTo(MaterialHttpTransport.RangeParallelism));
            Assert.That(progress.Values.Any(v=>v.RangeFallback&&v.Bytes==data.Length&&v.TotalBytes==data.Length),Is.True);
        }
        [UnityTest] public IEnumerator IgnoredInitialRangeAcceptsOneBoundedWholeResponse()=>AwaitCase(IgnoredInitialCase);
        static async Task IgnoredInitialCase()
        {
            var data=Bytes(40000);int requests=0;
            var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
            {Interlocked.Increment(ref requests);if(request.Headers.Range==null)throw new TaskCanceledException();return Task.FromResult(Whole(data));}));
            CollectionAssert.AreEqual(data,await transport.GetAsync(Texture,1024*1024,CancellationToken.None));Assert.That(requests,Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator ValidChunked206FragmentsNeedNoContentLengthHeader()=>AwaitCase(Chunked206Case);
        static async Task Chunked206Case()
        {
            var data=Bytes(70000);
            var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
            {
                if(request.Headers.Range==null)throw new TaskCanceledException();var part=Part(request,data);var range=request.Headers.Range.Ranges.Single();
                long start=range.From.Value,end=Math.Min(range.To.Value,data.Length-1);
                part.Content=new UnknownLengthContent(data.Skip((int)start).Take((int)(end-start+1)).ToArray());
                part.Content.Headers.ContentRange=new ContentRangeHeaderValue(start,end,data.Length);part.Content.Headers.ContentLength=null;
                Assert.That(part.Content.Headers.ContentLength,Is.Null);return Task.FromResult(part);
            }));
            CollectionAssert.AreEqual(data,await transport.GetAsync(Texture,1024*1024,CancellationToken.None));
        }
        [UnityTest] public IEnumerator IgnoredInitialRangeCanReturnOneBoundedChunked200()=>AwaitCase(Chunked200Case);
        static async Task Chunked200Case()
        {
            var data=Bytes(40000);int requests=0;
            var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
            {
                Interlocked.Increment(ref requests);if(request.Headers.Range==null)throw new TaskCanceledException();
                var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new UnknownLengthContent(data)};response.Content.Headers.ContentLength=null;
                Assert.That(response.Content.Headers.ContentLength,Is.Null);return Task.FromResult(response);
            }));
            CollectionAssert.AreEqual(data,await transport.GetAsync(Texture,1024*1024,CancellationToken.None));Assert.That(requests,Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator UnsolicitedPartial206TriggersExplicitRangeRecovery()=>AwaitCase(UnsolicitedPartialCase);
        static async Task UnsolicitedPartialCase()
        {
            var data=Bytes(70000);int requests=0;
            var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
            {
                Interlocked.Increment(ref requests);
                if(request.Headers.Range!=null)return Task.FromResult(Part(request,data));
                var unsolicited=new HttpResponseMessage(HttpStatusCode.PartialContent){Content=new ByteArrayContent(data.Take(12345).ToArray())};
                unsolicited.Content.Headers.ContentRange=new ContentRangeHeaderValue(0,12344,data.Length);return Task.FromResult(unsolicited);
            }));
            CollectionAssert.AreEqual(data,await transport.GetAsync(Texture,1024*1024,CancellationToken.None));
            Assert.That(requests,Is.EqualTo(1+(int)Math.Ceiling(data.Length/(double)MaterialHttpTransport.RangeChunkBytes)));
        }
        [UnityTest] public IEnumerator UnsolicitedComplete206CanBeReadAsOneVerifiedWholeBody()=>AwaitCase(UnsolicitedCompleteCase);
        static async Task UnsolicitedCompleteCase()
        {
            var data=Bytes(40000);int requests=0;
            var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
            {
                Interlocked.Increment(ref requests);Assert.That(request.Headers.Range,Is.Null);
                var response=new HttpResponseMessage(HttpStatusCode.PartialContent){Content=new UnknownLengthContent(data)};
                response.Content.Headers.ContentRange=new ContentRangeHeaderValue(0,data.Length-1,data.Length);return Task.FromResult(response);
            }));
            CollectionAssert.AreEqual(data,await transport.GetAsync(Texture,1024*1024,CancellationToken.None));Assert.That(requests,Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator Chunked206StillRequiresTheActualExactFragmentByteCount()=>AwaitCase(ChunkedLengthCase);
        static async Task ChunkedLengthCase()
        {
            var data=Bytes(70000);
            var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
            {
                if(request.Headers.Range==null)throw new TaskCanceledException();var part=Part(request,data);var range=request.Headers.Range.Ranges.Single();
                long start=range.From.Value,end=Math.Min(range.To.Value,data.Length-1);
                part.Content=new UnknownLengthContent(data.Skip((int)start).Take((int)(end-start)).ToArray());part.Content.Headers.ContentRange=new ContentRangeHeaderValue(start,end,data.Length);
                return Task.FromResult(part);
            }));
            await ExpectFailure<IOException>(async()=>await transport.GetAsync(Texture,1024*1024,CancellationToken.None));
        }
        [UnityTest] public IEnumerator IgnoredChunkedRangeStillCannotExceedTheActualBodyBudget()=>AwaitCase(ChunkedBudgetCase);
        static async Task ChunkedBudgetCase()
        {
            var data=Bytes(70000);int requests=0;
            var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
            {
                Interlocked.Increment(ref requests);if(request.Headers.Range==null)throw new TaskCanceledException();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new UnknownLengthContent(data)});
            }));
            await ExpectFailure<InvalidDataException>(async()=>await transport.GetAsync(Texture,40000,CancellationToken.None));Assert.That(requests,Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator ChangedFragmentStartIsRejectedBeforeReassembly()=>AwaitCase(()=>ChangedRangeCase("start"));
        [UnityTest] public IEnumerator ChangedFragmentTotalIsRejectedBeforeReassembly()=>AwaitCase(()=>ChangedRangeCase("total"));
        [UnityTest] public IEnumerator ChangedFragmentEtagIsRejectedBeforeReassembly()=>AwaitCase(()=>ChangedRangeCase("etag"));
        [UnityTest] public IEnumerator IgnoredNonzeroRangeIsRejectedBeforeReassembly()=>AwaitCase(()=>ChangedRangeCase("ignored-later"));
        static async Task ChangedRangeCase(string fault)
        {
            var data=Bytes(70000);
            var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
            {
                if(request.Headers.Range==null)throw new TaskCanceledException();var part=Part(request,data);var range=request.Headers.Range.Ranges.Single();
                if(range.From>0)
                {
                    if(fault=="start")part.Content.Headers.ContentRange=new ContentRangeHeaderValue(range.From.Value+1,range.To.Value+1,data.Length);
                    if(fault=="total")part.Content.Headers.ContentRange=new ContentRangeHeaderValue(range.From.Value,range.To.Value,data.Length+10);
                    if(fault=="etag")part.Headers.ETag=new EntityTagHeaderValue("\"fixture-v2\"");
                    if(fault=="ignored-later"){part.Dispose();return Task.FromResult(Whole(data));}
                }
                return Task.FromResult(part);
            }));
            await ExpectFailure<InvalidDataException>(async()=>await transport.GetAsync(Texture,1024*1024,CancellationToken.None));
        }
        [UnityTest] public IEnumerator RedirectsAndOversizedNormalResponseDoNotTriggerFragmentRequests()=>AwaitCase(RedirectBudgetCase);
        static async Task RedirectBudgetCase()
        {
            foreach(bool redirect in new[]{true,false})
            {
                int requests=0;var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
                {
                    requests++;if(redirect){var response=new HttpResponseMessage(HttpStatusCode.Redirect){Content=new ByteArrayContent(Array.Empty<byte>())};response.Headers.Location=new Uri("https://example.com/other.jpg");return Task.FromResult(response);}
                    return Task.FromResult(Whole(Bytes(300)));
                }));
                await ExpectFailure<InvalidDataException>(async()=>await transport.GetAsync(Texture,100,CancellationToken.None));Assert.That(requests,Is.EqualTo(1));
            }
        }
        [UnityTest] public IEnumerator UserCancellationStopsOutstandingFragmentsWithoutRetry()=>AwaitCase(CancelCase);
        static async Task CancelCase()
        {
            int requests=0;var data=Bytes(100000);
            using(var stop=new CancellationTokenSource())
            {
                var transport=new MaterialHttpTransport(()=>new Handler(async(request,token)=>
                {
                    Interlocked.Increment(ref requests);if(request.Headers.Range==null)throw new TaskCanceledException();
                    if(request.Headers.Range.Ranges.Single().From>0){stop.CancelAfter(10);await Task.Delay(10000,token);}
                    return Part(request,data);
                }));
                await ExpectFailure<OperationCanceledException>(async()=>await transport.GetAsync(Texture,1024*1024,stop.Token));
                Assert.That(requests,Is.LessThanOrEqualTo(2+MaterialHttpTransport.RangeParallelism));
            }
            await Task.CompletedTask;
        }
        [UnityTest] public IEnumerator RepeatedTransientFailuresCannotExceedTheFragmentRequestBudget()=>AwaitCase(RetryBudgetCase);
        static async Task RetryBudgetCase()
        {
            int requests=0;var attempts=new ConcurrentDictionary<long,int>();var data=Bytes(5*1024*1024);
            var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
            {
                Interlocked.Increment(ref requests);if(request.Headers.Range==null)throw new TaskCanceledException();long start=request.Headers.Range.Ranges.Single().From.Value;
                if(attempts.AddOrUpdate(start,1,(_,n)=>n+1)==1)throw new HttpRequestException("simulated transient failure");
                return Task.FromResult(Part(request,data));
            }));
            await ExpectFailure<InvalidDataException>(async()=>await transport.GetAsync(Texture,8*1024*1024,CancellationToken.None));
            Assert.That(requests,Is.LessThanOrEqualTo(MaterialHttpTransport.MaxRangeRequests+1));
        }
    }
}
