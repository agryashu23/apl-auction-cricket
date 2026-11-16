using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The stumps and bails, synthesised (no licence to track): a hard ball into
    /// ash — a sharp crack and the hollow ring of the struck stump with its
    /// neighbours knocking — and the bails: light wooden clicks bouncing and
    /// rattling to a stop on the turf.
    /// </summary>
    public static class StumpSounds
    {
        private const int Rate = 44100;

        public static AudioClip Stumps()
        {
            var rng = new System.Random(11);
            return Make("stumps_hit", 0.7f, t =>
            {
                float Knock(float at, float freq, float decay, float amp)
                {
                    var u = t - at;
                    if (u < 0) return 0;
                    return amp * Mathf.Exp(-u * decay) * (Mathf.Sin(2 * Mathf.PI * freq * u) + 0.45f * Mathf.Sin(2 * Mathf.PI * freq * 2.71f * u));
                }
                var crack = t < 0.012f ? (float)(rng.NextDouble() * 2 - 1) * (1 - t / 0.012f) : 0;
                return 0.9f * crack + Knock(0, 620, 34, 0.55f) + Knock(0.004f, 1340, 60, 0.25f)
                    + Knock(0.055f, 540, 40, 0.3f) + Knock(0.11f, 700, 45, 0.2f);
            });
        }

        public static AudioClip Bails()
        {
            var rng = new System.Random(5);
            // Bounces: each shorter and softer, two bails slightly out of step.
            var hits = new[] { 0f, 0.13f, 0.21f, 0.27f, 0.31f, 0.06f, 0.17f, 0.25f };
            var amps = new[] { 0.6f, 0.4f, 0.28f, 0.18f, 0.1f, 0.5f, 0.32f, 0.2f };
            return Make("bails", 0.45f, t =>
            {
                var s = 0f;
                for (var i = 0; i < hits.Length; i++)
                {
                    var u = t - hits[i];
                    if (u < 0 || u > 0.05f) continue;
                    var f = 2100f + 400f * (i % 3);
                    s += amps[i] * Mathf.Exp(-u * 120f) * (Mathf.Sin(2 * Mathf.PI * f * u) + 0.3f * (float)(rng.NextDouble() * 2 - 1));
                }
                return s;
            });
        }

        private static AudioClip Make(string name, float seconds, System.Func<float, float> sample)
        {
            var n = (int)(Rate * seconds);
            var data = new float[n];
            for (var i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i / (float)Rate), -1, 1);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
