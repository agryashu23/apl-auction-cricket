using Apl.Client.Modes;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The home page's auction cards (UI/UX dev, rater r1 §10):
    ///  - DAILY GOALS as AUCTION goals (Modes/DailyGoals.cs pool: buy at base, a bidding war over
    ///    ₹10 Cr, top 3, a room auction, RTM, steals…), each row with its gavel tile, XP, bar and
    ///    count, and your AUCTION CAREER (auctions · wins · best · points) along the card's foot.
    ///  - CHALLENGES (the match challenges) as a slim row level with NETS | MATCH, so the right
    ///    column's bottom lines up with the middle column's (named "Challenges" for AutoShot).
    ///  - AUCTION ► TOURNAMENT gets its path (AUCTION › PICK XI › LEAGUE) and what the season is,
    ///    in place of the empty middle.
    /// Uses the home grid helpers (HomeCard, TextAt, BarAt, At) from MainMenu.Home / HomeTop.
    /// </summary>
    public sealed partial class MainMenu
    {
        private RectTransform? _solidified;

        /// <summary>
        /// The see-through audit for the menu pages (home, team pick, format, retention): once per
        /// new page, every near-opaque face (0.8-0.99 alpha) becomes opaque (UiTheme.Solidify).
        /// Called from Update, so it runs after the page is fully built.
        /// </summary>
        private void SolidifyPage()
        {
            if (_page == null || _page == _solidified) return;
            _solidified = _page;
            var n = UiTheme.Solidify(_page);
            Debug.Log($"[APL ui] page opaque pass: {n} faces");
        }

        /// <summary>DAILY GOALS (auction): title, "n of 3 done", three goal rows, the career strip at the foot.</summary>
        private void AuctionGoalsCard(RectTransform page, float x, float y, float w, float h)
        {
            var goals = ModesInfo.Goals;
            var card = HomeCard(page, "DailyGoals", x, y, w, h, CardFill, UiKit.WithAlpha(UiKit.Gold, 0.4f));
            var title = TextAt(card, 32, 24, w * 0.6f, 62, "DAILY GOALS", 50, TextAnchor.MiddleLeft, Color.white);
            title.fontStyle = FontStyle.Italic;
            TextAt(card, w * 0.5f, 22, w * 0.5f - 32, 34, "AUCTION", 24, TextAnchor.MiddleRight, UiKit.Gold);
            TextAt(card, w * 0.5f, 54, w * 0.5f - 32, 30, $"{ModesInfo.GoalsDone} of {goals.Count} done", 22, TextAnchor.MiddleRight, UiKit.Dim, condensed: false, bold: false);

            const float top = 108f, careerH = 132f;
            var rows = Mathf.Max(3, goals.Count);
            var rowH = (h - top - careerH - 20f) / rows;
            for (var i = 0; i < goals.Count && i < 4; i++)
            {
                var g = goals[i];
                var ry = top + i * rowH;
                var colour = g.Done ? XpGreen : UiKit.Gold;
                // Row plate, gavel tile (a tick when done).
                PlayerCardKit.Shape(card, "GoalRow" + i, CardSprites.Round(16), new Color(1, 1, 1, 0.035f), At(20, ry, w - 40, rowH - 12), new Vector2(w - 40, rowH - 12), TopLeft);
                const float t = 64f;
                var tile = PlayerCardKit.Shape(card, "GoalIcon" + i, CardSprites.Round(14), UiKit.WithAlpha(colour, 0.18f), At(34, ry + (rowH - 12 - t) / 2f, t, t), new Vector2(t, t), TopLeft);
                PlayerCardKit.Shape((RectTransform)tile.transform, "Icon", g.Done ? CardSprites.Tick() : MenuIcons.For("gavel"), colour, Vector2.zero, new Vector2(40, 40)).preserveAspect = true;
                var tx = 34 + t + 18;
                var text = TextAt(card, tx, ry + 8, w - tx - 32 - 120, 44, g.Text, 27, TextAnchor.MiddleLeft, Color.white, condensed: false, bold: false);
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 18;
                text.resizeTextMaxSize = 27;
                TextAt(card, w - 32 - 120, ry + 8, 120, 44, $"+{g.Xp} XP", 28, TextAnchor.MiddleRight, colour);
                var frac = g.Done ? 1f : g.Progress / (float)Mathf.Max(1, g.Target);
                BarAt(card, tx, ry + 62, w - tx - 32 - 100, frac, colour);
                TextAt(card, w - 32 - 90, ry + 48, 90, 38, g.Done ? "DONE" : $"{g.Progress}/{g.Target}", 24, TextAnchor.MiddleRight, g.Done ? XpGreen : UiKit.Dim);
            }

            // The auction career along the foot: four stat columns.
            var c = Online.RoomCareer.Load();
            var fy = h - careerH - 16f;
            PlayerCardKit.Shape(card, "CareerStrip", CardSprites.Round(16), new Color(0.03f, 0.03f, 0.1f, 0.9f), At(20, fy, w - 40, careerH), new Vector2(w - 40, careerH), TopLeft);
            TextAt(card, 40, fy + 10, w - 80, 30, "YOUR AUCTION CAREER", 20, TextAnchor.MiddleLeft, UiKit.Dim);
            var stats = new (string label, string value)[]
            {
                ("AUCTIONS", c.Played.ToString()),
                (c.Wins == 1 ? "WIN" : "WINS", c.Wins.ToString()),
                ("BEST", c.BestRank > 0 ? "#" + c.BestRank : "—"),
                ("POINTS", c.TotalPoints.ToString("N0")),
            };
            var cw = (w - 80) / stats.Length;
            for (var i = 0; i < stats.Length; i++)
            {
                var sx = 40 + i * cw;
                TextAt(card, sx, fy + 44, cw, 50, stats[i].value, 44, TextAnchor.MiddleLeft, UiKit.Gold).fontStyle = FontStyle.Italic;
                TextAt(card, sx, fy + 94, cw, 26, stats[i].label, 18, TextAnchor.MiddleLeft, UiKit.Dim);
            }
        }

        /// <summary>The match CHALLENGES as a slim row: star tile, name, stars · difficulty · cleared, ›. Tap to open.</summary>
        private void MatchChallengesRow(RectTransform page, float x, float y, float w, float h)
        {
            var card = HomeCard(page, "Challenges", x, y, w, h, CardFill, UiKit.WithAlpha(Cyan, 0.35f));
            card.GetComponent<Image>().raycastTarget = true;
            card.gameObject.AddComponent<Button>().onClick.AddListener(() => ModesInfo.OpenChallenges(_data));
            const float t = 76f;
            var tile = PlayerCardKit.Shape(card, "IconTile", CardSprites.Round(20), new Color(0.3f, 0.25f, 0.1f, 1f), At(22, (h - t) / 2f, t, t), new Vector2(t, t), TopLeft);
            PlayerCardKit.Shape((RectTransform)tile.transform, "Star", RewardFx.Star, UiKit.Gold, Vector2.zero, new Vector2(48, 48));
            var name = TextAt(card, 112, h / 2f - 46, w - 190, 50, "MATCH CHALLENGES", 36, TextAnchor.LowerLeft, Color.white);
            name.fontStyle = FontStyle.Italic;
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 24;
            name.resizeTextMaxSize = 36;
            var sub = TextAt(card, 113, h / 2f + 8, w - 190, 34,
                $"{ModesInfo.Stars}/{ModesInfo.MaxStars} stars  ·  {ModesInfo.Difficulty}  ·  {ModesInfo.ChallengesCleared}/{ModesInfo.ChallengesCount} cleared", 21, TextAnchor.UpperLeft, UiKit.Dim, condensed: false, bold: false);
            sub.resizeTextForBestFit = true;
            sub.resizeTextMinSize = 14;
            sub.resizeTextMaxSize = 21;
            TextAt(card, w - 70, h / 2f - 35, 50, 70, "›", 72, TextAnchor.MiddleCenter, UiKit.Dim);
        }

        /// <summary>
        /// AUCTION ► TOURNAMENT's middle: two facts right of the icon (10 FRANCHISES, LEAGUE +
        /// PLAYOFFS) and the path as three numbered steps under it (1 AUCTION › 2 PICK XI › 3 LEAGUE).
        /// </summary>
        private static void TourneyFlow(RectTransform face, float w, float h)
        {
            var violet = new Color(0.6f, 0.4f, 1f);
            // Facts beside the icon tile (it is 96 px at 32,32; the › sits top right).
            var fx = 150f;
            var fw = (w - fx - 100f - 12f) / 2f;
            Fact(face, fx, 40, fw, "10", "FRANCHISES", violet);
            Fact(face, fx + fw + 12, 40, fw, "TOP 4", "TO THE PLAYOFFS", violet);

            // The path, between the icon row and the title.
            var labelTop = h - 140f;
            var sy = 140f;
            var sh = Mathf.Min(52f, labelTop - sy - 6f);
            if (sh < 30f) return;
            string[] steps = { "AUCTION", "PICK XI", "LEAGUE" };
            const float arrow = 26f;
            var sw = (w - 64f - arrow * (steps.Length - 1)) / steps.Length;
            for (var i = 0; i < steps.Length; i++)
            {
                var sx = 32f + i * (sw + arrow);
                var pill = PlayerCardKit.Shape(face, "Step" + i, CardSprites.Round(14), i == 0 ? UiKit.WithAlpha(UiKit.Accent, 0.9f) : new Color(0.16f, 0.12f, 0.3f, 1f), At(sx, sy, sw, sh), new Vector2(sw, sh), TopLeft);
                var prt = (RectTransform)pill.transform;
                var t = PlayerCardKit.Text(prt, Vector2.zero, Vector2.one, new Vector2(8, 0), new Vector2(-8, 0), $"{i + 1}  {steps[i]}", 24, TextAnchor.MiddleCenter, Color.white, condensed: true);
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 14;
                t.resizeTextMaxSize = 24;
                if (i < steps.Length - 1) TextAt(face, sx + sw, sy, arrow, sh, "›", 34, TextAnchor.MiddleCenter, UiKit.Dim);
            }
        }

        private static void Fact(RectTransform face, float x, float y, float w, string big, string small, Color tint)
        {
            PlayerCardKit.Shape(face, "Fact_" + small, CardSprites.Round(14), UiKit.WithAlpha(tint, 0.14f), At(x, y, w, 80), new Vector2(w, 80), TopLeft);
            TextAt(face, x + 14, y + 4, w - 28, 44, big, 38, TextAnchor.MiddleLeft, Color.white).fontStyle = FontStyle.Italic;
            var s = TextAt(face, x + 14, y + 46, w - 28, 26, small, 18, TextAnchor.MiddleLeft, UiKit.Dim);
            s.resizeTextForBestFit = true;
            s.resizeTextMinSize = 12;
            s.resizeTextMaxSize = 18;
        }
    }
}
