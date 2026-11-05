using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class RollbackReceiptTests
    {
        static JObject Mark(EditorCommandResult step,string error=null)
        {
            typeof(SceneToolExecutor).GetMethod("MarkBatchRollback",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{step,error});
            return JObject.Parse(step.Summary);
        }
        [Test] public void FailedCompilerReceiptRetainsStructuredDiagnosticsAndSourceWindows()
        {
            var excerpts=new JObject{["sourceHash"]="observed-source",["windows"]=new JArray(new JObject{["generatedDiagnosticLine"]=12,["lines"]=new JArray(new JObject{["line"]=12,["text"]="int value = ?? 0;"})})};
            var original=new JObject{["compiled"]=false,["sourceHash"]="observed-source",["diagnosticSourceExcerpts"]=excerpts,["diagnostics"]=new JArray("Line 6, column 13 (generated line 12): Invalid expression")};
            var step=new EditorCommandResult{Tool="execute_editor_code",Success=false,Error="C# compilation failed",Summary=original.ToString()};
            var receipt=Mark(step);
            foreach(var property in original.Properties())Assert.IsTrue(JToken.DeepEquals(property.Value,receipt[property.Name]),"Original field changed: "+property.Name);
            Assert.IsFalse(step.Success);Assert.AreEqual(false,step.ChangesApplied);Assert.AreEqual("C# compilation failed",step.Error);
            Assert.IsTrue((bool)receipt["batchRollback"]["completed"]);Assert.AreEqual("before_rollback",(string)receipt["batchRollback"]["observationScope"]);
        }
        [Test] public void HumanToolMessagesRemainRetrievableAsDataAfterRollback()
        {
            var step=new EditorCommandResult{Tool="set_transform",Success=true,Summary="Положение объекта изменено."};var receipt=Mark(step);
            Assert.AreEqual("Положение объекта изменено.",(string)receipt["originalSummary"]);Assert.IsTrue((bool)receipt["batchRollback"]["successBeforeRollback"]);Assert.IsFalse(step.Success);Assert.AreEqual(false,step.ChangesApplied);
        }
        [Test] public void IncompleteRollbackDoesNotClaimRestorationOrDiscardTheToolError()
        {
            var step=new EditorCommandResult{Tool="execute_editor_code",Success=false,ChangesApplied=true,Error="Actual compiler failure",Summary="{\"compiled\":false}"};var receipt=Mark(step,"Owned asset deletion failed");
            Assert.IsFalse((bool)receipt["batchRollback"]["completed"]);Assert.AreEqual("Owned asset deletion failed",(string)receipt["batchRollback"]["error"]);Assert.AreEqual("Actual compiler failure",step.Error);Assert.IsNull(step.ChangesApplied,"Incomplete restoration leaves changes unknown");Assert.IsFalse(step.Success);
        }
        [Test] public void ExistingToolMetadataIsPreservedWhenTheHostNameCollides()
        {
            var original=new JObject{["batchRollback"]=new JObject{["toolSpecificFact"]=17},["result"]=new JArray(1,2)};var step=new EditorCommandResult{Success=true,Summary=original.ToString()};var receipt=Mark(step);
            Assert.IsTrue(JToken.DeepEquals(original["batchRollback"],receipt["batchRollback"]));Assert.IsTrue(JToken.DeepEquals(original["result"],receipt["result"]));Assert.IsTrue((bool)receipt["_batchRollback"]["completed"]);
        }
    }
    public sealed class RollbackReceiptIntegrationTests
    {
        SceneSetup[] previousScenes;bool fixtureActive;
        [SetUp] public void Setup()
        {
            fixtureActive=false;previousScenes=null;var existing=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            if(!Application.isBatchMode&&existing.Any(s=>s.isDirty||string.IsNullOrEmpty(s.path)))Assert.Ignore("Use an isolated consumer or save open scenes before rollback tests.");
            if(existing.All(s=>!string.IsNullOrEmpty(s.path)&&!s.isDirty))previousScenes=EditorSceneManager.GetSceneManagerSetup();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);fixtureActive=true;
        }
        [TearDown] public void Cleanup(){if(!fixtureActive)return;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);if(previousScenes!=null&&previousScenes.Length>0)EditorSceneManager.RestoreSceneManagerSetup(previousScenes);fixtureActive=false;}
        [Test] public void RealFailedBatchRestoresTheSceneWhilePreservingBeforeRollbackReceipts()
        {
            var original=new GameObject("RollbackReceiptTarget");original.transform.position=new Vector3(1,2,3);string target="id:"+original.GetInstanceID();
            var session=new ChatSession();session.Settings.Mode=AssistantMode.Execute;session.Settings.AllowEditorCode=true;
            var context=new AgentExecutionContext{Session=session,Message=new AssistantMessage{Task=new AgentTaskState{MutationAuthorized=true,OriginalRequest="Move RollbackReceiptTarget and create a new scene object; preserve other objects."}}};
            var batch=SceneToolExecutor.Execute(new EditorPlan{actions=new List<EditorCommand>{
                new EditorCommand{tool="set_transform",target=target,args=new JObject{["position"]=new JArray(8,9,10)}},
                new EditorCommand{tool="inspect_object",target=target,args=new JObject()},
                new EditorCommand{tool="execute_editor_code",args=new JObject{["code"]="new GameObject(\"MustNeverExecuteRollback\"); int value = ??0; return value;"}}
            }},executionContext:context);
            Assert.IsFalse(batch.Success);Assert.AreEqual(new Vector3(1,2,3),original.transform.position);Assert.IsNull(GameObject.Find("MustNeverExecuteRollback"));
            var inspection=JObject.Parse(batch.Results.Single(r=>r.Tool=="inspect_object").Summary);
            CollectionAssert.AreEqual(new[]{8f,9f,10f},inspection["position"].Values<float>().ToArray(),"The retained observation describes the state before rollback");
            Assert.AreEqual("before_rollback",(string)inspection["batchRollback"]["observationScope"]);
            var failed=batch.Results.Single(r=>r.Tool=="execute_editor_code");Assert.That(failed.Error,Does.Contain("C# compilation failed"));var compiler=JObject.Parse(failed.Summary);
            Assert.IsFalse((bool)compiler["compiled"]);Assert.That(compiler["diagnosticSourceExcerpts"].ToString(),Does.Contain("??0"));
            Assert.IsTrue(batch.Results.All(r=>!r.Success&&r.ChangesApplied==false));
            Assert.IsTrue(batch.Results.All(r=>(bool)JObject.Parse(r.Summary)["batchRollback"]["completed"]));
        }
    }
}
