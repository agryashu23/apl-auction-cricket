using System;
using System.IO;
using System.Linq;
using Apl.Client.Hall;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apl.Client.EditorTools
{
    /// <summary>
    /// Command-line entry points (Unity -batchmode -executeMethod ...):
    /// Setup → BuildAnimators → BuildHall → Capture → BuildAndroid.
    /// </summary>
    public static class Pipeline
    {
        private const string Out = "../build/unity";

        [MenuItem("APL/1 Setup Project")]
        public static void Setup()
        {
            PlayerSettings.companyName = "APL";
            PlayerSettings.productName = "APL";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.apl.auction");
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // Friend rooms use a raw WebSocket, which Unity's auto-detection may miss.
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 2;
            EnsureUrp();
            TuneUrp();
            AssetDatabase.SaveAssets();
            Debug.Log("[APL] Project set up");
        }

        /// <summary>
        /// Quality for the match: 4x MSAA, HDR (bloom on the floodlights), soft
        /// player shadows out to the broadcast camera's distance, two cascades.
        /// </summary>
        private static void TuneUrp()
        {
            var asset = GraphicsSettings.defaultRenderPipeline;
            if (asset == null) return;
            void Put(string name, object value)
            {
                const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                var p = asset.GetType().GetProperty(name, all);
                try
                {
                    if (p != null && p.SetMethod != null)
                    {
                        p.SetValue(asset, value is int i && p.PropertyType.IsEnum ? Enum.ToObject(p.PropertyType, i) : value);
                        return;
                    }
                    var f = asset.GetType().GetField("m_" + char.ToUpperInvariant(name[0]) + name.Substring(1), all);
                    if (f != null)
                    {
                        f.SetValue(asset, value is int j && f.FieldType.IsEnum ? Enum.ToObject(f.FieldType, j) : value);
                        return;
                    }
                    Debug.Log($"[APL] URP: no {name}");
                }
                catch (Exception e)
                {
                    Debug.Log($"[APL] URP: {name} not set ({e.Message})");
                }
            }
            Put("msaaSampleCount", 4);
            Put("supportsHDR", true);
            Put("shadowDistance", 140f);
            Put("shadowCascadeCount", 2);
            Put("mainLightShadowmapResolution", 2048);
            Put("supportsSoftShadows", true);
            EditorUtility.SetDirty(asset);
        }

        /// <summary>Creates and assigns a URP asset if URP is installed (reflection: no hard dependency).</summary>
        private static void EnsureUrp()
        {
            if (GraphicsSettings.defaultRenderPipeline != null) return;
            var assetType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
            var dataType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRendererData, Unity.RenderPipelines.Universal.Runtime");
            if (assetType == null || dataType == null)
            {
                Debug.Log("[APL] URP not installed; using the built-in pipeline");
                return;
            }
            Directory.CreateDirectory("Assets/Apl/Settings");
            var rendererData = ScriptableObject.CreateInstance(dataType);
            AssetDatabase.CreateAsset(rendererData, "Assets/Apl/Settings/AplRenderer.asset");
            var create = assetType.GetMethod("Create", new[] { Type.GetType("UnityEngine.Rendering.Universal.ScriptableRendererData, Unity.RenderPipelines.Universal.Runtime")! });
            var asset = (RenderPipelineAsset)create!.Invoke(null, new object[] { rendererData })!;
            AssetDatabase.CreateAsset(asset, "Assets/Apl/Settings/AplUrp.asset");
            GraphicsSettings.defaultRenderPipeline = asset;
            QualitySettings.renderPipeline = asset;
        }

        /// <summary>
        /// Every cricket clip retargets through the one T-pose rig's avatar (see
        /// AssetImportRules.CricketRig); re-imports only the clips not already on it.
        /// </summary>
        public static void CricketAvatars()
        {
            AssetDatabase.ImportAsset(AssetImportRules.CricketRig, ImportAssetOptions.ForceSynchronousImport);
            StraightenRig();
            var rig = AssetDatabase.LoadAllAssetsAtPath(AssetImportRules.CricketRig).OfType<Avatar>().FirstOrDefault();
            if (rig == null || !rig.isHuman)
            {
                Debug.LogWarning($"[APL] cricket rig avatar missing at {AssetImportRules.CricketRig}; clips keep their own avatars");
                return;
            }
            // A fingerprint of the rig's zero pose: clips copy the avatar when they import, so they
            // re-import whenever the rig itself changes (and only then).
            var rigImporter = (ModelImporter)AssetImporter.GetAtPath(AssetImportRules.CricketRig);
            var stamp = "rig:" + string.Join(";", rigImporter.humanDescription.skeleton.Select(b => $"{b.name}:{b.rotation.x:0.0000},{b.rotation.y:0.0000},{b.rotation.z:0.0000},{b.rotation.w:0.0000}")).GetHashCode().ToString("x8");
            var changed = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty/Cricket" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) continue;
                    if (mi.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && mi.sourceAvatar == rig && mi.userData == stamp) continue;
                    mi.animationType = ModelImporterAnimationType.Human;
                    mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    mi.sourceAvatar = rig;
                    mi.userData = stamp;
                    mi.SaveAndReimport();
                    changed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            Debug.Log($"[APL] cricket clips on the T-pose rig avatar: {changed} re-imported");
        }

        /// <summary>
        /// The rig avatar's zero pose made dead straight, like the game bodies': spine, chest, neck
        /// and head vertical, legs hanging straight down. Unity measures every pose from the zero,
        /// so the rest pose's ~4° spine and ~10° neck/head lean would otherwise tip every head back
        /// in the game. Edits the skeleton description (what "Enforce T-Pose" does); re-imports the
        /// rig only when something moved.
        /// </summary>
        private static void StraightenRig()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(AssetImportRules.CricketRig);
            var desc = importer.humanDescription;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetImportRules.CricketRig);
            if (prefab == null || desc.skeleton == null || desc.human == null) return;
            var go = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var byName = new System.Collections.Generic.Dictionary<string, Transform>();
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) byName.TryAdd(t.name, t);
                var skeleton = desc.skeleton;
                foreach (var sb in skeleton)
                {
                    if (!byName.TryGetValue(sb.name, out var t) || t == go.transform) continue;
                    t.localPosition = sb.position;
                    t.localRotation = sb.rotation;
                }
                Transform? Bone(string human)
                {
                    foreach (var h in desc.human) if (h.humanName == human) return byName.TryGetValue(h.boneName, out var t) ? t : null;
                    return null;
                }
                var moved = 0f;
                // Parent before child; each bone aimed at its next human bone (the head at its end bone).
                var chain = new[]
                {
                    ("Spine", "Chest", Vector3.up), ("Chest", "UpperChest", Vector3.up), ("UpperChest", "Neck", Vector3.up),
                    ("Neck", "Head", Vector3.up), ("Head", "", Vector3.up),
                    ("LeftUpperLeg", "LeftLowerLeg", Vector3.down), ("LeftLowerLeg", "LeftFoot", Vector3.down),
                    ("RightUpperLeg", "RightLowerLeg", Vector3.down), ("RightLowerLeg", "RightFoot", Vector3.down),
                };
                foreach (var (name, childName, want) in chain)
                {
                    var bone = Bone(name);
                    if (bone == null) continue;
                    var child = childName.Length > 0 ? Bone(childName) : (bone.childCount > 0 ? bone.GetChild(0) : null);
                    if (child == null && childName == "UpperChest") child = Bone("Neck");
                    if (child == null) continue;
                    var have = child.position - bone.position;
                    if (have.sqrMagnitude < 1e-8f) continue;
                    var turn = Quaternion.FromToRotation(have.normalized, want);
                    moved = Mathf.Max(moved, Quaternion.Angle(Quaternion.identity, turn));
                    bone.rotation = turn * bone.rotation;
                }
                if (moved < 0.2f) return;
                for (var i = 0; i < skeleton.Length; i++)
                {
                    if (byName.TryGetValue(skeleton[i].name, out var t) && t != go.transform) skeleton[i].rotation = t.localRotation;
                }
                desc.skeleton = skeleton;
                importer.humanDescription = desc;
                importer.SaveAndReimport();
                Debug.Log($"[APL] cricket rig zero straightened (largest turn {moved:0.0}°)");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Shaders the game asks for by name at runtime (Shader.Find) ship only if a material in a
        /// scene or Resources uses them; the plain unlit one has none, so add it to Always Included.
        /// </summary>
        private static void IncludeShaders()
        {
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            if (settings == null) return;
            var so = new SerializedObject(settings);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in new[] { "Universal Render Pipeline/Unlit", "Sprites/Default" })
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;
                var present = false;
                for (var i = 0; i < list.arraySize; i++) present |= list.GetArrayElementAtIndex(i).objectReferenceValue == shader;
                if (present) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                Debug.Log($"[APL] always-included shader added: {name}");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void BuildEverything()
        {
            Setup();
            IncludeShaders();
            CricketAvatars();
            AnimatorBuilder.BuildAll();
            HallBuilder.Build();
            KitBuilder.Build();
            GroundBuilder.Build();
        }

        /// <summary>Renders every camera shot of the hall to build/unity/screens/*.png, people posed.</summary>
        [MenuItem("APL/Capture Shots")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(HallBuilder.ScenePath);
            // Run each character's real controller for a moment, so stills show the
            // poses the game shows (clip sampling leaves humanoids in T-pose).
            var i = 0;
            foreach (var animator in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                if (animator.runtimeAnimatorController == null) continue;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Update(0f);
                for (var step = 0; step < 12 + (i++ % 7) * 3; step++) animator.Update(1f / 15);
            }

            // Only the human's table glows in the game.
            foreach (var table in UnityEngine.Object.FindObjectsByType<TeamTable>(FindObjectsSortMode.None))
            {
                table.SetHighlight(table.TeamId == "MBH");
            }
            // Async compilation renders placeholders for shaders not compiled yet.
            EditorSettings.asyncShaderCompilation = false;

            var cam = Camera.main!;
            var hall = cam.GetComponent<HallCamera>();
            var dir = Path.Combine(Out, "screens");
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(1600, 900, 24);
            foreach (var shot in hall.Shots)
            {
                cam.transform.position = shot.Position;
                cam.transform.LookAt(shot.LookAt);
                cam.fieldOfView = shot.FieldOfView;
                cam.targetTexture = rt;
                cam.Render();
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, shot.Name + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            cam.targetTexture = null;
            RenderTexture.active = null;
            Debug.Log($"[APL] Captured {hall.Shots.Count} shots to {Path.GetFullPath(dir)}");
        }

        /// <summary>The gavel strike at wind-up and impact, from the auctioneer shot -> build/unity/strike_*.png.</summary>
        public static void CaptureStrike()
        {
            EditorSceneManager.OpenScene(HallBuilder.ScenePath);
            EditorSettings.asyncShaderCompilation = false;
            foreach (var animator in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                if (animator.runtimeAnimatorController == null) continue;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Update(0f);
                for (var step = 0; step < 15; step++) animator.Update(1f / 15);
            }
            var cast = UnityEngine.Object.FindAnyObjectByType<StageCast>();
            var cam = Camera.main!;
            var shot = cam.GetComponent<HallCamera>().Shots.Find(s => s.Name == "auctioneer");
            cam.transform.position = shot.Position;
            cam.transform.LookAt(shot.LookAt);
            cam.fieldOfView = shot.FieldOfView;
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var weight = typeof(StageCast).GetField("_armWeight", flags)!;
            var down = typeof(StageCast).GetField("_armDown", flags)!;
            var swing = typeof(StageCast).GetMethod("SwingArm", flags)!;
            var place = typeof(StageCast).GetMethod("PlaceGavel", flags)!;
            var rt = new RenderTexture(1280, 720, 24);
            foreach (var (name, d) in new[] { ("down", 1f), ("up", 0f) })
            {
                // Fresh animation pose, then the strike on top (as LateUpdate does).
                cast.Auctioneer!.Update(0.001f);
                weight.SetValue(cast, 1f);
                down.SetValue(cast, d);
                swing.Invoke(cast, null);
                place.Invoke(cast, null);
                cam.targetTexture = rt;
                cam.Render();
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(Out, $"strike_{name}.png"), tex.EncodeToPNG());
            }
            cam.targetTexture = null;
            RenderTexture.active = null;
            Debug.Log("[APL] strike captured");
        }

        /// <summary>Contact sheet of painted avatars (first 40 players) -> build/unity/avatars.png.</summary>
        public static void AvatarSheet()
        {
            var data = Apl.Core.Dataset.FromJson(File.ReadAllText("Assets/Resources/Data/apl_dataset.json"));
            const int cell = 192, cols = 8, rows = 5;
            var sheet = new Texture2D(cell * cols, cell * rows, TextureFormat.RGBA32, false);
            for (var i = 0; i < cols * rows && i < data.Players.Count; i++)
            {
                var tex = Apl.Client.Ui.Portraits.For(data.Players[i], cell);
                var x = i % cols * cell;
                var y = (rows - 1 - i / cols) * cell;
                sheet.SetPixels(x, y, cell, cell, tex.GetPixels());
            }
            sheet.Apply();
            Directory.CreateDirectory(Out);
            File.WriteAllBytes(Path.Combine(Out, "avatars.png"), sheet.EncodeToPNG());
            Debug.Log("[APL] Avatar sheet written");
        }

        /// <summary>Windows player for scripted play-throughs (AutoShot): build/unity/win/APL.exe.</summary>
        [MenuItem("APL/Build Windows Player")]
        public static void BuildWindows()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { HallBuilder.ScenePath, GroundBuilder.ScenePath },
                target = BuildTarget.StandaloneWindows64,
                locationPathName = Path.Combine(Out, "win", "APL.exe"),
                options = BuildOptions.Development,
            });
            Debug.Log($"[APL] Windows build: {report.summary.result}, {report.summary.totalErrors} errors");
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        [MenuItem("APL/Build Android APK")]
        public static void BuildAndroid()
        {
            Directory.CreateDirectory(Out);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { HallBuilder.ScenePath, GroundBuilder.ScenePath },
                target = BuildTarget.Android,
                locationPathName = Path.Combine(Out, "APL.apk"),
                options = BuildOptions.None,
            });
            Debug.Log($"[APL] Android build: {report.summary.result}, {report.summary.totalSize / 1048576} MB, {report.summary.totalErrors} errors");
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
