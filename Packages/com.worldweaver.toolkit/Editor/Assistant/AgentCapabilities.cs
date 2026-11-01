using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Progressive tool discovery. Selection is a context optimization, never a list of total capabilities.</summary>
    internal static class AgentCapabilities
    {
        internal const string ToolName="discover_tools";
        internal const string Reference="discover_tools: standalone read-only action, empty target, args {query?:string,names?:string[]}. Returns actual typed command definitions and relevant skills; those commands become available in the next planning step. Query a category or exact names. If a needed tool is absent from your current schema, discover it before claiming it does not exist. Discovery never grants new permissions or applies changes.";
        static readonly (string Name,string Terms,string[] Tools)[] Groups={
            ("terrain","terrain heightmap relief moon lunar crater cave mountain landscape лун кратер пещер земля рельеф ландшафт",new[]{"create_world","update_world","shape_terrain","create_cave","inspect_object"}),
            ("objects","objects scene hierarchy mesh geometry custom bridge building procedural объект меш геометр мост здан произвольн мяч площадк платформ",new[]{"create_node","create_mesh","create_object","scatter_objects","set_parent","set_transform","instantiate_prefab","delete_object"}),
            ("characters","character player hero rig skin animation персонаж игрок герой анимац",new[]{"create_animated_player","create_player","configure_animator","catalog_assets","inspect_object"}),
            ("npc","npc residents intelligence dialogue patrol social memory жители жител нпс диалог разум патрул",new[]{"create_npc","update_npc","configure_npc_ai","attach_behavior","configure_npc_routine","set_navigation"}),
            ("materials","texture material pbr surface ассет текстур материал поверхност",new[]{"material_search","material_import","material_apply","set_material","catalog_assets"}),
            ("physics","physics rigidbody collision gravity joint vehicle simulation физик гравитац коллиз тверд паден пада отскок машин",new[]{"configure_physics","inspect_component","add_component","set_component"}),
            ("ui","ui canvas interface menu button hud layout интерфейс меню кнопк экран панель текст",new[]{"create_ui_element","configure_ui","inspect_component","set_component","draft_script","attach_script"}),
            ("survival","survival oxygen resources sample shelter выжив кислород ресурс укрыт образц",new[]{"configure_survival","create_resource","create_cave","create_animated_player"}),
            ("code","code script mechanic gameplay behavior custom скрипт код механик поведен систем",new[]{"create_node","read_script","draft_script","patch_script","attach_script","inspect_component","add_component","set_component","read_console"}),
            ("navigation","navigation navmesh path route навигац маршрут проходим путь",new[]{"bake_navigation","set_navigation","validate_world","find_scene_objects"}),
            ("presentation","lighting camera audio sound light screenshot освещ свет камер звук сним",new[]{"set_lighting","create_camera","configure_audio","capture_scene","search_project"}),
            ("project","project tests build verify compile save проект тест провер сборк сохран компиляц",new[]{"inspect_project","search_project","run_tests","poll_job","save_scene","set_build_scenes","build_player","read_console","verify_scene"})
        };
        internal static string Index => "AVAILABLE CAPABILITY CATEGORIES (not all are in this step's schema):\n"+string.Join("\n",Groups.Select(g=>g.Name+": "+string.Join(",",g.Tools)))+"\n"+Reference;
        internal static HashSet<string> Match(string query)
        {
            query=Regex.Replace(query??"",@"(?i)карт\p{L}*\s+высот\p{L}*","heightmap");
            var tokens=Regex.Matches(query.ToLowerInvariant(),@"[\p{L}\p{N}_-]{3,}").Cast<Match>().Select(m=>m.Value).Distinct().ToArray();
            var result=new HashSet<string>(StringComparer.Ordinal);
            foreach(var g in Groups)
                if(tokens.Any(t=>t==g.Name||g.Tools.Contains(t)||g.Terms.Split(' ').Any(term=>t.StartsWith(term,StringComparison.Ordinal)&&!(g.Name=="ui"&&t.StartsWith("текстур",StringComparison.Ordinal)))))
                    foreach(string tool in g.Tools)result.Add(tool);
            return result;
        }
        internal static JObject Discover(JObject args)
        {
            var names=(args["names"] as JArray??new JArray()).Values<string>().ToArray();
            var selected=Match((string)args["query"]);
            foreach(var name in names)selected.Add(name);
            var all=((JArray)EditorToolSchema.CommandSchema()["anyOf"]).OfType<JObject>().ToArray();
            var known=new HashSet<string>(all.Select(EditorToolSchema.ToolName));
            if(selected.Count==0)return new JObject{["index"]=Index,["enabledTools"]=new JArray(),["hint"]="Choose a category or exact tool names; this was discovery only, no changes were made."};
            var enabled=selected.Where(known.Contains).OrderBy(x=>Array.IndexOf(names,x)>=0?0:1).ThenBy(x=>x,StringComparer.Ordinal).Take(16).ToArray();
            var instructions=string.Join("\n",new[]{SceneToolExecutor.ToolInstructions,WorldGameplayTools.ToolInstructions,AgentProjectTools.ToolInstructions,WorldMaterialTools.ToolInstructions,ConstructiveTools.Reference,AnimatedCharacterTools.ToolReference}.SelectMany(s=>s.Split('\n')).Where(line=>enabled.Any(t=>line.StartsWith(t+":",StringComparison.Ordinal))));
            return new JObject{["enabledTools"]=new JArray(enabled),["definitions"]=new JArray(all.Where(c=>enabled.Contains(EditorToolSchema.ToolName(c))).Select(c=>c.DeepClone())),["instructions"]=instructions,
                ["unknownNames"]=new JArray(names.Where(n=>!known.Contains(n))),["skills"]=JArray.FromObject(AgentSkillCatalog.CreateDefault().Search((string)args["query"]??string.Join(" ",names),6)),["appliedChanges"]=false};
        }
        internal static bool NeedsExtendedWorkflow(string input)=>Regex.IsMatch(input??"",@"(?i)меш|геометр|физик|пещер|кратер|отскок|падающ|rigidbody|collider|physics|\bUI\b|canvas|кноп|меню|выжив|кислород|собственн.{0,25}мост|\bmesh\b|\bcave\b|\bcrater\b|\bsurvival\b|C#|скрипт|\bкод\b|\bscript\b|пуст\p{L}*\s+уз[её]л");
        internal static bool IsChangeRequest(string input)=>Regex.IsMatch(input??"",@"(?i)(?:^|[.!?;\n])\s*(?:пожалуйста[, ]+)?(?:добав\p{L}*|созда\p{L}*|сдела\p{L}*|сгенер\p{L}*|постро\p{L}*|измени\p{L}*|исправ\p{L}*|удали\p{L}*|убери\p{L}*|настро\p{L}*|напиши\p{L}*|add|create|make|generate|build|change|fix|remove|delete|configure|write)\b");
    }
}
