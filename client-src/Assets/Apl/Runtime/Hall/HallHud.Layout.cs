using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Hall
{
    /// <summary>
    /// The HUD's bottom band on one grid (UI/UX dev, rater r1 §4: PASS sat offset above BID,
    /// the purple bidder box had dead space, PASSED showed twice, panels were see-through).
    /// Five columns in the 1920-wide reference (bottom-centre anchored, so a 20:9 phone keeps
    /// the same band centred with the extra width spare at the sides), two rows on one
    /// baseline each:
    ///
    ///   x:   A −915..−625   B −600..−110    C −80..160    D 190..520        E 540..915
    ///   top  BALANCE        PLAYER CARD     PORTRAIT      BIDDER chip       PASS
    ///   low  AUCTION HUB    SKIP · FAST     (portrait)    CURRENT BID       BID
    ///
    /// Upper row: bottom edge y = 122. Lower row: y 18..106. So the bid state (who leads,
    /// the price) is one column (D) and your two actions are one column (E) — the rooms
    /// client can lay its bot panel over column E (Pass + Bid) and keep the bidder in view.
    /// GameObject names are unchanged (Pause, Bid, Pass, Bidder, CurrentBid, Portrait, …):
    /// the rooms client and AutoShot find them by name.
    /// </summary>
    public sealed partial class HallHud
    {
        private bool _passedShown;

        /// <summary>Upper-row baseline and the lower row's centre / height.</summary>
        private const float UpperBase = 122f, LowY = 62f, LowH = 88f;

        private void ApplyAuctionLayout(RectTransform root)
        {
            // Column A.
            PlaceEdged(root, "Balance", new Vector2(-770, UpperBase + 62), new Vector2(290, 124));
            PlaceButton(root, "Hub", new Vector2(-770, LowY), new Vector2(290, LowH), UiTheme.Ghost);
            // Column B.
            PlaceEdged(root, "Card", new Vector2(-355, UpperBase + 88), new Vector2(490, 176));
            PlaceButton(root, "Skip", new Vector2(-482, LowY), new Vector2(236, LowH), UiTheme.Ghost);
            PlaceButton(root, "Hold", new Vector2(-228, LowY), new Vector2(236, LowH), UiTheme.Ghost);
            // Column C: the tall head-shot card, bottom on the lower row, top level with the player card.
            var portrait = root.Find("Portrait") as RectTransform;
            if (portrait != null)
            {
                portrait.anchoredPosition = new Vector2(40, 158);
                portrait.sizeDelta = new Vector2(240, 280);
            }
            // Column D: who leads over what it costs.
            PlaceEdged(root, "Bidder", new Vector2(355, UpperBase + 48), new Vector2(330, 96));
            PlaceEdged(root, "CurrentBid", new Vector2(355, LowY), new Vector2(330, LowH));
            // The bidder chip as a row: crest left, the line beside it (no empty box over a centred crest).
            var crest = _badgeCrest.rectTransform;
            crest.anchorMin = crest.anchorMax = new Vector2(0, 0.5f);
            crest.anchoredPosition = new Vector2(52, 0);
            crest.sizeDelta = new Vector2(72, 72);
            var title = (RectTransform)_badgeTitle.transform.parent;
            title.anchorMin = Vector2.zero;
            title.anchorMax = Vector2.one;
            title.offsetMin = new Vector2(96, 4);
            title.offsetMax = new Vector2(-12, -4);
            _badgeTitle.fontSize = 28;
            _badgeTitle.resizeTextMaxSize = 28;
            // Column E: your two actions, the same width, one above the other.
            PlaceButton(root, "Pass", new Vector2(727, UpperBase + 48), new Vector2(375, 96), UiTheme.Danger);
            PlaceButton(root, "Bid", new Vector2(727, LowY), new Vector2(375, LowH), UiTheme.Accent);
            _bidButtonText.fontSize = 36;
            _bidButtonText.resizeTextMaxSize = 36;

            // Opaque faces: the project is in linear colour space, where a 0.88-alpha dark panel lets
            // the lit LED wall read through its text (UiTheme).
            foreach (var face in new[] { "Balance/Face", "Card/Face" })
                if (root.Find(face) is Transform f) f.GetComponent<Image>().color = UiTheme.Panel;
            if (root.Find("Lot") is Transform lot) lot.GetComponent<Image>().color = UiTheme.Panel;
            foreach (var b in new[] { "Pause", "TeamsTab" })
                if (root.Find(b) is Transform t) t.GetComponent<Image>().color = UiTheme.Ghost;

            // The rules card: opaque, a gold rule under the title (rater r1: solo/10 see-through).
            var rules = (RectTransform)_rulesRoot.transform;
            rules.GetComponent<Image>().color = UiTheme.Panel;
            var rule = UiKit.Box(rules, "TitleRule", new Vector2(0.5f, 1), new Vector2(0, -100), new Vector2(1200, 3), UiTheme.Gold);
            rule.GetComponent<Image>().raycastTarget = false;
            if (rules.Find("SkipRules") is Transform skip) StyleButton(skip.GetComponent<Button>(), UiTheme.Ghost);
            _rtmPrompt.GetComponent<Image>().color = UiTheme.Panel;
            StyleActionButtons();
        }

        /// <summary>An EdgedPanel (back named <paramref name="name"/>, child Face) to a centre and size.</summary>
        private static void PlaceEdged(RectTransform root, string name, Vector2 centre, Vector2 size)
        {
            if (!(root.Find(name) is RectTransform back)) return;
            back.anchoredPosition = centre + new Vector2(6, -6);
            back.sizeDelta = size;
            if (back.Find("Face") is RectTransform face)
            {
                face.anchoredPosition = new Vector2(-6, 6);
                face.sizeDelta = size;
            }
        }

        private static void PlaceButton(RectTransform root, string name, Vector2 centre, Vector2 size, Color fill)
        {
            if (!(root.Find(name) is RectTransform rt)) return;
            rt.anchoredPosition = centre;
            rt.sizeDelta = size;
            var b = rt.GetComponent<Button>();
            if (b != null) StyleButton(b, fill);
            var t = rt.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.fontSize = UiTheme.ButtonFont(size.y);
                t.resizeTextMaxSize = t.fontSize;
            }
        }

        /// <summary>
        /// One button style: opaque fill, and a disabled state that stays OPAQUE (Unity's default
        /// disabled tint is half transparent: the WAIT… button showed the hall through it).
        /// </summary>
        private static void StyleButton(Button b, Color fill)
        {
            var img = b.GetComponent<Image>();
            if (img != null) img.color = fill;
            var c = b.colors;
            c.normalColor = Color.white;
            c.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            c.pressedColor = new Color(0.8f, 0.8f, 0.85f, 1f);
            c.selectedColor = Color.white;
            c.disabledColor = new Color(0.55f, 0.56f, 0.6f, 1f);
            b.colors = c;
        }

        /// <summary>PASS reads as a pressed state once passed (grey, the word once); BID dims its label while it can't be pressed.</summary>
        private void StyleActionButtons()
        {
            if (_passButton == null || _bidButton == null) return;
            var passImg = _passButton.GetComponent<Image>();
            passImg.color = _passedShown ? UiTheme.Disabled : UiTheme.Danger;
            _bidButtonText.color = _bidButton.interactable ? Color.white : new Color(1f, 1f, 1f, 0.75f);
        }
    }
}
