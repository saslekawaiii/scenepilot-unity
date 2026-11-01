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
using UnityEngine.UI;

namespace WorldWeaver.Editor.Assistant.Tests
{
    public sealed class ConstructiveAgentTests
    {
        Scene scene;
        readonly List<string> assets=new List<string>();
        readonly List<int> groups=new List<int>();
        [SetUp] public void SetUp(){scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        [TearDown] public void TearDown()
        {
            foreach(int group in groups.AsEnumerable().Reverse())try{Undo.RevertAllDownToGroup(group);}catch(ArgumentException){}
            groups.Clear();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            foreach(string file in assets.Distinct().Reverse())if(AssetDatabase.LoadMainAssetAtPath(file))AssetDatabase.DeleteAsset(file);assets.Clear();
        }
        EditorBatchResult Run(string tool,string name="",string target="",JObject args=null)
        {
            var result=SceneToolExecutor.Execute(new EditorPlan{actions=new List<EditorCommand>{new EditorCommand{tool=tool,name=name,target=target,args=args??new JObject()}}});
            if(result.UndoGroup>=0)groups.Add(result.UndoGroup);foreach(var step in result.Results)assets.AddRange(step.CreatedAssets);
            return result;
        }
        GameObject Named(string name)=>scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Single(t=>t.name==name).gameObject;
        GameObject World()
        {var r=Run("create_world","World",args:new JObject{["width"]=160,["length"]=160,["height"]=32,["resolution"]=129,["trees"]=0,["rocks"]=0,["buildings"]=0,["waterLevel"]=0});Assert.That(r.Success,Is.True,r.Error);return Named("World");}
        [TestCase("карта высот с кратерами и лунными жителями","shape_terrain")]
        [TestCase("мост из собственного меша","create_mesh")]
        [TestCase("меню с кнопкой","create_ui_element")]
        [TestCase("падающие предметы с отскоком","configure_physics")]
        [TestCase("кислород и выживание","configure_survival")]
        public void CapabilitySelectionCoversDifferentDomains(string query,string tool)=>Assert.That((IEnumerable<string>)typeof(ChatController).Assembly.GetType("WorldWeaver.Editor.Assistant.AgentCapabilities").GetMethod("Match",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{query}),Does.Contain(tool));
        [Test] public void DiscoveryReturnsRealSchemaAndNeverMutatesScene()
        {
            int before=scene.rootCount;var r=Run("discover_tools",args:new JObject{["names"]=new JArray("create_mesh","not_a_tool")});
            Assert.That(r.Success,Is.True,r.Error);var report=JObject.Parse(r.Results.Single().Summary);Assert.That(report["enabledTools"].Values<string>(),Does.Contain("create_mesh"));Assert.That(report["unknownNames"].Values<string>(),Does.Contain("not_a_tool"));Assert.That(scene.rootCount,Is.EqualTo(before));Assert.That(r.UndoGroup,Is.EqualTo(-1));
        }
        [Test] public void SkillsDeclareOnlyRealRegisteredCommands()
        {
            var catalog=AgentSkillCatalog.CreateDefault();var known=((JArray)EditorToolSchema.CommandSchema()["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName).ToHashSet();
            Assert.That(catalog.List().Count,Is.EqualTo(80));foreach(var skill in catalog.List())foreach(string tool in catalog.ToolsFor(skill.Id))Assert.That(known.Contains(tool),Is.True,skill.Id+": "+tool);
            Assert.That(catalog.ToolsFor("ww-mesh-authoring"),Does.Contain("create_mesh"));
        }
        [Test] public void LocalMeshGrammarAvoidsUnsupportedHugeRepetitionsWithoutWeakeningHostBounds()
        {
            var host=EditorPlan.Schema(true,new[]{"create_mesh"});var before=host.DeepClone();var decoder=OllamaDecoderSchema.FromHost(host);
            var args=(JObject)decoder["properties"]["actions"]["items"]["anyOf"][0]["properties"]["args"]["properties"];
            Assert.That(args["vertices"]["maxItems"],Is.Null);Assert.That(args["triangles"]["maxItems"],Is.Null);
            Assert.That((int)args["vertices"]["items"]["minItems"],Is.EqualTo(3));Assert.That((int)args["vertices"]["items"]["maxItems"],Is.EqualTo(3));Assert.That(JToken.DeepEquals(host,before),Is.True);
            Assert.That((int)host["properties"]["actions"]["items"]["anyOf"][0]["properties"]["args"]["properties"]["triangles"]["maxItems"],Is.EqualTo(12000));
        }
        [Test] public void IncompleteActionNamesHavePreciseRepairDiagnostics()
        {
            var error=Assert.Throws<ArgumentException>(()=>EditorPlan.Parse("{\"reply\":\"x\",\"complete\":false,\"actions\":[{\"tool\":\"inspect_project\",\"target\":\"\",\"args\":{}}]}"));StringAssert.Contains("actions[0].name",error.Message);
        }
        [Test] public void LocalGrammarTraversalHandlesAPropertyNamedType()
        {
            Assert.DoesNotThrow(()=>OllamaDecoderSchema.FromHost(EditorPlan.Schema(true,new[]{"inspect_component","create_mesh"})));
        }
        [Test] public void MeshPersistsAndCollisionMatchesAndBadIndicesCannotPartiallyMutate()
        {
            var args=new JObject{["vertices"]=new JArray(new JArray(-2,0,-2),new JArray(-2,0,2),new JArray(2,0,2),new JArray(2,0,-2)),["triangles"]=new JArray(0,1,2,0,2,3)};
            var r=Run("create_mesh","Bridge",args:args);Assert.That(r.Success,Is.True,r.Error);var go=Named("Bridge");var mesh=go.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(AssetDatabase.Contains(mesh),Is.True);Assert.That(go.GetComponent<MeshCollider>().sharedMesh,Is.SameAs(mesh));
            args["triangles"]=new JArray(0,1,99);int before=scene.rootCount;var bad=Run("create_mesh","Bad",args:args);Assert.That(bad.Success,Is.False);Assert.That(scene.rootCount,Is.EqualTo(before));
        }
        [Test] public void HierarchyPreservesWorldTransformAndRejectsCycles()
        {
            Assert.That(Run("create_node","Parent").Success,Is.True);Assert.That(Run("create_node","Child",args:new JObject{["position"]=new JArray(2,3,4)}).Success,Is.True);
            Assert.That(Run("set_parent",target:"Child",args:new JObject{["parent"]="Parent"}).Success,Is.True);Assert.That(Named("Child").transform.position,Is.EqualTo(new Vector3(2,3,4)));
            Assert.That(Run("set_parent",target:"Parent",args:new JObject{["parent"]="Child"}).Success,Is.False);
        }
        [Test] public void TerrainBrushChangesActualHeightsAndPreservesOriginalSharedAsset()
        {
            var terrain=World().GetComponentInChildren<Terrain>();var original=terrain.terrainData;float before=terrain.SampleHeight(Vector3.zero);
            var r=Run("shape_terrain",target:"World",args:new JObject{["operation"]="crater",["center"]=new JArray(0,0),["radius"]=12,["amount"]=3});Assert.That(r.Success,Is.True,r.Error);
            Assert.That(terrain.SampleHeight(Vector3.zero),Is.LessThan(before-2.5f));Assert.That(terrain.terrainData,Is.Not.SameAs(original));Assert.That(original.GetInterpolatedHeight(.5f,.5f),Is.EqualTo(before).Within(.01f));
        }
        [Test] public void ProceduralMoonIsSeededAndHasCraterBowlAndRim()
        {
            var spec=new TerrainSpec{width=160,length=160,height=32,resolution=129};var craters=ExplorationTerrain.Craters(42,8,160,160);
            var a=ExplorationTerrain.Generate(spec,craters,true);var b=ExplorationTerrain.Generate(spec,craters,true);Assert.That(a,Is.EqualTo(b));Assert.That(craters.Count,Is.EqualTo(8));
            var c=craters[0];int X(float v)=>Mathf.RoundToInt((v+80)/160*128);Assert.That(a[X(c.Center.y),X(c.Center.x)],Is.LessThan(a[X(c.Center.y),X(c.Center.x+c.Radius)]-.015f));
        }
        [Test] public void CaveHasOpenTerrainFootprintAndPhysicalRampAndRoof()
        {
            var terrain=World().GetComponentInChildren<Terrain>();var r=Run("create_cave","Cave","World",new JObject{["position"]=new JArray(0,0),["width"]=8,["length"]=24});Assert.That(r.Success,Is.True,r.Error);
            var cave=Named("Cave");var marker=cave.GetComponent<WorldCave>();Assert.That(cave.GetComponent<MeshCollider>().sharedMesh.vertexCount,Is.GreaterThan(100));
            int n=terrain.terrainData.holesResolution;var holes=terrain.terrainData.GetHoles(0,0,n,n);Assert.That(holes.Cast<bool>().Count(h=>!h),Is.GreaterThan(20));
            Physics.SyncTransforms();Assert.That(Physics.Raycast(marker.Interior+Vector3.up,Vector3.down,out var floor,3),Is.True);Assert.That(floor.collider,Is.SameAs(cave.GetComponent<MeshCollider>()));
            Assert.That(Physics.Raycast(marker.Interior+Vector3.up,Vector3.up,out var roof,6),Is.True);Assert.That(roof.collider,Is.SameAs(cave.GetComponent<MeshCollider>()));
        }
        [Test] public void PhysicsSetupUsesOwnedMaterialAndNoDuplicateColliders()
        {
            Run("create_object","Ball",args:new JObject{["kind"]="sphere",["on_terrain"]=false});var r=Run("configure_physics",target:"Ball",args:new JObject{["mass"]=2,["bounce"]=.8,["friction"]=.2});Assert.That(r.Success,Is.True,r.Error);
            var ball=Named("Ball");Assert.That(ball.GetComponents<Collider>().Length,Is.EqualTo(1));Assert.That(ball.GetComponent<Rigidbody>().mass,Is.EqualTo(2));Assert.That(AssetDatabase.Contains(ball.GetComponent<Collider>().sharedMaterial),Is.True);
        }
        [Test] public void CanvasButtonHasPersistentRealToggleAction()
        {
            Run("create_node","Target");var canvas=Run("create_ui_element","Canvas",args:new JObject{["kind"]="canvas"});Assert.That(canvas.Success,Is.True,canvas.Error);
            var result=Run("create_ui_element","Toggle","Canvas",new JObject{["kind"]="button",["text"]="Toggle",["toggle_target"]="Target"});Assert.That(result.Success,Is.True,result.Error);
            var button=Named("Toggle").GetComponent<Button>();Assert.That(button.onClick.GetPersistentEventCount(),Is.EqualTo(1));Assert.That(Named("Canvas").GetComponent<CanvasScaler>().referenceResolution,Is.EqualTo(new Vector2(1440,900)));
            Named("Toggle").GetComponent<WorldUIButton>().Toggle();Assert.That(Named("Target").activeSelf,Is.False);
        }
        [Test] public void FailedIndependentAssertionReturnsFailureWithoutChangingScene()
        {
            Run("create_node","Empty");int before=scene.rootCount;var r=Run("verify_scene",args:new JObject{["checks"]=new JArray(new JObject{["target"]="Empty",["kind"]="component",["type"]="Rigidbody"})});Assert.That(r.Success,Is.False);StringAssert.Contains("assertions failed",r.Error);Assert.That(scene.rootCount,Is.EqualTo(before));
        }
        [Test] public void RefusalWithNoChangesCannotPassMutationRequirement()
        {
            var message=new AssistantMessage{Task=AgentTaskVerifier.Create("Добавь произвольный механизм",true)};typeof(ChatController).GetMethod("VerifyTask",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{message});Assert.That(message.Task.Checks.Any(c=>!c.Passed),Is.True);
        }
        [Test] public void SurvivalRulesCoverVacuumShelterSuppliesDeathAndWin()
        {
            var state=new SurvivalState();state.Tick(90,false,false);Assert.That(state.Oxygen,Is.EqualTo(50).Within(.01f));float oxygen=state.Oxygen;state.Tick(10,false,true);Assert.That(state.Oxygen,Is.LessThan(oxygen));Assert.That(state.Exposure,Is.Zero);
            state.Tick(10,true,true);Assert.That(state.Oxygen,Is.EqualTo(100));state.Tick(200,false,false);state.Tick(10,false,false);Assert.That(state.Alive,Is.False);
            var win=new SurvivalState{Samples=3};win.Tick(1,true,true);Assert.That(win.Completed,Is.True);
        }
    }
}
