using System;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Ui
{
    /// <summary>
    /// The frame every player screen sits in: the stadium dimmed behind a dark
    /// backdrop, a glass panel (dark, translucent, a light edge),
    /// one header bar (title, subtitle, both sides' crests in their colours), a
    /// countdown ring, and rounded pill buttons at least 120 px tall.
    /// </summary>
    /// <summary>The header bar and its two lines (a screen updates them as picks are made).</summary>
    public sealed class HeaderView
    {
        public RectTransform Root = null!;
        public Text Title = null!, Subtitle = null!;
    }

    public static class PanelKit
    {
        public static readonly Color Glass = new Color(0.045f, 0.055f, 0.1f, 0.78f);

        /// <summary>The scene dimmed ~78%; catches taps so nothing behind is pressed.</summary>
        public static RectTransform Backdrop(RectTransform parent, float dim = 0.78f)
        {
            return UiKit.Stretch(parent, "Backdrop", new Color(0.01f, 0.012f, 0.03f, dim));
        }

        /// <summary>A glass panel: rounded, translucent dark, a faint light rim and a sheen along the top.</summary>
        public static RectTransform GlassPanel(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            PlayerCardKit.Shape(parent, name + "Rim", CardSprites.Round(26), new Color(1, 1, 1, 0.1f), pos, size + new Vector2(4, 4), anchor);
            var panel = PlayerCardKit.Shape(parent, name, CardSprites.Round(24), Glass, pos, size, anchor);
            var rt = (RectTransform)panel.transform;
            var sheen = PlayerCardKit.Shape(rt, "Sheen", CardSprites.FadeUp(), new Color(1, 1, 1, 0.05f), new Vector2(0, -size.y * 0.15f), new Vector2(size.x - 24, size.y * 0.3f), new Vector2(0.5f, 1));
            sheen.rectTransform.localRotation = Quaternion.Euler(180, 0, 0);
            return rt;
        }

        /// <summary>
        /// The header across the top: <paramref name="title"/> big, <paramref name="subtitle"/>
        /// under it, the crests of <paramref name="left"/> and <paramref name="right"/> at the ends
        /// over a bar in their colours.
        /// </summary>
        public static HeaderView Header(RectTransform parent, string title, string subtitle, Franchise? left, Franchise? right)
        {
            var bar = PlayerCardKit.Shape(parent, "Header", CardSprites.Round(20), new Color(0.03f, 0.035f, 0.07f, 0.92f), new Vector2(0, -70), new Vector2(1300, 112), new Vector2(0.5f, 1));
            var rt = (RectTransform)bar.transform;
            void End(Franchise f, int side)
            {
                var colour = UiKit.ParseHex(f.PrimaryColor);
                var stripe = PlayerCardKit.Shape(rt, "Stripe_" + f.Id, CardSprites.Round(20), UiKit.WithAlpha(PlayerCardKit.Bright(colour), 0.9f), new Vector2(side * -170, 0), new Vector2(340, 112), new Vector2(side < 0 ? 0 : 1, 0.5f));
                UiKit.Crest(rt, f.Id, new Vector2(side < 0 ? 0 : 1, 0.5f), new Vector2(side * -70, 0), 92);
                PlayerCardKit.Text(rt, new Vector2(side < 0 ? 0 : 1, 0), new Vector2(side < 0 ? 0 : 1, 1), new Vector2(side < 0 ? 128 : -330, 0), new Vector2(side < 0 ? 330 : -128, 0),
                    f.ShortName, 40, side < 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, Color.white, condensed: true);
            }
            if (left != null) End(left, -1);
            if (right != null) End(right, 1);
            // With a team stripe at an end (340 px in) the text keeps clear of both ends and
            // shrinks to fit, so a long title ("IMPACT PLAYERS · PICK YOUR SUBSTITUTES") never runs onto a chip.
            var half = left != null || right != null ? 300f : 420f;
            var t = PlayerCardKit.Text(rt, new Vector2(0.5f, 0.42f), new Vector2(0.5f, 1), new Vector2(-half, 0), new Vector2(half, -6), title, 44, TextAnchor.MiddleCenter, Color.white, condensed: true);
            var sub = PlayerCardKit.Text(rt, new Vector2(0.5f, 0), new Vector2(0.5f, 0.44f), new Vector2(-half, 6), new Vector2(half, 0), subtitle, 22, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            foreach (var x in new[] { t, sub })
            {
                x.horizontalOverflow = HorizontalWrapMode.Wrap;
                x.resizeTextForBestFit = true;
                x.resizeTextMaxSize = x.fontSize;
                x.resizeTextMinSize = Mathf.Max(14, x.fontSize / 2);
            }
            return new HeaderView { Root = rt, Title = t, Subtitle = sub };
        }

        /// <summary>A pill button (min 120 px tall for a thumb), team-coloured or the accent; show / hide it by its parent (with its shadow).</summary>
        public static Button Pill(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, string label, Color fill, Action act, int fontSize = 40)
        {
            size.y = Mathf.Max(size.y, 120);
            var holder = (RectTransform)new GameObject(name + "Holder", typeof(RectTransform)).transform;
            holder.SetParent(parent, false);
            holder.anchorMin = holder.anchorMax = anchor;
            holder.anchoredPosition = pos;
            holder.sizeDelta = size;
            PlayerCardKit.Shape(holder, name + "Shadow", CardSprites.Glow(30, 10), new Color(0, 0, 0, 0.5f), new Vector2(0, -8), size + new Vector2(10, 10));
            var img = PlayerCardKit.Shape(holder, name, CardSprites.Round(30), fill, Vector2.zero, size);
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => act());
            PlayerCardKit.Text((RectTransform)img.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, label, fontSize, TextAnchor.MiddleCenter, fill.grayscale > 0.6f ? UiKit.Ink : Color.white, condensed: true);
            return b;
        }

        /// <summary>A countdown ring with the seconds in it; drive it with <see cref="CountdownRing.Show"/>.</summary>
        public static CountdownRing Countdown(RectTransform parent, Vector2 anchor, Vector2 pos, float size = 130)
        {
            var back = PlayerCardKit.Shape(parent, "Countdown", CardSprites.Disc(), new Color(0.03f, 0.035f, 0.07f, 0.9f), pos, new Vector2(size, size), anchor);
            var rt = (RectTransform)back.transform;
            PlayerCardKit.Shape(rt, "Track", CardSprites.Ring(), new Color(1, 1, 1, 0.12f), Vector2.zero, new Vector2(size - 10, size - 10));
            var fill = PlayerCardKit.Shape(rt, "Fill", CardSprites.Ring(), UiKit.Gold, Vector2.zero, new Vector2(size - 10, size - 10));
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = false;
            var label = PlayerCardKit.Text(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, "", Mathf.RoundToInt(size * 0.42f), TextAnchor.MiddleCenter, Color.white, condensed: true);
            var ring = back.gameObject.AddComponent<CountdownRing>();
            ring.Fill = fill;
            ring.Label = label;
            return ring;
        }
    }
}
