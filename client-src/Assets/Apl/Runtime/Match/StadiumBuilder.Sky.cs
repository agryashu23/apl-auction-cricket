using System;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Round 8, the roof and the night sky (rating r7: "a grey band with a jagged
    /// dark edge", "flat lighting, no floodlight glow"): tensile roof cloth with
    /// seams and cables, a light strip along every roof's lip, glare halos on the
    /// floodlight heads.
    /// </summary>
    public static partial class StadiumBuilder
    {
        /// <summary>Tensile fabric from below: lit white cloth, a seam every panel, a darker cable down each.</summary>
        private static Texture2D Fabric() =>
            Tex("roof_fabric", 64, 64, (x, y) =>
            {
                var v = y / 63f;
                // Brighter toward the lip (the floodlights wash it), greyer toward the back.
                var c = Color.Lerp(new Color(0.62f, 0.64f, 0.7f), new Color(0.98f, 0.98f, 1f), v);
                if (x == 0 || x == 32) c *= 0.7f;           // seams
                if (Mathf.Abs(x - 16) < 1 || Mathf.Abs(x - 48) < 1) c *= 0.82f; // cables
                if (y > 60) c = new Color(0.82f, 0.82f, 0.86f); // the lip's rolled edge (light: the old dark one read as jagged)
                return c;
            });

        /// <summary>A soft round glow, white in the middle, clear at the edge (premultiplied look on the cutout shader).</summary>
        private static Texture2D Halo() =>
            Tex("halo", 64, 64, (x, y) =>
            {
                var dx = (x - 31.5f) / 31.5f;
                var dy = (y - 31.5f) / 31.5f;
                var r = Mathf.Sqrt(dx * dx + dy * dy);
                var a = Mathf.Pow(Mathf.Clamp01(1 - r), 2.2f);
                return new Color(1f, 0.97f, 0.88f, a);
            });

        /// <summary>
        /// Glare round each lamp head (two crossed quads, so it reads from any side)
        /// and a light strip along the lip of roofs that have no built-in lights.
        /// </summary>
        private static void Floodlit(Transform root, Mats m, StadiumLook look, float back, float top, Func<float, bool> roofSkip, float roofLip, float lipY)
        {
            var halos = new MeshKit();
            if (look.Towers)
            {
                var height = top + 34;
                foreach (var a in new[] { 42f, 138f, 222f, 318f })
                {
                    var c = At(a, back + 12, height - 2) - Dir(a) * 2f + Vector3.up * 3.5f;
                    Cross(halos, c, 15f);
                }
            }
            if (look.Roof != RoofKind.Open && look.Roof != RoofKind.Ring)
            {
                var strip = new MeshKit();
                Ring(strip, 0, _ => new[] { new Vector2(roofLip + 0.2f, lipY - 0.25f), new Vector2(roofLip + 1.6f, lipY - 0.05f) }, roofSkip, 4);
                strip.Spawn("RoofLipLights", root, m.Glow(new Color(1f, 0.97f, 0.9f), 3.2f));
                // A halo every 20° along the lip: the lamps in the roof.
                for (var a = 5f; a < 360; a += 20)
                    if (!roofSkip(a)) Cross(halos, At(a, roofLip + 0.8f, lipY - 0.6f), 4.5f);
            }
            if (!halos.Empty)
            {
                var glare = GlareMat(Halo(), 0.9f);
                if (glare != null) halos.Spawn("FloodGlare", root, glare);
            }
            // (Round 9: the haze band is gone — it turned the whole sky a flat brown-grey, rating r8 `63`.
            // The floodlit glow is in the night sky's horizon now, StadiumBuilder.NightSky.)
        }

        /// <summary>An additive glow material (Resources/Shaders/Glare), or null if the shader isn't in the build.</summary>
        private static Material? GlareMat(Texture2D tex, float intensity)
        {
            var shader = Resources.Load<Shader>("Shaders/Glare") ?? Shader.Find("Apl/Glare");
            if (shader == null)
            {
                Debug.LogWarning("[APL] Apl/Glare shader missing: no floodlight glare");
                return null;
            }
            var mat = new Material(shader) { name = "Glare_" + tex.name };
            mat.SetTexture("_MainTex", tex);
            mat.SetFloat("_Intensity", intensity);
            return mat;
        }

        /// <summary>Two crossed upright quads of <paramref name="size"/> m centred on <paramref name="c"/> (both faces).</summary>
        private static void Cross(MeshKit k, Vector3 c, float size)
        {
            var h = size / 2;
            foreach (var d in new[] { Vector3.right, Vector3.forward, (Vector3.right + Vector3.forward).normalized, (Vector3.right - Vector3.forward).normalized })
            {
                var r = d * h;
                var u = Vector3.up * h;
                k.Quad(0, c - r - u, c - r + u, c + r + u, c + r - u, Vector2.zero, Vector2.one);
                k.Quad(0, c + r - u, c + r + u, c - r + u, c - r - u, Vector2.zero, Vector2.one);
            }
        }
    }
}
