using UnityEngine;

namespace Apl.Client.Hall
{
    /// <summary>
    /// A hand's palm frame, measured from its humanoid bones (no per-avatar numbers):
    /// <see cref="Fingers"/> runs wrist → middle knuckle, <see cref="Across"/> index knuckle →
    /// little knuckle, and <see cref="Normal"/> points out of the palm (worked out per hand, so a
    /// left and a right hand both get their real palm side).
    /// </summary>
    public struct PalmFrame
    {
        public bool Right;
        public Vector3 Wrist, Fingers, Across, Normal;
        /// <summary>Wrist → middle knuckle, metres (scales every grip distance to the hand).</summary>
        public float Length;

        /// <summary>The frame as a rotation: forward = fingers, up = out of the palm.</summary>
        public Quaternion Rotation => Quaternion.LookRotation(Fingers, Normal);

        /// <summary>A point on the wrist → middle-knuckle line (0 = wrist, 1 = knuckle).</summary>
        public Vector3 At(float fraction) => Wrist + Fingers * (Length * fraction);
    }

    /// <summary>
    /// Rig-independent hand helpers for props held by seated members (the bidding paddle, the RTM
    /// card): measure the palm, close the fingers to set angles round the palm's own knuckle axis,
    /// aim the thumb. Every angle is measured from the bones each frame, so whatever the clip did
    /// to the hand, the result is the same.
    /// </summary>
    public static class HandGrip
    {
        // Index, middle, ring, little: proximal, intermediate, distal.
        private static readonly HumanBodyBones[,] RightFingers =
        {
            { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal },
            { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
            { HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal },
            { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal },
        };

        private static readonly HumanBodyBones[,] LeftFingers =
        {
            { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal },
            { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal },
            { HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal },
            { HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal },
        };

        public static HumanBodyBones Hand(bool right) => right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand;
        public static HumanBodyBones LowerArm(bool right) => right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm;
        public static HumanBodyBones UpperArm(bool right) => right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm;

        /// <summary>finger 0 = index … 3 = little; joint 0 = proximal … 2 = distal.</summary>
        public static HumanBodyBones Finger(bool right, int finger, int joint) => right ? RightFingers[finger, joint] : LeftFingers[finger, joint];

        public static HumanBodyBones Thumb(bool right, int joint) => joint switch
        {
            0 => right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal,
            1 => right ? HumanBodyBones.RightThumbIntermediate : HumanBodyBones.LeftThumbIntermediate,
            _ => right ? HumanBodyBones.RightThumbDistal : HumanBodyBones.LeftThumbDistal,
        };

        /// <summary>The palm frame of one hand as the bones are now. False when the rig lacks the finger bones.</summary>
        public static bool Measure(Animator a, bool right, out PalmFrame p)
        {
            p = default;
            p.Right = right;
            var hand = a.GetBoneTransform(Hand(right));
            var index = a.GetBoneTransform(Finger(right, 0, 0));
            var middle = a.GetBoneTransform(Finger(right, 1, 0));
            var little = a.GetBoneTransform(Finger(right, 3, 0));
            if (hand == null || index == null || middle == null || little == null) return false;
            var f = middle.position - hand.position;
            var len = f.magnitude;
            if (len < 1e-4f) return false;
            f /= len;
            var across = Vector3.ProjectOnPlane(little.position - index.position, f);
            if (across.sqrMagnitude < 1e-10f) return false;
            across.Normalize();
            p.Wrist = hand.position;
            p.Fingers = f;
            p.Across = across;
            p.Length = len;
            p.Normal = NormalFor(right, f, across);
            return true;
        }

        // A right hand in T-pose (facing +z): fingers +x, index at +z, palm -y — so the palm normal is
        // across × fingers for a right hand and fingers × across for a left one.
        public static Vector3 NormalFor(bool right, Vector3 fingers, Vector3 across) => right ? Vector3.Cross(across, fingers) : Vector3.Cross(fingers, across);

        /// <summary>The fingers direction that goes with a palm normal and an across axis (inverse of <see cref="NormalFor"/>).</summary>
        public static Vector3 FingersFor(bool right, Vector3 normal, Vector3 across) => right ? Vector3.Cross(normal, across) : Vector3.Cross(across, normal);

        /// <summary>The across axis (index → little knuckle) of a frame built from fingers and palm normal.</summary>
        public static Vector3 AcrossFor(bool right, Vector3 fingers, Vector3 normal) => right ? Vector3.Cross(fingers, normal) : Vector3.Cross(normal, fingers);

        /// <summary>A hand frame from its palm normal and across axis (index → little).</summary>
        public static Quaternion Frame(bool right, Vector3 normal, Vector3 across)
        {
            var f = FingersFor(right, normal, across);
            return Quaternion.LookRotation(f, normal);
        }

        /// <summary>
        /// Close the four fingers to set angles (degrees) about the palm's knuckle axis, blended by
        /// <paramref name="weight"/>: <paramref name="proximal"/> is the first phalanx's angle from the
        /// wrist → knuckle line, the other two are each joint's bend relative to the one before. Each
        /// finger from the index on closes <paramref name="spreadPerFinger"/> more (a fist's little finger is tightest).
        /// </summary>
        public static void Curl(Animator a, in PalmFrame p, float proximal, float intermediate, float distal, float weight, float spreadPerFinger = 4f)
        {
            if (weight <= 0.001f) return;
            // A positive turn about k takes the fingers into the palm (k × fingers = normal).
            var k = Vector3.Cross(p.Fingers, p.Normal);
            for (var finger = 0; finger < 4; finger++)
            {
                var b0 = a.GetBoneTransform(Finger(p.Right, finger, 0));
                var b1 = a.GetBoneTransform(Finger(p.Right, finger, 1));
                var b2 = a.GetBoneTransform(Finger(p.Right, finger, 2));
                if (b0 == null || b1 == null || b2 == null) continue;
                var spread = finger * spreadPerFinger;
                var seg0 = b1.position - b0.position;
                var cur = Vector3.SignedAngle(p.Fingers, Vector3.ProjectOnPlane(seg0, k), k);
                b0.rotation = Quaternion.AngleAxis((proximal + spread - cur) * weight, k) * b0.rotation;

                seg0 = b1.position - b0.position;
                var seg1 = b2.position - b1.position;
                cur = Vector3.SignedAngle(Vector3.ProjectOnPlane(seg0, k), Vector3.ProjectOnPlane(seg1, k), k);
                b1.rotation = Quaternion.AngleAxis((intermediate - cur) * weight, k) * b1.rotation;

                // The distal bone has no child: its direction is the same local axis the intermediate points along.
                seg1 = b2.position - b1.position;
                var dir = b2.rotation * (Quaternion.Inverse(b1.rotation) * seg1);
                cur = Vector3.SignedAngle(Vector3.ProjectOnPlane(seg1, k), Vector3.ProjectOnPlane(dir, k), k);
                b2.rotation = Quaternion.AngleAxis((distal - cur) * weight, k) * b2.rotation;
            }
        }

        /// <summary>Estimated tip of a finger chain's last bone (its own length again along its axis, a little short).</summary>
        public static Vector3 Tip(Transform middle, Transform last)
        {
            var seg = last.position - middle.position;
            return last.position + last.rotation * (Quaternion.Inverse(middle.rotation) * seg) * 0.8f;
        }

        /// <summary>
        /// Swing the thumb (from its base) so its tip heads for <paramref name="target"/>, at most
        /// <paramref name="maxDegrees"/>. Returns the tip's distance from the target afterwards (m), or -1.
        /// </summary>
        public static float AimThumb(Animator a, bool right, Vector3 target, float weight, float maxDegrees = 70f)
        {
            var t0 = a.GetBoneTransform(Thumb(right, 0));
            var t1 = a.GetBoneTransform(Thumb(right, 1));
            var t2 = a.GetBoneTransform(Thumb(right, 2));
            if (t0 == null || t1 == null || t2 == null) return -1f;
            if (weight > 0.001f)
            {
                var from = Tip(t1, t2) - t0.position;
                var to = target - t0.position;
                Quaternion.FromToRotation(from, to).ToAngleAxis(out var angle, out var axis);
                if (angle > 180f) angle -= 360f;
                if (Mathf.Abs(angle) > 0.01f && !float.IsInfinity(axis.x) && !float.IsNaN(axis.x))
                {
                    t0.rotation = Quaternion.AngleAxis(Mathf.Clamp(angle, -maxDegrees, maxDegrees) * weight, axis) * t0.rotation;
                }
            }
            return Vector3.Distance(Tip(t1, t2), target);
        }

        /// <summary>Distance from point <paramref name="p"/> to the line through <paramref name="origin"/> along unit <paramref name="dir"/>.</summary>
        public static float LineDistance(Vector3 p, Vector3 origin, Vector3 dir) => Vector3.Cross(p - origin, dir).magnitude;
    }
}
