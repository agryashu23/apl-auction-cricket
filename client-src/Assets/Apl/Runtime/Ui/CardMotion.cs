using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Ui
{
    /// <summary>
    /// A kit card's motion: it slides up and fades in after <see cref="Delay"/>
    /// (cards stagger in); selected, it lifts and grows a little with its glow
    /// up and the tick showing; pressed, it dips. Real time, so it moves while
    /// the game is paused under a picker.
    /// </summary>
    public sealed class CardMotion : MonoBehaviour
    {
        public float Delay;

        /// <summary>
        /// How visible the card is before its entrance (0 = hidden). A pick screen sets it so
        /// its panel is never empty on the first frame while the cards stagger in.
        /// </summary>
        public float FromAlpha
        {
            get => _fromAlpha;
            set
            {
                _fromAlpha = value;
                // Applied now, not next frame: a screenshot (or a frame) right after the cards are made shows them.
                if (_group != null) Apply(0f);
            }
        }

        private float _fromAlpha;
        public Image? Glow;
        public GameObject? Tick;
        public Graphic? Rim;

        private RectTransform _rt = null!;
        private CanvasGroup _group = null!;
        private Vector2 _home;
        private float _born, _lift, _press;
        private bool _selected;

        private const float In = 0.25f;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            // (Unity's missing-component "null" isn't C# null: no ?? here.)
            _group = gameObject.GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            // The card is placed before this is added: its clock starts now, not a frame later.
            _home = _rt.anchoredPosition;
            _born = Time.unscaledTime;
            Apply(0f);
        }

        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                if (Tick != null) Tick.SetActive(value);
            }
        }

        /// <summary>A quick dip when tapped.</summary>
        public void Press() => _press = 1f;

        private void Update()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _lift = Mathf.MoveTowards(_lift, _selected ? 1f : 0f, dt * 6f);
            _press = Mathf.MoveTowards(_press, 0f, dt * 7f);
            Apply(dt);
        }

        private void Apply(float dt)
        {
            var k = Mathf.Clamp01((Time.unscaledTime - _born - Delay) / In);
            var ease = 1f - Mathf.Pow(1f - k, 3f);
            _group.alpha = Mathf.Lerp(_fromAlpha, 1f, ease);
            var lift = Mathf.SmoothStep(0, 1, _lift);
            _rt.anchoredPosition = _home + new Vector2(0, (1 - ease) * -60f + lift * 14f);
            var s = Mathf.Lerp(0.92f, 1f, ease) * (1f + 0.07f * lift - 0.04f * _press);
            _rt.localScale = new Vector3(s, s, 1);
            if (Glow != null)
            {
                var c = Glow.color;
                // A slow breath while selected.
                c.a = lift * (0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f));
                Glow.color = c;
            }
            if (Rim != null)
            {
                var c = Rim.color;
                c.a = 0.18f + 0.82f * lift;
                Rim.color = c;
            }
        }
    }
}
