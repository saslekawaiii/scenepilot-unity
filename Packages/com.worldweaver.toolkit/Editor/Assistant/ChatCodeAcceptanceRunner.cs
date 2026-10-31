using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>
    /// Explicitly armed, hash-reviewed compiler/attach/revert acceptance. Initialization only
    /// resumes a previously armed batch run; it never imports a draft on package installation.
    /// </summary>
    [InitializeOnLoad]
    public static class ChatCodeAcceptanceRunner
    {
        private const string SessionKey = "WorldWeaver.ChatCodeAcceptance.Armed";
        private const string ImportMarker = "// Generated with WorldWeaver. User-reviewed C# source.";
        private const double PhaseTimeoutSeconds = 180;
        private static bool stepping, launching;
        private static double nextPoll;
        private static string StatePath => Path.Combine(ProjectDirectory, "UserSettings", "WorldWeaverChatCodeAcceptance.json");
        private static string ProjectDirectory => Directory.GetParent(Application.dataPath).FullName;

        [Serializable]
        private sealed class RunState
        {
            public bool Active, ApplyRequested, Applied, RevertRequested, Failed;
            public bool ImportCompilationStarted, ImportCompilationFinished, RestoreCompilationStarted, RestoreCompilationFinished;
            public string RunId, Phase, InputPath, OutputPath, HistoryFolder, SessionId, MessageId;
            public string ReviewedHash, ClassName, AssetPath, PlanJson, ImportedHash;
            public bool SourceExisted;
            public string SourceBeforeBase64, SourceBeforeHash;
            public int PreviousCompileSteps;
            public int TestSceneHandle;
            public string TestFolder, TestScenePath, TestObjectName;
            public DateTime DeadlineUtc;
            public JObject Report = new JObject();
        }

        static ChatCodeAcceptanceRunner()
        {
            EditorApplication.update += Tick;
            CompilationPipeline.compilationStarted += CompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += AssemblyFinished;
            CompilationPipeline.compilationFinished += CompilationFinished;
            try { SessionState.SetBool(SessionKey, Load()?.Active == true); }
            catch (Exception error) { UnityEngine.Debug.LogError("Cannot recover chat-code acceptance state: " + Describe(error)); }
        }

        /// <summary>Use -executeMethod WorldWeaver.Editor.Assistant.ChatCodeAcceptanceRunner.RunCLI without -quit.</summary>
        public static async void RunCLI()
        {
            if (launching)
            { UnityEngine.Debug.LogError("A chat-code acceptance run is already armed; resume or inspect its existing report."); return; }
            launching = true;
            RunState state = null;
            var rejected = new JObject { ["startedUtc"] = DateTime.UtcNow.ToString("o"),
                ["runner"] = typeof(ChatCodeAcceptanceRunner).FullName, ["unityVersion"] = Application.unityVersion,
                ["success"] = false, ["reviewHashMatched"] = false };
            string output = null;
            try
            {
                output = OutputPath();
                rejected["outputPath"] = output;
                if (Load()?.Active == true)
                { UnityEngine.Debug.LogError("A previous run is already armed; its persisted phases will resume."); return; }
                if (!Application.isBatchMode) throw new InvalidOperationException("This runner requires a disposable batch-mode Editor.");
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || ScriptDrafts.Pending)
                    throw new InvalidOperationException("Wait for Edit Mode, asset import and existing script compilation to finish.");
                if (Path.GetFullPath("Assets").TrimEnd(Path.DirectorySeparatorChar) != Application.dataPath.TrimEnd(Path.DirectorySeparatorChar))
                    throw new InvalidOperationException("The working directory must be the current Unity project.");

                string input = Environment.GetEnvironmentVariable("WW_CHAT_INPUT");
                string reviewedHash = Environment.GetEnvironmentVariable("WW_CODE_REVIEW_SHA256");
                if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("WW_CHAT_INPUT is required.");
                if (!Regex.IsMatch((reviewedHash ?? "").Trim(), "^[a-fA-F0-9]{64}$"))
                    throw new ArgumentException("WW_CODE_REVIEW_SHA256 must be the SHA256 of the exact UTF-8 args.code string.");
                reviewedHash = reviewedHash.Trim().ToLowerInvariant();
                input = Path.GetFullPath(input);
                var acceptance = JObject.Parse(File.ReadAllText(input), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                string sessionId = (string)acceptance["sessionId"];
                string historyFolder = (string)acceptance["historyFolder"];
                if (!ChatHistoryStore.ValidId(sessionId) || string.IsNullOrWhiteSpace(historyFolder))
                    throw new ArgumentException("Input has no valid acceptance sessionId/historyFolder.");
                historyFolder = Path.GetFullPath(historyFolder);
                string allowedHistory = Path.Combine(ProjectDirectory, "UserSettings", "WorldWeaverChatAcceptance") + Path.DirectorySeparatorChar;
                if (!historyFolder.StartsWith(allowedHistory, StringComparison.Ordinal) || !Directory.Exists(historyFolder))
                    throw new ArgumentException("Only this project's isolated chat-acceptance history may be changed.");
                var session = new ChatHistoryStore(historyFolder).Load().SingleOrDefault(s => s.Id == sessionId)
                    ?? throw new ArgumentException("Acceptance chat is missing from its history folder.");
                var candidates = session.Messages.Where(m => m.PendingPlan?.actions != null && m.PendingPlan.actions.Count == 1 &&
                    m.PendingPlan.actions[0].tool == "draft_script" &&
                    m.PendingPlan.actions[0].args?["code"]?.Type == JTokenType.String &&
                    Hash(Encoding.UTF8.GetBytes((string)m.PendingPlan.actions[0].args["code"])) == reviewedHash).ToArray();
                if (candidates.Length != 1) throw new ArgumentException("Exactly one pending single-script plan must match the reviewed code hash.");
                var message = candidates[0];
                var command = message.PendingPlan.actions[0];
                var captured = acceptance["capturedReviewOnlyPlans"]?[message.Id];
                if (captured == null || !JToken.DeepEquals(captured, JToken.FromObject(message.PendingPlan)))
                    throw new ArgumentException("The pending history plan differs from the captured acceptance plan.");
                if (!string.IsNullOrEmpty(command.target)) throw new ArgumentException("A draft must not target a scene object.");
                ScriptDrafts.Validate(command);
                var previousImport = StandardScriptState();
                if (previousImport != null && (bool?)previousImport["Reverted"] != true &&
                    previousImport["Files"] is JArray previousFiles && previousFiles.Count > 0)
                    throw new InvalidOperationException("An unreverted ScriptDrafts import already owns the global backup, possibly in another chat. Preserve/revert it before acceptance.");

                string code = (string)command.args["code"];
                string asset = "Assets/WorldWeaverGenerated/Scripts/" + command.name + ".cs";
                if (File.Exists(asset) || AssetDatabase.LoadMainAssetAtPath(asset))
                    throw new InvalidOperationException("Code acceptance requires a NEW unique LLM class/file name. Existing source assets, including BOM/UTF16 files, are never imported over by this test.");
                byte[] before = null;
                string runId = Guid.NewGuid().ToString("N");
                string folder = "Assets/WorldWeaverGenerated/CodeAcceptance/" + runId;
                state = new RunState {
                    Active = true, RunId = runId, InputPath = input, OutputPath = output, HistoryFolder = historyFolder,
                    SessionId = sessionId, MessageId = message.Id, ReviewedHash = reviewedHash, ClassName = command.name,
                    AssetPath = asset, PlanJson = JsonConvert.SerializeObject(message.PendingPlan),
                    ImportedHash = Hash(Encoding.UTF8.GetBytes(ImportMarker + "\n" + code + "\n")),
                    SourceExisted = before != null, SourceBeforeBase64 = before == null ? null : Convert.ToBase64String(before),
                    SourceBeforeHash = before == null ? null : Hash(before),
                    PreviousCompileSteps = message.Steps.Count(s => s.Tool == "compile_scripts"),
                    TestFolder = folder, TestScenePath = folder + "/CodeAcceptance.unity", TestObjectName = "CodeAcceptanceCube-" + runId,
                    Report = new JObject {
                        ["schemaVersion"] = 1, ["runner"] = typeof(ChatCodeAcceptanceRunner).FullName,
                        ["startedUtc"] = DateTime.UtcNow.ToString("o"), ["unityVersion"] = Application.unityVersion,
                        ["inputPath"] = input, ["outputPath"] = output, ["sessionId"] = sessionId, ["messageId"] = message.Id,
                        ["reviewHashMatched"] = true, ["reviewedCodeSha256"] = reviewedHash, ["className"] = command.name,
                        ["assetPath"] = asset, ["sourceExistedBefore"] = before != null,
                        ["sourceBeforeSha256"] = before == null ? null : Hash(before),
                        ["testScenePath"] = folder + "/CodeAcceptance.unity", ["events"] = new JArray(),
                        ["errors"] = new JArray(), ["checks"] = new JObject(),
                        ["scope"] = "Reviewed code import/compiler feedback/attach/revert; no new LLM calls."
                    }
                };
                Phase(state, "Armed"); // Authorization and the original pending plan survive any reload.
                CreateFixture(state);
                using (var controller = new ChatController(historyFolder: state.HistoryFolder))
                {
                    controller.OpenChat(state.SessionId);
                    var current = controller.Current.Messages.Single(m => m.Id == state.MessageId);
                    if (controller.Current.Id != state.SessionId || current.PendingPlan == null ||
                        Hash(Encoding.UTF8.GetBytes((string)current.PendingPlan.actions[0].args["code"])) != state.ReviewedHash ||
                        !JToken.DeepEquals(JToken.FromObject(current.PendingPlan), JToken.Parse(state.PlanJson)))
                        throw new InvalidOperationException("The reviewed pending plan changed before import.");
                    if (File.Exists(state.AssetPath) || AssetDatabase.LoadMainAssetAtPath(state.AssetPath))
                        throw new InvalidOperationException("The new script destination appeared after arming; refusing to overwrite it.");
                    state.ApplyRequested = true;
                    Phase(state, "AwaitImportCompilation"); // Persist BEFORE ApplyPendingAsync and AssetDatabase.Refresh.
                    await controller.ApplyPendingAsync(state.MessageId);
                }
                state = Load() ?? state;
                state.Applied = OwnScriptState(state) && FileHash(state.AssetPath) == state.ImportedHash;
                Check(state, "importedFileReadback", state.Applied);
                Save(state);
                if (!state.Applied) throw new InvalidOperationException("Reviewed Apply did not produce the exact authorized imported file.");
            }
            catch (Exception error)
            {
                if (state != null && state.Active)
                {
                    try { Fail(state, Describe(error)); }
                    catch (Exception cleanupError)
                    { Error(state, "Failure recovery error: " + Describe(cleanupError)); TryFinalCleanup(state); Finish(state, false); }
                }
                else
                {
                    rejected["finishedUtc"] = DateTime.UtcNow.ToString("o"); rejected["error"] = Describe(error);
                    try { if (output != null) Write(rejected, output); }
                    catch (Exception writeError) { UnityEngine.Debug.LogError(Describe(writeError)); }
                    UnityEngine.Debug.LogError("Chat-code acceptance refused before import: " + Describe(error));
                    if (Application.isBatchMode) EditorApplication.Exit(1);
                }
            }
            finally { launching = false; }
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(SessionKey, false) || !Application.isBatchMode || launching || stepping ||
                EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .25;
            stepping = true;
            RunState state = null;
            bool identityVerified = false;
            try
            {
                state = Load();
                if (state?.Active != true) { SessionState.SetBool(SessionKey, false); return; }
                VerifyIdentity(state);
                identityVerified = true;
                if (DateTime.UtcNow > state.DeadlineUtc)
                {
                    if (state.Phase == "AwaitImportCompilation" || state.Phase == "Armed") Fail(state, "Phase deadline exceeded: " + state.Phase);
                    else
                    {
                        Error(state, "Cleanup/restore phase deadline exceeded; cleanup is not confirmed: " + state.Phase);
                        RestorePending(state); CleanupScene(state); Finish(state, false);
                    }
                    return;
                }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                switch (state.Phase)
                {
                    case "Armed": Fail(state, "Run was interrupted before the explicit Apply call; no import is resumed automatically."); break;
                    case "AwaitImportCompilation": InspectImport(state); break;
                    case "CleanupWaiting":
                        if (!ScriptDrafts.Pending && ScriptDrafts.CanRevert(state.SessionId)) BeginRevert(state);
                        else if (!ScriptDrafts.Pending) { Error(state, "The standard ScriptDrafts state cannot revert this session."); RestorePending(state); CleanupScene(state); Finish(state, false); }
                        break;
                    case "AwaitRestoreCompilation": InspectRestore(state); break;
                    default: Fail(state, "Unknown persisted phase: " + state.Phase); break;
                }
            }
            catch (Exception error)
            {
                if (state?.Active == true)
                {
                    if (!identityVerified)
                    {
                        state.Report = state.Report ?? new JObject();
                        if (!(state.Report["errors"] is JArray)) state.Report["errors"] = new JArray();
                        Error(state, "Persisted identity could not be verified; no scene, source or history cleanup was attempted: " + Describe(error));
                        state.Active = false; state.OutputPath = OutputPath();
                        state.Report["success"] = false; state.Report["finishedUtc"] = DateTime.UtcNow.ToString("o");
                        Save(state); EditorApplication.Exit(1);
                    }
                    else if (state.Failed || state.RevertRequested)
                    { Error(state, Describe(error)); TryFinalCleanup(state); Finish(state, false); }
                    else Fail(state, Describe(error));
                }
                else { UnityEngine.Debug.LogError("Chat-code acceptance state error: " + Describe(error)); EditorApplication.Exit(1); }
            }
            finally { stepping = false; }
        }

        private static void InspectImport(RunState state)
        {
            state.Applied = state.Applied || OwnScriptState(state) && FileHash(state.AssetPath) == state.ImportedHash;
            var message = Message(state);
            var feedback = message.Steps.Where(s => s.Tool == "compile_scripts").Skip(state.PreviousCompileSteps).ToArray();
            if (feedback.Length == 0 || ScriptDrafts.Pending) return;
            state.Report["compilerFeedback"] = JArray.FromObject(feedback);
            bool historySuccess = feedback.Any(s => s.Success && (s.Target ?? "").Split(',').Select(p => p.Trim()).Contains(state.AssetPath)) && feedback.All(s => s.Success);
            Check(state, "compilerHistorySuccess", historySuccess);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(state.AssetPath);
            var type = script ? script.GetClass() : null;
            bool validClass = type != null && type.Name == state.ClassName && typeof(MonoBehaviour).IsAssignableFrom(type) && !type.IsAbstract;
            state.Report["compiledType"] = type?.FullName;
            Check(state, "compilerTypeValid", validClass);
            Check(state, "importCompilationFinished", state.ImportCompilationStarted && state.ImportCompilationFinished);
            Save(state);
            if (!state.Applied || !historySuccess || !validClass || !state.ImportCompilationStarted || !state.ImportCompilationFinished || ImportErrors(state).Count != 0)
            { Fail(state, "The real compiler feedback, compiler events or MonoScript type did not confirm a successful reviewed import."); return; }
            var fixture = Fixture(state);
            if (!fixture) throw new InvalidOperationException("The owned acceptance cube was lost after reload.");
            if (SceneManager.GetActiveScene().path != state.TestScenePath)
                throw new InvalidOperationException("The disposable fixture scene is no longer the active scene.");
            var attach = ScriptDrafts.Attach(new EditorCommand { tool = "attach_script", name = state.ClassName,
                target = "id:" + fixture.GetInstanceID(), args = new JObject() });
            state.Report["attachResult"] = JToken.FromObject(attach);
            bool attached = attach.Success && fixture.GetComponent(type) != null;
            Check(state, "attachedComponentReadback", attached);
            Save(state);
            if (!attached) { Fail(state, "attach_script did not add the compiled type to the owned cube."); return; }
            BeginRevert(state);
        }

        private static void BeginRevert(RunState state)
        {
            if (!OwnScriptState(state)) throw new InvalidOperationException("Refusing to revert a different ScriptDrafts session/import.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || ScriptDrafts.Pending || !ScriptDrafts.CanRevert(state.SessionId))
            { Phase(state, "CleanupWaiting"); return; }
            DestroyFixture(state); // Remove the component's owned object BEFORE removing/restoring its source.
            state.RevertRequested = true;
            Phase(state, "AwaitRestoreCompilation");
            ScriptDrafts.Revert(state.SessionId);
            var standard = StandardScriptState();
            bool invoked = standard != null && (string)standard["SessionId"] == state.SessionId &&
                (string)standard["MessageId"] == state.MessageId && (bool?)standard["Reverted"] == true;
            Check(state, "standardRevertConfirmed", invoked);
            if (!invoked) throw new InvalidOperationException("ScriptDrafts.Revert did not confirm restoration for the reviewed session.");
            Save(state);
            // Explicitly request the second real compilation even when the restored file is byte-identical.
            CompilationPipeline.RequestScriptCompilation();
        }

        private static void InspectRestore(RunState state)
        {
            if (!state.RestoreCompilationStarted || !state.RestoreCompilationFinished) return;
            bool sourceRestored = SourceRestored(state);
            bool compilerClean = RestoreErrors(state).Count == 0;
            Check(state, "sourceRestored", sourceRestored);
            Check(state, "restoreCompilationFinished", true);
            Check(state, "restoreCompilerErrorsAbsent", compilerClean);
            Check(state, "ownedFixtureDestroyed", !Fixture(state));
            if (!sourceRestored) Error(state, "The owned source asset was not removed/restored to its exact pre-run bytes.");
            if (!compilerClean) Error(state, "The real restoration compilation reported errors.");
            if (state.Failed) RestorePending(state);
            CleanupScene(state);
            var checks = (JObject)state.Report["checks"];
            bool passed = !state.Failed && new[] { "importedFileReadback", "compilerHistorySuccess", "compilerTypeValid",
                "importCompilationFinished", "attachedComponentReadback", "standardRevertConfirmed", "sourceRestored",
                "restoreCompilationFinished", "restoreCompilerErrorsAbsent", "ownedFixtureDestroyed", "testSceneCleaned" }
                .All(key => (bool?)checks[key] == true);
            Finish(state, passed);
        }

        private static void Fail(RunState state, string reason)
        {
            Error(state, reason);
            state.Applied = state.Applied || OwnScriptState(state) && FileHash(state.AssetPath) == state.ImportedHash;
            Save(state);
            if (state.Applied && !state.RevertRequested)
            {
                if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && !ScriptDrafts.Pending && ScriptDrafts.CanRevert(state.SessionId)) BeginRevert(state);
                else Phase(state, "CleanupWaiting");
            }
            else if (state.RevertRequested) { RestorePending(state); Save(state); }
            else { RestorePending(state); CleanupScene(state); Finish(state, false); }
        }

        private static void RestorePending(RunState state)
        {
            if (!state.ApplyRequested) return;
            try
            {
                var store = new ChatHistoryStore(state.HistoryFolder);
                var session = store.Load().Single(s => s.Id == state.SessionId);
                var message = session.Messages.Single(m => m.Id == state.MessageId);
                var original = JsonConvert.DeserializeObject<EditorPlan>(state.PlanJson);
                if (Hash(Encoding.UTF8.GetBytes((string)original.actions.Single().args["code"])) != state.ReviewedHash)
                    throw new InvalidOperationException("Persisted plan no longer matches the original reviewed code hash.");
                if (message.PendingPlan != null && !JToken.DeepEquals(JToken.FromObject(message.PendingPlan), JToken.FromObject(original)))
                    throw new InvalidOperationException("A changed pending plan will not be overwritten during recovery.");
                message.PendingPlan = original; message.IsError = true; message.Status = "Acceptance failed; reviewed draft restored for inspection";
                store.Save(session);
                Check(state, "pendingPlanRestoredAfterFailure", JToken.DeepEquals(JToken.FromObject(Message(state).PendingPlan), JToken.FromObject(original)));
            }
            catch (Exception error) { Error(state, "Could not restore this test's original pending history plan: " + Describe(error)); }
        }

        private static void CreateFixture(RunState state)
        {
            if (AssetDatabase.IsValidFolder(state.TestFolder) || File.Exists(state.TestScenePath))
                throw new InvalidOperationException("The supposedly unique acceptance scene path already exists.");
            string current = "Assets";
            foreach (string segment in state.TestFolder.Split('/').Skip(1))
            {
                string next = current + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, segment)))
                    throw new IOException("Cannot create the owned fixture folder: " + next);
                current = next;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            state.TestSceneHandle = scene.handle; Save(state);
            Selection.objects = Array.Empty<Object>();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = state.TestObjectName;
            var marker = cube.AddComponent<GeneratedSceneObject>(); marker.Kind = "code-acceptance-fixture"; marker.BatchId = state.RunId;
            if (!EditorSceneManager.SaveScene(scene, state.TestScenePath)) throw new IOException("Could not save the disposable acceptance scene.");
            state.Report["fixture"] = new JObject { ["scene"] = state.TestScenePath, ["object"] = state.TestObjectName, ["ownerRunId"] = state.RunId };
            Save(state);
        }
        private static GameObject Fixture(RunState state)
        {
            var matches = Object.FindObjectsByType<GeneratedSceneObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(m => m.BatchId == state.RunId && m.Kind == "code-acceptance-fixture" && m.gameObject.name == state.TestObjectName &&
                    (m.gameObject.scene.path == state.TestScenePath || string.IsNullOrEmpty(m.gameObject.scene.path) && m.gameObject.scene.handle == state.TestSceneHandle)).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("Multiple objects claim the acceptance fixture identity.");
            return matches.Length == 1 ? matches[0].gameObject : null;
        }
        private static void DestroyFixture(RunState state)
        {
            var fixture = Fixture(state);
            if (fixture) Object.DestroyImmediate(fixture);
            Check(state, "ownedFixtureDestroyed", !Fixture(state));
            Save(state);
        }
        private static void CleanupScene(RunState state)
        {
            var active = SceneManager.GetActiveScene();
            bool ownActive = active.path == state.TestScenePath || string.IsNullOrEmpty(active.path) && active.handle == state.TestSceneHandle && state.TestSceneHandle != 0;
            DestroyFixture(state);
            if (ownActive) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.IsValidFolder(state.TestFolder) && !AssetDatabase.DeleteAsset(state.TestFolder))
                throw new IOException("Cannot remove the owned disposable scene folder.");
            bool cleaned = !File.Exists(state.TestScenePath) && !AssetDatabase.IsValidFolder(state.TestFolder);
            Check(state, "testSceneCleaned", cleaned);
            if (!cleaned) Error(state, "The disposable test scene still exists.");
        }
        private static void TryFinalCleanup(RunState state)
        {
            RestorePending(state);
            try { CleanupScene(state); }
            catch (Exception error) { Error(state, "Fixture cleanup failed: " + Describe(error)); }
        }

        private static AssistantMessage Message(RunState state) => new ChatHistoryStore(state.HistoryFolder).Load()
            .Single(s => s.Id == state.SessionId).Messages.Single(m => m.Id == state.MessageId);
        private static JObject StandardScriptState()
        {
            const string path = "UserSettings/WorldWeaverChatCode.json";
            return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null;
        }
        private static bool OwnScriptState(RunState state)
        {
            var script = StandardScriptState();
            var files = script?["Files"] as JArray;
            return script != null && (string)script["SessionId"] == state.SessionId && (string)script["MessageId"] == state.MessageId &&
                (bool?)script["Reverted"] != true &&
                files?.Count == 1 && (string)files[0]["Path"] == state.AssetPath &&
                Hash(Encoding.UTF8.GetBytes((string)files[0]["After"] ?? "")) == state.ImportedHash;
        }
        private static bool SourceRestored(RunState state) => state.SourceExisted ? FileHash(state.AssetPath) == state.SourceBeforeHash :
            !File.Exists(state.AssetPath) && !AssetDatabase.LoadMainAssetAtPath(state.AssetPath);
        private static JArray ImportErrors(RunState state) => (JArray)(state.Report["importCompilerErrors"] ?? (state.Report["importCompilerErrors"] = new JArray()));
        private static JArray RestoreErrors(RunState state) => (JArray)(state.Report["restoreCompilerErrors"] ?? (state.Report["restoreCompilerErrors"] = new JArray()));

        private static void CompilationStarted(object context)
        {
            UpdateCompilation(state => {
                if (state.Phase == "AwaitRestoreCompilation") state.RestoreCompilationStarted = true;
                else if (state.ApplyRequested && !state.RevertRequested) state.ImportCompilationStarted = true;
                Event(state, "compilation_started");
            });
        }
        private static void AssemblyFinished(string assembly, CompilerMessage[] messages)
        {
            UpdateCompilation(state => {
                var target = state.RevertRequested ? RestoreErrors(state) : ImportErrors(state);
                foreach (var message in messages.Where(m => m.type == CompilerMessageType.Error))
                    if (target.Count < 100) target.Add(new JObject { ["assembly"] = assembly, ["file"] = message.file,
                        ["line"] = message.line, ["column"] = message.column, ["message"] = ChatHistoryStore.Redact(message.message) });
            });
        }
        private static void CompilationFinished(object context)
        {
            UpdateCompilation(state => {
                if (state.Phase == "AwaitRestoreCompilation") state.RestoreCompilationFinished = true;
                else if (state.ApplyRequested && !state.RevertRequested) state.ImportCompilationFinished = true;
                Event(state, "compilation_finished");
            });
        }
        private static void UpdateCompilation(Action<RunState> change)
        {
            if (!Application.isBatchMode || !SessionState.GetBool(SessionKey, false)) return;
            try { var state = Load(); if (state?.Active != true) return; change(state); Save(state); }
            catch (Exception error) { UnityEngine.Debug.LogError("Chat-code acceptance compiler event failed: " + Describe(error)); }
        }
        private static void Phase(RunState state, string phase)
        {
            if (state.Phase != phase) { state.Phase = phase; state.DeadlineUtc = DateTime.UtcNow.AddSeconds(PhaseTimeoutSeconds); Event(state, "phase"); }
            Save(state);
        }
        private static void Event(RunState state, string kind)
        { ((JArray)state.Report["events"]).Add(new JObject { ["utc"] = DateTime.UtcNow.ToString("o"), ["kind"] = kind, ["phase"] = state.Phase }); }
        private static void Check(RunState state, string key, bool passed) { ((JObject)state.Report["checks"])[key] = passed; }
        private static void Error(RunState state, string error)
        { state.Failed = true; ((JArray)state.Report["errors"]).Add(ChatHistoryStore.Redact(error)); UnityEngine.Debug.LogError("Chat-code acceptance: " + ChatHistoryStore.Redact(error)); }
        private static void Save(RunState state)
        {
            SessionState.SetBool(SessionKey, state.Active);
            state.Report["phase"] = state.Phase; state.Report["phaseDeadlineUtc"] = state.DeadlineUtc.ToString("o");
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
            string temporary = StatePath + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(state, Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(StatePath)) File.Replace(temporary, StatePath, null); else File.Move(temporary, StatePath);
            Write(state.Report, state.OutputPath);
        }
        private static RunState Load() => File.Exists(StatePath) ? JsonConvert.DeserializeObject<RunState>(File.ReadAllText(StatePath),
            new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 64 }) : null;
        private static void Finish(RunState state, bool success)
        {
            state.Active = false;
            state.Report["finishedUtc"] = DateTime.UtcNow.ToString("o");
            try
            {
                var pending = Message(state).PendingPlan;
                state.Report["pendingPlan"] = pending == null ? null : JToken.FromObject(pending);
                state.Report["sourceAfterSha256"] = FileHash(state.AssetPath);
                bool restored = SourceRestored(state);
                state.Report["sourceRestoredAtExit"] = restored;
                bool manualCleanup = state.Applied && (!restored || OwnScriptState(state));
                state.Report["manualCleanupRequired"] = manualCleanup;
                state.Report["manualCleanupPaths"] = manualCleanup ? new JArray(state.AssetPath,
                    "UserSettings/WorldWeaverChatCode.json", StatePath, state.TestFolder) : new JArray();
                success &= restored;
                string context = ScriptDrafts.CompilationContext();
                try { state.Report["standardScriptState"] = JToken.Parse(context); }
                catch (JsonException) { state.Report["standardScriptState"] = context; }
            }
            catch (Exception error) { Error(state, "Final evidence could not be verified: " + Describe(error)); success = false; }
            state.Report["success"] = success && !state.Failed;
            var checks = (JObject)state.Report["checks"];
            state.Report["passedChecks"] = checks.Properties().Count(p => (bool?)p.Value == true);
            state.Report["totalChecks"] = checks.Count;
            try { Save(state); }
            catch (Exception error)
            { state.Report["success"] = false; SessionState.SetBool(SessionKey, false); UnityEngine.Debug.LogError("Cannot persist final code-acceptance evidence: " + Describe(error)); }
            UnityEngine.Debug.Log("WorldWeaver chat-code acceptance completed: success=" + state.Report["success"] + "; report=" + state.OutputPath);
            if (Application.isBatchMode) EditorApplication.Exit((bool)state.Report["success"] ? 0 : 1);
        }
        private static void VerifyIdentity(RunState state)
        {
            if (!Regex.IsMatch(state.RunId ?? "", "^[a-f0-9]{32}$") || !Regex.IsMatch(state.ClassName ?? "", "^[A-Za-z_][A-Za-z0-9_]{0,79}$") ||
                !ChatHistoryStore.ValidId(state.SessionId) || !ChatHistoryStore.ValidId(state.MessageId) ||
                state.AssetPath != "Assets/WorldWeaverGenerated/Scripts/" + state.ClassName + ".cs" ||
                state.TestFolder != "Assets/WorldWeaverGenerated/CodeAcceptance/" + state.RunId ||
                state.TestScenePath != state.TestFolder + "/CodeAcceptance.unity" || state.TestObjectName != "CodeAcceptanceCube-" + state.RunId ||
                !Path.GetFullPath(state.HistoryFolder).StartsWith(Path.Combine(ProjectDirectory, "UserSettings", "WorldWeaverChatAcceptance") + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Persisted runner identity or owned paths are invalid; no cleanup paths may be trusted.");
            var plan = JsonConvert.DeserializeObject<EditorPlan>(state.PlanJson);
            if (plan?.actions?.Count != 1 || plan.actions[0].tool != "draft_script" || plan.actions[0].name != state.ClassName ||
                plan.actions[0].args?["code"]?.Type != JTokenType.String ||
                Hash(Encoding.UTF8.GetBytes((string)plan.actions[0].args["code"])) != state.ReviewedHash ||
                Hash(Encoding.UTF8.GetBytes(ImportMarker + "\n" + (string)plan.actions[0].args["code"] + "\n")) != state.ImportedHash)
                throw new InvalidOperationException("Persisted review authorization no longer matches the original exact code.");
        }
        private static string FileHash(string path) => File.Exists(path) ? Hash(File.ReadAllBytes(path)) : null;
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string Describe(Exception error) => ChatHistoryStore.Redact(error.GetType().Name + ": " + error.Message);
        private static string OutputPath()
        {
            string explicitPath = Environment.GetEnvironmentVariable("WW_CODE_OUTPUT");
            if (!string.IsNullOrWhiteSpace(explicitPath)) return Path.GetFullPath(explicitPath);
            for (var root = new DirectoryInfo(ProjectDirectory); root != null; root = root.Parent)
                if (Directory.Exists(Path.Combine(root.FullName, "Packages", "com.worldweaver.toolkit")))
                    return Path.Combine(root.FullName, "results", "chat-code-acceptance.json");
            return Path.Combine(ProjectDirectory, "results", "chat-code-acceptance.json");
        }
        private static void Write(JObject report, string path)
        { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, report.ToString(Formatting.Indented), new UTF8Encoding(false)); }
    }
}
