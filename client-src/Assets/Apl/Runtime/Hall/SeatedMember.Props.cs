using System;
using System.Collections;
using UnityEngine;

namespace Apl.Client.Hall
{
    /// <summary>
    /// The bidding paddle and the RTM card in a seated member's hand.
    ///
    /// Owner report: "the paddle stick or RTM card goes behind the hand's palm". The old code put
    /// the prop's grip on the wrist → middle-knuckle bone line (the middle of the hand, not the palm
    /// side), drove the hand with a fixed IK rotation (Euler -80 from HallBuilder, an open palm to the
    /// stage), never closed the fingers, and slid the prop through the air from the table to the hand
    /// by the IK weight (shot 32: paddle at chest height, arm up empty).
    ///
    /// Now: the hand reaches the prop on the table, closes on it, lifts it, holds it up, lowers it
    /// and lets go. Every frame, after IK and the head/chest gestures, the palm frame is measured from
    /// the bones (<see cref="HandGrip"/>), the hand is turned to the wanted frame, the fingers close
    /// round the stick (or lie flat behind the card, thumb over its face) and the prop is placed in
    /// that frame: the paddle's stick inside the fist on the palm side, face to the stage; the RTM
    /// card in front of the palm, its readable face to the stage. Distances scale with the measured
    /// hand and arm, so every avatar size works; the paddle uses the hand on its side of the table
    /// (right), the card the hand on its side (left).
    /// </summary>
    public sealed partial class SeatedMember
    {
        public enum PropPhase { None, Reach, Lift, Hold, Lower, Return }

        // Scaled seconds: about the old 1.7 s for a 1.1 s paddle hold.
        private const float ReachTime = 0.25f, LiftTime = 0.3f, LowerTime = 0.3f, ReturnTime = 0.2f;
        /// <summary>Paddle: the point of the handle that sits in the fist (0–0.26 m is handle). Card: its body starts this far above its root.</summary>
        private const float PaddleGripUp = 0.10f, CardBottom = 0.12f;
        /// <summary>Card: how far below the palm centre its bottom edge sits.</summary>
        private const float CardUnderPalm = 0.02f;
        /// <summary>Wrist → knuckle fraction where the stick crosses the palm (fist) / the card is held.</summary>
        private const float PaddleGripAt = 0.78f, CardGripAt = 0.55f;
        /// <summary>Palm centre → stick axis, and palm centre → card, as fractions of the hand length.</summary>
        private const float StickDepth = 0.34f, CardDepth = 0.18f;
        private const float StickRadius = 0.0175f;
        /// <summary>Most the wrist bends away from the forearm when the paddle is up (degrees).</summary>
        private const float WristLimit = 60f;

        public PropPhase Phase { get; private set; }
        /// <summary>0 = the prop on the table in the hand, 1 = up.</summary>
        public float LiftT => _liftT;
        public bool HoldingProp => _held != null;
        public bool HoldingRtm => _held != null && _held == RtmCard;
        public Transform? HeldProp => _held;
        public bool PropHandRight => _propRight;
        /// <summary>
        /// In the chair (the ceremony walks people off). Measured in the table's space: HallDirector
        /// swaps the human's table to the front after Awake, which moves the whole table, people too.
        /// </summary>
        public bool IsHome => (TableSpace(transform.position) - _homePos).sqrMagnitude < 0.04f;
        /// <summary>Where the prop is held (world): the stick in the fist, or the card's palm point.</summary>
        public Vector3 GripPoint { get; private set; }

        private Vector3 _homePos;
        private Transform? _tableRoot;
        private string _who = "";
        private bool _propRight, _propIsCard, _attached;
        private float _reachW, _liftT;
        private float _armLen = 0.55f;
        // The running raise, its deadline (scaled time) and the culling mode to put back.
        private Coroutine? _propRun;
        private float _propDeadline;
        private AnimatorCullingMode _propCulling;

