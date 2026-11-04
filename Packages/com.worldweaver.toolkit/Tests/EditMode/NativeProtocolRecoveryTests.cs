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
    }
}
