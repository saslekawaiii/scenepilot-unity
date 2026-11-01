using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class MaterialHttpTransportTests
    {
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
        static byte[] Bytes(int count)=>Enumerable.Range(0,count).Select(i=>(byte)((i*131+i/257)%251)).ToArray();
        static HttpResponseMessage Whole(byte[] data)=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(data)};
        static HttpResponseMessage Part(HttpRequestMessage request,byte[] data)
        {
            var range=request.Headers.Range.Ranges.Single();long start=range.From.Value,end=Math.Min(range.To.Value,data.Length-1);
            var response=new HttpResponseMessage(HttpStatusCode.PartialContent){Content=new ByteArrayContent(data.Skip((int)start).Take((int)(end-start+1)).ToArray())};
            response.Content.Headers.ContentRange=new ContentRangeHeaderValue(start,end,data.Length);response.Headers.ETag=new EntityTagHeaderValue("\"fixture-v1\"");return response;
        }
        [Test] public async Task HealthyTextureUsesOneNormalRequestWithoutFragmenting()
        {
            var data=Bytes(70000);int requests=0;
            var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
            {Interlocked.Increment(ref requests);Assert.That(request.Headers.Range,Is.Null);return Task.FromResult(Whole(data));}));
            CollectionAssert.AreEqual(data,await transport.GetAsync(Texture,1024*1024,CancellationToken.None));Assert.That(requests,Is.EqualTo(1));
        }
        [Test] public async Task StalledNormalTransferFallsBackToExactBoundedConcurrentFragments()
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
        [Test] public async Task IgnoredInitialRangeAcceptsOneBoundedWholeResponse()
        {
            var data=Bytes(40000);int requests=0;
            var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
            {Interlocked.Increment(ref requests);if(request.Headers.Range==null)throw new TaskCanceledException();return Task.FromResult(Whole(data));}));
            CollectionAssert.AreEqual(data,await transport.GetAsync(Texture,1024*1024,CancellationToken.None));Assert.That(requests,Is.EqualTo(2));
        }
        [TestCase("start")][TestCase("total")][TestCase("etag")][TestCase("ignored-later")]
        public void ChangedRangesOrIgnoredNonzeroRangeAreRejectedBeforeReassembly(string fault)
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
            Assert.CatchAsync<InvalidDataException>(async()=>await transport.GetAsync(Texture,1024*1024,CancellationToken.None));
        }
        [Test] public void RedirectsAndOversizedNormalResponseDoNotTriggerFragmentRequests()
        {
            foreach(bool redirect in new[]{true,false})
            {
                int requests=0;var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
                {
                    requests++;if(redirect){var response=new HttpResponseMessage(HttpStatusCode.Redirect){Content=new ByteArrayContent(Array.Empty<byte>())};response.Headers.Location=new Uri("https://example.com/other.jpg");return Task.FromResult(response);}
                    return Task.FromResult(Whole(Bytes(300)));
                }));
                Assert.CatchAsync<InvalidDataException>(async()=>await transport.GetAsync(Texture,100,CancellationToken.None));Assert.That(requests,Is.EqualTo(1));
            }
        }
        [Test] public async Task UserCancellationStopsOutstandingFragmentsWithoutRetry()
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
                Assert.CatchAsync<OperationCanceledException>(async()=>await transport.GetAsync(Texture,1024*1024,stop.Token));
                Assert.That(requests,Is.LessThanOrEqualTo(2+MaterialHttpTransport.RangeParallelism));
            }
            await Task.CompletedTask;
        }
        [Test] public void RepeatedTransientFailuresCannotExceedTheFragmentRequestBudget()
        {
            int requests=0;var attempts=new ConcurrentDictionary<long,int>();var data=Bytes(5*1024*1024);
            var transport=new MaterialHttpTransport(()=>new Handler((request,token)=>
            {
                Interlocked.Increment(ref requests);if(request.Headers.Range==null)throw new TaskCanceledException();long start=request.Headers.Range.Ranges.Single().From.Value;
                if(attempts.AddOrUpdate(start,1,(_,n)=>n+1)==1)throw new HttpRequestException("simulated transient failure");
                return Task.FromResult(Part(request,data));
            }));
            Assert.CatchAsync<InvalidDataException>(async()=>await transport.GetAsync(Texture,8*1024*1024,CancellationToken.None));
            Assert.That(requests,Is.LessThanOrEqualTo(MaterialHttpTransport.MaxRangeRequests+1));
        }
    }
}
