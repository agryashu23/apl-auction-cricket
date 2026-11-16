using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The innings break / target screen in the card kit (round 8; it was a plain
    /// brown box): a banner in the first-innings side's colours with its crest and
    /// score, glass tiles for its top batters and the other side's top bowlers, the
    /// chase strip in the chasing side's colours, and the go button.
    /// </summary>
    public sealed partial class MatchHud
    {
        private void BreakScreen(string kicker, Innings first, Franchise firstBat, Franchise chasing, string venueLine, string chase, string go, string button, IReadOnlyList<BallResult>? log = null)
        {
            if (_card != null) Destroy(_card);
            var page = UiKit.Stretch(_root, "BreakScreen", new Color(0.01f, 0.012f, 0.03f, 0.86f));
            _card = page.gameObject;

            var kit = PlayerCardKit.Bright(UiKit.ParseHex(firstBat.PrimaryColor));
            var banner = PlayerCardKit.Shape(page, "Banner", CardSprites.Round(24), Color.Lerp(kit, Color.black, 0.45f), new Vector2(0, 320), new Vector2(1560, 220));
            PlayerCardKit.Shape((RectTransform)banner.transform, "Band", CardSprites.Round(4), kit, new Vector2(0, 4), new Vector2(1500, 6), new Vector2(0.5f, 0));
            UiKit.Crest(page, firstBat.Id, Centre, new Vector2(-660, 320), 180);
            PlayerCardKit.Text(page, Centre, Centre, new Vector2(-540, 370), new Vector2(200, 420), kicker, 30, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            PlayerCardKit.Text(page, Centre, Centre, new Vector2(-540, 290), new Vector2(200, 372), firstBat.Name.ToUpperInvariant(), 64, TextAnchor.MiddleLeft, Color.white, condensed: true);
            PlayerCardKit.Text(page, Centre, Centre, new Vector2(-540, 236), new Vector2(700, 290), venueLine, 24, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
            PlayerCardKit.Text(page, Centre, Centre, new Vector2(200, 284), new Vector2(740, 426), first.Score, 120, TextAnchor.MiddleRight, Color.white, condensed: true);
            PlayerCardKit.Text(page, Centre, Centre, new Vector2(200, 236), new Vector2(740, 280), $"{first.Overs} OVERS  ·  EXTRAS {first.Extras}", 26, TextAnchor.MiddleRight, UiKit.Gold, condensed: true);

            var bats = first.Batting.Where(b => b.Balls > 0 || b.Batted).OrderByDescending(b => b.Runs).Take(3)
                .Select(b => (b.Player.Name, $"{b.Runs} ({b.Balls})")).ToList();
            var bowls = first.Bowling.Where(b => b.Balls > 0).OrderByDescending(b => b.Wickets).ThenBy(b => b.Runs).Take(3)
                .Select(b => (b.Player.Name, $"{b.Wickets}-{b.Runs} ({b.Overs})")).ToList();
            ListTile(page, "TOP BATTERS  ·  " + firstBat.ShortName, bats, -400);
            ListTile(page, "TOP BOWLERS  ·  " + chasing.ShortName, bowls, 400);

            // The fall of wickets, from the innings' own balls (a full match keeps them; the quick chase's simulated innings doesn't).
            var fow = FallOfWickets(log);
            if (fow.Length > 0)
            {
                PlayerCardKit.Text(page, Centre, Centre, new Vector2(-780, -192), new Vector2(-560, -146), "FALL OF WICKETS", 26, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
                var t = PlayerCardKit.Text(page, Centre, Centre, new Vector2(-550, -192), new Vector2(780, -146), fow, 26, TextAnchor.MiddleLeft, Color.white, condensed: true);
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 16;
                t.resizeTextMaxSize = 26;
            }

            // The chase, in the chasing side's colours.
            var ck = PlayerCardKit.Bright(UiKit.ParseHex(chasing.PrimaryColor));
            var strip = PlayerCardKit.Shape(page, "Chase", CardSprites.Round(22), Color.Lerp(ck, Color.black, 0.5f), new Vector2(0, -260), new Vector2(1560, 110));
            PlayerCardKit.Shape((RectTransform)strip.transform, "Edge", CardSprites.Round(4), ck, new Vector2(4, 0), new Vector2(6, 90), new Vector2(0, 0.5f));
            UiKit.Crest(page, chasing.Id, Centre, new Vector2(-700, -260), 96);
            PlayerCardKit.Text(page, Centre, Centre, new Vector2(-620, -310), new Vector2(760, -210), chase, 48, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            // Named as before (StartChase / StartSecond): the AutoShot run clicks it by name.
            PanelKit.Pill(page, button, Centre, new Vector2(0, -410), new Vector2(560, 120), go, UiKit.Accent, () => StartPressed?.Invoke(), 42);
        }

        /// <summary>A glass tile of up to three name / figures lines.</summary>
        private static void ListTile(RectTransform page, string heading, List<(string name, string figs)> rows, float x)
        {
            var tile = PanelKit.GlassPanel(page, "List_" + heading, Centre, new Vector2(x, 20), new Vector2(760, 300));
            PlayerCardKit.Text(tile, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -62), new Vector2(-30, -14), heading, 30, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            if (rows.Count == 0)
            {
                PlayerCardKit.Text(tile, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, "—", 40, TextAnchor.MiddleCenter, UiKit.Dim, condensed: true);
                return;
            }
            for (var i = 0; i < rows.Count; i++)
            {
                var top = -78f - i * 70f;
                if (i > 0) PlayerCardKit.Shape(tile, "Rule" + i, CardSprites.Round(2), new Color(1, 1, 1, 0.08f), new Vector2(0, top + 2), new Vector2(700, 2), new Vector2(0.5f, 1));
                PlayerCardKit.Text(tile, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, top - 64), new Vector2(-260, top), rows[i].name.ToUpperInvariant(), 34, TextAnchor.MiddleLeft, Color.white, condensed: true);
                PlayerCardKit.Text(tile, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-260, top - 64), new Vector2(-30, top), rows[i].figs, 34, TextAnchor.MiddleRight, i == 0 ? UiKit.Gold : Color.white, condensed: true);
            }
        }

        /// <summary>The chase's opening: the opponent's (simulated) innings and the target.</summary>
        private void TargetScreen(QuickMatch m, Franchise batting, Franchise bowling)
        {
            var venue = m.Venue != null ? $"{m.Venue.Name}, {m.Venue.City}".ToUpperInvariant() : "";
            BreakScreen("FIRST INNINGS", m.FirstInnings, bowling, batting, venue, $"{batting.Name.ToUpperInvariant()} NEED {m.Target} FROM {m.Overs * 6} BALLS", "START CHASE ►", "StartChase");
        }

        private void InningsBreakScreen(Innings first, Franchise firstBat, QuickMatch m, Franchise chasing) =>
            BreakScreen("INNINGS BREAK", first, firstBat, chasing, m.Venue != null ? $"{m.Venue.Name}, {m.Venue.City}".ToUpperInvariant() : "",
                $"{chasing.Name.ToUpperInvariant()} NEED {m.Target} FROM {m.Overs * 6} BALLS", "SECOND INNINGS ►", "StartSecond", m.FirstLog ?? (m.InningsNumber == 1 ? m.Log : null));

        /// <summary>"1-12 (1.3)   2-20 (2.4)": the score and the over when each wicket fell.</summary>
        private static string FallOfWickets(IReadOnlyList<BallResult>? log)
        {
            if (log == null) return "";
            var runs = 0;
            var balls = 0;
            var wickets = 0;
            var parts = new List<string>();
            foreach (var r in log)
            {
                runs += r.Runs;
                if (r.Legal) balls++;
                if (!r.Wicket) continue;
                wickets++;
                parts.Add($"{wickets}-{runs} ({Cricket.FormatOvers(balls)})");
            }
            return string.Join("    ", parts);
        }
    }
}
