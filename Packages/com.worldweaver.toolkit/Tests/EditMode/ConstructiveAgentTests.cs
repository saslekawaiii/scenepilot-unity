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
        [TestCase(true)] [TestCase(false)]
        public void HabitatAndCaveKeepTheirApproachesClearInEitherCreationOrder(bool caveFirst)
        {
            World();var player=Run("create_player","SitePlayer","@World",new JObject{["position"]=new JArray(0,0,0)});Assert.IsTrue(player.Success,player.Error);var pawn=SceneToolExecutor.ResolveTarget(player.Results.Single(r=>r.Tool=="create_player").Target);
            if(caveFirst){var cave=Run("create_cave","SiteCave","@World",new JObject{["position"]=new JArray(0,0),["width"]=6,["length"]=20});Assert.IsTrue(cave.Success,cave.Error);}
            var configured=Run("configure_survival",target:ProjectContextIndex.StableTarget(pawn),args:new JObject{["world"]="@World"});Assert.IsTrue(configured.Success,configured.Error);
            var survival=pawn.GetComponent<WorldSurvival>();
            if(!caveFirst)
            {
                var terrain=Named("World").GetComponentInChildren<Terrain>();var before=terrain.terrainData;
                var blocked=Run("create_cave","Blocked","@World",new JObject{["position"]=new JArray(0,0),["width"]=6,["length"]=20});Assert.IsFalse(blocked.Success);Assert.That(blocked.Error,Does.Contain("occupied"));Assert.AreSame(before,terrain.terrainData);
                var clear=Run("create_cave","SiteCave","@World",new JObject{["position"]=new JArray(24,24),["width"]=6,["length"]=20});Assert.IsTrue(clear.Success,clear.Error);
            }
            var actual=Named("SiteCave").GetComponent<WorldCave>();var position=survival.Habitat.position;
            Assert.IsFalse(new Rect(position.x-5,position.z-7,10,12).Overlaps(new Rect(actual.transform.position.x-actual.Size.x/2,actual.Entrance.z,actual.Size.x,actual.Size.z+1)));
            Assert.AreEqual(survival.Habitat.position+Vector3.up*.1f,survival.Spawn);
        }
        [Test] public void GroundedNpcPlacementKeepsCaveAccessClearAndDoesNotStackActors()
        {
            World();var created=Run("create_cave","ActorCave","@World",new JObject{["position"]=new JArray(0,0),["width"]=7,["length"]=24});Assert.IsTrue(created.Success,created.Error);
            foreach(string name in new[]{"GroundedA","GroundedB"}){var result=Run("create_npc",name,"@World",new JObject{["position"]=new JArray(0,0,0)});Assert.IsTrue(result.Success,result.Error);}
            var a=Named("GroundedA");var b=Named("GroundedB");Assert.Greater(Vector3.Distance(a.transform.position,b.transform.position),.7f);
            foreach(var npc in new[]{a,b})Assert.IsFalse(new Rect(-3.5f,-1,7,25).Contains(new Vector2(npc.transform.position.x,npc.transform.position.z)),"The default grounded route must not spawn on the cave cut/approach.");
            var playerResult=Run("create_player","SpawnProbe","@World",new JObject{["position"]=new JArray(a.transform.position.x,a.transform.position.y,a.transform.position.z)});Assert.IsTrue(playerResult.Success,playerResult.Error);
            var pawn=SceneToolExecutor.ResolveTarget(playerResult.Results.Single(r=>r.Tool=="create_player").Target);
            var survival=Run("configure_survival",target:ProjectContextIndex.StableTarget(pawn),args:new JObject{["world"]="@World"});Assert.IsTrue(survival.Success,survival.Error);Assert.Greater(Vector3.Distance(pawn.transform.position,a.transform.position),1,"The player's recovery spawn must not overlap an existing NPC.");
            var exact=Run("create_npc","ExactActor","@World",new JObject{["position"]=new JArray(0,50,0),["on_terrain"]=false});Assert.IsTrue(exact.Success,exact.Error);Assert.AreEqual(new Vector3(0,50,0),Named("ExactActor").transform.position);Assert.IsFalse(Named("ExactActor").GetComponent<GeneratedSceneObject>().SurfaceAnchored);
        }
        [Test] public void DiscoveryReturnsRealSchemaAndNeverMutatesScene()
        {
            int before=scene.rootCount;var r=Run("discover_tools",args:new JObject{["names"]=new JArray("create_mesh","not_a_tool")});
            Assert.That(r.Success,Is.True,r.Error);var report=JObject.Parse(r.Results.Single().Summary);Assert.That(report["enabledTools"].Values<string>(),Does.Contain("create_mesh"));Assert.That(report["unknownNames"].Values<string>(),Does.Contain("not_a_tool"));Assert.That(scene.rootCount,Is.EqualTo(before));Assert.That(r.UndoGroup,Is.EqualTo(-1));
        }
        [Test] public void SkillsDeclareOnlyRealRegisteredCommands()
        {
            var catalog=AgentSkillCatalog.CreateDefault();var known=((JArray)EditorToolSchema.CommandSchema()["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName).ToHashSet();
            Assert.That(catalog.List().Count,Is.EqualTo(114));foreach(var skill in catalog.List())foreach(string tool in catalog.ToolsFor(skill.Id))Assert.That(known.Contains(tool),Is.True,skill.Id+": "+tool);
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
        [Test] public void ExplicitEmptyHostCanBePlannedBeforeNewCodeWithoutExecutingRejectedActions()
        {
            var plan=new EditorPlan{actions=new List<EditorCommand>{new EditorCommand{tool="create_object",name="HiddenPrimitive",args=new JObject{["kind"]="cube"}},new EditorCommand{tool="draft_script",name="Timer",args=new JObject{["code"]="using UnityEngine; public class Timer : MonoBehaviour {}"}}}};
            string original=JObject.FromObject(plan).ToString();var request=new CompletionRequest{Schema=EditorPlan.Schema(true,new[]{"create_node","create_object","draft_script"})};
            var method=typeof(ChatController).Assembly.GetType("WorldWeaver.Editor.Assistant.AgentImplementationPolicy").GetMethod("BindSceneBeforeCode",BindingFlags.Static|BindingFlags.NonPublic);var args=new object[]{request,plan,false,"Создай пустой узел и подготовь компонент таймера",null};
            Assert.That((bool)method.Invoke(null,args),Is.True);Assert.That(((JArray)request.Schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName),Is.EquivalentTo(new[]{"create_node"}));Assert.That(JObject.FromObject(plan).ToString(),Is.EqualTo(original));Assert.That(scene.rootCount,Is.Zero);
        }
        [Test] public void ReadbackSupportsMeshesWhoseCpuDataWasReleased()
        {
            Run("create_node","Imported");var go=Named("Imported");var mesh=new Mesh{vertices=new[]{Vector3.zero,Vector3.right,Vector3.forward},triangles=new[]{0,1,2}};go.AddComponent<MeshFilter>().sharedMesh=mesh;mesh.UploadMeshData(true);
            try
            {
                var method=typeof(ChatController).Assembly.GetType("WorldWeaver.Editor.Assistant.ToolStateReadback").GetMethod("Add",BindingFlags.Static|BindingFlags.NonPublic);var report=new JObject();Assert.DoesNotThrow(()=>method.Invoke(null,new object[]{report,go}));Assert.That((int)report["mesh"]["indexCount"],Is.EqualTo(3));
            }
            finally{UnityEngine.Object.DestroyImmediate(mesh);}
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
        [Test] public void UnnamedCreationsReceiveUniqueDefaultsWithoutRewritingAuditedCommands()
        {
            var command=new EditorCommand{tool="create_node",name="",target="",args=new JObject()};
            var first=SceneToolExecutor.Execute(new EditorPlan{actions=new List<EditorCommand>{command}});if(first.UndoGroup>=0)groups.Add(first.UndoGroup);
            Assert.That(first.Success,Is.True,first.Error);Assert.That(command.name,Is.Empty);
            var second=Run("create_node");Assert.That(second.Success,Is.True,second.Error);Assert.That(first.Results.Single().Target,Is.Not.EqualTo(second.Results.Single().Target));
            Assert.That(Named("Узел"),Is.Not.Null);Assert.That(Named("Узел 2"),Is.Not.Null);
        }
        [Test] public void LegacyCreationToolsAlsoResolveEmptyNames()
        {
            var result=Run("create_world",args:new JObject{["resolution"]=33,["trees"]=0,["rocks"]=0,["buildings"]=0});Assert.That(result.Success,Is.True,result.Error);Assert.That(Named("Мир").GetComponentInChildren<Terrain>(),Is.Not.Null);
            var sphere=Run("create_object",args:new JObject{["kind"]="sphere",["position"]=new JArray(0,5,0),["on_terrain"]=false});Assert.That(sphere.Success,Is.True,sphere.Error);Assert.That(Named("Сфера").transform.position.y,Is.EqualTo(5));
        }
        [Test] public void HeightOfAnObjectDoesNotOfferWorldCreation()
        {
            var method=typeof(ChatController).GetMethod("SelectTools",BindingFlags.Static|BindingFlags.NonPublic);
            var tools=(IEnumerable<string>)method.Invoke(null,new object[]{"Создай красный мяч на высоте 5 метров с физикой и отскоком",AssistantMode.Execute});
            Assert.That(tools,Does.Contain("configure_physics"));Assert.That(tools,Does.Not.Contain("create_world"));Assert.That(tools,Does.Not.Contain("shape_terrain"));
        }
        [TestCase(3)] [TestCase(-3)] public void TerrainBrushChangesActualHeightsAndPreservesOriginalSharedAsset(float amount)
        {
            var terrain=World().GetComponentInChildren<Terrain>();var original=terrain.terrainData;float before=terrain.SampleHeight(Vector3.zero);
            var r=Run("shape_terrain",target:"World",args:new JObject{["operation"]="crater",["center"]=new JArray(0,0),["radius"]=12,["amount"]=amount});Assert.That(r.Success,Is.True,r.Error);
            Assert.That(terrain.SampleHeight(Vector3.zero),Is.LessThan(before-2.5f));Assert.That(terrain.terrainData,Is.Not.SameAs(original));Assert.That(original.GetInterpolatedHeight(.5f,.5f),Is.EqualTo(before).Within(.01f));
        }
        [Test] public void ResolutionRefinementPreservesExistingCraterHeightsAndHoles()
        {
            var terrain=World().GetComponentInChildren<Terrain>();Run("shape_terrain",target:"World",args:new JObject{["operation"]="crater",["radius"]=12,["amount"]=-3});float before=terrain.SampleHeight(Vector3.zero);
            int hn=terrain.terrainData.holesResolution;var holes=terrain.terrainData.GetHoles(0,0,hn,hn);holes[5,5]=false;terrain.terrainData.SetHoles(0,0,holes);
            var result=Run("shape_terrain",target:"World",args:new JObject{["operation"]="refine",["resolution"]=513});Assert.That(result.Success,Is.True,result.Error);
            Assert.That(terrain.SampleHeight(Vector3.zero),Is.EqualTo(before).Within(.01f));Assert.That(terrain.terrainData.heightmapResolution,Is.EqualTo(513));Assert.That(terrain.terrainData.GetHoles(22,22,1,1)[0,0],Is.False);Assert.That(terrain.GetComponent<GeneratedTerrain>().Specification.resolution,Is.EqualTo(513));
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
        [Test] public void ExistingButtonCanBeBoundAndConfiguredWithoutDraftingCode()
        {
            Run("create_ui_element","Canvas",args:new JObject{["kind"]="canvas"});Run("create_ui_element","Panel","Canvas",new JObject{["kind"]="panel"});Run("create_ui_element","Button","Canvas",new JObject{["kind"]="button",["text"]="Toggle"});
            var result=Run("configure_ui",target:"Button",args:new JObject{["toggle_target"]="Panel",["position"]=new JArray(0,-120)});Assert.That(result.Success,Is.True,result.Error);
            var button=Named("Button");Assert.That(button.GetComponent<WorldUIButton>().Target,Is.SameAs(Named("Panel")));Assert.That(button.GetComponent<Button>().onClick.GetPersistentEventCount(),Is.EqualTo(1));
            Run("configure_ui",target:"Button",args:new JObject{["toggle_target"]="Panel"});Assert.That(button.GetComponent<Button>().onClick.GetPersistentEventCount(),Is.EqualTo(1));
            var panelSize=Named("Panel").GetComponent<RectTransform>().sizeDelta;var buttonSize=button.GetComponent<RectTransform>().sizeDelta;Assert.That(panelSize.x*panelSize.y,Is.GreaterThan(2*buttonSize.x*buttonSize.y));
            Assert.That((bool)JObject.Parse(result.Results.Single().Summary)["ui"]["toggleBound"],Is.True);
        }
        [Test] public void ExplicitPhysicsShapeChangeIsAppliedAndReadBack()
        {
            Run("create_object","Shape",args:new JObject{["kind"]="cube"});var result=Run("configure_physics",target:"Shape",args:new JObject{["collider"]="sphere",["mass"]=2});Assert.That(result.Success,Is.True,result.Error);
            Assert.That(Named("Shape").GetComponent<SphereCollider>(),Is.Not.Null);Assert.That(Named("Shape").GetComponent<BoxCollider>(),Is.Null);Assert.That((string)JObject.Parse(result.Results.Single().Summary)["physics"]["collider"],Is.EqualTo("SphereCollider"));
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
