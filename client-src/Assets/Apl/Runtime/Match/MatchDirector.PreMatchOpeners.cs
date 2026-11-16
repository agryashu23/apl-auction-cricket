using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The openers come out, short (PM, round 11: ~8 s, it was ~20): one wide shot of
    /// them jogging out of the gate side by side, then a cut to the last few metres —
    /// each walking in to his own end, the striker framed as he reaches his crease —
    /// and the guard. (WarmUpAt / GlovePunch stay for a longer cut, unused now.) The
    /// jog and the walk are real (Strides), so the -aplwalkburst capture still films both.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>Where they meet: just off the pitch on the pavilion side, 5 m apart along it (the punch clip's spacing).</summary>
        private const float MeetX = -2.6f, MeetHalf = 2.55f;

        private IEnumerator OpenersEntry(BatterRig striker, BatterRig other, Vector3 strikerTo, Quaternion strikerRot, Vector3 otherTo, Quaternion otherRot)
        {
            var a = striker.GetComponent<Animator>();
            var b = other.GetComponent<Animator>();
            foreach (var r in new[] { striker, other }) { r.gameObject.SetActive(true); r.Carry(); }
            _pre.ShowSkip(true);
            _pre.ResetSkip();
            var gate = Gate();
            // Lanes: the striker on the batting end's side (−z) of his partner, always.
            var laneA = new Vector3(0, 0, -1.1f);
            var laneB = new Vector3(0, 0, 1.1f);

            // 1. A jog out of the pavilion, the camera ahead of them.
            var jogA = Hand(striker) + "run_loop_with_bat";
            var jogB = Hand(other) + "run_loop_with_bat";
            a.transform.position = gate + laneA;
            b.transform.position = gate + laneB;
            // Both of them, from ahead and a little to one side (the lead-only shot cut them at the edges).
            // Their transforms, not their hip bones: just woken and moved, the bones are still where they were
            // this frame, and the cut landed on empty grass under the caption (r10 f19).
            var hipsA = a.transform;
            var hipsB = b.transform;
            // A wide establishing shot (round 11: one shot out of the gate, ~3.5 s).
            TwoShot(hipsA, hipsB, new Vector3(1f, 0, 0.3f), 50f, cut: true, height: 2.6f, minDist: 10f, smooth: 0.3f);
            var running = 2;
            SlideScene.Tag(a, "batter walk-out (openers' jog)", 1f);
            SlideScene.Tag(b, "batter walk-out (openers' jog)", 1f);
            StartCoroutine(Done(Strides.Walk(a, gate + laneA + Vector3.right * 9f, 2.6f, jogA, 3.38f, () => _pre.Skipped), () => running--));
            StartCoroutine(Done(Strides.Walk(b, gate + laneB + Vector3.right * 9f, 2.6f, jogB, 3.38f, () => _pre.Skipped), () => running--));
            // The caption once they're in the shot.
            yield return new WaitForSeconds(0.4f);
            _pre.Caption($"{_match.OnStrike.Player.Name} and {_match.OffStrike.Player.Name} walk out".ToUpperInvariant());
            while (running > 0 && !_pre.Skipped) yield return null;

            // 2. (PM, round 11: the walk-out was ~20 s of follow shots.) A cut to the last few metres: each
            // walks in to his end — a real walk, on camera, the striker framed as he reaches his crease —
            // and the guard follows. The warm-up and glove punch are left out; about 8 s in all.
            _pre.Caption(null);
            if (!_pre.Skipped)
            {
                // From where they'd be coming in (the pavilion side, x < 0), a few strides short of their places.
                Vector3 Approach(Vector3 to, float zSide) => to + new Vector3(-4.2f, 0, zSide * 1.6f);
                a.speed = b.speed = 1f;
                a.transform.position = Approach(strikerTo, strikerTo.z < 0 ? 1f : -1f);
                b.transform.position = Approach(otherTo, otherTo.z < 0 ? 1f : -1f);
                var rigA = a.transform;
                Camera.Track(() =>
                {
                    var p = rigA.position;
                    return (new Vector3(p.x - 3.5f, 1.9f, p.z + (p.z < 0 ? 6.5f : -6.5f)), new Vector3(p.x, 1.0f, p.z));
                }, fov: 40f, smooth: 0.3f, cut: true);
                running = 2;
                SlideScene.Tag(a, "batter walk-out (openers to their ends)", 1f);
                SlideScene.Tag(b, "batter walk-out (openers to their ends)", 1f);
                StartCoroutine(Done(Strides.Walk(a, strikerTo, 1.8f, BatGait(a, striker.LeftHanded).Clip, 1.31f, () => _pre.Skipped), () => running--));
                StartCoroutine(Done(Strides.Walk(b, otherTo, 1.8f, BatGait(b, other.LeftHanded).Clip, 1.31f, () => _pre.Skipped), () => running--));
                for (var until = Time.time + 4.5f; running > 0 && !_pre.Skipped && Time.time < until;) yield return null;
            }
            a.speed = b.speed = 1f;
            striker.transform.SetPositionAndRotation(strikerTo, strikerRot);
            other.NonStriker(otherTo);
            other.transform.rotation = otherRot;
        }

        private static string Hand(BatterRig r) => r.LeftHanded ? "lhb_" : "rhb_";

        private static IEnumerator Done(IEnumerator routine, System.Action done)
        {
            yield return routine;
            done();
        }

        /// <summary>A warm-up clip where he stands, facing the middle (the cut puts him there).</summary>
        private static void WarmUpAt(Animator a, BatterRig r, Vector3 at, string what)
        {
            var clip = Hand(r) + what;
            a.transform.position = at;
            if (!Body.Has(a, clip)) return;
            a.SetBool(Animator.StringToHash("Mirror"), false);
            a.speed = 1.15f;
            a.Play(clip, 0, 0.1f);
            Square(a, new Vector3(-at.x, 0, -at.z));
        }
    }
}
