using UnityEditor;

namespace NepFX
{
    /// FBX-анимации из Assets/Idles импортируются как Humanoid, петлёй и без смещения корня.
    public class NepIdleImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').StartsWith("Assets/Idles/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true;
            mi.importBlendShapes = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.Replace('\\', '/').StartsWith("Assets/Idles/")) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = true;
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = false; c.heightFromFeet = true;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
        }
    }
}
