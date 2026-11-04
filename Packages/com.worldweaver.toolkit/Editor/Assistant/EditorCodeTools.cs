using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Trusted Editor extension path, deliberately not described as a sandbox.</summary>
    internal static class EditorCodeTools
    {
        internal const string Reference = @"reflect_api: empty target; args {type:string,member?:string}. Read public Unity/project C# signatures before using unfamiliar APIs. No method invocation.
execute_editor_code: optional existing owned target; when omitted the GameObject target parameter is NULL and the host creates NO helper objects. Supply an observed target when using target members, or find the existing objects explicitly. args {code:string}. Standalone C# METHOD BODY (optional leading using directives are supported; no namespace or class declarations), at most 20000 characters, ending with return object/anonymous data. Usings System, Collections.Generic, Linq, UnityEngine, UnityEditor, WorldWeaver, Newtonsoft.Json.Linq are supplied; Object and Random default to UnityEngine aliases (an explicit leading alias can override them). Requires the project's autonomous Editor C# setting. Compiles with real Roslyn diagnostics, then executes immediately on Unity main thread with full Editor permissions. Use Undo for every edit; create scene objects only when the original request authorizes them, and persistent runtime assets only under Assets/WorldWeaverGenerated. Review the automatic sceneReadback and geometric warnings against the request before finishing. Joint.anchor and Collider.center are LOCAL coordinates; use TransformPoint/InverseTransformPoint when comparing with WORLD bounds. Return actual measured state. No infinite loops, sleeps, network, shell, secret access, unrelated deletion, scene replacement, or claims of play verification. Runtime MonoBehaviours need saved source and normal compilation; dynamic snippet types cannot be serialized as runtime scripts.";
        static readonly Dictionary<string,Assembly> Cache=new Dictionary<string,Assembly>();
        internal static string Reflect(string name,string member)
        {
            var types=AppDomain.CurrentDomain.GetAssemblies().Where(a=>!a.IsDynamic).SelectMany(a=>{try{return a.GetExportedTypes();}catch(ReflectionTypeLoadException e){return e.Types.Where(t=>t!=null);}catch{return Array.Empty<Type>();}}).Where(t=>t.FullName==name||t.Name==name).Take(2).ToArray();
            if(types.Length!=1)throw new ArgumentException("Type is missing or ambiguous. Use its full namespace-qualified name.");
            var type=types[0];var flags=BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
            bool Match(string value)=>string.IsNullOrEmpty(member)||value.IndexOf(member,StringComparison.OrdinalIgnoreCase)>=0;
            return new JObject { ["type"]=type.FullName,["base"]=type.BaseType?.FullName,
                ["constructors"]=new JArray(type.GetConstructors().Take(10).Select(m=>m.ToString())),
                ["methods"]=new JArray(type.GetMethods(flags).Where(m=>!m.IsSpecialName&&Match(m.Name)).Take(50).Select(m=>m.ToString())),
                ["properties"]=new JArray(type.GetProperties(flags).Where(m=>Match(m.Name)).Take(35).Select(m=>m.PropertyType.FullName+" "+m.Name+" {"+(m.CanRead?"get;":"")+(m.CanWrite?"set;":"")+"}")),
                ["fields"]=new JArray(type.GetFields(flags).Where(m=>Match(m.Name)).Take(35).Select(m=>m.FieldType.FullName+" "+m.Name)) }.ToString(Formatting.None);
        }
        internal static EditorCommandResult Execute(EditorCommand command,Func<string,GameObject> resolve,AgentExecutionContext context)
        {
            if(context?.Session?.Settings?.AllowEditorCode!=true||context.Session.Settings.Mode!=AssistantMode.Execute&&context.Reviewed!=true)
                throw new InvalidOperationException("Autonomous Editor C# is disabled in this project. Enable it in chat settings or use reviewed script tools.");
            string code=(string)command.args["code"];
            if(string.IsNullOrWhiteSpace(code)||code.Length>20000)throw new ArgumentException("A complete C# method body up to 20000 characters is required.");
            string directives="";
            while(true)
            {
                var directive=Regex.Match(code,@"\A\s*using\s+(?:(?:static\s+)?[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*|[A-Za-z_][A-Za-z0-9_]*\s*=\s*[^;{}]+)\s*;");
                if(!directive.Success)break;directives+=directive.Value+"\n";code=code.Substring(directive.Length);
            }
            string source="using System; using System.Collections.Generic; using System.Linq;\nusing UnityEngine; using UnityEditor; using WorldWeaver; using Newtonsoft.Json.Linq;\n"+directives+EditorSnippetCompiler.DefaultUnityAliases(directives)+"\npublic static class WWEditorSnippet\n{\n public static object Execute(GameObject target)\n {\n"+code+"\n }\n}";
            string hash=TextChunker.Digest(source);
            if(!Cache.TryGetValue(hash,out var assembly))
            {
                if(Cache.Count>=64)throw new InvalidOperationException("64 distinct snippets compiled in this domain; reload the Editor domain before compiling more.");
                if(!EditorSnippetCompiler.IsAvailable)throw new InvalidOperationException("Unity's bundled Roslyn compiler could not be loaded. Reviewed scripts remain available.");
                var paths=AppDomain.CurrentDomain.GetAssemblies().Where(a=>!a.IsDynamic).Select(a=>{try{return a.Location;}catch{return "";}}).Where(p=>!string.IsNullOrEmpty(p)&&File.Exists(p)).Distinct().ToArray();
                assembly=EditorSnippetCompiler.Compile(source,paths,out var errors);
                if(assembly==null)return new EditorCommandResult{Tool=command.tool,Success=false,Error="C# compilation failed: "+string.Join("\n",errors.Take(12)),Summary=new JObject{["compiled"]=false,["sourceHash"]=hash,["diagnostics"]=new JArray(errors.Take(12))}.ToString(Formatting.None)};
                Cache.Add(hash,assembly);
            }
            GameObject target=null;
            if(!string.IsNullOrEmpty(command.target))target=resolve(command.target);
            if(target&&!target.GetComponent<GeneratedSceneObject>())throw new ArgumentException("Editor code target must be owned.");
            if(target)Undo.RegisterFullObjectHierarchyUndo(target,"WorldWeaver C# operation");
            var previousObjects=ProjectContextIndex.Objects().ToArray();
            var before=previousObjects.Select(g=>g.GetInstanceID()).ToHashSet();
            var knownIds=previousObjects.Select(g=>g.GetComponent<GeneratedSceneObject>()).Where(g=>g).Select(g=>g.ObjectId).ToHashSet();
            string sceneBefore=SceneSignature();
            object result;
            try{result=assembly.GetType("WWEditorSnippet").GetMethod("Execute").Invoke(null,new object[]{target});}
            catch(TargetInvocationException e){throw new InvalidOperationException("C# runtime error: "+(e.InnerException??e).Message);}
            var created=ProjectContextIndex.Objects().Where(g=>!before.Contains(g.GetInstanceID())).ToArray();
            foreach(var go in created)
            {
                Undo.RegisterCreatedObjectUndo(go,"Agent C# creation");
                if(!go.GetComponent<GeneratedSceneObject>())Undo.AddComponent<GeneratedSceneObject>(go).Kind="code_generated";
                var marker=go.GetComponent<GeneratedSceneObject>();
                if(knownIds.Contains(marker.ObjectId))marker.ObjectId=Guid.NewGuid().ToString("N");
                marker.EnsureUniqueIdentity();knownIds.Add(marker.ObjectId);
            }
            string data=JsonConvert.SerializeObject(result,new JsonSerializerSettings{ReferenceLoopHandling=ReferenceLoopHandling.Ignore,MaxDepth=8,Converters=new List<JsonConverter>{new UnityReadbackConverter()}});
            if(data.Length>16000)data=data.Substring(0,16000)+" [output truncated]";
            var report=new JObject{["compiled"]=true,["executed"]=true,["sceneChanged"]=sceneBefore!=SceneSignature(),["sourceHash"]=hash,["result"]=data,["createdObjects"]=new JArray(created.Take(80).Select(g=>ProjectContextIndex.StableTarget(g))),["sceneReadback"]=ReadScene(target,created),["verification"]="Compilation and execution only; runtime gameplay was not tested by this command."};
            return new EditorCommandResult{Tool=command.tool,Success=true,Target=target?ProjectContextIndex.StableTarget(target):"",Summary=report.ToString(Formatting.None)};
        }
        // Progress signal only, never a proof that the user's request is complete.
        // Includes serialized component fields throughout loaded scenes and does
        // not trust a snippet's return value. No implicit scene object is created.
        static string SceneSignature()=>TextChunker.Digest(string.Join("\n",RenderMaterialInitialization.Capture().OrderBy(p=>p.Key).Select(p=>p.Key+":"+p.Value))+"\n"+string.Join("\n",ProjectContextIndex.Objects().OrderBy(g=>g.GetInstanceID()).Select(g=>g.GetInstanceID()+":"+EditorJsonUtility.ToJson(g)+":"+string.Join("|",g.GetComponents<Component>().Where(c=>c).Select(c=>c.GetType().FullName+":"+EditorJsonUtility.ToJson(c))))));
        // Host measurements, independent of the snippet's self-reported return data.
        // Spatial evidence works for any generated component composition, not a named scenario.
        internal static JArray ReadScene(GameObject target,GameObject[] created)
        {
            Physics.SyncTransforms();var rows=new JArray();
            int serializedBudget=10000;
            var scope=target?target.GetComponentsInChildren<Transform>(true).Select(t=>t.gameObject):ProjectContextIndex.Objects();
            var objects=new[]{target}.Concat(created).Concat(scope).Where(g=>g).Distinct()
                .OrderBy(g=>g==target?0:g.GetComponents<MonoBehaviour>().Any(c=>c&&!(c is GeneratedSceneObject))?1:g.GetComponents<Component>().Any(c=>c&&!(c is Transform)&&!(c is GeneratedSceneObject))?2:3).Take(32);
            foreach(var go in objects)
            {
                var row=new JObject{["target"]=ProjectContextIndex.StableTarget(go),["name"]=go.name,["worldPosition"]=V(go.transform.position),["localScale"]=V(go.transform.localScale)};
                row["components"]=new JArray(go.GetComponents<Component>().Where(c=>c).Select(c=>c.GetType().Name));
                var serializedState=new JArray();
                foreach(var component in go.GetComponents<MonoBehaviour>().Where(c=>c&&!(c is GeneratedSceneObject)).OrderBy(c=>c is WorldPlayer||!(c.GetType().Namespace??"").StartsWith("WorldWeaver.",StringComparison.Ordinal)?0:1).Take(4))
                {
                    if(serializedBudget<200){row["serializedStateOmitted"]=true;break;}
                    var observed=AgentProjectTools.InspectComponent(component);var fields=new JArray();int used=0,cap=Math.Min(1600,serializedBudget);
                    foreach(var field in ((JArray)observed["fields"]).OfType<JObject>().Where(f=>!new[]{"m_ObjectHideFlags","m_CorrespondingSourceObject","m_PrefabInstance","m_PrefabAsset","m_GameObject","m_EditorHideFlags","m_EditorClassIdentifier"}.Contains((string)f["property"]))
                        .OrderBy(f=>(string)f["type"]=="String"||(string)f["type"]=="ObjectReference"?0:1).Take(18))
                    {int size=field.ToString(Formatting.None).Length;if(used+size>cap)continue;fields.Add(field.DeepClone());used+=size;}
                    serializedBudget-=used;serializedState.Add(new JObject{["component"]=observed["component"],["fields"]=fields});
                }
                if(serializedState.Count>0)row["serializedState"]=serializedState;
                var renderers=go.GetComponentsInChildren<Renderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
                if(renderers.Length>0){var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);row["renderWorldBounds"]=B(bounds);}
                row["materials"]=new JArray(renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct().Take(4).Select(m=>new JObject{["name"]=m.name,["shader"]=m.shader?m.shader.name:null,["texture"]=m.mainTexture?AssetDatabase.GetAssetPath(m.mainTexture):null,["color"]=m.HasProperty("_BaseColor")?JArray.FromObject(new[]{m.GetColor("_BaseColor").r,m.GetColor("_BaseColor").g,m.GetColor("_BaseColor").b}):m.HasProperty("_Color")?JArray.FromObject(new[]{m.color.r,m.color.g,m.color.b}):null}));
                var terrain=go.GetComponent<Terrain>();
                if(terrain&&terrain.terrainData)
                {
                    var heights=Enumerable.Range(0,81).Select(i=>terrain.terrainData.GetInterpolatedHeight((i%9)/8f,(i/9)/8f)).ToArray();
                    row["terrain"]=new JObject{["size"]=V(terrain.terrainData.size),["resolution"]=terrain.terrainData.heightmapResolution,["sampledMinHeight"]=heights.Min(),["sampledMaxHeight"]=heights.Max(),["layers"]=new JArray(terrain.terrainData.terrainLayers.Where(l=>l).Select(l=>new JObject{["name"]=l.name,["diffuse"]=l.diffuseTexture?AssetDatabase.GetAssetPath(l.diffuseTexture):null,["remapMax"]=JArray.FromObject(new[]{l.diffuseRemapMax.x,l.diffuseRemapMax.y,l.diffuseRemapMax.z})}))};
                }
                var visual=go.GetComponent<WorldModelVisual>();if(visual)row["modelVisual"]=new JObject{["source"]=visual.SourcePath,["target"]=visual.Visual?ProjectContextIndex.StableTarget(visual.Visual.gameObject):null};
                var animator=go.GetComponent<Animator>();if(animator)row["animation"]=new JObject{["validAvatar"]=animator.avatar&&animator.avatar.isValid,["humanoid"]=animator.avatar&&animator.avatar.isValid&&animator.avatar.isHuman,["controller"]=animator.runtimeAnimatorController?AssetDatabase.GetAssetPath(animator.runtimeAnimatorController):null,["proceduralLocomotion"]=go.GetComponent<WorldHumanoidLocomotion>()!=null};
                var light=go.GetComponent<Light>();if(light)row["light"]=new JObject{["type"]=light.type.ToString(),["range"]=light.range,["intensity"]=light.intensity};
                var body=go.GetComponent<Rigidbody>();if(body)row["body"]=new JObject{["mass"]=body.mass,["gravity"]=body.useGravity,["kinematic"]=body.isKinematic};
                var colliders=go.GetComponentsInChildren<Collider>().Where(c=>c.enabled&&c.gameObject.activeInHierarchy).Take(12).ToArray();
                row["colliders"]=new JArray(colliders.Select(c=>new JObject{["type"]=c.GetType().FullName,["trigger"]=c.isTrigger,["worldBounds"]=B(c.bounds)}));
                row["joints"]=new JArray(go.GetComponents<Joint>().Select(j=>
                {
                    var anchor=j.transform.TransformPoint(j.anchor);var own=colliders.Where(c=>c.attachedRigidbody==body&&!c.isTrigger).ToArray();
                    float distance=own.Length>0?own.Min(c=>Vector3.Distance(anchor,c.bounds.ClosestPoint(anchor))):0;
                    var item=new JObject{["type"]=j.GetType().FullName,["localAnchor"]=V(j.anchor),["worldAnchor"]=V(anchor),["worldAxis"]=V(j.transform.TransformDirection(j.axis).normalized),
                        ["connectedWorldAnchor"]=V(j.connectedBody?j.connectedBody.transform.TransformPoint(j.connectedAnchor):j.connectedAnchor),["distanceOutsideOwnColliderBounds"]=distance};
                    if(j is HingeJoint hinge){item["useLimits"]=hinge.useLimits;item["minDegrees"]=hinge.limits.min;item["maxDegrees"]=hinge.limits.max;}
                    if(distance>.02f)item["geometryWarning"]="Anchor lies outside the body's collider bounds in WORLD space. This can be intentional; compare with the user's attachment requirement and local scale. A requested edge anchor must coincide with the actual world-space edge, not an unscaled dimension.";
                    return item;
                }));rows.Add(row);
            }
            return rows;
        }
        static JArray V(Vector3 value)=>new JArray(value.x,value.y,value.z);
        static JObject B(Bounds value)=>new JObject{["min"]=V(value.min),["max"]=V(value.max),["size"]=V(value.size)};
        internal sealed class UnityReadbackConverter:JsonConverter
        {
            public override bool CanRead=>false;
            public override bool CanConvert(Type type)=>typeof(UnityEngine.Object).IsAssignableFrom(type)||type==typeof(Vector2)||type==typeof(Vector3)||type==typeof(Vector4)||type==typeof(Quaternion)||type==typeof(Color)||type==typeof(Bounds);
            public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer)=>throw new NotSupportedException();
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)
            {
                JToken data;
                if(value is Vector2 v2)data=new JArray(v2.x,v2.y);
                else if(value is Vector3 v3)data=new JArray(v3.x,v3.y,v3.z);
                else if(value is Vector4 v4)data=new JArray(v4.x,v4.y,v4.z,v4.w);
                else if(value is Quaternion q)data=new JArray(q.x,q.y,q.z,q.w);
                else if(value is Color color)data=new JArray(color.r,color.g,color.b,color.a);
                else if(value is Bounds bounds)data=new JObject{["center"]=new JArray(bounds.center.x,bounds.center.y,bounds.center.z),["size"]=new JArray(bounds.size.x,bounds.size.y,bounds.size.z)};
                else
                {
                    var obj=value as UnityEngine.Object;
                    if(!obj){writer.WriteNull();return;}
                    var go=obj as GameObject??(obj as Component)?.gameObject;
                    data=new JObject{["name"]=obj.name,["type"]=obj.GetType().FullName,["asset"]=AssetDatabase.GetAssetPath(obj),["target"]=go?ProjectContextIndex.StableTarget(go):null};
                }
                data.WriteTo(writer);
            }
        }
    }
}
