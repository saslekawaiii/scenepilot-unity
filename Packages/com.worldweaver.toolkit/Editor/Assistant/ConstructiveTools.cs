using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
using static WorldWeaver.Editor.Assistant.ToolArgumentValidation;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Composable operations, not per-prompt game generators. Every write uses the normal transaction.</summary>
    internal static class ConstructiveTools
    {
        internal const string Reference=@"create_node: name optional (empty resolves to a unique name), optional owned parent target; args {position?:[x,y,z]}. Empty owned GameObject for arbitrary component composition, not a visual placeholder.
set_parent: target existing owned object; args {parent:string,keep_world?:bool}. Reparent under another owned object, preserving world transform by default. Rejects cycles.
create_mesh: name optional (empty resolves to a unique name), optional owned parent; args {vertices:[[x,y,z]...],triangles:[integer...],uv?:[[u,v]...],color?:[r,g,b,a],collider?:bool}. 3..2048 vertices, 3..12000 indices divisible by three; explicit editable saved mesh, arbitrary geometry within bounds, not only primitives. Use correct winding and UVs. Complex imported art requires catalog_assets/instantiate_prefab, not pretending this is a pretrained 3D generator.
shape_terrain: target owned world/terrain; args {operation:crater|hill|flatten|lunar|refine,center?:[world_x,world_z],radius?:2..80,amount?:-30..30,resolution?:33|65|129|257|513,craters?:0..24,seed?:integer}. Clones TerrainData before modifying, measurements in metres. lunar builds a seeded lunar-style heightmap and crater records, not measured NASA geography; crater amount is a depth MAGNITUDE (both signs accepted), hill amount is a signed height delta. refine changes resolution by resampling existing heights/holes and preserves crater features; use it instead of update_world when only resolution is insufficient. Shape terrain BEFORE adding caves.
create_cave: name, target existing owned world/terrain; args {position:[world_x,world_z],length?:12..32,width?:4..10}. Creates a real walkable rock tube with a descending ramp, collision mesh and Terrain holes. Returns world entrance/interior for navigation. Do not sculpt under an existing cave.
configure_physics: target existing owned object; args {mass?:.01..10000,gravity?:bool,kinematic?:bool,collider?:box|sphere|capsule,friction?:0..1,bounce?:0..1}. Configures Rigidbody and collider; does not add a duplicate collider or mutate shared physics materials. A nonconvex mesh cannot be a dynamic body.
configure_audio: target existing owned object; args {clip:string,volume?:0..1,loop?:bool,spatial?:bool}. Existing imported AudioClip asset path only, not an invented URL; adds AudioSource and playOnAwake.
configure_animator: target existing owned object; args {controller:string}. Binds an actual AnimatorController asset path found through search_project; no claim that missing clips or a rig were generated.
create_ui_element: name optional (empty resolves to a unique name), optional owned Canvas/UI parent target; args {kind:canvas|panel|text|button|slider,text?:string,position?:[x,y],size?:[w,h],color?:[r,g,b,a],toggle_target?:string}. Creates real UGUI hierarchy and scaled overlay Canvas with EventSystem. UI position and size use PIXELS of a 1440x900 reference Canvas, not metres; coordinates are relative to the parent center. A button can toggle an explicitly addressed owned GameObject; custom actions need a compiled script.
configure_ui: target existing owned UI element; args {text?:string,position?:[x,y],size?:[w,h],color?:[r,g,b,a],visible?:bool,toggle_target?:string}. Configure layout in reference pixels or BIND AN EXISTING Button to toggle an owned target. This needs no new C# script. Prefer configuring the existing button over recreating it.
verify_scene: standalone read-only, empty target, args {checks:[{target:string,kind:component|active|mesh|terrain_height|path,type?:string,expected?:number,to?:string}]}. Independent live read-back: component uses full type/name, mesh checks saved vertices and collision, terrain_height compares sample at target center against expected metres, path checks actual NavMesh PathComplete. A failed assertion returns a failed tool result, not a successful narrative.
configure_survival: target existing owned WorldPlayer; args {world:string,oxygen_seconds?:30..1800,gravity?:.1..30}. Adds reusable oxygen/health/exposure loop, visible HUD and pressurized recovery habitat at the player's grounded position. Bare caves protect from exposure but do not generate oxygen. No global Physics.gravity changes.
create_resource: name, target owned world; args {kind:oxygen|sample,position:[x,y,z]}. Finite E-interactable oxygen reserve or collectible mission sample; snaps to ground. Survival mission is collect three samples and return to habitat.";
        static Dictionary<string,JObject> Definitions()=>new Dictionary<string,JObject>{
            ["create_node"]=Obj(new JObject{["position"]=Vector()}),
            ["set_parent"]=Obj(new JObject{["parent"]=Text(200),["keep_world"]=Bool()},"parent"),
            ["create_mesh"]=Obj(new JObject{["vertices"]=Array(Vector(),3,2048),["triangles"]=Array(Integer(0,2047),3,12000),["uv"]=Array(Vector(2,-1000,1000),3,2048),["color"]=Vector(4,0,1),["collider"]=Bool()},"vertices","triangles"),
            ["shape_terrain"]=Obj(new JObject{["operation"]=Enum("crater","hill","flatten","lunar","refine"),["center"]=Vector(2),["radius"]=Number(2,80),["amount"]=Number(-30,30),["resolution"]=new JObject{["type"]="integer",["enum"]=new JArray(33,65,129,257,513)},["craters"]=Integer(0,24),["seed"]=Integer(int.MinValue,int.MaxValue)},"operation"),
            ["create_cave"]=Obj(new JObject{["position"]=Vector(2),["length"]=Number(12,32),["width"]=Number(4,10)},"position"),
            ["configure_physics"]=Obj(new JObject{["mass"]=Number(.01,10000),["gravity"]=Bool(),["kinematic"]=Bool(),["collider"]=Enum("box","sphere","capsule"),["friction"]=Number(0,1),["bounce"]=Number(0,1)}),
            ["configure_audio"]=Obj(new JObject{["clip"]=Text(240),["volume"]=Number(0,1),["loop"]=Bool(),["spatial"]=Bool()},"clip"),
            ["configure_animator"]=Obj(new JObject{["controller"]=Text(240)},"controller"),
            ["create_ui_element"]=Obj(new JObject{["kind"]=Enum("canvas","panel","text","button","slider"),["text"]=Text(2000),["position"]=Vector(2),["size"]=Vector(2,1,4096),["color"]=Vector(4,0,1),["toggle_target"]=Text(200)},"kind"),
            ["configure_ui"]=Obj(new JObject{["text"]=Text(2000),["position"]=Vector(2),["size"]=Vector(2,1,4096),["color"]=Vector(4,0,1),["visible"]=Bool(),["toggle_target"]=Text(200)}),
            ["verify_scene"]=Obj(new JObject{["checks"]=Array(Obj(new JObject{["target"]=Text(200),["kind"]=Enum("component","active","mesh","terrain_height","path"),["type"]=Text(240),["expected"]=new JObject{["anyOf"]=new JArray(Number(-100000,100000),Bool())},["to"]=Text(200)},"target","kind"),1,24)},"checks"),
            ["configure_survival"]=Obj(new JObject{["world"]=Text(200),["oxygen_seconds"]=Number(30,1800),["gravity"]=Number(.1,30)},"world"),
            ["create_resource"]=Obj(new JObject{["kind"]=Enum("oxygen","sample"),["position"]=Vector()},"kind","position")
        };
        internal static bool Handles(string tool)=>Definitions().ContainsKey(tool);
        internal static bool IsReadOnly(string tool)=>tool=="verify_scene";
        internal static IEnumerable<JObject> CommandSchemas()=>Definitions().Select(p=>Obj(new JObject{["tool"]=Enum(p.Key),["name"]=Text(),["target"]=Text(200),["args"]=p.Value},"tool","name","target","args"));
        internal static int Cost(EditorCommand c)=>c.tool=="configure_survival"?14:c.tool=="create_cave"?8:c.tool=="create_ui_element"?9:1;
        internal static void Validate(EditorCommand c,Func<string,GameObject> resolve)
        {
            ToolArgumentValidation.Validate(c.args,Definitions()[c.tool]);
            bool creation=new[]{"create_node","create_mesh","create_ui_element","create_cave","create_resource"}.Contains(c.tool);
            if(creation&&c.name.Length>0&&(string.IsNullOrWhiteSpace(c.name)||c.name.Any(ch=>char.IsControl(ch)||ch=='/'||ch=='\\')))throw new ArgumentException("Creation name must contain visible text and no path separators; leave it empty for an automatic unique name.");
            bool optionalParent=c.tool=="create_node"||c.tool=="create_mesh"||c.tool=="create_ui_element";
            GameObject target=null;
            if(c.tool=="verify_scene")
            {
                if(c.target!="")throw new ArgumentException("Verification target must be empty; use explicit targets per check.");
                foreach(var row in c.args["checks"]){resolve((string)row["target"]);if((string)row["kind"]=="path")resolve((string)row["to"]);}
                return;
            }
            if(!optionalParent||!string.IsNullOrEmpty(c.target))
            {target=resolve(c.target);if(!target.GetComponent<GeneratedSceneObject>())throw new ArgumentException("This constructive tool requires an owned object.");}
            var a=c.args;
            if(c.tool=="create_mesh")
            {
                var vertices=(JArray)a["vertices"];var indices=(JArray)a["triangles"];
                if(indices.Count%3!=0||indices.Any(i=>(int)i>=vertices.Count))throw new ArgumentException("Invalid mesh triangle indices.");
                if(a["uv"] is JArray uv&&uv.Count!=vertices.Count)throw new ArgumentException("One UV is required per mesh vertex.");
                for(int i=0;i<indices.Count;i+=3)if(Vector3.Cross(V(vertices[(int)indices[i+1]])-V(vertices[(int)indices[i]]),V(vertices[(int)indices[i+2]])-V(vertices[(int)indices[i]])).sqrMagnitude<1e-10f)throw new ArgumentException("Degenerate mesh triangle.");
            }
            if(c.tool=="set_parent")
            {var parent=resolve((string)a["parent"]);if(!parent.GetComponent<GeneratedSceneObject>()||parent==target||parent.transform.IsChildOf(target.transform))throw new ArgumentException("Invalid/cyclic owned hierarchy.");}
            if(c.tool=="shape_terrain"||c.tool=="create_cave")TerrainConstruction.Validate(c,target);
            if(c.tool=="configure_physics")
            {
                if(target.GetComponent<CharacterController>())throw new ArgumentException("Do not add a Rigidbody to a CharacterController player.");
                var mesh=target.GetComponent<MeshCollider>();bool kinematic=(bool?)a["kinematic"]??(target.GetComponent<Rigidbody>()?target.GetComponent<Rigidbody>().isKinematic:false);if(mesh&&!mesh.convex&&!kinematic&&a["collider"]==null)throw new ArgumentException("A dynamic MeshCollider must be convex; use a box/sphere/capsule or a static mesh.");
            }
            if(c.tool=="configure_audio"&&!AssetDatabase.LoadAssetAtPath<AudioClip>((string)a["clip"]))throw new ArgumentException("Find an actual AudioClip asset first.");
            if(c.tool=="configure_animator"&&!AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>((string)a["controller"]))throw new ArgumentException("Find an actual AnimatorController asset first.");
            if(c.tool=="configure_survival")
            {if(!target.GetComponent<WorldPlayer>())throw new ArgumentException("Survival target must be a WorldPlayer.");var world=resolve((string)a["world"]);if(!world.GetComponent<GeneratedSceneObject>()||!world.GetComponentInChildren<Terrain>())throw new ArgumentException("Survival world must be an owned terrain world.");}
            if(c.tool=="create_resource"&&!target.GetComponentInChildren<Terrain>())throw new ArgumentException("Resource parent must contain terrain.");
            if(c.tool=="create_ui_element"||c.tool=="configure_ui")UiConstruction.Validate(c,target,resolve);
        }
        internal static Vector3 V(JToken v)=>new Vector3((float)v[0],(float)v[1],(float)v[2]);
        internal static Color C(JToken v)=>v==null?Color.white:new Color((float)v[0],(float)v[1],(float)v[2],(float)v[3]);
        internal static WorldGameplayToolResult Execute(EditorCommand c,SceneBuildTransaction tx,Transform parent,Func<string,GameObject> resolve)
        {
            Validate(c,resolve);
            if(c.name.Length==0&&new[]{"create_node","create_mesh","create_ui_element","create_cave","create_resource"}.Contains(c.tool))
            {
                string label=c.tool=="create_node"?"Узел":c.tool=="create_mesh"?"Меш":c.tool=="create_cave"?"Пещера":c.tool=="create_resource"?"Ресурс":"Интерфейс";
                var names=UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Select(t=>t.name).ToHashSet();
                string name=label;for(int i=2;names.Contains(name);i++)name=label+" "+i;
                c=new EditorCommand{tool=c.tool,name=name,target=c.target,args=c.args}; // Resolve a default; the original audited model plan stays unchanged.
            }
            var a=c.args;GameObject go=string.IsNullOrEmpty(c.target)?null:resolve(c.target);JObject report=null;
            switch(c.tool)
            {
                case "create_node":go=tx.Empty(c.name,go?go.transform:parent,"node");if(a["position"]!=null)go.transform.position=V(a["position"]);break;
                case "set_parent":Undo.SetTransformParent(go.transform,resolve((string)a["parent"]).transform,"Reparent owned object");if((bool?)a["keep_world"]==false){Undo.RecordObject(go.transform,"Reset local transform");go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;}break;
                case "create_mesh":
                    go=tx.Empty(c.name,go?go.transform:parent,"mesh");
                    var mesh=tx.Asset(new Mesh{name=c.name,vertices=((JArray)a["vertices"]).Select(V).ToArray(),triangles=a["triangles"].Values<int>().ToArray()},c.name);
                    if(a["uv"] is JArray uv)mesh.uv=uv.Select(v=>new Vector2((float)v[0],(float)v[1])).ToArray();
                    mesh.RecalculateNormals();mesh.RecalculateBounds();Undo.AddComponent<MeshFilter>(go).sharedMesh=mesh;Undo.AddComponent<MeshRenderer>(go).sharedMaterial=tx.Material("Mesh "+c.name,C(a["color"]));
                    if((bool?)a["collider"]??true)Undo.AddComponent<MeshCollider>(go).sharedMesh=mesh;break;
                case "shape_terrain":report=TerrainConstruction.Shape(c,tx,go);break;
                case "create_cave":go=TerrainConstruction.Cave(c,tx,go);report=new JObject{["entrance"]=new JArray(go.GetComponent<WorldCave>().Entrance.x,go.GetComponent<WorldCave>().Entrance.y,go.GetComponent<WorldCave>().Entrance.z),["interior"]=new JArray(go.GetComponent<WorldCave>().Interior.x,go.GetComponent<WorldCave>().Interior.y,go.GetComponent<WorldCave>().Interior.z),["terrainHoles"]=true,["meshCollider"]=true};break;
                case "configure_physics":
                    var collider=go.GetComponent<Collider>();
                    if(collider&&a["collider"]!=null&&((string)a["collider"]=="box"&&!(collider is BoxCollider)||(string)a["collider"]=="sphere"&&!(collider is SphereCollider)||(string)a["collider"]=="capsule"&&!(collider is CapsuleCollider)))
                    {if(go.GetComponents<Collider>().Length!=1)throw new ArgumentException("Several colliders exist; choose a single-collider object before replacing its shape.");Undo.DestroyObjectImmediate(collider);collider=null;}
                    if(!collider)collider=(string)a["collider"]=="sphere"?(Collider)Undo.AddComponent<SphereCollider>(go):(string)a["collider"]=="capsule"?Undo.AddComponent<CapsuleCollider>(go):Undo.AddComponent<BoxCollider>(go);
                    var body=go.GetComponent<Rigidbody>();if(!body)body=Undo.AddComponent<Rigidbody>(go);Undo.RecordObject(body,"Configure physics");body.mass=(float?)a["mass"]??body.mass;body.useGravity=(bool?)a["gravity"]??body.useGravity;body.isKinematic=(bool?)a["kinematic"]??body.isKinematic;
                    if(a["friction"]!=null||a["bounce"]!=null){Undo.RecordObject(collider,"Assign own physics material");collider.sharedMaterial=tx.Asset(new PhysicsMaterial{name="Agent physics",dynamicFriction=(float?)a["friction"]??.5f,staticFriction=(float?)a["friction"]??.5f,bounciness=(float?)a["bounce"]??0},"Physics",".physicMaterial");}break;
                case "configure_audio":var audio=go.GetComponent<AudioSource>();if(!audio)audio=Undo.AddComponent<AudioSource>(go);Undo.RecordObject(audio,"Configure audio");audio.clip=AssetDatabase.LoadAssetAtPath<AudioClip>((string)a["clip"]);audio.volume=(float?)a["volume"]??1;audio.loop=(bool?)a["loop"]??false;audio.spatialBlend=(bool?)a["spatial"]==false?0:1;audio.playOnAwake=true;break;
                case "configure_animator":var animator=go.GetComponent<Animator>();if(!animator)animator=Undo.AddComponent<Animator>(go);Undo.RecordObject(animator,"Bind Animator");animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>((string)a["controller"]);break;
                case "create_ui_element":go=UiConstruction.Create(c,tx,go?go.transform:parent,resolve);break;
                case "configure_ui":UiConstruction.Configure(c,tx,go,resolve);break;
                case "configure_survival":SurvivalConstruction.Configure(c,tx,go,resolve);break;
                case "create_resource":go=SurvivalConstruction.Resource(c,tx,go);break;
                case "verify_scene":report=Verify(c,resolve);break;
            }
            if(report==null)report=new JObject{["object"]=go?go.name:"",["components"]=new JArray(go?go.GetComponents<Component>().Where(x=>x).Select(x=>x.GetType().FullName):Enumerable.Empty<string>())};
            if(go)ToolStateReadback.Add(report,go);
            return new WorldGameplayToolResult{Target=go,Summary=report.ToString(Formatting.None)};
        }
        internal static JObject Verify(EditorCommand c,Func<string,GameObject> resolve)
        {
            var rows=new JArray();
            foreach(var check in c.args["checks"])
            {
                var target=resolve((string)check["target"]);string kind=(string)check["kind"];bool pass=false;string actual="";
                if(kind=="component"){string type=(string)check["type"];pass=target.GetComponents<Component>().Any(x=>x&&(x.GetType().FullName==type||x.GetType().Name==type));if(check["expected"]?.Type==JTokenType.Boolean)pass=pass==(bool)check["expected"];actual=string.Join(",",target.GetComponents<Component>().Where(x=>x).Select(x=>x.GetType().Name));}
                else if(kind=="active"){bool active=target.activeInHierarchy;pass=active==(check["expected"]?.Type==JTokenType.Boolean?(bool)check["expected"]:true);actual=active.ToString();}
                else if(kind=="mesh"){var filter=target.GetComponent<MeshFilter>();pass=filter&&filter.sharedMesh&&filter.sharedMesh.vertexCount>=3&&target.GetComponent<MeshCollider>()&&target.GetComponent<MeshCollider>().sharedMesh==filter.sharedMesh;actual=filter&&filter.sharedMesh?filter.sharedMesh.vertexCount+" vertices":"no mesh";}
                else if(kind=="terrain_height"){var terrain=target.GetComponentInChildren<Terrain>();float height=terrain?terrain.SampleHeight(target.transform.position)+terrain.transform.position.y:float.NaN;pass=(check["expected"]?.Type==JTokenType.Float||check["expected"]?.Type==JTokenType.Integer)&&Math.Abs(height-(float)check["expected"])<.1f;actual=height.ToString(System.Globalization.CultureInfo.InvariantCulture);}
                else if(kind=="path"){pass=WorldNavigation.TryPath(target.transform.position,resolve((string)check["to"]).transform.position,out _);actual=pass?"PathComplete":"No complete NavMesh path";}
                rows.Add(new JObject{["target"]=(string)check["target"],["kind"]=kind,["passed"]=pass,["actual"]=actual});
            }
            return new JObject{["success"]=rows.All(r=>(bool)r["passed"]),["checks"]=rows,["scope"]="These explicit assertions only; component presence is not proof of full gameplay."};
        }
    }
}
