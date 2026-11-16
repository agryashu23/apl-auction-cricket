using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The team cap's cloth, painted (white, tinted by the team colour) on the cap's
    /// UVs (tools/gear/cap.py): v 0–0.5 is the crown — u round from the back, the
    /// six panel seams at u = k/6 with twin top-stitching, an eyelet in each panel,
    /// the stitched sweatband at the rim — and v 0.5–1 the peak, with its rows of
    /// stitching. Without these the plain dome read as a hard hat (rating r7, `07`).
    /// The texel at (0, 0) stays plain cloth: the sun hat shares the cap's material
    /// and has no UVs.
    /// </summary>
    public sealed partial class CricketCast
    {
        private static Texture2D? _capCloth;

        private static Texture2D CapCloth()
        {
            if (_capCloth != null) return _capCloth;
            const int n = 256;
            var px = new Color[n * n];
            var rng = new System.Random(7);
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var u = (x + 0.5f) / n;
                var v = (y + 0.5f) / n;
                // Twill: a faint diagonal weave and grain.
                var k = 0.95f + 0.03f * Mathf.Sin((x + y) * 1.7f) + 0.02f * (float)rng.NextDouble();
                if (v < 0.5f) k *= Crown(u, v * 2, n);
                else k *= Peak(u, (v - 0.5f) * 2, n);
                px[y * n + x] = new Color(k, k, k, 1);
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "cap_cloth", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            tex.SetPixels(px);
            tex.Apply(true);
            return _capCloth = tex;
        }

        /// <summary>The crown at (u round, t from the button 0 to the rim 1).</summary>
        private static float Crown(float u, float t, int n)
        {
            var k = 1f;
            // Panel seams (none under the button): a dark crease with a stitch line either side.
            var d = Mathf.Abs(u * 6 - Mathf.Round(u * 6)) / 6 * n; // texels from the nearest seam
            if (t > 0.06f)
            {
                if (d < 1.1f) k *= 0.55f;
                else if (d > 2.6f && d < 3.6f && Dash(t * 90)) k *= 0.78f;
                // The panels bulge: a soft shade toward each seam.
                k *= 1f - 0.08f * Mathf.Clamp01(1 - d / 10f);
            }
            // Eyelets: a stitched ring with a dark hole, a third of the way down each panel.
            var pu = (u * 6 - Mathf.Floor(u * 6) - 0.5f) / 6 * n;
            var pv = (t - 0.3f) * n * 0.5f;
            var r = Mathf.Sqrt(pu * pu + pv * pv);
            if (r < 1.3f) k *= 0.3f;
            else if (r < 2.4f) k *= 0.85f;
            // The sweatband at the rim: two rows of stitching and a shadow under the edge.
            if (t > 0.9f && t < 0.92f && Dash(u * 400)) k *= 0.75f;
            if (t > 0.96f) k *= 0.82f;
            return k;
        }

        /// <summary>The peak at (u across, t from the crown 0 to the tip 1): eight rows of stitching.</summary>
        private static float Peak(float u, float t, int n)
        {
            var k = 1f;
            for (var row = 0; row < 8; row++)
            {
                var at = 0.18f + row * 0.095f;
                if (Mathf.Abs(t - at) * n * 0.5f < 0.6f && Dash(u * 300)) k *= 0.8f;
            }
            if (t > 0.97f) k *= 0.85f;
            return k;
        }

        /// <summary>A stitch: on for two thirds of each step along the line.</summary>
        private static bool Dash(float along) => along - Mathf.Floor(along) < 0.66f;
    }
}
