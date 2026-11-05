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
    /// <summary>Stabilizes only lights introduced or changed by an admitted mutation.
    /// URP ForwardLights otherwise adds serialized UniversalAdditionalLightData
    /// lazily on the next render, after an exact checkpoint was already sealed.</summary>
    internal static class RenderLightInitialization
    {
        const string AdditionalType="UnityEngine.Rendering.Universal.UniversalAdditionalLightData, Unity.RenderPipelines.Universal.Runtime";
        static IEnumerable<Light> Lights()=>ProjectContextIndex.Objects(true).SelectMany(go=>go.GetComponents<Light>()).Where(light=>light);
        static string Signature(Light light)
        {
            var transform=light.transform;var position=transform.position;var rotation=transform.rotation;var scale=transform.lossyScale;
            return new JObject{["componentSerialized"]=EditorJsonUtility.ToJson(light),["worldPosition"]=new JArray(position.x,position.y,position.z),["worldRotation"]=new JArray(rotation.x,rotation.y,rotation.z,rotation.w),["worldScale"]=new JArray(scale.x,scale.y,scale.z),["activeInHierarchy"]=light.gameObject.activeInHierarchy,["layer"]=light.gameObject.layer}.ToString(Formatting.None);
        }
        internal static Dictionary<int,string> Capture()=>Lights().ToDictionary(light=>light.GetInstanceID(),Signature);
        internal static bool ActiveUrp()
        {
            // Match the configuration that the next render will use, including a
            // quality override; a previously instantiated pipeline can be stale.
            var quality=QualitySettings.renderPipeline;var asset=quality?quality:GraphicsSettings.defaultRenderPipeline;
            if(!asset)return false;
            for(var type=asset.GetType();type!=null;type=type.BaseType)
                if(type.FullName=="UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset")return true;
            return false;
        }
        internal static void InitializeChanged(IReadOnlyDictionary<int,string> before)
        {
            if(before==null)throw new ArgumentNullException(nameof(before));
            if(!ActiveUrp())return;
            var dataType=Type.GetType(AdditionalType);
            if(dataType==null||!typeof(Component).IsAssignableFrom(dataType))throw new NotSupportedException("The active URP additional-light-data component type could not be loaded.");
            foreach(var light in Lights())
            {
                if(before.TryGetValue(light.GetInstanceID(),out string old)&&old==Signature(light))continue;
                if(light.GetComponent(dataType))continue;
                if(!Undo.AddComponent(light.gameObject,dataType))throw new InvalidOperationException("Could not initialize rendering data for an admitted light mutation.");
            }
        }
    }
}
