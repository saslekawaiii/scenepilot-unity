using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    public sealed class AgentExecutionContext
    {
        public ChatSession Session;
        public AssistantMessage Message;
        public bool Reviewed;
    }
    /// <summary>Typed project operations: no shell, arbitrary reflection invocation or credential file reader.</summary>
    internal static class AgentProjectTools
    {
        static readonly string[] Names={"find_unity_recipes","read_unity_recipe","read_task_step","execute_editor_code","reflect_api","plan_task","review_stage","discover_tools","list_scene_objects","find_scene_objects","inspect_project","inspect_component","set_component","add_component","read_console","capture_scene","run_runtime_probe","run_tests","run_play_check","run_player_check","poll_job","set_build_scenes","build_player","remember_design","list_skills","read_skill","open_scene"};
        internal static bool Handles(string tool)=>Names.Contains(tool);
        internal static bool IsReadOnly(string tool)=>new[]{"find_unity_recipes","read_unity_recipe","read_task_step","reflect_api","plan_task","review_stage","discover_tools","list_scene_objects","find_scene_objects","inspect_project","inspect_component","read_console","capture_scene","poll_job","list_skills","read_skill"}.Contains(tool);
        internal static bool IsDeferred(string tool)=>tool=="run_runtime_probe"||tool=="run_tests"||tool=="run_play_check"||tool=="run_player_check"||tool=="build_player"||tool=="open_scene";
        internal static bool RequiresReview(EditorCommand c)=>c.tool=="open_scene"||c.tool=="set_build_scenes"||c.tool=="add_component"&&ResolveType((string)c.args["type"]).IsSubclassOf(typeof(MonoBehaviour));
        internal const string ToolInstructions=@"PROJECT TOOLS (exact typed args):
read_task_step: target empty,args {index:0..8191,offset?:0..100000}. Retrieves a persisted earlier tool result from THIS task, including full real IDs, errors and asset paths. Use the durable index to recover evidence outside the recent context window. Paged data only; does not rerun the tool.
plan_task: target empty,args {steps:[string...]}, 1-12 stages covering ALL parts of a compound request. Records a proposed approach only; never marks stages completed. Plan before complex creation, then use actual tools and live checks.
review_stage: target empty,args {}. Request independent verification of the CURRENT model-planned stage after its work or inspection is ready. Does not mark it passed: the controller checks actual evidence before advancing. Use this instead of repeated reads while waiting for the next stage. finish_task is for the whole user request.
list_scene_objects: target empty,args {component?:type name,kind?:exact generated kind,parent?:actual scene target,offset?:0..20000,limit?:1..100}. Structural active-scene inventory independent of names/language. Defaults omit transform-only skeleton nodes. Use component Renderer/Terrain/WorldPlayer to find geometry; actual paths, materials and dimensions identify old surfaces. Limit is a MAXIMUM row count; pages also fit the model context. Follow actual nextOffset until null; never assume offset+requested limit. Oversized rows expose a stable target with listingDetailsOmitted for separate inspection. Paged read only.
find_scene_objects: target empty,args {query:string,limit?:1..50}. Literal AND-token search; no translation or synonyms. Empty query lists objects. Addressed retrieval over all loaded scenes/assets, returns stable targets. Editing remains restricted to active scene.
inspect_project: target empty,args {}. Actual Unity/platform/packages/build-scenes metadata, no environment files.
inspect_component: target existing object,args {type:string}. Read actual serialized component fields; credentials are masked.
set_component: target existing object,args {type:string,property:string,value:scalar|vector|object-reference}. Change one existing serialized field; inspect first. Does not invoke arbitrary methods. Reference value is {target:'existing scene target',component?:'UnityEngine.Transform'} or {asset:'Assets/path'} or null. Component references require component type explicitly.
add_component: target existing object,args {type:full registered Component type name}. Native components execute directly; custom scripts require user review.
read_console: target empty,args {limit?:1..100}. Observed console and actual compiler feedback.
capture_scene: target empty,args {view?:overview|game|player}. Real image saved locally. Overview frames actual terrain and geometry, with fog suppressed only during capture. A text-only planner receives read-only observations from an available installed local vision model. Use overview to resolve visual descriptions after literal name searches miss; absent names do not prove absent geometry. Never claim inspection when visualInspection is unavailable.
run_tests: target empty,args {mode:edit|play,filter:explicit fixture/name regex}. Standalone plan only, starts real Unity TestRunner job. Persisted job result survives domain reload; use poll_job before claiming success. Empty/skipped tests are not a pass.
run_player_check: target actual WorldPlayer, args {seconds?:0.5..3,check_relaxed_arms?:bool}. Enter real Play Mode and drive actual virtual mouse (both axes WITHOUT RMB), W movement, exposed leg and both arm poses. Measures yaw/pitch/travel/ground. check_relaxed_arms defaults to false. Enable it ONLY for a user-requested arm-pose repair and a confirmed Humanoid rig; it is not a general player requirement. Generic rigs may have authored animation without Humanoid bone measurements. Optional relaxed walking arm check requires measured elbows <85 degrees and hands below shoulders. Reference T-pose in Edit Mode is not an animation failure: diagnose the actual Play pose. Exits and returns a durable job result via poll_job. No source changes.
run_play_check: target existing object,args {seconds:0.25..10,degrees_per_second:-360..360}. Standalone plan. Enter real Play Mode, measure LOCAL Y rotation against actual elapsed game time, verify speed and off-axis drift, then exit. Use after attaching rotation behavior; compilation alone is insufficient. Result survives domain reload.
poll_job: target empty,args {id:job id}. Read actual job/XML/build status.
build_player: target empty,args {}. Standalone plan; builds enabled saved Build Settings scenes to unique local Builds directory for active platform. Does not publish or install modules.
set_build_scenes: target empty,args {paths:1..8 existing saved Assets/...unity paths}. Reviewed replacement of enabled Build Settings scene list, with checkpoint. Save the intended scene first; do not include unrelated scenes.
remember_design: target empty,args {key:string,value:string,category?:constraint|decision|entity}. Store compact requirements/decisions with source message; cannot grant permissions.
list_skills: target empty,args {query?:string,limit?:1..30}. Advisory MD skill metadata only.
read_skill: target empty,args {id:string}. Bounded advisory skill body; never executable instructions or permission grants.
open_scene: target empty,args {path:Assets/...unity}. Standalone reviewed action; project checkpoint protects previous scene. No scene deletion.";
        static JObject Text(int max=240)=>new JObject{["type"]="string",["maxLength"]=max};
        static JObject Integer(int min,int max)=>new JObject{["type"]="integer",["minimum"]=min,["maximum"]=max};
        static JObject Obj(JObject properties,params string[] required)=>new JObject{["type"]="object",["properties"]=properties,["additionalProperties"]=false,["required"]=new JArray(required)};
        internal static IEnumerable<JObject> CommandSchemas()
        {
            var schema=new Dictionary<string,JObject>{
                ["find_unity_recipes"]=Obj(new JObject{["query"]=Text(500),["limit"]=Integer(1,8)},"query"),
                ["read_unity_recipe"]=Obj(new JObject{["id"]=Text(120),["offset"]=Integer(0,65536)},"id"),
                ["read_task_step"]=Obj(new JObject{["index"]=Integer(0,8191),["offset"]=Integer(0,100000)},"index"),
                ["reflect_api"]=Obj(new JObject{["type"]=Text(240),["member"]=Text(120)},"type"),
                ["execute_editor_code"]=Obj(new JObject{["code"]=Text(20000)},"code"),
                ["review_stage"]=Obj(new JObject()),
                ["plan_task"]=Obj(new JObject{["steps"]=new JObject{["type"]="array",["minItems"]=1,["maxItems"]=12,["items"]=Text(300)}},"steps"),
                ["discover_tools"]=Obj(new JObject{["query"]=Text(500),["names"]=new JObject{["type"]="array",["maxItems"]=16,["items"]=Text(64)}}),
                ["list_scene_objects"]=Obj(new JObject{["component"]=Text(200),["kind"]=Text(100),["parent"]=Text(200),["offset"]=Integer(0,20000),["limit"]=Integer(1,100)}),
                ["find_scene_objects"]=Obj(new JObject{["query"]=Text(500),["limit"]=Integer(1,50)},"query"),
                ["inspect_project"]=Obj(new JObject()),["inspect_component"]=Obj(new JObject{["type"]=Text()},"type"),
                ["set_component"]=Obj(new JObject{["type"]=Text(),["property"]=Text(),["value"]=new JObject{["anyOf"]=new JArray(new JObject{["type"]="string",["maxLength"]=4000},new JObject{["type"]="number"},new JObject{["type"]="boolean"},new JObject{["type"]="null"},new JObject{["type"]="array",["items"]=new JObject{["type"]="number"},["minItems"]=2,["maxItems"]=4},Obj(new JObject{["target"]=Text(),["component"]=Text()},"target"),Obj(new JObject{["asset"]=Text()},"asset"))}},"type","property","value"),
                ["add_component"]=Obj(new JObject{["type"]=Text()},"type"),["read_console"]=Obj(new JObject{["limit"]=Integer(1,100)}),
                ["capture_scene"]=Obj(new JObject{["view"]=new JObject{["type"]="string",["enum"]=new JArray("overview","game","player")}}),["run_tests"]=Obj(new JObject{["mode"]=new JObject{["type"]="string",["enum"]=new JArray("edit","play")},["filter"]=Text(250)},"mode","filter"),
                ["run_runtime_probe"]=Obj(new JObject{["code"]=Text(16000),["seconds"]=new JObject{["type"]="number",["minimum"]=.25,["maximum"]=15}},"code"),
                ["run_player_check"]=Obj(new JObject{["seconds"]=new JObject{["type"]="number",["minimum"]=.5,["maximum"]=3},["check_relaxed_arms"]=new JObject{["type"]="boolean"}}),
                ["run_play_check"]=Obj(new JObject{["seconds"]=new JObject{["type"]="number",["minimum"]=.25,["maximum"]=10},["degrees_per_second"]=new JObject{["type"]="number",["minimum"]=-360,["maximum"]=360}},"seconds","degrees_per_second"),
                ["poll_job"]=Obj(new JObject{["id"]=Text(32)},"id"),["build_player"]=Obj(new JObject()),
                ["set_build_scenes"]=Obj(new JObject{["paths"]=new JObject{["type"]="array",["minItems"]=1,["maxItems"]=8,["uniqueItems"]=true,["items"]=Text(240)}},"paths"),
                ["remember_design"]=Obj(new JObject{["key"]=Text(120),["value"]=Text(3000),["category"]=new JObject{["type"]="string",["enum"]=new JArray("constraint","decision","entity")}},"key","value"),
                ["list_skills"]=Obj(new JObject{["query"]=Text(500),["limit"]=Integer(1,30)}),["read_skill"]=Obj(new JObject{["id"]=Text(80)},"id"),
                ["open_scene"]=Obj(new JObject{["path"]=Text(240)},"path")};
            foreach(var pair in schema)yield return Obj(new JObject{["tool"]=new JObject{["type"]="string",["const"]=pair.Key},["name"]=Text(160),["target"]=pair.Key=="run_player_check"?new JObject{["type"]="string",["minLength"]=1,["maxLength"]=240}:Text(240),["args"]=pair.Value},"tool","name","target","args");
        }
        static bool Sensitive(string property)=>Regex.IsMatch(property??"",@"(?i)(password|secret|credential|api[_-]?key|access[_-]?token)");
        static Type ResolveType(string name)
        {
            if(string.IsNullOrWhiteSpace(name)||name.Length>240)throw new ArgumentException("Specify a registered Component type.");
            var candidates=TypeCache.GetTypesDerivedFrom<Component>().Where(t=>!t.IsAbstract&&!t.IsGenericTypeDefinition&&(t.FullName==name||t.Name==name)).Take(2).ToArray();
            if(candidates.Length!=1)throw new ArgumentException("Component type is missing or ambiguous; use its full name.");return candidates[0];
        }
        static Component Component(EditorCommand c,Func<string,GameObject> resolve)
        {var component=resolve(c.target).GetComponent(ResolveType((string)c.args["type"]));if(!component)throw new ArgumentException("Target lacks component "+(string)c.args["type"]+". Inspect the actual child or add the required component first.");return component;}
        internal static void Validate(EditorCommand c,Func<string,GameObject> resolve)
        {
            var schema=CommandSchemas().First(s=>(string)s["properties"]["tool"]["const"]==c.tool);
            var args=(JObject)schema["properties"]["args"];var fields=(JObject)args["properties"];
            if(c.args.Properties().Any(p=>fields.Property(p.Name)==null))throw new ArgumentException("Unknown project tool argument.");
            foreach(string required in (JArray)args["required"])if(c.args[required]==null)throw new ArgumentException("Missing "+required);
            foreach(var p in c.args.Properties())
            {
                var definition=(JObject)fields[p.Name];string type=(string)definition["type"];
                if(type=="string"&&(p.Value.Type!=JTokenType.String||((string)p.Value).Length>((int?)definition["maxLength"]??240)))throw new ArgumentException("Invalid string "+p.Name);
                if(type=="integer"&&(p.Value.Type!=JTokenType.Integer||(int)p.Value<(int)definition["minimum"]||(int)p.Value>(int)definition["maximum"]))throw new ArgumentException("Integer out of range: "+p.Name);
                if(type=="number"&&((p.Value.Type!=JTokenType.Float&&p.Value.Type!=JTokenType.Integer)||double.IsNaN((double)p.Value)||double.IsInfinity((double)p.Value)||(double)p.Value<(double)definition["minimum"]||(double)p.Value>(double)definition["maximum"]))throw new ArgumentException("Number out of range: "+p.Name);
                if(definition["enum"] is JArray choices&&!choices.Any(v=>JToken.DeepEquals(v,p.Value)))throw new ArgumentException("Invalid choice: "+p.Name);
            }
            if(c.tool=="execute_editor_code"){if(!string.IsNullOrEmpty(c.target))resolve(c.target);}
            else if(new[]{"inspect_component","set_component","add_component","run_play_check","run_player_check"}.Contains(c.tool))resolve(c.target);else if(!string.IsNullOrEmpty(c.target))throw new ArgumentException("Project query/job requires empty target.");
            if(c.tool=="inspect_component")ResolveType((string)c.args["type"]);
            if(c.tool=="add_component")ResolveType((string)c.args["type"]);
            if(c.tool=="set_component")
            {
                string property=(string)c.args["property"];
                if(Sensitive(property)||property=="m_Script"||property=="m_ObjectHideFlags")throw new ArgumentException("Protected serialized property.");
                var serialized=new SerializedObject(Component(c,resolve));var field=serialized.FindProperty(property)??throw new ArgumentException("Serialized property not found. Inspect the component first.");
                if(!field.editable)throw new ArgumentException("Serialized property is read-only.");
                // Some compatible function-call providers encode union values as
                // strings. Only an unambiguous literal for an inspected bool field
                // can be normalized; arbitrary truthy strings remain invalid.
                if(field.propertyType==SerializedPropertyType.Boolean&&c.args["value"]?.Type==JTokenType.String&&bool.TryParse((string)c.args["value"],out bool literal))c.args["value"]=literal;
                ValidateProperty(field,c.args["value"],resolve);
            }
            if(c.tool=="plan_task" && (!(c.args["steps"] is JArray stages)||stages.Count<1||stages.Count>12||stages.Any(n=>n.Type!=JTokenType.String||string.IsNullOrWhiteSpace((string)n)||((string)n).Length>300)))throw new ArgumentException("A task plan needs 1-12 bounded steps.");
            if(c.tool=="discover_tools" && c.args["names"]!=null && (!(c.args["names"] is JArray names)||names.Count>16||names.Any(n=>n.Type!=JTokenType.String||((string)n).Length>64)))throw new ArgumentException("Invalid discovery tool names.");
            if(c.tool=="poll_job"&&!ChatHistoryStore.ValidId((string)c.args["id"]))throw new ArgumentException("Invalid job id.");
            if(c.tool=="run_tests"&&string.IsNullOrWhiteSpace((string)c.args["filter"]))throw new ArgumentException("An explicit test filter is required.");
            if(c.tool=="open_scene")ScenePath((string)c.args["path"]);
            if(c.tool=="set_build_scenes")
            {
                if(!(c.args["paths"] is JArray paths)||paths.Count<1||paths.Count>8||paths.Any(p=>p.Type!=JTokenType.String)||paths.Values<string>().Distinct(StringComparer.Ordinal).Count()!=paths.Count)throw new ArgumentException("Select 1–8 unique existing scene paths.");
                foreach(string path in paths)ScenePath(path);
            }
        }
        static string ScenePath(string path)
        {if(path==null||!path.StartsWith("Assets/",StringComparison.Ordinal)||path.Contains("..")||path.Contains('\\')||!path.EndsWith(".unity",StringComparison.OrdinalIgnoreCase)||!AssetDatabase.LoadAssetAtPath<SceneAsset>(path))throw new ArgumentException("Select an existing Assets/...unity scene.");return path;}
        static void ValidateProperty(SerializedProperty field,JToken value,Func<string,GameObject> resolve)
        {
            switch(field.propertyType)
            {
                case SerializedPropertyType.Integer:case SerializedPropertyType.Enum:if(value.Type!=JTokenType.Integer||(long)value<int.MinValue||(long)value>int.MaxValue)throw new ArgumentException("Expected 32-bit integer.");break;
                case SerializedPropertyType.Float:if((value.Type!=JTokenType.Integer&&value.Type!=JTokenType.Float)||double.IsInfinity((double)value)||double.IsNaN((double)value)||Math.Abs((double)value)>1e8)throw new ArgumentException("Expected finite bounded number.");break;
                case SerializedPropertyType.Boolean:if(value.Type!=JTokenType.Boolean)throw new ArgumentException("Expected JSON boolean true/false, not an arbitrary string or number.");break;
                case SerializedPropertyType.String:if(value.Type!=JTokenType.String||((string)value).Length>4000)throw new ArgumentException("Expected bounded string.");break;
                case SerializedPropertyType.Vector2:case SerializedPropertyType.Vector3:case SerializedPropertyType.Vector4:case SerializedPropertyType.Color:
                    int length=field.propertyType==SerializedPropertyType.Vector2?2:field.propertyType==SerializedPropertyType.Vector3?3:4;
                    if(!(value is JArray array)||array.Count!=length||array.Any(v=>(v.Type!=JTokenType.Float&&v.Type!=JTokenType.Integer)||double.IsNaN((double)v)||double.IsInfinity((double)v)||Math.Abs((double)v)>1e8))throw new ArgumentException("Expected finite vector/color array.");break;
                case SerializedPropertyType.ObjectReference:
                    var reference=ResolveReference(value,resolve);
                    var expected=Regex.Match(field.type??"",@"^PPtr<\$?(?<type>[^>]+)>$");
                    if(reference&&expected.Success)
                    {
                        string name=expected.Groups["type"].Value;
                        var types=TypeCache.GetTypesDerivedFrom<UnityEngine.Object>().Where(t=>t.Name==name||t.FullName==name).ToArray();
                        if(types.Length==1&&!types[0].IsInstanceOfType(reference))throw new ArgumentException("Object reference needs "+name+", received "+reference.GetType().Name+". Specify component for Component references.");
                    }
                    break;
                default:throw new ArgumentException("This serialized property type is intentionally unsupported.");
            }
        }
        static UnityEngine.Object ResolveReference(JToken value,Func<string,GameObject> resolve)
        {
            if(value.Type==JTokenType.Null)return null;
            if(!(value is JObject reference)||reference.Properties().Any(p=>p.Name!="target"&&p.Name!="component"&&p.Name!="asset")||reference.Count<1||reference.Count>2)throw new ArgumentException("Reference needs target (optional component) or asset.");
            if(reference["target"]?.Type==JTokenType.String)
            {
                if(reference["asset"]!=null)throw new ArgumentException("Reference cannot mix target and asset.");
                var go=resolve((string)reference["target"]);return reference["component"]==null?(UnityEngine.Object)go:go.GetComponent(ResolveType((string)reference["component"]))??throw new ArgumentException("Referenced component not found.");
            }
            if(reference.Count!=1||reference["component"]!=null)throw new ArgumentException("Asset reference has one asset field.");
            string path=(string)reference["asset"];
            if(path==null||!path.StartsWith("Assets/",StringComparison.Ordinal)||path.Contains("..")||path.Contains('\\'))throw new ArgumentException("Asset reference must stay inside Assets.");
            return AssetDatabase.LoadMainAssetAtPath(path)??throw new ArgumentException("Asset reference not found.");
        }
        internal static JObject InspectComponent(Component component)
        {
            var serialized=new SerializedObject(component);var property=serialized.GetIterator();var fields=new JArray();int budget=0;
            while(property.NextVisible(true)&&fields.Count<80)
            {
                if(property.depth>2||property.propertyPath=="m_Script")continue;
                string value=Sensitive(property.propertyPath)?"[скрыто]":PropertyText(property);
                if(budget+value.Length>9000)break;budget+=value.Length;
                var field=new JObject{["property"]=property.propertyPath,["type"]=property.propertyType.ToString(),["value"]=ChatHistoryStore.Redact(value),["editable"]=property.editable&&!Sensitive(property.propertyPath)};
                if(property.propertyType==SerializedPropertyType.ObjectReference&&!Sensitive(property.propertyPath))
                {
                    var referenced=property.objectReferenceValue;var referencedComponent=referenced as Component;
                    var sceneObject=referencedComponent?referencedComponent.gameObject:referenced as GameObject;
                    if(sceneObject&&sceneObject.scene.IsValid())field["reference"]=new JObject{["target"]=ProjectContextIndex.StableTarget(sceneObject),["component"]=referencedComponent?referencedComponent.GetType().FullName:null};
                    else if(referenced)field["reference"]=new JObject{["asset"]=AssetDatabase.GetAssetPath(referenced),["type"]=referenced.GetType().FullName};
                    else field["reference"]=JValue.CreateNull();
                }
                fields.Add(field);
            }
            return new JObject{["component"]=component.GetType().FullName,["fields"]=fields};
        }
        static string PropertyText(SerializedProperty p)
        {
            switch(p.propertyType)
            {
                case SerializedPropertyType.Integer:case SerializedPropertyType.Enum:return p.intValue.ToString();case SerializedPropertyType.Float:return p.doubleValue.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:return p.boolValue.ToString();case SerializedPropertyType.String:return p.stringValue.Substring(0,Math.Min(1000,p.stringValue.Length));
                case SerializedPropertyType.Vector2:return p.vector2Value.ToString();case SerializedPropertyType.Vector3:return p.vector3Value.ToString();case SerializedPropertyType.Vector4:return p.vector4Value.ToString();case SerializedPropertyType.Color:return p.colorValue.ToString();
                case SerializedPropertyType.ObjectReference:return p.objectReferenceValue?AssetDatabase.GetAssetPath(p.objectReferenceValue)+" "+p.objectReferenceValue.name:"null";
                default:return p.isArray?"array size "+p.arraySize:p.propertyType.ToString();
            }
        }
        internal static EditorCommandResult Execute(EditorCommand c,Func<string,GameObject> resolve,AgentExecutionContext context)
        {
            Validate(c,resolve);string summary;GameObject target=null;bool? changesApplied=null;var a=c.args;
            switch(c.tool)
            {
                case "find_unity_recipes":summary=UnityRecipeCatalog.CreateDefault().Search((string)a["query"],(int?)a["limit"]??6).ToString(Formatting.None);break;
                case "read_unity_recipe":summary=UnityRecipeCatalog.CreateDefault().Read((string)a["id"],(int?)a["offset"]??0).ToString(Formatting.None);break;
                case "read_task_step":
                    int index=(int)a["index"],offset=(int?)a["offset"]??0;
                    if(context?.Message==null||index>=context.Message.Steps.Count)throw new ArgumentException("Choose an existing step from this task's durable index.");
                    var evidence=context.Message.Steps[index];string body=evidence.Summary??"";
                    if(offset>body.Length)throw new ArgumentException("Offset exceeds stored evidence length "+body.Length);
                    int length=Math.Min(18000,body.Length-offset);
                    summary=new JObject{["index"]=index,["tool"]=evidence.Tool,["success"]=evidence.Success,["target"]=evidence.Target,["error"]=evidence.Error,["summary"]=body.Substring(offset,length),["offset"]=offset,["nextOffset"]=offset+length<body.Length?(JToken)(offset+length):JValue.CreateNull(),["totalLength"]=body.Length}.ToString(Formatting.None);break;
                case "execute_editor_code":return EditorCodeTools.Execute(c,resolve,context);
                case "reflect_api":summary=EditorCodeTools.Reflect((string)a["type"],(string)a["member"]);break;
                case "plan_task":if(context?.Message?.Task==null)throw new InvalidOperationException("A task context is required.");context.Message.Task.Approach=a["steps"].Values<string>().ToList();summary=new JObject{["stages"]=a["steps"].DeepClone(),["status"]="proposed, not completed",["changesApplied"]=false}.ToString(Formatting.None);break;
                case "review_stage":
                    var task=context?.Message?.Task;if(task==null||task.ActiveApproachStage<0||task.ActiveApproachStage>=task.Approach.Count)throw new InvalidOperationException("No active planned stage to review.");
                    summary=new JObject{["stage"]=task.ActiveApproachStage,["description"]=task.Approach[task.ActiveApproachStage],["status"]="review requested, not passed",["changesApplied"]=false}.ToString(Formatting.None);break;
                case "discover_tools":summary=AgentCapabilities.Discover(a).ToString(Formatting.None);break;
                case "list_scene_objects":summary=ProjectContextIndex.ListScene(a).ToString(Formatting.None);break;
                case "find_scene_objects":summary=ProjectContextIndex.Search((string)a["query"],(int?)a["limit"]??20).ToString(Formatting.None);break;
                case "inspect_project":summary=ProjectContextIndex.Summary().ToString(Formatting.None);break;
                case "inspect_component":
                    target=resolve(c.target);var inspectedType=ResolveType((string)a["type"]);var inspected=target.GetComponent(inspectedType);
                    summary=(inspected?InspectComponent(inspected):new JObject{["component"]=inspectedType.FullName,["exists"]=false,["target"]=ProjectContextIndex.StableTarget(target),["availableComponents"]=new JArray(target.GetComponents<Component>().Where(p=>p).Select(p=>p.GetType().FullName)),["descendantMatches"]=new JArray(target.GetComponentsInChildren(inspectedType,true).Where(p=>p).Take(12).Select(p=>new JObject{["target"]=ProjectContextIndex.StableTarget(p.gameObject),["component"]=p.GetType().FullName,["name"]=p.gameObject.name}))}).ToString(Formatting.None);break;
                case "set_component":
                    var component=Component(c,resolve);Undo.RecordObject(component,"WorldWeaver serialized property");var serialized=new SerializedObject(component);var field=serialized.FindProperty((string)a["property"]);var value=a["value"];
                    switch(field.propertyType)
                    {
                        case SerializedPropertyType.Integer:case SerializedPropertyType.Enum:field.intValue=(int)value;break;case SerializedPropertyType.Float:field.doubleValue=(double)value;break;case SerializedPropertyType.Boolean:field.boolValue=(bool)value;break;case SerializedPropertyType.String:field.stringValue=(string)value;break;
                        case SerializedPropertyType.Vector2:field.vector2Value=new Vector2((float)value[0],(float)value[1]);break;case SerializedPropertyType.Vector3:field.vector3Value=new Vector3((float)value[0],(float)value[1],(float)value[2]);break;case SerializedPropertyType.Vector4:field.vector4Value=new Vector4((float)value[0],(float)value[1],(float)value[2],(float)value[3]);break;case SerializedPropertyType.Color:field.colorValue=new Color((float)value[0],(float)value[1],(float)value[2],(float)value[3]);break;
                        case SerializedPropertyType.ObjectReference:field.objectReferenceValue=ResolveReference(value,resolve);break;
                    }
                    changesApplied=serialized.ApplyModifiedProperties();EditorUtility.SetDirty(component);target=component.gameObject;summary=InspectComponent(component).ToString(Formatting.None);break;
                case "add_component":
                    if(RequiresReview(c)&&context?.Reviewed!=true)throw new InvalidOperationException("Custom component requires explicit review.");target=resolve(c.target);var type=ResolveType((string)a["type"]);var added=Undo.AddComponent(target,type);if(!added)throw new InvalidOperationException("Component creation failed.");summary=InspectComponent(added).ToString(Formatting.None);break;
                case "read_console":summary=AgentEditorJobs.ConsoleSnapshot((int?)a["limit"]??20).ToString(Formatting.None);break;
                case "capture_scene":
                    if(context?.Message==null)throw new InvalidOperationException("Capture requires a chat task context.");var image=AgentViewCapture.Capture(view:(string)a["view"]??"overview");context.Message.Attachments.Add(image);var capture=AgentViewCapture.Describe(image);capture["view"]=(string)a["view"]??"overview";capture["diagnosticFogSuppressed"]=(string)capture["view"]=="overview";summary=capture.ToString(Formatting.None);break;
                case "run_runtime_probe":
                    if(context?.Session?.Settings?.AllowEditorCode!=true||context.Session.Settings.Mode!=AssistantMode.Execute&&context.Reviewed!=true)throw new InvalidOperationException("Runtime C# probes require the project's autonomous Editor C# permission.");
                    var probeJob=AgentEditorJobs.RunRuntimeProbe((string)a["code"],(float?)a["seconds"]??10);if(context?.Message?.Task!=null)context.Message.Task.PendingJobId=probeJob.Id;summary=JObject.FromObject(probeJob).ToString(Formatting.None);break;
                case "run_tests":var testJob=AgentEditorJobs.RunTests((string)a["mode"],(string)a["filter"]);if(context?.Message?.Task!=null)context.Message.Task.PendingJobId=testJob.Id;summary=JObject.FromObject(testJob).ToString(Formatting.None);break;
                case "run_player_check":var playerJob=AgentEditorJobs.RunPlayerCheck(ProjectContextIndex.StableTarget(resolve(c.target)),(float?)a["seconds"]??1,(bool?)a["check_relaxed_arms"]??false);if(context?.Message?.Task!=null)context.Message.Task.PendingJobId=playerJob.Id;summary=JObject.FromObject(playerJob).ToString(Formatting.None);break;
                case "run_play_check":var playJob=AgentEditorJobs.RunRotationCheck(ProjectContextIndex.StableTarget(resolve(c.target)),(float)a["seconds"],(float)a["degrees_per_second"]);if(context?.Message?.Task!=null)context.Message.Task.PendingJobId=playJob.Id;summary=JObject.FromObject(playJob).ToString(Formatting.None);break;
                case "poll_job":summary=AgentEditorJobs.Poll((string)a["id"]).ToString(Formatting.None);break;
                case "build_player":var build=AgentEditorJobs.BuildPlayer();if(context?.Message?.Task!=null)context.Message.Task.PendingJobId=build.Id;summary=JObject.FromObject(build).ToString(Formatting.None);break;
                case "set_build_scenes":
                    if(context?.Reviewed!=true)throw new InvalidOperationException("Build Settings scene changes require review.");
                    EditorBuildSettings.scenes=((JArray)a["paths"]).Values<string>().Select(path=>new EditorBuildSettingsScene(path,true)).ToArray();summary="Сцены сборки: "+string.Join(",",EditorBuildSettings.scenes.Select(s=>s.path));break;
                case "remember_design":if(context?.Session==null)throw new InvalidOperationException("Memory requires session context.");AgentDesignMemory.Remember(context.Session,(string)a["key"],(string)a["value"],context.Message.Id,(string)a["category"]??"decision");summary="Решение сохранено в памяти проекта с источником текущего запроса.";break;
                case "list_skills":var catalog=AgentSkillCatalog.CreateDefault(context?.Session?.Settings?.SkillFolders);summary=JsonConvert.SerializeObject(string.IsNullOrWhiteSpace((string)a["query"])?catalog.List().Take((int?)a["limit"]??20):catalog.Search((string)a["query"],(int?)a["limit"]??20));break;
                case "read_skill":var skills=AgentSkillCatalog.CreateDefault(context?.Session?.Settings?.SkillFolders);var skillId=(string)a["id"];summary=new JObject{["id"]=skillId,["body"]=skills.ReadStrict(skillId,6000),["enabledTools"]=new JArray(skills.ToolsFor(skillId)),["changesApplied"]=false}.ToString(Formatting.None);break;
                case "open_scene":if(context?.Reviewed!=true)throw new InvalidOperationException("Opening another scene requires review.");EditorSceneManager.OpenScene(ScenePath((string)a["path"]));summary="Открыта сцена "+(string)a["path"];break;
                default:throw new ArgumentException("Unknown project tool.");
            }
            return new EditorCommandResult{Success=true,ChangesApplied=changesApplied,Tool=c.tool,Summary=summary,Target=target?ProjectContextIndex.StableTarget(target):c.target};
        }
    }
}
