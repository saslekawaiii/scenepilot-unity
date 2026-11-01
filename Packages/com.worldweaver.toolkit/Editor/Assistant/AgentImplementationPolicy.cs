using System.Text.RegularExpressions;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Offer reviewed source tools for mechanics even when the user never says "script".</summary>
    internal static class AgentImplementationPolicy
    {
        internal static bool IsMechanicRequest(string text) => Regex.IsMatch(text ?? "",
            @"интерактив|механик|нажат|нажим|клавиш|подбор|подбира|спавн|респавн|перезаряд|стрельб|выстрел|сражен|\bбо[еяй]\b|\bвраг|противник|здоров|урон|таймер|обратн.{0,10}отсч|\bочки\b|побед|поражен|чекпоинт|контрольн.{0,10}точк|портал|телепорт|открыва.{0,30}(двер|ворот)|двер.{0,30}(откры|ключ)|процедурн.{0,20}анимац|\b(gameplay|mechanic|interactive|trigger|spawn|respawn|combat|damage|health|cooldown|pickup|teleport|checkpoint|score|timer)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static string Guidance(bool preferCode) => preferCode
            ? "Implementation preference: actively implement requested NEW gameplay mechanics with small cohesive C# components via draft_script, even when the user did not explicitly say script. Inspect existing scripts/components first and reuse suitable code. If no built-in tool fully implements the requested behavior, do not silently substitute a visual primitive or generic rotate/bob preset: create the scene with complete:false, then propose 1-4 purposeful scripts with exact controls and acceptance criteria. Code remains a reviewed draft; wait for real compilation before attach_script, then use actual Play checks or disclose what still needs a gameplay check. Do not generate pointless scripts for terrain, coloring, material assignment, a persona/goal-only update or a mechanic already implemented by the toolkit. Never claim a mechanism works solely because its object exists."
            : "Implementation preference: reuse the toolkit's existing components where they fully satisfy the requested mechanic. Offer reviewed C# for behavior that is not already implemented; never replace a requested mechanic with appearance alone.";
    }
}
