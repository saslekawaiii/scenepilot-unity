using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WorldWeaver.Editor.Assistant
{
    public sealed class DiscoveredOllamaModel
    {
        public string Id, Parameters, Digest, Revision;
        public long Bytes;
        public bool CapabilitiesKnown, Completion, Vision, Loaded, Remote;
    }

    public sealed class OllamaDiscoveryResult
    {
        public bool ServerAvailable, LoadedStateKnown;
        public string Message;
        public DateTime CheckedUtc;
        public IReadOnlyList<DiscoveredOllamaModel> Models = Array.Empty<DiscoveredOllamaModel>();
    }

    /// <summary>Metadata-only discovery. Neither lists nor capability checks load a model or run inference.</summary>
    public sealed class OllamaModelDiscovery : IDisposable
    {
        public const string DefaultEndpoint = "http://127.0.0.1:11434";
        public const int MaximumModels = 128, MaximumCapabilityChecksPerRefresh = 8;
        sealed class Capabilities
        {
            public string Revision;
            public bool Known, Completion, Vision, Remote;
            public DateTime RetryAfter;
        }
        readonly JsonTransport transport = new JsonTransport();
        readonly ConcurrentDictionary<string, Capabilities> capabilities = new ConcurrentDictionary<string, Capabilities>(StringComparer.Ordinal);
        readonly Func<DateTime> utcNow;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        readonly object lifecycle = new object();
        int activeOperations;
        bool primitivesDisposed;
        bool disposed;
        public string Endpoint { get; }

        public OllamaModelDiscovery(string endpoint = DefaultEndpoint, Func<DateTime> clock = null)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !uri.IsLoopback ||
                (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Автопоиск Ollama работает только на этом компьютере.");
            Endpoint = uri.AbsoluteUri.TrimEnd('/');
            utcNow = clock ?? (() => DateTime.UtcNow);
        }

        public static string CanonicalModelId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return null;
            string result = value.LastIndexOf(':') > value.LastIndexOf('/') ? value : value + ":latest";
            return result.Length <= 200 ? result : null;
        }

        public async Task<OllamaDiscoveryResult> RefreshAsync(CancellationToken token = default)
        {
            lock (lifecycle) { if (disposed) throw new ObjectDisposedException(nameof(OllamaModelDiscovery)); activeOperations++; }
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
                {
                    await gate.WaitAsync(linked.Token);
                    try { return await ReadCatalogAsync(linked.Token); }
                    finally { gate.Release(); }
                }
            }
            finally { lock (lifecycle) { activeOperations--; if (disposed && activeOperations == 0) DisposePrimitives(); } }
        }

        static string ReadString(JToken value) => value?.Type == JTokenType.String ? (string)value : null;
        static string ReadShortString(JToken value, int maximum)
        {
            string text = ReadString(value); return text != null && text.Length <= maximum && !text.Any(char.IsControl) ? text : "";
        }
        static string ReadModelId(JObject value) => CanonicalModelId(ReadString(value["name"]) ?? ReadString(value["model"]));

        async Task<OllamaDiscoveryResult> ReadCatalogAsync(CancellationToken token)
        {
            JObject tags;
            try { tags = await transport.SendAsync(Endpoint + "/api/tags", null, "", 3, 0, token); }
            catch (ProviderException)
            {
                return new OllamaDiscoveryResult { CheckedUtc = utcNow(), Message = "Ollama не отвечает. Запустите приложение Ollama или ollama serve — модели появятся автоматически." };
            }
            if (!(tags["models"] is JArray listed) || listed.Count > MaximumModels || listed.Any(item => !(item is JObject model) || ReadModelId(model) == null || model["details"] != null && !(model["details"] is JObject)))
                return new OllamaDiscoveryResult { CheckedUtc = utcNow(), Message = "Ollama вернула некорректный список или более 128 моделей. Прежний список сохранён." };

            var models = new Dictionary<string, DiscoveredOllamaModel>(StringComparer.Ordinal);
            foreach (var item in listed.OfType<JObject>())
            {
                string id = ReadModelId(item);
                if (id == null || models.ContainsKey(id)) continue;
                string digest = ReadShortString(item["digest"], 200);
                string modified = ReadShortString(item["modified_at"], 80);
                long size = item["size"]?.Type == JTokenType.Integer && long.TryParse(item["size"].ToString(), out long bytes) ? Math.Max(0, bytes) : 0;
                models.Add(id, new DiscoveredOllamaModel { Id = id, Digest = digest, Revision = digest + "|" + modified,
                    Bytes = size, Parameters = ReadShortString(item["details"]?["parameter_size"], 40),
                    Remote = !string.IsNullOrEmpty(ReadString(item["remote_host"])) || !string.IsNullOrEmpty(ReadString(item["remote_model"])) || id.EndsWith(":cloud", StringComparison.Ordinal) });
            }

            bool loadedKnown = false;
            try
            {
                var running = await transport.SendAsync(Endpoint + "/api/ps", null, "", 2, 0, token);
                if (running["models"] is JArray loaded && loaded.All(item => item is JObject model && ReadModelId(model) != null))
                {
                    var ids = new HashSet<string>(loaded.OfType<JObject>().Select(ReadModelId), StringComparer.Ordinal);
                    foreach (var model in models.Values) model.Loaded = ids.Contains(model.Id);
                    loadedKnown = true;
                }
            }
            catch (ProviderException) { /* Older Ollama versions can still provide a usable installed catalog. */ }

            DateTime now = utcNow();
            var pending = models.Values.Where(m => !capabilities.TryGetValue(m.Id, out var cached) || cached.Revision != m.Revision || cached.RetryAfter <= now)
                .OrderBy(m => capabilities.ContainsKey(m.Id) ? 1 : 0).ThenBy(m => m.Id, StringComparer.Ordinal)
                .Take(MaximumCapabilityChecksPerRefresh).ToList();
            // At most three concurrent metadata reads and eight new/changed models per poll.
            // Large catalogs are hydrated over subsequent polls without flooding the local server.
            for (int offset = 0; offset < pending.Count; offset += 3)
                await Task.WhenAll(pending.Skip(offset).Take(3).Select(model => ReadCapabilitiesAsync(model, token)));

            token.ThrowIfCancellationRequested();
            foreach (var model in models.Values)
                if (capabilities.TryGetValue(model.Id, out var cached) && cached.Revision == model.Revision)
                {
                    model.CapabilitiesKnown = cached.Known; model.Completion = cached.Completion;
                    model.Vision = cached.Known && cached.Completion && cached.Vision; model.Remote |= cached.Remote;
                }
            foreach (string removed in capabilities.Keys.Where(id => !models.ContainsKey(id)).ToArray()) capabilities.TryRemove(removed, out _);
            int chats = models.Values.Count(m => m.CapabilitiesKnown && m.Completion), waiting = models.Values.Count(m => !m.CapabilitiesKnown);
            return new OllamaDiscoveryResult { ServerAvailable = true, LoadedStateKnown = loadedKnown, CheckedUtc = utcNow(),
                Models = models.Values.OrderBy(m => m.Id, StringComparer.Ordinal).ToList(),
                Message = listed.Count == 0 ? "Ollama работает. Скачайте модель в Ollama — она появится здесь автоматически." :
                    "Ollama: моделей для чата " + chats + (waiting > 0 ? "; свойства ещё проверяются: " + waiting : "") + ". Список обновляется автоматически." };
        }

        async Task ReadCapabilitiesAsync(DiscoveredOllamaModel model, CancellationToken token)
        {
            var cached = new Capabilities { Revision = model.Revision, RetryAfter = utcNow().AddSeconds(30) };
            try
            {
                var info = await transport.SendAsync(Endpoint + "/api/show", new JObject { ["model"] = model.Id }, "", 2, 0, token);
                if (info["capabilities"] is JArray values && values.All(v => v.Type == JTokenType.String) && values.Count > 0)
                {
                    cached.Known = true; cached.Completion = values.Values<string>().Contains("completion");
                    cached.Vision = values.Values<string>().Contains("vision"); cached.RetryAfter = utcNow().AddMinutes(10);
                }
                cached.Remote = !string.IsNullOrEmpty(ReadString(info["remote_host"])) || !string.IsNullOrEmpty(ReadString(info["remote_model"]));
            }
            catch (ProviderException) { /* Never infer chat/vision support from a model's name. Retry later. */ }
            token.ThrowIfCancellationRequested();
            capabilities[model.Id] = cached;
        }

        public void Dispose()
        {
            lock (lifecycle)
            {
                if (disposed) return; disposed = true; lifetime.Cancel(); transport.Dispose();
                if (activeOperations == 0) DisposePrimitives();
            }
        }
        void DisposePrimitives() { if (primitivesDisposed) return; primitivesDisposed = true; gate.Dispose(); lifetime.Dispose(); }
    }
}
