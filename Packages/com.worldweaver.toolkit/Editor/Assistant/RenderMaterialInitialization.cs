using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Initialize only materials introduced or edited by an admitted operation.
    /// URP otherwise initializes legacy aliases/tags lazily during Play/rendering,
    /// changing inline scene serialization after an exact checkpoint was sealed.</summary>
    public static class RenderMaterialInitialization
    {
        static Material[] Materials()=>ProjectContextIndex.Objects().SelectMany(g=>g.GetComponents<Renderer>()).SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct().ToArray();
        public static Dictionary<int,string> Capture()=>Materials().ToDictionary(m=>m.GetInstanceID(),m=>EditorJsonUtility.ToJson(m));
        public static void InitializeChanged(IReadOnlyDictionary<int,string> before)
        {
            foreach(var material in Materials())
            {
                if(before.TryGetValue(material.GetInstanceID(),out string previous)&&previous==EditorJsonUtility.ToJson(material))continue;
                string path=AssetDatabase.GetAssetPath(material);
                // Referencing an existing user/package asset does not authorize editing it.
                if(!string.IsNullOrEmpty(path)&&!path.StartsWith("Assets/WorldWeaverGenerated/",StringComparison.Ordinal))continue;
                string type=material.shader?.name=="Universal Render Pipeline/Lit"?"LitShader":material.shader?.name=="Universal Render Pipeline/Simple Lit"?"SimpleLitShader":null;
                if(type==null)continue;
                var guiType=Type.GetType("UnityEditor.Rendering.Universal.ShaderGUI."+type+", Unity.RenderPipelines.Universal.Editor");
                if(guiType==null)throw new NotSupportedException("The installed URP ShaderGUI could not be loaded to initialize a generated material.");
                Undo.RecordObject(material,"Initialize generated pipeline material");
                ((ShaderGUI)Activator.CreateInstance(guiType,true)).ValidateMaterial(material);
                if(!string.IsNullOrEmpty(path)){EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);}
            }
        }
    }
}
