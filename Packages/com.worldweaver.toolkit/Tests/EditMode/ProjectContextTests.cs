using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class ProjectContextTests
    {
        string folder;
        [SetUp] public void Setup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            folder="Assets/WorldWeaverContextTest_"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        }
        [TearDown] public void Cleanup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);AssetDatabase.DeleteAsset(folder);
        }
        [Test] public void ThousandObjectsRemainAddressableAfterReloadWithoutTouchingOtherScene()
        {
            var root=new GameObject("Деревня");root.AddComponent<GeneratedSceneObject>().Kind="world";
            for(int i=0;i<1000;i++){var go=new GameObject(i==999?"Второй дом у ворот":"Дом "+i);go.transform.SetParent(root.transform);go.AddComponent<GeneratedSceneObject>().Kind="house";}
            var expected=root.transform.GetChild(999).gameObject;string stable=ProjectContextIndex.StableTarget(expected);
            string primary=folder+"/Primary.unity";Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),primary),Is.True);
            var other=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(other);
            var foreign=new GameObject("Второй дом у ворот");Assert.That(EditorSceneManager.SaveScene(other,folder+"/Other.unity"),Is.True);string foreignId=ProjectContextIndex.StableTarget(foreign);
            EditorSceneManager.OpenScene(primary,OpenSceneMode.Single);EditorSceneManager.OpenScene(folder+"/Other.unity",OpenSceneMode.Additive);
            var active=SceneManager.GetSceneByPath(primary);SceneManager.SetActiveScene(active);
            var search=ProjectContextIndex.Search("Второй дом у ворот",5);Assert.That((int)search["sceneMatches"],Is.EqualTo(2));
            Assert.That(((JArray)search["sceneObjects"]).Any(o=>(string)o["target"]==stable),Is.True);
            var reloaded=SceneToolExecutor.ResolveTarget(stable);Assert.That(reloaded.name,Is.EqualTo("Второй дом у ворот"));
            Assert.Throws<ArgumentException>(()=>SceneToolExecutor.ResolveTarget(foreignId));
            var result=SceneToolExecutor.Execute(new EditorPlan{actions={new EditorCommand{tool="set_transform",target=stable,args=new JObject{["position"]=new JArray(3,2,1)}}}});
            Assert.That(result.Success,Is.True,result.Error);Assert.That(reloaded.transform.position,Is.EqualTo(new Vector3(3,2,1)));
            var foreignScene=SceneManager.GetSceneByPath(folder+"/Other.unity");Assert.That(foreignScene.GetRootGameObjects().Single().transform.position,Is.EqualTo(Vector3.zero));
        }
        [Test] public void DesignConstraintSurvivesThirtyMessagesAndHistoryReload()
        {
            string history=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
            try
            {
                var session=new ChatSession();AgentDesignMemory.Remember(session,"Стиль","Сохранять деревянные дома и северные ворота.","initial","constraint");
                for(int i=0;i<30;i++){session.Messages.Add(new AssistantMessage{Role="user",Text="Реплика "+i});AgentDesignMemory.Remember(session,"Решение "+i,"Изменение №"+i,"source"+i);}
                var store=new ChatHistoryStore(history);store.Save(session);var loaded=store.Load().Single();
                var recall=AgentDesignMemory.Recall(loaded,"второй дом у ворот",1000);StringAssert.Contains("Сохранять деревянные дома",recall);StringAssert.Contains("initial",recall);
                Assert.That(recall.Length,Is.LessThanOrEqualTo(1000));Assert.That(loaded.Messages.Count,Is.EqualTo(30));
            }
            finally{if(Directory.Exists(history))Directory.Delete(history,true);}
        }
        [Test] public void SerializedComponentReferenceUsesExplicitComponentAndRejectsWrongType()
        {
            var subject=new GameObject("Subject");var behavior=subject.AddComponent<SceneBehavior>();var destination=new GameObject("Destination");
            var good=new EditorCommand{tool="set_component",target="Subject",args=new JObject{["type"]=typeof(SceneBehavior).FullName,["property"]="FollowTarget",["value"]=new JObject{["target"]="Destination",["component"]="UnityEngine.Transform"}}};
            var result=SceneToolExecutor.Execute(new EditorPlan{actions={good}});Assert.That(result.Success,Is.True,result.Error);Assert.That(behavior.FollowTarget,Is.EqualTo(destination.transform));
            good.args["value"]=new JObject{["target"]="Destination"};Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(new EditorPlan{actions={good}}));Assert.That(behavior.FollowTarget,Is.EqualTo(destination.transform));
        }
    }
}
