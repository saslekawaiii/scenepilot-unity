using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using WorldWeaver.Editor.Assistant;
using Object=UnityEngine.Object;

namespace WorldWeaver.Tests
{
    public sealed class AgentViewCaptureCheckpointTests
    {
        const string Images="UserSettings/WorldWeaverImages";
        SceneSetup[] previousScenes;
        string folder,checkpoint;
        Camera camera;
        Light light;
        GameObject cube;
        Material material;
        HashSet<string> imagesBefore;
        bool imageFolderExisted,fixtureActive;
        static Type AdditionalLightType()=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalLightData",false)).FirstOrDefault(t=>t!=null);
        static bool IsUrp()
        {
            var pipeline=QualitySettings.renderPipeline?QualitySettings.renderPipeline:GraphicsSettings.defaultRenderPipeline;
            for(var type=pipeline?pipeline.GetType():null;type!=null;type=type.BaseType)
                if(type.FullName=="UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset")return true;
            return false;
        }
        [SetUp] public void Setup()
        {
            fixtureActive=false;previousScenes=null;folder=null;checkpoint=null;imagesBefore=null;
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("Actual rendering requires a graphics-enabled test Editor.");
            var existing=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            if(!Application.isBatchMode&&existing.Any(s=>s.isDirty||string.IsNullOrEmpty(s.path)))Assert.Ignore("Use an isolated consumer or save open scenes before capture/checkpoint tests.");
            previousScenes=existing.All(s=>!string.IsNullOrEmpty(s.path)&&!s.isDirty)?EditorSceneManager.GetSceneManagerSetup():null;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);fixtureActive=true;
            folder="Assets/AgentCaptureCheckpoint-"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
            cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="Capture checkpoint cube";
            var shader=IsUrp()?Shader.Find("Universal Render Pipeline/Lit"):Shader.Find("Standard");if(!shader)shader=Shader.Find("Unlit/Color");Assert.IsTrue(shader);
            material=new Material(shader){color=new Color(.24f,.62f,.78f)};AssetDatabase.CreateAsset(material,folder+"/Surface.mat");cube.GetComponent<Renderer>().sharedMaterial=material;
            camera=new GameObject("Capture checkpoint camera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(0,1.4f,-4);camera.transform.LookAt(Vector3.zero);camera.nearClipPlane=.1f;camera.farClipPlane=100;
            light=new GameObject("Capture checkpoint light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.transform.rotation=Quaternion.Euler(45,35,0);
            imageFolderExisted=Directory.Exists(Images);imagesBefore=imageFolderExisted?new HashSet<string>(Directory.GetFiles(Images)):new HashSet<string>();
        }
        [TearDown] public void Cleanup()
        {
            if(!fixtureActive)return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            if(!string.IsNullOrEmpty(folder))AssetDatabase.DeleteAsset(folder);
            if(!string.IsNullOrEmpty(checkpoint))
            {
                string path=Path.Combine("UserSettings/WorldWeaverCheckpoints",checkpoint);if(Directory.Exists(path))Directory.Delete(path,true);
                AgentCheckpoints.CollectUnreferencedContent();checkpoint=null;
            }
            if(imagesBefore!=null&&Directory.Exists(Images))
            {
                foreach(string path in Directory.GetFiles(Images).Where(p=>!imagesBefore.Contains(p)))File.Delete(path);
                if(!imageFolderExisted&&!Directory.EnumerateFileSystemEntries(Images).Any())Directory.Delete(Images);
            }
            if(previousScenes!=null&&previousScenes.Length>0)EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
            fixtureActive=false;
        }
        void SealCleanScene()
        {
            Assert.IsTrue(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),folder+"/Fixture.unity"));
            Assert.IsFalse(SceneManager.GetActiveScene().isDirty);
            checkpoint=AgentCheckpoints.Begin(Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"));AgentCheckpoints.Seal(checkpoint);
            Assert.IsTrue(AgentCheckpoints.CanExtend(checkpoint),AgentCheckpoints.ExtensionStatus(checkpoint).ToString());
        }
        static int[] PersistentComponents()=>SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>(t.gameObject.hideFlags&HideFlags.DontSave)==0)
            .SelectMany(t=>t.GetComponents<Component>()).Where(c=>c&&(c.hideFlags&HideFlags.DontSave)==0).Select(c=>c.GetInstanceID()).OrderBy(i=>i).ToArray();
        ChatAttachment CaptureAndRequireUnchanged()
        {
            var before=PersistentComponents();bool dirty=SceneManager.GetActiveScene().isDirty;bool fog=RenderSettings.fog;var active=RenderTexture.active;
            var image=AgentViewCapture.Capture(320,180,"game");
            Assert.IsTrue(File.Exists(image.Path));Assert.Greater(new FileInfo(image.Path).Length,100);Assert.IsNotEmpty(AgentViewCapture.ReadBase64(image));
            CollectionAssert.AreEqual(before,PersistentComponents(),"Read-only capture added or replaced a persistent scene component");
            Assert.AreEqual(dirty,SceneManager.GetActiveScene().isDirty,"Read-only capture changed the clean/dirty scene state");
            Assert.AreEqual(fog,RenderSettings.fog);Assert.AreSame(active,RenderTexture.active);
            Assert.IsTrue(AgentCheckpoints.CanExtend(checkpoint),AgentCheckpoints.ExtensionStatus(checkpoint).ToString());return image;
        }
        static void RequireUrp()
        {if(!IsUrp()||AdditionalLightType()==null)Assert.Ignore("Dedicated regression requires an active URP pipeline and its actual additional-light component type.");}
        [Test] public void ActualGameCapturePreservesCleanSceneComponentsAndCheckpointExtension()
        {
            var type=AdditionalLightType();if(type!=null)Assert.IsNull(light.GetComponent(type),"Fresh fixture starts without URP additional-light data");
            SealCleanScene();CaptureAndRequireUnchanged();
        }
        [TestCase(false)] [TestCase(true)] public void UrpCaptureDoesNotAddPersistentDataOrPrefabOverrides(bool prefabInstance)
        {
            RequireUrp();var type=AdditionalLightType();Assert.IsNull(light.GetComponent(type));string prefabPath=folder+"/Light.prefab";byte[] prefabBytes=null;string beforeOverrides=null;
            if(prefabInstance)
            {
                Assert.IsNotNull(PrefabUtility.SaveAsPrefabAssetAndConnect(light.gameObject,prefabPath,InteractionMode.AutomatedAction));
                Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(light.gameObject));prefabBytes=File.ReadAllBytes(prefabPath);beforeOverrides=Overrides(light.gameObject);
            }
            Assert.IsNull(light.GetComponent(type));SealCleanScene();CaptureAndRequireUnchanged();Assert.IsNull(light.GetComponent(type));
            if(prefabInstance){CollectionAssert.AreEqual(prefabBytes,File.ReadAllBytes(prefabPath));Assert.AreEqual(beforeOverrides,Overrides(light.gameObject),"Capture left prefab override metadata behind");}
        }
        static string Overrides(GameObject instance)
        {
            var properties=(PrefabUtility.GetPropertyModifications(instance)??Array.Empty<PropertyModification>()).Select(p=>new{target=p.target?p.target.GetInstanceID():0,p.propertyPath,p.value,reference=p.objectReference?p.objectReference.GetInstanceID():0}).OrderBy(p=>p.target).ThenBy(p=>p.propertyPath).ToArray();
            var added=PrefabUtility.GetAddedComponents(instance).Select(p=>p.instanceComponent?p.instanceComponent.GetInstanceID():0).OrderBy(i=>i).ToArray();
            var removed=PrefabUtility.GetRemovedComponents(instance).Select(p=>p.assetComponent?p.assetComponent.GetInstanceID():0).OrderBy(i=>i).ToArray();
            return JsonConvert.SerializeObject(new{properties,added,removed});
        }
        [Test] public void CaptureKeepsPreexistingUrpDataInstanceAndSerializedSettings()
        {
            RequireUrp();var type=AdditionalLightType();var data=light.GetComponent(type)??light.gameObject.AddComponent(type);var serialized=new SerializedObject(data);
            var pipeline=serialized.FindProperty("m_UsePipelineSettings");Assert.NotNull(pipeline);pipeline.boolValue=false;
            var tier=serialized.FindProperty("m_AdditionalLightsShadowResolutionTier");Assert.NotNull(tier);tier.intValue=1;serialized.ApplyModifiedPropertiesWithoutUndo();
            SealCleanScene();int id=data.GetInstanceID();string before=EditorJsonUtility.ToJson(data);CaptureAndRequireUnchanged();
            Assert.AreEqual(id,light.GetComponent(type).GetInstanceID());Assert.AreEqual(before,EditorJsonUtility.ToJson(data),"Existing serialized URP light settings were changed by capture");
        }
        [TestCase("light")] [TestCase("material")] public void RealManualEditsStillBlockExtensionAfterCapture(string edit)
        {
            SealCleanScene();CaptureAndRequireUnchanged();
            if(edit=="light"){light.intensity=2;EditorUtility.SetDirty(light);EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());}
            else{material.color=Color.yellow;EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);}
            Assert.IsFalse(AgentCheckpoints.CanExtend(checkpoint),"Capture cleanup must not weaken real manual-edit conflict detection");
        }
        [Test] public void OverviewFailureCleansTemporaryRenderResourcesWithoutChangingCheckpoint()
        {
            Object.DestroyImmediate(cube);SealCleanScene();var before=PersistentComponents();bool fog=RenderSettings.fog;var active=RenderTexture.active;
            Assert.Throws<ArgumentException>(()=>AgentViewCapture.Capture(320,180,"overview"));
            CollectionAssert.AreEqual(before,PersistentComponents());Assert.IsFalse(SceneManager.GetActiveScene().isDirty);Assert.AreEqual(fog,RenderSettings.fog);Assert.AreSame(active,RenderTexture.active);
            Assert.IsFalse(Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None).Any(c=>c.gameObject.name=="WorldWeaver capture camera"));
            Assert.IsTrue(AgentCheckpoints.CanExtend(checkpoint),AgentCheckpoints.ExtensionStatus(checkpoint).ToString());
        }
    }
}
