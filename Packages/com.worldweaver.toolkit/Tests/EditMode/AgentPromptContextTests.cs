using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// <summary>Compaction changes model input, never host evidence or live Unity state.</summary>
    public sealed class AgentPromptContextTests
    {
        private Scene previous,scene;
        private GameObject[] selection;
        private string folder;
        private readonly List<ChatController> controllers=new List<ChatController>();
        [SetUp] public void SetUp()
        {
            previous=SceneManager.GetActiveScene();selection=Selection.gameObjects;
            bool untitled=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s=>s.isLoaded&&string.IsNullOrEmpty(s.path));
            if(untitled&&!Application.isBatchMode)Assert.Ignore("Сохраните открытую сцену перед интеграционным тестом.");
            scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,untitled?NewSceneMode.Single:NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            folder=Path.Combine(Path.GetTempPath(),"WorldWeaverPromptContext_"+Guid.NewGuid().ToString("N"));
        }
        [TearDown] public void TearDown()
        {
            foreach(var controller in controllers)controller.Dispose();controllers.Clear();
            Selection.objects=selection.Where(go=>go).Cast<UnityEngine.Object>().ToArray();
            if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
            if(scene.IsValid()&&scene.isLoaded){if(previous.IsValid()&&previous.isLoaded)EditorSceneManager.CloseScene(scene,true);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
        private ChatController Controller(AgentTaskState task)
        {
            var controller=new ChatController(_=>throw new InvalidOperationException("This test must not perform inference"),folder,Path.Combine(folder,"models"));controllers.Add(controller);
            controller.Settings.Power=AssistantPower.Balanced;
            typeof(ChatController).GetField("executingMessage",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(controller,new AssistantMessage{Task=task});
            return controller;
        }
        private static GameObject Owned(string name,string kind,Transform parent=null)
        {var go=new GameObject(name);go.transform.SetParent(parent);go.AddComponent<GeneratedSceneObject>().Kind=kind;return go;}
        private static JObject Context(ChatController controller)=>JObject.Parse((string)typeof(ChatController).GetMethod("BoundedSceneContext",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(controller,null));
        private static JObject Readback(EditorBatchResult batch)=>JObject.Parse((string)typeof(ChatController).GetMethod("ReadbackForModel",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{batch}));

        [Test] public void SceneSummaryAggregatesDecorationsButPreservesScopedCountsAndImportantIdentities()
        {
            var world=Owned("TaskValley","world");var ground=Owned("Ground","terrain",world.transform);ground.AddComponent<GeneratedTerrain>().Specification.width=160;
            var trees=Enumerable.Range(0,12).Select(i=>Owned("Pine_"+i,"tree",world.transform)).ToArray();
            for(int i=0;i<3;i++)Owned("House_"+i,"house",world.transform);
            for(int i=0;i<4;i++)Owned("Rock_"+i,"rock",world.transform);
            for(int i=0;i<100;i++)Owned("OtherTree_"+i,"tree");
            var named=Owned("NamedRock","rock");var npc=Owned("Guide","npc",world.transform);npc.AddComponent<IntelligentNPC>().Goals="Reach the gate";
            var light=Owned("Sun","light",world.transform);light.AddComponent<Light>();var camera=Owned("View","camera",world.transform);camera.AddComponent<Camera>().enabled=false;
            trees[0].transform.localScale=new Vector3(2,3,4);Selection.activeGameObject=trees[0];
            var task=new AgentTaskState{OriginalRequest="Осмотри NamedRock в TaskValley",ScopeTarget=ProjectContextIndex.StableTarget(world)};
            var full=SceneToolExecutor.Context();var compact=Context(Controller(task));
            TestContext.WriteLine("PROMPT_CHARS scene_before="+full.ToString(Formatting.None).Length+" scene_after="+compact.ToString(Formatting.None).Length);
            Assert.That(compact["capabilities"],Is.Null);Assert.That((int)compact["logicalCounts"]["tree"],Is.EqualTo(112));Assert.That((int)compact["scopeLogicalCounts"]["tree"],Is.EqualTo(12));
            Assert.That((int)compact["scopeLogicalCounts"]["house"],Is.EqualTo(3));Assert.That((int)compact["scopeLogicalCounts"]["rock"],Is.EqualTo(4));
            var objects=((JArray)compact["objects"]).OfType<JObject>().ToArray();
            foreach(var go in new[]{world,ground,trees[0],named,npc,light,camera})Assert.That(objects.Any(o=>(string)o["target"]==ProjectContextIndex.StableTarget(go)),Is.True,go.name);
            Assert.That(objects.Count(o=>(string)o["kind"]=="tree"),Is.EqualTo(1));Assert.That((float)objects.Single(o=>(string)o["target"]==ProjectContextIndex.StableTarget(trees[0]))["scale"][1],Is.EqualTo(3));
            Assert.That((string)compact["scopeTarget"],Is.EqualTo(task.ScopeTarget));Assert.That(compact.ToString(Formatting.None).Length,Is.LessThan(full.ToString(Formatting.None).Length/2));
            Assert.That(ProjectContextIndex.Objects().Count(),Is.EqualTo(125));Assert.That(SceneToolExecutor.Context()["capabilities"],Is.Not.Null,"Full host context remains unchanged");
        }
        [Test] public void OmittedObjectCanStillBeFoundAndInspectedThroughActualReadOnlyTools()
        {
            var omitted=Owned("UnlistedPine","tree");omitted.transform.position=new Vector3(17,2,31);
            var compact=Context(Controller(new AgentTaskState{OriginalRequest="Объясни сцену"}));
            Assert.That(((JArray)compact["objects"]).Any(o=>(string)o["target"]==ProjectContextIndex.StableTarget(omitted)),Is.False);
            var found=SceneToolExecutor.Execute(new EditorPlan{actions=new List<EditorCommand>{new EditorCommand{tool="find_scene_objects",args=new JObject{["query"]="UnlistedPine",["limit"]=2}}}});
            Assert.That(found.Success,Is.True,found.Error);var hits=JObject.Parse(found.Results.Single().Summary);string target=(string)hits["sceneObjects"][0]["target"];
            var inspected=SceneToolExecutor.Execute(new EditorPlan{actions=new List<EditorCommand>{new EditorCommand{tool="inspect_object",target=target,args=new JObject()}}});
            Assert.That(inspected.Success,Is.True,inspected.Error);Assert.That((int)JObject.Parse(inspected.Results.Single().Summary)["position"][0],Is.EqualTo(17));
            Assert.That((string)compact["snapshotNote"],Does.Contain("find_scene_objects").And.Contain("inspect_object"));
        }
        [TestCase("Осмотри trees у sunset",false)]
        [TestCase("Осмотри Tree",true)]
        public void CommonNamesCannotExpandSummaryToEveryDuplicate(string request,bool explicitlyAmbiguous)
        {
            for(int i=0;i<100;i++)Owned("Tree","tree");
            var compact=Context(Controller(new AgentTaskState{OriginalRequest=request}));
            Assert.That(((JArray)compact["objects"]).Count,Is.Zero);Assert.That(compact.ToString(Formatting.None).Length,Is.LessThan(1600));Assert.That((int)compact["logicalCounts"]["tree"],Is.EqualTo(100));
            if(explicitlyAmbiguous)Assert.That(compact["ambiguousRequestedNames"].Values<string>(),Does.Contain("Tree"));else Assert.That(compact["ambiguousRequestedNames"],Is.Null);
        }
        [Test] public void ManyImportantNpcsUseAddressedRetrievalInsteadOfExceedingSceneBudget()
        {
            for(int i=0;i<80;i++){var npc=Owned("Guide_"+i,"npc").AddComponent<IntelligentNPC>();npc.Goals=new string('g',300);}
            var world=Owned("World","world");var light=Owned("Sun","light");light.AddComponent<Light>();var camera=Owned("View","camera");camera.AddComponent<Camera>().enabled=false;
            var compact=Context(Controller(new AgentTaskState{OriginalRequest="Объясни сцену"}));
            Assert.That(compact.ToString(Formatting.None).Length,Is.LessThanOrEqualTo(4000));Assert.That((int)compact["logicalCounts"]["npc"],Is.EqualTo(80));Assert.That((int)compact["omittedObjectDetails"],Is.GreaterThan(0));Assert.That((int)compact["listedLimit"],Is.GreaterThan(0));
            foreach(var go in new[]{world,light,camera})Assert.That(((JArray)compact["objects"]).Any(row=>(string)row["target"]==ProjectContextIndex.StableTarget(go)),Is.True,go.name+" must remain visible even after 80 NPC details");
        }
        [Test] public void RequiredSelectionIdentityOverflowIsExplicitAndNeverSilentlyDropsTargets()
        {
            var selected=Enumerable.Range(0,100).Select(i=>Owned("SelectedPine_"+i,"tree")).ToArray();Selection.objects=selected;
            var compact=Context(Controller(new AgentTaskState{OriginalRequest="Объясни выбранные объекты"}));
            Assert.That((bool)compact["requiredIdentitiesExceedBudget"],Is.True);var ids=((JArray)compact["objects"]).Select(row=>(string)row["target"]).ToArray();Assert.That(ids,Is.EquivalentTo(selected.Select(ProjectContextIndex.StableTarget)));
        }
        [Test] public void TaskProjectionKeepsOriginalConstraintsAndAmbiguityWithoutBaselineIdsOrHostMutation()
        {
            var task=new AgentTaskState{OriginalRequest="Старые 500 деревьев оставь; добавь 3 новых дерева",ScopeTarget="owned:task-world",MutationAuthorized=true};
            var requirement=new AgentRequirement{Kind="count",Description="Добавлены 3 дерева",Expected="tree",Number=3,Relative=true,Baseline=500,BaselineTargets=Enumerable.Range(0,500).Select(i=>"owned:baseline-"+i).ToList()};
            task.Requirements.Add(requirement);task.Checks.Add(new AgentCheckResult{RequirementId=requirement.Id,Description=requirement.Description,Passed=false,Actual="2",Evidence=new string('e',3000)});task.Evidence.Add(new string('x',5000));task.OpenQuestions.Add("Нужна область размещения");
            string before=JsonConvert.SerializeObject(task);string text=AgentTaskVerifier.Context(task);var projection=JObject.Parse(text);
            TestContext.WriteLine("PROMPT_CHARS task_before="+before.Length+" task_after="+text.Length);
            Assert.That((string)projection["OriginalRequest"],Is.EqualTo(task.OriginalRequest));Assert.That((string)projection["ScopeTarget"],Is.EqualTo(task.ScopeTarget));
            Assert.That((int)projection["Requirements"][0]["Number"],Is.EqualTo(3));Assert.That((bool)projection["Requirements"][0]["Relative"],Is.True);Assert.That((int)projection["Requirements"][0]["BaselineTargetCount"],Is.EqualTo(500));
            Assert.That((int)projection["Checks"][0]["Requirement"],Is.EqualTo(0));Assert.That((bool)projection["Checks"][0]["Passed"],Is.False);Assert.That((string)projection["Checks"][0]["Actual"],Is.EqualTo("2"));Assert.That(projection["OpenQuestions"].Values<string>(),Does.Contain(task.OpenQuestions[0]));
            Assert.That(text,Does.Not.Contain("owned:baseline-").And.Not.Contain(requirement.Id).And.Not.Contain(task.Id));Assert.That(text.Length,Is.LessThan(3000));Assert.That(JsonConvert.SerializeObject(task),Is.EqualTo(before));
        }
        [Test] public void CompactTaskDoesNotWeakenStableBaselineVerification()
        {
            var original=Owned("OldPine","tree");var task=AgentTaskVerifier.Create("Добавь ровно 1 новое дерево",true);Owned("NewPine","tree");
            Assert.That(AgentTaskVerifier.Verify(task).Single().Passed,Is.True);string before=JsonConvert.SerializeObject(task);AgentTaskVerifier.Context(task);Assert.That(JsonConvert.SerializeObject(task),Is.EqualTo(before));
            UnityEngine.Object.DestroyImmediate(original);Owned("Replacement","tree");
            Assert.That(AgentTaskVerifier.Verify(task).Single().Passed,Is.False,"Replacing a baseline identity must not turn the extra object into an old one");
        }
        [Test] public void TaskEvidenceReportsEveryKindOfTruncationAndKeepsNewestEntries()
        {
            var task=new AgentTaskState();task.Checks.Add(new AgentCheckResult{Evidence=new string('e',800)});
            Assert.That((bool)JObject.Parse(AgentTaskVerifier.Context(task))["EvidenceTruncated"],Is.True);
            task.Checks.Clear();task.Evidence=Enumerable.Range(0,9).Select(i=>"evidence-"+i).ToList();var compact=JObject.Parse(AgentTaskVerifier.Context(task));
            Assert.That((bool)compact["EvidenceTruncated"],Is.True);Assert.That(compact["Evidence"].Values<string>(),Does.Contain("evidence-8").And.Not.Contain("evidence-0"));
        }
        [Test] public void ActualForestValleyAcceptancePromptBindsTerrainDimensionsAndAllLiteralCounts()
        {
            const string prompt="Создай лесную долину размером 160 на 160 метров: ровно 12 деревьев, 3 дома и 4 камня. Добавь подходящие свет и камеру для осмотра мира.";
            var task=AgentTaskVerifier.Create(prompt,true);
            Assert.That(task.Requirements.Single(r=>r.Kind=="terrain_width").Number,Is.EqualTo(160));Assert.That(task.Requirements.Single(r=>r.Kind=="terrain_length").Number,Is.EqualTo(160));
            foreach(var pair in new[]{new{Kind="tree",Count=12},new{Kind="house",Count=3},new{Kind="rock",Count=4}})Assert.That(task.Requirements.Single(r=>r.Kind=="count"&&r.Expected==pair.Kind).Number,Is.EqualTo(pair.Count));
            Assert.That(AgentTaskVerifier.Verify(task).All(check=>!check.Passed),Is.True,"An absent world cannot satisfy its extracted dimensions or counts");
        }
        [TestCase("Создай valley 160x120 height 24",true)]
        [TestCase("Сделай деревню размером 160x120 высотой 24",true)]
        [TestCase("Увеличь высоту мира до 24",true)]
        [TestCase("Теперь уменьши height существующего рельефа до 24 метров. Обнови именно эту долину, сохрани деревья, дома и камни; второй мир не создавай.",true)]
        [TestCase("В мире измени высоту дерева до 3",false)]
        [TestCase("В мире измени height существующего NPC до 3",false)]
        [TestCase("В мире создай дерево высотой 8 метров",false)]
        [TestCase("В мире добавь NPC высотой 2",false)]
        [TestCase("Сделай NPC Мирон высотой 2",false)]
        [TestCase("Сделай кнопку высотой 48",false)]
        public void TerrainHeightRecognizesWorldInflectionsWithoutTreatingNpcNamesAsWorlds(string prompt,bool hasTerrainHeight)
        {
            var task=AgentTaskVerifier.Create(prompt,true);Assert.That(task.Requirements.Any(r=>r.Kind=="terrain_height"),Is.EqualTo(hasTerrainHeight));
        }
        [Test] public void ReadbackBoundsAssetListsAndRetainsFailuresWithoutChangingFullAudit()
        {
            var batch=new EditorBatchResult{Success=false,Error="A measured verification failed"};
            for(int i=0;i<20;i++)batch.Results.Add(new EditorCommandResult{Tool="create_object",Success=true,Target="owned:object-"+i,Summary=new string('s',600),CreatedAssets=Enumerable.Range(i*20,20).Select(j=>"Assets/WorldWeaverGenerated/Material_"+j+".mat").ToList()});
            batch.Results.Add(new EditorCommandResult{Tool="job_result",Success=false,Target="job:real-failure",Error="Compiler CS0103 failed",Summary=new string('f',2000)});
            string original=JsonConvert.SerializeObject(batch);var compact=Readback(batch);string text=compact.ToString(Formatting.None);
            TestContext.WriteLine("PROMPT_CHARS readback_before="+original.Length+" readback_after="+text.Length);
            Assert.That(text.Length,Is.LessThanOrEqualTo(3000));Assert.That((bool)compact["Success"],Is.False);Assert.That((int)compact["ResultCount"],Is.EqualTo(21));Assert.That((int)compact["FailedResultCount"],Is.EqualTo(1));Assert.That((int)compact["CreatedAssetCount"],Is.EqualTo(400));
            Assert.That(((JArray)compact["CreatedAssetSamples"]).Count,Is.EqualTo(3));Assert.That((int)compact["OmittedResultCount"],Is.GreaterThan(0));Assert.That(text,Does.Contain("job:real-failure").And.Contain("CS0103"));Assert.That(JsonConvert.SerializeObject(batch),Is.EqualTo(original));
        }
        [Test] public void ReadbackPreservesSuccessfulMacroTargetAndLiteralTerrainEvidence()
        {
            var batch=new EditorBatchResult{Success=true};batch.Results.Add(new EditorCommandResult{Tool="create_world",Success=true,Target="owned:valley",Summary="Terrain 160x160; 12 trees, 3 houses, 4 rocks",CreatedAssets=Enumerable.Range(0,100).Select(i=>"Assets/WorldWeaverGenerated/Shared"+i+".mat").ToList()});
            var compact=Readback(batch);Assert.That((bool)compact["Success"],Is.True);Assert.That((string)compact["Results"][0]["Target"],Is.EqualTo("owned:valley"));Assert.That((string)compact["Results"][0]["Summary"],Does.Contain("160x160").And.Contain("12 trees").And.Contain("3 houses").And.Contain("4 rocks"));Assert.That((int)compact["CreatedAssetCount"],Is.EqualTo(100));Assert.That((int)compact["OmittedResultCount"],Is.Zero);
        }
    }
}
