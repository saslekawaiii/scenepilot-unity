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
