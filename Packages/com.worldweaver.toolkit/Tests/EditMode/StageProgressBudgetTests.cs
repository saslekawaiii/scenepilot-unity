using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class StageProgressBudgetTests
    {
        static object Call(string method,params object[] args)=>typeof(ChatController).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
        static AssistantMessage InspectedStages()
        {
            var answer=new AssistantMessage{Task=new AgentTaskState{Approach=new List<string>{"Inspect actual state","Capture actual output","Implement the missing result","Verify actual behavior","Save when authorized"},ActiveApproachStage=2}};
            for(int i=0;i<8;i++)answer.Steps.Add(new EditorCommandResult{Tool="inspect_component",Success=true,Summary="Observed input "+i});
            answer.EditorReviews.Add(new EditorResultReview{ApproachStage=0,StepCount=6,Passed=true});
            answer.EditorReviews.Add(new EditorResultReview{ApproachStage=1,StepCount=8,Passed=true});
            return answer;
        }
        [Test] public void HostAcceptedDiagnosticStagesStartFreshBoundedImplementationPhaseAcrossReload()
        {
            var answer=InspectedStages();answer.Steps.Add(new EditorCommandResult{Tool="execute_editor_code",Success=true,Summary="{\"sceneChanged\":false,\"projectChanged\":false}"});
            Assert.AreEqual(8,Call("ProgressBoundary",answer));Assert.AreEqual(1,Call("ObservationsSinceProgress",answer));
            var restored=JsonConvert.DeserializeObject<AssistantMessage>(JsonConvert.SerializeObject(answer));
            Assert.AreEqual(8,Call("ProgressBoundary",restored));Assert.AreEqual(1,Call("ObservationsSinceProgress",restored));
        }
        [Test] public void PlansNewVerificationIdsAndFailedOrUnacceptedReviewsDoNotGrantNewBudget()
        {
            var answer=InspectedStages();
            foreach(string tool in new[]{"plan_task","run_runtime_probe","job_result","read_task_step","inspect_component","capture_scene"})answer.Steps.Add(new EditorCommandResult{Tool=tool,Success=true,Summary="{}"});
            answer.EditorReviews.Add(new EditorResultReview{ApproachStage=2,StepCount=14,Passed=false});
            answer.EditorReviews.Add(new EditorResultReview{ApproachStage=2,StepCount=14,Passed=true}); // Host has not advanced past this stage.
            answer.EditorReviews.Add(new EditorResultReview{ApproachStage=4,StepCount=14,Passed=true});
            answer.EditorReviews.Add(new EditorResultReview{ApproachStage=-1,StepCount=14,Passed=true});
            Assert.AreEqual(8,Call("ProgressBoundary",answer));Assert.AreEqual(6,Call("ObservationsSinceProgress",answer),"Failed/unaccepted reviews, plans and new job IDs cannot avoid the persistent stall threshold");
        }
        [Test] public void SameImplementationPhaseAccumulatesReadsDespiteRepeatedFailedReviews()
        {
            var answer=InspectedStages();
            for(int i=0;i<13;i++) { answer.Steps.Add(new EditorCommandResult{Tool="inspect_component",Success=true,Summary="Additional observation "+i});answer.EditorReviews.Add(new EditorResultReview{ApproachStage=2,StepCount=answer.Steps.Count,Passed=false}); }
            Assert.AreEqual(13,Call("ObservationsSinceProgress",answer));
            var restored=JsonConvert.DeserializeObject<AssistantMessage>(JsonConvert.SerializeObject(answer));Assert.AreEqual(13,Call("ObservationsSinceProgress",restored));
        }
        [Test] public void ActualImplementationAfterAnAcceptedStageIsTheNewerProgressBoundary()
        {
            var answer=InspectedStages();answer.Steps.Add(new EditorCommandResult{Tool="set_transform",Success=true});
            Assert.AreEqual(9,Call("ProgressBoundary",answer));Assert.AreEqual(0,Call("ObservationsSinceProgress",answer));
        }
        [Test] public void CompilerFailureAndSourceWindowRemainInFreshContextAsHistoricalDataAfterRepair()
        {
            var excerpts=new JObject{["sourceHash"]="actual-generated-source",["windows"]=new JArray(new JObject{["generatedDiagnosticLine"]=37,["lines"]=new JArray(new JObject{["line"]=37,["text"]="int value = source / ?? 0;",["isDiagnosticLine"]=true})}),["truncated"]=false};
            var answer=new AssistantMessage();answer.Steps.Add(new EditorCommandResult{Tool="execute_editor_code",Success=false,Error="Line 31: Invalid expression term",Summary=new JObject{["compiled"]=false,["sourceHash"]="actual-generated-source",["diagnostics"]="Line 31: Invalid expression term",["diagnosticSourceExcerpts"]=excerpts}.ToString()});
            answer.Steps.Add(new EditorCommandResult{Tool="batch",Success=false,Summary="Batch rolled back"});
            var ledger=(JArray)Call("ObservationLedger",answer.Steps);var failed=ledger.OfType<JObject>().Single(r=>(string)r["tool"]=="execute_editor_code");
            Assert.IsTrue(JToken.DeepEquals(excerpts,failed["data"]["diagnosticSourceExcerpts"]));Assert.AreEqual("actual-generated-source",(string)failed["data"]["sourceHash"]);Assert.That((string)failed["failureScope"],Does.Contain("not a claim that project scripts are currently broken"));
            answer.Steps.Add(new EditorCommandResult{Tool="set_transform",Success=true});ledger=(JArray)Call("ObservationLedger",answer.Steps);failed=ledger.OfType<JObject>().Single(r=>(string)r["tool"]=="execute_editor_code");Assert.IsTrue((bool)failed["historicalBeforeLastChange"]);
        }
        [Test] public void FailedCompilerSourceSurvivesTheRealFreshConstructiveRequest()
        {
            string folder=Path.Combine(Path.GetTempPath(),"ScenePilot-stage-budget-"+Guid.NewGuid().ToString("N"));
            try
            {
                using(var controller=new ChatController(historyFolder:folder,modelConnectionsFolder:Path.Combine(folder,"models")))
                {
                    controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=false;controller.Settings.Mode=AssistantMode.Ask;
                    var excerpts=new JObject{["sourceHash"]="actual-failed-source",["windows"]=new JArray(new JObject{["lines"]=new JArray(new JObject{["line"]=37,["text"]="int value = source / ?? 0;"})}),["truncated"]=false};
                    var answer=new AssistantMessage();
                    answer.Steps.Add(new EditorCommandResult{Tool="execute_editor_code",Success=false,Error=new string('\"',5000),Summary=new JObject{["compiled"]=false,["diagnostics"]=new string('\"',12000),["sourceHash"]="actual-failed-source",["diagnosticSourceExcerpts"]=excerpts}.ToString()});
                    answer.Steps.Add(new EditorCommandResult{Tool="batch",Success=false,Summary="Batch did not complete"});
                    answer.Attempts.Add(new EditorPlanAttempt{RepairKind="native-fresh-plan",RawResponse="PRIVATE_PROVIDER_AUDIT"});
                    var request=(CompletionRequest)typeof(ChatController).GetMethod("BuildConstructiveRequest",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,new object[]{controller.Current,"Explain the known observations without changing the scene.",answer,AssistantMode.Ask});
                    const string marker="LATEST KNOWN HOST OBSERVATIONS";
                    string content=request.Messages.Single(m=>m.Content.StartsWith(marker,StringComparison.Ordinal)).Content;
                    var ledger=JArray.Parse(content.Substring(content.IndexOf(": [",StringComparison.Ordinal)+2));
                    var failed=ledger.OfType<JObject>().Single(r=>(string)r["tool"]=="execute_editor_code");
                    Assert.IsTrue(JToken.DeepEquals(excerpts,failed["data"]["diagnosticSourceExcerpts"]),"Bound the diagnostics while preserving the complete bounded source window as structured data");
                    Assert.AreEqual("actual-failed-source",(string)failed["data"]["sourceHash"]);
                    Assert.That(string.Join("\n",request.Messages.Select(m=>m.Content)),Does.Not.Contain("PRIVATE_PROVIDER_AUDIT"));
                }
            }
            finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
        }
    }
}
