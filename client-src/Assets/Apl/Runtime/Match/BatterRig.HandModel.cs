using Apl.Client.Hall;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Round 14, the owner's "hand, elbow, bat alignment is wrong": the palm grip.
    ///
    /// What was wrong (tools/motions/batgrip_check.py over every bat clip): the generator — and the game copying it
    /// (OrientHand) — held the bat with the index knuckle toward the handle TOP (100 % of hand-frames) and the
    /// fingers straight across the handle whatever the forearm did, so the hand sat 60–175° off the forearm
    /// (median ~110°; a human wrist bends ~50–70°) with the handle on the back of the hand. A real grip: thumbs and
    /// index fingers toward the blade, the handle diagonally across the palm (blade end toward the fingers), the
    /// hand roughly continuing the forearm, the palms facing each other round the handle.
    ///
    /// Per hand, every frame (after the animator): the hand frame is built from the handle and the forearm — the
    /// fingers <see cref="GripDiagonal"/>° down the handle on the face's side (the "V"s between splice and outside
    /// edge), turned round the handle toward the forearm until the hand is at most <see cref="MaxWristBend"/>° off
    /// it; the wrist goal is where that palm puts the handle in the fingers; a two-bone IK reaches it with the
    /// elbow's flexion held to 4–145°; the elbow is swivelled about the shoulder→wrist line (±60°, eased by a
    /// cost on the swivel and on last frame's elbow direction, so it never flips) until the forearm lines up with
    /// the grip; then the hand is turned onto the handle and half of that turn's twist goes to the forearm
    /// (pronation lives in the forearm, not in a candy-wrapped wrist). All measured from the bones: any skeleton.
    /// </summary>
    public sealed partial class BatterRig
    {
        /// <summary>The palm grip (off with <c>-aploldgrip</c>: the round-10 OrientHand path, for A/B captures).</summary>
        public static readonly bool PalmGrip = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-aploldgrip") < 0;

        /// <summary>The handle across the palm: the fingers this far down the handle from square (degrees).</summary>
        private const float GripDiagonal = 25f;
        /// <summary>The most the hand turns off the forearm (flexion and deviation together, degrees).</summary>
        private const float MaxWristBend = 50f;
        private const float ElbowFlexMax = 145f, ElbowFlexMin = 4f;
        /// <summary>Each hand's middle on the handle, metres below its top (the generator's: bottom 18 cm, top 8.5 cm).</summary>
        private const float BottomAlong = 0.18f, TopAlong = 0.085f;
        /// <summary>The handle's centre from the wrist, in hand lengths (wrist → middle knuckle): along the fingers, and out of the palm.</summary>
        private const float FingerReach = 0.95f, PalmDepth = 0.30f;
        /// <summary>Of the hand's turn about the forearm, the share the forearm takes (pronation / supination).</summary>
        private const float ForearmTwistShare = 0.5f;

        /// <summary>Per arm (0 bottom, 1 top): where the elbow pointed last frame (his local frame) and when.</summary>
        private readonly Vector3[] _elbowPt = new Vector3[2];
        private readonly float[] _elbowPtAt = { -1f, -1f };

        /// <summary>The bat pushed out of his body (eased), world metres.</summary>
        private Vector3 _bodyPush;

        /// <summary>The fingers' direction for a hand on this handle: square to it on the face's side, tipped down it.</summary>
        private static Vector3 FaceFingers(Vector3 trackFingers, Vector3 axis)
        {
            var perp = Vector3.ProjectOnPlane(trackFingers, axis);
            if (perp.sqrMagnitude < 1e-8f) perp = Vector3.ProjectOnPlane(Vector3.forward, axis);
            var t = GripDiagonal * Mathf.Deg2Rad;
            return (perp.normalized * Mathf.Cos(t) + axis * Mathf.Sin(t)).normalized;
        }

        /// <summary>The fingers turned round the handle toward the forearm until the hand is no more than MaxWristBend off it.</summary>
        private static Vector3 LimitBend(Vector3 want, Vector3 forearm, Vector3 axis)
        {
            if (Vector3.Angle(forearm, want) <= MaxWristBend) return want;
            var up = Vector3.ProjectOnPlane(forearm, axis);
            if (up.sqrMagnitude < 1e-8f) return want;
            var fp = Vector3.ProjectOnPlane(want, axis).normalized;
            var along = Vector3.Dot(want, axis);
            var across = Mathf.Sqrt(Mathf.Max(0f, 1f - along * along));
            var full = Vector3.SignedAngle(fp, up, axis);
            for (var i = 1; i <= 10; i++)
            {
                var f = Quaternion.AngleAxis(full * i / 10f, axis) * fp * across + axis * along;
                if (Vector3.Angle(forearm, f) <= MaxWristBend) return f.normalized;
            }
            var best = (Quaternion.AngleAxis(full, axis) * fp * across + axis * along).normalized;
            return Vector3.Slerp(forearm.normalized, best, MaxWristBend / Mathf.Max(MaxWristBend, Vector3.Angle(forearm, best))).normalized;
        }

        /// <summary>The palm's normal for fingers <paramref name="f"/> on a handle down <paramref name="axis"/>, index toward the blade.</summary>
        private static Vector3 PalmNormal(bool boneRight, Vector3 f, Vector3 axis)
        {
            var index = Vector3.ProjectOnPlane(axis, f).normalized;
            return HandGrip.NormalFor(boneRight, f, -index).normalized;
        }

        /// <summary>
        /// One hand onto the handle at <paramref name="along"/> below <paramref name="handleTop"/>: wrist goal from the palm,
        /// two-bone IK, elbow swivel, hand turned on. False when the rig lacks the finger bones (the caller falls back).
        /// </summary>
        private bool GripArm(int arm, Transform up, Transform low, Transform hand, bool boneRight, Vector3 handleTop, Vector3 axis, Vector3 faceFingers, float along, float w)
        {
            if (!HandGrip.Measure(_animator, boneRight, out var palm)) return false;
            var length = palm.Length;
            var pc = handleTop + axis * along;
            var start = hand.position;
            for (var it = 0; it < 2; it++)
            {
                var u = (hand.position - low.position).normalized;
                var f = LimitBend(faceFingers, u, axis);
                var n = PalmNormal(boneRight, f, axis);
                var wrist = pc - f * (FingerReach * length) - n * (PalmDepth * length);
                TwoBoneLimited(up, low, hand, Vector3.Lerp(start, wrist, w));
                if (it == 0) Swivel(arm, up, low, hand, faceFingers, w);
            }
            var uf = (hand.position - low.position).normalized;
            var ff = LimitBend(faceFingers, uf, axis);
            TurnHand(low, hand, boneRight, ff, PalmNormal(boneRight, ff, axis), w);
            return true;
        }

        /// <summary>Where this hand holds a handle now: the handle's centre in its fingers.</summary>
        private bool HeldPoint(bool boneRight, out Vector3 at)
        {
            at = default;
            if (!HandGrip.Measure(_animator, boneRight, out var p)) return false;
            at = p.At(FingerReach) + p.Normal * (PalmDepth * p.Length);
            return true;
        }

        /// <summary>
        /// The elbow round the shoulder→wrist line (the wrist stays put) to where the forearm best continues the grip,
        /// with a cost on the swivel itself and on turning away from last frame's elbow (no flips).
        /// </summary>
        private void Swivel(int arm, Transform up, Transform low, Transform hand, Vector3 faceFingers, float w)
        {
            var s = up.position;
            var sw = hand.position - s;
            if (sw.sqrMagnitude < 1e-6f) return;
            var axis = sw.normalized;
            var e0 = low.position - s;
            var hasLast = _elbowPtAt[arm] >= 0f && Time.time - _elbowPtAt[arm] < 0.25f;
            var last = hasLast ? transform.TransformDirection(_elbowPt[arm]) : Vector3.zero;
            var best = 0f;
            var bestCost = float.MaxValue;
            for (var phi = -60f; phi <= 60.01f; phi += 7.5f)
            {
                var e = Quaternion.AngleAxis(phi, axis) * e0;
                var cost = Vector3.Angle(hand.position - (s + e), faceFingers) + 0.3f * Mathf.Abs(phi);
                if (hasLast) cost += 0.6f * Vector3.Angle(Vector3.ProjectOnPlane(e, axis), last);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = phi;
                }
            }
            up.rotation = Quaternion.AngleAxis(best * w, axis) * up.rotation;
            var pt = Vector3.ProjectOnPlane(low.position - s, axis);
            if (pt.sqrMagnitude > 1e-8f)
            {
                _elbowPt[arm] = transform.InverseTransformDirection(pt.normalized);
                _elbowPtAt[arm] = Time.time;
            }
        }

        /// <summary>The hand turned so its palm frame is (<paramref name="f"/>, <paramref name="n"/>); half the twist about the forearm goes to the forearm.</summary>
        private void TurnHand(Transform low, Transform hand, bool boneRight, Vector3 f, Vector3 n, float w)
        {
            if (!HandGrip.Measure(_animator, boneRight, out var p)) return;
            var turn = Quaternion.LookRotation(f, n) * Quaternion.Inverse(Quaternion.LookRotation(p.Fingers, p.Normal));
            turn = Quaternion.Slerp(Quaternion.identity, turn, Mathf.Clamp01(w));
            var want = turn * hand.rotation;
            var u = hand.position - low.position;
            if (u.sqrMagnitude > 1e-8f)
            {
                u.Normalize();
                low.rotation = Quaternion.AngleAxis(TwistAbout(turn, u) * ForearmTwistShare, u) * low.rotation;
            }
            hand.rotation = want;
        }

        /// <summary>The signed twist (degrees) of <paramref name="q"/> about <paramref name="axis"/> (swing-twist split).</summary>
        private static float TwistAbout(Quaternion q, Vector3 axis)
        {
            var p = Vector3.Project(new Vector3(q.x, q.y, q.z), axis);
            var t = new Quaternion(p.x, p.y, p.z, q.w);
            var mag = Mathf.Sqrt(t.x * t.x + t.y * t.y + t.z * t.z + t.w * t.w);
            if (mag < 1e-6f) return 0f;
            t = new Quaternion(t.x / mag, t.y / mag, t.z / mag, t.w / mag);
            t.ToAngleAxis(out var angle, out var ax);
            if (float.IsNaN(ax.x) || float.IsInfinity(ax.x)) return 0f;
            if (angle > 180f) angle -= 360f;
            return Vector3.Dot(ax, axis) >= 0f ? angle : -angle;
        }

        /// <summary>Two-bone IK in the limb's own bend plane, the elbow's flexion held to ElbowFlexMin..ElbowFlexMax.</summary>
        private static void TwoBoneLimited(Transform a, Transform b, Transform c, Vector3 target)
        {
            var la = Vector3.Distance(a.position, b.position);
            var lb = Vector3.Distance(b.position, c.position);
            if (la < 1e-4f || lb < 1e-4f) return;
            float Reach(float flex) => Mathf.Sqrt(la * la + lb * lb - 2f * la * lb * Mathf.Cos((180f - flex) * Mathf.Deg2Rad));
            var d = Mathf.Clamp(Vector3.Distance(a.position, target), Reach(ElbowFlexMax), Reach(ElbowFlexMin));
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

        /// <summary>
        /// The bat in one hand (runs, celebrations, the walk off): the wrist eased to at most 35° off the forearm,
        /// then the handle in the fingers, diagonally across the palm, index toward the blade, the face where the palm looks.
        /// </summary>
        private bool PalmHold(Transform low, Transform hand, bool boneRight, out Vector3 top, out Quaternion rot)
        {
            top = default;
            rot = Quaternion.identity;
            if (!HandGrip.Measure(_animator, boneRight, out var p)) return false;
            var u = hand.position - low.position;
            var bend = Vector3.Angle(u, p.Fingers);
            if (u.sqrMagnitude > 1e-8f && bend > 35f)
            {
                hand.rotation = Quaternion.FromToRotation(p.Fingers, Vector3.Slerp(u.normalized, p.Fingers, 35f / bend)) * hand.rotation;
                if (!HandGrip.Measure(_animator, boneRight, out p)) return false;
            }
            var t = GripDiagonal * Mathf.Deg2Rad;
            var axis = (-p.Across * Mathf.Cos(t) + p.Fingers * Mathf.Sin(t)).normalized;
            var pc = p.At(FingerReach) + p.Normal * (PalmDepth * p.Length);
            top = pc - axis * BottomAlong;
            var face = Vector3.ProjectOnPlane(p.Normal, axis);
            rot = face.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(face.normalized, -axis) : Quaternion.FromToRotation(Vector3.down, axis);
            return true;
        }

        /// <summary>The bat placed in the bottom hand's palm, off the legs (the no-socket carries); false if the rig can't say where the palm is.</summary>
        private bool PalmInHand(Transform bottom)
        {
            var low = _animator.GetBoneTransform(BottomBone(1));
            if (low == null || !PalmHold(low, bottom, !LeftHanded, out var top, out var rot)) return false;
            var cleared = ClearOfLegs(top, rot);
            top = bottom.position + cleared * Quaternion.Inverse(rot) * (top - bottom.position);
            _grip = top;
            _batDir = cleared * Vector3.down;
            _bat.SetPositionAndRotation(top, cleared);
            return true;
        }

        // ---------------------------------------------------------------- blade out of the body

        private const float BatLength = 0.86f;

        /// <summary>The body as capsules (torso, head, thighs, shins): ends and radius.</summary>
        private System.Collections.Generic.IEnumerable<(Vector3 a, Vector3 b, float r)> BodyCapsules()
        {
            Transform? B(HumanBodyBones h) => _animator.GetBoneTransform(h);
            var hips = B(HumanBodyBones.Hips);
            var chest = B(HumanBodyBones.Chest) ?? B(HumanBodyBones.Spine);
            var neck = B(HumanBodyBones.Neck);
            var head = B(HumanBodyBones.Head);
            if (hips != null && chest != null) yield return (hips.position, chest.position, 0.12f);
            if (chest != null && neck != null) yield return (chest.position, neck.position, 0.11f);
            if (head != null) yield return (head.position + Vector3.up * 0.04f, head.position + Vector3.up * 0.14f, 0.10f);
            foreach (var (u, l, f) in new[] { (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot), (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot) })
            {
                var ub = B(u);
                var lb = B(l);
                var fb = B(f);
                if (ub != null && lb != null) yield return (ub.position, lb.position, 0.075f);
                if (lb != null && fb != null) yield return (lb.position, fb.position, 0.065f);
            }
        }

        /// <summary>How deep the blade (35–100 % down from the top) is inside the body, and the push that takes it out (m).</summary>
        private float BladeInBody(Vector3 top, Vector3 axis, out Vector3 push)
        {
            push = Vector3.zero;
            var worst = 0f;
            for (var i = 0; i <= 7; i++)
            {
                var p = top + axis * (BatLength * Mathf.Lerp(0.35f, 1f, i / 7f));
                foreach (var (a, b, r) in BodyCapsules())
                {
                    var ab = b - a;
                    var c = a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    var d = p - c;
                    var pen = r - d.magnitude;
                    if (pen <= worst) continue;
                    worst = pen;
                    var dir = Vector3.ProjectOnPlane(d.sqrMagnitude > 1e-8f ? d : transform.forward, axis);
                    push = (dir.sqrMagnitude > 1e-8f ? dir.normalized : transform.forward) * (pen + 0.015f);
                }
            }
            return worst;
        }

        /// <summary>The tracked bat slid out of thighs, pads, torso and head (the hands follow it), eased so it never pops.</summary>
        private void ClearOfBody(ref Vector3 batTop, Vector3 axis)
        {
            var want = Vector3.zero;
            for (var pass = 0; pass < 3; pass++)
            {
                if (BladeInBody(batTop + want, axis, out var push) <= 0f) break;
                want += push;
            }
            // Out quickly, back slowly.
            var rate = want.magnitude > _bodyPush.magnitude ? 3f : 0.8f;
            _bodyPush = Vector3.MoveTowards(_bodyPush, want, rate * Time.deltaTime);
            if (Time.deltaTime <= 0f) _bodyPush = want;
            batTop += _bodyPush;
        }

        // ---------------------------------------------------------------- measurement (-aplbatgrip, -aplbatcap)

        /// <summary>One frame's grip as rendered, measured from the final bones and the bat (independent of the solver).</summary>
        public struct GripMeasure
        {
            public float BottomGap, TopGap, BottomWrist, TopWrist, BottomBend, TopBend, BottomElbow, TopElbow, Pen;
            public bool TopOn, BottomPalm, TopPalm, BottomIndexDown, TopIndexDown;

            /// <summary>Thresholds: knuckles ≤ 5 cm from the handle's line, forearm→blade ≤ 115°, hand ≤ 55° off the forearm, elbow 3–150°, handle in the palm, index toward the blade, blade ≤ 1.5 cm into the body.</summary>
            public bool Pass =>
                Hand(BottomGap, BottomWrist, BottomBend, BottomElbow, BottomPalm, BottomIndexDown)
                && (!TopOn || Hand(TopGap, TopWrist, TopBend, TopElbow, TopPalm, TopIndexDown))
                && Pen <= 0.015f;

            private static bool Hand(float gap, float wrist, float bend, float elbow, bool palm, bool index) =>
                gap <= 0.05f && wrist <= 115f && bend <= 55f && elbow >= 3f && elbow <= 150f && palm && index;

            public override string ToString() =>
                $"bottomHandGap={BottomGap * 100:0.0}cm topHandGap={(TopOn ? (TopGap * 100).ToString("0.0") + "cm" : "off")} " +
                $"wristDeg={BottomWrist:0}/{(TopOn ? TopWrist.ToString("0") : "-")} bendDeg={BottomBend:0}/{(TopOn ? TopBend.ToString("0") : "-")} " +
                $"elbowDeg={BottomElbow:0}/{TopElbow:0} palm={(BottomPalm ? "in" : "OUT")}/{(TopOn ? TopPalm ? "in" : "OUT" : "-")} " +
                $"index={(BottomIndexDown ? "down" : "UP")}/{(TopOn ? TopIndexDown ? "down" : "UP" : "-")} pen={Pen * 100:0.0}cm {(Pass ? "PASS" : "FAIL")}";
        }

        /// <summary>The grip as it is on screen now (call after LateUpdate: end of frame).</summary>
        public GripMeasure MeasureGrip()
        {
            var m = new GripMeasure { TopOn = UsesClips && _trackWeight > 0.5f && _track.TopOn };
            if (_bat == null || _animator == null) return m;
            var axis = _bat.rotation * Vector3.down;
            var top = _bat.position;
            for (var i = 0; i < 2; i++)
            {
                var boneRight = i == 0 ? !LeftHanded : LeftHanded;
                var up = _animator.GetBoneTransform(i == 0 ? BottomBone(0) : TopBone(0));
                var low = _animator.GetBoneTransform(i == 0 ? BottomBone(1) : TopBone(1));
                var hand = _animator.GetBoneTransform(i == 0 ? BottomBone(2) : TopBone(2));
                if (up == null || low == null || hand == null || !HandGrip.Measure(_animator, boneRight, out var p)) continue;
                var k = Vector3.zero;
                for (var f = 0; f < 4; f++)
                {
                    var b = _animator.GetBoneTransform(HandGrip.Finger(boneRight, f, 0));
                    if (b != null) k += b.position / 4f;
                }
                var gap = Vector3.Cross(k - top, axis).magnitude;
                var fore = hand.position - low.position;
                var wrist = Vector3.Angle(fore, axis);
                var bend = Vector3.Angle(fore, p.Fingers);
                var elbow = Vector3.Angle(low.position - up.position, fore);
                var near = top + axis * Vector3.Dot(k - top, axis);
                var palmIn = Vector3.Dot(near - p.At(0.5f), p.Normal) > 0f;
                var indexDown = Vector3.Dot(-p.Across, axis) > 0f;
                if (i == 0) { m.BottomGap = gap; m.BottomWrist = wrist; m.BottomBend = bend; m.BottomElbow = elbow; m.BottomPalm = palmIn; m.BottomIndexDown = indexDown; }
                else { m.TopGap = gap; m.TopWrist = wrist; m.TopBend = bend; m.TopElbow = elbow; m.TopPalm = palmIn; m.TopIndexDown = indexDown; }
            }
            m.Pen = Mathf.Max(0f, BladeInBody(top, axis, out _));
            return m;
        }
    }
}
