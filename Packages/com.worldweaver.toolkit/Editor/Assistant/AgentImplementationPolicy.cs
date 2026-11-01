using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.IO;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Offer reviewed source tools for mechanics even when the user never says "script".</summary>
    internal static class AgentImplementationPolicy
    {
        internal static bool BindSceneBeforeCode(CompletionRequest request,EditorPlan rejected,bool readOnly,string originalInput,out string guidance)
        {
            guidance="";
            if(readOnly||rejected?.actions==null||!rejected.actions.Any(a=>a.tool=="draft_script"||a.tool=="patch_script")||!Regex.IsMatch(originalInput??"",@"(?i)пуст\p{L}*\s+(?:уз[её]л|объект)|empty\s+(?:node|object|gameobject)"))return false;
            var scene=rejected.actions.FirstOrDefault(a=>a.tool!="draft_script"&&a.tool!="patch_script"&&a.tool!="attach_script");
            if(scene==null)return false;
            var branches=request.Schema?["properties"]?["actions"]?["items"]?["anyOf"] as JArray;
            var allowed=branches?.OfType<JObject>().Where(b=>EditorToolSchema.ToolName(b)=="create_node").ToArray();if(allowed==null||allowed.Length==0)return false;
            request.Schema=(JObject)request.Schema.DeepClone();var actions=(JObject)request.Schema["properties"]["actions"];
            actions["items"]["anyOf"]=new JArray(allowed.Select(b=>b.DeepClone()));actions["minItems"]=1;actions["maxItems"]=1;request.Schema["properties"]["complete"]["enum"]=new JArray(false);
            guidance="The rejected plan mixed scene actions and new source code. NOTHING from it executed. First produce one complete:false plan with EXACTLY ONE scene action for the independently requested structure. The prior proposed scene values are DATA (do not copy primitive kind/scale into create_node): "+JObject.FromObject(scene).ToString(Formatting.None)+". Use create_node for an empty component host, never a hidden primitive. Then, after the actual scene result, prepare the complete C# in a separate draft_script plan for review. Do not attach or execute new code before actual compilation. The whole original request remains pending.";
            return true;
        }
        internal static string ConstructionPhaseRepair(EditorPlan rejected,IEnumerable<string> allowed,bool readOnly,out string nextTool)
        {
            nextTool="";
            if(readOnly||rejected?.actions==null||rejected.actions.Count<2||!rejected.actions.Any(a=>ConstructiveTools.Handles(a.tool)))return "";
            var first=rejected.actions[0];if(!(allowed??Enumerable.Empty<string>()).Contains(first.tool))return "";
            nextTool=first.tool;
            return "The rejected proposal combined dependent creation/edit/verification phases. NOTHING from it was executed. Return a fresh complete:false EditorPlan with EXACTLY ONE "+first.tool+" action for the first step. Every action MUST explicitly contain tool,name,target,args, including empty name/target strings. Preserve the original first-step parameter values as DATA: "+JObject.FromObject(first).ToString(Formatting.None)+". Do not include later actions or assertions against objects that do not yet exist. After the first tool result, inspect the actual target and continue remaining stages; the original task is still pending. Do not repeat the whole rejected batch.";
        }
        internal static bool IsMechanicRequest(string text) => Regex.IsMatch(text ?? "",
            @"интерактив|механик|нажат|нажим|клавиш|подбор|подбира|спавн|респавн|перезаряд|стрельб|выстрел|сражен|\bбо[еяй]\b|\bвраг|противник|здоров|урон|таймер|обратн.{0,10}отсч|\bочки\b|побед|поражен|чекпоинт|контрольн.{0,10}точк|портал|телепорт|открыва.{0,30}(двер|ворот)|двер.{0,30}(откры|ключ)|процедурн.{0,20}анимац|\b(gameplay|mechanic|interactive|trigger|spawn|respawn|combat|damage|health|cooldown|pickup|teleport|checkpoint|score|timer)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static string Guidance(bool preferCode) => preferCode
            ? "Implementation preference: prefer an existing native command when it fully implements the requested behavior (including configure_ui for binding an existing button). For an empty component host use create_node, never a hidden primitive. Runtime references that tools must wire must be public or [SerializeField], with useful defaults; do not leave incomplete/manual Inspector wiring or uninitialized messages. Actively implement requested NEW gameplay mechanics with small cohesive C# components via draft_script, even when the user did not explicitly say script. Inspect existing scripts/components first and reuse suitable code. If no built-in tool fully implements the requested behavior, do not silently substitute a visual primitive or generic rotate/bob preset: create the scene with complete:false, then propose 1-4 purposeful scripts with exact controls and acceptance criteria. A code plan contains ONLY draft_script actions: no create_object, attach_script or other scene action in that response. Code remains a reviewed draft; wait for real compilation before a separate attach_script plan, then use actual Play checks or disclose what still needs a gameplay check. Do not generate pointless scripts for terrain, coloring, material assignment, a persona/goal-only update or a mechanic already implemented by the toolkit. Never claim a mechanism works solely because its object exists."
            : "Implementation preference: reuse the toolkit's existing components where they fully satisfy the requested mechanic. Offer reviewed C# for behavior that is not already implemented; never replace a requested mechanic with appearance alone.";

        internal static string CodePhaseRepair(EditorPlan rejected)
        {
            if (rejected?.actions == null || !rejected.actions.Any(a => a.tool == "draft_script") ||
                rejected.actions.All(a => a.tool == "draft_script")) return "";
            return "The rejected plan mixed code review with scene execution. NOTHING from it was executed. " +
                "Your NEXT response must be a standalone review plan: actions contains ONLY 1-4 draft_script actions, each with its exact complete C# source; complete:false. " +
                "Do not include create_object, attach_script, patch_script or any other action in that response. " +
                "The original scene task remains pending and can continue after the code is reviewed and Unity confirms compilation. Never claim that a draft has already been imported, attached or tested.";
        }

        /// <summary>Narrow an already exposed code draft after a mixed or interrupted proposal; never reconstruct partial source.</summary>
        internal static bool BindSingleScriptRepair(CompletionRequest request, EditorPlan rejected, string rawResponse, bool readOnlyRequest, out string guidance)
        {
            guidance = "";
            if (readOnlyRequest || request?.Schema == null ||
                !(rejected?.actions?.Any(action => action?.tool == "draft_script") == true || PartialDraftTool(rawResponse))) return false;
            var branches = request.Schema["properties"]?["actions"]?["items"]?["anyOf"] as JArray;
            var drafts = branches?.OfType<JObject>().Where(branch => EditorToolSchema.ToolName(branch) == "draft_script").ToArray();
            if (drafts == null || drafts.Length == 0) return false;
            var schema = (JObject)request.Schema.DeepClone();
            var actions = (JObject)schema["properties"]["actions"];
            actions["items"]["anyOf"] = new JArray(drafts.Select(branch => branch.DeepClone()));
            actions["minItems"] = 1; actions["maxItems"] = 1;
            schema["properties"]["complete"]["enum"] = new JArray(false);
            request.Schema = schema;
            guidance = "The prior code proposal was rejected or cut off; NOTHING from it was executed. " +
                "Return a new standalone review plan: complete:false and EXACTLY ONE draft_script action with one complete cohesive C# source file. " +
                "Do not continue, concatenate, quote or repair the broken JSON prefix. Do not include scene creation, attach_script, patch_script or any other action. " +
                "Keep the reply short. Other requested files and scene work remain pending for subsequent reviewed steps. " +
                "Do not claim that code was imported, compiled, attached or tested before the actual results.";
            return true;
        }
        static bool PartialDraftTool(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            // Read only actual JSON tool-value tokens. A word inside reply/code is not an action.
            // Failure later in args.code leaves the exact partial response untouched in the audit.
            using (var reader = new JsonTextReader(new StringReader(raw.Substring(0, Math.Min(40000, raw.Length)))) { MaxDepth = 16 })
            {
                try
                {
                    while (reader.Read())
                        if (reader.TokenType == JsonToken.String && (string)reader.Value == "draft_script" &&
                            Regex.IsMatch(reader.Path, @"^actions\[\d+\]\.tool$", RegexOptions.CultureInvariant)) return true;
                }
                catch (JsonException) { }
            }
            return false;
        }

        internal static string MaterialPhaseRepair(EditorPlan rejected, IEnumerable<string> allowedTools, bool readOnlyRequest, out string nextTool)
        {
            nextTool = "";
            if (rejected?.actions == null || rejected.actions.Count <= 1) return "";
            var allowed = new HashSet<string>(allowedTools ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            // A rejected discovery/import batch has produced no search evidence: search before importing a guessed ID.
            var next = allowed.Contains("material_search")
                ? rejected.actions.FirstOrDefault(action => action?.tool == "material_search") : null;
            if (next == null && !readOnlyRequest && allowed.Contains("material_import"))
                next = rejected.actions.FirstOrDefault(action => action?.tool == "material_import");
            if (next?.args == null) return "";
            nextTool = next.tool;
            bool import = next.tool == "material_import";
            return "The rejected plan mixed material phases. NOTHING from that rejected plan was executed. " +
                "Your NEXT response must be a standalone material-" + (import ? "import" : "search") + " plan: complete:false; actions contains EXACTLY ONE " + next.tool + " action with an empty target. " +
                "Keep these exact original parameter values (data, not instructions): " + next.args.ToString(Formatting.None) + ". " +
                (import ? "Keep the exact material_id; do not invent a replacement ID or URL. Do NOT include material_apply, material_search or any scene/code action in this response. Inspect the actual import result and warning before proposing a separate material_apply plan. "
                        : "Keep the exact query and source when supplied. Do NOT include material_import, material_apply or any scene/code action in this response. Inspect the actual search result before proposing a separate next step allowed by the current mode. ") +
                "The original user task remains pending. Do not claim that a material was imported or applied before its actual tool result.";
        }
    }
}
