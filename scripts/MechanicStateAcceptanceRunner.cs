using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;
using Object = UnityEngine.Object;

/// <summary>
/// Opt-in disposable batch acceptance: a fresh real local chat receives an explicit boolean-state specification and must propose reviewed C#
/// for a mechanic without the prompt asking for code. Never approves, imports or attaches drafts.
/// Install this harness into a disposable consumer's Assets/Editor and invoke Run without -quit.
/// </summary>
public static class MechanicStateAcceptanceRunner
{
    const string Prompt = "Создай синий куб «Маяк». Когда игрок нажимает F, цвет куба должен переключаться между синим и янтарным; повторное нажатие возвращает исходный синий цвет. На одно нажатие — одно переключение. Уточняю способ хранения состояния после предыдущих неудачных попыток: храни два отдельных неизменяемых цвета — синий RGBA(0.235,0.286,1,1) и янтарный RGBA(1,0.749,0.11,1). Отдельный булев флаг isAmber первоначально равен false. При запуске установи отображаемый синий цвет. Только на фронте нажатия F, определяемом GetKeyDown(F), один раз инвертируй isAmber: при true выбирай янтарный, при false — исходный синий. Удержание F не вызывает новых переключений. Не используй изменяемый текущий цвет одновременно в качестве постоянного синего значения. Проверь последовательность: запуск — синий; первое отдельное нажатие — янтарный; второе — исходный синий.";
    static bool running;

