using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>
    /// Explicit, destructive-to-the-test-scene acceptance command using the real local provider.
    /// No pending plan is ever approved here. Run without -quit: Run exits batch mode after completion.
    /// </summary>
    public static class ChatAcceptanceRunner
    {
        private static string ModelKey => Environment.GetEnvironmentVariable("WW_CHAT_MODEL") ?? new ChatSettings().ModelKey;
        private static AssistantPower Power => Enum.TryParse(Environment.GetEnvironmentVariable("WW_CHAT_POWER"), true, out AssistantPower value) && Enum.IsDefined(typeof(AssistantPower), value) ? value : AssistantPower.Balanced;
        private static bool running;

        [MenuItem("Tools/WorldWeaver/Testing/Chat Acceptance")]
        public static async void Run()
        {
            if (running) { UnityEngine.Debug.LogError("Chat acceptance is already running."); return; }
            running = true;
            ChatController controller = null;
            bool previousRunInBackground = Application.runInBackground;
            string output = null;
            var tests = new JArray();
            var capturedPending = new JObject();
            var report = new JObject {
                ["schemaVersion"] = 1, ["runner"] = typeof(ChatAcceptanceRunner).FullName,
                ["acceptanceCriteriaVersion"] = 2, ["lightCriterion"] = "Three actual orange Points within terrain bounds, at least3m above terrain/water, with range reaching ground; no render-quality claim.",
                ["startedUtc"] = DateTime.UtcNow.ToString("o"), ["unityVersion"] = Application.unityVersion,
                ["modelKey"] = ModelKey, ["power"] = Power.ToString(),
                ["providerMode"] = ModelKey.StartsWith("pollinations:", StringComparison.Ordinal) ? "real-free-cloud-inference" : "real-local-inference", ["tests"] = tests,
                ["capturedReviewOnlyPlans"] = capturedPending,
                ["scriptReviewGranted"] = false, ["pendingPlansAppliedByRunner"] = 0,
                ["limitations"] = new JArray("One sequential conversation, not an estimate of general reliability.",
                    "A pending C# draft is inspected but is not imported, compiled or executed by this runner.")
            };
            bool success = false;
            try
            {
                output = OutputPath();
                report["outputPath"] = output;
                Write(report, output);
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    throw new InvalidOperationException("Exit Play Mode and wait for compilation before acceptance.");
                if (ScriptDrafts.Pending)
                    throw new InvalidOperationException("A previously reviewed script import is still compiling.");
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    throw new OperationCanceledException("Acceptance canceled at the scene save prompt; no test scene created.");

                // Clearing the scene is confined to this explicitly invoked test command, never a chat tool.
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Selection.objects = Array.Empty<Object>();
                Application.runInBackground = true;
                string history = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UserSettings",
                    "WorldWeaverChatAcceptance", Guid.NewGuid().ToString("N"));
                controller = new ChatController(historyFolder: history);
                if (!controller.Models.Any(model => model.Key == ModelKey)) await controller.RefreshModelsAsync();
                controller.Settings.ModelKey = ModelKey;
                controller.Settings.Power = Power;
                controller.Settings.AutoApply = true;
                controller.SaveSettings();
                string sessionId = controller.Current.Id;
                report["sessionId"] = sessionId;
                report["historyFolder"] = history;
                report["initialScene"] = Capture(scene);

                // Do not refresh model catalogs here: the selected seeded model is local and the
                // catalog refresh also contacts the free cloud service. No provider factory is supplied.
                string[] prompts = {
                    "Создай лесную долину размером 160 на 160 метров: ровно 12 деревьев, 3 дома и 4 камня. Добавь подходящие свет и камеру для осмотра мира.",
                    "Теперь уменьши height существующего рельефа до 24 метров. Обнови именно эту долину, сохрани деревья, дома и камни; второй мир не создавай.",
                    "Создай в этой долине NPC по имени Игнат: роль — страж, личность — осторожный. Добавь ему знание: «Северные ворота открываются в шесть утра».",
                    "Теперь измени его цель: патрулировать площадь и северные ворота. Остальные сведения об этом страже сохрани.",
                    "Создай в долине ровно три новых оранжевых точечных источника света типа Point в разных местах около площади. Существующее солнце сохрани.",
                    "Напиши отдельный C# MonoBehaviour для плавного вращения объекта вокруг оси Y со скоростью 30 градусов в секунду. Подготовь код для моего ревью, пока не импортируй, не подключай и не исполняй его.",
                    "Какая модель сейчас выбрана и что находится в этой сцене? Только объясни в чате. Ничего не создавай и не изменяй; предыдущий черновик C# оставь ожидающим моего ревью."
                };
                for (int index = 0; index < prompts.Length; index++)
                {
                    if (!scene.IsValid() || !scene.isLoaded || SceneManager.GetActiveScene() != scene)
                        throw new InvalidOperationException("The acceptance scene changed while the test was running.");
                    var row = new JObject { ["number"] = index + 1, ["prompt"] = prompts[index] };
                    tests.Add(row);
                    var reasons = new List<string>();
                    var before = Capture(scene);
                    row["sceneBefore"] = before;
                    int previousMessageCount = controller.Current.Messages.Count;
                    var watch = Stopwatch.StartNew();
                    try { await controller.SendAsync(prompts[index]); }
                    catch (Exception error) { reasons.Add("SendAsync threw: " + Describe(error)); }
                    watch.Stop();
                    row["ms"] = watch.ElapsedMilliseconds;
                    var answer = controller.Current.Messages.Skip(previousMessageCount)
                        .LastOrDefault(m => m.Role == "assistant");
                    var after = Capture(scene);
                    row["sceneAfter"] = after;
                    row["response"] = answer?.Text ?? "";
                    row["status"] = answer?.Status ?? "No assistant message";
                    row["isError"] = answer?.IsError ?? true;
                    row["attempts"] = answer == null ? new JArray() : JArray.FromObject(answer.Attempts);
                    row["steps"] = answer == null ? new JArray() : JArray.FromObject(answer.Steps);
                    row["pendingPlan"] = answer?.PendingPlan == null ? null : JToken.FromObject(answer.PendingPlan);
                    row["messageElapsedMs"] = answer?.ElapsedMs ?? 0;
                    row["inputTokens"] = answer?.InputTokens ?? 0;
                    row["outputTokens"] = answer?.OutputTokens ?? 0;
                    Require(controller.Current.Id == sessionId, reasons, "Conversation unexpectedly changed.");
                    Require(answer != null, reasons, "No new assistant response was created.");
                    if (answer != null)
                    {
                        Require(!answer.IsError, reasons, "The assistant reported failure: " + answer.Status);
                        Require(!string.IsNullOrWhiteSpace(answer.Text), reasons, "The assistant response is empty.");
                        Require(!RawPlan(answer.Text), reasons, "Raw tool-plan JSON leaked into the chat response.");
                        if (index != 5) Require(answer.PendingPlan == null, reasons, "This operation remained pending instead of completing.");
                        if (answer.PendingPlan != null && answer.PendingPlan.actions.Any(a => a.tool == "draft_script"))
                            capturedPending[answer.Id] = JToken.FromObject(answer.PendingPlan);
                    }
                    Validate(index, before, after, answer, reasons);
                    foreach (var pending in capturedPending.Properties())
                    {
                        var retained = controller.Current.Messages.FirstOrDefault(m => m.Id == pending.Name)?.PendingPlan;
                        Require(retained != null && JToken.DeepEquals(pending.Value, JToken.FromObject(retained)),
                            reasons, "A previously captured review-only plan was lost or modified: " + pending.Name);
                    }
                    row["observed"] = Observed(after);
                    row["passed"] = reasons.Count == 0;
                    row["reasons"] = JArray.FromObject(reasons);
                    report["completed"] = tests.Count;
                    report["passed"] = tests.OfType<JObject>().Count(t => (bool)t["passed"]);
                    Write(report, output);
                    UnityEngine.Debug.Log("WorldWeaver chat acceptance " + (index + 1) + "/" + prompts.Length +
                        ": " + (reasons.Count == 0 ? "PASS" : "FAIL") + " (" + watch.ElapsedMilliseconds + " ms)");
                    if (controller.Busy || EditorApplication.isCompiling)
                        throw new InvalidOperationException("The controller stayed busy or initiated compilation without script approval.");
                }
                report["pendingPlansInChat"] = new JArray(controller.Current.Messages.Where(m => m.PendingPlan != null)
                    .Select(m => new JObject { ["messageId"] = m.Id, ["plan"] = JToken.FromObject(m.PendingPlan) }));
                report["draftPreservedInHistory"] = capturedPending.Count > 0 &&
                    new ChatHistoryStore(history).Load().Where(s => s.Id == sessionId)
                        .Any(s => capturedPending.Properties().All(p => s.Messages.Any(m => m.Id == p.Name &&
                            m.PendingPlan != null && JToken.DeepEquals(p.Value, JToken.FromObject(m.PendingPlan)))));
                success = tests.Count == prompts.Length && tests.OfType<JObject>().All(t => (bool)t["passed"]) &&
                    (bool)report["draftPreservedInHistory"];
                string fixture = Environment.GetEnvironmentVariable("WW_CHAT_SAVE_SCENE");
                if (!string.IsNullOrWhiteSpace(fixture))
                {
                    if (!fixture.StartsWith("Assets/WorldWeaverGenerated/", StringComparison.Ordinal) ||
                        !fixture.EndsWith(".unity", StringComparison.Ordinal) || fixture.Contains("..") ||
                        File.Exists(fixture)) throw new ArgumentException("Fixture path must be a new scene under Assets/WorldWeaverGenerated.");
                    Directory.CreateDirectory(Path.GetDirectoryName(fixture)); AssetDatabase.Refresh();
                    if (!EditorSceneManager.SaveScene(scene, fixture)) throw new IOException("Could not save acceptance fixture.");
                    report["fixtureSavedByRunner"] = fixture;
                    report["fixtureSaveIsModelAction"] = false;
                }
            }
            catch (Exception error) { report["fatalError"] = Describe(error); UnityEngine.Debug.LogError("Chat acceptance: " + Describe(error)); }
            finally
            {
                controller?.Dispose();
                Application.runInBackground = previousRunInBackground;
                report["finishedUtc"] = DateTime.UtcNow.ToString("o");
                report["completed"] = tests.Count;
                report["passed"] = tests.OfType<JObject>().Count(t => (bool?)t["passed"] == true);
                report["failed"] = tests.OfType<JObject>().Count(t => (bool?)t["passed"] != true);
                report["success"] = success;
                try { if (output != null) Write(report, output); }
                catch (Exception error) { success = false; UnityEngine.Debug.LogError("Could not write acceptance report: " + Describe(error)); }
                running = false;
                UnityEngine.Debug.Log("WorldWeaver chat acceptance completed: " + report["passed"] + "/" + tests.Count +
                    " passed; success=" + success + "; report=" + output);
                if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
            }
        }

        private static void Validate(int index, JObject before, JObject after, AssistantMessage answer, List<string> reasons)
        {
            var terrains = Rows(after, "terrains");
            var oldTerrains = Rows(before, "terrains");
            switch (index)
            {
                case 0:
                    Require(terrains.Length == 1, reasons, "Expected exactly one actual Terrain.");
                    if (terrains.Length == 1)
                    {
                        Require(Near((float)terrains[0]["size"][0], 160) && Near((float)terrains[0]["size"][2], 160), reasons, "Terrain is not 160 x 160 metres.");
                        Require((float)terrains[0]["heightMax"] > (float)terrains[0]["heightMin"], reasons, "The generated terrain is flat.");
                        Require((float?)terrains[0]["spec"]?["valleyDepth"] > 0, reasons, "Terrain does not retain a valley specification.");
                    }
                    Require(Count(after, "tree") == 12, reasons, "Expected 12 semantic tree roots.");
                    Require(Count(after, "house") == 3, reasons, "Expected 3 semantic house roots.");
                    Require(Count(after, "rock") == 4, reasons, "Expected 4 semantic rock roots.");
                    Require((int)after["visibleDecoration"] == 19, reasons, "Some semantic decoration roots have no rendered geometry.");
                    Require((int)after["cameraCount"] > 0 && Rows(after, "lights").Length > 0, reasons, "World lacks an actual camera or lighting.");
                    break;
                case 1:
                    Require(oldTerrains.Length == 1 && terrains.Length == 1, reasons, "Terrain update requires exactly one terrain before and after.");
                    if (oldTerrains.Length == 1 && terrains.Length == 1)
                    {
                        Require((int)oldTerrains[0]["id"] == (int)terrains[0]["id"], reasons, "Terrain was replaced or a new world was created.");
                        Require(Near((float)terrains[0]["size"][1], 24) && Near((float?)terrains[0]["spec"]?["height"] ?? -1, 24), reasons, "Actual TerrainData and stored height must both equal 24.");
                    }
                    Require(JToken.DeepEquals(before["decoration"], after["decoration"]), reasons, "Existing trees, houses or rocks were replaced, removed or duplicated.");
                    Require((int)before["worldCount"] == (int)after["worldCount"], reasons, "World count changed during height update.");
                    break;
                case 2:
                case 3:
                    var matches = Rows(after, "npcs").Where(n => Normal((string)n["name"]) == "игнат").ToArray();
                    Require(matches.Length == 1, reasons, "Expected exactly one IntelligentNPC named Игнат.");
                    if (matches.Length != 1) break;
                    var npc = matches[0];
                    Require(ContainsAny((string)npc["role"], "страж", "охран", "guard"), reasons, "Игнат's role is not a guard.");
                    Require(ContainsAny((string)npc["personality"], "осторож", "осмотр", "бдител", "cautious"), reasons, "Игнат's cautious personality was not retained.");
                    Require((bool)npc["inventoryAssigned"] && (bool)npc["entityAssigned"] && (bool)npc["routesAssigned"], reasons, "NPC lacks Inventory, WorldEntity or actual model routes.");
                    Require(Rows(npc, "knowledge").Any(d => (string)d["Scope"] == (string)npc["characterId"] && GateFact((string)d["Text"])), reasons, "NPC has no correctly scoped fact about the northern gates opening at six in the morning.");
                    if (index == 3)
                    {
                        var oldNpc = Rows(before, "npcs").FirstOrDefault(n => Normal((string)n["name"]) == "игнат");
                        Require(oldNpc != null && (int)oldNpc["id"] == (int)npc["id"], reasons, "The existing guard was replaced instead of updated.");
                        Require(ContainsAny((string)npc["goals"], "патрул", "patrol"), reasons, "NPC goal does not express patrol.");
                        if (oldNpc != null) Require(JToken.DeepEquals(oldNpc["knowledge"], npc["knowledge"]), reasons, "Changing the goal altered existing knowledge.");
                    }
                    Require(JToken.DeepEquals(before["decoration"], after["decoration"]), reasons, "NPC authoring changed existing decoration.");
                    break;
                case 4:
                    var oldIds = new HashSet<int>(Rows(before, "lights").Select(l => (int)l["id"]));
                    var added = Rows(after, "lights").Where(l => !oldIds.Contains((int)l["id"])).ToArray();
                    Require(added.Length == 3, reasons, "Expected exactly three new Light components.");
                    foreach (var light in added)
                    {
                        Require((string)light["type"] == LightType.Point.ToString(), reasons, "A new light is not a Point light.");
                        var c = (JArray)light["color"];
                        Color.RGBToHSV(new Color((float)c[0], (float)c[1], (float)c[2]), out float hue, out float saturation, out float value);
                        Require(hue >= .025f && hue <= .18f && saturation >= .5f && value > .05f, reasons, "A new light is not orange.");
                        Require((float)light["intensity"] > 0 && (float)light["range"] > 0 && (bool)light["enabled"], reasons, "A new Point light cannot illuminate the scene.");
                        var placement=light["terrainPlacement"] as JObject;
                        Require(placement!=null, reasons, "A new Point light is outside the actual terrain footprint.");
                        if(placement!=null)
                        {
                            Require((float)placement["surfaceClearance"]>=3f-.001f, reasons, "A new Point light is buried or lacks3m clearance above terrain/generated water.");
                            Require((bool)placement["rangeReachesGround"], reasons, "A new Point light's range does not reach the actual ground below it.");
                        }
                    }
                    Require(added.Select(l => l["position"].ToString(Formatting.None)).Distinct().Count() == 3, reasons, "The three new lights are not at different positions.");
                    Require(Rows(before, "lights").All(l => Rows(after, "lights").Any(a => JToken.DeepEquals(l, a))), reasons, "Existing lighting was modified or deleted.");
                    Require(JToken.DeepEquals(before["terrains"], after["terrains"]) && JToken.DeepEquals(before["npcs"], after["npcs"]) &&
                        JToken.DeepEquals(before["decoration"], after["decoration"]), reasons, "Creating lights changed existing terrain, NPC or decoration.");
                    break;
                case 5:
                    var draft = answer?.PendingPlan?.actions.FirstOrDefault(a => a.tool == "draft_script");
                    Require(draft != null && !string.IsNullOrWhiteSpace((string)draft.args["code"]), reasons, "The C# request did not produce a nonempty review-only draft_script plan.");
                    Require(answer == null || answer.Steps.All(s => s.Tool != "draft_script" && s.Tool != "attach_script"), reasons, "A code tool was executed without review.");
                    Require((string)before["fingerprint"] == (string)after["fingerprint"], reasons, "Preparing a review-only draft changed the scene.");
                    Require(JToken.DeepEquals(before["scripts"], after["scripts"]), reasons, "Preparing the draft created or changed a .cs file under Assets.");
                    break;
                case 6:
                    Require((string)before["fingerprint"] == (string)after["fingerprint"], reasons, "A read-only explanation changed actual scene state.");
                    Require(JToken.DeepEquals(before["scripts"], after["scripts"]), reasons, "A read-only explanation changed source assets.");
                    bool namedModel = answer != null && (ModelKey.Contains("qwen2.5") ? ContainsAny(answer.Text, "qwen2.5", "qwen 2.5", "квен2.5", "квен 2.5") : ModelKey.Contains("qwen3.5") ? ContainsAny(answer.Text, "qwen3.5", "qwen 3.5", "квен3.5", "квен 3.5") : ModelKey.StartsWith("pollinations:", StringComparison.Ordinal) ? ContainsAny(answer.Text, "openai-fast") || ContainsAny(answer.Text, "gpt") && ContainsAny(answer.Text, "oss") : ContainsAny(answer.Text, ModelKey));
                    Require(namedModel, reasons, "The explanation did not identify the selected model: " + ModelKey);
                    if (terrains.Length > 0) Require(answer != null && ContainsAny(answer.Text, "долин", "рельеф", "terrain", "лес"), reasons, "The explanation did not describe the actual terrain.");
                    if (Rows(after, "npcs").Any(n => (string)n["name"] == "Игнат")) Require(answer != null && ContainsAny(answer.Text, "игнат", "страж", "npc"), reasons, "The explanation did not describe the actual guard.");
                    break;
            }
        }

        private static JObject Capture(Scene scene)
        {
            var objects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(o => o.scene == scene).OrderBy(o => o.GetInstanceID()).ToArray();
            var owned = objects.Select(o => o.GetComponent<GeneratedSceneObject>()).Where(o => o).ToArray();
            var terrainRows = new JArray();
            var actualTerrains=objects.Select(o=>o.GetComponent<Terrain>()).Where(t=>t&&t.enabled&&t.gameObject.activeInHierarchy&&t.terrainData).ToArray();
            foreach (var terrain in objects.Select(o => o.GetComponent<Terrain>()).Where(t => t))
            {
                var data = terrain.terrainData;
                var marker = terrain.GetComponent<GeneratedTerrain>();
                float[,] heights = data ? data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution) : new float[0, 0];
                var values = heights.Cast<float>().ToArray();
                var bytes = new byte[values.Length * sizeof(float)];
                Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
                terrainRows.Add(new JObject { ["id"] = terrain.gameObject.GetInstanceID(), ["size"] = data ? Vec(data.size) : null,
                    ["spec"] = marker?.Specification == null ? null : JObject.FromObject(marker.Specification),
                    ["heightHash"] = Digest(bytes), ["heightMin"] = values.Length == 0 ? 0 : values.Min(),
                    ["heightMax"] = values.Length == 0 ? 0 : values.Max() });
            }
            var npcs = new JArray(objects.Select(o => o.GetComponent<IntelligentNPC>()).Where(n => n).Select(n => new JObject {
                ["id"] = n.gameObject.GetInstanceID(), ["characterId"] = n.CharacterId, ["name"] = n.NpcName,
                ["role"] = n.Role, ["personality"] = n.Personality, ["goals"] = n.Goals,
                ["inventoryAssigned"] = (bool)n.GetComponent<Inventory>(), ["entityAssigned"] = (bool)n.GetComponent<WorldEntity>(),
                ["routesAssigned"] = n.Models && n.Models.Dialogue && n.Models.Planning,
                ["knowledge"] = n.Knowledge ? JArray.FromObject(n.Knowledge.Documents) : new JArray()
            }));
            var lights = new JArray(objects.Select(o => o.GetComponent<Light>()).Where(l => l).Select(l => new JObject {
                ["id"] = l.gameObject.GetInstanceID(), ["type"] = l.type.ToString(), ["color"] = new JArray(l.color.r, l.color.g, l.color.b),
                ["intensity"] = l.intensity, ["range"] = l.range, ["enabled"] = l.enabled, ["position"] = Vec(l.transform.position),
                ["terrainPlacement"] = LightTerrainSnapshot(l,actualTerrains)
            }));
            var objectRows = new JArray(objects.Select(o => new JObject {
                ["id"] = o.GetInstanceID(), ["name"] = o.name, ["active"] = o.activeSelf,
                ["parent"] = o.transform.parent ? o.transform.parent.gameObject.GetInstanceID() : 0,
                ["components"] = new JArray(o.GetComponents<Component>().Select(c => c ? new JObject {
                    ["type"] = c.GetType().FullName, ["data"] = JToken.Parse(EditorJsonUtility.ToJson(c))
                } : new JObject { ["type"] = "Missing component" }))
            }));
            var materials = new JArray(objects.SelectMany(o => o.GetComponents<Renderer>()).SelectMany(r => r.sharedMaterials)
                .Where(m => m).Distinct().OrderBy(m => m.GetInstanceID()).Select(m => new JObject {
                    ["id"] = m.GetInstanceID(), ["data"] = JToken.Parse(EditorJsonUtility.ToJson(m))
                }));
            var decoration = new JObject();
            foreach (string kind in new[] { "tree", "house", "rock" })
                decoration[kind] = new JArray(owned.Where(o => o.Kind == kind).Select(o => o.gameObject.GetInstanceID()).OrderBy(id => id));
            var dataRows = new JObject { ["objects"] = objectRows, ["materials"] = materials, ["terrains"] = terrainRows,
                ["npcs"] = npcs, ["lights"] = lights, ["decoration"] = decoration,
                ["worldCount"] = owned.Count(o => o.Kind == "world"),
                ["visibleDecoration"] = owned.Count(o => (o.Kind == "tree" || o.Kind == "house" || o.Kind == "rock") && o.GetComponentsInChildren<Renderer>(true).Length > 0),
                ["cameraCount"] = objects.Count(o => o.GetComponent<Camera>()) };
            dataRows["fingerprint"] = Digest(Encoding.UTF8.GetBytes(dataRows.ToString(Formatting.None)));
            dataRows["scripts"] = Scripts();
            dataRows["context"] = SceneToolExecutor.Context();
            return dataRows;
        }

        private static JObject LightTerrainSnapshot(Light light,IEnumerable<Terrain> terrains)
        {
            if(light.type!=LightType.Point&&light.type!=LightType.Spot)return null;
            var point=light.transform.position;JObject result=null;float highestSurface=float.NegativeInfinity;
            // Independent acceptance readback: sample real TerrainData, not the executor's placement helper.
            foreach(var terrain in terrains)
            {
                var origin=terrain.transform.position;var data=terrain.terrainData;
                if(point.x<origin.x||point.x>origin.x+data.size.x||point.z<origin.z||point.z>origin.z+data.size.z)continue;
                float x=(point.x-origin.x)/data.size.x,z=(point.z-origin.z)/data.size.z;
                float ground=origin.y+data.GetInterpolatedHeight(x,z);
                var spec=terrain.GetComponent<GeneratedTerrain>()?.Specification;
                float? water=spec==null?(float?)null:origin.y+spec.waterLevel*spec.height;
                float surface=Mathf.Max(ground,water??ground);
                if(result!=null&&surface<=highestSurface)continue;
                highestSurface=surface;
                result=new JObject{["terrainId"]=terrain.gameObject.GetInstanceID(),["insideBounds"]=true,
                    ["groundY"]=ground,["waterY"]=water,["surfaceY"]=surface,["groundClearance"]=point.y-ground,
                    ["surfaceClearance"]=point.y-surface,["minimumPlacementY"]=surface+3f,
                    ["rangeReachesGround"]=light.range>Mathf.Abs(point.y-ground)+.001f};
            }
            return result;
        }

        private static JObject Scripts()
        {
            var scripts = new JObject();
            foreach (string path in Directory.EnumerateFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                scripts["Assets/" + path.Substring(Application.dataPath.Length + 1).Replace('\\', '/')] = Digest(File.ReadAllBytes(path));
            return scripts;
        }
        private static JObject Observed(JObject scene) => new JObject {
            ["gameObjects"] = ((JArray)scene["objects"]).Count, ["terrains"] = Rows(scene, "terrains").Length,
            ["trees"] = Count(scene, "tree"), ["houses"] = Count(scene, "house"), ["rocks"] = Count(scene, "rock"),
            ["npcs"] = Rows(scene, "npcs").Length, ["pointLights"] = Rows(scene, "lights").Count(l => (string)l["type"] == "Point"),
            ["sceneFingerprint"] = scene["fingerprint"]
        };
        private static JObject[] Rows(JObject value, string name) => (value[name] as JArray)?.OfType<JObject>().ToArray() ?? Array.Empty<JObject>();
        private static int Count(JObject value, string kind) => ((JArray)value["decoration"][kind]).Count;
        private static JArray Vec(Vector3 value) => new JArray(value.x, value.y, value.z);
        private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < .01f;
        private static void Require(bool condition, List<string> reasons, string reason) { if (!condition) reasons.Add(reason); }
        private static string Normal(string text) => (text ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');
        private static bool ContainsAny(string text, params string[] parts) => parts.Any(p => Normal(text).Contains(p));
        private static bool GateFact(string text) => ContainsAny(text, "северн", "north") && ContainsAny(text, "ворот", "gate") &&
            ContainsAny(text, "открыв", "открыт", "open") && ContainsAny(text, "шест", "6", "six") && ContainsAny(text, "утр", "morning", "am");
        private static bool RawPlan(string text)
        {
            try { var value = JObject.Parse((text ?? "").Trim()); return value["actions"] is JArray && value["reply"] != null; }
            catch (JsonException) { return false; }
        }
        private static string Digest(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string Describe(Exception error) => ChatHistoryStore.Redact(error.GetType().Name + ": " + error.Message);
        private static string OutputPath()
        {
            string explicitPath = Environment.GetEnvironmentVariable("WW_CHAT_OUTPUT");
            if (!string.IsNullOrWhiteSpace(explicitPath)) return Path.GetFullPath(explicitPath);
            var project = Directory.GetParent(Application.dataPath);
            for (var directory = project; directory != null; directory = directory.Parent)
                if (Directory.Exists(Path.Combine(directory.FullName, "Packages", "com.worldweaver.toolkit")))
                    return Path.Combine(directory.FullName, "results", "chat-agent-acceptance.json");
            return Path.Combine(project.FullName, "results", "chat-agent-acceptance.json");
        }
        private static void Write(JObject report, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, report.ToString(Formatting.Indented), new UTF8Encoding(false));
        }
    }
}
