using System.Collections;
using UnityEngine;

namespace Apl.Client.Hall
{
    /// <summary>
    /// A person at a team table. Body animation comes from the Seated
    /// controller; raising the paddle (or the RTM card) is humanoid IK plus a
    /// bone pass that grips the prop (SeatedMember.Props.cs). Head
    /// shakes and nods are procedural, so they need no extra clips.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed partial class SeatedMember : MonoBehaviour
    {
        /// <summary>Where the right hand went when a prop was raised (unused now: the raise is worked out from the arm, SeatedMember.Props.cs).</summary>
        public Transform? RaisedHandTarget;

        /// <summary>The bidding paddle, resting on the table.</summary>
        public Transform? Paddle;
        public Transform? PaddleRest;

        /// <summary>The team's Right to Match card, resting on the table.</summary>
        public Transform? RtmCard;
        public Transform? RtmRest;

        /// <summary>Laptop keyboard in front of this seat (null = no laptop).</summary>
        public Transform? Keyboard;
        private float _typeUntil, _typeWeight;

        private Animator _animator = null!;
        private Transform? _held, _heldRest;
        private float _shakeUntil, _nodUntil;

        // Turning to a neighbour: head and chest yaw towards them, blended in and out.
        private Transform? _lookAt;
        private float _lookUntil, _lookWeight;

        // High five: one hand reaches a point between two seats (IK), then drops.
        private AvatarIKGoal _fiveHand;
        private Vector3 _fivePoint;
        private float _fiveWeight, _fiveGoal;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            NoteHome();
            // Desync idles so a hall of people doesn't breathe in unison.
            _animator.SetFloat(Anim.Variant, Random.value);
            _animator.Update(Random.value * 3f);
        }

        public void React(int trigger)
        {
            if (Has(trigger)) _animator.SetTrigger(trigger);
        }

        /// <summary>Whether the controller has this trigger (optional clips may be missing).</summary>
        public bool Has(int trigger)
        {
            foreach (var p in _animator.parameters)
            {
                if (p.nameHash == trigger) return true;
            }
            return false;
        }

        public void RaisePaddle(float hold = 1.1f) => Raise(Paddle, PaddleRest, hold);

        /// <summary>Hold the RTM card up for the room.</summary>
        public void RaiseRtm(float hold = 2.4f) => Raise(RtmCard, RtmRest, hold);

        /// <summary>"No" — a couple of head shakes.</summary>
        public void ShakeHead() => _shakeUntil = Time.time + 1.3f;

        /// <summary>"No thanks": raise a hand and shake the head (upper-body clip), or a plain head shake.</summary>
        public void SayNo()
        {
            if (Has(Anim.RaiseNo)) _animator.SetTrigger(Anim.RaiseNo);
            else ShakeHead();
        }

        /// <summary>Seated disapproval, or a head shake without the clip.</summary>
        public void Disapprove()
        {
            if (Has(Anim.No)) _animator.SetTrigger(Anim.No);
            else ShakeHead();
        }

        /// <summary>"Yes" — a nod (the seated clip if there is one).</summary>
        public void Nod()
        {
            if (Has(Anim.NodYes)) _animator.SetTrigger(Anim.NodYes);
            else _nodUntil = Time.time + 0.9f;
        }

        /// <summary>Turn to <paramref name="other"/> for a few seconds, as if talking to them.</summary>
        public void LookAt(Transform other, float seconds)
        {
            _typeUntil = 0;
            _lookAt = other;
            _lookUntil = Time.time + seconds;
        }

        /// <summary>Hands on the laptop keyboard, typing, head down to the screen.</summary>
        public void Type(float seconds)
        {
            if (Keyboard == null || _held != null || Time.time < _lookUntil) return;
            _typeUntil = Time.time + seconds;
            // The typing clip moves the fingers and arms; IK keeps the hands on the keys.
            React(Anim.Typing);
        }

        /// <summary>Slap hands with a neighbour at <paramref name="point"/> (world space).</summary>
        public void HighFive(Vector3 point, bool rightHand, float delay = 0f)
        {
            if (_held != null) return;
            StartCoroutine(FiveRoutine(point, rightHand ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand, delay));
        }

