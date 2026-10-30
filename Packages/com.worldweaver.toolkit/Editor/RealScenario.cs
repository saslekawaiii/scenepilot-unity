using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WorldWeaver.Editor
{
    public static class RealScenario
    {
        [MenuItem("Tools/WorldWeaver/Run Real Local Scenario")]
        public static void Local() { Begin("local"); }
        [MenuItem("Tools/WorldWeaver/Run Real Free Cloud Scenario")]
        public static void Cloud() { Begin("cloud"); }
        private static void Begin(string provider)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            if (File.Exists("Assets/WorldWeaverDemo/WorldWeaverDemo.unity")) UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/WorldWeaverDemo/WorldWeaverDemo.unity"); else DemoBuilder.Create();
            SessionState.SetString("WorldWeaverScenario", provider);
            EditorApplication.playModeStateChanged -= OnPlay; EditorApplication.playModeStateChanged += OnPlay;
            EditorApplication.isPlaying = true;
        }
        [InitializeOnLoadMethod] private static void Initialize() { EditorApplication.playModeStateChanged -= OnPlay; EditorApplication.playModeStateChanged += OnPlay; }
        private static async void OnPlay(PlayModeStateChange state)
        {
            string provider = SessionState.GetString("WorldWeaverScenario", "");
            if (state != PlayModeStateChange.EnteredPlayMode || string.IsNullOrEmpty(provider)) return;
            SessionState.EraseString("WorldWeaverScenario");
            string root = Directory.GetParent(Application.dataPath).Parent.FullName;
            var report = new JObject { ["provider"] = provider, ["timestamp"] = DateTime.UtcNow.ToString("O"), ["unity_version"] = Application.unityVersion };
            try
            {
                var npc = GameObject.Find("Merchant").GetComponent<IntelligentNPC>();
                Application.runInBackground = true;
                var demo = GameObject.Find("Dialogue").GetComponent<DemoDialogue>(); npc.Models = provider == "cloud" ? demo.CloudRoutes : demo.LocalRoutes;
                var player = GameObject.Find("Player").GetComponent<Inventory>();
                await Task.Delay(1000);
                var start = npc.transform.position;
                report["inventory_before"] = npc.GetComponent<Inventory>().Count("Crystal");
                var turns = new JArray(); report["dialogue_turns"] = turns;
                foreach (var proposal in new[] {
                    "Мне нужен один Crystal для больного брата. Я верну долг после урожая. Если решишь помочь, передай один кристалл игроку Player сейчас.",
                    "Понимаю твои сомнения. Согласен на долг в семь серебряных монет за один Crystal. Обязуюсь вернуть до второго вечернего колокола. Дай только один, чтобы не рисковать всем запасом.",
                    "Договорились: один Crystal за семь серебряных монет в долг до второго вечернего колокола. Принимаю твои условия. Можешь передать один Crystal игроку Player и записать долг?" })
                {
                    var trace = await npc.TalkAsync(proposal); turns.Add(JToken.FromObject(trace)); report["dialogue"] = JToken.FromObject(trace);
                    if (player.Count("Crystal") > 0) break;
                }
                report["inventory_after"] = npc.GetComponent<Inventory>().Count("Crystal"); report["player_crystals"] = player.Count("Crystal");
                var plan = await npc.PlanAsync(); report["planning"] = JToken.FromObject(plan);
                await Task.Delay(3000); report["distance_moved"] = Vector3.Distance(start, npc.transform.position);
                report["success"] = player.Count("Crystal") == 1 && Vector3.Distance(start, npc.transform.position) > .3f;
            }
            catch (Exception error) { report["error"] = error is ProviderException ? error.Message : error.GetType().Name + ": " + error.Message; report["success"] = false; }
            Directory.CreateDirectory(Path.Combine(root, "results")); File.WriteAllText(Path.Combine(root, "results/gui-scenario-" + provider + ".json"), report.ToString(Formatting.Indented));
            UnityEngine.Debug.Log("WorldWeaver real scenario completed: " + report["success"]);
        }
    }
}
