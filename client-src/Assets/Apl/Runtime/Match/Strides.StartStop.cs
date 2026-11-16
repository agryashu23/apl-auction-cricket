using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Deep area 1: the fielders' start and stop transitions (the owner's starts_stops delivery, ITEM 2:
    /// fielder_start_walk / _jog / _sprint, fielder_stop_from_walk / _jog / _sprint) and the travelling turns (fielder_turn_and_go_180,
    /// bowler_walkback_turn_180), plugged in by name — while they aren't on the animator nothing changes.
    /// <list type="bullet">
    /// <item>Their travel is imported as ROOT MOTION from the centre of mass, not baked into the pose (AssetImportRules v9):
    /// the body stays over the transform, like the in-place loops, so FootLock's free mode — the planted foot's travel IS
    /// the body's — carries him through them exactly as through a loop.</item>
    /// <item>Iteration 5 (reviewer S-1/S-2): the sidecars are parsed as JSON (ClipSidecars) — speeds, travel_m, braking_m. The
    /// clips are long (starts 2.8-3.5 s over 3.5 / 6.6 / 13 m; stops 2.1 / 4.6 / 8.8 m, braking 0.55 / 1.99 / 4.94 m), so a
    /// start is used only between balls and only when the run is at least its travel; a stop is handed over to only between
    /// balls, when the distance left comes down to its travel (never under its braking distance), and the run then ends
    /// with the clip — never after arrival (the old Coast hand-over carried him metres past his spot).</item>
    /// <item>For frames with no foot down (a sprint's push-off) the feet's estimate (<see cref="FeetSpeed"/>) ramps: a start
    /// from 0 to its end speed, a stop from its start speed to 0.</item>
    /// </list>
    /// </summary>
    public static partial class Strides
    {
        /// <summary>Start clips by the gait they lead into (walk, jog, sprint), their end speed (m/s) and travel (m) if no sidecar.</summary>
        private static readonly (string clip, float speed, float travel)[] StartClips =
        {
            ("fielder_start_walk", 1.27f, 3.51f), ("fielder_start_jog", 3.27f, 6.59f), ("fielder_start_sprint", 6.94f, 12.95f),
        };

        /// <summary>Stop clips from walk, jog, sprint: their start speed (m/s), travel and braking distance (m) if no sidecar.</summary>
        private static readonly (string clip, float speed, float travel, float braking)[] StopClips =
        {
            ("fielder_stop_from_walk", 1.27f, 2.0f, 0.55f), ("fielder_stop_from_jog", 3.27f, 4.32f, 1.99f), ("fielder_stop_from_sprint", 6.94f, 8.58f, 4.94f), // (2026-11-13 re-delivery's figures)
        };

        private static bool _ssLogged;

        private static void LogSidecars()
        {
            if (_ssLogged || !Debug.isDebugBuild) return;
            _ssLogged = true;
            var n = 0;
            foreach (var c in StartClips) if (ClipSidecars.Of(c.clip) != null) n++;
            foreach (var c in StopClips) if (ClipSidecars.Of(c.clip) != null) n++;
            Debug.Log($"[APL strides] start/stop clip sidecars: {n} entries");
        }

        private static float StartSpeed(int i) => ClipSidecars.Num(ClipSidecars.Of(StartClips[i].clip), "speed_at_end_mps") ?? StartClips[i].speed;
        private static float StartTravel(int i) => ClipSidecars.Travel(ClipSidecars.Of(StartClips[i].clip)) ?? StartClips[i].travel;
        private static float StopSpeed(int i) => ClipSidecars.Num(ClipSidecars.Of(StopClips[i].clip), "speed_at_start_mps") ?? StopClips[i].speed;
        private static float StopTravel(int i) => ClipSidecars.Travel(ClipSidecars.Of(StopClips[i].clip)) ?? StopClips[i].travel;
        private static float StopBraking(int i) => ClipSidecars.Num(ClipSidecars.Of(StopClips[i].clip), "braking_m") ?? StopClips[i].braking;

        private static int GaitIndex(int hash)
        {
            for (var i = 0; i < GaitHashes.Length; i++) if (GaitHashes[i] == hash) return i;
            return -1;
        }

        /// <summary>The start clip into <paramref name="gait"/> (one of the gait loops) on this body, and its travel (m); null if none.</summary>
        public static (string clip, float travel)? StartClipFor(Animator a, string gait)
        {
            LogSidecars();
            var i = gait == Gaits[0].clip ? 0 : gait == Gaits[1].clip ? 1 : gait == Gaits[2].clip ? 2 : -1;
            return i >= 0 && Body.Has(a, StartClips[i].clip) ? (StartClips[i].clip, StartTravel(i)) : ((string, float)?)null;
        }

        /// <summary>
        /// The stop clip for a body in a gait loop whose feet go <paramref name="speed"/> m/s: the one whose entry speed is
        /// within 0.8-1.25x of it (iteration 6, reviewer I5-2: picked by the loop, a slowed 2.1 m/s jog got the 3.27 m/s jog
        /// stop) — its name, travel, braking distance (m) and entry speed (m/s); null if none fits or he isn't in a gait loop.
        /// </summary>
        public static (string clip, float travel, float braking, float entry)? StopClipFor(Animator a, float speed)
        {
            LogSidecars();
            var st = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : a.GetCurrentAnimatorStateInfo(0);
            if (GaitIndex(st.shortNameHash) < 0) return null;
            for (var i = 0; i < StopClips.Length; i++)
            {
                var entry = StopSpeed(i);
                if (speed < 0.8f * entry || speed > 1.25f * entry || !Body.Has(a, StopClips[i].clip)) continue;
                return (StopClips[i].clip, StopTravel(i), StopBraking(i), entry);
            }
            return null;
        }

        private static readonly int[] StartHashes = { Animator.StringToHash(StartClips[0].clip), Animator.StringToHash(StartClips[1].clip), Animator.StringToHash(StartClips[2].clip) };
        private static readonly int[] StopHashes = { Animator.StringToHash(StopClips[0].clip), Animator.StringToHash(StopClips[1].clip), Animator.StringToHash(StopClips[2].clip) };

        /// <summary>A start clip's state.</summary>
        public static bool IsStart(int hash) => System.Array.IndexOf(StartHashes, hash) >= 0;

        /// <summary>A stop clip's state.</summary>
        public static bool IsStop(int hash) => System.Array.IndexOf(StopHashes, hash) >= 0;

        /// <summary>The ground speed a start or stop clip has at this point (m/s at playback 1): a ramp up or down; 0 for anything else.</summary>
        private static float TransitionSpeed(AnimatorStateInfo info)
        {
            var t = Mathf.Clamp01(info.normalizedTime);
            var s = System.Array.IndexOf(StartHashes, info.shortNameHash);
            if (s >= 0) return StartSpeed(s) * t;
            var e = System.Array.IndexOf(StopHashes, info.shortNameHash);
            return e >= 0 ? StopSpeed(e) * (1f - t) : 0f;
        }

        // ------------------------------------------------------- hand-over out of a travelling turn

        private static readonly HumanBodyBones[] PoseBones =
        {
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.Head,
        };

        /// <summary>The pose's main joints relative to the hips, in the body's own frame (its facing taken out).</summary>
        private static Vector3[] BodyPose(Animator a)
        {
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            var inv = Quaternion.Inverse(Quaternion.LookRotation(BodyForward(a)));
            var o = new Vector3[PoseBones.Length];
            for (var i = 0; i < PoseBones.Length; i++)
            {
                var b = a.GetBoneTransform(PoseBones[i]);
                o[i] = b != null && hips != null ? inv * (b.position - hips.position) : Vector3.zero;
            }
            return o;
        }

        private static readonly Dictionary<string, float> Phases = new Dictionary<string, float>();

        /// <summary>
        /// Out of a clip whose pose has turned or travelled off its transform (a turn baked into the pose) straight into
        /// <paramref name="loop"/> at the phase whose pose is nearest the one drawn now (24 phases sampled, once per clip, loop
        /// and body), the transform put under the body (its hips, its facing as that phase stands) — no fade, so no blend of
        /// two facings; the loops these clips end in match to about 2 cm (verify_report).
        /// </summary>
        public static void RebaseInto(Animator a, string loop)
        {
            var tr = a.transform;
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null || !Body.Has(a, loop))
            {
                if (Body.Has(a, loop)) a.Play(loop, 0, 0f);
                return;
            }
            var key = $"{a.GetCurrentAnimatorStateInfo(0).shortNameHash}>{loop}|{a.avatar?.name}";
            var at = hips.position;
            var face = BodyForward(a);
            if (!Phases.TryGetValue(key, out var phase))
            {
                var now = BodyPose(a);
                var best = float.MaxValue;
                for (var k = 0; k < 24; k++)
                {
                    a.Play(loop, 0, k / 24f);
                    a.Update(0f);
                    var p = BodyPose(a);
                    var gap = 0f;
                    for (var i = 0; i < p.Length; i++) gap = Mathf.Max(gap, (p[i] - now[i]).magnitude);
                    if (gap >= best) continue;
                    best = gap;
                    phase = k / 24f;
                }
                Phases[key] = phase;
                if (Debug.isDebugBuild) Debug.Log($"[APL strides] {a.name}: into {loop} at phase {phase:0.00} (pose {best * 100:0.0} cm off)");
            }
            a.Play(loop, 0, phase);
            a.Update(0f);
            var yaw = ClipYaw(a);
            var local = Quaternion.Inverse(tr.rotation) * new Vector3(hips.position.x - tr.position.x, 0, hips.position.z - tr.position.z);
            var rot = Quaternion.LookRotation(face) * Quaternion.Euler(0, -yaw, 0);
            var pos = new Vector3(at.x, 0, at.z) - rot * local;
            tr.SetPositionAndRotation(new Vector3(pos.x, tr.position.y, pos.z), rot);
            a.Update(0f);
        }
    }
}
