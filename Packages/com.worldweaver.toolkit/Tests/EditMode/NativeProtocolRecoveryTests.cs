using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver.Editor.Assistant;
namespace WorldWeaver.Tests
{
    public sealed class NativeProtocolRecoveryTests
    {
        sealed class Provider:ILLMProvider
        {
            public int Calls;public bool JsonSeen,RejectionSeen;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                Calls++;
                if(Calls<=2)
                {
                    Assert.IsTrue(request.UseToolCalls);Assert.IsFalse(GameObject.Find("ProtocolRecoveredGoal"),"Malformed calls must not execute");
                    string wire=new JObject{["tool_calls"]=new JArray(new JObject{["id"]="invalid_"+Calls,["function"]=new JObject{["name"]="create_object",["arguments"]=new JObject{["name"]="ProtocolRecoveredGoal"}}})}.ToString(Formatting.None);
                    throw new ProviderException("FUNCTION_PROTOCOL: create_object is missing required parameters: kind. No action executed.",wireResponse:wire);
                }
                JsonSeen|=!request.UseToolCalls;RejectionSeen|=request.Messages.Any(m=>m.Role=="tool"&&m.Content.Contains("missing required parameters: kind"));
                Assert.IsFalse(request.UseToolCalls);Assert.AreEqual(false,request.EnableThinking);
                var plan=new EditorPlan{complete=Calls>3};if(Calls==3)plan.actions.Add(new EditorCommand{tool="create_node",name="ProtocolRecoveredGoal"});
                return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan)});
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);
            public void Dispose(){}
        }
        [Test] public async Task RepeatedMalformedNativeCallsRecoverThroughTheSameValidatedJsonContract()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var provider=new Provider();
            try
            {
                using(var controller=new ChatController(_=>provider,"UserSettings/ProtocolRecovery-"+Guid.NewGuid().ToString("N")))
                {
                    controller.Settings.UseNativeFunctions=true;controller.Settings.AutoVerify=false;controller.Settings.UseSkills=false;controller.Settings.Power=AssistantPower.Deep;
                    await controller.SendAsync("Создай пустой узел ProtocolRecoveredGoal.");var answer=controller.Current.Messages.Last();
                    Assert.IsFalse(answer.IsError,answer.Text);Assert.IsTrue(provider.JsonSeen);Assert.IsTrue(provider.RejectionSeen);Assert.IsTrue(GameObject.Find("ProtocolRecoveredGoal"));
                    Assert.AreEqual(1,answer.Steps.Count(s=>s.Tool=="create_node"));Assert.AreEqual("native-protocol-to-json-contract",answer.Attempts[2].RepairKind);Assert.AreEqual(4,provider.Calls);
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        sealed class HostRejectionProvider:ILLMProvider
        {
            public int Calls;public bool Consecutive,JsonSeen;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                Calls++;JsonSeen|=!request.UseToolCalls;
                bool invalid=Consecutive?Calls<=2:Calls<=5&&Calls%2==1;
                var plan=new EditorPlan{complete=Calls>(Consecutive?3:6)};
                if(invalid)
                {
                    plan.actions.Add(new EditorCommand{tool="create_node",name="RejectedA"});
                    plan.actions.Add(new EditorCommand{tool="create_node",name="RejectedB"});
                }
                else if(Calls<(Consecutive?3:6))plan.actions.Add(new EditorCommand{tool="list_scene_objects"});
                else if(!plan.complete)plan.actions.Add(new EditorCommand{tool="create_node",name="HostRecoveredGoal"});
                return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan)});
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);
            public void Dispose(){}
        }
        [TestCase(false)] [TestCase(true)]
        public async Task HostValidationRecoveryCountsConsecutiveFailuresAndNeverAppliesRejectedMutations(bool consecutive)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var provider=new HostRejectionProvider{Consecutive=consecutive};
            try
            {
                using(var controller=new ChatController(_=>provider,"UserSettings/HostRecovery-"+Guid.NewGuid().ToString("N")))
                {
                    controller.Settings.UseNativeFunctions=true;controller.Settings.AutoVerify=false;controller.Settings.UseSkills=false;controller.Settings.Power=AssistantPower.Deep;
                    await controller.SendAsync("Создай пустой узел HostRecoveredGoal.");var answer=controller.Current.Messages.Last();
                    Assert.IsFalse(answer.IsError,answer.Text);Assert.IsTrue(GameObject.Find("HostRecoveredGoal"));Assert.IsFalse(GameObject.Find("RejectedA"));Assert.IsFalse(GameObject.Find("RejectedB"));
                    Assert.AreEqual(consecutive,provider.JsonSeen);Assert.AreEqual(consecutive?4:7,provider.Calls);
                    Assert.AreEqual(consecutive?2:3,answer.Attempts.Count(a=>!a.Accepted));
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        sealed class ComponentPermissionProvider:ILLMProvider
        {
            public int Calls;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                Calls++;var plan=new EditorPlan{complete=Calls>2};
                if(Calls==1)plan.actions.Add(new EditorCommand{tool="create_node",name="PermissionComponentGoal"});
                if(Calls==2)plan.actions.Add(new EditorCommand{tool="add_component",target="PermissionComponentGoal",args=new JObject{["type"]="WorldWeaver.WorldPlayer"}});
                return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan)});
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);
            public void Dispose(){}
        }
        [TestCase(false)] [TestCase(true)]
        public async Task AutonomousScriptPermissionAlsoControlsTypedMonoBehaviourAttachment(bool allow)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var provider=new ComponentPermissionProvider();
            try
            {
                using(var controller=new ChatController(_=>provider,"UserSettings/ComponentPermission-"+Guid.NewGuid().ToString("N")))
                {
                    controller.Settings.UseNativeFunctions=true;controller.Settings.AutoVerify=false;controller.Settings.UseSkills=false;controller.Settings.Power=AssistantPower.Deep;controller.Settings.AllowAutonomousScripts=allow;
                    await controller.SendAsync("Создай пустой узел PermissionComponentGoal и добавь к нему компонент WorldWeaver.WorldPlayer.");var answer=controller.Current.Messages.Last();
                    Assert.IsFalse(answer.IsError,answer.Text);Assert.AreEqual(allow,GameObject.Find("PermissionComponentGoal").GetComponent<WorldPlayer>()!=null);
                    Assert.AreEqual(!allow,answer.PendingPlan!=null);Assert.AreEqual(allow?3:2,provider.Calls);
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        sealed class SemanticRepairProvider:ILLMProvider
        {
            public int Calls;public bool JsonSeen;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                Calls++;JsonSeen|=!request.UseToolCalls;
                if(Calls==1)return Task.FromResult(new CompletionResult{Text="```json\n{}\n```"});
                var plan=new EditorPlan{complete=Calls>3};
                if(Calls==2)plan.actions.Add(new EditorCommand{tool="set_transform",target="NoSuchSceneTarget",args=new JObject{["position"]=new JArray(0,0,0)}});
                if(Calls==3)plan.actions.Add(new EditorCommand{tool="create_node",name="SemanticRecoveredGoal"});
                return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan)});
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);
            public void Dispose(){}
        }
        [Test] public async Task MissingSceneTargetAfterOneFormatErrorDoesNotDisableWorkingNativeTools()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var provider=new SemanticRepairProvider();
            try
            {
                using(var controller=new ChatController(_=>provider,"UserSettings/SemanticRecovery-"+Guid.NewGuid().ToString("N")))
                {
                    controller.Settings.UseNativeFunctions=true;controller.Settings.AutoVerify=false;controller.Settings.UseSkills=false;controller.Settings.Power=AssistantPower.Deep;
                    await controller.SendAsync("Создай пустой узел SemanticRecoveredGoal.");var answer=controller.Current.Messages.Last();
                    Assert.IsFalse(answer.IsError,answer.Text);Assert.IsFalse(provider.JsonSeen);Assert.AreEqual(4,provider.Calls);Assert.IsTrue(GameObject.Find("SemanticRecoveredGoal"));Assert.AreEqual(1,answer.Steps.Count(s=>s.Tool=="create_node"));
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        sealed class AvailabilityRepairProvider:ILLMProvider
        {
            public int Calls;public bool JsonSeen;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                Calls++;JsonSeen|=!request.UseToolCalls;
                if(Calls==1)throw new ProviderException("FUNCTION_PROTOCOL: functions not advertised in this turn: inspect_component, read_script. No action executed. Available now: plan_task, finish_task.");
                if(Calls==2)throw new ProviderException("FUNCTION_PROTOCOL: function 'discover_tools' was not advertised. No action executed. Advertised: plan_task, finish_task.");
                var plan=new EditorPlan{complete=Calls>3};
                if(Calls==3)plan.actions.Add(new EditorCommand{tool="create_node",name="AvailabilityRecoveredGoal"});
                return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan)});
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);
            public void Dispose(){}
        }
        [Test] public async Task TwoWrongPhaseCallsCannotSwitchAWorkingNativeConnectionToTextMode()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var provider=new AvailabilityRepairProvider();
            try
            {
                using(var controller=new ChatController(_=>provider,"UserSettings/AvailabilityRecovery-"+Guid.NewGuid().ToString("N")))
                {
                    controller.Settings.UseNativeFunctions=true;controller.Settings.AutoVerify=false;controller.Settings.UseSkills=false;controller.Settings.Power=AssistantPower.Deep;
                    await controller.SendAsync("Создай пустой узел AvailabilityRecoveredGoal.");var answer=controller.Current.Messages.Last();
                    Assert.IsFalse(answer.IsError,answer.Text);Assert.IsFalse(provider.JsonSeen);Assert.AreEqual(4,provider.Calls);
                    Assert.IsTrue(GameObject.Find("AvailabilityRecoveredGoal"));Assert.AreEqual(1,answer.Steps.Count(s=>s.Tool=="create_node"));
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        [Test] public void PlanningPhaseRejectionExplainsTheAvailableRecoveryInsteadOfRequestingDisabledDiscovery()
        {
            var schema=EditorPlan.Schema(true,nativeFunctions:true);
            var branches=(JArray)schema["properties"]["actions"]["items"]["anyOf"];
            schema["properties"]["actions"]["items"]["anyOf"]=new JArray(branches.OfType<JObject>().Where(b=>EditorToolSchema.ToolName(b)=="plan_task").Select(b=>b.DeepClone()));
            var message=new JObject{["tool_calls"]=new JArray(new JObject{["function"]=new JObject{["name"]="discover_tools",["arguments"]=new JObject()}})};
            var error=Assert.Throws<ProviderException>(()=>AgentFunctionProtocol.Normalize(message,schema));
            Assert.That(error.Message,Does.Contain("Complete the required plan_task step first"));Assert.That(error.Message,Does.Contain("Discovery is not enabled"));Assert.That(error.Message,Does.Not.Contain("Discover the exact missing function"));
            Assert.IsTrue(AgentFunctionProtocol.IsToolAvailabilityError(error.Message));
        }
        sealed class PlannedReadLoopProvider:ILLMProvider
        {
            public int Calls;public bool Narrowed;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                Calls++;Assert.IsTrue(request.UseToolCalls,"Valid read calls cannot disable native encoding");
                var plan=new EditorPlan{complete=Calls>7};
                if(Calls==1)plan.actions.Add(new EditorCommand{tool="plan_task",args=new JObject{["steps"]=new JArray("Inspect the current scene","Create the requested node","Verify the original request")}});
                else if(Calls<=6)plan.actions.Add(new EditorCommand{tool="list_scene_objects"});
                else if(Calls==7)
                {
                    var tools=AgentFunctionProtocol.Definitions(request.Schema).Select(d=>(string)d["function"]["name"]).ToArray();
                    Narrowed=!tools.Contains("list_scene_objects")&&tools.Contains("discover_tools")&&tools.Contains("finish_task");
                    Assert.IsTrue(Narrowed,"A planned task must leave repeated general inspection after six observation rounds");
                    plan.actions.Add(new EditorCommand{tool="create_node",name="PlannedReadLoopGoal"});
                }
                return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan)});
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);
            public void Dispose(){}
        }
        [Test] public async Task ProposedStagesCannotBypassObservationBoundsOrTriggerAnUnneededProtocolSwitch()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var provider=new PlannedReadLoopProvider();
            try
            {
                using(var controller=new ChatController(_=>provider,"UserSettings/PlannedReadLoop-"+Guid.NewGuid().ToString("N")))
                {
                    controller.Settings.UseNativeFunctions=true;controller.Settings.AutoVerify=false;controller.Settings.UseSkills=false;controller.Settings.Power=AssistantPower.Deep;controller.Settings.AllowAutonomousScripts=true;
                    await controller.SendAsync("Создай пустой узел PlannedReadLoopGoal.");var answer=controller.Current.Messages.Last();
                    Assert.IsFalse(answer.IsError,answer.Text);Assert.IsTrue(provider.Narrowed);Assert.AreEqual(8,provider.Calls);Assert.IsTrue(GameObject.Find("PlannedReadLoopGoal"));
                    Assert.AreEqual(1,answer.Steps.Count(s=>s.Tool=="create_node"));
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
    }
}
