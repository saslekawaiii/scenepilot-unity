using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using WorldWeaver.Editor.Assistant;
using Object=UnityEngine.Object;

namespace WorldWeaver.Tests
{
    public sealed class RenderLightInitializationTests
    {
        string checkpoint,fixtureFolder;readonly List<Material> materials=new List<Material>();
        SceneSetup[] previousScenes;bool fixtureActive;
        static object Call(string method,params object[] args)
        {
            var type=typeof(SceneToolExecutor).Assembly.GetType("WorldWeaver.Editor.Assistant.RenderLightInitialization",true);
            try{return type.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}
            catch(TargetInvocationException error)when(error.InnerException!=null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        static Dictionary<int,string> Capture()=>(Dictionary<int,string>)Call("Capture");
        static void Initialize(IReadOnlyDictionary<int,string> before)=>Call("InitializeChanged",before);
        static Type DataType()=>Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalLightData, Unity.RenderPipelines.Universal.Runtime");
        static void RequireUrp(){if(!(bool)Call("ActiveUrp"))Assert.Ignore("This dedicated test requires an actually configured URP; the fixture does not change pipeline settings.");Assert.That(DataType(),Is.Not.Null);}
        static Light Light(string name,bool enabled=true){var light=new GameObject(name).AddComponent<Light>();light.type=LightType.Directional;light.enabled=enabled;return light;}
        [SetUp] public void Setup()
        {
            fixtureActive=false;previousScenes=null;checkpoint=null;fixtureFolder=null;
            var existing=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            if(!Application.isBatchMode&&existing.Any(scene=>scene.isDirty||string.IsNullOrEmpty(scene.path)))Assert.Ignore("Use an isolated consumer or save open scenes before light/checkpoint tests.");
            previousScenes=existing.All(scene=>!string.IsNullOrEmpty(scene.path)&&!scene.isDirty)?EditorSceneManager.GetSceneManagerSetup():null;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);fixtureActive=true;
        }
        [TearDown] public void Cleanup()
        {
            if(!fixtureActive)return;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);if(!string.IsNullOrEmpty(fixtureFolder))AssetDatabase.DeleteAsset(fixtureFolder);foreach(var material in materials)if(material)Object.DestroyImmediate(material);materials.Clear();
                if(checkpoint!=null){string folder=Path.Combine("UserSettings/WorldWeaverCheckpoints",checkpoint);if(Directory.Exists(folder))Directory.Delete(folder,true);checkpoint=null;}
            }
            finally
            {
                if(previousScenes!=null&&previousScenes.Length>0)EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
                fixtureActive=false;
            }
        }
        [Test] public void CaptureIncludesAdditiveLoadedSceneLightsAndTheirActualSerializedValues()
        {
            var first=Light("Active scene light");fixtureFolder="Assets/RenderLightInitialization-"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",Path.GetFileName(fixtureFolder));
            Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),fixtureFolder+"/Initial.unity"),Is.True,"Additive Editor scenes require a saved initial scene.");
            var additive=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(additive);var second=Light("Additive light");var before=Capture();
            Assert.That(before.ContainsKey(first.GetInstanceID()),Is.True);Assert.That(before.ContainsKey(second.GetInstanceID()),Is.True);Assert.That((string)JObject.Parse(before[first.GetInstanceID()])["componentSerialized"],Is.EqualTo(EditorJsonUtility.ToJson(first)));
            second.intensity+=3;Assert.That(Capture()[second.GetInstanceID()],Is.Not.EqualTo(before[second.GetInstanceID()]));Assert.That(additive.isLoaded,Is.True);
        }
        [Test] public void OnlyNewOrChangedLightsAreInitializedAndExistingDataRemainsUntouched()
        {
            RequireUrp();var unchanged=Light("Unchanged user light");var changed=Light("Admitted changed light");var existing=Light("Light with authored URP data");var data=Undo.AddComponent(existing.gameObject,DataType());string originalData=EditorJsonUtility.ToJson(data);int identity=data.GetInstanceID();var before=Capture();
            changed.intensity+=2;existing.intensity+=1;var added=Light("Admitted new light");string changedValues=EditorJsonUtility.ToJson(changed);Initialize(before);
            Assert.That((bool)unchanged.GetComponent(DataType()),Is.False);Assert.That((bool)changed.GetComponent(DataType()),Is.True);Assert.That((bool)added.GetComponent(DataType()),Is.True);
            Assert.That(existing.GetComponent(DataType()).GetInstanceID(),Is.EqualTo(identity));Assert.That(EditorJsonUtility.ToJson(existing.GetComponent(DataType())),Is.EqualTo(originalData));Assert.That(EditorJsonUtility.ToJson(changed),Is.EqualTo(changedValues));
            int changedData=changed.GetComponent(DataType()).GetInstanceID();Initialize(Capture());Assert.That(changed.GetComponent(DataType()).GetInstanceID(),Is.EqualTo(changedData));
        }
        [Test] public void UnchangedExistingLightWithoutPipelineDataIsNotAnAuthorizedInitialization()
        {
            RequireUrp();var light=Light("Existing untouched light");var before=Capture();Assert.That((bool)light.GetComponent(DataType()),Is.False);Initialize(before);Assert.That((bool)light.GetComponent(DataType()),Is.False);Assert.That(Capture()[light.GetInstanceID()],Is.EqualTo(before[light.GetInstanceID()]));
        }
        [Test] public void AdmittedTransformVisibilityAndLayerEditsInitializeWithoutLightPropertyChanges()
        {
            RequireUrp();var moved=Light("Transform-only moved light");var rotated=Light("Transform-only rotated light");var scaled=Light("Transform-only scaled light");var deactivated=Light("Activation-only light");var layered=Light("Layer-only light");var child=Light("Parent-transform light");var parent=new GameObject("Admitted light parent");child.transform.SetParent(parent.transform,false);
            var lights=new[]{moved,rotated,scaled,deactivated,layered,child};var untouched=Light("Unchanged outside admitted edits");var values=lights.ToDictionary(l=>l.GetInstanceID(),l=>EditorJsonUtility.ToJson(l));var before=Capture();
            moved.transform.position=Vector3.right*3;rotated.transform.rotation=Quaternion.Euler(20,40,0);scaled.transform.localScale=Vector3.one*2;deactivated.gameObject.SetActive(false);layered.gameObject.layer=2;parent.transform.position=Vector3.up*4;
            foreach(var light in lights){Assert.That(EditorJsonUtility.ToJson(light),Is.EqualTo(values[light.GetInstanceID()]),light.name+" component properties should be unchanged");Assert.That(Capture()[light.GetInstanceID()],Is.Not.EqualTo(before[light.GetInstanceID()]),light.name+" observed lighting signature must change");}
            Initialize(before);foreach(var light in lights)Assert.That((bool)light.GetComponent(DataType()),Is.True,light.name);Assert.That((bool)untouched.GetComponent(DataType()),Is.False);
        }
        [Test] public void AdmittedLightInitializationKeepsCheckpointValidAcrossActualCameraRender()
        {
            RequireUrp();if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("Actual render regression requires graphics; no simulated render is substituted.");
            var camera=new GameObject("Existing capture test camera").AddComponent<Camera>();camera.enabled=false;camera.transform.position=new Vector3(0,2,-5);camera.transform.LookAt(Vector3.zero);
            var cameraData=Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");Assert.That(cameraData,Is.Not.Null);Undo.AddComponent(camera.gameObject,cameraData);
            var materialBefore=RenderMaterialInitialization.Capture();var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var shader=Shader.Find("Universal Render Pipeline/Lit");Assert.That(shader,Is.Not.Null);var material=new Material(shader);materials.Add(material);cube.GetComponent<Renderer>().sharedMaterial=material;RenderMaterialInitialization.InitializeChanged(materialBefore);
            var light=Light("Admitted enabled light",false);var before=Capture();checkpoint=AgentCheckpoints.Begin(Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"));light.enabled=true;light.intensity=2;Initialize(before);Assert.That((bool)light.GetComponent(DataType()),Is.True);AgentCheckpoints.Seal(checkpoint);Assert.That(AgentCheckpoints.CanExtend(checkpoint),Is.True,AgentCheckpoints.Inspect(checkpoint).ToString());
            var target=RenderTexture.GetTemporary(96,64,24);var previous=camera.targetTexture;var previousActive=RenderTexture.active;
            try{camera.targetTexture=target;camera.Render();}
            finally{camera.targetTexture=previous;RenderTexture.active=previousActive;RenderTexture.ReleaseTemporary(target);}
            Assert.That(AgentCheckpoints.CanExtend(checkpoint),Is.True,AgentCheckpoints.Inspect(checkpoint).ToString());
            // Protection is still exact: an actual later scene edit must conflict.
            light.intensity+=1;Assert.That(AgentCheckpoints.CanExtend(checkpoint),Is.False);
        }
    }
}
