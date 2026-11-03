using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class WebAgentContractTests
    {
        [Test] public void NativeDefinitionsExplainActualToolPurposeRatherThanOnlyProtocolRules()
        {
            var functions=AgentFunctionProtocol.Definitions(EditorPlan.Schema(nativeFunctions:true));
            string Description(string name)=>(string)functions.Single(f=>(string)f["function"]["name"]==name)["function"]["description"];
            Assert.That(Description("run_player_check"),Does.Contain("Play Mode"));Assert.That(Description("run_player_check"),Does.Contain("arm"));Assert.That(Description("terrain_from_heightmap"),Does.Contain("scalar elevation"));Assert.That(Description("web_research"),Does.Contain("smolagents"));Assert.That(Description("replace_model"),Does.Contain("preserving"));
        }
        sealed class ReadBudgetProvider:ILLMProvider
        {
            public int Calls;
            public System.Threading.Tasks.Task<CompletionResult> CompleteAsync(CompletionRequest request,System.Threading.CancellationToken token)
            {
                Calls++;var plan=new EditorPlan{complete=false};
                if(Calls<=17)for(int i=0;i<8;i++)plan.actions.Add(new EditorCommand{tool="find_scene_objects",args=new JObject{["query"]="budget_observation_"+i}});
                else if(Calls==18)plan.actions.Add(new EditorCommand{tool="create_node",name="ReadBudgetGoal"});else{plan.complete=true;plan.reply="Готово";}
                return System.Threading.Tasks.Task.FromResult(new CompletionResult{Text=Newtonsoft.Json.JsonConvert.SerializeObject(plan)});
            }
            public System.Threading.Tasks.Task<bool> HealthAsync(System.Threading.CancellationToken token)=>System.Threading.Tasks.Task.FromResult(true);
            public void Dispose(){}
        }
        [Test] public async System.Threading.Tasks.Task NativeObservationsCannotExhaustTheBudgetNeededForTheActualChange()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);var provider=new ReadBudgetProvider();
            try
            {
                using(var controller=new ChatController(_=>provider,"UserSettings/ReadBudget-"+Guid.NewGuid().ToString("N")))
                {
                    controller.Settings.UseNativeFunctions=true;controller.Settings.Power=AssistantPower.Deep;controller.Settings.AutoVerify=false;controller.Settings.UseSkills=false;
                    await controller.SendAsync("Создай пустой узел ReadBudgetGoal.");Assert.IsTrue(UnityEngine.GameObject.Find("ReadBudgetGoal"));Assert.IsFalse(controller.Current.Messages.Last().IsError,controller.Current.Messages.Last().Text);Assert.GreaterOrEqual(controller.Current.Messages.Last().Steps.Count,137);
                }
            }
            finally{UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);}
        }
        [Test] public void ImportedArtifactPathSurvivesManyLaterReadsWithoutInventingFailedOutputs()
        {
            var steps=new System.Collections.Generic.List<EditorCommandResult>{new EditorCommandResult{Tool="model_import",Success=true,Summary="{\"model_path\":\"Assets/Imported/Observed.fbx\",\"cacheHit\":true}"}};
            for(int i=0;i<100;i++)steps.Add(new EditorCommandResult{Tool="inspect_object",Success=true,Summary="{}"});
            steps.Add(new EditorCommandResult{Tool="asset_download",Success=false,Summary="{\"path\":\"Assets/NotActuallyDownloaded.img\"}"});
            var method=typeof(ChatController).GetMethod("ArtifactLedger",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);var ledger=(JArray)method.Invoke(null,new object[]{steps});
            Assert.AreEqual(1,ledger.Count);Assert.AreEqual(0,(int)ledger[0]["index"]);Assert.AreEqual("Assets/Imported/Observed.fbx",(string)ledger[0]["observedOutput"]["model_path"]);
        }
        [Test] public void RuntimeNpcRoutesDoNotInheritEditorReasoningBudget()
        {
            var source=UnityEngine.ScriptableObject.CreateInstance<ModelProfile>();source.Model="route-fixture-"+Guid.NewGuid().ToString("N");source.EnableCloudReasoning=true;var created=new System.Collections.Generic.List<string>();
            try
            {
                var method=typeof(ChatController).GetMethod("PersistentRoutes",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);var routes=(OrchestratorProfile)method.Invoke(null,new object[]{source,created});
                Assert.IsFalse(routes.Dialogue.EnableCloudReasoning);Assert.AreEqual(1024,routes.Dialogue.MaxTokens);Assert.IsTrue(source.EnableCloudReasoning,"The temporary editor profile itself remains unchanged");
            }
            finally{foreach(string path in created.AsEnumerable().Reverse())UnityEditor.AssetDatabase.DeleteAsset(path);UnityEngine.Object.DestroyImmediate(source);}
        }
        [Test] public void InstalledPackageSourceCanBeReadWithItsHashWhileTraversalStillFails()
        {
            const string path="Packages/com.worldweaver.toolkit/Runtime/World/WorldInput.cs";
            var command=new EditorCommand{tool="read_script",args=new JObject{["path"]=path}};var plan=new EditorPlan{actions=new System.Collections.Generic.List<EditorCommand>{command}};
            var batch=SceneToolExecutor.Execute(plan);Assert.IsTrue(batch.Success,batch.Error);var data=JObject.Parse(batch.Results[0].Summary);Assert.IsTrue((bool)data["readOnlyPackage"]);Assert.That((string)data["source"],Does.Contain("class WorldInput"));Assert.AreEqual(64,((string)data["sha256"]).Length);
            command.args["path"]="Packages/com.worldweaver.toolkit/../../Private.cs";Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(plan));
        }
        sealed class ObservationRecoveryProvider:ILLMProvider
        {
            public int Reads,Reviews;public bool FeedbackSeen,ImplementationPhaseSeen;
            public System.Threading.Tasks.Task<CompletionResult> CompleteAsync(CompletionRequest request,System.Threading.CancellationToken token)
            {
                if(request.Schema?["properties"]?["passed"]!=null)
                {
                    Reviews++;bool exists=UnityEngine.GameObject.Find("ObservedGoal");return System.Threading.Tasks.Task.FromResult(new CompletionResult{Text=new JObject{["passed"]=exists,["issues"]=exists?new JArray():new JArray("The requested node has not been created; observations alone are insufficient."),["evidence"]=exists?"Actual node exists":"Actual node is absent"}.ToString()});
                }
                var plan=new EditorPlan{complete=false};bool feedback=request.Messages.Any(m=>m.Content.Contains("LATEST INDEPENDENT REVIEW")&&m.Content.Contains("requested node"));FeedbackSeen|=feedback;
                if(feedback){var available=((JArray)request.Schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName).ToArray();ImplementationPhaseSeen=available.Contains("create_node")&&available.Contains("discover_tools")&&!available.Contains("inspect_object");}
                if(!feedback){Reads++;plan.actions.Add(new EditorCommand{tool="find_scene_objects",args=new JObject{["query"]="missing_observation_fixture"}});}
                else if(!UnityEngine.GameObject.Find("ObservedGoal"))plan.actions.Add(new EditorCommand{tool="create_node",name="ObservedGoal"});
                else{plan.complete=true;plan.reply="Готово";}
                return System.Threading.Tasks.Task.FromResult(new CompletionResult{Text=Newtonsoft.Json.JsonConvert.SerializeObject(plan)});
            }
            public System.Threading.Tasks.Task<bool> HealthAsync(System.Threading.CancellationToken token)=>System.Threading.Tasks.Task.FromResult(true);
            public void Dispose(){}
        }
        [Test] public async System.Threading.Tasks.Task ObservationStallGetsIndependentFeedbackAndCanContinueToARealMutation()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);var provider=new ObservationRecoveryProvider();string folder="UserSettings/ObservationRecovery-"+Guid.NewGuid().ToString("N");
            try
            {
                using(var controller=new ChatController(_=>provider,folder))
                {
                    controller.Settings.UseNativeFunctions=true;controller.Settings.AllowAutonomousScripts=true;controller.Settings.AutoVerify=true;controller.Settings.Power=AssistantPower.Deep;controller.Settings.UseSkills=false;
                    await controller.SendAsync("Создай пустой узел ObservedGoal.");Assert.IsTrue(provider.FeedbackSeen);Assert.IsTrue(provider.ImplementationPhaseSeen);Assert.AreEqual(6,provider.Reads);Assert.GreaterOrEqual(provider.Reviews,2);Assert.IsTrue(UnityEngine.GameObject.Find("ObservedGoal"));Assert.IsFalse(controller.Current.Messages.Last().IsError,controller.Current.Messages.Last().Text);
                }
            }
            finally{UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);}
        }
        [Test] public void MissingComponentObservationReturnsRealDescendantsWithoutAUnityFakeNullException()
        {
            var root=new UnityEngine.GameObject("Component root");root.AddComponent<GeneratedSceneObject>();var child=new UnityEngine.GameObject("Animated visual");child.transform.SetParent(root.transform);child.AddComponent<GeneratedSceneObject>();child.AddComponent<UnityEngine.Animator>();
            try
            {
                var plan=new EditorPlan{actions=new System.Collections.Generic.List<EditorCommand>{new EditorCommand{tool="inspect_component",target=ProjectContextIndex.StableTarget(root),args=new JObject{["type"]="UnityEngine.Animator"}}}};
                var batch=SceneToolExecutor.Execute(plan);Assert.IsTrue(batch.Success,batch.Error);var data=JObject.Parse(batch.Results[0].Summary);Assert.IsFalse((bool)data["exists"]);Assert.AreEqual(ProjectContextIndex.StableTarget(child),(string)data["descendantMatches"][0]["target"]);Assert.That(data["availableComponents"].Values<string>(),Does.Contain("UnityEngine.Transform"));
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public async System.Threading.Tasks.Task InvalidLocalReadDoesNotCancelAnIndependentSceneRead()
        {
            var root=new UnityEngine.GameObject("Local observation fixture");root.AddComponent<GeneratedSceneObject>();
            try
            {
                var plan=new EditorPlan{complete=false,actions=new System.Collections.Generic.List<EditorCommand>{new EditorCommand{tool="read_script",args=new JObject{["path"]="../forbidden.env"}},new EditorCommand{tool="inspect_object",target=ProjectContextIndex.StableTarget(root)}}};
                var batch=await SceneToolExecutor.ExecuteAsync(plan);Assert.IsFalse(batch.Success);Assert.AreEqual(2,batch.Results.Count);Assert.AreEqual("read_script",batch.Results[0].Tool);Assert.IsFalse(batch.Results[0].Success);Assert.IsTrue(batch.Results[1].Success);Assert.AreEqual(root.name,(string)JObject.Parse(batch.Results[1].Summary)["name"]);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public async System.Threading.Tasks.Task FailedWebReadDoesNotDiscardAnIndependentActualSceneObservation()
        {
            string previous=Environment.GetEnvironmentVariable("WORLDWEAVER_WEB_PYTHON");var root=new UnityEngine.GameObject("Independent observation fixture");root.AddComponent<GeneratedSceneObject>();
            try
            {
                Environment.SetEnvironmentVariable("WORLDWEAVER_WEB_PYTHON",System.IO.Path.GetFullPath("UserSettings/MissingWorker-"+Guid.NewGuid().ToString("N")));
                var plan=new EditorPlan{complete=false,actions=new System.Collections.Generic.List<EditorCommand>{new EditorCommand{tool="web_search",args=new JObject{["query"]="fixture never sent to network"}},new EditorCommand{tool="inspect_object",target=ProjectContextIndex.StableTarget(root)}}};
                var batch=await SceneToolExecutor.ExecuteAsync(plan);Assert.IsFalse(batch.Success);Assert.AreEqual(2,batch.Results.Count);Assert.AreEqual("web_search",batch.Results[0].Tool);Assert.IsFalse(batch.Results[0].Success);Assert.AreEqual("inspect_object",batch.Results[1].Tool);Assert.IsTrue(batch.Results[1].Success);Assert.AreEqual(root.name,(string)JObject.Parse(batch.Results[1].Summary)["name"]);
            }
            finally{Environment.SetEnvironmentVariable("WORLDWEAVER_WEB_PYTHON",previous);UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void NativeSchemaDeclaredArraysDecodeProviderWrappersWithoutChangingTextArguments()
        {
            var schema=EditorPlan.Schema(nativeFunctions:true);
            JObject Call(string tool,JObject args)=>new JObject{["tool_calls"]=new JArray(new JObject{["function"]=new JObject{["name"]=tool,["arguments"]=args}})};
            var original=new JObject{["names"]="[\"read_script\",\"set_component\"]"};
            var plan=EditorPlan.Parse(AgentFunctionProtocol.Normalize(Call("discover_tools",original),schema));
            Assert.AreEqual(JTokenType.Array,plan.actions[0].args["names"].Type);Assert.AreEqual("set_component",(string)plan.actions[0].args["names"][1]);Assert.AreEqual(JTokenType.String,original["names"].Type,"Raw wire data remains unchanged for audit");
            string code="// [\"this is source text, never parsed as data\"]\npublic class Probe:UnityEngine.MonoBehaviour{}";
            plan=EditorPlan.Parse(AgentFunctionProtocol.Normalize(Call("draft_script",new JObject{["name"]="Probe",["code"]=code}),schema));Assert.AreEqual(code,(string)plan.actions[0].args["code"]);
        }
        [Test] public void ReadBatchReportsTheExactUnavailableFunctionInsteadOfAnUnrelatedMutationError()
        {
            var schema=EditorPlan.Schema(allowedTools:new System.Collections.Generic.HashSet<string>{"inspect_object"},nativeFunctions:true);
            var calls=new JArray(new JObject{["function"]=new JObject{["name"]="inspect_object",["arguments"]=new JObject{["target"]="owned:original"}}},new JObject{["function"]=new JObject{["name"]="read_script",["arguments"]=new JObject{["path"]="Assets/Existing.cs"}}});
            var error=Assert.Throws<ProviderException>(()=>AgentFunctionProtocol.Normalize(new JObject{["tool_calls"]=calls},schema));Assert.That(error.Message,Does.Contain("not advertised in this turn: read_script"));Assert.That(error.Message,Does.Contain("No action executed"));
        }
        static bool NativeHistory(CompletionRequest request,AssistantMessage answer)=>(bool)typeof(ChatController).GetMethod("AddNativeToolHistory",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{request,answer});
        [Test] public void NativeHistoryBindsActualObservationToItsOriginalCallAndKeepsLegacyHistoryUninvented()
        {
            var answer=new AssistantMessage();answer.Attempts.Add(new EditorPlanAttempt{Round=1,Accepted=true,FirstResultIndex=0,ResultCount=1,WireResponse="{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"id\":\"call_actual\",\"function\":{\"name\":\"inspect_object\",\"arguments\":{\"target\":\"owned:original\"}}}]}"});
            answer.Steps.Add(new EditorCommandResult{Tool="inspect_object",Success=false,Error="Actual object missing",Summary="No object read"});
            var request=new CompletionRequest();Assert.IsTrue(NativeHistory(request,answer));Assert.AreEqual(2,request.Messages.Count);
            Assert.AreEqual("assistant",request.Messages[0].Role);Assert.AreEqual("call_actual",(string)request.Messages[0].ToolCalls[0]["id"]);
            Assert.AreEqual("owned:original",(string)request.Messages[0].ToolCalls[0]["function"]["arguments"]["target"]);
            Assert.AreEqual("tool",request.Messages[1].Role);Assert.AreEqual("call_actual",request.Messages[1].ToolCallId);Assert.IsFalse((bool)JObject.Parse(request.Messages[1].Content)["success"]);
            answer.Steps[0].Summary="{\"observedField\":false}";var structured=new CompletionRequest();Assert.IsTrue(NativeHistory(structured,answer));Assert.IsFalse((bool)JObject.Parse(structured.Messages[1].Content)["data"]["observedField"],"Structured observations must not be multiply escaped into a text prefix");
            answer.Attempts[0].FirstResultIndex=-1;Assert.IsFalse(NativeHistory(new CompletionRequest(),answer),"Legacy logs without result mapping must not fabricate a tool response");
        }
        [Test] public void RejectedNativeCallReceivesExplicitNonExecutionFeedbackInItsToolChannel()
        {
            var answer=new AssistantMessage();answer.Attempts.Add(new EditorPlanAttempt{Round=2,Accepted=false,Error="Missing name",WireResponse="{\"tool_calls\":[{\"id\":\"call_rejected\",\"function\":{\"name\":\"attach_script\",\"arguments\":{\"target\":\"owned:original\"}}}]}"});
            var request=new CompletionRequest();Assert.IsTrue(NativeHistory(request,answer));var result=JObject.Parse(request.Messages[1].Content);
            Assert.IsFalse((bool)result["success"]);Assert.IsFalse((bool)result["executed"]);Assert.That((string)result["error"],Does.Contain("Missing name"));Assert.AreEqual("call_rejected",request.Messages[1].ToolCallId);
        }
        [TestCase("true",true)]
        [TestCase("false",false)]
        public void SerializedBooleanProviderLiteralsAreNormalizedAgainstTheActualField(string literal,bool expected)
        {
            var root=new UnityEngine.GameObject("Boolean field fixture");root.AddComponent<GeneratedSceneObject>();var player=root.AddComponent<WorldPlayer>();player.AlwaysLook=!expected;
            try
            {
                var command=new EditorCommand{tool="set_component",target=ProjectContextIndex.StableTarget(root),args=new JObject{["type"]="WorldWeaver.WorldPlayer",["property"]="AlwaysLook",["value"]=literal}};
                var result=SceneToolExecutor.Execute(new EditorPlan{actions=new System.Collections.Generic.List<EditorCommand>{command}});
                Assert.IsTrue(result.Success,result.Error);Assert.AreEqual(expected,player.AlwaysLook);Assert.AreEqual(JTokenType.Boolean,command.args["value"].Type);
                command.args["value"]="yes";Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(new EditorPlan{actions=new System.Collections.Generic.List<EditorCommand>{command}}));Assert.AreEqual(expected,player.AlwaysLook);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void StructuralInventoryFindsMultipleRussianNamedSurfacesAfterAnEnglishMiss()
        {
            var first=UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Plane);first.name="Серая площадка";
            var second=UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);second.name="Основание";
            try
            {
                first.AddComponent<GeneratedSceneObject>();second.AddComponent<GeneratedSceneObject>();
                var rows=ProjectContextIndex.ListScene(new JObject{["component"]="Renderer",["limit"]=100});
                Assert.That(rows["objects"].Select(o=>(string)o["name"]),Does.Contain(first.name));
                Assert.That(rows["objects"].Select(o=>(string)o["name"]),Does.Contain(second.name));
                var miss=ProjectContextIndex.Search("missing_english_surface_2026");
                Assert.AreEqual(0,(int)miss["sceneMatches"]);Assert.NotNull(miss["activeSceneInventory"]);
                Assert.That((string)miss["queryHint"],Does.Contain("literal"));
            }
            finally{UnityEngine.Object.DestroyImmediate(first);UnityEngine.Object.DestroyImmediate(second);}
        }
        [TestCase("draft_script")]
        [TestCase("attach_script")]
        public void NativeScriptCallsRequireTheActualClassName(string tool)
        {
            var schema=EditorPlan.Schema(nativeFunctions:true);
            var function=AgentFunctionProtocol.Definitions(schema).Single(t=>(string)t["function"]["name"]==tool)["function"];
            Assert.That(function["parameters"]["required"].Values<string>(),Does.Contain("name"));
            Assert.AreEqual(1,(int)function["parameters"]["properties"]["name"]["minLength"]);
            var args=tool=="attach_script"?new JObject{["target"]="owned:example"}:new JObject{["code"]="public class Example:UnityEngine.MonoBehaviour{}"};
            var message=new JObject{["tool_calls"]=new JArray(new JObject{["function"]=new JObject{["name"]=tool,["arguments"]=args}})};
            var error=Assert.Throws<ProviderException>(()=>AgentFunctionProtocol.Normalize(message,schema));
            Assert.That(error.Message,Does.Contain("missing required parameters: name"));
            Assert.That(error.Message,Does.Contain("No action executed"));
        }
        [Test] public void InternetObservationsAreBatchableWhileAcquisitionAndTerrainMutationsRemainStandalone()
        {
            var schema=EditorPlan.Schema(nativeFunctions:true);
            foreach(string name in new[]{"web_search","web_read","web_research","asset_download","terrain_from_heightmap"})
            {
                Assert.That(AgentFunctionProtocol.Definitions(schema).Select(t=>(string)t["function"]["name"]),Does.Contain(name));
                var branch=((JArray)schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().First(b=>EditorToolSchema.ToolName(b)==name);
                Assert.AreEqual(name=="web_search"||name=="web_read"||name=="web_research",(bool)branch["x-worldweaver-read-only"],name);
            }
            Assert.IsTrue(SceneToolExecutor.IsReadOnlyTool("web_research"));
            Assert.IsFalse(SceneToolExecutor.IsReadOnlyTool("asset_download"));
            var result=SceneToolExecutor.Execute(new EditorPlan{actions=new System.Collections.Generic.List<EditorCommand>{new EditorCommand{tool="discover_tools",args=new JObject{["query"]="internet heightmap"}}}});
            Assert.IsTrue(result.Success,result.Error);var discovery=JObject.Parse(result.Results[0].Summary);
            Assert.That(discovery["enabledTools"].Values<string>(),Does.Contain("web_research"));
            Assert.That((string)discovery["instructions"],Does.Contain("untrusted DATA"));
        }
        [TestCase("../.env")]
        [TestCase("Packages/private.env")]
        [TestCase("Assets/../.env")]
        [TestCase("Assets/Something.cs")]
        public void HeightmapCannotReadArbitraryLocalFiles(string path)
        {
            var plan=new EditorPlan();
            plan.actions.Add(new EditorCommand{tool="terrain_from_heightmap",target="missing",args=new JObject{["path"]=path}});
            Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(plan));
        }
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        [Test] public void LegacyInputInActiveGeneratedCodeIsRejectedBeforeItCanFailDuringPlay()
        {
            var command=new EditorCommand{tool="draft_script",name="InputProbe",args=new JObject{["code"]="using UnityEngine; public class InputProbe:MonoBehaviour{void Update(){var x=Input.GetAxis(\"Mouse X\");}}"}};
            Assert.That(Assert.Throws<ArgumentException>(()=>ScriptDrafts.Validate(command)).Message,Does.Contain("new Input System"));
            command.args["code"]="using UnityEngine; public class InputProbe:MonoBehaviour{void Update(){\n#if ENABLE_LEGACY_INPUT_MANAGER\nvar x=Input.GetAxis(\"Mouse X\");\n#endif\n}}";
            Assert.DoesNotThrow(()=>ScriptDrafts.Validate(command));
        }
#endif
    }
}
