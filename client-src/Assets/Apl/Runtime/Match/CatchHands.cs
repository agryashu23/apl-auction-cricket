using System;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Deep area 3 (owner: "the hand and ball coordination is too different, looks like the ball hitting their
    /// body and not the hands"): the hands go to the ball, not the ball to the body. On a catch, a keeper's take
    /// or a return catch, over the last <see cref="Window"/> s before the ball arrives the hand IK carries both
    /// hands (or the one taking it) so the palm cup (<see cref="HandPoint"/>) is on the ball's real line, holds
    /// them there through the take, then lets the clip have them back; from the take the ball is kept in the cup
    /// after the animation and the IK (never a frame behind, never at the wrist). Test runs: at the moment of
    /// contact it reports how far the ball's own flight was from the cup BEFORE anything moved the ball, and
    /// whether the ball came within reach of the torso before the hands closed. Added to a body when first used.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class CatchHands : MonoBehaviour
    {
        /// <summary>The hands start reaching this long (s) before the ball arrives, and hold it on for <see cref="HoldOn"/> after, then fade over <see cref="FadeOut"/>.</summary>
        public const float Window = 0.25f, HoldOn = 0.3f, FadeOut = 0.3f;

        /// <summary>How far (m) the IK may carry the cup off where the clip has it (an arm's spare reach).</summary>
        public const float MaxReach = 0.65f;

        /// <summary>How much of a miss (m) is left to the arms rather than moving the body (steer, a keeper's shuffle): no slide for it.</summary>
        public const float FreeReach = 0.3f;

        /// <summary>A reach is on (the body steer leaves the first <see cref="FreeReach"/> to the arms).</summary>
        public bool Reaching => _target != null;

        /// <summary>Torso half-depth (m) about the hips–spine–chest–neck bone line, and the "body-hit" clearance beyond it.</summary>
        public const float Torso = 0.1f, BodyClear = 0.05f;

        /// <summary>What a reach found at the moment of contact (for the test-run log).</summary>
        public struct Contact
        {
            /// <summary>
            /// Gap: the ball's true flight point vs the cup (did the hands get to the ball's line). Drawn: the ball
            /// as actually DRAWN at contact (the paced flight, the residual and the blend) vs the cup — a lagging
            /// flight shows here, not as an unrecorded jump into the cup (review R1).
            /// </summary>
            public float Gap, Drawn, Weight, BodyMin, HeldMax;
            public Vector3 Truth, Cup;
            public bool BodyHit;
        }

        /// <summary>The drawn ball is never blown up past this (× real size) through the take and the hold (review R3: ~20 cm at 2.7× read as touching the chest).</summary>
        public const float TakeBoost = 1.3f;

        private Animator _a = null!;
        private Transform? _cupT;

        /// <summary>The palm cup this frame, after the animation and the IK.</summary>
        public Vector3 Cup { get; private set; }

        /// <summary>A transform kept on the cup every frame (for code that holds the ball on a Transform).</summary>
        public Transform CupTransform
        {
            get
            {
                if (_cupT == null)
                {
                    _cupT = new GameObject("BallCup").transform;
                    _cupT.SetParent(transform, false);
                    _cupT.position = HandPoint.Cup(_a, _left, _right);
                }
                return _cupT;
            }
        }

        // The reach.
        private Func<Vector3>? _target, _drawn;
        private bool _waitArrival;

        /// <summary>
        /// A take at the stumps (final rating 2026-11-14: the ball snapped 1.2–2.2 m into the hands): the contact waits
        /// until the drawn ball is within this (m) of the cup — 0: the planned moment as before.
        /// </summary>
        private float _takeWithin;

        /// <summary>The contact was taken at the timeout, the ball not yet at the hands (a keeper's take).</summary>
        public bool LateContact { get; private set; }
        private float _at, _weight;
        private bool _left, _right, _contactDone;
        private Transform? _ball;
        private Action<Contact>? _onContact;
        private float _bodyMin = float.MaxValue;
        private Vector3 _animCup;
        private bool _animCupValid;

        // Palm offsets from the wrists (last frame), for the animated cup inside the IK pass.
        private Vector3 _offL, _offR;

        // The hold.
        private Transform? _held;
        private float _holdUntil;
        private Func<bool>? _keep;
        private float _heldMax;

        public static CatchHands For(Animator a)
        {
            var h = a.GetComponent<CatchHands>();
            return h != null ? h : a.gameObject.AddComponent<CatchHands>();
        }

        private void Awake() => _a = GetComponent<Animator>();

        /// <summary>
        /// Reach for the ball arriving at <paramref name="target"/> (world, asked every frame) at game time
        /// <paramref name="at"/>; <paramref name="leftOnly"/> / <paramref name="rightOnly"/>: a one-handed take.
        /// <paramref name="ball"/> is watched for the body check; <paramref name="onContact"/> gets the measure.
        /// </summary>
        public void Reach(Func<Vector3> target, float at, Transform? ball, bool leftOnly = false, bool rightOnly = false, Action<Contact>? onContact = null, Func<Vector3>? drawn = null, bool waitArrival = false, float takeWithin = 0f)
        {
            _target = target;
            _drawn = drawn;
            _waitArrival = waitArrival;
            _takeWithin = takeWithin;
            _at = at;
            _ball = ball;
            _left = leftOnly;
            _right = rightOnly;
            _onContact = onContact;
            _contactDone = false;
            _bodyMin = float.MaxValue;
            _heldMax = 0f;
            _animCupValid = false;
            // Animated even off the main camera through the take (review b4: a dive "1.9 m off", the catcher in no shot —
            // a culled animator keeps its last pose, so his hands and the IK were wherever he last was drawn).
            if (!_culled.HasValue)
            {
                _culled = _a.cullingMode;
                _a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        private AnimatorCullingMode? _culled;

        /// <summary>The reach is over: the animator's own culling back.</summary>
        private void EndReach()
        {
            _target = null;
            if (_culled is AnimatorCullingMode m) _a.cullingMode = m;
            _culled = null;
        }

        /// <summary>The ball stays in the cup (after the animation and IK) for <paramref name="seconds"/>, while <paramref name="keep"/> says so.</summary>
        public void Hold(Transform ball, float seconds, Func<bool>? keep = null)
        {
            _held = ball;
            _holdUntil = Time.time + seconds;
            _keep = keep;
        }

        /// <summary>Out of his hands (thrown, tossed up in celebration, lobbed back): the cup stops holding it.</summary>
        public void Let() => _held = null;

        /// <summary>Stop reaching at once (a new ball, a reset).</summary>
        public void Stop()
        {
            EndReach();
            _held = null;
            _weight = 0f;
        }

        /// <summary>
        /// What the arms can't make up: the ball must be shown this much off its own line at the take
        /// (0 while it's within <see cref="MaxReach"/> of where the clip has the hands).
        /// </summary>
        public Vector3 Residual(Vector3 truth)
        {
            if (!_animCupValid) return Vector3.zero;
            var reach = Vector3.ClampMagnitude(truth - _animCup, MaxReach);
            var predicted = _animCup + reach - truth;
            // As the IK takes hold, what the hands really achieve (the arm-length limit, the solver) replaces the
            // prediction — so the flight ends in the cup and the last blend has nothing to drag (review b3: 13–32 cm).
            return Vector3.Lerp(predicted, Cup - truth, _weight);
        }

        private float WeightNow()
        {
            if (_target == null) return 0f;
            var t = Time.time - _at;
            if (t < -Window) return 0f;
            if (t < 0f) return Mathf.SmoothStep(0f, 1f, 1f + t / Window);
            // On the ball until it's really there (a keeper's take: the delivery can land a beat after the plan), then the hold.
            if (!_contactDone) return 1f;
            t = Time.time - _contactAt;
            if (t < HoldOn) return 1f;
            var f = 1f - (t - HoldOn) / FadeOut;
            if (f <= 0f)
            {
                EndReach();
                return 0f;
            }
            return Mathf.SmoothStep(0f, 1f, f);
        }

        private Vector3 Wrist(AvatarIKGoal goal, HumanBodyBones bone)
        {
            _a.SetIKPositionWeight(goal, 0f);
            var p = _a.GetIKPosition(goal);
            if (p.sqrMagnitude < 1e-4f)
            {
                var b = _a.GetBoneTransform(bone);
                if (b != null) p = b.position;
            }
            return p;
        }

        private void OnAnimatorIK(int layer)
        {
            _weight = WeightNow();
            // Not reaching: hands off the IK entirely (another component — the handshake — may own it).
            if (_target == null) return;
            // The clip's hands this frame (before any pull), and the cup they make.
            var wl = Wrist(AvatarIKGoal.LeftHand, HumanBodyBones.LeftHand);
            var wr = Wrist(AvatarIKGoal.RightHand, HumanBodyBones.RightHand);
            var pl = wl + _offL;
            var pr = wr + _offR;
            _animCup = _left ? pl : _right ? pr : HandPoint.Cup(pl, pr);
            _animCupValid = true;
            if (_weight <= 0f) return;
            var delta = Vector3.ClampMagnitude(_target() - _animCup, MaxReach) * _weight;
            // Both hands move together (the clip's cup shape kept); a one-handed take moves that hand only.
            if (!_right)
            {
                _a.SetIKPositionWeight(AvatarIKGoal.LeftHand, _weight);
                _a.SetIKPosition(AvatarIKGoal.LeftHand, InReach(wl, wl + delta, true));
            }
            if (!_left)
            {
                _a.SetIKPositionWeight(AvatarIKGoal.RightHand, _weight);
                _a.SetIKPosition(AvatarIKGoal.RightHand, InReach(wr, wr + delta, false));
            }
        }

        /// <summary>Never straighter than this share of the arm's length (shoulder to wrist): no locked elbow or pop.</summary>
        private const float ArmStretch = 0.94f;

        /// <summary>
        /// A wrist goal no further from the shoulder than <see cref="ArmStretch"/> of the arm (review r3: watch the
        /// 0.65 m reach for over-straight arms) — or, if the clip already has the arm out that far, no further than it is.
        /// </summary>
        private Vector3 InReach(Vector3 wristNow, Vector3 goal, bool left)
        {
            var sh = _a.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var el = _a.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var wr = _a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (sh == null || el == null || wr == null) return goal;
            var arm = Vector3.Distance(sh.position, el.position) + Vector3.Distance(el.position, wr.position);
            var max = Mathf.Max(arm * ArmStretch, Vector3.Distance(sh.position, wristNow));
            var off = goal - sh.position;
            return off.magnitude > max ? sh.position + off.normalized * max : goal;
        }

        /// <summary>The drawn ball's radius (m): it's blown up for the phone far off (BallView) and outlined.</summary>
        private static float DrawnRadius(Transform ball) => ball.localScale.x * 0.5f * BallView.OutlineScale;

        /// <summary>When the contact was measured (the ball at the hands), and last frame's cup (what other code holds it at).</summary>
        private float _contactAt;
        private Vector3 _prevCup;

        /// <summary>The ball's within this (m) of where it's meant to meet the hands: it has arrived.</summary>
        private const float Arrived = 0.08f;

        /// <summary>The longest the reach waits past its planned moment for the ball (s).</summary>
        private const float LateMax = 0.5f;

        private void LateUpdate()
        {
            _prevCup = Cup;
            var lh = _a.GetBoneTransform(HumanBodyBones.LeftHand);
            var rh = _a.GetBoneTransform(HumanBodyBones.RightHand);
            var pl = HandPoint.Palm(_a, true);
            var pr = HandPoint.Palm(_a, false);
            if (lh != null) _offL = pl - lh.position;
            if (rh != null) _offR = pr - rh.position;
            Cup = _left ? pl : _right ? pr : HandPoint.Cup(pl, pr);
            if (_cupT != null) _cupT.position = Cup;

            // Through the take and the hold the drawn ball isn't blown up past TakeBoost (review R3).
            var ballNow = _held != null ? _held : _ball;
            if (ballNow != null && (_held != null || _target != null && Time.time >= _at - Window - 0.2f))
            {
                var view = ballNow.GetComponent<BallView>();
                if (view != null) view.CapBoost(TakeBoost);
            }

            if (_target != null && !_contactDone)
            {
                // Before the hands close: how near the DRAWN ball (its drawn size) came to his torso's surface.
                if (_ball != null && Time.time < _at) _bodyMin = Mathf.Min(_bodyMin, TorsoGap(_ball.position) - DrawnRadius(_ball));
                // Contact: the planned moment, once the ball really is there (review r1: keeper takes measured the
                // ball 20–32 cm short — the delivery landed after the plan's moment and the hands had moved on).
                var due = _target();
                var drawnNow = _drawn != null ? _drawn() : due;
                // (At its meeting point — or already in the hands, where a catch's flight is steered to end.)
                // (Only a keeper's take waits for the ball: a catch's flight is paced to the planned moment, and waiting
                // there let a turn-and-throw clip carry the cup 2 m round before the measure — review b2, seq_36.)
                var there = !_waitArrival || _ball == null || _drawn == null || Vector3.Distance(drawnNow, due) < Arrived || Vector3.Distance(drawnNow, Cup) < Arrived;
                // A stumps take: held until the ball is really at his hands (it flies into them; never measured, or shown, short).
                if (_takeWithin > 0f && _drawn != null) there = Vector3.Distance(drawnNow, Cup) <= _takeWithin;
                if (Time.time >= _at && (there || Time.time >= _at + LateMax))
                {
                    _contactDone = true;
                    _contactAt = Time.time;
                    LateContact = !there;
                    var truth = due;
                    var drawn = drawnNow;
                    var c = new Contact
                    {
                        Truth = truth,
                        Cup = Cup,
                        Gap = Vector3.Distance(truth, Cup),
                        Drawn = Vector3.Distance(drawn, Cup),
                        Weight = _weight,
                        BodyMin = _bodyMin,
                        BodyHit = _bodyMin < BodyClear,
                    };
                    _onContact?.Invoke(c);
                }
            }

            if (_held != null)
            {
                if (Time.time > _holdUntil || _keep != null && !_keep()) _held = null;
                else
                {
                    // Held gap (review R2/r1): where everything else left the ball this frame, BEFORE we put it in the cup,
                    // against the cup they could know (last frame's: coroutines run before the animation) — a writer
                    // holding it at the wrist or anywhere but the cup shows here; the hands' own motion doesn't.
                    _heldMax = Mathf.Max(_heldMax, Vector3.Distance(_held.position, _prevCup));
                    _held.position = Cup;
                }
            }
        }

        /// <summary>The held gap so far (m): the most the drawn ball was ever off the cup while held.</summary>
        public float HeldMax => _heldMax;

        /// <summary>Distance (m) from a point to his torso's surface (the bone line less <see cref="Torso"/>); negative inside.</summary>
        private float TorsoGap(Vector3 p)
        {
            var best = float.MaxValue;
            // (Hips to chest: up to the neck flagged every over-the-shoulder take as a body hit — review b2.)
            var chain = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest };
            Transform? prev = null;
            foreach (var b in chain)
            {
                var t = _a.GetBoneTransform(b);
                if (t == null) continue;
                if (prev != null) best = Mathf.Min(best, SegmentDistance(p, prev.position, t.position));
                prev = t;
            }
            return best == float.MaxValue ? float.MaxValue : best - Torso;
        }

        private static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var k = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * k);
        }

        private void OnDisable() => Stop();
    }
}
