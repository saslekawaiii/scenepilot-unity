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
                if(feedback){var available=((JArray)request.Schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName).ToArray();ImplementationPhaseSeen|=available.Contains("create_node")&&available.Contains("discover_tools")&&!available.Contains("inspect_object");}
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
                    await controller.SendAsync("Создай пустой узел ObservedGoal.");Assert.IsTrue(provider.FeedbackSeen,"All request builders must return independent review feedback to the implementer");Assert.IsTrue(provider.ImplementationPhaseSeen,"A stalled read-only phase must expose implementation and discovery");Assert.AreEqual(6,provider.Reads);Assert.GreaterOrEqual(provider.Reviews,2);Assert.IsTrue(UnityEngine.GameObject.Find("ObservedGoal"));Assert.IsFalse(controller.Current.Messages.Last().IsError,controller.Current.Messages.Last().Text);
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
        sealed class StageProvider:ILLMProvider
        {
            public int Calls,StageReviews,FullReviews;public bool InspectionFirst;
            public System.Threading.Tasks.Task<CompletionResult> CompleteAsync(CompletionRequest request,System.Threading.CancellationToken token)
            {
                if(request.Schema?["properties"]?["passed"]!=null)
                {
                    bool stage=request.Messages.Any(m=>m.Content.Contains("checkpoint for ONE model-planned"));if(stage)StageReviews++;else FullReviews++;
                    bool first=UnityEngine.GameObject.Find("StageOne"),second=UnityEngine.GameObject.Find("StageTwo");
                    bool passed=stage?request.Messages.Any(m=>m.Content.Contains("\"ApproachStage\":0"))?InspectionFirst?!UnityEngine.GameObject.Find("AbsentStageNode"):first:second:InspectionFirst?second:first&&second;
                    return System.Threading.Tasks.Task.FromResult(new CompletionResult{Text=new JObject{["passed"]=passed,["issues"]=passed?new JArray():new JArray("Actual required node missing"),["evidence"]="Actual scene nodes"}.ToString()});
                }
                Calls++;var plan=new EditorPlan{complete=false};
                if(Calls==1)plan.actions.Add(new EditorCommand{tool="plan_task",args=new JObject{["steps"]=new JArray(InspectionFirst?"Проверь наличие AbsentStageNode; отрицательный результат поиска является допустимым выводом исследования":"Создай пустой узел StageOne","Создай пустой узел StageTwo")}});
                else if(InspectionFirst&&Calls==2)plan.actions.Add(new EditorCommand{tool="find_scene_objects",args=new JObject{["query"]="AbsentStageNode"}});
                else if(!InspectionFirst&&!UnityEngine.GameObject.Find("StageOne"))plan.actions.Add(new EditorCommand{tool="create_node",name="StageOne"});
                else if(request.Messages.Any(m=>m.Content.Contains("CURRENT EXECUTION FOCUS: stage 2"))&&!UnityEngine.GameObject.Find("StageTwo"))plan.actions.Add(new EditorCommand{tool="create_node",name="StageTwo"});
                else plan.actions.Add(new EditorCommand{tool="review_stage"});
                return System.Threading.Tasks.Task.FromResult(new CompletionResult{Text=Newtonsoft.Json.JsonConvert.SerializeObject(plan)});
            }
            public System.Threading.Tasks.Task<bool> HealthAsync(System.Threading.CancellationToken token)=>System.Threading.Tasks.Task.FromResult(true);
            public void Dispose(){}
        }
        [TestCase(false)][TestCase(true)] public async System.Threading.Tasks.Task PlannedStageEvidenceNeverCompletesAnUnfinishedWholeTask(bool inspectionFirst)
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);var provider=new StageProvider{InspectionFirst=inspectionFirst};
            try{using(var controller=new ChatController(_=>provider,"UserSettings/Stages-"+Guid.NewGuid().ToString("N")))
            {
                controller.Settings.UseNativeFunctions=true;controller.Settings.AllowAutonomousScripts=true;controller.Settings.AutoVerify=true;controller.Settings.Power=AssistantPower.Deep;controller.Settings.UseSkills=false;
                await controller.SendAsync(inspectionFirst?"Исследуй сцену: проверь, есть ли узел AbsentStageNode, и запиши фактический отрицательный результат, если его нет. Затем создай пустой узел StageTwo. Сохрани остальное содержимое проекта и не подменяй отрицательный результат вымышленной находкой.":"Создай два пустых узла StageOne и StageTwo. Сначала создай первый узел и проверь его существование, затем создай второй и проверь его существование. Сохрани всё остальное содержимое проекта без изменений.");
                var answer=controller.Current.Messages.Last();Assert.IsFalse(answer.IsError,answer.Text);Assert.AreEqual(!inspectionFirst,(bool)UnityEngine.GameObject.Find("StageOne"),"An inspection stage must not force an unrelated mutation");Assert.IsTrue(UnityEngine.GameObject.Find("StageTwo"));Assert.AreEqual(2,answer.Task.ActiveApproachStage);Assert.AreEqual(2,provider.StageReviews);Assert.AreEqual(1,provider.FullReviews);
                Assert.AreEqual(2,answer.Steps.Count(r=>r.Success&&r.Tool=="review_stage"));Assert.AreEqual(2,answer.EditorReviews.Count(r=>r.ApproachStage>=0));Assert.AreEqual(1,answer.EditorReviews.Count(r=>r.ApproachStage<0));
            }}finally{UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);}
        }
        [Test] public void DiscoveryWithinAReadBatchEnablesItsExactReadOnTheNextImplementationTurn()
        {
            using(var controller=new ChatController(historyFolder:"UserSettings/DiscoveryBatch-"+Guid.NewGuid().ToString("N")))
            {
                controller.Settings.Mode=AssistantMode.Execute;controller.Settings.UseNativeFunctions=true;controller.Settings.AllowAutonomousScripts=true;
                var answer=new AssistantMessage();answer.Steps.Add(new EditorCommandResult{Success=true,Tool="discover_tools",Summary="{\"enabledTools\":[\"inspect_object\"]}"});answer.Steps.Add(new EditorCommandResult{Success=true,Tool="web_search",Summary="{}"});
                answer.Attempts.Add(new EditorPlanAttempt{Accepted=true,FirstResultIndex=0,ResultCount=2});
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                typeof(ChatController).GetField("executingMessage",flags).SetValue(controller,answer);typeof(ChatController).GetField("requireImplementationStep",flags).SetValue(controller,true);
                CompletionRequest Build()=>new CompletionRequest{Schema=EditorPlan.Schema(true,new[]{"create_node","inspect_object","discover_tools","web_search"},true)};
                CompletionRequest Filter()=>(CompletionRequest)typeof(ChatController).GetMethod("RequireImplementation",flags).Invoke(controller,new object[]{Build()});
                Assert.That(AgentFunctionProtocol.Definitions(Filter().Schema).Select(t=>(string)t["function"]["name"]),Does.Contain("inspect_object"),"Discovery before another independent read must still take effect");
                answer.Steps.Add(new EditorCommandResult{Success=true,Tool="inspect_object",Summary="{}"});answer.Attempts.Add(new EditorPlanAttempt{Accepted=true,FirstResultIndex=2,ResultCount=1});
                Assert.That(AgentFunctionProtocol.Definitions(Filter().Schema).Select(t=>(string)t["function"]["name"]),Does.Not.Contain("inspect_object"),"The targeted prerequisite read expires after its observation turn");
                answer.Task=new AgentTaskState{Approach=new System.Collections.Generic.List<string>{"Implement a general stage"}};
                Assert.That(AgentFunctionProtocol.Definitions(Filter().Schema).Select(t=>(string)t["function"]["name"]),Does.Contain("inspect_object"),"A scoped stage retains faithful read definitions instead of causing a discovery/rejection cycle");
            }
        }
        [Test] public void StageWorkingMemoryKeepsLatestActualFieldAndFlagsObservationsBeforeAChange()
        {
            var steps=new System.Collections.Generic.List<EditorCommandResult>{new EditorCommandResult{Tool="read_script",Success=true,Summary="{\"path\":\"Assets/Observed.cs\",\"source\":\"ACTUAL_SOURCE_END\"}"}};
            for(int i=0;i<40;i++)steps.Add(new EditorCommandResult{Tool="inspect_component",Target="owned:fixture",Success=true,Summary=new JObject{["component"]="Fixture",["observedValue"]=i}.ToString()});
            steps.Add(new EditorCommandResult{Tool="set_component",Success=true});
            steps.Add(new EditorCommandResult{Tool="inspect_component",Target="owned:fixture",Success=true,Summary="{\"component\":\"Fixture\",\"observedValue\":42}"});
            var method=typeof(ChatController).GetMethod("ObservationLedger",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);var rows=(JArray)method.Invoke(null,new object[]{steps});
            Assert.AreEqual(2,rows.Count);var field=rows.Single(r=>(string)r["tool"]=="inspect_component");Assert.AreEqual(42,(int)field["data"]["observedValue"]);Assert.IsFalse((bool)field["historicalBeforeLastChange"]);
            var source=rows.Single(r=>(string)r["tool"]=="read_script");Assert.AreEqual("ACTUAL_SOURCE_END",(string)source["data"]["source"]);Assert.IsTrue((bool)source["historicalBeforeLastChange"]);
        }
        [Test] public void ObservedWebSourceSurvivesLaterReadsWithoutInventingACompletedDownload()
        {
            var steps=new System.Collections.Generic.List<EditorCommandResult>{new EditorCommandResult{Tool="web_research",Success=false,Summary="{\"completed\":false,\"observedDataFiles\":[{\"url\":\"https://data.example/elevation.img\",\"observedFrom\":\"https://data.example/index\"}]}"}};
            for(int i=0;i<40;i++)steps.Add(new EditorCommandResult{Tool="inspect_object",Success=true,Summary="{}"});
            steps.Add(new EditorCommandResult{Tool="reply",Success=true,Summary="{\"url\":\"https://not-observed.example/imagined.img\"}"});
            var method=typeof(ChatController).GetMethod("WebSourceLedger",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);var rows=(JArray)method.Invoke(null,new object[]{steps});
            Assert.AreEqual(1,rows.Count);Assert.AreEqual("https://data.example/elevation.img",(string)rows[0]["url"]);Assert.That((string)rows[0]["status"],Does.Contain("not proof of download"));Assert.AreEqual(0,(int)rows[0]["index"]);
        }
        [Test] public void APlannedStageUsesOnlyTheLatestAcceptedNativeExchangeAndKeepsTheFullAudit()
        {
            var answer=new AssistantMessage{Task=new AgentTaskState{Approach=new System.Collections.Generic.List<string>{"Implement a pending stage"}}};
            for(int i=0;i<8;i++)
            {
                answer.Steps.Add(new EditorCommandResult{Tool="inspect_object",Success=true,Summary=new JObject{["actualValue"]=i}.ToString()});
                answer.Attempts.Add(new EditorPlanAttempt{Accepted=true,Round=i+1,FirstResultIndex=i,ResultCount=1,WireResponse=new JObject{["tool_calls"]=new JArray(new JObject{["id"]="actual_"+i,["function"]=new JObject{["name"]="inspect_object",["arguments"]=new JObject{["target"]="owned:fixture"}}})}.ToString()});
            }
            answer.Attempts.Add(new EditorPlanAttempt{Accepted=false,Error="Unknown tool",WireResponse="{\"tool_calls\":[{\"function\":{\"name\":\"not_registered\",\"arguments\":{}}}]}"});
            var request=new CompletionRequest();Assert.IsTrue(NativeHistory(request,answer));Assert.AreEqual(2,request.Messages.Count);Assert.AreEqual("actual_7",request.Messages[1].ToolCallId);Assert.AreEqual(7,(int)JObject.Parse(request.Messages[1].Content)["data"]["actualValue"]);Assert.AreEqual(9,answer.Attempts.Count);Assert.AreEqual(8,answer.Steps.Count);
        }
        [Test] public void NativeHistoryKeepsALargeSourceBatchedBesideSmallObservations()
        {
            var answer=new AssistantMessage();var calls=new JArray();
            for(int i=0;i<4;i++)
            {
                string name=i==0?"read_script":"inspect_object";
                calls.Add(new JObject{["id"]="call_batch_"+i,["function"]=new JObject{["name"]=name,["arguments"]=new JObject()}});
                answer.Steps.Add(new EditorCommandResult{Tool=name,Success=true,Summary=i==0?new JObject{["source"]=new string('s',15000)+"SOURCE_END"}.ToString():"{\"observed\":true}"});
            }
            answer.Attempts.Add(new EditorPlanAttempt{Accepted=true,FirstResultIndex=0,ResultCount=4,WireResponse=new JObject{["tool_calls"]=calls}.ToString()});
            var request=new CompletionRequest();Assert.IsTrue(NativeHistory(request,answer));
            Assert.That((string)JObject.Parse(request.Messages.Single(m=>m.ToolCallId=="call_batch_0").Content)["data"]["source"],Does.EndWith("SOURCE_END"));
            Assert.Less(request.Messages.Sum(m=>(m.Content??"").Length),40000);
        }
        [Test] public void NativeHistoryBindsActualObservationToItsOriginalCallAndKeepsLegacyHistoryUninvented()
        {
            var answer=new AssistantMessage();answer.Attempts.Add(new EditorPlanAttempt{Round=1,Accepted=true,FirstResultIndex=0,ResultCount=1,WireResponse="{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"id\":\"call_actual\",\"function\":{\"name\":\"inspect_object\",\"arguments\":{\"target\":\"owned:original\"}}}]}"});
            var originalWire=JObject.Parse(answer.Attempts[0].WireResponse);originalWire["thinking"]="OPAQUE_HISTORY_STATE";answer.Attempts[0].WireResponse=originalWire.ToString();
            answer.Steps.Add(new EditorCommandResult{Tool="inspect_object",Success=false,Error="Actual object missing",Summary="No object read"});
            var request=new CompletionRequest();Assert.IsTrue(NativeHistory(request,answer));Assert.AreEqual(2,request.Messages.Count);
            Assert.AreEqual("assistant",request.Messages[0].Role);Assert.AreEqual("OPAQUE_HISTORY_STATE",request.Messages[0].Thinking);Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(request.Messages[0]),Does.Not.Contain("OPAQUE_HISTORY_STATE"));Assert.AreEqual("call_actual",(string)request.Messages[0].ToolCalls[0]["id"]);
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