        private IEnumerator FiveRoutine(Vector3 point, AvatarIKGoal hand, float delay)
        {
            _typeUntil = 0;
            if (delay > 0) yield return new WaitForSeconds(delay);
            _fiveHand = hand;
            _fivePoint = point;
            _fiveGoal = 1f;
            yield return new WaitForSeconds(0.55f);
            _fiveGoal = 0f;
        }

        /// <summary>
        /// After IK: head and chest gestures first, then the held prop's hand
        /// pass (grip, fingers, prop placed in the palm) so nothing moves the
        /// hand after the prop is put in it.
        /// </summary>
        private void LateUpdate()
        {
            CalibrateHands();
            Gestures();
            PropLateUpdate();
        }

        private void Gestures()
        {
            var head = _animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;
            if (_lookWeight > 0.001f && _lookAt != null)
            {
                // Rig-agnostic: yaw about world up by the angle to the neighbour.
                var to = _lookAt.position - transform.position;
                to.y = 0;
                var yaw = Mathf.Clamp(Vector3.SignedAngle(transform.forward, to, Vector3.up), -70f, 70f) * Mathf.SmoothStep(0, 1, _lookWeight);
                var chest = _animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? _animator.GetBoneTransform(HumanBodyBones.Chest);
                if (chest != null) chest.rotation = Quaternion.AngleAxis(yaw * 0.3f, Vector3.up) * chest.rotation;
                head.rotation = Quaternion.AngleAxis(yaw * 0.6f, Vector3.up) * head.rotation;
            }
            if (_typeWeight > 0.001f)
            {
                // Eyes on the screen.
                head.rotation = Quaternion.AngleAxis(18f * Mathf.SmoothStep(0, 1, _typeWeight), transform.right) * head.rotation;
            }
            if (Time.time < _shakeUntil)
            {
                var left = _shakeUntil - Time.time;
                var env = Mathf.Clamp01(left / 0.3f) * Mathf.Clamp01((1.3f - left) / 0.15f);
                head.rotation = Quaternion.AngleAxis(Mathf.Sin(left * 19f) * 24f * env, Vector3.up) * head.rotation;
            }
            if (Time.time < _nodUntil)
            {
                var left = _nodUntil - Time.time;
                var env = Mathf.Clamp01(left / 0.25f) * Mathf.Clamp01((0.9f - left) / 0.15f);
                head.rotation = Quaternion.AngleAxis(Mathf.Abs(Mathf.Sin(left * 14f)) * 16f * env, transform.right) * head.rotation;
            }
        }

        private void Update()
        {
            _lookWeight = Mathf.MoveTowards(_lookWeight, Time.time < _lookUntil ? 1f : 0f, Time.deltaTime * 2.5f);
            _typeWeight = Mathf.MoveTowards(_typeWeight, Time.time < _typeUntil ? 1f : 0f, Time.deltaTime * 2f);
            // Fast up (a slap), slower down.
            _fiveWeight = Mathf.MoveTowards(_fiveWeight, _fiveGoal, Time.deltaTime * (_fiveGoal > _fiveWeight ? 4f : 2.2f));
        }

        private void OnAnimatorIK(int layerIndex)
        {
            NoteIkGoals();
            if (_typeWeight > 0.001f && Keyboard != null && _fiveWeight <= 0.001f && _held == null)
            {
                // Both hands on the keys, fingers tapping: small, out-of-step bobs.
                var tw = Mathf.SmoothStep(0, 1, _typeWeight) * (Has(Anim.Typing) ? 0.75f : 1f);
                var t = Time.time;
                var side = Keyboard.right * 0.09f;
                var left = Keyboard.position - side + Vector3.up * (0.012f * Mathf.Abs(Mathf.Sin(t * 17f)));
                var right = Keyboard.position + side + Vector3.up * (0.012f * Mathf.Abs(Mathf.Sin(t * 19f + 1.3f)));
                _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, tw);
                _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, tw);
                _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
                _animator.SetIKPosition(AvatarIKGoal.LeftHand, left);
                _animator.SetIKPosition(AvatarIKGoal.RightHand, right);
                return;
            }
            var five = Mathf.SmoothStep(0, 1, _fiveWeight);
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, _fiveHand == AvatarIKGoal.LeftHand ? five : 0f);
            if (five > 0.001f)
            {
                _animator.SetIKPosition(_fiveHand, _fivePoint);
                if (_fiveHand == AvatarIKGoal.RightHand)
                {
                    _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, five);
                    _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
                    return;
                }
            }
            PropIK();
        }
    }
}
