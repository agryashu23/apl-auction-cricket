using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Playing a clip on anyone from where they are. The cricket clips carry
    /// their travel in the pose (root motion baked), so after a run-up, a dive
    /// or a celebration the body can be metres from its transform: bring the
    /// transform under the body, turned the way the body faces, before the next
    /// clip starts from there.
    /// </summary>
    public static class Body
    {
        private static readonly int MirrorParam = Animator.StringToHash("Mirror");

        public static void Rebase(Animator a, bool turn = true)
        {
            AnimTrace.Note(a, "Body.Rebase"); // FLD-1 log only
            // From the bones (the animator's body pose may only be read in the IK pass).
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) return;
            a.transform.position = new Vector3(hips.position.x, 0, hips.position.z);
            if (!turn) return;
            // Facing: square to the line across the hips and shoulders (left to right).
            var right = Across(a, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg) + Across(a, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm);
            var f = Vector3.Cross(right, Vector3.up);
            f.y = 0;
            if (f.sqrMagnitude > 0.0001f) a.transform.rotation = Quaternion.LookRotation(f);
        }

        private static Vector3 Across(Animator a, HumanBodyBones left, HumanBodyBones right)
        {
            var l = a.GetBoneTransform(left);
            var r = a.GetBoneTransform(right);
            return l != null && r != null ? r.position - l.position : Vector3.zero;
        }

        public static bool Has(Animator a, string clip) =>
            a.runtimeAnimatorController != null && a.HasState(0, Animator.StringToHash(clip));

        /// <summary>
        /// Play <paramref name="clip"/> from where the body is, facing
        /// <paramref name="face"/> if given (else the way it already faces).
        /// False if the clip isn't there.
        /// </summary>
        public static bool Play(Animator a, string clip, Vector3? face = null)
        {
            if (!Has(a, clip)) return false;
            AnimTrace.Note(a, "Body.Play " + clip); // FLD-1 log only
            Rebase(a, face == null);
            if (face is Vector3 f && new Vector3(f.x, 0, f.z).sqrMagnitude > 0.01f) a.transform.rotation = Quaternion.LookRotation(new Vector3(f.x, 0, f.z));
            a.speed = 1f;
            a.SetBool(MirrorParam, false);
            // No cross-fade: the old pose was relative to where the transform was.
            a.Play(clip, 0, 0f);
            return true;
        }
    }
}
