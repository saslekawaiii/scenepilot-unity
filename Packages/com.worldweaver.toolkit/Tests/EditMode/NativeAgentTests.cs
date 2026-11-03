using System;
using System.Linq;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using WorldWeaver.Editor.Assistant;
namespace WorldWeaver.Tests
{
 public class NativeAgentTests
 {
  sealed class ReviewingProvider:ILLMProvider
  {
   public int MainCalls,Reviews;public bool Independent;
   public System.Threading.Tasks.Task<CompletionResult> CompleteAsync(CompletionRequest request,System.Threading.CancellationToken token)
   {
    if(request.Schema?["properties"]?["passed"]!=null)
    {
     Reviews++;Independent=request.Messages.All(m=>m.Role!="assistant")&&request.Messages.Any(m=>m.Content.Contains("CurrentHostMeasurements"));
     return System.Threading.Tasks.Task.FromResult(new CompletionResult{Text=Reviews==1?"{\"passed\":false,\"issues\":[\"Высота объекта 0 вместо 2 м\"],\"evidence\":\"Свежие bounds\"}":"{\"passed\":true,\"issues\":[],\"evidence\":\"Высота 2 м\"}",Model="fixture-review"});
    }
    MainCalls++;var plan=new EditorPlan{reply="Готово"};
    if(MainCalls==1||MainCalls==3){plan.complete=false;plan.actions.Add(new EditorCommand{tool="execute_editor_code",args=new JObject{["code"]=MainCalls==1?"var part=GameObject.CreatePrimitive(PrimitiveType.Cube); part.transform.SetParent(target.transform,false); return part.transform.position;":"target.transform.GetChild(0).position=Vector3.up*2; return target.transform.GetChild(0).position;"}});}
    return System.Threading.Tasks.Task.FromResult(new CompletionResult{Text=Newtonsoft.Json.JsonConvert.SerializeObject(plan),Model="fixture-main"});
   }
   public System.Threading.Tasks.Task<bool> HealthAsync(System.Threading.CancellationToken token)=>System.Threading.Tasks.Task.FromResult(true);
   public void Dispose(){}
  }
  [Test] public async System.Threading.Tasks.Task SeparateReviewCanRejectCompletionAndLetTheAgentRepairItsOwnCode()
  {
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);string folder=Path.Combine("UserSettings","SeparateReview-"+Guid.NewGuid().ToString("N"));var provider=new ReviewingProvider();
   try
   {
    using(var controller=new ChatController(_=>provider,folder))
    {
     controller.Settings.AllowEditorCode=true;controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=false;
     await controller.SendAsync("Через C# создай объект на высоте 2 м.");var answer=controller.Current.Messages.Last();
     Assert.IsFalse(answer.IsError,answer.Text);Assert.AreEqual(2,provider.Reviews);Assert.AreEqual(4,provider.MainCalls);Assert.IsTrue(provider.Independent);
     Assert.IsFalse(answer.EditorReviews[0].Passed);Assert.IsTrue(answer.EditorReviews[1].Passed);Assert.AreEqual(2,GameObject.Find("WorldWeaver Code Workspace").transform.GetChild(0).position.y);
    }
   }
   finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
  }
  sealed class RecoveredReadReviewProvider:ILLMProvider
  {
   public int Calls,Reviews;
   public System.Threading.Tasks.Task<CompletionResult> CompleteAsync(CompletionRequest request,System.Threading.CancellationToken token)
   {
    if(request.Schema?["properties"]?["passed"]!=null)
    {
     Reviews++;return System.Threading.Tasks.Task.FromResult(new CompletionResult{Text="{\"passed\":true,\"issues\":[],\"evidence\":\"Current host measurements confirm the requested cube at height two\"}",Model="fixture-review"});
    }
    var plan=new EditorPlan{reply="Готово"};Calls++;
    if(Calls==1){plan.complete=false;plan.actions.Add(new EditorCommand{tool="execute_editor_code",args=new JObject{["code"]="var part=GameObject.CreatePrimitive(PrimitiveType.Cube); part.transform.SetParent(target.transform,false); part.transform.position=Vector3.up*2; return part.transform.position;"}});}
    else if(Calls==2){plan.complete=false;plan.actions.Add(new EditorCommand{tool="read_task_step",args=new JObject{["index"]=8191}});}
    return System.Threading.Tasks.Task.FromResult(new CompletionResult{Text=Newtonsoft.Json.JsonConvert.SerializeObject(plan),Model="fixture-main"});
   }
   public System.Threading.Tasks.Task<bool> HealthAsync(System.Threading.CancellationToken token)=>System.Threading.Tasks.Task.FromResult(true);
   public void Dispose(){}
  }
  [Test] public async System.Threading.Tasks.Task FirstFreshWholeTaskReviewCanResolveAnEarlierFailedObservation()
  {
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);string folder=Path.Combine("UserSettings","RecoveredReadReview-"+Guid.NewGuid().ToString("N"));var provider=new RecoveredReadReviewProvider();
   try
   {
    using(var controller=new ChatController(_=>provider,folder))
    {
     controller.Settings.AllowEditorCode=true;controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=false;
     await controller.SendAsync("Через C# создай объект на высоте 2 м.");var answer=controller.Current.Messages.Last();
     Assert.AreEqual(1,provider.Reviews);Assert.IsTrue(answer.Attempts.All(a=>a.Accepted),"Exercise real execution failure, not malformed-plan repair");Assert.IsTrue(answer.Steps.Any(s=>!s.Success),"The historical execution failure remains auditable");Assert.IsTrue(answer.Task.Checks.All(c=>c.Passed));Assert.IsFalse(answer.IsError,answer.Text);Assert.AreEqual("Проверки пройдены",answer.Status);Assert.AreEqual(2,GameObject.Find("WorldWeaver Code Workspace").transform.GetChild(0).position.y);
    }
   }
   finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
  }
  [Test] public void InvalidOwnedReferenceReturnsActualCandidatesWithoutFuzzyResolution()
  {
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   try
   {
    var go=new GameObject("ReferenceRecoveryProbe");go.AddComponent<GeneratedSceneObject>();string valid=ProjectContextIndex.StableTarget(go);Assert.That(valid,Does.StartWith("owned:"));
    var error=Assert.Throws<ArgumentException>(()=>ProjectContextIndex.ResolveStable(valid.Substring(0,valid.Length-5)));
    Assert.That(error.Message,Does.Contain(valid));Assert.That(error.Message,Does.Contain("@ReferenceRecoveryProbe"));Assert.That(error.Message,Does.Contain("No fuzzy resolution"));
    Assert.AreSame(go,ProjectContextIndex.ResolveStable(valid));
   }
   finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
  }
  [Test] public void ProgressiveContextPreservesRecipeCodeAndIndependentGeometry()
  {
   var method=typeof(ChatController).GetMethod("ToolEvidenceForModel",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
   string recipe=new string('x',5000)+"ACTUAL_METHOD_BODY_AT_END";
   Assert.That((string)method.Invoke(null,new object[]{new EditorCommandResult{Tool="read_unity_recipe",Summary=recipe}}),Does.Contain("ACTUAL_METHOD_BODY_AT_END"));
   var summary=new JObject{["compiled"]=true,["result"]=new string('x',16000),["sceneReadback"]=new JArray(new JObject{["joints"]=new JArray(new JObject{["geometryWarning"]="WORLD_ANCHOR_OUTSIDE_ACTUAL_BOUNDS"})})}.ToString();
   Assert.That((string)method.Invoke(null,new object[]{new EditorCommandResult{Tool="execute_editor_code",Summary=summary}}),Does.Contain("WORLD_ANCHOR_OUTSIDE_ACTUAL_BOUNDS"));
  }
  [Test] public void ImportedRecipesArePinnedPagedAndReadOnly()
  {
   var catalog=UnityRecipeCatalog.CreateDefault();Assert.AreEqual(466,catalog.Count);
   var search=catalog.Search("physics raycast",4);var id=(string)search["recipes"][0]["id"];var recipe=catalog.Read(id);
   Assert.IsFalse((bool)recipe["codeExecuted"]);Assert.That((string)recipe["markdown"],Does.Contain("Physics.Raycast"));Assert.That((string)recipe["guidance"],Does.Contain("NOT installed"));
   Assert.IsNotEmpty((JArray)recipe["prerequisites"]);var apiSearch=catalog.Search("hinge joint door",4);Assert.That(apiSearch["apiTypes"].Values<string>(),Does.Contain("UnityEngine.HingeJoint"));Assert.That(apiSearch["recipes"].Select(r=>(string)r["id"]),Does.Contain("component-component-add"));Assert.Throws<ArgumentException>(()=>catalog.Read("../../.env"));Assert.Throws<ArgumentException>(()=>catalog.Read(id,-1));
   var skills=AgentSkillCatalog.CreateDefault();Assert.AreEqual(34,skills.List().Count(m=>m.Id.StartsWith("ww-recipes-")));
   foreach(var domain in skills.List().Where(m=>m.Id.StartsWith("ww-recipes-")))Assert.IsTrue(domain.Trusted,domain.Id);
  }
  sealed class RepeatedReadProvider:ILLMProvider
  {
   public int Calls;public bool ForcedImplementation;
   public System.Threading.Tasks.Task<CompletionResult> CompleteAsync(CompletionRequest request,System.Threading.CancellationToken token)
   {
    Calls++;var plan=new EditorPlan();
    if(Calls<=2){plan.complete=false;plan.actions.Add(new EditorCommand{tool="find_scene_objects",args=new JObject{["query"]="missing-adaptive-host"}});}
    else if(Calls==3)
    {
     ForcedImplementation=request.Messages.Any(m=>m.Content.Contains("exact reads already yielded no new evidence"))&&((JArray)request.Schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Any(b=>EditorToolSchema.ToolName(b)=="read_unity_recipe");
     plan.complete=false;plan.actions.Add(new EditorCommand{tool="create_node",name="AdaptiveHost"});
    }
    return System.Threading.Tasks.Task.FromResult(new CompletionResult{Text=Newtonsoft.Json.JsonConvert.SerializeObject(plan),Model="fixture"});
   }
   public System.Threading.Tasks.Task<bool> HealthAsync(System.Threading.CancellationToken token)=>System.Threading.Tasks.Task.FromResult(true);
   public void Dispose(){}
  }
  [Test] public async System.Threading.Tasks.Task IdenticalReadResultsCannotReplaceMissingImplementation()
  {
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);string folder=Path.Combine("UserSettings","AdaptiveRead-"+Guid.NewGuid().ToString("N"));var provider=new RepeatedReadProvider();
   try{using(var controller=new ChatController(_=>provider,folder)){controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=false;await controller.SendAsync("Создай пустой узел.");Assert.IsTrue(provider.ForcedImplementation);Assert.IsNotNull(GameObject.Find("AdaptiveHost"));Assert.IsFalse(controller.Current.Messages.Last().IsError);Assert.AreEqual(4,provider.Calls);}}
   finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
  }
  [Test] public void PhysicsTaskCannotPassWithOnlyItsGroundOrAnIncompleteBody()
  {
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   try
   {
    var task=AgentTaskVerifier.Create("Создай красный мяч на высоте 5 метров. Масса 2 кг, гравитация и отскок 0.7. Площадка под ним с коллайдером.",true);
    var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);AgentTaskVerifier.Verify(task);Assert.IsFalse(task.Checks.Single(c=>task.Requirements.Any(r=>r.Id==c.RequirementId&&r.Kind=="body_physics")).Passed);
    var ball=GameObject.CreatePrimitive(PrimitiveType.Sphere);ball.transform.position=Vector3.up*5;var body=ball.AddComponent<Rigidbody>();body.mass=2;body.useGravity=true;
    AgentTaskVerifier.Verify(task);Assert.IsFalse(task.Checks.Single(c=>task.Requirements.Any(r=>r.Id==c.RequirementId&&r.Kind=="body_physics")).Passed);
    var physics=new PhysicsMaterial{bounciness=.7f};ball.GetComponent<Collider>().sharedMaterial=physics;var material=new Material(Shader.Find("Standard")??Shader.Find("Universal Render Pipeline/Lit"));material.color=Color.red;ball.GetComponent<Renderer>().sharedMaterial=material;
    AgentTaskVerifier.Verify(task);Assert.IsTrue(task.Checks.Single(c=>task.Requirements.Any(r=>r.Id==c.RequirementId&&r.Kind=="body_physics")).Passed);UnityEngine.Object.DestroyImmediate(ground);AgentTaskVerifier.Verify(task);Assert.IsFalse(task.Checks.Single(c=>task.Requirements.Any(r=>r.Id==c.RequirementId&&r.Kind=="body_physics")).Passed);UnityEngine.Object.DestroyImmediate(physics);UnityEngine.Object.DestroyImmediate(material);
   }
   finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
  }
  [Test] public void NewPhysicalArtifactMustBeVisibleButExplicitInvisibleAndExistingBodiesAreExempt()
  {
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   try
   {
    new GameObject("Existing invisible service").AddComponent<Rigidbody>();
    var task=AgentTaskVerifier.Create("Создай физический объект.",true);var rule=task.Requirements.Single(r=>r.Kind=="body_presentation");
    AgentTaskVerifier.Verify(task);Assert.IsTrue(task.Checks.Single(c=>c.RequirementId==rule.Id).Passed,"Pre-existing service bodies are outside this task.");
    var body=new GameObject("New physical artifact").AddComponent<Rigidbody>();body.gameObject.AddComponent<BoxCollider>();
    AgentTaskVerifier.Verify(task);Assert.IsFalse(task.Checks.Single(c=>c.RequirementId==rule.Id).Passed,"An invisible collider is not a rendered artifact.");
    var visual=GameObject.CreatePrimitive(PrimitiveType.Cube);visual.transform.SetParent(body.transform,false);
    AgentTaskVerifier.Verify(task);Assert.IsTrue(task.Checks.Single(c=>c.RequirementId==rule.Id).Passed,"Compound bodies may render through a child.");
    var anchor=new GameObject("Connected service anchor").AddComponent<Rigidbody>();anchor.isKinematic=true;body.gameObject.AddComponent<HingeJoint>().connectedBody=anchor;
    AgentTaskVerifier.Verify(task);Assert.IsTrue(task.Checks.Single(c=>c.RequirementId==rule.Id).Passed,"A connected kinematic helper without a collider is a service body.");
    visual.GetComponent<Renderer>().enabled=false;AgentTaskVerifier.Verify(task);Assert.IsFalse(task.Checks.Single(c=>c.RequirementId==rule.Id).Passed);
    Assert.IsFalse(AgentTaskVerifier.Create("Создай невидимый физический объект.",true).Requirements.Any(r=>r.Kind=="body_presentation"));
   }
   finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
  }
  [Test] public void EveryRegisteredCommandProducesAnObjectFunctionContract()
  {
   var definitions=AgentFunctionProtocol.Definitions(EditorPlan.Schema());
   Assert.Greater(definitions.Count,40);
   foreach(var definition in definitions)Assert.AreEqual("object",(string)definition["function"]["parameters"]["type"],definition.ToString());
  }
  [Test] public void CapabilityRequirementsDoNotConfuseCaveWalkabilityWithNamedPlaceNavigation()
  {
   var cave=AgentTaskVerifier.Create("Создай каменистую пещеру с проходимым входом.",true);
   Assert.IsFalse(cave.Requirements.Any(r=>r.Kind=="navigation"));Assert.IsTrue(cave.Requirements.Any(r=>r.Kind=="cave_collision"));
   var survival=AgentTaskVerifier.Create("Создай мир, чтобы игрок мог выживать. Добавь звездное небо.",true);
   Assert.IsTrue(survival.Requirements.Any(r=>r.Kind=="survival_loop"));Assert.IsTrue(survival.Requirements.Any(r=>r.Kind=="starfield"));
   AgentTaskVerifier.Verify(survival);Assert.IsTrue(survival.Checks.Any(c=>!c.Passed));
  }
  [Test] public void CaveRefinesItsGridAndWorldResizePreservesItsPhysicalOpening()
  {
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   try
   {
    var world=SceneToolExecutor.Execute(new EditorPlan{actions={new EditorCommand{tool="create_world",name="GridWorld",args=JObject.Parse("{\"style\":\"moon\",\"width\":512,\"length\":512,\"resolution\":129,\"rocks\":0}")}}});Assert.IsTrue(world.Success,world.Error);
    string target=world.Results.Single().Target;
    var cave=SceneToolExecutor.Execute(new EditorPlan{actions={new EditorCommand{tool="create_cave",name="Opening",target=target,args=JObject.Parse("{\"position\":[40,40],\"width\":7}")}}});Assert.IsTrue(cave.Success,cave.Error);
    var terrain=UnityEngine.Object.FindFirstObjectByType<Terrain>();Assert.AreEqual(513,terrain.terrainData.heightmapResolution);
    var state=UnityEngine.Object.FindFirstObjectByType<WorldCave>();var originalCave=state.GetInstanceID();
    var update=SceneToolExecutor.Execute(new EditorPlan{actions={new EditorCommand{tool="update_world",target=target,args=JObject.Parse("{\"width\":1024,\"height\":60}")}}});Assert.IsTrue(update.Success,update.Error);
    Assert.AreEqual(originalCave,state.GetInstanceID());Assert.AreEqual(1025,terrain.terrainData.heightmapResolution);Assert.IsTrue(state.GetComponent<MeshCollider>());
    var task=AgentTaskVerifier.Create("Создай пещеру.",true);AgentTaskVerifier.Verify(task);Assert.IsTrue(task.Checks.Single(c=>task.Requirements.Any(r=>r.Id==c.RequirementId&&r.Kind=="cave_collision")).Passed);
   }
   finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
  }
  [Test] public void NativeDefinitionsPreserveTypedArgumentsAndDeduplicateCommands()
  {
   var tools=AgentFunctionProtocol.Definitions(EditorPlan.Schema(true,new[]{"set_lighting","create_mesh"}));
   Assert.AreEqual(3,tools.Count);Assert.AreEqual(1,tools.Count(t=>(string)t["function"]["name"]=="set_lighting"));
   Assert.NotNull(tools.First(t=>(string)t["function"]["name"]=="create_mesh")["function"]["parameters"]["properties"]["vertices"]);
  }
  [Test] public void NativeCallNormalizesWithoutInventingMissingArguments()
  {
   var schema=EditorPlan.Schema(true,new[]{"create_node"});
   var message=JObject.Parse("{\"content\":\"\",\"tool_calls\":[{\"function\":{\"name\":\"create_node\",\"arguments\":{\"name\":\"Host\",\"target\":\"\",\"args\":{}}}}]}");
   var plan=EditorPlan.Parse(AgentFunctionProtocol.Normalize(message,schema));Assert.AreEqual("Host",plan.actions.Single().name);Assert.IsFalse(plan.complete);
   ((JObject)message["tool_calls"][0]["function"]["arguments"]).Remove("name");
   Assert.AreEqual("",EditorPlan.Parse(AgentFunctionProtocol.Normalize(message,schema)).actions.Single().name);
  }
  [Test] public void NativeUnknownFunctionAndParallelCallsAreRejected()
  {
   var schema=EditorPlan.Schema(true,new[]{"inspect_object"});
   Assert.Throws<ProviderException>(()=>AgentFunctionProtocol.Normalize(JObject.Parse("{\"tool_calls\":[{\"function\":{\"name\":\"delete_object\",\"arguments\":{}}}]}"),schema));
   Assert.Throws<ProviderException>(()=>AgentFunctionProtocol.Normalize(JObject.Parse("{\"tool_calls\":[{},{}]}"),schema));
  }
  [Test] public void NativeFinishIsAProposalWithoutFabricatedToolEvidence()
  {
   var schema=EditorPlan.Schema(true,new[]{"inspect_object"});
   var plan=EditorPlan.Parse(AgentFunctionProtocol.Normalize(JObject.Parse("{\"tool_calls\":[{\"function\":{\"name\":\"finish_task\",\"arguments\":\"{\\\"reply\\\":\\\"Готово\\\"}\"}}]}"),schema));
   Assert.IsTrue(plan.complete);Assert.IsEmpty(plan.actions);
  }
  [Test] public async System.Threading.Tasks.Task EditorCodeReportsWorldGeometryIndependentlyOfReturnedClaims()
  {
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   string folder=Path.Combine("UserSettings","SpatialReadback-"+Guid.NewGuid().ToString("N"));
   try
   {
    using(var controller=new ChatController(historyFolder:folder))
    {
     controller.Settings.AllowEditorCode=true;controller.Settings.Mode=AssistantMode.Execute;
     var context=new AgentExecutionContext{Session=controller.Current,Message=new AssistantMessage()};
     var plan=new EditorPlan{actions={new EditorCommand{tool="execute_editor_code",args=new JObject{["code"]="using UnityEngine; using UnityEditor; var part=GameObject.CreatePrimitive(PrimitiveType.Cube); part.transform.SetParent(target.transform,false); part.transform.localScale=new Vector3(2,3,.1f); var body=Undo.AddComponent<Rigidbody>(part); var joint=Undo.AddComponent<HingeJoint>(part); joint.anchor=Vector3.left; return new { claimedCorrect=true };"}}}};
     var batch=await SceneToolExecutor.ExecuteAsync(plan,null,context,default);Assert.IsTrue(batch.Success,batch.Error);
     var report=JObject.Parse(batch.Results.Single(s=>s.Tool=="execute_editor_code").Summary);
     var observed=((JArray)report["sceneReadback"]).Single(r=>((JArray)r["joints"]).Count>0);var joint=observed["joints"][0];
     Assert.AreEqual(-2f,(float)joint["worldAnchor"][0]);Assert.AreEqual(-1f,(float)observed["renderWorldBounds"]["min"][0]);
     Assert.AreEqual(1f,(float)joint["distanceOutsideOwnColliderBounds"]);Assert.NotNull(joint["geometryWarning"]);
    }
   }
   finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
  }
  [Test] public async System.Threading.Tasks.Task TrustedEditorCodeCompilesActualUnityApiAndRepairsFromDiagnostics()
  {
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   string folder=Path.Combine("UserSettings","NativeCodeTest-"+Guid.NewGuid().ToString("N"));
   using(var controller=new ChatController(historyFolder:folder))
   {
    var session=controller.Current;session.Settings.AllowEditorCode=true;session.Settings.Mode=AssistantMode.Execute;
    var context=new AgentExecutionContext{Session=session,Message=new AssistantMessage()};
    var bad=new EditorPlan{actions={new EditorCommand{tool="execute_editor_code",args=new JObject{["code"]="return missingSymbol;"}}}};
    var failed=await SceneToolExecutor.ExecuteAsync(bad,null,context,default);Assert.IsFalse(failed.Success);Assert.That(failed.Results.Last().Error,Does.Contain("missingSymbol"));
    var good=new EditorPlan{actions={new EditorCommand{tool="execute_editor_code",args=new JObject{["code"]="var light=Undo.AddComponent<Light>(target); light.type=LightType.Point; light.range=17f; return new { range=light.range, position=target.transform.position, owner=target };"}}}};
    var result=await SceneToolExecutor.ExecuteAsync(good,null,context,default);Assert.IsTrue(result.Success,result.Error);
    Assert.AreEqual(17,UnityEngine.Object.FindFirstObjectByType<Light>().range);
    var readback=JObject.Parse((string)JObject.Parse(result.Results.Single(s=>s.Tool=="execute_editor_code").Summary)["result"]);Assert.AreEqual(JTokenType.Array,readback["position"].Type);Assert.AreEqual(result.Results.Single(s=>s.Tool=="execute_editor_code").Target,(string)readback["owner"]["target"]);
    Assert.IsTrue((bool)JObject.Parse(result.Results.Single(s=>s.Tool=="execute_editor_code").Summary)["sceneChanged"]);
    var observation=new EditorPlan{actions={new EditorCommand{tool="execute_editor_code",args=new JObject{["code"]="return target.GetComponents<Component>().Length;"}}}};
    var read=await SceneToolExecutor.ExecuteAsync(observation,null,context,default);Assert.IsTrue(read.Success,read.Error);
    var readStep=read.Results.Single(s=>s.Tool=="execute_editor_code");Assert.IsFalse((bool)JObject.Parse(readStep.Summary)["sceneChanged"],"A successful inspection snippet must not reset implementation progress");
    session.Settings.AllowEditorCode=false;var denied=await SceneToolExecutor.ExecuteAsync(good,null,context,default);Assert.IsFalse(denied.Success);
   }
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  }
 }
}
