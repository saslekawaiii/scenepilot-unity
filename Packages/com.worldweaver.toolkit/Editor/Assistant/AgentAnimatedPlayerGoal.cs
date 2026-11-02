using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Verify an imported character on this task's target, independently of completion prose.</summary>
    internal static class AgentAnimatedPlayerGoal
    {
        internal const string Kind = "animated_player";
        static bool Has(string text,string pattern) => Regex.IsMatch(text??"",pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        internal static bool Requested(string text)
        {
            if (!Has(text,@"главн\p{L}*\s+геро\p{L}*|протагонист\p{L}*|игрок\p{L}*|играбельн\p{L}*|управляем\p{L}*\s+персонаж\p{L}*|\b(?:player|playable|protagonist|main\s+character)\b")) return false;
            if (!Has(text,@"анимац|\banimat|риг\b|\brigged\b|реальн\p{L}*\s+модел|настоящ\p{L}*\s+модел|не\s+используй\s+примитив|без\s+примитив|\b(?:real\s+model|skinned|non-primitive)\b")) return false;
            return Has(text,@"созда[йт]|сдела[йт]|добав|сгенер|\b(?:create|make|add|generate)\b") &&
                !Has(text,@"(?<![\p{L}\p{N}_])не\s+(?:создавай|создавайте|создавать|созда[йт]|делай|делать|добавляй)|\b(?:never|don't|do\s+not)\s+(?:create|make|add|generate)\b");
        }
        internal static AgentRequirement Capture(string text,bool authorized) => authorized&&Requested(text)
            ? new AgentRequirement {Kind=Kind,Number=1,Expected="skinned_locomotion_player",Description="Управляемый герой использует импортированную модель с костями и подключённой анимацией передвижения; примитивы не заменяют модель. Тип анимации указывается явно, движение проверяется отдельно в Play Mode."} : null;
        static bool Active(AgentTaskState task) => task?.MutationAuthorized==true&&task.Requirements.Any(r=>r.Kind==Kind);
        internal static bool AdditionalNpcs(string text)=>Has(text,@"\b(?:NPC|НПС)\b|страж|торгов|жител|\b(?:guard|merchant|villager)\b|(?:ещ[её]|друг\p{L}*|нескол\p{L}*|два|двух|три|тр[её]х|\d+)\s+персонаж");
        internal static bool SimpleRequest(string text)=>Requested(text)&&!AdditionalNpcs(text)&&!Has(text,
            @"скрипт|\bкод\b|C#|компонент|механик|здоров|урон|интерфейс|\bUI\b|canvas|script|component|compile|мир\b|рельеф|ландшафт|террейн|террен|долин|terrain|world|valley|деревн|village|settlement|текстур|материал|texture|material|куб|сфер|дом\b|дерев|камень|камни|объект|cube|sphere|house|tree|rock|object|свет|освещ|ламп|light|камер|camera|сохран|save|удал|убери|delete|remove|квест|quest|ворот|gate");
        internal static void Bind(JObject schema,AgentTaskState task)
        {
            if (!Active(task)) return;
            var branches=schema?["properties"]?["actions"]?["items"]?["anyOf"] as JArray;
            if (branches==null) return;
            foreach(var branch in branches.OfType<JObject>().Where(b=>EditorToolSchema.ToolName(b)=="create_player"||EditorToolSchema.ToolName(b)=="create_npc"&&!AdditionalNpcs(task.OriginalRequest)).ToArray()) branch.Remove();
        }
        internal static void Validate(AgentTaskState task,EditorPlan plan)
        {
            if (Active(task)&&plan.actions.Any(a=>a.tool=="create_player"||a.tool=="create_npc"&&!AdditionalNpcs(task.OriginalRequest)))
                throw new ArgumentException("Для настоящего анимированного главного героя используйте create_animated_player. create_player/create_npc создают примитивы и не выполняют это требование.");
        }
        internal static AgentCheckResult Verify(AgentRequirement goal,IEnumerable<EditorCommandResult> steps)
        {
            var observations=new List<JObject>();bool passed=false;
            foreach(var step in steps.Where(s=>s.Success&&(s.Tool==AnimatedCharacterTools.ToolName||s.Tool=="replace_model")&&!string.IsNullOrEmpty(s.Target)))
            {
                try
                {
                    var target=SceneToolExecutor.ResolveTarget(step.Target);
                    var observation=AnimatedCharacterTools.Inspect(target);
                    var holder=target.GetComponent<WorldModelVisual>();var visual=holder?holder.Visual:null;
                    var animator=visual?visual.GetComponent<Animator>():null;var gait=visual?visual.GetComponent<WorldHumanoidLocomotion>():null;
                    var player=target.GetComponent<WorldPlayer>();var motor=target.GetComponent<CharacterController>();
                    bool humanoid=animator&&animator.avatar&&animator.avatar.isValid&&animator.avatar.isHuman;
                    bool locomotion=humanoid&&((gait&&gait.enabled&&gait.Actor==target.transform&&gait.Animator==animator)||
                        (animator.runtimeAnimatorController&&animator.runtimeAnimatorController.animationClips.Any(c=>c&&c.isHumanMotion)&&animator.parameters.Any(p=>p.name=="Speed"&&p.type==AnimatorControllerParameterType.Float)));
                    bool skinned=visual&&visual.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.sharedMesh&&AssetDatabase.Contains(r.sharedMesh)&&r.sharedMesh.vertexCount>100&&r.bones.Length>=4&&r.bones.All(b=>b));
                    bool importedActor=holder&&player&&player.enabled&&player.View&&motor&&motor.enabled&&skinned&&locomotion;
                    observation["otherImportedHumanoid"]=new JObject{["success"]=importedActor,["sourcePath"]=holder?holder.SourcePath:null,["humanoid"]=humanoid,["movementBound"]=locomotion,["animationType"]=gait?"procedural, not authored clips":"Humanoid controller"};
                    observation["target"]=step.Target;observations.Add(observation);
                    passed|=(bool?)observation["success"]==true||importedActor;
                }
                catch(ArgumentException){}
                catch(InvalidOperationException){}
            }
            return new AgentCheckResult {RequirementId=goal.Id,Description=goal.Description,Passed=passed,
                Actual=passed?"Модель, кости, привязка анимации и компоненты игрока подтверждены в сцене":"Нет подтверждённого анимированного героя текущей задачи. Проверьте импортированную модель, риг, привязку анимации и управление; пустой ответ или примитив не выполняют запрос.",
                Evidence="Live scene target read-back (structural checks, not a gameplay test): "+JsonConvert.SerializeObject(observations)};
        }
    }
}
