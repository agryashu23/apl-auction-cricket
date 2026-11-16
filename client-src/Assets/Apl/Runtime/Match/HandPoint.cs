using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Deep area 3 (fielding): the ONE place a ball sits in a hand. Humanoid hand bones pivot at the WRIST,
    /// so a ball put on <c>HumanBodyBones.RightHand</c> is drawn against the forearm — on a cup catch, the
    /// belly (owner: "looks like the ball hitting their body, not the hands"). The palm point is where the
    /// fingers meet the palm, a touch out along them (the same cradle as <see cref="BallView"/> and dev 1's
    /// <c>ThrowCraft.Palm</c>); the cup is the two palms' midpoint while the hands are together.
    /// </summary>
    public static class HandPoint
    {
        /// <summary>Hands further apart than this (m) aren't a cup: the take is the right hand's.</summary>
        public const float CupSpan = 0.45f;

        /// <summary>The palm point of one hand (world). Falls back to the wrist when the rig has no fingers.</summary>
        public static Vector3 Palm(Animator a, bool left)
        {
            var h = a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var f = a.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            if (h == null) return a.transform.position + Vector3.up;
            return f == null ? h.position : Palm(h.position, f.position);
        }

        /// <summary>The palm point from a wrist and its middle finger's root.</summary>
        public static Vector3 Palm(Vector3 wrist, Vector3 fingerRoot) => Vector3.LerpUnclamped(wrist, fingerRoot, 1.15f);

        /// <summary>
        /// Where a ball taken by these hands sits: one hand's palm (<paramref name="left"/> / <paramref name="right"/>
        /// only), else between both palms while they're together, else the right palm.
        /// </summary>
        public static Vector3 Cup(Animator a, bool leftOnly = false, bool rightOnly = false)
        {
            if (leftOnly) return Palm(a, true);
            if (rightOnly) return Palm(a, false);
            return Cup(Palm(a, true), Palm(a, false));
        }

        public static Vector3 Cup(Vector3 leftPalm, Vector3 rightPalm) =>
            Vector3.Distance(leftPalm, rightPalm) < CupSpan ? (leftPalm + rightPalm) * 0.5f : rightPalm;
    }
}
