using System;
using System.Collections;
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
using UnityEngine.TestTools;
using WorldWeaver.Editor.Assistant;
namespace WorldWeaver.Tests
{
    public sealed class AutonomousLoopTests
    {
        [Test] public void FinishRemainsAvailableInAnImplementationPhaseAndPreservesHonestBlockers()
        {
            var schema=EditorPlan.Schema(true,new[]{"create_node"});schema["properties"]["actions"]["minItems"]=1;
            Assert.That(AgentFunctionProtocol.Definitions(schema).Select(f=>(string)f["function"]["name"]),Does.Contain("finish_task"));
            var wire=new JObject{["tool_calls"]=new JArray(new JObject{["function"]=new JObject{["name"]="finish_task",["arguments"]=new JObject{["reply"]="Нужен ресурс",["outcome"]="blocked",["blockers"]=new JArray("Источник недоступен")}}})};
            var result=EditorPlan.Parse(AgentFunctionProtocol.Normalize(wire,schema));Assert.AreEqual("blocked",result.outcome);Assert.AreEqual(0,result.actions.Count);
            var printed=new JObject{["content"]="finish_task({\"reply\":\"Недоступен ресурс\",\"outcome\":\"blocked\",\"blockers\":[\"Нет доступа\"]})"};
            Assert.AreEqual("Недоступен ресурс",EditorPlan.Parse(AgentFunctionProtocol.Normalize(printed,schema)).reply);
            Assert.Throws<ArgumentException>(()=>EditorPlan.Parse("{\"reply\":\"done\",\"complete\":true,\"outcome\":\"blocked\",\"actions\":[]}"));
        }
        [Test] public void InvalidNativeFunctionRetainsActualWireAndActionableName()
        {
            var wire=JObject.Parse("{\"tool_calls\":[{\"function\":{\"name\":\"invented_mutation\",\"arguments\":{}}}]}");
            var error=Assert.Throws<ProviderException>(()=>AgentFunctionProtocol.Normalize(wire,EditorPlan.Schema(true,new[]{"discover_tools"})));
            Assert.That(error.Message,Does.Contain("invented_mutation"));Assert.That(error.Message,Does.Contain("discover_tools"));Assert.IsTrue(JToken.DeepEquals(wire,JObject.Parse(error.WireResponse)));
        }
        [Test] public void NativeObservationsCanBeBatchedButMutationsCannot()
        {
            var schema=EditorPlan.Schema(true,new[]{"model_search","inspect_project","create_node"},true);
            JObject Call(string name,JObject args)=>new JObject{["function"]=new JObject{["name"]=name,["arguments"]=args}};
            var calls=new JArray(Call("model_search",new JObject{["query"]="female"}),Call("inspect_project",new JObject()));
            var plan=EditorPlan.Parse(AgentFunctionProtocol.Normalize(new JObject{["tool_calls"]=calls},schema));Assert.AreEqual(2,plan.actions.Count);Assert.IsTrue(plan.actions.All(a=>SceneToolExecutor.IsReadOnlyTool(a.tool)));
            calls.Add(Call("create_node",new JObject()));Assert.Throws<ProviderException>(()=>AgentFunctionProtocol.Normalize(new JObject{["tool_calls"]=calls},schema));
            Assert.IsFalse(EditorPlan.Schema(true,new[]{"inspect_project"}).ToString().Contains("x-worldweaver"),"Legacy provider schemas must not receive private native annotations.");
        }
        [Test] public void PhysicalPresentationIsCheckedFromActualChangesWithoutMatchingTaskVocabulary()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            try
            {
                var service=new GameObject("Existing service node");
                var task=AgentTaskVerifier.Create("Собери неизвестный артефакт в пустой сцене.",true);
                service.AddComponent<Rigidbody>();AgentTaskVerifier.Verify(task);Assert.IsFalse(task.Requirements.Any(r=>r.Kind=="body_presentation"),"Adding a component to an existing node is not creating a new visible artifact.");
                var body=new GameObject("New artifact").AddComponent<Rigidbody>();body.gameObject.AddComponent<BoxCollider>();AgentTaskVerifier.Verify(task);
                Assert.IsFalse(task.Checks.Single(c=>task.Requirements.Single(r=>r.Kind=="body_presentation").Id==c.RequirementId).Passed);
                var mesh=GameObject.CreatePrimitive(PrimitiveType.Cube);mesh.transform.SetParent(body.transform,false);AgentTaskVerifier.Verify(task);
                Assert.IsTrue(task.Checks.Single(c=>task.Requirements.Single(r=>r.Kind=="body_presentation").Id==c.RequirementId).Passed);
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        [TestCase("В пустой сцене создай главного героя с настоящей моделью и анимацией.",true)]
        [TestCase("Мне создай главного героя с настоящей моделью и анимацией.",true)]
        [TestCase("Не создавай главного героя с настоящей моделью и анимацией.",false)]
        public void AWordEndingInNeIsNotAnExplicitNegation(string prompt,bool expected)
        {Assert.AreEqual(expected,AgentTaskVerifier.Create(prompt,true).Requirements.Any(r=>r.Kind=="animated_player"));}
        sealed class BlockedProvider:ILLMProvider
        {
            public int Calls;public bool ObservationAvailable;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                Calls++;ObservationAvailable=AgentFunctionProtocol.Definitions(request.Schema).Any(t=>(string)t["function"]["name"]=="model_search");
                var plan=new EditorPlan{reply="Ресурс недоступен"};
                if(Calls==1){plan.complete=false;plan.actions.Add(new EditorCommand{tool="discover_tools",args=new JObject{["query"]="project"}});}
                else {plan.outcome="blocked";plan.blockers.Add("Внешний ресурс не предоставлен");}
                return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan),Model="fixture"});
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);
            public void Dispose(){}
        }
        [Test] public async Task ARealBlockerDoesNotForceAnUnrelatedMutationOrProtocolRetryLoop()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);string folder="UserSettings/LoopTest-"+Guid.NewGuid().ToString("N");var provider=new BlockedProvider();
            try{using(var controller=new ChatController(_=>provider,folder))
            {
                controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=false;await controller.SendAsync("Создай персонажа из недоступного внешнего ресурса.");
                var answer=controller.Current.Messages.Last();Assert.AreEqual(2,provider.Calls,answer.Text);Assert.IsTrue(provider.ObservationAvailable);Assert.IsFalse(answer.IsError,answer.Text);Assert.AreEqual("blocked",answer.Task.Status);Assert.That(ChatTaskPresentation.From(answer).CssClass,Is.EqualTo("task-card-pending"));Assert.IsFalse(answer.Steps.Any(s=>!SceneToolExecutor.IsReadOnlyTool(s.Tool)));Assert.IsTrue(string.IsNullOrEmpty(answer.Task.CheckpointId));
            }}finally{if(Directory.Exists(folder))Directory.Delete(folder,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        [Test] public void PersistentToolEvidenceCanBePagedWithoutRepeatingItsOriginalAction()
        {
            var message=new AssistantMessage();message.Steps.Add(new EditorCommandResult{Success=true,Tool="model_import",Summary=new string('x',20000)+"ACTUAL_ASSET_AT_END",Target="owned:preserved"});
            var command=new EditorCommand{tool="read_task_step",args=new JObject{["index"]=0,["offset"]=18000}};
            var result=SceneToolExecutor.Execute(new EditorPlan{actions={command}},executionContext:new AgentExecutionContext{Message=message}).Results.Single();
            Assert.That(result.Summary,Does.Contain("ACTUAL_ASSET_AT_END"));Assert.That(result.Summary,Does.Contain("owned:preserved"));Assert.AreEqual(1,message.Steps.Count);
            command.args["index"]=1;Assert.IsFalse(SceneToolExecutor.Execute(new EditorPlan{actions={command}},executionContext:new AgentExecutionContext{Message=message}).Success);
            for(int i=1;i<=200;i++)message.Steps.Add(new EditorCommandResult{Success=true,Tool="inspect_object",Summary="Actual observation "+i});
            command.args["index"]=200;command.args["offset"]=0;var later=SceneToolExecutor.Execute(new EditorPlan{actions={command}},executionContext:new AgentExecutionContext{Message=message});Assert.IsTrue(later.Success,later.Error);Assert.That(later.Results.Single().Summary,Does.Contain("Actual observation 200"));
        }
        sealed class WholeTaskReviewProvider:ILLMProvider
        {
            public int Calls,Reviews;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                if(request.Schema["properties"]?["passed"]!=null)
                {
                    Reviews++;return Task.FromResult(new CompletionResult{Text=Reviews==1?"{\"passed\":false,\"issues\":[\"Куб высотой 1 м вместо 2 м\"],\"evidence\":\"Текущие bounds\"}":"{\"passed\":true,\"issues\":[],\"evidence\":\"Текущие bounds 2 м\"}"});
                }
                Calls++;var plan=new EditorPlan();
                if(Calls==1){plan.complete=false;plan.actions.Add(new EditorCommand{tool="create_object",name="SemanticCube",args=new JObject{["kind"]="cube"}});}
                if(Calls==3){plan.complete=false;plan.actions.Add(new EditorCommand{tool="set_transform",target="@SemanticCube",args=new JObject{["scale"]=new JArray(2,2,2)}});}
                return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan)});
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);
            public void Dispose(){}
        }
        [Test] public async Task DeepReviewChecksTheWholeTaskEvenWithoutEditorCode()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);string folder="UserSettings/WholeTaskReview-"+Guid.NewGuid().ToString("N");var provider=new WholeTaskReviewProvider();
            try{using(var controller=new ChatController(_=>provider,folder))
            {
                controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=false;controller.Settings.Power=AssistantPower.Deep;
                await controller.SendAsync("Создай куб со стороной 2 метра.");var answer=controller.Current.Messages.Last();
                Assert.IsFalse(answer.IsError,answer.Text);Assert.AreEqual(2,provider.Reviews);Assert.AreEqual(4,provider.Calls);Assert.AreEqual(2,GameObject.Find("SemanticCube").GetComponent<Renderer>().bounds.size.y,.001);Assert.IsFalse(answer.Steps.Any(s=>s.Tool=="execute_editor_code"));
            }}finally{if(Directory.Exists(folder))Directory.Delete(folder,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        sealed class WanderingProvider:ILLMProvider
        {
            public int Calls,Reviews;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                if(request.Schema["properties"]?["passed"]!=null){Reviews++;return Task.FromResult(new CompletionResult{Text="{\"passed\":true,\"issues\":[],\"evidence\":\"Куб находится на высоте 3 м\"}"});}
                Calls++;var plan=new EditorPlan{complete=false};
                if(Calls==1)plan.actions.Add(new EditorCommand{tool="create_object",name="WanderingCube",args=new JObject{["kind"]="cube"}});
                else plan.actions.Add(new EditorCommand{tool="set_transform",target="@WanderingCube",args=new JObject{["position"]=new JArray(0,Calls-1,0)}});
                return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan)});
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);
            public void Dispose(){}
        }
        [Test] public async Task HostReviewStopsFurtherChangesWhenTheGoalIsAlreadyMet()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);string folder="UserSettings/ProgressReview-"+Guid.NewGuid().ToString("N");var provider=new WanderingProvider();
            try{using(var controller=new ChatController(_=>provider,folder))
            {
                controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=false;controller.Settings.Power=AssistantPower.Deep;
                await controller.SendAsync("Создай куб и поставь его на высоту 3 метра.");var answer=controller.Current.Messages.Last();
                Assert.IsFalse(answer.IsError,answer.Text);Assert.AreEqual(4,provider.Calls);Assert.AreEqual(1,provider.Reviews);Assert.AreEqual(3,GameObject.Find("WanderingCube").transform.position.y,.001);
            }}finally{if(Directory.Exists(folder))Directory.Delete(folder,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        [TestCase("female adult","female")][TestCase("male adult","male")][TestCase("child","child")]
        public void LicensedModelCatalogSearchIsSemanticAndOffline(string query,string expected)
        {
            var batch=SceneToolExecutor.Execute(new EditorPlan{actions={new EditorCommand{tool="model_search",args=new JObject{["query"]=query,["source"]="rocketbox",["limit"]=4}}}});Assert.IsTrue(batch.Success,batch.Error);var result=JObject.Parse(batch.Results.Single().Summary);Assert.AreEqual(115,(int)result["availableLibraryModels"]);Assert.IsNotEmpty((JArray)result["models"]);
            Assert.That(((string)result["models"][0]["name"]).ToLowerInvariant(),Does.Contain(expected));
            foreach(var row in result["models"]){Assert.That((string)row["model_id"],Does.StartWith("rocketbox:"));Assert.That((string)row["license"],Does.Contain("MIT"));Assert.Greater((long)row["downloadBytes"],10000);}
        }
        [UnityTest] public IEnumerator IdleEditorFramesDoNotMoveTheStarfieldAfterCheckpointSeal()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var camera=new GameObject("Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(20,5,10);
            var stars=new GameObject("Stars").AddComponent<WorldStarfield>();string id=AgentCheckpoints.Begin(Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"));
            try
            {
                AgentCheckpoints.Seal(id,true);EditorApplication.QueuePlayerLoopUpdate();yield return null;yield return null;
                Assert.AreEqual(Vector3.zero,stars.transform.position);Assert.IsTrue(AgentCheckpoints.CanExtend(id),AgentCheckpoints.ExtensionStatus(id).ToString());
                stars.transform.position=Vector3.up;Assert.IsFalse(AgentCheckpoints.CanExtend(id));Assert.That(AgentCheckpoints.ExtensionStatus(id)["conflicts"].ToString(),Does.Contain("Scene working state changed"));
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Directory.Delete("UserSettings/WorldWeaverCheckpoints/"+id,true);}
        }
    }
}