        // Per hand (0 left, 1 right): hand length, palm frame in hand-bone space, IK goal → bone.
        private readonly float[] _handLen = { 0.09f, 0.09f };
        private readonly Quaternion[] _palmLocal = { Quaternion.identity, Quaternion.identity };
        private readonly bool[] _palmKnown = new bool[2];
        private readonly Quaternion[] _ikToBone = { Quaternion.identity, Quaternion.identity };
        private readonly Quaternion[] _calGoal = new Quaternion[2];
        private readonly bool[] _calibrated = new bool[2];
        private int _calFrame = -1, _calCount;
        /// <summary>The IK-goal calibration proved wrong on this rig: the bone pass does all the turning.</summary>
        private bool _ikRotOff;
        /// <summary>Wrist-limit correction of the raised paddle frame (from the forearm, last frame).</summary>
        private Quaternion _clamp = Quaternion.identity;
        private float _clampDeg;

        // Per-raise measurements for the "[APL prop]" line.
        private float _pickupGap, _releasePop, _releaseTurn, _ikResid, _holdErr, _holdFront, _holdThumbBase, _holdDot, _holdCamDot, _holdWrap, _holdThumb, _holdThumbOnCard;
        private int _holdFrames;
        private bool _mirrored;

        /// <summary>Awake: remember the seat, in table space.</summary>
        private void NoteHome()
        {
            var table = GetComponentInParent<TeamTable>();
            _tableRoot = table != null ? table.transform : null;
            _homePos = TableSpace(transform.position);
        }

        private Vector3 TableSpace(Vector3 world) => _tableRoot != null ? _tableRoot.InverseTransformPoint(world) : world;

        private void Start()
        {
            if (Paddle == null && RtmCard == null) return;
            // Note: at Start the bones are still in the standing bind pose (shoulder ~1.4 m up; forcing an
            // Update(0) here did not change that in r2), so the rest line's reach is a bind-pose estimate only.
            // The real reach is each raise's "pickup gap" (0.0 cm on every r2 raise).
            var table = GetComponentInParent<TeamTable>();
            var model = name.StartsWith("Member_") ? name.Substring(7) : name;
            _who = table != null ? $"{table.TeamId}/{model}" : model;
            MeasureHands();
            var up = Bone(HumanBodyBones.RightUpperArm);
            var lo = Bone(HumanBodyBones.RightLowerArm);
            var hd = Bone(HumanBodyBones.RightHand);
            if (up != null && lo != null && hd != null) _armLen = Vector3.Distance(up.position, lo.position) + Vector3.Distance(lo.position, hd.position);
            _seatRight = BodyRight();
            if (PaddleRest != null) _paddleRight = Vector3.Dot(PaddleRest.position - transform.position, _seatRight) > 0;
            if (RtmRest != null) _rtmRight = Vector3.Dot(RtmRest.position - transform.position, _seatRight) > 0;
            SettleRest(PaddleRest, card: false);
            SettleRest(RtmRest, card: true);
        }

        private Transform? Bone(HumanBodyBones b) => _animator.GetBoneTransform(b);

        private void MeasureHands()
        {
            for (var i = 0; i < 2; i++)
            {
                var hand = Bone(HandGrip.Hand(i == 1));
                if (hand == null || !HandGrip.Measure(_animator, i == 1, out var p)) continue;
                _palmLocal[i] = Quaternion.Inverse(hand.rotation) * p.Rotation;
                _handLen[i] = p.Length;
                _palmKnown[i] = true;
            }
        }

        /// <summary>
        /// The seat's right, fixed at Start from the seated shoulders. Hand choice, the stage direction
        /// and the raise target use it: the live shoulder line turns when the bidder confers (chest yaw
        /// up to ~21°), which in auction_fix flipped ~5% of paddle raises to the LEFT hand reaching across
        /// the body (half of them with a pickup gap over 3 cm).
        /// </summary>
        private Vector3 SeatRight() => _seatRight.sqrMagnitude > 0.5f ? _seatRight : BodyRight();

        private Vector3 _seatRight;
        private bool _paddleRight = true, _rtmRight;

        /// <summary>The member's right, from the shoulders now (the model's own axes are not trusted).</summary>
        private Vector3 BodyRight()
        {
            var l = Bone(HumanBodyBones.LeftUpperArm);
            var r = Bone(HumanBodyBones.RightUpperArm);
            var v = l != null && r != null ? r.position - l.position : transform.right;
            v.y = 0;
            return v.sqrMagnitude > 1e-6f ? v.normalized : transform.right;
        }

