using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldWeaver.Editor.Assistant
{
    // Shader support and pipeline compatibility are different observations.
    // This helper reads configuration; it does not replace materials/shaders,
    // change pipeline assets, render a frame or infer image quality.
    internal static class RenderingDiagnostics
    {
        static string Short(string value,int length=180)=>ChatHistoryStore.Redact((value??"").Substring(0,Math.Min(length,(value??"").Length)));
        static JObject Asset(UnityEngine.Object value)=>value?new JObject{["name"]=Short(value.name,80),["path"]=Short(AssetDatabase.GetAssetPath(value),240),["type"]=value.GetType().FullName}:null;
        static string Family(RenderPipelineAsset asset)
        {
            if(!asset)return "builtin";
            for(var type=asset.GetType();type!=null;type=type.BaseType)
            {
                if(type.FullName=="UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset")return "universal";
                if(type.FullName=="UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset")return "highDefinition";
            }
            return "custom";
        }
        internal static JObject PipelineEvidence()
        {
            var quality=QualitySettings.renderPipeline;var defaults=GraphicsSettings.defaultRenderPipeline;var current=GraphicsSettings.currentRenderPipeline;
            var effective=current?current:quality?quality:defaults;int qualityIndex=QualitySettings.GetQualityLevel();var names=QualitySettings.names;
            return new JObject{["family"]=Family(effective),["effectiveAsset"]=Asset(effective),["reportedCurrentAsset"]=Asset(current),["qualityOverride"]=Asset(quality),["defaultAsset"]=Asset(defaults),["qualityIndex"]=qualityIndex,["qualityName"]=qualityIndex>=0&&qualityIndex<names.Length?Short(names[qualityIndex]):null,["selectionSource"]=current&&current!=quality&&current!=defaults?"observed current asset":quality?"quality override":defaults?"graphics default":"built-in (no SRP asset)",["runtimePipelineInstancePresent"]=RenderPipelineManager.currentPipeline!=null,["selectionNote"]="Asset selection is observed independently of pipeline-instance initialization; a null runtime instance before rendering does not imply Built-in RP."};
        }
        internal static JObject Compatibility(string shaderName,string[] pipelineTags,string family,bool supported)
        {
            shaderName=shaderName??"";pipelineTags=pipelineTags??Array.Empty<string>();
            bool error=shaderName=="Hidden/InternalErrorShader"||shaderName=="Hidden/Universal Render Pipeline/FallbackError"||shaderName=="Hidden/HDRP/FallbackError"||shaderName=="Hidden/HDRenderPipeline/FallbackError";
            bool builtinStandard=shaderName=="Standard"||shaderName=="Standard (Specular setup)"||shaderName=="Legacy Shaders/Diffuse"||shaderName=="Legacy Shaders/Specular";
            // A project shader can use any namespace-like name. Only these
            // explicit prebuilt names establish a known family; prefixes alone do not.
            bool universal=new[]{"Universal Render Pipeline/Lit","Universal Render Pipeline/Simple Lit","Universal Render Pipeline/Unlit","Universal Render Pipeline/Baked Lit","Universal Render Pipeline/Terrain/Lit"}.Contains(shaderName);
            bool hd=shaderName=="HDRP/Lit"||shaderName=="HDRP/Unlit";
            bool knownFamily=family=="builtin"||family=="universal"||family=="highDefinition";
            bool mismatch=knownFamily&&(builtinStandard&&family!="builtin"||universal&&family!="universal"||hd&&family!="highDefinition");
            string expected=family=="universal"?"UniversalPipeline":family=="highDefinition"?"HDRenderPipeline":"";
            bool taggedDifferent=knownFamily&&pipelineTags.Any(t=>!string.IsNullOrEmpty(t))&&!pipelineTags.Contains(expected);
            string state=shaderName.Length==0?"missing_shader":error?"error_shader":!supported?"shader_not_supported":!knownFamily?"custom_pipeline_compatibility_unknown":mismatch?"known_pipeline_mismatch":taggedDifferent?"declared_pipeline_tag_differs_unverified":builtinStandard||universal||hd?"known_shader_family_matches":"custom_or_untagged_shader_unverified";
            var result=new JObject{["status"]=state,["definitePipelineMismatch"]=mismatch,["errorShader"]=error,["shaderSupportObserved"]=supported,["renderedOutputVerified"]=false};
            if(mismatch)result["warning"]="This known prebuilt shader belongs to a different active render-pipeline family. Shader.isSupported alone does not establish compatibility. Select an observed compatible shader intentionally, then verify the actual rendered result.";
            else if(error||shaderName.Length==0||!supported)result["warning"]="Shader is missing, unsupported or an error/fallback shader. The material cannot be accepted as visually verified from mesh/physics checks.";
            else if(taggedDifferent)result["warning"]="Declared RenderPipeline tags differ from the observed pipeline. Custom multi-pipeline subshaders/fallbacks are not fully evaluated; verify actual rendering rather than assuming a definite failure.";
            else if(!knownFamily)result["warning"]="Custom pipeline detected. No universal compatibility judgment or automatic shader replacement is inferred.";
            return result;
        }
        static string[] Tags(Shader shader)
        {
            if(!shader)return Array.Empty<string>();var tags=new List<string>();for(int i=0;i<Math.Min(shader.subshaderCount,4);i++)tags.Add(Short(shader.FindSubshaderTagValue(i,new ShaderTagId("RenderPipeline")).name??"",128));return tags.Distinct().ToArray();
        }
        internal static JObject MaterialFacts(Material material,string family)
        {
            if(!material)return new JObject{["materialMissing"]=true,["renderedOutputVerified"]=false,["warning"]="Shared material slot is empty; runtime assignment is not inferred."};
            var shader=material.shader;string name=shader?Short(shader.name,240):"";var tags=Tags(shader);string activeTag=shader?material.GetTag("RenderPipeline",false,""):"";
            var result=new JObject{["material"]=Asset(material),["shaderName"]=name,["shaderAsset"]=Asset(shader),["shaderSupported"]=shader&&shader.isSupported,["activeSubshaderRenderPipelineTag"]=activeTag,["declaredSubshaderRenderPipelineTags"]=new JArray(tags),["renderQueue"]=material.renderQueue,["compatibility"]=Compatibility(name,tags,family,shader&&shader.isSupported)};
            if(shader)result["shaderPassCount"]=material.passCount;return result;
        }
        static JArray Alternatives(string family)
        {
            string[] candidates=family=="universal"?new[]{"Universal Render Pipeline/Lit","Universal Render Pipeline/Simple Lit","Universal Render Pipeline/Unlit"}:family=="highDefinition"?new[]{"HDRP/Lit","HDRP/Unlit"}:family=="builtin"?new[]{"Standard","Standard (Specular setup)","Unlit/Color"}:Array.Empty<string>();
            var result=new JArray();foreach(string name in candidates)
            {
                var shader=Shader.Find(name);if(!shader)continue;var tags=Tags(shader);result.Add(new JObject{["shaderName"]=shader.name,["isSupported"]=shader.isSupported,["declaredPipelineTags"]=new JArray(tags),["compatibility"]=Compatibility(shader.name,tags,family,shader.isSupported)});
            }
            return result;
        }
        internal static JObject Summary(GameObject go)
        {
            if(!go)return null;var renderer=go.GetComponent<Renderer>();if(!renderer)return null;var pipeline=PipelineEvidence();string family=(string)pipeline["family"];var materials=renderer.sharedMaterials;
            return new JObject{["rendererEnabled"]=renderer.enabled,["activeInHierarchy"]=renderer.gameObject.activeInHierarchy,["pipelineFamily"]=family,["effectivePipelineAsset"]=pipeline["effectiveAsset"]?.DeepClone(),["materialSlotCount"]=materials.Length,["materialSlotsTruncated"]=materials.Length>4,["materials"]=new JArray(materials.Take(4).Select(m=>MaterialFacts(m,family))),["readOnly"]=true,["renderedOutputVerified"]=false};
        }
        internal static JObject ReviewSceneEvidence(IEnumerable<GameObject> objects)
        {
            var pipeline=PipelineEvidence();string family=(string)pipeline["family"];var source=(objects??Enumerable.Empty<GameObject>()).Where(g=>g&&g.scene.IsValid()).Distinct().Take(2049).ToArray();var renderers=source.Take(2048).Select(g=>g.GetComponent<Renderer>()).Where(r=>r).Take(257).ToArray();var details=new List<JObject>();var cache=new Dictionary<Material,JObject>();
            foreach(var renderer in renderers.Take(256))
            {
                var materials=renderer.sharedMaterials;var slots=new JArray();foreach(var material in materials.Take(4))
                {
                    if(!material){slots.Add(MaterialFacts(null,family));continue;}
                    if(!cache.TryGetValue(material,out var facts)){facts=MaterialFacts(material,family);cache.Add(material,facts);}slots.Add(facts.DeepClone());
                }
                int materialProblems=slots.Count(m=>(bool?)m["compatibility"]?["definitePipelineMismatch"]==true||(bool?)m["compatibility"]?["errorShader"]==true||(string)m["compatibility"]?["status"]=="shader_not_supported");
                bool active=renderer.enabled&&renderer.gameObject.activeInHierarchy;
                details.Add(new JObject{["target"]=ProjectContextIndex.StableTarget(renderer.gameObject),["name"]=Short(renderer.name,80),["enabled"]=renderer.enabled,["activeInHierarchy"]=renderer.gameObject.activeInHierarchy,["participatesInCurrentRendering"]=active,["materialSlotCount"]=materials.Length,["materialSlotsTruncated"]=materials.Length>4,["definiteObservedProblems"]=active?materialProblems:0,["inactiveOnlyMaterialProblems"]=active?0:materialProblems,["materials"]=slots});
            }
            var chosen=new JArray();int budget=0;foreach(var item in details.OrderByDescending(d=>(bool)d["participatesInCurrentRendering"]).ThenByDescending(d=>(int)d["definiteObservedProblems"]).Take(8))
            {int length=item.ToString(Formatting.None).Length;if(budget+length+chosen.Count+2>16000)break;budget+=length;chosen.Add(item);}
            return new JObject{["readOnly"]=true,["actualPixelsInspected"]=false,["pipeline"]=pipeline,["inspectedRendererCount"]=details.Count,["inputScopeTruncated"]=source.Length>2048||renderers.Length>256,["rendererDetailsLimit"]=8,["rendererDetailsCharacterLimit"]=16000,["detailsTruncated"]=chosen.Count<details.Count,["renderersWithDefiniteObservedProblems"]=details.Count(d=>(int)d["definiteObservedProblems"]>0),["inactiveRenderersWithCompatibilityWarnings"]=details.Count(d=>(int)d["inactiveOnlyMaterialProblems"]>0),["renderers"]=chosen,["observedShaderAlternatives"]=Alternatives(family),["alternativesMeaning"]="These Shader.Find results are availability/support observations, not automatic replacements or proof of final appearance. Custom pipelines receive no universal alternative list.",["verificationNote"]="Inactive or disabled renderer material warnings are reported separately and do not establish a current rendering problem. Global diagnostics are observations, not additional user requirements. Mesh geometry, collider tests, shader existence and Shader.isSupported do not prove visual correctness. Check active-pipeline compatibility and capture/inspect actual rendering after material changes."};
        }
    }
}
