using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
using Object = UnityEngine.Object;

namespace WorldWeaver.Tests
{
    public sealed class MaterialApplicationGoalTests
    {
        const string Request = "Найди текстуру дерева и примени её к выделенной двери.";
        GameObject door, control;
        HashSet<string> originalFolders;
        readonly List<TerrainData> data = new List<TerrainData>();
        string history;
        int undo;

        [SetUp] public void Setup()
        {
            if (!Application.isBatchMode) Assert.Ignore("Material-goal integration uses a disposable batch project, preserving user scenes.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            originalFolders = Folders(); history = Path.GetFullPath("UserSettings/MaterialGoalTests/" + Guid.NewGuid().ToString("N"));
            Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup();
            door = GameObject.CreatePrimitive(PrimitiveType.Cube); door.name = "Дверь";
            control = GameObject.CreatePrimitive(PrimitiveType.Cube); control.name = "Контроль";
            Selection.activeGameObject = door;
        }
        [TearDown] public void Cleanup()
        {
            if (!Application.isBatchMode) return;
            Selection.objects = Array.Empty<Object>(); Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(undo);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (var value in data) if (value) Object.DestroyImmediate(value); data.Clear();
            foreach (string path in Folders().Where(p => !originalFolders.Contains(p))) AssetDatabase.DeleteAsset(path.Replace('\\', '/'));
            if (Directory.Exists(history)) Directory.Delete(history, true);
        }
        static HashSet<string> Folders() => new HashSet<string>(Directory.Exists(WorldMaterialLibrary.Root) ? Directory.GetDirectories(WorldMaterialLibrary.Root) : Array.Empty<string>(), StringComparer.Ordinal);
        static EditorCommand Command(string tool, string target = "", JObject args = null) => new EditorCommand { tool = tool, name = "", target = target, args = args ?? new JObject() };
        static EditorPlan Plan(bool complete, params EditorCommand[] actions) => new EditorPlan { reply = "Controlled material-goal fixture", complete = complete, actions = actions.ToList() };
        static EditorCommand Apply(string target = "selected") => Command("material_apply", target, new JObject { ["material_id"] = "builtin:wood" });
        static AssistantMessage Answer(string request = Request) => new AssistantMessage { Task = AgentTaskVerifier.Create(request, true) };
        static AgentCheckResult Check(AssistantMessage answer)
        {
            var method = typeof(ChatController).GetMethod("VerifyTask", BindingFlags.Static | BindingFlags.NonPublic);
            return ((IReadOnlyList<AgentCheckResult>)method.Invoke(null, new object[] { answer })).Single(c => answer.Task.Requirements.Any(r => r.Kind == "material_application" && r.Id == c.RequirementId));
        }
        static void ExecuteApply(AssistantMessage answer, string target = "selected")
        {
            var result = SceneToolExecutor.Execute(Plan(true, Apply(target))); Assert.IsTrue(result.Success, result.Error); answer.Steps.AddRange(result.Results);
        }
        static IEnumerator Await(Task task)
        {
            float end = Time.realtimeSinceStartup + 25;
            while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(task.IsCompleted, "Controlled material controller exceeded deadline."); task.GetAwaiter().GetResult();
        }

        [TestCase(Request)]
        [TestCase("Apply a wood material to the selected door.")]
        [TestCase("Assign this texture to the selected object.")]
        [TestCase("К выделенной двери примени материал дерева.")]
        public void ExplicitApplicationCapturesOriginalSelection(string request)
        {
            var task = AgentTaskVerifier.Create(request, true); var requirement = task.Requirements.Single(r => r.Kind == "material_application");
            Assert.AreEqual(ProjectContextIndex.StableTarget(door), requirement.Target); Assert.AreEqual("renderer", requirement.Expected);
            Selection.activeGameObject = control; Assert.AreEqual(ProjectContextIndex.StableTarget(door), requirement.Target);
            Assert.IsEmpty(AgentTaskVerifier.Verify(task), "Generic verifier must defer this requirement to the controller's actual step evidence.");
        }
        [TestCase("Не применяй текстуру к выделенной двери, только найди её.")]
        [TestCase("Найди материал без применения к выделенной двери.")]
        [TestCase("Объясни, как применить материал к выделенной двери.")]
        [TestCase("Do not apply a material to the selected door.")]
        [TestCase("Don't apply a texture to the selected object.")]
        [TestCase("Explain how to apply a material to the selected door.")]
        [TestCase("Apply a material to the window. The selected door must stay unchanged.")]
        [TestCase("Импортируй материал дерева в проект.")]
        public void NonApplicationAndExplanationDoNotInventAGoal(string request) => Assert.IsFalse(AgentTaskVerifier.Create(request, true).Requirements.Any(r => r.Kind == "material_application"));
        [Test] public void AskMissingAmbiguousAndUnsupportedSelectionDoNotInventAGoal()
        {
            Assert.IsFalse(AgentTaskVerifier.Create(Request, false).Requirements.Any(r => r.Kind == "material_application"));
            Selection.objects = Array.Empty<Object>(); Assert.IsFalse(AgentTaskVerifier.Create(Request, true).Requirements.Any(r => r.Kind == "material_application"));
            Selection.objects = new Object[] { door, control }; Assert.IsFalse(AgentTaskVerifier.Create(Request, true).Requirements.Any(r => r.Kind == "material_application"));
            Selection.activeGameObject = new GameObject("Unsupported empty root"); Assert.IsFalse(AgentTaskVerifier.Create(Request, true).Requirements.Any(r => r.Kind == "material_application"));
        }
        [Test] public void ProseCompleteAndFailedApplyCannotPass()
        {
            var answer = Answer(); answer.Text = "Готово, материал применён";
            answer.Steps.Add(new EditorCommandResult { Tool = "material_apply", Success = false, Target = "selected", Summary = "{}" });
            Assert.IsFalse(Check(answer).Passed); Assert.AreEqual("checks_failed", answer.Task.Status);
        }
        [UnityTest] public IEnumerator ImportAloneFailsAndRealApplyPasses() => Await(ImportAndApply());
        async Task ImportAndApply()
        {
            var answer = Answer(); var originalControl = control.GetComponent<Renderer>().sharedMaterial;
            var imported = await SceneToolExecutor.ExecuteAsync(Plan(false, Command("material_import", args: new JObject { ["material_id"] = "builtin:wood" })));
            Assert.IsTrue(imported.Success, imported.Error); answer.Steps.AddRange(imported.Results);
            Assert.IsFalse(Check(answer).Passed, "An actual successful import does not apply anything to the selected Renderer.");
            ExecuteApply(answer); Assert.IsTrue(Check(answer).Passed);
            Assert.AreSame(originalControl, control.GetComponent<Renderer>().sharedMaterial);
        }
        [Test] public void WrongTargetFailsAndSelectionChangesCannotReassignCapturedGoal()
        {
            var answer = Answer(); Selection.activeGameObject = control;
            ExecuteApply(answer); Assert.IsFalse(Check(answer).Passed, "A successful selected-alias step on another door is insufficient.");
            Selection.activeGameObject = door; ExecuteApply(answer); Selection.activeGameObject = control;
            Assert.IsTrue(Check(answer).Passed, "Re-resolving selected now would incorrectly reject the actually changed original door.");
        }
        [Test] public void ManualMaterialRevertAndDeletedTargetFailDespiteSuccessfulStep()
        {
            var answer = Answer(); var original = door.GetComponent<Renderer>().sharedMaterial;
            ExecuteApply(answer); Assert.IsTrue(Check(answer).Passed);
            door.GetComponent<Renderer>().sharedMaterial = original; Assert.IsFalse(Check(answer).Passed);
            ExecuteApply(answer); Assert.IsTrue(Check(answer).Passed);
            Object.DestroyImmediate(door); Assert.IsFalse(Check(answer).Passed);
        }
        [Test] public void ActualUndoInvalidatesSuccessfulApplyEvidence()
        {
            var answer = Answer(); var original = door.GetComponent<Renderer>().sharedMaterial;
            var result = SceneToolExecutor.Execute(Plan(true, Apply())); Assert.IsTrue(result.Success, result.Error); answer.Steps.AddRange(result.Results);
            Assert.IsTrue(Check(answer).Passed); Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(result.UndoGroup);
            Assert.AreSame(original, door.GetComponent<Renderer>().sharedMaterial); Assert.IsFalse(Check(answer).Passed);
        }
        [Test] public void TerrainUsesActualCreatedLayerAndRestoredTerrainDataFails()
        {
            var original = new TerrainData { heightmapResolution = 33, size = new Vector3(32, 10, 32) }; data.Add(original);
            var terrain = Terrain.CreateTerrainGameObject(original).GetComponent<Terrain>(); Selection.activeGameObject = terrain.gameObject;
            var answer = Answer("Примени текстуру травы к выделенному Terrain."); ExecuteApply(answer); Assert.IsTrue(Check(answer).Passed);
            Assert.AreNotSame(original, terrain.terrainData); terrain.terrainData = original; Assert.IsFalse(Check(answer).Passed);
        }

        sealed class SixRoundProvider : ILLMProvider
        {
            public int Calls;
            public bool SawRemainingApplicationRequirement;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); Calls++; EditorPlan plan;
                if (Calls == 2) plan = Plan(false, Command("material_import", args: new JObject { ["material_id"] = "builtin:wood" }));
                else if (Calls == 3) plan = Plan(false, Command("inspect_object", "selected"));
                else if (Calls == 5) plan = Plan(true);
                else if (Calls == 6)
                {
                    string context = string.Join("\n", request.Messages.Select(m => m.Content));
                    SawRemainingApplicationRequirement = context.Contains("material_application") && context.Contains("Нет применения к исходной цели") && context.Contains("builtin:wood");
                    plan = Plan(true, Apply());
                }
                else plan = Plan(false, Command("material_search", args: new JObject { ["query"] = "wood", ["source"] = "builtin" }));
                return Task.FromResult(new CompletionResult { Text = JsonConvert.SerializeObject(plan), Model = "controlled-fixture", Provider = "controlled-fixture" });
            }
            public Task<bool> HealthAsync(CancellationToken token) => Task.FromResult(true);
            public void Dispose() { }
        }
        [UnityTest] public IEnumerator FifthRoundPrematureCompleteContinuesAndSixthRealApplyRefreshesChecks() => Await(SixRoundContinuation());
        async Task SixRoundContinuation()
        {
            var provider = new SixRoundProvider(); string checkpoint = null;
            try
            {
                using (var controller = new ChatController(_ => provider, history + "/history", history + "/models"))
                {
                    controller.Settings.Mode = AssistantMode.Execute; controller.Settings.Power = AssistantPower.Deep;
                    controller.Settings.AutoApply = true; controller.Settings.AutoVerify = true; controller.Settings.UseSkills = false;
                    await controller.SendAsync(Request); var answer = controller.Current.Messages.Last(); checkpoint = answer.Task.CheckpointId;
                    Assert.AreEqual(6, provider.Calls); Assert.IsTrue(provider.SawRemainingApplicationRequirement);
                    Assert.IsTrue(answer.Steps.Any(s => s.Success && s.Tool == "material_apply"));
                    Assert.IsFalse(answer.IsError, answer.Text); Assert.AreEqual("checks_passed", answer.Task.Status); Assert.AreEqual("Проверки пройдены", answer.Status);
                    Assert.IsTrue(answer.Task.Checks.Single(c => answer.Task.Requirements.Any(r => r.Kind == "material_application" && r.Id == c.RequirementId)).Passed,
                        "The controller must refresh its persisted fifth-round failed check after the actual sixth-round apply; this assertion does not invoke VerifyTask itself.");
                }
            }
            finally
            {
                if (Guid.TryParseExact(checkpoint, "N", out _)) { string path = Path.Combine("UserSettings/WorldWeaverCheckpoints", checkpoint); if (Directory.Exists(path)) Directory.Delete(path, true); }
            }
        }
    }
}
