using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;

// An external isolated fixture exercises the production job, including Play
// reloads and a negative no-mouse control. It does not repair agent output.
[InitializeOnLoad]
public static class PlayerJobAcceptanceRunner
{
    static string Output=>Environment.GetEnvironmentVariable("WW_PLAYER_JOB_OUTPUT");
    static PlayerJobAcceptanceRunner(){EditorApplication.update+=Tick;}
    public static void Run()
    {
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE")!="1"||Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName)!=Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""))throw new InvalidOperationException("Explicit isolated fixture required");
        if(File.Exists(Output))throw new InvalidOperationException("Fresh output required");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.transform.localScale=Vector3.one*10;
        var root=new GameObject("Input fixture");root.transform.position=Vector3.up*2;root.AddComponent<GeneratedSceneObject>();
        var camera=new GameObject("Camera").AddComponent<Camera>();camera.transform.SetParent(root.transform,false);
        var player=root.AddComponent<WorldPlayer>();player.View=camera;player.AlwaysLook=true;
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/PlayerJobFixture.unity");
        var job=AgentEditorJobs.RunPlayerCheck(ProjectContextIndex.StableTarget(root));
        File.WriteAllText(Output,new JObject{["positiveJob"]=job.Id,["phase"]=0,["startedUtc"]=DateTime.UtcNow.ToString("O")}.ToString());
    }
    static void Tick()
    {
        if(string.IsNullOrEmpty(Output)||!File.Exists(Output)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        var report=JObject.Parse(File.ReadAllText(Output));int phase=(int)report["phase"];if(phase>=2)return;
        try
        {
            var job=AgentEditorJobs.Load((string)report[phase==0?"positiveJob":"negativeJob"]);if(job==null||AgentEditorJobs.Busy)return;
            if(phase==0)
            {
                report["positive"]=JObject.FromObject(job);var player=UnityEngine.Object.FindFirstObjectByType<WorldPlayer>();player.AlwaysLook=false;
                var negative=AgentEditorJobs.RunPlayerCheck(ProjectContextIndex.StableTarget(player.gameObject));report["negativeJob"]=negative.Id;report["phase"]=1;File.WriteAllText(Output,report.ToString());
            }
            else
            {
                report["negative"]=JObject.FromObject(job);report["phase"]=2;report["passed"]=(bool)report["positive"]["Success"]&&!job.Success;File.WriteAllText(Output,report.ToString());EditorApplication.Exit((bool)report["passed"]?0:1);
            }
        }
        catch(Exception error){report["error"]=error.ToString();report["phase"]=2;report["passed"]=false;File.WriteAllText(Output,report.ToString());EditorApplication.Exit(1);}
    }
}
