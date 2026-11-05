using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class RejectionFeedbackTests
    {
        static object Call(string name,params object[] args)=>typeof(ChatController).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
        static CompletionRequest Request(params string[] tools)=>new CompletionRequest{Schema=EditorPlan.Schema(true,new HashSet<string>(tools),nativeFunctions:true)};
        static string Apply(CompletionRequest request,bool nativeWire,bool readOnly=false,AssistantMessage answer=null,string error="The proposed operation was rejected.")
        {
            Call("AddRejectedStepFeedback",request,answer,error,"Preserve the requested work and correct the unmet behavior.",readOnly,nativeWire);
            return request.Messages.Last().Content;
        }
        static JObject Data(string content)
        {
            const string marker="\nHOST REJECTION DATA:\n";
            return JObject.Parse(content.Substring(content.IndexOf(marker,System.StringComparison.Ordinal)+marker.Length));
        }
        [Test] public void NativeSemanticRepairKeepsNativeWireAndOnlyCurrentlyAdvertisedFunctions()
        {
            var request=Request("reflect_api");string schema=request.Schema.ToString();string content=Apply(request,true);var data=Data(content);
            Assert.IsTrue(request.UseToolCalls);Assert.AreEqual(schema,request.Schema.ToString(),"Feedback must not widen the current recovery schema");
            Assert.That(content,Does.Contain("native function calls").And.Contain("FLAT parameters"));
            Assert.That(content,Does.Not.Contain("EditorPlan").And.Not.Contain("\"actions\"").And.Not.Contain("complete:true"));
            CollectionAssert.AreEquivalent(new[]{"reflect_api","finish_task"},data["advertisedTools"].Values<string>());
            Assert.AreEqual("native_function_calls",(string)data["activeWireContract"]);
        }
        [Test] public void TextRecoveryUsesEditorPlanWhenTheActiveWireActuallyHasNoFunctions()
        {
            var request=Request("reflect_api");request.UseToolCalls=true;string content=Apply(request,false);var data=Data(content);
            Assert.IsFalse(request.UseToolCalls);Assert.That(content,Does.Contain("EditorPlan JSON instance").And.Contain("\"actions\""));
            Assert.IsTrue(request.Messages.Any(m=>m.Role=="system"&&m.Content.Contains("Native function calls are disabled for this response")));
            Assert.AreEqual("EditorPlan_JSON",(string)data["activeWireContract"]);CollectionAssert.AreEqual(new[]{"reflect_api"},data["advertisedTools"].Values<string>());
        }
        [Test] public void NativeReadOnlyRepairFinishesWithNativeFunctionInsteadOfLegacyEmptyActions()
        {
            var request=Request("inspect_component");string content=Apply(request,true,true);
            Assert.That(content,Does.Contain("read-only request").And.Contain("finish_task"));
            Assert.That(content,Does.Not.Contain("actions:[]").And.Not.Contain("complete:true").And.Not.Contain("EditorPlan"));
        }
        [Test] public void TextReadOnlyRepairRetainsItsCorrectEmptyActionContract()
        {
            string content=Apply(Request("inspect_component"),false,true);
            Assert.That(content,Does.Contain("actions:[] and complete:true"));Assert.That(content,Does.Not.Contain("finish_task"));
        }
        [Test] public void RelevantActualReturnAndFailureAreIncludedWithoutProviderAuditInternals()
        {
            var answer=new AssistantMessage();
            answer.Steps.Add(new EditorCommandResult{Tool="execute_editor_code",Success=true,Summary="{\"compiled\":true,\"executed\":true,\"sceneChanged\":false,\"projectChanged\":false,\"result\":\"{\\\"observedCount\\\":41}\",\"sourceHash\":\"actual-source-hash\"}"});
            answer.Steps.Add(new EditorCommandResult{Tool="create_node",Success=true,Summary="Actual requested host created"});
            answer.Steps.Add(new EditorCommandResult{Tool="batch",Success=false,Error="No applicable overload accepts this parameter type.",Summary="No changes from the failed batch"});
            answer.Attempts.Add(new EditorPlanAttempt{RawResponse="PRIVATE_AUDIT_RESPONSE",WireResponse="PRIVATE_PROVIDER_INTERNALS"});
            string content=Apply(Request("reflect_api","execute_editor_code"),true,false,answer,"This exact snippet is already observed at durable task step 0.");
            var data=Data(content);var rows=(JArray)data["relevantActualObservations"];
            var source=rows.OfType<JObject>().Single(r=>(int)r["index"]==0);
            Assert.AreEqual("{\"observedCount\":41}",(string)source["observation"]["returnedData"]);
            Assert.IsTrue(rows.OfType<JObject>().Any(r=>(string)r["error"]=="No applicable overload accepts this parameter type."));
            Assert.That(content,Does.Not.Contain("PRIVATE_AUDIT_RESPONSE").And.Not.Contain("PRIVATE_PROVIDER_INTERNALS"));
            Assert.AreEqual(3,answer.Steps.Count);Assert.AreEqual("PRIVATE_AUDIT_RESPONSE",answer.Attempts[0].RawResponse,"Feedback must not alter the audit trail");
        }
        [Test] public void RealConstructiveRequestSwitchesFallbackWireWithoutChangingOtherHostOrToolData()
        {
            string folder=Path.Combine(Path.GetTempPath(),"ScenePilot-feedback-"+Guid.NewGuid().ToString("N"));
            try
            {
                using(var controller=new ChatController(historyFolder:folder,modelConnectionsFolder:Path.Combine(folder,"models")))
                {
                    controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=false;controller.Settings.Mode=AssistantMode.Ask;
                    var answer=new AssistantMessage();
                    var request=(CompletionRequest)typeof(ChatController).GetMethod("BuildConstructiveRequest",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,new object[]{controller.Current,"Explain the current scene without changing it.",answer,AssistantMode.Ask});
                    const string prefix="SCENEPILOT HOST RESPONSE CONTRACT: ";
                    request.Messages.Add(new ChatMessage("user",prefix+"literal user data, preserve exactly"));
                    request.Messages.Add(new ChatMessage("tool","actual tool data containing native function calls and EditorPlan words"));
                    var others=request.Messages.Where(m=>!(m.Role=="system"&&m.Content.StartsWith(prefix,StringComparison.Ordinal))).ToArray();
                    var contents=others.Select(m=>m.Content).ToArray();string schema=request.Schema.ToString();
                    Assert.IsTrue(request.UseToolCalls);Assert.AreEqual(1,request.Messages.Count(m=>m.Role=="system"&&m.Content.StartsWith(prefix,StringComparison.Ordinal)));
                    Call("ApplyActiveWireContract",request,false);
                    var owned=request.Messages.Where(m=>m.Role=="system"&&m.Content.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
                    Assert.AreEqual(1,owned.Length);Assert.That(owned[0].Content,Does.Contain("Reply ONLY an EditorPlan JSON instance").And.Not.Contain("do not print EditorPlan JSON"));Assert.IsFalse(request.UseToolCalls);
                    Assert.IsFalse(request.Messages.Any(m=>m.Role=="system"&&m.Content.Contains("Use native function calls with FLAT parameters from their current schemas")));
                    CollectionAssert.AreEqual(others,request.Messages.Where(m=>!(m.Role=="system"&&m.Content.StartsWith(prefix,StringComparison.Ordinal))).ToArray());
                    CollectionAssert.AreEqual(contents,others.Select(m=>m.Content).ToArray());Assert.AreEqual(schema,request.Schema.ToString());
                    Call("ApplyActiveWireContract",request,true);
                    Assert.IsTrue(request.UseToolCalls);Assert.That(request.Messages.Single(m=>m.Role=="system"&&m.Content.StartsWith(prefix,StringComparison.Ordinal)).Content,Does.Contain("Use native function calls").And.Not.Contain("Reply ONLY an EditorPlan JSON instance"));
                    Assert.AreEqual(1,request.Messages.Count(m=>m.Role=="system"&&m.Content.StartsWith(prefix,StringComparison.Ordinal)));
                    CollectionAssert.AreEqual(contents,others.Select(m=>m.Content).ToArray());Assert.AreEqual(schema,request.Schema.ToString());
                }
            }
            finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
        }
        [TestCase("draft_script")]
        [TestCase("material_import")]
        [TestCase("create_node")]
        public void NativeNarrowedPhaseGuidanceDoesNotReintroduceLegacyJsonFormat(string tool)
        {
            var request=Request(tool);string before=request.Schema.ToString();
            string guidance=(string)Call("NativePhaseRecoveryGuidance",request);
            Assert.That(guidance,Does.Contain(tool).And.Contain("FLAT parameters"));
            Assert.That(guidance,Does.Not.Contain("EditorPlan").And.Not.Contain("complete:false").And.Not.Contain("tool,name,target,args"));
            Assert.AreEqual(before,request.Schema.ToString());
        }
    }
}
