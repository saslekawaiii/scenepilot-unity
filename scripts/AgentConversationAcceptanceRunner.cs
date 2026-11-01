using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;

/// <summary>Exact user requests and an unrelated C# operation, all through the public chat controller.
/// No generated plans, mock provider, predetermined tool sequence, or fallback scene mutations.</summary>
public static class AgentConversationAcceptanceRunner
{
    public static async void Run()
    {
        string output=Environment.GetEnvironmentVariable("WW_CONVERSATION_OUTPUT");
        ChatController controller=null;
        var report=new JObject{["startedUtc"]=DateTime.UtcNow.ToString("O"),["providerInjected"]=false,["paidApiUsed"]=false,["cases"]=new JArray()};
        try
        {
            string project=Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName);
            if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE")!="1"||project!=Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""))throw new InvalidOperationException("Explicit isolated project required.");
            if(string.IsNullOrEmpty(output)||File.Exists(output))throw new IOException("Unique new output required.");
            string package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WorldPlayer).Assembly).resolvedPath;
            var hashes=new JObject();foreach(string file in Directory.GetFiles(package,"*",SearchOption.AllDirectories).OrderBy(p=>p))if(!file.EndsWith(".DS_Store"))hashes[file.Substring(package.Length+1)]=Hash(File.ReadAllBytes(file));report["packageFiles"]=hashes;
            string history=Path.Combine(project,"UserSettings/ConversationAcceptance",Guid.NewGuid().ToString("N"));
            var connection=Newtonsoft.Json.JsonConvert.DeserializeObject<ChatModelConnection>(File.ReadAllText(Environment.GetEnvironmentVariable("WW_AGENT_CONNECTION")));
            string model=new ChatModelConnections(Path.Combine(history,"models")).Add(connection).ModelKey;
            report["model"]=connection.Model;report["history"]=history;
            controller=new ChatController(historyFolder:history,modelConnectionsFolder:Path.Combine(history,"models"));
            controller.Settings.ModelKey=model;controller.Settings.Power=AssistantPower.Deep;controller.Settings.Mode=AssistantMode.Execute;
            controller.Settings.AllowEditorCode=true;controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=true;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var prompts=new[]{
                ("hero","Создай персонажа: сделай главного героя не используй примитив. используй реальную модель. добавь ей анимации ходьбы, передвижения и тд"),
                ("moon","добавь карту высот луны. кратеры и пещеры и лунных жителей чтобы игрок мог выживать на луне"),
                ("status","что ты создал а что не смог?"),
                ("open_world","ты не создал лунную поверхность. создай лунную поверхность чтобы я мог по ней ходить. большой открытый мир. добавь звездное небо и ночную атмосферу луны"),
                ("model","а какая сейчас модель?"),
                ("general_code","Через C# в редакторе создай круг из 12 точечных источников света радиусом 18 метров вокруг начала координат. Высота каждого 8 метров, дальность света 6 метров. Верни измеренные число источников и радиус. Сохрани существующий мир и героя.")
            };
            foreach(var item in prompts)
            {
                var row=new JObject{["case"]=item.Item1,["prompt"]=item.Item2,["passed"]=false};((JArray)report["cases"]).Add(row);
                var before=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(t=>t.GetInstanceID()).ToArray();
                controller.Changed+=Changed;
                await controller.SendAsync(item.Item2);
                controller.Changed-=Changed;
                var answer=controller.Current.Messages.Last(m=>m.Role=="assistant");
                row["messages"]=JArray.FromObject(controller.Current.Messages);row["elapsedMs"]=answer.ElapsedMs;
                var checks=new JObject{["realModelUsed"]=answer.Attempts.Count>0,["noError"]=!answer.IsError,["noPendingPlan"]=answer.PendingPlan==null};
                var player=UnityEngine.Object.FindFirstObjectByType<WorldPlayer>();
                var terrain=UnityEngine.Object.FindFirstObjectByType<Terrain>();
                if(item.Item1=="hero")
                {
                    checks["onePlayableHero"]=UnityEngine.Object.FindObjectsByType<WorldPlayer>(FindObjectsSortMode.None).Length==1;
                    checks["importedSkinnedModel"]=player&&player.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.sharedMesh&&AssetDatabase.Contains(r.sharedMesh)&&r.bones.Length>5);
                    checks["realAnimator"]=player&&player.GetComponentsInChildren<Animator>().Any(a=>a.runtimeAnimatorController&&a.runtimeAnimatorController.animationClips.Length>=4);
                    checks["characterController"]=player&&player.GetComponent<CharacterController>();
                }
                if(item.Item1=="moon")
                {
                    checks["terrainCollision"]=terrain&&terrain.GetComponent<TerrainCollider>()&&terrain.terrainData.size.x>=128;
                    checks["multipleCraters"]=UnityEngine.Object.FindObjectsByType<WorldEnvironment>(FindObjectsSortMode.None).Any(e=>e.Craters.Count>=2);
                    checks["physicalCave"]=UnityEngine.Object.FindObjectsByType<WorldCave>(FindObjectsSortMode.None).Any(c=>c.GetComponent<MeshCollider>());
                    checks["lunarResidents"]=UnityEngine.Object.FindObjectsByType<IntelligentNPC>(FindObjectsSortMode.None).Length>=1;
                    var survival=player?player.GetComponent<WorldSurvival>():null;
                    checks["survivalAndHabitat"]=survival&&survival.Environment&&survival.Habitat&&survival.OxygenSeconds>0;
                    checks["localLunarGravity"]=player&&player.UseLocalGravity&&Math.Abs(player.Gravity-1.62f)<.05;
                }
                if(item.Item1=="status"||item.Item1=="model")
                {
                    checks["readOnly"]=before.SequenceEqual(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(t=>t.GetInstanceID()));
                    checks["noFalseMissingTools"]=!answer.Text.Contains("инструментов нет")&&!answer.Text.Contains("отсутствует команда")&&!answer.Text.Contains("инструмента нет");
                    if(item.Item1=="model")checks["actualAssistantModelNamed"]=answer.Text.IndexOf(connection.Model.Split(':')[0],StringComparison.OrdinalIgnoreCase)>=0;
                }
                if(item.Item1=="open_world")
                {
                    checks["largePhysicalWorld"]=terrain&&terrain.terrainData.size.x>=512&&terrain.terrainData.size.z>=512&&terrain.GetComponent<TerrainCollider>();
                    checks["realPersistentStars"]=UnityEngine.Object.FindObjectsByType<WorldStarfield>(FindObjectsSortMode.None).Any(s=>s.StarCount>=100&&s.GetComponent<MeshFilter>()&&AssetDatabase.Contains(s.GetComponent<MeshFilter>().sharedMesh));
                    checks["nightBackground"]=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Any(c=>c.enabled&&c.backgroundColor.maxColorComponent<.12f);
                    Physics.SyncTransforms();checks["groundBelowPlayer"]=player&&Physics.Raycast(player.transform.position+Vector3.up*.4f,Vector3.down,out var hit,3)&&!hit.collider.isTrigger;
                    row["playerPosition"]=player?new JArray(player.transform.position.x,player.transform.position.y,player.transform.position.z):null;
                }
                if(item.Item1=="general_code")
                {
                    var lights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>l.type==LightType.Point&&Math.Abs(l.range-6)<.01&&Math.Abs(l.transform.position.y-8)<.01&&Math.Abs(new Vector2(l.transform.position.x,l.transform.position.z).magnitude-18)<.05).ToArray();
                    checks["compiledAndExecuted"]=answer.Steps.Any(s=>s.Tool=="execute_editor_code"&&s.Success);
                    checks["twelveMeasuredLights"]=lights.Length==12;checks["worldAndHeroPreserved"]=player&&terrain;
                }
                row["checks"]=checks;row["passed"]=checks.Properties().All(p=>(bool)p.Value);Write();
                void Changed(){row["messages"]=JArray.FromObject(controller.Current.Messages);Write();}
            }
            Directory.CreateDirectory("Assets/ConversationAcceptance");AssetDatabase.Refresh();
            string scene="Assets/ConversationAcceptance/"+Path.GetFileNameWithoutExtension(output)+".unity";
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),scene);report["scene"]=scene;
            report["packageUnchanged"]=hashes.Properties().All(p=>File.Exists(Path.Combine(package,p.Name))&&Hash(File.ReadAllBytes(Path.Combine(package,p.Name)))==(string)p.Value);
            report["finishedUtc"]=DateTime.UtcNow.ToString("O");report["passed"]=((JArray)report["cases"]).All(c=>(bool)c["passed"])&&(bool)report["packageUnchanged"];Write();
            controller.Dispose();EditorApplication.Exit((bool)report["passed"]?0:1);
        }
        catch(Exception e){report["error"]=e.ToString();Write();controller?.Dispose();EditorApplication.Exit(1);}
        void Write(){if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,report.ToString(Newtonsoft.Json.Formatting.Indented));}}
    }
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
}
