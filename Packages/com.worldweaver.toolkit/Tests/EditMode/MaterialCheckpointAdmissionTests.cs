using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
    public sealed class MaterialCheckpointAdmissionTests
    {
        string folder, checkpoint, history, file;
        GameObject fixture;
        HashSet<string> materialFolders;
        [SetUp] public void Setup()
        {
            if(!Application.isBatchMode) Assert.Ignore("Запускайте проверки контрольных точек в отдельном batchmode-проекте, чтобы сохранить открытые пользовательские сцены.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            folder = "Assets/WorldWeaverMaterialAdmission" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            file = folder + "/state.txt"; File.WriteAllText(file, "before"); AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceSynchronousImport);
            history = Path.GetFullPath("UserSettings/WorldWeaverMaterialAdmission/" + Guid.NewGuid().ToString("N"));
            fixture = new GameObject("Material admission fixture");
            materialFolders = Folders();
        }
        [TearDown] public void Cleanup()
        {
            if(!Application.isBatchMode) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); AssetDatabase.DeleteAsset(folder);
            foreach (string path in Folders().Where(p => !materialFolders.Contains(p))) AssetDatabase.DeleteAsset(path.Replace('\\', '/'));
            if (checkpoint != null) Directory.Delete(Path.Combine("UserSettings/WorldWeaverCheckpoints", checkpoint), true);
            if (Directory.Exists(history)) Directory.Delete(history, true);
        }
        static HashSet<string> Folders() => new HashSet<string>(Directory.Exists(WorldMaterialLibrary.Root) ? Directory.GetDirectories(WorldMaterialLibrary.Root) : Array.Empty<string>(), StringComparer.Ordinal);
        AssistantMessage ConflictedTask(ChatController controller)
        {
            var message = new AssistantMessage { Task = new AgentTaskState { MutationAuthorized = true }, Role = "assistant" }; controller.Current.Messages.Add(message);
            checkpoint = AgentCheckpoints.Begin(controller.Current.Id, message.Id);
            File.WriteAllText(file, "agent"); AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceSynchronousImport);
            fixture.transform.position = Vector3.right; EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); AgentCheckpoints.Seal(checkpoint, true);
            message.Task.CheckpointId = checkpoint; message.Task.CheckpointSealed = true;
            File.WriteAllText(file, "manual edit after sealed agent step"); AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceSynchronousImport);
            fixture.transform.position = Vector3.up * 7; EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Assert.IsFalse(AgentCheckpoints.CanExtend(checkpoint)); Assert.IsFalse(AgentCheckpoints.CanRestore(checkpoint)); return message;
        }
        Task<EditorBatchResult> Execute(ChatController controller, AssistantMessage message, string material, bool online, CancellationToken token = default)
        {
            var plan = new EditorPlan { actions = new List<EditorCommand> { new EditorCommand { tool = "material_import", args = new JObject { ["material_id"] = material, ["allow_online"] = online } } } };
            var method = typeof(ChatController).GetMethod("ExecutePlanAsync", BindingFlags.Instance | BindingFlags.NonPublic); Assert.IsNotNull(method);
            return (Task<EditorBatchResult>)method.Invoke(controller, new object[] { plan, controller.Current.Id, message.Id, null, false, token });
        }
        byte[] Journal() => File.ReadAllBytes(Path.Combine("UserSettings/WorldWeaverCheckpoints", checkpoint, "journal.json"));
        void Preserved(byte[] journal, HashSet<string> before)
        {
            CollectionAssert.AreEqual(journal, Journal(), "A refused/unstarted import must never reseal the older journal around unrelated manual edits.");
            Assert.AreEqual("manual edit after sealed agent step", File.ReadAllText(file)); Assert.AreEqual(Vector3.up * 7, fixture.transform.position);
            Assert.IsFalse(AgentCheckpoints.CanExtend(checkpoint)); Assert.IsFalse(AgentCheckpoints.CanRestore(checkpoint)); CollectionAssert.AreEquivalent(before, Folders());
        }
        [UnityTest] public IEnumerator RejectedMutationCallbackKeepsTheOlderSealedJournalByteExactly() => Await(CheckRejectedAdmission());
        async Task CheckRejectedAdmission()
        {
            string uncached = WorldMaterialLibrary.BuiltIns.Select(d => d.Id).FirstOrDefault(id => !WorldMaterialLibrary.IsImported(id));
            Assert.IsNotNull(uncached, "This isolated acceptance project must keep at least one starter surface uncached to exercise first-write admission.");
            using (var controller = new ChatController(historyFolder: history))
            {
                var message = ConflictedTask(controller); byte[] journal = Journal(); var before = Folders();
                var result = await Execute(controller, message, uncached, false); Assert.IsFalse(result.Success); StringAssert.Contains("Проект изменён между шагами", result.Error);
                Preserved(journal, before);
            }
        }
        [UnityTest] public IEnumerator CacheHitCannotResealOrAbsorbUnrelatedManualEdits() => Await(CheckCacheHit());
        async Task CheckCacheHit()
        {
            WorldMaterialLibrary.ImportBuiltIn("builtin:wood");
            using (var controller = new ChatController(historyFolder: history))
            {
                var message = ConflictedTask(controller); byte[] journal = Journal(); var before = Folders();
                var result = await Execute(controller, message, "builtin:wood", false); Assert.IsTrue(result.Success, result.Error); Assert.IsTrue((bool)JObject.Parse(result.Results.Single().Summary)["CacheHit"]);
                Preserved(journal, before);
            }
        }
        [UnityTest] public IEnumerator PreCallbackCancellationAndImportFailureCannotChangeTheSealedJournal() => Await(CheckCancellationAndEarlyFailure());
        async Task CheckCancellationAndEarlyFailure()
        {
            using (var controller = new ChatController(historyFolder: history))
            using (var cancelled = new CancellationTokenSource())
            {
                var message = ConflictedTask(controller); byte[] journal = Journal(); var before = Folders(); cancelled.Cancel(); bool caught = false;
                try { await Execute(controller, message, "polyhaven:admission_fixture", false, cancelled.Token); } catch (OperationCanceledException) { caught = true; }
                Assert.IsTrue(caught); Preserved(journal, before);
                var failed = await Execute(controller, message, "polyhaven:admission_fixture", false); Assert.IsFalse(failed.Success); StringAssert.Contains("not cached", failed.Error);
                Preserved(journal, before);
            }
        }
        static IEnumerator Await(Task task) { while (!task.IsCompleted) yield return null; if (task.IsFaulted) throw task.Exception.GetBaseException(); if (task.IsCanceled) Assert.Fail("Unexpected cancellation."); }
    }
}
