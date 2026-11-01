using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using Newtonsoft.Json;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Offer reviewed source tools for mechanics even when the user never says "script".</summary>
    internal static class AgentImplementationPolicy
    {
        internal static bool IsMechanicRequest(string text) => Regex.IsMatch(text ?? "",
            @"интерактив|механик|нажат|нажим|клавиш|подбор|подбира|спавн|респавн|перезаряд|стрельб|выстрел|сражен|\bбо[еяй]\b|\bвраг|противник|здоров|урон|таймер|обратн.{0,10}отсч|\bочки\b|побед|поражен|чекпоинт|контрольн.{0,10}точк|портал|телепорт|открыва.{0,30}(двер|ворот)|двер.{0,30}(откры|ключ)|процедурн.{0,20}анимац|\b(gameplay|mechanic|interactive|trigger|spawn|respawn|combat|damage|health|cooldown|pickup|teleport|checkpoint|score|timer)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static string Guidance(bool preferCode) => preferCode
            ? "Implementation preference: actively implement requested NEW gameplay mechanics with small cohesive C# components via draft_script, even when the user did not explicitly say script. Inspect existing scripts/components first and reuse suitable code. If no built-in tool fully implements the requested behavior, do not silently substitute a visual primitive or generic rotate/bob preset: create the scene with complete:false, then propose 1-4 purposeful scripts with exact controls and acceptance criteria. A code plan contains ONLY draft_script actions: no create_object, attach_script or other scene action in that response. Code remains a reviewed draft; wait for real compilation before a separate attach_script plan, then use actual Play checks or disclose what still needs a gameplay check. Do not generate pointless scripts for terrain, coloring, material assignment, a persona/goal-only update or a mechanic already implemented by the toolkit. Never claim a mechanism works solely because its object exists."
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

        internal static string MaterialPhaseRepair(EditorPlan rejected, IEnumerable<string> allowedTools, bool readOnlyRequest, out string nextTool)
        {
            nextTool = "";
            if (rejected?.actions == null || rejected.actions.Count <= 1) return "";
            var allowed = new HashSet<string>(allowedTools ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var next = !readOnlyRequest && allowed.Contains("material_import")
                ? rejected.actions.FirstOrDefault(action => action?.tool == "material_import") : null;
            if (next == null && allowed.Contains("material_search"))
                next = rejected.actions.FirstOrDefault(action => action?.tool == "material_search");
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
