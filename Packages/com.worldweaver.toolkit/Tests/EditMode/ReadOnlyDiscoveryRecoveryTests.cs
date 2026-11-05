using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    /// <summary>Synthetic durable receipts test host completion policy, not a live internet/model call.</summary>
    public sealed class ReadOnlyDiscoveryRecoveryTests
    {
        static EditorPlan Done()=>new EditorPlan{complete=true,reply="Explanation based on the later observed source."};
        static bool Recovered(AssistantMessage answer,EditorPlan terminal=null)=>(bool)typeof(ChatController).GetMethod("RecoveredReadOnlyDiscovery",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{answer,terminal??Done()});
        static AssistantMessage RecoveredHistory(string failedTool="web_search")
        {
            var answer=new AssistantMessage{Task=new AgentTaskState{Purpose="explanation",MutationAuthorized=false}};
            answer.Steps.Add(new EditorCommandResult{Tool=failedTool,Success=false,Error="Discovery route unavailable",Summary="Original failed observation"});
            answer.Steps.Add(new EditorCommandResult{Tool="read_script",Success=true,Summary="{\"source\":\"actual retained source data\"}"});
            answer.Attempts.Add(new EditorPlanAttempt{Accepted=true,FirstResultIndex=0,ResultCount=1});
            answer.Attempts.Add(new EditorPlanAttempt{Accepted=true,FirstResultIndex=1,ResultCount=1});
            return answer;
        }
        [TestCase("web_search")]
        [TestCase("search_project")]
        [TestCase("find_unity_recipes")]
        public void LaterCleanObservationRecoversDiscoveryWithoutChangingHistoricReceiptsAcrossReload(string tool)
        {
            var answer=RecoveredHistory(tool);string original=JsonConvert.SerializeObject(answer);
            Assert.IsTrue(Recovered(answer));Assert.AreEqual(original,JsonConvert.SerializeObject(answer));
            var restored=JsonConvert.DeserializeObject<AssistantMessage>(original);Assert.IsTrue(Recovered(restored));Assert.IsFalse(restored.Steps[0].Success);Assert.AreEqual("Discovery route unavailable",restored.Steps[0].Error);
        }
        [TestCase("web_read")]
        [TestCase("verify_scene")]
        [TestCase("job_result")]
        [TestCase("compile_scripts")]
        [TestCase("execute_editor_code")]
        [TestCase("set_transform")]
        public void RequiredReadBehaviorCompilationAndMutationFailuresCannotBeAbsolvedByAnotherRead(string tool)
        { Assert.IsFalse(Recovered(RecoveredHistory(tool))); }
        [Test] public void LatestFailedDiscoveryAndSuccessInsideTheSameFailedBatchRemainUnresolved()
        {
            var answer=RecoveredHistory();answer.Attempts.Clear();answer.Attempts.Add(new EditorPlanAttempt{Accepted=true,FirstResultIndex=0,ResultCount=2});Assert.IsFalse(Recovered(answer));
            answer=RecoveredHistory();answer.Steps.Add(new EditorCommandResult{Tool="web_search",Success=false,Error="Latest search failed"});Assert.IsFalse(Recovered(answer));
        }
        [Test] public void FailedExplicitCheckIncompleteOrEmptyAnswerAndImplementationPurposeRemainUnresolved()
        {
            var answer=RecoveredHistory();answer.Task.Checks.Add(new AgentCheckResult{Passed=false,Description="Explicit required check failed"});Assert.IsFalse(Recovered(answer));answer.Task.Checks.Clear();
            Assert.IsFalse(Recovered(answer,new EditorPlan{complete=false,reply="Still researching"}));Assert.IsFalse(Recovered(answer,new EditorPlan{complete=true,reply="  "}));
            answer.Task.Purpose="implementation";Assert.IsFalse(Recovered(answer));
        }
        [Test] public void MetadataAndPersistedPageReplayDoNotReplaceActualSourceObservations()
        {
            foreach(string tool in new[]{"discover_tools","list_skills","read_skill","read_task_step","job_result","poll_job","verify_scene"})
            {var answer=RecoveredHistory();answer.Steps[1].Tool=tool;Assert.IsFalse(Recovered(answer),tool);}
        }
    }
}
