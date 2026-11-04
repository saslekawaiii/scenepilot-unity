using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;
using UnityEngine.InputSystem;

// Offline tool acceptance, separate from any real-agent acceptance fixture.
[InitializeOnLoad] public static class RuntimeProbeAcceptanceRunner
{
    static bool ticking;static JObject report;
    static string Output=>Environment.GetEnvironmentVariable("WW_RUNTIME_PROBE_OUTPUT");
    static readonly (string,string,bool,float)[] Cases={
        ("actual_unity_value_measurements","var p=Object.FindFirstObjectByType<WorldPlayer>();var start=p.transform.position;probe.Record(\"pose\",new{position=start,rotation=p.transform.rotation,area=new Bounds(start,Vector3.one)});yield return null;probe.Check(p.transform.position.y>-.1f,\"actual actor grounded height\",p.transform.position);",true,5),
        ("measure_after_frames","var light=UnityEngine.Object.FindFirstObjectByType<Light>();probe.Record(\"before\",light.intensity);light.intensity=2.5f;yield return null;probe.Check(Mathf.Abs(light.intensity-2.5f)<.001f,\"actual light changed\",light.intensity);",true,5),
        ("actual_keyboard_and_mouse","using UnityEngine.InputSystem; using UnityEngine.InputSystem.LowLevel; var p=UnityEngine.Object.FindFirstObjectByType<WorldPlayer>();var start=p.transform.position;var look=p.View.transform.eulerAngles;InputSystem.QueueStateEvent(probe.Keyboard,new KeyboardState(Key.W));InputSystem.QueueStateEvent(probe.Mouse,new MouseState{delta=new Vector2(60,25)});float until=Time.time+.3f;while(Time.time<until)yield return null;InputSystem.QueueStateEvent(probe.Keyboard,new KeyboardState());InputSystem.QueueStateEvent(probe.Mouse,new MouseState());yield return null;float distance=Vector3.Distance(start,p.transform.position);float yaw=Mathf.Abs(Mathf.DeltaAngle(look.y,p.View.transform.eulerAngles.y));probe.Check(distance>.5f,\"real W translation\",distance);probe.Check(yaw>.1f,\"real mouse yaw\",yaw);",true,8),
        ("actual_interact_edge","var p=UnityEngine.Object.FindFirstObjectByType<WorldPlayer>();string before=p.LastMessage;InputSystem.QueueStateEvent(probe.Keyboard,new KeyboardState(Key.E));yield return null;probe.Check(p.LastMessage!=before,\"E edge reached actual gameplay Update\",new{before,after=p.LastMessage});InputSystem.QueueStateEvent(probe.Keyboard,new KeyboardState());yield return null;",true,5),
        ("current_keyboard_interact_edge","var p=Object.FindFirstObjectByType<WorldPlayer>();string before=p.LastMessage;probe.Check(Keyboard.current!=null&&Mouse.current!=null,\"standard current devices installed\");InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.E));yield return null;probe.Check(p.LastMessage!=before,\"Keyboard.current E reached gameplay Update\",new{before,after=p.LastMessage});InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());yield return null;",true,5),
        ("failed_assertion","yield return null;probe.Check(false,\"intentional failing measurement\",new{actual=1,expected=2});",false,3),
        ("missing_assertion","yield return null;probe.Record(\"measuredOnly\",Time.frameCount);",false,3),
        ("yielded_timeout","while(true)yield return null;",false,.5f),
        ("actual_exception","yield return null;throw new InvalidOperationException(\"intentional probe exception\");",false,3),
        ("cancel","while(true)yield return null;",false,10)
    };
    static RuntimeProbeAcceptanceRunner(){if(!AssetDatabase.IsAssetImportWorkerProcess())EditorApplication.update+=Tick;}
    static string Hash(string p){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(p))).Replace("-","").ToLowerInvariant();}
    public static void Run()
    {
        if(Environment.GetEnvironmentVariable("WW_RUNTIME_PROBE_ACCEPTANCE")!="1"||!Application.isBatchMode)throw new InvalidOperationException("Explicit offline fixture required");
        if(File.Exists(Output))throw new IOException("Fresh report required");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="ProbeFixtureFloor";floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(20,1,20);
        var p=new GameObject("ProbeFixturePlayer");p.AddComponent<GeneratedSceneObject>();var player=p.AddComponent<WorldPlayer>();player.AlwaysLook=true;var view=new GameObject("ProbeFixtureCamera").AddComponent<Camera>();view.transform.SetParent(p.transform,false);view.transform.localPosition=new Vector3(0,1.5f,0);player.View=view;
        var light=new GameObject("ProbeFixtureLight").AddComponent<Light>();light.intensity=1;
        Directory.CreateDirectory("Assets/ProbeAcceptance");const string scene="Assets/ProbeAcceptance/fixture.unity";EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),scene);
        bool invalidRejected=false;try{AgentEditorJobs.RunRuntimeProbe("invalid csharp ???",2);}catch(ArgumentException){invalidRejected=true;}
        report=new JObject{["startedUtc"]=DateTime.UtcNow.ToString("O"),["invalidCodeRejectedBeforePlay"]=invalidRejected,["scene"]=scene,["sceneHash"]=Hash(scene),["devices"]=new JArray(InputSystem.devices.Select(d=>d.deviceId)),["caseIndex"]=0,["jobId"]=null,["cases"]=new JArray(),["completed"]=false};Save();
    }
    static async void Tick()
    {
        if(ticking||string.IsNullOrEmpty(Output)||!File.Exists(Output)||AssetDatabase.IsAssetImportWorkerProcess()||EditorApplication.isCompiling||EditorApplication.isUpdating)return;ticking=true;
        try
        {
            if(report==null)report=JObject.Parse(File.ReadAllText(Output));if((bool)report["completed"])return;
            int i=(int)report["caseIndex"];var item=Cases[i];string id=(string)report["jobId"];
            if(string.IsNullOrEmpty(id))
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode)return;
                var started=AgentEditorJobs.RunRuntimeProbe(item.Item2,item.Item4);report["jobId"]=started.Id;Save();return;
            }
            var job=AgentEditorJobs.Load(id);if(job==null)throw new InvalidOperationException("Durable job missing");
            if(item.Item1=="cancel"&&job.Status=="running"){AgentEditorJobs.Cancel(id);return;}
            if(AgentEditorJobs.Busy||EditorApplication.isPlayingOrWillChangePlaymode)return;
            var player=UnityEngine.Object.FindFirstObjectByType<WorldPlayer>();var light=UnityEngine.Object.FindFirstObjectByType<Light>();
            bool restored=player&&Vector3.Distance(player.transform.position,Vector3.zero)<.001f&&light&&Mathf.Abs(light.intensity-1)<.001f;
            bool sceneUnchanged=Hash((string)report["scene"])==(string)report["sceneHash"];
            bool devicesRestored=!InputSystem.devices.Any(d=>d.name.StartsWith("WorldWeaverRuntimeProbe",StringComparison.Ordinal));
            var compilationEvidence=typeof(ChatController).GetMethod("SuccessfulPlayCompilationEvidence",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            var measuredStep=new EditorCommandResult{Tool="job_result",Target=job.Id,Success=job.Success,Summary=JObject.FromObject(job).ToString()};
            bool compilationEvidenceMatched=(compilationEvidence.Invoke(null,new object[]{new System.Collections.Generic.List<EditorCommandResult>{measuredStep}})!=null)==job.Success;
            bool passed=job.Success==item.Item3&&restored&&sceneUnchanged&&devicesRestored&&File.Exists(job.ResultPath)&&compilationEvidenceMatched;
            if(item.Item1=="yielded_timeout")passed&=(job.Error??"").Contains("wall-time");
            if(item.Item1=="missing_assertion")passed&=(job.Error??"").Contains("No runtime assertion");
            if(item.Item1=="cancel")passed&=(job.Error??"").Contains("cancelled");
            ((JArray)report["cases"]).Add(new JObject{["case"]=item.Item1,["expectedSuccess"]=item.Item3,["actualSuccess"]=job.Success,["status"]=job.Status,["error"]=job.Error,["passed"]=passed,["sceneRestored"]=restored,["sceneFileUnchanged"]=sceneUnchanged,["devicesRestored"]=devicesRestored,["compilationEvidenceMatched"]=compilationEvidenceMatched,["devicesAfter"]=new JArray(InputSystem.devices.Select(d=>new JObject{["name"]=d.name,["layout"]=d.layout})),["samples"]=job.Samples,["sourceHash"]=job.ProbeSourceHash,["observations"]=job.ProbeObservations,["assertions"]=job.ProbeAssertions});
            report["caseIndex"]=i+1;report["jobId"]=null;
            if(i+1==Cases.Length){report["completed"]=true;report["passed"]=(bool)report["invalidCodeRejectedBeforePlay"]&&((JArray)report["cases"]).All(c=>(bool)c["passed"]);report["finishedUtc"]=DateTime.UtcNow.ToString("O");Save();EditorApplication.Exit((bool)report["passed"]?0:1);return;}Save();
        }
        catch(Exception e){if(report==null)report=new JObject();report["completed"]=true;report["passed"]=false;report["error"]=e.ToString();Save();EditorApplication.Exit(1);}
        finally{ticking=false;}
        await System.Threading.Tasks.Task.CompletedTask;
    }
    static void Save(){File.WriteAllText(Output,report.ToString());}
}
