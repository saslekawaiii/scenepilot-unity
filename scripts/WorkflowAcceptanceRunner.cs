using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;

/// <summary>Opt-in disposable-project test. Injected plans, real source writes/compiler/reloads/Play tests/restore.</summary>
[InitializeOnLoad]
public static class WorkflowAcceptanceRunner
{
    const string StatePath="UserSettings/WorkflowAcceptance.json",Health="Assets/WorkflowHealth.cs",Damage="Assets/WorkflowDamage.cs";
    const string HealthBefore="using UnityEngine; public class WorkflowHealth:MonoBehaviour { public int Maximum=10; public int Current; public bool Dead=>Current<=0; void OnEnable(){Current=Maximum;} public void Damage(int amount){Current=Mathf.Max(0,Current-amount);} }";
    const string DamageBefore="using UnityEngine; public class WorkflowDamage:MonoBehaviour { public int Amount=2; }";
    [Serializable] sealed class State
    {
        public string Phase,History,Session,Message,Job;
        public List<string> Events=new List<string>();
        public int Reloads;
    }
    static bool stepping;
    static WorkflowAcceptanceRunner()
    {
        if(Environment.GetEnvironmentVariable("WW_WORKFLOW_ACCEPTANCE")=="1")
        {
            if(File.Exists(StatePath)){var state=Load();state.Reloads++;Save(state);}
            EditorApplication.update+=Tick;
        }
    }
    static State Load()=>JsonConvert.DeserializeObject<State>(File.ReadAllText(StatePath));
    static void Save(State state){File.WriteAllText(StatePath,JsonConvert.SerializeObject(state,Formatting.Indented));}
    static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    static EditorCommand Patch(string path,string find,string replacement)=>new EditorCommand{tool="patch_script",args=new JObject{["path"]=path,["expected_sha256"]=Hash(path),["find"]=find,["replace"]=replacement}};
    static string Plan(params EditorCommand[] actions)=>JsonConvert.SerializeObject(new EditorPlan{reply="Контрольный план теста",complete=actions.Length==0,actions=actions.ToList()});
    sealed class Provider:ILLMProvider
    {
        readonly Func<string> response;public Provider(Func<string> value){response=value;}
        public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)=>Task.FromResult(new CompletionResult{Text=response(),Model="injected-workflow-fixture"});
        public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);
        public void Dispose(){}
    }
    public static void Run()
    {
        if(Environment.GetEnvironmentVariable("WW_WORKFLOW_ACCEPTANCE")!="1"||!Application.isBatchMode)throw new InvalidOperationException("Explicit disposable batch run required.");
        if(File.Exists(Health)||File.Exists(Damage)||File.Exists(StatePath))throw new InvalidOperationException("Fixture destinations must be absent.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var state=new State{Phase="initial",History=Path.GetFullPath("UserSettings/WorkflowHistory")};Save(state);
        File.WriteAllText(Health,HealthBefore);File.WriteAllText(Damage,DamageBefore);
        Directory.CreateDirectory("Assets/WorkflowTests");
        File.WriteAllText("Assets/WorkflowTests/WorkflowTests.asmdef","{\"name\":\"WorkflowTests\",\"optionalUnityReferences\":[\"TestAssemblies\"]}");
        File.WriteAllText("Assets/WorkflowTests/HealthWorkflowPlayTests.cs",@"using System;using System.Collections;using System.Linq;using NUnit.Framework;using UnityEngine;using UnityEngine.TestTools;
public class HealthWorkflowPlayTests{[UnityTest]public IEnumerator DamageAndDeath(){var types=AppDomain.CurrentDomain.GetAssemblies();var health=types.Select(a=>a.GetType(""WorkflowHealth"")).First(t=>t!=null);var damage=types.Select(a=>a.GetType(""WorkflowDamage"")).First(t=>t!=null);var go=new GameObject(""Health behavior probe"");try{var h=go.AddComponent(health);var d=go.AddComponent(damage);yield return null;Assert.That((int)health.GetField(""Maximum"").GetValue(h),Is.EqualTo(50));Assert.That((int)damage.GetField(""Amount"").GetValue(d),Is.EqualTo(15));health.GetMethod(""Damage"").Invoke(h,new object[]{15});Assert.That((int)health.GetField(""Current"").GetValue(h),Is.EqualTo(35));Assert.That((bool)health.GetProperty(""Dead"").GetValue(h),Is.False);health.GetMethod(""Damage"").Invoke(h,new object[]{50});Assert.That((int)health.GetField(""Current"").GetValue(h),Is.EqualTo(0));Assert.That((bool)health.GetProperty(""Dead"").GetValue(h),Is.True);}finally{UnityEngine.Object.Destroy(go);}}}");
        AssetDatabase.Refresh();
    }
    static ChatController Controller(State state,Func<string> response)
    {
        var controller=new ChatController(_=>new Provider(response),state.History);if(state.Session!=null)controller.OpenChat(state.Session);controller.Settings.Power=AssistantPower.Balanced;controller.Settings.Mode=AssistantMode.Execute;controller.SaveSettings();return controller;
    }
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static async void Tick()
    {
        if(stepping||!File.Exists(StatePath)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||ScriptDrafts.Pending||AgentSourcePatches.Pending||AgentEditorJobs.Busy)return;
        stepping=true;var state=Load();
        try
        {
            if(state.Phase=="initial")
            {
                using(var controller=Controller(state,()=>Plan(Patch(Health,"Maximum=10","Maximum=BrokenSymbol"),Patch(Damage,"Amount=2","Amount=15"))))
                {
                    await controller.SendAsync("Измени C# скрипт здоровья до 50 и урона до пятнадцати в двух существующих файлах. Затем проверь получение урона и смерть реальным Play Mode тестом.");
                    var answer=controller.Current.Messages.Last();Require(answer.PendingPlan?.actions.Count==2,"Expected a reviewed two-file patch.");Require(File.ReadAllText(Health)==HealthBefore,"Plan mutated before review.");
                    state.Session=controller.Current.Id;state.Message=answer.Id;state.Phase="failed_compilation";state.Events.Add("Two-file diff reviewed by this explicit test runner; deliberately invalid symbol exercises bounded compile repair.");Save(state);
                    await controller.ApplyPendingAsync(answer.Id);Require(answer.PendingPlan==null,"Reviewed patch did not apply.");
                }
            }
            else if(state.Phase=="failed_compilation")
            {
                using(var controller=Controller(state,()=>Plan(Patch(Health,"Maximum=BrokenSymbol","Maximum=50"))))
                {
                    var answer=controller.Current.Messages.Single(m=>m.Id==state.Message);Require(answer.Steps.Any(s=>s.Tool=="compile_patch"&&!s.Success),"Actual compiler failure was not recorded.");Require(answer.Task.ContinueAfterReload,"Compiler continuation was lost.");
                    state.Phase="repaired";state.Events.Add("Compiler error observed; repair limited to reviewed Health file.");Save(state);await controller.ResumeAsync(answer.Id);Require(answer.PendingPlan==null,"In-scope compiler repair unexpectedly needed new approval.");
                }
            }
            else if(state.Phase=="repaired")
            {
                using(var controller=Controller(state,()=>Plan(new EditorCommand{tool="run_tests",args=new JObject{["mode"]="play",["filter"]="HealthWorkflowPlayTests"}})))
                {
                    var answer=controller.Current.Messages.Single(m=>m.Id==state.Message);Require(answer.Steps.Any(s=>s.Tool=="compile_patch"&&s.Success),"Repair compilation did not pass.");
                    state.Phase="play";state.Events.Add("Successful compiler callback survived reload; request resumed into actual TestRunner job.");Save(state);await controller.ResumeAsync(answer.Id);state.Job=answer.Task.PendingJobId;Require(!string.IsNullOrEmpty(state.Job),"Play test job not queued.");Save(state);
                }
            }
            else if(state.Phase=="play")
            {
                var job=AgentEditorJobs.Load(state.Job);Require(job!=null&&job.Success&&job.Passed==1&&job.Failed==0,"Actual damage/death Play test failed.");
                using(var controller=Controller(state,()=>Plan()))
                {
                    await controller.ResumeAsync(state.Message);var answer=controller.Current.Messages.Single(m=>m.Id==state.Message);Require(!answer.IsError&&answer.Task.Checks.Count>=3&&answer.Task.Checks.All(c=>c.Passed),"Failed evidence was cleared or task checks were lost.");Require(answer.Task.ResumeAttempts==3,"Unexpected continuation count.");
                    state.Events.Add("Actual Play damage/death checks pass; final empty model reply accepted only with passing independent evidence.");
                    state.Phase="restored";Save(state);
                    var journal=JObject.Parse(File.ReadAllText("UserSettings/WorldWeaverCheckpoints/"+answer.Task.CheckpointId+"/journal.json"));
                    string prior=(string)journal["ScenesAfter"][0]["Hash"];
                    File.Copy("UserSettings/WorldWeaverCheckpoints/"+answer.Task.CheckpointId+"/content/"+prior,"UserSettings/workflow-scene-before.yaml",true);
                    string temporary="Assets/__WorldWeaverCheckpoint-workflow-debug.unity";
                    EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),temporary,true);File.Copy(temporary,"UserSettings/workflow-scene-after.yaml",true);AssetDatabase.DeleteAsset(temporary);
                    controller.RestoreTask(answer.Id);Require(answer.Task.Status=="restored","Checkpoint restore failed: "+answer.Steps.Last().Summary);
                }
            }
            else if(state.Phase=="restored")
            {
                Require(File.ReadAllText(Health)==HealthBefore&&File.ReadAllText(Damage)==DamageBefore,"Original source bytes not restored.");
                var history=new ChatHistoryStore(state.History).Load().Single(s=>s.Id==state.Session);var answer=history.Messages.Single(m=>m.Id==state.Message);
                Require(!answer.Task.ContinueAfterReload&&answer.Task.Status=="restored","Restored task would resume again.");
                var report=new JObject{["kind"]="injected-plan-real-editor-workflow",["success"]=true,["unityVersion"]=Application.unityVersion,["reloads"]=state.Reloads,["events"]=JArray.FromObject(state.Events),["message"]=JObject.FromObject(answer),["job"]=JObject.FromObject(AgentEditorJobs.Load(state.Job)),["llmInferencePerformed"]=false,["scope"]="Controller review/2-source patch/compiler failure/bounded repair/reload/TestRunner Play damage-death/checkpoint restore"};
                Finish(report,0);
            }
        }
        catch(Exception error){Finish(new JObject{["success"]=false,["phase"]=state.Phase,["error"]=error.ToString(),["events"]=JArray.FromObject(state.Events)},1);}
        finally{stepping=false;}
    }
    static void Finish(JObject report,int code)
    {
        string output=Environment.GetEnvironmentVariable("WW_WORKFLOW_RESULT");if(!string.IsNullOrEmpty(output))File.WriteAllText(output,report.ToString(Formatting.Indented));if(File.Exists(StatePath))File.Delete(StatePath);EditorApplication.Exit(code);
    }
}
