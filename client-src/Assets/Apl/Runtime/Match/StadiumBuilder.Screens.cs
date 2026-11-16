using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The sight screens as LED walls: still dark (a batter has to pick the
    /// ball up against them) but showing the APL artwork dimly between a lit
    /// top and bottom strip, instead of a dead black slab in every broadcast shot.
    /// </summary>
    public static partial class StadiumBuilder
    {
        private static Texture2D? _cover;

        private static void SightScreenFaces(Transform root, Mats m, float h)
        {
            if (_cover == null) return;
            var face = new MeshKit();
            var trim = new MeshKit();
            const float w = 26f;
            var fh = Mathf.Min(h - 1.2f, w / 2.59f);
            foreach (var end in new[] { 0f, 180f })
            {
                var inward = -Dir(end);
                var right = -Vector3.Cross(Vector3.up, inward) * (w / 2);
                // Just in front of the box's face toward the middle.
                var c = At(end, 7.5f - 0.42f, h / 2);
                var u = Vector3.up * (fh / 2);
                face.Quad(0, c - right - u, c - right + u, c + right + u, c + right - u);
                foreach (var y in new[] { -h / 2 + 0.35f, h / 2 - 0.35f })
                {
                    var t = c + Vector3.up * y + inward * 0.01f;
                    var s = Vector3.up * 0.12f;
                    trim.Quad(0, t - right - s, t - right + s, t + right + s, t + right - s);
                }
            }
            // Round 9: no artwork — at the far end it read as a second big screen stuck on the logo
            // (rating r8 `41`); a real sight screen is plain black.
            // Round 10: plain black read as missing geometry (r9 `209`); now the venue's own dark screen —
            // navy gradient, LED cabinet seams, a faint motif, its accent frame (tools/venue_art.py).
            var art = VenueTex("screen");
            face.Spawn("SightScreenArt", root, art != null ? m.Glow(new Color(0.8f, 0.8f, 0.85f), 0.55f, art, 0.1f) : m.Lit(new Color(0.05f, 0.07f, 0.14f), smooth: 0.05f));
            trim.Spawn("SightScreenTrim", root, m.Glow(new Color(0.35f, 0.25f, 0.75f), 1.4f));
        }
    }
}
