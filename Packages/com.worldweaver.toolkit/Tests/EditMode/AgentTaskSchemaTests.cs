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
        const string ActualLightRequest="Создай в долине ровно три новых оранжевых точечных источника света типа Point в разных местах около площади. Существующее солнце сохрани.";
        readonly List<ChatController> controllers = new List<ChatController>();
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
        static JObject Arguments(JObject schema, string tool = "create_world") => (JObject)((JArray)schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Single(branch => EditorToolSchema.ToolName(branch) == tool)["properties"]["args"];
        static JObject WorldArgs() => new JObject { ["width"] = 160, ["length"] = 160, ["height"] = 24, ["trees"] = 12, ["buildings"] = 3, ["rocks"] = 4, ["resolution"] = 33 };
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
        [UnityTest] public IEnumerator ActualThreeLightRequestCanApplyAllThreeLightsInOneBatch()
        {
            var plan=new EditorPlan{reply="Три точечных источника",complete=true};
            for(int i=0;i<3;i++)plan.actions.Add(new EditorCommand{tool="set_lighting",name="Point_"+i,args=new JObject{["type"]="point",["position"]=new JArray(i*5,2,0),["color"]="#FF8800"}});
            var provider=new Provider{Plan=plan};var controller=Controller(provider);yield return Finish(controller.SendAsync(ActualLightRequest));var answer=controller.Current.Messages.Last();
            Assert.That(provider.Calls,Is.EqualTo(1));Assert.That((int?)provider.LastRequest.Schema["properties"]["actions"]["maxItems"],Is.Not.EqualTo(1));
            Assert.That(answer.Steps.Count(step=>step.Tool=="set_lighting"&&step.Success),Is.EqualTo(3));Assert.That(answer.IsError,Is.False,answer.Text);
            Assert.That(ProjectContextIndex.Objects().Count(go=>go.GetComponent<Light>()&&go.GetComponent<Light>().type==LightType.Point),Is.EqualTo(3));
            Assert.That(answer.Task.Requirements.Single(r=>r.Expected=="point_light").Number,Is.EqualTo(3));Assert.That(answer.Task.Checks.All(check=>check.Passed),Is.True);
            Assert.That(ProjectContextIndex.Objects().Any(go=>go.GetComponent<GeneratedTerrain>()||go.GetComponent<GeneratedSceneObject>()?.Kind=="world"),Is.False);
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
