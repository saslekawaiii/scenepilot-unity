using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class OllamaDiscoveryTests
    {
        string folder;
        [SetUp] public void Setup() { folder = Path.GetFullPath("UserSettings/WorldWeaverDiscoveryTests/" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void Cleanup() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        static JObject Tag(string id, string digest = "revision1") => new JObject { ["name"] = id, ["digest"] = digest, ["size"] = 1000000000L, ["details"] = new JObject { ["parameter_size"] = "2B" } };
        static JObject Tags(params JObject[] models) => new JObject { ["models"] = new JArray(models) };
        static JObject Capability(params string[] values) => new JObject { ["capabilities"] = new JArray(values) };

        [TestCase("qwen3.5", "qwen3.5:latest")]
        [TestCase("namespace/model", "namespace/model:latest")]
        [TestCase("localhost:5000/team/model", "localhost:5000/team/model:latest")]
        [TestCase("namespace/model:2b", "namespace/model:2b")]
        public void ModelAliasesAreCanonicalizedWithoutDroppingExplicitTags(string value, string expected) => Assert.AreEqual(expected, OllamaModelDiscovery.CanonicalModelId(value));
        [Test] public void AutomaticDiscoveryCannotProbeRemoteEndpointsOrCredentialUrls()
        {
            foreach (string endpoint in new[] { "http://remote.example.com:11434", "https://remote.example.com", "http://name:secret@127.0.0.1:11434", "http://127.0.0.1:11434?key=secret" })
                Assert.Throws<ArgumentException>(() => new OllamaModelDiscovery(endpoint));
        }

        [UnityTest] public IEnumerator InstalledLoadedVisionAndEmbeddingStatesComeFromActualMetadata() => Await(CheckMetadata());
        async Task CheckMetadata()
        {
            using (var server = new ConnectionHttpServer((path, body) => path == "/api/tags" ? Tags(Tag("alpha"), Tag("alpha:latest"), Tag("not-named-embedding"), Tag("embed-in-name-but-chat")) :
                path == "/api/ps" ? Tags(Tag("alpha")) : (string)body["model"] == "not-named-embedding:latest" ? Capability("embedding", "vision") : Capability("completion", "vision")))
            using (var discovery = new OllamaModelDiscovery(server.Url))
            {
                var result = await discovery.RefreshAsync(); Assert.IsTrue(result.ServerAvailable); Assert.IsTrue(result.LoadedStateKnown); Assert.AreEqual(3, result.Models.Count);
                var alpha = result.Models.Single(m => m.Id == "alpha:latest"); Assert.IsTrue(alpha.Loaded); Assert.IsTrue(alpha.Completion); Assert.IsTrue(alpha.Vision);
                var inactive = result.Models.Single(m => m.Id == "embed-in-name-but-chat:latest"); Assert.IsFalse(inactive.Loaded); Assert.IsTrue(inactive.Completion, "Model names must not decide embedding-only support.");
                var embedding = result.Models.Single(m => m.Id == "not-named-embedding:latest"); Assert.IsTrue(embedding.CapabilitiesKnown); Assert.IsFalse(embedding.Completion); Assert.IsFalse(embedding.Vision);
                Assert.AreEqual(3, server.Requests.Count(r => r.Path == "/api/show")); Assert.IsTrue(server.Requests.All(r => r.Path == "/api/tags" || r.Path == "/api/ps" || r.Path == "/api/show"));
                Assert.IsTrue(server.Requests.All(r => string.IsNullOrEmpty(r.Authorization)));
            }
        }

        [UnityTest] public IEnumerator ModelsAppearAfterServerStartAndNewDownloadsWithoutReplacingSelectionOrCustomApi() => Await(CheckCatalogChanges());
        async Task CheckCatalogChanges()
        {
            int stage = 0;
            using (var server = new ConnectionHttpServer((path, body) => path == "/api/tags" ? stage == 0 ? Tags() : stage == 1 ? Tags(Tag("first:2b"), Tag("second:2b"), Tag("vectors")) : Tags(Tag("new-download:latest")) :
                path == "/api/ps" ? Tags() : (string)body["model"] == "vectors:latest" ? Capability("embedding") : Capability("completion")))
            using (var controller = new ChatController(null, folder + "/history", folder + "/models", new OllamaModelDiscovery(server.Url)))
            {
                var custom = controller.AddModelConnection(new ChatModelConnection { Name = "My API", Endpoint = "https://api.example.com/v1", Model = "custom-api" });
                await controller.RefreshLocalModelsAsync(); Assert.AreEqual(custom.ModelKey, controller.Settings.ModelKey); Assert.IsTrue(controller.Models.Any(m => m.Key == custom.ModelKey));
                stage = 1; await controller.RefreshLocalModelsAsync(); Assert.IsTrue(controller.Models.Single(m => m.Key == "ollama:first:2b").Available); Assert.IsFalse(controller.Models.Any(m => m.Model == "vectors:latest"));
                Assert.AreEqual(custom.ModelKey, controller.Settings.ModelKey, "Discovery must not replace the user's API selection.");
                controller.Settings.ModelKey = "ollama:first:2b"; controller.SaveSettings();
                stage = 2; await controller.RefreshLocalModelsAsync(); Assert.IsTrue(controller.Models.Single(m => m.Key == "ollama:new-download:latest").Available);
                Assert.AreEqual("ollama:first:2b", controller.Settings.ModelKey); Assert.IsFalse(controller.Models.Single(m => m.Key == "ollama:first:2b").Available, "Deleted selected model remains visible, unavailable.");
                Assert.IsFalse(controller.Models.Any(m => m.Key == "ollama:second:2b"), "A confirmed removed, unselected model must not stay available.");
                Assert.IsTrue(controller.Models.Any(m => m.Key == custom.ModelKey)); Assert.IsTrue(controller.Models.Any(m => m.Provider == ProviderKind.Pollinations));
                Assert.IsTrue(server.Requests.All(r => r.Path != "/api/chat" && r.Path != "/v1/models"));
            }
        }

        [UnityTest] public IEnumerator ServerStopsWithoutLosingInstalledRowsOrSelection() => Await(CheckServerRestart());
        async Task CheckServerRestart()
        {
            var server = new ConnectionHttpServer((path, body) => path == "/api/tags" ? Tags(Tag("stable:2b")) : path == "/api/ps" ? Tags(Tag("stable:2b")) : Capability("completion", "vision"));
            using (var controller = new ChatController(null, folder + "/history", folder + "/models", new OllamaModelDiscovery(server.Url)))
            {
                await controller.RefreshLocalModelsAsync(); controller.Settings.ModelKey = "ollama:stable:2b"; controller.SaveSettings(); Assert.IsTrue(controller.Connected);
                server.Dispose(); await controller.RefreshLocalModelsAsync(); Assert.IsFalse(controller.Connected); Assert.AreEqual("ollama:stable:2b", controller.Settings.ModelKey);
                var row = controller.Models.Single(m => m.Key == "ollama:stable:2b"); Assert.IsFalse(row.Available); Assert.IsFalse(row.Loaded); StringAssert.Contains("автоматически", row.Description);
            }
        }

        [UnityTest] public IEnumerator CapabilityCacheInvalidatesOnDigestAndNeverPromotesUnknownModels() => Await(CheckCache());
        async Task CheckCache()
        {
            DateTime clock = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc); string revision = "one"; bool vision = true, unknown = false;
            using (var server = new ConnectionHttpServer((path, body) => path == "/api/tags" ? Tags(Tag("cached", revision)) : path == "/api/ps" ? new JObject() : unknown ? new JObject() : vision ? Capability("completion", "vision") : Capability("completion")))
            using (var discovery = new OllamaModelDiscovery(server.Url, () => clock))
            {
                var first = await discovery.RefreshAsync(); Assert.IsTrue(first.Models.Single().Vision); Assert.IsFalse(first.LoadedStateKnown);
                await discovery.RefreshAsync(); Assert.AreEqual(1, server.Requests.Count(r => r.Path == "/api/show"), "Unchanged models should not be probed every poll.");
                revision = "two"; vision = false; var changed = await discovery.RefreshAsync(); Assert.IsTrue(changed.Models.Single().Completion); Assert.IsFalse(changed.Models.Single().Vision); Assert.AreEqual(2, server.Requests.Count(r => r.Path == "/api/show"));
                revision = "three"; unknown = true; var missing = await discovery.RefreshAsync(); Assert.IsFalse(missing.Models.Single().CapabilitiesKnown); Assert.IsFalse(missing.Models.Single().Completion); Assert.IsFalse(missing.Models.Single().Vision);
                await discovery.RefreshAsync(); Assert.AreEqual(3, server.Requests.Count(r => r.Path == "/api/show"));
                clock = clock.AddSeconds(31); unknown = false; var retried = await discovery.RefreshAsync(); Assert.IsTrue(retried.Models.Single().Completion); Assert.AreEqual(4, server.Requests.Count(r => r.Path == "/api/show"));
            }
        }

        [UnityTest] public IEnumerator MalformedCatalogPreservesExistingRowsAsUnavailableInsteadOfTreatingThemAsDeleted() => Await(CheckMalformedCatalog());
        async Task CheckMalformedCatalog()
        {
            bool malformed = false;
            using (var server = new ConnectionHttpServer((path, body) => path == "/api/tags" ? malformed ? new JObject { ["models"] = new JArray(42) } : Tags(Tag("preserved")) : path == "/api/ps" ? Tags() : Capability("completion")))
            using (var controller = new ChatController(null, folder + "/history", folder + "/models", new OllamaModelDiscovery(server.Url)))
            {
                await controller.RefreshLocalModelsAsync(); Assert.IsTrue(controller.Models.Single(m => m.Model == "preserved:latest").Available);
                malformed = true; await controller.RefreshLocalModelsAsync(); Assert.IsFalse(controller.Models.Single(m => m.Model == "preserved:latest").Available);
                StringAssert.Contains("некорректный", controller.LocalDiscoveryStatus); Assert.AreEqual(1, server.Requests.Count(r => r.Path == "/api/show"));
                malformed = false; await controller.RefreshLocalModelsAsync(); Assert.IsTrue(controller.Models.Single(m => m.Model == "preserved:latest").Available);
            }
        }

        [UnityTest] public IEnumerator LargeCatalogHydratesInBoundedBatches() => Await(CheckBoundedCatalog());
        async Task CheckBoundedCatalog()
        {
            using (var server = new ConnectionHttpServer((path, body) => path == "/api/tags" ? Tags(Enumerable.Range(0, 12).Select(i => Tag("model-" + i.ToString("D2"))).ToArray()) : path == "/api/ps" ? Tags() : Capability("completion")))
            using (var discovery = new OllamaModelDiscovery(server.Url))
            {
                // Discovery itself has no Unity dependency and must also be safe without Unity's synchronization context.
                var first = await Task.Run(() => discovery.RefreshAsync()); Assert.AreEqual(12, first.Models.Count); Assert.AreEqual(8, first.Models.Count(m => m.CapabilitiesKnown)); Assert.AreEqual(8, server.Requests.Count(r => r.Path == "/api/show"));
                var second = await Task.Run(() => discovery.RefreshAsync()); Assert.AreEqual(12, second.Models.Count(m => m.Completion)); Assert.AreEqual(12, server.Requests.Count(r => r.Path == "/api/show"));
            }
        }

        [UnityTest] public IEnumerator BackgroundPollingIsThrottledAndWaitsForAgentWork() => Await(CheckPollLifecycle());
        async Task CheckPollLifecycle()
        {
            using (var server = new ConnectionHttpServer((path, body) => path == "/api/tags" ? Tags(Tag("polled")) : path == "/api/ps" ? Tags() : Capability("completion")))
            using (var controller = new ChatController(null, folder + "/history", folder + "/models", new OllamaModelDiscovery(server.Url)))
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var automatic = typeof(ChatController).GetField("automaticLocalDiscovery", flags); var deadline = typeof(ChatController).GetField("nextLocalDiscovery", flags);
                var poll = typeof(ChatController).GetMethod("PollLocalModels", flags); var taskField = typeof(ChatController).GetField("localDiscoveryTask", flags);
                Assert.IsNotNull(automatic); Assert.IsNotNull(deadline); Assert.IsNotNull(poll); automatic.SetValue(controller, true); deadline.SetValue(controller, -1d);
                poll.Invoke(controller, null); await (Task)taskField.GetValue(controller); Assert.IsTrue(controller.Models.Single(m => m.Key == "ollama:polled:latest").Available);
                int requests = server.Requests.Length; poll.Invoke(controller, null); Assert.AreEqual(requests, server.Requests.Length, "Editor frames must not issue a request before the ten-second deadline.");
                deadline.SetValue(controller, -1d); typeof(ChatController).GetProperty("Busy").SetValue(controller, true);
                poll.Invoke(controller, null); Assert.AreEqual(requests, server.Requests.Length, "Polling must wait while the agent owns a model request.");
                typeof(ChatController).GetProperty("Busy").SetValue(controller, false); poll.Invoke(controller, null); await (Task)taskField.GetValue(controller);
                Assert.AreEqual(2, server.Requests.Count(r => r.Path == "/api/tags")); Assert.AreEqual(1, server.Requests.Count(r => r.Path == "/api/show"));
            }
        }

        [UnityTest] public IEnumerator ClosingControllerCancelsAnInFlightProbeAndCoalescesConcurrentRefreshes() => Await(CheckCancellation());
        async Task CheckCancellation()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var controller = new ChatController(null, folder + "/history", folder + "/models", new OllamaModelDiscovery("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port));
            try
            {
                Task first = controller.RefreshLocalModelsAsync(), second = controller.RefreshLocalModelsAsync(); Assert.AreSame(first, second);
                using (var accepted = await listener.AcceptTcpClientAsync())
                {
                    Assert.IsTrue(controller.LocalDiscoveryRunning); controller.Dispose();
                    var completed = await Task.WhenAny(first, Task.Delay(2000)); Assert.AreSame(first, completed, "Closing the window must cancel a request without waiting for its network timeout."); await first;
                    Assert.IsFalse(controller.LocalDiscoveryRunning); Assert.IsNull(controller.LastLocalDiscoveryUtc);
                }
                await controller.RefreshLocalModelsAsync(); Assert.IsFalse(listener.Pending(), "Disposed controller must not start a new request.");
            }
            finally { controller.Dispose(); listener.Stop(); }
        }

        static IEnumerator Await(Task task) { while (!task.IsCompleted) yield return null; if (task.IsFaulted) throw task.Exception.GetBaseException(); if (task.IsCanceled) Assert.Fail("Unexpected cancellation."); }
    }
}
