using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Ui
{
    /// <summary>
    /// The one "this is you" mark, everywhere your franchise shows up (auction
    /// hall, hub, results, menus, tournament): a rounded outline with a soft glow
    /// round it, breathing white ↔ electric lime, and a YOU pill on its top edge.
    /// No franchise plays in white or lime, so it can't be read as a team's colour
    /// (the old gold read as Chennai's yellow). Rows and tiles keep their own
    /// colours underneath: the glow is outside them only.
    /// </summary>
    public sealed class YouMark : MonoBehaviour
    {
        /// <summary>Your colour, for text that names your side (no outline room).</summary>
        public static readonly Color Lime = new Color(0.62f, 1f, 0.32f);

        private Image _outline = null!, _glow = null!;
        private Image? _tag;
        private Text? _tagText;
        private bool _animated = true;
        private Color _still = Color.white;
        private float _phase;

        /// <summary>
        /// Outline <paramref name="target"/> (a tile, a row, a badge), <paramref name="thickness"/>
        /// px outside its edge, with <paramref name="tag"/> ("YOU", or empty for none) on the
        /// top edge. <paramref name="still"/>: a steady outline in that colour (a selection
        /// that isn't you, e.g. the opponent you picked).
        /// </summary>
        public static YouMark Add(RectTransform target, string tag = "YOU", float thickness = 5f, Color? still = null, int tagSize = 20)
        {
            var old = target.Find("YouMark");
            if (old != null) Destroy(old.gameObject);
            var go = new GameObject("YouMark", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(target, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-thickness, -thickness);
            rt.offsetMax = new Vector2(thickness, thickness);
            // Not a layout child of list rows.
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var mark = go.AddComponent<YouMark>();
            mark._animated = still == null;
            mark._still = still ?? Color.white;
            mark._phase = Random.value * 0.2f;
            mark._glow = Fill(rt, "Glow", CardSprites.OuterGlow(12, 18), -18);
            mark._outline = Fill(rt, "Outline", CardSprites.RoundOutline(12, Mathf.Max(3, Mathf.RoundToInt(thickness))), 0);
            if (tag.Length > 0)
            {
                var w = 26 + tag.Length * tagSize * 0.66f;
                // A pill straddling the top edge, in from the corner (rows pass no tag: their text says YOU).
                var chip = UiKit.Box(rt, "Tag", new Vector2(0, 1), new Vector2(w / 2 + thickness + 10, 0), new Vector2(w, tagSize * 1.45f), Color.white);
                mark._tag = chip.GetComponent<Image>();
                mark._tag.sprite = CardSprites.Round(14);
                mark._tag.type = Image.Type.Sliced;
                mark._tag.raycastTarget = false;
                mark._tagText = UiKit.Label(chip, tag, tagSize, TextAnchor.MiddleCenter, UiKit.Ink);
                mark._tagText.font = PlayerCardKit.Condensed;
            }
            mark.Paint(0);
            return mark;
        }

        /// <summary>Removes the mark from <paramref name="target"/> if it has one.</summary>
        public static void Remove(RectTransform target)
        {
            var old = target.Find("YouMark");
            if (old != null) Destroy(old.gameObject);
        }

        /// <summary>A sliced image over the whole mark, <paramref name="grow"/> px beyond it (negative: outward).</summary>
        private static Image Fill(RectTransform parent, string name, Sprite sprite, float grow)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(grow, grow);
            rt.offsetMax = new Vector2(-grow, -grow);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>The pulse colour now (white ↔ lime, a little under a second each way).</summary>
        public static Color Pulse(float time) => Color.Lerp(Color.white, Lime, 0.5f + 0.5f * Mathf.Sin(time * 4.2f));

        /// <summary>On top of whatever the tile got after the mark (crest, labels).</summary>
        private void Start() => transform.SetAsLastSibling();

        private void Update()
        {
            if (_animated) Paint(Time.unscaledTime + _phase);
        }

        private void Paint(float time)
        {
            var c = _animated ? Pulse(time) : _still;
            _outline.color = c;
            _glow.color = UiKit.WithAlpha(c, _animated ? 0.45f + 0.35f * Mathf.Sin(time * 4.2f) : 0.35f);
            if (_tag != null) _tag.color = c;
            if (_tagText != null) _tagText.color = UiKit.Ink;
        }
    }
}
