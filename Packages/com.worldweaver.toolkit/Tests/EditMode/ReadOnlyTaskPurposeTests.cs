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
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    /// <summary>Explanation purpose is distinct from an implementation plan awaiting permission.</summary>
    public sealed class ReadOnlyTaskPurposeTests
    {
        const string RussianExplanation = "Только объясни, как создать 2 дерева и управляемого игрока. Скрипты не изменяй, C# код не создавай и Play Mode не запускай.";
        const string EnglishExplanation = "Only explain how to create 2 trees and a playable player. Do not write a C# script or change code, and do not run Play Mode.";
        const string Implementation = "Create 2 trees, a playable player and write a C# script.";
        Scene scene, previous;
        string folder;
        bool fixtureActive;
        readonly List<ChatController> controllers = new List<ChatController>();

        sealed class Provider : ILLMProvider
        {
            public int Calls;
            public Func<int, EditorPlan> Reply;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new CompletionResult { Text = JsonConvert.SerializeObject(Reply(Calls++)), Model = "Injected", Provider = "InjectedTest" });
            }
            public Task<bool> HealthAsync(CancellationToken token) => Task.FromResult(true);
            public void Dispose() { }
        }

        [SetUp] public void SetUp()
        {
            fixtureActive = false; scene = default;
            previous = SceneManager.GetActiveScene();
            bool untitled = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s => s.isLoaded && string.IsNullOrEmpty(s.path));
            if (untitled && !Application.isBatchMode) Assert.Ignore("Save open scenes before GUI execution of this isolated fixture.");
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene); fixtureActive = true;
            folder = Path.Combine(Path.GetTempPath(), "ScenePilotTaskPurpose_" + Guid.NewGuid().ToString("N"));
        }
        [TearDown] public void TearDown()
        {
            if (!fixtureActive) return;
            var checkpoints = controllers.SelectMany(c => c.Sessions).SelectMany(s => s.Messages).Select(m => m.Task?.CheckpointId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToArray();
            foreach (var controller in controllers) controller.Dispose();
            controllers.Clear();
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (scene.IsValid() && scene.isLoaded)
            {
                if (previous.IsValid() && previous.isLoaded) EditorSceneManager.CloseScene(scene, true);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            foreach (string id in checkpoints)
                if (Guid.TryParseExact(id, "N", out _))
                {
                    string path = Path.Combine("UserSettings/WorldWeaverCheckpoints", id);
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                }
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            fixtureActive = false;
        }
        static IReadOnlyList<AgentCheckResult> Verify(AssistantMessage answer)
        {
            var method = typeof(ChatController).GetMethod("VerifyTask", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (IReadOnlyList<AgentCheckResult>)method.Invoke(null, new object[] { answer });
        }
        ChatController Controller(Provider provider, AssistantMode mode)
        {
            var controller = new ChatController(_ => provider, Path.Combine(folder, Guid.NewGuid().ToString("N")));
            controllers.Add(controller);
            controller.Settings.Mode = mode; controller.Settings.Power = AssistantPower.Balanced;
            controller.Settings.AutoVerify = true; controller.Settings.UseNativeFunctions = false; controller.Settings.UseSkills = false;
            return controller;
        }
        static EditorPlan Plan(params EditorCommand[] commands) => new EditorPlan { complete = commands.Length == 0, reply = "Observed explanation.", actions = commands.ToList() };
        static IEnumerator Finish(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "The injected controller did not complete.");
            task.GetAwaiter().GetResult();
        }

        [TestCase(RussianExplanation)]
        [TestCase(EnglishExplanation)]
        [TestCase(Implementation)]
        public void ExplanationPurposeDoesNotInferAuthoringCriteriaFromCodePlayerOrCounts(string input)
        {
            var task = AgentTaskVerifier.Create(input, false, true);
            Assert.That(task.Purpose, Is.EqualTo("explanation"));
            Assert.That(task.CompilationRequired, Is.False);
            Assert.That(task.Requirements, Is.Empty);
            var answer = new AssistantMessage { Task = task, Status = "Готово" };
            Assert.That(Verify(answer), Is.Empty);
            Assert.That(ChatTaskPresentation.From(answer).Visible, Is.False, "An explanation must not display missing world edits as a failed task card.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ExecuteAndUnapprovedPlanKeepFutureImplementationCriteria(bool mutationAuthorized)
        {
            var task = AgentTaskVerifier.Create(Implementation, mutationAuthorized);
            Assert.That(task.Purpose, Is.EqualTo("implementation"));
            Assert.That(task.CompilationRequired, Is.True);
            Assert.That(task.Requirements.Any(r => r.Kind == "count" && r.Expected == "tree" && r.Number == 2), Is.True);
            Assert.That(task.Requirements.Any(r => r.Kind == "player"), Is.True);
            var checks = Verify(new AssistantMessage { Task = task });
            Assert.That(checks.Any(c => c.RequirementId == "required_compilation" && !c.Passed), Is.True);
            Assert.That(checks.Count(c => !c.Passed), Is.GreaterThanOrEqualTo(3));
        }

        [TestCase(RussianExplanation)]
        [TestCase(EnglishExplanation)]
        public void LegacyExplicitExplanationDoesNotRequireNewCompilation(string input)
        {
            // Reproduces the actual RC16 persisted shape, including the stale inferred flag.
            var task = JsonConvert.DeserializeObject<AgentTaskState>(JsonConvert.SerializeObject(new { OriginalRequest = input, MutationAuthorized = false, CompilationRequired = true }));
            Assert.That(task.Purpose, Is.Null);
            var answer = new AssistantMessage { Task = task, Status = "Готово" };
            Assert.That(Verify(answer), Is.Empty);
            Assert.That(ChatTaskPresentation.From(answer).Visible, Is.False);
        }

        [Test] public void LegacyUnapprovedImplementationIsNotMistakenForAnExplanation()
        {
            var task = new AgentTaskState { OriginalRequest = "Write a C# script.", MutationAuthorized = false, CompilationRequired = true };
            Assert.That(Verify(new AssistantMessage { Task = task }).Single(c => c.RequirementId == "required_compilation").Passed, Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PurposeSurvivesPersistenceIndependentlyOfCurrentMutationPermission(bool explanation)
        {
            var task = JsonConvert.DeserializeObject<AgentTaskState>(JsonConvert.SerializeObject(AgentTaskVerifier.Create(Implementation, false, explanation)));
            Assert.That(task.MutationAuthorized, Is.False);
            Assert.That(task.Purpose, Is.EqualTo(explanation ? "explanation" : "implementation"));
            Assert.That(Verify(new AssistantMessage { Task = task }).Any(c => c.RequirementId == "required_compilation"), Is.EqualTo(!explanation));
        }

        [Test] public void ExplanationDoesNotHideActualFailedCompilationEvidence()
        {
            // Synthetic persisted compiler evidence tests aggregation, not a new compiler invocation.
            var answer = new AssistantMessage { Task = AgentTaskVerifier.Create(EnglishExplanation, false, true), Status = "Проверки не пройдены", IsError = true };
            answer.Task.CompilationRequired = true;
            answer.Steps.Add(new EditorCommandResult { Tool = "compile_scripts", Target = "Assets/ObservedFailure.cs", Success = false, Error = "Observed compiler failure", Summary = "Recorded failure for aggregation policy." });
            var checks = Verify(answer);
            Assert.That(checks.Any(c => c.RequirementId == "required_compilation"), Is.False);
            Assert.That(checks.Single(c => c.RequirementId == "compile:Assets/ObservedFailure.cs").Passed, Is.False);
            Assert.That(answer.IsError, Is.True);
            Assert.That(ChatTaskPresentation.From(answer).CssClass, Is.EqualTo("task-card-error"));
        }

        [Test] public void ExplanationDoesNotEraseExplicitPreservationChecks()
        {
            var retained = new GameObject("RetainedTree"); retained.AddComponent<GeneratedSceneObject>().Kind = "tree";
            string target = ProjectContextIndex.StableTarget(retained);
            var task = AgentTaskVerifier.Create(EnglishExplanation, false, true);
            var requirement = new AgentRequirement { Kind = "preserve", Expected = "tree", Number = 1, Description = "Observed object must remain unchanged.", BaselineTargets = new List<string> { target } };
            task.Requirements.Add(requirement);
            var answer = new AssistantMessage { Task = task };
            Assert.That(Verify(answer).Single(c => c.RequirementId == requirement.Id).Passed, Is.True);
            UnityEngine.Object.DestroyImmediate(retained);
            Assert.That(Verify(answer).Single(c => c.RequirementId == requirement.Id).Passed, Is.False);
        }

        [UnityTest] public IEnumerator AskAndExplicitExecuteExplanationsRemainReadOnlyAfterActualInspection()
        {
            var inspected = new GameObject("InspectedObject"); inspected.transform.position = new Vector3(3, 2, 1);
            foreach (var row in new[] { (AssistantMode.Ask, Implementation), (AssistantMode.Execute, RussianExplanation), (AssistantMode.Execute, EnglishExplanation) })
            {
                var provider = new Provider { Reply = round => round == 0 ? Plan(new EditorCommand { tool = "inspect_object", target = "id:" + inspected.GetInstanceID() }) : Plan() };
                var controller = Controller(provider, row.Item1);
                yield return Finish(controller.SendAsync(row.Item2));
                var answer = controller.Current.Messages.Last();
                Assert.That(answer.Task.Purpose, Is.EqualTo("explanation"));
                Assert.That(answer.Task.MutationAuthorized, Is.False);
                Assert.That(answer.Task.CompilationRequired, Is.False);
                Assert.That(answer.Task.Requirements, Is.Empty);
                Assert.That(answer.Task.Checks, Is.Empty);
                Assert.That(answer.IsError, Is.False, answer.Text);
                Assert.That(answer.Steps.Single().Tool, Is.EqualTo("inspect_object"));
                Assert.That(answer.Steps.Single().Success, Is.True);
                Assert.That(answer.PendingPlan, Is.Null);
                Assert.That(ChatTaskPresentation.From(answer).Visible, Is.False);
                Assert.That(inspected.transform.position, Is.EqualTo(new Vector3(3, 2, 1)));
                Assert.That(scene.rootCount, Is.EqualTo(1));
            }
        }

        [UnityTest] public IEnumerator PlanApprovalRetainsPlayerAndCompilationRequirements()
        {
            var provider = new Provider { Reply = _ => new EditorPlan { complete = true, reply = "A partial plan for review.", actions = new List<EditorCommand> { new EditorCommand { tool = "create_node", name = "ApprovedPartialNode", args = new JObject() } } } };
            var controller = Controller(provider, AssistantMode.Plan);
            yield return Finish(controller.SendAsync("Create a playable player and write a C# script."));
            var answer = controller.Current.Messages.Last();
            Assert.That(answer.PendingPlan, Is.Not.Null);
            Assert.That(answer.Task.MutationAuthorized, Is.False);
            Assert.That(answer.Task.Purpose, Is.EqualTo("implementation"));
            Assert.That(answer.Task.CompilationRequired, Is.True);
            string playerRequirement = answer.Task.Requirements.Single(r => r.Kind == "player").Id;
            Assert.That(scene.rootCount, Is.Zero);
            // Do not schedule further inference; this fixture covers approval and real executor verification.
            answer.Task.ResumeAttempts = int.MaxValue;
            yield return Finish(controller.ApplyPendingAsync(answer.Id));
            Assert.That(answer.Task.MutationAuthorized, Is.True);
            Assert.That(answer.Task.Purpose, Is.EqualTo("implementation"));
            Assert.That(answer.Task.CompilationRequired, Is.True);
            Assert.That(answer.PendingPlan, Is.Null);
            Assert.That(answer.Steps.Any(s => s.Tool == "create_node" && s.Success), Is.True);
            Assert.That(answer.Task.Checks.Single(c => c.RequirementId == playerRequirement).Passed, Is.False);
            Assert.That(answer.Task.Checks.Single(c => c.RequirementId == "required_compilation").Passed, Is.False);
            Assert.That(answer.IsError, Is.True, "Approval of a partial node plan cannot satisfy missing player/code requirements.");
        }
    }
}
