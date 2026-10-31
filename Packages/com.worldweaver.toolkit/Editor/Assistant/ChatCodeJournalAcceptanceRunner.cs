using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    /// Explicit batch-only, two-import compiler/journal acceptance. Both exact source strings
    /// require independent review hashes. No LLM call and no automatic import on installation.
    /// Existing ChatCodeAcceptanceRunner and its real-LLM evidence are independent of this fixture.
    /// </summary>
    [InitializeOnLoad]
    public static class ChatCodeJournalAcceptanceRunner
    {
        private const string SessionKey = "WorldWeaver.ChatCodeJournalAcceptance.Armed";
        private const string Marker = "// Generated with WorldWeaver. User-reviewed C# source.";
        private const string JournalFolder = "UserSettings/WorldWeaverChatCode";
        private const double PhaseTimeoutSeconds = 180;
        private static bool launching, stepping;
        private static double nextPoll;
        private static string ProjectDirectory => Directory.GetParent(Application.dataPath).FullName;
        private static string StatePath => Path.Combine(ProjectDirectory, "UserSettings", "WorldWeaverChatCodeJournalAcceptance.json");
        private static readonly string[] RequiredChecks = {
            "reviewHashesMatched", "newSourceDestination", "importAExactFile", "importACompilerHistory",
            "importACompilerEvents", "importACompilerJournal", "importATypeRevision", "attachAReadback",
            "importBExactFile", "importBBacksUpExactA", "staleGetClassObserved", "unconfirmedBAttachRejected",
            "unconfirmedBAttachNoMutation", "importBCompilerHistory", "importBCompilerEvents",
            "importBCompilerJournal", "importBTypeRevision", "attachBReadback", "canRevertAUnavailableUnderB",
            "revertAOutOfOrderRejected", "fixtureDestroyedBeforeRevert", "revertBExactABytes",
            "restoreBCompilerHistory", "restoreBCompilerEvents", "restoreBCompilerJournal",
            "restoredATypeRevision", "canRevertAAfterB", "revertADeletesOwnSource", "restoreACompilerHistory",
            "restoreACompilerEvents", "restoreACompilerJournal", "fixtureSceneCleaned",
            "foreignJournalUnchanged", "allOwnedImportsReverted"
        };

        [Serializable] private sealed class SourceSpec
        {
            public string Label, MessageId, PlanJson, ReviewedHash, ImportedHash, ImportId;
            public int ExpectedRevision, RevertAttempts;
            public float ExpectedSpeed;
            public bool ApplyRequested;
        }
        [Serializable] private sealed class RunState
        {
            public bool Active, Failed;
            public string RunId, Phase, OutputPath, InputPath, ClassName, AssetPath, HistoryFolder, SessionId;
            public string TestFolder, TestScenePath, TestObjectName;
            public int TestSceneHandle;
            public DateTime DeadlineUtc;
            public SourceSpec A, B;
            public Dictionary<string, string> ForeignJournalHashes = new Dictionary<string, string>();
            public JObject Report = new JObject();
        }

        static ChatCodeJournalAcceptanceRunner()
        {
            EditorApplication.update += Tick;
            CompilationPipeline.compilationStarted += CompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += AssemblyFinished;
            CompilationPipeline.compilationFinished += CompilationFinished;
            try { SessionState.SetBool(SessionKey, Load()?.Active == true); }
            catch (Exception error) { Debug.LogError("Cannot recover journal acceptance: " + Describe(error)); }
        }

        /// <summary>
        /// -executeMethod WorldWeaver.Editor.Assistant.ChatCodeJournalAcceptanceRunner.RunCLI, without -quit.
        /// Requires WW_CODE_JOURNAL_INPUT, WW_CODE_JOURNAL_REVIEW_A_SHA256, WW_CODE_JOURNAL_REVIEW_B_SHA256.
        /// WW_CODE_JOURNAL_OUTPUT overrides the default results/chat-code-journal-acceptance.json.
        /// </summary>
        public static void RunCLI()
        {
            if (launching) { Debug.LogError("Journal acceptance is already launching."); return; }
            launching = true;
            RunState state = null;
            string output = null;
            try
            {
                output = OutputPath();
                if (Load()?.Active == true) { Debug.LogError("An explicitly armed journal test is already active; it will resume its persisted phase."); return; }
                if (!Application.isBatchMode) throw new InvalidOperationException("Use a disposable batch-mode Editor; this command never replaces a GUI user's scene.");
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || ScriptDrafts.Pending)
                    throw new InvalidOperationException("Wait for Edit Mode, asset import and existing compilation to finish.");
                if (Path.GetFullPath("Assets").TrimEnd(Path.DirectorySeparatorChar) != Application.dataPath.TrimEnd(Path.DirectorySeparatorChar))
                    throw new InvalidOperationException("The working directory must be the current Unity project.");
                string input = Environment.GetEnvironmentVariable("WW_CODE_JOURNAL_INPUT");
                if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("WW_CODE_JOURNAL_INPUT is required.");
                input = Path.GetFullPath(input);
                var fixture = JObject.Parse(File.ReadAllText(input), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                string className = (string)fixture["className"];
                if (!Regex.IsMatch(className ?? "", "^[A-Za-z_][A-Za-z0-9_]{0,79}$")) throw new ArgumentException("A unique runtime MonoBehaviour class name is required.");
                string hashA = ReviewHash("WW_CODE_JOURNAL_REVIEW_A_SHA256"), hashB = ReviewHash("WW_CODE_JOURNAL_REVIEW_B_SHA256");
                string codeA = RequiredString(fixture, "codeA"), codeB = RequiredString(fixture, "codeB");
                if (Hash(Encoding.UTF8.GetBytes(codeA)) != hashA || Hash(Encoding.UTF8.GetBytes(codeB)) != hashB || hashA == hashB)
                    throw new ArgumentException("Both different, exact UTF-8 source strings must match the independently supplied review hashes. Nothing was imported.");
                string asset = "Assets/WorldWeaverGenerated/Scripts/" + className + ".cs";
                if (File.Exists(asset) || File.Exists(asset + ".meta") || AssetDatabase.LoadMainAssetAtPath(asset))
                    throw new InvalidOperationException("This test requires a NEW class and absent .cs/.meta destination. Existing sources are never overwritten by the initial fixture import.");
                var a = Spec("A", codeA, hashA, className, RequiredInteger(fixture, "expectedRevisionA"), RequiredNumber(fixture, "expectedSpeedA"));
                var b = Spec("B", codeB, hashB, className, RequiredInteger(fixture, "expectedRevisionB"), RequiredNumber(fixture, "expectedSpeedB"));
                if (a.ExpectedRevision == b.ExpectedRevision || a.ExpectedSpeed == b.ExpectedSpeed)
                    throw new ArgumentException("The reviewed fixtures must expose different Revision constants and Speed defaults.");
                ScriptDrafts.Validate(Plan(a).actions.Single()); ScriptDrafts.Validate(Plan(b).actions.Single());
                string id = Guid.NewGuid().ToString("N");
                string folder = "Assets/WorldWeaverGenerated/CodeJournalAcceptance/" + id;
                state = new RunState {
                    Active = true, RunId = id, InputPath = input, OutputPath = output, ClassName = className, AssetPath = asset,
                    HistoryFolder = Path.Combine(ProjectDirectory, "UserSettings", "WorldWeaverChatJournalAcceptance", id),
                    SessionId = Guid.NewGuid().ToString("N"), A = a, B = b,
                    TestFolder = folder, TestScenePath = folder + "/CodeJournalAcceptance.unity", TestObjectName = "CodeJournalCube-" + id,
                    ForeignJournalHashes = JournalHashes(),
                    Report = new JObject {
                        ["schemaVersion"] = 1, ["runner"] = typeof(ChatCodeJournalAcceptanceRunner).FullName,
                        ["scope"] = "Two explicitly reviewed synthetic source fixtures; real Unity compilation, attach and per-import journal restoration. No LLM inference; separate from the real-LLM 11-check run.",
                        ["startedUtc"] = DateTime.UtcNow.ToString("o"), ["unityVersion"] = Application.unityVersion,
                        ["inputPath"] = input, ["outputPath"] = output, ["className"] = className,
                        ["assetPath"] = asset, ["sourceExistedBefore"] = false, ["sourceMetaExistedBefore"] = false,
                        ["sessionId"] = null, ["historyFolder"] = null, ["reviewedCodeASha256"] = hashA, ["reviewedCodeBSha256"] = hashB,
                        ["events"] = new JArray(), ["errors"] = new JArray(), ["checks"] = new JObject(), ["compilations"] = new JObject()
                    }
                };
                foreach (string check in RequiredChecks) Check(state, check, false);
                Check(state, "reviewHashesMatched", true); Check(state, "newSourceDestination", true);
                state.Report["sessionId"] = state.SessionId; state.Report["historyFolder"] = state.HistoryFolder;
                VerifyIdentity(state);
                Phase(state, "Armed"); // Persist exact review authorization before any owned scene or import.
                SaveHistory(state); CreateFixture(state);
                Apply(state, state.A, "ImportA");
            }
            catch (Exception error)
            {
                if (state?.Active == true) StartCleanup(state, Describe(error));
                else
                {
                    var rejected = new JObject { ["runner"] = typeof(ChatCodeJournalAcceptanceRunner).FullName,
                        ["success"] = false, ["refusedBeforeImport"] = true, ["finishedUtc"] = DateTime.UtcNow.ToString("o"), ["error"] = Describe(error) };
                    try { if (output != null) Write(rejected, output); } catch (Exception writing) { Debug.LogError(Describe(writing)); }
                    Debug.LogError("Journal acceptance refused: " + Describe(error));
                    if (Application.isBatchMode) EditorApplication.Exit(1);
                }
            }
            finally { launching = false; }
        }

        private static SourceSpec Spec(string label, string code, string hash, string className, int revision, float speed)
        {
            var plan = new EditorPlan { reply = "Synthetic journal acceptance fixture " + label, complete = true,
                actions = new List<EditorCommand> { new EditorCommand { tool = "draft_script", name = className, target = "",
                    args = new JObject { ["code"] = code, ["description"] = "Reviewed synthetic journal fixture " + label } } } };
            return new SourceSpec { Label = label, MessageId = Guid.NewGuid().ToString("N"), ReviewedHash = hash,
                ImportedHash = Hash(Encoding.UTF8.GetBytes(Marker + "\n" + code + "\n")), PlanJson = JsonConvert.SerializeObject(plan),
                ExpectedRevision = revision, ExpectedSpeed = speed };
        }
        private static EditorPlan Plan(SourceSpec spec) => JsonConvert.DeserializeObject<EditorPlan>(spec.PlanJson);

        private static void SaveHistory(RunState state)
        {
            var chat = new ChatSession { Id = state.SessionId, Title = "Synthetic reviewed journal acceptance " + state.RunId };
            foreach (var spec in new[] { state.A, state.B }) chat.Messages.Add(new AssistantMessage { Id = spec.MessageId,
                Text = "Explicitly reviewed synthetic fixture " + spec.Label, PendingPlan = Plan(spec), Status = "Код ожидает отдельного просмотра" });
            new ChatHistoryStore(state.HistoryFolder).Save(chat);
        }

        private static void Apply(RunState state, SourceSpec spec, string phase)
        {
            VerifyIdentity(state);
            string expectedBefore = spec.Label == "A" ? null : state.A.ImportedHash;
            if (FileHash(state.AssetPath) != expectedBefore || spec.ApplyRequested)
                throw new InvalidOperationException("The owned source or apply authorization changed before import " + spec.Label + ".");
            var original = Message(state, spec).PendingPlan;
            if (original == null || !JToken.DeepEquals(JToken.FromObject(original), JToken.Parse(spec.PlanJson)))
                throw new InvalidOperationException("The isolated pending fixture differs from the exact reviewed plan.");
            spec.ApplyRequested = true;
            Phase(state, phase); // Import authorization and phase survive AssetDatabase.Refresh/domain reload.
            using (var controller = new ChatController(historyFolder: state.HistoryFolder))
            { controller.OpenChat(state.SessionId); controller.ApplyPendingAsync(spec.MessageId).GetAwaiter().GetResult(); }
            state = Load() ?? state;
            spec = spec.Label == "A" ? state.A : state.B;
            var journal = Journal(state, spec);
            if (journal == null || (bool?)journal["Pending"] != true || FileHash(state.AssetPath) != spec.ImportedHash || Message(state, spec).PendingPlan != null)
                throw new InvalidOperationException("ApplyPendingAsync did not create the exact reviewed pending import " + spec.Label + ".");
            spec.ImportId = (string)journal["ImportId"];
            state.Report["import" + spec.Label + "JournalId"] = spec.ImportId;
            Check(state, "import" + spec.Label + "ExactFile", true);
            if (spec.Label == "B")
            {
                var file = journal["Files"].Single();
                bool exactA = (bool?)file["Existed"] == true && (string)file["BeforeSha256"] == state.A.ImportedHash &&
                    Hash(Convert.FromBase64String((string)file["BeforeBytes"] ?? "")) == state.A.ImportedHash;
                Check(state, "importBBacksUpExactA", exactA);
                if (!exactA) throw new InvalidOperationException("Import B did not preserve exact bytes of A in its own journal.");
                ProbeUnconfirmedAttach(state);
            }
            Save(state);
        }

        private static void ProbeUnconfirmedAttach(RunState state)
        {
            var cube = Fixture(state) ?? throw new InvalidOperationException("Owned cube is absent before the stale-type probe.");
            var staleType = CompiledType(state);
            bool stale = TypeRevision(staleType) == state.A.ExpectedRevision;
            Check(state, "staleGetClassObserved", stale);
            int countBefore = cube.GetComponents<Component>().Length;
            bool rejected = false; string reason = null;
            try { ScriptDrafts.Attach(AttachCommand(state, cube)); }
            catch (ArgumentException error)
            { reason = Describe(error); rejected = error.Message.Contains("подтверждённой успешной компиляции текущего файла"); }
            bool unchanged = countBefore == cube.GetComponents<Component>().Length && (staleType == null || !cube.GetComponent(staleType));
            Check(state, "unconfirmedBAttachRejected", rejected); Check(state, "unconfirmedBAttachNoMutation", unchanged);
            state.Report["unconfirmedAttachProbe"] = new JObject { ["sourceSha256"] = FileHash(state.AssetPath), ["oldTypeRevision"] = TypeRevision(staleType),
                ["journalPending"] = (bool?)Journal(state, state.B)?["Pending"], ["rejected"] = rejected, ["exception"] = reason, ["componentsUnchanged"] = unchanged };
            Save(state);
            if (!stale || !rejected || !unchanged) throw new InvalidOperationException("A real stale GetClass must exist, and attaching unconfirmed B must be refused without adding a component.");
        }

        private static void Tick()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(SessionKey, false) || launching || stepping || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .25; stepping = true;
            RunState state = null; bool verified = false;
            try
            {
                state = Load();
                if (state?.Active != true) { SessionState.SetBool(SessionKey, false); return; }
                VerifyIdentity(state); verified = true;
                if (DateTime.UtcNow > state.DeadlineUtc)
                {
                    if (!state.Phase.StartsWith("Cleanup", StringComparison.Ordinal)) StartCleanup(state, "Phase deadline exceeded: " + state.Phase);
                    else { Error(state, "Cleanup compilation/deadline not confirmed: " + state.Phase); Finish(state, false); }
                    return;
                }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (state.Phase.StartsWith("Cleanup", StringComparison.Ordinal)) { CleanupTick(state); return; }
                switch (state.Phase)
                {
                    case "Armed": StartCleanup(state, "Interrupted before explicit first Apply; no initial import is resumed automatically."); break;
                    case "ImportA": InspectImport(state, state.A); break;
                    case "ImportB": InspectImport(state, state.B); break;
                    case "RestoreB": InspectRestoration(state, state.B); break;
                    case "RestoreA": InspectRestoration(state, state.A); break;
                    default: StartCleanup(state, "Unknown persisted phase: " + state.Phase); break;
                }
            }
            catch (Exception error)
            {
                if (state?.Active == true && verified)
                {
                    if (state.Phase.StartsWith("Cleanup", StringComparison.Ordinal))
                    { Error(state, "Cleanup failed: " + Describe(error)); Finish(state, false); }
                    else StartCleanup(Load() ?? state, Describe(error));
                }
                else
                {
                    Debug.LogError("Journal acceptance identity/state is untrusted; no asset/history cleanup was attempted: " + Describe(error));
                    if (state != null)
                    {
                        state.Active = false; state.OutputPath = OutputPath();
                        state.Report = state.Report ?? new JObject(); state.Report["success"] = false; state.Report["manualCleanupRequired"] = true;
                        state.Report["error"] = "Untrusted persisted identity; no cleanup attempted: " + Describe(error);
                        Save(state);
                    }
                    EditorApplication.Exit(1);
                }
            }
            finally { stepping = false; }
        }

        private static void InspectImport(RunState state, SourceSpec spec)
        {
            if (ScriptDrafts.Pending || !HasHistory(state, spec, "compile_scripts")) return;
            bool history = HistorySuccess(state, spec, "compile_scripts", "import" + spec.Label);
            bool events = CompileSuccess(state, "Import" + spec.Label);
            bool journal = JournalProof(state, spec, false);
            var type = CompiledType(state);
            bool correctType = type != null && typeof(MonoBehaviour).IsAssignableFrom(type) && !type.IsAbstract && type.Name == state.ClassName && TypeRevision(type) == spec.ExpectedRevision;
            Check(state, "import" + spec.Label + "CompilerHistory", history); Check(state, "import" + spec.Label + "CompilerEvents", events);
            Check(state, "import" + spec.Label + "CompilerJournal", journal); Check(state, "import" + spec.Label + "TypeRevision", correctType);
            Save(state);
            if (!history || !events || !journal || !correctType || FileHash(state.AssetPath) != spec.ImportedHash)
                throw new InvalidOperationException("Import " + spec.Label + " lacks successful real compiler/history/journal/type evidence.");
            var cube = Fixture(state) ?? throw new InvalidOperationException("The owned cube was lost during compilation.");
            var attach = ScriptDrafts.Attach(AttachCommand(state, cube));
            var component = cube.GetComponent(type);
            bool attached = attach.Success && component && Math.Abs((float)type.GetField("Speed").GetValue(component) - spec.ExpectedSpeed) < .0001f;
            Check(state, "attach" + spec.Label + "Readback", attached); state.Report["attach" + spec.Label + "Result"] = JToken.FromObject(attach);
            Save(state);
            if (!attached) throw new InvalidOperationException("Real attach did not produce fixture " + spec.Label + " with the expected Speed default.");
            if (spec.Label == "A")
            {
                Object.DestroyImmediate(component);
                if (cube.GetComponent(type)) throw new InvalidOperationException("The A component was not removed before the unconfirmed B probe.");
                if (!EditorSceneManager.SaveScene(cube.scene)) throw new IOException("Cannot save the owned cube without A's component.");
                Apply(state, state.B, "ImportB");
            }
            else
            {
                bool unavailable = !ScriptDrafts.CanRevert(state.SessionId, state.A.MessageId);
                bool rejected = false; string before = FileHash(state.AssetPath), reason = null;
                try { ScriptDrafts.Revert(state.SessionId, state.A.MessageId); }
                catch (InvalidOperationException error) { rejected = error.Message.Contains("Файл изменён вручную"); reason = Describe(error); }
                bool unchanged = FileHash(state.AssetPath) == before && (bool?)Journal(state, state.A)?["Reverted"] != true && !ScriptDrafts.Pending;
                Check(state, "canRevertAUnavailableUnderB", unavailable); Check(state, "revertAOutOfOrderRejected", rejected && unchanged);
                state.Report["outOfOrderRevertProbe"] = new JObject { ["rejected"] = rejected, ["unchanged"] = unchanged, ["exception"] = reason };
                Save(state);
                if (!unavailable || !rejected || !unchanged) throw new InvalidOperationException("Reverting A while B owns the source must be refused without mutation.");
                DestroyFixture(state);
                Check(state, "fixtureDestroyedBeforeRevert", !Fixture(state)); Save(state);
                BeginRevert(state, state.B, "RestoreB");
            }
        }

        private static void BeginRevert(RunState state, SourceSpec spec, string phase)
        {
            var journal = Journal(state, spec);
            if (journal == null || (bool?)journal["Reverted"] == true || !ScriptDrafts.CanRevert(state.SessionId, spec.MessageId) || FileHash(state.AssetPath) != spec.ImportedHash)
                throw new InvalidOperationException("Only the exact current owned import may be reverted: " + spec.Label + ".");
            if (spec.RevertAttempts >= 2) throw new InvalidOperationException("The bounded standard-Revert recovery attempts are exhausted for " + spec.Label + ".");
            DestroyFixture(state);
            spec.RevertAttempts++;
            Phase(state, phase); // Persist ownership and deadline before the synchronous standard Revert/Refresh.
            ScriptDrafts.Revert(state.SessionId, spec.MessageId);
            state = Load() ?? state; spec = spec.Label == "A" ? state.A : state.B;
            journal = Journal(state, spec);
            if (journal == null || (bool?)journal["Reverted"] != true || (bool?)journal["Restoring"] != true)
                throw new InvalidOperationException("The standard journal did not record this message's Revert: " + spec.Label + ".");
            Save(state);
        }

        private static void InspectRestoration(RunState state, SourceSpec spec)
        {
            if (ScriptDrafts.Pending || !HasHistory(state, spec, "restore_compile_scripts")) return;
            bool history = HistorySuccess(state, spec, "restore_compile_scripts", "restore" + spec.Label);
            bool events = CompileSuccess(state, "Restore" + spec.Label), journal = JournalProof(state, spec, true);
            Check(state, "restore" + spec.Label + "CompilerHistory", history); Check(state, "restore" + spec.Label + "CompilerEvents", events);
            Check(state, "restore" + spec.Label + "CompilerJournal", journal);
            if (spec.Label == "B")
            {
                bool exact = FileHash(state.AssetPath) == state.A.ImportedHash;
                bool typeA = TypeRevision(CompiledType(state)) == state.A.ExpectedRevision;
                bool canA = ScriptDrafts.CanRevert(state.SessionId, state.A.MessageId);
                Check(state, "revertBExactABytes", exact); Check(state, "restoredATypeRevision", typeA); Check(state, "canRevertAAfterB", canA);
                Save(state);
                if (!history || !events || !journal || !exact || !typeA || !canA) throw new InvalidOperationException("B's real restoration must recover exact compiled A and its independent Revert.");
                BeginRevert(state, state.A, "RestoreA");
            }
            else
            {
                Check(state, "revertADeletesOwnSource", OriginalSourceRestored(state));
                Check(state, "allOwnedImportsReverted", AllOwnedReverted(state));
                Check(state, "foreignJournalUnchanged", ForeignJournalsUnchanged(state));
                CleanupScene(state); Save(state);
                Finish(state, RequiredChecks.All(key => (bool?)state.Report["checks"][key] == true));
            }
        }

        private static bool HasHistory(RunState state, SourceSpec spec, string tool) => Message(state, spec).Steps.Any(s => s.Tool == tool);
        private static bool HistorySuccess(RunState state, SourceSpec spec, string tool, string reportKey)
        {
            var feedback = Message(state, spec).Steps.Where(s => s.Tool == tool).ToArray();
            state.Report[reportKey + "CompilerFeedback"] = JArray.FromObject(feedback);
            return feedback.Length == 1 && feedback[0].Success && (feedback[0].Target ?? "").Split(',').Select(p => p.Trim()).Contains(state.AssetPath);
        }
        private static bool CompileSuccess(RunState state, string phase)
        {
            var item = state.Report["compilations"]?[phase];
            return (bool?)item?["started"] == true && (bool?)item?["finished"] == true && item["errors"] is JArray errors && errors.Count == 0;
        }
        private static bool JournalProof(RunState state, SourceSpec spec, bool restored)
        {
            var journal = Journal(state, spec);
            if (journal == null) return false;
            state.Report[(restored ? "restore" : "import") + spec.Label + "JournalProof"] = new JObject {
                ["importId"] = (string)journal["ImportId"], ["pending"] = (bool?)journal["Pending"], ["reverted"] = (bool?)journal["Reverted"],
                ["restoring"] = (bool?)journal["Restoring"], ["compilationConfirmed"] = (bool?)journal["CompilationConfirmed"], ["compilationSucceeded"] = (bool?)journal["CompilationSucceeded"] };
            return (bool?)journal["Pending"] == false && (bool?)journal["Reverted"] == restored && (bool?)journal["Restoring"] == restored &&
                (bool?)journal["CompilationConfirmed"] == true && (bool?)journal["CompilationSucceeded"] == true;
        }
        private static JObject Journal(RunState state, SourceSpec spec)
        {
            if (!Directory.Exists(JournalFolder)) return null;
            var matches = new List<JObject>();
            foreach (string path in Directory.GetFiles(JournalFolder, "*.json"))
            {
                var item = JObject.Parse(File.ReadAllText(path));
                if ((string)item["SessionId"] != state.SessionId || (string)item["MessageId"] != spec.MessageId) continue;
                var files = item["Files"] as JArray;
                if (!Regex.IsMatch((string)item["ImportId"] ?? "", "^[a-f0-9]{32}$") ||
                    Path.GetFileNameWithoutExtension(path) != (string)item["ImportId"] || files?.Count != 1 ||
                    (string)files[0]["Path"] != state.AssetPath || (string)files[0]["AfterSha256"] != spec.ImportedHash ||
                    Hash(Encoding.UTF8.GetBytes((string)files[0]["After"] ?? "")) != spec.ImportedHash ||
                    Path.GetFullPath((string)item["HistoryFolder"] ?? "") != state.HistoryFolder ||
                    !string.IsNullOrEmpty(spec.ImportId) && (string)item["ImportId"] != spec.ImportId)
                    throw new InvalidOperationException("An owned journal's import identity, history path or exact source hash differs from the armed fixture.");
                matches.Add(item);
            }
            if (matches.Count > 1) throw new InvalidOperationException("Multiple imports claim one isolated fixture message; cleanup is ambiguous.");
            return matches.SingleOrDefault();
        }
        private static AssistantMessage Message(RunState state, SourceSpec spec) => new ChatHistoryStore(state.HistoryFolder).Load()
            .Single(s => s.Id == state.SessionId).Messages.Single(m => m.Id == spec.MessageId);
        private static Type CompiledType(RunState state)
        { var script = AssetDatabase.LoadAssetAtPath<MonoScript>(state.AssetPath); return script ? script.GetClass() : null; }
        private static int? TypeRevision(Type type)
        {
            var field = type?.GetField("Revision", BindingFlags.Public | BindingFlags.Static);
            return field != null && field.IsLiteral && field.FieldType == typeof(int) ? (int?)field.GetRawConstantValue() : null;
        }
        private static EditorCommand AttachCommand(RunState state, GameObject cube) => new EditorCommand {
            tool = "attach_script", name = state.ClassName, target = "id:" + cube.GetInstanceID(), args = new JObject() };

        private static void StartCleanup(RunState state, string reason)
        {
            Error(state, reason);
            Phase(state, "Cleanup"); // Never re-apply source in recovery. Only LIFO standard Revert of exact owned hashes.
        }
        private static void CleanupTick(RunState state)
        {
            DestroyFixture(state);
            if (ScriptDrafts.Pending) return;
            var b = Journal(state, state.B); var a = Journal(state, state.A);
            if (b != null && (bool?)b["Reverted"] != true)
            { BeginRevert(state, state.B, "CleanupRestoreB"); return; }
            if (a != null && (bool?)a["Reverted"] != true)
            { BeginRevert(state, state.A, "CleanupRestoreA"); return; }
            // A restored journal with unconfirmed/error feedback never becomes a passing cleanup result.
            if ((a != null && !JournalProof(state, state.A, true)) || (b != null && !JournalProof(state, state.B, true)))
                Error(state, "Source restoration occurred, but a restoration compiler proof is unconfirmed or failed.");
            RestorePendingHistory(state); CleanupScene(state); Finish(state, false);
        }
        private static void RestorePendingHistory(RunState state)
        {
            try
            {
                var store = new ChatHistoryStore(state.HistoryFolder);
                var session = store.Load().Single(s => s.Id == state.SessionId);
                foreach (var spec in new[] { state.A, state.B })
                {
                    if (!spec.ApplyRequested) continue;
                    var message = session.Messages.Single(m => m.Id == spec.MessageId); var plan = Plan(spec);
                    if (message.PendingPlan != null && !JToken.DeepEquals(JToken.FromObject(message.PendingPlan), JToken.FromObject(plan)))
                        throw new InvalidOperationException("Changed pending history will not be overwritten during recovery.");
                    message.PendingPlan = plan; message.IsError = true; message.Status = "Synthetic acceptance failed; reviewed draft restored for inspection";
                }
                store.Save(session); state.Report["reviewedPendingHistoryRestoredAfterFailure"] = true;
            }
            catch (Exception error) { Error(state, "Could not restore owned reviewed pending history: " + Describe(error)); }
        }

        private static void CreateFixture(RunState state)
        {
            if (AssetDatabase.IsValidFolder(state.TestFolder) || Directory.Exists(state.TestFolder) || File.Exists(state.TestScenePath))
                throw new InvalidOperationException("Unique fixture scene path already exists.");
            string current = "Assets";
            foreach (string segment in state.TestFolder.Split('/').Skip(1))
            {
                string next = current + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, segment))) throw new IOException("Cannot create owned folder " + next);
                current = next;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            state.TestSceneHandle = scene.handle; Save(state); Selection.objects = Array.Empty<Object>();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = state.TestObjectName;
            var marker = cube.AddComponent<GeneratedSceneObject>(); marker.Kind = "code-journal-acceptance-fixture"; marker.BatchId = state.RunId;
            if (!EditorSceneManager.SaveScene(scene, state.TestScenePath)) throw new IOException("Cannot save owned fixture scene.");
            state.Report["fixture"] = new JObject { ["scene"] = state.TestScenePath, ["object"] = state.TestObjectName, ["runId"] = state.RunId }; Save(state);
        }
        private static GameObject Fixture(RunState state)
        {
            var matches = Object.FindObjectsByType<GeneratedSceneObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(m => m.BatchId == state.RunId && m.Kind == "code-journal-acceptance-fixture" && m.gameObject.name == state.TestObjectName &&
                    (m.gameObject.scene.path == state.TestScenePath || string.IsNullOrEmpty(m.gameObject.scene.path) && m.gameObject.scene.handle == state.TestSceneHandle)).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("Ambiguous fixture ownership; multiple cubes share the run identity.");
            return matches.Length == 1 ? matches[0].gameObject : null;
        }
        private static void DestroyFixture(RunState state)
        {
            var cube = Fixture(state);
            if (!cube) return;
            var scene = cube.scene; Object.DestroyImmediate(cube);
            if (scene.path == state.TestScenePath && scene.isLoaded && !EditorSceneManager.SaveScene(scene)) throw new IOException("Cannot persist removal of owned fixture component/object.");
        }
        private static void CleanupScene(RunState state)
        {
            DestroyFixture(state); var scene = SceneManager.GetActiveScene();
            if (scene.path == state.TestScenePath || string.IsNullOrEmpty(scene.path) && scene.handle == state.TestSceneHandle && state.TestSceneHandle != 0)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.IsValidFolder(state.TestFolder) && !AssetDatabase.DeleteAsset(state.TestFolder)) throw new IOException("Cannot delete owned fixture folder.");
            bool clean = !Directory.Exists(state.TestFolder) && !File.Exists(state.TestFolder + ".meta") && !Fixture(state);
            Check(state, "fixtureSceneCleaned", clean);
            if (!clean) throw new IOException("Owned fixture scene/folder cleanup is not confirmed.");
        }
        private static bool OriginalSourceRestored(RunState state) => !File.Exists(state.AssetPath) && !File.Exists(state.AssetPath + ".meta") && !AssetDatabase.LoadMainAssetAtPath(state.AssetPath);
        private static bool AllOwnedReverted(RunState state) => new[] { state.A, state.B }.All(spec => {
            var journal = Journal(state, spec); return journal == null ? !spec.ApplyRequested : (bool?)journal["Reverted"] == true && (bool?)journal["Pending"] == false; });
        private static Dictionary<string, string> JournalHashes() => Directory.Exists(JournalFolder)
            ? Directory.GetFiles(JournalFolder, "*.json").ToDictionary(p => p, FileHash, StringComparer.Ordinal) : new Dictionary<string, string>();
        private static bool ForeignJournalsUnchanged(RunState state) => state.ForeignJournalHashes.All(pair => FileHash(pair.Key) == pair.Value);

        private static void CompilationStarted(object context) => UpdateCompilation(state => {
            var item = Compilation(state); item["started"] = true; item["startedUtc"] = DateTime.UtcNow.ToString("o"); Event(state, "compilation_started"); });
        private static void AssemblyFinished(string assembly, CompilerMessage[] messages) => UpdateCompilation(state => {
            var errors = (JArray)Compilation(state)["errors"];
            foreach (var message in messages.Where(m => m.type == CompilerMessageType.Error)) if (errors.Count < 100)
                errors.Add(new JObject { ["assembly"] = assembly, ["file"] = message.file, ["line"] = message.line, ["column"] = message.column, ["message"] = ChatHistoryStore.Redact(message.message) }); });
        private static void CompilationFinished(object context) => UpdateCompilation(state => {
            var item = Compilation(state); item["finished"] = true; item["finishedUtc"] = DateTime.UtcNow.ToString("o"); Event(state, "compilation_finished"); });
        private static JObject Compilation(RunState state)
        {
            var all = (JObject)state.Report["compilations"];
            if (!(all[state.Phase] is JObject item)) { item = new JObject { ["started"] = false, ["finished"] = false, ["errors"] = new JArray() }; all[state.Phase] = item; }
            return item;
        }
        private static void UpdateCompilation(Action<RunState> change)
        {
            if (!Application.isBatchMode || !SessionState.GetBool(SessionKey, false)) return;
            try { var state = Load(); if (state?.Active != true) return; VerifyIdentity(state); change(state); Save(state); }
            catch (Exception error) { Debug.LogError("Journal acceptance compiler-event evidence could not be persisted: " + Describe(error)); }
        }

        private static void Finish(RunState state, bool success)
        {
            state.Active = false;
            try
            {
                if (state.Failed) RestorePendingHistory(state);
                bool source = OriginalSourceRestored(state), allReverted = AllOwnedReverted(state), foreign = ForeignJournalsUnchanged(state);
                bool scene = !Directory.Exists(state.TestFolder) && !File.Exists(state.TestFolder + ".meta") && !Fixture(state);
                bool compilerConfirmed = new[] { state.A, state.B }.All(spec => Journal(state, spec) == null || JournalProof(state, spec, true));
                state.Report["sourceRestoredAtExit"] = source; state.Report["allOwnedImportsRevertedAtExit"] = allReverted;
                state.Report["foreignJournalsUnchangedAtExit"] = foreign; state.Report["fixtureCleanedAtExit"] = scene;
                state.Report["restoreCompilationConfirmedAtExit"] = compilerConfirmed;
                bool manual = !source || !allReverted || !scene || !compilerConfirmed || !foreign || ScriptDrafts.Pending;
                state.Report["manualCleanupRequired"] = manual;
                state.Report["manualCleanupPaths"] = manual ? new JArray(state.AssetPath, state.AssetPath + ".meta", state.TestFolder, StatePath,
                    JournalFolder + "/" + state.A.ImportId + ".json", JournalFolder + "/" + state.B.ImportId + ".json") : new JArray();
                state.Report["cleanupInstructions"] = manual ? "Inspect exact listed owned paths and journal; preserve manual edits. Use the standard message-specific Revert in B→A order after compilation completes. Do not mark this run passed." : null;
                success &= source && allReverted && foreign && scene && compilerConfirmed && !manual;
            }
            catch (Exception error) { Error(state, "Final cleanup evidence failed: " + Describe(error)); state.Report["manualCleanupRequired"] = true; success = false; }
            state.Report["finishedUtc"] = DateTime.UtcNow.ToString("o"); state.Report["success"] = success && !state.Failed;
            var checks = (JObject)state.Report["checks"]; state.Report["passedChecks"] = checks.Properties().Count(p => (bool?)p.Value == true); state.Report["totalChecks"] = RequiredChecks.Length;
            try { Save(state); }
            catch (Exception error) { SessionState.SetBool(SessionKey, false); state.Report["success"] = false; Debug.LogError("Cannot persist final journal acceptance evidence: " + Describe(error)); }
            Debug.Log("WorldWeaver journal acceptance completed: success=" + state.Report["success"] + "; checks=" + state.Report["passedChecks"] + "/" + state.Report["totalChecks"] + "; report=" + state.OutputPath);
            if (Application.isBatchMode) EditorApplication.Exit((bool)state.Report["success"] ? 0 : 1);
        }
        private static void VerifyIdentity(RunState state)
        {
            if (!ChatHistoryStore.ValidId(state.RunId) || !ChatHistoryStore.ValidId(state.SessionId) || state.A == null || state.B == null ||
                !ChatHistoryStore.ValidId(state.A.MessageId) || !ChatHistoryStore.ValidId(state.B.MessageId) || state.A.MessageId == state.B.MessageId ||
                !Regex.IsMatch(state.ClassName ?? "", "^[A-Za-z_][A-Za-z0-9_]{0,79}$") || state.AssetPath != "Assets/WorldWeaverGenerated/Scripts/" + state.ClassName + ".cs" ||
                state.TestFolder != "Assets/WorldWeaverGenerated/CodeJournalAcceptance/" + state.RunId || state.TestScenePath != state.TestFolder + "/CodeJournalAcceptance.unity" ||
                state.TestObjectName != "CodeJournalCube-" + state.RunId || state.HistoryFolder != Path.Combine(ProjectDirectory, "UserSettings", "WorldWeaverChatJournalAcceptance", state.RunId) ||
                state.ForeignJournalHashes == null || state.ForeignJournalHashes.Keys.Any(p => Path.GetDirectoryName(p).Replace('\\', '/') != JournalFolder || !Regex.IsMatch(Path.GetFileNameWithoutExtension(p), "^[a-f0-9]{32}$")))
                throw new InvalidOperationException("Persisted run identity/owned paths are untrusted; no cleanup paths may be used.");
            foreach (var spec in new[] { state.A, state.B })
            {
                var plan = Plan(spec);
                if (spec.Label != (ReferenceEquals(spec, state.A) ? "A" : "B") || !Regex.IsMatch(spec.ReviewedHash ?? "", "^[a-f0-9]{64}$") ||
                    plan?.actions?.Count != 1 || plan.actions[0].tool != "draft_script" || plan.actions[0].name != state.ClassName || plan.actions[0].target != "" ||
                    plan.actions[0].args?["code"]?.Type != JTokenType.String || Hash(Encoding.UTF8.GetBytes((string)plan.actions[0].args["code"])) != spec.ReviewedHash ||
                    Hash(Encoding.UTF8.GetBytes(Marker + "\n" + (string)plan.actions[0].args["code"] + "\n")) != spec.ImportedHash ||
                    !string.IsNullOrEmpty(spec.ImportId) && !ChatHistoryStore.ValidId(spec.ImportId))
                    throw new InvalidOperationException("Persisted source authorization differs from the exact reviewed fixture.");
            }
            if (state.A.ReviewedHash == state.B.ReviewedHash) throw new InvalidOperationException("Fixtures A and B are identical.");
        }
        private static void Phase(RunState state, string phase)
        { if (state.Phase != phase) { state.Phase = phase; state.DeadlineUtc = DateTime.UtcNow.AddSeconds(PhaseTimeoutSeconds); Event(state, "phase"); } Save(state); }
        private static void Event(RunState state, string kind) => ((JArray)state.Report["events"]).Add(new JObject { ["utc"] = DateTime.UtcNow.ToString("o"), ["kind"] = kind, ["phase"] = state.Phase });
        private static void Check(RunState state, string key, bool passed) => ((JObject)state.Report["checks"])[key] = passed;
        private static void Error(RunState state, string reason)
        { state.Failed = true; ((JArray)state.Report["errors"]).Add(ChatHistoryStore.Redact(reason)); Debug.LogError("Journal acceptance: " + ChatHistoryStore.Redact(reason)); }
        private static void Save(RunState state)
        {
            SessionState.SetBool(SessionKey, state.Active); state.Report["phase"] = state.Phase; state.Report["phaseDeadlineUtc"] = state.DeadlineUtc.ToString("o");
            AtomicText(StatePath, JsonConvert.SerializeObject(state, Formatting.Indented)); Write(state.Report, state.OutputPath);
        }
        private static RunState Load() => File.Exists(StatePath) ? JsonConvert.DeserializeObject<RunState>(File.ReadAllText(StatePath),
            new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 64 }) : null;
        private static string ReviewHash(string variable)
        { string hash = (Environment.GetEnvironmentVariable(variable) ?? "").Trim().ToLowerInvariant(); if (!Regex.IsMatch(hash, "^[a-f0-9]{64}$")) throw new ArgumentException(variable + " must be the reviewed exact UTF-8 source SHA256."); return hash; }
        private static string RequiredString(JObject value, string name)
        { if (value[name]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)value[name])) throw new ArgumentException(name + " must be a source string."); return (string)value[name]; }
        private static int RequiredInteger(JObject value, string name)
        { if (value[name]?.Type != JTokenType.Integer) throw new ArgumentException(name + " must be an integer."); return (int)value[name]; }
        private static float RequiredNumber(JObject value, string name)
        { if (value[name]?.Type != JTokenType.Integer && value[name]?.Type != JTokenType.Float) throw new ArgumentException(name + " must be numeric."); float result = (float)value[name]; if (float.IsNaN(result) || float.IsInfinity(result)) throw new ArgumentException(name + " must be finite."); return result; }
        private static string FileHash(string path) => File.Exists(path) ? Hash(File.ReadAllBytes(path)) : null;
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string Describe(Exception error) => ChatHistoryStore.Redact(error.GetType().Name + ": " + error.Message);
        private static string OutputPath()
        {
            string path = Environment.GetEnvironmentVariable("WW_CODE_JOURNAL_OUTPUT"); if (!string.IsNullOrWhiteSpace(path)) return Path.GetFullPath(path);
            for (var root = new DirectoryInfo(ProjectDirectory); root != null; root = root.Parent)
                if (Directory.Exists(Path.Combine(root.FullName, "Packages", "com.worldweaver.toolkit"))) return Path.Combine(root.FullName, "results", "chat-code-journal-acceptance.json");
            return Path.Combine(ProjectDirectory, "results", "chat-code-journal-acceptance.json");
        }
        private static void Write(JObject value, string path) => AtomicText(path, value.ToString(Formatting.Indented));
        private static void AtomicText(string path, string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, value, new UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
