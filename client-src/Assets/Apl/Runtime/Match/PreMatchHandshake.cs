using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// One captain's right hand in the toss handshake: in the IK pass it reads
    /// where the clip puts his hand, and (at <see cref="Weight"/>) pulls it to the
    /// point halfway between his hand and the other captain's — so the two hands
    /// meet exactly and still pump together. Added for the handshake, removed after.
    /// </summary>
    public sealed class PreMatchHandshake : MonoBehaviour
    {
        /// <summary>The other captain's hand.</summary>
        public PreMatchHandshake? Other;

        /// <summary>0: the clip alone; 1: the hands held together.</summary>
        public float Weight;

        /// <summary>Where the clip put his right hand this frame (before the pull).</summary>
        public Vector3 Animated { get; private set; }

        private Animator _a = null!;
        private bool _seen;

        private float _born;

        private void Awake()
        {
            _a = GetComponent<Animator>();
            _born = Time.unscaledTime;
        }

        /// <summary>
        /// Never outlives the handshake: a stopped coroutine (SKIP, a scene change) can't clean up
        /// after itself, and a copy left on a captain who then bats would keep his hand off the bat.
        /// </summary>
        private void Update()
        {
            if (Other == null || Time.unscaledTime - _born > 8f) Destroy(this);
        }

        private void OnDisable() => Destroy(this);

        private void OnAnimatorIK(int layer)
        {
            if (Other == null) return;
            // With no weight set this frame, the goal is the hand as the clip has it.
            _a.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            Animated = _a.GetIKPosition(AvatarIKGoal.RightHand);
            if (Animated.sqrMagnitude < 1e-4f)
            {
                // No goal reported: the hand bone as last drawn.
                var hand = _a.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand != null) Animated = hand.position;
            }
            _seen = true;
            if (Weight <= 0f || Other == null || !Other._seen) return;
            var shared = (Animated + Other.Animated) * 0.5f;
            _a.SetIKPositionWeight(AvatarIKGoal.RightHand, Mathf.Clamp01(Weight));
            _a.SetIKPosition(AvatarIKGoal.RightHand, shared);
        }
    }
}
