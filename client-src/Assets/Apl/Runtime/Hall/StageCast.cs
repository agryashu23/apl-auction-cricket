using System;
using System.Collections;
using UnityEngine;

namespace Apl.Client.Hall
{
    /// <summary>
    /// The auctioneer at the podium (gavel in hand) and the assistant at the
    /// player box, who draws each card and walks it over to him.
    /// </summary>
    public sealed class StageCast : MonoBehaviour
    {
        public Animator? Auctioneer;
        public Animator? Assistant;

        /// <summary>Gavel prop: follows the auctioneer's right hand, struck procedurally.</summary>
        public Transform? Gavel;
        public Transform? GavelStrikeTarget;
        public Transform? ChitInBox;

        /// <summary>The player card prop, passed from the assistant to the auctioneer.</summary>
        public Transform? Card;

        /// <summary>Where the assistant stands to hand the card over.</summary>
        public Transform? HandoverSpot;
        public float WalkSpeed = 1.4f;

        private static readonly int Walking = Animator.StringToHash("Walking");

        private float _strike;
        private Vector3 _home;
        private Quaternion _homeRotation;
        private Animator? _cardHolder;
        private HumanBodyBones _cardHand;
        private Coroutine? _delivery;

        public void Announce() => Auctioneer?.SetTrigger(Anim.Talk);
        public void PointAtBidder() => Auctioneer?.SetTrigger(Anim.Point);
        public void GoingCall() => Auctioneer?.SetTrigger(Anim.Raise);
        public void Applaud() => Assistant?.SetTrigger(Anim.Clap);

        private void Start()
        {
            if (Assistant != null)
            {
                _home = Assistant.transform.position;
                _homeRotation = Assistant.transform.rotation;
            }
            if (Card != null) Card.gameObject.SetActive(false);
        }

        // ----------------------------------------------------------- the card

        /// <summary>
        /// Assistant (standing beside the podium) draws from the box, turns and
        /// hands the card to the auctioneer (<paramref name="handedOver"/> fires).
        /// </summary>
        public void DeliverCard(Action handedOver)
        {
            if (_delivery != null) StopCoroutine(_delivery);
            _delivery = StartCoroutine(Deliver(handedOver));
        }

        private IEnumerator Deliver(Action handedOver)
        {
            if (Assistant == null || Auctioneer == null || HandoverSpot == null || Card == null)
            {
                handedOver();
                yield break;
            }
            var t = Assistant.transform;
            t.position = _home;
            t.rotation = _homeRotation;
            Assistant.SetTrigger(Anim.Pick);
            yield return new WaitForSeconds(0.9f);
            Hold(Assistant, HumanBodyBones.RightHand);
            yield return new WaitForSeconds(0.35f);
            yield return Turn(t, Quaternion.LookRotation(Flat(Auctioneer.transform.position - t.position), Vector3.up));
            Assistant.SetTrigger(Anim.Raise);
            yield return new WaitForSeconds(0.55f);
            Hold(Auctioneer, HumanBodyBones.LeftHand);
            handedOver();
            yield return new WaitForSeconds(0.5f);
            yield return Turn(t, _homeRotation);
            _delivery = null;
        }

        /// <summary>The lot is over: the card goes away.</summary>
        public void ClearCard()
        {
            _cardHolder = null;
            if (Card != null) Card.gameObject.SetActive(false);
        }

        private void Hold(Animator who, HumanBodyBones hand)
        {
            _cardHolder = who;
            _cardHand = hand;
            if (Card != null) Card.gameObject.SetActive(true);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0;
            return v.sqrMagnitude < 0.0001f ? Vector3.forward : v;
        }

