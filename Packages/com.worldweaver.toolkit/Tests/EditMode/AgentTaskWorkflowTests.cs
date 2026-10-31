using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WorldWeaver.Editor.Assistant;
using Object = UnityEngine.Object;

namespace WorldWeaver.Tests
{
    /// <summary>Task criteria and permission gates use the real controller; only inference is injected.</summary>
    public sealed class AgentTaskWorkflowTests
    {
        private Scene scene, previous;
        private string folder;
        private readonly List<ChatController> controllers = new List<ChatController>();
        private readonly List<string> imageFiles = new List<string>();
        private readonly List<Object> nativeData = new List<Object>();
        private readonly HashSet<string> assets = new HashSet<string>();
        private sealed class Provider : ILLMProvider
        {
            public int Calls;
            public Func<CompletionRequest, CancellationToken, Task<CompletionResult>> Answer;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken token) { Calls++; return Answer(request, token); }
            public Task<bool> HealthAsync(CancellationToken token) => Task.FromResult(true);
            public void Dispose() { }
        }
        [SetUp] public void SetUp()
        {
            previous = SceneManager.GetActiveScene();
            bool untitled = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(value => value.isLoaded && string.IsNullOrEmpty(value.path));
            if (untitled && !Application.isBatchMode) Assert.Ignore("Сохраните открытую сцену перед запуском интеграционных тестов через GUI.");
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            folder = Path.Combine(Path.GetTempPath(), "WorldWeaverTaskWorkflow_" + Guid.NewGuid().ToString("N"));
        }
        [TearDown] public void TearDown()
        {
            var checkpoints = new HashSet<string>();
            foreach (var controller in controllers)
            {
                foreach (var message in controller.Sessions.SelectMany(session => session.Messages))
                {
                    if (!string.IsNullOrEmpty(message.Task?.CheckpointId)) checkpoints.Add(message.Task.CheckpointId);
                    foreach (string asset in message.Steps.SelectMany(step => step.CreatedAssets ?? new List<string>())) assets.Add(asset);
                }
                controller.Dispose();
            }
            controllers.Clear();
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (scene.IsValid() && scene.isLoaded)
            { if (previous.IsValid() && previous.isLoaded) EditorSceneManager.CloseScene(scene, true); else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
            foreach (var value in nativeData) if (value) Object.DestroyImmediate(value); nativeData.Clear();
            foreach (string asset in assets.OrderByDescending(path => path.Length)) if (AssetDatabase.LoadMainAssetAtPath(asset)) AssetDatabase.DeleteAsset(asset); assets.Clear();
            foreach (string path in imageFiles) if (File.Exists(path)) File.Delete(path); imageFiles.Clear();
            foreach (string id in checkpoints)
                if (Guid.TryParseExact(id, "N", out _)) { string path = Path.Combine("UserSettings/WorldWeaverCheckpoints", id); if (Directory.Exists(path)) Directory.Delete(path, true); }
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        private ChatController Controller(Provider provider, AssistantMode mode = AssistantMode.Ask)
        {
            var value = new ChatController(_ => provider, folder); controllers.Add(value);
            value.Settings.Mode = mode; value.Settings.Power = AssistantPower.Fast; value.Settings.UseSkills = false;
            return value;
        }
        private static CompletionResult Result(params EditorCommand[] actions) => new CompletionResult
        { Text = JsonConvert.SerializeObject(new EditorPlan { reply = "Ответ для проверки", complete = true, actions = actions.ToList() }), Model = "Injected", Provider = "InjectedTest" };
        private static Provider EmptyProvider() => new Provider { Answer = (request, token) => Task.FromResult(Result()) };
        private static IEnumerator Finish(Task task)
        {
            float end = Time.realtimeSinceStartup + 20;
            while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Injected controller did not finish."); task.GetAwaiter().GetResult();
        }
        private static GameObject Owned(string name, string kind, Transform parent = null)
        { var value = new GameObject(name); value.transform.SetParent(parent); value.AddComponent<GeneratedSceneObject>().Kind = kind; return value; }
        private static GameObject Light(string name, LightType type, Transform parent = null)
        { var value = Owned(name, "light", parent); value.AddComponent<UnityEngine.Light>().type = type; return value; }
        private static IReadOnlyList<AgentCheckResult> VerifyEvidence(AssistantMessage answer)
        {
            var method = typeof(ChatController).GetMethod("VerifyTask", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Controller must verify actual editor evidence independently of model completion.");
            return (IReadOnlyList<AgentCheckResult>)method.Invoke(null, new object[] { answer });
        }
        // Synthetic persisted results exercise aggregation, without claiming that this fixture ran a real compiler or Play session.
        private static EditorCommandResult Job(string kind, string filter, bool success, double speed = 0, int samples = 0)
        {
            return new EditorCommandResult { Tool = "job_result", Target = Guid.NewGuid().ToString("N"), Success = success, Error = success ? null : "Actual editor job failed",
                Summary = new JObject { ["Kind"] = kind, ["Filter"] = filter, ["DegreesPerSecond"] = speed, ["Samples"] = samples }.ToString(Formatting.None) };
        }
        private sealed class RepairFixture : IDisposable
        {
            public readonly string Folder, Path;
            public readonly string Source;
            public readonly AssistantMessage Answer;
            public RepairFixture(string source, string request = "Вращай объект 30 градусов в секунду")
            {
                // Synchronous raw files are removed before the editor can import them. Diagnostics below are synthetic policy inputs.
                Folder = "Assets/WorldWeaverWorkflowPolicy_" + Guid.NewGuid().ToString("N"); Path = Folder + "/RepairFixture.cs"; Source = source;
                Directory.CreateDirectory(Folder); File.WriteAllText(Path, source, new UTF8Encoding(false));
                Answer = new AssistantMessage { Task = new AgentTaskState { OriginalRequest = request, ResumeAttempts = 1, MutationAuthorized = true, ReviewedCodeScope = new List<string> { Path } } };
                Answer.Steps.Add(new EditorCommandResult { Tool = "draft_script", Success = true, Target = Path, SourceHashes = new Dictionary<string, string> { [Path] = AgentSourcePatches.SourceHash(Path) } });
                Answer.Steps.Add(new EditorCommandResult { Tool = "compile_scripts", Success = false, Target = Path });
            }
            public string Diagnostic(string find, string code, int offset = 0, string detail = "")
            {
                int index = Source.IndexOf(find, StringComparison.Ordinal) + offset;
                int line = Source.Take(index).Count(c => c == '\n') + 1;
                int lineStart = Source.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
                return Path + "(" + line + "," + (index - lineStart + 1) + "): error " + code + ": " + detail;
            }
            public EditorPlan Patch(string find, string replacement)
            {
                return new EditorPlan { actions = new List<EditorCommand> { new EditorCommand { tool = "patch_script", args = new JObject {
                    ["path"] = Path, ["expected_sha256"] = AgentSourcePatches.SourceHash(Path), ["find"] = find, ["replace"] = replacement } } } };
            }
            public void Failure(string diagnostic) { Answer.Steps.Last().Error = diagnostic; }
            public void Dispose()
            {
                if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
                if (File.Exists(Folder + ".meta")) File.Delete(Folder + ".meta");
            }
        }
        private const string MissingSymbolSource = "class RepairFixture\n{\n    float speed = missingSpeed;\n}\n";
        private static bool CanRepair(AssistantMessage answer, EditorPlan plan)
        {
            var method = typeof(ChatController).GetMethod("CanRepairWithinScope", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (bool)method.Invoke(null, new object[] { answer, plan });
        }

        [TestCase("Создай 1000 деревьев", "tree", 1000)]
        [TestCase("Create 12345 trees", "tree", 12345)]
        [TestCase("Создай три дома", "house", 3)]
        public void LiteralCountsAreNotSilentlyTruncated(string request, string kind, int expected)
        {
            var task = AgentTaskVerifier.Create(request, true);
            Assert.That(task.Requirements.Single(row => row.Kind == "count" && row.Expected == kind).Number, Is.EqualTo(expected));
            Assert.That(AgentTaskVerifier.Verify(task).Single().Passed, Is.False, "A missing scene cannot satisfy a positive literal count.");
        }
        [Test] public void VillageAndUiSizeDoNotInventTreeOrTerrainRequirements()
        {
            Assert.That(AgentTaskVerifier.Create("Создай 2 деревни", true).Requirements.Any(row => row.Kind == "count" && row.Expected == "tree"), Is.False);
            Assert.That(AgentTaskVerifier.Create("Создай иконку 64x64 пикселя", true).Requirements.Any(row => row.Kind.StartsWith("terrain_")), Is.False);
            Assert.That(AgentTaskVerifier.Create("Сделай кнопку высотой 48", true).Requirements.Any(row => row.Kind.StartsWith("terrain_")), Is.False);
        }
        [Test] public void TerrainChecksUseActualDimensionsAndDetectChangedHeight()
        {
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(160, 24, 120) }; nativeData.Add(data);
            var terrain = Owned("RequestedTerrain", "terrain"); terrain.AddComponent<Terrain>().terrainData = data; terrain.AddComponent<GeneratedTerrain>();
            var task = AgentTaskVerifier.Create("Создай мир 160×120 метров высотой 24", true);
            Assert.That(AgentTaskVerifier.Verify(task).Count, Is.EqualTo(3)); Assert.That(task.Checks.All(row => row.Passed), Is.True);
            data.size = new Vector3(160, 32, 120); AgentTaskVerifier.Verify(task);
            Assert.That(task.Checks.Count(row => !row.Passed), Is.EqualTo(1));
            Assert.That(task.Checks.Single(row => !row.Passed).Actual, Is.EqualTo("32"));
        }
        [Test] public void ScopedRelativeCountsUseObjectIdentityInsteadOfGlobalCountDifference()
        {
            var world = Owned("TargetWorld", "world"); var other = Owned("OtherWorld", "world");
            var old = Owned("OldTree", "tree", world.transform); Owned("OldTree2", "tree", world.transform);
            for (int i = 0; i < 5; i++) Owned("OtherOld" + i, "tree", other.transform);
            var task = AgentTaskVerifier.Create("Добавь ровно 3 новых дерева", true); task.ScopeTarget = ProjectContextIndex.StableTarget(world);
            Object.DestroyImmediate(old);
            for (int i = 0; i < 3; i++) Owned("NewTree" + i, "tree", world.transform);
            for (int i = 0; i < 4; i++) Owned("OtherNew" + i, "tree", other.transform);
            var check = AgentTaskVerifier.Verify(task).Single();
            Assert.That(check.Passed, Is.True); Assert.That(check.Actual, Is.EqualTo("3"));
        }
        [Test] public void PreserveOldTreesAndAddNewTreesRemainSeparateObservableCriteria()
        {
            var world = Owned("PreservedWorld", "world"); var old = new List<GameObject>();
            for (int i = 0; i < 5; i++) old.Add(Owned("OldTree" + i, "tree", world.transform));
            var task = AgentTaskVerifier.Create("Старые 5 деревьев оставь, добавь 3 новых дерева", true); task.ScopeTarget = ProjectContextIndex.StableTarget(world);
            Assert.That(task.Requirements.Single(row => row.Kind == "preserve").Number, Is.EqualTo(5));
            Assert.That(task.Requirements.Single(row => row.Kind == "count").Number, Is.EqualTo(3));
            for (int i = 0; i < 3; i++) Owned("AddedTree" + i, "tree", world.transform);
            Assert.That(AgentTaskVerifier.Verify(task).All(row => row.Passed), Is.True);
            Object.DestroyImmediate(old[0]); var checks = AgentTaskVerifier.Verify(task);
            Assert.That(checks.Single(row => row.RequirementId == task.Requirements.Single(requirement => requirement.Kind == "preserve").Id).Passed, Is.False);
            Assert.That(checks.Single(row => row.RequirementId == task.Requirements.Single(requirement => requirement.Kind == "count").Id).Passed, Is.True, "The three new identities remain distinct from preserving every old identity.");
        }
        [Test] public void TwoSpatialTreeCountsDoNotSilentlyChooseTheFirstAsTheWholeGoal()
        {
            var task = AgentTaskVerifier.Create("Создай 5 деревьев на юге, 3 дерева на севере", true);
            Assert.That(task.OpenQuestions, Is.Not.Empty); Assert.That(task.Requirements.Any(row => row.Kind == "count" && row.Expected == "tree"), Is.False);
            for (int i = 0; i < 5; i++) Owned("OnlySouth" + i, "tree");
            var checks = AgentTaskVerifier.Verify(task);
            Assert.That(checks.Any(row => !row.Passed), Is.True, "Five trees alone must not be reported as satisfying two different placement clauses.");
            Assert.That(task.Status, Is.EqualTo("checks_failed"));
        }
        [Test] public void NewPointLightCriterionPreservesSunAndExistingPointLight()
        {
            var world = Owned("LightingWorld", "world");
            var sun = Light("ExistingSun", LightType.Directional, world.transform); var old = Light("ExistingPoint", LightType.Point, world.transform);
            var task = AgentTaskVerifier.Create("Добавь ровно 3 новых Point Light", true); task.ScopeTarget = ProjectContextIndex.StableTarget(world);
            for (int i = 0; i < 3; i++) Light("NewPoint" + i, LightType.Point, world.transform);
            Light("UnrelatedSpot", LightType.Spot, world.transform);
            Assert.That(task.Requirements.Single().Expected, Is.EqualTo("point_light"));
            Assert.That(AgentTaskVerifier.Verify(task).Single().Passed, Is.True);
            Assert.That(sun.GetComponent<UnityEngine.Light>().type, Is.EqualTo(LightType.Directional)); Assert.That(old, Is.Not.Null);
            var total = AgentTaskVerifier.Create("Всего ровно 6 источников света", true); total.ScopeTarget = ProjectContextIndex.StableTarget(world);
            Assert.That(AgentTaskVerifier.Verify(total).Single().Passed, Is.True, "Generic lights must still include Directional, Point and Spot.");
        }
        [Test] public void MissingScopeCannotPassUsingUnrelatedWorldObjects()
        {
            var missing = Owned("MissingWorld", "world"); var other = Owned("UnrelatedWorld", "world");
            var task = AgentTaskVerifier.Create("Создай 3 дерева", true); task.ScopeTarget = ProjectContextIndex.StableTarget(missing);
            Object.DestroyImmediate(missing); for (int i = 0; i < 3; i++) Owned("OtherTree" + i, "tree", other.transform);
            Assert.That(AgentTaskVerifier.Verify(task).Single().Passed, Is.False);
        }
        [Test] public void CompilationRequiresEvidenceAndAnUnrelatedSuccessCannotMaskAFileFailure()
        {
            var answer = new AssistantMessage { Task = new AgentTaskState { CompilationRequired = true } };
            Assert.That(VerifyEvidence(answer).Single(row => row.RequirementId == "required_compilation").Passed, Is.False);
            answer.Steps.Add(new EditorCommandResult { Tool = "compile_scripts", Target = "Assets/A.cs", Success = false, Error = "CS1002" });
            answer.Steps.Add(new EditorCommandResult { Tool = "compile_scripts", Target = "Assets/B.cs", Success = true, Summary = "Unity compiled B" });
            var checks = VerifyEvidence(answer);
            Assert.That(checks.Single(row => row.RequirementId == "compile:Assets/A.cs").Passed, Is.False);
            Assert.That(checks.Single(row => row.RequirementId == "compile:Assets/B.cs").Passed, Is.True);
            Assert.That(answer.Task.Status, Is.EqualTo("checks_failed"));
            answer.Steps.Add(new EditorCommandResult { Tool = "compile_patch", Target = "Assets/A.cs", Success = true, Summary = "Unity recompiled A" });
            Assert.That(VerifyEvidence(answer).All(row => row.Passed), Is.True);
            Assert.That(answer.Task.Checks.Count, Is.EqualTo(2), "A later repair supersedes the same file, preserving every other file's result.");
        }
        [Test] public void EditorJobResultsAreLatestPerKindAndFilter()
        {
            var answer = new AssistantMessage { Task = new AgentTaskState() };
            answer.Steps.Add(Job("tests", "World.A", false)); answer.Steps.Add(Job("tests", "World.B", true));
            Assert.That(VerifyEvidence(answer).Count(row => !row.Passed), Is.EqualTo(1));
            answer.Steps.Add(Job("build", "World.A", true));
            Assert.That(VerifyEvidence(answer).Count(row => !row.Passed), Is.EqualTo(1), "A build must not erase a failed test job with the same filter.");
            answer.Steps.Add(Job("tests", "World.A", true));
            Assert.That(VerifyEvidence(answer).All(row => row.Passed), Is.True); Assert.That(answer.Task.Checks.Count, Is.EqualTo(3));
        }
        [TestCase(true, 60, 10, false)]
        [TestCase(true, 30, 2, false)]
        [TestCase(false, 30, 10, false)]
        [TestCase(true, 30, 3, true)]
        public void RotationEvidenceMustMatchOriginalSpeedAndHaveEnoughSuccessfulSamples(bool success, double measuredSpeed, int samples, bool expected)
        {
            var task = AgentTaskVerifier.Create("Вращай объект 30 градусов в секунду", true);
            var requirement = task.Requirements.Single(row => row.Kind == "rotation_speed");
            var answer = new AssistantMessage { Task = task }; answer.Steps.Add(Job("rotation", "owned:rotation-target", success, measuredSpeed, samples));
            Assert.That(VerifyEvidence(answer).Single(row => row.RequirementId == requirement.Id).Passed, Is.EqualTo(expected));
            Assert.That(task.Status, Is.EqualTo(expected ? "checks_passed" : "checks_failed"));
        }
        [Test] public void LatestRotationMeasurementCannotReuseOlderSuccessfulSpeed()
        {
            var task = AgentTaskVerifier.Create("Вращай объект 30 градусов в секунду", true); var answer = new AssistantMessage { Task = task };
            answer.Steps.Add(Job("rotation", "same-target", true, 30, 5)); answer.Steps.Add(Job("rotation", "same-target", true, 60, 5));
            var check = VerifyEvidence(answer).Single(row => row.RequirementId == task.Requirements.Single().Id);
            Assert.That(check.Passed, Is.False);
        }
        [TestCase("Вращай объект 30 градусов в секунду", "30f", true)]
        [TestCase("Вращай объект 30 градусов в секунду на 2 оборота", "30f", false)]
        [TestCase("Вращай объект тридцать градусов в секунду", "30f", false)]
        [TestCase("Вращай объект 30 градусов в секунду", "60f", false)]
        [TestCase("Вращай объект 30 градусов в секунду", "30f; InvokeExtra()", false)]
        public void AutomaticUnknownSymbolRepairRequiresOneExplicitNumericLiteral(string request, string replacement, bool expected)
        {
            using (var fixture = new RepairFixture(MissingSymbolSource, request))
            {
                fixture.Failure(fixture.Diagnostic("missingSpeed", "CS0103", detail: "The name 'missingSpeed' does not exist in the current context"));
                Assert.That(CanRepair(fixture.Answer, fixture.Patch("missingSpeed", replacement)), Is.EqualTo(expected));
            }
        }
        [TestCase(0, false)] [TestCase(1, true)] [TestCase(2, true)] [TestCase(3, false)]
        public void AutomaticCompilerRepairIsLimitedToTwoReviewedContinuations(int attempts, bool expected)
        {
            using (var fixture = new RepairFixture(MissingSymbolSource))
            {
                fixture.Answer.Task.ResumeAttempts = attempts;
                fixture.Failure(fixture.Diagnostic("missingSpeed", "CS0103", detail: "The name 'missingSpeed' does not exist in the current context"));
                Assert.That(CanRepair(fixture.Answer, fixture.Patch("missingSpeed", "30f")), Is.EqualTo(expected));
            }
        }
        [Test] public void AutomaticRepairRejectsFullDraftExtraActionsAndUnreviewedFile()
        {
            using (var fixture = new RepairFixture(MissingSymbolSource))
            {
                fixture.Failure(fixture.Diagnostic("missingSpeed", "CS0103", detail: "The name 'missingSpeed' does not exist in the current context"));
                var plan = fixture.Patch("missingSpeed", "30f"); plan.actions[0].tool = "draft_script";
                Assert.That(CanRepair(fixture.Answer, plan), Is.False);
                plan = fixture.Patch("missingSpeed", "30f"); plan.actions.Add(new EditorCommand { tool = "create_object", name = "Unrelated" });
                Assert.That(CanRepair(fixture.Answer, plan), Is.False);
                fixture.Answer.Task.ReviewedCodeScope.Clear();
                Assert.That(CanRepair(fixture.Answer, fixture.Patch("missingSpeed", "30f")), Is.False);
            }
        }
        [Test] public void AutomaticRepairRejectsChangedBytesOrSuccessfulLatestCompilation()
        {
            using (var fixture = new RepairFixture(MissingSymbolSource))
            {
                fixture.Failure(fixture.Diagnostic("missingSpeed", "CS0103", detail: "The name 'missingSpeed' does not exist in the current context"));
                File.AppendAllText(fixture.Path, "// Manual edit\n");
                Assert.That(CanRepair(fixture.Answer, fixture.Patch("missingSpeed", "30f")), Is.False);
                File.WriteAllText(fixture.Path, fixture.Source, new UTF8Encoding(false)); fixture.Answer.Steps.Last().Success = true;
                Assert.That(CanRepair(fixture.Answer, fixture.Patch("missingSpeed", "30f")), Is.False);
            }
        }
        [Test] public void AutomaticRepairCannotBorrowAnotherFileLineColumnOrErrorCode()
        {
            using (var fixture = new RepairFixture(MissingSymbolSource))
            {
                string actual = fixture.Diagnostic("missingSpeed", "CS0103", detail: "The name 'missingSpeed' does not exist in the current context");
                var plan = fixture.Patch("missingSpeed", "30f");
                fixture.Failure(actual.Replace(fixture.Path, "Assets/Unrelated.cs")); Assert.That(CanRepair(fixture.Answer, plan), Is.False);
                fixture.Failure(actual.Replace("(3,", "(4,")); Assert.That(CanRepair(fixture.Answer, plan), Is.False);
                fixture.Failure(fixture.Diagnostic("missingSpeed", "CS0103", 1, "The name 'missingSpeed' does not exist in the current context")); Assert.That(CanRepair(fixture.Answer, plan), Is.False);
                fixture.Failure(actual.Replace("CS0103", "CS0117") + "\n" + actual.Replace("(3,", "(99,")); Assert.That(CanRepair(fixture.Answer, plan), Is.False);
            }
        }
        [Test] public void AutomaticSemicolonRepairOnlyAppendsOneTokenAtTheActualDiagnosticColumn()
        {
            const string find = "    float speed = 30f";
            using (var fixture = new RepairFixture("class RepairFixture\n{\n" + find + "\n}\n"))
            {
                fixture.Failure(fixture.Diagnostic(find, "CS1002", find.Length, "; expected"));
                Assert.That(CanRepair(fixture.Answer, fixture.Patch(find, find + ";")), Is.True);
                Assert.That(CanRepair(fixture.Answer, fixture.Patch(find, find + "; InvokeExtra();")), Is.False);
                fixture.Failure(fixture.Diagnostic(find, "CS1002", find.Length - 1, "; expected"));
                Assert.That(CanRepair(fixture.Answer, fixture.Patch(find, find + ";")), Is.False);
            }
        }
        [UnityTest] public IEnumerator AskModeRejectsMutationEvenWhenProviderProposesIt()
        {
            var provider = new Provider { Answer = (request, token) => Task.FromResult(Result(new EditorCommand { tool = "create_object", name = "MustNotExist", args = new JObject { ["kind"] = "cube" } })) };
            var controller = Controller(provider, AssistantMode.Ask);
            yield return Finish(controller.SendAsync("Создай куб"));
            var answer = controller.Current.Messages.Last();
            Assert.That(answer.IsError, Is.True); Assert.That(answer.Task.MutationAuthorized, Is.False); Assert.That(answer.PendingPlan, Is.Null);
            Assert.That(scene.rootCount, Is.Zero); Assert.That(answer.Task.CheckpointId, Is.Null.Or.Empty);
        }
        [TestCase(AssistantMode.Ask, "Создай мир, напиши скрипт, собери игру и добавь персонажа")]
        [TestCase(AssistantMode.Ask, "Изучи ассеты, материалы, навыки и изображение")]
        [TestCase(AssistantMode.Execute, "Только объясни сцену. Ничего не создавай и не изменяй.")]
        public void ExpandedToolSelectionNeverEscapesAskOrExplicitExplanationPermissions(AssistantMode mode, string request)
        {
            var controller = Controller(EmptyProvider(), mode);
            controller.Current.Messages.Add(new AssistantMessage { Role = "user", Text = request });
            controller.Current.Messages.Add(new AssistantMessage { Task = AgentTaskVerifier.Create(request, false) });
            typeof(ChatController).GetField("expandToolSelection", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, true);
            var build = typeof(ChatController).GetMethod("BuildRequest", BindingFlags.NonPublic | BindingFlags.Instance);
            var envelope = (CompletionRequest)build.Invoke(controller, new object[] { controller.Current, request });
            var exposed = ((JArray)envelope.Schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName).ToArray();
            Assert.That(exposed, Is.Not.Empty); Assert.That(exposed.All(SceneToolExecutor.IsReadOnlyTool), Is.True);
            Assert.That(envelope.Messages.Any(message => message.Role == "system" && message.Content.Contains("User interaction mode: Ask.")), Is.True);
            Assert.That(controller.Current.Messages.Last().Task.MutationAuthorized, Is.False); Assert.That(scene.rootCount, Is.Zero);
        }
        [UnityTest] public IEnumerator IncompleteAskReplyDoesNotExpandToolsOrRunAnEditingFallback()
        {
            var provider = new Provider { Answer = (request, token) =>
            {
                var exposed = ((JArray)request.Schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName);
                Assert.That(exposed.All(SceneToolExecutor.IsReadOnlyTool), Is.True);
                return Task.FromResult(new CompletionResult { Text = JsonConvert.SerializeObject(new EditorPlan { reply = "Нужны уточнения", complete = false }) });
            } };
            var controller = Controller(provider, AssistantMode.Ask); controller.Settings.Power = AssistantPower.Balanced;
            yield return Finish(controller.SendAsync("Объясни скрипт и текущую сцену"));
            var answer = controller.Current.Messages.Last(); Assert.That(provider.Calls, Is.EqualTo(1));
            Assert.That(answer.Task.MutationAuthorized, Is.False); Assert.That(answer.Steps, Is.Empty); Assert.That(answer.PendingPlan, Is.Null);
            Assert.That(answer.Task.CheckpointId, Is.Null.Or.Empty); Assert.That(scene.rootCount, Is.Zero);
        }
        [UnityTest] public IEnumerator AskKeepsLiteralRequirementsWithoutClaimingTheyWereChecked()
        {
            var controller = Controller(EmptyProvider(), AssistantMode.Ask);
            yield return Finish(controller.SendAsync("Как создать 3 дерева?"));
            var answer = controller.Current.Messages.Last(); Assert.That(answer.Task.Requirements.Count, Is.EqualTo(1));
            Assert.That(answer.Task.Checks, Is.Empty); Assert.That(answer.Status, Is.EqualTo("Готово")); Assert.That(scene.rootCount, Is.Zero);
        }
        [UnityTest] public IEnumerator PlanDefersMutationAndApplyingItKeepsOriginalCriteria()
        {
            var commands = Enumerable.Range(0, 3).Select(i => new EditorCommand { tool = "set_lighting", name = "ReviewedPoint" + i,
                args = new JObject { ["type"] = "point", ["position"] = new JArray(i * 4, 5, 0), ["intensity"] = 1 } }).ToArray();
            var provider = new Provider { Answer = (request, token) => Task.FromResult(Result(commands)) };
            var controller = Controller(provider, AssistantMode.Plan);
            yield return Finish(controller.SendAsync("Создай ровно 3 источника света"));
            var answer = controller.Current.Messages.Last();
            Assert.That(scene.rootCount, Is.Zero); Assert.That(answer.PendingPlan, Is.Not.Null); Assert.That(answer.Task.MutationAuthorized, Is.False);
            Assert.That(answer.Task.Requirements.Single().Number, Is.EqualTo(3));
            yield return Finish(controller.ApplyPendingAsync(answer.Id));
            Assert.That(answer.IsError, Is.False, answer.Text); Assert.That(answer.PendingPlan, Is.Null);
            Assert.That(ProjectContextIndex.Objects().Count(go => go.GetComponent<UnityEngine.Light>()), Is.EqualTo(3));
            Assert.That(answer.Task.MutationAuthorized, Is.True); Assert.That(answer.Task.Checks.Single().Passed, Is.True);
            Assert.That(provider.Calls, Is.EqualTo(1), "Applying a reviewed plan should not ask a second model to recreate it.");
        }
        [UnityTest] public IEnumerator EmptyModelCompletionCannotPassMissingLiteralGoal()
        {
            var controller = Controller(EmptyProvider(), AssistantMode.Execute);
            yield return Finish(controller.SendAsync("Создай 3 дерева"));
            var answer = controller.Current.Messages.Last();
            Assert.That(answer.IsError, Is.True); Assert.That(answer.Task.Checks.Single().Passed, Is.False);
            Assert.That(answer.Status, Is.Not.EqualTo("Проверки пройдены")); Assert.That(scene.rootCount, Is.Zero);
        }
        [UnityTest] public IEnumerator EmptyModelCompletionCannotOverrideFailedCompilerEvidence()
        {
            ChatController controller = null;
            var provider = new Provider { Answer = (request, token) =>
            {
                controller.Current.Messages.Last().Steps.Add(new EditorCommandResult { Tool = "compile_scripts", Target = "Assets/FailedWorkflowFixture.cs", Success = false, Error = "CS1002" });
                return Task.FromResult(Result());
            } };
            controller = Controller(provider, AssistantMode.Execute);
            yield return Finish(controller.SendAsync("Создай C# скрипт для проверки"));
            var answer = controller.Current.Messages.Last();
            Assert.That(answer.IsError, Is.True); Assert.That(answer.Task.Status, Is.EqualTo("checks_failed"));
            Assert.That(answer.Task.Checks.Single(row => row.RequirementId == "compile:Assets/FailedWorkflowFixture.cs").Passed, Is.False);
            Assert.That(scene.rootCount, Is.Zero);
        }
        [UnityTest] public IEnumerator EmptyModelCompletionCannotOverrideFailedEditorJobEvidence()
        {
            ChatController controller = null;
            var provider = new Provider { Answer = (request, token) =>
            {
                controller.Current.Messages.Last().Steps.Add(Job("tests", "Workflow.Failed", false));
                return Task.FromResult(Result());
            } };
            controller = Controller(provider, AssistantMode.Execute);
            yield return Finish(controller.SendAsync("Проверь результаты тестирования"));
            var answer = controller.Current.Messages.Last();
            Assert.That(answer.IsError, Is.True); Assert.That(answer.Task.Status, Is.EqualTo("checks_failed"));
            Assert.That(answer.Task.Checks.Any(row => !row.Passed && row.RequirementId == "job:tests:Workflow.Failed"), Is.True);
            Assert.That(scene.rootCount, Is.Zero);
        }
        [UnityTest] public IEnumerator PendingContinuationBlocksNewSendAndStopClearsIt()
        {
            var provider = EmptyProvider(); var controller = Controller(provider);
            controller.Current.Messages.Add(new AssistantMessage { Task = new AgentTaskState { ContinueAfterReload = true }, Text = "Waiting" });
            Assert.That(controller.Busy, Is.False); Assert.That(controller.HasPendingWork, Is.True);
            yield return Finish(controller.SendAsync("Это сообщение не должно запуститься"));
            Assert.That(provider.Calls, Is.Zero); Assert.That(controller.Current.Messages.Count, Is.EqualTo(1));
            controller.Cancel(); Assert.That(controller.HasPendingWork, Is.False); Assert.That(controller.Current.Messages.Single().Task.ContinueAfterReload, Is.False);
        }
        [UnityTest] public IEnumerator StreamShowsOnlyDecodedReplyWhileJsonIsIncomplete()
        {
            ChatController controller = null;
            var provider = new Provider { Answer = async (request, token) =>
            {
                Assert.That(request.Stream, Is.True);
                request.OnContentDelta("{\"reply\":\"Привет \\u04");
                Assert.That(controller.Current.Messages.Last().Text, Is.EqualTo("Привет "));
                await Task.Yield();
                request.OnContentDelta("3Cир\\n\\\"");
                Assert.That(controller.Current.Messages.Last().Text, Is.EqualTo("Привет мир\n\""));
                request.OnContentDelta("да\\\"\",\"complete\":true,\"actions\":[]}");
                return new CompletionResult { Text = "{\"reply\":\"Привет \\u043Cир\\n\\\"да\\\"\",\"complete\":true,\"actions\":[]}" };
            } };
            controller = Controller(provider);
            yield return Finish(controller.SendAsync("Объясни сцену"));
            Assert.That(controller.Current.Messages.Last().Text, Is.EqualTo("Привет мир\n\"да\"")); Assert.That(scene.rootCount, Is.Zero);
        }
        [UnityTest] public IEnumerator CancellationIgnoresLateStreamAndProviderCompletion()
        {
            ChatController controller = null;
            var provider = new Provider { Answer = (request, token) =>
            {
                request.OnContentDelta("{\"reply\":\"Начало"); controller.Cancel(); request.OnContentDelta(" обман после отмены");
                return Task.FromResult(Result(new EditorCommand { tool = "create_object", name = "LateObject", args = new JObject { ["kind"] = "cube" } }));
            } };
            controller = Controller(provider, AssistantMode.Execute);
            yield return Finish(controller.SendAsync("Создай куб"));
            var answer = controller.Current.Messages.Last(); Assert.That(answer.Status, Is.EqualTo("Остановлено"));
            StringAssert.DoesNotContain("обман", answer.Text); Assert.That(scene.rootCount, Is.Zero); Assert.That(answer.Steps, Is.Empty);
        }
        [UnityTest] public IEnumerator UnsupportedModelAndOversizedImageListFailBeforeHistoryOrInference()
        {
            var provider = EmptyProvider(); var controller = Controller(provider);
            var images = new[] { new ChatAttachment { Path = "../outside.png", Sha256 = new string('0', 64) } };
            controller.Settings.ModelKey = "ollama:qwen2.5-coder:latest";
            yield return Finish(ExpectArgument(controller.SendAsync("Изучи изображение", images)));
            controller.Settings.ModelKey = "ollama:qwen3.5:9b";
            yield return Finish(ExpectArgument(controller.SendAsync("Изучи изображения", Enumerable.Repeat(images[0], 4))));
            yield return Finish(ExpectArgument(controller.SendAsync("Изучи изображение", images)));
            Assert.That(provider.Calls, Is.Zero); Assert.That(controller.Current.Messages, Is.Empty); Assert.That(scene.rootCount, Is.Zero);
        }
        [UnityTest] public IEnumerator ManagedImageHashAndNativeRequestEnvelopeAreEnforced()
        {
            var image = ManagedImage(); var provider = EmptyProvider(); var controller = Controller(provider);
            var changed = new ChatAttachment { Path = image.Path, Name = image.Name, Sha256 = new string('0', 64) };
            yield return Finish(ExpectArgument(controller.SendAsync("Изучи изображение", new[] { changed })));
            Assert.That(controller.Current.Messages, Is.Empty);
            provider.Answer = (request, token) =>
            {
                Assert.That(request.Messages.Sum(message => message.Images.Count), Is.EqualTo(1));
                Assert.That(request.Messages.Last().Images.Single(), Is.EqualTo(AgentViewCapture.ReadBase64(image)));
                return Task.FromResult(Result());
            };
            yield return Finish(controller.SendAsync("Изучи изображение", new[] { image }));
            Assert.That(provider.Calls, Is.EqualTo(1)); Assert.That(controller.Current.Messages.First().Attachments.Single().Sha256, Is.EqualTo(image.Sha256));
        }
        private ChatAttachment ManagedImage()
        {
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+iZEEAAAAASUVORK5CYII=");
            string path = "UserSettings/WorldWeaverImages/workflow-test-" + Guid.NewGuid().ToString("N") + ".png"; Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, png); imageFiles.Add(path);
            using (var sha = SHA256.Create()) return new ChatAttachment { Path = path, Name = "Test reference", Sha256 = BitConverter.ToString(sha.ComputeHash(png)).Replace("-", "").ToLowerInvariant() };
        }
        private static async Task ExpectArgument(Task operation)
        { bool rejected = false; try { await operation; } catch (ArgumentException) { rejected = true; } Assert.That(rejected, Is.True); }
    }
}
