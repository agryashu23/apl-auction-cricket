using System;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Deep area 3, developer 1 (gather and throw): the shared rules for a fielder's pick-up → set → throw.
    /// The owner: "fielder throws the ball facing in the opposite direction; they throw the ball immediately as
    /// it comes to them (picking the ball and then throwing takes some time)". So:
    /// a gather that takes time (by how it was collected, a touch quicker or slower by the man);
    /// the ball in his throwing hand (BallView, after the animation — never a frame behind it) from the transfer
    /// to the release frame, and the flight from there; a body that turns to the target no faster than a body
    /// turns (clip turns faster than <see cref="SnapRate"/> are slowed, code turns are capped at <see cref="TurnRate"/>);
    /// the throw that suits the distance — named clips plug in when they're installed, the current ones otherwise.
    /// <c>-aplnothrowfix</c> puts the old behaviour back (for a before/after run on one build); the
    /// <c>[APL throw]</c> / <c>[APL pickup]</c> lines are logged either way in test runs.
    /// </summary>
    public static class ThrowCraft
    {
        public static readonly bool On = !Environment.GetCommandLineArgs().Contains("-aplnothrowfix");

        /// <summary>Hands on it → in the throwing hand (s): a clean two-hand pick-up, one hand on the run, up from a dive.</summary>
        public const float GatherClean = 0.3f, GatherOnTheRun = 0.25f, GatherBarrier = 0.35f, GatherDive = 0.45f;

        /// <summary>The ball moves from the collecting hands to the throwing hand over this (s), after it's in them.</summary>
        public const float Transfer = 0.35f;

        /// <summary>Never released sooner than this after the hands met it (s), even by a clip's on-the-run flick.</summary>
        public const float MinRelease = 0.45f;

        /// <summary>He rises from the gather into the throw clip over this (s), his hips held where they are.</summary>
        public const float RiseBlend = 0.2f;

        /// <summary>A throw clip is played at most this much quicker (×) to keep the engine's moment.</summary>
        public const float MaxSpeedUp = 1.3f;

        /// <summary>He's turned in code at most this fast (deg/s); a clip turning faster than <see cref="SnapRate"/> is slowed to it.</summary>
        public const float TurnRate = 420f, SnapRate = 650f;

        /// <summary>A clip's own whole-body turn after the take is slowed to this (deg/s): under SnapRate, so the code turn
        /// (TurnCapped) has room left to close the throw line (build 4: seq_22 left 17–20° off with the clip at 650).</summary>
        public const float PaceRate = 450f;

        /// <summary>Throw choice by distance (m): an underarm flick below <see cref="UnderarmMax"/>, a snap throw to
        /// <see cref="SnapMax"/>, a crow hop from <see cref="DeepThrow"/>, and from <see cref="BounceFrom"/> one bounce in.</summary>
        public const float UnderarmMax = 9f, SnapMax = 25f, DeepThrow = 32f, BounceFrom = 45f;

        /// <summary>He needs to turn more than this (deg) at the gather: the turn-and-throw, when it's there.</summary>
        public const float TurnAndThrow = 90f; // (build 2: never chosen at 120°; build 3: twice at 100°)

        // Clip names. The mocap throw clips are used by name once installed into
        // Data/cricket/fielding.json (type "throw", release_frame); until then the current ones stand in.
        public const string Overarm = "throw_18_overarm_throw", Underarm = "throw_19_underarm_throw",
            CrowHop = "throw_crow_hop_overarm", SideArm = "throw_sidearm_snap", TurnThrow = "throw_turn_and_throw_180";

        /// <summary>The gather for a collecting clip, a touch quicker or slower by the man (±0.04 s, the same each time).</summary>
        public static float GatherFor(SeqClip clip, Animator a)
        {
            var g = clip.Style == FieldStyle.Dive ? GatherDive
                : clip.Style == FieldStyle.Stand ? GatherBarrier
                : clip.RightHand || clip.LeftHand ? GatherOnTheRun
                : GatherClean;
            g += (Tempo(a.name) - 0.5f) * 0.08f;
            // Down on the turf (a dive, a slide, the long barrier's knee): he gets up first — the clip's own
            // "up and throwing" frame, less the blend into the throw.
            if ((clip.Style == FieldStyle.Dive || clip.Style == FieldStyle.Chase || clip.Style == FieldStyle.Stand) && !float.IsNaN(clip.ChainAt))
                g = Mathf.Max(g, clip.ChainAt - clip.EventTime - RiseBlend);
            return g;
        }

        /// <summary>0–1 from a name, the same on every run.</summary>
        public static float Tempo(string s)
        {
            var h = 11;
            foreach (var ch in s) h = (h * 31 + ch) & 0xffff;
            return (h % 89) / 88f;
        }

        /// <summary>
        /// The throw for this distance and turn, from the clips this body has: the turn-and-throw for a ball
        /// gathered facing away; an underarm flick from close; a snap throw in the ring; a crow hop from the deep;
        /// the overarm throw otherwise (and for any of those not installed yet).
        /// </summary>
        public static SeqClip? PickThrow(Animator a, float dist, float turnDeg)
        {
            var wants = new System.Collections.Generic.List<string>();
            // (Build 3: used at 7 m, where it should be an underarm flick.) From UnderarmMax out only.
            if (turnDeg > TurnAndThrow && dist >= UnderarmMax) wants.Add(TurnThrow);
            if (dist < UnderarmMax) wants.Add(Underarm);
            else if (dist <= SnapMax) wants.Add(SideArm);
            else if (dist >= DeepThrow) wants.Add(CrowHop);
            wants.Add(Overarm);
            wants.Add(Underarm);
            foreach (var n in wants)
            {
                var c = SeqClip.Named(n);
                if (c != null && c.HasThrow && FieldPlay.Has(a, c)) return c;
            }
            return null;
        }

        /// <summary>The ball in the throwing hand: where the fingers meet the palm (as BallView cradles it).</summary>
        public static Vector3 Palm(Animator a) => HandPoint.Palm(a, false);

        /// <summary>Put the ball in his throwing hand (followed after the animation every frame), or out of it (null).</summary>
        public static void InHand(Transform ball, Animator? a)
        {
            var view = ball.GetComponent<BallView>();
            if (view == null) return;
            if (a == null)
            {
                if (view.InHand) view.Release();
                return;
            }
            view.Hold(a.GetBoneTransform(HumanBodyBones.RightHand), a.GetBoneTransform(HumanBodyBones.RightMiddleProximal));
        }

        /// <summary>The ball kept on <paramref name="at"/> after the animation each frame (BallView.Follow) — the collecting hands — until InHand or a throw takes it.</summary>
        public static void Follow(Transform ball, Func<Vector3> at)
        {
            var view = ball.GetComponent<BallView>();
            if (view != null) view.Follow(at);
        }

        /// <summary>Which way his chest faces (yaw, deg), square to his hips and shoulders, from the bones.</summary>
        public static float ChestYaw(Animator a)
        {
            var right = Across(a, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg) + Across(a, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm);
            var f = Vector3.Cross(right, Vector3.up);
            return f.sqrMagnitude > 1e-6f ? Yaw(f) : Yaw(a.transform.forward);
        }

        /// <summary>Which way his hips face (yaw, deg), square to the line across them (the reviewer's "hips fwd").</summary>
        public static float HipsYaw(Animator a)
        {
            var f = Vector3.Cross(Across(a, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg), Vector3.up);
            return f.sqrMagnitude > 1e-6f ? Yaw(f) : Yaw(a.transform.forward);
        }

        private static Vector3 Across(Animator a, HumanBodyBones l, HumanBodyBones r)
        {
            var lt = a.GetBoneTransform(l);
            var rt = a.GetBoneTransform(r);
            return lt != null && rt != null ? new Vector3(rt.position.x - lt.position.x, 0, rt.position.z - lt.position.z) : Vector3.zero;
        }

        /// <summary>Turn him so his chest comes round toward <paramref name="at"/>, no faster than <paramref name="rate"/> deg/s (about his planted foot, <see cref="Yawed"/>).</summary>
        public static void TurnChest(Animator a, Vector3 at, float rate)
        {
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            var from = hips != null ? hips.position : a.transform.position;
            var to = new Vector3(at.x - from.x, 0, at.z - from.z);
            if (to.sqrMagnitude < 1f) return;
            var delta = Mathf.DeltaAngle(ChestYaw(a), Yaw(to));
            TurnCapped(a, Mathf.Clamp(delta, -rate * Time.deltaTime, rate * Time.deltaTime));
        }

        /// <summary>
        /// THE one place a throw turns a body (reviewer R1, sliding's rule: a capped pivot on the planted foot only):
        /// <paramref name="deg"/> this frame about his planted (lower) foot, so it stays where it is — not about his
        /// hips, which swept the feet at ~1.8 m/s. The sliding area's in-play pivot (TurnStep.PivotYaw: FootPin's held
        /// contact, by delta only, never a clip; + = to his right, as here). Coroutines only, never LateUpdate.
        /// </summary>
        /// <summary>
        /// A code turn of up to <paramref name="want"/> deg this frame, held so his chest's turn — the clip's own plus
        /// this — stays under <see cref="SnapRate"/> (build 3: seq_21's paced 650°/s spin plus the 420°/s cap read as
        /// 900–1,240°/s), and never over <see cref="TurnRate"/>. The clip's own turn is what his chest did last frame
        /// beyond what was put on it. Returns the degrees put on.
        /// </summary>
        public static float TurnCapped(Animator a, float want)
        {
            var dt = Mathf.Max(1e-4f, Time.deltaTime);
            var chest = ChestYaw(a);
            var id = a.GetInstanceID();
            var own = LastChest.TryGetValue(id, out var last) && last.frame == Time.frameCount - 1 ? Mathf.Abs(Mathf.DeltaAngle(last.after, chest)) / dt : 0f;
            var cap = Mathf.Min(TurnRate, Mathf.Max(60f, SnapRate - own)) * dt;
            var step = Mathf.Clamp(want, -cap, cap);
            Yawed(a, step);
            LastChest[id] = (chest + step, Time.frameCount);
            return step;
        }

        private static readonly System.Collections.Generic.Dictionary<int, (float after, int frame)> LastChest = new System.Collections.Generic.Dictionary<int, (float, int)>();

        public static void Yawed(Animator a, float deg)
        {
            if (Mathf.Abs(deg) < 1e-4f) return;
            TurnStep.PivotYaw(a, deg);
        }

        public static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
    }
}