        /// <summary>
        /// The rests were laid out ~0.75 m in front of the seat, out of a seated arm's reach (so the
        /// old code flew the prop to the hand). Slide each towards the seat, staying on the table,
        /// until the hand can take it.
        /// </summary>
        private void SettleRest(Transform? rest, bool card)
        {
            if (rest == null || rest.parent == null) return;
            var right = card ? _rtmRight : _paddleRight;
            var shoulder = Bone(HandGrip.UpperArm(right));
            if (shoulder == null) return;
            var centre = rest.parent.position;
            var top = rest.parent.Find("Top");
            var radius = top != null ? top.lossyScale.x * 0.5f : 0.95f;
            var toSeat = transform.position - rest.position;
            toSeat.y = 0;
            if (toSeat.sqrMagnitude < 1e-4f) return;
            toSeat.Normalize();
            var reach = _armLen * 0.85f;
            var start = rest.position;
            for (var i = 0; i < 80 && Vector3.Distance(RestGrip(rest, card), shoulder.position) > reach; i++)
            {
                var next = rest.position + toSeat * 0.01f;
                var flat = next - centre;
                flat.y = 0;
                if (flat.magnitude > radius - 0.08f) break;
                rest.position = next;
            }
            // Clear of the water bottles: step out to the prop's own side.
            var side = right ? SeatRight() : -SeatRight();
            // At the rim the step is pulled back onto the rim, so it walks along it (r1 stopped at once).
            for (var i = 0; i < 40 && HitsBottle(rest, card); i++)
            {
                var next = rest.position + side * 0.01f;
                var flat = next - centre;
                flat.y = 0;
                if (flat.magnitude > radius - 0.08f) next = centre + flat.normalized * (radius - 0.08f) + Vector3.up * (next.y - centre.y);
                rest.position = next;
            }
            var gap = Vector3.Distance(RestGrip(rest, card), shoulder.position) - reach;
            Debug.Log($"[APL prop] {_who} {(card ? "rtm" : "paddle")} rest: moved {(rest.position - start).magnitude * 100f:0} cm toward the seat{(HitsBottle(rest, card) ? ", STILL ON A BOTTLE" : "")}, " +
                      $"bind-pose reach estimate {(gap > 0 ? $"{gap * 100f:0} cm over" : "ok")}, real reach = pickup gap per raise ({(right ? "right" : "left")} hand, arm {_armLen:0.00} m, shoulder {shoulder.position.y:0.00} m up, " +
                      $"{Vector3.ProjectOnPlane(RestGrip(rest, card) - shoulder.position, Vector3.up).magnitude:0.00} m out to the grip)");
        }

        /// <summary>The lying prop's footprint (paddle face / card body as a circle) overlaps a bottle on the table.</summary>
        private static bool HitsBottle(Transform rest, bool card)
        {
            var centre = rest.position + rest.up * (card ? CardBottom + 0.08f : 0.42f);
            var r = card ? 0.14f : 0.17f;
            foreach (Transform t in rest.parent)
            {
                if (t.name != "Bottle") continue;
                var d = t.position - centre;
                d.y = 0;
                if (d.magnitude < r + t.lossyScale.x * 0.5f + 0.01f) return true;
            }
            return false;
        }

        private static Vector3 RestGrip(Transform rest, bool card) => rest.position + rest.up * (card ? CardBottom + CardUnderPalm : PaddleGripUp);

        private void Raise(Transform? prop, Transform? rest, float hold)
        {
            _typeUntil = 0;
            if (_held != null || prop == null || rest == null || !isActiveAndEnabled) return;
            _propRun = StartCoroutine(PropRoutine(prop, rest, hold));
        }