    public static async void Run()
    {
        string requestedOutput = Environment.GetEnvironmentVariable("WW_MECHANIC_STATE_OUTPUT");
        if (!Application.isBatchMode || Environment.GetEnvironmentVariable("WW_MECHANIC_STATE_ACCEPTANCE") != "1" || string.IsNullOrWhiteSpace(requestedOutput))
            throw new InvalidOperationException("Disposable batch mode, WW_MECHANIC_STATE_ACCEPTANCE=1 and WW_MECHANIC_STATE_OUTPUT are required.");
        if (running) throw new InvalidOperationException("Mechanic acceptance already runs.");
        running = true;
        string output = requestedOutput, project = null, packageRoot = null;bool outputApproved=false;
        var checks = new JObject(); var failures = new JArray(); var captured = new JObject(); var drafts = new JArray();
        var report = new JObject {
            ["schemaVersion"] = 1, ["kind"] = "actual-main-chat-explicit-state-mechanic-draft", ["runner"] = typeof(MechanicStateAcceptanceRunner).FullName,
            ["startedUtc"] = DateTime.UtcNow.ToString("o"), ["unityVersion"] = Application.unityVersion, ["prompt"] = Prompt, ["userRequest"] = Prompt,
            ["guidanceDisclosure"] = new JObject { ["algorithmGuided"] = true, ["freshConversation"] = true, ["priorFailedAttempts"] = 3, ["promptSha256"] = Hash(Encoding.UTF8.GetBytes(Prompt)),
                ["scope"] = "Explicit boolean state algorithm supplied after earlier failed model attempts; not unassisted first-attempt success." },
            ["providerMode"] = "real-local-Ollama-inference", ["providerFactoryInjected"] = false, ["externalApiRequested"] = false,
            ["scriptReviewGranted"] = false, ["pendingPlansAppliedByRunner"] = 0, ["capturedReviewOnlyPlans"] = captured,
            ["drafts"] = drafts, ["checks"] = checks, ["failureReasons"] = failures, ["success"] = false,
            ["manualSemanticReviewRequired"] = true,
            ["limitations"] = new JArray("One fresh local-model conversation with an explicitly supplied state algorithm after three failed candidates; not unguided generation or a reliability estimate.",
                "Success means a retained review-only MonoBehaviour candidate with static mechanic indicators. It does not prove compilation or input/color behavior.",
                "No pending plan is approved; generated source is exported outside Assets only. Actual compilation and F-key/blue-amber-blue execution need a separate hash-reviewed run.")
        };
        ChatController controller = null; bool success = false; bool previousBackground = Application.runInBackground;
        var watch = Stopwatch.StartNew(); Dictionary<string, string> packageBefore = null, scriptsBefore = null;
        try
        {
            output=Path.GetFullPath(requestedOutput);project=Directory.GetParent(Application.dataPath).FullName;
            string disposableProject=Environment.GetEnvironmentVariable("WW_MECHANIC_STATE_DISPOSABLE_PROJECT");
            if(string.IsNullOrWhiteSpace(disposableProject)||Path.GetFullPath(disposableProject)!=Path.GetFullPath(project))throw new InvalidOperationException("Identify this exact disposable project with WW_MECHANIC_STATE_DISPOSABLE_PROJECT.");
            if(File.Exists(output))throw new IOException("Use a new output path; previous receipts are never overwritten.");
            if(IsWithin(output,Application.dataPath)||IsWithin(output,Path.Combine(project,"Packages")))throw new InvalidOperationException("Receipt/draft output must remain outside Assets and Packages to avoid imports.");
            outputApproved=true;
            packageRoot=ResolvePackageRoot();
            if(IsWithin(output,packageRoot)){outputApproved=false;throw new InvalidOperationException("Acceptance outputs must remain outside the resolved package source.");}
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,report.ToString());
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || ScriptDrafts.Pending)
                throw new InvalidOperationException("Wait for Edit Mode/import/compilation and previous reviewed code operations.");
            if (Path.GetFullPath("Assets").TrimEnd(Path.DirectorySeparatorChar) != Application.dataPath.TrimEnd(Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Working directory must be the disposable Unity project.");
            string model = Environment.GetEnvironmentVariable("WW_MECHANIC_STATE_MODEL") ?? "qwen2.5-coder:latest";
            if (model.StartsWith("ollama:", StringComparison.Ordinal)) model = model.Substring(7);
            model = OllamaModelDiscovery.CanonicalModelId(model) ?? throw new ArgumentException("Invalid local model ID.");
            var power = AssistantPower.Deep;
            string requestedPower = Environment.GetEnvironmentVariable("WW_MECHANIC_STATE_POWER");
            if (!string.IsNullOrEmpty(requestedPower) && (!Enum.TryParse(requestedPower, true, out power) || !Enum.IsDefined(typeof(AssistantPower), power))) throw new ArgumentException("WW_MECHANIC_STATE_POWER must be Fast, Balanced or Deep.");
            report["modelKey"] = "ollama:" + model; report["power"] = power.ToString();
            using(var localMetadata=new OllamaModelDiscovery())
            {
                var metadata=await localMetadata.RefreshAsync();var installed=metadata.Models.SingleOrDefault(m=>m.Id==model);
                if(!metadata.ServerAvailable||installed==null||!installed.CapabilitiesKnown||!installed.Completion||installed.Remote)throw new InvalidOperationException("Live metadata must confirm the exact installed non-remote completion model.");
                report["localMetadataModel"]=JObject.FromObject(installed);
            }
            packageBefore = Files(packageRoot, false); scriptsBefore = Files("Assets", true);
            string harnessPath="Assets/Editor/MechanicStateAcceptanceRunner.cs";
            if(!File.Exists(harnessPath))throw new FileNotFoundException("Install the public state-guided harness at the recorded standard Assets/Editor path.");
            string actualHarnessHash=Hash(File.ReadAllBytes(harnessPath));string expectedHarnessHash=Environment.GetEnvironmentVariable("WW_MECHANIC_STATE_HARNESS_SHA256");
            if(!string.IsNullOrEmpty(expectedHarnessHash)&&!string.Equals(expectedHarnessHash,actualHarnessHash,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Installed external harness bytes differ from the expected SHA256.");
            report["harnessSha256"]=actualHarnessHash;report["harnessSourcePath"]=harnessPath;
            report["source"] = new JObject { ["packageFiles"] = JObject.FromObject(packageBefore), ["fileCount"] = packageBefore.Count,
                ["packageRoot"] = packageRoot,["harnessSha256"]=actualHarnessHash,["harnessSourcePath"]=harnessPath,
                ["treeSha256"] = TreeHash(packageBefore), ["treeFormat"] = "Ordinal relative path + NUL + lowercase file SHA256 + LF, UTF-8; all package files except .DS_Store.",
                ["packageVersion"] = (string)JObject.Parse(File.ReadAllText(Path.Combine(packageRoot,"package.json")))["version"] };
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); Selection.objects = Array.Empty<Object>();
            Application.runInBackground = true;
            string history = Path.Combine(project, "UserSettings", "WorldWeaverChatAcceptance", Guid.NewGuid().ToString("N"));
            controller = new ChatController(historyFolder: history, modelConnectionsFolder: Path.Combine(history, "models"));
            // Local metadata only. RefreshModelsAsync would also contact the free-cloud catalog and is deliberately not used.
            await controller.RefreshLocalModelsAsync();
            var selected = controller.Models.SingleOrDefault(m => m.Key == "ollama:" + model);
            if (selected == null || !selected.Available || selected.Provider != ProviderKind.Ollama || !new Uri(selected.Endpoint).IsLoopback)
                throw new InvalidOperationException("Actual local metadata did not confirm an available completion model: " + model + ". " + controller.LocalDiscoveryStatus);
            controller.Settings.ModelKey = selected.Key; controller.Settings.Power = power; controller.Settings.Mode = AssistantMode.Execute;
            controller.Settings.PreferGeneratedScripts = true; controller.Settings.AutoApply = true; controller.SaveSettings();
            report["localModel"] = JObject.FromObject(selected); report["localDiscoveryStatus"] = controller.LocalDiscoveryStatus;
            report["sessionId"] = controller.Current.Id; report["historyFolder"] = history; report["sceneBefore"] = SceneToolExecutor.Context();
            checks["promptNeverAsksForCodeOrScript"] = !Regex.IsMatch(Prompt, @"(?i)скрипт|\bкод\b|\bC#|\bscript\b|\bcode\b");
            File.WriteAllText(output, report.ToString());
            var inference = Stopwatch.StartNew(); await controller.SendAsync(Prompt); inference.Stop();
            report["inferenceConversationMs"] = inference.ElapsedMilliseconds;
            var answer = controller.Current.Messages.LastOrDefault(m => m.Role == "assistant");
            report["sceneAfter"] = SceneToolExecutor.Context(); report["response"] = answer?.Text ?? ""; report["status"] = answer?.Status ?? "No assistant response";
            report["isError"] = answer?.IsError ?? true; report["attempts"] = answer == null ? new JArray() : JArray.FromObject(answer.Attempts);
            report["steps"] = answer == null ? new JArray() : JArray.FromObject(answer.Steps); report["pendingPlan"] = answer?.PendingPlan == null ? null : JToken.FromObject(answer.PendingPlan);
            report["inputTokens"] = answer?.InputTokens ?? 0; report["outputTokens"] = answer?.OutputTokens ?? 0;
            checks["actualAssistantResponse"] = answer != null; checks["assistantDidNotReportError"] = answer != null && !answer.IsError;
            checks["draftRemainsPendingForReview"] = answer?.PendingPlan?.actions?.Any(a => a.tool == "draft_script") == true;
            if (answer?.PendingPlan != null) captured[answer.Id] = JToken.FromObject(answer.PendingPlan);
            foreach (var action in answer?.PendingPlan?.actions?.Where(a => a.tool == "draft_script") ?? Enumerable.Empty<EditorCommand>())
            {
                ScriptDrafts.Validate(action); string code = (string)action.args["code"];
                string draftPath = Path.Combine(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output) + ".draft-" + (drafts.Count + 1) + "-" + action.name + ".cs.txt");
                byte[] bytes = new UTF8Encoding(false).GetBytes(code); File.WriteAllBytes(draftPath, bytes);
                // These checks intentionally do not claim semantic/running correctness. Independent review must bind the two colors and state transitions.
                var indicators = new JObject {
                    ["declaresExactMonoBehaviour"] = Regex.IsMatch(code, @"\bclass\s+" + Regex.Escape(action.name) + @"\s*:\s*(?:UnityEngine\.)?MonoBehaviour\b"),
                    ["namesFInput"] = Regex.IsMatch(code, @"KeyCode\s*\.\s*F\b|\bfKey\b|<Keyboard>/f"),
                    ["hasPressEdgeIndicator"] = Regex.IsMatch(code, @"\bGetKeyDown\s*\(|\bwasPressedThisFrame\b|\bperformed\s*\+="),
                    ["usesRendererOrMaterialPropertyBlock"] = Regex.IsMatch(code, @"\bRenderer\b|\bMeshRenderer\b|\bMaterialPropertyBlock\b"),
                    ["hasColorMutationIndicator"] = Regex.IsMatch(code, @"\.color\s*=|\bSetColor\s*\(|\bSetPropertyBlock\s*\(")
                };
                drafts.Add(new JObject { ["className"] = action.name, ["sourcePath"] = draftPath, ["sourceSha256"] = Hash(bytes), ["bytes"] = bytes.Length,
                    ["description"] = (string)action.args["description"] ?? "", ["staticMechanicIndicators"] = indicators,
                    ["allStaticIndicators"] = indicators.Properties().All(p => (bool)p.Value), ["imported"] = false });
            }
            checks["atLeastOnePurposefulMonoBehaviourCandidate"] = drafts.OfType<JObject>().Any(d => (bool)d["allStaticIndicators"]);
            checks["draftRetainedByteExactlyInHistory"] = answer?.PendingPlan != null && new ChatHistoryStore(history).Load().Any(s => s.Id == controller.Current.Id && s.Messages.Any(m => m.Id == answer.Id && m.PendingPlan != null && JToken.DeepEquals(JToken.FromObject(m.PendingPlan), captured[answer.Id])));
            checks["noGeneratedSourceImported"] = Same(scriptsBefore, Files("Assets", true));
            checks["noCompilationOrScriptImportPending"] = !EditorApplication.isCompiling && !ScriptDrafts.Pending;
            checks["packageSourceUnchanged"] = Same(packageBefore, Files(packageRoot, false));
            report["singleScriptCodeAcceptanceCompatible"] = answer?.PendingPlan?.actions?.Count == 1 && answer.PendingPlan.actions[0].tool == "draft_script";
            success = checks.Properties().All(p => p.Value.Type == JTokenType.Boolean && (bool)p.Value);
        }
        catch (Exception error) { report["error"] = ChatHistoryStore.Redact(error.ToString()); }
        finally
        {
            try
            {
                controller?.Dispose();Application.runInBackground=previousBackground;
                if(packageBefore!=null)checks["packageSourceUnchangedAtExit"]=Same(packageBefore,Files(packageRoot,false));
                if(scriptsBefore!=null)checks["sourceAssetsUnchangedAtExit"]=Same(scriptsBefore,Files("Assets",true));
            }
            catch(Exception error){report["cleanupError"]=ChatHistoryStore.Redact(error.ToString());success=false;}
            watch.Stop();report["sourceAssetsComparisonScope"]="Assets/**/*.cs only. Authorized scene/material generation is not counted as a source-code import.";
            foreach (var check in checks.Properties().Where(p => p.Value.Type != JTokenType.Boolean || !(bool)p.Value)) failures.Add(check.Name);
            success = success && failures.Count == 0; report["success"] = success; report["passed"] = success; report["ms"] = watch.ElapsedMilliseconds; report["finishedUtc"] = DateTime.UtcNow.ToString("o");
            try
            {
                if(!outputApproved)output=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"UserSettings","WorldWeaverAcceptanceFailures","mechanic-"+Guid.NewGuid().ToString("N")+".json");
                Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,ChatHistoryStore.Redact(report.ToString()));
            }
            catch(Exception error){UnityEngine.Debug.LogError("Cannot write mechanic acceptance failure receipt: "+ChatHistoryStore.Redact(error.ToString()));success=false;}
            finally{running=false;EditorApplication.Exit(success?0:1);}
        }
    }
    static Dictionary<string, string> Files(string root, bool scriptsOnly)
    {
        string physical=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if(!Directory.Exists(physical))throw new DirectoryNotFoundException("Acceptance source directory is absent: "+physical);
        return Directory.GetFiles(physical, scriptsOnly ? "*.cs" : "*", SearchOption.AllDirectories).Where(p => Path.GetFileName(p) != ".DS_Store").OrderBy(p => p, StringComparer.Ordinal)
            .ToDictionary(p => p.Substring(physical.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'), p => Hash(File.ReadAllBytes(p)), StringComparer.Ordinal);
    }
    static string ResolvePackageRoot()
    {
        var info=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SceneToolExecutor).Assembly);string path=info?.resolvedPath;
        if(string.IsNullOrWhiteSpace(path)||!Directory.Exists(path)||!File.Exists(Path.Combine(path,"package.json")))throw new DirectoryNotFoundException("Cannot resolve the physical WorldWeaver package source via PackageInfo.");
        return Path.GetFullPath(path);
    }
    static bool IsWithin(string path,string folder)=>Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.Ordinal);
    static bool Same(Dictionary<string, string> first, Dictionary<string, string> second) => first.Count == second.Count && first.All(p => second.TryGetValue(p.Key, out string hash) && hash == p.Value);
    static string TreeHash(Dictionary<string, string> files) => Hash(Encoding.UTF8.GetBytes(string.Concat(files.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "\0" + p.Value + "\n"))));
    static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
}
