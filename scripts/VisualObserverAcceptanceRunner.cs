using System;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;
public static class VisualObserverAcceptanceRunner
{
 public static async void Run()
 {
  string output=Environment.GetEnvironmentVariable("WW_VISUAL_OUTPUT");var report=new JObject{["startedUtc"]=DateTime.UtcNow.ToString("O"),["sceneRepairByHarness"]=false,["paidApiUsed"]=false};
  try
  {
   string project=Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName);if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE")!="1"||project!=Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""))throw new InvalidOperationException("Isolated fixture required");
   EditorSceneManager.OpenScene(Environment.GetEnvironmentVariable("WW_VISUAL_SCENE"));var image=AgentViewCapture.Capture(view:"overview");report["image"]=Path.GetFullPath(image.Path);report["imageSha256"]=image.Sha256;
   using(var discovery=new OllamaModelDiscovery())
   {
    var snapshot=await discovery.RefreshAsync(CancellationToken.None);var observer=AgentVisualObserver.SelectLocalObserver(snapshot.Models.Where(m=>m.CapabilitiesKnown&&m.Completion).Select(m=>new AvailableModel{Provider=ProviderKind.Ollama,Endpoint=discovery.Endpoint,Model=m.Id,Available=true,AutoDiscovered=true,SupportsVision=m.Vision,Remote=m.Remote,SizeBytes=m.Bytes}));
    var inventory=AgentVisualObserver.SceneInventory();report["inventory"]=inventory;
    var observation=await AgentVisualObserver.ObserveAsync(observer,image,inventory,CancellationToken.None);report["observation"]=observation;
    var legacy=ProjectContextIndex.Objects().FirstOrDefault(g=>g.name=="Water"&&g.GetComponent<Renderer>()&&g.GetComponent<Renderer>().bounds.size.x>=500);string target=legacy?ProjectContextIndex.StableTarget(legacy):null;report["expectedSurfacePresent"]=legacy!=null;report["describedExpectedSurface"]=target!=null&&observation["candidates"].Any(c=>(string)c["target"]==target);report["passed"]=(string)observation["status"]=="observed"&&(bool)report["describedExpectedSurface"];
   }
  }
  catch(Exception error){report["error"]=error.ToString();report["passed"]=false;}
  report["finishedUtc"]=DateTime.UtcNow.ToString("O");File.WriteAllText(output,report.ToString());EditorApplication.Exit((bool)report["passed"]?0:1);
 }
}
