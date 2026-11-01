using System;
using System.Collections.Generic;
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
    /// <summary>Actual allowlisted editor transaction checks; no provider calls or network access.</summary>
    public sealed class NpcConfigurationToolTests
    {
        Scene previous,scene;
        readonly List<Object> owned=new List<Object>();
        readonly List<int> undoGroups=new List<int>();
        T Keep<T>(T obj)where T:Object {owned.Add(obj);return obj;}
        [SetUp] public void Setup()
        {
            previous=SceneManager.GetActiveScene();bool untitled=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(value=>value.isLoaded&&string.IsNullOrEmpty(value.path));
            if(untitled&&!Application.isBatchMode)Assert.Ignore("Сохраните открытые сцены перед GUI-запуском этих интеграционных проверок.");
            scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,untitled?NewSceneMode.Single:NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        }
        [TearDown] public void Cleanup()
        {
            foreach(int group in undoGroups.OrderByDescending(value=>value))try{Undo.RevertAllDownToGroup(group);}catch(ArgumentException){}
            undoGroups.Clear();
            if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
            if(scene.IsValid()&&scene.isLoaded) {if(previous.IsValid()&&previous.isLoaded)EditorSceneManager.CloseScene(scene,true);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            foreach(var obj in owned)if(obj)Object.DestroyImmediate(obj);owned.Clear();
        }
        OrchestratorProfile Routes(string model)
        {
            var profile=Keep(ScriptableObject.CreateInstance<ModelProfile>());profile.Model=model;
            var routes=Keep(ScriptableObject.CreateInstance<OrchestratorProfile>());routes.Dialogue=routes.Planning=profile;return routes;
        }
        IntelligentNPC Npc(string name,OrchestratorProfile routes=null,string id=null)
        {
            var npc=new GameObject(name).AddComponent<IntelligentNPC>();npc.CharacterId=id??Guid.NewGuid().ToString("N");npc.Models=routes;npc.AiEnabled=false;npc.Autonomous=false;
            npc.Personality="Осторожный и внимательный";npc.Goals="Охранять мост";return npc;
        }
        static EditorCommand Command(string tool,GameObject target,JObject args)=>new EditorCommand {tool=tool,name="",target="id:"+target.GetInstanceID(),args=args};
        EditorBatchResult Run(OrchestratorProfile routes,params EditorCommand[] actions)
        {
            var result=SceneToolExecutor.Execute(new EditorPlan {actions=actions.ToList()},routes);if(result.UndoGroup>=0)undoGroups.Add(result.UndoGroup);return result;
        }
        EditorBatchResult Configure(IntelligentNPC npc,JObject args,OrchestratorProfile routes=null)=>Run(routes,Command("configure_npc_ai",npc.gameObject,args));
        void Revert(EditorBatchResult result)
        {
            Undo.FlushUndoRecordObjects();Undo.RevertAllDownToGroup(result.UndoGroup);undoGroups.RemoveAll(group=>group>=result.UndoGroup);
        }
        [TestCase("role", "Role")]
        [TestCase("personality", "Personality")]
        [TestCase("biography", "Biography")]
        [TestCase("goals", "Goals")]
        [TestCase("constraints", "Constraints")]
        public void AuthoringBoundaryProfileSurvivesActualUpdateCaptureAndRestore(string argument,string field)
        {
            var world=new GameObject("Boundary save world").AddComponent<WorldSaveState>();
            var npc=Npc("Boundary NPC",Routes("local-model"));npc.transform.SetParent(world.transform);
            npc.gameObject.AddComponent<WorldPlace>();npc.gameObject.AddComponent<WorldEntity>();
            npc.Knowledge=Keep(ScriptableObject.CreateInstance<KnowledgeBase>());
            string authored=new string('Ж',4000);
            var updated=Run(null,Command("update_npc",npc.gameObject,new JObject {[argument]=authored}));
            Assert.IsTrue(updated.Success,updated.Error);
            var saved=world.Capture();typeof(IntelligentNPC).GetField(field).SetValue(npc,"Изменено после сохранения");
            Assert.DoesNotThrow(()=>world.Restore(saved));
            Assert.AreEqual(authored,(string)typeof(IntelligentNPC).GetField(field).GetValue(npc));
            typeof(IntelligentNPC).GetField(field).SetValue(npc,new string('Ж',4001));
            Assert.Throws<InvalidOperationException>(()=>world.Capture(),"Capture must reject an unloadable profile before overwriting a valid save file.");
        }
        [Test] public void SaveRoundTripRestoresActualSocialComponentEnabledState()
        {
            var world=new GameObject("Social save world").AddComponent<WorldSaveState>();
            var npc=Npc("Social NPC",Routes("local-model"));npc.transform.SetParent(world.transform);npc.gameObject.AddComponent<WorldPlace>();
            var social=npc.gameObject.AddComponent<NpcSocialAutonomy>();social.Enabled=true;
            var enabled=world.Capture();social.enabled=false;world.Restore(enabled);Assert.IsTrue(social.enabled&&social.Enabled);
            social.enabled=false;var disabled=world.Capture();social.enabled=true;social.Enabled=true;world.Restore(disabled);
            Assert.IsFalse(social.enabled);Assert.IsFalse(social.Enabled);
        }
        [Test] public void EnableDisableSocialTopicAndUndoPreserveRoutesAndPersona()
        {
            var routes=Routes("old-local-model");var npc=Npc("Guard",routes);string persona=npc.Personality,goals=npc.Goals;
            var enabled=Configure(npc,new JObject {["enabled"]=true,["social"]=true,["autonomous"]=true,["topic"]="Нужно ли закрывать мост ночью?",["cooldown_seconds"]=30});
            Assert.IsTrue(enabled.Success,enabled.Error);Undo.FlushUndoRecordObjects();var social=npc.GetComponent<NpcSocialAutonomy>();
            Assert.IsTrue(npc.AiEnabled);Assert.IsTrue(npc.Autonomous);Assert.IsTrue(social&&social.enabled&&social.Enabled);Assert.AreEqual("Нужно ли закрывать мост ночью?",social.Topic);Assert.AreEqual(30,social.CooldownSeconds);Assert.AreEqual(30,npc.PlanningCooldown);Assert.AreSame(routes,npc.Models);
            var disabled=Configure(npc,new JObject {["enabled"]=false,["social"]=false,["autonomous"]=false});
            Assert.IsTrue(disabled.Success,disabled.Error);Assert.IsFalse(npc.AiEnabled);Assert.IsFalse(npc.Autonomous);Assert.IsFalse(social.enabled);Assert.IsFalse(social.Enabled);Assert.AreSame(routes,npc.Models);
            Revert(disabled);Assert.IsTrue(npc.AiEnabled);Assert.IsTrue(npc.Autonomous);Assert.IsTrue(social.enabled&&social.Enabled);Assert.AreSame(routes,npc.Models);
            Revert(enabled);Assert.IsFalse(npc.AiEnabled);Assert.IsFalse(npc.Autonomous);Assert.IsNull(npc.GetComponent<NpcSocialAutonomy>());Assert.AreSame(routes,npc.Models);Assert.AreEqual(persona,npc.Personality);Assert.AreEqual(goals,npc.Goals);
        }
        [Test] public void AssignSelectedModelUsesPassedRouteWithoutMutatingSharedOldProfile()
        {
            var old=Routes("old-local-model");var next=Routes("selected-local-model");var npc=Npc("Guard",old);var sibling=Npc("Merchant",old);
            string oldRoutes=EditorJsonUtility.ToJson(old),oldProfile=EditorJsonUtility.ToJson(old.Dialogue),nextProfile=EditorJsonUtility.ToJson(next.Dialogue);
            var result=Configure(npc,new JObject {["enabled"]=true,["assign_selected_model"]=true},next);
            Assert.IsTrue(result.Success,result.Error);Assert.AreSame(next,npc.Models);Assert.AreEqual("selected-local-model",npc.Models.Dialogue.Model);Assert.AreSame(old,sibling.Models);
            Assert.AreEqual(oldRoutes,EditorJsonUtility.ToJson(old));Assert.AreEqual(oldProfile,EditorJsonUtility.ToJson(old.Dialogue));Assert.AreEqual(nextProfile,EditorJsonUtility.ToJson(next.Dialogue));
            Revert(result);Assert.AreSame(old,npc.Models);Assert.AreSame(old,sibling.Models);Assert.IsFalse(npc.AiEnabled);
        }
        [Test] public void DuplicateCharacterIdRejectsWholeBatchBeforeAnyMutation()
        {
            var routes=Routes("local-model");var npc=Npc("Guard",routes,"duplicate");Npc("Merchant",routes,"duplicate");var consumer=new GameObject("Unrelated consumer");consumer.transform.position=new Vector3(3,4,5);
            int count=scene.rootCount;
            var result=Run(null,Command("set_transform",consumer,new JObject {["position"]=new JArray(9,9,9)}),Command("configure_npc_ai",npc.gameObject,new JObject {["enabled"]=true,["social"]=true}));
            Assert.IsFalse(result.Success);StringAssert.Contains("unique",result.Error);Assert.AreEqual(new Vector3(3,4,5),consumer.transform.position);Assert.AreEqual(count,scene.rootCount);Assert.IsFalse(npc.AiEnabled);Assert.IsNull(npc.GetComponent<NpcSocialAutonomy>());Assert.AreSame(routes,npc.Models);
        }
        [Test] public void ExplicitUnownedNpcCanBeConfiguredButEnableNeedsExistingOrExplicitSelectedRoute()
        {
            var npc=Npc("Authored foreign NPC");Assert.IsNull(npc.GetComponent<GeneratedSceneObject>());
            var missing=Configure(npc,new JObject {["enabled"]=true,["social"]=true});Assert.IsFalse(missing.Success);Assert.IsFalse(npc.AiEnabled);Assert.IsNull(npc.Models);Assert.IsNull(npc.GetComponent<NpcSocialAutonomy>());
            var selected=Routes("selected-local-model");var configured=Configure(npc,new JObject {["enabled"]=true,["social"]=true,["assign_selected_model"]=true},selected);
            Assert.IsTrue(configured.Success,configured.Error);Assert.IsTrue(npc.AiEnabled);Assert.AreSame(selected,npc.Models);Assert.IsTrue(npc.GetComponent<NpcSocialAutonomy>().Enabled);Assert.IsNull(npc.GetComponent<GeneratedSceneObject>(),"Explicit configuration must not silently take ownership of authored NPCs.");
            var disabled=Configure(npc,new JObject {["enabled"]=false,["social"]=false});Assert.IsTrue(disabled.Success,disabled.Error);Assert.AreSame(selected,npc.Models);
        }
    }
}
