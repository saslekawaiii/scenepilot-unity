using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;

// Exact historical prompts; durable orchestration survives real compile/Play reloads.
// No injected plans, URLs or corrective scene mutations.
[InitializeOnLoad]
public static class SingleRequestAcceptanceRunner
{
    static ChatController controller;static JObject report;static bool running;static double nextStartAttempt,nextSnapshot;static bool snapshotPending;
    static string Output=>Environment.GetEnvironmentVariable("WW_REGRESSION_OUTPUT");
    static (string,string)[] Cases;
    static void EnsureCases(){if(Cases==null)Cases=new[]{(Environment.GetEnvironmentVariable("WW_TEST_CASE_NAME")??"request",File.ReadAllText(Environment.GetEnvironmentVariable("WW_TEST_PROMPT")))};}
    static void FlushSnapshot(){if(!snapshotPending||controller==null||report==null)return;report["messages"]=JArray.FromObject(controller.Current.Messages);report["controllerPhase"]=controller.Phase;report["lastActivityUtc"]=DateTime.UtcNow.ToString("O");snapshotPending=false;nextSnapshot=EditorApplication.timeSinceStartup+1;Save();}
    static SingleRequestAcceptanceRunner(){if(AssetDatabase.IsAssetImportWorkerProcess())return;AssemblyReloadEvents.beforeAssemblyReload+=FlushSnapshot;EditorApplication.update+=Tick;EditorApplication.update+=()=>{string flag=Environment.GetEnvironmentVariable("WW_TEST_STOP_FILE");if(controller!=null&&controller.Busy&&!string.IsNullOrEmpty(flag)&&File.Exists(flag)){report["stoppedByHarness"]=true;report["stopReason"]=File.ReadAllText(flag);controller.Cancel();}};}
    static string Package=>UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WorldPlayer).Assembly).resolvedPath;
    static string Hash(byte[] value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(value)).Replace("-","").ToLowerInvariant();}
    static string PackageDigest()=>Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",Directory.GetFiles(Package,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".DS_Store")&&!p.Contains("__pycache__")).OrderBy(p=>p,StringComparer.Ordinal).Select(p=>p.Substring(Package.Length+1)+":"+Hash(File.ReadAllBytes(p))))));
    static bool VerifyInstalledPackage(JObject proof,string repositoryRoot)
    {
        var expected=((JArray)proof["files"]).ToDictionary(r=>(string)r["path"],r=>(string)r["sha256"]);
        var files=Directory.GetFiles(Package,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".DS_Store")&&!p.Contains("__pycache__")).ToDictionary(p=>p.Substring(Package.Length+1).Replace('\\','/'));
        if(files.Count!=expected.Count||files.Keys.Any(n=>!expected.ContainsKey(n)))throw new InvalidOperationException("Installed package file set differs from frozen archive");
        bool fingerprint=false;
        foreach(var item in expected)
        {
            if(Hash(File.ReadAllBytes(files[item.Key]))==item.Value)continue;
            if(item.Key!="package.json")throw new InvalidOperationException("Installed package bytes differ: "+item.Key);
            string original=Path.Combine(repositoryRoot,"Packages/com.worldweaver.toolkit/package.json");
            if(Hash(File.ReadAllBytes(original))!=item.Value)throw new InvalidOperationException("Source manifest no longer matches frozen proof");
            var source=JObject.Parse(File.ReadAllText(original));var installed=JObject.Parse(File.ReadAllText(files[item.Key]));
            if(source["_fingerprint"]!=null||installed["_fingerprint"]?.Type!=JTokenType.String)throw new InvalidOperationException("Unexpected installed manifest difference");
            installed.Remove("_fingerprint");
            if(!JToken.DeepEquals(source,installed))throw new InvalidOperationException("Original package manifest fields differ");
            fingerprint=true;
        }
        return fingerprint;
    }
    static string SceneDigest()
    {
        var all=new JArray();int offset=0;
        while(true)
        {
            var page=ProjectContextIndex.ListScene(new JObject{["limit"]=100,["offset"]=offset});foreach(var row in (JArray)page["objects"])all.Add(row.DeepClone());
            if(page["nextOffset"].Type==JTokenType.Null)break;int next=(int)page["nextOffset"];if(next<=offset)throw new InvalidOperationException("Scene inventory cursor did not advance");offset=next;
        }
        return Hash(System.Text.Encoding.UTF8.GetBytes(all.ToString(Formatting.None)));
    }
    public static void Run()
    {
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE")!="1"||Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName)!=Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""))throw new InvalidOperationException("Explicit isolated fixture required");
        if(File.Exists(Output))throw new IOException("Fresh output required");
        EnsureCases();string initial=Environment.GetEnvironmentVariable("WW_AGENT_INITIAL_SCENE");
        if(string.IsNullOrEmpty(initial))EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);else EditorSceneManager.OpenScene(initial);
        string proofPath=Environment.GetEnvironmentVariable("WW_AGENT_PACKAGE_PROOF");var proof=JObject.Parse(File.ReadAllText(proofPath));
        string archive=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(proofPath)),(string)proof["archive"]);
        if(Hash(File.ReadAllBytes(archive))!=(string)proof["sha256"])throw new InvalidOperationException("Frozen archive checksum differs");
        bool installedFingerprint=VerifyInstalledPackage(proof,Path.GetDirectoryName(Path.GetDirectoryName(proofPath)));
        var connection=JsonConvert.DeserializeObject<ChatModelConnection>(File.ReadAllText(Environment.GetEnvironmentVariable("WW_AGENT_CONNECTION")));
        string history="UserSettings/RegressionAcceptance/"+Guid.NewGuid().ToString("N"),key=new ChatModelConnections(Path.Combine(history,"models")).Add(connection).ModelKey;
        report=new JObject{["installedManifestFingerprintAdded"]=installedFingerprint,["installedOriginalManifestFieldsUnchanged"]=true,["installedPayloadMatchesFrozenProof"]=true,["frozenSourceTreeSha256"]=proof["treeSha256"].DeepClone(),["packageSourceCommit"]=proof["sourceCommit"].DeepClone(),["packageArchiveSha256"]=proof["sha256"].DeepClone(),["initialScene"]=initial,["continuationFromSavedArtifact"]=!string.IsNullOrEmpty(initial),["observerSnapshotIntervalSeconds"]=1,["startedUtc"]=DateTime.UtcNow.ToString("O"),["history"]=history,["modelKey"]=key,["model"]=connection.Model,["providerInjected"]=false,["sceneRepairByHarness"]=false,["paidApiUsed"]=false,["packageDigest"]=PackageDigest(),["caseIndex"]=0,["startedCase"]=false,["completed"]=false,["cases"]=new JArray()};Save();Tick();
    }
    static async void Tick()
    {
        if(snapshotPending&&EditorApplication.timeSinceStartup>=nextSnapshot)FlushSnapshot();
        if(AssetDatabase.IsAssetImportWorkerProcess()||running||string.IsNullOrEmpty(Output)||!File.Exists(Output)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;running=true;
        try
        {
            EnsureCases();if(report==null)report=JObject.Parse(File.ReadAllText(Output));if((bool)report["completed"])return;
            if(controller==null)
            {
                string history=(string)report["history"];controller=new ChatController(historyFolder:history,modelConnectionsFolder:Path.Combine(history,"models"));
                controller.Settings.ModelKey=(string)report["modelKey"];controller.Settings.Power=AssistantPower.Deep;controller.Settings.Mode=AssistantMode.Execute;controller.Settings.AllowEditorCode=true;controller.Settings.AllowAutonomousScripts=true;controller.Settings.AutoApply=true;controller.Settings.AutoVerify=true;controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=true;controller.SaveSettings();
                controller.Changed+=()=>{snapshotPending=true;};await controller.RefreshModelConnectionsAsync();if(!controller.Connected)throw new InvalidOperationException("Real model connection gate failed");
            }
            // RefreshModelConnectionsAsync may yield across a compilation transition.
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||ScriptDrafts.Pending||AgentSourcePatches.Pending||AgentEditorJobs.Busy||EditorApplication.timeSinceStartup<nextStartAttempt)return;
            if((bool)report["startedCase"]&&!controller.Current.Messages.Any(m=>m.Role=="assistant")&&!controller.Busy)
            {
                report["startedCase"]=false;((JArray)report["cases"]).Remove(((JArray)report["cases"]).Last);Save();
            }
            int index=(int)report["caseIndex"];var item=Cases[index];JObject row;
            if(!(bool)report["startedCase"])
            {
                row=new JObject{["case"]=item.Item1,["prompt"]=item.Item2,["beforeSceneDigest"]=SceneDigest()};((JArray)report["cases"]).Add(row);report["startedCase"]=true;Save();await controller.SendAsync(item.Item2);
                if(!controller.Current.Messages.Any(m=>m.Role=="assistant"))
                {
                    report["startRetries"]=(int?)report["startRetries"]+1??1;report["lastStartPhase"]=controller.Phase;
                    report["startedCase"]=false;((JArray)report["cases"]).Remove(row);nextStartAttempt=EditorApplication.timeSinceStartup+1;Save();
                    if((int)report["startRetries"]>=120)throw new InvalidOperationException("Fixture could not submit the original request within 120 attempts: "+controller.Phase);return;
                }
            }
            else{var pending=controller.Current.Messages.LastOrDefault(m=>m.Role=="assistant");if(pending?.Task?.ContinueAfterReload==true)await controller.ResumeAsync(pending.Id);}
            var answer=controller.Current.Messages.LastOrDefault(m=>m.Role=="assistant");if(answer==null||controller.Busy||answer.Task?.ContinueAfterReload==true||ScriptDrafts.Pending||AgentSourcePatches.Pending)return;
            row=(JObject)((JArray)report["cases"]).Last;string name=item.Item1;
                var checks=new JObject{["realModelUsed"]=answer.Attempts.Count>0,["noError"]=!answer.IsError,["noPendingPlan"]=answer.PendingPlan==null};
            checks["notBlocked"]=answer.Task?.Status!="blocked"&&answer.Task?.Status!="partial"&&answer.Task?.Status!="cancelled"&&answer.Task?.Status!="failed"&&answer.Task?.Status!="timed_out";
            row["behaviorNotYetIndependentlyVerified"]=true;checks["notStoppedByHarness"]=(bool?)report["stoppedByHarness"]!=true;row["checks"]=checks;row["passed"]=checks.Properties().All(p=>(bool)p.Value);row["answer"]=JObject.FromObject(answer);report["caseIndex"]=index+1;report["startedCase"]=false;
            Directory.CreateDirectory("Assets/RegressionAcceptance");EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/RegressionAcceptance/"+name+".unity");Save();
            if(index+1==Cases.Length){report["packageUnchanged"]=(string)report["packageDigest"]==PackageDigest();report["completed"]=true;report["passed"]=((JArray)report["cases"]).All(c=>(bool)c["passed"])&&(bool)report["packageUnchanged"];report["finishedUtc"]=DateTime.UtcNow.ToString("O");Save();controller.Dispose();EditorApplication.Exit((bool)report["passed"]?0:1);}
        }
        catch(Exception error){if(report==null)report=new JObject();report["error"]=error.ToString();report["completed"]=true;report["passed"]=false;Save();EditorApplication.Exit(1);}
        finally{running=false;}
    }
    static void Save(){if(!string.IsNullOrEmpty(Output))File.WriteAllText(Output,report.ToString(Formatting.Indented));}
}
