using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;
using Object=UnityEngine.Object;

/// <summary>
/// Opt-in, disposable main-chat acceptance. The real local model chooses its own search/import/apply plans.
/// No tool is invoked by the harness, no pending code is approved and no provider response is replaced.
/// Install into disposable Assets/Editor only between Unity runs; invoke Run without -quit.
/// </summary>
public static class MaterialChatAcceptanceRunner
{
    const string Prompt="Найди на Poly Haven текстуру выветренного дерева и примени её к выделенной двери.";
    static bool running;
    public static async void Run()
    {
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_MATERIAL_CHAT_ACCEPTANCE")!="1")
            throw new InvalidOperationException("Explicit disposable batch mode and WW_MATERIAL_CHAT_ACCEPTANCE=1 are required.");
        if(running)throw new InvalidOperationException("Material chat acceptance is already running.");running=true;
        string output=null,project=Directory.GetParent(Application.dataPath).FullName,package=null,fixture=null;
        bool outputApproved=false,sceneStarted=false,passed=false,oldBackground=Application.runInBackground;
        ChatController controller=null;OllamaModelDiscovery discovery=null;Action changed=null;
        Dictionary<string,string> before=null,source=null,sourceScripts=null;
        var owned=new HashSet<string>(StringComparer.Ordinal);var checks=new JObject();var failures=new JArray();var watch=Stopwatch.StartNew();
        var report=new JObject { ["schemaVersion"]=1,["kind"]="actual-main-chat-polyhaven-search-import-and-selected-renderer",["startedUtc"]=DateTime.UtcNow.ToString("O"),
            ["unity"]=Application.unityVersion,["prompt"]=Prompt,["passed"]=false,["success"]=false,["checks"]=checks,["failureReasons"]=failures,["manualSemanticReviewRequired"]=true,
            ["providerFactoryInjected"]=false,["llmInferenceUsed"]=true,["externalLlmApiUsed"]=false,["paidApiUsed"]=false,["pendingPlansAppliedByHarness"]=0,["scriptApprovalGranted"]=false,
            ["limitations"]=new JArray("One dependent main-chat task on one actual local model, not a reliability or visual-quality benchmark.",
                "Only the model may choose and invoke the material tools. A truthful built-in fallback or a cache hit still fails the fresh-online-download criterion.",
                "This measures selected Renderer references, actual imported bytes and source preservation; it does not prove GPU appearance, Terrain painting or Player builds.",
                "No pending plan is approved or patched by the harness; raw failed plans and source receipts remain in the report.") };
        try
        {
            string expected=Required("WW_MATERIAL_CHAT_DISPOSABLE_PROJECT");
            if(Path.GetFullPath(expected)!=Path.GetFullPath(project))throw new InvalidOperationException("The exact disposable project must be explicitly identified.");
            output=Path.GetFullPath(Required("WW_MATERIAL_CHAT_OUTPUT"));package=ResolvePackage();
            if(Within(output,Application.dataPath)||Within(output,Path.Combine(project,"Packages"))||Within(output,package))
                throw new InvalidOperationException("Result output must remain outside Assets, Packages and the resolved package.");
            outputApproved=true;Write(output,report);
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||ScriptDrafts.Pending||AgentSourcePatches.Pending||AgentEditorJobs.Busy)
                throw new InvalidOperationException("Wait until the disposable Editor is idle in Edit Mode.");
            if(Path.GetFullPath("Assets")!=Path.GetFullPath(Application.dataPath))throw new InvalidOperationException("Working directory must be this disposable Unity project.");
            before=Files("Assets");sourceScripts=Scripts();source=Files(package);
            report["consumerAssetSha256Before"]=JObject.FromObject(before);report["sourceAssetSha256Before"]=JObject.FromObject(sourceScripts);
            report["rawCaptureScope"]="Exact raw plan strings retained by ChatController (redacted, bounded to 40000 characters per attempt); no harness normalization, patch or tool substitution.";
            report["source"]=new JObject { ["packageRoot"]=package,["fileCount"]=source.Count,["packageFiles"]=JObject.FromObject(source),
                ["treeSha256"]=Tree(source,package),["treeDigestFormat"]="Ordinal relative path + NUL + lowercase file SHA256 + LF, UTF-8; .DS_Store excluded",
                ["packageVersion"]=(string)JObject.Parse(File.ReadAllText(Path.Combine(package,"package.json")))["version"] };
            string harness="Assets/Editor/MaterialChatAcceptanceRunner.cs";
            report["harnessSha256"]=File.Exists(harness)?Hash(File.ReadAllBytes(harness)):"not found at standard installation path";
            checks["harnessSourceHashRecorded"]=File.Exists(harness);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);sceneStarted=true;
            fixture="Assets/MaterialChatFixture-"+Guid.NewGuid().ToString("N");
            if(string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets",Path.GetFileName(fixture))))throw new IOException("Cannot create the isolated fixture.");
            owned.Add(fixture);var shader=Shader.Find("Standard");if(!shader)throw new InvalidOperationException("The requested neutral Standard fixture shader is unavailable.");
            var neutral=new Material(shader){name="Original neutral door source",color=new Color(.55f,.55f,.55f,1)};
            string neutralPath=fixture+"/Neutral.mat";AssetDatabase.CreateAsset(neutral,neutralPath);owned.Add(neutralPath);AssetDatabase.SaveAssetIfDirty(neutral);
            string neutralHash=Hash(File.ReadAllBytes(neutralPath));
            var door=GameObject.CreatePrimitive(PrimitiveType.Cube);door.name="Дверь";door.transform.localScale=new Vector3(1.4f,2.4f,.15f);
            var renderer=door.GetComponent<Renderer>();renderer.sharedMaterial=neutral;
            var control=GameObject.CreatePrimitive(PrimitiveType.Cube);control.name="Контрольная поверхность";control.transform.position=Vector3.right*4;control.GetComponent<Renderer>().sharedMaterial=neutral;
            string fixtureScene=fixture+"/SelectedDoor.unity";EditorSceneManager.SaveScene(door.scene,fixtureScene);owned.Add(fixtureScene);
            int doorId=door.GetInstanceID();Selection.activeGameObject=door;
            report["fixture"]=new JObject { ["doorName"]=door.name,["doorInstanceId"]=doorId,["doorTarget"]="id:"+doorId,["scene"]=fixtureScene,
                ["selectedInstanceId"]=Selection.activeGameObject.GetInstanceID(),["initialMaterialPath"]=neutralPath,["initialMaterialSha256"]=neutralHash,
                ["initialAlbedo"]=AssetDatabase.GetAssetPath(neutral.mainTexture),["controlName"]=control.name };
            report["sceneBefore"]=SceneToolExecutor.Context();
            string model=Environment.GetEnvironmentVariable("WW_MATERIAL_CHAT_MODEL")??"qwen3.5:9b";
            if(model.StartsWith("ollama:",StringComparison.Ordinal))model=model.Substring(7);
            model=OllamaModelDiscovery.CanonicalModelId(model)??throw new ArgumentException("Invalid local model ID.");
            discovery=new OllamaModelDiscovery();var metadata=await discovery.RefreshAsync();
            var actual=metadata.Models.SingleOrDefault(m=>m.Id==model);
            if(!metadata.ServerAvailable||actual==null||!actual.CapabilitiesKnown||!actual.Completion||actual.Remote)
                throw new InvalidOperationException("Actual local metadata must confirm an installed non-remote completion model: "+model);
            report["localModelMetadata"]=JObject.FromObject(actual);report["metadataCheckedUtc"]=metadata.CheckedUtc.ToString("O");
            string history=Path.Combine(project,"UserSettings","WorldWeaverMaterialChatAcceptance",Guid.NewGuid().ToString("N"));
            controller=new ChatController(historyFolder:history,modelConnectionsFolder:Path.Combine(history,"models"),discovery:discovery);
            await controller.RefreshLocalModelsAsync();
            var choice=controller.Models.SingleOrDefault(m=>m.Key=="ollama:"+model);
            if(choice==null||!choice.Available||choice.Provider!=ProviderKind.Ollama||!new Uri(choice.Endpoint).IsLoopback)
                throw new InvalidOperationException("The actual chat controller did not confirm the exact local model.");
            controller.Settings.ModelKey=choice.Key;controller.Settings.Power=AssistantPower.Deep;controller.Settings.Mode=AssistantMode.Execute;controller.Settings.AutoApply=true;controller.SaveSettings();
            report["modelKey"]=choice.Key;report["power"]="Deep";report["historyFolder"]=history;report["sessionId"]=controller.Current.Id;
            report["localModel"]=JObject.FromObject(choice);report["chatSettings"]=JObject.FromObject(controller.Settings);
            Application.runInBackground=true;string signature=null;
            changed=()=>
            {
                var answer=controller.Current.Messages.LastOrDefault(m=>m.Role=="assistant");
                string next=(answer?.Attempts.Count??0)+":"+(answer?.Steps.Count??0)+":"+answer?.Status+":"+(answer?.PendingPlan!=null);
                if(next==signature)return;signature=next;Capture(controller,report,owned);Write(output,report);
            };
            controller.Changed+=changed;Write(output,report);
            await controller.SendAsync(Prompt);Capture(controller,report,owned);
            var response=controller.Current.Messages.LastOrDefault(m=>m.Role=="assistant");
            checks["actualAssistantResponse"]=response!=null&&response.Attempts.Count>0;
            checks["exactLocalModelUsedForEveryAttempt"]=response!=null&&response.Attempts.Count>0&&response.Attempts.All(a=>a.Model==model);
            checks["assistantDidNotReportError"]=response!=null&&!response.IsError;
            checks["noPendingPlanApprovedOrLeft"]=response!=null&&response.PendingPlan==null;
            var steps=response?.Steps??new List<EditorCommandResult>();
            var accepted=new List<EditorCommand>();var plans=new JArray();
            foreach(var attempt in response?.Attempts??new List<EditorPlanAttempt>())
            {
                try {var plan=EditorPlan.Parse(attempt.Response);plans.Add(new JObject {["round"]=attempt.Round,["accepted"]=attempt.Accepted,["plan"]=JObject.FromObject(plan)});if(attempt.Accepted)accepted.AddRange(plan.actions);}
                catch(Exception error){plans.Add(new JObject {["round"]=attempt.Round,["accepted"]=attempt.Accepted,["parseObservationError"]=error.GetType().Name});}
            }
            report["observedParsedPlans"]=plans;
            checks["modelChoseSearchImportAndApply"]=new[]{"material_search","material_import","material_apply"}.All(tool=>accepted.Any(a=>a.tool==tool)&&steps.Any(s=>s.Success&&s.Tool==tool));
            checks["noUnexpectedMutationToolsExecuted"]=steps.Where(s=>s.Success&&!SceneToolExecutor.IsReadOnlyTool(s.Tool)).All(s=>s.Tool=="material_import"||s.Tool=="material_apply");
            checks["noCodeApprovalImportOrCompilation"]=Same(sourceScripts,Scripts())&&!ScriptDrafts.Pending&&!AgentSourcePatches.Pending&&!EditorApplication.isCompiling;
            var imports=steps.Where(s=>s.Success&&s.Tool=="material_import").Select(s=>new {Step=s,Data=JObject.Parse(s.Summary)}).ToArray();
            var imported=imports.LastOrDefault(i=>((string)i.Data["MaterialId"]??"").StartsWith("polyhaven:",StringComparison.Ordinal));
            var apply=steps.LastOrDefault(s=>s.Success&&s.Tool=="material_apply");
            JObject application=apply==null?null:JObject.Parse(apply.Summary);
            bool actualDoor=door&&renderer&&door.GetInstanceID()==doorId&&renderer.gameObject==door;
            var assigned=actualDoor?renderer.sharedMaterial:null;
            string assignedPath=assigned?AssetDatabase.GetAssetPath(assigned):null;
            report["actualRendererReadback"]=new JObject { ["doorStillExists"]=actualDoor,["doorInstanceId"]=actualDoor?door.GetInstanceID():0,["assignedMaterialPath"]=assignedPath,
                ["assignedShader"]=assigned?assigned.shader.name:null,["albedo"]=assigned?AssetDatabase.GetAssetPath(assigned.mainTexture):null,
                ["normal"]=assigned&&assigned.HasProperty("_BumpMap")?AssetDatabase.GetAssetPath(assigned.GetTexture("_BumpMap")):null,
                ["packedMetallicSmoothness"]=assigned&&assigned.HasProperty("_MetallicGlossMap")?AssetDatabase.GetAssetPath(assigned.GetTexture("_MetallicGlossMap")):null };
            checks["actualSelectedRendererReferenceChanged"]=actualDoor&&assigned&&assigned!=neutral&&assignedPath==(string)application?["MaterialPath"]&&(int?)application?["AppliedRenderers"]==1;
            checks["controlAndOriginalNeutralAssetPreserved"]=control&&control.GetComponent<Renderer>().sharedMaterial==neutral&&File.Exists(neutralPath)&&Hash(File.ReadAllBytes(neutralPath))==neutralHash;
            checks["actualOnlineImportWithoutFallback"]=imported!=null&&(string)imported.Data["RequestedMaterialId"]==(string)imported.Data["MaterialId"]&&string.IsNullOrEmpty((string)imported.Data["Warning"]);
            checks["actualFreshDownloadNotPreexistingCache"]=imported!=null&&(bool?)imported.Data["CacheHit"]==false&&imported.Step.CreatedAssets.Count>0&&!before.ContainsKey((string)imported.Data["MaterialPath"]);
            checks["actualOnlineIdAppliedToDoor"]=imported!=null&&application!=null&&(string)application["MaterialId"]==(string)imported.Data["MaterialId"];
            var searched=steps.Where(s=>s.Success&&s.Tool=="material_search").SelectMany(s=>(JObject.Parse(s.Summary)["Materials"]as JArray??new JArray()).OfType<JObject>()).Select(m=>(string)m["Id"]).ToArray();
            checks["importedIdActuallyReturnedBySearch"]=imported!=null&&searched.Contains((string)imported.Data["MaterialId"],StringComparer.Ordinal);
            if(imported!=null)
            {
                string matPath=(string)imported.Data["MaterialPath"],folder=Path.GetDirectoryName(matPath).Replace('\\','/'),receiptPath=folder+"/material-source.json";
                var receipt=JObject.Parse(File.ReadAllText(receiptPath));report["actualImportReceipt"]=receipt;report["actualImportReceiptPath"]=receiptPath;report["actualImportReceiptSha256"]=Hash(File.ReadAllBytes(receiptPath));
                var maps=(receipt["source_maps"]as JArray??new JArray()).OfType<JObject>().ToArray();
                checks["receiptIsFreshPolyHavenCc0"]=!before.ContainsKey(receiptPath)&&(string)receipt["material_id"]==(string)imported.Data["MaterialId"]&&(string)receipt["license"]=="CC0-1.0"&&((string)receipt["credit"]??"").Contains("Powered by Poly Haven");
                checks["downloadedMapsIndependentlyMatchMd5ShaAndBytes"]=maps.Length>=3&&maps.All(m=>
                {
                    string file=folder+"/"+(string)m["role"]+Path.GetExtension(new Uri((string)m["url"]).AbsolutePath);if(!File.Exists(file))return false;
                    byte[] bytes=File.ReadAllBytes(file);return bytes.LongLength==(long)m["bytes"]&&Hash(bytes)==(string)m["sha256"]&&PolyHavenMaterialClient.Hash(bytes,false)==(string)m["providerMd5"];
                });
                var albedo=maps.FirstOrDefault(m=>(string)m["role"]=="albedo");var normal=maps.FirstOrDefault(m=>(string)m["role"]=="normal");
                string albedoPath=albedo==null?null:folder+"/albedo"+Path.GetExtension(new Uri((string)albedo["url"]).AbsolutePath);
                string normalPath=normal==null?null:folder+"/normal"+Path.GetExtension(new Uri((string)normal["url"]).AbsolutePath);
                checks["doorReferencesActualDownloadedAlbedoNormalAndPackedMap"]=assigned&&albedo!=null&&normal!=null&&AssetDatabase.GetAssetPath(assigned.mainTexture)==albedoPath&&
                    AssetDatabase.GetAssetPath(assigned.GetTexture("_BumpMap"))==normalPath&&AssetDatabase.GetAssetPath(assigned.GetTexture("_MetallicGlossMap"))==folder+"/metallic_smoothness.png"&&File.Exists(folder+"/metallic_smoothness.png");
                var a=albedoPath==null?null:AssetImporter.GetAtPath(albedoPath)as TextureImporter;var n=normalPath==null?null:AssetImporter.GetAtPath(normalPath)as TextureImporter;
                var packed=AssetImporter.GetAtPath(folder+"/metallic_smoothness.png")as TextureImporter;
                checks["actualImportersUseColorAndLinearNormalPackedSemantics"]=a&&a.sRGBTexture&&n&&n.textureType==TextureImporterType.NormalMap&&!n.sRGBTexture&&packed&&!packed.sRGBTexture;
            }
            else failures.Add("No actual online import was produced by the main chat; fallback/cache/empty plans cannot establish fresh online application.");
            report["sceneAfter"]=SceneToolExecutor.Context();checks["originalConsumerAssetBytesPreservedBeforeCleanup"]=Unchanged(before);checks["packageUnchangedBeforeCleanup"]=Same(source,Files(package));
            passed=checks.Properties().All(p=>p.Value.Type==JTokenType.Boolean&&(bool)p.Value)&&failures.Count==0;
        }
        catch(Exception error){report["error"]=ChatHistoryStore.Redact(error.ToString());}
        finally
        {
            try
            {
                if(controller!=null){Capture(controller,report,owned);if(changed!=null)controller.Changed-=changed;controller.Dispose();}else discovery?.Dispose();
                Application.runInBackground=oldBackground;
                if(sceneStarted){Selection.objects=Array.Empty<Object>();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
                var refused=new JArray();
                foreach(string path in owned.OrderByDescending(p=>p.Count(c=>c=='/')).ThenByDescending(p=>p.Length))
                {
                    if(!SafeOwned(path,before)){refused.Add(path);continue;}
                    if(Directory.Exists(path)){if(!Directory.EnumerateFileSystemEntries(path).Any())AssetDatabase.DeleteAsset(path);}
                    else if(File.Exists(path))AssetDatabase.DeleteAsset(path);
                }
                report["cleanupRefusedPaths"]=refused;report["ownedCreatedAssetPaths"]=JArray.FromObject(owned.OrderBy(p=>p,StringComparer.Ordinal));
                checks["cleanupOnlyUsesNewOwnedPaths"]=refused.Count==0;
                checks["ownedCreatedAssetsRemoved"]=owned.All(p=>!File.Exists(p)&&!Directory.Exists(p));
                checks["originalConsumerAssetBytesPreservedAfterCleanup"]=before!=null&&Unchanged(before);
                if(before!=null)report["changedOrMissingOriginalAssetFiles"]=JArray.FromObject(before.Where(p=>!File.Exists(p.Key)||Hash(File.ReadAllBytes(p.Key))!=p.Value).Select(p=>new {path=p.Key,beforeSha256=p.Value,afterSha256=File.Exists(p.Key)?Hash(File.ReadAllBytes(p.Key)):null}));
                if(before!=null){var extra=Files("Assets").Keys.Except(before.Keys,StringComparer.Ordinal).ToArray();report["remainingNewAssetFiles"]=JArray.FromObject(extra);checks["noUntrackedNewAssetFilesRemain"]=extra.Length==0;}
                if(source!=null)checks["immutablePackageUnchangedAtExit"]=Same(source,Files(package));
                if(sourceScripts!=null)checks["allOriginalSourceAssetsUnchangedAtExit"]=Same(sourceScripts,Scripts());
            }
            catch(Exception error){report["cleanupError"]=ChatHistoryStore.Redact(error.ToString());passed=false;}
            foreach(var p in checks.Properties().Where(p=>p.Value.Type!=JTokenType.Boolean||!(bool)p.Value))failures.Add(p.Name);
            passed=passed&&failures.Count==0;report["checks"]=checks;report["passed"]=passed;report["success"]=passed;report["ms"]=watch.ElapsedMilliseconds;report["finishedUtc"]=DateTime.UtcNow.ToString("O");
            try
            {
                if(!outputApproved)output=Path.Combine(project,"UserSettings","WorldWeaverAcceptanceFailures","material-chat-"+Guid.NewGuid().ToString("N")+".json");
                Write(output,report);
            }
            catch(Exception error){UnityEngine.Debug.LogError("Material chat receipt write failed: "+error.GetType().Name);passed=false;}
            finally{running=false;EditorApplication.Exit(passed?0:1);}
        }
    }
    static void Capture(ChatController controller,JObject report,HashSet<string> owned)
    {
        var answer=controller.Current.Messages.LastOrDefault(m=>m.Role=="assistant");
        report["phase"]=controller.Phase;report["response"]=answer?.Text;report["status"]=answer?.Status;report["isError"]=answer?.IsError??false;
        report["attempts"]=answer==null?new JArray():JArray.FromObject(answer.Attempts);report["steps"]=answer==null?new JArray():JArray.FromObject(answer.Steps);
        report["pendingPlan"]=answer?.PendingPlan==null?null:JObject.FromObject(answer.PendingPlan);report["task"]=answer?.Task==null?null:JObject.FromObject(answer.Task);
        report["inputTokens"]=answer?.InputTokens??0;report["outputTokens"]=answer?.OutputTokens??0;
        foreach(string path in answer?.Steps.SelectMany(s=>s.CreatedAssets??new List<string>())??Enumerable.Empty<string>())owned.Add(path.Replace('\\','/'));
    }
    static bool SafeOwned(string path,Dictionary<string,string> before)
    {
        if(string.IsNullOrEmpty(path)||!path.StartsWith("Assets/",StringComparison.Ordinal)||path.Contains("..")||path.Contains('\\')||before==null||before.ContainsKey(path)||before.ContainsKey(path+".meta"))return false;
        string assets=Path.GetFullPath("Assets"),full=Path.GetFullPath(path);if(!Within(full,assets))return false;
        for(string entry=full;entry!=null;entry=Path.GetDirectoryName(entry)){if((File.Exists(entry)||Directory.Exists(entry))&&(File.GetAttributes(entry)&FileAttributes.ReparsePoint)!=0)return false;if(entry==assets)break;}return true;
    }
    static Dictionary<string,string> Files(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(p=>Path.GetFileName(p)!=".DS_Store").OrderBy(p=>p,StringComparer.Ordinal).ToDictionary(p=>p.Replace('\\','/'),p=>Hash(File.ReadAllBytes(p)),StringComparer.Ordinal);
    static Dictionary<string,string> Scripts()=>Files("Assets").Where(p=>p.Key.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal);
    static bool Unchanged(Dictionary<string,string> files)=>files.All(p=>File.Exists(p.Key)&&Hash(File.ReadAllBytes(p.Key))==p.Value);
    static bool Same(Dictionary<string,string> a,Dictionary<string,string> b)=>a.Count==b.Count&&a.All(p=>b.TryGetValue(p.Key,out var value)&&value==p.Value);
    static string Tree(Dictionary<string,string> files,string root)=>Hash(Encoding.UTF8.GetBytes(string.Concat(files.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key.Substring(root.Length).TrimStart('/')+"\0"+p.Value+"\n"))));
    static string Hash(byte[] bytes)=>PolyHavenMaterialClient.Hash(bytes);
    static string ResolvePackage(){var info=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SceneToolExecutor).Assembly);if(string.IsNullOrWhiteSpace(info?.resolvedPath))throw new DirectoryNotFoundException("Cannot resolve frozen package.");return Path.GetFullPath(info.resolvedPath);}
    static bool Within(string path,string folder)=>Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.Ordinal);
    static string Required(string key)=>string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))?throw new ArgumentException(key+" is required."):Environment.GetEnvironmentVariable(key);
    static void Write(string path,JObject report){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,ChatHistoryStore.Redact(report.ToString(Newtonsoft.Json.Formatting.Indented)));}
}
