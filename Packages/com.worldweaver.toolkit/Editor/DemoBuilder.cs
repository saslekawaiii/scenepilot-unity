using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace WorldWeaver.Editor
{
    public static class DemoBuilder
    {
        [MenuItem("Tools/WorldWeaver/Create Demo in New Scene")]
        public static void Create()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PlayerSettings.runInBackground = true;
            const string folder = "Assets/WorldWeaverDemo"; Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var terrain = TerrainAuthoring.Create(new TerrainSpec(), folder);
            var local = Profile(folder, "Local", ProviderKind.Ollama, "http://127.0.0.1:11434", "qwen3.5:2b", "");
            var reasoned = Profile(folder, "LocalDialogue", ProviderKind.Ollama, "http://127.0.0.1:11434", "qwen2.5-coder:latest", "");
            var cloud = Profile(folder, "FreeCloud", ProviderKind.Pollinations, "https://text.pollinations.ai", "openai-fast", ""); cloud.StrictSchema = false; cloud.TimeoutSeconds = 120; cloud.Retries = 2;
            var groq = Profile(folder, "Groq", ProviderKind.Groq, "https://api.groq.com/openai/v1", "openai/gpt-oss-120b", "GROQ_API_KEY");
            var localRoutes = Routes(folder, "LocalRoutes", local, reasoned, reasoned);
            var cloudRoutes = Routes(folder, "CloudRoutes", local, cloud, reasoned);
            var knowledge = ScriptableObject.CreateInstance<KnowledgeBase>();
            knowledge.Documents = VillageKnowledge.Documents(); Save(knowledge, folder + "/VillageKnowledge.asset");
            var plaza = Shape("Market Square", PrimitiveType.Cube, new Vector3(28, 14, -25), new Vector3(35, 1, 28), new Color(.54f, .49f, .38f), folder);
            var nav = new GameObject("Demo NavMesh").AddComponent<DemoNavigation>(); nav.Surfaces = new[] { plaza.GetComponent<MeshFilter>() };
            var player = Shape("Player", PrimitiveType.Capsule, new Vector3(25, 15.5f, -27), Vector3.one, new Color(.24f, .54f, .7f), folder);
            var playerEntity = player.AddComponent<WorldEntity>(); playerEntity.Id = "Player"; var playerInventory = player.AddComponent<Inventory>(); playerInventory.Add("Coin", 20);
            var merchant = Shape("Merchant", PrimitiveType.Capsule, new Vector3(23, 15.5f, -25), Vector3.one, new Color(.76f, .43f, .24f), folder);
            var merchantEntity = merchant.AddComponent<WorldEntity>(); merchantEntity.Id = "Merchant";
            var agent = merchant.AddComponent<NavMeshAgent>(); agent.speed = 3; agent.stoppingDistance = .8f; agent.radius = .4f;
            var npc = merchant.AddComponent<IntelligentNPC>(); npc.Models = localRoutes; npc.Knowledge = knowledge; npc.GetComponent<Inventory>().Add("Crystal", 10);
            Shape("House Building", PrimitiveType.Cube, new Vector3(37, 17, -17), new Vector3(7, 5, 5), new Color(.76f, .66f, .48f), folder);
            var roof = Shape("House Roof", PrimitiveType.Cube, new Vector3(37, 20, -17), new Vector3(8, 1.2f, 6), new Color(.32f, .28f, .25f), folder); roof.transform.rotation = Quaternion.Euler(0, 0, 8);
            var house = new GameObject("House Interaction Point"); house.transform.position = new Vector3(34, 14.5f, -21); var houseEntity = house.AddComponent<WorldEntity>(); houseEntity.Id = "House"; houseEntity.Interactable = true;
            var well = Shape("Well", PrimitiveType.Cylinder, new Vector3(17, 15.2f, -17), new Vector3(2, .7f, 2), Color.gray, folder); var wellEntity = well.AddComponent<WorldEntity>(); wellEntity.Id = "Well"; wellEntity.Interactable = true;
            var guard = Shape("Guard", PrimitiveType.Capsule, new Vector3(36, 15.5f, -33), Vector3.one, new Color(.34f, .43f, .36f), folder);
            var guardEntity = guard.AddComponent<WorldEntity>(); guardEntity.Id = "Guard";
            var guardNpc = guard.AddComponent<IntelligentNPC>(); guardNpc.CharacterId = "guard"; guardNpc.NpcName = "Лада"; guardNpc.Role = "Страж"; guardNpc.Personality = "Спокойная и дисциплинированная"; guardNpc.Goals = "Охранять деревню"; guardNpc.Knowledge = knowledge; guardNpc.Models = localRoutes; guardNpc.AllowedActions = new[] { "Talk", "Wait" };
            npc.KnownEntities = new List<WorldEntity> { playerEntity, houseEntity, wellEntity, guardEntity }; guardNpc.KnownEntities = new List<WorldEntity> { playerEntity, merchantEntity };
            Shape("Crystal Display", PrimitiveType.Sphere, new Vector3(21, 15.3f, -25), new Vector3(.6f, 1, .6f), new Color(.15f, .87f, .87f), folder);
            var sunlight = new GameObject("Sun").AddComponent<Light>(); sunlight.type = LightType.Directional; sunlight.intensity = 1.2f; sunlight.transform.rotation = Quaternion.Euler(45, -35, 0); sunlight.shadows = LightShadows.Soft;
            RenderSettings.ambientLight = new Color(.62f, .69f, .74f); RenderSettings.fog = true; RenderSettings.fogColor = new Color(.57f, .68f, .72f); RenderSettings.fogDensity = .002f;
            var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera"; camera.transform.position = new Vector3(50, 58, -78); camera.transform.LookAt(new Vector3(18, 11, -12)); camera.fieldOfView = 53; camera.backgroundColor = new Color(.55f, .7f, .78f); camera.clearFlags = CameraClearFlags.SolidColor;
            var ui = new GameObject("Dialogue").AddComponent<DemoDialogue>(); ui.Merchant = npc; ui.PlayerInventory = playerInventory; ui.LocalRoutes = localRoutes; ui.CloudRoutes = cloudRoutes;
            string scenePath = AssetDatabase.GenerateUniqueAssetPath(folder + "/WorldWeaverDemo.unity");
            EditorSceneManager.SaveScene(scene, scenePath);
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            buildScenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray(); AssetDatabase.SaveAssets();
            WorldWeaverWindow.ShowWindow(); Selection.activeGameObject = merchant;
        }
        private static GameObject Shape(string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color, string folder)
        {
            var obj = GameObject.CreatePrimitive(type); obj.name = name; obj.transform.position = position; obj.transform.localScale = scale;
            var material = new Material(Shader.Find("Standard")) { color = color }; Save(material, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name.Replace(" ", "") + ".mat")); obj.GetComponent<Renderer>().sharedMaterial = material; return obj;
        }
        private static ModelProfile Profile(string folder, string name, ProviderKind kind, string endpoint, string model, string env)
        { var p = ScriptableObject.CreateInstance<ModelProfile>(); p.Provider = kind; p.Endpoint = endpoint; p.Model = model; p.ApiKeyEnvironmentVariable = env; Save(p, folder + "/" + name + ".asset"); return p; }
        private static OrchestratorProfile Routes(string folder, string name, ModelProfile terrain, ModelProfile dialogue, ModelProfile planning)
        { var p = ScriptableObject.CreateInstance<OrchestratorProfile>(); p.Terrain = terrain; p.Dialogue = dialogue; p.Planning = planning; Save(p, folder + "/" + name + ".asset"); return p; }
        private static void Save(Object asset, string path) { AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(path)); }
    }
}
