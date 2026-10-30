using System.IO;
using UnityEditor;
using UnityEngine;

namespace WorldWeaver.Editor
{
    public static class TerrainAuthoring
    {
        /// <summary>Creates a new owned terrain. Existing scene objects are never replaced.</summary>
        public static Terrain Create(TerrainSpec spec, string folder = "Assets/WorldWeaverGenerated")
        {
            spec.Validate(); Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var data = new TerrainData { heightmapResolution = spec.resolution, size = new Vector3(spec.width, spec.height, spec.length) };
            data.SetHeights(0, 0, TerrainGenerator.Generate(spec));
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/Terrain.asset"); AssetDatabase.CreateAsset(data, path);
            var go = Terrain.CreateTerrainGameObject(data); go.name = "WorldWeaver Terrain " + spec.seed;
            Undo.RegisterCreatedObjectUndo(go, "Create WorldWeaver Terrain");
            go.transform.position = new Vector3(-spec.width / 2, 0, -spec.length / 2);
            go.AddComponent<GeneratedTerrain>().Specification = spec;
            var terrain = go.GetComponent<Terrain>(); terrain.drawInstanced = true;
            var layers = new TerrainLayer[3];
            Color[] colors = { new Color(.21f, .34f, .23f), new Color(.39f, .40f, .37f), new Color(.88f, .91f, .91f) };
            for (int i = 0; i < 3; i++)
            {
                var texture = new Texture2D(4, 4); var pixels = new Color[16]; for (int j = 0; j < pixels.Length; j++) pixels[j] = colors[i]; texture.SetPixels(pixels); texture.Apply();
                AssetDatabase.CreateAsset(texture, AssetDatabase.GenerateUniqueAssetPath(folder + "/Biome" + i + ".asset"));
                layers[i] = new TerrainLayer { diffuseTexture = texture, tileSize = new Vector2(12, 12) };
                AssetDatabase.CreateAsset(layers[i], AssetDatabase.GenerateUniqueAssetPath(folder + "/Layer" + i + ".terrainlayer"));
            }
            data.terrainLayers = layers;
            ApplyAlphamap(data, spec);
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane); water.name = "Water"; Undo.RegisterCreatedObjectUndo(water, "Create water");
            water.transform.SetParent(go.transform); water.transform.localPosition = new Vector3(spec.width / 2, spec.waterLevel * spec.height, spec.length / 2);
            water.transform.localScale = new Vector3(spec.width / 10, 1, spec.length / 10);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            var material = new Material(Shader.Find("Standard")) { color = new Color(.13f, .42f, .52f) };
            material.SetFloat("_Glossiness", .88f); AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(folder + "/Water.mat")); water.GetComponent<Renderer>().sharedMaterial = material;
            AssetDatabase.SaveAssets(); Selection.activeGameObject = go; return terrain;
        }
        private static void ApplyAlphamap(TerrainData data, TerrainSpec spec)
        {
            // Custom layers authored by a consumer retain their own texture policy.
            if (data.terrainLayers.Length != 3) return;
            int n = 64; data.alphamapResolution = n; var alpha = new float[n, n, 3];
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
            {
                float h = data.GetInterpolatedHeight(x / (float)(n - 1), z / (float)(n - 1)) / spec.height;
                float snow = Mathf.Clamp01((h - .7f) * 5), rock = Mathf.Clamp01((h - .38f) * 3) * (1 - snow);
                alpha[z, x, 0] = 1 - rock - snow; alpha[z, x, 1] = rock; alpha[z, x, 2] = snow;
            }
            data.SetAlphamaps(0, 0, alpha);
        }
        public static void Regenerate(GeneratedTerrain owned, TerrainSpec spec)
        {
            spec.Validate(); var terrain = owned.GetComponent<Terrain>(); if (!terrain) return;
            Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Regenerate Terrain");
            Undo.RegisterCompleteObjectUndo(terrain.terrainData.alphamapTextures, "Regenerate Terrain textures");
            Undo.RecordObject(owned, "Update TerrainSpec"); Undo.RecordObject(owned.transform, "Resize Terrain");
            terrain.terrainData.heightmapResolution = spec.resolution; terrain.terrainData.size = new Vector3(spec.width, spec.height, spec.length);
            terrain.terrainData.SetHeights(0, 0, TerrainGenerator.Generate(spec));
            ApplyAlphamap(terrain.terrainData, spec);
            owned.Specification = spec; owned.transform.position = new Vector3(-spec.width / 2, 0, -spec.length / 2);
            var water = owned.transform.Find("Water");
            if (water) { Undo.RecordObject(water, "Resize water"); water.localPosition = new Vector3(spec.width / 2, spec.waterLevel * spec.height, spec.length / 2); water.localScale = new Vector3(spec.width / 10, 1, spec.length / 10); }
            EditorUtility.SetDirty(terrain.terrainData); EditorUtility.SetDirty(owned);
        }
    }
}
