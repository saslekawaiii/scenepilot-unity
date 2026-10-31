using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
    /// <summary>Literal schema specialization and real controller mutation guards; injected inference, no network.</summary>
    public sealed class AgentTaskSchemaTests
    {
        const string Request = "Создай мир 160 на 160 метров высотой 24: 12 деревьев, 3 дома, 4 камня.";
        const string ActualNpcRequest="Создай в этой долине NPC по имени Игнат: роль — страж, личность — осторожный. Добавь ему знание: «Северные ворота открываются в шесть утра».";
        const string ActualGoalRequest="Теперь измени его цель: патрулировать площадь и северные ворота. Остальные сведения об этом страже сохрани.";
        const string ActualLightRequest="Создай в долине ровно три новых оранжевых точечных источника света типа Point в разных местах около площади. Существующее солнце сохрани.";
        readonly List<ChatController> controllers = new List<ChatController>();
        readonly HashSet<string> fixtureAssets = new HashSet<string>();
        Scene scene, previous;
        string folder;
        sealed class Provider : ILLMProvider
        {
            internal int Calls;
            internal CompletionRequest LastRequest;
            internal EditorPlan Plan = new EditorPlan { reply = "Проверяемый ответ", complete = true };
            public Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken token)
            { Calls++; LastRequest = request; return Task.FromResult(new CompletionResult { Text = JsonConvert.SerializeObject(Plan) }); }
            public Task<bool> HealthAsync(CancellationToken token) => Task.FromResult(true);
            public void Dispose() { }
        }
        [SetUp] public void SetUp()
        {
            scene = default; previous = SceneManager.GetActiveScene();
            bool untitled = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(value => value.isLoaded && string.IsNullOrEmpty(value.path));
            if (untitled && !Application.isBatchMode) Assert.Ignore("Сохраните открытую сцену перед GUI-запуском интеграционных тестов.");
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene); folder = Path.Combine(Path.GetTempPath(), "WorldWeaverSchema_" + Guid.NewGuid().ToString("N"));
        }
        [TearDown] public void TearDown()
        {
            var assets = new HashSet<string>(); var checkpoints = new HashSet<string>();
            foreach (var controller in controllers)
            {
                foreach (var message in controller.Sessions.SelectMany(session => session.Messages))
                {
                    foreach (string asset in message.Steps.SelectMany(step => step.CreatedAssets)) assets.Add(asset);
                    if (!string.IsNullOrEmpty(message.Task?.CheckpointId)) checkpoints.Add(message.Task.CheckpointId);
                }
                controller.Dispose();
            }
            controllers.Clear();
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (scene.IsValid() && scene.isLoaded)
            { if (previous.IsValid() && previous.isLoaded) EditorSceneManager.CloseScene(scene, true); else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
            assets.UnionWith(fixtureAssets);fixtureAssets.Clear();
            foreach (string asset in assets.OrderByDescending(path => path.Length)) if (AssetDatabase.LoadMainAssetAtPath(asset)) AssetDatabase.DeleteAsset(asset);
            foreach (string id in checkpoints) if (Guid.TryParseExact(id, "N", out _))
            { string path = Path.Combine("UserSettings/WorldWeaverCheckpoints", id); if (Directory.Exists(path)) Directory.Delete(path, true); }
            if (folder != null && Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        static object Invoke(string method, params object[] args)
        {
            var type = typeof(ChatController).Assembly.GetType("WorldWeaver.Editor.Assistant.AgentTaskSchema", true);
            try { return type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
            catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        static HashSet<string> SelectedTools(string request,AssistantMode mode=AssistantMode.Execute)=>
            (HashSet<string>)typeof(ChatController).GetMethod("SelectTools",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{request,mode});
        static IEnumerable<TestCaseData> ActualAcceptanceCreationCases()
        {
            yield return new TestCaseData("Создай лесную долину размером 160 на 160 метров: ровно 12 деревьев, 3 дома и 4 камня. Добавь подходящие свет и камеру для осмотра мира.",true).SetName("ActualTask1CreatesWorld");
            yield return new TestCaseData("Теперь уменьши height существующего рельефа до 24 метров. Обнови именно эту долину, сохрани деревья, дома и камни; второй мир не создавай.",false).SetName("ActualTask2UpdatesTerrain");
            yield return new TestCaseData(ActualNpcRequest,false).SetName("ActualTask3AddsNpcInsideWorld");
            yield return new TestCaseData("Теперь измени его цель: патрулировать площадь и северные ворота. Остальные сведения об этом страже сохрани.",false).SetName("ActualTask4ChangesNpcGoal");
            yield return new TestCaseData(ActualLightRequest,false).SetName("ActualTask5AddsThreeLightsInsideWorld");
            yield return new TestCaseData("Напиши отдельный C# MonoBehaviour для плавного вращения объекта вокруг оси Y со скоростью 30 градусов в секунду. Подготовь код для моего ревью, пока не импортируй, не подключай и не исполняй его.",false).SetName("ActualTask6DraftsCode");
            yield return new TestCaseData("Какая модель сейчас выбрана и что находится в этой сцене? Только объясни в чате. Ничего не создавай и не изменяй; предыдущий черновик C# оставь ожидающим моего ревью.",false).SetName("ActualTask7OnlyExplains");
        }
        static JObject Arguments(JObject schema, string tool = "create_world") => (JObject)((JArray)schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Single(branch => EditorToolSchema.ToolName(branch) == tool&&(tool!="set_lighting"||(string)branch["properties"]?["target"]?["enum"]?[0]==""))["properties"]["args"];
        static JObject ExistingLightArguments(JObject schema)=>(JObject)((JArray)schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Single(branch=>EditorToolSchema.ToolName(branch)=="set_lighting"&&branch["properties"]?["target"]?["enum"]==null)["properties"]["args"];
        static JObject WorldArgs() => new JObject { ["width"] = 160, ["length"] = 160, ["height"] = 24, ["trees"] = 12, ["buildings"] = 3, ["rocks"] = 4, ["resolution"] = 33 };
        Terrain FlatTerrain()
        {
            var data=new TerrainData{heightmapResolution=33,size=new Vector3(160,24,160)};
            var heights=new float[33,33];for(int z=0;z<33;z++)for(int x=0;x<33;x++)heights[z,x]=.5f;data.SetHeights(0,0,heights);
            string asset="Assets/WWLightTerrain-"+Guid.NewGuid().ToString("N")+".asset";AssetDatabase.CreateAsset(data,asset);fixtureAssets.Add(asset);
            var ground=Terrain.CreateTerrainGameObject(data);ground.name="LightPlacementTerrain";ground.AddComponent<GeneratedSceneObject>().Kind="terrain";
            var generated=ground.AddComponent<GeneratedTerrain>();generated.Specification.height=24;generated.Specification.waterLevel=.6f;
            return ground.GetComponent<Terrain>();
        }
        static string[] ExposedTools(CompletionRequest request)=>((JArray)request.Schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName).ToArray();
        CompletionRequest RequestEnvelope(string input,AssistantMode mode=AssistantMode.Execute,bool cloud=false)
        {
            var controller=Controller(new Provider(),mode,cloud);
            controller.Current.Messages.Add(new AssistantMessage{Role="user",Text=input});
            controller.Current.Messages.Add(new AssistantMessage{Task=AgentTaskVerifier.Create(input,mode==AssistantMode.Execute)});
            return (CompletionRequest)typeof(ChatController).GetMethod("BuildRequest",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,new object[]{controller.Current,input});
        }
        static EditorPlan WorldPlan(JObject args = null) => new EditorPlan { reply = "Готовый план", complete = true, actions = new List<EditorCommand> { new EditorCommand { tool = "create_world", name = "BoundWorld", args = args ?? WorldArgs() } } };
        ChatController Controller(Provider provider, AssistantMode mode = AssistantMode.Execute, bool cloud = false)
        {
            var controller = new ChatController(_ => provider, Path.Combine(folder, Guid.NewGuid().ToString("N"))); controllers.Add(controller);
            controller.Settings.Mode = mode; controller.Settings.Power = AssistantPower.Fast; controller.Settings.UseSkills = false;
            if (cloud) controller.Settings.ModelKey = "pollinations:openai-fast";
            return controller;
        }
        static IEnumerator Finish(Task task)
        {
            float end = Time.realtimeSinceStartup + 20;
            while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(task.IsCompleted, Is.True); task.GetAwaiter().GetResult();
        }
        [TestCase(true)] [TestCase(false)]
        public void WorldSchemaRequiresExactOriginalCountsAndDimensionsForLocalAndCloud(bool typed)
        {
            var schema = EditorPlan.Schema(typed, new[] { "create_world" });
            Invoke("Bind", schema, AgentTaskVerifier.Create(Request, true)); var args = Arguments(schema);
            foreach (var pair in new Dictionary<string, double> { ["trees"] = 12, ["buildings"] = 3, ["rocks"] = 4, ["width"] = 160, ["length"] = 160, ["height"] = 24 })
            {
                var allowed = (JArray)args["properties"][pair.Key]["enum"];
                Assert.That(allowed.Count, Is.EqualTo(1), pair.Key); Assert.That((double)allowed[0], Is.EqualTo(pair.Value), pair.Key);
                Assert.That(args["required"].Values<string>(), Does.Contain(pair.Key));
            }
            Assert.That(Arguments(EditorPlan.Schema(typed, new[] { "create_world" }))["properties"]["trees"]["enum"], Is.Null, "One request must not contaminate the shared schema factory.");
        }
        [TestCaseSource(nameof(ActualAcceptanceCreationCases))]
        public void ActualAcceptanceTasksOnlySpecializeTheDirectWorldCreation(string request,bool createsWorld)
        {
            var task=AgentTaskVerifier.Create(request,true);Assert.That((bool)Invoke("DirectWorldCreation",request),Is.EqualTo(createsWorld));Assert.That((bool)Invoke("CreatesWorld",task),Is.EqualTo(createsWorld));
            var schema=EditorPlan.Schema(true);var before=schema.DeepClone();Invoke("Bind",schema,task);
            if(createsWorld)Assert.That((int?)schema["properties"]["actions"]["maxItems"],Is.EqualTo(1));
            else if((bool)Invoke("GoalOnlyNpcUpdate",request))
            {
                Assert.That(JToken.DeepEquals(schema["properties"]["actions"]["maxItems"],before["properties"]["actions"]["maxItems"]),Is.True);
                Assert.That(((JObject)Arguments(schema,"update_npc")["properties"]).Properties().Select(p=>p.Name),Is.EqualTo(new[]{"goals"}));
            }
            else if((bool)Invoke("GroundedPointLights",task))
            {
                Assert.That(JToken.DeepEquals(schema["properties"]["actions"]["maxItems"],before["properties"]["actions"]["maxItems"]),Is.True,"New lights must keep their ordinary multi-action limit");
                Assert.That(Arguments(schema,"set_lighting")["properties"]["on_terrain"]["enum"].Values<bool>(),Is.EqualTo(new[]{true}));
            }
            else Assert.That(JToken.DeepEquals(schema,before),Is.True,"Adding to an existing world must not inherit the single-new-world macro limit");
            Assert.That(SelectedTools(request,AssistantMode.Ask).All(SceneToolExecutor.IsReadOnlyTool),Is.True,"Intent routing cannot enlarge Ask permissions");
            Assert.That(scene.rootCount,Is.Zero);
        }
        [TestCase("Создай новый мир с NPC",true)]
        [TestCase("Сгенерируй процедурную долину",true)]
        [TestCase("Сделай небольшую деревню",true)]
        [TestCase("Создай мне пожалуйста новый лесной мир",true)]
        [TestCase("Сгенерь мир",true)]
        [TestCase("Create a new world with NPCs",true)]
        [TestCase("Generate a connected village",true)]
        [TestCase("Make a small procedural terrain",true)]
        [TestCase("Создай в долине NPC",false)]
        [TestCase("В долине создай 3 источника света",false)]
        [TestCase("Создай 3 источника света в мире",false)]
        [TestCase("Create 3 lights in the world",false)]
        [TestCase("Create NPC inside the world",false)]
        [TestCase("Создай описание существующего мира",false)]
        [TestCase("Не создавай новый мир; создай NPC",false)]
        [TestCase("Не нужно создать новый мир; добавь свет",false)]
        [TestCase("Do not create a new world; create lights",false)]
        [TestCase("Don't create a new world",false)]
        [TestCase("Never generate a world",false)]
        [TestCase("Сделай миронову мастерскую",false)]
        [TestCase("Не создавай деревню; создай новый мир",true)]
        [TestCase(null,false)]
        public void FiniteCreationGrammarSeparatesDirectObjectFromLocationAndNegation(string request,bool expected)
        {Assert.That((bool)Invoke("DirectWorldCreation",request),Is.EqualTo(expected));}
        [TestCase("Create 3 trees in the world")]
        [TestCase("Создай в долине 3 новых дерева")]
        public void ObjectsInsideExistingWorldKeepTheirOrdinaryCreationTools(string request)
        {
            var tools=SelectedTools(request);Assert.That(tools,Does.Contain("create_object").And.Contain("scatter_objects"));
            var schema=EditorPlan.Schema(true,tools);var before=schema.DeepClone();Invoke("Bind",schema,AgentTaskVerifier.Create(request,true));Assert.That(JToken.DeepEquals(schema,before),Is.True);
        }
        [Test] public void CombinedNewWorldAndNpcKeepsNpcToolsForTheFollowingRound()
        {
            const string request="Создай новый мир с NPC и 3 деревьями";var tools=SelectedTools(request);Assert.That(tools,Does.Contain("create_world").And.Contain("create_npc"));Assert.That(tools,Does.Not.Contain("create_object").And.Not.Contain("scatter_objects"));
            var task=AgentTaskVerifier.Create(request,true);var schema=EditorPlan.Schema(true,tools);Invoke("Bind",schema,task);Assert.That((int?)schema["properties"]["actions"]["maxItems"],Is.EqualTo(1));
            task.ScopeTarget="owned:already-created-world";var next=EditorPlan.Schema(true,tools);var originalLimit=next["properties"]["actions"]["maxItems"]?.DeepClone();Invoke("Bind",next,task);Assert.That(JToken.DeepEquals(next["properties"]["actions"]["maxItems"],originalLimit),Is.True,"After the world exists the NPC can be created in ordinary following actions");
        }
        [TestCase(ActualGoalRequest,true)]
        [TestCase("Измени цель Игната на охранять площадь.",true)]
        [TestCase("Установи NPC Игнату цель: охранять северные ворота.",true)]
        [TestCase("Please change his goal to patrol the square and north gate. Keep the rest.",true)]
        [TestCase("Set Ignat's goal to guard the gate.",true)]
        [TestCase("Update the guard's goal: patrol the square.",true)]
        [TestCase("Измени его цель: охранять площадь; затем создай второго NPC.",false)]
        [TestCase("Измени его цель: охранять площадь и удали старые деревья.",false)]
        [TestCase("Измени его цель: охранять площадь и перекрась его материал.",false)]
        [TestCase("Измени его цель: охранять площадь и перемести его к воротам.",false)]
        [TestCase("Измени его цель: охранять площадь, настрой NavMesh и маршрут.",false)]
        [TestCase("Измени его цель: охранять площадь и установи расписание.",false)]
        [TestCase("Change his goal to guard the gate and create another NPC.",false)]
        [TestCase("Set his goal to patrol and attach a bob behavior.",false)]
        [TestCase("Set his goal to patrol and move him to the gate.",false)]
        [TestCase("Set his goal to patrol and paint his material red.",false)]
        [TestCase("Измени цель Игната на охранять площадь. Также напиши C# скрипт.",false)]
        [TestCase("Измени цель Игната на охранять площадь. Затем сохрани сцену.",false)]
        [TestCase("Set his goal to guard the gate, then write a C# script.",false)]
        [TestCase("Измени цель Игната на охранять площадь и прочитай навык unity-architecture.",false)]
        [TestCase("Set his goal to guard the gate and list agent skills.",false)]
        [TestCase("Set his goal to guard the gate and capture a screenshot.",false)]
        [TestCase("Не изменяй его цель: только объясни текущее поведение.",false)]
        [TestCase("Как изменить цель NPC?",false)]
        [TestCase("Цель стража — охранять площадь.",false)]
        [TestCase("Измени его цель: ",false)]
        [TestCase(null,false)]
        public void GoalOnlyRoutingRequiresAnExplicitPropertyEditAndRejectsCompounds(string request,bool expected)
        {Assert.That((bool)Invoke("GoalOnlyNpcUpdate",request),Is.EqualTo(expected));}
        [TestCase(ActualGoalRequest,AssistantMode.Execute,false)]
        [TestCase(ActualGoalRequest,AssistantMode.Plan,true)]
        [TestCase("Change his goal to patrol the square and north gate. Preserve the rest.",AssistantMode.Execute,true)]
        [TestCase("Set the guard's goal to patrol the square.",AssistantMode.Plan,false)]
        public void ActualGoalRequestEnvelopeOffersOnlyReadsProfileUpdateAndDesignMemory(string input,AssistantMode mode,bool cloud)
        {
            var request=RequestEnvelope(input,mode,cloud);
            Assert.That(ExposedTools(request),Is.EquivalentTo(new[]{"find_scene_objects","inspect_object","inspect_project","update_npc","remember_design"}));
            var args=Arguments(request.Schema,"update_npc");Assert.That(((JObject)args["properties"]).Properties().Select(p=>p.Name),Is.EqualTo(new[]{"goals"}));Assert.That(args["required"].Values<string>(),Is.EqualTo(new[]{"goals"}));Assert.That((bool)args["additionalProperties"],Is.False);
            Assert.That(((JObject)Arguments(EditorPlan.Schema(true,new[]{"update_npc"}),"update_npc")["properties"]).Property("role"),Is.Not.Null,"A task-specific scope must not alter the shared NPC schema.");
            Assert.That(request.Messages.Any(m=>m.Content.Contains(input)),Is.True,"Narrowing must preserve the actual request.");
            Assert.That(scene.rootCount,Is.Zero);
            Assert.That(ExposedTools(RequestEnvelope(input,AssistantMode.Ask,cloud)).All(SceneToolExecutor.IsReadOnlyTool),Is.True);
        }
        [TestCase("Измени его цель: охранять площадь и перекрась его материал.","set_material")]
        [TestCase("Измени его цель: охранять площадь и удали старые деревья.","delete_object")]
        [TestCase("Set his goal to patrol and attach a bob behavior.","attach_behavior")]
        [TestCase("Измени цель Игната на охранять площадь. Также напиши C# скрипт.","draft_script")]
        [TestCase("Измени цель Игната на охранять площадь. Затем сохрани сцену.","save_scene")]
        [TestCase("Set his goal to guard the gate, then write a C# script.","draft_script")]
        [TestCase("Измени цель Игната на охранять площадь и прочитай навык unity-architecture.","read_skill")]
        [TestCase("Set his goal to guard the gate and list agent skills.","list_skills")]
        [TestCase("Set his goal to guard the gate and capture a screenshot.","capture_scene")]
        public void CompoundGoalEditsRetainTheirAdditionalRequestedTools(string input,string extraTool)
        {Assert.That(ExposedTools(RequestEnvelope(input)),Does.Contain("update_npc").And.Contain(extraTool));}
        [Test] public void UnknownPhrasingRetainsOrdinaryExpandedToolFlow()
        {
            const string input="Помоги развить ощущение жизни в сцене.";
            Assert.That((bool)Invoke("GoalOnlyNpcUpdate",input),Is.False);
            Assert.That(ExposedTools(RequestEnvelope(input)),Does.Contain("create_world").And.Contain("update_npc").And.Contain("attach_behavior").And.Contain("draft_script"));
        }
        [TestCase(ActualNpcRequest)]
        [TestCase("Create NPC Ignat: role guard, cautious personality. Give him knowledge that the north gate opens at six.")]
        public void NpcProfileCreationDoesNotOfferUnrequestedPhysicalBehavior(string input)
        {
            var tools=ExposedTools(RequestEnvelope(input));Assert.That(tools,Does.Contain("create_npc").And.Contain("update_npc"));
            Assert.That(tools,Does.Not.Contain("attach_behavior").And.Not.Contain("set_navigation"));
        }
        [TestCase("Создай NPC и добавь ему патруль по площади.")]
        [TestCase("Создай персонажа с анимацией покачивания.")]
        [TestCase("Create NPC and attach a bob behavior.")]
        [TestCase("Create a guard and make him follow the player.")]
        [TestCase("Create NPC that walks between the square and the gate.")]
        [TestCase("Создай NPC с маршрутом по площади.")]
        public void ExplicitNpcMovementKeepsPhysicalBehaviorTools(string input)
        {Assert.That(ExposedTools(RequestEnvelope(input)),Does.Contain("attach_behavior").And.Contain("set_navigation"));}
        [UnityTest] public IEnumerator GoalOnlyControllerUpdatePreservesNpcProfileKnowledgeInventoryAndBehavior()
        {
            var provider=new Provider{Plan=new EditorPlan{complete=true,actions=new List<EditorCommand>{
                new EditorCommand{tool="create_npc",name="GoalOnlyGuard",args=new JObject{["role"]="Страж",["personality"]="Осторожный",["biography"]="Живёт у моста",["constraints"]="Не брать чужие вещи",["goals"]="Охранять мост",["knowledge"]="Северные ворота открываются в шесть утра",["inventory"]=new JObject{["Crystal"]=3},["autonomous"]=false}},
                new EditorCommand{tool="attach_behavior",target="@GoalOnlyGuard",args=new JObject{["behavior"]="bob",["amplitude"]=.7,["frequency"]=2.5,["speed"]=1.25}}
            }}};
            var controller=Controller(provider);yield return Finish(controller.SendAsync("Создай NPC GoalOnlyGuard с поведением bob."));
            var npc=ProjectContextIndex.Objects().Single(go=>go.name=="GoalOnlyGuard").GetComponent<IntelligentNPC>();
            var knowledge=npc.Knowledge;string knowledgeBefore=EditorJsonUtility.ToJson(knowledge);var models=npc.Models;
            var inventory=npc.GetComponent<Inventory>();string inventoryBefore=EditorJsonUtility.ToJson(inventory);
            var behavior=npc.GetComponent<SceneBehavior>();string behaviorBefore=EditorJsonUtility.ToJson(behavior);var position=npc.transform.position;
            var known=new GameObject("KnownGoalEntity").AddComponent<WorldEntity>();known.Id="KnownGoalEntity";npc.KnownEntities.Clear();npc.KnownEntities.Add(known);var configured=npc.KnownEntities;
            npc.RememberEvent("Получил приказ у моста");string memoryBefore=JsonConvert.SerializeObject(npc.ExportMemory());
            int objectsBefore=ProjectContextIndex.Objects().Count();
            provider.Plan=new EditorPlan{reply="Обновлена только цель",complete=true,actions=new List<EditorCommand>{new EditorCommand{tool="update_npc",target=ProjectContextIndex.StableTarget(npc.gameObject),args=new JObject{["goals"]="Патрулировать площадь и северные ворота"}}}};
            yield return Finish(controller.SendAsync(ActualGoalRequest));var answer=controller.Current.Messages.Last();
            Assert.That(answer.IsError,Is.False,answer.Text);Assert.That(answer.Steps.Single().Tool,Is.EqualTo("update_npc"));
            Assert.That(ExposedTools(provider.LastRequest),Is.EquivalentTo(new[]{"find_scene_objects","inspect_object","inspect_project","update_npc","remember_design"}));
            Assert.That(npc.Goals,Is.EqualTo("Патрулировать площадь и северные ворота"));Assert.That(npc.Role,Is.EqualTo("Страж"));Assert.That(npc.Personality,Is.EqualTo("Осторожный"));
            Assert.That(npc.Biography,Is.EqualTo("Живёт у моста"));Assert.That(npc.Constraints,Is.EqualTo("Не брать чужие вещи"));Assert.That(npc.Autonomous,Is.False);
            Assert.That(npc.Knowledge,Is.SameAs(knowledge));Assert.That(EditorJsonUtility.ToJson(knowledge),Is.EqualTo(knowledgeBefore));Assert.That(npc.Models,Is.SameAs(models));
            Assert.That(npc.GetComponent<Inventory>(),Is.SameAs(inventory));Assert.That(EditorJsonUtility.ToJson(inventory),Is.EqualTo(inventoryBefore));
            Assert.That(npc.GetComponent<SceneBehavior>(),Is.SameAs(behavior));Assert.That(EditorJsonUtility.ToJson(behavior),Is.EqualTo(behaviorBefore));Assert.That(npc.transform.position,Is.EqualTo(position));
            Assert.That(npc.KnownEntities,Is.SameAs(configured));Assert.That(npc.KnownEntities,Is.EqualTo(new[]{known}));Assert.That(JsonConvert.SerializeObject(npc.ExportMemory()),Is.EqualTo(memoryBefore));
            Assert.That(ProjectContextIndex.Objects().Count(),Is.EqualTo(objectsBefore));
        }
        [UnityTest] public IEnumerator GoalOnlyHostRejectsAnotherProfileFieldBeforeChangingTheNpc()
        {
            var go=new GameObject("ExistingScopedGuard");go.AddComponent<GeneratedSceneObject>().Kind="npc";var npc=go.AddComponent<IntelligentNPC>();npc.Role="Страж";npc.Personality="Осторожный";npc.Goals="Охранять мост";
            var provider=new Provider{Plan=new EditorPlan{complete=true,actions=new List<EditorCommand>{new EditorCommand{tool="update_npc",target=ProjectContextIndex.StableTarget(go),args=new JObject{["goals"]="Патрулировать площадь",["role"]=""}}}}};
            var controller=Controller(provider);yield return Finish(controller.SendAsync(ActualGoalRequest));var answer=controller.Current.Messages.Last();
            Assert.That(answer.IsError,Is.True);Assert.That(answer.Attempts.Single().Accepted,Is.False);StringAssert.Contains("только",answer.Attempts.Single().Error);Assert.That(answer.Steps,Is.Empty);Assert.That(answer.Task.CheckpointId,Is.Null.Or.Empty);
            Assert.That(npc.Role,Is.EqualTo("Страж"));Assert.That(npc.Personality,Is.EqualTo("Осторожный"));Assert.That(npc.Goals,Is.EqualTo("Охранять мост"));
        }
        [UnityTest] public IEnumerator ActualThreeLightRequestCanApplyAllThreeLightsInOneBatch()
        {
            FlatTerrain();
            var plan=new EditorPlan{reply="Три точечных источника",complete=true};
            for(int i=0;i<3;i++)plan.actions.Add(new EditorCommand{tool="set_lighting",name="Point_"+i,args=new JObject{["type"]="point",["position"]=new JArray(i*5,2,0),["color"]="#FF8800"}});
            var provider=new Provider{Plan=plan};var controller=Controller(provider);yield return Finish(controller.SendAsync(ActualLightRequest));var answer=controller.Current.Messages.Last();
            Assert.That(provider.Calls,Is.EqualTo(1));Assert.That((int?)provider.LastRequest.Schema["properties"]["actions"]["maxItems"],Is.Not.EqualTo(1));
            Assert.That(answer.Steps.Count(step=>step.Tool=="set_lighting"&&step.Success),Is.EqualTo(3));Assert.That(answer.IsError,Is.False,answer.Text);
            Assert.That(ProjectContextIndex.Objects().Count(go=>go.GetComponent<Light>()&&go.GetComponent<Light>().type==LightType.Point),Is.EqualTo(3));
            Assert.That(answer.Task.Requirements.Single(r=>r.Kind=="count"&&r.Expected=="point_light").Number,Is.EqualTo(3));Assert.That(answer.Task.Checks.All(check=>check.Passed),Is.True);
            Assert.That(ProjectContextIndex.Objects().Count(go=>go.GetComponent<GeneratedTerrain>()),Is.EqualTo(1),"The request must reuse the existing terrain.");
        }
        [TestCase(ActualLightRequest,true)]
        [TestCase("Add 3 new Point Light in the valley.",true)]
        [TestCase("Add three new orange point lights in the valley.",true)]
        [TestCase("Create 3 Point Light in the terrain.",true)]
        [TestCase("Создай в долине 3 точечных источника света под землёй.",false)]
        [TestCase("Создай в долине 3 точечных источника света в точках (0, 2, 0), (5, 2, 0), (10, 2, 0).",false)]
        [TestCase("Создай в долине 3 точечных источника света: on_terrain=false.",false)]
        [TestCase("Создай в долине 3 точечных источника света без привязки к рельефу.",false)]
        [TestCase("Create 3 Point Light in the world underground.",false)]
        [TestCase("Create 3 Point Light in the world underwater.",false)]
        [TestCase("Создай в долине 3 точечных источника света под водой.",false)]
        [TestCase("Создай в долине 3 точечных источника света на глубине 4 метра.",false)]
        [TestCase("Create 3 Point Light in the world at position [0, 2, 0].",false)]
        [TestCase("Add 3 new Point Light in the valley at x=10, y=2, z=20.",false)]
        [TestCase("Add 3 new Point Light in the valley floating above ground.",false)]
        [TestCase("Add 3 new Point Light in the valley and reduce the existing light's intensity.",false)]
        [TestCase("Создай в долине 3 точечных источника света, измени цвет старого света.",false)]
        [TestCase("Создай 3 точечных источника света.",false)]
        [TestCase("Сохрани в долине 3 точечных источника света.",false)]
        public void GroundedPointPolicyOnlyAppliesToClearNewTerrainLightsWithoutPlacementOverride(string input,bool expected)
        {
            var task=AgentTaskVerifier.Create(input,true);
            Assert.That((bool)Invoke("GroundedPointLights",task),Is.EqualTo(expected));
            Assert.That(task.Requirements.Any(r=>r.Kind=="point_light_placement"),Is.EqualTo(expected));
            var schema=EditorPlan.Schema(true,new[]{"set_lighting"});Invoke("Bind",schema,task);var args=Arguments(schema,"set_lighting");
            if(expected){Assert.That(args["properties"]["on_terrain"]["enum"].Values<bool>(),Is.EqualTo(new[]{true}));Assert.That(args["required"].Values<string>(),Does.Contain("on_terrain"));}
            else Assert.That(args["properties"]["on_terrain"]["enum"],Is.Null);
        }
        [TestCase(true)] [TestCase(false)]
        public void GroundedPointRequestRequiresAlignmentInActualLocalAndCloudRequest(bool cloud)
        {
            var envelope=RequestEnvelope(ActualLightRequest,cloud:cloud);var args=Arguments(envelope.Schema,"set_lighting");
            Assert.That(args["properties"]["on_terrain"]["enum"].Values<bool>(),Is.EqualTo(new[]{true}));Assert.That(args["required"].Values<string>(),Does.Contain("on_terrain"));
            Assert.That(ExistingLightArguments(envelope.Schema)["properties"]["on_terrain"]["enum"],Is.Null);Assert.That(ExistingLightArguments(envelope.Schema)["required"].Values<string>(),Does.Not.Contain("on_terrain"));
            Assert.That(envelope.Messages.Any(m=>m.Content.Contains(ActualLightRequest)),Is.True);
            Assert.That(Arguments(EditorPlan.Schema(true,new[]{"set_lighting"}),"set_lighting")["properties"]["on_terrain"]["enum"],Is.Null,"The shared schema must remain uncontaminated.");
        }
        [Test] public void GroundedCreationPolicyDoesNotOverrideExplicitAlignmentOnAnExistingLight()
        {
            FlatTerrain();var old=new GameObject("ExistingExactPoint");old.AddComponent<GeneratedSceneObject>().Kind="light";old.AddComponent<Light>().type=LightType.Point;old.transform.position=new Vector3(5,2,5);
            var task=AgentTaskVerifier.Create(ActualLightRequest,true);var plan=new EditorPlan{actions=new List<EditorCommand>{new EditorCommand{tool="set_lighting",target=ProjectContextIndex.StableTarget(old),args=new JObject{["type"]="point",["intensity"]=2,["on_terrain"]=false}}}};
            Assert.DoesNotThrow(()=>Invoke("Validate",task,plan));var result=SceneToolExecutor.Execute(plan);Assert.That(result.Success,Is.True,result.Error);Assert.That(old.transform.position,Is.EqualTo(new Vector3(5,2,5)));
        }
        [UnityTest] public IEnumerator HostRejectsUnrequestedFalseTerrainOverrideBeforeAnyNewLightMutation()
        {
            FlatTerrain();var provider=new Provider{Plan=new EditorPlan{complete=true,actions=new List<EditorCommand>{new EditorCommand{tool="set_lighting",name="Buried",args=new JObject{["type"]="point",["position"]=new JArray(5,2,5),["on_terrain"]=false}}}}};
            var controller=Controller(provider);yield return Finish(controller.SendAsync(ActualLightRequest));var answer=controller.Current.Messages.Last();
            Assert.That(provider.Calls,Is.EqualTo(1));Assert.That(answer.IsError,Is.True);Assert.That(answer.Attempts.Single().Accepted,Is.False);StringAssert.Contains("on_terrain=true",answer.Attempts.Single().Error);
            Assert.That(ProjectContextIndex.Objects().Any(go=>go.GetComponent<Light>()),Is.False);Assert.That(answer.Steps,Is.Empty);Assert.That(answer.Task.CheckpointId,Is.Null.Or.Empty);
        }
        [TestCase("buried")] [TestCase("out-of-bounds")] [TestCase("short-range")] [TestCase("no-terrain")]
        public void CountAloneCannotVerifyBuriedOutOfBoundsOrIneffectiveNewLights(string defect)
        {
            if(defect!="no-terrain")FlatTerrain();var task=AgentTaskVerifier.Create(ActualLightRequest,true);
            for(int i=0;i<3;i++)
            {
                var go=new GameObject("DefectivePoint_"+i);go.AddComponent<GeneratedSceneObject>().Kind="light";var light=go.AddComponent<Light>();light.type=LightType.Point;light.range=defect=="short-range"?1:20;
                go.transform.position=new Vector3(defect=="out-of-bounds"?200+i:i*5,defect=="buried"?2:20,5);
            }
            AgentTaskVerifier.Verify(task);var count=task.Requirements.Single(r=>r.Kind=="count");var placement=task.Requirements.Single(r=>r.Kind=="point_light_placement");
            Assert.That(task.Checks.Single(c=>c.RequirementId==count.Id).Passed,Is.True,"The regression must contain exactly three new Point lights.");
            var failed=task.Checks.Single(c=>c.RequirementId==placement.Id);Assert.That(failed.Passed,Is.False);StringAssert.Contains("TerrainData",failed.Evidence);StringAssert.Contains("DefectivePoint_0",failed.Evidence);
        }
        [Test] public void DefaultPlacementLiftsAboveWaterAndIgnoresAnUnchangedBadBaselineLight()
        {
            FlatTerrain();var old=new GameObject("ExistingBuriedLight");old.AddComponent<GeneratedSceneObject>().Kind="light";old.AddComponent<Light>().type=LightType.Point;old.transform.position=new Vector3(4,-20,4);
            var task=AgentTaskVerifier.Create(ActualLightRequest,true);var plan=new EditorPlan{actions=new List<EditorCommand>()};
            for(int i=0;i<3;i++)plan.actions.Add(new EditorCommand{tool="set_lighting",name="LiftedPoint_"+i,args=new JObject{["type"]="point",["position"]=new JArray(i*5,2,5),["range"]=10}});
            Invoke("Validate",task,plan);var result=SceneToolExecutor.Execute(plan);Assert.That(result.Success,Is.True,result.Error);
            Assert.That(AgentTaskVerifier.Verify(task).All(c=>c.Passed),Is.True);Assert.That(old.transform.position.y,Is.EqualTo(-20));
            foreach(var light in ProjectContextIndex.Objects().Where(go=>go.name.StartsWith("LiftedPoint_")))Assert.That(light.transform.position.y,Is.GreaterThanOrEqualTo(17.39f));
            string evidence=task.Checks.Single(c=>c.RequirementId==task.Requirements.Single(r=>r.Kind=="point_light_placement").Id).Evidence;
            StringAssert.DoesNotContain("ExistingBuriedLight",evidence);
        }
        [TestCase("Создай в долине 3 точечных источника света под землёй.")]
        [TestCase("Создай в долине 3 точечных источника света в точках (0, 2, 0), (5, 2, 0), (10, 2, 0).")]
        public void ExplicitUserPlacementRetainsExactFalseAlignmentContract(string input)
        {
            FlatTerrain();var task=AgentTaskVerifier.Create(input,true);var plan=new EditorPlan{actions=new List<EditorCommand>()};
            for(int i=0;i<3;i++)plan.actions.Add(new EditorCommand{tool="set_lighting",name="ExplicitPoint_"+i,args=new JObject{["type"]="point",["position"]=new JArray(i*5,2,0),["on_terrain"]=false}});
            Assert.DoesNotThrow(()=>Invoke("Validate",task,plan));var result=SceneToolExecutor.Execute(plan);Assert.That(result.Success,Is.True,result.Error);
            Assert.That(task.Requirements.Any(r=>r.Kind=="point_light_placement"),Is.False);
            foreach(var light in ProjectContextIndex.Objects().Where(go=>go.name.StartsWith("ExplicitPoint_")))Assert.That(light.transform.position.y,Is.EqualTo(2));
        }
        [Test] public void DecoderHintUsesTheSpecializedRequiredOrderAndOnlySelectedTools()
        {
            var schema = EditorPlan.Schema(true, new[] { "create_world" }); Invoke("Bind", schema, AgentTaskVerifier.Create(Request, true));
            string hint = (string)Invoke("KeyOrderHint", schema);
            StringAssert.Contains("create_world: width,length,height,trees,buildings,rocks,style", hint);
            StringAssert.DoesNotContain("scatter_objects:", hint); StringAssert.DoesNotContain("create_npc:", hint);
        }
        [TestCase("trees", 300)] [TestCase("buildings", 50)] [TestCase("rocks", 200)] [TestCase("width", 256)]
        public void HostRejectsModelValuesThatDisagreeWithOriginalLiterals(string field, int wrong)
        {
            var task = AgentTaskVerifier.Create(Request, true); var args = WorldArgs(); args[field] = wrong;
            Assert.Throws<ArgumentException>(() => Invoke("Validate", task, WorldPlan(args)));
            Assert.That(scene.rootCount, Is.Zero);
        }
        [Test] public void HostRequiresBoundFieldsAndAcceptsTheExactMacroWithoutExtraDecoration()
        {
            var task = AgentTaskVerifier.Create(Request, true); var args = WorldArgs(); args.Remove("trees");
            Assert.Throws<ArgumentException>(() => Invoke("Validate", task, WorldPlan(args)));
            Assert.DoesNotThrow(() => Invoke("Validate", task, WorldPlan())); Assert.That(scene.rootCount, Is.Zero);
        }
        [TestCase("scatter_objects")] [TestCase("create_object")]
        public void HostRejectsDuplicatingLiteralDecorationsInTheSameWorldPlan(string tool)
        {
            var plan = WorldPlan(); plan.actions.Add(new EditorCommand { tool = tool, name = "DuplicateTree", target = "@BoundWorld", args = new JObject { ["kind"] = "tree", ["count"] = 12 } });
            Assert.Throws<ArgumentException>(() => Invoke("Validate", AgentTaskVerifier.Create(Request, true), plan));
            Assert.That(scene.rootCount, Is.Zero);
        }
        [TestCase("Создай 12 деревьев, 3 дома и 4 камня")]
        [TestCase("Измени существующий мир: добавь 3 дерева")]
        public void NonWorldCreationRequestsDoNotAcquireInventedWorldConstraints(string request)
        {
            var schema = EditorPlan.Schema(true); var before = schema.DeepClone();
            Invoke("Bind", schema, AgentTaskVerifier.Create(request, true)); Assert.That(JToken.DeepEquals(schema, before), Is.True);
        }
        [Test] public void AmbiguousCountsAreNotBoundToAnArbitraryFirstNumber()
        {
            var task = AgentTaskVerifier.Create("Создай мир 160 на 160 метров: 5 деревьев на юге, 3 дерева на севере", true);
            Assert.That(task.OpenQuestions, Is.Not.Empty);
            var schema = EditorPlan.Schema(true, new[] { "create_world" }); var original = schema.DeepClone(); Invoke("Bind", schema, task);
            Assert.That(JToken.DeepEquals(schema, original), Is.True);
            Assert.That(AgentTaskVerifier.Verify(task).Single(check => check.RequirementId == "ambiguous").Passed, Is.False);
        }
        [UnityTest] public IEnumerator OutOfRangeLiteralsFailBeforeInferenceAndBeforeAnyMutation()
        {
            foreach (string request in new[] { "Создай мир 160 на 160 метров с 1000 деревьев", "Создай мир 4096 на 160 метров" })
            {
                var provider = new Provider(); var controller = Controller(provider);
                yield return Finish(controller.SendAsync(request)); var answer = controller.Current.Messages.Last();
                Assert.That(provider.Calls, Is.Zero); Assert.That(answer.IsError, Is.True); Assert.That(answer.Attempts, Is.Empty);
                Assert.That(answer.Steps, Is.Empty); Assert.That(answer.Task.CheckpointId, Is.Null.Or.Empty); Assert.That(scene.rootCount, Is.Zero);
            }
        }
        [UnityTest] public IEnumerator AskNeverExposesWorldMutationEvenWhenTheRequestContainsWorldLiterals()
        {
            var provider = new Provider { Plan = WorldPlan() }; var controller = Controller(provider, AssistantMode.Ask);
            yield return Finish(controller.SendAsync(Request)); var answer = controller.Current.Messages.Last();
            var exposed = ((JArray)provider.LastRequest.Schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName).ToArray();
            Assert.That(exposed, Does.Not.Contain("create_world")); Assert.That(exposed.All(SceneToolExecutor.IsReadOnlyTool), Is.True);
            Assert.That(answer.IsError, Is.True); Assert.That(answer.Task.MutationAuthorized, Is.False);
            Assert.That(answer.PendingPlan, Is.Null); Assert.That(answer.Steps, Is.Empty); Assert.That(scene.rootCount, Is.Zero);
        }
        [UnityTest] public IEnumerator CloudHostRejectsRepeatedWorldDecorationBeforeExecutorCommit()
        {
            var plan = WorldPlan(); plan.actions.Add(new EditorCommand { tool = "scatter_objects", name = "DuplicateForest", target = "@BoundWorld", args = new JObject { ["kind"] = "tree", ["count"] = 12 } });
            var provider = new Provider { Plan = plan }; var controller = Controller(provider, cloud: true);
            yield return Finish(controller.SendAsync(Request)); var answer = controller.Current.Messages.Last();
            Assert.That(provider.Calls, Is.EqualTo(1)); Assert.That(provider.LastRequest.Stream, Is.False);
            Assert.That(answer.IsError, Is.True); Assert.That(answer.Attempts.Single().Accepted, Is.False);
            StringAssert.Contains("повторное", answer.Attempts.Single().Error);
            Assert.That(answer.Steps, Is.Empty); Assert.That(answer.Task.CheckpointId, Is.Null.Or.Empty); Assert.That(scene.rootCount, Is.Zero);
        }
        [UnityTest] public IEnumerator WorldRequestSchemaOmitsGenericDecorationToolsUntilExplicitExpansion()
        {
            var provider = new Provider(); var controller = Controller(provider);
            yield return Finish(controller.SendAsync(Request));
            var exposed = ((JArray)provider.LastRequest.Schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName).ToArray();
            Assert.That(exposed, Does.Contain("create_world")); Assert.That(exposed, Does.Not.Contain("create_object")); Assert.That(exposed, Does.Not.Contain("scatter_objects"));
            Assert.That(scene.rootCount, Is.Zero);
        }
    }
}
