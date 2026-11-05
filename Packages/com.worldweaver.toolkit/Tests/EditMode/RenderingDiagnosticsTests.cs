using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class RenderingDiagnosticsTests
    {
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        static JObject Call(string method,params object[] args)
        {
            var type=typeof(SceneToolExecutor).Assembly.GetType("WorldWeaver.Editor.Assistant.RenderingDiagnostics",true);
            return (JObject)type.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
        }
        static JObject Compatibility(string shader,string family,bool supported=true,params string[] tags)=>Call("Compatibility",shader,tags,family,supported);
        Material Material()
        {
            var shader=Shader.Find("Standard");Assert.That(shader,Is.Not.Null,"Built-in Standard shader is needed for a real reference read-back fixture");var material=new Material(shader);owned.Add(material);return material;
        }
        GameObject Renderer(Material material,string name="Observed renderer")
        {
            var go=new GameObject(name);owned.Add(go);go.AddComponent<MeshRenderer>().sharedMaterial=material;return go;
        }
        [TearDown] public void Cleanup(){for(int i=owned.Count-1;i>=0;i--)if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);owned.Clear();}

        [TestCase("universal")]
        [TestCase("highDefinition")]
        public void StandardSupportDoesNotHideKnownSrpMismatch(string family)
        {
            var result=Compatibility("Standard",family,true);Assert.That((bool)result["shaderSupportObserved"],Is.True);Assert.That((bool)result["definitePipelineMismatch"],Is.True);Assert.That((string)result["status"],Is.EqualTo("known_pipeline_mismatch"));
        }
        [Test] public void StandardMatchesBuiltinFamilyButDoesNotProvePixels()
        {
            var result=Compatibility("Standard","builtin");Assert.That((bool)result["definitePipelineMismatch"],Is.False);Assert.That((string)result["status"],Is.EqualTo("known_shader_family_matches"));Assert.That((bool)result["renderedOutputVerified"],Is.False);
        }
        [Test] public void CustomPipelineDoesNotReceiveAUniversalMismatchVerdict()
        {
            var result=Compatibility("Standard","custom");Assert.That((bool)result["definitePipelineMismatch"],Is.False);Assert.That((string)result["status"],Is.EqualTo("custom_pipeline_compatibility_unknown"));
        }
        [Test] public void CustomShaderTagDifferenceRemainsUnverifiedRatherThanDefiniteFailure()
        {
            var result=Compatibility("My custom multipipeline shader","universal",true,"HDRenderPipeline");Assert.That((bool)result["definitePipelineMismatch"],Is.False);Assert.That((string)result["status"],Is.EqualTo("declared_pipeline_tag_differs_unverified"));
        }
        [Test] public void KnownOfficialSrpFamiliesAreDistinguished()
        {
            Assert.That((bool)Compatibility("Universal Render Pipeline/Lit","builtin",true,"UniversalPipeline")["definitePipelineMismatch"],Is.True);
            Assert.That((bool)Compatibility("HDRP/Lit","universal",true,"HDRenderPipeline")["definitePipelineMismatch"],Is.True);
            Assert.That((bool)Compatibility("Universal Render Pipeline/Lit","universal",true,"UniversalPipeline")["definitePipelineMismatch"],Is.False);
        }
        [Test] public void ErrorShaderAndUnsupportedShaderAreExplicitObservedProblems()
        {
            Assert.That((bool)Compatibility("Hidden/InternalErrorShader","custom")["errorShader"],Is.True);Assert.That((string)Compatibility("Custom supported elsewhere","builtin",false)["status"],Is.EqualTo("shader_not_supported"));
        }
        [Test] public void MaterialReadBackPreservesShaderAndSerializedProperties()
        {
            var material=Material();material.color=Color.magenta;string before=EditorJsonUtility.ToJson(material);var shader=material.shader;var facts=Call("MaterialFacts",material,"universal");
            Assert.That((string)facts["shaderName"],Is.EqualTo("Standard"));Assert.That((bool)facts["compatibility"]["definitePipelineMismatch"],Is.True);Assert.That((bool)facts["compatibility"]["errorShader"],Is.False,"An intentional magenta color is not itself an error-shader observation");Assert.That(material.shader,Is.EqualTo(shader));Assert.That(EditorJsonUtility.ToJson(material),Is.EqualTo(before));
        }
        [Test] public void PipelineSnapshotReadsQualityAndDefaultWithoutChangingSelection()
        {
            var quality=QualitySettings.renderPipeline;var defaults=GraphicsSettings.defaultRenderPipeline;int level=QualitySettings.GetQualityLevel();var result=Call("PipelineEvidence");
            Assert.That(result["family"],Is.Not.Null);Assert.That((int)result["qualityIndex"],Is.EqualTo(level));Assert.That(QualitySettings.renderPipeline,Is.EqualTo(quality));Assert.That(GraphicsSettings.defaultRenderPipeline,Is.EqualTo(defaults));Assert.That(QualitySettings.GetQualityLevel(),Is.EqualTo(level));
            if(quality)Assert.That((string)result["effectiveAsset"]["name"],Is.EqualTo(quality.name));else if(defaults)Assert.That((string)result["effectiveAsset"]["name"],Is.EqualTo(defaults.name));else Assert.That((string)result["family"],Is.EqualTo("builtin"));
        }
        [Test] public void PlainObjectsHaveNoRendererSummaryWithoutFakeNullDereference()
        {
            var go=new GameObject("Non-rendered light");owned.Add(go);go.AddComponent<Light>();Assert.That(Call("Summary",go),Is.Null);
        }
        [Test] public void ProactiveReviewIncludesActualMaterialReferencesAndBoundedObservedAlternatives()
        {
            var material=Material();var go=Renderer(material);var result=Call("ReviewSceneEvidence",(object)new[]{go});
            Assert.That((int)result["inspectedRendererCount"],Is.EqualTo(1));Assert.That((string)result["renderers"][0]["materials"][0]["shaderName"],Is.EqualTo("Standard"));Assert.That(((JArray)result["observedShaderAlternatives"]).Count,Is.LessThanOrEqualTo(3));Assert.That((bool)result["actualPixelsInspected"],Is.False);Assert.That(go.GetComponent<MeshRenderer>().sharedMaterial,Is.EqualTo(material));
        }
        [Test] public void ReviewDetailsRemainBoundedForRepeatedMaterialInstances()
        {
            var material=Material();var objects=new List<GameObject>();for(int i=0;i<30;i++)objects.Add(Renderer(material,"Renderer "+i));var result=Call("ReviewSceneEvidence",objects);
            Assert.That((int)result["inspectedRendererCount"],Is.EqualTo(30));Assert.That(((JArray)result["renderers"]).Count,Is.LessThanOrEqualTo(8));Assert.That(result["renderers"].ToString(Newtonsoft.Json.Formatting.None).Length,Is.LessThanOrEqualTo(16000));Assert.That((bool)result["detailsTruncated"],Is.True);
        }
    }
}
