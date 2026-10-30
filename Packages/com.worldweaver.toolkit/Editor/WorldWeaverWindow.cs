using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WorldWeaver.Editor
{
    public sealed class WorldWeaverWindow : EditorWindow
    {
        private static readonly string[] tabs = { "WORLD", "NPC", "KNOWLEDGE", "MODELS", "DEBUG" };
        private int tab, traceIndex;
        private string prompt = "Создать горную долину с озером в центре, горами по краям и пологой южной частью.";
        private string specJson = JsonConvert.SerializeObject(new TerrainSpec(), Formatting.Indented);
        private string status = "Создайте demo или назначьте профили моделей.";
        private Vector2 scroll;
        private OrchestratorProfile routes;
        private IntelligentNPC npc;
        private UnityEditor.Editor inspector;
        private UnityEngine.Object inspected;
        private CancellationTokenSource operation;
        private bool busy, showSpec = true;

        [MenuItem("Tools/Intelligent World Toolkit")]
        [MenuItem("Tools/WorldWeaver/Open Toolkit")]
        public static void ShowWindow() { GetWindow<WorldWeaverWindow>("WorldWeaver").minSize = new Vector2(490, 580); }
        // Menu alternatives make IMGUI views reachable through native keyboard menus.
        [MenuItem("Tools/WorldWeaver/Views/World")] private static void WorldView() => OpenView(0);
        [MenuItem("Tools/WorldWeaver/Views/Character")] private static void CharacterView() => OpenView(1);
        [MenuItem("Tools/WorldWeaver/Views/Knowledge")] private static void KnowledgeView() => OpenView(2);
        [MenuItem("Tools/WorldWeaver/Views/Models")] private static void ModelsView() => OpenView(3);
        [MenuItem("Tools/WorldWeaver/Views/Debug")] private static void TraceView() => OpenView(4);
        private static void OpenView(int index)
        {
            ShowWindow(); var window = GetWindow<WorldWeaverWindow>();
            var selected = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<IntelligentNPC>() : null;
            if (!selected && GameObject.Find("Merchant")) selected = GameObject.Find("Merchant").GetComponent<IntelligentNPC>();
            if (selected) { window.npc = selected; Selection.activeGameObject = selected.gameObject; }
            window.SelectTab(index);
        }
        [MenuItem("Tools/WorldWeaver/Frame Demo Terrain")]
        private static void FrameTerrain()
        {
            var terrain = UnityEngine.Object.FindFirstObjectByType<GeneratedTerrain>();
            if (!terrain) return; Selection.activeGameObject = terrain.gameObject;
            var view = SceneView.lastActiveSceneView;
            if (view) { view.FrameSelected(); view.Focus(); }
        }
        private void OnEnable()
        {
            EditorApplication.update += Repaint;
            var paths = AssetDatabase.FindAssets("LocalRoutes t:OrchestratorProfile");
            if (paths.Length > 0) routes = AssetDatabase.LoadAssetAtPath<OrchestratorProfile>(AssetDatabase.GUIDToAssetPath(paths[0]));
        }
        private void OnDisable() { EditorApplication.update -= Repaint; operation?.Cancel(); if (inspector) DestroyImmediate(inspector); }
        private void OnSelectionChange()
        { if (Selection.activeGameObject) { var selected = Selection.activeGameObject.GetComponent<IntelligentNPC>(); if (selected) npc = selected; } Repaint(); }
        public void SelectTab(int index) { tab = Mathf.Clamp(index, 0, 4); Repaint(); }
        private void OnGUI()
        {
            GUILayout.Space(12);
            GUILayout.Label("WORLDWEAVER", new GUIStyle(EditorStyles.boldLabel) { fontSize = 23 });
            GUILayout.Label("Intelligent World Toolkit   /   Terrain, knowledge and NPC decisions", EditorStyles.miniLabel);
            GUILayout.Space(10); tab = GUILayout.Toolbar(tab, tabs, GUILayout.Height(30)); GUILayout.Space(12);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            switch (tab) { case 0: World(); break; case 1: Npc(); break; case 2: Knowledge(); break; case 3: Models(); break; default: DebugView(); break; }
            EditorGUILayout.EndScrollView();
            GUILayout.Space(10); EditorGUILayout.HelpBox(status, MessageType.None);
            if (busy && GUILayout.Button("Cancel current request")) operation?.Cancel();
            GUILayout.Space(8);
        }
        private void World()
        {
            GUILayout.Label("Describe a world", EditorStyles.boldLabel);
            prompt = EditorGUILayout.TextArea(prompt, GUILayout.MinHeight(82));
            routes = (OrchestratorProfile)EditorGUILayout.ObjectField("Model routes", routes, typeof(OrchestratorProfile), false);
            using (new EditorGUI.DisabledScope(busy || !routes))
                if (GUILayout.Button("Interpret description with LLM", GUILayout.Height(32))) Run(Interpret);
            GUILayout.Space(12); showSpec = EditorGUILayout.Foldout(showSpec, "TerrainSpec · editable JSON", true);
            if (showSpec) specJson = EditorGUILayout.TextArea(specJson, GUILayout.MinHeight(275));
            using (new EditorGUI.DisabledScope(busy))
            {
                if (GUILayout.Button("Generate new Terrain", GUILayout.Height(36))) Run(() => { var spec = TerrainSpec.Parse(specJson); TerrainAuthoring.Create(spec); status = "Terrain created. Seed " + spec.seed + ". Undo is available."; return Task.CompletedTask; });
                var owned = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<GeneratedTerrain>() : null;
                using (new EditorGUI.DisabledScope(!owned))
                    if (GUILayout.Button("Regenerate selected WorldWeaver Terrain")) Run(() => { TerrainAuthoring.Regenerate(owned, TerrainSpec.Parse(specJson)); status = "Owned terrain regenerated."; return Task.CompletedTask; });
            }
            EditorGUILayout.HelpBox("LLM returns parameters. C# generates each height sample from the seed. Existing terrain is preserved.", MessageType.Info);
            if (GUILayout.Button("Create complete demo in a new scene")) DemoBuilder.Create();
        }
        private async Task Interpret()
        {
            var request = new CompletionRequest { Schema = TerrainSpec.Schema() };
            request.Messages.Add(new ChatMessage("system", "Interpret terrain descriptions as JSON TerrainSpec. All fields required. width/length 32..2048, height 4..512, resolution one of 33,65,129,257,513. scale .1..32, octaves 1..8, persistence .1..9, lacunarity 1..4, mountainStrength 0..1, valleyDepth 0..0.8, waterLevel 0..0.8, southSlope 0..1, smoothingPasses 0..8. Defaults: " + JsonConvert.SerializeObject(new TerrainSpec()) + ". Do not create a heightmap."));
            request.Messages.Add(new ChatMessage("user", prompt));
            using (var router = new ModelRouter(routes))
            {
                var result = await router.CompleteAsync(ModelTask.Terrain, request, operation.Token);
                var spec = TerrainSpec.Parse(result.Text); specJson = JsonConvert.SerializeObject(spec, Formatting.Indented);
                TraceLog.Add(new PipelineTrace { Character = "Terrain", Input = prompt, Route = "Terrain", Raw = result.Text, Completion = result, TotalMs = result.ElapsedMs, Speech = "TerrainSpec validated" });
                status = "TerrainSpec validated · " + result.Model + " · " + result.ElapsedMs + " ms";
            }
        }
        private void Npc()
        {
            GUILayout.Label("Character configuration", EditorStyles.boldLabel);
            npc = (IntelligentNPC)EditorGUILayout.ObjectField("NPC", npc, typeof(IntelligentNPC), true);
            if (!npc && Selection.activeGameObject && GUILayout.Button("Add IntelligentNPC to selected object")) npc = Undo.AddComponent<IntelligentNPC>(Selection.activeGameObject);
            if (!npc) { EditorGUILayout.HelpBox("Select a GameObject or create the demo scene.", MessageType.Info); return; }
            Inspect(npc);
            if (Application.isPlaying)
            {
                GUILayout.Label(npc.LastSpeech ?? "", EditorStyles.wordWrappedLabel);
                using (new EditorGUI.DisabledScope(busy || npc.Busy)) if (GUILayout.Button("Request autonomous decision")) Run(async () => { var t = await npc.PlanAsync(operation.Token); status = t.Validation.Reason; });
            }
        }
        private void Knowledge()
        {
            GUILayout.Label("Scoped knowledge and memory", EditorStyles.boldLabel);
            npc = (IntelligentNPC)EditorGUILayout.ObjectField("NPC", npc, typeof(IntelligentNPC), true);
            if (!npc) { EditorGUILayout.HelpBox("Assign an NPC to view its knowledge.", MessageType.Info); return; }
            var next = (KnowledgeBase)EditorGUILayout.ObjectField("Knowledge base", npc.Knowledge, typeof(KnowledgeBase), false);
            if (next != npc.Knowledge) { Undo.RecordObject(npc, "Assign knowledge"); npc.Knowledge = next; EditorUtility.SetDirty(npc); }
            if (!npc.Knowledge && GUILayout.Button("Create knowledge asset"))
            {
                string path = EditorUtility.SaveFilePanelInProject("Knowledge base", "Knowledge", "asset", "Save knowledge");
                if (!string.IsNullOrEmpty(path)) { npc.Knowledge = CreateInstance<KnowledgeBase>(); AssetDatabase.CreateAsset(npc.Knowledge, path); EditorUtility.SetDirty(npc); }
            }
            if (!npc.Knowledge) return;
            if (GUILayout.Button("Import TXT, Markdown or JSON")) Import();
            using (new EditorGUI.DisabledScope(busy || npc.Busy))
            {
                if (GUILayout.Button("Reindex with embedding model")) Run(async () => { await npc.ReindexAsync(operation.Token); status = "Knowledge reindexed for " + npc.CharacterId; });
                if (GUILayout.Button("Clear runtime memory and index")) { npc.ClearMemory(); status = "Runtime memory cleared. Authored knowledge is preserved."; }
            }
            GUILayout.Label("Current scope: " + npc.CharacterId, EditorStyles.boldLabel);
            foreach (var doc in npc.Knowledge.Documents)
            {
                GUILayout.Space(6); GUILayout.Label(doc.Title + "  [" + doc.Scope + "]", EditorStyles.boldLabel);
                GUILayout.Label(doc.Text ?? "", EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.HelpBox("Static knowledge, episodic events and conversations use separate retrieval channels. Other characters' knowledge is excluded before similarity scoring.", MessageType.Info);
        }
        private void Import()
        {
            string path = EditorUtility.OpenFilePanelWithFilters("Import knowledge", "", new[] { "Text", "txt,md,json" }); if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (new FileInfo(path).Length > 1024 * 1024) throw new ArgumentException("File exceeds 1 MiB.");
                var text = File.ReadAllText(path); Undo.RecordObject(npc.Knowledge, "Import knowledge");
                var docs = Path.GetExtension(path).ToLowerInvariant() == ".json" ? JArray.Parse(text).ToObject<System.Collections.Generic.List<KnowledgeDocument>>()
                    : new System.Collections.Generic.List<KnowledgeDocument> { new KnowledgeDocument { Id = TextChunker.Digest(text).Substring(0, 16), Title = Path.GetFileName(path), Text = text } };
                foreach (var doc in docs) { doc.Scope = npc.CharacterId; doc.Kind = MemoryKind.StaticKnowledge; npc.Knowledge.Documents.RemoveAll(d => d.Id == doc.Id && d.Scope == doc.Scope); npc.Knowledge.Documents.Add(doc); }
                EditorUtility.SetDirty(npc.Knowledge); AssetDatabase.SaveAssets(); status = "Imported " + docs.Count + " documents.";
            }
            catch (Exception error) { status = error.GetType().Name + ": check the knowledge file format."; }
        }
        private void Models()
        {
            GUILayout.Label("Task routing", EditorStyles.boldLabel);
            routes = (OrchestratorProfile)EditorGUILayout.ObjectField("Orchestrator", routes, typeof(OrchestratorProfile), false);
            if (!routes) return; Inspect(routes);
            foreach (var task in new[] { ModelTask.Terrain, ModelTask.Dialogue, ModelTask.Planning })
            {
                ModelProfile profile;
                try { profile = routes.Resolve(task); }
                catch (InvalidOperationException) { EditorGUILayout.HelpBox(task + ": assign a Model Profile to this route.", MessageType.Warning); continue; }
                GUILayout.Space(8); GUILayout.Label(task + " / " + profile.Model, EditorStyles.boldLabel);
                GUILayout.Label(profile.Provider + " · " + profile.Endpoint, EditorStyles.wordWrappedMiniLabel);
                if (!string.IsNullOrEmpty(profile.ApiKeyEnvironmentVariable)) GUILayout.Label(profile.ApiKeyEnvironmentVariable + ": " + (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(profile.ApiKeyEnvironmentVariable)) ? "missing" : "available"));
                using (new EditorGUI.DisabledScope(busy)) if (GUILayout.Button("Health check " + task)) Run(async () => { using (var provider = profile.CreateProvider()) status = await provider.HealthAsync(operation.Token) ? "Model is available." : "Endpoint reachable, model not listed."; });
            }
            EditorGUILayout.HelpBox("Profiles contain connection settings and environment variable names. Restart Unity from the provided launcher to inherit API credentials.", MessageType.Info);
        }
        private void DebugView()
        {
            GUILayout.Label("Pipeline inspection", EditorStyles.boldLabel);
            var entries = TraceLog.Entries;
            if (entries.Count == 0) { EditorGUILayout.HelpBox("Run a real dialogue or interpret a terrain description to capture a trace.", MessageType.Info); return; }
            traceIndex = EditorGUILayout.Popup("Trace", Mathf.Clamp(traceIndex, 0, entries.Count - 1), entries.Select(e => e.Route + " / " + e.Character + " / " + e.TotalMs + " ms").ToArray());
            var trace = entries[traceIndex];
            Field("Input", trace.Input); Field("Route", trace.Route + " / " + trace.Completion?.Provider + " / " + trace.Completion?.Model);
            GUILayout.Label("Total " + trace.TotalMs + " ms · Retrieval " + trace.RetrievalMs + " ms · Tokens " + trace.Completion?.InputTokens + "/" + trace.Completion?.OutputTokens, EditorStyles.wordWrappedLabel);
            foreach (var chunk in trace.Retrieved) Field(chunk.Kind + " / " + chunk.Title + " / " + chunk.Score.ToString("F3"), chunk.Text);
            Field("Context", trace.Context); Field("Raw structured output", trace.Raw);
            Field("Validation", trace.Validation == null ? "—" : trace.Validation.Accepted + " / " + trace.Validation.Reason + "\n" + string.Join(", ", trace.Validation.Executed));
            Field("Error", trace.Error);
            if (GUILayout.Button("Export trace JSON"))
            {
                string path = EditorUtility.SaveFilePanel("Trace", "", "worldweaver-trace.json", "json");
                if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, JsonConvert.SerializeObject(trace, Formatting.Indented));
            }
        }
        private static void Field(string name, string value) { GUILayout.Space(6); GUILayout.Label(name, EditorStyles.boldLabel); EditorGUILayout.SelectableLabel(value ?? "—", EditorStyles.textArea, GUILayout.Height(Mathf.Clamp((value ?? "").Length / 60 * 15 + 35, 35, 190))); }
        private void Inspect(UnityEngine.Object value)
        { if (inspected != value) { if (inspector) DestroyImmediate(inspector); inspector = UnityEditor.Editor.CreateEditor(value); inspected = value; } inspector.OnInspectorGUI(); }
        private async void Run(Func<Task> action)
        {
            if (busy) return; busy = true; operation = new CancellationTokenSource(); status = "Working…";
            try { await action(); }
            catch (OperationCanceledException) { status = "Cancelled."; }
            catch (Exception error) { status = error is ProviderException || error is ArgumentException ? error.Message : error.GetType().Name; }
            finally { busy = false; operation.Dispose(); operation = null; Repaint(); }
        }
    }
}
