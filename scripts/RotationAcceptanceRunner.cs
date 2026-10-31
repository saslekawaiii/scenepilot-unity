using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;
/// <summary>Opt-in isolated-project acceptance: one exact reviewed source and a stationary negative control.</summary>
[InitializeOnLoad]
public static class RotationAcceptanceRunner
{
    const string State="UserSettings/RotationAcceptanceRunner.json";
    [Serializable] sealed class RunState { public string Phase,Positive,Negative,ClassName,SourcePath,SourceSha256,OutputPath; }
    static RotationAcceptanceRunner() { if(Environment.GetEnvironmentVariable("WW_ROTATION_ACCEPTANCE")=="1")EditorApplication.update+=Poll; }
    static void Save(RunState state)=>File.WriteAllText(State,JsonConvert.SerializeObject(state));
    public static void Run()
    {
        if(Environment.GetEnvironmentVariable("WW_ROTATION_ACCEPTANCE")!="1")throw new InvalidOperationException("Set WW_ROTATION_ACCEPTANCE=1 only in an isolated acceptance project.");
        if(!Application.isBatchMode)throw new InvalidOperationException("Use a disposable batch-mode project for this acceptance.");
        if(File.Exists(State))throw new InvalidOperationException("A previous rotation acceptance state exists; inspect or resume it before starting another run.");
        string output=Environment.GetEnvironmentVariable("WW_ROTATION_RESULT");if(string.IsNullOrWhiteSpace(output))throw new InvalidOperationException("Set WW_ROTATION_RESULT output path.");
        string className=Environment.GetEnvironmentVariable("WW_ROTATION_CLASS")??"WorldWeaverSmoothRotateY";
        if(!Regex.IsMatch(className,"^[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)*$"))throw new ArgumentException("WW_ROTATION_CLASS must be a C# type name, optionally namespace-qualified, without an assembly name.");
        string source=SourcePath(Environment.GetEnvironmentVariable("WW_ROTATION_SOURCE")??"Assets/WorldWeaverSmoothRotateY.cs");
        string sourceHash=Hash(File.ReadAllBytes(source));
        string reviewedHash=Environment.GetEnvironmentVariable("WW_ROTATION_SOURCE_SHA256");
        if(!string.IsNullOrEmpty(reviewedHash)&&(!Regex.IsMatch(reviewedHash,"^[a-fA-F0-9]{64}$")||!string.Equals(sourceHash,reviewedHash,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("The exact imported source bytes do not match WW_ROTATION_SOURCE_SHA256.");
        string assetPath="Assets/"+source.Substring(Path.GetFullPath(Application.dataPath).Length+1).Replace(Path.DirectorySeparatorChar,'/');
        var script=AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
        var type=script?script.GetClass():null;
        if(type==null||type.FullName!=className||type.Assembly.GetName().Name!="Assembly-CSharp"||!typeof(MonoBehaviour).IsAssignableFrom(type)||type.IsAbstract)throw new InvalidOperationException("The exact reviewed source must define the requested concrete MonoBehaviour in Assembly-CSharp.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var go=new GameObject("Reviewed local model rotation");go.AddComponent<GeneratedSceneObject>();go.AddComponent(type);
        EditorSceneManager.SaveScene(go.scene,"Assets/RotationAcceptance.unity");
        var job=AgentEditorJobs.RunRotationCheck(ProjectContextIndex.StableTarget(go),2,30);Save(new RunState {Phase="positive",Positive=job.Id,ClassName=className,SourcePath=source,SourceSha256=sourceHash,OutputPath=Path.GetFullPath(output)});
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
                string currentHash=Hash(File.ReadAllBytes(SourcePath(state.SourcePath)));
                bool sourceUnchanged=currentHash==state.SourceSha256;
                bool success=positive.Success&&!negative.Success&&positive.Samples>=3&&negative.Samples>=3&&negative.MeasurementStarted&&sourceUnchanged;
                var report=new JObject { ["schema"]=2,["kind"]="actual-runtime-rotation-and-negative-control",["unity"]=Application.unityVersion,["success"]=success,["positive"]=JObject.FromObject(positive),["negative"]=JObject.FromObject(negative),["reviewedClassName"]=state.ClassName,["reviewedSourcePath"]=state.SourcePath,["reviewedSourceSha256"]=state.SourceSha256,["sourceSha256AfterMeasurement"]=currentHash,["reviewedSourceUnchanged"]=sourceUnchanged,["playModeExited"]=!EditorApplication.isPlaying,["llmInferencePerformed"]=false,["limitation"]="One exact previously reviewed script and one stationary control; not general C# correctness or a model reliability estimate." };
                File.WriteAllText(state.OutputPath,report.ToString(Formatting.Indented));File.Delete(State);EditorApplication.Exit(success?0:1);
            }
        }
        catch(Exception error) { Debug.LogException(error);File.Delete(State);EditorApplication.Exit(2); }
    }
    static string SourcePath(string path)
    {
        string assets=Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar),source=Path.GetFullPath(path);
        if(!source.StartsWith(assets+Path.DirectorySeparatorChar,StringComparison.Ordinal)||!string.Equals(Path.GetExtension(source),".cs",StringComparison.OrdinalIgnoreCase)||!File.Exists(source))throw new ArgumentException("WW_ROTATION_SOURCE must be an existing C# source under this project's Assets directory.");
        for(var entry=new FileInfo(source) as FileSystemInfo;entry!=null;entry=Directory.GetParent(entry.FullName))
        {
            if((entry.Attributes&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Rotation acceptance does not follow source symlinks.");
            if(entry.FullName==assets)break;
        }
        return source;
    }
    static string Hash(byte[] data) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant(); }
}
