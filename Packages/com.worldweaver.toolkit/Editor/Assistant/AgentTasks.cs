using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    [Serializable] public sealed class ChatAttachment
    {
        public string Path, Name, Sha256, Kind = "image";
    }
    [Serializable] public sealed class DesignNote
    {
        public string Key, Value, SourceMessageId, Category;
        public string UpdatedUtc = DateTime.UtcNow.ToString("o");
    }
    [Serializable] public sealed class AgentRequirement
    {
        public string Id = Guid.NewGuid().ToString("N"), Kind, Description, Target, Expected, Origin = "user_literal";
        public double Number;
        public bool Relative;
        public int Baseline;
        public List<string> BaselineTargets = new List<string>();
    }
    [Serializable] public sealed class AgentCheckResult
    {
        public string RequirementId, Description, Actual, Evidence;
        public bool Passed;
    }
    [Serializable] public sealed class AgentTaskState
    {
        public string Id = Guid.NewGuid().ToString("N"), OriginalRequest, Status = "planned", ScopeTarget, CheckpointId, PendingJobId;
        public int ResumeAttempts;
        public string CreatedUtc = DateTime.UtcNow.ToString("o");
        public bool MutationAuthorized, ContinueAfterReload, CompilationRequired, CheckpointSealed;
        public List<string> ReviewedCodeScope = new List<string>();
        public List<AgentRequirement> Requirements = new List<AgentRequirement>();
        public List<AgentCheckResult> Checks = new List<AgentCheckResult>();
        public List<string> Evidence = new List<string>();
        public List<string> OpenQuestions = new List<string>();
    }
    /// <summary>Observable literal constraints are checked by code, independently of model completion prose.</summary>
    public static class AgentTaskVerifier
    {
        static readonly Dictionary<string,int> Words = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase) {
            ["один"]=1,["одна"]=1,["одно"]=1,["два"]=2,["две"]=2,["три"]=3,["четыре"]=4,["пять"]=5,["шесть"]=6,["семь"]=7,["восемь"]=8,["девять"]=9,["десять"]=10,
            ["one"]=1,["two"]=2,["three"]=3,["four"]=4,["five"]=5,["six"]=6,["seven"]=7,["eight"]=8,["nine"]=9,["ten"]=10
        };
        static string Kind(GameObject go)
        {
            if (go.GetComponent<IntelligentNPC>()) return "npc";
            var light=go.GetComponent<Light>();if(light) return light.type==LightType.Point?"point_light":"light";
            if (go.GetComponent<GeneratedTerrain>()) return "terrain";
            return go.GetComponent<GeneratedSceneObject>()?.Kind ?? "";
        }
        public static AgentTaskState Create(string input, bool mutationAuthorized)
        {
            var task = new AgentTaskState { OriginalRequest=input,MutationAuthorized=mutationAuthorized };
            input=input??"";
            var ambiguousKinds=new HashSet<string>();
            var number = @"(?:\d{1,6}|один|одна|одно|два|две|три|четыре|пять|шесть|семь|восемь|девять|десять|one|two|three|four|five|six|seven|eight|nine|ten)";
            foreach (Match match in Regex.Matches(input, @"(?i)(?<![\w\d])(?<n>"+number+@")(?![\w\d])\s+(?:(?:новых|новые|новый|новое|new|orange|оранжевых|оранжевые|точечных|точечные)\s+){0,4}(?<kind>дерев(?:о|а|ья|ьев|ьям|ьями|ьях)\b|дом(?:ов|а|ом|ами|ах)?\b|камен\w*|камн\w*|персонаж\w*|NPC\b|lights?\b|Point\s*Light\b|источник\w*\s+света|trees?\b|houses?\b|rocks?\b)",RegexOptions.CultureInvariant))
            {
                string token=match.Groups["n"].Value; int count=Words.TryGetValue(token,out int named)?named:int.Parse(token);
                string word=match.Groups["kind"].Value.ToLowerInvariant();
                string kind=word.StartsWith("дерев")||word.StartsWith("tree")?"tree":word.StartsWith("дом")||word.StartsWith("house")?"house":word.StartsWith("кам")||word.StartsWith("rock")?"rock":word.StartsWith("персонаж")||word=="npc"?"npc":word.StartsWith("point")||Regex.IsMatch(match.Value,@"(?i)точечн")?"point_light":"light";
                char[] punctuation={'.','!','?',';',',','\n'};int from=match.Index==0?0:input.LastIndexOfAny(punctuation,match.Index-1)+1;int to=input.IndexOfAny(punctuation,match.Index+match.Length);if(to<0)to=input.Length;string clause=input.Substring(from,to-from);
                bool relative=Regex.IsMatch(clause,@"(?i)\b(?:добав\w*|созда\w*|сгенер\w*|add|create|generate|новы\w*|new)\b")&&!Regex.IsMatch(clause,@"(?i)\b(?:всего|total)\b");
                bool preserve=!relative&&Regex.IsMatch(clause,@"(?i)(остав|сохран|keep|preserve)");
                if(!preserve&&(ambiguousKinds.Contains(kind)||task.Requirements.Any(r=>r.Kind=="count"&&r.Expected==kind)))
                {
                    if(ambiguousKinds.Add(kind)){task.Requirements.RemoveAll(r=>r.Kind=="count"&&r.Expected==kind);task.OpenQuestions.Add("Несколько количеств "+kind+" с разными условиями размещения: автоматическая проверка не определяет их области.");}continue;
                }
                var baseline=ProjectContextIndex.Objects().Where(go=>MatchesKind(go,kind)).ToArray();
                task.Requirements.Add(new AgentRequirement{Kind=preserve?"preserve":"count",Expected=kind,Number=count,Relative=relative,Baseline=baseline.Length,BaselineTargets=baseline.Select(ProjectContextIndex.StableTarget).ToList(),Description=(preserve?"Сохранены исходные ":relative?"Добавлено ":"Количество ")+kind+": "+count});
            }
            var size=Regex.Match(input,@"(?i)(?<x>\d{2,4})\s*(?:на|[xх×])\s*(?<z>\d{2,4})\s*(?:метр\w*|м\b|meters?\b)?");
            bool worldContext=Regex.IsMatch(input,@"(?i)(terrain|террейн|террен|ландшафт|рельеф|\bмир(?:а|у|ом|е|ы|ов|ами|ах)?\b|world\b|участок|земл|долин|valley|деревн|village)");
            if(size.Success&&worldContext)
            {
                task.Requirements.Add(new AgentRequirement{Kind="terrain_width",Number=int.Parse(size.Groups["x"].Value),Description="Ширина Terrain соответствует запросу"});
                task.Requirements.Add(new AgentRequirement{Kind="terrain_length",Number=int.Parse(size.Groups["z"].Value),Description="Длина Terrain соответствует запросу"});
            }
            const string worldNoun=@"мир(?:а|у|ом|е|ы|ов|ами|ах)?|рельеф\w*|ландшафт\w*|террейн\w*|террен\w*|terrain|world|долин\w*|valley|деревн\w*|village";
            var height=Regex.Match(input,@"(?i)(?:height|высот\w*)\s*(?:(?:существующ\w*|текущ\w*|этого|этой|existing|current|this)\s+){0,2}(?:(?<terrain>"+worldNoun+@")\s+)?(?:до|=|:|to|of)?\s*(?<h>\d{1,3}(?:[.,]\d+)?)");
            var subjects=Regex.Matches(input.Substring(0,height.Success?height.Index:0),@"(?i)\b(?:(?<terrain>"+worldNoun+@")|дерев(?:о|а|ья|ьев)|trees?|NPC|персонаж\w*|игрок\w*|player|дом(?:а|ов)?|houses?|камн\w*|камен\w*|rocks?|кноп\w*|buttons?|икон\w*|icons?)\b");
            bool heightTargetsTerrain=height.Groups["terrain"].Success||(subjects.Count>0&&subjects[subjects.Count-1].Groups["terrain"].Success);
            if(height.Success&&worldContext&&heightTargetsTerrain&&double.TryParse(height.Groups["h"].Value.Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double h))
                task.Requirements.Add(new AgentRequirement{Kind="terrain_height",Number=h,Description="Высота Terrain соответствует запросу"});
            task.CompilationRequired=Regex.IsMatch(input,@"(?i)(?:C#|скрипт|код\b|script\b)")&&Regex.IsMatch(input,@"(?i)(напиш|созда|измен|исправ|доработ|write|create|change|fix|patch)");
            var rotation=Regex.Match(input,@"(?i)(?<speed>-?\d{1,3}(?:[.,]\d+)?)\s*(?:градус\w*|degrees?|°)\s*(?:в\s*секунд\w*|/\s*(?:с|s)|per\s*second)");
            if(rotation.Success&&Regex.IsMatch(input,@"(?i)(вращ|rotat)")&&double.TryParse(rotation.Groups["speed"].Value.Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double speed))
            {
                var named=ProjectContextIndex.Objects().Where(go=>go.name.Length>=3&&Regex.IsMatch(input,@"(?<![\p{L}\p{N}_])"+Regex.Escape(go.name)+@"(?![\p{L}\p{N}_])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)).Take(2).ToArray();
                task.Requirements.Add(new AgentRequirement{Kind="rotation_speed",Number=speed,Target=named.Length==1?ProjectContextIndex.StableTarget(named[0]):null,Description=named.Length==1?"Скорость вращения "+named[0].name+" подтверждена в Play Mode":"Скорость измеряемого объекта подтверждена в Play Mode"});
            }
            if(Regex.IsMatch(input,@"(?i)(играбельн|управляем\w*\s+(?:игрок|персонаж)|WASD|playable|player\s+controller)"))task.Requirements.Add(new AgentRequirement{Kind="player",Number=1,Description="В сцене есть управляемый игрок"});
            if(Regex.IsMatch(input,@"(?i)(квест|достав\w*\s+письм|delivery\s+quest)"))task.Requirements.Add(new AgentRequirement{Kind="quest",Number=1,Description="Квест доставки имеет двух участников и награду"});
            if(Regex.IsMatch(input,@"(?i)(проходим|достижим|NavMesh|connected\s+village|pathfinding)"))task.Requirements.Add(new AgentRequirement{Kind="navigation",Number=1,Description="Связи именованных мест имеют полные пути NavMesh"});
            return task;
        }
        static bool MatchesKind(GameObject go,string expected)=>expected=="light"?go.GetComponent<Light>()!=null:Kind(go)==expected;
        public static IReadOnlyList<AgentCheckResult> Verify(AgentTaskState task)
        {
            if(task==null)return Array.Empty<AgentCheckResult>();
            GameObject scope=null;
            if(!string.IsNullOrEmpty(task.ScopeTarget))try{scope=SceneToolExecutor.ResolveTarget(task.ScopeTarget);}catch(ArgumentException){}
            bool missingScope=!string.IsNullOrEmpty(task.ScopeTarget)&&!scope;
            var objects=(scope?scope.GetComponentsInChildren<Transform>(true).Select(t=>t.gameObject):ProjectContextIndex.Objects()).ToArray();
            task.Checks.Clear();
            foreach(var requirement in task.Requirements)
            {
                if(requirement.Kind=="rotation_speed")continue; // Actual Play sampling is combined with job evidence by the controller.
                double actual=double.NaN;string evidence="live Unity scene";
                if(missingScope){task.Checks.Add(new AgentCheckResult{RequirementId=requirement.Id,Description=requirement.Description,Actual="Мир задания отсутствует",Evidence="Не разрешён ScopeTarget; чужие объекты не учитываются",Passed=false});continue;}
                if(requirement.Kind=="count")
                {
                    var matching=objects.Where(go=>MatchesKind(go,requirement.Expected));
                    actual=requirement.Relative&&requirement.BaselineTargets!=null?matching.Count(go=>!requirement.BaselineTargets.Contains(ProjectContextIndex.StableTarget(go))):matching.Count()-(requirement.Relative?requirement.Baseline:0);
                }
                else if(requirement.Kind=="preserve")
                {
                    var ids=ProjectContextIndex.Objects().Where(go=>MatchesKind(go,requirement.Expected)).Select(ProjectContextIndex.StableTarget).ToArray();
                    actual=requirement.BaselineTargets!=null&&requirement.BaselineTargets.Count==(int)requirement.Number&&requirement.BaselineTargets.All(ids.Contains)?requirement.Number:double.NaN;evidence="Стабильные ID исходных объектов сохранены";
                }
                else if(requirement.Kind.StartsWith("terrain_",StringComparison.Ordinal))
                {
                    var terrain=objects.Select(go=>go.GetComponent<Terrain>()).FirstOrDefault(t=>t&&t.terrainData);
                    if(terrain){var size=terrain.terrainData.size;actual=requirement.Kind=="terrain_width"?size.x:requirement.Kind=="terrain_length"?size.z:size.y;evidence="TerrainData.size";}
                }
                else if(requirement.Kind=="player") {actual=objects.Any(go=>go.GetComponent<WorldPlayer>()&&go.GetComponent<CharacterController>())?1:0;evidence="WorldPlayer + CharacterController; наличие компонентов, управление проверяется отдельно в Play Mode";}
                else if(requirement.Kind=="quest") {actual=objects.Select(go=>go.GetComponent<DeliveryQuest>()).Any(q=>q&&q.Giver&&q.Recipient&&q.Giver!=q.Recipient&&q.RewardAmount>0)?1:0;evidence="DeliveryQuest references + reward; прохождение проверяется отдельно в Play Mode";}
                else if(requirement.Kind=="navigation")
                {
                    var navigation=objects.Select(go=>go.GetComponent<WorldNavigation>()).FirstOrDefault(n=>n);
                    var places=navigation?navigation.GetComponentsInChildren<WorldPlace>(true):Array.Empty<WorldPlace>();
                    actual=navigation&&places.Any(p=>p.Connections.Any(c=>c))&&navigation.ValidateConnections().Length==0?1:0;evidence="NavMesh.CalculatePath, PathComplete для разрешённых связей; закрытые ворота проверяются отдельно";
                }
                task.Checks.Add(new AgentCheckResult{RequirementId=requirement.Id,Description=requirement.Description,Actual=double.IsNaN(actual)?"Не найден объект для проверки":actual.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture),Passed=!double.IsNaN(actual)&&Math.Abs(actual-requirement.Number)<.02,Evidence=evidence});
            }
            foreach(string question in task.OpenQuestions)task.Checks.Add(new AgentCheckResult{RequirementId="ambiguous",Description=question,Actual="Нужна отдельная оценка",Evidence="Неоднозначное буквальное условие не объявляется подтверждённым",Passed=false});
            task.Status=task.Checks.Count==0?"actions_need_review":task.Checks.All(c=>c.Passed)?"literal_checks_passed":"checks_failed";
            return task.Checks;
        }
        public static string Context(AgentTaskState task)
        {
            if(task==null)return "";
            // Model-facing projection only. Stable baseline IDs remain in the host task for verification/restore.
            var requirements=task.Requirements??new List<AgentRequirement>();
            int evidenceBudget=1400;bool evidenceTruncated=(task.Evidence?.Count??0)>8;
            Func<string,int,string> bounded=(text,max)=>
            {
                text=text??"";int take=Math.Min(text.Length,Math.Min(max,Math.Max(0,evidenceBudget)));
                evidenceBudget-=take;if(take<text.Length)evidenceTruncated=true;return text.Substring(0,take)+(take<text.Length?"…":"");
            };
            var checks=(task.Checks??new List<AgentCheckResult>()).Select(check=>new
            {
                Requirement=requirements.FindIndex(r=>r.Id==check.RequirementId),check.Description,check.Passed,check.Actual,
                Evidence=bounded(check.Evidence,400)
            }).ToArray();
            return JsonConvert.SerializeObject(new
            {
                task.Status,task.OriginalRequest,task.ScopeTarget,task.MutationAuthorized,task.CompilationRequired,task.PendingJobId,
                Requirements=requirements.Select((r,index)=>new{Index=index,r.Kind,r.Description,r.Target,r.Expected,r.Number,r.Relative,r.Baseline,BaselineTargetCount=r.BaselineTargets?.Count??0}),
                Checks=checks,Evidence=(task.Evidence??new List<string>()).Skip(Math.Max(0,(task.Evidence?.Count??0)-8)).Select(e=>bounded(e,160)).ToArray(),
                task.OpenQuestions,
                EvidenceTruncated=evidenceTruncated
            });
        }
    }
    public static class AgentDesignMemory
    {
        public static void Remember(ChatSession session,string key,string value,string source,string category="decision")
        {
            if(string.IsNullOrWhiteSpace(key)||key.Length>120||value==null||value.Length>3000)throw new ArgumentException("Memory key1..120;value<=3000 characters.");
            if(session.DesignMemory==null)session.DesignMemory=new List<DesignNote>();
            var note=session.DesignMemory.FirstOrDefault(n=>n.Key==key);
            if(note==null){note=new DesignNote{Key=key};session.DesignMemory.Add(note);}
            note.Value=ChatHistoryStore.Redact(value);note.SourceMessageId=source;note.Category=category;note.UpdatedUtc=DateTime.UtcNow.ToString("o");
            while(session.DesignMemory.Count>128)session.DesignMemory.RemoveAt(0);
        }
        public static string Recall(ChatSession session,string query,int budget=3500)
        {
            var words=Regex.Split((query??"").ToLowerInvariant(),@"\W+").Where(w=>w.Length>2).ToArray();
            var rows=(session.DesignMemory??new List<DesignNote>()).Where(n=>n!=null)
                .OrderByDescending(n=>n.Category=="constraint").ThenByDescending(n=>words.Count(w=>((n.Key??"")+" "+(n.Value??"")).ToLowerInvariant().Contains(w)))
                .ThenByDescending(n=>n.UpdatedUtc,StringComparer.Ordinal);
            var result=new JArray();int used=0;
            foreach(var row in rows)
            {
                var entry=JObject.FromObject(new{row.Key,row.Value,row.Category,row.SourceMessageId});int size=entry.ToString(Formatting.None).Length;
                if(used+size+2>budget)continue;result.Add(entry);used+=size+1;
            }
            return result.ToString(Formatting.None);
        }
    }
}
