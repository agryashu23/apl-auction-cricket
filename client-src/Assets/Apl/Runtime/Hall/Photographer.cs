using System.Collections;
using UnityEngine;

namespace Apl.Client.Hall
{
    /// <summary>
    /// A press photographer in front of the stage. Holds a camera up to the
    /// face (hand IK, unless a downloaded "photo" clip already does) and fires
    /// a flash on request.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class Photographer : MonoBehaviour
    {
        /// <summary>The camera body prop (child of the photographer's root).</summary>
        public Transform? CameraProp;
        public Renderer? FlashGlow;
        public Light? FlashLight;

        /// <summary>False when the animation clip already holds a camera pose.</summary>
        public bool UseIK = true;

        /// <summary>Where they aim (the stage marks).</summary>
        public Vector3 AimAt = new Vector3(0, 1.5f, 7.6f);

        private Animator _animator = null!;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (FlashGlow != null) FlashGlow.enabled = false;
            if (FlashLight != null) FlashLight.enabled = false;
        }

        public void Flash() => StartCoroutine(FlashRoutine());

        private IEnumerator FlashRoutine()
        {
            if (FlashGlow != null) FlashGlow.enabled = true;
            if (FlashLight != null) FlashLight.enabled = true;
            yield return new WaitForSeconds(0.07f);
            if (FlashGlow != null) FlashGlow.enabled = false;
            if (FlashLight != null) FlashLight.enabled = false;
        }

        private void LateUpdate()
        {
            if (CameraProp == null) return;
            var head = _animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;
            // In front of the eyes, pointing at the stage.
            var aim = (AimAt - head.position).normalized;
            CameraProp.position = head.position + aim * 0.13f + Vector3.down * 0.02f;
            CameraProp.rotation = Quaternion.LookRotation(aim, Vector3.up);
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (!UseIK || CameraProp == null) return;
            var right = CameraProp.right;
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f);
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f);
            _animator.SetIKPosition(AvatarIKGoal.RightHand, CameraProp.position + right * 0.08f);
            _animator.SetIKPosition(AvatarIKGoal.LeftHand, CameraProp.position - right * 0.08f);
            _animator.SetLookAtWeight(1f, 0.2f, 0.8f);
            _animator.SetLookAtPosition(AimAt);
        }
    }
}
