using System;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Ui
{
    /// <summary>A card made by <see cref="PlayerCardKit"/>: tap it, select it, dim it with a note ("OUT 23 (15)").</summary>
    public sealed class PlayerCard
    {
        public RectTransform Root { get; }
        public CardMotion Motion { get; }
        public Button Button { get; }
        private readonly RectTransform _face;
        private GameObject? _note;

        public PlayerCard(RectTransform root, RectTransform face, CardMotion motion, Button button)
        {
            Root = root;
            _face = face;
            Motion = motion;
            Button = button;
        }

        public bool Selected
        {
            get => Motion.Selected;
            set => Motion.Selected = value;
        }

        /// <summary>Tapped: a dip, then <paramref name="act"/>.</summary>
        public void OnTap(Action act) => Button.onClick.AddListener(() =>
        {
            Motion.Press();
            act();
        });

        /// <summary>Greyed out with why (out, at the crease, bowled his quota); null: back to normal and tappable.</summary>
        public void Note(string? text)
        {
            if (_note != null) UnityEngine.Object.Destroy(_note);
            _note = null;
            Button.interactable = text == null;
            if (text == null) return;
            var shade = UiKit.Stretch(_face, "Note", new Color(0.02f, 0.025f, 0.06f, 0.7f));
            shade.GetComponent<Image>().raycastTarget = false;
            var s = Root.sizeDelta.x / 220f;
            PlayerCardKit.Text(shade, new Vector2(0, 0.35f), new Vector2(1, 0.75f), Vector2.zero, Vector2.zero, text, Mathf.RoundToInt(30 * s), TextAnchor.MiddleCenter, UiKit.Gold, condensed: true);
            _note = shade.gameObject;
        }

        /// <summary>A label pinned over the top edge ("STRIKER", "SUB"), in the side's bright colour; null removes it.</summary>
        public void Tag(string? text, Color colour)
        {
            var old = Root.Find("TagPill");
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            if (text == null) return;
            var s = Root.sizeDelta.x / 220f;
            var w = (24 + text.Length * 13) * s;
            var pill = PlayerCardKit.Shape(Root, "TagPill", CardSprites.Round(14), colour, new Vector2(0, Root.sizeDelta.y / 2 + 4 * s), new Vector2(w, 34 * s));
            PlayerCardKit.Text((RectTransform)pill.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, text, Mathf.RoundToInt(20 * s), TextAnchor.MiddleCenter, UiKit.Ink, condensed: true);
        }
    }
}
