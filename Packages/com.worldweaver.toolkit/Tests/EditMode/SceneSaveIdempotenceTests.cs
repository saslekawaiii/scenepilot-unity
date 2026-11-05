using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class SceneSaveIdempotenceTests
    {
        Scene scene,previous;
        string folder,outside;
        readonly HashSet<string> createdAssets=new HashSet<string>();
        [SetUp] public void Open()
        {
            outside=null;previous=SceneManager.GetActiveScene();
            bool untitled=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s=>s.isLoaded&&string.IsNullOrEmpty(s.path));
            if(untitled&&!Application.isBatchMode)Assert.Ignore("Save open scenes before running scene-persistence tests in the GUI.");
            scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,untitled?NewSceneMode.Single:NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            if(!AssetDatabase.IsValidFolder("Assets/WorldWeaverGenerated")){AssetDatabase.CreateFolder("Assets","WorldWeaverGenerated");createdAssets.Add("Assets/WorldWeaverGenerated");}
            folder="Assets/WorldWeaverGenerated/SceneSaveTests-"+Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets/WorldWeaverGenerated",Path.GetFileName(folder));createdAssets.Add(folder);
        }
        [TearDown] public void Close()
        {
            if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
            if(scene.IsValid()&&scene.isLoaded)
            {
                if(previous.IsValid()&&previous.isLoaded)EditorSceneManager.CloseScene(scene,true);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            }
            foreach(string path in createdAssets.OrderByDescending(s=>s.Length))
            {
                if(AssetDatabase.IsValidFolder(path)&&Directory.EnumerateFileSystemEntries(path).Any())
                {if(path==folder)AssetDatabase.DeleteAsset(path);continue;}
                if(AssetDatabase.LoadMainAssetAtPath(path)||File.Exists(path)||AssetDatabase.IsValidFolder(path))AssetDatabase.DeleteAsset(path);
            }
            createdAssets.Clear();
            if(!string.IsNullOrEmpty(outside))AssetDatabase.DeleteAsset(outside);
        }
        static AgentExecutionContext Context()=>new AgentExecutionContext{Message=new AssistantMessage{Task=new AgentTaskState{OriginalRequest="Save the requested scene"}}};
        JObject Save(AgentExecutionContext context,string path=null)
        {
            var args=path==null?new JObject():new JObject{["path"]=path};
            var batch=SceneToolExecutor.Execute(new EditorPlan{actions=new List<EditorCommand>{new EditorCommand{tool="save_scene",args=args}}},executionContext:context);
            Assert.IsTrue(batch.Success,batch.Error);Assert.AreEqual(1,batch.Results.Count);
            var receipt=batch.Results.Single();Assert.AreEqual(false,receipt.ChangesApplied,"Saving is not implementation progress");
            foreach(string asset in receipt.CreatedAssets)createdAssets.Add(asset);
            context.Message.Steps.Add(receipt);var data=JObject.Parse(receipt.Summary);
            Assert.IsTrue((bool)data["saved"]);Assert.IsFalse((bool)data["dirty"]);Assert.IsFalse((bool)data["implementationProgress"]);
            Assert.AreEqual(scene.path,(string)data["actualPath"]);return data;
        }
        void Reload(string path)
        {
            if(previous.IsValid()&&previous.isLoaded)
            {SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);}
            else scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
        }
        void DirtyObject(string name)
        {var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);EditorSceneManager.MarkSceneDirty(scene);}

        [Test] public void RepeatedRequestedPathSurvivesReceiptSerializationAndRoundTripsWithoutCopies()
        {
            string path=folder+"/Scene.unity";var context=Context();DirtyObject("FirstContent");var first=Save(context,path);
            context.Message=JsonConvert.DeserializeObject<AssistantMessage>(JsonConvert.SerializeObject(context.Message));
            DirtyObject("SecondContent");var second=Save(context,path);
            Assert.AreEqual(path,(string)first["actualPath"]);Assert.AreEqual(path,(string)second["actualPath"]);
            Assert.IsTrue((bool)second["reusedCurrentScene"]);Assert.IsFalse((bool)second["newSceneAsset"]);
            Assert.AreEqual(1,Directory.GetFiles(folder,"*.unity").Length);
            Assert.IsEmpty(context.Message.Steps.Last().CreatedAssets,"Reused scene must not enter the delete-on-rollback asset list");
            Reload(path);Assert.IsTrue(scene.GetRootGameObjects().Any(g=>g.name=="SecondContent"));
        }
        [Test] public void ExistingSameNamedFileIsPreservedAndBothObservedAliasesReuseTheActualDestination()
        {
            string requested=folder+"/Existing.unity";DirtyObject("OriginalFile");Assert.IsTrue(EditorSceneManager.SaveScene(scene,requested));byte[] original=File.ReadAllBytes(requested);
            var context=Context();DirtyObject("TaskContent");var first=Save(context,requested);string actual=(string)first["actualPath"];
            Assert.AreNotEqual(requested,actual);CollectionAssert.AreEqual(original,File.ReadAllBytes(requested));
            Save(context,requested);Save(context,actual);
            Assert.AreEqual(actual,scene.path);Assert.AreEqual(2,Directory.GetFiles(folder,"*.unity").Length);
            CollectionAssert.AreEqual(original,File.ReadAllBytes(requested));
            Reload(actual);Assert.IsTrue(scene.GetRootGameObjects().Any(g=>g.name=="TaskContent"));
        }
        [Test] public void AnotherTasksExplicitSaveCannotOverwriteTheCurrentGeneratedFile()
        {
            string requested=folder+"/OwnedByEarlierTask.unity";Save(Context(),requested);byte[] original=File.ReadAllBytes(requested);
            DirtyObject("AnotherTasksContent");var second=Save(Context(),requested);
            Assert.AreNotEqual(requested,(string)second["actualPath"]);Assert.IsFalse((bool)second["reusedCurrentScene"]);
            CollectionAssert.AreEqual(original,File.ReadAllBytes(requested));
        }
        [Test] public void NoArgsSavesTheAlreadyGeneratedCurrentSceneWithoutAnotherCopy()
        {
            string path=folder+"/Current.unity";Save(Context(),path);DirtyObject("NoArgsContent");var result=Save(Context());
            Assert.AreEqual(path,(string)result["actualPath"]);Assert.AreEqual(JTokenType.Null,result["requestedPath"].Type);
            Assert.IsTrue((bool)result["reusedCurrentScene"]);Assert.AreEqual(1,Directory.GetFiles(folder,"*.unity").Length);
            Reload(path);Assert.IsTrue(scene.GetRootGameObjects().Any(g=>g.name=="NoArgsContent"));
        }
        [Test] public void DifferentExplicitDestinationAndInactiveEarlierAliasRemainUniqueSaveAs()
        {
            string a=folder+"/A.unity",b=folder+"/B.unity";var context=Context();Save(context,a);byte[] originalA=File.ReadAllBytes(a);
            DirtyObject("SecondDestination");var second=Save(context,b);byte[] originalB=File.ReadAllBytes(b);
            Assert.AreEqual(b,(string)second["actualPath"]);Assert.IsFalse((bool)second["reusedCurrentScene"]);
            var third=Save(context,a);Assert.AreNotEqual(a,(string)third["actualPath"]);Assert.AreNotEqual(b,(string)third["actualPath"]);
            Assert.AreEqual(3,Directory.GetFiles(folder,"*.unity").Length);CollectionAssert.AreEqual(originalA,File.ReadAllBytes(a));CollectionAssert.AreEqual(originalB,File.ReadAllBytes(b));
        }
        [Test] public void NoArgsCannotOverwriteAnOutsideGeneratedSceneAndExplicitOutsidePathsStayRejected()
        {
            outside="Assets/SceneSaveForeign-"+Guid.NewGuid().ToString("N")+".unity";DirtyObject("OutsideOriginal");Assert.IsTrue(EditorSceneManager.SaveScene(scene,outside));byte[] original=File.ReadAllBytes(outside);
            DirtyObject("NewContent");var saved=Save(Context());Assert.That((string)saved["actualPath"],Does.StartWith("Assets/WorldWeaverGenerated/"));
            Assert.AreNotEqual(outside,(string)saved["actualPath"]);CollectionAssert.AreEqual(original,File.ReadAllBytes(outside));
            var invalid=SceneToolExecutor.Execute(new EditorPlan{actions=new List<EditorCommand>{new EditorCommand{tool="save_scene",args=new JObject{["path"]=outside}}}},executionContext:Context());
            Assert.IsFalse(invalid.Success);CollectionAssert.AreEqual(original,File.ReadAllBytes(outside));
        }
    }
}
