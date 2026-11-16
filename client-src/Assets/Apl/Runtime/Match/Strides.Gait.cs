using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// FLD-1 (fielders skating on jogs and sprints): the gait for a speed is the loop whose
    /// own stride speed (measured on this body, Strides.Stride.cs) needs the least change
    /// of playback to match it — never outside 0.65–1.45× — and a body being walked by
    /// <see cref="Walk"/> (FootLock active) belongs to that walk: nothing else plays
    /// states on him (<see cref="Busy"/>).
    /// </summary>
    public static partial class Strides
    {
        /// <summary>The fielders' locomotion loops and the packs' figures (the measured ones win).</summary>
        private static readonly (string clip, float pack)[] Gaits =
        {
            ("run_01_walk_in_loop", 1.27f), ("run_02_jog_loop", 3.27f), ("run_03_sprint_loop", 6.94f),
        };

        public const float MinPlayback = 0.65f, MaxPlayback = 1.45f;

        /// <summary>
        /// The loop for <paramref name="speed"/> m/s on <paramref name="a"/>, its natural speed and the playback
        /// that matches it. <paramref name="current"/>: the loop he's in now, kept while it can still match
        /// (no flicker between jog and sprint round the changeover).
        /// </summary>
        public static (string clip, float natural, float playback) PickGait(Animator a, float speed, string? current = null)
        {
            (string clip, float natural, float playback) best = default;
            var bestCost = float.MaxValue;
            foreach (var (clip, pack) in Gaits)
            {
                if (!Body.Has(a, clip)) continue;
                var natural = StrideSpeed(a, clip, pack);
                var k = speed / Mathf.Max(0.1f, natural);
                var cost = Mathf.Abs(Mathf.Log(k));
                if (clip == current && k >= MinPlayback && k <= MaxPlayback) cost -= 0.25f;
                if (cost >= bestCost) continue;
                bestCost = cost;
                // Owner v26 #17: the slowest loop (the walk) plays at exactly the speed he's carried at below
                // 0.65x — starting off, pulling up, a slow walk-in; the old clamp ran the legs at 0.65x while the
                // body crept, ~0.5-0.8 m/s of slide on every start and stop of a kinematic run (RunTo, the walk-in)
                // — and up to 1.65x, so there's no band between the walk's top and the jog's bottom that neither covers.
                var slowest = clip == Gaits[0].clip && SlideFix.On;
                best = (clip, natural, Mathf.Clamp(k, slowest ? 0.02f : MinPlayback, slowest ? 1.65f : MaxPlayback));
            }
            return best.clip != null ? best : ("run_02_jog_loop", 3.27f, 1f);
        }

        /// <summary>
        /// A replay on screen (set by MatchDirector): the recorder drives every body then, so walks hold —
        /// no stepping, no guard (r13 plain log: up to 12 "placement jumps" in one jog were the replay
        /// writing his recorded positions while the walk fought it).
        /// </summary>
        public static System.Func<bool>? Paused;

        public static bool IsPaused => Paused != null && Paused();

        private static readonly int[] GaitHashes = { Animator.StringToHash(Gaits[0].clip), Animator.StringToHash(Gaits[1].clip), Animator.StringToHash(Gaits[2].clip) };
        private static readonly System.Collections.Generic.Dictionary<Animator, float[]> GaitNatural = new System.Collections.Generic.Dictionary<Animator, float[]>();

        /// <summary>A gait loop's own stride speed on this body (m/s at playback 1), 0 for anything that isn't one of them.</summary>
        private static float Natural(Animator a, int hash)
        {
            if (!GaitNatural.TryGetValue(a, out var n))
            {
                n = new float[Gaits.Length];
                for (var i = 0; i < Gaits.Length; i++) n[i] = Body.Has(a, Gaits[i].clip) ? StrideSpeed(a, Gaits[i].clip, Gaits[i].pack) : 0f;
                GaitNatural[a] = n;
            }
            for (var i = 0; i < GaitHashes.Length; i++) if (GaitHashes[i] == hash) return n[i];
            return 0f;
        }

        /// <summary>
        /// Owner v26 #17 (round 3): how fast the pose on screen is pushing him over the ground NOW (m/s, along the
        /// way his body faces) — each fielder gait loop's own stride speed × the animator's playback, weighted by
        /// its share of the blend; a stance, a fielding clip or a frozen interrupted fade ("(none)") pushes nothing.
        /// A kinematic mover that travels at exactly this speed never slides: not while a run fades in from the
        /// stance, not while it fades out, not between two gaits.
        /// </summary>
        public static float FeetSpeed(Animator a)
        {
            var cur = a.GetCurrentAnimatorStateInfo(0);
            // (Deep area 1: a start or stop clip, Strides.StartStop.cs, ramps its own speed.)
            float Of(AnimatorStateInfo i) => Natural(a, i.shortNameHash) is var n && n > 0f ? n : TransitionSpeed(i);
            if (!a.IsInTransition(0)) return Of(cur) * a.speed;
            var next = a.GetNextAnimatorStateInfo(0);
            var t = Mathf.Clamp01(a.GetAnimatorTransitionInfo(0).normalizedTime);
            return ((1 - t) * Of(cur) + t * Of(next)) * a.speed;
        }

        /// <summary>Where his lower foot is on the ground (world, ankle), for turning him about it; null if neither is low.</summary>
        public static Vector3? LowFoot(Animator a)
        {
            var l = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            var r = a.GetBoneTransform(HumanBodyBones.RightFoot);
            if (l == null || r == null) return null;
            var low = l.position.y <= r.position.y ? l.position : r.position;
            return low.y < 0.16f ? low : (Vector3?)null;
        }

        /// <summary>Being walked by <see cref="Walk"/> right now: leave his animator and his position alone.</summary>
        public static bool Busy(Animator? a) => a != null && a.GetComponent<FootLock>() is FootLock f && f.Active && !f.Free;

        /// <summary>His feet are carrying him for a kinematic mover (FootLock.BeginFree): nobody else should move his transform.</summary>
        public static bool FeetDriven(Animator? a) => a != null && a.GetComponent<FootLock>() is FootLock f && f.Active && f.Free;
    }
}
