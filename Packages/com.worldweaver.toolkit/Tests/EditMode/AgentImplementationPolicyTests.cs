using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class AgentImplementationPolicyTests
    {
        static HashSet<string> Tools(string input, AssistantMode mode)
        {
            var method = typeof(ChatController).GetMethod("SelectTools", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method); return (HashSet<string>)method.Invoke(null, new object[] { input, mode });
        }
        static string MaterialHint(EditorPlan plan, IEnumerable<string> allowed, bool readOnly=false)
        {
            var type=typeof(ChatController).Assembly.GetType("WorldWeaver.Editor.Assistant.AgentImplementationPolicy");
            var method=type.GetMethod("MaterialPhaseRepair",BindingFlags.Static|BindingFlags.NonPublic);
            Assert.IsNotNull(method);var args=new object[]{plan,allowed,readOnly,null};string hint=(string)method.Invoke(null,args);
            Assert.AreEqual(hint.Length>0,!string.IsNullOrEmpty((string)args[3]),"A phase hint must identify the one already-permitted next tool.");return hint;
        }
        static EditorCommand Material(string tool,JObject args,string target="")=>new EditorCommand {tool=tool,target=target,args=args};
        [Test] public void MixedMaterialImportRepairPreservesExactParametersWithoutCopyingApplyPlan()
        {
            var import=Material("material_import",new JObject {["material_id"]="polyhaven:weathered_planks",["allow_online"]=true});
            var plan=new EditorPlan {actions=new List<EditorCommand>{import,Material("material_apply",new JObject {["material_id"]="polyhaven:weathered_planks"},"@RejectedDoorAnchor")}};
            string before=JsonConvert.SerializeObject(plan);string hint=MaterialHint(plan,new[]{"material_search","material_import","material_apply"});
            StringAssert.Contains("EXACTLY ONE material_import",hint);StringAssert.Contains("complete:false",hint);StringAssert.Contains(import.args.ToString(Formatting.None),hint);StringAssert.DoesNotContain("RejectedDoorAnchor",hint);
            Assert.AreEqual(before,JsonConvert.SerializeObject(plan),"Repair guidance must not split, mutate or normalize the recorded plan.");
        }
        [Test] public void UnexecutedMixedSearchImportAndApplyRepairsDiscoveryBeforeAnyGuessedIdImport()
        {
            var search=Material("material_search",new JObject {["query"]="weathered wood",["source"]="polyhaven",["limit"]=5});
            var plan=new EditorPlan {actions=new List<EditorCommand>{search,Material("material_import",new JObject {["material_id"]="polyhaven:guessed_before_discovery",["allow_online"]=true}),Material("material_apply",new JObject {["material_id"]="polyhaven:guessed_before_discovery"},"@GuessedDoor")}};
            string before=JsonConvert.SerializeObject(plan);string hint=MaterialHint(plan,new[]{"material_search","material_import","material_apply"});
            StringAssert.Contains("EXACTLY ONE material_search",hint);StringAssert.Contains(search.args.ToString(Formatting.None),hint);StringAssert.DoesNotContain("guessed_before_discovery",hint);StringAssert.DoesNotContain("GuessedDoor",hint);Assert.AreEqual(before,JsonConvert.SerializeObject(plan));
        }
        [Test] public void MixedMaterialSearchRepairPreservesQuerySourceAndLimit()
        {
            var search=Material("material_search",new JObject {["query"]="старое дерево / weathered wood",["source"]="builtin",["limit"]=3});
            var plan=new EditorPlan {actions=new List<EditorCommand>{search,Material("material_apply",new JObject {["material_id"]="builtin:wood"},"RejectedApply")}};
            string hint=MaterialHint(plan,new[]{"material_search","material_apply"});
            StringAssert.Contains("EXACTLY ONE material_search",hint);StringAssert.Contains(search.args.ToString(Formatting.None),hint);StringAssert.DoesNotContain("RejectedApply",hint);
        }
        [Test] public void MaterialPhaseGuidanceCannotEnableAnUnexposedOrReadOnlyForbiddenImport()
        {
            var import=Material("material_import",new JObject {["material_id"]="polyhaven:weathered_planks",["allow_online"]=true});
            var plan=new EditorPlan {actions=new List<EditorCommand>{import,Material("material_apply",new JObject {["material_id"]="polyhaven:weathered_planks"},"Door")}};
            Assert.AreEqual("",MaterialHint(plan,new[]{"material_search"}));Assert.AreEqual("",MaterialHint(plan,new[]{"material_import"},true));Assert.AreEqual("",MaterialHint(plan,Array.Empty<string>()));
            plan.actions.Add(Material("material_search",new JObject {["query"]="wood",["source"]="builtin"}));
            string ask=MaterialHint(plan,new[]{"material_search","material_import"},true);
            StringAssert.Contains("EXACTLY ONE material_search",ask);StringAssert.DoesNotContain("EXACTLY ONE material_import",ask);StringAssert.DoesNotContain("polyhaven:weathered_planks",ask);
        }
        [Test] public void MaterialPhaseGuidanceDoesNotAlterStandaloneOrUnrelatedPlans()
        {
            var import=Material("material_import",new JObject {["material_id"]="builtin:wood"});
            Assert.AreEqual("",MaterialHint(null,new[]{"material_import"}));Assert.AreEqual("",MaterialHint(new EditorPlan {actions=new List<EditorCommand>{import}},new[]{"material_import"}));
            Assert.AreEqual("",MaterialHint(new EditorPlan {actions=new List<EditorCommand>{Material("inspect_object",new JObject(),"Door"),Material("material_apply",new JObject {["material_id"]="builtin:wood"},"Door")}},new[]{"material_import","material_search"}));
        }
        [Test] public void RequestedMechanicOffersReviewedSourceToolsWithoutTheWordScript()
        {
            const string request = "Создай дверь, которая открывается по нажатию клавиши E, требует ключ и закрывается через 5 секунд.";
            var execute = Tools(request, AssistantMode.Execute);
            foreach (string tool in new[] { "read_script", "inspect_component", "draft_script", "attach_script", "run_tests", "run_play_check" }) Assert.IsTrue(execute.Contains(tool), tool);
            var ask = Tools(request, AssistantMode.Ask);
            Assert.IsTrue(ask.Contains("read_script")); Assert.IsTrue(ask.All(SceneToolExecutor.IsReadOnlyTool), "Ask may inspect a mechanic but must not expose a source/scene mutation.");
        }
        [Test] public void CombatWordsInAGoalOnlyNpcUpdateCannotEnableCodeOrAiReconfiguration()
        {
            const string request = "Измени цель NPC: защищать жителей от врагов и побеждать в бою.";
            var schema = typeof(ChatController).Assembly.GetType("WorldWeaver.Editor.Assistant.AgentTaskSchema");
            Assert.IsNotNull(schema); var goalOnly = schema.GetMethod("GoalOnlyNpcUpdate", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsTrue((bool)goalOnly.Invoke(null, new object[] { request }), "Fixture must exercise the narrow goal-only grammar.");
            var tools = Tools(request, AssistantMode.Execute); Assert.IsTrue(tools.Contains("update_npc"));
            foreach (string tool in new[] { "draft_script", "attach_script", "patch_script", "add_component", "set_component", "configure_npc_ai", "create_npc", "attach_behavior", "set_navigation" })
                Assert.IsFalse(tools.Contains(tool), tool + " would expand a profile text change into unrelated behavior.");
        }
        [Test] public void TextureOnlyRequestKeepsFocusedToolsAndAskCannotImport()
        {
            const string request = "Найди текстуру штукатурки.";
            var execute = Tools(request, AssistantMode.Execute);
            foreach (string tool in new[] { "material_search", "material_import", "material_apply" }) Assert.IsTrue(execute.Contains(tool), tool);
            foreach (string tool in new[] { "draft_script", "create_world", "delete_object", "configure_npc_ai", "build_player" }) Assert.IsFalse(execute.Contains(tool), "Texture request must not trigger the all-tools fallback: " + tool);
            var ask = Tools(request, AssistantMode.Ask); Assert.IsTrue(ask.Contains("material_search")); Assert.IsTrue(ask.All(SceneToolExecutor.IsReadOnlyTool));
        }
    }
}
