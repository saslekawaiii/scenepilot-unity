using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
execute_editor_code: optional existing owned target, otherwise an owned workspace; args {code:string}. Standalone C# METHOD BODY only (no using directives, namespace or class declarations), at most 20000 characters, ending with return object/anonymous data. Usings System, Collections.Generic, Linq, UnityEngine, UnityEditor, WorldWeaver, Newtonsoft.Json.Linq are supplied. Parameter GameObject target is the real object/workspace; use it instead of inventing an ID. Requires the project's autonomous Editor C# setting. Compiles with real Roslyn diagnostics, then executes immediately on Unity main thread with full Editor permissions. Use Undo for every edit, keep new scene objects under target, create persistent runtime assets under Assets/WorldWeaverGenerated only. Return actual measured state. No infinite loops, sleeps, network, shell, secret access, unrelated deletion, scene replacement, or claims of play verification. Runtime MonoBehaviours need saved source and normal compilation; dynamic snippet types cannot be serialized as runtime scripts.";
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
            string source="using System; using System.Collections.Generic; using System.Linq;\nusing UnityEngine; using UnityEditor; using WorldWeaver; using Newtonsoft.Json.Linq;\npublic static class WWEditorSnippet\n{\n public static object Execute(GameObject target)\n {\n"+code+"\n }\n}";
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
            else
            {
                target=ProjectContextIndex.Objects().FirstOrDefault(g=>g.name=="WorldWeaver Code Workspace"&&g.GetComponent<GeneratedSceneObject>());
                if(!target){target=new GameObject("WorldWeaver Code Workspace");Undo.RegisterCreatedObjectUndo(target,"Agent C# workspace");Undo.AddComponent<GeneratedSceneObject>(target).Kind="code_workspace";}
            }
            if(!target.GetComponent<GeneratedSceneObject>())throw new ArgumentException("Editor code target must be owned.");
            Undo.RegisterFullObjectHierarchyUndo(target,"WorldWeaver C# operation");
            var before=ProjectContextIndex.Objects().Select(g=>g.GetInstanceID()).ToHashSet();
            object result;
            try{result=assembly.GetType("WWEditorSnippet").GetMethod("Execute").Invoke(null,new object[]{target});}
            catch(TargetInvocationException e){throw new InvalidOperationException("C# runtime error: "+(e.InnerException??e).Message);}
            var created=ProjectContextIndex.Objects().Where(g=>!before.Contains(g.GetInstanceID())).ToArray();
            foreach(var go in created)
            {
                Undo.RegisterCreatedObjectUndo(go,"Agent C# creation");
                if(!go.GetComponent<GeneratedSceneObject>())Undo.AddComponent<GeneratedSceneObject>(go).Kind="code_generated";
            }
            string data=JsonConvert.SerializeObject(result,new JsonSerializerSettings{ReferenceLoopHandling=ReferenceLoopHandling.Ignore,MaxDepth=8,Converters=new List<JsonConverter>{new UnityReadbackConverter()}});
            if(data.Length>16000)data=data.Substring(0,16000)+" [output truncated]";
            var report=new JObject{["compiled"]=true,["executed"]=true,["sourceHash"]=hash,["result"]=data,["createdObjects"]=new JArray(created.Take(80).Select(g=>ProjectContextIndex.StableTarget(g))),["verification"]="Compilation and execution only; runtime gameplay was not tested by this command."};
            return new EditorCommandResult{Tool=command.tool,Success=true,Target=ProjectContextIndex.StableTarget(target),Summary=report.ToString(Formatting.None)};
        }
        sealed class UnityReadbackConverter:JsonConverter
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