        private IEnumerator PropRoutine(Transform prop, Transform rest, float hold)
        {
            _held = prop;
            _heldRest = rest;
            _propIsCard = prop == RtmCard;
            _propRight = _propIsCard ? _rtmRight : _paddleRight;
            _attached = false;
            _reachW = _liftT = 0f;
            _pickupGap = _releasePop = -1f;
            _releaseTurn = _ikResid = _holdErr = _holdWrap = _holdThumb = _holdThumbOnCard = 0f;
            _holdFront = _holdThumbBase = _holdDot = _holdCamDot = 0f;
            _holdFrames = 0;
            _clamp = Quaternion.identity;
            _clampDeg = 0f;
            _fiveGoal = 0f;
            // A camera cut mid-raise must find the arm where it is, not where it was when culled.
            _propCulling = _animator.cullingMode;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // Watchdog (scaled time, so fast-forward and pause are fine): the whole raise is ~hold + 1 s.
            _propDeadline = Time.time + hold + 4f;
            while (_fiveWeight > 0.001f) yield return null;

            Phase = PropPhase.Reach;
            yield return Ramp(v => _reachW = v, 0f, 1f, ReachTime);
            _attached = true;
            Phase = PropPhase.Lift;
            yield return Ramp(v => _liftT = v, 0f, 1f, LiftTime);
            Phase = PropPhase.Hold;
            var until = Time.time + Mathf.Max(0.5f, hold - 0.35f);
            while (Time.time < until)
            {
                // Measured after everything this frame has done to the bones (IK, gestures, our pass).
                yield return new WaitForEndOfFrame();
                MeasureHold();
            }
            Phase = PropPhase.Lower;
            yield return Ramp(v => _liftT = v, 1f, 0f, LowerTime);

            // Put it down where it lay (release pop = the hand's grip vs the rest on the last lowering frame).
            _attached = false;
            prop.localPosition = Vector3.zero;
            prop.localRotation = Quaternion.identity;
            Phase = PropPhase.Return;
            yield return Ramp(v => _reachW = v, 1f, 0f, ReturnTime);
            LogRaise();
            _held = _heldRest = null;
            Phase = PropPhase.None;
            _animator.cullingMode = _propCulling;
            _propRun = null;
        }

        /// <summary>
        /// Stop a raise part-way and put the prop back on its rest (never left floating): the member
        /// was disabled (scene change), walked off the chair, or the raise overran its watchdog.
        /// </summary>
        private void DropProp(string why)
        {
            if (_held == null) return;
            if (_propRun != null)
            {
                StopCoroutine(_propRun);
                _propRun = null;
            }
            var kind = _held == RtmCard ? "rtm" : "paddle";
            var off = _heldRest != null ? Vector3.Distance(_held.position, _heldRest.position) : 0f;
            _held.localPosition = Vector3.zero;
            _held.localRotation = Quaternion.identity;
            Debug.Log($"[APL prop] {_who} {kind} put back early ({why}): phase {Phase}, was {off * 100f:0} cm off its rest, now on it");
            _held = _heldRest = null;
            _attached = false;
            _reachW = _liftT = 0f;
            Phase = PropPhase.None;
            if (_animator != null) _animator.cullingMode = _propCulling;
        }

        private void OnDisable() => DropProp("member disabled");

        private static IEnumerator Ramp(Action<float> set, float from, float to, float seconds)
        {
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                set(Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }
            set(to);
        }

