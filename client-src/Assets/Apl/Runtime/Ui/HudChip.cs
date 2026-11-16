using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Apl.Client.Ui
{
    /// <summary>
    /// A HUD control chip: a rounded rectangle with a dark, see-through fill, a thin
    /// coloured edge and a coloured strip down its left side, a bold label. It fires
    /// the moment a finger lands (a Button fires on lift, ~0.1 s late — a late shot),
    /// and gives press feedback: it dips to 94% and its fill lights up in its colour
    /// until the finger lifts. <see cref="Selected"/> fills it solid (a selector).
    /// </summary>
    public sealed class HudChip : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action? Pressed;

        private Image _fill = null!, _edge = null!, _strip = null!;
        private Text _label = null!;
        private Color _color;
        private bool _down, _selected;

        private static readonly Color Fill = new Color(0.04f, 0.05f, 0.1f, 0.62f);

        public static HudChip Create(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, string label, Color color, int fontSize, Action? pressed)
        {
            var root = UiKit.Box(parent, name, anchor, pos, size, Color.white);
            var edge = root.GetComponent<Image>();
            edge.sprite = Rounded();
            edge.type = Image.Type.Sliced;
            var fill = UiKit.Box(root, "Fill", new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(4, 4), Fill);
            var fillImg = fill.GetComponent<Image>();
            fillImg.sprite = Rounded();
            fillImg.type = Image.Type.Sliced;
            fillImg.raycastTarget = false;
            // The colour strip down the left edge.
            var strip = UiKit.Box(root, "Strip", new Vector2(0f, 0.5f), new Vector2(9, 0), new Vector2(6, size.y - 22), color);
            strip.GetComponent<Image>().raycastTarget = false;
            var text = UiKit.Label(root, label, fontSize, TextAnchor.MiddleCenter);
            text.resizeTextForBestFit = false;
            text.raycastTarget = false;
            var chip = root.gameObject.AddComponent<HudChip>();
            chip._fill = fillImg;
            chip._edge = edge;
            chip._strip = strip.GetComponent<Image>();
            chip._label = text;
            chip._color = color;
            chip.Pressed = pressed;
            chip.Paint();
            return chip;
        }

        /// <summary>Lit solid in its colour (the type chosen on a selector).</summary>
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
            Pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData e) => Release();

        public void OnPointerExit(PointerEventData e) => Release();

        private void Release()
        {
            if (!_down) return;
            _down = false;
            Paint();
        }

        private void OnDisable() => _down = false;

        private void Paint()
        {
            if (_fill == null) return;
            var lit = _selected || _down;
            _fill.color = _selected ? UiKit.WithAlpha(_color, 0.92f) : _down ? UiKit.WithAlpha(_color, 0.55f) : Fill;
            _edge.color = UiKit.WithAlpha(_color, lit ? 1f : 0.7f);
            _strip.enabled = !_selected;
            _label.color = _selected ? UiKit.Ink : Color.white;
            transform.localScale = Vector3.one * (_down ? 0.94f : 1f);
        }

        private static Sprite? _rounded;

        /// <summary>A white rounded rectangle, 9-sliced (corner radius 14 px).</summary>
        public static Sprite Rounded()
        {
            if (_rounded != null) return _rounded;
            const int n = 48, r = 14;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "hud_chip", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                // Distance outside the inner rectangle the corners are rounded about.
                var dx = Mathf.Max(0f, Mathf.Max(r - (x + 0.5f), (x + 0.5f) - (n - r)));
                var dy = Mathf.Max(0f, Mathf.Max(r - (y + 0.5f), (y + 0.5f) - (n - r)));
                var a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return _rounded = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r + 2, r + 2, r + 2, r + 2));
        }
    }
}
