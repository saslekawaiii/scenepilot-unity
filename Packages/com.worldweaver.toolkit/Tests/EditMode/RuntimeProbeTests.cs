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
            Assert.AreEqual(15,(int)function["parameters"]["properties"]["seconds"]["maximum"]);
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
        [Test] public void RuntimeObservationProjectionKeepsMeasurementsAndOmitsExecutableSource()
        {
            var raw=new JObject{["Kind"]="runtime_probe",["Success"]=false,["ProbeCode"]="private source body",["ProbeSourceHash"]="actual-hash",["ProbeAssertions"]=new JArray(new JObject{["passed"]=false,["observed"]=3})};
            var method=typeof(ChatController).GetMethod("JobObservationForModel",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            var projected=(JObject)method.Invoke(null,new object[]{raw});Assert.IsNull(projected["ProbeCode"]);Assert.IsNotNull(raw["ProbeCode"]);Assert.AreEqual("actual-hash",(string)projected["ProbeSourceHash"]);Assert.IsFalse((bool)projected["ProbeAssertions"][0]["passed"]);
        }
    }
}
