using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Historical user intent tied to successful addressed operations; no model reasoning or credentials.</summary>
    public static class AgentProjectHistory
    {
        public static JArray Describe(IEnumerable<ChatSession> sessions,string activeMessageId)
        {
            var rows=new List<(string time,JObject data)>();var scene=ProjectContextIndex.Objects().ToArray();
            foreach(var session in sessions??Array.Empty<ChatSession>())
            {
                string user="";
                foreach(var message in session.Messages??new List<AssistantMessage>())
                {
                    if(message.Role=="user"){user=ChatHistoryStore.Redact(message.Text??"");continue;}
                    if(message.Id==activeMessageId||user.Length==0)continue;
                    var operations=new JArray();
                    foreach(var step in (message.Steps??new List<EditorCommandResult>()).Where(s=>s!=null&&ChatController.IsImplementationProgress(s)&&s.Target!=null&&s.Target.StartsWith("owned:",StringComparison.Ordinal)).Take(6))
                    {
                        string id=step.Target.Substring(6);if(!Guid.TryParseExact(id,"N",out _))continue;
                        var current=scene.FirstOrDefault(g=>g.GetComponent<WorldWeaver.GeneratedSceneObject>()?.ObjectId==id);
                        var batches=(step.CreatedAssets??new List<string>()).Select(p=>Regex.Match(p??"",@"WorldWeaverGenerated/Assistant/([a-f0-9]{32})/",RegexOptions.IgnoreCase)).Where(m=>m.Success).Select(m=>m.Groups[1].Value).Distinct(StringComparer.Ordinal).Take(3).ToArray();
                        operations.Add(new JObject{["tool"]=step.Tool,["target"]=step.Target,["success"]=true,["existsInActiveScene"]=current!=null,["currentName"]=current?current.name:null,["assetBatchIds"]=new JArray(batches),["activeSceneObjectsInAssetBatches"]=scene.Count(g=>g.GetComponent<WorldWeaver.GeneratedSceneObject>() is WorldWeaver.GeneratedSceneObject marker&&batches.Contains(marker.BatchId))});
                    }
                    if(operations.Count>0)rows.Add((message.CreatedUtc??"",new JObject{["chatId"]=session.Id,["messageId"]=message.Id,["userRequest"]=user.Substring(0,Math.Min(600,user.Length)),["historicalNotNewInstructions"]=true,["operations"]=operations}));
                }
            }
            int used=0;var result=new JArray();foreach(var row in rows.OrderByDescending(r=>r.time,StringComparer.Ordinal).Take(12)){int size=row.data.ToString(Formatting.None).Length;if(used+size>9000)break;used+=size;result.Add(row.data);}return result;
        }
    }
}