        private static IEnumerator Turn(Transform t, Quaternion to)
        {
            var from = t.rotation;
            for (var k = 0f; k < 1f; k += Time.deltaTime / 0.35f)
            {
                t.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0, 1, k));
                yield return null;
            }
            t.rotation = to;
        }

        private IEnumerator Walk(Transform t, Vector3 to)
        {
            to.y = t.position.y;
            Assistant!.SetBool(Walking, true);
            Face(t, to);
            while ((t.position - to).sqrMagnitude > 0.0025f)
            {
                t.position = Vector3.MoveTowards(t.position, to, WalkSpeed * Time.deltaTime);
                yield return null;
            }
            Assistant.SetBool(Walking, false);
        }

        private static void Face(Transform t, Vector3 target)
        {
            var d = target - t.position;
            d.y = 0;
            if (d.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(d, Vector3.up);
        }

        // ---------------------------------------------------------- the gavel

        /// <summary>Wind up and rap the block <paramref name="hits"/> times; <paramref name="onHit"/> fires on every impact.</summary>
        public void StrikeGavel(Action? onHit = null, int hits = 2)
        {
            if (_striking != null) StopCoroutine(_striking);
            _striking = StartCoroutine(StrikeRoutine(onHit, hits));
        }

        /// <summary>One rap (going once / going twice).</summary>
        public void TapGavel(Action? onHit = null) => StrikeGavel(onHit, 1);

        private Coroutine? _striking;

        /// <summary>How far the arm is taken over by the strike (0 = animation only).</summary>
        private float _armWeight;

        /// <summary>0 = gavel raised, 1 = head down on the block.</summary>
        private float _armDown;

        private IEnumerator StrikeRoutine(Action? onHit, int hits)
        {
            // Wind up: arm comes up and back.
            _armDown = 0f;
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
            {
                _armWeight = Mathf.SmoothStep(0, 1, t);
                yield return null;
            }
            _armWeight = 1f;
            for (var hit = 0; hit < hits; hit++)
            {
                // Down fast…
                for (var t = 0f; t < 1f; t += Time.deltaTime / 0.08f)
                {
                    _armDown = t * t;
                    yield return null;
                }
                _armDown = 1f;
                onHit?.Invoke();
                yield return new WaitForSeconds(0.06f);
                // …and back up for the next rap.
                for (var t = 0f; t < 1f; t += Time.deltaTime / 0.15f)
                {
                    _armDown = 1f - 0.9f * Mathf.SmoothStep(0, 1, t);
                    yield return null;
                }
            }
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
            {
                _armWeight = 1f - Mathf.SmoothStep(0, 1, t);
                yield return null;
            }
            _armWeight = 0f;
            _striking = null;
        }

        private void LateUpdate()
        {
            SwingArm();
            PlaceGavel();
            PlaceCard();
        }

        /// <summary>
        /// Rig-independent arm pose: turn the upper arm, then the forearm, so they
        /// point where we want (up and forward when raised, at the block when
        /// down), blended over whatever the animation is doing.
        /// </summary>
        private void SwingArm()
        {
            if (Auctioneer == null || GavelStrikeTarget == null || _armWeight <= 0f) return;
            var upper = Auctioneer.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var lower = Auctioneer.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var hand = Auctioneer.GetBoneTransform(HumanBodyBones.RightHand);
            if (upper == null || lower == null || hand == null) return;
            var fwd = Auctioneer.transform.forward;
            var target = GavelStrikeTarget.position + Vector3.up * 0.05f;

            var upperRaised = (fwd * 0.65f + Vector3.up * 0.35f + Auctioneer.transform.right * 0.25f).normalized;
            var upperDown = (target + Vector3.up * 0.28f - upper.position).normalized;
            var upperWant = Vector3.Slerp(upperRaised, upperDown, _armDown);
            var upperNow = (lower.position - upper.position).normalized;
            upper.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(upperNow, upperWant), _armWeight) * upper.rotation;

            var foreRaised = (Vector3.up * 0.8f + fwd * 0.2f).normalized;
            var foreDown = (target - lower.position).normalized;
            var foreWant = Vector3.Slerp(foreRaised, foreDown, _armDown);
            var foreNow = (hand.position - lower.position).normalized;
            lower.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(foreNow, foreWant), _armWeight) * lower.rotation;
        }

        private void PlaceGavel()
        {
            if (Auctioneer == null || Gavel == null) return;
            var hand = Auctioneer.GetBoneTransform(HumanBodyBones.RightHand);
            var lower = Auctioneer.GetBoneTransform(HumanBodyBones.RightLowerArm);
            if (hand == null || lower == null) return;
            // Handle runs on from the forearm through the fist; the head sits across it.
            var along = (hand.position - lower.position).normalized;
            Gavel.position = Grip(Auctioneer, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal);
            Gavel.rotation = Quaternion.LookRotation(along, Auctioneer.transform.forward);
        }

        private void PlaceCard()
        {
            if (_cardHolder == null || Card == null) return;
            var proximal = _cardHand == HumanBodyBones.LeftHand ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal;
            Card.position = Grip(_cardHolder, _cardHand, proximal) + _cardHolder.transform.forward * 0.04f + Vector3.up * 0.06f;
            // Face out from the holder, tilted back as if being read.
            Card.rotation = Quaternion.LookRotation(_cardHolder.transform.forward, Vector3.up) * Quaternion.Euler(-35, 0, 0);
        }

        /// <summary>Middle of the palm: between the wrist bone and the finger roots.</summary>
        public static Vector3 Grip(Animator a, HumanBodyBones hand, HumanBodyBones fingerRoot)
        {
            var h = a.GetBoneTransform(hand);
            var f = a.GetBoneTransform(fingerRoot);
            if (h == null) return a.transform.position;
            return f == null ? h.position : Vector3.Lerp(h.position, f.position, 0.6f);
        }
    }
}
