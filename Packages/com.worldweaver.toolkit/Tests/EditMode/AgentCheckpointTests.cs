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
            var journal=JObject.Parse(File.ReadAllText(Path.Combine("UserSettings/WorldWeaverCheckpoints",id,"journal.json")));
            string hash=(string)journal["Before"].Single(row=>(string)row["Path"]==file)["Hash"];
            string blob=(string)typeof(AgentCheckpoints).GetMethod("Blob",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{id,hash});byte[] original=File.ReadAllBytes(blob);
            try{File.WriteAllText(blob,"corrupt");Assert.Throws<InvalidDataException>(()=>AgentCheckpoints.Restore(id));Assert.AreEqual("agent",File.ReadAllText(file));Assert.AreEqual(Vector3.right,go.transform.position);}
            finally{File.WriteAllBytes(blob,original);}
        }
        [Test] public void IndependentCheckpointsShareAssetContentAndDeletingOneJournalDoesNotBreakTheOther()
        {
            string immutable=Folder+"/immutable.bytes",mutable=Folder+"/mutable.txt";byte[] data=new byte[1024*1024];new System.Random(19).NextBytes(data);Guid.NewGuid().ToByteArray().CopyTo(data,0);File.WriteAllBytes(immutable,data);File.WriteAllText(mutable,"before");
            string first=Begin(),second=null;
            try
            {
                File.WriteAllText(mutable,"first");AgentCheckpoints.Seal(first);
                second=checkpoint=AgentCheckpoints.Begin(Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"));File.WriteAllText(mutable,"second");AgentCheckpoints.Seal(second);
                string hash;using(var sha=System.Security.Cryptography.SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();
                string root="UserSettings/WorldWeaverCheckpoints",shared=Path.Combine(root,"shared-content",hash);
                CollectionAssert.AreEqual(data,File.ReadAllBytes(shared));Assert.AreEqual(0,new FileInfo(Path.Combine(root,first,"references",hash)).Length);Assert.AreEqual(0,new FileInfo(Path.Combine(root,second,"references",hash)).Length);
                Assert.IsFalse(Directory.Exists(Path.Combine(root,first,"content")),"Unchanged assets must not be copied into each checkpoint");
                Directory.Delete(Path.Combine(root,first),true);AgentCheckpoints.CollectUnreferencedContent();Assert.IsTrue(File.Exists(shared),"The second checkpoint still references these bytes");
                Assert.IsTrue((bool)AgentCheckpoints.Restore(second)["success"]);Assert.AreEqual("first",File.ReadAllText(mutable));CollectionAssert.AreEqual(data,File.ReadAllBytes(immutable));
                Directory.Delete(Path.Combine(root,second),true);second=checkpoint=null;AgentCheckpoints.CollectUnreferencedContent();Assert.IsFalse(File.Exists(shared),"Only content with no remaining references may be removed");
            }
            finally{foreach(string id in new[]{first,second}.Where(id=>id!=null)){string path=Path.Combine("UserSettings/WorldWeaverCheckpoints",id);if(Directory.Exists(path))Directory.Delete(path,true);}}
        }
        [Test] public void LegacyLocalContentLayoutRemainsRestorableAfterSharedContentCollection()
        {
            string file=Folder+"/legacy.txt";File.WriteAllText(file,"legacy original");string id=Begin();File.WriteAllText(file,"new value");AgentCheckpoints.Seal(id);
            string root=Path.Combine("UserSettings/WorldWeaverCheckpoints",id),references=Path.Combine(root,"references"),legacy=Path.Combine(root,"content");Directory.CreateDirectory(legacy);
            foreach(string reference in Directory.GetFiles(references))File.Copy(Path.Combine("UserSettings/WorldWeaverCheckpoints/shared-content",Path.GetFileName(reference)),Path.Combine(legacy,Path.GetFileName(reference)));
            Directory.Delete(references,true);AgentCheckpoints.CollectUnreferencedContent();
            Assert.IsTrue(AgentCheckpoints.CanRestore(id));Assert.IsTrue((bool)AgentCheckpoints.Restore(id)["success"]);Assert.AreEqual("legacy original",File.ReadAllText(file));
        }
        [Test] public void SharedStorageBudgetRejectsOverflowWithoutAllocatingLargeFiles()
        {
            var ensure=typeof(AgentCheckpoints).GetMethod("EnsureSharedCapacity",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);const long limit=2L*1024*1024*1024;
            Assert.DoesNotThrow(()=>ensure.Invoke(null,new object[]{limit-1,1L}));
            foreach(var value in new[]{Tuple.Create(limit,1L),Tuple.Create(long.MaxValue,1L),Tuple.Create(-1L,0L)})
            {var error=Assert.Throws<System.Reflection.TargetInvocationException>(()=>ensure.Invoke(null,new object[]{value.Item1,value.Item2}));Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);}
        }
    }
}
