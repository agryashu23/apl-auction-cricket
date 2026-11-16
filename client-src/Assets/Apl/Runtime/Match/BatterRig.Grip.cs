using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Both hands ON the bat, every frame. The bat is drawn from its authored track, and
    /// the IK pass only asks the humanoid IK to bring the wrists there — which it does
    /// only within the avatar's muscle limits and arm length, and not at all if another
    /// IK component on the body has its say last. So after the animator (here, in
    /// LateUpdate) the arms are solved again directly (two-bone, keeping the elbow's
    /// own bend plane): the bottom wrist onto the track's grip, then the bat is moved
    /// to wherever that wrist really is (the bat is in the bottom hand, always), then
    /// the top wrist onto the handle when the track has the top hand on it.
    /// </summary>
    public sealed partial class BatterRig
    {
        /// <summary>Grip log (batting capture): until when, for the striker (from the guard to the end of the follow-through).</summary>
        private float _gripLogUntil;

        /// <summary>How far each wrist was from its grip before the fix-up and after it (m), and how far the bat had to follow the bottom hand: the last frame's.</summary>
        public (float bottomBefore, float topBefore, float bottomAfter, float topAfter, float batMoved) GripError { get; private set; }

        private HumanBodyBones BottomBone(int i) => LeftHanded
            ? (i == 0 ? HumanBodyBones.LeftUpperArm : i == 1 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.LeftHand)
            : (i == 0 ? HumanBodyBones.RightUpperArm : i == 1 ? HumanBodyBones.RightLowerArm : HumanBodyBones.RightHand);

        private HumanBodyBones TopBone(int i) => LeftHanded
            ? (i == 0 ? HumanBodyBones.RightUpperArm : i == 1 ? HumanBodyBones.RightLowerArm : HumanBodyBones.RightHand)
            : (i == 0 ? HumanBodyBones.LeftUpperArm : i == 1 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.LeftHand);

        /// <summary>
        /// The bat at <paramref name="batTop"/> (the handle's top, drawn from the track): the
        /// wrists onto it, and the bat onto the bottom wrist if it couldn't quite get there.
        /// </summary>
        private void Grip(ref Vector3 batTop, Quaternion batRot)
        {
            var w = Mathf.Clamp01(_trackWeight);
            var u = _animator.GetBoneTransform(BottomBone(0));
            var l = _animator.GetBoneTransform(BottomBone(1));
            var h = _animator.GetBoneTransform(BottomBone(2));
            if (u == null || l == null || h == null || w <= 0f) return;
            if (PalmGrip && PalmGripBoth(ref batTop, batRot, w, u, l, h)) return;
            // The track's goals, carried with the bat (it may have been blended toward the socket).
            var carry = batTop - _track.Top;
            var bottomGoal = _track.BottomHand + carry;
            var topGoal = _track.TopHand + carry;
            var tu = _animator.GetBoneTransform(TopBone(0));
            var tl = _animator.GetBoneTransform(TopBone(1));
            var th = _animator.GetBoneTransform(TopBone(2));
            var bottomBefore = Vector3.Distance(h.position, bottomGoal);
            var topBefore = th != null ? Vector3.Distance(th.position, topGoal) : -1f;
            TwoBone(u, l, h, Vector3.Lerp(h.position, bottomGoal, w));
            OrientHand(h, false, w);
            // Whatever the arm couldn't reach, the bat comes to the hand.
            var moved = (h.position - bottomGoal) * w;
            batTop += moved;
            topGoal += moved;
            var topAfter = -1f;
            if (_track.TopOn && tu != null && tl != null && th != null)
            {
                TwoBone(tu, tl, th, Vector3.Lerp(th.position, topGoal, w));
                OrientHand(th, true, w);
                topAfter = Vector3.Distance(th.position, topGoal);
            }
            GripError = (bottomBefore, topBefore, Vector3.Distance(h.position, bottomGoal + moved), topAfter, moved.magnitude);
        }

        /// <summary>
        /// Round 14 (BatterRig.HandModel.cs): the bat out of the body, the bottom hand onto the handle (the bat then
        /// follows whatever the arm couldn't reach), then the top hand when the track has it on.
        /// </summary>
        private bool PalmGripBoth(ref Vector3 batTop, Quaternion batRot, float w, Transform u, Transform l, Transform h)
        {
            var axis = batRot * Vector3.down;
            var fingers = FaceFingers(_track.Fingers, axis);
            ClearOfBody(ref batTop, axis);
            var bottomRight = !LeftHanded;
            var bottomGoal = batTop + axis * BottomAlong;
            var bottomBefore = HeldPoint(bottomRight, out var held0) ? Vector3.Distance(held0, bottomGoal) : -1f;
            if (!GripArm(0, u, l, h, bottomRight, batTop, axis, fingers, BottomAlong, w)) return false;
            var moved = Vector3.zero;
            if (HeldPoint(bottomRight, out var held)) moved = (held - bottomGoal) * w;
            batTop += moved;
            var tu = _animator.GetBoneTransform(TopBone(0));
            var tl = _animator.GetBoneTransform(TopBone(1));
            var th = _animator.GetBoneTransform(TopBone(2));
            var topBefore = -1f;
            var topAfter = -1f;
            if (_track.TopOn && tu != null && tl != null && th != null)
            {
                var topGoal = batTop + axis * TopAlong;
                if (HeldPoint(LeftHanded, out var t0)) topBefore = Vector3.Distance(t0, topGoal);
                GripArm(1, tu, tl, th, LeftHanded, batTop, axis, fingers, TopAlong, w);
                if (HeldPoint(LeftHanded, out var t1)) topAfter = Vector3.Distance(t1, topGoal);
            }
            var bottomAfter = HeldPoint(bottomRight, out var held2) ? Vector3.Distance(held2, batTop + axis * BottomAlong) : -1f;
            GripError = (bottomBefore, topBefore, bottomAfter, topAfter, moved.magnitude);
            return true;
        }

        /// <summary>Round 10: the hands turned onto the handle as authored (off: the retargeted hand twist, as before).</summary>
        public static bool HandsOnHandle = true;

        /// <summary>
        /// The hand turned onto the handle as the generator gripped it (round 10, the owner's "bat handling"):
        /// Unity's humanoid retarget keeps where the wrist is but drops the hand's twist, so in game the
        /// knuckles wandered round the handle (and nothing put them back). The generator's grip, measured off
        /// its own solved bones: wrist → middle knuckle along the track's finger direction, and the knuckle line
        /// (little → index knuckle) along the handle, index toward the top. Both are bone positions, so this
        /// holds for any skeleton; the wrist stays where it is.
        /// </summary>
        private void OrientHand(Transform hand, bool top, float w)
        {
            if (!HandsOnHandle || w <= 0f) return;
            var left = LeftHanded ? !top : top;
            var mid = _animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            var index = _animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            var little = _animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            if (mid == null || index == null || little == null) return;
            var f = mid.position - hand.position;
            var k = index.position - little.position;
            var fWant = _track.Fingers;
            var kWant = Vector3.ProjectOnPlane(-_track.Axis, fWant);
            if (f.sqrMagnitude < 1e-6f || fWant.sqrMagnitude < 1e-6f || kWant.sqrMagnitude < 1e-6f) return;
            k = Vector3.ProjectOnPlane(k, f);
            if (k.sqrMagnitude < 1e-6f) return;
            var turn = Quaternion.LookRotation(fWant, kWant) * Quaternion.Inverse(Quaternion.LookRotation(f, k));
            hand.rotation = Quaternion.Slerp(Quaternion.identity, turn, Mathf.Clamp01(w)) * hand.rotation;
        }

        /// <summary>
        /// Two-bone IK: bend the middle joint so the end can reach <paramref name="target"/>,
        /// in the plane the limb is already bent in, then swing the root to point it there.
        /// Out of reach: the limb straightens toward it.
        /// </summary>
        private static void TwoBone(Transform a, Transform b, Transform c, Vector3 target)
        {
            var la = Vector3.Distance(a.position, b.position);
            var lb = Vector3.Distance(b.position, c.position);
            if (la < 1e-4f || lb < 1e-4f) return;
            var d = Mathf.Clamp(Vector3.Distance(a.position, target), Mathf.Abs(la - lb) + 1e-3f, la + lb - 1e-3f);
            var ba = a.position - b.position;
            var bc = c.position - b.position;
            var axis = Vector3.Cross(ba, bc);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(ba, Vector3.up);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(ba, Vector3.forward);
            var want = Mathf.Acos(Mathf.Clamp((la * la + lb * lb - d * d) / (2f * la * lb), -1f, 1f));
            var now = Vector3.Angle(ba, bc) * Mathf.Deg2Rad;
            b.rotation = Quaternion.AngleAxis((want - now) * Mathf.Rad2Deg, axis.normalized) * b.rotation;
            var toEnd = c.position - a.position;
            var toTarget = target - a.position;
            if (toEnd.sqrMagnitude > 1e-8f && toTarget.sqrMagnitude > 1e-8f) a.rotation = Quaternion.FromToRotation(toEnd, toTarget) * a.rotation;
        }

        /// <summary>How far the socketed bat is turned off his legs (eased, so it never pops).</summary>
        private Quaternion _legClear = Quaternion.identity;

        /// <summary>
        /// The bat in the hand socket (runs, celebrations — clips with no bat track) hangs
        /// straight down the bottom hand's side and so often down the back of the pad: from
        /// the batting close-ups it read as "the bat's gone" (the left-hander's run start).
        /// Swing the blade off whichever leg it is inside (toe away from the nearest point of
        /// thigh or shin) until it clears by 14 cm, eased over a few frames.
        /// </summary>
        private Quaternion ClearOfLegs(Vector3 top, Quaternion rot)
        {
            var want = Quaternion.identity;
            for (var pass = 0; pass < 2; pass++)
            {
                var r = want * rot;
                var axis = r * Vector3.down;
                var worst = 0f;
                var push = Vector3.zero;
                var at = Vector3.zero;
                foreach (var side in new[] { (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot), (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot) })
                {
                    var u = _animator.GetBoneTransform(side.Item1);
                    var l = _animator.GetBoneTransform(side.Item2);
                    var f = _animator.GetBoneTransform(side.Item3);
                    if (u == null || l == null || f == null) continue;
                    foreach (var k in new[] { 0.45f, 0.65f, 0.86f })
                    {
                        var p = top + axis * k;
                        foreach (var (a, b) in new[] { (u.position, l.position), (l.position, f.position) })
                        {
                            var ab = b - a;
                            var c = a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                            var d = p - c;
                            var pen = 0.14f - d.magnitude;
                            if (pen <= worst) continue;
                            worst = pen;
                            push = d.sqrMagnitude > 1e-6f ? d.normalized * pen : transform.forward * pen;
                            at = p;
                        }
                    }
                }
                if (worst <= 0f) break;
                want = Quaternion.FromToRotation(at - top, at + push - top) * want;
            }
            _legClear = Quaternion.Slerp(_legClear, want, 1f - Mathf.Exp(-Time.deltaTime * 14f));
            return _legClear * rot;
        }

        /// <summary>
        /// Round 10 safety net (r9 capture 194: a bat lying on the pitch metres from the diving batter): whatever
        /// placed it this frame, a bat more than 1.6 m from his hips goes back into his bottom hand, blade down.
        /// </summary>
        private void KeepBatWithHim()
        {
            if (_bat == null || !_bat.gameObject.activeInHierarchy || !UsesClips) return;
            var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            var hand = _animator.GetBoneTransform(LeftHanded ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (hips == null || hand == null || Vector3.Distance(_bat.position, hips.position) < 1.6f) return;
            if (!_batAwayLogged)
            {
                _batAwayLogged = true;
                Debug.Log($"[APL] bat {Vector3.Distance(_bat.position, hips.position):0.0} m from {name}'s hips (state {_animator.GetCurrentAnimatorStateInfo(0).shortNameHash}, track {_trackWeight:0.00}, carry {_carry}, free {_free}): back in his hand");
            }
            _bat.position = hand.position;
            _bat.rotation = Quaternion.FromToRotation(Vector3.down, (Vector3.down + transform.forward * 0.4f).normalized);
            _grip = _bat.position;
            _batDir = _bat.rotation * Vector3.down;
        }

        private bool _batAwayLogged;

        /// <summary>A batter in the play always has his bat (a ceremony may have hidden it).</summary>
        private void ShowBat()
        {
            if (_bat != null && !_bat.gameObject.activeSelf && isActiveAndEnabled) _bat.gameObject.SetActive(true);
        }

        /// <summary>
        /// Switched off (out, the innings over, a ceremony): his coroutines die with it, so
        /// nothing must be left half-done — a clock leaves the animator slowed (or stopped:
        /// the walk home scrubs at speed 0), a glide leaves the track drawn from an old place.
        /// </summary>
        private void ForgetRunning()
        {
            _settle = _after = _glide = _clock = _reach = _footGlide = _swing = null;
            _fromFrame = _toFrame = null;
            _lane = null;
            _along = null;
            _approach = null;
            if (_animator != null) _animator.speed = 1f;
        }

        /// <summary>Batting capture: one line a frame — states, track weight, IK weights, each wrist's distance to its grip.</summary>
        private void LogGrip()
        {
            if (!Apl.Client.Dev.BatCapture.On || Time.time > _gripLogUntil || !UsesClips) return;
            var cur = _animator.GetCurrentAnimatorStateInfo(0);
            var trans = _animator.IsInTransition(0);
            var next = trans ? _animator.GetNextAnimatorStateInfo(0).shortNameHash : 0;
            var e = GripError;
            Apl.Client.Dev.BatCapture.Note($"   grip {(LeftHanded ? "L" : "R")} state {cur.shortNameHash}@{cur.normalizedTime:0.00}{(trans ? $" -> {next} w{_animator.GetAnimatorTransitionInfo(0).normalizedTime:0.00}" : "")} speed {_animator.speed:0.00} track {_trackWeight:0.00} ik bottom {_trackWeight:0.00} top {(_track.TopOn ? _trackWeight : 0f):0.00} | wrist-grip before: bottom {e.bottomBefore:0.000} top {e.topBefore:0.000} after: bottom {e.bottomAfter:0.000} top {e.topAfter:0.000} | bat moved {e.batMoved:0.000} | [APL batgrip] {MeasureGrip()}");
        }
    }
}
