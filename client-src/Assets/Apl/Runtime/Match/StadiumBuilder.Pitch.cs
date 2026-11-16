using UnityEngine;
using PitchType = Apl.Core.Pitch;

namespace Apl.Client.Match
{
    /// <summary>
    /// A pitch that's been played on: the scene's clean strip gets a texture
    /// with the batters' guard scuffs at the popping creases, the bowlers'
    /// landing holes and follow-through streaks, rough outside the off stumps
    /// at both ends, ball marks on a length, a polished middle and green
    /// edges. Same mapping as the scene's (u across, v along the 22 m block).
    /// Owner v26 #23 ("every ground's pitch looks the same"): four looks, one per pitch type —
    /// GREEN (seaming: grass on it, mown stripes, little wear), DRY (turning: pale dust, a
    /// crazed web of cracks, deep rough and footmarks), FLAT (a hard, polished, pale belter)
    /// and BALANCED (the two-tone worn strip: a rolled light middle, darker played-on ends).
    /// The ground's default is built with the stadium; the match's own pitch (QuickMatch.Pitch,
    /// varied per match) replaces it once the match is made (<see cref="PitchFor"/>).
    /// </summary>
    public static partial class StadiumBuilder
    {
        private const float PitchLength = 22f, PitchWidth = 3.05f;

        /// <summary>The look on the strip now (the toss card and the test log read it).</summary>
        public static PitchType PitchShown { get; private set; }

        private static void PitchWear() => PitchFor(_v != null ? _v.DefaultPitch : PitchType.Balanced);

        /// <summary>Re-dress the strip for <paramref name="type"/> (cached per type).</summary>
        public static void PitchFor(PitchType type)
        {
            var pitch = GameObject.Find("Pitch");
            var r = pitch != null ? pitch.GetComponent<Renderer>() : null;
            if (r == null) return;
            PitchShown = type;
            var mat = r.material;
            mat.SetTexture("_BaseMap", PitchTexture(type));
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetTextureOffset("_BaseMap", Vector2.zero);
            // A belter shines, a green top is soft, a dust bowl is matt.
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", type == PitchType.Flat ? 0.42f : type == PitchType.SeamFriendly ? 0.18f : type == PitchType.SpinFriendly ? 0.05f : 0.2f);
            if (Debug.isDebugBuild) Debug.Log($"[APL pitch] strip dressed as {type}");
        }

        private static float Noise(float x, float y) => Mathf.PerlinNoise(x, y);

        /// <summary>Darkness of the wear at (x across, d metres from the stumps line toward the middle), 0..1.</summary>
        private static float Wear(float x, float d, int end)
        {
            var ax = Mathf.Abs(x);
            var seed = end * 37.1f;
            var w = 0f;
            // The batter's guard and shuffle: a scuffed patch round the popping crease, middle stump line.
            w = Mathf.Max(w, Bump(ax, 0f, 0.45f) * Bump(d, 1.25f, 0.55f) * (0.5f + 0.5f * Noise(x * 9 + seed, d * 9)));
            // Bowlers' front-foot landings just behind the popping crease, either side of the stumps.
            var landing = Bump(ax, 0.55f, 0.35f) * Bump(d, 0.95f, 0.45f);
            w = Mathf.Max(w, landing * Mathf.Clamp01(Noise(x * 14 + seed, d * 14) * 1.8f - 0.35f));
            // Follow-through streaks running down the pitch off the line of the stumps.
            var follow = Bump(ax, 0.9f, 0.3f) * Mathf.Clamp01(1 - Mathf.Abs(d - 3.2f) / 2.4f);
            w = Mathf.Max(w, follow * 0.6f * Mathf.Clamp01(Noise(x * 30 + seed, d * 2.5f) * 1.6f - 0.4f));
            // Rough outside the off stump (both sides: both hands bat).
            w = Mathf.Max(w, Bump(ax, 0.55f, 0.3f) * Bump(d, 2.3f, 0.9f) * Mathf.Clamp01(Noise(x * 18 + seed, d * 18) * 2f - 0.8f));
            // Ball marks on a good length, between 4 and 8 m from the batter.
            var spot = Noise(x * 22 + seed, d * 22);
            if (ax < 0.45f && d > 3.5f && d < 8f && spot > 0.74f) w = Mathf.Max(w, (spot - 0.74f) * 2.6f);
            return Mathf.Clamp01(w);
        }

        private static float Bump(float v, float centre, float half) => Mathf.Clamp01(1 - Mathf.Abs(v - centre) / half);

        /// <summary>
        /// Cracks: the edges of an irregular cell pattern (cells ~<paramref name="cell"/> m), 1 on a crack.
        /// Wide enough at 42 px/m to read as lines, not the old contour "doodles".
        /// </summary>
        private static float Crack(float x, float z, float cell, float width)
        {
            var gx = Mathf.FloorToInt(x / cell);
            var gz = Mathf.FloorToInt(z / cell);
            float f1 = float.MaxValue, f2 = float.MaxValue;
            for (var i = -1; i <= 1; i++)
            for (var j = -1; j <= 1; j++)
            {
                var cx = (gx + i + 0.15f + 0.7f * Hash(gx + i, gz + j)) * cell;
                var cz = (gz + j + 0.15f + 0.7f * Hash(gz + j + 911, gx + i + 37)) * cell;
                var dd = (x - cx) * (x - cx) + (z - cz) * (z - cz);
                if (dd < f1)
                {
                    f2 = f1;
                    f1 = dd;
                }
                else if (dd < f2) f2 = dd;
            }
            var edge = Mathf.Sqrt(f2) - Mathf.Sqrt(f1);
            return Mathf.Clamp01(1 - edge / width);
        }

