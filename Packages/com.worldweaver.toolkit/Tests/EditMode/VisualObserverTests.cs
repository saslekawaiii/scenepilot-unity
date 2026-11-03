using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEditor.SceneManagement;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class VisualObserverTests
    {
        sealed class ObserverProvider:ILLMProvider
        {
            public CompletionRequest Seen;public int Calls;
            public Task<CompletionResult> CompleteAsync(CompletionRequest request,CancellationToken token)
            {Seen=request;Calls++;return Task.FromResult(new CompletionResult{Text="{\"summary\":\"A broad flat surface is visible\",\"candidates\":[{\"target\":\"owned:actual\",\"reason\":\"Visible measured plane\"},{\"target\":\"owned:invented\",\"reason\":\"No actual target\"}],\"uncertainties\":[]}",Model="fixture-vision"});}
            public Task<bool> HealthAsync(CancellationToken token)=>Task.FromResult(true);public void Dispose(){}
        }
        static ChatAttachment Image()
        {
            string file=Path.Combine("UserSettings","VisionFixture-"+Guid.NewGuid().ToString("N")+".png");Directory.CreateDirectory("UserSettings");var texture=new Texture2D(4,4);try{File.WriteAllBytes(file,texture.EncodeToPNG());return AgentViewCapture.Import(file);}finally{UnityEngine.Object.DestroyImmediate(texture);File.Delete(file);}
        }
        static AvailableModel Model()=>new AvailableModel{Provider=ProviderKind.Ollama,Model="fixture-vision",Endpoint="http://127.0.0.1:11434",Available=true,AutoDiscovered=true,SupportsVision=true};
        [Test] public async Task LocalObserverReceivesActualPixelsAndRejectsInventedTargetIds()
        {
            var image=Image();var provider=new ObserverProvider();var inventory=new JObject{["objects"]=new JArray(new JObject{["target"]="owned:actual",["name"]="Actual surface"})};var result=await AgentVisualObserver.ObserveAsync(Model(),image,inventory,CancellationToken.None,_=>provider);
            Assert.AreEqual(1,provider.Calls);Assert.AreEqual(AgentViewCapture.ReadBase64(image),provider.Seen.Messages.Single(m=>m.Images.Count>0).Images.Single());Assert.IsFalse(provider.Seen.UseToolCalls);Assert.AreEqual(false,provider.Seen.EnableThinking);Assert.AreEqual(0,provider.Seen.LocalKeepAliveSeconds);Assert.AreEqual("observed",(string)result["status"]);Assert.AreEqual(image.Sha256,(string)result["imageSha256"]);Assert.AreEqual(1,((JArray)result["candidates"]).Count);Assert.AreEqual("owned:actual",(string)result["candidates"][0]["target"]);Assert.IsTrue((bool)result["advisoryOnly"]);
        }
        [Test] public void ObserverSelectionPrefersInstalledCapacityWithoutLoadingHugeOrRemoteModels()
        {
            var small=Model();small.Model="small";small.SizeBytes=2L*1024*1024*1024;var medium=Model();medium.Model="medium";medium.SizeBytes=6L*1024*1024*1024;var huge=Model();huge.Model="huge";huge.SizeBytes=40L*1024*1024*1024;var remote=Model();remote.SizeBytes=7L*1024*1024*1024;remote.Remote=true;
            Assert.AreSame(medium,AgentVisualObserver.SelectLocalObserver(new[]{small,huge,remote,medium}));Assert.AreSame(small,AgentVisualObserver.SelectLocalObserver(new[]{huge,small}));Assert.IsNull(AgentVisualObserver.SelectLocalObserver(new[]{remote}));
        }
        [Test] public void ObserverNeverFallsBackToRemoteOrCloudModels()
        {
            var image=Image();var provider=new ObserverProvider();var model=Model();model.Remote=true;Assert.ThrowsAsync<ArgumentException>(async()=>await AgentVisualObserver.ObserveAsync(model,image,new JObject(),CancellationToken.None,_=>provider));model.Remote=false;model.Endpoint="https://example.com";Assert.ThrowsAsync<ArgumentException>(async()=>await AgentVisualObserver.ObserveAsync(model,image,new JObject(),CancellationToken.None,_=>provider));model.Endpoint="http://127.0.0.1:11434";model.Model="fixture:cloud";Assert.ThrowsAsync<ArgumentException>(async()=>await AgentVisualObserver.ObserveAsync(model,image,new JObject(),CancellationToken.None,_=>provider));Assert.AreEqual(0,provider.Calls);
        }
        [Test] public void OverviewIncludesFlatGeometryBeyondTerrainButExcludesDistantBackgroundDomes()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var data=new TerrainData{size=new Vector3(128,30,64)};var ground=Terrain.CreateTerrainGameObject(data);var plane=GameObject.CreatePrimitive(PrimitiveType.Plane);plane.transform.localScale=new Vector3(12.8f,1,12.8f);plane.transform.position=new Vector3(64,3,32);var background=GameObject.CreatePrimitive(PrimitiveType.Sphere);background.transform.localScale=Vector3.one*3000;
            try{var bounds=AgentViewCapture.OverviewBounds();Assert.GreaterOrEqual(bounds.size.z,128-.01);Assert.Less(bounds.size.x,300);Assert.Less(bounds.size.y,100);}
            finally{UnityEngine.Object.DestroyImmediate(ground);UnityEngine.Object.DestroyImmediate(plane);UnityEngine.Object.DestroyImmediate(background);UnityEngine.Object.DestroyImmediate(data);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        [Test] public void NativeVisionPlannerActuallyReceivesItsCapturedImage()
        {
            string folder=Path.Combine("UserSettings","NativeVision-"+Guid.NewGuid().ToString("N"));using(var controller=new ChatController(_=>new ObserverProvider(),folder))
            {
                controller.Settings.ModelKey="ollama:qwen3.5:2b";controller.Settings.UseNativeFunctions=true;controller.Settings.UseSkills=false;var answer=new AssistantMessage{Task=AgentTaskVerifier.Create("Inspect scene",false)};answer.Attachments.Add(Image());var session=controller.Current;
                var method=typeof(ChatController).GetMethod("BuildConstructiveRequest",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var request=(CompletionRequest)method.Invoke(controller,new object[]{session,"Inspect scene",answer,AssistantMode.Ask});Assert.AreEqual(AgentViewCapture.ReadBase64(answer.Attachments.Single()),request.Messages.Single(m=>m.Images.Count>0).Images.Single());
            }
        }
    }
}
