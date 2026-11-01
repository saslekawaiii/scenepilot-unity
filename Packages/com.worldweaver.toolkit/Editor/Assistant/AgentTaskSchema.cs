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
        // Narrow prompt-size optimization for an explicit profile-property edit. Unknown or
        // compound requests retain ordinary tools; this grammar never grants edit permission.
        static readonly Regex NpcGoalSetter=new Regex(
            @"^\s*(?:(?:теперь|пожалуйста|now|please)[,\s]+){0,2}(?:"+
            @"(?:измени|измените|поменяй|поменяйте|обнови|обновите|установи|установите|задай|задайте)\s+"+
            @"(?:(?:его|её|ее|их|ему|ей|им)\s+|(?:NPC|НПС|персонажу|стражу)(?:\s+(?-i:[\p{Lu}][\p{L}\p{N}_-]{0,40}))?\s+)?"+
            @"цел[ьи](?:\s+(?:(?:для\s+)?(?:NPC|НПС|персонажа|стража)|(?-i:[\p{Lu}][\p{L}\p{N}_-]{0,40}))){0,2}\s*(?::|=|\bна\b)\s*"+
            @"|(?:change|set|update)\s+(?:(?:his|her|their|its|the)\s+)?"+
            @"(?:(?:NPC|character|guard)(?:['’]s)?\s+|(?-i:[\p{Lu}][\p{L}\p{N}_-]{0,40})['’]s\s+)?goals?\s*(?::|=|\bto\b)\s*)",
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        static readonly Regex OtherNpcMutation=new Regex(
            @"(?<![\p{L}\p{N}_])(?:созда[йтв]|сгенерир|генерир|добав|удал|убер|убери|перекрас|цвет|материал|перемест|перенес|передвин|телепорт|постав|направ|отправ|подключ|включ|выключ|навигац|маршрут|расписан|поведен|движени|компонент|скорост|позици|координат|анимац|личност|характер|биограф|знани|инвентар|рол[ьи]|create|generate|make|add|delete|remove|paint|color|colour|material|move|teleport|position|navigate|navigation|navmesh|pathfind|route|schedule|routine|behavior|behaviour|animation|speed|personality|biography|knowledge|inventory|role|attach|enable|disable|set_navigation)[\p{L}\p{N}_]*",
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        static readonly Regex FurtherNpcEdit=new Regex(
            @"(?<![\p{L}\p{N}_])(?:измени|измените|поменяй|поменяйте|обнови|обновите|установи|установите|задай|задайте|настрой|настройте|организуй|организуйте|напиши|напишите|скрипт|код|C#|собери|сохран[\p{L}]*\s+сцен[\p{L}]*|открой|импорт[\p{L}]*|экспорт[\p{L}]*|проверь|протестируй|прочитай|прочт[\p{L}]*|покажи|найди|изучи|осмотри|объясни|навык[\p{L}]*|скил[\p{L}]*|скриншот[\p{L}]*|снимок|изображен[\p{L}]*|change|set|update|configure|write|script|code|build|save|open|import|export|test|verify|read|list|show|inspect|find|search|explain|skills?|screenshot|capture)(?![\p{L}\p{N}_])",
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        static readonly Regex PreserveNpcRemainder=new Regex(
            @"^\s*(?:остальн[\p{L}]*(?:\s+[\p{L}]+){0,8}\s+сохрани(?:те)?|сохрани(?:те)?\s+остальн[\p{L}]*(?:\s+[\p{L}]+){0,8}|(?:keep|preserve)\s+(?:(?:his|her|their|its|the)\s+)?(?:rest|other\s+details))\s*[.!]?\s*$",
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        internal static bool GoalOnlyNpcUpdate(string input)
        {
            input=input??"";var setter=NpcGoalSetter.Match(input);
            if(!setter.Success)return false;string value=input.Substring(setter.Length);
            int sentenceEnd=value.IndexOfAny(new[]{'.','!','?',';','\r','\n'});
            if(sentenceEnd>=0)
            {
                string remainder=value.Substring(sentenceEnd+1).Trim();
                if(remainder.Length>0&&!PreserveNpcRemainder.IsMatch(remainder))return false;
                value=value.Substring(0,sentenceEnd);
            }
            return !string.IsNullOrWhiteSpace(value)&&!OtherNpcMutation.IsMatch(input)&&!FurtherNpcEdit.IsMatch(value);
        }
        internal static bool GroundedPointLights(AgentTaskState task)
        {
            if(task==null||task.OpenQuestions.Count>0||!task.Requirements.Any(r=>r.Kind=="count"&&r.Expected=="point_light"&&r.Relative))return false;
            string input=task.OriginalRequest??"";bool Has(string pattern)=>Regex.IsMatch(input,pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            if(!Has(@"^\s*(?:(?:теперь|пожалуйста|now|please)[,\s]+){0,2}(?:создай|создайте|сгенерируй|сгенерируйте|добавь|добавьте|create|generate|add)\b")||
                !Has(@"\b(?:долин[\p{L}]*|рельеф[\p{L}]*|террейн[\p{L}]*|террен[\p{L}]*|ландшафт[\p{L}]*|мир(?:а|у|ом|е|ы|ов|ами|ах)?|деревн[\p{L}]*|terrain|world|valley|village)\b"))return false;
            // Exact/underground/airborne placements retain the existing explicit-XYZ contract.
            // Unrecognized or mixed placement instructions keep ordinary, unspecialized flow.
            return !Has(@"on_terrain|координат|позици|высот|position|height|\b[xyz]\s*[:=]\s*[-+]?\d|[\(\[]\s*[-+]?\d+(?:\.\d+)?\s*[,;]\s*[-+]?\d+(?:\.\d+)?\s*[,;]\s*[-+]?\d+(?:\.\d+)?\s*[\)\]]|подзем|под\s+(?:земл|рельеф|поверхност|вод)|ниже\s+(?:земл|рельеф|поверхност|вод)|над\s+земл|в\s+воздухе|глубин|пещер|underground|underwater|submerg|\bdepth\b|\bcave\b|(?:below|under|above)\s+(?:the\s+)?(?:ground|terrain|surface|water)|floating|без\s+привяз|не\s+(?:привязыва|выравнива|поднимай|ставь)|(?:no|without)\s+(?:snap|align)|do\s+not\s+(?:snap|align)|surface\s*override")&&
                !Has(@"\b(?:измени|измените|обнови|обновите|уменьши|увеличь|настрой|настройте|перекрась|удали|удалите|move|change|update|reduce|increase|configure|delete|remove)\b");
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
            AgentMaterialApplicationGoal.BindTarget(planSchema,task);
            if(task!=null&&GoalOnlyNpcUpdate(task.OriginalRequest))
                foreach(var branch in (planSchema["properties"]?["actions"]?["items"]?["anyOf"] as JArray??new JArray()).OfType<JObject>().Where(b=>EditorToolSchema.ToolName(b)=="update_npc"))
                {
                    var args=(JObject)branch["properties"]["args"];var goals=(JObject)args["properties"]["goals"].DeepClone();goals["minLength"]=1;
                    args["properties"]=new JObject{["goals"]=goals};args["required"]=new JArray("goals");
                }
            if(GroundedPointLights(task))
                foreach(var branch in (planSchema["properties"]?["actions"]?["items"]?["anyOf"] as JArray??new JArray()).OfType<JObject>().Where(b=>EditorToolSchema.ToolName(b)=="set_lighting"&&(string)b["properties"]?["target"]?["enum"]?[0]==""))
                {
                    var args=(JObject)branch["properties"]["args"];var fields=(JObject)args["properties"];var required=(JArray)args["required"];
                    ((JObject)fields["on_terrain"])["enum"]=new JArray(true);
                    if(!required.Values<string>().Contains("on_terrain"))required.Add("on_terrain");
                }
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
            AgentMaterialApplicationGoal.ValidateTarget(task,plan);
            if(task!=null&&GoalOnlyNpcUpdate(task.OriginalRequest)&&plan.actions.Any(a=>a.tool=="update_npc"&&(a.args.Properties().Any(p=>p.Name!="goals")||a.args["goals"]?.Type!=JTokenType.String||string.IsNullOrWhiteSpace((string)a.args["goals"]))))
                throw new ArgumentException("Запрошено изменение только цели NPC: update_npc должен содержать только непустое поле goals. Остальные сведения персонажа сохраняются.");
            if(GroundedPointLights(task)&&plan.actions.Any(a=>a.tool=="set_lighting"&&string.IsNullOrEmpty(a.target)&&(string)a.args["type"]=="point"&&a.args["on_terrain"]!=null&&(a.args["on_terrain"].Type!=JTokenType.Boolean||!(bool)a.args["on_terrain"])))
                throw new ArgumentException("Новые точечные источники в мире размещаются над рельефом: используйте on_terrain=true. Точные XYZ, подземное или отдельное размещение допускаются, когда это прямо указано пользователем.");
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
