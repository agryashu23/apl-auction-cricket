using System;
using System.Linq;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Modes
{
    /// <summary>
    /// THE VAULT (owner v26 #11: it was the LOCKER; the class and canvas keep the old name for the
    /// test runs). From the home page's bat icon and the challenges page's career column: a tab per
    /// slot — BAT, HELMET, PADS &amp; GLOVES, CELEBRATION, TITLE — and its items as cards with a
    /// picture (the painted bat itself, the colourway's swatches), a line, and
    /// EQUIPPED / tap to equip / what unlocks it. Your own batters wear them; every item is real:
    /// bats, helmets and pads on your batters (Locker.cs), the celebration at their milestones and
    /// winning runs (Celebrations.cs), the title on your captain's toss card and the rewards screen.
    /// </summary>
    public sealed class LockerPage : MonoBehaviour
    {
        private RectTransform _root = null!;
        private Slot _slot = Slot.Bat;
        private Action? _closed;

        public static string SlotName(Slot s) => s switch
        {
            Slot.Bat => "BAT",
            Slot.Helmet => "HELMET",
            Slot.Pads => "PADS & GLOVES",
            Slot.Celebration => "CELEBRATION",
            _ => "TITLE",
        };

        public static LockerPage Open(Action? closed = null)
        {
            foreach (var old in FindObjectsByType<LockerPage>(FindObjectsSortMode.None)) Destroy(old.gameObject);
            var canvas = UiKit.Canvas("Locker", 50);
            var page = canvas.gameObject.AddComponent<LockerPage>();
            page._root = (RectTransform)canvas.transform;
            page._closed = closed;
            page.Build();
            return page;
        }

        private void Build()
        {
            foreach (Transform c in _root) c.gameObject.SetActive(false);
            UiKit.Clear(_root);
            var bg = UiKit.Stretch(_root, "Page", new Color(0.02f, 0.03f, 0.07f, 1f));
            PlayerCardKit.Text(bg, new Vector2(0, 1), new Vector2(1, 1), new Vector2(60, -110), new Vector2(-60, -20), "THE VAULT", 60, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            PlayerCardKit.Text(bg, new Vector2(0, 1), new Vector2(1, 1), new Vector2(400, -110), new Vector2(-300, -20), $"LEVEL {Progression.Level}  ·  STARS {Progression.TotalStars}  ·  TITLE: {Progression.Equipped(Slot.Title).Name}  ·  worn by your own batters", 26, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
            PanelKit.Pill(bg, "Back", new Vector2(1, 1), new Vector2(-150, -70), new Vector2(240, 120), "◄ BACK", new Color(0.12f, 0.14f, 0.22f, 0.95f), () =>
            {
                Destroy(gameObject);
                _closed?.Invoke();
            }, 30);
            var tabs = (Slot[])Enum.GetValues(typeof(Slot));
            for (var i = 0; i < tabs.Length; i++)
            {
                var s = tabs[i];
                PanelKit.Pill(bg, "Tab_" + s, new Vector2(0.5f, 1), new Vector2((i - (tabs.Length - 1) / 2f) * 370, -190), new Vector2(355, 120), SlotName(s),
                    s == _slot ? UiKit.Gold : new Color(0.12f, 0.14f, 0.22f, 0.95f), () => { _slot = s; Build(); }, 30);
            }
            // Ten to a page (two rows of five); ◄ ► through the rest (round 12: the ladder to 50 made the lists long).
            var all = Array.FindAll(Cosmetics.All, c => c.Slot == _slot);
            const int perPage = 10;
            var pages = Mathf.Max(1, (all.Length + perPage - 1) / perPage);
            _pageOf.TryGetValue(_slot, out var pg);
            pg = Mathf.Clamp(pg, 0, pages - 1);
            var items = all.Skip(pg * perPage).Take(perPage).ToArray();
            // Two rows ending ~190 px above the bottom edge: the page row (◄ PAGE n ►) has its own strip
            // under the grid and never sits on a card (round 13, presentation-ui).
            const float w = 330, h = 300, gap = 18;
            for (var i = 0; i < items.Length; i++)
            {
                var row = i / 5;
                var col = i % 5;
                var inRow = Mathf.Min(5, items.Length - row * 5);
                var x = (col - (inRow - 1) / 2f) * (w + gap);
                Card(bg, items[i], new Vector2(x, -270 - h / 2 - row * (h + gap)), new Vector2(w, h));
            }
            if (pages > 1)
            {
                var slot = _slot;
                PlayerCardKit.Text(bg, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-200, 50), new Vector2(200, 110), $"PAGE {pg + 1} OF {pages}  ·  {all.Count(Progression.Unlocked)}/{all.Length} UNLOCKED", 24, TextAnchor.MiddleCenter, UiKit.Dim, condensed: true);
                if (pg > 0) PanelKit.Pill(bg, "PagePrev", new Vector2(0.5f, 0), new Vector2(-330, 75), new Vector2(180, 110), "◄", new Color(0.12f, 0.14f, 0.22f, 0.95f), () => { _pageOf[slot] = pg - 1; Build(); }, 40);
                if (pg < pages - 1) PanelKit.Pill(bg, "PageNext", new Vector2(0.5f, 0), new Vector2(330, 75), new Vector2(180, 110), "►", new Color(0.12f, 0.14f, 0.22f, 0.95f), () => { _pageOf[slot] = pg + 1; Build(); }, 40);
            }
        }

        private readonly System.Collections.Generic.Dictionary<Slot, int> _pageOf = new System.Collections.Generic.Dictionary<Slot, int>();
        private void Card(RectTransform parent, Cosmetic c, Vector2 pos, Vector2 size)
        {
            var on = Progression.Unlocked(c);
            var worn = Progression.Equipped(c.Slot) == c;
            var top = new Vector2(0.5f, 1);
            PlayerCardKit.Shape(parent, "Rim_" + c.Id, CardSprites.Round(22), worn ? UiKit.Gold : new Color(1, 1, 1, on ? 0.15f : 0.06f), pos, size + new Vector2(6, 6), top);
            var face = PlayerCardKit.Shape(parent, "Item_" + c.Id, CardSprites.Round(20), new Color(0.06f, 0.07f, 0.13f, 0.98f), pos, size, top);
            var rt = (RectTransform)face.transform;
            Picture(rt, c, on);
            PlayerCardKit.Text(rt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(14, 106), new Vector2(-14, 142), c.Name, 26, TextAnchor.MiddleCenter, on ? Color.white : UiKit.Dim, condensed: true);
            var line = PlayerCardKit.Text(rt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(16, 54), new Vector2(-16, 106), c.Line, 17, TextAnchor.UpperCenter, UiKit.Dim, bold: false);
            line.horizontalOverflow = HorizontalWrapMode.Wrap;
            var state = worn ? "EQUIPPED" : on ? "TAP TO EQUIP" : "UNLOCKS AT " + c.Requirement;
            PlayerCardKit.Text(rt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(14, 10), new Vector2(-14, 50), state, 21, TextAnchor.MiddleCenter, worn ? UiKit.Gold : on ? UiKit.Good : UiKit.Dim, condensed: true);
            if (!on || worn) return;
            face.raycastTarget = true;
            face.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                Progression.Equip(c);
                Debug.Log($"[APL vault] equipped {c.Slot} {c.Id}");
                Build();
            });
        }

        /// <summary>The bat itself, painted; a colourway's swatches; a celebration's badge.</summary>
        internal static void Picture(RectTransform card, Cosmetic c, bool on)
        {
            var tint = on ? Color.white : new Color(1, 1, 1, 0.3f);
            if (c.Slot == Slot.Bat)
            {
                var go = new GameObject("Bat", typeof(RectTransform), typeof(RawImage));
                var rt = (RectTransform)go.transform;
                rt.SetParent(card, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1);
                rt.anchoredPosition = new Vector2(0, -85);
                rt.sizeDelta = new Vector2(125, 140);
                var img = go.GetComponent<RawImage>();
                img.texture = BatPaint.For(c);
                img.uvRect = BatPaint.PreviewUv;
                img.color = tint;
                img.raycastTarget = false;
                return;
            }
            if (c.Slot == Slot.Title)
            {
                // A title is a name plate.
                var plate = PlayerCardKit.Shape(card, "Plate", CardSprites.Round(16), on ? new Color(0.85f, 0.65f, 0.15f) * tint : new Color(0.3f, 0.3f, 0.35f), new Vector2(0, -88), new Vector2(290, 90), new Vector2(0.5f, 1));
                PlayerCardKit.Text((RectTransform)plate.transform, Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0), c.Name, 26, TextAnchor.MiddleCenter, UiKit.Ink, condensed: true);
                return;
            }
            if (c.Slot == Slot.Celebration)
            {
                RewardCard.StarRow(card, new Vector2(0, -88), 3, 46);
                return;
            }
            // Helmet / pads: the main colour big, the trim beside it.
            var main = c.TeamMain ? new Color(0.25f, 0.45f, 0.85f) : c.A;
            var second = c.Slot == Slot.Pads ? (c.B.a > 0 ? c.B : main) : new Color(0.12f, 0.12f, 0.14f);
            PlayerCardKit.Shape(card, "Main", CardSprites.Disc(), main * tint, new Vector2(-40, -88), new Vector2(115, 115), new Vector2(0.5f, 1));
            PlayerCardKit.Shape(card, "Second", CardSprites.Disc(), second * tint, new Vector2(55, -112), new Vector2(70, 70), new Vector2(0.5f, 1));
            if (c.TeamMain) PlayerCardKit.Text(card, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-98, -108), new Vector2(18, -68), "TEAM", 22, TextAnchor.MiddleCenter, Color.white, condensed: true);
        }
    }
}
