using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public sealed class WorldGameplayEditorTests
    {
        private object transaction;
        private Type helper,transactionType;
        private int undo;
        private readonly List<string> sceneToolAssets=new List<string>();
        [SetUp] public void Setup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var assembly=typeof(WorldWeaver.Editor.Assistant.SceneToolExecutor).Assembly;helper=assembly.GetType("WorldWeaver.Editor.Assistant.WorldGameplayTools",true);transactionType=assembly.GetType("WorldWeaver.Editor.Assistant.SceneBuildTransaction",true);transaction=Activator.CreateInstance(transactionType,true);
            Undo.IncrementCurrentGroup();undo=Undo.GetCurrentGroup();
            sceneToolAssets.Clear();
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
            foreach(string path in sceneToolAssets.AsEnumerable().Reverse())AssetDatabase.DeleteAsset(path);
            foreach(string path in sceneToolAssets.Select(p=>Path.GetDirectoryName(p)?.Replace('\\','/')).Where(p=>p!=null).Distinct())
                if(AssetDatabase.IsValidFolder(path)&&AssetDatabase.FindAssets("",new[]{path}).Length==0)AssetDatabase.DeleteAsset(path);
            sceneToolAssets.Clear();
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
        private static EditorCommand SceneCommand(string tool,string name="",string target="",JObject args=null)=>new EditorCommand{tool=tool,name=name,target=target,args=args??new JObject()};
        private void RunScene(params EditorCommand[] commands)
        {
            var result=SceneToolExecutor.Execute(new EditorPlan{actions=commands.ToList()});
            sceneToolAssets.AddRange(result.Results.SelectMany(r=>r.CreatedAssets));
            Assert.That(result.Success,Is.True,result.Error);
        }
        private static GameObject SceneObject(string name)=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Single(t=>t.name==name).gameObject;
        private static JObject SmallWorld(float height=20,float water=.8f)=>new JObject{["width"]=32,["length"]=32,["height"]=height,["waterLevel"]=water,["resolution"]=33,["trees"]=0,["buildings"]=0,["rocks"]=0};
        private static float SurfaceAt(Terrain terrain,Vector3 position)
        {
            var origin=terrain.transform.position;var size=terrain.terrainData.size;var spec=terrain.GetComponent<GeneratedTerrain>().Specification;
            return origin.y+Mathf.Max(terrain.terrainData.GetInterpolatedHeight((position.x-origin.x)/size.x,(position.z-origin.z)/size.z),spec.waterLevel*spec.height);
        }
        [Test] public void NewLightAnchorsToCoveringWorldAcrossIndependentUpdatesAndReload()
        {
            RunScene(SceneCommand("create_world","Light World A",args:SmallWorld()),SceneCommand("create_world","Light World B",args:SmallWorld()),
                SceneCommand("set_transform",target:"@Light World B",args:new JObject{["position"]=new JArray(100,7,200)}),
                SceneCommand("set_lighting","World B Point",args:new JObject{["type"]="point",["position"]=new JArray(100,-9,200)}),
                SceneCommand("set_lighting","Outside Terrain Point",args:new JObject{["type"]="point",["position"]=new JArray(1000,-9,1000)}),
                SceneCommand("set_lighting","Unanchored Sun",args:new JObject{["type"]="directional",["position"]=new JArray(100,-9,200)}));
            var terrainA=SceneObject("Light World A").GetComponentInChildren<Terrain>();var terrainB=SceneObject("Light World B").GetComponentInChildren<Terrain>();
            var lamp=SceneObject("World B Point");var anchor=lamp.GetComponent<WorldSurfaceAnchor>();
            Assert.That(anchor,Is.Not.Null);Assert.That(anchor.Terrain,Is.SameAs(terrainB));Assert.That(anchor.Terrain,Is.Not.SameAs(terrainA));Assert.That(anchor.Offset,Is.EqualTo(3).Within(.001));
            Assert.That(lamp.transform.position.y,Is.EqualTo(SurfaceAt(terrainB,lamp.transform.position)+3).Within(.001));
            Assert.That(SceneObject("Outside Terrain Point").GetComponent<WorldSurfaceAnchor>(),Is.Null);Assert.That(SceneObject("Outside Terrain Point").transform.position,Is.EqualTo(new Vector3(1000,-9,1000)));
            Assert.That(SceneObject("Unanchored Sun").GetComponent<WorldSurfaceAnchor>(),Is.Null);Assert.That(SceneObject("Unanchored Sun").transform.position,Is.EqualTo(new Vector3(100,-9,200)));
            var before=lamp.transform.position;
            RunScene(SceneCommand("update_world",target:"Light World A",args:new JObject{["height"]=40,["waterLevel"]=.8f}));
            Assert.That(lamp.transform.position,Is.EqualTo(before),"Updating a non-overlapping world must not move this light.");
            RunScene(SceneCommand("update_world",target:"Light World B",args:new JObject{["height"]=40,["waterLevel"]=.8f}));
            Assert.That(lamp.transform.position.y,Is.EqualTo(SurfaceAt(terrainB,lamp.transform.position)+3).Within(.001));Assert.That(lamp.transform.position.y,Is.GreaterThan(before.y));
            Assert.That(lamp.transform.position.x,Is.EqualTo(before.x));Assert.That(lamp.transform.position.z,Is.EqualTo(before.z));
            var finalPosition=lamp.transform.position;string folder=(string)transactionType.GetProperty("Folder",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(transaction);string path=folder+"/TwoWorldLightReload.unity";
            AssetDatabase.SaveAssets();Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),path),Is.True);EditorSceneManager.OpenScene(path);
            lamp=SceneObject("World B Point");anchor=lamp.GetComponent<WorldSurfaceAnchor>();terrainB=SceneObject("Light World B").GetComponentInChildren<Terrain>();
            Assert.That(anchor.Terrain,Is.SameAs(terrainB),"The covering Terrain reference must survive scene serialization.");Assert.That(anchor.Offset,Is.EqualTo(3).Within(.001));Assert.That(lamp.transform.position,Is.EqualTo(finalPosition));
            Assert.That(lamp.transform.position.y,Is.EqualTo(SurfaceAt(terrainB,lamp.transform.position)+3).Within(.001));
        }
        [Test] public void OverlappingTerrainsAnchorLightToTheHighestGroundOrWaterSurface()
        {
            RunScene(SceneCommand("create_world","Lower Light World",args:SmallWorld(20)),SceneCommand("create_world","Upper Light World",args:SmallWorld(40)),
                SceneCommand("set_lighting","Overlapping Point",args:new JObject{["type"]="point",["position"]=new JArray(0,-9,0)}));
            var lower=SceneObject("Lower Light World").GetComponentInChildren<Terrain>();var upper=SceneObject("Upper Light World").GetComponentInChildren<Terrain>();var lamp=SceneObject("Overlapping Point");
            Assert.That(SurfaceAt(upper,lamp.transform.position),Is.GreaterThan(SurfaceAt(lower,lamp.transform.position)));Assert.That(lamp.GetComponent<WorldSurfaceAnchor>().Terrain,Is.SameAs(upper));
            Assert.That(lamp.transform.position.y,Is.EqualTo(SurfaceAt(upper,lamp.transform.position)+3).Within(.001));
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
