using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WorldWeaver.Editor.Assistant;
namespace WorldWeaver.Tests
{
 public sealed class DurableObservationTests
 {
  static object Call(string name,params object[] args)=>typeof(ChatController).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
  static EditorCommandResult Page(int index,int offset=0)=>new EditorCommandResult{Tool="read_task_step",Success=true,Summary=new JObject{["index"]=index,["offset"]=offset,["summary"]="measured evidence"}.ToString()};
  static EditorCommand Read(int index,int offset=0)=>new EditorCommand{tool="read_task_step",args=new JObject{["index"]=index,["offset"]=offset}};
  [Test] public void PersistedPageReplayIsDetectedAcrossReloadButNewPageRemainsAvailable()
  {
   var restored=JsonConvert.DeserializeObject<List<EditorCommandResult>>(JsonConvert.SerializeObject(new[]{Page(3)}));
   Assert.AreEqual(0,Call("RepeatedEvidencePage",restored,Read(3)));Assert.AreEqual(-1,Call("RepeatedEvidencePage",restored,Read(3,18000)));Assert.AreEqual(-1,Call("RepeatedEvidencePage",restored,Read(4)));
  }
  [Test] public void EvictedEvidenceCanBeReadAgainAndFailedRuntimeFactsHavePriority()
  {
   var steps=new List<EditorCommandResult>{new EditorCommandResult{Tool="job_result",Success=false,Summary="{\"Kind\":\"runtime_probe\",\"ProbeAssertions\":[{\"name\":\"actual failed contact\",\"passed\":false}]}"}};
   for(int i=1;i<25;i++)steps.Add(Page(i));
   Assert.AreEqual(-1,Call("RepeatedEvidencePage",steps,Read(1)),"An evicted page must remain retrievable");
   var ledger=(JArray)Call("ObservationLedger",steps);Assert.AreEqual("job_result",(string)ledger[0]["tool"]);Assert.That(ledger.ToString(),Does.Contain("actual failed contact"));
  }
  [Test] public void ReadOnlyEditorMeasurementsAndRecoveredPagesRemainInDurableContext()
  {
   var steps=new List<EditorCommandResult>{new EditorCommandResult{Tool="execute_editor_code",Success=true,Summary="{\"sceneChanged\":false,\"projectChanged\":false,\"commandFingerprint\":\"actual-shape-read\",\"result\":\"measured-vertices\"}"},Page(0)};
   var data=(JArray)Call("ObservationLedger",steps);Assert.AreEqual(2,data.Count);Assert.That(data.ToString(),Does.Contain("measured-vertices"));Assert.That(data.ToString(),Does.Contain("measured evidence"));
   steps.Add(new EditorCommandResult{Tool="execute_editor_code",Success=true,Summary="{\"sceneChanged\":true,\"projectChanged\":false}"});data=(JArray)Call("ObservationLedger",steps);Assert.IsTrue(data.OfType<JObject>().All(r=>(bool)r["historicalBeforeLastChange"]));
  }
  [Test] public void ObservationBudgetSurvivesSerializationAndIgnoresNewVerificationJobIds()
  {
   var steps=new List<EditorCommandResult>{new EditorCommandResult{Tool="create_node",Success=true},Page(0),new EditorCommandResult{Tool="run_runtime_probe",Success=true},new EditorCommandResult{Tool="job_result",Success=false,Summary="{\"Kind\":\"runtime_probe\",\"Status\":\"failed\"}"}};
   var restored=JsonConvert.DeserializeObject<List<EditorCommandResult>>(JsonConvert.SerializeObject(steps));Assert.AreEqual(3,Call("ObservationsSinceImplementation",restored));Assert.IsTrue((bool)Call("HasUnresolvedBehaviorFailure",restored));
   restored.Add(new EditorCommandResult{Tool="job_result",Success=true,Summary="{\"Kind\":\"runtime_probe\",\"Status\":\"completed\"}"});Assert.IsFalse((bool)Call("HasUnresolvedBehaviorFailure",restored));restored.Add(new EditorCommandResult{Tool="set_transform",Success=true});Assert.AreEqual(0,Call("ObservationsSinceImplementation",restored));
  }
  [Test] public void RecoveryDoesNotHideTargetedDiagnosticsOrCapture()
  {
   using(var controller=new ChatController(historyFolder:"UserSettings/RecoverySchema-"+System.Guid.NewGuid().ToString("N")))
   {
    controller.Settings.Mode=AssistantMode.Execute;controller.Settings.UseNativeFunctions=true;controller.Settings.AllowAutonomousScripts=true;
    typeof(ChatController).GetField("requireImplementationStep",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,true);
    var request=new CompletionRequest{Schema=EditorPlan.Schema(true,nativeFunctions:true)};string before=request.Schema.ToString();
    var result=(CompletionRequest)typeof(ChatController).GetMethod("RequireImplementation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,new object[]{request});
    Assert.AreEqual(before,result.Schema.ToString(),"Recovery should guide the model without removing the unmet capture or diagnostic tool");Assert.That(result.Schema.ToString(),Does.Contain("capture_scene"));Assert.That(result.Schema.ToString(),Does.Contain("inspect_component"));
   }
  }
  [Test] public void TimingReferenceWarnsAgainstTreatingFrameCountAsPhysicalTime()
  {
   var description=AgentFunctionProtocol.Definitions(EditorPlan.Schema(true,nativeFunctions:true)).Single(d=>(string)d["function"]["name"]=="run_runtime_probe")["function"]["description"].ToString();Assert.That(description,Does.Contain("Frame counts are NOT elapsed physical time"));Assert.That(description,Does.Contain("Time.timeAsDouble"));
  }
 }
}
