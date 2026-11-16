using System;
using System.Collections.Generic;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// One round button on the delivery wheel: lit solid when chosen; it fires on a deliberate tap (down and
    /// up on it), never on a finger merely landing on it or sliding across it.
    /// </summary>
    public sealed class WheelSlot : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
    {
        private Image _edge = null!, _fill = null!;
        private Text _label = null!;
        private Color _color;
        private bool _down, _selected;
        private Action? _pressed;

        private static readonly Color Fill = new Color(0.04f, 0.05f, 0.1f, 0.66f);

        public static WheelSlot Create(RectTransform parent, string name, Vector2 at, float size, string label, Color color, Action pressed)
        {
            var root = UiKit.Box(parent, name, new Vector2(0.5f, 0.5f), at, new Vector2(size, size), color);
            var edge = root.GetComponent<Image>();
            edge.sprite = MatchHud.DiscSprite;
            var fill = UiKit.Box(root, "Fill", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size - 8, size - 8), Fill);
            var fillImg = fill.GetComponent<Image>();
            fillImg.sprite = MatchHud.DiscSprite;
            fillImg.raycastTarget = false;
            var text = UiKit.LabelAt(root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size - 18, size - 30), label, 19, TextAnchor.MiddleCenter, Color.white);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 11;
            text.resizeTextMaxSize = 19;
            text.raycastTarget = false;
            var slot = root.gameObject.AddComponent<WheelSlot>();
            slot._edge = edge;
            slot._fill = fillImg;
            slot._label = text;
            slot._color = color;
            slot._pressed = pressed;
            slot.Paint();
            return slot;
        }

        /// <summary>Lit solid in its colour (the type chosen).</summary>
        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                Paint();
            }
        }

        public void OnPointerDown(PointerEventData e)
        {
            _down = true;
            Paint();
        }

        public void OnPointerUp(PointerEventData e)
        {
            _down = false;
            Paint();
        }

        public void OnPointerExit(PointerEventData e)
        {
            _down = false;
            Paint();
        }

        /// <summary>Down and up on this button: a deliberate pick.</summary>
        public void OnPointerClick(PointerEventData e) => _pressed?.Invoke();

        private void Paint()
        {
            if (_fill == null) return;
            var lit = _selected || _down;
            _edge.color = new Color(_color.r, _color.g, _color.b, lit ? 1f : 0.85f);
            _fill.color = _selected ? _color : _down ? Color.Lerp(Fill, _color, 0.45f) : Fill;
            _label.color = _selected ? UiKit.Ink : Color.white;
        }
    }
}
