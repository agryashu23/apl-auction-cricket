using System.Linq;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Ui
{
    /// <summary>
    /// The one player card every player screen uses (squads, openers, SELECT
    /// BATTER, impact picks, bowler cards, captains' intros): a rounded card in
    /// the side's colours with a soft shadow, the portrait large, the name in
    /// bold condensed type over a dark fade, the rating badge, the role icon,
    /// captain / keeper chips; selected, it lifts with a glow in the team colour
    /// and a tick (CardMotion). Game names only — the data carries no real ones.
    /// </summary>
    public static class PlayerCardKit
    {
        private static Font? _condensed;

        /// <summary>Bold condensed type for names and numbers (APL Condensed, Oswald, OFL).</summary>
        public static Font Condensed => _condensed != null ? _condensed : _condensed = Resources.Load<Font>("Ui/AplCondensed") ?? UiKit.Font;

        /// <summary>The card's number: how good he is in his role (0–99).</summary>
        public static int Rating(Player p) => Mathf.Clamp(p.Impact + p.Form, 0, 99);

        /// <summary>A side's rating: its XI's mean card rating.</summary>
        public static int TeamRating(System.Collections.Generic.IReadOnlyList<Player> xi) => xi.Count == 0 ? 0 : Mathf.RoundToInt((float)xi.Average(Rating));

        /// <summary>"RHB · PACE", "LHB", "RHB · SPIN".</summary>
        public static string Style(Player p)
        {
            var hand = p.LeftHanded ? "LHB" : "RHB";
            if (p.Role == Role.Batter || p.Role == Role.WicketKeeper || p.BowlType == BowlType.None) return hand;
            return hand + " · " + (p.BowlType == BowlType.Spin ? "SPIN" : "PACE");
        }

        /// <summary>A brighter version of a kit colour for glows and rims (dark kits still light up).</summary>
        public static Color Bright(Color c)
        {
            Color.RGBToHSV(c, out var h, out var s, out var v);
            return Color.HSVToRGB(h, Mathf.Min(s, 0.85f), Mathf.Max(v, 0.9f));
        }

        public static Text Text(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offMin, Vector2 offMax, string s, int size, TextAnchor align, Color color, bool condensed = false, bool bold = true)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
            var t = UiKit.Label(rt, s, size, align, color, bold);
            if (condensed) t.font = Condensed;
            return t;
        }

        public static Image Shape(RectTransform parent, string name, Sprite sprite, Color color, Vector2 pos, Vector2 size, Vector2? anchor = null)
        {
            var rt = UiKit.Box(parent, name, anchor ?? new Vector2(0.5f, 0.5f), pos, size, color);
            var img = rt.GetComponent<Image>();
            img.sprite = sprite;
            img.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// A card for <paramref name="p"/> of <paramref name="team"/>, <paramref name="size"/> px
        /// (a 220 × 300 card is the reference), centred at <paramref name="pos"/>; it staggers in
        /// after <paramref name="delay"/> s. Tap it through <see cref="PlayerCard.Button"/>.
        /// </summary>
        public static PlayerCard Card(RectTransform parent, Player p, Franchise team, Vector2 anchor, Vector2 pos, Vector2 size, bool captain = false, float delay = 0f)
        {
            var s = size.x / 220f;
            var kit = UiKit.ParseHex(team.PrimaryColor);
            var trim = UiKit.ParseHex(team.SecondaryColor);
            var bright = Bright(kit);
            var root = (RectTransform)new GameObject("Card_" + p.Id, typeof(RectTransform)).transform;
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = anchor;
            root.anchoredPosition = pos;
            root.sizeDelta = size;

            var glow = Shape(root, "Glow", CardSprites.Glow(20, 26), UiKit.WithAlpha(bright, 0f), Vector2.zero, size + new Vector2(52, 52) * s);
            Shape(root, "Shadow", CardSprites.Glow(20, 12), new Color(0, 0, 0, 0.55f), new Vector2(0, -10 * s), size + new Vector2(16, 16) * s);
            var rim = Shape(root, "Rim", CardSprites.Round(23), UiKit.WithAlpha(bright, 0.18f), Vector2.zero, size + new Vector2(6, 6) * s);
            var face = Shape(root, "Face", CardSprites.Round(20), Color.white, Vector2.zero, size);
            face.raycastTarget = true;
            face.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var fr = (RectTransform)face.transform;

            // The side's colours: the kit, a slash of the trim across the top, the portrait, the dark fade for the name.
            var fill = UiKit.Stretch(fr, "Kit", Color.Lerp(kit, Color.black, 0.15f));
            fill.GetComponent<Image>().raycastTarget = false;
            var slash = UiKit.Box(fr, "Slash", new Vector2(1, 1), new Vector2(-40 * s, -30 * s), new Vector2(420 * s, 70 * s), UiKit.WithAlpha(trim, 0.45f));
            slash.localRotation = Quaternion.Euler(0, 0, 32);
            slash.GetComponent<Image>().raycastTarget = false;
            var pic = new GameObject("Face", typeof(RectTransform), typeof(RawImage));
            var prt = (RectTransform)pic.transform;
            prt.SetParent(fr, false);
            prt.anchorMin = new Vector2(0.06f, 0.3f);
            prt.anchorMax = new Vector2(0.94f, 0.97f);
            prt.offsetMin = prt.offsetMax = Vector2.zero;
            var raw = pic.GetComponent<RawImage>();
            raw.texture = Portraits.For(p, size.x > 180 ? 256 : 128);
            raw.raycastTarget = false;
            var fade = Shape(fr, "Fade", CardSprites.FadeUp(), new Color(0.03f, 0.035f, 0.07f, 0.97f), new Vector2(0, size.y * 0.25f), new Vector2(size.x, size.y * 0.5f), new Vector2(0.5f, 0));
            fade.type = Image.Type.Simple;

            var (first, last) = Split(p.Name);
            // The first name sits where the fade is thin, over the kit (grey vanished on gold kits): near-white with a shadow.
            var firstText = Text(fr, new Vector2(0, 0), new Vector2(1, 0), new Vector2(8 * s, 74 * s), new Vector2(-8 * s, 96 * s), first.ToUpperInvariant(), Mathf.RoundToInt(17 * s), TextAnchor.MiddleCenter, new Color(0.94f, 0.95f, 1f), bold: true);
            var shade = firstText.gameObject.AddComponent<Shadow>();
            shade.effectColor = new Color(0, 0, 0, 0.85f);
            shade.effectDistance = new Vector2(1.5f, -1.5f);
            Text(fr, new Vector2(0, 0), new Vector2(1, 0), new Vector2(6 * s, 32 * s), new Vector2(-6 * s, 76 * s), last.ToUpperInvariant(), Mathf.RoundToInt(38 * s), TextAnchor.MiddleCenter, Color.white, condensed: true);
            Text(fr, new Vector2(0, 0), new Vector2(1, 0), new Vector2(8 * s, 8 * s), new Vector2(-8 * s, 32 * s), Style(p), Mathf.RoundToInt(17 * s), TextAnchor.MiddleCenter, bright, bold: true);

            // Rating badge (top left), role icon (top right).
            var badge = Shape(fr, "Rating", CardSprites.Round(12), new Color(0.03f, 0.035f, 0.07f, 0.78f), new Vector2(40 * s, -42 * s), new Vector2(64, 70) * s, new Vector2(0, 1));
            var br = (RectTransform)badge.transform;
            Text(br, new Vector2(0, 0.3f), new Vector2(1, 1), Vector2.zero, Vector2.zero, Rating(p).ToString(), Mathf.RoundToInt(36 * s), TextAnchor.MiddleCenter, Color.white, condensed: true);
            Text(br, new Vector2(0, 0), new Vector2(1, 0.34f), Vector2.zero, Vector2.zero, "OVR", Mathf.RoundToInt(13 * s), TextAnchor.MiddleCenter, bright, bold: true);
            var role = Shape(fr, "RoleDisc", CardSprites.Disc(), new Color(0.03f, 0.035f, 0.07f, 0.78f), new Vector2(-34 * s, -34 * s), new Vector2(48, 48) * s, new Vector2(1, 1));
            Shape((RectTransform)role.transform, "Role", RoleIcons.For(p.Role), Color.white, Vector2.zero, new Vector2(30, 30) * s);
            var chipY = -92f * s;
            if (captain) { Chip(fr, "C", new Vector2(40 * s, chipY), s, UiKit.Gold); chipY -= 34 * s; }
            if (p.Role == Role.WicketKeeper) Chip(fr, "WK", new Vector2(40 * s, chipY), s, Color.white);

            // The tick, over the corner (shown when selected).
            var tick = Shape(root, "Tick", CardSprites.Disc(), bright, new Vector2(size.x / 2 - 6 * s, size.y / 2 - 6 * s), new Vector2(52, 52) * s);
            Shape((RectTransform)tick.transform, "Mark", CardSprites.Tick(), UiKit.Ink, Vector2.zero, new Vector2(40, 40) * s);
            tick.gameObject.SetActive(false);

            var motion = root.gameObject.AddComponent<CardMotion>();
            motion.Delay = delay;
            motion.Glow = glow;
            motion.Rim = rim;
            motion.Tick = tick.gameObject;
            var button = face.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            return new PlayerCard(root, fr, motion, button);
        }

        /// <summary>
        /// A card frame with nothing in it (field presets, options): the same shadow, rim,
        /// glow, tick and motion as a player card, a dark glass face to fill.
        /// </summary>
        public static (RectTransform face, CardMotion motion, Button button) Tile(RectTransform parent, string name, Vector2 pos, Vector2 size, Color accent, float delay = 0f)
        {
            var bright = Bright(accent);
            var root = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = pos;
            root.sizeDelta = size;
            var glow = Shape(root, "Glow", CardSprites.Glow(20, 26), UiKit.WithAlpha(bright, 0f), Vector2.zero, size + new Vector2(52, 52));
            Shape(root, "Shadow", CardSprites.Glow(20, 12), new Color(0, 0, 0, 0.55f), new Vector2(0, -10), size + new Vector2(16, 16));
            var rim = Shape(root, "Rim", CardSprites.Round(23), UiKit.WithAlpha(bright, 0.18f), Vector2.zero, size + new Vector2(6, 6));
            var face = Shape(root, "Face", CardSprites.Round(20), new Color(0.06f, 0.07f, 0.12f, 0.96f), Vector2.zero, size);
            face.raycastTarget = true;
            var tick = Shape(root, "Tick", CardSprites.Disc(), bright, new Vector2(size.x / 2 - 6, size.y / 2 - 6), new Vector2(52, 52));
            Shape((RectTransform)tick.transform, "Mark", CardSprites.Tick(), UiKit.Ink, Vector2.zero, new Vector2(40, 40));
            tick.gameObject.SetActive(false);
            var motion = root.gameObject.AddComponent<CardMotion>();
            motion.Delay = delay;
            motion.Glow = glow;
            motion.Rim = rim;
            motion.Tick = tick.gameObject;
            var button = face.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            return ((RectTransform)face.transform, motion, button);
        }

        private static void Chip(RectTransform parent, string text, Vector2 pos, float s, Color fill)
        {
            var w = text.Length > 1 ? 52f : 34f;
            var chip = Shape(parent, "Chip_" + text, CardSprites.Round(12), fill, pos, new Vector2(w, 30) * s, new Vector2(0, 1));
            Text((RectTransform)chip.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, text, Mathf.RoundToInt(18 * s), TextAnchor.MiddleCenter, UiKit.Ink, condensed: true);
        }

        /// <summary>"Rohit Varma" → ("Rohit", "Varma"); one word stays whole.</summary>
        public static (string first, string last) Split(string name)
        {
            var parts = name.Split(' ');
            return parts.Length < 2 ? ("", name) : (string.Join(" ", parts.Take(parts.Length - 1)), parts[parts.Length - 1]);
        }
    }
}