        private static Texture2D PitchTexture(PitchType type) =>
            Tex("pitch_" + type, 128, 1024, (px, py) =>
            {
                var u = (px + 0.5f) / 128f;
                var v = (py + 0.5f) / 1024f;
                var x = (u - 0.5f) * PitchWidth;
                var z = (v - 0.5f) * PitchLength;
                var d = GroundSpace.HalfPitch - Mathf.Abs(z);
                var end = z < 0 ? 0 : 1;
                var wear = d > -1.5f ? Wear(x, d, end) : 0;
                var edge = Mathf.Clamp01((Mathf.Abs(x) - 1.22f) / 0.3f);
                var beyond = Mathf.Clamp01((-d - 0.6f) / 0.4f);
                var grain = Hash(px, py) * 0.07f;
                Color c;
                switch (type)
                {
                    case PitchType.SeamFriendly:
                    {
                        // A green top: grass all over the clay, mown in two-tone stripes along the strip,
                        // bare only where the batters take guard and the bowlers land.
                        var clay = new Color(0.66f, 0.6f, 0.42f);
                        var stripe = Mathf.Repeat(z / 1.1f, 2f) < 1f ? 0.06f : -0.04f;
                        var grass = Color.Lerp(new Color(0.42f, 0.55f, 0.27f), new Color(0.52f, 0.63f, 0.33f), Noise(x * 7, z * 7)) * (1 + stripe);
                        var cover = 0.78f + 0.2f * Noise(x * 3 + 5, z * 3);
                        c = Color.Lerp(clay, grass, cover * (1 - wear * 0.8f));
                        c = Color.Lerp(c, new Color(0.56f, 0.47f, 0.32f), wear * 0.55f);
                        c = Color.Lerp(c, new Color(0.38f, 0.52f, 0.24f), Mathf.Max(edge, beyond) * 0.8f);
                        break;
                    }
                    case PitchType.SpinFriendly:
                    {
                        // A dust bowl: pale, dry, a crazed web of cracks, deep rough and footmarks at both ends.
                        var dust = Color.Lerp(new Color(0.9f, 0.82f, 0.62f), new Color(0.84f, 0.74f, 0.53f), Noise(x * 1.6f, z * 0.5f));
                        c = dust;
                        var crack = Crack(x, z, 0.32f, 0.018f) * (0.55f + 0.45f * Noise(x * 4 + 3, z * 4));
                        c = Color.Lerp(c, new Color(0.46f, 0.37f, 0.25f), crack * 0.85f);
                        // The rough: wide patches outside both off stumps, scuffed and dark.
                        var rough = Bump(Mathf.Abs(x), 0.62f, 0.42f) * Bump(d, 2.4f, 1.6f) * Mathf.Clamp01(Noise(x * 11 + end * 9, d * 11) * 2.2f - 0.6f);
                        c = Color.Lerp(c, new Color(0.55f, 0.44f, 0.29f), Mathf.Max(wear, rough) * 0.9f);
                        // Loose dust puffs on a length.
                        c = Color.Lerp(c, new Color(0.95f, 0.88f, 0.7f), Mathf.Clamp01(Noise(x * 16, z * 16) - 0.7f) * 1.4f);
                        c = Color.Lerp(c, new Color(0.62f, 0.6f, 0.36f), Mathf.Max(edge, beyond) * 0.55f);
                        break;
                    }
                    case PitchType.Flat:
                    {
                        // A belter: hard, rolled, almost white down the middle, hardly a mark on it.
                        var hard = Color.Lerp(new Color(0.93f, 0.87f, 0.7f), new Color(0.88f, 0.81f, 0.63f), Noise(x * 0.9f, z * 0.3f));
                        c = Color.Lerp(hard, new Color(0.97f, 0.93f, 0.8f), Bump(x, 0, 0.9f) * 0.45f);
                        c = Color.Lerp(c, new Color(0.7f, 0.6f, 0.43f), wear * 0.35f);
                        c = Color.Lerp(c, new Color(0.5f, 0.6f, 0.32f), Mathf.Max(edge, beyond) * (0.5f + 0.25f * Noise(x * 6, z * 6)));
                        grain *= 0.5f;
                        break;
                    }
                    default:
                    {
                        // Balanced, two-tone worn: a lighter rolled band down the middle, darker played-on ends.
                        var clay = Color.Lerp(new Color(0.84f, 0.75f, 0.54f), new Color(0.78f, 0.68f, 0.48f), Noise(x * 1.2f, z * 0.35f));
                        clay = Color.Lerp(clay, new Color(0.9f, 0.82f, 0.62f), Bump(x, 0, 0.6f) * Bump(Mathf.Abs(z), 0, 6.5f) * 0.45f);
                        clay = Color.Lerp(clay, new Color(0.72f, 0.62f, 0.43f), Mathf.Clamp01((Mathf.Abs(z) - 5f) / 3f) * 0.35f);
                        clay = Color.Lerp(clay, new Color(0.6f, 0.49f, 0.33f), wear * 0.85f);
                        var green = Mathf.Max(edge, beyond) * (0.55f + 0.3f * Noise(x * 6, z * 6));
                        c = Color.Lerp(clay, new Color(0.42f, 0.53f, 0.27f), green);
                        break;
                    }
                }
                return c * (0.96f + grain);
            });
    }
}
