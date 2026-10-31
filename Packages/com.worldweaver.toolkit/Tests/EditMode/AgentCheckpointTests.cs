using System;
using System.IO;
using System.Collections;
using UnityEngine.TestTools;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldWeaver.Editor.Assistant;
using Object=UnityEngine.Object;
namespace WorldWeaver.Tests
{
    public sealed class AgentCheckpointTests
    {
        private const string Folder="Assets/WorldWeaverCheckpointTests";
        private string checkpoint;
        [SetUp] public void Setup()
        {
            // Unity Test Framework reruns fixture setup when resuming an EditMode test after domain reload.
            if(TestContext.CurrentContext.Test.Name==nameof(CodeSealIncludesMetaAndSurvivesRealCompileDomainReload)&&!string.IsNullOrEmpty(SessionState.GetString("WorldWeaver.CheckpointCompileTest","")))return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);if(!Directory.Exists(Folder))Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            // Even the comment-only Source.cs fixture is imported by Restore and schedules a compile on deletion.
            bool compiledSource=Directory.Exists(Folder)&&Directory.GetFiles(Folder,"*.cs",SearchOption.AllDirectories).Length>0;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);AssetDatabase.DeleteAsset(Folder);
            if(checkpoint!=null) { string path=Path.Combine("UserSettings/WorldWeaverCheckpoints",checkpoint);if(Directory.Exists(path))Directory.Delete(path,true); }
            if(compiledSource)yield return new RecompileScripts(false);else yield return null;
        }
        private string Begin()=>checkpoint=AgentCheckpoints.Begin(Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"));
        [Test] public void UnsavedDirtySceneAssetsAndSourceRestoreAfterReloadingJournal()
        {
            var original=GameObject.CreatePrimitive(PrimitiveType.Cube);original.name="before";original.transform.position=Vector3.right*3;var ownership=original.AddComponent<GeneratedSceneObject>();string persistent=ownership.ObjectId;
            RenderSettings.fog=true;RenderSettings.fogColor=Color.magenta;EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            string source=Folder+"/Source.cs",meta=source+".meta";File.WriteAllText(source,"// before reviewed source\n");File.WriteAllText(meta,"fileFormatVersion: 2\nguid: "+Guid.NewGuid().ToString("N")+"\n");string metaBefore=File.ReadAllText(meta);
            string id=Begin();Assert.AreEqual("",SceneManager.GetActiveScene().path);Assert.IsTrue(SceneManager.GetActiveScene().isDirty);
            original.transform.position=Vector3.up*10;new GameObject("after");RenderSettings.fog=false;EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());File.WriteAllText(source,"// after reviewed source\n");File.WriteAllText(Folder+"/new.txt","new asset");
            AgentCheckpoints.Seal(id);
            Assert.IsTrue(AgentCheckpoints.CanRestore(id),AgentCheckpoints.Inspect(id).ToString());
            // API reloads the on-disk journal on every operation; no in-memory backup is consulted.
            Assert.IsTrue((bool)AgentCheckpoints.Restore(id)["success"]);
            Assert.AreEqual("",SceneManager.GetActiveScene().path);Assert.IsTrue(SceneManager.GetActiveScene().isDirty);Assert.IsNull(GameObject.Find("after"));Assert.AreEqual(Vector3.right*3,GameObject.Find("before").transform.position);Assert.AreEqual(persistent,GameObject.Find("before").GetComponent<GeneratedSceneObject>().ObjectId);
            Assert.IsTrue(RenderSettings.fog);Assert.AreEqual(Color.magenta,RenderSettings.fogColor);Assert.AreEqual("// before reviewed source\n",File.ReadAllText(source));Assert.AreEqual(metaBefore,File.ReadAllText(meta));Assert.IsFalse(File.Exists(Folder+"/new.txt"));Assert.IsFalse(AgentCheckpoints.CanRestore(id));
        }
        [Test] public void SavedSceneDirtyWorkingStateRestoresWithoutChangingItsDiskBytes()
        {
            var go=new GameObject("persistent");go.transform.position=Vector3.one;string path=Folder+"/Saved.unity";Assert.IsTrue(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),path));byte[] originalDisk=File.ReadAllBytes(path);
            go.transform.position=Vector3.one*2;EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());string id=Begin();
            go.transform.position=Vector3.one*7;EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());AgentCheckpoints.Seal(id);
            Assert.IsTrue((bool)AgentCheckpoints.Restore(id)["success"]);Assert.AreEqual(path,SceneManager.GetActiveScene().path);Assert.IsTrue(SceneManager.GetActiveScene().isDirty);Assert.AreEqual(Vector3.one*2,GameObject.Find("persistent").transform.position);CollectionAssert.AreEqual(originalDisk,File.ReadAllBytes(path));
        }
        [Test] public void ManualFileConflictPreservesEveryCurrentChange()
        {
            string file=Folder+"/asset.txt";File.WriteAllText(file,"before");var go=new GameObject("original");string id=Begin();File.WriteAllText(file,"agent");go.transform.position=Vector3.right;EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());AgentCheckpoints.Seal(id);
            File.WriteAllText(file,"manual");Assert.IsFalse(AgentCheckpoints.CanRestore(id));var result=AgentCheckpoints.Restore(id);Assert.IsFalse((bool)result["success"]);Assert.AreEqual("manual",File.ReadAllText(file));Assert.AreEqual(Vector3.right,go.transform.position);
        }
        [Test] public void ManualInMemorySceneEditIsAConflictAndUnrelatedFileIsPreserved()
        {
            var go=new GameObject("original");string id=Begin();go.transform.position=Vector3.right;EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());AgentCheckpoints.Seal(id);
            go.transform.position=Vector3.up;EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());File.WriteAllText(Folder+"/unrelated.txt","manual unrelated");Assert.IsFalse(AgentCheckpoints.CanRestore(id));Assert.IsFalse((bool)AgentCheckpoints.Restore(id)["success"]);Assert.AreEqual(Vector3.up,go.transform.position);Assert.AreEqual("manual unrelated",File.ReadAllText(Folder+"/unrelated.txt"));
        }
        [UnityTest] public IEnumerator CodeSealIncludesMetaAndSurvivesRealCompileDomainReload()
        {
            const string source=Folder+"/WWCheckpointCompiledProbe.cs",key="WorldWeaver.CheckpointCompileTest";
            new GameObject("before compile");EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());string id=Begin();SessionState.SetString(key,id);
            File.WriteAllText(source,"public static class WWCheckpointCompiledProbe { public static int Value => 9001; }\n");
            AgentCheckpoints.Seal(id,true);Assert.IsTrue(File.Exists(source+".meta"));Assert.IsTrue(((JArray)AgentCheckpoints.Inspect(id)["writeSet"]).Any(p=>(string)p==source+".meta"));
            yield return new RecompileScripts(false);
            checkpoint=id=SessionState.GetString(key,"");var type=Type.GetType("WWCheckpointCompiledProbe, Assembly-CSharp");Assert.NotNull(type);Assert.AreEqual(9001,type.GetProperty("Value").GetValue(null));
            Assert.IsTrue(AgentCheckpoints.CanRestore(id),AgentCheckpoints.Inspect(id).ToString());Assert.IsTrue((bool)AgentCheckpoints.Restore(id)["success"]);
            yield return new RecompileScripts(false);
            checkpoint=SessionState.GetString(key,"");Assert.IsFalse(File.Exists(source));Assert.IsFalse(File.Exists(source+".meta"));Assert.NotNull(GameObject.Find("before compile"));Assert.AreEqual("",SceneManager.GetActiveScene().path);SessionState.EraseString(key);
        }
        [Test] public void MultipleBatchesRestoreOnlyTheirWriteSetAndPreserveLaterUnrelatedFile()
        {
            string first=Folder+"/one.txt",second=Folder+"/two.txt",manual=Folder+"/manual.txt";File.WriteAllText(first,"before");var go=new GameObject("before");string id=Begin();
            File.WriteAllText(first,"batch one");go.transform.position=Vector3.right;EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());AgentCheckpoints.Seal(id);Assert.IsTrue(AgentCheckpoints.CanExtend(id));
            File.WriteAllText(second,"batch two");go.transform.position=Vector3.up;EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());AgentCheckpoints.Seal(id);
            File.WriteAllText(manual,"later unrelated manual file");Assert.IsTrue(AgentCheckpoints.CanRestore(id));Assert.IsFalse(AgentCheckpoints.CanExtend(id),"A next agent batch must not absorb unrelated manual changes.");Assert.IsTrue((bool)AgentCheckpoints.Restore(id)["success"]);
            Assert.AreEqual("before",File.ReadAllText(first));Assert.IsFalse(File.Exists(second));Assert.AreEqual("later unrelated manual file",File.ReadAllText(manual));Assert.AreEqual(Vector3.zero,GameObject.Find("before").transform.position);
        }
        [Test] public void CorruptBackupFailsBeforeChangingSceneOrFiles()
        {
            string file=Folder+"/asset.txt";File.WriteAllText(file,"before");var go=new GameObject("original");string id=Begin();File.WriteAllText(file,"agent");go.transform.position=Vector3.right;EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());AgentCheckpoints.Seal(id);
            string directory=Path.Combine("UserSettings/WorldWeaverCheckpoints",id,"content");foreach(string blob in Directory.GetFiles(directory))File.WriteAllText(blob,"corrupt");
            Assert.Throws<InvalidDataException>(()=>AgentCheckpoints.Restore(id));Assert.AreEqual("agent",File.ReadAllText(file));Assert.AreEqual(Vector3.right,go.transform.position);
        }
    }
}
