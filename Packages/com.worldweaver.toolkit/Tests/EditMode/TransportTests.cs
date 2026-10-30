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
namespace WorldWeaver.Tests
{
    public sealed class TransportTests
    {
        [UnityTest] public IEnumerator RetryRateLimit() => Await(CheckRetry());
        private static async Task CheckRetry()
        {
            using (var server = new LocalServer(n => n == 1 ? 429 : 200, "{}"))
            using (var transport = new JsonTransport())
            { var r = await transport.SendAsync(server.Url, new JObject(), "", 5, 1, CancellationToken.None); Assert.AreEqual(2, server.Count); Assert.IsNotNull(r); }
        }
        [UnityTest] public IEnumerator ClientErrorNotRetried() => Await(CheckClientError());
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
                            var buffer = new byte[8192]; await stream.ReadAsync(buffer, 0, buffer.Length, stop.Token);
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
