using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;
/// <summary>Opt-in isolated-project acceptance of the public job API and actual NUnit XML callbacks.</summary>
[InitializeOnLoad]
public static class EditorJobsAcceptanceRunner
{
    const string State="UserSettings/EditorJobsAcceptanceRunner.json";
    [Serializable] sealed class RunState { public string Phase,Edit,Play,Empty,Cancelled,Checkpoint,ObjectId; }
    static EditorJobsAcceptanceRunner() {if(Environment.GetEnvironmentVariable("WW_EDITOR_JOBS_ACCEPTANCE")=="1")EditorApplication.update+=Poll;}
    static void Save(RunState state)=>File.WriteAllText(State,JsonConvert.SerializeObject(state));
    public static void Run()
    {
        if(Environment.GetEnvironmentVariable("WW_EDITOR_JOBS_ACCEPTANCE")!="1")throw new InvalidOperationException("Run only in an isolated acceptance project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var original=GameObject.CreatePrimitive(PrimitiveType.Cube);original.name="Original unsaved user object";original.transform.position=Vector3.right*3;string objectId=original.AddComponent<GeneratedSceneObject>().ObjectId;
        RenderSettings.fog=true;RenderSettings.fogColor=Color.magenta;EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        string checkpoint=AgentCheckpoints.Begin(Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"));AgentCheckpoints.Seal(checkpoint);
        var cancelled=AgentEditorJobs.RunTests("edit","WorldWeaver.Tests.WorldGameplayEditorTests");AgentEditorJobs.Cancel(cancelled.Id);
        var edit=AgentEditorJobs.RunTests("edit","WorldWeaver.Tests.WorldGameplayEditorTests");Save(new RunState {Phase="edit",Edit=edit.Id,Cancelled=cancelled.Id,Checkpoint=checkpoint,ObjectId=objectId});
    }
    static bool Done(AgentEditorJob job)=>job!=null&&(job.Status=="completed"||job.Status=="failed"||job.Status=="cancelled"||job.Status=="interrupted");
    static void Poll()
    {
        if(!File.Exists(State)||EditorApplication.isPlayingOrWillChangePlaymode)return;
        var state=JsonConvert.DeserializeObject<RunState>(File.ReadAllText(State));if(state==null)return;
        try
        {
            if(state.Phase=="edit"&&Done(AgentEditorJobs.Load(state.Edit)))
            {
                if(!AgentCheckpoints.CanRestore(state.Checkpoint))throw new InvalidOperationException("TestRunner EditMode cleanup did not restore the original scene setup: "+AgentCheckpoints.Inspect(state.Checkpoint));
                var play=AgentEditorJobs.RunTests("play","WorldWeaver.Tests.(WorldGameplayTests|NavigationTests|AssistantBehaviorTests)");state.Play=play.Id;state.Phase="play";Save(state);return;
            }
            if(state.Phase=="play"&&Done(AgentEditorJobs.Load(state.Play)))
            {
                if(!AgentCheckpoints.CanRestore(state.Checkpoint))throw new InvalidOperationException("TestRunner PlayMode cleanup did not restore the original scene setup: "+AgentCheckpoints.Inspect(state.Checkpoint));
                var empty=AgentEditorJobs.RunTests("edit","WorldWeaver.Tests.DoesNotExistAcceptanceFixture");state.Empty=empty.Id;state.Phase="empty";Save(state);return;
            }
            if(state.Phase=="empty"&&Done(AgentEditorJobs.Load(state.Empty)))
            {
                var edit=AgentEditorJobs.Load(state.Edit);var play=AgentEditorJobs.Load(state.Play);var empty=AgentEditorJobs.Load(state.Empty);var cancelled=AgentEditorJobs.Load(state.Cancelled);
                var original=GameObject.Find("Original unsaved user object");bool userState=original&&original.transform.position==Vector3.right*3&&original.GetComponent<GeneratedSceneObject>().ObjectId==state.ObjectId&&RenderSettings.fog&&RenderSettings.fogColor==Color.magenta&&SceneManager.GetActiveScene().isDirty;
                bool cleanup=AgentCheckpoints.CanRestore(state.Checkpoint);bool success=edit.Success&&edit.Passed==4&&play.Success&&play.Passed==6&&!empty.Success&&empty.Passed==0&&cancelled.Status=="cancelled"&&string.IsNullOrEmpty(cancelled.RunGuid)&&cleanup&&userState;
                var report=new JObject { ["schema"]=1,["kind"]="public-editor-job-api-acceptance",["success"]=success,["edit"]=JObject.FromObject(edit),["play"]=JObject.FromObject(play),["emptyNegative"]=JObject.FromObject(empty),["cancelledQueued"]=JObject.FromObject(cancelled),["sceneCleanupVerified"]=cleanup,["dirtyObjectIdentityRenderSettingsPreserved"]=userState,["originalUntitledScenePath"]=SceneManager.GetActiveScene().path,["actualXmlExists"]=File.Exists(edit.ResultPath)&&File.Exists(play.ResultPath),["llmInferencePerformed"]=false };
                string path=Environment.GetEnvironmentVariable("WW_EDITOR_JOBS_RESULT");if(string.IsNullOrEmpty(path))throw new InvalidOperationException("Set WW_EDITOR_JOBS_RESULT.");File.WriteAllText(path,report.ToString(Formatting.Indented));File.Delete(State);EditorApplication.Exit(success?0:1);
            }
        }
        catch(Exception error) {UnityEngine.Debug.LogException(error);string result=Environment.GetEnvironmentVariable("WW_EDITOR_JOBS_RESULT");if(!string.IsNullOrEmpty(result))File.WriteAllText(result,new JObject{["success"]=false,["phase"]=state.Phase,["error"]=error.ToString(),["job"]=state.Phase=="edit"?JObject.FromObject(AgentEditorJobs.Load(state.Edit)):null}.ToString());File.Delete(State);EditorApplication.Exit(2);}
    }
}
