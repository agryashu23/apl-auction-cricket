using System;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// How the ball reads on a phone. It sits in the bowler's hand from his mark
    /// (it follows the bowling hand's fingers until release), it's drawn never
    /// smaller than a few pixels whatever the zoom, it has a thin dark outline so
    /// it's a ball against the pitch and the sky rather than a dot of light, and a
    /// soft shadow on the ground under it (darker and tighter the lower it is —
    /// that's what tells the eye where it is in depth). No glint, no trail before
    /// the hit: the old release flare rode along with the ball and read as a white
    /// streak. Whoever moves the ball (the flight, the fielders) just sets its
    /// position; this scales it and draws the outline, shadow and timing ring.
    /// </summary>
    public sealed partial class BallView : MonoBehaviour
    {
        /// <summary>The ball's own size (m, the sphere's scale).</summary>
        public const float Size = 0.075f;

        /// <summary>
        /// Smallest it's drawn: this share of the screen's height (≈6 px on a 1080-line
        /// screen) and never under <see cref="MinPixels"/>; never blown up more than
        /// <see cref="MaxBoost"/>× (a floor for far shots, not a beach ball).
        /// </summary>
        public const float MinScreenShare = 0.0058f, MinPixels = 4.5f, MaxBoost = 2.2f;

        /// <summary>Outline: this much bigger than the ball, drawn behind it in near-black.</summary>
        public const float OutlineScale = 1.22f;

        /// <summary>Shadow: diameter in drawn-ball sizes on the ground, fading out by this height (m).</summary>
        public const float ShadowSize = 1.6f, ShadowFadeHeight = 4f;

        private Transform? _hand, _fingers;
        private Transform _shadow = null!;
        private Material _shadowMat = null!;
        private float _drawn = Size;

        /// <summary>
        /// Deep area 3 (dev 2, review R3): through a catch or a take the ball isn't blown up past a cap — at 2.2×
        /// (~20 cm outlined, from 40–60 m) it read as touching the catcher's chest even centred in his palms.
        /// Asked every frame by CatchHands; eased in and out so it doesn't pop.
        /// </summary>
        public void CapBoost(float cap)
        {
            _capFrame = Time.frameCount;
            _cap = cap;
        }

        private int _capFrame = -10;
        private float _cap = MaxBoost, _boost = MaxBoost;

        /// <summary>He's holding it (it follows his hand).</summary>
        public bool InHand => _hand != null || _follow != null;

        /// <summary>
        /// Deep area 3 (dev 1, Unity review r1 item 1): a point the ball keeps to after the animation every frame — the
        /// collecting hands through a pick-up, eased into the throwing palm — so it's never a frame behind a running
        /// fielder's hands (placed from a coroutine it trailed them by a frame: 12 cm at a sprint, a metre on a long frame).
        /// </summary>
        private Func<Vector3>? _follow;

        /// <summary>Keep the ball on <paramref name="at"/> (asked after the animation each frame); null stops it.</summary>
        public void Follow(Func<Vector3>? at)
        {
            _follow = at;
            if (at == null) return;
            _hand = _fingers = null;
            gameObject.SetActive(true);
            transform.position = at();
        }

        public static BallView Add(GameObject ball)
        {
            var view = ball.AddComponent<BallView>();
            var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (unlit != null) Outline(ball, unlit);
            // The shadow: a soft dark disc flat on the ground (its own object; the ball's scale changes).
            var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(disc.GetComponent<Collider>());
            disc.name = "BallShadow";
            view._shadowMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = Soft(), color = new Color(0, 0, 0, 0) };
            var dr = disc.GetComponent<Renderer>();
            dr.material = view._shadowMat;
            dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dr.receiveShadows = false;
            view._shadow = disc.transform;
            disc.SetActive(false);
            view.MakeRing();
            return view;
        }

        /// <summary>The outline: a dark shell drawn inside-out (front faces culled) around the ball.</summary>
        private static void Outline(GameObject ball, Shader unlit)
        {
            var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(shell.GetComponent<Collider>());
            shell.name = "Outline";
            shell.transform.SetParent(ball.transform, false);
            shell.transform.localScale = Vector3.one * OutlineScale;
            var ink = new Color(0.06f, 0.07f, 0.09f);
            var dark = new Material(unlit) { color = ink };
            dark.SetColor("_BaseColor", ink);
            dark.SetFloat("_Cull", 1f);
            var sr = shell.GetComponent<Renderer>();
            sr.material = dark;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
        }

        /// <summary>In the bowler's hand (at the base of the fingers) until <see cref="Release"/>.</summary>
        public void Hold(Transform? hand, Transform? fingers)
        {
            _follow = null;
            _hand = hand;
            _fingers = fingers;
            StopRing();
            gameObject.SetActive(hand != null);
            if (hand != null) transform.position = InHandAt();
        }

        /// <summary>Out of the hand: the flight has it now.</summary>
        public void Release()
        {
            _follow = null;
            _hand = _fingers = null;
            gameObject.SetActive(true);
        }

        /// <summary>Between balls: nowhere.</summary>
        public void Hide()
        {
            _follow = null;
            _hand = _fingers = null;
            StopRing();
            gameObject.SetActive(false);
        }

        private Vector3 InHandAt() => _hand == null ? transform.position : Cradle(_hand, _fingers);

        /// <summary>
        /// The ball in a hand this view follows: a thin wrapper on <see cref="HandPoint.Palm(Vector3, Vector3)"/>, the ONE
        /// palm helper (area 3) — cradled where the fingers meet the palm (<paramref name="fingers"/>: the middle finger's root).
        /// </summary>
        public static Vector3 Cradle(Transform hand, Transform? fingers) =>
            fingers == null ? hand.position : HandPoint.Palm(hand.position, fingers.position); // (the one helper: HandPoint)

        private void LateUpdate()
        {
            if (_hand != null)
            {
                if (!_hand.gameObject.activeInHierarchy) _hand = null;
                else transform.position = InHandAt();
            }
            else if (_follow != null) transform.position = _follow();
            var cam = Camera.main;
            _drawn = Size;
            if (cam != null)
            {
                // Pixels per metre at the ball's distance; scale up to the floor if it's smaller.
                var dist = Mathf.Max(0.5f, Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward));
                var perMetre = cam.pixelHeight / (2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                var want = Mathf.Max(MinPixels, cam.pixelHeight * MinScreenShare);
                var px = Size * perMetre;
                // (Capped through a take: CapBoost, asked last frame or this.)
                _boost = Mathf.MoveTowards(_boost, Time.frameCount - _capFrame <= 1 ? _cap : MaxBoost, Time.unscaledDeltaTime * 6f);
                if (px < want) _drawn = Size * Mathf.Min(_boost, want / Mathf.Max(0.01f, px));
            }
            transform.localScale = Vector3.one * _drawn;
            Shadow();
            Ring(cam);
        }

        private void Shadow()
        {
            var h = transform.position.y;
            // Not while he's carrying it (the bowler's own shadow does that), nor high in the air.
            var on = _hand == null && h < ShadowFadeHeight && gameObject.activeInHierarchy;
            if (_shadow.gameObject.activeSelf != on) _shadow.gameObject.SetActive(on);
            if (!on) return;
            var k = Mathf.Clamp01(h / ShadowFadeHeight);
            _shadow.SetPositionAndRotation(new Vector3(transform.position.x, 0.015f, transform.position.z), Quaternion.Euler(90f, 0, 0));
            _shadow.localScale = Vector3.one * (_drawn * ShadowSize * (1f + 1.2f * k));
            _shadowMat.color = new Color(0, 0, 0, 0.62f * (1f - k));
        }

        private void OnDisable()
        {
            if (_shadow != null) _shadow.gameObject.SetActive(false);
            HideRing();
        }

        private void OnDestroy()
        {
            if (_shadow != null) Destroy(_shadow.gameObject);
            DestroyRing();
        }
    }
}
