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
    session.Settings.AllowEditorCode=false;var denied=await SceneToolExecutor.ExecuteAsync(good,null,context,default);Assert.IsFalse(denied.Success);
   }
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  }
 }
}
