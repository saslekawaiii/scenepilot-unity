using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Verifies a literal application to an explicitly selected existing surface, not the artistic meaning of a material.</summary>
    internal static class AgentMaterialApplicationGoal
    {
        internal const string Kind = "material_application";
        const string ApplyVerb = @"(?:примени(?:те)?|применяй(?:те)?|применить|нанеси(?:те)?|нанести|наложи(?:те)?|наложить|apply|assign)";
        const string Selected = @"(?:выделенн\p{L}*|выбранн\p{L}*|selected)";
        static bool Matches(string input, string pattern) => Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static AgentRequirement Capture(string input, bool mutationAuthorized)
        {
            input = input ?? "";
            if (!mutationAuthorized || !Matches(input, @"\b(?:материал\p{L}*|текстур\p{L}*|materials?|textures?)\b")) return null;
            if (Matches(input, @"\b(?:не\s+(?:\p{L}+\s+){0,2}" + ApplyVerb + @"|без\s+применен\p{L}*|(?:do\s+not|don['’]t|never|not)\s+" + ApplyVerb + @"|without\s+(?:applying|assigning|application))\b")) return null;
            if (Matches(input, @"\b(?:не|not)\s+(?:(?:к|на|to|on|the)\s+){0,2}" + Selected + @"\b")) return null;
            // Explanation-only questions cannot become a scene-mutation requirement in Execute mode either.
            if (Matches(input, @"\b(?:объясни\p{L}*|расскажи\p{L}*|как\s+(?:мне\s+)?|explain|describe|how\s+to)\b[^.!?;\r\n]{0,100}\b" + ApplyVerb + @"\b") &&
                !Matches(input, @"\b(?:затем|после\s+этого|теперь|then|after\s+that)\s+" + ApplyVerb + @"\b")) return null;
            bool selectedApplication = Matches(input, @"\b" + ApplyVerb + @"\b[^.!?;\r\n]{0,140}\b(?:к|на|to|onto|on)\s+(?:(?:the|current|currently)\s+){0,2}" + Selected + @"\b") ||
                Matches(input, @"\b(?:к|на|to|onto|on)\s+(?:the\s+)?" + Selected + @"\b[^.!?;\r\n]{0,100}\b" + ApplyVerb + @"\b");
            if (!selectedApplication || Selection.gameObjects.Length != 1) return null;
            var target = Selection.activeGameObject;
            if (!target || target.scene != SceneManager.GetActiveScene() || (!target.GetComponent<Renderer>() && !target.GetComponent<Terrain>())) return null;
            return new AgentRequirement { Kind = Kind, Target = ProjectContextIndex.StableTarget(target), Expected = target.GetComponent<Terrain>() ? "terrain" : "renderer", Number = 1,
                Description = "Материал фактически применён к исходному выделенному объекту «" + target.name + "». Поиск и импорт без применения недостаточны." };
        }

        static AgentRequirement Captured(AgentTaskState task)
        {
            if (task?.MutationAuthorized != true) return null;
            var goals = task.Requirements.Where(r => r.Kind == Kind).Take(2).ToArray();
            return goals.Length == 1 && !string.IsNullOrEmpty(goals[0].Target) ? goals[0] : null;
        }

        internal static void BindTarget(JObject schema, AgentTaskState task)
        {
            var goal = Captured(task);
            var branches = schema?["properties"]?["actions"]?["items"]?["anyOf"] as JArray;
            if (goal == null || branches == null) return;
            for (int i = 0; i < branches.Count; i++)
            {
                if (!(branches[i] is JObject branch) || EditorToolSchema.ToolName(branch) != "material_apply") continue;
                var specialized = (JObject)branch.DeepClone();
                if (!(specialized["properties"]?["target"] is JObject target)) continue;
                target["const"] = goal.Target;
                branches[i] = specialized; // Never mutate the caller's earlier branch or introduce a disallowed tool.
            }
        }

        internal static void ValidateTarget(AgentTaskState task, EditorPlan plan)
        {
            var goal = Captured(task); if (goal == null) return;
            var rewrittenAliases = new HashSet<string>(StringComparer.Ordinal);
            foreach (var command in plan.actions)
            {
                // Existing-light edits register names at execution too; common virtual preflight only registers creations.
                if (command.tool == "set_lighting" && !string.IsNullOrEmpty(command.target) && !string.IsNullOrEmpty(command.name))
                { rewrittenAliases.Add(command.name); rewrittenAliases.Add("@" + command.name); }
                if (command.tool != "material_apply") continue;
                if (rewrittenAliases.Contains(command.target))
                    throw new ArgumentException("Предыдущая команда затеняет цель material_apply временным именем. Уберите конфликтующее имя; исходная сохранённая цель: " + goal.Target);
                GameObject captured, actual;
                try { captured = SceneToolExecutor.ResolveTarget(goal.Target); actual = SceneToolExecutor.ResolveTarget(command.target); }
                catch (Exception error) when (error is ArgumentException || error is InvalidOperationException)
                { throw new ArgumentException("material_apply должен адресовать исходный выделенный объект. Точная сохранённая цель: " + goal.Target + ". Она и цель команды должны существовать в активной сцене.", error); }
                if (!captured || !actual || actual != captured)
                    throw new ArgumentException("material_apply не должен менять другой объект или новое выделение. Используйте исходную сохранённую цель: " + goal.Target);
            }
        }

        internal static AgentCheckResult Verify(AgentRequirement requirement, IEnumerable<EditorCommandResult> steps)
        {
            GameObject target = null;
            try { target = SceneToolExecutor.ResolveTarget(requirement.Target); }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            var observations = new List<object>(); bool passed = false;
            if (target)
            {
                foreach (var step in steps.Where(s => s.Success && s.Tool == "material_apply"))
                {
                    MaterialApplyResult result;
                    try { result = JsonConvert.DeserializeObject<MaterialApplyResult>(step.Summary ?? ""); }
                    catch (JsonException) { continue; }
                    if (result == null) continue;
                    bool matches = false;
                    if (requirement.Expected == "terrain")
                    {
                        var terrain = target.GetComponent<Terrain>();
                        var createdLayers = (result.CreatedAssets ?? new List<string>()).Where(p => p != null && p.EndsWith("/Applied.terrainlayer", StringComparison.Ordinal)).ToArray();
                        matches = result.AppliedTerrainLayers > 0 && terrain && terrain.terrainData && createdLayers.Any(path => {
                            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                            return layer && terrain.terrainData.terrainLayers.Any(actual => actual == layer);
                        });
                    }
                    else
                    {
                        var renderer = target.GetComponent<Renderer>();
                        var material = string.IsNullOrEmpty(result.MaterialPath) ? null : AssetDatabase.LoadAssetAtPath<Material>(result.MaterialPath);
                        matches = result.AppliedRenderers > 0 && renderer && material && renderer.sharedMaterials.Length > 0 && renderer.sharedMaterials.All(actual => actual == material);
                    }
                    // Each real application creates a unique asset. Read the captured object, never a now-changed "selected" alias.
                    observations.Add(new { step.Target, result.MaterialId, result.MaterialPath, result.AppliedRenderers, result.AppliedTerrainLayers, matches });
                    passed |= matches;
                }
            }
            string actual = !target ? "Исходный объект отсутствует или больше не разрешается в активной сцене" : passed ? "Успешный material_apply подтверждён живыми ссылками исходной цели" :
                "Нет применения к исходной цели. Выполни material_apply с уже импортированным материалом; поиск или повторный импорт не заменяет применение.";
            return new AgentCheckResult { RequirementId = requirement.Id, Description = requirement.Description, Passed = passed, Actual = actual,
                Evidence = "Captured target=" + requirement.Target + "; current-selection-independent material/terrain-layer reference read-back: " + JsonConvert.SerializeObject(observations) };
        }
    }
}
