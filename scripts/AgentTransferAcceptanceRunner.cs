using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;

/// <summary>Held-out tasks through the public controller and a real provider.
/// The harness never supplies a plan, repairs a generated scene or substitutes a provider.</summary>
public static class AgentTransferAcceptanceRunner
{
    static JObject report;
    static string output;
    static ChatController controller;
    static string package;
    static string PackageDigest()=>Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",Directory.GetFiles(package,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".DS_Store")).OrderBy(p=>p,StringComparer.Ordinal).Select(p=>p.Substring(package.Length+1)+":"+Hash(File.ReadAllBytes(p))))));
    public static async void Run()
    {
        output=Environment.GetEnvironmentVariable("WW_TRANSFER_OUTPUT");
        report=new JObject{["startedUtc"]=DateTime.UtcNow.ToString("O"),["providerInjected"]=false,["sceneRepairByHarness"]=false,["paidApiUsed"]=false,["cases"]=new JArray()};
        try
        {
            string project=Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName);
            if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE")!="1"||project!=Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""))throw new InvalidOperationException("Explicit isolated project required.");
            if(string.IsNullOrWhiteSpace(output)||File.Exists(output))throw new InvalidOperationException("New receipt path required.");
            package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WorldPlayer).Assembly).resolvedPath;string digest=PackageDigest();report["packageDigest"]=digest;
            report["harnessSha256"]=Hash(File.ReadAllBytes("Assets/Editor/AgentTransferAcceptanceRunner.cs"));
            var connection=JsonConvert.DeserializeObject<ChatModelConnection>(File.ReadAllText(Environment.GetEnvironmentVariable("WW_AGENT_CONNECTION")));
            report["model"]=connection.Model;
            foreach(string suite in (Environment.GetEnvironmentVariable("WW_TRANSFER_CASES")??"character,physics,geometry,inspection,terrain").Split(','))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                string history=Path.Combine(project,"UserSettings/TransferAcceptance",Guid.NewGuid().ToString("N"));
                string key=new ChatModelConnections(Path.Combine(history,"models")).Add(connection).ModelKey;
                controller=new ChatController(historyFolder:history,modelConnectionsFolder:Path.Combine(history,"models"));
                controller.Settings.ModelKey=key;await controller.RefreshModelConnectionsAsync();
                if(!controller.Connected)throw new InvalidOperationException("Model unavailable in real UI connection gate.");
                controller.Settings.Power=AssistantPower.Deep;controller.Settings.Mode=AssistantMode.Execute;controller.Settings.AllowEditorCode=true;controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=true;controller.Settings.AutoApply=true;controller.Settings.AutoVerify=true;
                if(suite=="character")
                {
                    await Send("hero","Создай главного героя с реальной моделью и анимацией ходьбы, чтобы им можно было управлять.",()=>new JObject{["onePlayer"]=Players().Length==1,["skinnedModel"]=Players().Any(p=>p.GetComponentInChildren<SkinnedMeshRenderer>()),["motor"]=Players().Any(p=>p.GetComponent<CharacterController>())});
                    var previous=Players().SingleOrDefault();string identity=previous?previous.GetComponent<GeneratedSceneObject>().ObjectId:"";var motor=previous?previous.GetComponent<CharacterController>():null;
                    await Send("replace_appearance","Мне не нравится этот персонаж. Сделай другого: девушку в стиле реализм. Найди подходящую модель сам и замени облик, сохрани управление и камеру.",()=>new JObject{["onePlayer"]=Players().Length==1,["sameIdentityAndMotor"]=Players().Any(p=>p.GetComponent<GeneratedSceneObject>().ObjectId==identity&&p.GetComponent<CharacterController>()==motor),["realLibraryFemale"]=Players().Any(p=>p.GetComponent<WorldModelVisual>()&&p.GetComponent<WorldModelVisual>().SourcePath.ToLowerInvariant().Contains("female")),["validHumanoid"]=Players().Any(p=>p.GetComponentsInChildren<Animator>().Any(a=>a.avatar&&a.avatar.isValid&&a.avatar.isHuman)),["locomotionConfigured"]=Players().Any(p=>p.GetComponentInChildren<WorldHumanoidLocomotion>()||p.GetComponentsInChildren<Animator>().Any(a=>a.runtimeAnimatorController)),["cameraPreserved"]=Players().Any(p=>p.View)});
                }
                if(suite=="new_avatar")await Send("cold_realistic_hero","В этой пустой сцене создай управляемого главного героя: реалистичного взрослого мужчину с ригом и анимацией ходьбы. Найди и загрузи подходящую настоящую модель, которой ещё нет в кэше, сохрани возможность движения и камеру. Стилизованный пресет не подходит.",()=>new JObject{["onePlayer"]=Players().Length==1,["libraryModel"]=Players().Any(p=>p.GetComponent<WorldModelVisual>()&&p.GetComponent<WorldModelVisual>().SourcePath.ToLowerInvariant().Contains("/male_adult")),["rigAndLocomotion"]=Players().Any(p=>p.GetComponentInChildren<WorldHumanoidLocomotion>()&&p.GetComponentsInChildren<Animator>().Any(a=>a.avatar&&a.avatar.isValid&&a.avatar.isHuman)),["cameraAndMotor"]=Players().Any(p=>p.View&&p.GetComponent<CharacterController>())});
                if(suite=="physics")await Send("seesaw","Через C# собери работающие качели-балансир: доска длиной 6 м и шириной 1 м, центр на высоте 1 м. У доски Rigidbody массой 12 кг и коллайдер. Петля HingeJoint в центре, ось вдоль Z, ограничение от -25 до 25 градусов. Основание неподвижное. Не заменяй физику декоративным объектом.",()=>new JObject{["actualBodyAndJoint"]=Objects().Select(g=>g.GetComponent<HingeJoint>()).Any(j=>j&&j.GetComponent<Rigidbody>()&&Math.Abs(j.GetComponent<Rigidbody>().mass-12)<.1&&j.GetComponentInChildren<Collider>()&&j.useLimits&&Math.Abs(j.limits.min+25)<.1&&Math.Abs(j.limits.max-25)<.1&&Math.Abs(j.axis.normalized.z)>.99),["plankDimensions"]=Objects().Select(g=>g.GetComponent<Renderer>()).Any(r=>r&&Math.Abs(r.bounds.size.x-6)<.1&&Math.Abs(r.bounds.size.z-1)<.1)});
                if(suite=="geometry")await Send("ramp","Через C# построй пандус из собственного меша: ширина 3 м, длина вдоль Z 7 м, подъём от Y=0 до Y=2. По нему можно ходить, нужен MeshCollider. Примитив вместо меша не подходит. Верни измеренные размеры.",()=>new JObject{["customCollisionMesh"]=Objects().Select(g=>g.GetComponent<MeshFilter>()).Any(f=>f&&f.sharedMesh&&AssetDatabase.Contains(f.sharedMesh)&&f.GetComponent<MeshCollider>()&&Math.Abs(f.sharedMesh.bounds.size.x-3)<.1&&Math.Abs(f.sharedMesh.bounds.size.z-7)<.1&&Math.Abs(f.sharedMesh.bounds.size.y-2)<.1)});
                if(suite=="inspection")
                {
                    controller.Settings.Mode=AssistantMode.Ask;
                    await Send("library_inspection","Найди в доступной библиотеке реалистичные модели мужчины, женщины и ребёнка. Назови реальные ID, лицензию и порядок импорта. Ничего не скачивай и сцену не меняй.",()=>new JObject{["sceneUntouched"]=Objects().Length==0,["realCatalogRead"]=controller.Current.Messages.Last().Steps.Any(s=>s.Success&&s.Tool=="model_search"),["noMutations"]=controller.Current.Messages.Last().Steps.All(s=>SceneToolExecutor.IsReadOnlyTool(s.Tool))});
                }
                if(suite=="terrain")
                {
                    await Send("new_world","Сделай каменистый мир с горами размером 256 на 256 м, без деревьев и зданий. Земля должна быть оранжевой. Добавь ночное звёздное небо и героя, которым можно ходить по поверхности.",()=>new JObject{["physicalTerrain"]=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Any(t=>t.GetComponent<TerrainCollider>()&&Math.Abs(t.terrainData.size.x-256)<1&&Math.Abs(t.terrainData.size.z-256)<1),["orangeTerrainTint"]=OrangeGround(),["noTreesOrBuildings"]=!Objects().Any(g=>g.GetComponent<GeneratedSceneObject>()&&(g.GetComponent<GeneratedSceneObject>().Kind=="tree"||g.GetComponent<GeneratedSceneObject>().Kind=="house")),["stars"]=UnityEngine.Object.FindObjectsByType<WorldStarfield>(FindObjectsSortMode.None).Any(s=>s.StarCount>=100),["player"]=Players().Length==1});
                    var previous=Players().SingleOrDefault();var beforeTerrain=UnityEngine.Object.FindFirstObjectByType<Terrain>();float beforeMax=MaxHeight(beforeTerrain);
                    await Send("continue_after_idle","Увеличь высоту гор на этом же мире, а ночное небо, героя и его управление сохрани. Новый мир не создавай.",()=>new JObject{["oneTerrain"]=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Length==1,["heightsIncreased"]=MaxHeight(UnityEngine.Object.FindFirstObjectByType<Terrain>())>beforeMax+.1f,["samePlayer"]=previous&&Players().Contains(previous),["starsPreserved"]=UnityEngine.Object.FindObjectsByType<WorldStarfield>(FindObjectsSortMode.None).Length==1});
                }
                controller.Dispose();controller=null;
            }
            report["packageUnchanged"]=digest==PackageDigest();report["passed"]=((JArray)report["cases"]).All(r=>(bool)r["passed"])&&(bool)report["packageUnchanged"];report["finishedUtc"]=DateTime.UtcNow.ToString("O");Write();EditorApplication.Exit((bool)report["passed"]?0:1);
        }
        catch(Exception error){report["error"]=error.ToString();Write();controller?.Dispose();EditorApplication.Exit(1);}
    }
    static async Task Send(string name,string prompt,Func<JObject> evaluate)
    {
        var row=new JObject{["case"]=name,["prompt"]=prompt,["passed"]=false};((JArray)report["cases"]).Add(row);
        void Changed(){row["messages"]=JArray.FromObject(controller.Current.Messages);Write();}
        controller.Changed+=Changed;await controller.SendAsync(prompt);controller.Changed-=Changed;
        var answer=controller.Current.Messages.Last();var checks=evaluate();checks["realModelUsed"]=answer.Attempts.Count>0;checks["noError"]=!answer.IsError;checks["noPendingPlan"]=answer.PendingPlan==null;checks["notBlocked"]=answer.Task?.Status!="blocked"&&answer.Task?.Status!="partial";checks["noUnrecoveredProtocolFailure"]=!answer.IsError||!answer.Text.Contains("FUNCTION_PROTOCOL");row["protocolRepairCount"]=answer.Attempts.Count(a=>(a.Error??"").Contains("FUNCTION_PROTOCOL"));
        row["checks"]=checks;row["elapsedMs"]=answer.ElapsedMs;row["messages"]=JArray.FromObject(controller.Current.Messages);row["passed"]=checks.Properties().All(p=>(bool)p.Value);
        Directory.CreateDirectory("Assets/TransferAcceptance");AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string path="Assets/TransferAcceptance/"+Path.GetFileNameWithoutExtension(output)+"-"+name+".unity";EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),path);row["scene"]=path;Write();
    }
    static bool OrangeGround()
    {
        foreach(var terrain in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))foreach(var layer in terrain.terrainData.terrainLayers)
        {
            if(!layer)continue;var tint=layer.diffuseRemapMax;
            if(tint.x>tint.y+.1f&&tint.y>tint.z+.1f)return true;
            if(!layer.diffuseTexture)continue;string path=AssetDatabase.GetAssetPath(layer.diffuseTexture);string extension=Path.GetExtension(path).ToLowerInvariant();
            if(!File.Exists(path)||!(extension==".png"||extension==".jpg"||extension==".jpeg")||new FileInfo(path).Length>16*1024*1024)continue;
            var sample=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                if(!ImageConversion.LoadImage(sample,File.ReadAllBytes(path)))continue;int orange=0;
                for(int y=0;y<8;y++)for(int x=0;x<8;x++){var color=sample.GetPixelBilinear((x+.5f)/8,(y+.5f)/8);if(color.r>.2f&&color.r>color.g*1.15f&&color.g>color.b*1.2f)orange++;}
                if(orange>=40)return true;
            }
            finally{UnityEngine.Object.DestroyImmediate(sample);}
        }
        return false;
    }
    static float MaxHeight(Terrain terrain)=>terrain?Enumerable.Range(0,81).Max(i=>terrain.terrainData.GetInterpolatedHeight((i%9)/8f,(i/9)/8f)):float.NaN;
    static WorldPlayer[] Players()=>UnityEngine.Object.FindObjectsByType<WorldPlayer>(FindObjectsSortMode.None);
    static GameObject[] Objects()=>UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(t=>t.gameObject.scene==UnityEngine.SceneManagement.SceneManager.GetActiveScene()).Select(t=>t.gameObject).ToArray();
    static string Hash(byte[] b){using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
    static void Write(){if(string.IsNullOrWhiteSpace(output))return;Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,report.ToString(Formatting.Indented));}
}
