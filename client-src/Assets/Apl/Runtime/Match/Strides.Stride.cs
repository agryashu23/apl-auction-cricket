using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// No foot slide: each walk / jog / run loop's own ground speed is measured
    /// from its feet (an in-place loop's planted foot travels back under the body
    /// at exactly the speed the body should travel forward), once per clip, so
    /// the transform can be carried at the speed the feet say — or the loop
    /// played faster or slower to match the speed it's carried at.
    /// Round 10 (the owner, rounds 7 and 10: "the batter slides"): measured per body
    /// and in world metres. It was cached per clip from whoever walked first and read in
    /// the transform's scaled space, so a taller, broader or longer-legged batter (bodies
    /// differ, CricketCast.Looks scales them ±10 %) was carried at the wrong speed.
    /// </summary>
    public static partial class Strides
    {
        private static readonly Dictionary<string, float> Measured = new Dictionary<string, float>();

        /// <summary>
        /// Metres a second <paramref name="clip"/> covers at animator speed 1, from its feet;
        /// <paramref name="fallback"/> (the pack's figure) if it can't be measured.
        /// </summary>
        public static float StrideSpeed(Animator a, string clip, float fallback)
        {
            var key = a == null ? clip : $"{clip}|{a.avatar?.name}|{a.transform.lossyScale.x:0.000}|{a.transform.lossyScale.z:0.000}";
            if (Measured.TryGetValue(key, out var known)) return known;
            if (a == null || !a.isHuman || !Body.Has(a, clip))
            {
                if (a != null && Measured.TryAdd("missing|" + clip, fallback)) Debug.LogWarning($"[APL strides] {clip}: not on {a.name}'s animator, pace {fallback:0.00} m/s unmatched");
                return fallback;
            }
            // (Deep area 1, RC1: a play asked this frame is applied first, so the state put back is the one he's in.)
            a.Update(0f);
            var was = a.GetCurrentAnimatorStateInfo(0);
            var speed = a.speed;
            var mirror = a.GetBool(MirrorHash);
            a.speed = 1f;
            a.SetBool(MirrorHash, false);
            a.Play(clip, 0, 0f);
            a.Update(0f);
            var length = a.GetCurrentAnimatorStateInfo(0).length;
            var result = fallback;
            if (length > 0.1f)
            {
                const int n = 48;
                var feet = new[] { a.GetBoneTransform(HumanBodyBones.LeftFoot), a.GetBoneTransform(HumanBodyBones.RightFoot) };
                if (feet[0] != null && feet[1] != null)
                {
                    var pos = new Vector3[2, n + 1];
                    var low = float.MaxValue;
                    for (var i = 0; i <= n; i++)
                    {
                        a.Play(clip, 0, i / (float)n);
                        a.Update(0f);
                        for (var f = 0; f < 2; f++)
                        {
                            // World metres in the body's own frame (InverseTransformPoint would divide by his scale).
                            pos[f, i] = Quaternion.Inverse(a.transform.rotation) * (feet[f].position - a.transform.position);
                            low = Mathf.Min(low, pos[f, i].y);
                        }
                    }
                    // The planted foot: the lower one, near the ground; its slide back is the ground speed.
                    var dt = length / n;
                    var sum = 0f;
                    var count = 0;
                    for (var i = 0; i < n; i++)
                    {
                        var f = pos[0, i].y <= pos[1, i].y ? 0 : 1;
                        if (pos[f, i].y > low + 0.05f || pos[f, i + 1].y > low + 0.05f) continue;
                        var d = pos[f, i + 1] - pos[f, i];
                        d.y = 0;
                        sum += d.magnitude / dt;
                        count++;
                    }
                    if (count >= n / 6)
                    {
                        var v = sum / count;
                        if (v > 0.4f && v < 10f) result = v;
                    }
                }
            }
            a.SetBool(MirrorHash, mirror);
            a.Play(was.fullPathHash, 0, was.normalizedTime);
            a.Update(0f);
            a.speed = speed;
            Measured[key] = result;
            if (Debug.isDebugBuild) Debug.Log($"[APL strides] {key}: {result:0.00} m/s (pack says {fallback:0.00})");
            return result;
        }

        private static readonly int MirrorHash = Animator.StringToHash("Mirror");

        /// <summary>
        /// The animator speed for carrying him at <paramref name="pace"/> in <paramref name="clip"/>
        /// (kept within what still looks like the same gait), and the pace that then matches his feet exactly.
        /// </summary>
        public static (float animSpeed, float pace) Match(Animator a, string clip, float pace, float fallback)
        {
            var natural = StrideSpeed(a, clip, fallback);
            var k = Mathf.Clamp(pace / natural, 0.7f, 1.4f);
            return (k, natural * k);
        }
    }
}
