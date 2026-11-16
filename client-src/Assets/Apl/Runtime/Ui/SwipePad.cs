using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Apl.Client.Ui
{
    /// <summary>
    /// A transparent touch area for swipe input. A swipe fires the moment it has
    /// travelled <see cref="Threshold"/> (reference px) — it doesn't wait for the
    /// finger to lift — with its direction on screen (x right, y up); once per
    /// touch. A gold trail follows the finger and fades out after the swipe.
    /// </summary>
    public sealed class SwipePad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>How far (canvas reference px, ≈ 4 mm on a phone) a swipe travels before it counts.</summary>
        public const float Threshold = 60f;

        /// <summary>The trail's colour and how long it takes to fade.</summary>
        private static readonly Color TrailColor = new Color(0.95f, 0.76f, 0.31f, 0.9f);
        private const float FadeSeconds = 0.35f;

        public event Action<Vector2>? Swiped;

        private RectTransform _rt = null!, _line = null!, _head = null!;
        private Image _lineImg = null!, _headImg = null!;
        private Vector2 _start;
        private int _pointer = int.MinValue;
        private bool _fired;
        private float _fadeFrom = -1f;

        public static SwipePad Create(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var pad = go.AddComponent<SwipePad>();
            pad._rt = rt;
            pad._line = UiKit.Box(rt, "Trail", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 0), TrailColor);
            pad._line.pivot = new Vector2(0.5f, 0f);
            pad._lineImg = pad._line.GetComponent<Image>();
            pad._lineImg.raycastTarget = false;
            pad._head = UiKit.Box(rt, "Head", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 30), TrailColor);
            pad._headImg = pad._head.GetComponent<Image>();
            pad._headImg.sprite = HudChip.Rounded();
            pad._headImg.type = Image.Type.Sliced;
            pad._headImg.raycastTarget = false;
            pad.Show(false);
            return pad;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_pointer != int.MinValue) return;
            _pointer = e.pointerId;
            _fired = false;
            _fadeFrom = -1f;
            _start = Local(e);
            Draw(_start, 1f);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            var now = Local(e);
            if (_fadeFrom < 0) Draw(now, 1f);
            var d = now - _start;
            if (_fired || d.magnitude < Threshold) return;
            _fired = true;
            // The swipe is the shot: the trail holds where it fired and fades.
            _fadeFrom = Time.unscaledTime;
            Swiped?.Invoke(d.normalized);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            _pointer = int.MinValue;
            if (_fadeFrom < 0) _fadeFrom = Time.unscaledTime;
        }

        private void OnDisable()
        {
            _pointer = int.MinValue;
            Show(false);
        }

        private void Update()
        {
            if (_fadeFrom < 0) return;
            var k = (Time.unscaledTime - _fadeFrom) / FadeSeconds;
            if (k >= 1f)
            {
                _fadeFrom = -1f;
                Show(false);
                return;
            }
            Tint(1f - k);
        }

        private Vector2 Local(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out var p);
            return p;
        }

        private void Draw(Vector2 to, float alpha)
        {
            var d = to - _start;
            _line.anchoredPosition = _start;
            _line.sizeDelta = new Vector2(10, d.magnitude);
            _line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(-d.x, d.y) * Mathf.Rad2Deg);
            _head.anchoredPosition = to;
            Show(true);
            Tint(alpha);
        }

        private void Tint(float alpha)
        {
            _lineImg.color = UiKit.WithAlpha(TrailColor, TrailColor.a * alpha * 0.8f);
            _headImg.color = UiKit.WithAlpha(TrailColor, TrailColor.a * alpha);
        }

        private void Show(bool on)
        {
            if (_line == null) return;
            _line.gameObject.SetActive(on);
            _head.gameObject.SetActive(on);
        }
    }
}
