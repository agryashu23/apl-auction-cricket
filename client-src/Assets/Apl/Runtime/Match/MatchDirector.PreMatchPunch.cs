using System.Collections;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The openers' glove punch at the middle (celebrations.json
    /// rhb_/lhb_bat_mid_pitch_glove_punch: each walks 2.5 m in and punches at
    /// frame 55). They're set facing each other along the pitch, both clips are
    /// sampled at the punch frame, and the partner is shifted so the two closest
    /// gloves are on the same spot — then both play from the start.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private const float PunchFrame = 55f / 100f, PunchLength = 100f / 30f;

        private IEnumerator GlovePunch(Animator a, BatterRig ra, Animator b, BatterRig rb)
        {
            var ca = Hand(ra) + "bat_mid_pitch_glove_punch";
            var cb = Hand(rb) + "bat_mid_pitch_glove_punch";
            if (!Body.Has(a, ca) || !Body.Has(b, cb)) yield break;
            // The batting clips' toward-the-bowler axis turned to point at the partner (BatterRig's facing).
            var turn = Quaternion.Euler(0, 90f, 0);
            a.transform.rotation = Quaternion.LookRotation(Vector3.forward) * turn;
            b.transform.rotation = Quaternion.LookRotation(Vector3.back) * turn;
            Sample(a, ca, PunchFrame);
            Sample(b, cb, PunchFrame);
            var (ga, gb) = Closest(a, b);
            var shift = ga - gb;
            shift.y = 0;
            // A clip that walks 2.5 m each way can be fooled by a wild sample: keep the fix small.
            if (shift.magnitude < 2.5f) b.transform.position += shift;
            foreach (var (body, clip) in new[] { (a, ca), (b, cb) })
            {
                body.SetBool(Animator.StringToHash("Mirror"), false);
                body.speed = 1f;
                body.Play(clip, 0, 0f);
            }
            var centre = (a.transform.position + b.transform.position) / 2f + Vector3.forward * 0f;
            Camera.Track(() => (centre + new Vector3(-5.2f, 1.45f, 0), centre + Vector3.up * 1.1f), fov: 38f, smooth: 0.3f, cut: true);
            yield return Hold(Secs(PunchLength));
            // The clip carried them in: bring each transform back under him before he walks on.
            Body.Rebase(a);
            Body.Rebase(b);
        }

        /// <summary>The pose at <paramref name="normalized"/> of <paramref name="clip"/>, evaluated now.</summary>
        private static void Sample(Animator a, string clip, float normalized)
        {
            a.SetBool(Animator.StringToHash("Mirror"), false);
            var speed = a.speed;
            a.speed = 1f;
            a.Play(clip, 0, normalized);
            a.Update(0f);
            a.speed = speed;
        }

        /// <summary>The nearest pair of hands between the two of them.</summary>
        private static (Vector3 a, Vector3 b) Closest(Animator a, Animator b)
        {
            var best = (Vector3.zero, Vector3.zero);
            var bestD = float.MaxValue;
            foreach (var ha in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
            foreach (var hb in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
            {
                var ta = a.GetBoneTransform(ha);
                var tb = b.GetBoneTransform(hb);
                if (ta == null || tb == null) continue;
                var d = Vector3.Distance(ta.position, tb.position);
                if (d >= bestD) continue;
                bestD = d;
                best = (ta.position, tb.position);
            }
            return best;
        }
    }
}
