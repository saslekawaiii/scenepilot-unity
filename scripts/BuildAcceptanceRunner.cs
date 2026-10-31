using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using WorldWeaver.Editor.Assistant;

/// <summary>Explicit local build of the playable village in a disposable consumer project.</summary>
[InitializeOnLoad]
public static class BuildAcceptanceRunner
{
    const string State="UserSettings/WorldWeaverBuildAcceptance.txt";
    static BuildAcceptanceRunner(){if(Environment.GetEnvironmentVariable("WW_BUILD_ACCEPTANCE")=="1")EditorApplication.update+=Tick;}
    public static void Run()
    {
        if(Environment.GetEnvironmentVariable("WW_BUILD_ACCEPTANCE")!="1")throw new InvalidOperationException("Explicit acceptance flag required.");
        VillageDemoBuilder.Create();var job=AgentEditorJobs.BuildPlayer();File.WriteAllText(State,job.Id);
    }
    static void Tick()
    {
        if(!File.Exists(State)||AgentEditorJobs.Busy)return;var job=AgentEditorJobs.Load(File.ReadAllText(State));if(job==null)return;
        var report=JObject.FromObject(job);report["kind"]="actual-playable-village-player-build";report["unity"]=UnityEngine.Application.unityVersion;report["llmInferencePerformed"]=false;report["outputExists"]=File.Exists(job.ResultPath)||Directory.Exists(job.ResultPath);
        File.WriteAllText(Environment.GetEnvironmentVariable("WW_BUILD_RESULT"),report.ToString());File.Delete(State);EditorApplication.Exit(job.Success?0:1);
    }
}
