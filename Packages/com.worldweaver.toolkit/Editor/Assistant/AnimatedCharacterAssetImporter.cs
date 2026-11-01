using System;
using UnityEditor;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Only configures the two immutable, licensed built-in character sources.</summary>
    internal sealed class AnimatedCharacterAssetImporter : AssetPostprocessor
    {
        private bool IsBuiltIn => AnimatedCharacterTools.IsSourceModelPath(assetPath);
        private void OnPreprocessModel()
        {
            if (!IsBuiltIn) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.optimizeGameObjects = false;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsBuiltIn) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                string name = AnimatedCharacterTools.CanonicalClipName(clip.name);
                if (name != "idle" && name != "walk" && name != "sprint" && name != "fall") continue;
                clip.loopTime = true;
                clip.loopPose = true;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }
        public override uint GetVersion() => 1;
    }
}
