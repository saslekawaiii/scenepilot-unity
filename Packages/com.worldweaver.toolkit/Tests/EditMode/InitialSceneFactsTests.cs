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
    public sealed class InitialSceneFactsTests
    {
        sealed class Provider:ILLMProvider
        {
            public int Calls;public bool ReviewSawOriginalStaticScale;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {
                if(request.Schema?["properties"]?["passed"]!=null)
                {
                    var data=JObject.Parse(request.Messages.Last(m=>m.Role=="user").Content);var initial=JArray.Parse((string)data["InitialHostMeasurements"]);var ramp=initial.Single(o=>(string)o["name"]=="StaticScaleFixture");ReviewSawOriginalStaticScale=(float)ramp["localScale"][0]==6;
                    return Task.FromResult(new CompletionResult{Text="{\"passed\":true,\"issues\":[],\"evidence\":\"Measured static fixture\"}"});
                }
                Calls++;var plan=new EditorPlan{complete=Calls>1};if(Calls==1)plan.actions.Add(new EditorCommand{tool="set_transform",target=ProjectContextIndex.StableTarget(GameObject.Find("StaticScaleFixture")),args=new JObject{["scale"]=new JArray(7.2,.2,3)}});
                return Task.FromResult(new CompletionResult{Text=JsonConvert.SerializeObject(plan)});
            }
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);
            public void Dispose(){}
        }
        [Test] public async Task StaticGeometryMustExistInTheImmutableInitialMeasurementsForRelativeEdits()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="StaticScaleFixture";go.AddComponent<GeneratedSceneObject>();go.transform.localScale=new Vector3(6,.2f,3);var provider=new Provider();
            try
            {
                using(var controller=new ChatController(_=>provider,"UserSettings/InitialStatic-"+Guid.NewGuid().ToString("N")))
                {
                    controller.Settings.Power=AssistantPower.Deep;controller.Settings.UseNativeFunctions=true;controller.Settings.AutoVerify=true;controller.Settings.UseSkills=false;
                    await controller.SendAsync("Увеличь только локальный масштаб StaticScaleFixture по X на 20 процентов. Остальное сохрани.");var answer=controller.Current.Messages.Last();
                    Assert.IsFalse(answer.IsError,answer.Text);var initial=JArray.Parse(answer.Task.InitialSceneFacts);var stored=initial.Single(o=>(string)o["name"]==go.name);Assert.AreEqual(6,(float)stored["localScale"][0]);Assert.AreEqual(7.2f,go.transform.localScale.x,.0001f);Assert.IsTrue(provider.ReviewSawOriginalStaticScale,"Reviewer must receive the original static geometry, not just Rigidbody/terrain/player objects");
                }
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
    }
}
