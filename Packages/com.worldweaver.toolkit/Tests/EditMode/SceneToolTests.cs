using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldWeaver.Editor.Assistant;
using Object = UnityEngine.Object;

namespace WorldWeaver.Tests
{
    public sealed class SceneToolTests
    {
        private Scene testScene, previous;
        private readonly HashSet<string> assets = new HashSet<string>();
        private readonly List<int> groups = new List<int>();
        [SetUp] public void SetUp()
        {
            testScene=default;previous=SceneManager.GetActiveScene();
            bool untitled=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s=>s.isLoaded&&string.IsNullOrEmpty(s.path));
            if(untitled&&!Application.isBatchMode)Assert.Ignore("Перед GUI-запуском этих тестов сохраните открытые сцены: Unity запрещает additive-сцену рядом с несохранённой.");
            testScene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,untitled?NewSceneMode.Single:NewSceneMode.Additive);SceneManager.SetActiveScene(testScene);
        }
        [TearDown] public void TearDown()
        {
            foreach(int group in groups.OrderByDescending(g=>g))try{Undo.RevertAllDownToGroup(group);}catch(ArgumentException){ }
            groups.Clear();
            if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
            if(testScene.IsValid()&&testScene.isLoaded){if(previous.IsValid()&&previous.isLoaded)EditorSceneManager.CloseScene(testScene,true);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            foreach(string path in assets.OrderByDescending(s=>s.Length))if(AssetDatabase.LoadMainAssetAtPath(path))AssetDatabase.DeleteAsset(path);
            foreach(string folder in assets.Select(p=>Path.GetDirectoryName(p)?.Replace('\\','/')).Where(p=>p!=null).Distinct().OrderByDescending(p=>p.Length))
                if(AssetDatabase.IsValidFolder(folder)&&AssetDatabase.FindAssets("",new[]{folder}).Length==0)AssetDatabase.DeleteAsset(folder);
            assets.Clear();
        }
        private static EditorCommand Cmd(string tool,string name="",string target="",JObject args=null)=>new EditorCommand{tool=tool,name=name,target=target,args=args??new JObject()};
        private static EditorPlan Plan(params EditorCommand[] commands)=>new EditorPlan{actions=commands.ToList()};
        private EditorBatchResult Run(EditorPlan plan)
        {
            var result=SceneToolExecutor.Execute(plan);if(result.UndoGroup>=0)groups.Add(result.UndoGroup);
            foreach(var step in result.Results)foreach(string path in step.CreatedAssets)assets.Add(path);return result;
        }
        private GameObject Find(string name)=>testScene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Single(t=>t.name==name).gameObject;
        private static JObject MinimalWorld()=>new JObject{["resolution"]=33,["trees"]=0,["rocks"]=0,["buildings"]=0};
        [Test] public void EmptyPlanDoesNotCreateUndoOrObjects()
        {var result=Run(Plan());Assert.That(result.Success,Is.True);Assert.That(result.UndoGroup,Is.EqualTo(-1));Assert.That(testScene.rootCount,Is.Zero);}
        [Test] public void ReadOnlyPlanPreservesUndoGroupAndScene()
        {
            var go=new GameObject("InspectConsumer");int before=Undo.GetCurrentGroup();
            var result=Run(Plan(Cmd("inspect_object",target:"id:"+go.GetInstanceID()),Cmd("search_project",args:new JObject{["query"]="t:Material",["limit"]=2})));
            Assert.That(result.Success,Is.True,result.Error);Assert.That(result.UndoGroup,Is.EqualTo(-1));Assert.That(Undo.GetCurrentGroup(),Is.EqualTo(before));Assert.That(testScene.rootCount,Is.EqualTo(1));
        }
        [Test] public void InvalidLaterCommandPreventsEarlierMutation()
        {
            var foreign=new GameObject("Consumer");foreign.transform.position=new Vector3(3,4,5);
            var plan=Plan(Cmd("set_transform",target:"id:"+foreign.GetInstanceID(),args:new JObject{["position"]=new JArray(9,9,9)}),Cmd("create_world","Unsafe",args:new JObject{["typo"]=1}));
            Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(plan));var result=Run(plan);
            Assert.That(result.Success,Is.False);Assert.That(foreign.transform.position,Is.EqualTo(new Vector3(3,4,5)));Assert.That(testScene.rootCount,Is.EqualTo(1));
        }
        [Test] public void PriorCreationCanBeTargetedAndOneUndoRestoresWholeBatch()
        {
            var foreign=new GameObject("Consumer");foreign.transform.position=new Vector3(1,2,3);
            var result=Run(Plan(Cmd("create_object","Crate",args:new JObject{["kind"]="cube",["on_terrain"]=false}),Cmd("set_transform",target:"@Crate",args:new JObject{["position"]=new JArray(6,2,4)})));
            Assert.That(result.Success,Is.True,result.Error);Assert.That(Find("Crate").transform.position,Is.EqualTo(new Vector3(6,2,4)));
            Undo.RevertAllDownToGroup(result.UndoGroup);groups.Remove(result.UndoGroup);Assert.That(testScene.rootCount,Is.EqualTo(1));Assert.That(foreign.transform.position,Is.EqualTo(new Vector3(1,2,3)));
        }
        [Test] public void FutureTargetIsRejectedBeforeMutations()
        {Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(Plan(Cmd("set_transform",target:"@Later",args:new JObject{["position"]=new JArray(0,0,0)}),Cmd("create_object","Later",args:new JObject{["kind"]="cube"}))));Assert.That(testScene.rootCount,Is.Zero);}
        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)]
        public void NonFiniteCoordinatesAreRejected(double value)
        {var foreign=new GameObject("Consumer");Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(Plan(Cmd("set_transform",target:"id:"+foreign.GetInstanceID(),args:new JObject{["position"]=new JArray(value,0,0)}))));}
        [Test] public void RecursiveBudgetCountsTreePartsBeforeGeneration()
        {Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(Plan(Cmd("create_world","TooDense",args:new JObject{["trees"]=150,["rocks"]=0,["buildings"]=0}))));Assert.That(testScene.rootCount,Is.Zero);}
        [Test] public void SceneBudgetCannotBeBypassedAcrossBatches()
        {
            for(int i=0;i<1999;i++){var go=new GameObject("owned"+i);go.AddComponent<GeneratedSceneObject>().Kind="part";}
            Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(Plan(Cmd("create_npc","Overflow"))));
        }
        [Test] public void ConsumerObjectAndForeignDescendantCannotBeDeleted()
        {
            var consumer=new GameObject("Consumer");Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(Plan(Cmd("delete_object",target:"id:"+consumer.GetInstanceID()))));
            var owned=new GameObject("Owned");owned.AddComponent<GeneratedSceneObject>();consumer.transform.SetParent(owned.transform);
            Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(Plan(Cmd("delete_object",target:"id:"+owned.GetInstanceID()))));Assert.That(consumer,Is.Not.Null);
        }
        [Test] public void DeletedCreationCannotBeReferencedThroughOtherAlias()
        {
            var plan=Plan(Cmd("create_object","Crate",args:new JObject{["kind"]="cube"}),Cmd("delete_object",target:"@Crate"),Cmd("inspect_object",target:"Crate"));
            Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(plan));Assert.That(testScene.rootCount,Is.Zero);
        }
        [Test] public void WorldCreatesActualDecoratedTerrainWithPersistentAssets()
        {
            var args=MinimalWorld();args["trees"]=3;args["rocks"]=4;args["buildings"]=2;args["style"]="alpine";
            var result=Run(Plan(Cmd("create_world","Alpine",args:args)));Assert.That(result.Success,Is.True,result.Error);
            var world=Find("Alpine");var markers=world.GetComponentsInChildren<GeneratedSceneObject>();
            Assert.That(markers.Count(m=>m.Kind=="tree"),Is.EqualTo(3));Assert.That(markers.Count(m=>m.Kind=="rock"),Is.EqualTo(4));Assert.That(markers.Count(m=>m.Kind=="house"),Is.EqualTo(2));
            var terrain=world.GetComponentInChildren<Terrain>();Assert.That(terrain.terrainData.terrainLayers.Length,Is.EqualTo(3));Assert.That(AssetDatabase.Contains(terrain.terrainData),Is.True);
            Assert.That(terrain.terrainData.terrainLayers.All(l=>l&&l.diffuseTexture&&AssetDatabase.Contains(l.diffuseTexture)),Is.True);
            Assert.That(world.GetComponentInChildren<Light>(),Is.Not.Null);Assert.That(world.GetComponentInChildren<Camera>(),Is.Not.Null);Assert.That(world.transform.Find("Water"),Is.Not.Null);
        }
        [Test] public void SameSeedProducesSameDecorationPositions()
        {
            var args=MinimalWorld();args["trees"]=5;args["seed"]=142;
            var first=Run(Plan(Cmd("create_world","First",args:(JObject)args.DeepClone())));Assert.That(first.Success,Is.True,first.Error);
            var second=Run(Plan(Cmd("create_world","Second",args:(JObject)args.DeepClone())));Assert.That(second.Success,Is.True,second.Error);
            var a=Find("First").GetComponentsInChildren<GeneratedSceneObject>().Where(m=>m.Kind=="tree").Select(m=>m.transform.position).ToArray();
            var b=Find("Second").GetComponentsInChildren<GeneratedSceneObject>().Where(m=>m.Kind=="tree").Select(m=>m.transform.position).ToArray();Assert.That(b,Is.EqualTo(a));
        }
        [Test] public void RuntimeFailureRollsBackSceneAndEveryNewAsset()
        {
            var consumer=new GameObject("Consumer");consumer.transform.position=new Vector3(1,2,3);
            var before=AssetDatabase.FindAssets("",new[]{"Assets"}).OrderBy(x=>x).ToArray();
            var result=Run(Plan(Cmd("create_world","Rollback",args:MinimalWorld()),Cmd("set_transform",target:"id:"+consumer.GetInstanceID(),args:new JObject{["position"]=new JArray(8,9,10)}),Cmd("scatter_objects","Impossible","@Rollback",new JObject{["kind"]="house",["count"]=2,["size"]=new JArray(1,1),["spacing"]=100})));
            Assert.That(result.Success,Is.False);Assert.That(consumer.transform.position,Is.EqualTo(new Vector3(1,2,3)));Assert.That(testScene.rootCount,Is.EqualTo(1));
            Assert.That(AssetDatabase.FindAssets("",new[]{"Assets"}).OrderBy(x=>x).ToArray(),Is.EqualTo(before),"Rollback leaked assets/folders.");
        }
        [Test] public void NpcHasScopedKnowledgeRoutesInventoryAndEditableProfile()
        {
            var result=Run(Plan(Cmd("create_npc","Игнат",args:new JObject{["role"]="Страж",["personality"]="Внимательный",["goals"]="Беречь мост",["knowledge"]=new JArray("Мост построен из гранита.","Плата — две монеты."),["inventory"]=new JObject{["Crystal"]=4}}),Cmd("update_npc",target:"@Игнат",args:new JObject{["personality"]="Спокойный"})));
            Assert.That(result.Success,Is.True,result.Error);var go=Find("Игнат");var npc=go.GetComponent<IntelligentNPC>();Assert.That(npc.Role,Is.EqualTo("Страж"));Assert.That(npc.Personality,Is.EqualTo("Спокойный"));Assert.That(npc.Goals,Is.EqualTo("Беречь мост"));
            Assert.That(npc.Knowledge.Documents.Count,Is.EqualTo(2));Assert.That(npc.Knowledge.Documents.All(d=>d.Scope==npc.CharacterId),Is.True);Assert.That(AssetDatabase.Contains(npc.Knowledge),Is.True);
            Assert.That(npc.Models.Dialogue,Is.Not.Null);Assert.That(npc.Models.Planning,Is.Not.Null);Assert.That(go.GetComponent<Inventory>().Count("Crystal"),Is.EqualTo(4));Assert.That(go.GetComponent<WorldEntity>().Id,Is.EqualTo("Игнат"));
        }
        [Test] public void NpcSnapsToDryGroundUnlessExplicitlyDisabled()
        {
            var result=Run(Plan(Cmd("create_world","DrySpawnWorld",args:MinimalWorld()),Cmd("create_npc","LandNpc","@DrySpawnWorld",new JObject{["position"]=new JArray(0,0,0)}),Cmd("create_npc","UnderwaterNpc","@DrySpawnWorld",new JObject{["position"]=new JArray(0,-10,0),["on_terrain"]=false})));
            Assert.That(result.Success,Is.True,result.Error);var terrain=Find("DrySpawnWorld").GetComponentInChildren<Terrain>();var spec=terrain.GetComponent<GeneratedTerrain>().Specification;
            Assert.That(Find("LandNpc").transform.position.y,Is.GreaterThanOrEqualTo(terrain.transform.position.y+spec.waterLevel*spec.height+.34f));
            Assert.That(Find("UnderwaterNpc").transform.position,Is.EqualTo(new Vector3(0,-10,0)));
        }
        [Test] public void UpdatingNpcGoalPreservesConfiguredKnownEntities()
        {
            var created=Run(Plan(Cmd("create_npc","ExistingNpc",args:new JObject{["role"]="Страж",["personality"]="Осторожный",["biography"]="Живёт у моста",["goals"]="Беречь мост",["constraints"]="Не трогать чужие вещи",["knowledge"]="Мост каменный."})));
            Assert.That(created.Success,Is.True,created.Error);var npc=Find("ExistingNpc").GetComponent<IntelligentNPC>();
            var player=new GameObject("Player").AddComponent<WorldEntity>();player.Id="Player";
            var known=new GameObject("KnownItem").AddComponent<WorldEntity>();known.Id="KnownItem";
            var privateTarget=new GameObject("PrivateTarget").AddComponent<WorldEntity>();privateTarget.Id="PrivateTarget";
            var knowledge=npc.Knowledge;var models=npc.Models;var inventory=npc.GetComponent<Inventory>();
            var contexts=new[]{new List<WorldEntity>{player},new List<WorldEntity>{known,player},new List<WorldEntity>()};
            for(int i=0;i<contexts.Length;i++)
            {
                var configured=contexts[i];npc.KnownEntities=configured;var expected=configured.ToArray();string goal="Новая цель "+i;
                var result=Run(Plan(Cmd("update_npc",target:"id:"+npc.gameObject.GetInstanceID(),args:new JObject{["goals"]=goal})));
                Assert.That(result.Success,Is.True,result.Error);Assert.That(npc.Goals,Is.EqualTo(goal));
                Assert.That(npc.KnownEntities,Is.SameAs(configured),"Updating a profile must not replace the user's context list.");
                Assert.That(npc.KnownEntities,Is.EqualTo(expected),"Known entity references and their order must stay unchanged, including an empty list.");
                Assert.That(npc.KnownEntities.Contains(privateTarget),Is.False,"Updating a profile must not add an unrelated scene entity.");
                Assert.That(npc.Role,Is.EqualTo("Страж"));Assert.That(npc.Personality,Is.EqualTo("Осторожный"));Assert.That(npc.Biography,Is.EqualTo("Живёт у моста"));Assert.That(npc.Constraints,Is.EqualTo("Не трогать чужие вещи"));
                Assert.That(npc.Knowledge,Is.SameAs(knowledge));Assert.That(npc.Models,Is.SameAs(models));Assert.That(npc.GetComponent<Inventory>(),Is.SameAs(inventory));
            }
        }
        [Test] public void InspectReturnsActualNpcKnowledgeAndLightParameters()
        {
            var result=Run(Plan(Cmd("create_npc","ContextNpc",args:new JObject{["biography"]="Живёт у моста",["constraints"]="Беречь мост",["knowledge"]=new JArray("Мост каменный.")}),Cmd("set_lighting","ContextLamp",args:new JObject{["type"]="point",["range"]=15}),Cmd("inspect_object",target:"@ContextNpc"),Cmd("inspect_object",target:"@ContextLamp")));
            Assert.That(result.Success,Is.True,result.Error);var npc=JObject.Parse(result.Results[2].Summary)["npc"];var light=JObject.Parse(result.Results[3].Summary)["light"];
            Assert.That((string)npc["biography"],Is.EqualTo("Живёт у моста"));Assert.That((string)npc["knowledgeExcerpt"][0]["text"],Is.EqualTo("Мост каменный."));Assert.That((string)light["type"],Is.EqualTo("point"));Assert.That((float)light["range"],Is.EqualTo(15));
        }
        [Test] public void MaterialEditCreatesCopyAndDoesNotAlterSharedAsset()
        {
            var original=new Material(Shader.Find("Standard")){color=Color.red};string path=AssetDatabase.GenerateUniqueAssetPath("Assets/AssistantConsumerMaterial.mat");AssetDatabase.CreateAsset(original,path);assets.Add(path);
            var consumer=GameObject.CreatePrimitive(PrimitiveType.Cube);consumer.name="Consumer";consumer.GetComponent<Renderer>().sharedMaterial=original;
            var result=Run(Plan(Cmd("set_material",target:"id:"+consumer.GetInstanceID(),args:new JObject{["color"]="#00FF00"})));
            Assert.That(result.Success,Is.True,result.Error);Assert.That(original.color,Is.EqualTo(Color.red));Assert.That(consumer.GetComponent<Renderer>().sharedMaterial,Is.Not.SameAs(original));Assert.That(consumer.GetComponent<Renderer>().sharedMaterial.color.g,Is.EqualTo(1));
        }
        [Test] public void PointLightsAndRealRuntimePresetsCanBeCreated()
        {
            var result=Run(Plan(Cmd("set_lighting","Lantern",args:new JObject{["type"]="point",["range"]=12,["intensity"]=3,["position"]=new JArray(1,2,3)}),Cmd("attach_behavior",target:"@Lantern",args:new JObject{["behavior"]="bob",["amplitude"]=.2,["frequency"]=.5})));
            Assert.That(result.Success,Is.True,result.Error);var go=Find("Lantern");Assert.That(go.GetComponent<Light>().type,Is.EqualTo(LightType.Point));Assert.That(go.GetComponent<Light>().range,Is.EqualTo(12));Assert.That(go.GetComponent<SceneBehavior>().Mode,Is.EqualTo(SceneBehaviorKind.Bob));
        }
        [Test] public void UpdateWorldClonesDataAndReanchorsExistingDecoration()
        {
            var args=MinimalWorld();args["trees"]=3;var first=Run(Plan(Cmd("create_world","Editable",args:args)));Assert.That(first.Success,Is.True,first.Error);
            var terrain=Find("Editable").GetComponentInChildren<Terrain>();var original=terrain.terrainData;var oldHeights=original.GetHeights(0,0,33,33);
            var second=Run(Plan(Cmd("update_world",target:"Editable",args:new JObject{["height"]=20,["mountainStrength"]=.1})));Assert.That(second.Success,Is.True,second.Error);
            Assert.That(terrain.terrainData,Is.Not.SameAs(original));Assert.That(original.GetHeights(0,0,33,33),Is.EqualTo(oldHeights));Assert.That(terrain.GetComponent<GeneratedTerrain>().Specification.height,Is.EqualTo(20));
            foreach(var marker in Find("Editable").GetComponentsInChildren<GeneratedSceneObject>().Where(m=>m.Kind=="tree"))Assert.That(marker.transform.position.y,Is.EqualTo(terrain.SampleHeight(marker.transform.position)+terrain.transform.position.y).Within(.001));
        }
        [Test] public void ProjectTraversalAndNonFinalSaveAreRejected()
        {
            Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(Plan(Cmd("read_script",args:new JObject{["path"]="Assets/../../private.cs"}))));
            Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(Plan(Cmd("save_scene",args:new JObject{["path"]="Assets/Consumer.unity"}))));
            Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(Plan(Cmd("save_scene"),Cmd("search_project",args:new JObject{["query"]="t:Material"}))));
        }
        [Test] public void ContextIsBoundedAndTargetCannotEscapeActiveScene()
        {
            for(int i=0;i<90;i++)new GameObject("Object"+i);
            var context=SceneToolExecutor.Context();Assert.That(((JArray)context["objects"]).Count,Is.EqualTo(80));
            var other=EditorSceneManager.NewPreviewScene();SceneManager.SetActiveScene(testScene);var foreign=new GameObject("OtherScene");SceneManager.MoveGameObjectToScene(foreign,other);
            try{Assert.Throws<ArgumentException>(()=>SceneToolExecutor.ResolveTarget("id:"+foreign.GetInstanceID()));}finally{EditorSceneManager.ClosePreviewScene(other);}
        }
    }
}