        /// <summary>How much the prop sits in the hand rather than on its rest: in over the first quarter of the lift, out over the last quarter of the lowering.</summary>
        private float HoldBlend => Phase switch
        {
            PropPhase.Lift or PropPhase.Lower => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_liftT / 0.25f)),
            PropPhase.Hold => 1f,
            _ => 0f,
        };

        /// <summary>
        /// Where the hand should be: its palm frame (forward = fingers, up = out of the palm) and the
        /// palm centre, blended from the grip on the table to the raised grip; plus an elbow hint.
        /// </summary>
        private bool WantedPose(out Quaternion frame, out Vector3 palm, out Vector3 elbow, bool clamped = true)
        {
            frame = Quaternion.identity;
            palm = elbow = Vector3.zero;
            if (_heldRest == null) return false;
            var shoulderBone = Bone(HandGrip.UpperArm(_propRight));
            if (shoulderBone == null) return false;
            var i = _propRight ? 1 : 0;
            var up = Vector3.up;
            var right = SeatRight();
            var fw = Vector3.Cross(right, up);
            var outward = _propRight ? right : -right;
            var L = _armLen;
            var s0 = shoulderBone.position;
            var t = Mathf.SmoothStep(0f, 1f, _liftT);
            var restUp = _heldRest.up;
            var len = _handLen[i];
            Quaternion f0, f1;
            Vector3 c0, c1;
            if (!_propIsCard)
            {
                // On the table: palm turned in to the handle, fingers down round it, index toward the face.
                var n0 = Vector3.ProjectOnPlane(-outward, restUp).normalized;
                f0 = HandGrip.Frame(_propRight, n0, -restUp);
                c0 = _heldRest.position + restUp * PaddleGripUp;
                // Up: the stick near upright (top a touch back), palm turned in, fingers forward round it.
                // The hand goes forward of the shoulder with the elbow low at the side, so the forearm leans
                // forward and the wrist stays inside its limit (r1: hand high over the shoulder, forearm near
                // vertical, wrist clamp 32-37 deg tipped the stick back: face-forward dot 0.75-0.82).
                var s1 = (up * Mathf.Cos(5f * Mathf.Deg2Rad) - fw * Mathf.Sin(5f * Mathf.Deg2Rad)).normalized;
                var n1 = Vector3.ProjectOnPlane(-outward, s1).normalized;
                f1 = (clamped ? _clamp : Quaternion.identity) * HandGrip.Frame(_propRight, n1, -s1);
                c1 = s0 + up * (0.55f * L) + fw * (0.45f * L) + outward * (0.15f * L);
            }
            else
            {
                // On the table: palm down on the card's near half, fingers along it (it lies face up, top away).
                f0 = Quaternion.LookRotation(restUp, _heldRest.forward);
                c0 = _heldRest.position + restUp * (CardBottom + CardUnderPalm) - _heldRest.forward * (CardDepth * len);
                // Up: palm to the stage (tilted back a little), fingers up, the card in front of it.
                var n1 = (fw * Mathf.Cos(10f * Mathf.Deg2Rad) + up * Mathf.Sin(10f * Mathf.Deg2Rad)).normalized;
                f1 = Quaternion.LookRotation(Vector3.ProjectOnPlane(up, n1).normalized, n1);
                c1 = s0 + up * (0.60f * L) + fw * (0.32f * L) + outward * (0.18f * L);
            }
            frame = Quaternion.Slerp(f0, f1, t);
            // The paddle swings out to its own side on the way up and down, so its face never passes
            // in front of the bidder's face (r2 review: it hid the face in the table close-ups when lowered).
            var grip = Vector3.Lerp(c0, c1, t) + up * (0.08f * L * Mathf.Sin(Mathf.PI * t))
                       + (_propIsCard ? Vector3.zero : outward * (0.3f * L * Mathf.Sin(Mathf.PI * t)));
            var n = frame * Vector3.up;
            palm = _propIsCard ? grip : grip - n * (StickDepth * len);
            var raisedElbow = _propIsCard
                ? outward * (0.40f * L) - up * (0.10f * L) + fw * (0.10f * L)
                : outward * (0.25f * L) - up * (0.25f * L);
            elbow = s0 + Vector3.Lerp(outward * (0.35f * L) - up * (0.45f * L) - fw * (0.05f * L), raisedElbow, t);
            return true;
        }

        /// <summary>Humanoid IK for the prop hand (called from OnAnimatorIK); idle, it clears both hands' rotation goals and the elbow hints.</summary>
        private void PropIK()
        {
            var w = Mathf.SmoothStep(0f, 1f, _reachW);
            if (_held == null || w <= 0.0001f || !WantedPose(out var frame, out var palm, out var elbow))
            {
                _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
                _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
                _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
                _animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
                return;
            }
            var i = _propRight ? 1 : 0;
            var goal = _propRight ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
            if (!_propRight)
            {
                _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            }
            var wrist = palm - frame * Vector3.forward * (_handLen[i] * (_propIsCard ? CardGripAt : PaddleGripAt));
            _animator.SetIKPositionWeight(goal, w);
            _animator.SetIKPosition(goal, wrist);
            var rotW = _calibrated[i] && _palmKnown[i] && !_ikRotOff ? w : 0f;
            _animator.SetIKRotationWeight(goal, rotW);
            if (rotW > 0f) _animator.SetIKRotation(goal, frame * Quaternion.Inverse(_palmLocal[i]) * Quaternion.Inverse(_ikToBone[i]));
            var hint = _propRight ? AvatarIKHint.RightElbow : AvatarIKHint.LeftElbow;
            _animator.SetIKHintPositionWeight(hint, w * 0.7f);
            _animator.SetIKHintPosition(hint, elbow);
        }

        /// <summary>
        /// First thing in OnAnimatorIK: while no IK touches the hands, note the animated hand goals,
        /// so LateUpdate can learn this rig's goal → hand-bone rotation (a few frames, before any raise).
        /// </summary>
        private void NoteIkGoals()
        {
            if (Paddle == null && RtmCard == null) return;
            if (_calCount >= 8 || _held != null || _typeWeight > 0.001f || _fiveWeight > 0.001f) return;
            _calGoal[0] = _animator.GetIKRotation(AvatarIKGoal.LeftHand);
            _calGoal[1] = _animator.GetIKRotation(AvatarIKGoal.RightHand);
            _calFrame = Time.frameCount;
        }

        /// <summary>First thing in LateUpdate: pair the goals noted this frame with the bones the animation produced.</summary>
        private void CalibrateHands()
        {
            if (_calFrame != Time.frameCount) return;
            _calFrame = -1;
            _calCount++;
            for (var i = 0; i < 2; i++)
            {
                var hand = Bone(HandGrip.Hand(i == 1));
                if (hand == null) continue;
                _ikToBone[i] = Quaternion.Inverse(_calGoal[i]) * hand.rotation;
                _calibrated[i] = true;
            }
            MeasureHands();
        }

        /// <summary>
        /// Last thing in LateUpdate: turn the hand to the wanted frame (whatever IK and the gestures
        /// left), close the fingers, set the thumb, then put the prop in the hand.
        /// </summary>
        private void PropLateUpdate()
        {
            if (_held == null || _heldRest == null) return;
            if (!IsHome)
            {
                DropProp("left the chair");
                return;
            }
            if (Time.time > _propDeadline)
            {
                DropProp("over time");
                return;
            }
            var w = Mathf.SmoothStep(0f, 1f, _reachW);
            var hb = HoldBlend;
            var hand = Bone(HandGrip.Hand(_propRight));
            if (hand == null || !HandGrip.Measure(_animator, _propRight, out var m) || !WantedPose(out var frame, out _, out _))
            {
                _held.SetPositionAndRotation(_heldRest.position, _heldRest.rotation);
                return;
            }

            // How far IK alone left the hand from the wanted frame (logged at the hold).
            if (Phase == PropPhase.Hold) _ikResid = Mathf.Max(_ikResid, Quaternion.Angle(m.Rotation, frame));

            // The wrist only bends so far: tip the raised paddle frame toward the forearm (for next frame's IK too).
            if (!_propIsCard)
            {
                var lower = Bone(HandGrip.LowerArm(_propRight));
                if (lower != null && WantedPose(out var raw, out _, out _, clamped: false))
                {
                    var u = (hand.position - lower.position).normalized;
                    var f = raw * Vector3.forward;
                    var angle = Vector3.Angle(u, f);
                    var excess = (angle - WristLimit) * Mathf.SmoothStep(0f, 1f, _liftT);
                    var axis = Vector3.Cross(f, u);
                    _clamp = excess > 0f && axis.sqrMagnitude > 1e-8f ? Quaternion.AngleAxis(excess, axis.normalized) : Quaternion.identity;
                    _clampDeg = Mathf.Max(_clampDeg, Mathf.Max(0f, excess));
                    WantedPose(out frame, out _, out _);
                }
            }

            // 1. The hand to the wanted frame.
            hand.rotation = Quaternion.Slerp(Quaternion.identity, frame * Quaternion.Inverse(m.Rotation), w) * hand.rotation;
            HandGrip.Measure(_animator, _propRight, out m);

            // 2. Fingers: a fist round the stick, or near-flat behind the card (pre-shaped on the reach, closed as it lifts).
            var curl = w * Mathf.Lerp(0.3f, 1f, hb);
            if (_propIsCard) HandGrip.Curl(_animator, m, 0f, 0f, 0f, curl, spreadPerFinger: 0f);
            else HandGrip.Curl(_animator, m, 70f, 88f, 40f, curl, spreadPerFinger: 4f);

            // 3. Thumb: across the front of the stick, or pressing on the card's face.
            var gripAt = m.At(_propIsCard ? CardGripAt : PaddleGripAt);
            var thumbTarget = _propIsCard
                // Thumb along the card's bottom strip, toward the index side: on the face but under the
                // "RIGHT TO MATCH" line (r2 review: it lay across that text); the fingers stay straight behind.
                ? m.At(0.45f) + m.Normal * (CardDepth * m.Length + 0.012f) - m.Across * 0.07f
                : gripAt + m.Normal * (StickDepth * m.Length + StickRadius + 0.006f) + m.Across * 0.004f;
            var thumbGap = HandGrip.AimThumb(_animator, _propRight, thumbTarget, w * Mathf.Lerp(0.3f, 1f, hb));

            // 4. The prop in that frame.
            Vector3 pos;
            Quaternion rot;
            if (!_propIsCard)
            {
                var c = gripAt + m.Normal * (StickDepth * m.Length);
                var s = -m.Across;
                // Face: the fingers' side on the table (as it lies), square to the stage when up (the stick spins freely in the fist).
                var lying = Vector3.ProjectOnPlane(m.Fingers, s).normalized;
                var stage = Vector3.ProjectOnPlane(Vector3.Cross(SeatRight(), Vector3.up), s);
                var face = stage.sqrMagnitude > 0.04f ? Vector3.Slerp(lying, stage.normalized, Mathf.SmoothStep(0f, 1f, _liftT)) : lying;
                rot = Quaternion.LookRotation(face, s);
                pos = c - s * PaddleGripUp;
                GripPoint = c;
            }
            else
            {
                rot = Quaternion.LookRotation(m.Normal, m.Fingers);
                pos = gripAt + m.Normal * (CardDepth * m.Length) - m.Fingers * (CardBottom + CardUnderPalm);
                GripPoint = gripAt;
            }
            // Only near the table (liftT <= 0.15): under 16x fast-forward one frame covers most of the
            // ramp, and the "gap" would just be how far the lift got (auction_fix: the 7-14 cm outliers).
            if (_attached && Phase == PropPhase.Lift && _pickupGap < 0f && _liftT <= 0.15f) _pickupGap = Vector3.Distance(pos, _heldRest.position);
            if (_attached && Phase == PropPhase.Lower && _liftT <= 0.15f)
            {
                _releasePop = Vector3.Distance(pos, _heldRest.position);
                _releaseTurn = Quaternion.Angle(rot, _heldRest.rotation);
            }
            if (!_attached) hb = 0f;
            _held.SetPositionAndRotation(Vector3.Lerp(_heldRest.position, pos, hb), Quaternion.Slerp(_heldRest.rotation, rot, hb));
            if (Phase == PropPhase.Hold) _holdThumb = Mathf.Max(_holdThumb, thumbGap);
        }

        /// <summary>
        /// End of a hold frame (after IK, gestures, our pass): is the prop where the hand is? Checked
        /// against the bones as they will render, with the palm side cross-checked by the thumb base.
        /// </summary>
        private void MeasureHold()
        {
            if (_held == null || !HandGrip.Measure(_animator, _propRight, out var m)) return;
            _holdFrames++;
            var stage = Vector3.Cross(SeatRight(), Vector3.up);
            var cam = Camera.main;
            if (!_propIsCard)
            {
                var palm = m.At(PaddleGripAt);
                var want = palm + m.Normal * (StickDepth * m.Length);
                var axis = _held.up;
                var origin = _held.position;
                _holdErr = Mathf.Max(_holdErr, HandGrip.LineDistance(want, origin, axis));
                var onStick = origin + axis * Vector3.Dot(palm - origin, axis);
                _holdFront = Vector3.Dot(onStick - palm, m.Normal);
                // Fingers wrapped: middle and ring joints' distance from the stick's surface.
                var wrap = 0f;
                var n = 0;
                for (var finger = 1; finger <= 2; finger++)
                {
                    for (var joint = 1; joint <= 2; joint++)
                    {
                        var b = Bone(HandGrip.Finger(_propRight, finger, joint));
                        if (b == null) continue;
                        wrap += HandGrip.LineDistance(b.position, origin, axis) - StickRadius;
                        n++;
                    }
                }
                if (n > 0) _holdWrap = wrap / n;
                _holdDot = Vector3.Dot(_held.forward, stage);
                if (cam != null) _holdCamDot = Mathf.Abs(Vector3.Dot(_held.forward, (cam.transform.position - _held.position).normalized));
            }
            else
            {
                var palm = m.At(CardGripAt);
                var held = _held.position + _held.up * (CardBottom + CardUnderPalm) - _held.forward * (CardDepth * m.Length);
                _holdErr = Mathf.Max(_holdErr, Vector3.Distance(held, palm));
                // The card's root lies in its plane: its offset along the palm normal is the card's distance in front of the palm.
                _holdFront = Vector3.Dot(_held.position - palm, m.Normal);
                _holdDot = Vector3.Dot(_held.forward, stage);
                // Which face the camera sees: + = the face that reads left to right from in front (local +z).
                if (cam != null) _holdCamDot = Vector3.Dot(_held.forward, (cam.transform.position - _held.position).normalized);
            }
            var thumbBase = Bone(HandGrip.Thumb(_propRight, 0));
            if (thumbBase != null) _holdThumbBase = Vector3.Dot(thumbBase.position - m.At(0.5f), m.Normal);
            if (_propIsCard)
            {
                // Thumb tip's height above the card's bottom edge (the small text's lower edge is ~3.9 cm up).
                var t1 = Bone(HandGrip.Thumb(_propRight, 1));
                var t2 = Bone(HandGrip.Thumb(_propRight, 2));
                if (t1 != null && t2 != null) _holdThumbOnCard = Mathf.Max(_holdThumbOnCard, Vector3.Dot(HandGrip.Tip(t1, t2) - (_held.position + _held.up * CardBottom), _held.up));
            }
            _mirrored = _held.localToWorldMatrix.determinant < 0f;
        }

        /// <summary>"n/a (ff)": not measured, the frame skipped past the table (fast-forward).</summary>
        private static string Cm(float metres) => metres < 0f ? "n/a (ff)" : $"{metres * 100f:0.0} cm";

        private void LogRaise()
        {
            if (_holdFrames == 0)
            {
                Debug.Log($"[APL prop] {_who} {(_propIsCard ? "rtm" : "paddle")} grip: not measured (no hold frames or no finger bones)");
                return;
            }
            Debug.Log($"[APL prop] {_who} {(_propIsCard ? "rtm" : "paddle")} grip: stick-in-palm err {_holdErr * 100f:0.0} cm, face-forward dot {_holdDot:0.00}" +
                      $" | hand {(_propRight ? "R" : "L")}, palm-side {_holdFront * 100f:+0.0;-0.0} cm, thumb-base {_holdThumbBase * 100f:+0.0;-0.0} cm," +
                      $" {(_propIsCard ? "" : $"wrap {_holdWrap * 100f:0.0} cm, ")}thumb-tip {_holdThumb * 100f:0.0} cm{(_propIsCard ? $" (up {_holdThumbOnCard * 100f:0.0} cm on the card, text from ~3.9)" : "")}, cam-dot {_holdCamDot:0.00}," +
                      $" ik-rot resid {_ikResid:0}°{(_ikRotOff ? " (ik rot off)" : "")}, wrist clamp {_clampDeg:0}°, pickup gap {Cm(_pickupGap)}," +
                      $" release pop {Cm(_releasePop)}/{_releaseTurn:0}°, mirrored {(_mirrored ? "YES" : "no")}, hand {_handLen[_propRight ? 1 : 0] * 100f:0.0} cm, arm {_armLen:0.00} m");
            // The bone pass fixes any IK miss, but a large one means the goal calibration is wrong for this
            // rig and IK would twist the forearm: turn IK rotation off for the next raises.
            if (_ikResid > 25f && !_ikRotOff && _calibrated[_propRight ? 1 : 0])
            {
                _ikRotOff = true;
                Debug.Log($"[APL prop] {_who} ik rotation off (resid {_ikResid:0}° > 25°): the bone pass turns the hand alone from now on");
            }
        }
    }
}
