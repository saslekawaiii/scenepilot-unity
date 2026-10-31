using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace WorldWeaver.Tests
{
    public sealed class WorldGameplayEditorTests
    {
        private object transaction;
        private Type helper,transactionType;
        private int undo;
        [SetUp] public void Setup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var assembly=typeof(WorldWeaver.Editor.Assistant.SceneToolExecutor).Assembly;helper=assembly.GetType("WorldWeaver.Editor.Assistant.WorldGameplayTools",true);transactionType=assembly.GetType("WorldWeaver.Editor.Assistant.SceneBuildTransaction",true);transaction=Activator.CreateInstance(transactionType,true);
            Undo.IncrementCurrentGroup();undo=Undo.GetCurrentGroup();
        }
        private object Call(string name,params object[] args)=>helper.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).Invoke(null,args);
        private GameObject Run(string tool,string name,string target,JObject args)
        {
            var command=new WorldWeaver.Editor.Assistant.EditorCommand {tool=tool,name=name,target=target,args=args};
            Func<string,GameObject> resolve=WorldWeaver.Editor.Assistant.SceneToolExecutor.ResolveTarget;
            var result=Call("Execute",command,transaction,null,resolve,null);
            return (GameObject)result.GetType().GetField("Target",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(result);
        }
        [TearDown] public void Cleanup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Undo.RevertAllDownToGroup(undo);
            var assets=(List<string>)transactionType.GetField("Assets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(transaction);
            foreach(string path in assets.AsEnumerable().Reverse())AssetDatabase.DeleteAsset(path);
            var folder=(string)transactionType.GetField("folder",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(transaction);if(!string.IsNullOrEmpty(folder))AssetDatabase.DeleteAsset(folder);
        }
        [Test] public void VillageHasConnectedEntrancesAndSurvivesSceneReload()
        {
            var world=Run("create_village","Connected Village","",new JObject { ["houses"]=5,["trees"]=0 });
            var nav=world.GetComponent<WorldNavigation>();Assert.NotNull(nav);Assert.IsEmpty(nav.ValidateConnections());Assert.IsTrue(world.GetComponentInChildren<WorldGate>().IsOpen);Assert.NotNull(world.GetComponentInChildren<NpcDailyRoutine>());
            var places=world.GetComponentsInChildren<WorldPlace>();Assert.AreEqual(5,places.Count(p=>p.Kind==WorldPlaceKind.House));Assert.AreEqual(5,places.Count(p=>p.Kind==WorldPlaceKind.Entrance));Assert.NotNull(nav.Find("north_gate"));Assert.NotNull(nav.Find("square"));Assert.NotNull(world.GetComponentInChildren<WorldPlayer>());Assert.NotNull(world.GetComponentInChildren<DeliveryQuest>());
            var guard=nav.Find("guard").GetComponent<SceneBehavior>();Assert.AreEqual(2,guard.NamedWaypoints.Length);Assert.NotNull(guard.GetComponent<UnityEngine.AI.NavMeshAgent>());
            var ids=places.Select(p=>p.PersistentId).OrderBy(p=>p).ToArray();Assert.AreEqual(ids.Length,ids.Distinct().Count());
            string folder=(string)transactionType.GetProperty("Folder",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(transaction);string path=folder+"/GameplayReload.unity";AssetDatabase.SaveAssets();Assert.IsTrue(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),path));EditorSceneManager.OpenScene(path);
            world=Object.FindFirstObjectByType<WorldSaveState>().gameObject;Assert.AreEqual(ids,world.GetComponentsInChildren<WorldPlace>().Select(p=>p.PersistentId).OrderBy(p=>p).ToArray());Assert.IsEmpty(world.GetComponent<WorldNavigation>().ValidateConnections());Assert.IsTrue(world.GetComponentInChildren<WorldGate>().IsOpen);Assert.AreEqual(8,world.GetComponent<WorldClock>().Hour);AssetDatabase.DeleteAsset(path);
        }
        [Test] public void WorldClockToolUpdatesPhysicalGateAndRollsBackAsOneUndoGroup()
        {
            var world=Run("create_village","Clocked Village","",new JObject { ["houses"]=5,["trees"]=0 });var gate=world.GetComponentInChildren<WorldGate>();Assert.IsTrue(gate.IsOpen);Assert.IsFalse(gate.Panel.activeSelf);
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Run("configure_world_time","","id:"+world.GetInstanceID(),new JObject { ["hour"]=22,["day"]=3 });Assert.IsFalse(gate.IsOpen);Assert.IsTrue(gate.Panel.activeSelf);Assert.AreEqual(3,world.GetComponent<WorldClock>().Day);
            Undo.FlushUndoRecordObjects();Undo.RevertAllDownToGroup(group);Assert.AreEqual(8,world.GetComponent<WorldClock>().Hour);Assert.IsTrue(gate.IsOpen);Assert.IsFalse(gate.Panel.activeSelf);
        }
        [Test] public void AnchoredLightRetainsClearanceAfterWaterAndHeightChange()
        {
            var data=new TerrainData { heightmapResolution=33,size=new Vector3(32,20,32) };var terrainObject=Terrain.CreateTerrainGameObject(data);var terrain=terrainObject.GetComponent<Terrain>();var generated=terrainObject.AddComponent<GeneratedTerrain>();generated.Specification=new TerrainSpec { width=32,length=32,height=20,waterLevel=.2f };
            var lamp=new GameObject("Anchored light");lamp.transform.position=new Vector3(16,10,16);var light=lamp.AddComponent<Light>();light.type=LightType.Point;Call("MarkLightAnchor",light,terrain);var anchor=lamp.GetComponent<WorldSurfaceAnchor>();Assert.That(anchor.Offset,Is.EqualTo(6).Within(.001));
            generated.Specification.waterLevel=.5f;anchor.Reanchor();Assert.That(lamp.transform.position.y,Is.EqualTo(16).Within(.001));Assert.That(lamp.transform.position.x,Is.EqualTo(16));Object.DestroyImmediate(lamp);Object.DestroyImmediate(terrainObject);Object.DestroyImmediate(data);
        }
        [Test] public void PrefabScriptsRequireExplicitReviewAndOriginalAssetIsUntouched()
        {
            string folder=(string)transactionType.GetProperty("Folder",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(transaction);var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.AddComponent<SceneBehavior>();var original=go.AddComponent<WorldPlace>();string id=original.PersistentId;string path=folder+"/Reviewed.prefab";PrefabUtility.SaveAsPrefabAsset(go,path);Object.DestroyImmediate(go);
            var command=new WorldWeaver.Editor.Assistant.EditorCommand { tool="instantiate_prefab",name="copy",args=new JObject { ["path"]=path } };Func<string,GameObject> resolve=WorldWeaver.Editor.Assistant.SceneToolExecutor.ResolveTarget;
            var error=Assert.Throws<TargetInvocationException>(()=>Call("Validate",command,resolve));Assert.IsInstanceOf<ArgumentException>(error.InnerException);
            var copy=Run("instantiate_prefab","copy","",new JObject { ["path"]=path,["allow_scripts"]=true });Assert.AreNotEqual(id,copy.GetComponent<WorldPlace>().PersistentId);Assert.AreEqual(id,AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<WorldPlace>().PersistentId);Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(copy));AssetDatabase.DeleteAsset(path);
        }
    }
}
