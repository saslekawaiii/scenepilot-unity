using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WorldWeaver;
using Object = UnityEngine.Object;

/// <summary>Explicit live-inference configuration fixture; clones routes/profiles in memory only.</summary>
[InitializeOnLoad]
public static class NativeNpcRuntimeProfileQaMenu
{
    sealed class Session { public IntelligentNPC Npc; public OrchestratorProfile Original, Applied; public readonly List<ScriptableObject> Owned = new List<ScriptableObject>(); public string Id, Output, SourceSha; }
    static readonly List<Session> sessions = new List<Session>();
    static NativeNpcRuntimeProfileQaMenu()
    {
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode) Cleanup(false); };
        EditorApplication.update += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode && sessions.Count > 0) Cleanup(false); };
        AssemblyReloadEvents.beforeAssemblyReload += () => Cleanup(true);
    }
    [MenuItem("WorldWeaver/Acceptance/QA NPC Runtime Profile/9B 512 tokens 150s no retries")]
    public static void ApplyKnownProfile()
    {
        Require(Application.isPlaying && !Application.isBatchMode, "Use an idle, open actual conversation in GUI Play Mode.");
        var players = Object.FindObjectsByType<WorldPlayer>(FindObjectsSortMode.None).Where(p => p.isActiveAndEnabled && p.Conversation && p.Conversation.IsOpen).ToArray();
        Require(players.Length == 1, "Exactly one actual WorldPlayer conversation must already be open.");
        var ui = players[0].Conversation; var npc = ui.ActiveNpc; var input = ui.View?.Q<TextField>("conversation-input");
        Require(npc && !npc.Busy && npc.PlayerConversationActive && input != null && input.enabledInHierarchy && string.IsNullOrEmpty(input.value), "The open conversation must be idle, enabled and have an empty input; no text will be cleared by this fixture.");
        Require(!sessions.Any(s => ReferenceEquals(s.Npc, npc)), "The QA profile is already active for this NPC; stop Play Mode to restore it.");
        var original = npc.Models; Require(original && original.Dialogue, "The actual NPC needs an authored dialogue route.");
        var endpoint = new Uri(original.Dialogue.Endpoint); Require(original.Dialogue.Provider == ProviderKind.Ollama && endpoint.IsLoopback, "This fixture only configures the existing local Ollama connection.");
        var session = new Session { Npc = npc, Original = original, Id = Guid.NewGuid().ToString("N"), Output = OutputDirectory(), SourceSha = Hash(File.ReadAllBytes(Path.GetFullPath(SourcePath()))) };
        var before = Profile(original.Dialogue); bool rag = npc.UseRag; int history = npc.ConversationHistory.Count;
        try
        {
            session.Applied = Clone(original, session); session.Applied.name = "WorldWeaver QA runtime-only routes";
            // Separate clones prevent a shared original Dialogue/Planning/Terrain asset from being modified indirectly.
            session.Applied.Terrain = original.Terrain ? Clone(original.Terrain, session) : null;
            session.Applied.Planning = original.Planning ? Clone(original.Planning, session) : null;
            var profile = Clone(original.Dialogue, session); session.Applied.Dialogue = profile;
            profile.Model = "qwen3.5:9b"; profile.MaxTokens = 512; profile.TimeoutSeconds = 150; profile.Retries = 0; profile.Temperature = .25f; profile.ContextWindow = 8192; profile.StrictSchema = true; profile.Validate();
            // The existing cached ModelRouter holds the old routes object. ReleaseServices preserves memory;
            // the next real NPC request lazily creates its normal real router/embedding provider from these clones.
            npc.ReleaseServices(); npc.Models = session.Applied; sessions.Add(session);
            Write(session, new JObject { ["event"] = "applied", ["profileBefore"] = before, ["profileAfter"] = Profile(profile), ["ragBefore"] = rag, ["ragAfter"] = npc.UseRag,
                ["historyCountBefore"] = history, ["historyCountAfter"] = npc.ConversationHistory.Count, ["ownedRuntimeObjectCount"] = session.Owned.Count,
                ["embeddingModelUnchanged"] = session.Applied.EmbeddingModel == original.EmbeddingModel, ["embeddingEndpointUnchanged"] = session.Applied.EmbeddingEndpoint == original.EmbeddingEndpoint,
                ["modelsReferenceIsOwnedClone"] = npc.Models == session.Applied, ["persistentAssetsWritten"] = false, ["inferenceRequestedByFixture"] = false, ["uiOpenedOrTextChangedByFixture"] = false,
                ["scope"] = "Actual inference QA configuration only: the next native user send uses the normal real pipeline. RAG, knowledge, memory, conversation and non-dialogue route settings remain unchanged. Cleanup restores the original route reference and destroys owned clones after requests settle or on domain reload." });
            Debug.Log("Runtime-only NPC profile QA applied: qwen3.5:9b,512 tokens,150s,retries0,temp0.25,context8192; RAG unchanged.");
        }
        catch
        {
            if (npc && !npc.Busy) { npc.ReleaseServices(); if (npc.Models == session.Applied) npc.Models = original; }
            sessions.Remove(session); DestroyOwned(session); throw;
        }
    }
    [MenuItem("WorldWeaver/Acceptance/QA NPC Runtime Profile/9B 512 tokens 150s no retries", true)]
    static bool Enabled() => Application.isPlaying && !Application.isBatchMode;
    static T Clone<T>(T source, Session session) where T : ScriptableObject { var copy = Object.Instantiate(source); copy.hideFlags = HideFlags.HideAndDontSave; session.Owned.Add(copy); return copy; }
    static JObject Profile(ModelProfile p) => new JObject { ["provider"] = p.Provider.ToString(), ["model"] = p.Model, ["maxTokens"] = p.MaxTokens, ["timeoutSeconds"] = p.TimeoutSeconds, ["retries"] = p.Retries, ["temperature"] = p.Temperature, ["contextWindow"] = p.ContextWindow, ["strictSchema"] = p.StrictSchema };
    static void Cleanup(bool domainReload)
    {
        foreach (var session in sessions.ToArray())
        {
            // Stopping Play cancels NPC work. Do not destroy a profile still owned by a settling real request.
            if (!domainReload && !ReferenceEquals(session.Npc, null) && session.Npc.Busy) continue;
            try
            {
                if (session.Npc) { session.Npc.ReleaseServices(); if (session.Npc.Models == session.Applied) session.Npc.Models = session.Original; }
                DestroyOwned(session); Write(session, new JObject { ["event"] = "cleaned", ["domainReload"] = domainReload, ["allOwnedClonesDestroyed"] = session.Owned.All(o => !o), ["persistentAssetsWritten"] = false });
            }
            catch (Exception error) { Debug.LogWarning("Runtime NPC QA cleanup: " + error.GetType().Name); }
            finally { sessions.Remove(session); }
        }
    }
    static void DestroyOwned(Session session) { foreach (var item in session.Owned.AsEnumerable().Reverse()) if (item) Object.DestroyImmediate(item); }
    static void Write(Session session, JObject report)
    {
        report["kind"] = "explicit-runtime-only-native-npc-profile-qa"; report["recordedUtc"] = DateTime.UtcNow.ToString("o"); report["sessionId"] = session.Id; report["sourceSha256"] = session.SourceSha; report["unity"] = Application.unityVersion;
        Directory.CreateDirectory(session.Output); string file = Path.Combine(session.Output, "native-npc-profile-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
        using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write)) using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(report.ToString());
    }
    static string OutputDirectory()
    {
        string output = Environment.GetEnvironmentVariable("WW_NATIVE_UI_OUTPUT_DIR"); var project = Directory.GetParent(Application.dataPath);
        if (string.IsNullOrWhiteSpace(output)) { Require(project?.Name == "UnityProject" && project.Parent != null && Directory.Exists(Path.Combine(project.Parent.FullName, "results")), "Set WW_NATIVE_UI_OUTPUT_DIR outside project Assets/Packages."); output = Path.Combine(project.Parent.FullName, "results/screenshots-1.3"); }
        output = Path.GetFullPath(output); Require(!Under(output, Application.dataPath) && !Under(output, Path.Combine(project.FullName, "Packages")), "QA receipts must remain outside Assets/Packages."); return output;
    }
    static bool Under(string path, string root) => path == root || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    static string SourcePath([CallerFilePath] string path = "") => path;
    static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
