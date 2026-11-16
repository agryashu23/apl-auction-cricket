using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Apl.Client.Ui
{
    /// <summary>
    /// A thumb-sized horizontal slider (owner v26 #4): a rounded track, a gold fill, a big
    /// round knob and its % readout. Touch anywhere on the row's hit area (120 px tall) and
    /// the knob jumps there and follows the finger; <see cref="Changed"/> fires while it
    /// moves (live preview), <see cref="Released"/> once the finger lifts (save, sample sound).
    /// Snaps to whole percent (and to 0 / 100 within 2% so OFF and FULL are easy to hit).
    /// </summary>
    public sealed class VolumeSlider : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Action<float>? Changed;
        public Action<float>? Released;

        private RectTransform _track = null!;
        private RectTransform _fill = null!;
        private RectTransform _knob = null!;
        private Text _readout = null!;
        private float _value;
        private bool _dragging;

        public float Value => _value;

        /// <summary>
        /// The slider across <paramref name="parent"/> at <paramref name="pos"/> (top-left anchored,
        /// centre of the row), <paramref name="width"/> px of track, the readout to its right.
        /// </summary>
        public static VolumeSlider Build(RectTransform parent, string name, Vector2 pos, float width, float value)
        {
            // The hit area: the whole row, tall enough for a thumb.
            var hit = UiKit.Box(parent, name, new Vector2(0, 1), pos, new Vector2(width + 60, 120), new Color(0, 0, 0, 0.001f));
            var s = hit.gameObject.AddComponent<VolumeSlider>();
            hit.GetComponent<Image>().raycastTarget = true;
            var track = PlayerCardKit.Shape(hit, "Track", CardSprites.Round(14), new Color(0.12f, 0.14f, 0.22f, 0.95f), Vector2.zero, new Vector2(width, 28));
            s._track = (RectTransform)track.transform;
            var fill = PlayerCardKit.Shape(s._track, "Fill", CardSprites.Round(14), UiKit.Gold, Vector2.zero, new Vector2(0, 28), new Vector2(0, 0.5f));
            s._fill = (RectTransform)fill.transform;
            s._fill.pivot = new Vector2(0, 0.5f);
            // Quarter ticks under the track, so the old 25 / 50 / 75 marks are still there to aim at.
            for (var i = 1; i < 4; i++)
                PlayerCardKit.Shape(s._track, "Tick" + i, CardSprites.Round(2), new Color(1, 1, 1, 0.22f), new Vector2(width * i / 4f, -26), new Vector2(4, 14), new Vector2(0, 0.5f));
            PlayerCardKit.Shape(hit, "KnobShadow", CardSprites.Disc(), new Color(0, 0, 0, 0.45f), new Vector2(0, -4), new Vector2(76, 76));
            var knob = PlayerCardKit.Shape(hit, "Knob", CardSprites.Disc(), Color.white, Vector2.zero, new Vector2(70, 70));
            s._knob = (RectTransform)knob.transform;
            PlayerCardKit.Shape(s._knob, "Dot", CardSprites.Disc(), UiKit.Gold, Vector2.zero, new Vector2(34, 34));
            s._readout = PlayerCardKit.Text(parent, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(pos.x + width / 2 + 50, pos.y - 40), new Vector2(pos.x + width / 2 + 190, pos.y + 40), "", 40, TextAnchor.MiddleRight, Color.white, condensed: true);
            s.Set(value, false);
            return s;
        }

        /// <summary>Moves the knob to <paramref name="v"/> (0–1); <paramref name="notify"/> fires Changed.</summary>
        public void Set(float v, bool notify)
        {
            v = Mathf.Clamp01(Mathf.Round(v * 100f) / 100f);
            if (v < 0.02f) v = 0f;
            if (v > 0.98f) v = 1f;
            var moved = !Mathf.Approximately(v, _value);
            _value = v;
            var w = _track.sizeDelta.x;
            _fill.sizeDelta = new Vector2(Mathf.Max(v > 0 ? 28 : 0, w * v), _fill.sizeDelta.y);
            _fill.gameObject.SetActive(v > 0);
            _knob.anchoredPosition = new Vector2(-w / 2 + w * v, 0);
            _readout.text = v <= 0f ? "OFF" : Mathf.RoundToInt(v * 100) + "%";
            _readout.color = v <= 0f ? UiKit.Dim : Color.white;
            if (notify && moved) Changed?.Invoke(v);
        }

        private void FromPointer(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_track, e.position, e.pressEventCamera, out var local)) return;
            var w = _track.sizeDelta.x;
            Set((local.x + w / 2) / w, true);
        }

        public void OnPointerDown(PointerEventData e)
        {
            _dragging = true;
            _knob.localScale = Vector3.one * 1.15f;
            FromPointer(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (_dragging) FromPointer(e);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;
            _knob.localScale = Vector3.one;
            Released?.Invoke(_value);
        }
    }
}
