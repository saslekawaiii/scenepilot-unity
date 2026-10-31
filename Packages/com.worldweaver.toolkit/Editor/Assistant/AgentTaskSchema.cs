using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Literal user constraints specialize generation; the host repeats the same pre-mutation guard.</summary>
    internal static class AgentTaskSchema
    {
        // High-precision finite direct-object grammar. A location ("in the valley") is never a modifier.
        // Unknown phrasing keeps ordinary tool flow; this heuristic is not an authorization boundary.
        static readonly Regex WorldCreation=new Regex(
            @"(?<![\p{L}\p{N}_])(?:"+
            @"(?:создай|создайте|создать|создавай|создавайте|сгенерируй|сгенерируйте|сгенерировать|сгенерь|генерируй|генерируйте|генерь|сделай|сделайте|сделать)"+
            @"(?:\s+(?:мне|нам|пожалуйста|новый|новую|небольшой|небольшую|большой|большую|процедурный|процедурную|лесной|лесную|игровой|игровую|играбельный|играбельную|связный|связную|пустой|пустую)){0,6}"+
            @"\s+(?:мир|долину|деревню|поселение|ландшафт|рельеф|террейн|террен)"+
            @"|(?:create|generate|make)(?:\s+(?:a|an|the|new|small|large|procedural|playable|forest|forested|connected|empty|game)){0,6}"+
            @"\s+(?:world|terrain|valley|village|settlement|landscape))(?![\p{L}\p{N}_])",
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        static readonly Regex NegatedCreation=new Regex(
            @"(?:\bне(?:\s+(?:надо|нужно|следует))?|\bnever|\bnot|\bdon['’]t|\bdo\s+not)\s*$",
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        internal static bool DirectWorldCreation(string input)
        {
            input=input??"";
            foreach(Match match in WorldCreation.Matches(input))
                if(!NegatedCreation.IsMatch(input.Substring(0,match.Index)))return true;
            return false;
        }
        internal static bool CreatesWorld(AgentTaskState task)=>task!=null&&task.OpenQuestions.Count==0&&DirectWorldCreation(task.OriginalRequest);
        static Dictionary<string,double> Bindings(AgentTaskState task,string tool)
        {
            var result=new Dictionary<string,double>();
            foreach(var r in task.Requirements)
            {
                if(r.Kind=="count")
                {
                    string field=r.Expected=="tree"?"trees":r.Expected=="house"?tool=="create_village"?"houses":"buildings":r.Expected=="rock"&&tool=="create_world"?"rocks":null;
                    if(field!=null)result[field]=r.Number;
                }
                else if(r.Kind=="terrain_width")result["width"]=r.Number;
                else if(r.Kind=="terrain_length"&&tool=="create_world")result["length"]=r.Number;
                else if(r.Kind=="terrain_height"&&tool=="create_world")result["height"]=r.Number;
            }
            return result;
        }
        internal static void Bind(JObject planSchema,AgentTaskState task)
        {
            if(!CreatesWorld(task))return;
            if(string.IsNullOrEmpty(task.ScopeTarget))((JObject)planSchema["properties"]["actions"])["maxItems"]=1;
            var union=planSchema["properties"]?["actions"]?["items"]?["anyOf"] as JArray;if(union==null)return;
            foreach(var branch in union.OfType<JObject>())
            {
                string tool=EditorToolSchema.ToolName(branch);if(tool!="create_world"&&tool!="create_village")continue;
                var args=(JObject)branch["properties"]["args"];var fields=(JObject)args["properties"];var required=(JArray)args["required"];
                foreach(var pair in Bindings(task,tool))
                {
                    var definition=fields[pair.Key] as JObject;if(definition==null)continue;
                    if(pair.Value<(double?)definition["minimum"]||pair.Value>(double?)definition["maximum"])throw new ArgumentException("Запрошено "+pair.Key+"="+pair.Value+", инструмент поддерживает "+definition["minimum"]+"…"+definition["maximum"]+". Изменения не выполнялись.");
                    definition["enum"]=new JArray((string)definition["type"]=="integer"?(JToken)new JValue((int)pair.Value):new JValue(pair.Value));
                    if(!required.Values<string>().Contains(pair.Key))required.Add(pair.Key);
                }
                var names=new HashSet<string>(required.Values<string>());args["required"]=new JArray(fields.Properties().Select(p=>p.Name).Where(names.Contains));
            }
        }
        internal static string KeyOrderHint(JObject schema)
        {
            var lines=new List<string>();var union=schema["properties"]?["actions"]?["items"]?["anyOf"] as JArray;if(union==null)return "";
            foreach(var command in union.OfType<JObject>())
            {
                var args=(JObject)command["properties"]["args"];
                foreach(var branch in (args["anyOf"] as JArray??new JArray(args)).OfType<JObject>())
                {
                    var required=(branch["required"] as JArray??new JArray()).Values<string>().ToArray();var properties=branch["properties"] as JObject;if(properties==null)continue;
                    string line=EditorToolSchema.ToolName(command)+": "+string.Join(",",required.Concat(properties.Properties().Select(p=>p.Name).Where(n=>!required.Contains(n))));if(!lines.Contains(line))lines.Add(line);
                }
            }
            return "LOCAL DECODER ORDER: plan reply,complete,actions; command tool,name,target,args. In args output required keys first, then ONLY needed optional keys in this order. Skip unwanted options, never return to an earlier key.\n"+string.Join("\n",lines);
        }
        internal static void Validate(AgentTaskState task,EditorPlan plan)
        {
            if(!CreatesWorld(task))return;
            foreach(var world in plan.actions.Where(a=>a.tool=="create_world"||a.tool=="create_village"))
            {
                foreach(var pair in Bindings(task,world.tool))
                {
                    var value=world.args[pair.Key];if(value==null||(value.Type!=JTokenType.Integer&&value.Type!=JTokenType.Float)||Math.Abs((double)value-pair.Value)>.001)throw new ArgumentException(world.tool+" должен передать буквальное условие "+pair.Key+"="+pair.Value+" из запроса. Не заменяйте его другими числами или повторным scatter.");
                }
                if(plan.actions.Any(a=>(a.tool=="scatter_objects"||a.tool=="create_object")&&task.Requirements.Any(r=>r.Kind=="count"&&r.Expected==(string)a.args["kind"])))throw new ArgumentException("create_world/create_village уже создаёт указанные декорации. Уберите повторное создание тех же деревьев/домов/камней.");
            }
        }
    }
}
