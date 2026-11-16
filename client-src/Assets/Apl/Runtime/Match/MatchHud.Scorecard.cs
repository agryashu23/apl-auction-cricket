using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The full scorecard (owner v26 #19: "too compact, batting and bowling on one view"): a page
    /// with two rows of tabs — the innings (1ST / 2ND, each with its score) and the view
    /// (BATTING | BOWLING | SUMMARY) — and one roomy table at a time. BATTING: every man who
    /// batted, how he went, R B 4s 6s SR, the not-outs gold, then extras and the total, and who
    /// is yet to bat on one line. BOWLING: O R W ECON DOTS WD NB, wicket-takers lit. SUMMARY
    /// (from the ball-by-ball log): run rate, boundaries, dots, the fall of wickets, the
    /// partnerships as bars and the runs in each over (wickets marked). The tabs it was left on
    /// are kept for the next open. Public entry: <see cref="ShowScorecard()"/>.
    /// Button names for the test runs: ScoreTab_Inn1 / ScoreTab_Inn2, ScoreTab_Batting /
    /// ScoreTab_Bowling / ScoreTab_Summary, CloseScorecard.
    /// </summary>
    public sealed partial class MatchHud
    {
        /// <summary>One innings for the card, whichever book it came from.</summary>
        private sealed class CardInnings
        {
            public Franchise? Team;
            public string TeamName = "";
            public readonly List<(string name, string how, int r, int b, int fours, int sixes, bool notOut)> Bat = new List<(string, string, int, int, int, int, bool)>();
            public readonly List<string> YetToBat = new List<string>();
            public readonly List<(string name, int balls, int runs, int wkts, int dots, int wides, int noBalls)> Bowl = new List<(string, int, int, int, int, int, int)>();
            public int Runs, Wickets, Legal, Extras;
            public List<BallResult>? Log;
            public List<Player> Order = new List<Player>();
            public bool Live;
            /// <summary>"Extras 7 (wd 3, nb 2, b 1, lb 1)" (QuickMatch.ExtrasText for the innings being played).</summary>
            public string ExtrasLine = "";
            public string Score => $"{Runs}/{Wickets}";
        }

        private static int _cardInnings = -1, _cardView;
        private GameObject? _scorecard;

        private static readonly Color CardTabOff = new Color(0.12f, 0.14f, 0.22f, 0.95f);
        private static readonly Color CardStripe = new Color(1, 1, 1, 0.045f);

        /// <summary>The scorecard over the pause menu (or the HUD), on the tabs it was last left on.</summary>
        public void ShowScorecard() => ShowScorecard(_cardInnings, _cardView);

        /// <summary>The scorecard on innings <paramref name="innings"/> (1 / 2; -1 the one being played) and view 0 BATTING, 1 BOWLING, 2 SUMMARY.</summary>
        public void ShowScorecard(int innings, int view)
        {
            var src = Scorecard?.Invoke();
            if (src == null) return;
            var (m, batting, bowling) = src.Value;
            var cards = BuildCards(m, batting, bowling);
            var parent = _pauseMenu != null ? (RectTransform)_pauseMenu.transform : _root;
            if (_scorecard != null) Destroy(_scorecard);
            var dim = UiKit.Stretch(parent, "Scorecard", new Color(0.02f, 0.03f, 0.07f, 0.97f));
            _scorecard = dim.gameObject;
            var panel = PanelKit.GlassPanel(dim, "ScorecardPanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1760, 1010));
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -96), new Vector2(520, -20), "SCORECARD", 52, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            var status = m.MatchOver ? m.MatchResultText()
                : m.Chasing ? $"{batting.ShortName} need {m.RunsNeeded} off {m.BallsLeft} {(m.BallsLeft == 1 ? "ball" : "balls")}  ·  target {m.Target}"
                : $"{batting.ShortName} batting first  ·  {Cricket.FormatOvers(m.LegalBalls)} of {m.Overs} overs";
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(520, -96), new Vector2(-40, -20), status, 30, TextAnchor.MiddleRight, Color.white, condensed: true);
            var inns = innings == 2 && cards[1] != null ? 2 : innings == 1 ? 1 : cards[1] != null ? 2 : 1;
            _cardInnings = innings < 0 ? -1 : inns;
            _cardView = Mathf.Clamp(view, 0, 2);
            RectTransform? content = null;
            var tabs = new List<(Image img, int inn, int view)>();
            void Paint()
            {
                foreach (var (img, inn, v) in tabs)
                {
                    if (!img.GetComponent<Button>().interactable) continue;
                    var on = inn > 0 ? inn == inns : v == _cardView;
                    img.color = on ? UiKit.Gold : CardTabOff;
                    foreach (var t in img.GetComponentsInChildren<Text>()) t.color = on ? UiKit.Ink : Color.white;
                }
            }
            void Draw()
            {
                if (content != null)
                {
                    content.gameObject.SetActive(false);
                    Destroy(content.gameObject);
                }
                content = (RectTransform)new GameObject("CardContent", typeof(RectTransform)).transform;
                content.SetParent(panel, false);
                content.anchorMin = Vector2.zero;
                content.anchorMax = Vector2.one;
                content.offsetMin = content.offsetMax = Vector2.zero;
                var c = cards[inns - 1]!;
                if (_cardView == 0) BattingTable(content, c);
                else if (_cardView == 1) BowlingTable(content, c);
                else SummaryView(content, c, m.Overs);
                Paint();
            }
            // The innings tabs on the left, the view tabs on the right.
            for (var i = 0; i < 2; i++)
            {
                var n = i + 1;
                var c = cards[i];
                var label = c == null ? (n == 1 ? "1ST INNINGS" : "2ND INNINGS  ·  YET TO BAT") : $"{(n == 1 ? "1ST" : "2ND")} INNS  ·  {c.TeamName} {c.Score} ({Cricket.FormatOvers(c.Legal)})";
                var img = CardTab(panel, "ScoreTab_Inn" + n, label, new Vector2(40 + 225 + i * 462, -150), 450, c != null, () =>
                {
                    inns = n;
                    _cardInnings = n;
                    Draw();
                });
                tabs.Add((img, n, -1));
                if (c?.Team != null)
                    PlayerCardKit.Shape((RectTransform)img.transform, "TeamBar", CardSprites.Round(4), PlayerCardKit.Bright(UiKit.ParseHex(c.Team.PrimaryColor)), new Vector2(0, 6), new Vector2(410, 6), new Vector2(0.5f, 0));
            }
            var views = new[] { "BATTING", "BOWLING", "SUMMARY" };
            for (var v = 0; v < views.Length; v++)
            {
                var k = v;
                var img = CardTab(panel, "ScoreTab_" + views[v][0] + views[v].Substring(1).ToLowerInvariant(), views[v], new Vector2(1760 - 40 - 110 - (views.Length - 1 - v) * 232, -150), 220, true, () =>
                {
                    _cardView = k;
                    Draw();
                });
                tabs.Add((img, -1, v));
            }
            Draw();
            PanelKit.Pill(panel, "CloseScorecard", new Vector2(1, 0), new Vector2(-230, 74), new Vector2(380, 120), "BACK", UiKit.Accent, () =>
            {
                if (_scorecard != null) Destroy(_scorecard);
                _scorecard = null;
            }, 36);
        }

        private static Image CardTab(RectTransform panel, string name, string label, Vector2 pos, float width, bool enabled, System.Action act)
        {
            var img = PlayerCardKit.Shape(panel, name, CardSprites.Round(18), CardTabOff, pos, new Vector2(width, 84), new Vector2(0, 1));
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            b.interactable = enabled;
            b.onClick.AddListener(() => act());
            PlayerCardKit.Text((RectTransform)img.transform, Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0), label, 28, TextAnchor.MiddleCenter, enabled ? Color.white : UiKit.Dim, condensed: true);
            return img;
        }

        // ------------------------------------------------------------ the books

        /// <summary>[0] the first innings, [1] the second (null until it starts).</summary>
        private static CardInnings?[] BuildCards(QuickMatch m, Franchise batting, Franchise bowling)
        {
            var current = new CardInnings { Team = batting, TeamName = batting.ShortName, Runs = m.Runs, Wickets = m.Wickets, Legal = m.LegalBalls, Extras = m.Extras, Log = m.Log, Live = !m.MatchOver, ExtrasLine = m.ExtrasText() };
            foreach (var b in m.Order)
            {
                current.Order.Add(b.Player);
                if (b.Batted) current.Bat.Add((b.Player.Name, b.HowOut ?? "not out", b.Runs, b.Balls, b.Fours, b.Sixes, b.HowOut == null));
                else current.YetToBat.Add(b.Player.Name);
            }
            foreach (var f in m.BowlingFigures) current.Bowl.Add((f.Player.Name, f.Balls, f.Runs, f.Wickets, f.Dots, f.Wides, f.NoBalls));
            if (m.TwoInnings && m.InningsNumber == 1) return new[] { current, null };
            var fi = m.FirstInnings;
            var firstTeam = bowling.Id == fi.BattingTeamId ? bowling : batting.Id == fi.BattingTeamId ? batting : bowling;
            var first = new CardInnings { Team = firstTeam, TeamName = firstTeam.ShortName, Runs = fi.Runs, Wickets = fi.Wickets, Legal = fi.LegalBalls, Extras = fi.Extras, Log = m.FirstLog, ExtrasLine = ExtrasOf(m.FirstLog, fi.Extras) };
            if (m.FirstOrder != null)
            {
                foreach (var b in m.FirstOrder)
                {
                    first.Order.Add(b.Player);
                    if (b.Batted) first.Bat.Add((b.Player.Name, b.HowOut ?? "not out", b.Runs, b.Balls, b.Fours, b.Sixes, b.HowOut == null));
                    else first.YetToBat.Add(b.Player.Name);
                }
            }
            else
            {
                foreach (var b in fi.Batting)
                {
                    first.Order.Add(b.Player);
                    if (b.Batted) first.Bat.Add((b.Player.Name, b.Dismissal ?? "not out", b.Runs, b.Balls, b.Fours, b.Sixes, b.Dismissal == null));
                    else first.YetToBat.Add(b.Player.Name);
                }
            }
            var bowlers = m.FirstBowling != null
                ? m.FirstBowling.Select(f => (f.Player.Name, f.Balls, f.Runs, f.Wickets, f.Dots, f.Wides, f.NoBalls))
                : fi.Bowling.Select(f => (f.Player.Name, f.Balls, f.Runs, f.Wickets, f.Dots, f.Wides, f.NoBalls));
            first.Bowl.AddRange(bowlers);
            return new[] { first, current };
        }

        // ------------------------------------------------------------ the views

        /// <summary>A cell: text in a column from <paramref name="x"/>, <paramref name="width"/> wide, centred on <paramref name="y"/>.</summary>
        private static Text Cell(RectTransform parent, float x, float y, float width, string text, int size, Color colour, TextAnchor align = TextAnchor.MiddleLeft, bool bold = false, bool condensed = false)
        {
            var t = PlayerCardKit.Text(parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, y - 24), new Vector2(x + width, y + 24), text, size, align, colour, condensed, bold);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        private static void Stripe(RectTransform parent, float y, float height, Color colour) =>
            PlayerCardKit.Shape(parent, "Stripe", CardSprites.Round(10), colour, new Vector2(880, y), new Vector2(1680, height), new Vector2(0, 1));

        private const float CardTop = -232f, CardRow = 48f;

        private static void BattingTable(RectTransform c, CardInnings inn)
        {
            float[] w = { 440, 560, 120, 110, 100, 100, 140 };
            string[] head = { "BATTER", "", "R", "B", "4s", "6s", "SR" };
            var y = CardTop;
            var x = 70f;
            for (var i = 0; i < head.Length; i++) { Cell(c, x, y, w[i], head[i], 24, UiKit.Dim, i < 2 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, condensed: true); x += w[i]; }
            y -= 46;
            var row = 0;
            foreach (var b in inn.Bat)
            {
                if (row++ % 2 == 0) Stripe(c, y, CardRow - 4, CardStripe);
                var sr = b.b > 0 ? (b.r * 100.0 / b.b).ToString("0.0", CultureInfo.InvariantCulture) : "-";
                var name = b.notOut ? UiKit.Gold : Color.white;
                x = 70f;
                Cell(c, x, y, w[0], b.name + (b.notOut ? " *" : ""), 30, name, bold: true); x += w[0];
                Cell(c, x, y, w[1], b.how, 24, b.notOut ? UiKit.Gold : UiKit.Dim); x += w[1];
                Cell(c, x, y, w[2], b.r.ToString(), 32, name, TextAnchor.MiddleCenter, true, true); x += w[2];
                Cell(c, x, y, w[3], b.b.ToString(), 26, Color.white, TextAnchor.MiddleCenter); x += w[3];
                Cell(c, x, y, w[4], b.fours.ToString(), 26, b.fours > 0 ? Color.white : UiKit.Dim, TextAnchor.MiddleCenter); x += w[4];
                Cell(c, x, y, w[5], b.sixes.ToString(), 26, b.sixes > 0 ? Color.white : UiKit.Dim, TextAnchor.MiddleCenter); x += w[5];
                Cell(c, x, y, w[6], sr, 26, UiKit.Dim, TextAnchor.MiddleCenter);
                y -= CardRow;
            }
            y -= 8;
            Stripe(c, y, CardRow + 2, new Color(1, 1, 1, 0.09f));
            Cell(c, 70, y, 440, inn.ExtrasLine.ToUpperInvariant(), 26, UiKit.Dim, condensed: true, bold: true);
            Cell(c, 510, y, 1100, $"TOTAL  {inn.Score}  ({Cricket.FormatOvers(inn.Legal)} ov)   ·   RR {Rate(inn.Runs, inn.Legal)}", 34, UiKit.Gold, TextAnchor.MiddleLeft, true, true);
            if (inn.YetToBat.Count > 0)
            {
                y -= CardRow + 6;
                var t = Cell(c, 70, y, 1240, "YET TO BAT:  " + string.Join("  ·  ", inn.YetToBat), 24, UiKit.Dim);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
        }

        private static void BowlingTable(RectTransform c, CardInnings inn)
        {
            float[] w = { 520, 150, 140, 140, 160, 150, 130, 130 };
            string[] head = { "BOWLER", "O", "R", "W", "ECON", "DOTS", "WD", "NB" };
            var y = CardTop;
            var x = 70f;
            for (var i = 0; i < head.Length; i++) { Cell(c, x, y, w[i], head[i], 24, UiKit.Dim, i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, condensed: true); x += w[i]; }
            y -= 46;
            if (inn.Bowl.Count == 0) Cell(c, 70, y, 1200, "No overs bowled yet.", 26, UiKit.Dim);
            var row = 0;
            foreach (var f in inn.Bowl)
            {
                if (row++ % 2 == 0) Stripe(c, y, CardRow - 4, CardStripe);
                var econ = f.balls > 0 ? (f.runs * 6.0 / f.balls).ToString("0.00", CultureInfo.InvariantCulture) : "-";
                x = 70f;
                Cell(c, x, y, w[0], f.name, 30, Color.white, bold: true); x += w[0];
                Cell(c, x, y, w[1], Cricket.FormatOvers(f.balls), 28, Color.white, TextAnchor.MiddleCenter); x += w[1];
                Cell(c, x, y, w[2], f.runs.ToString(), 28, Color.white, TextAnchor.MiddleCenter); x += w[2];
                Cell(c, x, y, w[3], f.wkts.ToString(), 32, f.wkts > 0 ? UiKit.Gold : UiKit.Dim, TextAnchor.MiddleCenter, true, true); x += w[3];
                Cell(c, x, y, w[4], econ, 28, Color.white, TextAnchor.MiddleCenter); x += w[4];
                Cell(c, x, y, w[5], f.dots.ToString(), 26, UiKit.Dim, TextAnchor.MiddleCenter); x += w[5];
                Cell(c, x, y, w[6], f.wides.ToString(), 26, f.wides > 0 ? Color.white : UiKit.Dim, TextAnchor.MiddleCenter); x += w[6];
                Cell(c, x, y, w[7], f.noBalls.ToString(), 26, f.noBalls > 0 ? Color.white : UiKit.Dim, TextAnchor.MiddleCenter);
                y -= CardRow;
            }
            y -= 8;
            Stripe(c, y, CardRow + 2, new Color(1, 1, 1, 0.09f));
            Cell(c, 70, y, 1600, $"{inn.Bowl.Sum(f => f.wkts)} WICKETS  ·  {inn.Bowl.Sum(f => f.wides)} WIDES  ·  {inn.Bowl.Sum(f => f.noBalls)} NO-BALLS  ·  EXTRAS {inn.Extras}", 28, UiKit.Gold, condensed: true, bold: true);
        }

        /// <summary>The first innings' extras line in QuickMatch.ExtrasText's words, from its book (the simulated one has only the total).</summary>
        private static string ExtrasOf(List<BallResult>? log, int extras)
        {
            if (log == null) return $"Extras {extras}";
            int wd = 0, nb = 0, b = 0, lb = 0;
            foreach (var r in log)
            {
                if (r.Wide) wd += r.Penalty;
                else if (r.NoBall) nb += r.Penalty;
                b += r.Byes;
                lb += r.LegByes;
            }
            var parts = new List<string>();
            if (wd > 0) parts.Add($"wd {wd}");
            if (nb > 0) parts.Add($"nb {nb}");
            if (b > 0) parts.Add($"b {b}");
            if (lb > 0) parts.Add($"lb {lb}");
            return parts.Count == 0 ? $"Extras {extras}" : $"Extras {extras} ({string.Join(", ", parts)})";
        }

        private static string Rate(int runs, int legal) => legal > 0 ? (runs * 6.0 / legal).ToString("0.00", CultureInfo.InvariantCulture) : "-";

        /// <summary>"R. Sharma" for a summary line.</summary>
        private static string Short(Player? p) => p == null ? "—" : ShortName(p.Name);

        /// <summary>SUMMARY: the headline numbers, then FALL OF WICKETS, PARTNERSHIPS and RUNS PER OVER side by side.</summary>
        private static void SummaryView(RectTransform c, CardInnings inn, int overs)
        {
            var log = inn.Log;
            var fours = inn.Bat.Sum(b => b.fours);
            var sixes = inn.Bat.Sum(b => b.sixes);
            var dots = log?.Count(r => r.Legal && r.Runs == 0) ?? inn.Bowl.Sum(f => f.dots);
            Cell(c, 70, CardTop, 1620, $"{inn.TeamName}  {inn.Score}  ({Cricket.FormatOvers(inn.Legal)} ov)   ·   RUN RATE {Rate(inn.Runs, inn.Legal)}   ·   {fours} × 4   {sixes} × 6   ·   {dots} DOTS   ·   EXTRAS {inn.Extras}", 30, UiKit.Gold, condensed: true, bold: true);
            if (log == null)
            {
                Cell(c, 70, CardTop - 80, 1600, "The ball-by-ball book isn't kept for a simulated innings: see BATTING and BOWLING.", 26, UiKit.Dim);
                return;
            }
            // Walk the book: the fall of wickets, the stands (who was in, runs, balls), each over's runs and wickets.
            var fow = new List<(int wkt, int score, string over, string who)>();
            var stands = new List<(int runs, int balls, string pair, bool open)>();
            var perOver = new int[Mathf.Max(1, overs)];
            var wktsOver = new int[perOver.Length];
            var pair = new List<Player>();
            int total = 0, legal = 0, standRuns = 0, standBalls = 0, wkts = 0;
            foreach (var r in log)
            {
                if (r.Facing != null && !pair.Contains(r.Facing) && pair.Count < 2) pair.Add(r.Facing);
                var ov = Mathf.Min(perOver.Length - 1, legal / 6);
                total += r.Runs;
                standRuns += r.Runs;
                perOver[ov] += r.Runs;
                if (r.Legal) { legal++; standBalls++; }
                if (!r.Wicket) continue;
                wkts++;
                wktsOver[ov]++;
                var outP = r.OutBatter ?? r.Facing;
                fow.Add((wkts, total, Cricket.FormatOvers(legal), Short(outP)));
                stands.Add((standRuns, standBalls, string.Join(" & ", pair.Select(Short)), false));
                if (outP != null) pair.Remove(outP);
                standRuns = standBalls = 0;
            }
            if (standRuns > 0 || standBalls > 0 || inn.Live) stands.Add((standRuns, standBalls, string.Join(" & ", pair.Select(Short)), true));

            // FALL OF WICKETS.
            float x0 = 70, y0 = CardTop - 70;
            Cell(c, x0, y0, 460, "FALL OF WICKETS", 28, Color.white, condensed: true, bold: true);
            var y = y0 - 48;
            if (fow.Count == 0) Cell(c, x0, y, 460, "No wickets yet.", 24, UiKit.Dim);
            foreach (var f in fow.Take(10))
            {
                Cell(c, x0, y, 110, $"{f.score}-{f.wkt}", 28, UiKit.Gold, bold: true, condensed: true);
                Cell(c, x0 + 110, y, 250, f.who, 24, Color.white);
                Cell(c, x0 + 360, y, 110, f.over + " ov", 22, UiKit.Dim, TextAnchor.MiddleRight);
                y -= 46;
            }

            // PARTNERSHIPS: a bar each, the longest full width.
            float x1 = 590;
            Cell(c, x1, y0, 520, "PARTNERSHIPS", 28, Color.white, condensed: true, bold: true);
            y = y0 - 48;
            var best = Mathf.Max(1, stands.Count == 0 ? 1 : stands.Max(s => s.runs));
            var ordinal = new[] { "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th", "9th", "10th" };
            for (var i = 0; i < stands.Count && i < 10; i++)
            {
                var s = stands[i];
                Cell(c, x1, y + 10, 60, ordinal[i], 20, UiKit.Dim, condensed: true);
                Cell(c, x1 + 60, y + 10, 380, s.pair, 20, s.open ? UiKit.Gold : Color.white);
                Cell(c, x1 + 380, y + 10, 140, $"{s.runs}{(s.open ? "*" : "")} ({s.balls})", 22, Color.white, TextAnchor.MiddleRight, true, true);
                var full = 460f;
                PlayerCardKit.Shape(c, "StandBack", CardSprites.Round(5), new Color(1, 1, 1, 0.08f), new Vector2(x1 + 60 + full / 2, y - 14), new Vector2(full, 10), new Vector2(0, 1));
                var bw = Mathf.Max(10f, full * s.runs / best);
                PlayerCardKit.Shape(c, "Stand", CardSprites.Round(5), s.open ? UiKit.Gold : new Color(0.45f, 0.7f, 1f), new Vector2(x1 + 60 + bw / 2, y - 14), new Vector2(bw, 10), new Vector2(0, 1));
                y -= 50;
            }

            // RUNS PER OVER: a bar each, a red dot above for each wicket.
            float x2 = 1150, chartW = 560, chartH = 360;
            Cell(c, x2, y0, 560, "RUNS PER OVER", 28, Color.white, condensed: true, bold: true);
            var baseY = y0 - 60 - chartH;
            var colW = chartW / perOver.Length;
            var top = Mathf.Max(12, perOver.Max());
            PlayerCardKit.Shape(c, "ChartBase", CardSprites.Round(1), new Color(1, 1, 1, 0.25f), new Vector2(x2 + chartW / 2, baseY), new Vector2(chartW, 2), new Vector2(0, 1));
            for (var o = 0; o < perOver.Length; o++)
            {
                var played = o * 6 < legal || perOver[o] > 0;
                var bh = played ? Mathf.Max(3f, chartH * perOver[o] / top) : 0f;
                var cx = x2 + colW * (o + 0.5f);
                if (bh > 0)
                {
                    var colour = perOver[o] >= 12 ? UiKit.Gold : perOver[o] >= 8 ? new Color(0.45f, 0.7f, 1f) : new Color(0.55f, 0.6f, 0.72f);
                    PlayerCardKit.Shape(c, "Over" + (o + 1), CardSprites.Round(3), colour, new Vector2(cx, baseY + bh / 2), new Vector2(Mathf.Max(6, colW * 0.7f), bh), new Vector2(0, 1));
                    if (perOver[o] > 0 && colW >= 22) Cell(c, cx - 30, baseY + bh + 16, 60, perOver[o].ToString(), 18, Color.white, TextAnchor.MiddleCenter, condensed: true);
                }
                for (var k = 0; k < wktsOver[o]; k++)
                    PlayerCardKit.Shape(c, "Wkt", CardSprites.Disc(), UiKit.Bad, new Vector2(cx, baseY + bh + 38 + k * 18), new Vector2(14, 14), new Vector2(0, 1));
                if (perOver.Length <= 10 || (o + 1) % 5 == 0 || o == 0)
                    Cell(c, cx - 30, baseY - 22, 60, (o + 1).ToString(), 18, UiKit.Dim, TextAnchor.MiddleCenter, condensed: true);
            }
            Cell(c, x2, baseY - 60, 560, "gold 12+ · blue 8+ · red dot = wicket", 18, UiKit.Dim, TextAnchor.MiddleCenter);
        }
    }
}
