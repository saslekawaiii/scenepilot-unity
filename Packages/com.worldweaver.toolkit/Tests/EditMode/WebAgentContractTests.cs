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
        [Test] public void MissingPermittedSourceIsAnActualNegativeObservationWithoutCreatingAFileOrDroppingAnotherRead()
        {
            string path="Assets/WorldWeaverGenerated/Scripts/Absent_"+Guid.NewGuid().ToString("N")+".cs";
            var root=new UnityEngine.GameObject("Independent source-miss observation");root.AddComponent<GeneratedSceneObject>();
            try
            {
                var plan=new EditorPlan{actions=new System.Collections.Generic.List<EditorCommand>{new EditorCommand{tool="read_script",args=new JObject{["path"]=path}},new EditorCommand{tool="inspect_object",target=ProjectContextIndex.StableTarget(root)}}};
                var result=SceneToolExecutor.Execute(plan);Assert.IsTrue(result.Success,result.Error);Assert.AreEqual(2,result.Results.Count);
                var read=JObject.Parse(result.Results[0].Summary);Assert.AreEqual(path,(string)read["path"]);Assert.IsFalse((bool)read["exists"]);Assert.AreEqual(JTokenType.Null,read["source"].Type);Assert.AreEqual(JTokenType.Null,read["sha256"].Type);
                Assert.IsTrue(((JArray)read["availableScripts"]).Values<string>().All(System.IO.File.Exists));Assert.IsFalse(System.IO.File.Exists(path));
                Assert.AreEqual(root.name,(string)JObject.Parse(result.Results[1].Summary)["name"]);
                plan.actions[0].args["path"]="Packages/com.not-installed-worldweaver/missing.cs";Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(plan));
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void AssetSearchKeepsItsQueryTypesAndIndependentResultsInDurableMemory()
        {
            var command=new EditorCommand{tool="search_project",args=new JObject{["query"]="t:Script",["limit"]=2}};
            var result=SceneToolExecutor.Execute(new EditorPlan{actions=new System.Collections.Generic.List<EditorCommand>{command}});Assert.IsTrue(result.Success,result.Error);
            var actual=JObject.Parse(result.Results.Single().Summary);Assert.AreEqual("t:Script",(string)actual["query"]);Assert.LessOrEqual((int)actual["count"],2);
            foreach(var asset in actual["assets"]){Assert.IsNotEmpty((string)asset["path"]);Assert.IsNotNull(asset["mainAssetType"]);}
            var steps=new System.Collections.Generic.List<EditorCommandResult>{result.Results.Single(),new EditorCommandResult{Tool="search_project",Success=true,Summary=new JObject{["query"]="DifferentMissingAsset",["assets"]=new JArray(),["count"]=0}.ToString()}};
            var ledger=typeof(ChatController).GetMethod("ObservationLedger",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            var rows=(JArray)ledger.Invoke(null,new object[]{steps});Assert.AreEqual(2,rows.Count,"Unrelated asset searches must not overwrite one another in working memory");
            Assert.IsTrue(rows.Any(r=>(string)r["data"]["query"]=="DifferentMissingAsset"&&(int)r["data"]["count"]==0));Assert.IsTrue(rows.Any(r=>(string)r["data"]["query"]=="t:Script"));
        }
        sealed class MissingSourceRecoveryProvider:ILLMProvider
        {
            public int Calls;public bool ActualMissSeen;
            public System.Threading.Tasks.Task<CompletionResult> CompleteAsync(CompletionRequest request,System.Threading.CancellationToken token)
            {
                Calls++;Assert.IsTrue(request.UseToolCalls);var plan=new EditorPlan{complete=Calls>2};
                if(Calls==1)plan.actions.Add(new EditorCommand{tool="read_script",args=new JObject{["path"]="Assets/WorldWeaverGenerated/Scripts/AbsentSourceFixture.cs"}});
                if(Calls==2)
                {
                    ActualMissSeen=request.Messages.Any(m=>m.Content.Contains("A class name in a proposed plan is not an existing asset"));Assert.IsTrue(ActualMissSeen);
                    plan.actions.Add(new EditorCommand{tool="create_node",name="MissingSourceRecoveredGoal"});
                }
                return System.Threading.Tasks.Task.FromResult(new CompletionResult{Text=Newtonsoft.Json.JsonConvert.SerializeObject(plan)});
            }
            public System.Threading.Tasks.Task<bool> HealthAsync(System.Threading.CancellationToken token)=>System.Threading.Tasks.Task.FromResult(true);
            public void Dispose(){}
        }
        [Test] public async System.Threading.Tasks.Task ASourceMissCanReachTheNextJustifiedOperationInsteadOfRepeatingFatalPlanRejections()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);var provider=new MissingSourceRecoveryProvider();
            try
            {
                using(var controller=new ChatController(_=>provider,"UserSettings/MissingSourceRecovery-"+Guid.NewGuid().ToString("N")))
                {
                    controller.Settings.UseNativeFunctions=true;controller.Settings.Power=AssistantPower.Deep;controller.Settings.AutoVerify=false;controller.Settings.UseSkills=false;
                    await controller.SendAsync("Проверь путь Assets/WorldWeaverGenerated/Scripts/AbsentSourceFixture.cs и создай пустой узел MissingSourceRecoveredGoal.");var answer=controller.Current.Messages.Last();
                    Assert.IsFalse(answer.IsError,answer.Text);Assert.IsTrue(provider.ActualMissSeen);Assert.AreEqual(3,provider.Calls);Assert.IsTrue(UnityEngine.GameObject.Find("MissingSourceRecoveredGoal"));Assert.AreEqual(1,answer.Steps.Count(s=>s.Tool=="read_script"));
                }
            }
            finally{UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);}
        }
        sealed class ObservationRecoveryProvider:ILLMProvider
        {
            public int Reads,Reviews;public bool FeedbackSeen,ImplementationPhaseSeen,JsonRecoverySeen;
            public System.Threading.Tasks.Task<CompletionResult> CompleteAsync(CompletionRequest request,System.Threading.CancellationToken token)
            {
                if(request.Schema?["properties"]?["passed"]!=null)
                {
                    Reviews++;bool exists=UnityEngine.GameObject.Find("ObservedGoal");return System.Threading.Tasks.Task.FromResult(new CompletionResult{Text=new JObject{["passed"]=exists,["issues"]=exists?new JArray():new JArray("The requested node has not been created; observations alone are insufficient."),["evidence"]=exists?"Actual node exists":"Actual node is absent"}.ToString()});
                }
                var plan=new EditorPlan{complete=false};bool feedback=request.Messages.Any(m=>m.Content.Contains("LATEST INDEPENDENT REVIEW")&&m.Content.Contains("requested node"));FeedbackSeen|=feedback;
                if(feedback){JsonRecoverySeen|=!request.UseToolCalls;var available=((JArray)request.Schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName).ToArray();ImplementationPhaseSeen|=available.Contains("create_node")&&available.Contains("discover_tools")&&available.Contains("inspect_object");}
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
                    await controller.SendAsync("Создай пустой узел ObservedGoal.");Assert.IsTrue(provider.FeedbackSeen,"All request builders must return independent review feedback to the implementer");Assert.IsTrue(provider.ImplementationPhaseSeen,"A stalled read-only phase must expose implementation and discovery");Assert.IsFalse(provider.JsonRecoverySeen,"Repeated valid native observations are a planning problem; they cannot disable working native encoding");Assert.AreEqual(6,provider.Reads);Assert.GreaterOrEqual(provider.Reviews,2);Assert.IsTrue(UnityEngine.GameObject.Find("ObservedGoal"));Assert.IsFalse(controller.Current.Messages.Last().IsError,controller.Current.Messages.Last().Text);
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
                    var data=JObject.Parse(request.Messages.Last(m=>m.Role=="user").Content);
                    Assert.AreEqual((string)data["FullUserScope"],(string)data["OriginalRequest"],"A tentative stage must never replace the original user request");
                    if(stage)Assert.IsNotNull(data["TentativeStage"],"Stage responsibility must be separate from user authority");
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
                Assert.That(AgentFunctionProtocol.Definitions(Filter().Schema).Select(t=>(string)t["function"]["name"]),Does.Contain("inspect_object"),"Targeted diagnosis must remain available; only proven duplicate observations are bounded");
                answer.Task=new AgentTaskState{Approach=new System.Collections.Generic.List<string>{"Implement a general stage"}};
                Assert.That(AgentFunctionProtocol.Definitions(Filter().Schema).Select(t=>(string)t["function"]["name"]),Does.Contain("inspect_object"),"A proposed stage must not hide needed diagnostics");
                answer.Steps.Add(new EditorCommandResult{Success=true,Tool="discover_tools",Summary="{\"enabledTools\":[\"inspect_object\"]}"});answer.Attempts.Add(new EditorPlanAttempt{Accepted=true,FirstResultIndex=3,ResultCount=1});
                Assert.That(AgentFunctionProtocol.Definitions(Filter().Schema).Select(t=>(string)t["function"]["name"]),Does.Contain("inspect_object"),"A planned stage can still discover a specific missing prerequisite and read it on the following turn");
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
        [Test] public void CompletedReloadedJobsReachThePlannerAsLatestMeasuredObservations()
        {
            Assert.IsTrue(SceneToolExecutor.IsReadOnlyTool("job_result"),"A completed measurement is an observation, not a scene mutation");
            var steps=new System.Collections.Generic.List<EditorCommandResult>();
            for(int i=0;i<2;i++)steps.Add(new EditorCommandResult{Tool="job_result",Target="different_job_"+i,Success=i==1,Summary=new JObject{["Id"]="different_job_"+i,["Kind"]="player_control",["StableTarget"]="owned:actual-player",["Status"]="completed",["Success"]=i==1,["diagnosticHeader"]=new string('h',1600),["Measurement"]=new JObject{["yawDegrees"]=i==1?24:0,["pitchDegrees"]=i==1?-18:0,["relaxedWalkingArms"]=i==1,["tailEvidence"]="MEASURED_END"}.ToString(Newtonsoft.Json.Formatting.None)}.ToString(Newtonsoft.Json.Formatting.None)});
            var progress=typeof(ChatController).GetMethod("IsImplementationProgress",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);Assert.IsFalse((bool)progress.Invoke(null,new object[]{steps.Last()}));
            var method=typeof(ChatController).GetMethod("ObservationLedger",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);var rows=(JArray)method.Invoke(null,new object[]{steps});Assert.AreEqual(1,rows.Count,"Keep the latest result for the same measured subject, despite a new job ID");var row=rows.Single();Assert.AreEqual("owned:actual-player",(string)row["target"]);Assert.AreEqual("different_job_1",(string)row["data"]["Id"]);Assert.IsTrue((bool)row["data"]["Success"]);var measurement=(JObject)row["data"]["Measurement"];Assert.AreEqual(24,(int)measurement["yawDegrees"]);Assert.AreEqual(-18,(int)measurement["pitchDegrees"]);Assert.AreEqual("MEASURED_END",(string)measurement["tailEvidence"]);
        }
        [Test] public void RuntimeEvidenceUsesOnlyItsOwnMetricsAndRequestsFreshChecksAfterChanges()
        {
            var raw=new JObject{["Id"]="actual-job",["Kind"]="player_control",["StableTarget"]="owned:actual-player",["Success"]=false,["MeasurementStarted"]=false,["Samples"]=0,["RequestedSeconds"]=2,["RequireRelaxedArms"]=true,["Measurement"]="{\"yawDegrees\":0,\"pitchDegrees\":0,\"horizontalTravel\":4.4,\"realInputBackend\":\"Input System only; unconditional legacy calls fail\"}"};var before=raw.DeepClone();var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
            var project=typeof(ChatController).GetMethod("JobObservationForModel",flags);var observation=(JObject)project.Invoke(null,new object[]{raw});Assert.IsTrue(JToken.DeepEquals(before,raw));Assert.IsNull(observation["MeasurementStarted"]);Assert.IsNull(observation["Samples"]);Assert.AreEqual(4.4,(double)observation["Measurement"]["horizontalTravel"],.001);Assert.AreEqual("Input System only",(string)observation["Measurement"]["inputBackendConfiguration"]);Assert.IsNull(observation["Measurement"]["realInputBackend"]);
            var steps=new System.Collections.Generic.List<EditorCommandResult>{new EditorCommandResult{Tool="job_result",Success=false,Summary=raw.ToString()}};var recheck=typeof(ChatController).GetMethod("RequiredRuntimeRechecks",flags);Assert.IsEmpty((JArray)recheck.Invoke(null,new object[]{steps}));steps.Add(new EditorCommandResult{Tool="attach_script",Success=true,Target="owned:actual-camera"});var gaps=(JArray)recheck.Invoke(null,new object[]{steps});Assert.AreEqual(1,gaps.Count);Assert.AreEqual("run_player_check",(string)gaps[0]["tool"]);Assert.AreEqual("owned:actual-player",(string)gaps[0]["target"]);Assert.IsTrue((bool)gaps[0]["args"]["check_relaxed_arms"]);
            steps.Add(new EditorCommandResult{Tool="run_player_check",Success=true});var fresh=(JObject)raw.DeepClone();fresh["Success"]=true;steps.Add(new EditorCommandResult{Tool="job_result",Success=true,Summary=fresh.ToString()});Assert.IsEmpty((JArray)recheck.Invoke(null,new object[]{steps}),"Launching and completing another check is not another behavior edit");
        }
        [Test] public void ObservedWebSourceSurvivesLaterReadsWithoutInventingACompletedDownload()
        {
            var steps=new System.Collections.Generic.List<EditorCommandResult>{new EditorCommandResult{Tool="web_research",Success=false,Summary="{\"completed\":false,\"observedDataFiles\":[{\"url\":\"https://data.example/elevation.img\",\"observedFrom\":\"https://data.example/index\"}]}"}};
            for(int i=0;i<40;i++)steps.Add(new EditorCommandResult{Tool="inspect_object",Success=true,Summary="{}"});
            steps.Add(new EditorCommandResult{Tool="reply",Success=true,Summary="{\"url\":\"https://not-observed.example/imagined.img\"}"});
            var method=typeof(ChatController).GetMethod("WebSourceLedger",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);var rows=(JArray)method.Invoke(null,new object[]{steps});
            Assert.AreEqual(1,rows.Count);Assert.AreEqual("https://data.example/elevation.img",(string)rows[0]["url"]);Assert.That((string)rows[0]["status"],Does.Contain("not proof of download"));Assert.AreEqual(0,(int)rows[0]["index"]);
        }
        [Test] public void APlannedStageKeepsBoundedCausalExchangesIncludingTheLatestRejection()
        {
            var answer=new AssistantMessage{Task=new AgentTaskState{Approach=new System.Collections.Generic.List<string>{"Implement a pending stage"}}};
            for(int i=0;i<8;i++)
            {
                answer.Steps.Add(new EditorCommandResult{Tool="inspect_object",Success=true,Summary=new JObject{["actualValue"]=i}.ToString()});
                answer.Attempts.Add(new EditorPlanAttempt{Accepted=true,Round=i+1,FirstResultIndex=i,ResultCount=1,WireResponse=new JObject{["tool_calls"]=new JArray(new JObject{["id"]="actual_"+i,["function"]=new JObject{["name"]="inspect_object",["arguments"]=new JObject{["target"]="owned:fixture"}}})}.ToString()});
            }
            answer.Attempts.Add(new EditorPlanAttempt{Accepted=false,Error="Unknown tool",WireResponse="{\"tool_calls\":[{\"function\":{\"name\":\"not_registered\",\"arguments\":{}}}]}"});
            var request=new CompletionRequest();Assert.IsTrue(NativeHistory(request,answer));Assert.AreEqual(8,request.Messages.Count);var latestObservation=request.Messages.Single(m=>m.ToolCallId=="actual_7");Assert.AreEqual(7,(int)JObject.Parse(latestObservation.Content)["data"]["actualValue"]);Assert.That(request.Messages.Last().Content,Does.Contain("Unknown tool"));Assert.IsFalse((bool)JObject.Parse(request.Messages.Last().Content)["executed"]);Assert.AreEqual(9,answer.Attempts.Count);Assert.AreEqual(8,answer.Steps.Count);
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
        [Test] public void StagedHistoryKeepsTheLatestRejectionAfterSuccessfulObservations()
        {
            var answer=new AssistantMessage{Task=new AgentTaskState()};
            answer.Task.Approach.Add("Implement the requested behavior");
            answer.Steps.Add(new EditorCommandResult{Tool="read_task_step",Success=true,Summary="{\"actual\":true}"});
            answer.Attempts.Add(new EditorPlanAttempt{Round=1,Accepted=true,FirstResultIndex=0,ResultCount=1,WireResponse="{\"tool_calls\":[{\"id\":\"read_actual\",\"function\":{\"name\":\"read_task_step\",\"arguments\":{\"index\":0}}}]}"});
            answer.Attempts.Add(new EditorPlanAttempt{Round=2,Accepted=false,Error="Actual validation feedback",WireResponse="{\"tool_calls\":[{\"id\":\"rejected_actual\",\"function\":{\"name\":\"attach_script\",\"arguments\":{}}}]}"});
            var request=new CompletionRequest();Assert.IsTrue(NativeHistory(request,answer));
            Assert.AreEqual("rejected_actual",request.Messages.Last().ToolCallId);
            Assert.That(request.Messages.Last().Content,Does.Contain("Actual validation feedback"));
            Assert.IsFalse((bool)JObject.Parse(request.Messages.Last().Content)["executed"]);
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
                Assert.AreEqual(true,result.Results[0].ChangesApplied,"A changed field needs a factual mutation receipt");
                var unchanged=SceneToolExecutor.Execute(new EditorPlan{actions=new System.Collections.Generic.List<EditorCommand>{command}});
                Assert.IsTrue(unchanged.Success);Assert.AreEqual(false,unchanged.Results[0].ChangesApplied,"An idempotent assignment must not refill the progress budget");
                var progress=typeof(ChatController).GetMethod("IsImplementationProgress",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
                Assert.IsFalse((bool)progress.Invoke(null,new object[]{unchanged.Results[0]}));
                command.args["value"]="yes";Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(new EditorPlan{actions=new System.Collections.Generic.List<EditorCommand>{command}}));Assert.AreEqual(expected,player.AlwaysLook);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void EncodedReferenceIsDecodedOnlyForAnActualReferenceFieldAndStillValidatesItsScope()
        {
            var root=new UnityEngine.GameObject("Reference field fixture");root.AddComponent<GeneratedSceneObject>();var player=root.AddComponent<WorldPlayer>();
            var cameraObject=new UnityEngine.GameObject("Reference camera fixture");cameraObject.AddComponent<GeneratedSceneObject>();var camera=cameraObject.AddComponent<UnityEngine.Camera>();
            try
            {
                string encoded=new JObject{["target"]=ProjectContextIndex.StableTarget(cameraObject),["component"]="UnityEngine.Camera"}.ToString(Newtonsoft.Json.Formatting.None);
                var command=new EditorCommand{tool="set_component",target=ProjectContextIndex.StableTarget(root),args=new JObject{["type"]="WorldWeaver.WorldPlayer",["property"]="View",["value"]=encoded}};
                var plan=new EditorPlan{actions=new System.Collections.Generic.List<EditorCommand>{command}};var assigned=SceneToolExecutor.Execute(plan);Assert.IsTrue(assigned.Success,assigned.Error);Assert.AreSame(camera,player.View);Assert.AreEqual(JTokenType.Object,command.args["value"].Type);
                command.args["property"]="LastMessage";command.args["value"]=encoded;var text=SceneToolExecutor.Execute(plan);Assert.IsTrue(text.Success,text.Error);Assert.AreEqual(encoded,player.LastMessage);Assert.AreEqual(JTokenType.String,command.args["value"].Type);
                command.args["property"]="View";command.args["value"]="{\"asset\":\"../private.asset\"}";Assert.Throws<ArgumentException>(()=>SceneToolExecutor.Validate(plan));Assert.AreSame(camera,player.View);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraObject);}
        }
        [Test] public void ScenePaginationDeliversEveryRowThroughNativeAndWorkingMemoryBudgets()
        {
            var root=new UnityEngine.GameObject("PagedInventoryFixture");root.AddComponent<GeneratedSceneObject>();var expected=new System.Collections.Generic.HashSet<string>();
            try
            {
                for(int i=0;i<45;i++){var go=UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);go.name="Inventory_"+i.ToString("D3")+"_"+new string('я',160);go.transform.SetParent(root.transform);go.AddComponent<GeneratedSceneObject>();expected.Add(ProjectContextIndex.StableTarget(go));}
                var seen=new System.Collections.Generic.HashSet<string>();int offset=0,pages=0;
                while(true)
                {
                    var page=ProjectContextIndex.ListScene(new JObject{["parent"]=ProjectContextIndex.StableTarget(root),["component"]="Renderer",["limit"]=100,["offset"]=offset});var objects=(JArray)page["objects"];Assert.AreEqual(45,(int)page["total"]);Assert.Greater(objects.Count,0);Assert.Less(page.ToString(Newtonsoft.Json.Formatting.None).Length,13000);
                    foreach(var row in objects)Assert.IsTrue(seen.Add((string)row["target"]),"Pagination must never repeat or skip a row");
                    var answer=new AssistantMessage();answer.Steps.Add(new EditorCommandResult{Tool="list_scene_objects",Success=true,Summary=page.ToString(Newtonsoft.Json.Formatting.None)});answer.Attempts.Add(new EditorPlanAttempt{Accepted=true,FirstResultIndex=0,ResultCount=1,WireResponse="{\"tool_calls\":[{\"id\":\"page_call\",\"function\":{\"name\":\"list_scene_objects\",\"arguments\":{}}}]}"});var request=new CompletionRequest();Assert.IsTrue(NativeHistory(request,answer),"The complete page must enter native history");Assert.IsTrue(JToken.DeepEquals(JObject.Parse(page.ToString(Newtonsoft.Json.Formatting.None)),JObject.Parse(request.Messages.Single(m=>m.Role=="tool").Content)["data"]),"The complete normalized JSON page must survive the native tool channel");
                    var method=typeof(ChatController).GetMethod("ObservationLedger",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);var memory=(JArray)method.Invoke(null,new object[]{answer.Steps});Assert.IsTrue(JToken.DeepEquals(JObject.Parse(page.ToString(Newtonsoft.Json.Formatting.None)),memory.Single()["data"]),"The working-memory path must retain the complete delivered page too");pages++;
                    if(page["nextOffset"].Type==JTokenType.Null)break;Assert.AreEqual(offset+objects.Count,(int)page["nextOffset"]);offset=(int)page["nextOffset"];Assert.Less(pages,45);
                }
                Assert.Greater(pages,1);CollectionAssert.AreEquivalent(expected,seen);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void OversizedInventoryRowKeepsAnInspectableTargetWithoutBreakingThePage()
        {
            var go=UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);go.AddComponent<GeneratedSceneObject>();var material=new UnityEngine.Material(UnityEngine.Shader.Find("Standard")??UnityEngine.Shader.Find("Universal Render Pipeline/Lit"));material.name=new string('m',20000);go.GetComponent<UnityEngine.Renderer>().sharedMaterial=material;
            try
            {
                var page=ProjectContextIndex.ListScene(new JObject{["parent"]=ProjectContextIndex.StableTarget(go),["component"]="Renderer",["limit"]=100});Assert.Less(page.ToString(Newtonsoft.Json.Formatting.None).Length,13000);Assert.AreEqual(1,(int)page["returned"]);Assert.AreEqual(ProjectContextIndex.StableTarget(go),(string)page["objects"][0]["target"]);Assert.IsTrue((bool)page["objects"][0]["listingDetailsOmitted"]);Assert.AreEqual(JTokenType.Null,page["nextOffset"].Type);
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(material);}
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
