using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Apl.Client.EditorTools
{
    /// <summary>
    /// The hall's finish: generated wall-panel and carpet textures, wainscot
    /// and LED strips, ceiling downlights and stage trusses, wall washes, and
    /// a post-processing volume (bloom, tonemapping, colour) so emissive
    /// surfaces glow. URP types are reached by reflection, so this compiles
    /// without a hard URP reference.
    /// </summary>
    public static class HallDecor
    {
        private const string Folder = "Assets/Apl/Art/Generated";

        // ------------------------------------------------------------ textures

        /// <summary>Recessed panel: deep blue-purple field, bevelled border, gold inlay line.</summary>
        public static Texture2D WallPanel() => Generate("wall_panel", 512, (x, y, n) =>
        {
            var u = x / (float)n;
            var v = y / (float)n;
            var baseC = new Color(0.1f, 0.08f, 0.2f);
            var m = 0.08f;
            var inset = u > m && u < 1 - m && v > m && v < 1 - m;
            if (!inset)
            {
                // Frame with a soft vertical sheen.
                return baseC * (0.85f + 0.15f * Mathf.Sin(v * Mathf.PI));
            }
            // Bevel: lit top/left edge, shaded bottom/right edge.
            var e = Mathf.Min(Mathf.Min(u - m, 1 - m - u), Mathf.Min(v - m, 1 - m - v));
            if (e < 0.018f)
            {
                var lit = (v > 1 - m - 0.018f) || (u < m + 0.018f);
                return lit ? baseC * 1.6f : baseC * 0.55f;
            }
            // Gold inlay line inside the bevel.
            if (e > 0.04f && e < 0.048f) return new Color(0.78f, 0.6f, 0.26f);
            var glow = 1f + 0.12f * (1f - Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.55f)));
            return new Color(0.13f, 0.1f, 0.26f) * glow;
        });

        /// <summary>Maroon carpet with a faint diamond lattice and gold points.</summary>
        public static Texture2D Carpet() => Generate("carpet", 512, (x, y, n) =>
        {
            var u = x / (float)n;
            var v = y / (float)n;
            var baseC = new Color(0.3f, 0.04f, 0.07f);
            // Diamond lattice from two diagonal line families.
            var d1 = Mathf.Abs(Mathf.Repeat(u + v, 0.25f) - 0.125f);
            var d2 = Mathf.Abs(Mathf.Repeat(u - v, 0.25f) - 0.125f);
            var line = Mathf.Min(d1, d2);
            var c = line > 0.118f ? baseC * 1.35f : baseC;
            // Small gold point at each lattice crossing.
            var cx = Mathf.Repeat(u, 0.125f) - 0.0625f;
            var cy = Mathf.Repeat(v, 0.125f) - 0.0625f;
            if (cx * cx + cy * cy < 0.00009f && Mathf.Repeat((u + v) * 4, 1) < 0.5f) c = new Color(0.62f, 0.45f, 0.18f);
            // A little weave noise.
            var noise = Mathf.PerlinNoise(x * 0.35f, y * 0.35f) * 0.12f;
            return c * (0.94f + noise);
        });

        internal static Texture2D Generate(string name, int size, Func<int, int, int, Color> pixel)
        {
            Directory.CreateDirectory(Folder);
            var path = $"{Folder}/{name}.png";
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            var px = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++) px[y * size + x] = pixel(x, y, size);
            }
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------- post effects

        /// <summary>
        /// Global volume: bloom on the glowing bits, tonemapping, colour, soft
        /// vignette. The hall is warm and neutral; the ground (<paramref name="night"/>)
        /// a floodlit broadcast look — filmic tonemapping, bloom round the lights,
        /// punchier greens. Each scene keeps its own profile asset.
        /// </summary>
        public static void PostProcessing(Camera camera, string name = "HallPost", bool night = false)
        {
            var core = "Unity.RenderPipelines.Core.Runtime";
            var urp = "Unity.RenderPipelines.Universal.Runtime";
            var volumeType = Type.GetType($"UnityEngine.Rendering.Volume, {core}");
            var profileType = Type.GetType($"UnityEngine.Rendering.VolumeProfile, {core}");
            if (volumeType == null || profileType == null)
            {
                Debug.Log("[APL] no URP volume types; skipping post effects");
                return;
            }
            Directory.CreateDirectory("Assets/Apl/Settings");
            var path = $"Assets/Apl/Settings/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance(profileType);
            AssetDatabase.CreateAsset(profile, path);
            var add = profileType.GetMethod("Add", new[] { typeof(Type), typeof(bool) })!;

            object? Component(string type)
            {
                var t = Type.GetType($"UnityEngine.Rendering.Universal.{type}, {urp}");
                if (t == null) return null;
                var c = add.Invoke(profile, new object[] { t, true });
                AssetDatabase.AddObjectToAsset((UnityEngine.Object)c!, profile);
                return c;
            }

            var bloom = Component("Bloom");
            Set(bloom, "intensity", night ? 0.9f : 0.7f);
            Set(bloom, "threshold", night ? 1.0f : 0.9f);
            Set(bloom, "scatter", 0.7f);
            var tone = Component("Tonemapping");
            SetEnum(tone, "mode", night ? "ACES" : "Neutral");
            var color = Component("ColorAdjustments");
            Set(color, "postExposure", night ? 0.75f : 0.45f);
            Set(color, "contrast", night ? 16f : 10f);
            Set(color, "saturation", night ? 18f : 12f);
            var vignette = Component("Vignette");
            Set(vignette, "intensity", night ? 0.25f : 0.22f);
            EditorUtility.SetDirty(profile);

            var go = new GameObject("PostFX");
            var volume = go.AddComponent(volumeType);
            volumeType.GetField("isGlobal")?.SetValue(volume, true);
            volumeType.GetField("sharedProfile")?.SetValue(volume, profile);

            var cameraData = Type.GetType($"UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, {urp}");
            if (cameraData != null)
            {
                var data = camera.GetComponent(cameraData) ?? camera.gameObject.AddComponent(cameraData);
                cameraData.GetProperty("renderPostProcessing")?.SetValue(data, true);
            }
        }

        private static void Set(object? component, string field, float value)
        {
            var p = component?.GetType().GetField(field)?.GetValue(component);
            if (p == null) return;
            p.GetType().GetProperty("value")?.SetValue(p, value);
            p.GetType().GetProperty("overrideState")?.SetValue(p, true);
        }

        private static void SetEnum(object? component, string field, string value)
        {
            var p = component?.GetType().GetField(field)?.GetValue(component);
            var prop = p?.GetType().GetProperty("value");
            if (p == null || prop == null) return;
            prop.SetValue(p, Enum.Parse(prop.PropertyType, value));
            p.GetType().GetProperty("overrideState")?.SetValue(p, true);
        }
    }
}
