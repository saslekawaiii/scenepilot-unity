using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Allowlisted editor tools. Entire plans are checked before the first scene or asset mutation.</summary>
    public static class SceneToolExecutor
    {
        private const int BatchLimit = 600, SceneLimit = 2000;
        private static readonly string[] TerrainKeys = typeof(TerrainSpec).GetFields().Select(f => f.Name).ToArray();
        private static readonly string[] Kinds = { "cube", "sphere", "capsule", "cylinder", "plane", "tree", "rock", "house" };
        public static string ToolInstructions => @"You edit only the active scene. Never say work was done before tool results. Each action is {tool,name,target,args}. All args are STRICT: unknown keys are rejected. Numbers must be finite. Use target id:<instanceId> from context; @Name refers to an earlier creation in this plan; exact unique scene names and selected also work. Use unique nonempty name on creations. New objects live under the owned WorldWeaverGenerated group. Existing objects are editable only by explicit target. Delete accepts only owned objects with no foreign children. No reflection, arbitrary code, shell commands, arbitrary URLs/downloads, or new/cleared scenes. Network research and bounded data acquisition are provided by registered web/model/material tools only. At most24 actions,300 scatter placements,600 actual GameObjects per batch and2000 owned objects in the scene. Trees cost4 objects,houses9,NPCs8. Batch failure rolls back scene and new assets. No scene save without explicit save_scene. Tools:
create_world: name; target empty. args {style?:alpine|forest|desert|moon,width?:32..2048,length?:32..2048,height?:4..512,trees?:0..300,buildings?:0..100,rocks?:0..300,seed?:integer,resolution?:33|65|129|257|513,scale?:0.1..32,octaves?:1..8,persistence?:0.1..0.9,lacunarity?:1..4,mountainStrength?:0.0..1.0,valleyDepth?:0.0..0.8,waterLevel?:0.0..0.8,southSlope?:0.0..1.0,smoothingPasses?:0..8}. Width,length,height are METRES. valleyDepth,waterLevel,mountainStrength,southSlope are NORMALIZED FRACTIONS, not metres; valleyDepth and waterLevel must not exceed 0.8. Partial TerrainSpec has consistent defaults: keep optional terrain noise/water factors at defaults unless the user asks to change them. Creates textured terrain,lake,spaced trees/rocks/houses,sun and camera. Preserves all existing scene objects. Presets alter geometry and colors; these are original low-poly primitives, not imported art.
update_world: target owned world or terrain. args same partial TerrainSpec fields and optional style. Rebuilds heights/ground layers/water, keeps existing contents anchored to new ground. To lower mountains use mountainStrength or height. Does not add/remove decoration.
create_npc: name; target optional owned parent world/group. args {role?:text,personality?:text,biography?:text,goals?:text,constraints?:text,knowledge?:text|array of strings,position?:[x,y,z],rotation?:[x,y,z],scale?:[x,y,z],on_terrain?:bool,inventory?:{item:integer},autonomous?:bool}. Creates visible character plus real IntelligentNPC,Inventory,WorldEntity,scoped KnowledgeBase and valid model routes; defaults are local Ollama. With on_terrain=true(default), deterministically snaps to nearby dry, clear, moderately sloped ground (up to20 radial search steps), avoiding terrain holes, cave approaches and overlapping actors/structures; x/z may shift. on_terrain=false preserves explicit xyz for special placements.
create_object: name; target optional owned parent. args {kind:cube|sphere|capsule|cylinder|plane|tree|rock|house,position?:[x,y,z],rotation?:[x,y,z],scale?:[x,y,z],color?:#RRGGBB|#RRGGBBAA|[r,g,b,a],on_terrain?:bool,inventory?:{item:integer}}. Also registers WorldEntity with id=name.
scatter_objects: name; target owned world containing terrain (or terrain itself). args {kind:tree|rock|house|cube|sphere|capsule|cylinder|plane,count:1..300,seed?:integer,center?:[x,y,z],size?:[width,length],spacing?:.1..100}. Samples dry terrain, rejects unsuitable slopes, deterministic seed; fails if requested count cannot fit. Produces an owned named group.
set_transform: target; args {position?:[x,y,z],rotation?:[x,y,z],scale?:[x,y,z]}; at least one. World position/rotation,local scale(.01..100); coordinates bounded ±100000.
set_material: target; args {color:hex or RGB(A) array,smoothness?:0..1,children?:bool}. Creates a new material and assigns it; never mutates existing shared materials. children defaults false; target must have matching renderer(s).
set_lighting: target existing Light OR empty to create owned light using name. args {type?:directional|point|spot,intensity?:0..8,color?:color,position?:[x,y,z],rotation?:[x,y,z],range?:.1..500,spot_angle?:1..179,shadows?:bool,on_terrain?:bool}. NEW point/spot lights default on_terrain=true: preserve x/z and any higher y, raise only up to at least3m above terrain and generated water under that x/z. Without terrain under x/z keep position. Existing lights preserve exact position unless on_terrain=true is explicit, including edits with position; false preserves exact xyz. Directional lights are never moved by this option. Terrain anchoring is serialized; update_world reanchors these lights above the changed surface/water. Set range large enough to reach the ground from the final height. Does not modify global RenderSettings.
create_camera: name; target optional owned parent. args {position?:[x,y,z],rotation?:[x,y,z],look_at?:[x,y,z],field_of_view?:10..120,orthographic?:bool,size?:1..1000,enabled?:bool,background?:color}. Never disables existing cameras; explicitly enable only when needed.
update_npc: target IntelligentNPC; args {role?,personality?,biography?,goals?,constraints?,knowledge?:text|strings,inventory?:object,autonomous?:bool}. Knowledge replacement creates a new scoped asset, leaving any old/shared asset intact. A goal-only change does not require attach_behavior; add physical movement only when the user asks for it.
attach_behavior: target; args {behavior:rotate|bob|patrol|followplayer,speed?:.01..360,amplitude?:0..10,frequency?:.01..10,axis?:[x,y,z],points?:array of2..32 world vectors,follow_target?:target,stopping_distance?:0..20}. Patrol requires points,followplayer requires explicit follow_target. Real runtime preset components, no arbitrary type names. One SceneBehavior per object; replaces its settings.
delete_object: target owned object; args {}. Cannot delete unrelated or foreign descendants.
save_scene: target empty; args {path?:Assets/WorldWeaverGenerated/.../Name.unity}. Must be the LAST action. Always a unique destination, never overwrites a scene. Does not touch build settings.
inspect_object: target; args {}. Read-back of transform,components,TerrainSpec,NPC and inventory.
search_project: target empty; args {query:text,limit?:1..40}. Read-only asset search under Assets/Packages. Returns query and typed asset records; use t:Prefab, t:Material or t:Script when appropriate. A matching icon is not a playable prefab. Search never creates a proposed asset.
read_script: target empty; args {path:Assets/.../Name.cs or installed Packages/.../Name.cs}. Read-only maximum64KiB, no traversal. Returns exists and actual source/hash. A missing permitted path returns exists:false and available source paths; a planned class name is not an existing file. Package scripts remain read-only. draft_script is handled by the controller separately.";

        private sealed class VirtualTarget
        {
            internal GameObject Object;
            internal string Parent;
            internal bool Owned, Npc, Terrain, Light, Renderer, OwnRenderer;
            internal bool Deleted;
        }
        private static IEnumerable<GameObject> SceneObjects()
        {
            var scene=SceneManager.GetActiveScene();
            if(!scene.IsValid()||!scene.isLoaded) throw new InvalidOperationException("An active loaded scene is required.");
            return scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject);
        }
        public static GameObject ResolveTarget(string target)
        {
            if(string.IsNullOrWhiteSpace(target))throw new ArgumentException("Explicit target required. Use id:<instanceId> from context.");
            if(target.StartsWith("owned:",StringComparison.Ordinal)||target.StartsWith("global:",StringComparison.Ordinal))return ProjectContextIndex.ResolveStable(target);
            var objects=SceneObjects().ToArray();
            if(target=="selected") {var selected=Selection.activeGameObject;if(selected&&selected.scene==SceneManager.GetActiveScene())return selected;throw new ArgumentException("Selected object is not in active scene.");}
            if(target.StartsWith("id:",StringComparison.Ordinal)&&int.TryParse(target.Substring(3),out int id))
            {var obj=objects.FirstOrDefault(o=>o.GetInstanceID()==id);if(obj&&obj.scene==SceneManager.GetActiveScene())return obj;throw new ArgumentException("Target ID is not in active scene: "+target);}
            if(target.StartsWith("id:",StringComparison.Ordinal))throw new ArgumentException("Target must be id:<NUMERIC existing scene ID> or @Name for an EARLIER creation. Never combine prefixes as id:@Name. A creation target is its existing parent, not itself; use an empty target for create_npc when no parent is needed.");
            string name=target.StartsWith("@",StringComparison.Ordinal)?target.Substring(1):target;
            var matches=objects.Where(o=>o.name==name).Take(2).ToArray();
            if(matches.Length!=1)throw new ArgumentException("Target name must exist exactly once: "+target+". Use stable id.");return matches[0];
        }
        private static VirtualTarget Meta(GameObject go) => new VirtualTarget {Object=go,Owned=go.GetComponent<GeneratedSceneObject>(),Npc=go.GetComponent<IntelligentNPC>(),Terrain=go.GetComponentInChildren<GeneratedTerrain>(true),Light=go.GetComponent<Light>(),OwnRenderer=go.GetComponent<Renderer>(),Renderer=go.GetComponentsInChildren<Renderer>(true).Length>0};
        private static VirtualTarget Resolve(string target,Dictionary<string,VirtualTarget> aliases)
        {
            if(aliases.TryGetValue(target,out var meta)){if(meta.Deleted)throw new ArgumentException("Target was deleted earlier in the plan: "+target);return meta;}
            var obj=ResolveTarget(target);
            if(aliases.Values.Any(v=>v.Deleted&&v.Object&&(obj==v.Object||obj.transform.IsChildOf(v.Object.transform))))throw new ArgumentException("Target was deleted earlier in the plan.");
            return Meta(obj);
        }
        public static JObject Context()
        {
            var scene=SceneManager.GetActiveScene();var all=SceneObjects().ToArray();
            var selected=Selection.gameObjects.Where(o=>o.scene==scene).Select(Id).ToArray();
            var listed=all.Where(o=>selected.Contains(Id(o))||o.GetComponent<GeneratedSceneObject>()?.Kind!="part").OrderByDescending(o=>selected.Contains(Id(o))).ThenByDescending(o=>o.GetComponent<GeneratedTerrain>()||o.GetComponent<IntelligentNPC>()).ThenByDescending(o=>o.GetComponent<GeneratedSceneObject>()?.Kind=="world"||o.GetComponent<GeneratedSceneObject>()?.Kind=="group").ThenByDescending(o=>o.GetComponent<Light>()||o.GetComponent<Camera>()).ThenBy(o=>o.GetInstanceID()).Take(80);
            var terrain=all.Select(o=>o.GetComponent<GeneratedTerrain>()).FirstOrDefault(t=>t);
            return new JObject { ["scene"]=scene.name,["path"]=scene.path,["dirty"]=scene.isDirty,["selected"]=new JArray(selected),
                ["objectCount"]=all.Length,["ownedObjectCount"]=all.Count(o=>o.GetComponent<GeneratedSceneObject>()),["listedLimit"]=80,["logicalCounts"]=JObject.FromObject(all.Select(o=>o.GetComponent<GeneratedSceneObject>()).Where(m=>m&&!string.IsNullOrEmpty(m.Kind)&&m.Kind!="part").GroupBy(m=>m.Kind).ToDictionary(g=>g.Key,g=>g.Count())),
                ["objects"]=new JArray(listed.Select(Inspect)),["currentTerrainSpecification"]=terrain?JObject.FromObject(terrain.Specification):null,
                ["ownedRoots"]=new JArray(all.Where(o=>o.GetComponent<GeneratedSceneObject>()&&(!o.transform.parent||!o.transform.parent.GetComponent<GeneratedSceneObject>())).Select(o=>new JObject{["target"]=Id(o),["name"]=o.name})),
                ["capabilities"]=new JArray(((JArray)EditorToolSchema.CommandSchema()["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName).Distinct()),
                ["safety"]=new JObject{["batchGameObjects"]=BatchLimit,["sceneOwnedGameObjects"]=SceneLimit,["readOnlyAssets"]=true,["requiresExplicitSave"]=true}};
        }
        private static string Id(GameObject go)=>ProjectContextIndex.StableTarget(go);
        private static JArray Vec(Vector3 p)=>new JArray(p.x,p.y,p.z);
        private static JObject Inspect(GameObject go)
        {
            var value=new JObject{["target"]=Id(go),["name"]=go.name,["kind"]=go.GetComponent<GeneratedSceneObject>()?.Kind,["parent"]=go.transform.parent?Id(go.transform.parent.gameObject):null,["owned"]=(bool)go.GetComponent<GeneratedSceneObject>(),["active"]=go.activeSelf,["position"]=Vec(go.transform.position),["rotation"]=Vec(go.transform.eulerAngles),["scale"]=Vec(go.transform.localScale),["components"]=new JArray(go.GetComponents<Component>().Where(c=>c).Take(12).Select(c=>c.GetType().Name))};
            var terrain=go.GetComponent<GeneratedTerrain>();if(terrain)value["terrainSpecification"]=JObject.FromObject(terrain.Specification);
            var actualTerrain=go.GetComponent<Terrain>();if(actualTerrain)value["terrainData"]=ProjectContextIndex.TerrainFacts(actualTerrain);
            var npc=go.GetComponent<IntelligentNPC>();if(npc)value["npc"]=new JObject{["id"]=npc.CharacterId,["name"]=npc.NpcName,["role"]=npc.Role,["personality"]=Short(npc.Personality,300),["goals"]=Short(npc.Goals,300),["biography"]=Short(npc.Biography,300),["constraints"]=Short(npc.Constraints,300),["autonomous"]=npc.Autonomous,["knowledgeDocuments"]=npc.Knowledge?npc.Knowledge.Documents.Count:0,["knowledgeExcerpt"]=npc.Knowledge?new JArray(npc.Knowledge.Documents.Take(3).Select(d=>new JObject{["title"]=Short(d.Title,100),["scope"]=Short(d.Scope,100),["text"]=Short(d.Text,300)})):new JArray(),["routesAssigned"]=npc.Models&&npc.Models.Dialogue&&npc.Models.Planning};
            if(npc)
            {
                var state=(JObject)value["npc"];var social=npc.GetComponent<NpcSocialAutonomy>();state["aiEnabled"]=npc.AiEnabled;state["conversationEntries"]=npc.ConversationHistory.Count;
                state["social"]=social?new JObject{["enabled"]=social.Enabled,["cooldownSeconds"]=social.CooldownSeconds,["topic"]=Short(social.Topic,1000),["active"]=social.ConversationActive}:null;
            }
            var inventory=go.GetComponent<Inventory>();if(inventory)value["inventory"]=JObject.FromObject(inventory.Snapshot());
            var light=go.GetComponent<Light>();if(light)value["light"]=new JObject{["type"]=light.type.ToString().ToLowerInvariant(),["range"]=light.range,["spot_angle"]=light.spotAngle,["intensity"]=light.intensity,["color"]=new JArray(light.color.r,light.color.g,light.color.b)};
            var renderer=go.GetComponent<Renderer>();if(renderer&&renderer.sharedMaterial)value["material"]=renderer.sharedMaterial.name;
            var geometry=GeometryDiagnostics.Summary(go);if(geometry!=null)value["geometry"]=geometry;
            return value;
        }
        private static string Short(string text,int max)=>(text??"").Substring(0,Math.Min(max,(text??"").Length));
        private static void Keys(JObject args,IEnumerable<string> allowed)
        {
            var set=new HashSet<string>(allowed,StringComparer.Ordinal);var bad=args.Properties().Where(p=>!set.Contains(p.Name)).Select(p=>p.Name).ToArray();
            if(bad.Length>0)throw new ArgumentException("Unknown args: "+string.Join(",",bad)+". Allowed: "+string.Join(",",set));
        }
        private static void Keys(JObject a,params string[] allowed)=>Keys(a,(IEnumerable<string>)allowed);
        private static float Number(JToken t,string key,float min,float max)
        {if(t==null||(t.Type!=JTokenType.Float&&t.Type!=JTokenType.Integer))throw new ArgumentException(key+" must be numeric.");double d=(double)t;if(double.IsNaN(d)||double.IsInfinity(d)||d<min||d>max)throw new ArgumentException(key+" must be finite in ["+min+","+max+"].");return(float)d;}
        private static int Integer(JObject a,string key,int min,int max,int fallback)
        {var t=a[key];if(t==null)return fallback;if(t.Type!=JTokenType.Integer)throw new ArgumentException(key+" must be integer.");Number(t,key,min,max);return(int)t;}
        private static Vector3 Vector(JToken token,string key,float min=-100000,float max=100000)
        {if(!(token is JArray a)||a.Count!=3)throw new ArgumentException(key+" requires exactly3 numbers.");return new Vector3(Number(a[0],key,min,max),Number(a[1],key,min,max),Number(a[2],key,min,max));}
        private static void OptionalVector(JObject a,string key,float min=-100000,float max=100000){if(a[key]!=null)Vector(a[key],key,min,max);}
        private static void OptionalNumber(JObject a,string key,float min,float max){if(a[key]!=null)Number(a[key],key,min,max);}
        private static void Boolean(JObject a,string key){if(a[key]!=null&&a[key].Type!=JTokenType.Boolean)throw new ArgumentException(key+" must be boolean.");}
        private static string Text(JObject a,string key,int max,bool required=false)
        {var t=a[key];if(t==null&&!required)return null;if(t?.Type!=JTokenType.String||((string)t).Length>max||(required&&string.IsNullOrWhiteSpace((string)t)))throw new ArgumentException("Invalid text "+key+" (max "+max+").");return(string)t;}
        private static Color ColorValue(JToken t)
        {
            if(t?.Type==JTokenType.String)
            {string s=(string)t;if((s.Length!=7&&s.Length!=9)||!s.StartsWith("#",StringComparison.Ordinal)||!ColorUtility.TryParseHtmlString(s,out var c))throw new ArgumentException("Color must be #RRGGBB or #RRGGBBAA.");return c;}
            if(t is JArray a&&(a.Count==3||a.Count==4))return new Color(Number(a[0],"red",0,1),Number(a[1],"green",0,1),Number(a[2],"blue",0,1),a.Count==4?Number(a[3],"alpha",0,1):1);
            throw new ArgumentException("Color requires hex or RGB(A) array.");
        }
        private static string Choice(JObject a,string key,string[] choices,string fallback=null)
        {string v=Text(a,key,50,fallback==null)??fallback;if(!choices.Contains(v))throw new ArgumentException(key+" must be "+string.Join("|",choices));return v;}
        private static void TransformArgs(JObject a){OptionalVector(a,"position");OptionalVector(a,"rotation");OptionalVector(a,"scale",.01f,100);Boolean(a,"on_terrain");}
        private static void Knowledge(JObject a)
        {
            if(a["knowledge"]==null)return;
            if(a["knowledge"].Type==JTokenType.String){Text(a,"knowledge",16000);return;}
            if(!(a["knowledge"] is JArray list)||list.Count>32||list.Any(t=>t.Type!=JTokenType.String||((string)t).Length>4000)||list.Sum(t=>((string)t).Length)>16000)throw new ArgumentException("knowledge must be text or <=32 strings, <=16000 total characters.");
        }
        private static void InventoryArgs(JObject a)
        {
            if(a["inventory"]==null)return;if(!(a["inventory"] is JObject items)||items.Count>30)throw new ArgumentException("inventory requires <=30 items.");
            long total=0;foreach(var p in items.Properties()){if(string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>80||p.Value.Type!=JTokenType.Integer)throw new ArgumentException("Invalid inventory item.");total+=(int)Number(p.Value,p.Name,0,10000);}if(total>10000)throw new ArgumentException("Inventory capacity10000 exceeded.");
        }
        private static int Cost(string kind)=>kind=="tree"?4:kind=="house"?9:1;
        private static void EmptyTarget(EditorCommand command){if(!string.IsNullOrEmpty(command.target))throw new ArgumentException(command.tool+" does not accept target.");}
        private static string AssetPath(JObject args,string key,string extension,bool generated)
        {
            string path=Text(args,key,240,true);
            if(!path.StartsWith(generated?"Assets/WorldWeaverGenerated/":"Assets/",StringComparison.Ordinal)||!path.EndsWith(extension,StringComparison.OrdinalIgnoreCase)||path.Contains("..")||path.Contains('\\')||path.Contains(':')||path.Split('/').Any(s=>string.IsNullOrWhiteSpace(s))||path.IndexOfAny(new[]{'\0','\r','\n','*','?','"','<','>','|'})>=0)throw new ArgumentException("Invalid project asset path: "+key);
            return path;
        }
        private static string ReadableScriptPath(string path)
        {
            if(string.IsNullOrEmpty(path)||path.Length>240||!(path.StartsWith("Assets/",StringComparison.Ordinal)||path.StartsWith("Packages/",StringComparison.Ordinal))||!path.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)||path.Contains("..")||path.Contains('\\')||path.Contains(':')||path.Split('/').Any(string.IsNullOrWhiteSpace)||path.IndexOfAny(new[]{'\0','\r','\n','*','?','"','<','>','|'})>=0)throw new ArgumentException("Read an existing Assets/...cs or installed Packages/...cs without traversal.");
            string physical=path;
            if(path.StartsWith("Packages/",StringComparison.Ordinal))
            {
                var info=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);if(info==null||!path.StartsWith(info.assetPath+"/",StringComparison.Ordinal))throw new ArgumentException("Script is not in an installed package.");
                physical=Path.Combine(info.resolvedPath,path.Substring(info.assetPath.Length+1));
            }
            // Missing data inside an allowed boundary is an observation, not an
            // invalid plan. Retain all existing-script type and size checks.
            if(!File.Exists(physical))return physical;
            if(!AssetDatabase.LoadAssetAtPath<MonoScript>(path))throw new ArgumentException("Path is not a registered C# script asset; use an actual source path.");
            if(new FileInfo(physical).Length>65536)throw new ArgumentException("Script larger than 64KiB.");return physical;
        }
        static JObject ReadScriptObservation(string path)
        {
            string physical=ReadableScriptPath(path);
            if(!File.Exists(physical))
            {
                string parent=Path.GetDirectoryName(path)?.Replace('\\','/');
                var available=AssetDatabase.FindAssets("t:MonoScript",new[]{"Assets"}).Select(AssetDatabase.GUIDToAssetPath).Where(File.Exists)
                    .OrderByDescending(p=>Path.GetDirectoryName(p)?.Replace('\\','/')==parent).ThenBy(p=>p.Contains("/Editor/")).ThenBy(p=>p,StringComparer.Ordinal).Take(12);
                return new JObject{["path"]=path,["exists"]=false,["readOnlyPackage"]=path.StartsWith("Packages/",StringComparison.Ordinal),["source"]=null,["sha256"]=null,["availableScripts"]=new JArray(available),
                    ["note"]="The requested source file is not currently present at this path. A class name in a proposed plan is not an existing asset. Use actual observed paths; implement a required new class through the permitted source workflow instead of repeatedly reading this absent path."};
            }
            byte[] bytes=File.ReadAllBytes(physical);string hash;using(var sha=System.Security.Cryptography.SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            return new JObject{["path"]=path,["exists"]=true,["sha256"]=hash,["readOnlyPackage"]=path.StartsWith("Packages/",StringComparison.Ordinal),["source"]=ChatHistoryStore.Redact(File.ReadAllText(physical))};
        }
        static JObject SearchProjectObservation(JObject args)
        {
            string query=(string)args["query"];int limit=(int?)args["limit"]??20;
            var matches=AssetDatabase.FindAssets(query,new[]{"Assets","Packages"});
            var assets=new JArray(matches.Take(limit).Select(AssetDatabase.GUIDToAssetPath).Select(path=>new JObject{["path"]=path,["mainAssetType"]=AssetDatabase.GetMainAssetTypeAtPath(path)?.Name??"unknown",["isFolder"]=AssetDatabase.IsValidFolder(path),["readOnlyPackage"]=path.StartsWith("Packages/",StringComparison.Ordinal)}));
            return new JObject{["query"]=query,["assets"]=assets,["count"]=assets.Count,["truncated"]=matches.Length>limit,["note"]="Observed matches, not created assets. Queries may match subassets; mainAssetType describes the path's main asset. Use a type filter when looking for runtime prefabs, materials or scripts."};
        }
        public static void Validate(EditorPlan plan)
        {
            plan=SceneCreationDefaults.Resolve(plan);
            if(PrefabStageUtility.GetCurrentPrefabStage()!=null)throw new InvalidOperationException("Завершите редактирование Prefab перед изменением сцены.");
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Editor scene tools require Edit Mode.");
            if(plan?.actions==null||plan.actions.Count>24)throw new ArgumentException("Plan requires <=24 actions.");
            var all=SceneObjects().ToArray();int created=all.Any(o=>o.GetComponent<GeneratedSceneObject>()&&o.GetComponent<GeneratedSceneObject>().Kind=="group")?0:1;
            int owned=all.Count(o=>o.GetComponent<GeneratedSceneObject>());
            var aliases=new Dictionary<string,VirtualTarget>(StringComparer.Ordinal);var names=new HashSet<string>(StringComparer.Ordinal);var ids=new HashSet<string>(all.Select(o=>o.GetComponent<WorldEntity>()).Where(e=>e).Select(e=>e.Id));
            bool saved=false;
            foreach(var command in plan.actions)
            {
                if(command==null||command.args==null||command.name==null||command.target==null||command.tool==null)throw new ArgumentException("Null command field.");
                if(command.name.Length>160||command.target.Length>200||command.args.ToString().Length>40000)throw new ArgumentException("Command too large.");
                if(saved)throw new ArgumentException("save_scene must be the final command.");
                var a=command.args;VirtualTarget target=null;bool creation=false;int cost=0;
                if(WebResearchTools.Handles(command.tool))
                {
                    WebResearchTools.Validate(command,s=>Resolve(s,aliases).Object??throw new ArgumentException("Resource target must already exist."));
                    if(plan.actions.Count!=1&&!plan.actions.All(a=>IsReadOnlyTool(a.tool)))throw new ArgumentException("Internet acquisition/heightmap mutations are standalone async steps; only independent observations may batch.");
                    continue;
                }
                if(ModelAssetTools.Handles(command.tool))
                {
                    ModelAssetTools.Validate(command,s=>Resolve(s,aliases).Object??throw new ArgumentException("Model target must already exist."));
                    if(command.tool!="model_search"&&plan.actions.Count!=1)throw new ArgumentException("Model import/binding are standalone steps.");
                    if(command.tool=="replace_model"&&owned+400>SceneLimit)throw new ArgumentException("Insufficient scene object budget for model binding.");
                    continue;
                }
                if(WorldMaterialTools.Handles(command.tool))
                {
                    WorldMaterialTools.Validate(command,s=>Resolve(s,aliases).Object??throw new ArgumentException("Material target must already exist; apply in a subsequent step after creation."));
                    if(WorldMaterialTools.IsAsync(command.tool)&&plan.actions.Count!=1&&!plan.actions.All(a=>IsReadOnlyTool(a.tool)))throw new ArgumentException("Material import requires a standalone plan. Apply the returned material in a subsequent step.");
                    continue;
                }
                if(AgentProjectTools.Handles(command.tool))
                {
                    AgentProjectTools.Validate(command,s=>Resolve(s,aliases).Object??throw new ArgumentException("This project operation requires an already existing target; split creations and inspection into separate steps."));
                    if(AgentProjectTools.IsDeferred(command.tool)&&plan.actions.Count!=1)throw new ArgumentException("Test/build/open-scene operations require a standalone plan.");
                    continue;
                }
                if(ConstructiveTools.Handles(command.tool))
                {
                    ConstructiveTools.Validate(command,s=>Resolve(s,aliases).Object??throw new ArgumentException("Constructive targets must exist; run creation and dependent operations in separate steps."));
                    if(command.tool=="verify_scene"&&plan.actions.Any(a=>!IsReadOnlyTool(a.tool)))throw new ArgumentException("Verification is a standalone read-only step.");
                    created+=ConstructiveTools.Cost(command);if(created>BatchLimit||owned+created>SceneLimit)throw new ArgumentException("Constructive object budget exceeded.");
                    continue;
                }
                if(AnimatedCharacterTools.Handles(command.tool))
                {
                    AnimatedCharacterTools.Validate(command,s=>Resolve(s,aliases).Object??throw new ArgumentException("Character target must already exist; create the world in a prior step."));
                    string characterName=string.IsNullOrWhiteSpace(command.name)?"Главный герой":command.name;
                    if(characterName.Any(c=>char.IsControl(c)||c=='/'||c=='\\')||!names.Add(characterName))throw new ArgumentException("Character name must contain no path separators and be unique in this plan.");
                    created+=AnimatedCharacterTools.EstimateCost(command);
                    if(created>BatchLimit||owned+created>SceneLimit)throw new ArgumentException("Animated character object budget exceeded.");
                    var future=new VirtualTarget{Owned=true,Renderer=true};aliases["@"+characterName]=future;aliases[characterName]=future;
                    continue;
                }
                if(WorldGameplayTools.Handles(command.tool))
                {
                    WorldGameplayTools.Validate(command,s=>Resolve(s,aliases).Object??throw new ArgumentException("Gameplay target must already exist; use a subsequent step after creating the world."));
                    cost=WorldGameplayTools.EstimateCost(command);created+=cost;
                    if(created>BatchLimit||owned+created>SceneLimit)throw new ArgumentException("Gameplay object budget exceeded.");
                    if(command.tool=="create_village"||command.tool=="instantiate_prefab")
                    {
                        if(string.IsNullOrWhiteSpace(command.name)||command.name.Any(c=>char.IsControl(c)||c=='/'||c=='\\')||!names.Add(command.name)||all.Any(o=>o.name==command.name))throw new ArgumentException("Creation name must be unique and contain no path separators.");
                        var future=new VirtualTarget{Owned=true,Terrain=command.tool=="create_village",Renderer=true};aliases["@"+command.name]=future;aliases[command.name]=future;
                    }
                    continue;
                }
                switch(command.tool)
                {
                    case "create_world":
                        EmptyTarget(command);Keys(a,TerrainKeys.Concat(new[]{"style","trees","rocks","buildings"}));
                        string style=Choice(a,"style",new[]{"forest","alpine","desert","moon"},"forest");WorldComposer.Specification(a,style).Validate();
                        cost=5+4*Integer(a,"trees",0,300,style=="moon"?0:style=="desert"?12:40)+Integer(a,"rocks",0,300,18)+9*Integer(a,"buildings",0,100,4);
                        target=new VirtualTarget{Owned=true,Terrain=true,Renderer=true};creation=true;break;
                    case "update_world":
                        Keys(a,TerrainKeys.Concat(new[]{"style"}));target=Resolve(command.target,aliases);if(!target.Owned||!target.Terrain)throw new ArgumentException("update_world requires owned terrain/world.");
                        string nextStyle=Choice(a,"style",new[]{"forest","alpine","desert","moon"},"forest");
                        var oldSpec=target.Object?target.Object.GetComponentInChildren<GeneratedTerrain>(true)?.Specification:null;
                        WorldComposer.Specification(a,nextStyle,oldSpec).Validate();break;
                    case "create_npc":
                        Keys(a,"role","personality","biography","goals","constraints","knowledge","position","rotation","scale","on_terrain","inventory","autonomous");
                        TransformArgs(a);NpcArgs(a);Parent(command,aliases);target=new VirtualTarget{Owned=true,Npc=true,Renderer=true,Parent=command.target};cost=8;creation=true;break;
                    case "create_object":
                        Keys(a,"kind","position","rotation","scale","color","on_terrain","inventory");TransformArgs(a);InventoryArgs(a);if(a["color"]!=null)ColorValue(a["color"]);
                        string kind=Choice(a,"kind",Kinds);Parent(command,aliases);cost=Cost(kind);target=new VirtualTarget{Owned=true,Renderer=true,OwnRenderer=kind!="tree"&&kind!="house",Parent=command.target};creation=true;break;
                    case "scatter_objects":
                        Keys(a,"kind","count","seed","center","size","spacing");target=Resolve(command.target,aliases);if(!target.Owned||!target.Terrain)throw new ArgumentException("scatter target must contain owned terrain.");
                        string scatterKind=Choice(a,"kind",Kinds);int count=Integer(a,"count",1,300,1);Integer(a,"seed",int.MinValue,int.MaxValue,42);OptionalVector(a,"center");OptionalNumber(a,"spacing",.1f,100);
                        if(a["size"]!=null){if(!(a["size"] is JArray size)||size.Count!=2)throw new ArgumentException("size must be[width,length].");foreach(var token in size)Number(token,"size",1,2048);}
                        cost=1+Cost(scatterKind)*count;target=new VirtualTarget{Owned=true,Renderer=true,Parent=command.target};creation=true;break;
                    case "set_transform":Keys(a,"position","rotation","scale");if(a.Count==0)throw new ArgumentException("Transform needs a field.");TransformArgs(a);target=Resolve(command.target,aliases);break;
                    case "set_material":
                        Keys(a,"color","smoothness","children");ColorValue(a["color"]);OptionalNumber(a,"smoothness",0,1);Boolean(a,"children");target=Resolve(command.target,aliases);
                        if(!target.Renderer)throw new ArgumentException("No renderers on material target.");
                        if((bool?)a["children"]!=true&&!target.OwnRenderer)throw new ArgumentException("Target has no renderer; set children=true explicitly.");break;
                    case "set_lighting":
                        Keys(a,"type","intensity","color","position","rotation","range","spot_angle","shadows","on_terrain");Boolean(a,"on_terrain");Choice(a,"type",new[]{"directional","point","spot"},"directional");OptionalNumber(a,"intensity",0,8);OptionalNumber(a,"range",.1f,500);OptionalNumber(a,"spot_angle",1,179);OptionalVector(a,"position");OptionalVector(a,"rotation");Boolean(a,"shadows");if(a["color"]!=null)ColorValue(a["color"]);
                        if(string.IsNullOrEmpty(command.target)){creation=true;cost=1;target=new VirtualTarget{Owned=true,Light=true};}else{target=Resolve(command.target,aliases);if(!target.Light)throw new ArgumentException("Target must have Light.");}break;
                    case "create_camera":
                        Keys(a,"position","rotation","look_at","field_of_view","orthographic","size","enabled","background");OptionalVector(a,"position");OptionalVector(a,"rotation");OptionalVector(a,"look_at");OptionalNumber(a,"field_of_view",10,120);OptionalNumber(a,"size",1,1000);Boolean(a,"orthographic");Boolean(a,"enabled");if(a["background"]!=null)ColorValue(a["background"]);
                        Parent(command,aliases);target=new VirtualTarget{Owned=true,Parent=command.target};creation=true;cost=1;break;
                    case "update_npc":Keys(a,"role","personality","biography","goals","constraints","knowledge","inventory","autonomous");NpcArgs(a);target=Resolve(command.target,aliases);if(!target.Npc)throw new ArgumentException("Target lacks IntelligentNPC.");break;
                    case "attach_behavior":
                        Keys(a,"behavior","speed","amplitude","frequency","axis","points","follow_target","stopping_distance");string behavior=Choice(a,"behavior",new[]{"rotate","bob","patrol","followplayer"});OptionalNumber(a,"speed",.01f,360);OptionalNumber(a,"amplitude",0,10);OptionalNumber(a,"frequency",.01f,10);OptionalNumber(a,"stopping_distance",0,20);OptionalVector(a,"axis",-1,1);
                        if(a["axis"]!=null&&Vector(a["axis"],"axis",-1,1).sqrMagnitude<.0001f)throw new ArgumentException("axis cannot be zero.");
                        if(a["points"]!=null){if(!(a["points"] is JArray points)||points.Count<2||points.Count>32)throw new ArgumentException("Patrol requires2..32 points.");foreach(var point in points)Vector(point,"waypoint");}
                        if(behavior=="patrol"&&a["points"]==null)throw new ArgumentException("Patrol requires points.");
                        if(a["follow_target"]!=null)Resolve(Text(a,"follow_target",200,true),aliases);
                        if(behavior=="followplayer"&&a["follow_target"]==null)throw new ArgumentException("followplayer requires follow_target.");target=Resolve(command.target,aliases);break;
                    case "delete_object":
                        Keys(a);target=Resolve(command.target,aliases);if(!target.Owned)throw new ArgumentException("Delete is restricted to owned WorldWeaver objects.");
                        if(target.Object&&target.Object.GetComponentsInChildren<Transform>(true).Any(t=>!t.GetComponent<GeneratedSceneObject>()))throw new ArgumentException("Owned object contains foreign descendants; delete refused.");
                        target.Deleted=true;aliases[command.target]=target;bool more;do{more=false;foreach(var meta in aliases.Values.Where(v=>!v.Deleted&&!string.IsNullOrEmpty(v.Parent)).ToArray())if(aliases.TryGetValue(meta.Parent,out var ancestor)&&ancestor.Deleted){meta.Deleted=true;more=true;}}while(more);break;
                    case "save_scene":EmptyTarget(command);Keys(a,"path");if(a["path"]!=null)AssetPath(a,"path",".unity",true);saved=true;break;
                    case "inspect_object":Keys(a);Resolve(command.target,aliases);break;
                    case "search_project":EmptyTarget(command);Keys(a,"query","limit");Text(a,"query",120,true);Integer(a,"limit",1,40,20);break;
                    case "read_script":
                        EmptyTarget(command);Keys(a,"path");ReadableScriptPath((string)a["path"]);break;
                    default:throw new ArgumentException("Unknown editor tool: "+command.tool);
                }
                if(creation)
                {
                    if(string.IsNullOrWhiteSpace(command.name)||command.name.Any(c=>char.IsControl(c)||c=='/'||c=='\\')||!names.Add(command.name)||all.Any(o=>o.name==command.name))throw new ArgumentException("Creation name must be unique, nonempty and contain no path separators: "+command.name);
                    aliases["@"+command.name]=target;aliases[command.name]=target;
                    if((command.tool=="create_npc"||command.tool=="create_object")&&!ids.Add(command.name))throw new ArgumentException("WorldEntity ID already exists: "+command.name);
                    created+=cost;if(created>BatchLimit||owned+created>SceneLimit)throw new ArgumentException("Object budget exceeded: batch="+created+"/600, scene="+(owned+created)+"/2000. Reduce decoration counts.");
                }
            }
        }
        private static void NpcArgs(JObject a)
        {foreach(string key in new[]{"role","personality","biography","goals","constraints"})Text(a,key,4000);Knowledge(a);InventoryArgs(a);Boolean(a,"autonomous");}
        private static void Parent(EditorCommand command,Dictionary<string,VirtualTarget> aliases)
        {if(!string.IsNullOrEmpty(command.target)&&!Resolve(command.target,aliases).Owned)throw new ArgumentException("Creation parent must be owned WorldWeaver group/world.");}

        public static bool IsReadOnlyTool(string tool)=>tool=="job_result"||tool=="inspect_object"||tool=="search_project"||tool=="read_script"||WorldGameplayTools.IsReadOnly(tool)||AgentProjectTools.IsReadOnly(tool)||WorldMaterialTools.IsReadOnly(tool)||ConstructiveTools.IsReadOnly(tool)||ModelAssetTools.IsReadOnly(tool)||WebResearchTools.IsReadOnly(tool);

        public static async Task<EditorBatchResult> ExecuteAsync(EditorPlan plan,OrchestratorProfile routes=null,AgentExecutionContext executionContext=null,CancellationToken token=default,Action beforeMutation=null)
        {
            token.ThrowIfCancellationRequested();
            if(plan?.actions?.Count>1&&plan.actions.All(a=>a!=null&&IsReadOnlyTool(a.tool)))
            {
                var observations=new EditorBatchResult{Success=true,Summary="Независимые наблюдения выполнены последовательно."};
                foreach(var observation in plan.actions)
                {
                    token.ThrowIfCancellationRequested();var part=await ExecuteAsync(new EditorPlan{complete=false,actions=new List<EditorCommand>{observation}},routes,executionContext,token,beforeMutation);
                    if(!part.Success&&part.Results.Count==1&&part.Results[0].Tool=="batch")part.Results[0].Tool=observation.tool;
                    observations.Results.AddRange(part.Results);if(!part.Success){observations.Success=false;observations.Error=string.Join("; ",new[]{observations.Error,part.Error}.Where(s=>!string.IsNullOrEmpty(s)));}
                }
                return observations;
            }
            if(plan?.actions?.Any(a=>a!=null&&(WorldMaterialTools.IsAsync(a.tool)||ModelAssetTools.IsAsync(a.tool)||WebResearchTools.IsAsync(a.tool)))!=true)return Execute(plan,routes,executionContext);
            try
            {
                Validate(plan);
                var command=plan.actions.Single();
                Action admit=()=>{token.ThrowIfCancellationRequested();Validate(plan);beforeMutation?.Invoke();};
                var result=WebResearchTools.Handles(command.tool)?await WebResearchTools.ExecuteAsync(command,token,admit,executionContext):ModelAssetTools.IsAsync(command.tool)?await ModelAssetTools.ImportAsync(command,token,admit):await WorldMaterialTools.ExecuteAsync(command,token,admit);
                return new EditorBatchResult{Success=result.Success,Error=result.Error,Summary="Выполнен инструмент "+command.tool,Results=new List<EditorCommandResult>{result}};
            }
            catch(OperationCanceledException){throw;}
            catch(Exception error)
            {
                return new EditorBatchResult{Success=false,Error=error.Message,Summary="Инструмент не выполнен.",Results=new List<EditorCommandResult>{new EditorCommandResult{Success=false,Tool=plan?.actions?.FirstOrDefault()?.tool??"batch",Error=error.Message}}};
            }
        }

        public static EditorBatchResult Execute(EditorPlan plan,OrchestratorProfile routes=null,AgentExecutionContext executionContext=null)
        {
            var result=new EditorBatchResult();var tx=new SceneBuildTransaction();int undo=-1;
            try
            {
                plan=SceneCreationDefaults.Resolve(plan);
                Validate(plan);
                if(plan.actions.Any(a=>WorldMaterialTools.IsAsync(a.tool)||ModelAssetTools.IsAsync(a.tool)||WebResearchTools.IsAsync(a.tool)))throw new ArgumentException("Use await SceneToolExecutor.ExecuteAsync for network/resource operations; synchronous scene transactions never wait for network I/O.");
                if(plan.actions.Count==0){result.Success=true;result.Summary="Нет редакторских действий.";return result;}
                if(routes&&plan.actions.Any(a=>a.tool=="create_npc"||a.tool=="create_village"||a.tool=="configure_npc_ai"&&(bool?)a.args["assign_selected_model"]==true)){routes.Resolve(ModelTask.Dialogue).Validate();routes.Resolve(ModelTask.Planning).Validate();}
                if(plan.actions.Any(a=>!IsReadOnlyTool(a.tool))){Undo.IncrementCurrentGroup();undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("WorldWeaver assistant batch");result.UndoGroup=undo;}
                var aliases=new Dictionary<string,GameObject>(StringComparer.Ordinal);var createdNpcs=new List<IntelligentNPC>();var changedNpcs=new List<IntelligentNPC>();OrchestratorProfile actualRoutes=routes;
                GameObject group=null;
                Func<GameObject> getGroup=()=>group?group:group=OwnedGroup(tx);
                Func<string,GameObject> resolve=s=>aliases.TryGetValue(s,out var o)&&o?o:ResolveTarget(s);
                foreach(var command in plan.actions)
                {
                    int assetStart=tx.Assets.Count;var a=command.args;GameObject go=null;string summary="";
                    if(ModelAssetTools.Handles(command.tool)){result.Results.Add(ModelAssetTools.Execute(command,resolve,tx));continue;}
                    if(command.tool=="material_apply")
                    {result.Results.Add(WorldMaterialTools.Apply(command,resolve,tx));continue;}
                    if(AgentProjectTools.Handles(command.tool))
                    {
                        var projectResult=AgentProjectTools.Execute(command,resolve,executionContext);
                        result.Results.Add(projectResult);
                        if(!projectResult.Success)throw new InvalidOperationException(projectResult.Error??"Project command failed.");
                        continue;
                    }
                    if(ConstructiveTools.Handles(command.tool))
                    {
                        var outcome=ConstructiveTools.Execute(command,tx,ConstructiveTools.IsReadOnly(command.tool)?null:getGroup().transform,resolve);go=outcome.Target;
                        if(command.tool=="verify_scene"&&(bool?)JObject.Parse(outcome.Summary)["success"]!=true)throw new InvalidOperationException("Scene assertions failed: "+outcome.Summary);
                        if(go&&!string.IsNullOrWhiteSpace(command.name)){aliases["@"+command.name]=go;aliases[command.name]=go;}
                        result.Results.Add(new EditorCommandResult{Success=true,Tool=command.tool,Target=go?Id(go):command.target,Summary=outcome.Summary,CreatedAssets=tx.Assets.Skip(assetStart).ToList()});continue;
                    }
                    if(AnimatedCharacterTools.Handles(command.tool))
                    {
                        var characterParent=string.IsNullOrEmpty(command.target)?getGroup().transform:resolve(command.target).transform;
                        var outcome=AnimatedCharacterTools.Execute(command,tx,characterParent,resolve,routes);go=outcome.Target;
                        if(go){string characterName=string.IsNullOrWhiteSpace(command.name)?go.name:command.name;aliases["@"+characterName]=go;aliases[characterName]=go;}
                        result.Results.Add(new EditorCommandResult{Success=true,Tool=command.tool,Target=go?Id(go):command.target,Summary=outcome.Summary,CreatedAssets=tx.Assets.Skip(assetStart).ToList()});continue;
                    }
                    if(WorldGameplayTools.Handles(command.tool))
                    {
                        Transform gameplayParent=null;
                        if(command.tool=="create_village"||command.tool=="instantiate_prefab")gameplayParent=string.IsNullOrEmpty(command.target)?getGroup().transform:resolve(command.target).transform;
                        var outcome=WorldGameplayTools.Execute(command,tx,gameplayParent,resolve,routes);go=outcome.Target;summary=outcome.Summary;
                        if(go&&(command.tool=="create_village"||command.tool=="instantiate_prefab")){aliases["@"+command.name]=go;aliases[command.name]=go;}
                        result.Results.Add(new EditorCommandResult{Success=true,Tool=command.tool,Target=go?Id(go):command.target,Summary=summary,CreatedAssets=tx.Assets.Skip(assetStart).ToList()});continue;
                    }
                    Transform parent=null;
                    if(new[]{"create_npc","create_object","create_camera"}.Contains(command.tool))parent=string.IsNullOrEmpty(command.target)?getGroup().transform:resolve(command.target).transform;
                    switch(command.tool)
                    {
                        case "create_world":go=WorldComposer.World(tx,getGroup().transform,command.name,a);summary="Мир создан: рельеф, вода, освещение, камера и окружение.";break;
                        case "update_world":go=resolve(command.target);UpdateWorld(tx,go,a);WorldGameplayTools.RevalidateAfterTerrainUpdate(tx,go);summary="Рельеф, материалы земли, вода, опорные высоты и навигация обновлены.";break;
                        case "create_npc":
                            if(!getGroup().GetComponent<WorldPlayground>())Undo.AddComponent<WorldPlayground>(getGroup());
                            go=WorldComposer.Npc(tx,parent,command.name,(string)a["role"]??"Житель");Undo.AddComponent<Inventory>(go);var entity=Undo.AddComponent<WorldEntity>(go);entity.Id=command.name;entity.Interactable=true;
                            var npc=Undo.AddComponent<IntelligentNPC>(go);npc.CharacterId="npc-"+tx.BatchId.Substring(0,8)+"-"+SceneBuildTransaction.SafeName(command.name);npc.NpcName=command.name;npc.Role=(string)a["role"]??"Житель";npc.Personality="Доброжелательный житель, говорит кратко и следует известным фактам.";npc.Biography="Живёт в созданном мире.";npc.Goals="Помогать посетителям и заниматься своим делом.";npc.Constraints="Не утверждать неизвестные факты. Не передавать вещи без согласия.";
                            actualRoutes=actualRoutes?actualRoutes:CreateRoutes(tx);npc.Models=actualRoutes;NpcValues(tx,npc,a,true);ApplyNpcTransform(go,a,FindTerrain(parent));createdNpcs.Add(npc);changedNpcs.Add(npc);summary="Персонаж создан: внешность, знания, инвентарь и маршруты моделей.";break;
                        case "create_object":
                            go=WorldComposer.ObjectOfKind(tx,parent,command.name,(string)a["kind"],Style(parent));var worldEntity=Undo.AddComponent<WorldEntity>(go);worldEntity.Id=command.name;worldEntity.Interactable=true;
                            if(a["inventory"]!=null){Undo.AddComponent<Inventory>(go);SetInventory(go.GetComponent<Inventory>(),(JObject)a["inventory"]);}
                            ApplyTransform(go,a,FindTerrain(parent));if(a["color"]!=null)MaterialValues(tx,go,a,true);summary="Создан объект «"+command.name+"» ("+(string)a["kind"]+").";break;
                        case "scatter_objects":
                            var scatterTarget=resolve(command.target);var terrain=FindTerrain(scatterTarget.transform);go=tx.Empty(command.name,scatterTarget.transform,"scatter");
                            WorldComposer.Populate(tx,go.transform,terrain,(string)a["kind"],(int?)a["count"]??1,new System.Random((int?)a["seed"]??42),new List<Vector3>(),Style(scatterTarget.transform),a["center"]!=null?(Vector3?)Vector(a["center"],"center"):null,a["size"] is JArray size?(Vector2?)new Vector2((float)size[0],(float)size[1]):null,(float?)a["spacing"]??0);
                            summary="Размещено объектов: "+go.transform.childCount+" ("+(string)a["kind"]+").";if(go.transform.childCount!=((int?)a["count"]??1))throw new InvalidOperationException("Scatter read-back count mismatch.");break;
                        case "set_transform":go=resolve(command.target);Undo.RecordObject(go.transform,"Assistant transform");ApplyTransform(go,a,null);summary="Положение объекта «"+go.name+"» изменено.";break;
                        case "set_material":go=resolve(command.target);MaterialValues(tx,go,a,(bool?)a["children"]??false);summary="Новый материал назначен. Исходные общие материалы сохранены.";break;
                        case "set_lighting":
                            go=string.IsNullOrEmpty(command.target)?tx.Empty(command.name,getGroup().transform,"light"):resolve(command.target);var light=go.GetComponent<Light>();if(!light){light=Undo.AddComponent<Light>(go);light.type=LightType.Directional;}
                            Undo.RecordObject(light,"Assistant light");Undo.RecordObject(go.transform,"Assistant light rotation");if(a["type"]!=null)light.type=(string)a["type"]=="point"?LightType.Point:(string)a["type"]=="spot"?LightType.Spot:LightType.Directional;if(a["position"]!=null)go.transform.position=Vector(a["position"],"position");if(a["range"]!=null)light.range=(float)a["range"];if(a["spot_angle"]!=null)light.spotAngle=(float)a["spot_angle"];if(a["intensity"]!=null)light.intensity=(float)a["intensity"];if(a["color"]!=null)light.color=ColorValue(a["color"]);if(a["rotation"]!=null)go.transform.eulerAngles=Vector(a["rotation"],"rotation");if(a["shadows"]!=null)light.shadows=(bool)a["shadows"]?LightShadows.Soft:LightShadows.None;
                            // New point/spot placement is terrain-aware; edits remain exact unless explicitly opted in.
                            bool alignLight=(bool?)a["on_terrain"]==true||(string.IsNullOrEmpty(command.target)&&(bool?)a["on_terrain"]!=false);
                            Terrain lightTerrain=null;
                            bool liftedLight=alignLight&&LiftLightAboveTerrain(light,out lightTerrain);
                            // Explicit positioning/opt-out and Directional lights must not retain an old
                            // serialized terrain dependency. Parameter-only edits preserve that dependency.
                            bool clearLightAnchor=(bool?)a["on_terrain"]==false||light.type==LightType.Directional||
                                (!string.IsNullOrEmpty(command.target)&&a["position"]!=null&&!alignLight)||(alignLight&&!lightTerrain);
                            var previousLightAnchor=go.GetComponent<WorldSurfaceAnchor>();
                            if(clearLightAnchor&&previousLightAnchor)Undo.DestroyObjectImmediate(previousLightAnchor);
                            else if(alignLight)WorldGameplayTools.MarkLightAnchor(light,lightTerrain);
                            if(a["intensity"]!=null&&!Mathf.Approximately(light.intensity,(float)a["intensity"]))throw new InvalidOperationException("Light read-back failed.");summary=liftedLight?"Параметры света применены; источник поднят над рельефом и водой с сохранением XZ.":"Параметры источника света применены.";break;
                        case "create_camera":
                            go=tx.Empty(command.name,parent,"camera");var cam=Undo.AddComponent<Camera>(go);ApplyTransform(go,a,null);if(a["look_at"]!=null)go.transform.LookAt(Vector(a["look_at"],"look_at"));cam.fieldOfView=(float?)a["field_of_view"]??50;cam.orthographic=(bool?)a["orthographic"]??false;cam.orthographicSize=(float?)a["size"]??10;cam.farClipPlane=4000;cam.enabled=(bool?)a["enabled"]??!SceneObjects().Select(o=>o.GetComponent<Camera>()).Any(c=>c&&c!=cam&&c.enabled);if(a["background"]!=null){cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=ColorValue(a["background"]);}summary="Камера создана. Существующие камеры сохранены.";break;
                        case "update_npc":go=resolve(command.target);var update=go.GetComponent<IntelligentNPC>();NpcValues(tx,update,a,false);changedNpcs.Add(update);summary="Настройки персонажа «"+update.NpcName+"» обновлены.";break;
                        case "attach_behavior":
                            go=resolve(command.target);var behavior=go.GetComponent<SceneBehavior>();if(!behavior)behavior=Undo.AddComponent<SceneBehavior>(go);Undo.RecordObject(behavior,"Assistant behavior");string mode=(string)a["behavior"];behavior.Mode=mode=="rotate"?SceneBehaviorKind.Rotate:mode=="bob"?SceneBehaviorKind.Bob:mode=="patrol"?SceneBehaviorKind.Patrol:SceneBehaviorKind.FollowPlayer;behavior.Speed=(float?)a["speed"]??(mode=="rotate"?30:2);behavior.Amplitude=(float?)a["amplitude"]??.4f;behavior.Frequency=(float?)a["frequency"]??1;behavior.Axis=a["axis"]!=null?Vector(a["axis"],"axis"):Vector3.up;behavior.Waypoints=a["points"] is JArray points?points.Select(p=>Vector(p,"point")).ToArray():Array.Empty<Vector3>();behavior.FollowTarget=a["follow_target"]!=null?resolve((string)a["follow_target"]).transform:null;behavior.StoppingDistance=(float?)a["stopping_distance"]??2;summary="Подключено поведение "+mode+".";break;
                        case "delete_object":go=resolve(command.target);string deleted=go.name;Undo.DestroyObjectImmediate(go);go=null;summary="Удалён собственный объект "+deleted+".";break;
                        case "save_scene":
                            string path=(string)a["path"]??"Assets/WorldWeaverGenerated/Scenes/World.unity";EnsureFolders(path,tx);path=AssetDatabase.GenerateUniqueAssetPath(path);tx.Assets.Add(path);
                            InitializeNpcContexts(createdNpcs);ValidateNpcDependencies(changedNpcs);SaveNewAssets(tx);
                            if(!EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),path)||!File.Exists(path)||!AssetDatabase.LoadAssetAtPath<SceneAsset>(path))throw new IOException("Scene save/read-back failed.");summary="Сцена сохранена: "+path;break;
                        case "inspect_object":go=resolve(command.target);summary=Inspect(go).ToString(Newtonsoft.Json.Formatting.None);break;
                        case "search_project":summary=SearchProjectObservation(a).ToString(Newtonsoft.Json.Formatting.None);break;
                        case "read_script":
                            summary=ReadScriptObservation((string)a["path"]).ToString(Newtonsoft.Json.Formatting.None);break;
                    }
                    if(go&&new[]{"create_world","create_npc","create_object","scatter_objects","create_camera","set_lighting"}.Contains(command.tool))
                    {aliases["@"+command.name]=go;aliases[command.name]=go;}
                    if(go&&go.scene!=SceneManager.GetActiveScene())throw new InvalidOperationException("Created/edited object escaped active scene.");
                    result.Results.Add(new EditorCommandResult{Success=true,Tool=command.tool,Target=go?Id(go):command.target,Summary=summary,CreatedAssets=tx.Assets.Skip(assetStart).ToList()});
                }
                if(!plan.actions.Any(a=>a.tool=="save_scene")){InitializeNpcContexts(createdNpcs);ValidateNpcDependencies(changedNpcs);SaveNewAssets(tx);}
                foreach(var obj in tx.Objects.Where(o=>o))if(!obj.GetComponent<GeneratedSceneObject>())throw new InvalidOperationException("Ownership read-back failed.");
                if(tx.Objects.Count>BatchLimit||SceneObjects().Count(o=>o.GetComponent<GeneratedSceneObject>())>SceneLimit)throw new InvalidOperationException("Object budget read-back failed.");
                if(undo>=0)Undo.CollapseUndoOperations(undo);result.Success=true;result.Summary="Выполнено инструментов: "+result.Results.Count+". Создано объектов: "+tx.Objects.Count+". Undo отменяет пакет изменений сцены.";return result;
            }
            catch(Exception error)
            {
                string rollbackError=null;
                try{if(undo>=0)Undo.RevertAllDownToGroup(undo);tx.RollbackAssets();}catch(Exception rollback){rollbackError=rollback.Message;}
                result.Success=false;result.UndoGroup=-1;result.Error=error.Message+(rollbackError==null?"":"; rollback error: "+rollbackError);result.Summary="Пакет не применён; изменения отменены.";
                foreach(var step in result.Results){step.Success=false;step.Summary="Отменено вместе с пакетом: "+step.Summary;}
                result.Results.Add(new EditorCommandResult{Success=false,Tool="batch",Error=result.Error,Summary=result.Summary});return result;
            }
        }
        private static void InitializeNpcContexts(IEnumerable<IntelligentNPC> createdNpcs)
        {
            foreach(var npc in createdNpcs.Where(n=>n))
            {
                Undo.RecordObject(npc,"Assistant entity context");npc.KnownEntities=SceneObjects().Select(o=>o.GetComponent<WorldEntity>()).Where(e=>e&&e.gameObject!=npc.gameObject).Take(32).ToList();EditorUtility.SetDirty(npc);
            }
        }
        private static void ValidateNpcDependencies(IEnumerable<IntelligentNPC> changedNpcs)
        {
            foreach(var npc in changedNpcs.Where(n=>n).Distinct())
            {
                if(!npc.Knowledge||!npc.Models||!npc.Models.Dialogue||!npc.Models.Planning||!npc.GetComponent<Inventory>()||!npc.GetComponent<WorldEntity>())throw new InvalidOperationException("NPC dependency read-back failed.");
            }
        }
        private static void SaveNewAssets(SceneBuildTransaction tx)
        {
            foreach(string path in tx.Assets)
            {
                var asset=AssetDatabase.LoadMainAssetAtPath(path);
                if(asset is Material material&&material.HasProperty("_BaseMap")&&material.HasProperty("_MainTex"))
                {
                    // URP synchronizes this legacy alias on a later import. Seal
                    // the already canonical state instead of seeing that flush as
                    // a manual edit in the next tool. Only this batch's new assets.
                    material.SetTexture("_MainTex",material.GetTexture("_BaseMap"));
                    material.SetTextureScale("_MainTex",material.GetTextureScale("_BaseMap"));material.SetTextureOffset("_MainTex",material.GetTextureOffset("_BaseMap"));
                }
                if(asset){EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);}
            }
        }
        private static GameObject OwnedGroup(SceneBuildTransaction tx)
        {
            var existing=SceneObjects().FirstOrDefault(o=>o.GetComponent<GeneratedSceneObject>()&&o.GetComponent<GeneratedSceneObject>().Kind=="group");
            return existing?existing:tx.Empty("WorldWeaverGenerated",null,"group");
        }
        private static Terrain FindTerrain(Transform parent)
        {
            if(parent){var local=parent.GetComponentInChildren<Terrain>(true);if(local)return local;for(var t=parent.parent;t;t=t.parent){local=t.GetComponentInChildren<Terrain>(true);if(local)return local;}}
            return SceneObjects().Select(o=>o.GetComponent<Terrain>()).FirstOrDefault(t=>t&&t.GetComponent<GeneratedTerrain>());
        }
        private static bool LiftLightAboveTerrain(Light light,out Terrain lightTerrain)
        {
            lightTerrain=null;
            if(light.type!=LightType.Point&&light.type!=LightType.Spot)return false;
            var position=light.transform.position;
            float minimum=float.NegativeInfinity;
            foreach(var terrain in SceneObjects().Select(o=>o.GetComponent<Terrain>()).Where(t=>t&&t.enabled&&t.gameObject.activeInHierarchy&&t.terrainData))
            {
                var origin=terrain.transform.position;var size=terrain.terrainData.size;
                // Never sample an unrelated edge outside this terrain's actual world footprint.
                if(position.x<origin.x||position.x>origin.x+size.x||position.z<origin.z||position.z>origin.z+size.z)continue;
                float surface=terrain.SampleHeight(position)+origin.y;
                var generated=terrain.GetComponent<GeneratedTerrain>();
                if(generated&&generated.Specification!=null)surface=Mathf.Max(surface,origin.y+generated.Specification.waterLevel*generated.Specification.height);
                if(surface+3f>minimum){minimum=surface+3f;lightTerrain=terrain;}
            }
            if(float.IsNegativeInfinity(minimum)||position.y>=minimum)return false;
            // Undo.RecordObject(transform) is captured by the caller before this one-time adjustment.
            position.y=minimum;light.transform.position=position;
            if(Mathf.Abs(light.transform.position.y-minimum)>.001f)throw new InvalidOperationException("Light terrain placement read-back failed.");
            // Deliberately leave SurfaceAnchored=false: UpdateWorld's ground anchor has no light offset.
            return true;
        }
        private static string Style(Transform parent)
        {for(var t=parent;t;t=t.parent){var own=t.GetComponent<GeneratedSceneObject>();if(own&&!string.IsNullOrEmpty(own.Style))return own.Style;}return "forest";}
        private static void ApplyNpcTransform(GameObject go,JObject args,Terrain terrain)
        {
            ApplyTransform(go,args,null);
            var marker=go.GetComponent<GeneratedSceneObject>();if(marker){Undo.RecordObject(marker,"NPC ground dependency");marker.SurfaceAnchored=terrain&&(bool?)args["on_terrain"]!=false;}
            if(!terrain||(bool?)args["on_terrain"]==false)return;
            Vector3 requested=go.transform.position;Physics.SyncTransforms();
            var data=terrain.terrainData;var origin=terrain.transform.position;
            var spec=terrain.GetComponent<GeneratedTerrain>().Specification;
            Vector3 center=new Vector3(Mathf.Clamp(requested.x,origin.x+1,origin.x+data.size.x-1),0,Mathf.Clamp(requested.z,origin.z+1,origin.z+data.size.z-1));
            float water=origin.y+spec.waterLevel*spec.height;
            Func<Vector3,bool> dry=p=>p.x>=origin.x+1&&p.x<=origin.x+data.size.x-1&&p.z>=origin.z+1&&p.z<=origin.z+data.size.z-1&&p.y>=water+.35f&&data.GetSteepness((p.x-origin.x)/data.size.x,(p.z-origin.z)/data.size.z)<38&&WorldSitePlacement.ActorClear(terrain,go,p);
            Vector3 candidate=WorldComposer.Ground(terrain,center);
            if(dry(candidate)){go.transform.position=candidate;return;}
            float step=Mathf.Max(1,Mathf.Min(data.size.x,data.size.z)/40);
            float phase=(spec.seed%12)*Mathf.PI/6;
            for(int radius=1;radius<=20;radius++)for(int sector=0;sector<12;sector++)
            {
                float angle=phase+sector*Mathf.PI/6;
                candidate=WorldComposer.Ground(terrain,center+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius*step);
                if(dry(candidate)){go.transform.position=candidate;return;}
            }
            throw new InvalidOperationException("Не найдено сухое свободное место для персонажа рядом с указанной точкой. Измените положение или уровень воды; on_terrain=false сохраняет точные координаты.");
        }
        private static void ApplyTransform(GameObject go,JObject a,Terrain terrain)
        {
            if(a["rotation"]!=null)go.transform.eulerAngles=Vector(a["rotation"],"rotation");if(a["scale"]!=null)go.transform.localScale=Vector(a["scale"],"scale",.01f,100);
            if(a["position"]!=null)go.transform.position=Vector(a["position"],"position");
            if(terrain&&(bool?)a["on_terrain"]!=false)go.transform.position=GroundPoint(go,terrain,go.transform.position);
            if(a["position"]!=null&&!terrain&&(go.transform.position-Vector(a["position"],"position")).sqrMagnitude>.00001f)throw new InvalidOperationException("Положение не совпало с заданным.");
            if(a["scale"]!=null&&(go.transform.localScale-Vector(a["scale"],"scale")).sqrMagnitude>.00001f)throw new InvalidOperationException("Масштаб не совпал с заданным.");
        }
        internal static Vector3 GroundPoint(GameObject go,Terrain terrain,Vector3 point)
        {point=WorldComposer.Ground(terrain,point);var filter=go.GetComponent<MeshFilter>();if(filter&&filter.sharedMesh)point.y+=filter.sharedMesh.bounds.extents.y*Mathf.Abs(go.transform.lossyScale.y);return point;}
        private static void MaterialValues(SceneBuildTransaction tx,GameObject go,JObject a,bool children)
        {
            var renderers=children?go.GetComponentsInChildren<Renderer>(true):new[]{go.GetComponent<Renderer>()};var material=tx.Material("Assistant color",ColorValue(a["color"]),(float?)a["smoothness"]??.2f);
            foreach(var renderer in renderers){if(!renderer)throw new ArgumentException("No renderer on material target.");Undo.RecordObject(renderer,"Assistant material");var slots=renderer.sharedMaterials;if(slots.Length==0)slots=new Material[1];for(int i=0;i<slots.Length;i++)slots[i]=material;renderer.sharedMaterials=slots;if(renderer.sharedMaterials.Any(m=>m!=material))throw new InvalidOperationException("Material read-back failed.");}
        }
        private static OrchestratorProfile CreateRoutes(SceneBuildTransaction tx)
        {
            var terrain=ScriptableObject.CreateInstance<ModelProfile>();terrain.Model="qwen3.5:2b";tx.Asset(terrain,"TerrainModel");
            var dialogue=ScriptableObject.CreateInstance<ModelProfile>();dialogue.Model="qwen2.5-coder:latest";tx.Asset(dialogue,"DialoguePlanningModel");
            var profile=ScriptableObject.CreateInstance<OrchestratorProfile>();profile.Terrain=terrain;profile.Dialogue=dialogue;profile.Planning=dialogue;return tx.Asset(profile,"LocalRoutes");
        }
        private static void NpcValues(SceneBuildTransaction tx,IntelligentNPC npc,JObject a,bool create)
        {
            Undo.RecordObject(npc,"Assistant NPC settings");
            if(a["role"]!=null)npc.Role=(string)a["role"];if(a["personality"]!=null)npc.Personality=(string)a["personality"];if(a["biography"]!=null)npc.Biography=(string)a["biography"];if(a["goals"]!=null)npc.Goals=(string)a["goals"];if(a["constraints"]!=null)npc.Constraints=(string)a["constraints"];if(a["autonomous"]!=null)npc.Autonomous=(bool)a["autonomous"];
            if(create||a["knowledge"]!=null)
            {
                var knowledge=ScriptableObject.CreateInstance<KnowledgeBase>();var token=a["knowledge"];var texts=token is JArray list?list.Select(v=>(string)v).ToArray():new[]{(string)token??("Имя: "+npc.NpcName+". Роль: "+npc.Role+". Биография: "+npc.Biography+". Цели: "+npc.Goals)};
                for(int i=0;i<texts.Length;i++)if(!string.IsNullOrWhiteSpace(texts[i]))knowledge.Documents.Add(new KnowledgeDocument{Id="knowledge-"+i,Scope=npc.CharacterId,Title=npc.NpcName+" "+(i+1),Text=texts[i],Kind=MemoryKind.StaticKnowledge});
                npc.Knowledge=tx.Asset(knowledge,npc.NpcName+"Knowledge");
            }
            if(a["inventory"]!=null)SetInventory(npc.GetComponent<Inventory>(),(JObject)a["inventory"]);
            EditorUtility.SetDirty(npc);
        }
        private static void SetInventory(Inventory inventory,JObject values)
        {
            if(!inventory)throw new InvalidOperationException("Inventory component missing.");Undo.RecordObject(inventory,"Assistant inventory");
            var serialized=new SerializedObject(inventory);var items=serialized.FindProperty("items");items.arraySize=values.Count;int i=0;
            foreach(var pair in values.Properties()){var item=items.GetArrayElementAtIndex(i++);item.FindPropertyRelative("Item").stringValue=pair.Name;item.FindPropertyRelative("Amount").intValue=(int)pair.Value;}
            inventory.Capacity=10000;serialized.ApplyModifiedProperties();foreach(var pair in values.Properties())if(inventory.Count(pair.Name)!=(int)pair.Value)throw new InvalidOperationException("Inventory read-back failed.");
        }
        private static void UpdateWorld(SceneBuildTransaction tx,GameObject target,JObject args)
        {
            var generated=target.GetComponentInChildren<GeneratedTerrain>(true);var terrain=generated.GetComponent<Terrain>();if(!terrain||!terrain.terrainData)throw new InvalidOperationException("Terrain data missing.");
            var root=generated.transform.parent?generated.transform.parent:generated.transform;string style=(string)args["style"]??Style(root);var spec=WorldComposer.Specification(args,style,generated.Specification);
            // Preserve constructed features when resizing/rebuilding the owned world.
            var environment=root.GetComponent<WorldEnvironment>();
            if(style=="moon"&&!environment){environment=Undo.AddComponent<WorldEnvironment>(root.gameObject);environment.Terrain=terrain;environment.Profile="moon";environment.Craters=spec.width<96||spec.length<96?new List<ImpactCrater>():ExplorationTerrain.Craters(spec.seed,Mathf.Min(8,Mathf.Max(1,(int)(spec.width*spec.length/12000))),spec.width,spec.length);}
            if(environment)Undo.RecordObject(environment,"Preserve environment features");
            var caves=root.GetComponentsInChildren<WorldCave>();
            if(caves.Length>0)
            {
                int required=Mathf.NextPowerOfTwo(Mathf.CeilToInt(Mathf.Max(spec.width,spec.length)/(caves.Min(c=>c.Size.x)/4)))+1;
                if(required>2049)throw new ArgumentException("Existing cave entrances need a finer grid than 2049 after this resize. Use smaller extents or wider caves.");
                spec.resolution=Mathf.Max(spec.resolution,required);
            }
            // Clone even owned data before editing, so imported/shared TerrainData is never modified.
            var data=tx.Asset(Object.Instantiate(terrain.terrainData),"UpdatedTerrain");Undo.RecordObject(terrain,"Assistant terrain data");var collider=terrain.GetComponent<TerrainCollider>();Undo.RecordObject(collider,"Assistant terrain collider");Undo.RecordObject(generated,"Assistant TerrainSpec");Undo.RecordObject(terrain.transform,"Assistant terrain extent");
            terrain.terrainData=data;collider.terrainData=data;data.heightmapResolution=spec.resolution;data.size=new Vector3(spec.width,spec.height,spec.length);data.SetHeights(0,0,environment&&environment.Profile=="moon"?ExplorationTerrain.Generate(spec,environment.Craters,true):TerrainGenerator.Generate(spec));WorldComposer.Layers(tx,data,spec,style);generated.Specification=spec;terrain.transform.localPosition=new Vector3(-spec.width/2,0,-spec.length/2);
            TerrainConstruction.ReanchorCaves(terrain);
            foreach(var survival in ProjectContextIndex.Objects().Select(g=>g.GetComponent<WorldSurvival>()).Where(s=>s&&s.Environment&&s.Environment.Terrain==terrain))
            {
                Undo.RecordObject(survival.transform,"Reanchor linked survival player");var p=survival.transform.position;p.y=terrain.SampleHeight(p)+terrain.transform.position.y+.1f;survival.transform.position=p;
                if(survival.Habitat){Undo.RecordObject(survival.Habitat,"Reanchor survival habitat");var h=survival.Habitat.position;h.y=terrain.SampleHeight(h)+terrain.transform.position.y;survival.Habitat.position=h;Undo.RecordObject(survival,"Reanchor survival spawn");survival.Spawn=h+Vector3.up*.1f;}
            }
            var rootMarker=root.GetComponent<GeneratedSceneObject>();if(rootMarker){Undo.RecordObject(rootMarker,"Assistant world style");rootMarker.Style=style;}
            var water=root.Find("Water");if(water&&water.GetComponent<GeneratedSceneObject>()){Undo.RecordObject(water,"Assistant water");water.localPosition=new Vector3(0,spec.waterLevel*spec.height,0);water.localScale=new Vector3(spec.width/10,1,spec.length/10);}
            foreach(var marker in root.GetComponentsInChildren<GeneratedSceneObject>(true).Where(m=>m.SurfaceAnchored))
            {Undo.RecordObject(marker.transform,"Assistant ground anchor");marker.transform.position=GroundPoint(marker.gameObject,terrain,marker.transform.position);}
            if(terrain.terrainData!=data||collider.terrainData!=data||data.heightmapResolution!=spec.resolution)throw new InvalidOperationException("Terrain read-back failed.");
        }
        private static void EnsureFolders(string filePath,SceneBuildTransaction tx)
        {
            string[] parts=filePath.Split('/');string current="Assets";for(int i=1;i<parts.Length-1;i++){string next=current+"/"+parts[i];if(!AssetDatabase.IsValidFolder(next)){if(string.IsNullOrEmpty(AssetDatabase.CreateFolder(current,parts[i])))throw new IOException("Cannot create scene folder.");tx.Folders.Add(next);}current=next;}
        }
    }
}
