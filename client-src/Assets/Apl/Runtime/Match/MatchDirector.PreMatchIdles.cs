using System.Collections;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The toss party's idles (round 7, motion-other's level-headed loops in
    /// ceremony.json): captain_idle_relaxed_loop while a captain waits,
    /// captain_listen_nod_loop while he listens to the presenter,
    /// presenter_listen_loop while the presenter waits on a captain's answer —
    /// in place of the generic "Stand". Each falls back to the old pose when the
    /// clip isn't in the controller.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private const string CaptainRelaxed = "captain_idle_relaxed_loop";
        private const string CaptainListen = "captain_listen_nod_loop";
        private const string PresenterListen = "presenter_listen_loop";

        /// <summary>
        /// Stand <paramref name="a"/> at <paramref name="at"/> facing <paramref name="face"/> in the idle loop
        /// <paramref name="clip"/> (mirrored: weight and hand on the other side), from a random point in the loop so
        /// two captains never move in step. The clips are made facing the rig's forward, so he's only turned by his
        /// pose if that's well off (a weight shift twists the hips a little; that's the pose, not the facing).
        /// </summary>
        private static void Idle(Animator? a, Vector3 at, Vector3 face, string clip, bool mirror = false, string? fallback = "Stand")
        {
            if (a == null) return;
            if (!Body.Has(a, clip))
            {
                Stand(a, at, face, fallback);
                return;
            }
            a.transform.position = at;
            a.transform.rotation = Quaternion.LookRotation(new Vector3(face.x, 0, face.z));
            a.SetBool(Animator.StringToHash("Mirror"), mirror);
            a.speed = 1f;
            a.Play(clip, 0, Random.value);
            a.Update(0f);
            face.y = 0;
            var (fwd, _) = Facing(a);
            var off = Vector3.SignedAngle(fwd, face, Vector3.up);
            if (Mathf.Abs(off) > 35f) a.transform.rotation = Quaternion.Euler(0, off, 0) * a.transform.rotation;
        }

        /// <summary>
        /// Blend <paramref name="a"/> where he stands into the loop <paramref name="clip"/> (the mirror kept, so the
        /// outgoing pose doesn't flip); nothing if he hasn't got it.
        /// </summary>
        private static void Blend(Animator? a, string clip, float fade = 0.4f)
        {
            if (a == null || !Body.Has(a, clip)) return;
            a.speed = 1f;
            a.CrossFadeInFixedTime(clip, fade, 0, clip.EndsWith("_loop") ? Random.value * 2f : 0f);
        }

        /// <summary>
        /// From his idle into a one-off clip's first frame, held there (for the flip): a short blend, then
        /// <see cref="Stand"/> squares him on the clip's own pose (a few frames back to frame 1 — no visible snap).
        /// </summary>
        private IEnumerator Into(Animator? a, Vector3 at, Vector3 face, string clip, float fade = 0.3f)
        {
            if (a == null) yield break;
            if (Body.Has(a, clip) && a.GetBool(Animator.StringToHash("Mirror")) == false)
            {
                a.speed = 1f;
                a.CrossFadeInFixedTime(clip, fade, 0, 0f);
                yield return new WaitForSeconds(fade);
            }
            Stand(a, at, face, clip);
        }

        /// <summary>
        /// After <paramref name="wait"/> s (his line said), <paramref name="a"/> from <paramref name="from"/> into
        /// <paramref name="clip"/> — only if he's still in <paramref name="from"/> (the mic moment may have come first).
        /// </summary>
        private IEnumerator BlendLater(Animator? a, string from, string clip, float wait)
        {
            yield return new WaitForSeconds(wait);
            if (a == null || !a.isActiveAndEnabled || a.IsInTransition(0) || !a.GetCurrentAnimatorStateInfo(0).IsName(from)) yield break;
            Blend(a, clip);
        }

        /// <summary>Both captains into their flip clips together (<see cref="Into"/>).</summary>
        private IEnumerator IntoFlip(Animator? capA, Animator? capB)
        {
            var other = StartCoroutine(Into(capA, Mine, TossFwd, "captain_toss_watch"));
            yield return Into(capB, TossAt, TossFwd, "captain_toss_flip");
            yield return other;
        }
    }
}
