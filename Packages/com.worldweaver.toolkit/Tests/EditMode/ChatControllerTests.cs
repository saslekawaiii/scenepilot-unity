using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// <summary>Exercise the real controller/executor with injected inference. No external network or scripted success fallback.</summary>
    public sealed class ChatControllerTests
    {
        private Scene testScene,previous;
        private string folder;
        private readonly List<ChatController> controllers=new List<ChatController>();
        private readonly HashSet<string> assets=new HashSet<string>();
        private sealed class RecordingProvider : ILLMProvider
        {
            internal readonly List<string[]> Requests=new List<string[]>();
            internal Func<int,CompletionRequest,CancellationToken,Task<string>> Respond;
            public async Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                int index=Requests.Count;Requests.Add(request.Messages.Select(m=>m.Role+": "+m.Content).ToArray());
                string text=await Respond(index,request,token);token.ThrowIfCancellationRequested();
                return new CompletionResult{Text=text,Provider="InjectedTest",Model="Recorded",InputTokens=13,OutputTokens=7};
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);
            public void Dispose(){ }
        }
        [SetUp] public void SetUp()
        {
            testScene=default;previous=SceneManager.GetActiveScene();
            bool untitled=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s=>s.isLoaded&&string.IsNullOrEmpty(s.path));
            if(untitled&&!Application.isBatchMode)Assert.Ignore("Перед GUI-запуском этих тестов сохраните открытые сцены: Unity запрещает additive-сцену рядом с несохранённой.");
            testScene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,untitled?NewSceneMode.Single:NewSceneMode.Additive);SceneManager.SetActiveScene(testScene);
            folder=Path.Combine(Path.GetTempPath(),"WorldWeaverChatTests_"+Guid.NewGuid().ToString("N"));
        }
        [TearDown] public void TearDown()
        {
            foreach(var controller in controllers)
            {
                foreach(var session in controller.Sessions)foreach(var step in session.Messages.SelectMany(m=>m.Steps))foreach(string path in step.CreatedAssets)assets.Add(path);
                controller.Dispose();
            }
            controllers.Clear();
            if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
            if(testScene.IsValid()&&testScene.isLoaded){if(previous.IsValid()&&previous.isLoaded)EditorSceneManager.CloseScene(testScene,true);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            foreach(string asset in assets.OrderByDescending(a=>a.Length))if(AssetDatabase.LoadMainAssetAtPath(asset))AssetDatabase.DeleteAsset(asset);
            foreach(string path in assets.Select(p=>Path.GetDirectoryName(p)?.Replace('\\','/')).Where(p=>p!=null).Distinct().OrderByDescending(p=>p.Length))
                if(AssetDatabase.IsValidFolder(path)&&AssetDatabase.FindAssets("",new[]{path}).Length==0)AssetDatabase.DeleteAsset(path);
            assets.Clear();if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
        private ChatController Controller(RecordingProvider provider)
        {var controller=new ChatController(_=>provider,folder);controllers.Add(controller);return controller;}
        private GameObject Find(string name)=>testScene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject).FirstOrDefault(o=>o.name==name);
        private static string Reply(bool complete,params EditorCommand[] commands)=>JsonConvert.SerializeObject(new EditorPlan{reply="Проверяемый план",complete=complete,actions=commands.ToList()});
        private static EditorCommand Create(string name)=>new EditorCommand{tool="create_object",name=name,args=new JObject{["kind"]="cube",["on_terrain"]=false}};
        private static EditorCommand MaterialSearch()=>new EditorCommand {tool="material_search",args=new JObject {["query"]="wood",["source"]="builtin",["limit"]=2}};
        private static EditorCommand MaterialImport()=>new EditorCommand {tool="material_import",args=new JObject {["material_id"]="builtin:wood",["allow_online"]=false}};
        private static EditorCommand MaterialApply(string target)=>new EditorCommand {tool="material_apply",target=target,args=new JObject {["material_id"]="builtin:wood"}};
        private static EditorCommand Draft(string name,string code=null)=>new EditorCommand{tool="draft_script",name=name,args=new JObject{["code"]=code??("using UnityEngine; public class "+name+" : MonoBehaviour { void Update() { transform.Rotate(Vector3.up,30f*Time.deltaTime,Space.Self); } }"),["description"]="Код для просмотра"}};
        private static IEnumerator Finish(Task task)
        {
            float until=Time.realtimeSinceStartup+10;
            while(!task.IsCompleted&&Time.realtimeSinceStartup<until)yield return null;
            Assert.That(task.IsCompleted,Is.True,"Controller did not complete after injected provider result/cancellation.");
            if(task.IsFaulted)Assert.Fail(task.Exception.ToString());
        }
        [UnityTest] public IEnumerator MultiRoundRequestContainsActualSceneReadback()
        {
            var provider=new RecordingProvider();provider.Respond=(round,request,token)=>
            {
                if(round==0)return Task.FromResult(Reply(false,Create("FirstCube")));
                var created=Find("FirstCube");Assert.That(created,Is.Not.Null,"Later inference began before actual creation.");
                string observation=request.Messages.Last().Content;StringAssert.Contains("FirstCube",observation);StringAssert.Contains(ProjectContextIndex.StableTarget(created),observation);
                if(round==1)return Task.FromResult(Reply(true,new EditorCommand{tool="set_transform",target="id:"+created.GetInstanceID(),args=new JObject{["position"]=new JArray(4,2,8)}}));
                Assert.That(round,Is.EqualTo(2));Assert.That(created.transform.position,Is.EqualTo(new Vector3(4,2,8)));
                return Task.FromResult(Reply(true));
            };
            var controller=Controller(provider);controller.Settings.Power=AssistantPower.Balanced;
            yield return Finish(controller.SendAsync("Создай куб и затем переставь его."));
            var answer=controller.Current.Messages.Last();Assert.That(answer.IsError,Is.False,answer.Text);Assert.That(answer.Status,Is.EqualTo("Изменения применены"));Assert.That(provider.Requests.Count,Is.EqualTo(3));Assert.That(answer.Steps.Count(s=>s.Success),Is.EqualTo(2));
            Assert.That(Find("FirstCube").transform.position,Is.EqualTo(new Vector3(4,2,8)));Assert.That(answer.InputTokens,Is.EqualTo(39));Assert.That(answer.OutputTokens,Is.EqualTo(21));
        }
        [UnityTest] public IEnumerator RejectedPlanHasNoMutationAndProviderCanRepairIt()
        {
            var provider=new RecordingProvider();provider.Respond=(round,request,token)=>
            {
                if(round==0)return Task.FromResult(Reply(true,Create("MustNotExist"),new EditorCommand{tool="create_world",name="Invalid",args=new JObject{["unknown_argument"]=1}}));
                Assert.That(Find("MustNotExist"),Is.Null);
                if(round==1)
                {
                    Assert.That(testScene.rootCount,Is.Zero);StringAssert.Contains("План отклонён",request.Messages.Last().Content);
                    return Task.FromResult(Reply(true,Create("RepairedCube")));
                }
                Assert.That(round,Is.EqualTo(2));var created=Find("RepairedCube");Assert.That(created,Is.Not.Null);
                StringAssert.Contains(ProjectContextIndex.StableTarget(created),request.Messages.Last().Content);
                return Task.FromResult(Reply(true));
            };
            var controller=Controller(provider);controller.Settings.Power=AssistantPower.Balanced;yield return Finish(controller.SendAsync("Создай объект."));
            var answer=controller.Current.Messages.Last();Assert.That(answer.IsError,Is.False,answer.Text);Assert.That(answer.Status,Is.EqualTo("Изменения применены"));Assert.That(provider.Requests.Count,Is.EqualTo(3));Assert.That(Find("MustNotExist"),Is.Null);Assert.That(Find("RepairedCube"),Is.Not.Null);Assert.That(answer.Steps.Count(s=>s.Success),Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator MixedCodePlanRepairsIntoReviewOnlyWithoutExecutingOrReanchoringRejectedActions()
        {
            const string className="MixedReviewOnly";
            string source="using UnityEngine; public class "+className+" : MonoBehaviour { void Update() { if(Input.GetKeyDown(KeyCode.F)) enabled=false; } }";
            var provider=new RecordingProvider();provider.Respond=(round,request,token)=>
            {
                if(round==0)return Task.FromResult(Reply(false,Create("RejectedMixedCube"),Draft(className,source),new EditorCommand {tool="attach_script",name=className,target="@RejectedMixedCube",args=new JObject()}));
                Assert.AreEqual(1,round,"Only one actual repair completion is needed for this contract correction.");
                Assert.IsNull(Find("RejectedMixedCube"));Assert.IsFalse(File.Exists("Assets/WorldWeaverGenerated/Scripts/"+className+".cs"));
                Assert.IsTrue(request.Messages.Any(m=>m.Role=="system"&&m.Content.Contains("standalone review plan")));
                Assert.IsFalse(request.Messages.Any(m=>m.Content.Contains("RejectedMixedCube")),"The rejected mixed proposal remains in the audit trail, but must not anchor the repair prompt.");
                return Task.FromResult(Reply(false,Draft(className,source)));
            };
            var controller=Controller(provider);controller.Settings.Power=AssistantPower.Balanced;controller.Settings.AutoApply=true;
            yield return Finish(controller.SendAsync("Создай куб, который реагирует на нажатие клавиши F."));
            var answer=controller.Current.Messages.Last();Assert.IsFalse(answer.IsError,answer.Text);Assert.AreEqual(2,answer.Attempts.Count);
            Assert.IsFalse(answer.Attempts[0].Accepted);StringAssert.Contains("RejectedMixedCube",answer.Attempts[0].RawResponse);
            Assert.IsTrue(answer.Attempts[1].Accepted);Assert.AreEqual(1,answer.PendingPlan.actions.Count);Assert.AreEqual("draft_script",answer.PendingPlan.actions[0].tool);
            Assert.AreEqual(source,(string)answer.PendingPlan.actions[0].args["code"]);Assert.AreEqual(0,answer.Steps.Count);
            Assert.IsNull(Find("RejectedMixedCube"));Assert.IsFalse(File.Exists("Assets/WorldWeaverGenerated/Scripts/"+className+".cs"));
            var saved=new ChatHistoryStore(folder).Load().Single(s=>s.Id==controller.Current.Id).Messages.Last();
            Assert.AreEqual(source,(string)saved.PendingPlan.actions[0].args["code"]);
        }
        [UnityTest] public IEnumerator MixedMaterialImportRepairsIntoExactStandalonePendingPlanWithoutMutation()
        {
            var beforeAssets=AssetDatabase.GetAllAssetPaths();string raw=Reply(false,MaterialImport(),MaterialApply("@RejectedMaterialDoorAnchor"));
            var provider=new RecordingProvider();ChatController controller=null;JObject initialSchema=null;string initialSchemaJson=null;
            provider.Respond=(round,request,token)=>
            {
                if(round==0) {initialSchema=request.Schema;initialSchemaJson=initialSchema.ToString(Formatting.None);return Task.FromResult(raw);}
                Assert.AreEqual(1,round);Assert.AreEqual(0,testScene.rootCount);CollectionAssert.AreEquivalent(beforeAssets,AssetDatabase.GetAllAssetPaths());
                Assert.AreNotSame(initialSchema,request.Schema);Assert.AreEqual(initialSchemaJson,initialSchema.ToString(Formatting.None));
                var repairActions=request.Schema["properties"]["actions"];Assert.AreEqual(1,(int)repairActions["minItems"]);Assert.AreEqual(1,(int)repairActions["maxItems"]);
                CollectionAssert.AreEqual(new[]{"material_import"},((JArray)repairActions["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName));
                CollectionAssert.AreEqual(new[]{false},request.Schema["properties"]["complete"]["enum"].Values<bool>());
                Assert.IsTrue(((JArray)initialSchema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Any(branch=>EditorToolSchema.ToolName(branch)=="material_apply"));
                Assert.IsTrue(string.IsNullOrEmpty(controller.Current.Messages.Last().Task.CheckpointId));
                Assert.IsTrue(request.Messages.Any(m=>m.Role=="system"&&m.Content.Contains("standalone material-import plan")&&m.Content.Contains("EXACTLY ONE material_import")&&m.Content.Contains("builtin:wood")));
                Assert.IsFalse(request.Messages.Any(m=>m.Content.Contains("RejectedMaterialDoorAnchor")),"Rejected mixed actions must remain only in audit, not anchor the repair request.");
                return Task.FromResult(Reply(false,MaterialImport()));
            };
            controller=Controller(provider);controller.Settings.Mode=AssistantMode.Plan;controller.Settings.Power=AssistantPower.Balanced;controller.Settings.AutoApply=true;
            yield return Finish(controller.SendAsync("Найди текстуру дерева и подготовь её применение к двери."));
            var answer=controller.Current.Messages.Last();Assert.IsFalse(answer.IsError,answer.Text);Assert.AreEqual(2,answer.Attempts.Count);Assert.IsFalse(answer.Attempts[0].Accepted);Assert.AreEqual(raw,answer.Attempts[0].RawResponse);Assert.IsTrue(answer.Attempts[1].Accepted);
            Assert.IsNotNull(answer.PendingPlan);Assert.IsFalse(answer.PendingPlan.complete);Assert.AreEqual(1,answer.PendingPlan.actions.Count);Assert.AreEqual("material_import",answer.PendingPlan.actions[0].tool);Assert.IsTrue(JToken.DeepEquals(MaterialImport().args,answer.PendingPlan.actions[0].args));
            Assert.IsEmpty(answer.Steps);Assert.IsTrue(string.IsNullOrEmpty(answer.Task.CheckpointId));Assert.AreEqual(0,testScene.rootCount);CollectionAssert.AreEquivalent(beforeAssets,AssetDatabase.GetAllAssetPaths());
            var stored=new ChatHistoryStore(folder).Load().Single(s=>s.Id==controller.Current.Id).Messages.Last();Assert.AreEqual(raw,stored.Attempts[0].RawResponse);Assert.AreEqual("material_import",stored.PendingPlan.actions.Single().tool);
        }
        [UnityTest] public IEnumerator AskRejectsMixedImportAndRepairsWithOnlyPermittedOfflineSearch()
        {
            var beforeAssets=AssetDatabase.GetAllAssetPaths();var provider=new RecordingProvider();ChatController controller=null;JObject initialSchema=null;string initialSchemaJson=null;
            provider.Respond=(round,request,token)=>
            {
                if(round==0) {initialSchema=request.Schema;initialSchemaJson=initialSchema.ToString(Formatting.None);return Task.FromResult(Reply(false,MaterialImport(),MaterialSearch(),MaterialApply("@RejectedAskMaterialAnchor")));}
                var tools=((JArray)request.Schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName).ToArray();
                Assert.IsTrue(tools.Contains("material_search"));Assert.IsTrue(tools.All(SceneToolExecutor.IsReadOnlyTool));Assert.IsFalse(tools.Contains("material_import"));
                Assert.IsFalse(request.Messages.Any(m=>m.Role=="system"&&m.Content.Contains("EXACTLY ONE material_import")));
                if(round==1)
                {
                    Assert.AreEqual(initialSchemaJson,initialSchema.ToString(Formatting.None));CollectionAssert.AreEqual(new[]{"material_search"},tools);
                    Assert.AreEqual(1,(int)request.Schema["properties"]["actions"]["minItems"]);Assert.AreEqual(1,(int)request.Schema["properties"]["actions"]["maxItems"]);CollectionAssert.AreEqual(new[]{false},request.Schema["properties"]["complete"]["enum"].Values<bool>());
                    Assert.IsTrue(request.Messages.Any(m=>m.Role=="system"&&m.Content.Contains("EXACTLY ONE material_search")&&m.Content.Contains("builtin")));
                    Assert.IsFalse(request.Messages.Any(m=>m.Content.Contains("RejectedAskMaterialAnchor")));return Task.FromResult(Reply(false,MaterialSearch()));
                }
                Assert.AreEqual(2,round);Assert.AreEqual("material_search",controller.Current.Messages.Last().Steps.Single().Tool);
                return Task.FromResult(Reply(true));
            };
            controller=Controller(provider);controller.Settings.Mode=AssistantMode.Ask;controller.Settings.Power=AssistantPower.Balanced;
            yield return Finish(controller.SendAsync("Найди текстуру дерева и только объясни доступные варианты."));
            var answer=controller.Current.Messages.Last();Assert.IsFalse(answer.IsError,answer.Text);Assert.AreEqual(3,answer.Attempts.Count);Assert.IsFalse(answer.Attempts[0].Accepted);Assert.IsTrue(answer.Attempts[1].Accepted);Assert.IsTrue(answer.Attempts[2].Accepted);
            Assert.AreEqual(1,answer.Steps.Count);Assert.IsTrue(answer.Steps[0].Success);Assert.AreEqual("material_search",answer.Steps[0].Tool);Assert.IsNull(answer.PendingPlan);Assert.IsFalse(answer.Task.MutationAuthorized);Assert.IsTrue(string.IsNullOrEmpty(answer.Task.CheckpointId));
            Assert.AreEqual(0,testScene.rootCount);CollectionAssert.AreEquivalent(beforeAssets,AssetDatabase.GetAllAssetPaths());
        }
        [UnityTest] public IEnumerator FirstMixedDiscoveryCannotImportAGuessedIdBeforeItsActualSearchResult()
        {
            const string guessed="polyhaven:unverified_before_first_search";
            var guessedImport=new EditorCommand {tool="material_import",args=new JObject {["material_id"]=guessed,["allow_online"]=false}};
            string raw=Reply(false,MaterialSearch(),guessedImport,new EditorCommand {tool="material_apply",target="@UnverifiedDiscoveryDoor",args=new JObject {["material_id"]=guessed}});
            var beforeAssets=AssetDatabase.GetAllAssetPaths();var provider=new RecordingProvider();ChatController controller=null;
            provider.Respond=(round,request,token)=>
            {
                if(round==0)return Task.FromResult(raw);
                Assert.IsFalse(request.Messages.Any(message=>message.Content.Contains(guessed)||message.Content.Contains("UnverifiedDiscoveryDoor")),"Rejected guessed IDs cannot anchor the next phase.");
                Assert.IsTrue(string.IsNullOrEmpty(controller.Current.Messages.Last().Task.CheckpointId));Assert.AreEqual(0,testScene.rootCount);CollectionAssert.AreEquivalent(beforeAssets,AssetDatabase.GetAllAssetPaths());
                if(round==1)
                {
                    Assert.IsEmpty(controller.Current.Messages.Last().Steps);
                    var actions=request.Schema["properties"]["actions"];CollectionAssert.AreEqual(new[]{"material_search"},((JArray)actions["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName));
                    Assert.AreEqual(1,(int)actions["minItems"]);Assert.AreEqual(1,(int)actions["maxItems"]);CollectionAssert.AreEqual(new[]{false},request.Schema["properties"]["complete"]["enum"].Values<bool>());
                    Assert.IsTrue(request.Messages.Any(message=>message.Role=="system"&&message.Content.Contains("EXACTLY ONE material_search")));
                    return Task.FromResult(Reply(false,MaterialSearch()));
                }
                Assert.AreEqual(2,round);var search=controller.Current.Messages.Last().Steps.Single();Assert.AreEqual("material_search",search.Tool);Assert.IsTrue(search.Success);StringAssert.Contains("builtin:wood",search.Summary);
                return Task.FromResult(Reply(false,MaterialImport()));
            };
            controller=Controller(provider);controller.Settings.Mode=AssistantMode.Plan;controller.Settings.Power=AssistantPower.Balanced;
            yield return Finish(controller.SendAsync("Найди текстуру дерева и подготовь её применение к двери."));
            var answer=controller.Current.Messages.Last();Assert.IsFalse(answer.IsError,answer.Text);Assert.AreEqual(raw,answer.Attempts[0].RawResponse);Assert.IsFalse(answer.Attempts[0].Accepted);Assert.IsTrue(answer.Attempts[1].Accepted);Assert.IsTrue(answer.Attempts[2].Accepted);
            Assert.AreEqual(1,answer.Steps.Count);Assert.AreEqual("material_search",answer.Steps[0].Tool);Assert.IsNotNull(answer.PendingPlan);Assert.AreEqual("material_import",answer.PendingPlan.actions.Single().tool);Assert.AreEqual("builtin:wood",(string)answer.PendingPlan.actions[0].args["material_id"]);
            Assert.IsTrue(string.IsNullOrEmpty(answer.Task.CheckpointId));Assert.AreEqual(0,testScene.rootCount);CollectionAssert.AreEquivalent(beforeAssets,AssetDatabase.GetAllAssetPaths());
        }
        [UnityTest] public IEnumerator FailedMaterialPlanAfterReadOnlySearchNeverClaimsSceneChanges()
        {
            var beforeAssets=AssetDatabase.GetAllAssetPaths();var provider=new RecordingProvider();
            provider.Respond=(round,request,token)=>Task.FromResult(round==0?Reply(false,MaterialSearch()):Reply(false,MaterialImport(),MaterialApply("@RejectedFailureDoor")));
            var controller=Controller(provider);controller.Settings.Power=AssistantPower.Balanced;
            yield return Finish(controller.SendAsync("Найди текстуру дерева и примени её к двери."));
            var answer=controller.Current.Messages.Last();Assert.IsTrue(answer.IsError);Assert.AreEqual(3,answer.Attempts.Count);Assert.AreEqual(1,answer.Steps.Count);Assert.IsTrue(answer.Steps[0].Success);Assert.AreEqual("material_search",answer.Steps[0].Tool);
            StringAssert.Contains("Полученные данные доступны в шагах",answer.Text);StringAssert.DoesNotContain("Часть задачи уже выполнена в сцене",answer.Text);StringAssert.DoesNotContain("изменения доступны в сцене",answer.Text);
            Assert.IsNull(answer.PendingPlan);Assert.IsTrue(string.IsNullOrEmpty(answer.Task.CheckpointId));Assert.AreEqual(0,testScene.rootCount);CollectionAssert.AreEquivalent(beforeAssets,AssetDatabase.GetAllAssetPaths());
        }
        [UnityTest] public IEnumerator CancellationAfterReadOnlySearchNeverClaimsUndoableSceneChanges()
        {
            var beforeAssets=AssetDatabase.GetAllAssetPaths();var provider=new RecordingProvider();ChatController controller=null;
            provider.Respond=(round,request,token)=>
            {
                if(round==0)return Task.FromResult(Reply(false,MaterialSearch()));
                Assert.AreEqual(1,round);Assert.AreEqual("material_search",controller.Current.Messages.Last().Steps.Single().Tool);controller.Cancel();token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("The actual operation token must have been cancelled.");
            };
            controller=Controller(provider);controller.Settings.Power=AssistantPower.Balanced;
            yield return Finish(controller.SendAsync("Найди текстуру дерева и примени её к двери."));
            var answer=controller.Current.Messages.Last();Assert.AreEqual("Остановлено",answer.Status);Assert.IsFalse(answer.IsError);Assert.AreEqual(1,answer.Steps.Count);
            StringAssert.Contains("Полученные данные доступны в шагах",answer.Text);StringAssert.DoesNotContain("изменения доступны в сцене",answer.Text);StringAssert.DoesNotContain("их можно отменить",answer.Text);
            Assert.IsTrue(string.IsNullOrEmpty(answer.Task.CheckpointId));Assert.AreEqual(0,testScene.rootCount);CollectionAssert.AreEquivalent(beforeAssets,AssetDatabase.GetAllAssetPaths());
        }
        [UnityTest] public IEnumerator ReadOnlyInspectionCannotClearAnExecuteTasksFailedImport()
        {
            var beforeAssets=AssetDatabase.GetAllAssetPaths();string unavailable="polyhaven:phase_repair_uncached_"+Guid.NewGuid().ToString("N");
            var provider=new RecordingProvider();ChatController controller=null;
            provider.Respond=(round,request,token)=>
            {
                if(round==0)return Task.FromResult(Reply(false,new EditorCommand {tool="material_import",args=new JObject {["material_id"]=unavailable,["allow_online"]=false}}));
                if(round==1)
                {
                    var failed=controller.Current.Messages.Last().Steps.Single();Assert.AreEqual("material_import",failed.Tool);Assert.IsFalse(failed.Success);StringAssert.Contains("not cached",failed.Error);
                    return Task.FromResult(Reply(false,MaterialSearch()));
                }
                Assert.AreEqual(2,round);Assert.AreEqual(2,controller.Current.Messages.Last().Steps.Count);Assert.IsTrue(controller.Current.Messages.Last().Steps.Last().Success);return Task.FromResult(Reply(true));
            };
            controller=Controller(provider);controller.Settings.Mode=AssistantMode.Execute;controller.Settings.Power=AssistantPower.Balanced;
            yield return Finish(controller.SendAsync("Найди и импортируй текстуру дерева для двери."));
            var answer=controller.Current.Messages.Last();Assert.IsTrue(answer.IsError,"A successful lookup cannot repair the failed requested mutation.");Assert.IsTrue(answer.Task.MutationAuthorized);Assert.AreEqual("Проверки не пройдены",answer.Status);
            Assert.AreEqual(2,answer.Steps.Count);Assert.IsFalse(answer.Steps[0].Success);Assert.AreEqual("material_search",answer.Steps[1].Tool);Assert.IsTrue(answer.Steps[1].Success);Assert.IsTrue(string.IsNullOrEmpty(answer.Task.CheckpointId));
            Assert.AreEqual(0,testScene.rootCount);CollectionAssert.AreEquivalent(beforeAssets,AssetDatabase.GetAllAssetPaths());
        }
        [UnityTest] public IEnumerator FailedAskReadFollowedByUnrelatedProjectInspectionStillReportsFailure()
        {
            string missing="absent-skill-"+Guid.NewGuid().ToString("N");var beforeAssets=AssetDatabase.GetAllAssetPaths();var provider=new RecordingProvider();ChatController controller=null;
            provider.Respond=(round,request,token)=>
            {
                if(round==0)
                {
                    Assert.IsTrue(((JArray)request.Schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Any(branch=>EditorToolSchema.ToolName(branch)=="read_skill"));
                    return Task.FromResult(Reply(false,new EditorCommand {tool="read_skill",args=new JObject {["id"]=missing}}));
                }
                if(round==1)
                {
                    var failure=controller.Current.Messages.Last().Steps.Single();Assert.IsFalse(failure.Success);StringAssert.Contains("Unknown skill",failure.Error);
                    return Task.FromResult(Reply(false,new EditorCommand {tool="inspect_project"}));
                }
                Assert.AreEqual(2,round);var steps=controller.Current.Messages.Last().Steps;Assert.AreEqual(2,steps.Count);Assert.IsFalse(steps[0].Success);Assert.IsTrue(steps[1].Success);Assert.AreEqual("inspect_project",steps[1].Tool);
                return Task.FromResult(Reply(true));
            };
            controller=Controller(provider);controller.Settings.Mode=AssistantMode.Ask;controller.Settings.Power=AssistantPower.Balanced;
            yield return Finish(controller.SendAsync("Прочитай навык и осмотри проект. Только объясни, ничего не изменяй."));
            var answer=controller.Current.Messages.Last();Assert.IsTrue(answer.IsError,"An unrelated successful query cannot resolve a real failed read.");Assert.AreEqual("Проверки не пройдены",answer.Status);Assert.IsFalse(answer.Task.MutationAuthorized);Assert.IsTrue(answer.Attempts.All(attempt=>attempt.Accepted),"The fixture must exercise actual execution failure, not malformed-plan rejection.");
            Assert.AreEqual(2,answer.Steps.Count);Assert.IsFalse(answer.Steps[0].Success);Assert.IsTrue(answer.Steps[1].Success);Assert.IsTrue(string.IsNullOrEmpty(answer.Task.CheckpointId));Assert.AreEqual(0,testScene.rootCount);CollectionAssert.AreEquivalent(beforeAssets,AssetDatabase.GetAllAssetPaths());
        }
        [UnityTest] public IEnumerator CompleteFlagStillRequiresFreshReadbackAndGoalRepair()
        {
            const string goal="Создай ровно один куб VerifiedCube в точке (6, 1, -3).";
            var expected=new Vector3(6,1,-3);var provider=new RecordingProvider();
            provider.Respond=(round,request,token)=>
            {
                // The first model answer wrongly declares completion without fulfilling the requested position.
                if(round==0)return Task.FromResult(Reply(true,Create("VerifiedCube")));
                var cube=Find("VerifiedCube");Assert.That(cube,Is.Not.Null,"Verification must run after the real executor commits creation.");
                string observation=request.Messages.Last().Content;
                StringAssert.Contains("VerifiedCube",observation);StringAssert.Contains(ProjectContextIndex.StableTarget(cube),observation);
                Assert.That(request.Messages.Any(message=>message.Content.Contains(goal)),Is.True,"Verification lost the original user goal.");
                if(round==1)
                {
                    Assert.That(cube.transform.position,Is.Not.EqualTo(expected),"The intended position must still be missing before the repair round.");
                    return Task.FromResult(Reply(true,new EditorCommand{tool="set_transform",target="id:"+cube.GetInstanceID(),args=new JObject{["position"]=new JArray(6,1,-3)}}));
                }
                Assert.That(round,Is.EqualTo(2));Assert.That(cube.transform.position,Is.EqualTo(expected));
                return Task.FromResult(Reply(true));
            };
            var controller=Controller(provider);controller.Settings.Power=AssistantPower.Balanced;
            yield return Finish(controller.SendAsync(goal));
            var answer=controller.Current.Messages.Last();Assert.That(answer.IsError,Is.False,answer.Text);Assert.That(answer.Status,Is.EqualTo("Изменения применены"));
            Assert.That(provider.Requests.Count,Is.EqualTo(3));Assert.That(answer.Attempts.Count,Is.EqualTo(3));Assert.That(answer.Attempts.All(attempt=>attempt.Accepted),Is.True);
            Assert.That(answer.Steps.Count,Is.EqualTo(2));Assert.That(answer.Steps.All(step=>step.Success),Is.True);
            Assert.That(answer.Steps.Select(step=>step.Tool),Is.EqualTo(new[]{"create_object","set_transform"}));
            Assert.That(answer.Steps.Select(step=>step.Target).Distinct().Count(),Is.EqualTo(1));Assert.That(Find("VerifiedCube").transform.position,Is.EqualTo(expected));
            Assert.That(testScene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<Transform>(true)).Count(item=>item.name=="VerifiedCube"),Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator AutoApplyDisabledRetainsPlanUntilExplicitApply()
        {
            var provider=new RecordingProvider{Respond=(round,request,token)=>Task.FromResult(Reply(true,Create("ReviewedCube")))};
            var controller=Controller(provider);controller.Settings.AutoApply=false;
            yield return Finish(controller.SendAsync("Подготовь куб."));
            var answer=controller.Current.Messages.Last();Assert.That(answer.PendingPlan,Is.Not.Null);Assert.That(answer.Steps,Is.Empty);Assert.That(Find("ReviewedCube"),Is.Null);
            yield return Finish(controller.ApplyPendingAsync(answer.Id));
            Assert.That(answer.PendingPlan,Is.Null);Assert.That(answer.IsError,Is.False,answer.Text);Assert.That(Find("ReviewedCube"),Is.Not.Null);Assert.That(answer.Steps.Single().Success,Is.True);
        }
        [UnityTest] public IEnumerator EmptyFinalAnswerCannotMarkRolledBackBatchAsDone()
        {
            var provider = new RecordingProvider { Respond = (round, request, token) => Task.FromResult(round == 0
                ? Reply(true,
                    new EditorCommand { tool = "create_world", name = "RollbackFixture", args = new JObject {
                        ["resolution"] = 33, ["width"] = 32, ["length"] = 32, ["height"] = 4,
                        ["trees"] = 0, ["rocks"] = 0, ["buildings"] = 0, ["waterLevel"] = 0 } },
                    new EditorCommand { tool = "scatter_objects", name = "CannotFit", target = "@RollbackFixture", args = new JObject {
                        ["kind"] = "house", ["count"] = 2, ["size"] = new JArray(1, 1), ["spacing"] = 100 } })
                : Reply(true)) };
            var controller = Controller(provider); controller.Settings.Power = AssistantPower.Balanced;
            yield return Finish(controller.SendAsync("Создай мир с домами."));
            var answer = controller.Current.Messages.Last();
            Assert.That(provider.Requests.Count, Is.EqualTo(2));
            Assert.That(answer.Steps.Any(step => !step.Success && step.Tool == "batch"), Is.True,
                "The regression must reach a real execution failure, not a validation rejection.");
            Assert.That(testScene.rootCount, Is.Zero, "The failed batch must remain rolled back.");
            Assert.That(answer.IsError, Is.True); Assert.That(answer.Status, Is.EqualTo("Проверки не пройдены"));
        }
        [UnityTest] public IEnumerator CancellationBeforeCompletionCreatesNothing()
        {
            var gate=new TaskCompletionSource<string>();
            var provider=new RecordingProvider{Respond=(round,request,token)=>{token.Register(()=>gate.TrySetCanceled());return gate.Task;}};
            var controller=Controller(provider);var task=controller.SendAsync("Создай мир.");Assert.That(controller.Busy,Is.True);controller.Cancel();yield return Finish(task);
            var answer=controller.Current.Messages.Last();Assert.That(controller.Busy,Is.False);Assert.That(answer.Status,Is.EqualTo("Остановлено"));Assert.That(answer.Steps,Is.Empty);Assert.That(testScene.rootCount,Is.Zero);
        }
        [UnityTest] public IEnumerator CancellationAfterCommittedRoundRetainsActualChange()
        {
            var gate=new TaskCompletionSource<string>();var provider=new RecordingProvider{Respond=(round,request,token)=>
            {if(round==0)return Task.FromResult(Reply(false,Create("CommittedCube")));token.Register(()=>gate.TrySetCanceled());return gate.Task;}};
            var controller=Controller(provider);var task=controller.SendAsync("Создай куб и проверь результат.");Assert.That(provider.Requests.Count,Is.EqualTo(2));Assert.That(Find("CommittedCube"),Is.Not.Null);
            controller.Cancel();yield return Finish(task);var answer=controller.Current.Messages.Last();Assert.That(answer.Status,Is.EqualTo("Остановлено"));Assert.That(answer.Steps.Any(s=>s.Success),Is.True);Assert.That(Find("CommittedCube"),Is.Not.Null);StringAssert.Contains("выполненные изменения",answer.Text);
        }
        [UnityTest] public IEnumerator InvalidJsonDoesNotEditSceneAndRecordsFailure()
        {
            var provider=new RecordingProvider{Respond=(round,request,token)=>Task.FromResult("```json\n{broken}\n```")};
            var controller=Controller(provider);controller.Settings.Power=AssistantPower.Fast;
            yield return Finish(controller.SendAsync("Создай куб."));var answer=controller.Current.Messages.Last();Assert.That(answer.IsError,Is.True);Assert.That(answer.Status,Is.EqualTo("Не выполнено"));Assert.That(answer.Steps,Is.Empty);Assert.That(testScene.rootCount,Is.Zero);Assert.That(controller.Busy,Is.False);
        }
        [UnityTest] public IEnumerator MalformedAssistantPrefixIsNotRepeatedInRepairContext()
        {
            const string broken = "{\"reply{\"type\":\"object\"}";
            var provider = new RecordingProvider { Respond = (round, request, token) =>
            {
                if (round == 0) return Task.FromResult(broken);
                Assert.That(request.Messages.Any(message => message.Role == "assistant" && message.Content == broken),
                    Is.False, "Do not reinforce the malformed assistant prefix in a repair turn.");
                Assert.That(request.Messages.Any(message => message.Content.Contains("Создай RepairPrefixCube")), Is.True);
                if (round == 1)
                {
                    StringAssert.Contains("План отклонён", request.Messages.Last().Content);
                    Assert.That(request.Messages.Last().Content,Does.EndWith("CURRENT USER TASK: Создай RepairPrefixCube"),"A repair must end with this user task, not an older code-writing task.");
                    Assert.That(Find("RepairPrefixCube"), Is.Null);
                    return Task.FromResult(Reply(true, Create("RepairPrefixCube")));
                }
                Assert.That(Find("RepairPrefixCube"), Is.Not.Null);
                return Task.FromResult(Reply(true));
            } };
            var controller = Controller(provider); controller.Settings.Power = AssistantPower.Balanced;
            yield return Finish(controller.SendAsync("Создай RepairPrefixCube"));
            var answer = controller.Current.Messages.Last();
            Assert.That(answer.Attempts[0].Response, Is.EqualTo(broken), "Retain the raw failure for auditing.");
            Assert.That(answer.Attempts[0].Error, Is.Not.Empty);
            Assert.That(answer.IsError, Is.False, answer.Text); Assert.That(answer.Status, Is.EqualTo("Изменения применены"));
            Assert.That(Find("RepairPrefixCube"), Is.Not.Null);
        }
        [UnityTest] public IEnumerator HistoryAndProviderRequestsRedactCredentialsAndRestoreSessionSettings()
        {
            // Deliberately fake sentinel, not a credential. It is long enough to exercise the real redaction pattern.
            string sentinel= "sk-" + new string('x', 39);
            var provider=new RecordingProvider{Respond=(round,request,token)=>Task.FromResult(JsonConvert.SerializeObject(new EditorPlan{reply="Готово "+sentinel,complete=true}))};
            var controller=Controller(provider);controller.Settings.Power=AssistantPower.Deep;controller.Settings.AutoApply=false;controller.SaveSettings();string id=controller.Current.Id;
            yield return Finish(controller.SendAsync("Объясни сцену. Тестовый секрет "+sentinel));
            Assert.That(string.Join("\n",provider.Requests.Single()),Does.Not.Contain(sentinel));
            string persisted=File.ReadAllText(Path.Combine(folder,id+".json"));Assert.That(persisted,Does.Not.Contain(sentinel));StringAssert.Contains("секрет скрыт",persisted);
            var reloaded=Controller(new RecordingProvider());Assert.That(reloaded.Current.Id,Is.EqualTo(id));Assert.That(reloaded.Settings.Power,Is.EqualTo(AssistantPower.Deep));Assert.That(reloaded.Settings.AutoApply,Is.False);Assert.That(reloaded.Current.Messages.Count,Is.EqualTo(2));Assert.That(reloaded.Current.Messages.Last().Text,Does.Not.Contain(sentinel));
        }
        [UnityTest] public IEnumerator NewChatKeepsOldHistoryAndCanReopenIt()
        {
            var provider=new RecordingProvider{Respond=(round,request,token)=>Task.FromResult(Reply(true))};var controller=Controller(provider);
            yield return Finish(controller.SendAsync("Первый разговор"));string id=controller.Current.Id;controller.NewChat();Assert.That(controller.Current.Id,Is.Not.EqualTo(id));Assert.That(controller.Current.Messages,Is.Empty);controller.OpenChat(id);Assert.That(controller.Current.Messages.First().Text,Is.EqualTo("Первый разговор"));
            var restored=Controller(new RecordingProvider());Assert.That(restored.Sessions.Count,Is.EqualTo(2));Assert.That(restored.Sessions.Any(s=>s.Id==id&&s.Messages.Count==2),Is.True);
        }
        [UnityTest] public IEnumerator ClassicFinalRootClosurePreservesEscapedCodeAndRawAudit()
        {
            const string code="using UnityEngine; public class EscapeRotor : MonoBehaviour { string text = \"quoted \\\" ] } { \\\"\"; void Update() { transform.Rotate(Vector3.up,30f*Time.deltaTime,Space.Self); } }";
            string valid=Reply(true,Draft("EscapeRotor",code));string raw=valid.Substring(0,valid.Length-1)+" \n";
            var provider=new RecordingProvider{Respond=(round,request,token)=>Task.FromResult(raw)};
            var controller=Controller(provider);controller.Settings.ModelKey="pollinations:openai-fast";controller.Settings.Power=AssistantPower.Fast;
            yield return Finish(controller.SendAsync("Напиши вращение для моего ревью, пока не импортируй."));
            var answer=controller.Current.Messages.Last();var attempt=answer.Attempts.Single();
            Assert.That(answer.IsError,Is.False,answer.Text);Assert.That(answer.PendingPlan,Is.Not.Null);Assert.That(answer.Steps,Is.Empty);
            Assert.That((string)answer.PendingPlan.actions.Single().args["code"],Is.EqualTo(code));
            Assert.That(attempt.Response,Is.EqualTo(raw));Assert.That(attempt.RawResponse,Is.EqualTo(raw));Assert.That(attempt.RepairKind,Is.EqualTo("classic-final-root-close"));
            Assert.That(attempt.NormalizedResponse,Is.EqualTo(raw+"}"));Assert.That(attempt.Accepted,Is.True);Assert.That(testScene.rootCount,Is.Zero);
            Assert.That(File.Exists("Assets/WorldWeaverGenerated/Scripts/EscapeRotor.cs"),Is.False,"A reviewed draft must not be imported by normalization.");
            string persisted=File.ReadAllText(Path.Combine(folder,controller.Current.Id+".json"));var stored=JsonConvert.DeserializeObject<ChatSession>(persisted).Messages.Last().Attempts.Single();
            Assert.That(stored.RawResponse,Is.EqualTo(raw));Assert.That(stored.NormalizedResponse,Is.EqualTo(raw+"}"));Assert.That(stored.RepairKind,Is.EqualTo(attempt.RepairKind));
        }
        [UnityTest] public IEnumerator ClassicNormalizedPlanUsesValidatedTextInLaterInference()
        {
            string sentinel="sk-"+new string('y',39);
            string valid=JsonConvert.SerializeObject(new EditorPlan{reply="Готово "+sentinel,complete=true,actions=new List<EditorCommand>{Create("NormalizedCube")}});string raw=valid.Substring(0,valid.Length-1);
            string normalized=ChatHistoryStore.Redact(valid);
            var provider=new RecordingProvider{Respond=(round,request,token)=>
            {
                if(round==0)return Task.FromResult(raw);
                Assert.That(Find("NormalizedCube"),Is.Not.Null,"The real scene executor must have committed before verification.");
                Assert.That(request.Messages.Any(m=>m.Role=="assistant"&&m.Content==normalized),Is.True);
                Assert.That(request.Messages.Any(m=>m.Role=="assistant"&&m.Content==raw),Is.False,"Do not reinforce malformed raw JSON after a successful adapter repair.");
                Assert.That(string.Join("\n",request.Messages.Select(m=>m.Content)),Does.Not.Contain(sentinel),"Normalized assistant text must retain credential redaction before the next request.");
                return Task.FromResult(Reply(true));
            }};
            var controller=Controller(provider);controller.Settings.ModelKey="pollinations:openai-fast";controller.Settings.Power=AssistantPower.Balanced;
            yield return Finish(controller.SendAsync("Создай NormalizedCube."));var answer=controller.Current.Messages.Last();
            Assert.That(answer.IsError,Is.False,answer.Text);Assert.That(answer.Steps.Single().Success,Is.True);Assert.That(answer.Attempts[0].RawResponse,Is.EqualTo(ChatHistoryStore.Redact(raw)));
            Assert.That(answer.Attempts[0].NormalizedResponse,Is.EqualTo(normalized));
            Assert.That(provider.Requests.Count,Is.EqualTo(2));Assert.That(Find("NormalizedCube"),Is.Not.Null);
        }
        [UnityTest] public IEnumerator ClassicRepairRejectsIncompleteInnerStringsAndMismatchedBrackets()
        {
            string[] invalid={
                "{\"reply\":\"x\",\"complete\":true,\"actions\":[{\"tool\":\"create_object\",\"name\":\"UnsafeCube\",\"target\":\"\",\"args\":{\"kind\":\"cube\"}]",
                "{\"reply\":\"unterminated escaped \\\" string ]",
                "{\"reply\":\"x\",\"complete\":true,\"actions\":[}",
                "{\"reply\":\"x\",\"complete\":true,\"actions\":\"[]\"",
                Reply(true)+"]",
                "```json\n{\"reply\":\"x\",\"complete\":true,\"actions\":[]\n```"
            };
            foreach(string raw in invalid)
            {
                var provider=new RecordingProvider{Respond=(round,request,token)=>Task.FromResult(raw)};var controller=Controller(provider);
                controller.Settings.ModelKey="pollinations:openai-fast";controller.Settings.Power=AssistantPower.Fast;
                yield return Finish(controller.SendAsync("Создай объект."));var answer=controller.Current.Messages.Last();var attempt=answer.Attempts.Single();
                Assert.That(answer.IsError,Is.True,raw);Assert.That(attempt.RepairKind,Is.Null,raw);Assert.That(attempt.NormalizedResponse,Is.Null);Assert.That(attempt.RawResponse,Is.EqualTo(raw));
                Assert.That(answer.PendingPlan,Is.Null);Assert.That(answer.Steps,Is.Empty);Assert.That(testScene.rootCount,Is.Zero);
            }
        }
        [UnityTest] public IEnumerator LocalProviderDoesNotUseClassicRootOrPlainTextRepair()
        {
            string valid=Reply(true,Create("LocalMustNotExist"));string[] responses={valid.Substring(0,valid.Length-1),"Сейчас выбрана локальная модель, сцена пуста."};
            foreach(string raw in responses)
            {
                var provider=new RecordingProvider{Respond=(round,request,token)=>Task.FromResult(raw)};var controller=Controller(provider);controller.Settings.Power=AssistantPower.Fast;
                controller.Settings.ModelKey="ollama:qwen3.5:9b";
                yield return Finish(controller.SendAsync("Только объясни сцену."));var answer=controller.Current.Messages.Last();var attempt=answer.Attempts.Single();
                Assert.That(answer.IsError,Is.True);Assert.That(attempt.RawResponse,Is.EqualTo(raw));Assert.That(attempt.RepairKind,Is.Null);Assert.That(attempt.NormalizedResponse,Is.Null);
                Assert.That(answer.PendingPlan,Is.Null);Assert.That(testScene.rootCount,Is.Zero);
            }
        }
        [UnityTest] public IEnumerator RepairedClassicJsonStillRejectsForbiddenTool()
        {
            string valid=Reply(true,new EditorCommand{tool="run_shell",name="Forbidden",args=new JObject{["command"]="fictional-command"}});string raw=valid.Substring(0,valid.Length-1);
            var provider=new RecordingProvider{Respond=(round,request,token)=>Task.FromResult(raw)};var controller=Controller(provider);
            controller.Settings.ModelKey="pollinations:openai-fast";controller.Settings.Power=AssistantPower.Fast;
            yield return Finish(controller.SendAsync("Создай объект."));var answer=controller.Current.Messages.Last();var attempt=answer.Attempts.Single();
            Assert.That(attempt.RepairKind,Is.EqualTo("classic-final-root-close"));Assert.That(attempt.NormalizedResponse,Is.EqualTo(valid));
            Assert.That(attempt.Accepted,Is.False);Assert.That(answer.IsError,Is.True);Assert.That(answer.PendingPlan,Is.Null);Assert.That(answer.Steps,Is.Empty);Assert.That(testScene.rootCount,Is.Zero);
        }
        [UnityTest] public IEnumerator ExplicitReadOnlyBlocksCreateDraftAndAttachWithoutReplacingOlderDraft()
        {
            var mutations=new[]{Create("ReadOnlyMustNotExist"),Draft("ReadOnlyMustNotDraft"),new EditorCommand{tool="attach_script",name="ReadOnlyMustNotAttach",target="MissingTarget"}};
            var provider=new RecordingProvider{Respond=(round,request,token)=>Task.FromResult(round==0?Reply(true,Draft("ExistingReviewRotor")):Reply(true,mutations[(round-1)%mutations.Length]))};
            var controller=Controller(provider);controller.Settings.Power=AssistantPower.Fast;
            yield return Finish(controller.SendAsync("Напиши вращение, не импортируй: сначала ревью."));var oldAnswer=controller.Current.Messages.Last();
            Assert.That(oldAnswer.PendingPlan,Is.Not.Null);string previousPlan=JsonConvert.SerializeObject(oldAnswer.PendingPlan);
            foreach(string prompt in new[]{"Какая модель сейчас выбрана и что находится в этой сцене? Только объясни в чате. Ничего не создавай и не изменяй; предыдущий черновик C# оставь ожидающим моего ревью.","Only explain the current scene. Do not create or import anything.","Только расскажи о сцене.","Только объясни сцену. Напиши кратко.","Only explain the scene. Write in Russian.","Только объясни сцену. Сделай ответ коротким."})
            {
                yield return Finish(controller.SendAsync(prompt));var answer=controller.Current.Messages.Last();
                Assert.That(answer.IsError,Is.True);Assert.That(answer.Attempts.Single().Error,Does.Contain("только объяснение"));Assert.That(answer.PendingPlan,Is.Null);Assert.That(answer.Steps,Is.Empty);
                Assert.That(JsonConvert.SerializeObject(oldAnswer.PendingPlan),Is.EqualTo(previousPlan));Assert.That(testScene.rootCount,Is.Zero);
            }
            var restored=JsonConvert.DeserializeObject<ChatSession>(File.ReadAllText(Path.Combine(folder,controller.Current.Id+".json")));
            Assert.That(JsonConvert.SerializeObject(restored.Messages.Single(m=>m.Id==oldAnswer.Id).PendingPlan),Is.EqualTo(previousPlan));
        }
        [UnityTest] public IEnumerator ClassicReadOnlyPlainTextIsAuditedAndHasNoSceneOrPendingEffects()
        {
            const string plain="Выбрана GPT-OSS. В сцене куб ExistingSceneCube; прежний код ожидает ревью.";
            var cube=new GameObject("ExistingSceneCube");cube.transform.position=new Vector3(3,2,1);
            var provider=new RecordingProvider{Respond=(round,request,token)=>
            {
                if(round==0)return Task.FromResult(Reply(true,Draft("PreservedReviewRotor")));
                Assert.That(request.Messages.Any(m=>m.Role=="system"&&m.Content.Contains("User interaction mode: Ask.")),Is.True);
                var exposed=((JArray)request.Schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName).ToArray();
                Assert.That(exposed,Is.Not.Empty);Assert.That(exposed.All(SceneToolExecutor.IsReadOnlyTool),Is.True,"An explanation request must expose only read tools.");
                return Task.FromResult(plain);
            }};
            var controller=Controller(provider);controller.Settings.ModelKey="pollinations:openai-fast";controller.Settings.Power=AssistantPower.Fast;
            yield return Finish(controller.SendAsync("Напиши код для ревью, не импортируй."));var older=controller.Current.Messages.Last();string retained=JsonConvert.SerializeObject(older.PendingPlan);
            yield return Finish(controller.SendAsync("Какая модель и что находится в сцене? Только объясни в чате. Ничего не создавай и не изменяй."));
            var answer=controller.Current.Messages.Last();var attempt=answer.Attempts.Single();Assert.That(answer.IsError,Is.False,answer.Text);Assert.That(answer.Status,Is.EqualTo("Готово"));Assert.That(answer.Text,Is.EqualTo(plain));
            Assert.That(attempt.RawResponse,Is.EqualTo(plain));Assert.That(attempt.Response,Is.EqualTo(plain));Assert.That(attempt.RepairKind,Is.EqualTo("classic-readonly-text"));Assert.That(attempt.Accepted,Is.True);
            var normalized=EditorPlan.Parse(attempt.NormalizedResponse);Assert.That(normalized.reply,Is.EqualTo(plain));Assert.That(normalized.actions,Is.Empty);Assert.That(normalized.complete,Is.True);
            Assert.That(answer.PendingPlan,Is.Null);Assert.That(answer.Steps,Is.Empty);Assert.That(JsonConvert.SerializeObject(older.PendingPlan),Is.EqualTo(retained));
            Assert.That(testScene.rootCount,Is.EqualTo(1));Assert.That(cube.transform.position,Is.EqualTo(new Vector3(3,2,1)));Assert.That(Find("ExistingSceneCube"),Is.SameAs(cube));
        }
        [UnityTest] public IEnumerator ClassicCodeReviewPlainTextIsNotAnAcceptedDraft()
        {
            const string plain="Вот MonoBehaviour для вращения; импортируйте его после ревью.";
            var provider=new RecordingProvider{Respond=(round,request,token)=>Task.FromResult(plain)};var controller=Controller(provider);
            controller.Settings.ModelKey="pollinations:openai-fast";controller.Settings.Power=AssistantPower.Fast;
            yield return Finish(controller.SendAsync("Напиши отдельный C# MonoBehaviour для плавного вращения. Подготовь код для моего ревью, пока не импортируй, не подключай и не исполняй его."));
            var answer=controller.Current.Messages.Last();Assert.That(answer.IsError,Is.True);Assert.That(answer.Attempts.Single().RepairKind,Is.Null);Assert.That(answer.PendingPlan,Is.Null);Assert.That(answer.Steps,Is.Empty);Assert.That(testScene.rootCount,Is.Zero);
        }
        [UnityTest] public IEnumerator ClassicReadOnlyDoesNotWrapJsonArraysFencesOrMalformedObjects()
        {
            foreach(string raw in new[]{"{broken}","[\"explanation\"]","```json\n{\"reply\":\"explanation\"}\n```"})
            {
                var provider=new RecordingProvider{Respond=(round,request,token)=>Task.FromResult(raw)};var controller=Controller(provider);
                controller.Settings.ModelKey="pollinations:openai-fast";controller.Settings.Power=AssistantPower.Fast;
                yield return Finish(controller.SendAsync("Только опиши сцену."));var answer=controller.Current.Messages.Last();
                Assert.That(answer.IsError,Is.True);Assert.That(answer.Attempts.Single().RepairKind,Is.Null);Assert.That(answer.PendingPlan,Is.Null);Assert.That(answer.Steps,Is.Empty);Assert.That(testScene.rootCount,Is.Zero);
            }
        }
        [UnityTest] public IEnumerator QuotedReadOnlyWordsAndMixedEditRequestsKeepTheirActualEditIntent()
        {
            foreach(string prompt in new[]{"Создай куб с именем «Только объясни».","Создай куб. Затем только объясни результат.","Create a cube named `Only explain`.","Нужен куб с именем «Пример. Только объясни».","Please produce a cube named `Example. Only explain`.","Сначала создай куб. Только расскажи, что сделал.","Сгенерируй куб. Только объясни результат.","Build a cube. Only explain the result.","Только объясни сцену, затем создай куб.","Only explain the scene, then create a cube.","Только объясни сцену. Напиши скрипт вращения."})
            {
                string name="IntentCube"+Guid.NewGuid().ToString("N");var provider=new RecordingProvider{Respond=(round,request,token)=>Task.FromResult(round==0?Reply(true,Create(name)):Reply(true))};
                var controller=Controller(provider);controller.Settings.Power=AssistantPower.Balanced;
                yield return Finish(controller.SendAsync(prompt));var answer=controller.Current.Messages.Last();
                Assert.That(answer.Task.MutationAuthorized,Is.True,"Mixed edit/explain requests retain their explicit edit authorization.");
                Assert.That(answer.Steps.Single().Success,Is.True);Assert.That(Find(name),Is.Not.Null);
                if(prompt.EndsWith("Напиши скрипт вращения.",StringComparison.Ordinal))
                {
                    Assert.That(answer.Task.CompilationRequired,Is.True);
                    Assert.That(answer.IsError,Is.True,"Creating only a cube cannot complete an additional request for a compiled script.");
                    Assert.That(answer.Task.Checks.Single(check=>check.RequirementId=="required_compilation").Passed,Is.False);
                }
                else Assert.That(answer.IsError,Is.False,answer.Text);
            }
        }
        [UnityTest] public IEnumerator ReadOnlyInspectionUsesRealReadToolWithoutMutatingScene()
        {
            var cube=new GameObject("InspectableCube");cube.transform.position=new Vector3(8,5,2);
            var provider=new RecordingProvider{Respond=(round,request,token)=>Task.FromResult(round==0?Reply(false,new EditorCommand{tool="inspect_object",target="id:"+cube.GetInstanceID()}):Reply(true))};
            var controller=Controller(provider);controller.Settings.Power=AssistantPower.Balanced;
            yield return Finish(controller.SendAsync("Only explain the existing cube after inspecting it."));var answer=controller.Current.Messages.Last();
            Assert.That(answer.IsError,Is.False,answer.Text);Assert.That(answer.Steps.Single().Tool,Is.EqualTo("inspect_object"));Assert.That(answer.Steps.Single().Success,Is.True);
            Assert.That(answer.PendingPlan,Is.Null);Assert.That(testScene.rootCount,Is.EqualTo(1));Assert.That(cube.transform.position,Is.EqualTo(new Vector3(8,5,2)));
        }
    }
}
