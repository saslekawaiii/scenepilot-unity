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
public static class AgentRegressionSuiteRunner
{
    static ChatController controller;static JObject report;static bool running;
    static string Output=>Environment.GetEnvironmentVariable("WW_REGRESSION_OUTPUT");
    static readonly (string,string)[] Cases={
      ("hero","Создай персонажа: сделай главного героя не используй примитив. используй реальную модель. добавь ей анимации ходьбы, передвижения и тд"),
      ("moon","добавь карту высот луны. кратеры и пещеры и лунных жителей чтобы игрок мог выживать на луне"),
      ("status","что ты создал а что не смог?"),
      ("open_world","ты не создал лунную поверхность. создай лунную поверхность чтобы я мог по ней ходить. большой открытый мир. добавь звездное небо и ночную атмосферу луны"),
      ("model","а какая сейчас модель?"),
      ("general_code","Через C# в редакторе создай круг из 12 точечных источников света радиусом 18 метров вокруг начала координат. Высота каждого 8 метров, дальность света 6 метров. Верни измеренные число источников и радиус. Сохрани существующий мир и героя.")};
    static AgentRegressionSuiteRunner(){EditorApplication.update+=Tick;}
    static string Package=>UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WorldPlayer).Assembly).resolvedPath;
    static string Hash(byte[] value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(value)).Replace("-","").ToLowerInvariant();}
    static string PackageDigest()=>Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",Directory.GetFiles(Package,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".DS_Store")&&!p.Contains("__pycache__")).OrderBy(p=>p,StringComparer.Ordinal).Select(p=>p.Substring(Package.Length+1)+":"+Hash(File.ReadAllBytes(p))))));
    static string SceneDigest()=>Hash(System.Text.Encoding.UTF8.GetBytes(ProjectContextIndex.ListScene(new JObject{["limit"]=100}).ToString(Formatting.None)));
    public static void Run()
    {
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE")!="1"||Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName)!=Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""))throw new InvalidOperationException("Explicit isolated fixture required");
        if(File.Exists(Output))throw new IOException("Fresh output required");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var connection=JsonConvert.DeserializeObject<ChatModelConnection>(File.ReadAllText(Environment.GetEnvironmentVariable("WW_AGENT_CONNECTION")));
        string history="UserSettings/RegressionAcceptance/"+Guid.NewGuid().ToString("N"),key=new ChatModelConnections(Path.Combine(history,"models")).Add(connection).ModelKey;
        report=new JObject{["startedUtc"]=DateTime.UtcNow.ToString("O"),["history"]=history,["modelKey"]=key,["model"]=connection.Model,["providerInjected"]=false,["sceneRepairByHarness"]=false,["paidApiUsed"]=false,["packageDigest"]=PackageDigest(),["caseIndex"]=0,["startedCase"]=false,["completed"]=false,["cases"]=new JArray()};Save();Tick();
    }
    static async void Tick()
    {
        if(running||string.IsNullOrEmpty(Output)||!File.Exists(Output)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;running=true;
        try
        {
            if(report==null)report=JObject.Parse(File.ReadAllText(Output));if((bool)report["completed"])return;
            if(controller==null)
            {
                string history=(string)report["history"];controller=new ChatController(historyFolder:history,modelConnectionsFolder:Path.Combine(history,"models"));
                controller.Settings.ModelKey=(string)report["modelKey"];controller.Settings.Power=AssistantPower.Deep;controller.Settings.Mode=AssistantMode.Execute;controller.Settings.AllowEditorCode=true;controller.Settings.AllowAutonomousScripts=true;controller.Settings.AutoApply=true;controller.Settings.AutoVerify=true;controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=true;controller.SaveSettings();
                controller.Changed+=()=>{report["messages"]=JArray.FromObject(controller.Current.Messages);Save();};await controller.RefreshModelConnectionsAsync();if(!controller.Connected)throw new InvalidOperationException("Real model connection gate failed");
            }
            int index=(int)report["caseIndex"];var item=Cases[index];JObject row;
            if(!(bool)report["startedCase"])
            {
                row=new JObject{["case"]=item.Item1,["prompt"]=item.Item2,["beforeSceneDigest"]=SceneDigest()};((JArray)report["cases"]).Add(row);report["startedCase"]=true;Save();await controller.SendAsync(item.Item2);
            }
            else{var pending=controller.Current.Messages.LastOrDefault(m=>m.Role=="assistant");if(pending?.Task?.ContinueAfterReload==true)await controller.ResumeAsync(pending.Id);}
            var answer=controller.Current.Messages.LastOrDefault(m=>m.Role=="assistant");if(answer==null||controller.Busy||answer.Task?.ContinueAfterReload==true||ScriptDrafts.Pending||AgentSourcePatches.Pending)return;
            row=(JObject)((JArray)report["cases"]).Last;string name=item.Item1;
                var checks=new JObject{["realModelUsed"]=answer.Attempts.Count>0,["noError"]=!answer.IsError,["noPendingPlan"]=answer.PendingPlan==null};
                var player=UnityEngine.Object.FindFirstObjectByType<WorldPlayer>();
                var terrain=UnityEngine.Object.FindFirstObjectByType<Terrain>();
                if(name=="hero")
                {
                    checks["onePlayableHero"]=UnityEngine.Object.FindObjectsByType<WorldPlayer>(FindObjectsSortMode.None).Length==1;
                    checks["importedSkinnedModel"]=player&&player.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.sharedMesh&&AssetDatabase.Contains(r.sharedMesh)&&r.bones.Length>5);
                    checks["boundHumanoidOrAuthoredClips"]=player&&player.GetComponentsInChildren<Animator>().Any(a=>a.avatar&&a.avatar.isValid&&a.avatar.isHuman&&(a.GetComponent<WorldHumanoidLocomotion>()||a.runtimeAnimatorController&&a.runtimeAnimatorController.animationClips.Length>=4));
                    checks["characterController"]=player&&player.GetComponent<CharacterController>();
                }
                if(name=="moon")
                {
                    checks["terrainCollision"]=terrain&&terrain.GetComponent<TerrainCollider>()&&terrain.terrainData.size.x>=128;
                    checks["multipleCraters"]=UnityEngine.Object.FindObjectsByType<WorldEnvironment>(FindObjectsSortMode.None).Any(e=>e.Craters.Count>=2);
                    checks["physicalCave"]=UnityEngine.Object.FindObjectsByType<WorldCave>(FindObjectsSortMode.None).Any(c=>c.GetComponent<MeshCollider>());
                    checks["lunarResidents"]=UnityEngine.Object.FindObjectsByType<IntelligentNPC>(FindObjectsSortMode.None).Length>=1;
                    var survival=player?player.GetComponent<WorldSurvival>():null;
                    checks["survivalAndHabitat"]=survival&&survival.Environment&&survival.Habitat&&survival.OxygenSeconds>0;
                    checks["localLunarGravity"]=player&&player.UseLocalGravity&&Math.Abs(player.Gravity-1.62f)<.05;
                }
                if(name=="status"||name=="model")
                {
                    checks["readOnly"]=(string)row["beforeSceneDigest"]==SceneDigest();
                    checks["noFalseMissingTools"]=!answer.Text.Contains("инструментов нет")&&!answer.Text.Contains("отсутствует команда")&&!answer.Text.Contains("инструмента нет");
                    if(name=="model")checks["actualAssistantModelNamed"]=answer.Text.IndexOf(((string)report["model"]).Split(':')[0],StringComparison.OrdinalIgnoreCase)>=0;
                }
                if(name=="open_world")
                {
                    checks["largePhysicalWorld"]=terrain&&terrain.terrainData.size.x>=512&&terrain.terrainData.size.z>=512&&terrain.GetComponent<TerrainCollider>();
                    checks["realPersistentStars"]=UnityEngine.Object.FindObjectsByType<WorldStarfield>(FindObjectsSortMode.None).Any(s=>s.StarCount>=100&&s.GetComponent<MeshFilter>()&&AssetDatabase.Contains(s.GetComponent<MeshFilter>().sharedMesh));
                    checks["cavesPreservedAfterResize"]=UnityEngine.Object.FindObjectsByType<WorldCave>(FindObjectsSortMode.None).Any(c=>c.Terrain&&c.GetComponent<MeshCollider>()&&c.Terrain.terrainData.IsHole(Mathf.Clamp((int)((c.Interior.x-c.Terrain.transform.position.x)/c.Terrain.terrainData.size.x*c.Terrain.terrainData.holesResolution),0,c.Terrain.terrainData.holesResolution-1),Mathf.Clamp((int)((c.Interior.z-c.Terrain.transform.position.z)/c.Terrain.terrainData.size.z*c.Terrain.terrainData.holesResolution),0,c.Terrain.terrainData.holesResolution-1)));
                    checks["survivalPreservedAfterResize"]=player&&player.GetComponent<WorldSurvival>()&&player.GetComponent<WorldSurvival>().Habitat;
                    checks["nightBackground"]=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Any(c=>c.enabled&&c.backgroundColor.maxColorComponent<.12f);
                    Physics.SyncTransforms();checks["groundBelowPlayer"]=player&&Physics.Raycast(player.transform.position+Vector3.up*.4f,Vector3.down,out var hit,3)&&!hit.collider.isTrigger;
                    row["playerPosition"]=player?new JArray(player.transform.position.x,player.transform.position.y,player.transform.position.z):null;
                }
                if(name=="general_code")
                {
                    var lights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>l.type==LightType.Point&&Math.Abs(l.range-6)<.01&&Math.Abs(l.transform.position.y-8)<.01&&Math.Abs(new Vector2(l.transform.position.x,l.transform.position.z).magnitude-18)<.05).ToArray();
                    checks["compiledAndExecuted"]=answer.Steps.Any(s=>s.Tool=="execute_editor_code"&&s.Success);
                    checks["twelveMeasuredLights"]=lights.Length==12;checks["worldAndHeroPreserved"]=player&&terrain;
                }

            checks["notBlocked"]=answer.Task?.Status!="blocked"&&answer.Task?.Status!="partial";
            row["checks"]=checks;row["passed"]=checks.Properties().All(p=>(bool)p.Value);row["answer"]=JObject.FromObject(answer);report["caseIndex"]=index+1;report["startedCase"]=false;
            Directory.CreateDirectory("Assets/RegressionAcceptance");EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/RegressionAcceptance/"+name+".unity");Save();
            if(index+1==Cases.Length){report["packageUnchanged"]=(string)report["packageDigest"]==PackageDigest();report["completed"]=true;report["passed"]=((JArray)report["cases"]).All(c=>(bool)c["passed"])&&(bool)report["packageUnchanged"];report["finishedUtc"]=DateTime.UtcNow.ToString("O");Save();controller.Dispose();EditorApplication.Exit((bool)report["passed"]?0:1);}
        }
        catch(Exception error){if(report==null)report=new JObject();report["error"]=error.ToString();report["completed"]=true;report["passed"]=false;Save();EditorApplication.Exit(1);}
        finally{running=false;}
    }
    static void Save(){if(!string.IsNullOrEmpty(Output))File.WriteAllText(Output,report.ToString(Formatting.Indented));}
}
