using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;

/// <summary>Real chat corpus: no injected provider, scripted tool plans, fallback mutations or per-case package edits.</summary>
public static class GeneralAgentAcceptanceRunner
{
    static readonly Dictionary<string,string> Prompts=new Dictionary<string,string>{
        ["mesh"]="Построй небольшой мост длиной 8 метров и шириной 3 метра из собственного меша с коллайдером. Не используй куб вместо моста. Проверь геометрию.",
        ["physics"]="Создай красный мяч на высоте 5 метров. Добавь ему физику: масса 2 кг, гравитация и отскок 0.7. Сделай под ним площадку с коллайдером, чтобы он падал на неё.",
        ["ui"]="Создай экранное меню с панелью и кнопкой «Скрыть панель». Кнопка должна реально переключать видимость этой панели.",
        ["terrain"]="Создай небольшой каменистый мир без деревьев, домов и воды. Добавь в центре кратер радиусом 10 метров и глубиной 3 метра, а в стороне пещеру с проходимым входом.",
        ["code"]="Создай отдельный пустой узел и подготовь компонент на C#, который отсчитывает 15 секунд и затем показывает сообщение о завершении. Нужен реальный код таймера, а не вращающийся куб.",
        ["discovery"]="Найди доступные возможности для создания интерфейса, физики и собственной геометрии, затем загрузи подходящий навык. Сцену пока не изменяй, только объясни найденные возможности."
    };
    public static async void Run()
    {
        string output=Environment.GetEnvironmentVariable("WW_AGENT_OUTPUT");ChatController controller=null;
        var report=new JObject{["startedUtc"]=DateTime.UtcNow.ToString("O"),["providerInjected"]=false,["paidApiUsed"]=false,["cases"]=new JArray(),["passed"]=false};
        try
        {
            string project=Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName);
            if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE")!="1"||project!=Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""))throw new InvalidOperationException("Explicit isolated project required.");
            if(string.IsNullOrEmpty(output)||File.Exists(output))throw new IOException("A unique new receipt is required.");
            string package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WorldPlayer).Assembly).resolvedPath;
            var source=new JObject();foreach(string file in Directory.GetFiles(package,"*",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal))if(!file.EndsWith(".DS_Store",StringComparison.Ordinal))source[file.Substring(package.Length+1).Replace('\\','/')]=Hash(File.ReadAllBytes(file));report["packageFiles"]=source;
            report["harnessSha256"]=Hash(File.ReadAllBytes("Assets/Editor/GeneralAgentAcceptanceRunner.cs"));
            string model=Environment.GetEnvironmentVariable("WW_AGENT_MODEL")??"ollama:qwen3.5:9b";report["model"]=model;
            string[] selected=(Environment.GetEnvironmentVariable("WW_AGENT_CASES")??"mesh,physics,ui,terrain,code,discovery").Split(',');
            foreach(string key in selected)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                string history=Path.Combine(project,"UserSettings/GeneralAgentAcceptance",Guid.NewGuid().ToString("N"));
                string selectedModel=model;
                string connectionFile=Environment.GetEnvironmentVariable("WW_AGENT_CONNECTION");
                if(!string.IsNullOrEmpty(connectionFile))
                {
                    // The same public connection store used by the UI; only model
                    // configuration changes, never the provider/executor or tool plan.
                    if(new FileInfo(connectionFile).Length>65536)throw new IOException("Connection metadata too large.");
                    var connection=Newtonsoft.Json.JsonConvert.DeserializeObject<ChatModelConnection>(File.ReadAllText(connectionFile));
                    selectedModel=new ChatModelConnections(Path.Combine(history,"models")).Add(connection).ModelKey;
                    report["model"]=connection.Protocol.ToString().ToLowerInvariant()+":"+connection.Model;
                    report["connectionEndpoint"]=ChatModelConnections.ApiBase(connection);
                }
                controller=new ChatController(historyFolder:history,modelConnectionsFolder:Path.Combine(history,"models"));
                if(selectedModel.StartsWith("ollama:",StringComparison.Ordinal))await controller.RefreshLocalModelsAsync();
                controller.Settings.ModelKey=selectedModel;controller.Settings.Power=AssistantPower.Deep;controller.Settings.Mode=key=="discovery"?AssistantMode.Ask:AssistantMode.Execute;controller.Settings.UseSkills=true;controller.Settings.AutoApply=true;controller.Settings.AutoVerify=true;
                var row=new JObject{["case"]=key,["prompt"]=Prompts[key],["history"]=history,["passed"]=false};((JArray)report["cases"]).Add(row);
                float? craterBaseline=null;int observedRound=0,observedSteps=0;
                controller.Changed+=()=>
                {
                    var live=controller.Current.Messages.LastOrDefault(m=>m.Role=="assistant");
                    if(key=="terrain"&&live!=null)
                    {
                        // Measure the real surface before carving. A lunar rebuild
                        // changes its base profile: the original noise is no longer
                        // a valid reference. This observer never edits the scene.
                        foreach(var step in live.Steps.Skip(observedSteps))
                            if(step.Success&&(step.Tool=="create_world"||step.Tool=="shape_terrain"&&JObject.Parse(step.Summary)["operation"]?.ToString()=="lunar"))craterBaseline=null;
                        observedSteps=live.Steps.Count;
                        var attempt=live.Attempts.LastOrDefault();
                        if(attempt?.Accepted==true&&attempt.Round!=observedRound)
                        {
                            observedRound=attempt.Round;var pending=EditorPlan.Parse(attempt.NormalizedResponse??attempt.Response);
                            if(pending.actions.Any(a=>a.tool=="shape_terrain"&&(string)a.args["operation"]=="crater")&&!craterBaseline.HasValue)
                            {
                                var surfaces=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
                                if(surfaces.Length==1){craterBaseline=surfaces[0].terrainData.GetInterpolatedHeight(.5f,.5f);row["measuredCenterBeforeCrater"]=craterBaseline.Value;}
                            }
                        }
                    }
                    row["messages"]=JArray.FromObject(controller.Current.Messages);Write(output,report);
                };
                await controller.SendAsync(Prompts[key]);var answer=controller.Current.Messages.Last(m=>m.Role=="assistant");
                row["elapsedMs"]=answer.ElapsedMs;row["status"]=answer.Status;row["messages"]=JArray.FromObject(controller.Current.Messages);
                var objects=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(t=>t.gameObject.scene==UnityEngine.SceneManagement.SceneManager.GetActiveScene()).Select(t=>t.gameObject).ToArray();
                var checks=new JObject{["realModelUsed"]=answer.Attempts.Count>0,["noError"]=!answer.IsError};
                if(key=="mesh")checks["actualCustomBridge"]=objects.Select(g=>g.GetComponent<MeshFilter>()).Any(f=>f&&f.sharedMesh&&AssetDatabase.Contains(f.sharedMesh)&&f.GetComponent<MeshCollider>()&&Math.Abs(Mathf.Max(f.sharedMesh.bounds.size.z,f.sharedMesh.bounds.size.x)-8)<.1f&&Math.Abs(Mathf.Min(f.sharedMesh.bounds.size.z,f.sharedMesh.bounds.size.x)-3)<.1f);
                if(key=="physics")
                {checks["redAppearance"]=objects.Select(g=>g.GetComponent<Rigidbody>()).Any(b=>b&&b.GetComponent<Renderer>()&&b.GetComponent<Renderer>().sharedMaterial&&b.GetComponent<Renderer>().sharedMaterial.color.r>.8f&&b.GetComponent<Renderer>().sharedMaterial.color.g<.2f&&b.GetComponent<Renderer>().sharedMaterial.color.b<.2f);checks["actualBody"]=objects.Select(g=>g.GetComponent<Rigidbody>()).Any(b=>b&&b.useGravity&&Math.Abs(b.mass-2)<.01f&&b.transform.position.y>=4.5f&&b.GetComponent<Collider>()&&b.GetComponent<Collider>().sharedMaterial&&Math.Abs(b.GetComponent<Collider>().sharedMaterial.bounciness-.7f)<.01f);checks["physicalGround"]=objects.Select(g=>g.GetComponent<Collider>()).Any(c=>c&&c.enabled&&c.bounds.size.x>=4&&c.bounds.center.y<1);}
                if(key=="ui")
                {
                    var bound=objects.Select(g=>g.GetComponent<Button>()).FirstOrDefault(b=>b&&b.onClick.GetPersistentEventCount()>0&&b.GetComponent<WorldUIButton>()&&b.GetComponent<WorldUIButton>().Target);
                    checks["realBoundButtonAndCanvas"]=objects.Any(g=>g.GetComponent<Canvas>())&&bound;
                    var panel=bound?bound.GetComponent<WorldUIButton>().Target:null;var panelRect=panel?panel.GetComponent<RectTransform>():null;var buttonRect=bound?bound.GetComponent<RectTransform>():null;
                    checks["toggleRemainsReachable"]=bound&&panel&&!panel.GetComponent<Canvas>()&&!bound.transform.IsChildOf(panel.transform);
                    checks["panelVisibleAroundButton"]=panelRect&&buttonRect&&panelRect.rect.width*panelRect.rect.height>2*buttonRect.rect.width*buttonRect.rect.height;
                }
                if(key=="terrain")
                {checks["actualTerrain"]=objects.Any(g=>g.GetComponent<Terrain>());checks["craterRecord"]=objects.Select(g=>g.GetComponent<WorldEnvironment>()).Any(e=>e&&e.Craters.Any(c=>Math.Abs(c.Radius-10)<.1f&&Math.Abs(c.Depth-3)<.1f));checks["physicalCave"]=objects.Any(g=>g.GetComponent<WorldCave>()&&g.GetComponent<MeshCollider>());var terrain=objects.Select(g=>g.GetComponent<Terrain>()).FirstOrDefault(t=>t);float finalHeight=terrain?terrain.terrainData.GetInterpolatedHeight(.5f,.5f):float.NaN;row["measuredCenterAfterCrater"]=finalHeight;checks["actualCraterDepth"]=craterBaseline.HasValue&&Math.Abs(craterBaseline.Value-finalHeight-3)<.15f;}
                if(key=="code"){checks["actualEmptyNode"]=objects.Any(g=>g.GetComponent<GeneratedSceneObject>()&&g.GetComponent<GeneratedSceneObject>().Kind=="node");checks["completeTimerDraftForReview"]=answer.PendingPlan?.actions.Any(a=>a.tool=="draft_script"&&((string)a.args["code"]).Contains("15")&&new[]{"Time.","WaitForSeconds","Invoke(","Task.Delay"}.Any(timer=>((string)a.args["code"]).Contains(timer)))==true;}
                if(key=="discovery"){checks["discoveryUsed"]=answer.Steps.Any(s=>s.Success&&s.Tool=="discover_tools");checks["skillLoaded"]=answer.Steps.Any(s=>s.Success&&s.Tool=="read_skill")||answer.Skills.Any(id=>answer.Attempts.Any(t=>t.Messages.Any(m=>(m.Content??"").Contains("id=\""+id+"\"")))) ;row["skillLoadedBy"]=answer.Steps.Any(s=>s.Success&&s.Tool=="read_skill")?"read_skill":(bool)checks["skillLoaded"]?"automatic_verified_markdown_context":"none";checks["sceneUntouched"]=objects.Length==0;}
                row["checks"]=checks;row["passed"]=checks.Properties().All(p=>(bool)p.Value);
                if(objects.Length>0){Directory.CreateDirectory("Assets/GeneralAgentAcceptance");AssetDatabase.Refresh();string scene="Assets/GeneralAgentAcceptance/"+Path.GetFileNameWithoutExtension(output)+"-"+key+".unity";EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),scene);row["scene"]=scene;}
                controller.Dispose();controller=null;Write(output,report);
            }
            report["packageUnchanged"]=source.Properties().All(p=>File.Exists(Path.Combine(package,p.Name))&&Hash(File.ReadAllBytes(Path.Combine(package,p.Name)))==(string)p.Value);
            report["passed"]=((JArray)report["cases"]).All(c=>(bool)c["passed"])&&(bool)report["packageUnchanged"];report["finishedUtc"]=DateTime.UtcNow.ToString("O");Write(output,report);EditorApplication.Exit((bool)report["passed"]?0:1);
        }
        catch(Exception error){report["error"]=error.ToString();if(!string.IsNullOrEmpty(output))Write(output,report);controller?.Dispose();EditorApplication.Exit(1);}
    }
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static void Write(string output,JObject report){Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,report.ToString(Newtonsoft.Json.Formatting.Indented));}
}
