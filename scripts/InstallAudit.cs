using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor;

/// <summary>Consumer-project audit, copied by verify_clean_install.py into a fresh Assets/Editor.</summary>
public static class InstallAudit
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string Digest(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
    public static void Run()
    {
        var result = new JObject { ["unity"] = Application.unityVersion, ["timestamp"] = DateTime.UtcNow.ToString("O") };
        int exit = 0;
        try
        {
            var original = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Existing project content"); EditorSceneManager.SaveScene(original, "Assets/Existing.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Existing.unity", false) };
            DemoBuilder.Create();
            string first = EditorSceneManager.GetActiveScene().path;
            new GameObject("Preserved developer addition"); EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            string originalHash = Digest(first);
            DemoBuilder.Create();
            string second = EditorSceneManager.GetActiveScene().path;
            Require(first != second, "Second demo overwrote first scene path.");
            Require(Digest(first) == originalHash, "Developer changes in first demo were lost.");
            var scenes = EditorBuildSettings.scenes;
            Require(scenes.Length == 3 && scenes[0].path == "Assets/Existing.unity" && !scenes[0].enabled && scenes[1].path == first && scenes[2].path == second, "Existing build scenes changed.");
            Require(UnityEngine.Object.FindObjectsByType<IntelligentNPC>(FindObjectsSortMode.None).Length == 2, "Two demo NPCs required.");
            var owner = UnityEngine.Object.FindFirstObjectByType<GeneratedTerrain>();
            Require(owner, "Generated terrain missing.");
            var data = owner.GetComponent<Terrain>().terrainData;
            Require(data.heightmapResolution == 129 && data.terrainLayers.Length == 3 && data.terrainLayers.All(layer => layer && layer.diffuseTexture), "Terrain assets are incomplete.");
            var before = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight).Cast<float>().ToArray();
            TerrainAuthoring.Regenerate(owner, new TerrainSpec { mountainStrength = 0, valleyDepth = 0, height = 20 });
            var after = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight).Cast<float>().ToArray();
            Require(!before.SequenceEqual(after), "Regenerate retained stale texture weights.");
            var npc = GameObject.Find("Merchant").GetComponent<IntelligentNPC>();
            Require(npc.Models.Dialogue.Model == "qwen2.5-coder:latest" && npc.Models.Terrain.Model == "qwen3.5:2b", "Incorrect task routes.");
            Require(npc.Knowledge.Documents.Count == 11 && npc.GetComponent<Inventory>().Count("Crystal") == 10, "Demo configuration incomplete.");
            result["second_demo_has_unique_path"] = true; result["first_demo_preserved"] = true; result["build_scene_order_and_enabled_flags_preserved"] = true;
            result["two_npcs"] = true; result["terrain_assets_resolved"] = true; result["regenerate_updates_alphamap"] = true; result["task_routes_and_knowledge_valid"] = true;
            result["first_scene"] = first; result["second_scene"] = second; result["success"] = true;
        }
        catch (Exception error) { result["success"] = false; result["error"] = error.GetType().Name + ": " + error.Message; exit = 1; }
        string output = Environment.GetEnvironmentVariable("WW_AUDIT_OUTPUT");
        if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, result.ToString(Formatting.Indented));
        Debug.Log("WorldWeaver consumer audit: " + result["success"]);
        EditorApplication.Exit(exit);
    }
}
