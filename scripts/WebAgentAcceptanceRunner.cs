using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;
using Object=UnityEngine.Object;

/// <summary>Exact user regression through the public controller, including domain reload.
/// No provider injection, supplied plan, topic URL or scene repair by the harness.</summary>
[InitializeOnLoad]
public static class WebAgentAcceptanceRunner
{
    static bool running;static ChatController controller;static JObject report;
    static string Output=>Environment.GetEnvironmentVariable("WW_WEB_OUTPUT");
    static string State=>Output+".state";
    static string PackageDigest()
    {
        string folder=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WorldPlayer).Assembly).resolvedPath;
        string Hash(byte[] value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(value)).Replace("-","").ToLowerInvariant();}
        return Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".DS_Store")&&!p.Contains("__pycache__")).OrderBy(p=>p,StringComparer.Ordinal).Select(p=>p.Substring(folder.Length+1)+":"+Hash(File.ReadAllBytes(p))))));
    }
    static WebAgentAcceptanceRunner(){EditorApplication.update+=Tick;}
    public static void Run()
    {
        string project=Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName);
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE")!="1"||project!=Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""))throw new InvalidOperationException("Explicit isolated project required.");
        if(File.Exists(Output)||File.Exists(State))throw new InvalidOperationException("New report/state path required.");
        EditorSceneManager.OpenScene("Assets/AgentReproduction/user-before.unity");
        string history="UserSettings/WebAgentAcceptance/"+Guid.NewGuid().ToString("N");
        var connection=JsonConvert.DeserializeObject<ChatModelConnection>(File.ReadAllText(Environment.GetEnvironmentVariable("WW_AGENT_CONNECTION")));
        string key=new ChatModelConnections(Path.Combine(history,"models")).Add(connection).ModelKey;
        string prior=Environment.GetEnvironmentVariable("WW_WEB_HISTORY");if(!string.IsNullOrEmpty(prior)){Directory.CreateDirectory(history);File.Copy(prior,Path.Combine(history,Path.GetFileName(prior)),false);}
        report=new JObject{["startedUtc"]=DateTime.UtcNow.ToString("O"),["history"]=history,["modelKey"]=key,["model"]=connection.Model,["providerInjected"]=false,["sceneRepairByHarness"]=false,["paidApiUsed"]=false,["initialScene"]=SceneFacts(),["prompt"]=Environment.GetEnvironmentVariable("WW_WEB_PROMPT"),["started"]=false,["completed"]=false};
        report["packageDigest"]=PackageDigest();Save();Tick();
    }
    static async void Tick()
    {
        if(running||string.IsNullOrEmpty(Output)||!File.Exists(State)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        running=true;
        try
        {
            if(report==null)report=JObject.Parse(File.ReadAllText(State));if((bool?)report["completed"]==true)return;
            if(controller==null)
            {
                string history=(string)report["history"];controller=new ChatController(historyFolder:history,modelConnectionsFolder:Path.Combine(history,"models"));
                controller.Settings.ModelKey=(string)report["modelKey"];controller.Settings.Power=AssistantPower.Deep;controller.Settings.Mode=AssistantMode.Execute;controller.Settings.AllowEditorCode=true;controller.Settings.AllowAutonomousScripts=true;controller.Settings.AutoApply=true;controller.Settings.AutoVerify=true;controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=true;controller.SaveSettings();
                controller.Changed+=()=>{report["messages"]=JArray.FromObject(controller.Current.Messages);Save();};
                await controller.RefreshModelConnectionsAsync();if(!controller.Connected)throw new InvalidOperationException("Model unavailable in connection gate.");
            }
            if(!(bool)report["started"])
            {report["started"]=true;Save();await controller.SendAsync((string)report["prompt"]);}
            else
            {
                var pending=controller.Current.Messages.LastOrDefault(m=>m.Role=="assistant");
                if(pending?.Task?.ContinueAfterReload==true){await controller.ResumeAsync(pending.Id);}
            }
            var answer=controller.Current.Messages.LastOrDefault(m=>m.Role=="assistant");
            if(answer==null||controller.Busy||answer.Task?.ContinueAfterReload==true||ScriptDrafts.Pending||AgentSourcePatches.Pending)return;
            report["messages"]=JArray.FromObject(controller.Current.Messages);report["finalScene"]=SceneFacts();
            var terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);var environment=Object.FindObjectsByType<WorldEnvironment>(FindObjectsSortMode.None);
            var checks=new JObject{["realModelUsed"]=answer.Attempts.Count>0,["noError"]=!answer.IsError,["noPendingPlan"]=answer.PendingPlan==null,["notBlocked"]=answer.Task?.Status!="blocked"&&answer.Task?.Status!="partial",["realInternetResearch"]=answer.Steps.Any(s=>s.Success&&(s.Tool=="web_research"||s.Tool=="web_search")),["realDownloadedData"]=answer.Steps.Any(s=>s.Success&&s.Tool=="asset_download"),["measuredHeightmapApplied"]=answer.Steps.Any(s=>s.Success&&s.Tool=="terrain_from_heightmap"),["actualPlayerInputChecked"]=answer.Steps.Any(s=>s.Success&&s.Tool=="job_result"&&s.Summary.Contains("player_control")&&s.Summary.Contains("yawDegrees")),["physicalTerrain"]=terrain.Any(t=>t.terrainData&&t.GetComponent<TerrainCollider>()&&t.GetComponent<TerrainCollider>().terrainData==t.terrainData),["elevationProvenance"]=environment.Any(e=>(e.HeightmapSource??"").Contains("sha256:")),["playerPreserved"]=Object.FindObjectsByType<WorldPlayer>(FindObjectsSortMode.None).Length==1};
            checks["packageUnchanged"]=(string)report["packageDigest"]==PackageDigest();report["checks"]=checks;report["passed"]=checks.Properties().All(p=>(bool)p.Value);report["completed"]=true;report["finishedUtc"]=DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory("Assets/AgentReproduction");string scene="Assets/AgentReproduction/"+Path.GetFileNameWithoutExtension(Output)+"-result.unity";EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),scene);report["resultScene"]=scene;Save();controller.Dispose();EditorApplication.Exit((bool)report["passed"]?0:1);
        }
        catch(Exception error){if(report==null)report=new JObject();report["error"]=error.ToString();report["completed"]=true;report["passed"]=false;Save();EditorApplication.Exit(1);}
        finally{running=false;}
    }
    static JObject SceneFacts()=>new JObject{["renderers"]=ProjectContextIndex.ListScene(new JObject{["component"]="Renderer",["limit"]=100}),["terrains"]=new JArray(Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Select(t=>new JObject{["name"]=t.name,["target"]=ProjectContextIndex.StableTarget(t.gameObject),["size"]=JArray.FromObject(new[]{t.terrainData.size.x,t.terrainData.size.y,t.terrainData.size.z}),["source"]=t.GetComponentInParent<WorldEnvironment>()?.HeightmapSource})),["players"]=new JArray(Object.FindObjectsByType<WorldPlayer>(FindObjectsSortMode.None).Select(p=>new JObject{["target"]=ProjectContextIndex.StableTarget(p.gameObject),["model"]=p.GetComponent<WorldModelVisual>()?.SourcePath,["alwaysLook"]=p.AlwaysLook,["externalCameraControl"]=p.ExternalCameraControl,["components"]=new JArray(p.GetComponents<Component>().Where(c=>c).Select(c=>c.GetType().Name)),["cameraComponents"]=new JArray(p.View?p.View.GetComponents<Component>().Where(c=>c).Select(c=>c.GetType().Name):Array.Empty<string>())}))};
    static void Save(){if(string.IsNullOrEmpty(Output))return;Directory.CreateDirectory(Path.GetDirectoryName(Output));File.WriteAllText(Output,report.ToString(Formatting.Indented));File.WriteAllText(State,report.ToString(Formatting.Indented));}
}
