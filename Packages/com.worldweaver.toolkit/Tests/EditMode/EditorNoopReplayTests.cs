using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public class EditorNoopReplayTests
    {
        static int Stale(IReadOnlyList<EditorCommandResult> steps,EditorCommand command)=>(int)typeof(ChatController).GetMethod("StaleEditorCodeObservation",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{steps,command});
        static bool NativeHistory(CompletionRequest request,AssistantMessage answer)=>(bool)typeof(ChatController).GetMethod("AddNativeToolHistory",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{request,answer});
        static bool Progress(EditorCommandResult step)=>(bool)typeof(ChatController).GetMethod("IsImplementationProgress",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{step});
        static EditorCommand Code(string body)=>new EditorCommand{tool="execute_editor_code",args=new JObject{["code"]=body}};
        static AgentExecutionContext Context(ChatController controller)
        {
            controller.Settings.AllowEditorCode=true;controller.Settings.Mode=AssistantMode.Execute;
            return new AgentExecutionContext{Session=controller.Current,Message=new AssistantMessage()};
        }
        static async Task<EditorCommandResult> Execute(EditorCommand command,AgentExecutionContext context)
        {
            var batch=await SceneToolExecutor.ExecuteAsync(new EditorPlan{actions={command}},null,context,default);Assert.IsTrue(batch.Success,batch.Error);return batch.Results.Single(s=>s.Tool=="execute_editor_code");
        }
        [Test] public async Task RepeatedEditorReadIsDurableAndInvalidatedByActualSceneChanges()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            try
            {
                var observed=new GameObject("ExistingReadSubject");observed.AddComponent<GeneratedSceneObject>();
                using(var controller=new ChatController(historyFolder:Path.Combine("UserSettings","EditorNoop-"+Guid.NewGuid().ToString("N"))))
                {
                    var context=Context(controller);var command=Code("return GameObject.Find(\"ExistingReadSubject\").transform.position;");
                    var result=await Execute(command,context);Assert.IsFalse(Progress(result));
                    var restored=JsonConvert.DeserializeObject<List<EditorCommandResult>>(JsonConvert.SerializeObject(new[]{result}));
                    Assert.AreEqual(0,Stale(restored,command),"Replay protection must use persisted evidence, not an ephemeral compiler cache");
                    observed.transform.position=Vector3.up*2;Assert.AreEqual(-1,Stale(restored,command),"A real intervening edit permits a fresh observation");
                    Assert.AreEqual(-1,Stale(restored,Code("return GameObject.Find(\"ExistingReadSubject\").name;")),"A different needed observation remains available");
                    var legacy=new EditorCommandResult{Tool="execute_editor_code",Success=true,Summary="{\"sceneChanged\":false}"};Assert.AreEqual(-1,Stale(new[]{legacy},command));
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        [Test] public async Task SavedAndUnsavedAssetChangesAreDistinctFromAnEditorRead()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            string child="EditorNoopAsset-"+Guid.NewGuid().ToString("N"),folder="Assets/"+child;AssetDatabase.CreateFolder("Assets",child);string path=folder+"/Probe.physicMaterial";
            try
            {
                var material=new PhysicsMaterial("ReplayAsset"){dynamicFriction=.25f};AssetDatabase.CreateAsset(material,path);AssetDatabase.SaveAssets();
                using(var controller=new ChatController(historyFolder:Path.Combine("UserSettings",child)))
                {
                    var context=Context(controller);
                    var write=Code("var m=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(\""+path+"\"); Undo.RecordObject(m,\"Change material\"); m.dynamicFriction=.75f; EditorUtility.SetDirty(m); AssetDatabase.SaveAssets(); return m.dynamicFriction;");
                    var changed=await Execute(write,context);var report=JObject.Parse(changed.Summary);
                    Assert.IsFalse((bool)report["sceneChanged"]);Assert.IsTrue((bool)report["projectChanged"]);Assert.IsTrue(Progress(changed));Assert.AreEqual(.75f,material.dynamicFriction);
                    Assert.AreEqual(-1,Stale(new[]{changed},write),"An effective asset write must not be mistaken for an inspection");
                    var read=Code("return AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(\""+path+"\").dynamicFriction;");var observation=await Execute(read,context);Assert.IsFalse(Progress(observation));Assert.AreEqual(0,Stale(new[]{observation},read));
                    material.dynamicFriction=.4f;EditorUtility.SetDirty(material);
                    Assert.AreEqual(-1,Stale(new[]{observation},read),"Dirty asset changes matter before a disk save");
                    var dirty=await Execute(Code("var m=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(\""+path+"\"); m.dynamicFriction=.6f; EditorUtility.SetDirty(m); return m.dynamicFriction;"),context);
                    Assert.IsTrue((bool)JObject.Parse(dirty.Summary)["projectChanged"]);Assert.IsTrue(Progress(dirty));
                }
            }
            finally{AssetDatabase.DeleteAsset(folder);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        [Test] public void FreshNativeEpochSurvivesSerializationAndDoesNotReintroduceRejectedOpaqueState()
        {
            EditorPlanAttempt Attempt(int round,string state,string id)=>new EditorPlanAttempt{Round=round,Accepted=true,FirstResultIndex=round-1,ResultCount=1,WireResponse=new JObject{["content"]="",["thinking"]=state,["tool_calls"]=new JArray(new JObject{["id"]=id,["function"]=new JObject{["name"]="inspect_object",["arguments"]=new JObject{["target"]="owned:fixture"}}})}.ToString()};
            var answer=new AssistantMessage();answer.Attempts.Add(Attempt(1,"OLD_ACCEPTED_STATE","old-call"));answer.Steps.Add(new EditorCommandResult{Tool="inspect_object",Success=true,Summary="{\"observed\":1}"});
            answer.Attempts.Add(new EditorPlanAttempt{Round=2,Accepted=false,RepairKind="native-fresh-plan",Error="Semantic rejection",WireResponse=Attempt(2,"OLD_REJECTED_STATE","rejected-call").WireResponse});
            var fresh=new CompletionRequest();Assert.IsFalse(NativeHistory(fresh,answer));Assert.IsTrue(fresh.Messages.Any(m=>m.Content.Contains("Fresh native planning epoch")));Assert.IsFalse(fresh.Messages.Any(m=>m.Thinking!=null));
            answer.Steps.Add(new EditorCommandResult{Tool="inspect_object",Success=true,Summary="{\"observed\":2}"});var next=Attempt(3,"NEW_EPOCH_STATE","new-call");next.FirstResultIndex=1;answer.Attempts.Add(next);
            var restored=JsonConvert.DeserializeObject<AssistantMessage>(JsonConvert.SerializeObject(answer));var replay=new CompletionRequest();Assert.IsTrue(NativeHistory(replay,restored));
            Assert.AreEqual(1,replay.Messages.Count(m=>m.Role=="assistant"));Assert.IsTrue(replay.Messages.Any(m=>m.Thinking=="NEW_EPOCH_STATE"));Assert.IsFalse(replay.Messages.Any(m=>(m.Thinking??"").Contains("OLD_")));
            Assert.AreEqual("new-call",replay.Messages.Single(m=>m.Role=="tool").ToolCallId);Assert.AreEqual(2,restored.Steps.Count,"Actual observations remain durable across replanning");
        }
        sealed class FreshPlanningProvider:ILLMProvider
        {
            public int Calls;public bool FreshEpochSeen,NativePreserved=true,OriginalScopeSeen,ActualObservationSeen;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                Calls++;NativePreserved&=request.UseToolCalls;
                if(Calls==4){FreshEpochSeen=request.Messages.Any(m=>m.Content.Contains("Fresh native planning epoch"));OriginalScopeSeen=request.Messages.Any(m=>m.Content.Contains("Через C# создай один объект."));ActualObservationSeen=request.Messages.Any(m=>m.Content.Contains("execute_editor_code")&&m.Content.Contains("returnedData"));}
                var plan=new EditorPlan{reply="Продолжаю",complete=Calls>=5};
                if(Calls<=3)plan.actions.Add(Code("return target==null;"));else if(Calls==4)plan.actions.Add(Code("return new GameObject(\"FreshPlanResult\");"));
                var calls=new JArray(plan.actions.Select((c,i)=>new JObject{["id"]="fixture_"+Calls+"_"+i,["function"]=new JObject{["name"]=c.tool,["arguments"]=new JObject{["code"]=c.args["code"]}}}));
                return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan),WireResponse=new JObject{["content"]="",["thinking"]="OPAQUE_FIXTURE_"+Calls,["tool_calls"]=calls}.ToString(),Model="controlled-fresh-plan-fixture"});
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);public void Dispose(){}
        }
        [Test] public async Task RepeatedSemanticRejectionGetsOneFreshNativePlanWithoutLosingTheTask()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var provider=new FreshPlanningProvider();
            try
            {
                using(var controller=new ChatController(_=>provider,Path.Combine("UserSettings","FreshNativePlan-"+Guid.NewGuid().ToString("N"))))
                {
                    controller.Settings.AllowEditorCode=true;controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=false;controller.Settings.AutoVerify=false;controller.Settings.Power=AssistantPower.Deep;
                    await controller.SendAsync("Через C# создай один объект.");var answer=controller.Current.Messages.Last();
                    Assert.IsFalse(answer.IsError,answer.Text);Assert.AreEqual(5,provider.Calls);Assert.IsTrue(provider.FreshEpochSeen);Assert.IsTrue(provider.NativePreserved);Assert.IsTrue(provider.OriginalScopeSeen);Assert.IsTrue(provider.ActualObservationSeen);
                    Assert.AreEqual(1,answer.Attempts.Count(a=>a.RepairKind=="native-fresh-plan"));Assert.AreEqual(2,answer.Steps.Count(s=>s.Tool=="execute_editor_code"));Assert.IsTrue(GameObject.Find("FreshPlanResult"));
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        sealed class AvailabilityPlanningProvider:ILLMProvider
        {
            public int Calls;public bool FreshEpochSeen,NativePreserved=true;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                Calls++;NativePreserved&=request.UseToolCalls;
                if(Calls<=2)throw new ProviderException("FUNCTION_PROTOCOL: function 'plan_task' was not advertised. No action executed. Advertised: execute_editor_code, finish_task.");
                if(Calls==3)FreshEpochSeen=request.Messages.Any(m=>m.Content.Contains("Fresh native planning epoch"));
                var plan=new EditorPlan{reply="Продолжаю",complete=Calls>=4};if(Calls==3)plan.actions.Add(Code("return new GameObject(\"AfterAvailabilityReset\");"));
                return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan),Model="availability-fresh-plan-fixture"});
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);public void Dispose(){}
        }
        [Test] public async Task RepeatedUnavailableFunctionGetsABoundedFreshNativeContext()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var provider=new AvailabilityPlanningProvider();
            try
            {
                using(var controller=new ChatController(_=>provider,Path.Combine("UserSettings","AvailabilityNativePlan-"+Guid.NewGuid().ToString("N"))))
                {
                    controller.Settings.AllowEditorCode=true;controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=false;controller.Settings.AutoVerify=false;controller.Settings.Power=AssistantPower.Deep;
                    await controller.SendAsync("Через C# создай один объект.");var answer=controller.Current.Messages.Last();Assert.IsFalse(answer.IsError,answer.Text);
                    Assert.AreEqual(4,provider.Calls);Assert.IsTrue(provider.FreshEpochSeen);Assert.IsTrue(provider.NativePreserved);Assert.AreEqual(1,answer.Attempts.Count(a=>a.RepairKind=="native-fresh-plan"));Assert.IsTrue(GameObject.Find("AfterAvailabilityReset"));
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        sealed class RepeatingApproachProvider:ILLMProvider
        {
            public int Calls;public bool FreshEpochSeen;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                Calls++;if(Calls==4)FreshEpochSeen=request.Messages.Any(m=>m.Content.Contains("Fresh native planning epoch"));
                var plan=new EditorPlan{reply="Продолжаю",complete=Calls>=5};
                if(Calls<=3)plan.actions.Add(new EditorCommand{tool="plan_task",args=new JObject{["steps"]=new JArray("Создать один объект и проверить его наличие.")}});
                else if(Calls==4)plan.actions.Add(Code("return new GameObject(\"AfterRepeatedApproach\");"));
                return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan),Model="repeated-approach-fixture"});
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);public void Dispose(){}
        }
        [Test] public async Task RepeatingAnUnchangedApproachDoesNotMasqueradeAsTaskExecution()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var provider=new RepeatingApproachProvider();
            try
            {
                using(var controller=new ChatController(_=>provider,Path.Combine("UserSettings","RepeatedApproach-"+Guid.NewGuid().ToString("N"))))
                {
                    controller.Settings.AllowEditorCode=true;controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=false;controller.Settings.AutoVerify=false;controller.Settings.Power=AssistantPower.Deep;
                    await controller.SendAsync("Через C# создай один объект.");var answer=controller.Current.Messages.Last();Assert.IsFalse(answer.IsError,answer.Text);
                    Assert.AreEqual(5,provider.Calls);Assert.IsTrue(provider.FreshEpochSeen);Assert.AreEqual(1,answer.Steps.Count(v=>v.Tool=="plan_task"));Assert.AreEqual(1,answer.Steps.Count(v=>v.Tool=="execute_editor_code"));
                    Assert.AreEqual(2,answer.Attempts.Count(a=>!a.Accepted&&(a.Error??"").Contains("This exact tentative approach")));Assert.IsTrue(GameObject.Find("AfterRepeatedApproach"));
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        sealed class CapturingReviewProvider:ILLMProvider
        {
            public JObject Input;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token){Input=JObject.Parse(request.Messages.Last().Content);return Task.FromResult(new CompletionResult{Text="{\"passed\":false,\"issues\":[\"Actual behavior needs verification\"],\"evidence\":\"Controlled review input capture\"}",Model="review-input-fixture"});}
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);public void Dispose(){}
        }
        [Test] public async Task FailedRuntimeEvidenceDoesNotBecomeHistoricalAfterAnActualEditorRead()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            try
            {
                var go=new GameObject("ReviewValueSubject");go.AddComponent<GeneratedSceneObject>();var light=go.AddComponent<Light>();light.intensity=0;
                using(var controller=new ChatController(historyFolder:Path.Combine("UserSettings","ReviewReadChronology-"+Guid.NewGuid().ToString("N"))))
                {
                    var context=Context(controller);var answer=context.Message;answer.Task=AgentTaskVerifier.Create("Проверь фактическое поведение света",false);
                    answer.Steps.Add(new EditorCommandResult{Tool="job_result",Target="fixture-observation",Success=false,Summary="{\"Kind\":\"runtime_probe\",\"Status\":\"failed\",\"Success\":false,\"Samples\":1,\"ProbeAssertions\":[{\"name\":\"measured value\",\"passed\":false,\"observed\":0}]}"});
                    var read=await Execute(Code("return GameObject.Find(\"ReviewValueSubject\").GetComponent<Light>().intensity;"),context);Assert.IsFalse(Progress(read));answer.Steps.Add(read);
                    var reviewer=new CapturingReviewProvider();var method=typeof(ChatController).GetMethod("ReviewEditorResultAsync",BindingFlags.Instance|BindingFlags.NonPublic);
                    await (Task<EditorResultReview>)method.Invoke(controller,new object[]{answer,reviewer,CancellationToken.None,-1});
                    Assert.IsFalse((bool)reviewer.Input["ActualToolEvidence"][0]["HistoricalObservation"],"A read must not erase the earlier failed behavioral observation");
                    Assert.IsFalse((bool)reviewer.Input["DurableHostVerification"][0]["HistoricalBeforeLastChange"],"Both host evidence views must agree");
                    var write=await Execute(Code("var light=GameObject.Find(\"ReviewValueSubject\").GetComponent<Light>(); Undo.RecordObject(light,\"Change light\"); light.intensity=3; return light.intensity;"),context);Assert.IsTrue(Progress(write));answer.Steps.Add(write);
                    await (Task<EditorResultReview>)method.Invoke(controller,new object[]{answer,reviewer,CancellationToken.None,-1});
                    Assert.IsTrue((bool)reviewer.Input["ActualToolEvidence"][0]["HistoricalObservation"]);Assert.IsTrue((bool)reviewer.Input["DurableHostVerification"][0]["HistoricalBeforeLastChange"],"An actual intervening change requires a fresh runtime check");
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        sealed class ReadOnlyEditorProvider:ILLMProvider
        {
            int calls;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token){var plan=new EditorPlan{reply="Значение прочитано",complete=++calls>=2};if(calls==1)plan.actions.Add(Code("return target==null;"));return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan),Model="read-status-fixture"});}
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);public void Dispose(){}
        }
        [Test] public async Task SuccessfulEditorInspectionDoesNotReportAppliedChanges()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            try
            {
                using(var controller=new ChatController(_=>new ReadOnlyEditorProvider(),Path.Combine("UserSettings","ReadStatus-"+Guid.NewGuid().ToString("N"))))
                {
                    controller.Settings.AllowEditorCode=true;controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=false;controller.Settings.AutoVerify=false;controller.Settings.Power=AssistantPower.Deep;
                    await controller.SendAsync("Изучи текущее значение через C# и сообщи наблюдение.");var answer=controller.Current.Messages.Last();Assert.IsFalse(answer.IsError,answer.Text);
                    Assert.AreEqual(1,answer.Steps.Count(v=>v.Tool=="execute_editor_code"));Assert.IsFalse(Progress(answer.Steps.Single(v=>v.Tool=="execute_editor_code")));Assert.AreNotEqual("Изменения применены",answer.Status);
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        sealed class RepeatingProvider:ILLMProvider
        {
            public int Calls;public bool SawActualReplayDiagnostic,NativePreserved=true;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                Calls++;NativePreserved&=request.UseToolCalls;SawActualReplayDiagnostic|=request.Messages.Any(m=>m.Content.Contains("Its result is still current"));
                var plan=new EditorPlan{reply="Продолжаю",complete=Calls>=4};
                if(Calls<=2)plan.actions.Add(Code("return target==null;"));
                else if(Calls==3)plan.actions.Add(Code("return new GameObject(\"AfterReplayDiagnostic\");"));
                return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan),Model="controlled-replay-fixture"});
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);
            public void Dispose(){}
        }
        [Test] public async Task ControllerRejectsRepeatedNoopAndLetsTheModelChooseAnotherOperation()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var provider=new RepeatingProvider();
            try
            {
                using(var controller=new ChatController(_=>provider,Path.Combine("UserSettings","EditorReplayController-"+Guid.NewGuid().ToString("N"))))
                {
                    controller.Settings.AllowEditorCode=true;controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=false;controller.Settings.AutoVerify=false;controller.Settings.Power=AssistantPower.Deep;
                    await controller.SendAsync("Через C# создай один объект.");var answer=controller.Current.Messages.Last();
                    Assert.IsFalse(answer.IsError,answer.Text);Assert.AreEqual(4,provider.Calls);Assert.IsTrue(provider.SawActualReplayDiagnostic);Assert.IsTrue(provider.NativePreserved);
                    Assert.AreEqual(2,answer.Steps.Count(s=>s.Tool=="execute_editor_code"),"The duplicate observation must not execute");
                    Assert.IsTrue(answer.Attempts.Any(a=>!a.Accepted&&(a.Error??"").Contains("Its result is still current")));Assert.IsTrue(GameObject.Find("AfterReplayDiagnostic"));
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
    }
}
