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
        static bool SingleScriptRepair(CompletionRequest request, EditorPlan rejected, string raw, bool readOnly, out string hint)
        {
            var type=typeof(ChatController).Assembly.GetType("WorldWeaver.Editor.Assistant.AgentImplementationPolicy");
            var args=new object[]{request,rejected,raw,readOnly,null};bool bound=(bool)type.GetMethod("BindSingleScriptRepair",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);hint=(string)args[4];return bound;
        }
        [Test] public void InterruptedCodeProposalGetsOneAlreadyAllowedDraftWithoutRepairingRawOrOriginalSchema()
        {
            var schema=EditorPlan.Schema(true,new[]{"inspect_object","draft_script","attach_script","create_object"});string before=schema.ToString();
            const string raw="{\"reply\":\"Готовлю героя\",\"complete\":false,\"actions\":[{\"tool\":\"create_object\",\"name\":\"RejectedHero\",\"target\":\"\",\"args\":{}},{\"tool\":\"draft_script\",\"name\":\"HeroController\",\"target\":\"\",\"args\":{\"code\":\"using UnityEngine; public class";
            var request=new CompletionRequest {Schema=schema};Assert.IsTrue(SingleScriptRepair(request,null,raw,false,out string hint));
            Assert.AreEqual(before,schema.ToString(),"A previous request keeps its exact schema for the audit trail.");Assert.AreNotSame(schema,request.Schema);
            var actions=request.Schema["properties"]["actions"];Assert.AreEqual(1,(int)actions["minItems"]);Assert.AreEqual(1,(int)actions["maxItems"]);
            var branches=((JArray)actions["items"]["anyOf"]).OfType<JObject>().ToArray();Assert.AreEqual(1,branches.Length);Assert.AreEqual("draft_script",EditorToolSchema.ToolName(branches[0]));
            Assert.AreEqual(false,(bool)request.Schema["properties"]["complete"]["enum"][0]);StringAssert.Contains("EXACTLY ONE draft_script",hint);StringAssert.DoesNotContain("RejectedHero",hint);StringAssert.DoesNotContain("using UnityEngine",hint);
            Assert.Throws<JsonReaderException>(()=>EditorPlan.Parse(raw),"The partial raw output must stay broken, not be auto-completed into an executable plan.");
        }
        [Test] public void SingleScriptRepairCannotEnableCodeForAskAbsentToolsOrUnrelatedMalformedReplies()
        {
            const string code="{\"reply\":\"\",\"complete\":false,\"actions\":[{\"tool\":\"draft_script\",\"args\":{\"code\":\"cut";
            foreach(bool ask in new[]{false,true})
            {
                var request=new CompletionRequest {Schema=EditorPlan.Schema(true,ask?new[]{"draft_script","inspect_object"}:new[]{"inspect_object"})};var schema=request.Schema;string before=schema.ToString();
                Assert.IsFalse(SingleScriptRepair(request,null,code,ask,out string hint));Assert.AreSame(schema,request.Schema);Assert.AreEqual(before,schema.ToString());Assert.AreEqual("",hint);
            }
            foreach(string raw in new[]{"{\"reply\":\"draft_script is only a word", "{\"reply\":\"\",\"complete\":false,\"actions\":[{\"tool\":\"material_search\",\"args\":{\"query\":\"draft_script"})
            {
                var request=new CompletionRequest {Schema=EditorPlan.Schema(true,new[]{"draft_script","material_search"})};var schema=request.Schema;
                Assert.IsFalse(SingleScriptRepair(request,null,raw,false,out _));Assert.AreSame(schema,request.Schema);
            }
        }
        [Test] public void ParsedMixedCodeRepairDoesNotMutateRejectedActionsOrAddPreviouslyMissingTools()
        {
            var plan=new EditorPlan {actions=new List<EditorCommand>{new EditorCommand {tool="create_object",name="RawRejectedCube",args=new JObject()},new EditorCommand {tool="draft_script",name="Draft",args=new JObject { ["code"]="complete source remains exact"}}}};
            string before=JsonConvert.SerializeObject(plan);var request=new CompletionRequest {Schema=EditorPlan.Schema(false,new[]{"draft_script","create_object"})};
            Assert.IsTrue(SingleScriptRepair(request,plan,null,false,out _));Assert.AreEqual(before,JsonConvert.SerializeObject(plan));
            var denied=new CompletionRequest {Schema=EditorPlan.Schema(false,new[]{"create_object"})};Assert.IsFalse(SingleScriptRepair(denied,plan,null,false,out _));
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
