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
            var result=new JObject{["sceneMatches"]=rows.Count,["sceneObjects"]=new JArray(rows.Take(limit).Select(v=>Entry(v.Go))),["assets"]=new JArray(assets),["limit"]=limit};
            if(rows.Count==0){result["queryHint"]="Scene search is literal AND-token matching in actual names/components; it does not translate languages. Do not repeat English synonyms for Russian names. Use list_scene_objects with component/kind, or inspect this actual active-scene inventory.";result["activeSceneInventory"]=ListScene(new JObject{["limit"]=12});}
            return result;
        }
        public static JObject ListScene(JObject args)
        {
            string type=(string)args["component"],kind=(string)args["kind"],parent=(string)args["parent"];
            int limit=(int?)args["limit"]??30,offset=(int?)args["offset"]??0;
            if(limit<1||limit>100||offset<0||offset>20000)throw new ArgumentException("Scene page requires limit1..100 and offset0..20000.");
            GameObject ancestor=string.IsNullOrEmpty(parent)?null:SceneToolExecutor.ResolveTarget(parent);
            bool HasType(Component c){for(var t=c.GetType();t!=null;t=t.BaseType)if(string.Equals(t.Name,type,StringComparison.OrdinalIgnoreCase)||string.Equals(t.FullName,type,StringComparison.OrdinalIgnoreCase))return true;return false;}
            var all=Objects(true).Where(g=>g.scene==SceneManager.GetActiveScene()).Where(g=>!ancestor||g==ancestor||g.transform.IsChildOf(ancestor.transform))
                .Where(g=>string.IsNullOrEmpty(kind)||string.Equals(g.GetComponent<GeneratedSceneObject>()?.Kind,kind,StringComparison.OrdinalIgnoreCase))
                .Where(g=>string.IsNullOrEmpty(type)?g.GetComponents<Component>().Any(c=>c&&!(c is Transform)&&!(c is GeneratedSceneObject)):g.GetComponents<Component>().Any(c=>c&&HasType(c)))
                .OrderByDescending(g=>g.GetComponent<Terrain>()||g.GetComponent<WorldPlayer>()).ThenByDescending(g=>g.GetComponent<Renderer>()!=null).ThenBy(g=>HierarchyPath(g),StringComparer.Ordinal).ToArray();
            // The cursor must cover only rows actually delivered to the model.
            // Item-only pages could exceed the tool-context budget and silently
            // skip most objects when a 50 KB page was clipped to 14 KB.
            const int pageCharacters=12000;int used=0;var rows=new JArray();
            foreach(var g in all.Skip(offset).Take(limit))
            {
                var row=Entry(g);var renderer=g.GetComponent<Renderer>();
                if(renderer){row["boundsSize"]=JArray.FromObject(new[]{renderer.bounds.size.x,renderer.bounds.size.y,renderer.bounds.size.z});row["materials"]=new JArray(renderer.sharedMaterials.Where(m=>m).Select(m=>new JObject{["name"]=m.name,["asset"]=AssetDatabase.GetAssetPath(m),["color"]=m.HasProperty("_BaseColor")?m.GetColor("_BaseColor").ToString():m.HasProperty("_Color")?m.color.ToString():null}));}
                row["childCount"]=g.transform.childCount;
                int cost=row.ToString(Newtonsoft.Json.Formatting.None).Length+1;
                if(cost>pageCharacters)
                {
                    string name=g.name;row=new JObject{["target"]=row["target"],["name"]=name.Substring(0,Math.Min(name.Length,160)),["kind"]=row["kind"],["listingDetailsOmitted"]=true,["detailHint"]="Inspect this actual target for full object details."};cost=row.ToString(Newtonsoft.Json.Formatting.None).Length+1;
                }
                if(rows.Count>0&&used+cost>pageCharacters)break;
                rows.Add(row);used+=cost;
            }
            int next=offset+rows.Count;
            return new JObject{["total"]=all.Length,["offset"]=offset,["returned"]=rows.Count,["nextOffset"]=next<all.Length?(JToken)next:JValue.CreateNull(),["objects"]=rows};
        }
        public static JObject TerrainFacts(Terrain terrain)
        {
            var data=terrain.terrainData;if(!data)return new JObject{["target"]=StableTarget(terrain.gameObject),["dataMissing"]=true};
            float min=float.PositiveInfinity,max=float.NegativeInfinity;
            for(int y=0;y<=8;y++)for(int x=0;x<=8;x++){float height=data.GetInterpolatedHeight(x/8f,y/8f);min=Mathf.Min(min,height);max=Mathf.Max(max,height);}
            var collider=terrain.GetComponent<TerrainCollider>();var environment=terrain.GetComponent<WorldEnvironment>()??terrain.GetComponentInParent<WorldEnvironment>();
            return new JObject{["target"]=StableTarget(terrain.gameObject),["name"]=terrain.name,["dataAsset"]=AssetDatabase.GetAssetPath(data),["position"]=JArray.FromObject(new[]{terrain.transform.position.x,terrain.transform.position.y,terrain.transform.position.z}),["size"]=JArray.FromObject(new[]{data.size.x,data.size.y,data.size.z}),["heightmapResolution"]=data.heightmapResolution,["sampledMinHeight"]=min,["sampledMaxHeight"]=max,["sampleGrid"]=9,["colliderMatches"]=collider&&collider.enabled&&collider.terrainData==data,["heightmapSource"]=environment?environment.HeightmapSource:null};
        }
        public static JArray TerrainFacts()=>new JArray(Objects().Select(g=>g.GetComponent<Terrain>()).Where(t=>t).Take(8).Select(TerrainFacts));
        public static JObject Summary() => new JObject {
            ["unityVersion"]=Application.unityVersion,["platform"]=EditorUserBuildSettings.activeBuildTarget.ToString(),
            ["inputBackend"]=WorldInput.Backend,
            ["loadedScenes"]=new JArray(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i)).Where(s=>s.isLoaded).Select(s=>new JObject{["name"]=s.name,["path"]=s.path,["dirty"]=s.isDirty})),
            ["assetCount"]=AssetDatabase.GetAllAssetPaths().Count(p=>p.StartsWith("Assets/",StringComparison.Ordinal)),
            ["buildScenes"]=new JArray(EditorBuildSettings.scenes.Select(s=>new JObject{["path"]=s.path,["enabled"]=s.enabled})),
            ["packages"]=new JArray(UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Select(p=>new JObject{["name"]=p.name,["version"]=p.version})),
            ["savedObjectReferences"]="global IDs for saved scene objects; ownership GUIDs for generated objects. Unsaved foreign objects remain temporary and are labelled as such."
        };
    }
}
