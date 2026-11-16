using System;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Ui
{
    /// <summary>
    /// The auction game's one visual system (UI/UX dev, rater r1 §4/§5/§8/§10): the colours,
    /// the type scale, and the panel / button / chip styles every auction-path screen uses
    /// (home, setup, retention, the hall HUD, hub, results, lobby, host panel), so no screen
    /// invents its own.
    ///
    /// Why panels are OPAQUE here: the project renders in LINEAR colour space, and a dark
    /// panel at alpha 0.88-0.97 blends in linear light, so a bright LED wall or crest behind it
    /// still shows through text clearly (rater: rules panel solo/10, lobby players panel
    /// host/room_03, standings breakdown). Text-bearing panels use alpha 1; only scene
    /// dimmers stay translucent.
    /// </summary>
    public static class UiTheme
    {
        // ------------------------------------------------------------ colours

        /// <summary>Full-screen page behind a menu (opaque).</summary>
        public static readonly Color Page = new Color(0.02f, 0.03f, 0.07f, 1f);

        /// <summary>The scene dimmer behind a page that keeps the hall visible (not for text).</summary>
        public static readonly Color Dimmer = new Color(0.01f, 0.012f, 0.03f, 0.82f);

        /// <summary>A card / panel face that carries text: opaque.</summary>
        public static readonly Color Panel = new Color(0.055f, 0.07f, 0.13f, 1f);

        /// <summary>A header bar, a selected row, a raised strip inside a panel.</summary>
        public static readonly Color Raised = new Color(0.10f, 0.13f, 0.22f, 1f);

        /// <summary>Table rows, alternating (opaque).</summary>
        public static readonly Color RowA = new Color(0.09f, 0.11f, 0.18f, 1f), RowB = new Color(0.065f, 0.08f, 0.14f, 1f);

        /// <summary>Hairlines and rims.</summary>
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.12f);

        public static Color Gold => UiKit.Gold;
        public static Color Accent => UiKit.Accent;
        public static Color Good => UiKit.Good;
        public static Color Bad => UiKit.Bad;
        public static Color Dim => UiKit.Dim;
        public static Color Ink => UiKit.Ink;

        /// <summary>Secondary (ghost) buttons: HUB, SKIP, FAST, BACK.</summary>
        public static readonly Color Ghost = new Color(0.13f, 0.15f, 0.22f, 1f);

        /// <summary>PASS / destructive.</summary>
        public static readonly Color Danger = new Color(0.40f, 0.13f, 0.14f, 1f);

        /// <summary>A control that can't be pressed right now (PASSED, WAIT…).</summary>
        public static readonly Color Disabled = new Color(0.20f, 0.21f, 0.26f, 1f);

        /// <summary>The gold trim's darker edge (edged panels, frames).</summary>
        public static readonly Color GoldDeep = new Color(0.55f, 0.38f, 0.10f, 1f);

        // ---------------------------------------------------------- type scale

        public const int Display = 64, H1 = 46, H2 = 34, Body = 26, Small = 20, Tiny = 16;

        /// <summary>Every button's label size by its height (one rule, so no screen guesses).</summary>
        public static int ButtonFont(float height) => height >= 110 ? 38 : height >= 86 ? 32 : height >= 70 ? 28 : 24;

        // --------------------------------------------------------------- styles

        public enum Style { Primary, Gold, Ghost, Danger }

        public static Color Fill(Style s) => s switch
        {
            Style.Primary => Accent,
            Style.Gold => Gold,
            Style.Danger => Danger,
            _ => Ghost,
        };

        /// <summary>Text colour that reads on <paramref name="fill"/>.</summary>
        public static Color InkOn(Color fill) => fill.grayscale > 0.55f ? Ink : Color.white;

        /// <summary>Force a graphic opaque (keeps its hue).</summary>
        public static void Opaque(Graphic? g)
        {
            if (g == null) return;
            var c = g.color;
            c.a = 1f;
            g.color = c;
        }

        /// <summary>
        /// Every near-opaque face under <paramref name="root"/> (alpha 0.8-0.99) made fully opaque:
        /// the see-through audit's one-line fix for a page built elsewhere. The root itself (a
        /// scene dimmer) and deliberate tints (alpha under 0.8) are left alone.
        /// </summary>
        public static int Solidify(RectTransform root)
        {
            var n = 0;
            foreach (var img in root.GetComponentsInChildren<Graphic>(true))
            {
                if (img.transform == root || img is Text) continue;
                var c = img.color;
                if (c.a < 0.8f || c.a >= 1f) continue;
                img.color = new Color(c.r, c.g, c.b, 1f);
                n++;
            }
            return n;
        }

        /// <summary>
        /// An opaque backing card behind a group of rows already built on <paramref name="parent"/>
        /// (named with <paramref name="prefix"/>): sized to their bounds plus <paramref name="pad"/>,
        /// placed under them in draw order. Null when no row matches.
        /// </summary>
        public static RectTransform? BackCard(RectTransform parent, string prefix, float pad, float extraTop = 0f)
        {
            RectTransform? first = null;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (Transform t in parent)
            {
                if (!t.name.StartsWith(prefix) || !(t is RectTransform rt)) continue;
                first ??= rt;
                var lo = rt.anchoredPosition - rt.sizeDelta * rt.pivot;
                var hi = lo + rt.sizeDelta;
                min = Vector2.Min(min, lo);
                max = Vector2.Max(max, hi);
            }
            if (first == null) return null;
            var size = max - min + new Vector2(pad * 2, pad * 2 + extraTop);
            var centre = (min + max) / 2f + new Vector2(0, extraTop / 2f);
            var card = PlayerCardKit.Shape(parent, prefix + "Back", CardSprites.Round(16), Panel, centre, size, first.anchorMin);
            card.transform.SetSiblingIndex(first.GetSiblingIndex());
            var rim = PlayerCardKit.Shape(parent, prefix + "Rim", CardSprites.RoundOutline(16, 2), Line, centre, size + new Vector2(2, 2), first.anchorMin);
            rim.transform.SetSiblingIndex(card.transform.GetSiblingIndex());
            return (RectTransform)card.transform;
        }

        /// <summary>
        /// The standard panel: an opaque rounded face, a 2 px light rim and a soft shadow;
        /// with <paramref name="title"/>, a gold title on a raised header band with a gold rule
        /// under it. Returns the face (children placed inside it).
        /// </summary>
        public static RectTransform Card(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, string? title = null, Color? rule = null)
        {
            PlayerCardKit.Shape(parent, name + "Shadow", CardSprites.Glow(22, 16), new Color(0, 0, 0, 0.5f), pos + new Vector2(0, -6), size + new Vector2(24, 24), anchor);
            PlayerCardKit.Shape(parent, name + "Rim", CardSprites.Round(20), Line, pos, size + new Vector2(4, 4), anchor);
            var face = PlayerCardKit.Shape(parent, name, CardSprites.Round(18), Panel, pos, size, anchor);
            face.raycastTarget = true;
            var rt = (RectTransform)face.transform;
            if (title != null)
            {
                var band = PlayerCardKit.Shape(rt, "Header", CardSprites.Round(18), Raised, new Vector2(0, -32), new Vector2(size.x, 64), new Vector2(0.5f, 1));
                var bandRt = (RectTransform)band.transform;
                // Square off the band's lower corners.
                PlayerCardKit.Shape(bandRt, "Square", CardSprites.Round(2), Raised, new Vector2(0, 10), new Vector2(size.x, 20), new Vector2(0.5f, 0));
                PlayerCardKit.Shape(bandRt, "Rule", CardSprites.Round(2), rule ?? Gold, new Vector2(0, 1), new Vector2(size.x - 36, 3), new Vector2(0.5f, 0));
                var t = UiKit.Label(bandRt, title, H2 - 6, TextAnchor.MiddleLeft, Gold);
                t.rectTransform.offsetMin = new Vector2(24, 0);
                t.rectTransform.offsetMax = new Vector2(-24, 0);
            }
            return rt;
        }

        /// <summary>The standard button: rounded, opaque, label sized by height (<see cref="ButtonFont"/>).</summary>
        public static Button Button(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, string label, Style style, Action? act)
        {
            var fill = Fill(style);
            var img = PlayerCardKit.Shape(parent, name, CardSprites.Round(14), fill, pos, size, anchor);
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            if (act != null) b.onClick.AddListener(() => act());
            var t = UiKit.Label((RectTransform)img.transform, label, ButtonFont(size.y), TextAnchor.MiddleCenter, InkOn(fill));
            t.rectTransform.offsetMin = new Vector2(10, 4);
            t.rectTransform.offsetMax = new Vector2(-10, -4);
            return b;
        }

        /// <summary>A small rounded tag ("TITLE", "AUCTION #3", "YOU").</summary>
        public static Text Chip(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, string text, Color fill, Color? ink = null)
        {
            var img = PlayerCardKit.Shape(parent, name, CardSprites.Round(10), fill, pos, size, anchor);
            var t = UiKit.Label((RectTransform)img.transform, text, Mathf.RoundToInt(Mathf.Min(size.y * 0.55f, 26)), TextAnchor.MiddleCenter, ink ?? InkOn(fill));
            t.rectTransform.offsetMin = new Vector2(8, 0);
            t.rectTransform.offsetMax = new Vector2(-8, 0);
            return t;
        }

        /// <summary>A horizontal bar: track plus fill (0-1), from the left of a box at <paramref name="pos"/>.</summary>
        public static RectTransform Bar(RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, float fill, Color colour)
        {
            var track = PlayerCardKit.Shape(parent, "BarTrack", CardSprites.Round(6), new Color(1, 1, 1, 0.08f), pos, size, anchor);
            var rt = (RectTransform)track.transform;
            var f = Mathf.Clamp01(fill);
            if (f > 0)
            {
                var w = Mathf.Max(size.y, size.x * f);
                PlayerCardKit.Shape(rt, "BarFill", CardSprites.Round(6), colour, new Vector2(w / 2f, 0), new Vector2(w, size.y), new Vector2(0, 0.5f));
            }
            return rt;
        }
    }
}
