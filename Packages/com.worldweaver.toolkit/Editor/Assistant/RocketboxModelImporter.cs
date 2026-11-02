// Adapted from Microsoft-Rocketbox Assets/Editor/FixRocketboxMaxImport.cs
// commit 0943055db6ec570bcef9f2c8b41c9e5467c808f9, MIT, Copyright (c) 2020 Microsoft.
// See ThirdPartyNotices/Microsoft-Rocketbox-MIT.txt. Scoped to our acquired library.
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    internal sealed class RocketboxModelImporter:AssetPostprocessor
    {
        bool InLibrary=>assetPath.StartsWith(ModelAssetTools.Root,StringComparison.Ordinal);
        void OnPreprocessTexture()
        {
            if(!InLibrary)return;var texture=(TextureImporter)assetImporter;texture.maxTextureSize=2048;
            texture.textureType=TextureImporterType.Default;texture.sRGBTexture=true;texture.alphaIsTransparency=assetPath.Contains("opacity");
        }
        void OnPostprocessMeshHierarchy(GameObject model)
        {if(InLibrary&&model.name.ToLowerInvariant().Contains("poly")&&!model.name.ToLowerInvariant().Contains("hipoly"))model.SetActive(false);}
        void OnPostprocessMaterial(Material material)
        {
            if(!InLibrary)return;var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");if(shader)material.shader=shader;material.color=Color.white;
            string folder=assetPath.Substring(0,assetPath.LastIndexOf("/Export/",StringComparison.Ordinal));
            string name=material.name.ToLowerInvariant();
            var textures=AssetDatabase.FindAssets("t:Texture2D",new[]{folder+"/Textures"}).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p=>p.Contains("_color.")).ToArray();
            var matches=textures.Where(p=>{string key=System.IO.Path.GetFileNameWithoutExtension(p).Replace("_color","").Split('_').Last().ToLowerInvariant();return name.Contains(key);}).ToArray();
            if(matches.Length==1)
            {
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(matches[0]);material.mainTexture=texture;if(material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",texture);
                if(material.HasProperty("_MainTex"))material.SetTexture("_MainTex",texture);
                material.mainTextureScale=Vector2.one;material.mainTextureOffset=Vector2.zero;
                if(matches[0].Contains("opacity")){if(material.HasProperty("_AlphaClip"))material.SetFloat("_AlphaClip",1);if(material.HasProperty("_Cutoff"))material.SetFloat("_Cutoff",.35f);material.EnableKeyword("_ALPHATEST_ON");material.renderQueue=2450;}
            }
            if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",.2f);
        }
        void OnPostprocessModel(GameObject model)
        {
            if(!InLibrary)return;
            foreach(var node in model.GetComponentsInChildren<Transform>(true))node.name=node.name.Replace("Bip02","Bip01");
            var bones=model.GetComponentsInChildren<Transform>(true);
            Transform Bone(string name)=>bones.FirstOrDefault(b=>b.name=="Bip01 "+name);
            var pelvis=Bone("Pelvis");var chest=Bone("Spine2");if(!pelvis||!chest)return;
            foreach(string side in new[]{"L","R"})
            {
                var thigh=Bone(side+" Thigh");if(thigh)thigh.SetParent(pelvis,true);
                var clavicle=Bone(side+" Clavicle");if(clavicle)clavicle.SetParent(chest,true);
            }
            var left=Bone("L Clavicle");var right=Bone("R Clavicle");
            if(left)left.rotation=new Quaternion(-.7215106f,0,0,.6924035f);
            if(right)right.rotation=new Quaternion(0,-.6925546f,.721365f,0);
            foreach(string side in new[]{"L","R"}){var arm=Bone(side+" UpperArm");if(arm)arm.rotation=Quaternion.identity;}
            var mapping=new[]{("Hips","Pelvis"),("Spine","Spine"),("Chest","Spine1"),("UpperChest","Spine2"),("Neck","Neck"),("Head","Head"),
                ("LeftShoulder","L Clavicle"),("RightShoulder","R Clavicle"),("LeftUpperArm","L UpperArm"),("RightUpperArm","R UpperArm"),
                ("LeftLowerArm","L Forearm"),("RightLowerArm","R Forearm"),("LeftHand","L Hand"),("RightHand","R Hand"),
                ("LeftUpperLeg","L Thigh"),("RightUpperLeg","R Thigh"),("LeftLowerLeg","L Calf"),("RightLowerLeg","R Calf"),
                ("LeftFoot","L Foot"),("RightFoot","R Foot"),("LeftToes","L Toe0"),("RightToes","R Toe0")};
            var importer=(ModelImporter)assetImporter;
            importer.humanDescription=new HumanDescription{
                human=mapping.Where(m=>Bone(m.Item2)).Select(m=>new HumanBone{humanName=m.Item1,boneName=Bone(m.Item2).name,limit=new HumanLimit{useDefaultValues=true}}).ToArray(),
                skeleton=model.GetComponentsInChildren<Transform>(true).Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray(),
                armStretch=.05f,legStretch=.05f,upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,feetSpacing=0,hasTranslationDoF=false};
        }
    }
}
