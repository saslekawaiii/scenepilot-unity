using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using WorldWeaver.Editor.Assistant;
namespace WorldWeaver.Tests
{
    public sealed class RuntimeProbeTests
    {
        [Test] public void RuntimeProbeAdvertisesBoundedIteratorAndActualAssertionSemantics()
        {
            var definitions=AgentFunctionProtocol.Definitions(EditorPlan.Schema(true,nativeFunctions:true));
            var function=definitions.Single(d=>(string)d["function"]["name"]=="run_runtime_probe")["function"];
            string description=(string)function["description"];
            Assert.That(description,Does.Contain("Play Mode"));Assert.That(description,Does.Contain("Check"));Assert.That(description,Does.Contain("yield"));
            var oversized=new EditorCommand{tool="run_runtime_probe",args=new JObject{["code"]=new string('x',16001)}};
            Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(new EditorPlan{actions=new System.Collections.Generic.List<EditorCommand>{oversized}}),"Provider-specific wire relaxation must not weaken host code bounds");
            Assert.AreEqual(120,(int)function["parameters"]["properties"]["seconds"]["maximum"]);
        }
        [TestCase(30f)][TestCase(120f)]
        public void LongObservationBudgetSurvivesDurableJobAndCanBeCancelledBeforePlay(float seconds)
        {
            var command=new EditorCommand{tool="run_runtime_probe",args=new JObject{["code"]="while(true)yield return null;",["seconds"]=seconds}};
            SceneToolExecutor.Validate(new EditorPlan{actions=new System.Collections.Generic.List<EditorCommand>{command}});
            var job=AgentEditorJobs.RunRuntimeProbe((string)command.args["code"],seconds);
            Assert.AreEqual(seconds,AgentEditorJobs.Load(job.Id).RequestedSeconds);
            AgentEditorJobs.Cancel(job.Id);var cancelled=AgentEditorJobs.Load(job.Id);
            Assert.IsFalse(cancelled.Success);Assert.That(cancelled.Error,Does.Contain("cancelled"));Assert.IsFalse(AgentEditorJobs.Busy);Assert.IsFalse(EditorApplication.isPlayingOrWillChangePlaymode);
        }
        [TestCase(.24f)][TestCase(120.01f)][TestCase(float.NaN)][TestCase(float.PositiveInfinity)]
        public void OutOfBoundObservationBudgetNeverCreatesAJobOrEntersPlay(float seconds)
        {
            Assert.IsFalse(AgentEditorJobs.Busy);
            Assert.Throws<ArgumentException>(()=>AgentEditorJobs.RunRuntimeProbe("yield return null;",seconds));
            Assert.IsFalse(AgentEditorJobs.Busy);Assert.IsFalse(EditorApplication.isPlayingOrWillChangePlaymode);
        }
        [Test] public void InvalidProbeCodeCannotEnterPlayModeOrCreateAnActiveJob()
        {
            bool before=AgentEditorJobs.Busy;
            Assert.IsFalse(before,"Fixture needs an idle Editor");
            var error=Assert.Throws<ArgumentException>(()=>AgentEditorJobs.RunRuntimeProbe("this is not a valid iterator C# body ???",1));
            Assert.That(error.Message,Does.Contain("compilation failed"));Assert.IsFalse(EditorApplication.isPlayingOrWillChangePlaymode);Assert.IsFalse(AgentEditorJobs.Busy);
        }
        [Test] public void ProbeRequiresTheSameExplicitCodePermissionAsEditorSnippets()
        {
            var command=new EditorCommand{tool="run_runtime_probe",args=new JObject{["code"]="probe.Check(true,\"fixture\");yield return null;"}};
            var result=SceneToolExecutor.Execute(new EditorPlan{actions=new System.Collections.Generic.List<EditorCommand>{command}});
            Assert.IsFalse(result.Success);Assert.That(result.Error,Does.Contain("permission"));Assert.IsFalse(EditorApplication.isPlayingOrWillChangePlaymode);Assert.IsFalse(AgentEditorJobs.Busy);
        }
        [Test] public void LeadingUsingDirectivesCompileAndCancellationPreventsTheQueuedPlayTransition()
        {
            var job=AgentEditorJobs.RunRuntimeProbe("using System.Linq; using Counts = System.Collections.Generic.List<int>; var values=new Counts{1,2,3};probe.Check(values.Count()==3,\"actual count\",values.Count);yield return null;",1);
            AgentEditorJobs.Cancel(job.Id);var cancelled=AgentEditorJobs.Load(job.Id);
            Assert.IsFalse(cancelled.Success);Assert.That(cancelled.Error,Does.Contain("cancelled"));Assert.IsFalse(AgentEditorJobs.Busy);Assert.IsFalse(EditorApplication.isPlayingOrWillChangePlaymode);
        }
        [TestCase("var all=Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);float r=Random.Range(0f,1f);probe.Check(all!=null&&r>=0,\"Unity aliases\");yield return null;")]
        [TestCase("using Object=System.Object;var value=new Object();probe.Check(value!=null,\"Explicit alias\");yield return null;")]
        public void UnityObjectAliasesCompileAndExplicitAliasesRemainAuthoritative(string code)
        {
            var job=AgentEditorJobs.RunRuntimeProbe(code,1);AgentEditorJobs.Cancel(job.Id);Assert.IsFalse(AgentEditorJobs.Busy);Assert.IsFalse(EditorApplication.isPlayingOrWillChangePlaymode);
        }
        [Test] public void RuntimeObservationProjectionKeepsMeasurementsAndOmitsExecutableSource()
        {
            var raw=new JObject{["Kind"]="runtime_probe",["Success"]=false,["ProbeCode"]="private source body",["ProbeSourceHash"]="actual-hash",["ProbeAssertions"]=new JArray(new JObject{["passed"]=false,["observed"]=3})};
            var method=typeof(ChatController).GetMethod("JobObservationForModel",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            var projected=(JObject)method.Invoke(null,new object[]{raw});Assert.IsNull(projected["ProbeCode"]);Assert.IsNotNull(raw["ProbeCode"]);Assert.AreEqual("actual-hash",(string)projected["ProbeSourceHash"]);Assert.IsFalse((bool)projected["ProbeAssertions"][0]["passed"]);
        }
        [Test] public void ActualUnityValueObservationsAndNestedAssertionsSerializeWithoutFollowingComputedProperties()
        {
            using(var probe=new RuntimeProbeContext())
            {
                var position=new UnityEngine.Vector3(2,3,4);
                probe.Record("pose",new{position,rotation=UnityEngine.Quaternion.identity,color=UnityEngine.Color.red,bounds=new UnityEngine.Bounds(position,UnityEngine.Vector3.one),path=new[]{position,UnityEngine.Vector3.zero}});
                var observed=probe.Measurements["pose"];
                CollectionAssert.AreEqual(new[]{2f,3f,4f},observed["position"].Values<float>());CollectionAssert.AreEqual(new[]{0f,0f,0f,1f},observed["rotation"].Values<float>());
                CollectionAssert.AreEqual(new[]{1f,0f,0f,1f},observed["color"].Values<float>());Assert.AreEqual(2,observed["path"].Count());Assert.AreEqual(3,(float)observed["bounds"]["center"][1]);
                probe.Check(position.y==3,"measured height",position);Assert.IsTrue((bool)probe.Assertions[0]["passed"]);Assert.AreEqual(3,(float)probe.Assertions[0]["observed"][1]);
                Assert.That(observed.ToString(),Does.Not.Contain("normalized"));
            }
        }
        [Test] public void UnityObjectObservationIsAnIdentityReadbackRatherThanARecursiveSceneSerialization()
        {
            var actor=new UnityEngine.GameObject("ProbeObservedActor");
            try
            {
                using(var probe=new RuntimeProbeContext())
                {
                    probe.Record("actor",new{actor=actor.transform});var observed=probe.Measurements["actor"]["actor"];
                    Assert.AreEqual(actor.name,(string)observed["name"]);Assert.AreEqual("UnityEngine.Transform",(string)observed["type"]);Assert.IsNull(observed["position"]);Assert.IsNull(observed["gameObject"]);
                    Assert.Less(observed.ToString().Length,1000);
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(actor);}
        }
        [Test] public void SuccessfulActualPlayCanConfirmUnchangedProjectCompilationButNeverALaterSourceEdit()
        {
            var method=typeof(ChatController).GetMethod("SuccessfulPlayCompilationEvidence",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public);
            var steps=new System.Collections.Generic.List<EditorCommandResult>();
            var job=new JObject{["Id"]="play-job",["Kind"]="runtime_probe",["Status"]="completed",["Success"]=true,["Passed"]=2,["Samples"]=10};
            var result=new EditorCommandResult{Tool="job_result",Target="play-job",Success=true,Summary=job.ToString()};steps.Add(result);
            Assert.AreSame(result,method.Invoke(null,new object[]{steps}));
            steps.Add(new EditorCommandResult{Tool="read_script",Success=true});Assert.AreSame(result,method.Invoke(null,new object[]{steps}));
            steps.Add(new EditorCommandResult{Tool="draft_script",Success=true,ChangesApplied=true});Assert.IsNull(method.Invoke(null,new object[]{steps}));
            steps.Add(result);Assert.AreSame(result,method.Invoke(null,new object[]{steps}));
            steps.Clear();job["Status"]="running";result.Summary=job.ToString();steps.Add(result);Assert.IsNull(method.Invoke(null,new object[]{steps}));
            job["Status"]="completed";job["Kind"]="tests";result.Summary=job.ToString();Assert.IsNull(method.Invoke(null,new object[]{steps}),"EditMode tests do not prove a Play transition");
            job["Kind"]="runtime_probe";job["Samples"]=0;result.Summary=job.ToString();Assert.IsNull(method.Invoke(null,new object[]{steps}),"No actual frame measured");
        }
        [Test] public void FailedGenericProbeRemainsVisibleAndNeedsFreshMeasurementAfterSourceRepair()
        {
            var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
            var ledger=typeof(ChatController).GetMethod("ObservationLedger",flags);var rechecks=typeof(ChatController).GetMethod("RequiredRuntimeRechecks",flags);
            var failed=new JObject{["Id"]="measured-job",["Kind"]="runtime_probe",["Status"]="failed",["Success"]=false,["ProbeSourceHash"]=new string('a',64),["ProbeCode"]="fixture-only code",["RequestedSeconds"]=8,["ProbeAssertions"]=new JArray(new JObject{["name"]="actual condition",["passed"]=false,["observed"]=1})};
            var steps=new System.Collections.Generic.List<EditorCommandResult>{new EditorCommandResult{Tool="job_result",Success=false,Summary=failed.ToString()}};
            var observations=(JArray)ledger.Invoke(null,new object[]{steps});Assert.AreEqual(1,observations.Count);Assert.IsFalse((bool)observations[0]["data"]["Success"]);Assert.IsNull(observations[0]["data"]["ProbeCode"]);
            Assert.IsEmpty((JArray)rechecks.Invoke(null,new object[]{steps}));steps.Add(new EditorCommandResult{Tool="draft_script",Success=true});steps.Add(new EditorCommandResult{Tool="compile_scripts",Success=true});
            var gaps=(JArray)rechecks.Invoke(null,new object[]{steps});Assert.AreEqual(1,gaps.Count);Assert.AreEqual("run_runtime_probe",(string)gaps[0]["tool"]);Assert.AreEqual(0,(int)gaps[0]["recoverSourceWith"]["args"]["index"]);
            steps.Add(new EditorCommandResult{Tool="run_runtime_probe",Success=true});var fresh=(JObject)failed.DeepClone();fresh["Success"]=true;fresh["Status"]="completed";steps.Add(new EditorCommandResult{Tool="job_result",Success=true,Summary=fresh.ToString()});
            Assert.IsEmpty((JArray)rechecks.Invoke(null,new object[]{steps}));Assert.AreEqual(1,((JArray)ledger.Invoke(null,new object[]{steps})).Count);
        }
        [Test] public void ReadingAndResubmittingGeneratedSourceDoesNotMultiplyServiceComments()
        {
            var source=typeof(ScriptDrafts).GetMethod("SourceForImport",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            const string legacy="// Generated with WorldWeaver. User-reviewed C# source.";
            const string body="// Keep this user comment\nusing UnityEngine;public class Echo : MonoBehaviour {public string Value=\"unchanged\";}";
            string once=(string)source.Invoke(null,new object[]{body});
            Assert.AreEqual(once,(string)source.Invoke(null,new object[]{once+"\n\n"}));Assert.AreEqual(once,(string)source.Invoke(null,new object[]{legacy+"\r\n"+legacy+"\n"+body+"\n"}));
            Assert.That(once.Split('\n')[0],Does.Not.Contain("User-reviewed"));Assert.That(once,Does.EndWith(body+"\n"));
        }
    }
}
