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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    /// <summary>Actual controller, journal and executor with explicit controlled inference; no network or source import.</summary>
    public sealed class TruncatedPlanControllerTests
    {
        Scene scene, previous;
        string folder;
        ChatController controller;
        readonly HashSet<string> assets = new HashSet<string>();
        sealed class Provider : ILLMProvider
        {
            public readonly List<CompletionRequest> Requests = new List<CompletionRequest>();
            public Func<int, CompletionRequest, CompletionResult> Reply;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); int round = Requests.Count; Requests.Add(request);
                return Task.FromResult(Reply(round, request));
            }
            public Task<bool> HealthAsync(CancellationToken token) => Task.FromResult(true);
            public void Dispose() { }
        }
        [SetUp] public void SetUp()
        {
            previous = SceneManager.GetActiveScene();
            bool untitled = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s => s.isLoaded && string.IsNullOrEmpty(s.path));
            if (untitled && !Application.isBatchMode) Assert.Ignore("Save open GUI scenes before these isolated controller tests.");
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene); folder = Path.Combine(Path.GetTempPath(), "WorldWeaverTruncatedPlanTests_" + Guid.NewGuid().ToString("N"));
        }
        [TearDown] public void TearDown()
        {
            if (controller != null)
            {
                foreach (var step in controller.Sessions.SelectMany(s => s.Messages).SelectMany(m => m.Steps))
                    foreach (string path in step.CreatedAssets) assets.Add(path);
                controller.Dispose(); controller = null;
            }
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (scene.IsValid() && scene.isLoaded)
            {
                if (previous.IsValid() && previous.isLoaded) EditorSceneManager.CloseScene(scene, true);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            foreach (string path in assets.OrderByDescending(p => p.Length)) if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
            foreach (string parent in assets.Select(p => Path.GetDirectoryName(p)?.Replace('\\', '/')).Where(p => p != null).Distinct().OrderByDescending(p => p.Length))
                if (AssetDatabase.IsValidFolder(parent) && AssetDatabase.FindAssets("", new[] { parent }).Length == 0) AssetDatabase.DeleteAsset(parent);
            assets.Clear(); if (folder != null && Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        void CreateController(Provider provider)
        {
            controller = new ChatController(_ => provider, folder, Path.Combine(folder, "models"));
            controller.Settings.Mode = AssistantMode.Execute; controller.Settings.Power = AssistantPower.Fast;
            controller.Settings.ModelKey = "ollama:qwen3.5:9b"; controller.Settings.AutoApply = true; controller.Settings.UseSkills = false;
        }
        GameObject Find(string name) => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).FirstOrDefault(o => o.name == name);
        static string Plan(params EditorCommand[] actions) => JsonConvert.SerializeObject(new EditorPlan { reply = "Controlled fixture", complete = true, actions = actions.ToList() });
        static EditorCommand Cube(string name) => new EditorCommand { tool = "create_object", name = name, args = new JObject { ["kind"] = "cube", ["on_terrain"] = false } };
        static CompletionResult Result(string text, string reason = "stop") => new CompletionResult { Text = text, FinishReason = reason, OutputTruncated = CompletionResult.IsOutputLimit(reason), Provider = "controlled-test", Model = "controlled-model", ElapsedMs = 17, InputTokens = 13, OutputTokens = 7 };
        static IEnumerator Finish(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted, "Controlled provider should finish without inference or external network.");
            if (task.IsFaulted) Assert.Fail(task.Exception.ToString());
        }
        [UnityTest] public IEnumerator TokenLimitedBalancedJsonNeverExecutesAndFastReservesOnlyAPlanningRepair()
        {
            string limited = Plan(Cube("MustNotExecuteLimitedCube")); var provider = new Provider();
            provider.Reply = (round, request) =>
            {
                if (round == 0) return Result(limited, "length");
                Assert.AreEqual(1, round); Assert.IsNull(Find("MustNotExecuteLimitedCube")); Assert.AreEqual(0, scene.rootCount);
                Assert.IsFalse(request.Messages.Any(m => m.Content.Contains("MustNotExecuteLimitedCube")), "A length-limited response must stay in the audit trail and never anchor any fresh repair message.");
                return Result(Plan(Cube("OnlyRepairedCube")));
            };
            CreateController(provider); yield return Finish(controller.SendAsync("Создай куб."));
            var answer = controller.Current.Messages.Last(); Assert.IsFalse(answer.IsError, answer.Text); Assert.AreEqual(2, provider.Requests.Count);
            Assert.IsNull(Find("MustNotExecuteLimitedCube")); Assert.IsNotNull(Find("OnlyRepairedCube")); Assert.AreEqual(1, answer.Steps.Count(s => s.Success && s.Tool == "create_object"));
            var first = answer.Attempts[0]; Assert.IsFalse(first.Accepted); Assert.IsTrue(first.OutputTruncated); Assert.AreEqual("length", first.FinishReason);
            Assert.AreEqual(limited, first.RawResponse); Assert.IsNull(first.NormalizedResponse); StringAssert.Contains("лимит", first.Error);
            Assert.AreEqual(2, answer.Attempts.Select(a => a.Round).Distinct().Count(), "The planning retry must retain distinct audit attempt numbers.");
            var saved = new ChatHistoryStore(folder).Load().Single(s => s.Id == controller.Current.Id).Messages.Last();
            Assert.IsTrue(saved.Attempts[0].OutputTruncated); Assert.AreEqual("length", saved.Attempts[0].FinishReason); Assert.AreEqual(limited, saved.Attempts[0].RawResponse);
        }
        [UnityTest] public IEnumerator UnterminatedMixedCodeGetsOneCompletePendingDraftWithoutAnySceneOrSourceWrites()
        {
            const string raw = "{\"reply\":\"Preparing\",\"complete\":true,\"actions\":[{\"tool\":\"create_npc\",\"name\":\"RejectedPartialHero\",\"target\":\"\",\"args\":{}},{\"tool\":\"draft_script\",\"name\":\"HeroMovement.cs\",\"target\":\"invented-target\",\"args\":{\"code\":\"using UnityEngine; public class HeroMovement { void Update() { animator.Play(\\\"Walk";
            const string className = "TruncatedReviewMover";
            const string source = "using UnityEngine; public class TruncatedReviewMover : MonoBehaviour { void Update() { if(Input.GetKeyDown(KeyCode.F)) enabled=false; } }";
            string path = "Assets/WorldWeaverGenerated/Scripts/" + className + ".cs"; Assert.IsFalse(File.Exists(path)); var provider = new Provider();
            provider.Reply = (round, request) =>
            {
                if (round == 0) return Result(raw, null); // Older gateways may omit finish_reason; malformed code still has a bounded repair path.
                Assert.AreEqual(1, round); Assert.AreEqual(0, scene.rootCount); Assert.IsFalse(File.Exists(path));
                var actions = request.Schema["properties"]["actions"]; var tools = ((JArray)actions["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName).ToArray();
                CollectionAssert.AreEqual(new[] { "draft_script" }, tools); Assert.AreEqual(1, (int)actions["minItems"]); Assert.AreEqual(1, (int)actions["maxItems"]);
                Assert.AreEqual(false, (bool)request.Schema["properties"]["complete"]["enum"][0]); Assert.IsFalse(request.Messages.Any(m => m.Content.Contains("RejectedPartialHero") || m.Content.Contains("invented-target")), "Rejected raw identifiers stay in history, not the fresh repair prompt.");
                return Result(JsonConvert.SerializeObject(new EditorPlan { complete = false, actions = new List<EditorCommand> { new EditorCommand { tool = "draft_script", name = className, args = new JObject { ["code"] = source } } } }));
            };
            CreateController(provider); yield return Finish(controller.SendAsync("Напиши C# MonoBehaviour, реагирующий на клавишу F; подготовь полный исходник для просмотра."));
            var answer = controller.Current.Messages.Last(); Assert.IsFalse(answer.IsError, answer.Text); Assert.AreEqual(2, answer.Attempts.Count);
            Assert.IsFalse(answer.Attempts[0].Accepted); Assert.AreEqual(raw, answer.Attempts[0].RawResponse); Assert.IsNull(answer.Attempts[0].NormalizedResponse);
            Assert.IsTrue(answer.Attempts[1].Accepted); Assert.AreEqual(0, scene.rootCount); Assert.IsEmpty(answer.Steps); Assert.IsFalse(File.Exists(path));
            Assert.IsNotNull(answer.PendingPlan); Assert.AreEqual(1, answer.PendingPlan.actions.Count); Assert.AreEqual("draft_script", answer.PendingPlan.actions[0].tool); Assert.AreEqual(source, (string)answer.PendingPlan.actions[0].args["code"]);
            var saved = new ChatHistoryStore(folder).Load().Single(s => s.Id == controller.Current.Id).Messages.Last(); Assert.AreEqual(raw, saved.Attempts[0].RawResponse); Assert.IsNotNull(saved.PendingPlan);
        }
        [UnityTest] public IEnumerator ValidReadOnlyReplacementClearsOnlyThePreviousFormatFailure()
        {
            var provider=new Provider();provider.Reply=(round,request)=>Result(Plan(),round==0?"length":"stop");
            CreateController(provider);controller.Settings.Mode=AssistantMode.Ask;
            yield return Finish(controller.SendAsync("Объясни назначение CharacterController."));
            var answer=controller.Current.Messages.Last();Assert.AreEqual(2,answer.Attempts.Count);
            Assert.IsFalse(answer.Attempts[0].Accepted);Assert.IsTrue(answer.Attempts[1].Accepted);
            Assert.IsFalse(answer.IsError,answer.Text);Assert.IsEmpty(answer.Steps);Assert.AreEqual(0,scene.rootCount);
        }
        [UnityTest] public IEnumerator RealHeroUsesCompactContractAndAnEmptyAnswerCannotPassEvenWithoutOptionalAutoVerify()
        {
            var provider=new Provider();provider.Reply=(round,request)=>
            {
                Assert.Less(request.Messages.Sum(m=>m.Content.Length),5000,"A built-in hero should not receive unrelated code/material/skill prompts.");
                var branches=(JArray)request.Schema["properties"]["actions"]["items"]["anyOf"];
                CollectionAssert.AreEquivalent(new[]{"create_animated_player","find_scene_objects","inspect_object"},branches.OfType<JObject>().Select(EditorToolSchema.ToolName));
                return Result(Plan());
            };
            CreateController(provider);controller.Settings.AutoVerify=false;controller.Settings.UseSkills=true;
            yield return Finish(controller.SendAsync(AnimatedPlayerGoalTests.Request));
            var answer=controller.Current.Messages.Last();Assert.IsTrue(answer.IsError);Assert.IsEmpty(answer.Steps);
            Assert.That(answer.Task.Checks.Any(c=>!c.Passed),Is.True);Assert.AreEqual(0,scene.rootCount);
        }
    }
}
