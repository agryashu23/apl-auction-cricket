using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Walking people about for the ceremonies: the walk-out loops (in place, made
    /// at 1.31 m/s — ceremony.json), the transform carried along the ground at the
    /// matching pace, turned the way each clip faces; then they're handed back.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>Walking pace (m/s): a brisk walk, so the shots don't drag.</summary>
        private const float WalkPace = 1.4f; // round 10: inside the feet-matching range (stride 1.11 m/s × ≤1.4) even at +8 %

        /// <summary>A walk loop: its name and the ground speed it was made at (its facing is measured as he walks).</summary>
        private sealed class Gait
        {
            public string Clip = "run_01_walk_in_loop";
            public float Speed = 1.5f;
        }

        private sealed class Walker
        {
            public Animator A = null!;
            public Vector3 To;
            public Quaternion Rot;
            /// <summary>Done: he's in his place. Halted: stopped where he is (the next shot moves him on).</summary>
            public bool Done, Halted;
            /// <summary>Turning on the spot into his final facing (after <see cref="Done"/>).</summary>
            public bool Turning;
            public Action? Arrived;
        }

        /// <summary>A fielder's walk out: the pack's loop (now and then the clapping one), else the plain walk.</summary>
        private Gait FieldGait(Animator a)
        {
            var clap = _visRng.Chance(0.3) && Body.Has(a, "fielder_walk_out_clapping_loop");
            var clip = clap ? "fielder_walk_out_clapping_loop" : "fielder_walk_out_loop";
            return Body.Has(a, clip) ? new Gait { Clip = clip, Speed = 1.31f } : new Gait();
        }

        /// <summary>
        /// A batter's walk out, bat tucked under the arm or carried (rhb_/lhb_). These
        /// are made in the batting frame (facing sideways to the rig): Strides turns
        /// the body, not the rig, to the way he's going.
        /// </summary>
        private Gait BatGait(Animator a, bool leftHanded)
        {
            var hand = leftHanded ? "lhb_" : "rhb_";
            var clip = hand + (_visRng.Chance(0.6) ? "walk_out_bat_tucked_loop" : "walk_out_bat_carried_loop");
            if (!Body.Has(a, clip)) clip = hand + "walk_out_bat_tucked_loop";
            return Body.Has(a, clip) ? new Gait { Clip = clip, Speed = 1.31f } : new Gait();
        }

        private Walker StartWalk(Animator a, Vector3 from, Vector3 to, Quaternion rot, Gait g, Action? arrived = null)
        {
            var w = new Walker { A = a, To = to, Rot = rot, Arrived = arrived };
            StartCoroutine(Walk(w, from, g));
            return w;
        }

        /// <summary>From <paramref name="from"/> to his place on the shared walk (Strides: the body always faces where he's going).</summary>
        private IEnumerator Walk(Walker w, Vector3 from, Gait g)
        {
            w.A.transform.position = from;
            if (SlideFix.Round1)
            {
                // (Iteration 11, reviewer: the new batter's first walk-out step slid 40.8 cm — the holds from where he stood before
                // the move to the gate, and a pose not yet updated for the phase match: dropped, and updated.)
                FootPin.On(w.A)?.DropHolds();
                w.A.Update(0f);
            }
            // Each his own pace (±8 %, stable per body): a group no longer marches in step (r9 `226`).
            var own = WalkPace * (0.92f + 0.16f * (Mathf.Abs(w.A.name.GetHashCode()) % 1000) / 999f);
            // Deep area 1, log only: who's walking out (the batters' walk loops carry the bat).
            SlideScene.Tag(w.A, g.Clip.Contains("walk_out_bat") ? (_newBatterWalking ? "batter walk-out (new batter)" : "batter walk-out") : "fielder taking the field (walk-out)");
            yield return Strides.Walk(w.A, w.To, own, g.Clip, g.Speed, () => w.Done || w.Halted);
            if (w.Halted || w.A == null || w.Done) yield break;
            // Into his final facing over a moment as the idle fades in, not a snap on the spot.
            var was = w.A.transform.rotation;
            Arrive(w);
            var turn = Quaternion.Angle(was, w.Rot);
            if (turn < 3f) yield break;
            w.Turning = true;
            var t = w.A.transform;
            var secs = Mathf.Lerp(0.25f, 0.6f, turn / 180f);
            if (SlideFix.Round1)
            {
                // Deep area 1 (reviewer RC2: "*walk_out*>*75_non_striker_idle_loop +turn" 1.0-1.6 m/s): the turn into his
                // final facing steps round (TurnStep: pivot foot held, the other foot steps), not a spin on the spot.
                // (Batters: no turn clip here — theirs end in the guard stance; the next shot sets that up.)
                yield return TurnStep.Turn(w.A, w.Rot, secs, () => !w.Turning, clips: w.A.GetComponent<BatterRig>() == null);
                if (t != null && w.Turning) t.rotation = w.Rot;
                w.Turning = false;
                yield break;
            }
            for (var k = 0f; k < 1f && t != null && w.Turning; k += Time.deltaTime / secs)
            {
                t.rotation = Quaternion.Slerp(was, w.Rot, Mathf.SmoothStep(0, 1, k));
                yield return null;
            }
            if (t != null && w.Turning) t.rotation = w.Rot;
            w.Turning = false;
        }

        /// <summary>He's there: in his place, standing as he'll stand, then whatever comes next for him.</summary>
        private static void Arrive(Walker w)
        {
            if (w.Done) return;
            w.Done = true;
            // His feet stop driving him now (not a frame later, over whatever he does next).
            if (w.A.GetComponent<FootLock>() is FootLock feet) feet.End();
            w.A.speed = 1f;
            w.A.transform.SetPositionAndRotation(w.To, w.Rot);
            if (w.Arrived != null) w.Arrived();
            else if (Body.Has(w.A, "Ready")) w.A.CrossFadeInFixedTime("Ready", 0.25f);
        }

        /// <summary>Until they're all there (or SKIP, or <paramref name="max"/> s); <paramref name="finish"/>: put the rest in place.</summary>
        private IEnumerator Walkers(List<Walker> walkers, float max, bool finish = true)
        {
            for (var until = Time.time + max; Time.time < until && !_pre.Skipped && walkers.Any(w => !w.Done || w.Turning);) yield return null;
            foreach (var w in walkers)
            {
                if (w.Turning && w.A != null) w.A.transform.rotation = w.Rot;
                w.Turning = false;
            }
            if (!finish) yield break;
            foreach (var w in walkers.Where(w => !w.Done)) Arrive(w);
            yield return null;
        }

        /// <summary>Stop walkers where they are (a cut to the next shot moves them on).</summary>
        private static void Halt(IEnumerable<Walker> walkers)
        {
            foreach (var w in walkers)
            {
                w.Halted = true;
                if (w.A != null && w.A.GetComponent<FootLock>() is FootLock feet) feet.End();
            }
        }

        /// <summary>The point on the way from <paramref name="from"/> to <paramref name="to"/> at most <paramref name="left"/> m short of it.</summary>
        private static Vector3 Short(Vector3 from, Vector3 to, float left) =>
            Vector3.Distance(from, to) <= left ? from : Vector3.MoveTowards(to, from, left);
    }
}
