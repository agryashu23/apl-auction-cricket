using System.Collections;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Footwork before the ball: down the track, or a step across to the off side or
    /// away to leg. With motion-batting's clips (charge_down_the_track, move_across_off,
    /// move_leg_side — travel in the pose) he plays them, and the shot then starts from
    /// where he got to (the transform is brought under his body first, so nothing jumps).
    /// Until they're installed: a glide of the whole body (down the track only part of
    /// the way — the down-the-track shots, 63–66, carry him the rest).
    /// </summary>
    public sealed partial class BatterRig
    {
        /// <summary>Glide fallback: how far down the track (the shot's own travel does the rest), how far across, and how long each takes (s).</summary>
        public const float ChargeGlide = 0.7f, AcrossStep = 0.35f, ChargeSeconds = 0.45f, AcrossSeconds = 0.3f;

        /// <summary>He's charged; and his step across (+1 to his off side, −1 away to leg, 0 none) — for the engine's ShotInput.</summary>
        public bool Charged { get; private set; }
        public int Across { get; private set; }

        /// <summary>The charge clip is installed (so the down-the-track shots, which carry their own charge, aren't used after it).</summary>
        public bool HasChargeClip => UsesClips && Body.Has(_animator, Named("82_charge_down_the_track"));

        /// <summary>How far he's moved from his guard (world m) once any footwork under way is done: the shot's new origin.</summary>
        public Vector3 Shift => (_footGlide != null ? _glideEnd : transform.position) - _home;

        /// <summary>The shot's about to be chosen and timed: settle any footwork clip's travel now.</summary>
        public void SettleFootwork() => CommitFootwork(false);

        private Vector3 _glideEnd;

        /// <summary>A footwork clip is playing with its travel still in the pose: (state, where his body was when it began).</summary>
        private (int state, Vector3 hips)? _approach;
        private Coroutine? _footGlide;

        private void ResetFootwork()
        {
            Charged = false;
            Across = 0;
            _approach = null;
            if (_footGlide != null) StopCoroutine(_footGlide);
            _footGlide = null;
        }

        /// <summary>Down the track, now.</summary>
        public void Charge()
        {
            if (Charged) return;
            Charged = true;
            if (!PlayFootwork("82_charge_down_the_track"))
                Glide(Vector3.forward * ChargeGlide, ChargeSeconds); // up the pitch, at the bowler
        }

        /// <summary>A step across: +1 toward his off side (covering off stump), −1 away to leg (room through the off side).</summary>
        public void MoveAcross(int offSide)
        {
            if (Across != 0 || offSide == 0) return;
            Across = offSide > 0 ? 1 : -1;
            if (!PlayFootwork(Across > 0 ? "83_move_across_off" : "84_move_to_leg_side"))
                Glide(Off * (AcrossStep * Across), AcrossSeconds);
        }

        private bool PlayFootwork(string clip)
        {
            var name = Named(clip);
            if (!UsesClips || !Body.Has(_animator, name)) return false;
            // A second footwork clip on top of the first: settle the first's travel before it starts.
            CommitFootwork(false);
            StopClock();
            Mirror();
            _approach = (Animator.StringToHash(name), HipsGround());
            _animator.CrossFadeInFixedTime(name, 0.12f, 0, 0f);
            return true;
        }

        /// <summary>
        /// A shot (or the shape-up) is about to start from his guard pose: if a footwork clip
        /// has carried his body away from the transform, bring the transform under it first.
        /// </summary>
        private void CommitFootwork(bool sameApproach)
        {
            if (_approach is not (int _, Vector3 from)) return;
            _approach = null;
            var moved = HipsGround() - from;
            moved.y = 0;
            transform.position += moved;
        }

        private void Glide(Vector3 by, float seconds)
        {
            if (_footGlide != null) StopCoroutine(_footGlide);
            _glideEnd = transform.position + by;
            _footGlide = StartCoroutine(GlideBy(by, seconds));
        }

        private IEnumerator GlideBy(Vector3 by, float seconds)
        {
            var done = Vector3.zero;
            for (var e = 0f; e < seconds;)
            {
                e += Time.deltaTime;
                var want = by * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / seconds));
                transform.position += want - done;
                done = want;
                yield return null;
            }
            _footGlide = null;
        }
    }
}
