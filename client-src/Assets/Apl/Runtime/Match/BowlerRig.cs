using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The bowler: walks back to his mark, runs in, and brings the arm over in
    /// code (a windmill of the bowling arm); the release fires just past the
    /// top of the arc. A bowling clip, when one exists,
    /// takes over the body; the arm is still drawn so the release lines up.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class BowlerRig : MonoBehaviour
    {
        private static readonly int Walking = Animator.StringToHash("Walking");

        private Animator _animator = null!;
        private float _arm = -1f;  // -1 = arm at rest; else 0..1 through the action
        private Coroutine? _run;
        private bool _leftArm;

        /// <summary>The bowling clips are there (else the run-up and arm drawn in code).</summary>
        public bool UsesClips { get; private set; }

        /// <summary>The run-up shown: clip seconds before the release (the clips' whole run-ups are 4–5 s).</summary>
        private const float RunUpSeconds = 3.4f;

        /// <summary>
        /// The run-up plays this much faster — the clips jog in where a bowler
        /// charges — then real speed for the gather and the delivery stride.
        /// </summary>
        private const float RunUpPace = 1f, RunUpSpin = 0.85f, DeliverySeconds = 0.9f;

        /// <summary>
        /// How much of the clip's run-up is shown, by type (round 13, until the owner's mocap: pace and spin
        /// run-ups looked the same — every bowler showed 3.4 s). A spinner walks in from a few paces, a medium
        /// pacer has a short rhythmic run, the quick men the long charge.
        /// </summary>
        private static float RunUpFor(BowlClip clip) =>
            clip.Action.Contains("spin") ? 1.9f
            : clip.Sprints || clip.Action.Contains("express") ? 3.8f
            : clip.Action.Contains("fast_medium") ? 3.0f
            : 2.5f;

        /// <summary>
        /// This bowler's run-up against a fast-medium man's (rules-bowling, Bowling.RunUpPace):
        /// express bowlers charge in, medium pacers jog. Set by the director when he's given the over.
        /// </summary>
        public float RunUpScale = 1f;

        public Vector3 Hand
        {
            get
            {
                var h = _animator.GetBoneTransform(HumanBodyBones.RightHand);
                return h != null ? h.position : transform.position + Vector3.up * 2f;
            }
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            UsesClips = _animator.runtimeAnimatorController != null && _animator.HasState(0, Animator.StringToHash("Stance"));
        }

        /// <summary>
        /// Bowl with a clip: stand on its mark (it runs in towards the batter on
        /// its own — the run-up is baked into the clip), join it shortly before
        /// the release, and fire <paramref name="onRelease"/> on the release frame.
        /// The mark is set so the bowling hand is at <paramref name="releaseAt"/>
        /// (where the ball starts) on that frame: the clip's run-up length
        /// changes with the body it's retargeted onto, so it's measured, not
        /// taken from the clip's notes.
        /// </summary>
        public void Bowl(BowlClip clip, Vector3 releaseAt, Action onRelease)
        {
            if (_run != null) StopCoroutine(_run);
            _leftArm = clip.Arm == "left";
            _run = StartCoroutine(ClipRoutine(clip, releaseAt, onRelease));
        }

        /// <summary>Where the bowling hand is at release, relative to where he stands (facing the batter).</summary>
        private readonly Dictionary<string, Vector3> _handAtRelease = new Dictionary<string, Vector3>();

        public Vector3 HandAtRelease(BowlClip clip)
        {
            if (_handAtRelease.TryGetValue(clip.Name, out var offset)) return offset;
            var pos = transform.position;
            var rot = transform.rotation;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(Vector3.back, Vector3.up));
            _animator.Play(clip.Name, 0, clip.ReleaseTime / Mathf.Max(0.01f, clip.Length));
            _animator.Update(0f);
            var hand = _animator.GetBoneTransform(clip.Arm == "left" ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            offset = hand != null ? hand.position : new Vector3(clip.StartLeft, 2f, -clip.StartBehind);
            transform.SetPositionAndRotation(pos, rot);
            _handAtRelease[clip.Name] = offset;
            return offset;
        }

        private IEnumerator ClipRoutine(BowlClip clip, Vector3 releaseAt, Action onRelease)
        {
            _arm = -1f;
            // The mark: wherever puts the hand over the release point on the release frame.
            var hand = HandAtRelease(clip);
            transform.position = new Vector3(releaseAt.x - hand.x, 0, releaseAt.z - hand.z);
            transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            var length = Mathf.Max(0.01f, clip.Length);
            var start = Mathf.Max(0, clip.ReleaseTime - RunUpFor(clip));
            var fast = (clip.Sprints ? 1f : clip.Action.Contains("spin") ? RunUpSpin : RunUpPace) * (clip.Sprints ? 1f + (RunUpScale - 1f) * 0.5f : RunUpScale);
            // Deep area 1, iteration 7 (reviewer A2: 6-9 fielders 1.3-2.45 m short of their marks at every release): the time the
            // ball really leaves his hand — the run-up plays at "fast" up to the delivery stride — for the fielders' walk-in,
            // which was timed to the clip's own release time (seconds longer).
            var runUp = Mathf.Max(0f, clip.ReleaseTime - DeliverySeconds - start);
            ReleaseDueAt = Time.time + runUp / Mathf.Max(0.1f, fast) + Mathf.Min(DeliverySeconds, clip.ReleaseTime - start);
            var hash = Animator.StringToHash(clip.Name);
            _animator.Play(clip.Name, 0, start / length);
            _animator.speed = fast;
            // Follow the clip's own clock (the state's time once it's playing) to the release frame.
            var clipTime = start;
            while (clipTime < clip.ReleaseTime)
            {
                yield return null;
                var info = _animator.GetCurrentAnimatorStateInfo(0);
                clipTime = info.shortNameHash == hash ? info.normalizedTime * length : clipTime + Time.deltaTime * _animator.speed;
                _animator.speed = clipTime >= clip.ReleaseTime - DeliverySeconds ? 1f : fast;
            }
            _animator.speed = 1f;
            onRelease();
            yield return new WaitForSeconds(Mathf.Max(0, clip.Length - clip.ReleaseTime - 0.2f));
            _run = null;
            // Nothing else for him to do (fielding, an appeal): turn and walk back to his mark.
            if (SlideFix.Round1 && UsesClips && Body.Has(_animator, WalkBackTurn) && Body.Has(_animator, WalkBackLoop))
            {
                _run = StartCoroutine(WalkBackRoutine());
                yield break;
            }
            Body.Play(_animator, (_leftArm ? "la_" : "ra_") + "turn_and_walk_back_to_mark");
        }

        /// <summary>When this ball is due out of his hand (Time.time), set as the action starts; -1 before any.</summary>
        public float ReleaseDueAt { get; private set; } = -1f;

        /// <summary>
        /// Deep area 1, iteration 7 (reviewer A5): who to walk round on the walk back (the non-striker's hips, set every frame by
        /// MatchDirector.WalkBack while he's near) — the walk steers round him instead of being pushed sideways.
        /// </summary>
        public void AvoidOnWalkBack(Vector3 at)
        {
            _avoid = at;
            _avoidAt = Time.time;
        }

        private Vector3 _avoid;
        private float _avoidAt = -1f;

        /// <summary>Walking back on his feet (the turn into it or the loop): the walk steers; nobody should push him.</summary>
        public bool WalkingBackOnFeet => _walkingBack && GetComponent<FootLock>() is FootLock f && f.Active && f.Free; // (a stopped routine's feet time out in 2 frames)

        private bool _walkingBack;

        /// <summary>The owner's turn into the walk back (deep area 1, iteration 5), and the walk-back loop for this arm.</summary>
        private const string WalkBackTurn = "bowler_walkback_turn_180";

        private string WalkBackLoop => (_leftArm ? "la_" : "ra_") + "walk_back_loop";

        /// <summary>
        /// Deep area 1 (iteration 5): the walk back on his feet. From the follow-through's end into the owner's
        /// bowler_walkback_turn_180 (a 180 on the planted foot, into the walk) with the transform kept under the blend
        /// (Strides.FadeUnderBody: no Rebase pop); its travel is root motion his planted foot carries (FootLock, free — the
        /// old turn_and_walk_back clip carried it in the pose, the transform left behind and the feet slid on the shove round
        /// the non-striker); at its end straight into the walk-back loop under his turned body (Strides.RebaseInto), and on
        /// toward the top of his run-up until the next ball takes him (Mark / Settle / Act stop this routine).
        /// </summary>
        private IEnumerator WalkBackRoutine()
        {
            _walkingBack = true;
            try
            {
                var e = WalkBackSteps();
                while (e.MoveNext()) yield return e.Current;
            }
            finally
            {
                _walkingBack = false;
            }
        }

        private IEnumerator WalkBackSteps()
        {
            var a = _animator;
            SlideScene.Tag(a, "bowler walk back", 0.5f);
            var fade = Strides.FadeUnderBody(a, WalkBackTurn, 0.2f);
            while (fade.MoveNext()) yield return fade.Current;
            if (a == null) yield break;
            var feet = GetComponent<FootLock>() ?? gameObject.AddComponent<FootLock>();
            var mark = new Vector3(transform.position.x, 0, GroundSpace.HalfPitch + 14f);
            var mine = feet.BeginFree(mark);
            var hash = Animator.StringToHash(WalkBackTurn);
            for (var t = 0f; t < 4f; t += Time.deltaTime)
            {
                if (feet.Owner != mine || !feet.Active) yield break;
                var info = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : a.GetCurrentAnimatorStateInfo(0);
                if (t > 0.2f && (info.shortNameHash != hash || info.normalizedTime >= 0.95f)) break;
                feet.Beat = Time.frameCount;
                SlideScene.Tag(a, "bowler walk back");
                yield return null;
            }
            if (a.GetCurrentAnimatorStateInfo(0).shortNameHash != hash) yield break; // something else took his animator
            Strides.RebaseInto(a, WalkBackLoop);
            mine = feet.BeginFree(mark);
            for (var t = 0f; t < 40f; t += Time.deltaTime)
            {
                if (feet.Owner != mine || !feet.Active) yield break;
                var off = mark - transform.position;
                off.y = 0f;
                if (off.magnitude < 0.3f) break;
                // (Iteration 6, reviewer I5-4: steered at his mark — at most 60°/s, about the planted foot — so he gets there
                // and stops, not walking on past it in the loop.)
                // Round the non-striker (iteration 7, A5): within 2.5 m and ahead of him, aim 1.4 m to his far side.
                var aim = off;
                var near = _avoid - transform.position;
                near.y = 0f;
                if (Time.time - _avoidAt < 0.2f && near.magnitude < 2.5f && Vector3.Dot(near, off) > 0f)
                {
                    var side = Vector3.Cross(Vector3.up, off.normalized);
                    if (Vector3.Dot(side, near) > 0f) side = -side;
                    aim = near + side * 1.4f;
                }
                if (!Strides.IsPaused) TurnStep.TurnTowards(a, aim, 60f);
                feet.Beat = Time.frameCount;
                SlideScene.Tag(a, "bowler walk back");
                yield return null;
            }
            if (feet.Owner == mine) feet.End();
            _run = null;
            var settle = Strides.FadeUnderBody(a, "Ready", 0.3f);
            while (settle.MoveNext()) yield return settle.Current;
        }

        /// <summary>
        /// An appeal or a celebration from wherever the action left him, facing
        /// <paramref name="face"/> if given (appeals start facing the batter and
        /// turn round to the umpire themselves).
        /// </summary>
        public bool Act(string clip, Vector3? face = null)
        {
            if (!UsesClips || !Body.Has(_animator, clip)) return false;
            if (_run != null) StopCoroutine(_run);
            _run = null;
            _arm = -1f;
            _animator.speed = 1f;
            if (!Body.Play(_animator, clip, face)) return false;
            // When it's played through, back to the ready stance where he ended up (no frozen last frame).
            if (!clip.EndsWith("_loop")) _run = StartCoroutine(SettleAfter(clip));
            return true;
        }

        private IEnumerator SettleAfter(string clip)
        {
            var hash = Animator.StringToHash(clip);
            yield return null;
            while (true)
            {
                var info = _animator.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash != hash || info.normalizedTime >= 0.97f) break;
                yield return null;
            }
            if (SlideFix.Round1)
            {
                // Deep area 1 (reviewer RC5: Rebase, then a cross-fade from a pose still drawn off the old transform — the
                // celebration and appeal rows over target): the transform kept under the blend instead.
                yield return Strides.FadeUnderBody(_animator, "Ready", 0.3f);
                _run = null;
                yield break;
            }
            _run = null;
            Body.Rebase(_animator);
            _animator.CrossFadeInFixedTime("Ready", 0.3f);
        }

        /// <summary>The arm of the action he bowled with this ball.</summary>
        public bool LeftArm => _leftArm;

        /// <summary>
        /// Let someone else drive his animator from here (a return catch cross-fades
        /// from the action in place): stops his own routine, leaves pose and transform alone.
        /// </summary>
        public void Release()
        {
            if (_run != null) StopCoroutine(_run);
            _run = null;
            _arm = -1f;
        }

        /// <summary>
        /// Stop the action where he is (to field the ball): the run-up is baked
        /// into the clip, so his feet are wherever the clip has taken him, not
        /// at his mark — move him there before anything else plays.
        /// </summary>
        public void Settle()
        {
            if (_run != null) StopCoroutine(_run);
            _run = null;
            _arm = -1f;
            _animator.speed = 1f;
            var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null) transform.position = new Vector3(hips.position.x, 0, hips.position.z);
            if (UsesClips) _animator.Play("Ready", 0, 0f);
        }

        /// <summary>To the top of the run-up (spinners walk in from closer).</summary>
        public void Mark(bool spin)
        {
            if (_run != null) StopCoroutine(_run);
            _run = null;
            _arm = -1f;
            _animator.speed = 1f;
            if (UsesClips) _animator.Play("Stand", 0, 0f);
            else _animator.SetBool(Walking, false);
            transform.position = new Vector3(0.9f, 0, GroundSpace.HalfPitch + (spin ? 6f : 13f));
            transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        }

        /// <summary>Run in and bowl; <paramref name="onRelease"/> fires at the release point.</summary>
        public void Bowl(bool spin, Action onRelease)
        {
            if (_run != null) StopCoroutine(_run);
            _run = StartCoroutine(BowlRoutine(spin, onRelease));
        }

        private IEnumerator BowlRoutine(bool spin, Action onRelease)
        {
            var start = transform.position;
            var crease = new Vector3(0.45f, 0, GroundSpace.HalfPitch - 0.4f);
            var runTime = spin ? 1.3f : 1.7f;
            _animator.SetBool(Walking, true);
            for (var t = 0f; t < 1f; t += Time.deltaTime / runTime)
            {
                transform.position = Vector3.Lerp(start, crease, t);
                yield return null;
            }
            _animator.SetBool(Walking, false);
            // The action: arm from down behind, over the top, down in front.
            var released = false;
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
            {
                _arm = t;
                if (!released && t >= 0.54f)
                {
                    released = true;
                    onRelease();
                }
                transform.position = crease + Vector3.back * (t * 1.2f);
                yield return null;
            }
            _arm = -1f;
            // Follow through down the pitch a few strides.
            _animator.SetBool(Walking, true);
            var from = transform.position;
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.8f)
            {
                transform.position = Vector3.Lerp(from, from + Vector3.back * 3f + Vector3.right * 1.2f, t);
                yield return null;
            }
            _animator.SetBool(Walking, false);
            _run = null;
        }

        private void LateUpdate()
        {
            if (_arm < 0) return;
            var upper = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var lower = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var hand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (upper == null || lower == null || hand == null) return;
            // Swing in the vertical plane along the run: behind (−fwd) → up → in front, down.
            var fwd = transform.forward;
            // θ = 0 points the arm straight down, −90° behind, ±180° up, −270° (= 90°) in front.
            var angle = Mathf.Lerp(-45f, -330f, _arm) * Mathf.Deg2Rad;
            var want = (-Vector3.up * Mathf.Cos(angle) + fwd * Mathf.Sin(angle)).normalized;
            upper.rotation = Quaternion.FromToRotation((lower.position - upper.position).normalized, want) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation((hand.position - lower.position).normalized, want) * lower.rotation;
        }
    }
}
