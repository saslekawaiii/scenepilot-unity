using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;
/// <summary>Opt-in isolated-project acceptance: actual previously reviewed 9B source and stationary negative control.</summary>
[InitializeOnLoad]
public static class RotationAcceptanceRunner
{
    const string State="UserSettings/RotationAcceptanceRunner.json";
    [Serializable] sealed class RunState { public string Phase,Positive,Negative; }
    static RotationAcceptanceRunner() { if(Environment.GetEnvironmentVariable("WW_ROTATION_ACCEPTANCE")=="1")EditorApplication.update+=Poll; }
    static void Save(RunState state)=>File.WriteAllText(State,JsonConvert.SerializeObject(state));
    public static void Run()
    {
        if(Environment.GetEnvironmentVariable("WW_ROTATION_ACCEPTANCE")!="1")throw new InvalidOperationException("Set WW_ROTATION_ACCEPTANCE=1 only in an isolated acceptance project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var go=new GameObject("Reviewed local 9B rotation");go.AddComponent<GeneratedSceneObject>();
        var type=Type.GetType("WorldWeaverSmoothRotateY, Assembly-CSharp");if(type==null)throw new InvalidOperationException("Import the exact reviewed WorldWeaverSmoothRotateY source first.");go.AddComponent(type);
        EditorSceneManager.SaveScene(go.scene,"Assets/RotationAcceptance.unity");
        var job=AgentEditorJobs.RunRotationCheck(ProjectContextIndex.StableTarget(go),2,30);Save(new RunState {Phase="positive",Positive=job.Id});
    }
    static void Poll()
    {
        if(!File.Exists(State)||EditorApplication.isPlayingOrWillChangePlaymode)return;
        var state=JsonConvert.DeserializeObject<RunState>(File.ReadAllText(State));if(state==null)return;
        try
        {
            if(state.Phase=="positive")
            {
                var positive=AgentEditorJobs.Load(state.Positive);if(positive==null||positive.Status=="queued"||positive.Status=="entering_play"||positive.Status=="running"||positive.Status=="exiting_play")return;
                var stationary=new GameObject("Stationary negative control");stationary.AddComponent<GeneratedSceneObject>();EditorSceneManager.SaveScene(stationary.scene,"Assets/RotationAcceptance.unity");
                var negative=AgentEditorJobs.RunRotationCheck(ProjectContextIndex.StableTarget(stationary),2,30);state.Negative=negative.Id;state.Phase="negative";Save(state);return;
            }
            if(state.Phase=="negative")
            {
                var negative=AgentEditorJobs.Load(state.Negative);if(negative==null||negative.Status=="queued"||negative.Status=="entering_play"||negative.Status=="running"||negative.Status=="exiting_play")return;
                var positive=AgentEditorJobs.Load(state.Positive);
                bool success=positive.Success&&!negative.Success&&positive.Samples>=3&&negative.Samples>=3&&negative.MeasurementStarted;
                var report=new JObject { ["schema"]=1,["kind"]="actual-runtime-rotation-and-negative-control",["unity"]=Application.unityVersion,["success"]=success,["positive"]=JObject.FromObject(positive),["negative"]=JObject.FromObject(negative),["reviewedSourceSha256"]=Hash(File.ReadAllBytes("Assets/WorldWeaverSmoothRotateY.cs")),["playModeExited"]=!EditorApplication.isPlaying,["llmInferencePerformed"]=false,["limitation"]="One exact previously reviewed script and one stationary control; not general C# correctness or a model reliability estimate." };
                string output=Environment.GetEnvironmentVariable("WW_ROTATION_RESULT");if(string.IsNullOrEmpty(output))throw new InvalidOperationException("Set WW_ROTATION_RESULT output path.");File.WriteAllText(output,report.ToString(Formatting.Indented));File.Delete(State);EditorApplication.Exit(success?0:1);
            }
        }
        catch(Exception error) { Debug.LogException(error);File.Delete(State);EditorApplication.Exit(2); }
    }
    static string Hash(byte[] data) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant(); }
}
