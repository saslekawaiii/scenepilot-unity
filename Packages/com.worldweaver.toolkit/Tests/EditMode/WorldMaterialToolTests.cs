using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class WorldMaterialToolTests
    {
        HashSet<string> originalFolders;
        string history;
        int undo;
        [SetUp] public void Setup()
        {
            if(!Application.isBatchMode) Assert.Ignore("Запускайте интеграционные проверки материалов в отдельном batchmode-проекте, чтобы сохранить открытые пользовательские сцены.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            originalFolders = MaterialFolders(); history = Path.GetFullPath("UserSettings/WorldWeaverMaterialToolTests/" + Guid.NewGuid().ToString("N"));
            Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup();
        }
        [TearDown] public void Cleanup()
        {
            if(!Application.isBatchMode) return;
            Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(undo); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (string path in MaterialFolders().Where(p => !originalFolders.Contains(p))) AssetDatabase.DeleteAsset(path.Replace('\\', '/'));
            if (Directory.Exists(history)) Directory.Delete(history, true);
        }
        static HashSet<string> MaterialFolders() => new HashSet<string>(Directory.Exists(WorldMaterialLibrary.Root) ? Directory.GetDirectories(WorldMaterialLibrary.Root) : Array.Empty<string>(), StringComparer.Ordinal);
        static EditorCommand Command(string tool, JObject args, string target = "") => new EditorCommand { tool = tool, args = args, target = target, name = "" };
        static EditorPlan Plan(params EditorCommand[] commands) => new EditorPlan { reply = "Проверка материалов", complete = true, actions = commands.ToList() };

        [Test] public void ContractsExposeThreeTypedMaterialToolsAndOnlySearchIsReadOnly()
        {
            var schemas = ((JArray)EditorToolSchema.CommandSchema()["anyOf"]).OfType<JObject>().Where(s => EditorToolSchema.ToolName(s).StartsWith("material_", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(3, schemas.Length);
            foreach (var schema in schemas) Assert.IsFalse((bool)schema["properties"]["args"]["additionalProperties"]);
            Assert.IsTrue(SceneToolExecutor.IsReadOnlyTool("material_search")); Assert.IsFalse(SceneToolExecutor.IsReadOnlyTool("material_import")); Assert.IsFalse(SceneToolExecutor.IsReadOnlyTool("material_apply"));
        }
        [UnityTest] public IEnumerator OfflineSearchUsesAsyncToolPathWithoutAssetsOrUndo() => Await(CheckSearch());
        async Task CheckSearch()
        {
            int group = Undo.GetCurrentGroup(); var before = MaterialFolders();
            var result = await SceneToolExecutor.ExecuteAsync(Plan(Command("material_search", new JObject { ["query"] = "wood дерево", ["source"] = "builtin", ["limit"] = 4 })));
            Assert.IsTrue(result.Success, result.Error); Assert.AreEqual(-1, result.UndoGroup); Assert.AreEqual(group, Undo.GetCurrentGroup());
            var materials = (JArray)JObject.Parse(result.Results.Single().Summary)["Materials"]; Assert.IsTrue(materials.Any(m => (string)m["Id"] == "builtin:wood"));
            CollectionAssert.AreEquivalent(before, MaterialFolders()); Assert.IsEmpty(result.Results.Single().CreatedAssets);
        }
        [UnityTest] public IEnumerator OfflineImportReportsActualFilesAndInvokesCheckpointHookOnlyBeforeNewWrites() => Await(CheckImport());
        async Task CheckImport()
        {
            int called = 0; bool wasCached = WorldMaterialLibrary.IsImported("builtin:leather");
            var result = await SceneToolExecutor.ExecuteAsync(Plan(Command("material_import", new JObject { ["material_id"] = "builtin:leather", ["allow_online"] = false })), beforeMutation: () => { called++; Assert.AreEqual(wasCached, WorldMaterialLibrary.IsImported("builtin:leather")); });
            Assert.IsTrue(result.Success, result.Error); Assert.AreEqual(wasCached ? 0 : 1, called); Assert.AreEqual(-1, result.UndoGroup);
            var imported = JObject.Parse(result.Results.Single().Summary); Assert.AreEqual("builtin:leather", (string)imported["MaterialId"]);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Material>((string)imported["MaterialPath"])); Assert.IsTrue(result.Results.Single().CreatedAssets.All(p => File.Exists(p) || Directory.Exists(p)));
            int previous = called; var cached = await SceneToolExecutor.ExecuteAsync(Plan(Command("material_import", new JObject { ["material_id"] = "builtin:leather" })), beforeMutation: () => called++);
            Assert.IsTrue(cached.Success); Assert.AreEqual(previous, called); Assert.IsEmpty(cached.Results.Single().CreatedAssets);
        }
        [Test] public void NetworkToolsCannotShareASceneTransactionOrBlockSynchronousExecute()
        {
            var import = Command("material_import", new JObject { ["material_id"] = "builtin:wood" });
            var search = Command("material_search", new JObject { ["query"] = "wood", ["source"] = "builtin" });
            Assert.Throws<ArgumentException>(() => SceneToolExecutor.Validate(Plan(import, search)));
            var result = SceneToolExecutor.Execute(Plan(search)); Assert.IsFalse(result.Success); StringAssert.Contains("ExecuteAsync", result.Error); CollectionAssert.AreEquivalent(originalFolders, MaterialFolders());
        }
        [Test] public void UnknownArgumentsUrlsAndNonExistingApplicationTargetsFailBeforeMutation()
        {
            foreach (var command in new[] {
                Command("material_import", new JObject { ["material_id"] = "https://example.com/wood.png" }),
                Command("material_search", new JObject { ["query"] = "wood", ["source"] = "arbitrary-web" }),
                Command("material_import", new JObject { ["material_id"] = "builtin:wood", ["url"] = "https://example.com/wood.png" }),
                Command("material_apply", new JObject { ["material_id"] = "builtin:wood", ["tiling_x"] = 0 }, "missing"),
                Command("material_apply", new JObject { ["material_id"] = "builtin:wood" }, "@future") })
                Assert.Throws<ArgumentException>(() => SceneToolExecutor.Validate(Plan(command)));
            CollectionAssert.AreEquivalent(originalFolders, MaterialFolders());
        }
        [Test] public void LaterRuntimeFailureRevertsAppliedRendererAndRemovesOnlyNewMaterialAssets()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = "MaterialRollbackCube"; var renderer = cube.GetComponent<Renderer>(); var original = renderer.sharedMaterial;
            var world = new GameObject("MaterialRollbackWorld"); world.AddComponent<GeneratedSceneObject>().Kind = "world";
            var npc = new GameObject("MaterialRollbackNpc"); npc.transform.SetParent(world.transform); npc.AddComponent<GeneratedSceneObject>().Kind = "npc"; npc.AddComponent<IntelligentNPC>();
            var before = MaterialFolders();
            var plan = Plan(Command("material_apply", new JObject { ["material_id"] = "builtin:wood", ["tiling_x"] = 3 }, cube.name),
                Command("create_delivery_quest", new JObject { ["giver"] = npc.name, ["recipient"] = npc.name }, world.name));
            SceneToolExecutor.Validate(plan); // Quest participant identity fails during execution, after the preceding application.
            var result = SceneToolExecutor.Execute(plan); Assert.IsFalse(result.Success); StringAssert.Contains("different NPCs", result.Error);
            Assert.AreSame(original, renderer.sharedMaterial); CollectionAssert.AreEquivalent(before, MaterialFolders());
            Assert.IsTrue(result.Results.Any(r => r.Tool == "material_apply" && !r.Success));
        }
        [UnityTest] public IEnumerator CancelledImportCannotCreateAssetsOrCallMutationHook() => Await(CheckCancelledImport());
        async Task CheckCancelledImport()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); bool called = false, cancelled = false;
                try { await SceneToolExecutor.ExecuteAsync(Plan(Command("material_import", new JObject { ["material_id"] = "builtin:wood" })), token: cancellation.Token, beforeMutation: () => called = true); }
                catch (OperationCanceledException) { cancelled = true; }
                Assert.IsTrue(cancelled); Assert.IsFalse(called); CollectionAssert.AreEquivalent(originalFolders, MaterialFolders());
            }
        }
        [UnityTest] public IEnumerator ControllerExecutesAsyncMaterialImportAndRetainsRealToolEvidence() => Await(CheckControllerImport());
        async Task CheckControllerImport()
        {
            var provider = new MaterialProvider();
            using (var controller = new ChatController(_ => provider, history + "/history", history + "/models"))
            {
                controller.Settings.Mode = AssistantMode.Execute; controller.Settings.AutoApply = true; controller.Settings.Power = AssistantPower.Fast;
                await controller.SendAsync("Импортируй встроенный материал дерева builtin:wood для проекта. Применять к объектам пока не нужно.");
                var answer = controller.Current.Messages.Last(); Assert.IsFalse(answer.IsError, answer.Text); Assert.IsNull(answer.PendingPlan);
                var step = answer.Steps.Single(s => s.Tool == "material_import"); Assert.IsTrue(step.Success); Assert.AreEqual("builtin:wood", (string)JObject.Parse(step.Summary)["MaterialId"]);
                if (step.CreatedAssets.Count > 0) { Assert.IsNotEmpty(answer.Task.CheckpointId); Assert.IsTrue(answer.Task.CheckpointSealed); }
            }
        }
        sealed class MaterialProvider : ILLMProvider
        {
            int calls;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); var plan = calls++ == 0 ? Plan(Command("material_import", new JObject { ["material_id"] = "builtin:wood" })) : Plan();
                return Task.FromResult(new CompletionResult { Text = JsonConvert.SerializeObject(plan), Model = "fixture", Provider = "fixture" });
            }
            public Task<bool> HealthAsync(CancellationToken token) => Task.FromResult(true);
            public void Dispose() { }
        }
        static IEnumerator Await(Task task) { while (!task.IsCompleted) yield return null; if (task.IsFaulted) throw task.Exception.GetBaseException(); if (task.IsCanceled) Assert.Fail("Unexpected cancellation."); }
    }
}
