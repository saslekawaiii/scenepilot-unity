using System;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WorldWeaver.Tests
{
    public sealed class CoreTests
    {
        private readonly List<Object> owned = new List<Object>();
        private T Keep<T>(T obj) where T : Object { owned.Add(obj); return obj; }
        [TearDown] public void Cleanup() { foreach (var obj in owned) if (obj) Object.DestroyImmediate(obj); owned.Clear(); TraceLog.Clear(); }
        private IntelligentNPC Npc(out Inventory player)
        {
            var npc = Keep(new GameObject("NPC")).AddComponent<IntelligentNPC>(); npc.GetComponent<Inventory>().Add("Crystal", 10);
            var p = Keep(new GameObject("Player")); player = p.AddComponent<Inventory>(); var entity = p.AddComponent<WorldEntity>(); entity.Id = "Player"; npc.KnownEntities.Add(entity); return npc;
        }
        private static NpcDecision Decision(int amount, string type = "GiveItem", string target = "Player") => new NpcDecision { speech = "Вот кристалл.", actions = new[] { new ActionCommand { type = type, target = target, item = "Crystal", amount = amount } } };
        [Test] public void TerrainRoundtrip() { var spec = TerrainSpec.Parse(JsonConvert.SerializeObject(new TerrainSpec())); Assert.AreEqual(42, spec.seed); }
        [TestCase(32)] [TestCase(128)] [TestCase(2050)] public void InvalidResolutionIsRejected(int n) { var s = new TerrainSpec { resolution = n }; Assert.Throws<ArgumentException>(() => s.Validate()); }
        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(-1)] public void InvalidTerrainNumberIsRejected(float value) { var s = new TerrainSpec { width = value }; Assert.Throws<ArgumentException>(() => s.Validate()); }
        [Test] public void MissingTerrainFieldRejected() { Assert.Throws<ArgumentException>(() => TerrainSpec.Parse("{}")); }
        [Test] public void FractionalTerrainIntegerRejected() { var obj = JObject.FromObject(new TerrainSpec()); obj["resolution"] = 129.2; Assert.Throws<ArgumentException>(() => TerrainSpec.Parse(obj.ToString())); }
        [Test] public void TerrainUnknownFieldRejected() { var obj = JObject.FromObject(new TerrainSpec()); obj["execute"] = "code"; Assert.Throws<JsonSerializationException>(() => TerrainSpec.Parse(obj.ToString())); }
        [Test] public void TerrainSameSeedIsBitwiseEqual() { var a = TerrainGenerator.Generate(new TerrainSpec { resolution = 33 }); var b = TerrainGenerator.Generate(new TerrainSpec { resolution = 33 }); CollectionAssert.AreEqual(a, b); }
        [Test] public void TerrainDifferentSeedsDiffer() { var a = TerrainGenerator.Generate(new TerrainSpec { seed = 1, resolution = 33 }); var b = TerrainGenerator.Generate(new TerrainSpec { seed = 2, resolution = 33 }); Assert.AreNotEqual(a[8, 9], b[8, 9]); }
        [Test] public void TerrainHeightsAreNormalized() { foreach (float h in TerrainGenerator.Generate(new TerrainSpec { resolution = 33 })) Assert.That(h, Is.InRange(0, 1)); }
        [Test] public void TerrainValleyBelowRim() { var h = TerrainGenerator.Generate(new TerrainSpec { resolution = 33 }); Assert.Greater(h[0, 0], h[16, 16]); }
        [Test] public void GiveItemMutatesBothInventories() { var npc = Npc(out var p); var r = new ActionDispatcher().Execute(npc, Decision(1)); Assert.IsTrue(r.Accepted); Assert.AreEqual(9, npc.GetComponent<Inventory>().Count("Crystal")); Assert.AreEqual(1, p.Count("Crystal")); }
        [TestCase(0)] [TestCase(-1)] [TestCase(11)] [TestCase(2)] public void InvalidAmountHasNoSideEffects(int amount) { var npc = Npc(out var p); var r = new ActionDispatcher().Execute(npc, Decision(amount)); Assert.IsFalse(r.Accepted); Assert.AreEqual(10, npc.GetComponent<Inventory>().Count("Crystal")); Assert.AreEqual(0, p.Count("Crystal")); }
        [Test] public void UnknownTargetRejected() { var npc = Npc(out var p); Assert.IsFalse(new ActionDispatcher().Execute(npc, Decision(1, target: "Admin")).Accepted); Assert.AreEqual(0, p.Count("Crystal")); }
        [Test] public void UnknownItemRejected() { var npc = Npc(out var p); var d = Decision(1); d.actions[0].item = "Sword"; Assert.IsFalse(new ActionDispatcher().Execute(npc, d).Accepted); }
        [Test] public void ForbiddenActionRejected() { var npc = Npc(out var p); npc.AllowedActions = new[] { "Talk" }; Assert.IsFalse(new ActionDispatcher().Execute(npc, Decision(1)).Accepted); }
        [Test] public void PlanningCannotRepeatInventoryTransfer() { var npc = Npc(out var p); Assert.IsFalse(new ActionDispatcher().Execute(npc, Decision(1), npc.PlanningActions).Accepted); Assert.AreEqual(10, npc.GetComponent<Inventory>().Count("Crystal")); Assert.AreEqual(0, p.Count("Crystal")); }
        [Test] public void FarTransferRejected() { var npc = Npc(out var p); p.transform.position = new Vector3(100, 0, 0); Assert.IsFalse(new ActionDispatcher().Execute(npc, Decision(1)).Accepted); }
        [Test] public void DuplicateTargetsRejected() { var npc = Npc(out var p); npc.KnownEntities.Add(npc.KnownEntities[0]); Assert.IsFalse(new ActionDispatcher().Execute(npc, Decision(1)).Accepted); }
        [Test] public void WholeBatchValidatedBeforeMutation() { var npc = Npc(out var p); var d = Decision(1); d.actions = new[] { d.actions[0], new ActionCommand { type = "GiveItem", target = "Unknown", item = "Crystal", amount = 1 } }; Assert.IsFalse(new ActionDispatcher().Execute(npc, d).Accepted); Assert.AreEqual(10, npc.GetComponent<Inventory>().Count("Crystal")); Assert.AreEqual(0, p.Count("Crystal")); }
        [Test] public void CapacityRejectedWithoutLoss() { var npc = Npc(out var p); p.Capacity = 0; Assert.IsFalse(new ActionDispatcher().Execute(npc, Decision(1)).Accepted); Assert.AreEqual(10, npc.GetComponent<Inventory>().Count("Crystal")); }
        [Test] public void NegativeInventoryMutationRejected() { var npc = Npc(out var p); Assert.IsFalse(p.Add("Crystal", -1)); Assert.IsFalse(p.Remove("Crystal", -1)); }
        [Test] public void ProjectedInventoryPreventsDoubleSpend() { var npc = Npc(out var p); npc.MaxTransferPerDecision = 100; var d = Decision(6); d.actions = new[] { d.actions[0], d.actions[0] }; Assert.IsFalse(new ActionDispatcher().Execute(npc, d).Accepted); Assert.AreEqual(0, p.Count("Crystal")); }
        [Test] public void MissingDecisionFieldsRejected() { Assert.Throws<ArgumentException>(() => NpcDecision.Parse("{\"speech\":\"x\"}")); }
        [Test] public void ExtraDecisionFieldsRejected() { Assert.Throws<ArgumentException>(() => NpcDecision.Parse("{\"speech\":\"x\",\"actions\":[],\"admin\":true}")); }
        [Test] public void DuplicateDecisionFieldsRejected() { Assert.Throws<JsonReaderException>(() => NpcDecision.Parse("{\"speech\":\"a\",\"speech\":\"b\",\"actions\":[]}")); }
        [Test] public void StringActionAmountRejected() { Assert.Throws<ArgumentException>(() => NpcDecision.Parse("{\"speech\":\"x\",\"actions\":[{\"type\":\"GiveItem\",\"target\":\"Player\",\"item\":\"Crystal\",\"amount\":\"1\"}]}")); }
        [Test] public void MarkdownJsonRejected() { Assert.Throws<JsonReaderException>(() => NpcDecision.Parse("```json\n{}\n```")); }
        [Test] public void EmptyDecisionAccepted() { Assert.AreEqual(0, NpcDecision.Parse("{\"speech\":\"Не знаю\",\"actions\":[]}").actions.Length); }
        [Test] public void ChunkOverlapAndCoverage() { var d = new KnowledgeDocument { Id = "a", Scope = "merchant", Text = new string('x', 180) }; var c = TextChunker.Split(d, 100, 20); Assert.AreEqual(2, c.Count); Assert.AreEqual(80, c[1].Offset); Assert.AreEqual(100, c[1].Text.Length); }
        [Test] public void EmptyKnowledgeHasNoChunks() { Assert.AreEqual(0, TextChunker.Split(new KnowledgeDocument { Scope = "a", Text = " " }).Count); }
        [Test] public void ChunkIdentifiersStableAndScoped() { var d = new KnowledgeDocument { Scope = "a", Id = "x", Text = "hello" }; string id = TextChunker.Split(d)[0].Id; Assert.AreEqual(id, TextChunker.Split(d)[0].Id); d.Scope = "b"; Assert.AreNotEqual(id, TextChunker.Split(d)[0].Id); }
        [Test] public void ChunkInvalidOverlapRejected() { Assert.Throws<ArgumentException>(() => TextChunker.Split(new KnowledgeDocument { Scope = "a" }, 100, 100)); }
        [Test] public void RetrievalRanksAndIsolatesScope()
        {
            var store = new InMemoryVectorStore(); store.Upsert(new KnowledgeChunk { Id = "1", Scope = "merchant", Vector = new[] { 1f, 0f } }); store.Upsert(new KnowledgeChunk { Id = "2", Scope = "merchant", Vector = new[] { 0f, 1f } }); store.Upsert(new KnowledgeChunk { Id = "secret", Scope = "guard", Vector = new[] { 1f, 0f } });
            var r = store.Search(new[] { 1f, 0f }, "merchant", MemoryKind.StaticKnowledge, 3); Assert.AreEqual(2, r.Count); Assert.AreEqual("1", r[0].Id); Assert.IsFalse(r.Exists(c => c.Id == "secret"));
        }
        [Test] public void DimensionChangesRejected() { var store = new InMemoryVectorStore(); store.Upsert(new KnowledgeChunk { Id = "1", Vector = new[] { 1f } }); Assert.Throws<ArgumentException>(() => store.Upsert(new KnowledgeChunk { Id = "2", Vector = new[] { 1f, 2f } })); }
        [Test] public void ZeroCosineSafe() { Assert.AreEqual(0, InMemoryVectorStore.Cosine(new[] { 0f }, new[] { 1f })); }
        [Test] public void NonFiniteCosineRejected() { Assert.Throws<ArgumentException>(() => InMemoryVectorStore.Cosine(new[] { float.NaN }, new[] { 1f })); }
        [Test] public void ProviderRejectsInsecureRemoteEndpoint() { var p = Keep(ScriptableObject.CreateInstance<ModelProfile>()); p.Endpoint = "http://example.com"; Assert.Throws<ArgumentException>(() => p.Validate()); }
        [Test] public void ProviderRejectsCredentialInUrl() { var p = Keep(ScriptableObject.CreateInstance<ModelProfile>()); p.Endpoint = "https://user:password@example.com"; Assert.Throws<ArgumentException>(() => p.Validate()); }
        [Test] public void ProviderAcceptsLoopback() { Keep(ScriptableObject.CreateInstance<ModelProfile>()).Validate(); }
        [Test] public void RouterSeparatesTasks() { var routes = Keep(ScriptableObject.CreateInstance<OrchestratorProfile>()); var a = Keep(ScriptableObject.CreateInstance<ModelProfile>()); var b = Keep(ScriptableObject.CreateInstance<ModelProfile>()); routes.Dialogue = a; routes.Planning = b; Assert.AreSame(a, routes.Resolve(ModelTask.Dialogue)); Assert.AreSame(b, routes.Resolve(ModelTask.Planning)); Assert.Throws<InvalidOperationException>(() => routes.Resolve(ModelTask.Terrain)); }
        [UnityTest] public IEnumerator MockPipelineRetrievesValidatesAndTransfers() => Await(MockPipeline());
        [UnityTest] public IEnumerator CancellationAfterCommitRetainsExecutedDecision() => Await(CancelAfterCommit());
        private async Task CancelAfterCommit()
        {
            var npc = Npc(out var player);
            using (var cancellation = new CancellationTokenSource())
            using (var pipeline = new NpcPipeline(new FixedRouter(JsonConvert.SerializeObject(Decision(1))), new RagIndex(new CancelAfterTransferEmbedding(player, cancellation)), new ActionDispatcher()))
            {
                var trace = await pipeline.RunAsync(npc, "Дай кристалл", ModelTask.Dialogue, true, cancellation.Token);
                Assert.IsTrue(cancellation.IsCancellationRequested);
                Assert.IsTrue(trace.Validation.Accepted);
                Assert.AreEqual(1, player.Count("Crystal"));
                Assert.AreEqual(9, npc.GetComponent<Inventory>().Count("Crystal"));
                Assert.AreEqual("Вот кристалл.", trace.Speech);
                StringAssert.Contains("memory indexing cancelled", trace.Error);
            }
        }
        private async Task MockPipeline()
        {
            var npc = Npc(out var p); npc.Knowledge = Keep(ScriptableObject.CreateInstance<KnowledgeBase>()); npc.Knowledge.Documents = VillageKnowledge.Documents();
            using (var pipeline = new NpcPipeline(new FixedRouter(JsonConvert.SerializeObject(Decision(1))), new RagIndex(new FixedEmbedding()), new ActionDispatcher()))
            { var result = await pipeline.RunAsync(npc, "Дай кристалл", ModelTask.Dialogue, true, CancellationToken.None); Assert.IsTrue(result.Validation.Accepted); Assert.Greater(result.Retrieved.Count, 0); Assert.AreEqual(1, p.Count("Crystal")); }
        }
        [UnityTest] public IEnumerator ReindexCancellationPreservesPreviousIndex() => Await(ReindexCancellation());
        private async Task ReindexCancellation()
        {
            using (var rag = new RagIndex(new FixedEmbedding()))
            { await rag.ReindexAsync(new[] { new KnowledgeDocument { Scope = "a", Id = "1", Text = "fact" } }, CancellationToken.None); var c = new CancellationToken(true); bool cancelled = false; try { await rag.ReindexAsync(VillageKnowledge.Documents(), c); } catch (OperationCanceledException) { cancelled = true; } Assert.IsTrue(cancelled); Assert.AreEqual(1, rag.Count); }
        }
        [UnityTest] public IEnumerator MemoryHasFiniteCapacity() => Await(MemoryCapacity());
        private async Task MemoryCapacity()
        {
            using (var rag = new RagIndex(new FixedEmbedding())) { for (int i = 0; i < 40; i++) await rag.RememberAsync("a", "entry " + i, MemoryKind.Conversation, CancellationToken.None); Assert.AreEqual(32, rag.Count); rag.Clear(); Assert.AreEqual(0, rag.Count); }
        }
        private static IEnumerator Await(Task task) { while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult(); }
    }
    public sealed class FixedEmbedding : IEmbeddingProvider
    {
        public string Identity => "test-only";
        public Task<float[]> EmbedAsync(string text, bool query, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(new[] { 1f, 0f }); }
        public void Dispose() { }
    }
    public sealed class CancelAfterTransferEmbedding : IEmbeddingProvider
    {
        private readonly Inventory player;
        private readonly CancellationTokenSource cancellation;
        public string Identity => "test-cancel-after-commit";
        public CancelAfterTransferEmbedding(Inventory player, CancellationTokenSource cancellation) { this.player = player; this.cancellation = cancellation; }
        public Task<float[]> EmbedAsync(string text, bool query, CancellationToken token)
        {
            if (player.Count("Crystal") > 0) cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new[] { 1f, 0f });
        }
        public void Dispose() { }
    }
    public sealed class FixedRouter : IModelRouter
    {
        private readonly string response;
        public FixedRouter(string response) { this.response = response; }
        public Task<CompletionResult> CompleteAsync(ModelTask task, CompletionRequest request, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(new CompletionResult { Text = response, Provider = "test mock", Model = "fixed" }); }
        public void Dispose() { }
    }
}
