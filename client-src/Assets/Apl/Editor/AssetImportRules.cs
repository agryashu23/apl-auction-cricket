using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Apl.Client.EditorTools
{
    /// <summary>
    /// Import settings for third-party art, applied automatically:
    /// Rocketbox people and Mixamo motions are both Humanoid (so any motion
    /// retargets onto any person), and textures are sized for phones.
    /// </summary>
    public sealed class AssetImportRules : AssetPostprocessor
    {
        /// <summary>Bump when these rules change so Unity re-imports what they touch.</summary>
        public override uint GetVersion() => 10; // 10 (9 + the bowler walk-back turn): the fielders' start/stop (and travelling turn) clips keep their travel as root motion, from the centre of mass (deep area 1)

        private const string Rocketbox = "Assets/ThirdParty/Rocketbox/";
        private const string Mixamo = "Assets/ThirdParty/Mixamo/";

        /// <summary>Match mode's batting, bowling and fielding clips (same Mixamo skeleton).</summary>
        private const string Cricket = "Assets/ThirdParty/Cricket/";

        private static bool IsMotion(string path) => path.StartsWith(Mixamo) || path.StartsWith(Cricket);

        /// <summary>
        /// The generator's skeleton in its rest T-pose. Every cricket clip copies its avatar:
        /// a clip's own avatar is built from its first pose (knees ~39° bent, head ~28° tipped),
        /// which then counts as "straight" — every clip lost that much knee bend.
        /// </summary>
        public const string CricketRig = "Assets/ThirdParty/CricketRig/mixamo_tpose.fbx";

        /// <summary>Clips that loop (idles, talking); the rest play once.</summary>
        private static readonly string[] Looping =
        {
            "sit_idle", "sit_idle_2", "sit_talk", "stand_idle", "talk_1", "walk", "victory", "photo", "standing_photographer_camera",
            // Round 10: "_loop" isn't at the end of these names, so they imported as one-shots — the openers'
            // jog out froze on its last frame after a second and he glided the rest of the way (the owner's
            // "sliding, not walking", rounds 7 and 10).
            "rhb_run_loop_with_bat", "lhb_run_loop_with_bat",
        };

        private void OnPreprocessModel()
        {
            var importer = (ModelImporter)assetImporter;
            if (assetPath.StartsWith(Rocketbox))
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false;
                importer.importBlendShapes = assetPath.Contains("_facial");
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                importer.meshCompression = ModelImporterMeshCompression.Medium;
                importer.isReadable = false;
            }
            else if (assetPath == CricketRig)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
            }
            else if (IsMotion(assetPath))
            {
                importer.animationType = ModelImporterAnimationType.Human;
                // Cricket clips keep the rig's avatar once Pipeline.CricketAvatars has given it to them.
                var shared = assetPath.StartsWith(Cricket) && importer.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && importer.sourceAvatar != null;
                if (!shared) importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
            }
        }

        /// <summary>Clips that run long: keep just the start.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, float> MaxSeconds = new System.Collections.Generic.Dictionary<string, float>
        {
            ["sit_talk_2"] = 6f,
        };

        private void OnPreprocessAnimation()
        {
            if (!IsMotion(assetPath)) return;
            var importer = (ModelImporter)assetImporter;
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            var clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var c in clips)
            {
                c.name = name;
                c.loopTime = Looping.Contains(name) || name.EndsWith("_loop");
                // Keep characters planted where we put them (seat / podium).
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                // Deep area 1: the fielders' start and stop transitions (fielder_start_*, fielder_stop_*) keep their ground travel
                // as root motion, so the pose stays over the transform like the in-place loops and the planted foot's travel
                // carries him (FootLock); the animators don't apply root motion (CricketCast), so nothing else moves him.
                // Iteration 4 (reviewer B4): our generator keeps the file's root node still and carries the travel in the hips,
                // so root motion measured from the root node would be 0 and the body would walk off its transform; it's taken
                // from the body's centre of mass instead (keepOriginalPositionXZ off), which is where the travel is. Same for
                // the owner's travelling turns (fielder_turn_and_go_*, fielder_walk_turn_*) — their plug-in must let FootLock's
                // free mode carry him, like the starts and stops. (Verify: -aplclipslide … -aplclipfilter fielder_st, hips_travel_m ≈ 0.)
                var rootTravel = name.StartsWith("fielder_start_") || name.StartsWith("fielder_stop_") || name.StartsWith("fielder_turn_and_go") || name.StartsWith("fielder_walk_turn_")
                                 || name.StartsWith("bowler_walkback_turn"); // (iteration 5: the bowler's turn into the walk back travels too)
                c.lockRootPositionXZ = !rootTravel;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = !rootTravel; // (B4: the travelling clips' root from the centre of mass)
                // The cricket clips carry a rest-pose key before frame 1 (arms down):
                // start at frame 1, as their metadata counts (contact / release frames).
                if (assetPath.StartsWith(Cricket) && c.firstFrame < 1f) c.firstFrame = 1f;
                // Long takes are trimmed to one usable beat (30 fps clips).
                if (MaxSeconds.TryGetValue(name, out var max) && c.lastFrame - c.firstFrame > max * 30f)
                {
                    c.lastFrame = c.firstFrame + max * 30f;
                }
            }
            importer.clipAnimations = clips;
        }

        /// <summary>The crowd's sprite sheet and its team-colour mask (tools/gear/crowd_atlas.py).</summary>
        private const string CrowdAtlas = "Assets/Resources/Gear/crowd_atlas.png", CrowdMask = "Assets/Resources/Gear/crowd_mask.png";

        private void OnPreprocessTexture()
        {
            if (assetPath == CrowdAtlas || assetPath == CrowdMask)
            {
                // Alpha-tested people: mips keep each sprite's coverage at the shader's cut-off,
                // so the far rows don't thin out; full size, clamped cells; the mask is linear data.
                var crowd = (TextureImporter)assetImporter;
                crowd.mipmapEnabled = true;
                crowd.mipMapsPreserveCoverage = assetPath == CrowdAtlas;
                crowd.alphaTestReferenceValue = 0.3f;
                crowd.alphaIsTransparency = assetPath == CrowdAtlas;
                crowd.sRGBTexture = assetPath == CrowdAtlas;
                crowd.wrapMode = TextureWrapMode.Clamp;
                crowd.maxTextureSize = 1024;
                crowd.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            if (!assetPath.StartsWith(Rocketbox)) return;
            var importer = (TextureImporter)assetImporter;
            var lower = assetPath.ToLowerInvariant();
            if (lower.Contains("_normal")) importer.textureType = TextureImporterType.NormalMap;
            importer.maxTextureSize = lower.Contains("_head") ? 1024 : 512;
            importer.mipmapEnabled = true;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = importer.maxTextureSize;
            android.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(android);
        }
    }
}
