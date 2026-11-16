using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The player card the pre-match screens are built from (squads, pick your
    /// batters, intros): the side's colour, the portrait, the game name, a role
    /// icon, the rating, his hand; captain and keeper chips. Code-drawn, no art.
    /// </summary>
    public static class PreMatchCard
    {
        /// <summary>Card size at scale 1 (the squad screens use it smaller).</summary>
        public static readonly Vector2 Size = new Vector2(150, 204);

        /// <summary>
        /// A card for <paramref name="p"/> centred at <paramref name="pos"/>, <paramref name="scale"/>
        /// times <see cref="Size"/>. Returns the card (add a button / outline to it).
        /// </summary>
        public static RectTransform Build(RectTransform parent, Player p, Franchise team, Vector2 anchor, Vector2 pos, float scale = 1f, bool captain = false)
        {
            var size = Size * scale;
            var kit = UiKit.ParseHex(team.PrimaryColor);
            var trim = UiKit.ParseHex(team.SecondaryColor);
            var card = UiKit.Box(parent, "Card_" + p.Id, anchor, pos, size, Color.Lerp(kit, Color.black, 0.45f));
            card.GetComponent<Image>().raycastTarget = true;
            // The side's colour across the top, its second colour as a rim along the bottom.
            var top = UiKit.Box(card, "Top", new Vector2(0.5f, 1), new Vector2(0, -size.y * 0.33f), new Vector2(size.x, size.y * 0.66f), Color.Lerp(kit, Color.white, 0.08f));
            top.GetComponent<Image>().raycastTarget = false;
            // The names on a dark band whatever the kit (white on a yellow side didn't read).
            UiKit.Box(card, "NameBand", new Vector2(0.5f, 0), new Vector2(0, size.y * 0.21f), new Vector2(size.x, size.y * 0.42f), new Color(0.04f, 0.05f, 0.1f, 0.94f)).GetComponent<Image>().raycastTarget = false;
            UiKit.Box(card, "Rim", new Vector2(0.5f, 0), new Vector2(0, 3 * scale), new Vector2(size.x, 6 * scale), trim).GetComponent<Image>().raycastTarget = false;
            var edge = card.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(1, 1, 1, 0.28f);
            edge.effectDistance = new Vector2(2, -2);

            var face = new GameObject("Face", typeof(RectTransform), typeof(RawImage));
            var frt = (RectTransform)face.transform;
            frt.SetParent(card, false);
            frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 1);
            frt.anchoredPosition = new Vector2(0, -size.y * 0.3f);
            frt.sizeDelta = new Vector2(size.x * 0.74f, size.y * 0.5f);
            var img = face.GetComponent<RawImage>();
            img.texture = Portraits.For(p, scale > 0.9f ? 256 : 128);
            img.raycastTarget = false;

            // Rating (top right), role (top left).
            var ink = kit.grayscale > 0.55f ? UiKit.Ink : Color.white;
            UiKit.LabelAt(card, new Vector2(1, 1), new Vector2(-23 * scale, -21 * scale), new Vector2(50 * scale, 38 * scale), Rating(p).ToString(), Mathf.RoundToInt(34 * scale), TextAnchor.MiddleCenter, ink);
            UiKit.LabelAt(card, new Vector2(1, 1), new Vector2(-23 * scale, -45 * scale), new Vector2(50 * scale, 16 * scale), "OVR", Mathf.RoundToInt(13 * scale), TextAnchor.MiddleCenter, UiKit.WithAlpha(ink, 0.75f), bold: false);
            Icon(card, p, new Vector2(22 * scale, -22 * scale), 34 * scale, ink);

            // Name: initials small, surname big.
            var (first, last) = SplitName(p.Name);
            UiKit.LabelAt(card, new Vector2(0.5f, 0), new Vector2(0, 80 * scale), new Vector2(size.x - 8, 20 * scale), first, Mathf.RoundToInt(17 * scale), TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            UiKit.LabelAt(card, new Vector2(0.5f, 0), new Vector2(0, 54 * scale), new Vector2(size.x - 6, 32 * scale), last, Mathf.RoundToInt(28 * scale), TextAnchor.MiddleCenter, Color.white);
            UiKit.LabelAt(card, new Vector2(0.5f, 0), new Vector2(0, 25 * scale), new Vector2(size.x - 8, 20 * scale), Style(p), Mathf.RoundToInt(17 * scale), TextAnchor.MiddleCenter, UiKit.Dim, bold: false);

            // Captain / keeper chips on the portrait's lower corners.
            if (captain) Chip(card, "C", new Vector2(-size.x * 0.36f, -size.y * 0.56f), 26 * scale, UiKit.Gold, UiKit.Ink);
            if (p.IsKeeper && p.Role == Role.WicketKeeper) Chip(card, "WK", new Vector2(size.x * 0.33f, -size.y * 0.56f), 26 * scale, Color.white, UiKit.Ink);
            return card;
        }

        /// <summary>The card's number: how good he is in his role (0–100).</summary>
        public static int Rating(Player p) => Mathf.Clamp(p.Impact + p.Form, 0, 99);

        /// <summary>A side's rating for the squad screen: its XI's mean card rating.</summary>
        public static int TeamRating(IReadOnlyList<Player> xi) => xi.Count == 0 ? 0 : Mathf.RoundToInt((float)xi.Average(Rating));

        /// <summary>"RHB · PACE", "LHB", "RHB · SPIN".</summary>
        public static string Style(Player p)
        {
            var hand = p.LeftHanded ? "LHB" : "RHB";
            if (p.Role == Role.Batter || p.Role == Role.WicketKeeper || p.BowlType == BowlType.None) return hand;
            return hand + " · " + (p.BowlType == BowlType.Spin ? "SPIN" : "PACE");
        }

        /// <summary>"Right Hand Batter", "Left Hand Batter · Spin Bowler" — the intro card's line.</summary>
        public static string Description(Player p)
        {
            var hand = p.LeftHanded ? "Left Hand" : "Right Hand";
            return p.Role switch
            {
                Role.Bowler => (p.BowlType == BowlType.Spin ? "Spin Bowler" : "Fast Bowler") + " · " + hand + " Bat",
                Role.AllRounder => hand + " Batter · " + (p.BowlType == BowlType.Spin ? "Spin" : "Pace") + " All-rounder",
                Role.WicketKeeper => "Wicketkeeper · " + hand + " Batter",
                _ => hand + " Batter",
            };
        }

        /// <summary>"Rohit Varma" → ("R.", "Varma"); one word stays whole.</summary>
        public static (string first, string last) SplitName(string name)
        {
            var parts = name.Split(' ');
            if (parts.Length < 2) return ("", name);
            return (string.Join(" ", parts.Take(parts.Length - 1)), parts[parts.Length - 1]);
        }

        private static void Chip(RectTransform card, string text, Vector2 pos, float size, Color fill, Color ink)
        {
            var chip = UiKit.Box(card, "Chip_" + text, new Vector2(0.5f, 1), pos, new Vector2(size * (text.Length > 1 ? 1.5f : 1f), size), fill);
            chip.GetComponent<Image>().sprite = Round();
            chip.GetComponent<Image>().raycastTarget = false;
            UiKit.Label(chip, text, Mathf.RoundToInt(size * 0.62f), TextAnchor.MiddleCenter, ink);
        }

        // ---------------------------------------------------------- role icons

        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        private static Sprite? _round;

        private static void Icon(RectTransform card, Player p, Vector2 pos, float size, Color ink)
        {
            var key = p.Role switch
            {
                Role.Batter => "bat",
                Role.Bowler => "ball",
                Role.AllRounder => "both",
                _ => "gloves",
            };
            var go = new GameObject("Role", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(card, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.sprite = RoleSprite(key);
            img.color = ink;
            img.raycastTarget = false;
        }

        /// <summary>A rounded pill (chips).</summary>
        private static Sprite Round() => _round ??= Draw("round", (x, y) => Mathf.Clamp01((0.5f - Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f))) * 64f));

        /// <summary>
        /// White role glyphs, drawn from distance functions: a bat (blade and
        /// handle, tilted), a ball with its seam, both for an all-rounder, a
        /// keeper's glove.
        /// </summary>
        public static Sprite RoleSprite(string key)
        {
            if (Icons.TryGetValue(key, out var s)) return s;
            float Bat(float x, float y)
            {
                // Rotate 35° about the centre: the blade along v, the handle above it.
                var c = Mathf.Cos(0.6f);
                var sn = Mathf.Sin(0.6f);
                var u = (x - 0.5f) * c - (y - 0.5f) * sn;
                var v = (x - 0.5f) * sn + (y - 0.5f) * c;
                var blade = Mathf.Max(Mathf.Abs(u) - 0.09f, Mathf.Abs(v + 0.1f) - 0.3f);
                var handle = Mathf.Max(Mathf.Abs(u) - 0.035f, Mathf.Abs(v - 0.3f) - 0.12f);
                return Mathf.Min(blade, handle);
            }
            float Ball(float x, float y, float cx, float cy, float r)
            {
                var d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) - r;
                // The seam: a thin ring cut out across the middle.
                var seam = Mathf.Abs(Vector2.Distance(new Vector2(x, y), new Vector2(cx - r * 1.1f, cy)) - r * 1.25f) - r * 0.08f;
                return Mathf.Max(d, -seam);
            }
            float Glove(float x, float y)
            {
                var palm = Mathf.Max(Mathf.Abs(x - 0.5f) - 0.22f, Mathf.Abs(y - 0.4f) - 0.2f) - 0.04f;
                var fingers = Mathf.Max(Mathf.Abs(x - 0.5f) - 0.2f, Mathf.Abs(y - 0.68f) - 0.12f);
                // Gaps between the fingers.
                var gaps = Mathf.Min(Mathf.Abs(x - 0.4f), Mathf.Min(Mathf.Abs(x - 0.5f), Mathf.Abs(x - 0.6f))) - 0.012f;
                fingers = y > 0.6f ? Mathf.Max(fingers, -gaps) : fingers;
                var thumb = Vector2.Distance(new Vector2(x, y), new Vector2(0.2f, 0.5f)) - 0.09f;
                return Mathf.Min(Mathf.Min(palm, fingers), thumb);
            }
            System.Func<float, float, float> sdf = key switch
            {
                "bat" => Bat,
                "ball" => (x, y) => Ball(x, y, 0.5f, 0.5f, 0.36f),
                "both" => (x, y) => Mathf.Min(Bat(x * 1.1f - 0.08f, y * 1.1f - 0.02f), Ball(x, y, 0.77f, 0.23f, 0.17f)),
                _ => Glove,
            };
            return Icons[key] = Draw(key, (x, y) => Mathf.Clamp01(0.5f - sdf(x, y) * 64f));
        }

        private static Sprite Draw(string name, System.Func<float, float, float> alpha)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "icon_" + name, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var a = alpha((x + 0.5f) / n, (y + 0.5f) / n);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }
    }
}
