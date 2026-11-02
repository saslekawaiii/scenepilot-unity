using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Addressed retrieval over the live scene and imported asset database; no full-project prompt dump.</summary>
    public static class ProjectContextIndex
    {
        public static IEnumerable<GameObject> Objects(bool allLoadedScenes = false)
        {
            var active = SceneManager.GetActiveScene();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || (!allLoadedScenes && scene != active)) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var tr in root.GetComponentsInChildren<Transform>(true)) yield return tr.gameObject;
            }
        }
        public static string StableTarget(GameObject go)
        {
            var marker = go.GetComponent<GeneratedSceneObject>();
            if (marker)
            {
                // Existing ownership identifiers persist in serialized scene data, including an unsaved scene.
                var serialized = new SerializedObject(marker);
                var id = serialized.FindProperty("ObjectId");
                if (id != null && !string.IsNullOrEmpty(id.stringValue)) return "owned:" + id.stringValue;
            }
            if (!string.IsNullOrEmpty(go.scene.path)) return "global:" + GlobalObjectId.GetGlobalObjectIdSlow(go);
            return "id:" + go.GetInstanceID();
        }
        public static GameObject ResolveStable(string target)
        {
            if (target.StartsWith("global:", StringComparison.Ordinal))
            {
                if (!GlobalObjectId.TryParse(target.Substring(7), out var id)) throw new ArgumentException("Invalid GlobalObjectId.");
                var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
                if (!obj || obj.scene != SceneManager.GetActiveScene()) throw new ArgumentException("Object is not in the active loaded scene.");
                return obj;
            }
            if (target.StartsWith("owned:", StringComparison.Ordinal))
            {
                var matches = Objects().Where(go => StableTarget(go) == target).Take(2).ToArray();
                if (matches.Length != 1)
                {
                    var objects=Objects().ToArray();
                    var candidates=objects.Where(go=>go.GetComponent<GeneratedSceneObject>()).Select(go=>new{Go=go,Id=StableTarget(go)})
                        .OrderByDescending(row=>row.Id.Zip(target,(a,b)=>a==b).TakeWhile(equal=>equal).Count()).ThenBy(row=>row.Go.name,StringComparer.Ordinal).Take(5)
                        .Select(row=>new JObject{["target"]=row.Id,["name"]=row.Go.name,["uniqueNameTarget"]=objects.Count(go=>go.name==row.Go.name)==1?"@"+row.Go.name:null});
                    throw new ArgumentException("Owned identifier must resolve uniquely in the active scene. Rejected target: "+target+". No fuzzy resolution was performed. Choose a full actual target below or find_scene_objects for another object. Candidate data: "+new JArray(candidates).ToString(Newtonsoft.Json.Formatting.None));
                }
                return matches[0];
            }
            return null;
        }
        public static string HierarchyPath(GameObject go)
        {
            var names = new List<string>();
            for (var tr = go.transform; tr; tr = tr.parent) names.Add(tr.name);
            names.Reverse(); return string.Join("/", names);
        }
        public static JObject Entry(GameObject go) => new JObject {
            ["target"] = StableTarget(go), ["temporaryTarget"] = "id:" + go.GetInstanceID(),
            ["name"] = go.name, ["path"] = HierarchyPath(go), ["scene"] = go.scene.path,
            ["position"] = new JArray(go.transform.position.x, go.transform.position.y, go.transform.position.z),
            ["components"] = new JArray(go.GetComponents<Component>().Where(c=>c).Select(c=>c.GetType().Name)),
            ["kind"] = go.GetComponent<GeneratedSceneObject>()?.Kind
        };
        public static JObject Search(string query, int limit = 20)
        {
            if (query == null || query.Length > 500 || limit < 1 || limit > 50) throw new ArgumentException("Search query <=500 characters, limit1..50.");
            var words = query.ToLowerInvariant().Split(new[]{' ', '/', '\\', '.', ',', ';'}, StringSplitOptions.RemoveEmptyEntries);
            var rows = Objects(true).Select(go => new { Go=go, Text=(HierarchyPath(go)+" "+go.GetComponent<GeneratedSceneObject>()?.Kind+" "+go.GetComponent<WorldPlace>()?.Key+" "+go.GetComponent<IntelligentNPC>()?.Role+" "+string.Join(" ",go.GetComponents<Component>().Where(c=>c).Select(c=>c.GetType().Name))).ToLowerInvariant() })
                .Select(v=>new {v.Go, Score=words.Sum(w=>v.Text.Contains(w)?1:0)})
                .Where(v=>words.Length==0||v.Score==words.Length).OrderByDescending(v=>v.Score).ThenBy(v=>HierarchyPath(v.Go),StringComparer.Ordinal).ToList();
            var assets = AssetDatabase.FindAssets(query, new[]{"Assets","Packages"}).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p=>!AssetDatabase.IsValidFolder(p)).OrderBy(p=>p,StringComparer.Ordinal).Take(limit)
                .Select(path=>new JObject{["path"]=path,["guid"]=AssetDatabase.AssetPathToGUID(path),["type"]=AssetDatabase.GetMainAssetTypeAtPath(path)?.Name});
            return new JObject{["sceneMatches"]=rows.Count,["sceneObjects"]=new JArray(rows.Take(limit).Select(v=>Entry(v.Go))),["assets"]=new JArray(assets),["limit"]=limit};
        }
        public static JObject Summary() => new JObject {
            ["unityVersion"]=Application.unityVersion,["platform"]=EditorUserBuildSettings.activeBuildTarget.ToString(),
            ["loadedScenes"]=new JArray(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i)).Where(s=>s.isLoaded).Select(s=>new JObject{["name"]=s.name,["path"]=s.path,["dirty"]=s.isDirty})),
            ["assetCount"]=AssetDatabase.GetAllAssetPaths().Count(p=>p.StartsWith("Assets/",StringComparison.Ordinal)),
            ["buildScenes"]=new JArray(EditorBuildSettings.scenes.Select(s=>new JObject{["path"]=s.path,["enabled"]=s.enabled})),
            ["packages"]=new JArray(UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Select(p=>new JObject{["name"]=p.name,["version"]=p.version})),
            ["savedObjectReferences"]="global IDs for saved scene objects; ownership GUIDs for generated objects. Unsaved foreign objects remain temporary and are labelled as such."
        };
    }
}
