using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public sealed class AgentTestSceneScopeTests
    {
        const string Folder="Assets/WorldWeaverTestSceneScopeTests";
        string scope,checkpoint;
        static object Call(string method,params object[] args)=>typeof(AgentCheckpoints).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
        [SetUp] public void Setup(){EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Directory.CreateDirectory(Folder);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);}
        [TearDown] public void Cleanup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);AssetDatabase.DeleteAsset(Folder);
            foreach(string path in Directory.GetFiles("Assets","__WorldWeaverCheckpoint-test-*.unity"))AssetDatabase.DeleteAsset(path);
            foreach(string id in new[]{scope,checkpoint}.Where(s=>!string.IsNullOrEmpty(s))){string path="UserSettings/WorldWeaverCheckpoints/"+id;if(Directory.Exists(path))Directory.Delete(path,true);}
        }
        string Capture(){scope=(string)Call("BeginTestSceneScope");return SceneManager.GetActiveScene().path;}
        [UnityTest] public IEnumerator DirtySavedWorkingCopyAndDiskBytesSurviveStagingAndReload()
        {
            var go=new GameObject("Original");go.transform.position=Vector3.right;string path=Folder+"/Original.unity";Assert.IsTrue(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),path));byte[] bytes=File.ReadAllBytes(path);
            yield return null;
            go.transform.position=Vector3.up*5;EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());checkpoint=AgentCheckpoints.Begin(Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"));AgentCheckpoints.Seal(checkpoint);
            string staged=Capture();Assert.AreNotEqual(path,staged);Assert.IsFalse(SceneManager.GetActiveScene().isDirty);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(path));
            // Reproduce the runner replacing the loaded scene, then restoring its staged setup.
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);EditorSceneManager.OpenScene(staged);yield return null;Call("EndTestSceneScope",scope);yield return null;
            Assert.AreEqual(path,SceneManager.GetActiveScene().path);Assert.IsTrue(SceneManager.GetActiveScene().isDirty);Assert.AreEqual(Vector3.up*5,GameObject.Find("Original").transform.position);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(path));Assert.IsTrue(AgentCheckpoints.CanRestore(checkpoint),AgentCheckpoints.Inspect(checkpoint).ToString());Assert.IsFalse(File.Exists(staged));
        }
        [UnityTest] public IEnumerator LaterManualSceneEditIsPreservedAndOriginalSnapshotRetained()
        {
            var go=new GameObject("Original");go.transform.position=Vector3.right;yield return null;string staged=Capture();go.transform.position=Vector3.up*8;EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            yield return null;
            var error=Assert.Throws<TargetInvocationException>(()=>Call("EndTestSceneScope",scope));Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);StringAssert.Contains("Working scenes changed",error.InnerException.Message);Assert.AreEqual(Vector3.up*8,go.transform.position);Assert.AreEqual(staged,SceneManager.GetActiveScene().path);Assert.IsTrue(File.Exists("UserSettings/WorldWeaverCheckpoints/"+scope+"/test-scenes.json"));
        }
        [UnityTest] public IEnumerator LaterOriginalSceneAssetEditIsNeverOverwritten()
        {
            new GameObject("Original");string path=Folder+"/Original.unity";EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),path);yield return null;Capture();File.AppendAllText(path,"\n# later manual source change\n");byte[] manual=File.ReadAllBytes(path);yield return null;
            var error=Assert.Throws<TargetInvocationException>(()=>Call("EndTestSceneScope",scope));Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);StringAssert.Contains("original scene asset",error.InnerException.Message);CollectionAssert.AreEqual(manual,File.ReadAllBytes(path));
        }
    }
}
