using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class AgentResearchBudgetTests
    {
        static EditorCommand Query(string text)=>new EditorCommand{tool="web_search",args=new JObject{["query"]=text}};
        static readonly Type Policy=typeof(ChatController).Assembly.GetType("WorldWeaver.Editor.Assistant.WebResearchTools",true);
        static string Fingerprint(EditorCommand command)=>(string)Policy.GetMethod("ResearchFingerprint",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{command});
        static void Check(AgentTaskState task,EditorCommand command,AssistantPower power)
        {
            try{Policy.GetMethod("ValidateResearchBudget",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{task,command,power});}
            catch(TargetInvocationException e){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        [Test] public void AThirdUnchangedFailedReadIsRefusedButOtherQueriesRemainAvailable()
        {
            var command=Query("primary dataset");string fingerprint=Fingerprint(command);
            var task=new AgentTaskState{ResearchAttempts=new List<AgentResearchAttempt>{new AgentResearchAttempt{Fingerprint=fingerprint},new AgentResearchAttempt{Fingerprint=fingerprint}}};
            Assert.Throws<ArgumentException>(()=>Check(task,command,AssistantPower.Deep));
            Assert.DoesNotThrow(()=>Check(task,Query("another primary archive"),AssistantPower.Deep));
            task.ResearchAttempts.Add(new AgentResearchAttempt{Fingerprint=fingerprint,Success=true});
            Assert.DoesNotThrow(()=>Check(task,command,AssistantPower.Deep));
        }
        [Test] public void ResearchLimitSurvivesSerializationAndDoesNotDisableIndependentImplementation()
        {
            var task=JsonConvert.DeserializeObject<AgentTaskState>(JsonConvert.SerializeObject(new AgentTaskState{ResearchElapsedMs=240000}));
            foreach(var tool in new[]{"web_search","web_read","web_research"})Assert.Throws<ArgumentException>(()=>Check(task,new EditorCommand{tool=tool,args=new JObject()},AssistantPower.Deep));
            foreach(var tool in new[]{"create_object","asset_download","terrain_from_heightmap"})Assert.DoesNotThrow(()=>Check(task,new EditorCommand{tool=tool,args=new JObject()},AssistantPower.Deep));
        }
    }
}
