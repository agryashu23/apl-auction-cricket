using System.Collections.Generic;
using System.Linq;
using Apl.Client.Online;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// A room auction's rewards on the results screen: the PODIUM for the winner, the
    /// STANDINGS with points (tap a row for how that team earned them), and the AWARDS cards
    /// (Best Squad, Steal of the Auction, Big Spender, Most Balanced, Value King …). The
    /// player's lifetime totals (RoomCareer) sit along the bottom of every results screen.
    /// </summary>
    public sealed partial class ResultsScreen
    {
        private RoomRewards? _rewards;
        private string? _breakdownTeam;

        private static IEnumerable<(string, string)> RewardTabs() => new[] { ("podium", "PODIUM"), ("standings", "STANDINGS"), ("awards", "AWARDS") };

        private string SeatName(RoomStanding s) => s.Human && s.Name.Length > 0 ? s.Name : "AI BOT";

        private void CareerLine()
        {
            var c = RoomCareer.Load();
            var box = UiKit.Box(_root, "Career", new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(760, 64), new Color(0.06f, 0.08f, 0.14f, 0.92f));
            UiKit.LabelAt(box, new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(740, 24), "YOUR CAREER", 18, TextAnchor.MiddleCenter, UiKit.Dim);
            UiKit.LabelAt(box, new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(740, 32), c.Line(), 24, TextAnchor.MiddleCenter, UiKit.Gold);
        }

        // ------------------------------------------------------------- podium

        private void ShowPodium()
        {
            var r = _rewards!;
            var top = r.Standings.Take(3).ToList();
            // 2nd | 1st | 3rd, the winner's block tallest.
            var order = new[] { 1, 0, 2 };
            var heights = new[] { 300f, 220f, 170f };
            var xs = new[] { -440f, 0f, 440f };
            for (var k = 0; k < order.Length; k++)
            {
                var i = order[k];
                if (i >= top.Count) continue;
                var s = top[i];
                var team = Team(s.TeamId);
                var colour = UiKit.ParseHex(team.PrimaryColor);
                var h = heights[i];
                var block = UiKit.EdgedPanel(_body, "Podium_" + (i + 1), new Vector2(0.5f, 0), new Vector2(xs[k], 60 + h / 2), new Vector2(380, h),
                    UiKit.WithAlpha(colour, 0.92f), i == 0 ? UiKit.Gold : new Color(0.75f, 0.78f, 0.85f));
                var ink = colour.grayscale > 0.55f ? UiKit.Ink : Color.white;
                UiKit.LabelAt(block, new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(360, 70), r.PlaceLabel(s), i == 0 ? 64 : 52, TextAnchor.MiddleCenter, ink);
                // Tied on points: say how the tie was split (or that the place is shared).
                var tie = r.TieNote(s);
                UiKit.LabelAt(block, new Vector2(0.5f, 0), new Vector2(0, tie != null ? 58 : 40), new Vector2(360, 44), $"{s.Points:N0} PTS", 34, TextAnchor.MiddleCenter, ink);
                if (tie != null)
                {
                    var note = UiKit.LabelAt(block, new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(366, 30), tie.ToUpperInvariant(), 16, TextAnchor.MiddleCenter, ink, bold: false);
                    note.resizeTextForBestFit = true;
                    note.resizeTextMinSize = 11;
                    note.resizeTextMaxSize = 16;
                    Debug.Log($"[APL room] podium tie: {s.TeamId} {r.PlaceLabel(s)} {s.Points} pts overall {s.Overall}: {tie}");
                }
                if (s.TeamId == _me) YouMark.Add(block, "YOU", 6);
                // Above the block: crest, team, who played it.
                var crestSize = i == 0 ? 190f : 140f;
                UiKit.Crest(_body, s.TeamId, new Vector2(0.5f, 0), new Vector2(xs[k], 60 + h + 50 + crestSize / 2), crestSize);
                UiKit.LabelAt(_body, new Vector2(0.5f, 0), new Vector2(xs[k], 60 + h + 26), new Vector2(400, 36), team.ShortName + "  ·  " + SeatName(s), 26, TextAnchor.MiddleCenter, Color.white);
                if (i == 0)
                {
                    UiKit.LabelAt(_body, new Vector2(0.5f, 0), new Vector2(0, 60 + h + 84 + crestSize), new Vector2(900, 60), r.Place(s).Shared ? "JOINT WINNERS" : "WINNER", 54, TextAnchor.MiddleCenter, UiKit.Gold);
                }
            }
            var mine = r.For(_me);
            if (mine != null)
            {
                var (place, shared) = r.Place(mine);
                // Rater r1 #3: three unlabelled ranks (9th / #7 / #1). The headline names each one.
                var (squadRank, seasonPos) = OtherRanks();
                var head = place == 1
                    ? $"AUCTION POINTS RANK: {(shared ? "JOINT 1ST" : "1ST")} ({mine.Points:N0} PTS), YOU WON THE AUCTION!"
                    : $"AUCTION POINTS RANK: {r.PlaceLabel(mine)} OF {r.Standings.Count} ({mine.Points:N0} PTS)";
                var text = $"{head}   ·   SQUAD RATING RANK #{squadRank}   ·   SEASON SIM POSITION #{seasonPos}";
                var line = UiKit.LabelAt(_body, new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(1680, 44), text, 30, TextAnchor.MiddleCenter, YouMark.Lime);
                line.resizeTextForBestFit = true;
                line.resizeTextMinSize = 18;
                line.resizeTextMaxSize = 30;
            }
        }

        // ---------------------------------------------------------- standings

        private void ShowStandings()
        {
            var r = _rewards!;
            _breakdownTeam ??= r.For(_me) != null ? _me : r.Standings.FirstOrDefault()?.TeamId;
            const float width = 1000;
            var left = UiKit.Box(_body, "Standings", new Vector2(0, 1), new Vector2(width / 2, -360), new Vector2(width, 720), new Color(0, 0, 0, 0));
            var cols = new (float, string, TextAnchor)[]
            {
                (70, "#", TextAnchor.MiddleCenter), (70, "", TextAnchor.MiddleCenter), (220, "TEAM", TextAnchor.MiddleLeft),
                (440, "PLAYER", TextAnchor.MiddleLeft), (200, "POINTS", TextAnchor.MiddleCenter),
            };
            Row(left, -RowH / 2, width, new Color(0, 0, 0, 0), UiKit.Dim, 24, cols);
            // Ten teams at most: rows a little shorter than the ratings table so all fit with the header.
            for (var i = 0; i < r.Standings.Count && i < 11; i++)
            {
                var s = r.Standings[i];
                var y = -RowH / 2 - (i + 1) * RowH;
                var selected = s.TeamId == _breakdownTeam;
                var row = Row(left, y, width, selected ? new Color(0.95f, 0.76f, 0.31f, 0.28f) : RowColor(s.TeamId, i), Color.white, 26,
                    (70, (r.Place(s).Shared ? "=" : "") + r.Place(s).Place, TextAnchor.MiddleCenter), (70, "", TextAnchor.MiddleCenter),
                    (220, Team(s.TeamId).ShortName, TextAnchor.MiddleLeft),
                    (440, SeatName(s) + (s.TeamId == _me ? "  · YOU" : ""), TextAnchor.MiddleLeft),
                    (200, s.Points.ToString("N0"), TextAnchor.MiddleCenter));
                row.name = "Standing_" + s.TeamId;
                MarkMine(row, s.TeamId);
                CrestAt(left, s.TeamId, -width / 2 + 70 + 35, y);
                var team = s.TeamId;
                row.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    _breakdownTeam = team;
                    Show("standings");
                });
            }
            Breakdown(r.For(_breakdownTeam ?? ""));
            // Ties on points: how each was split, under the table.
            var notes = r.Standings.Select(st => (st, note: r.TieNote(st))).Where(x => x.note != null).Select(x => $"{Team(x.st.TeamId).ShortName}: {x.note}").Distinct().ToList();
            if (notes.Count > 0)
            {
                var foot = UiKit.LabelAt(left, new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(width, 40), string.Join("   ·   ", notes), 18, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
                foot.resizeTextForBestFit = true;
                foot.resizeTextMinSize = 11;
                foot.resizeTextMaxSize = 18;
            }
        }

        /// <summary>The right-hand card: how one team earned its points, line by line.</summary>
        private void Breakdown(RoomStanding? s)
        {
            var card = UiKit.Box(_body, "Breakdown", new Vector2(1, 1), new Vector2(-330, -360), new Vector2(660, 720), UiKit.Panel);
            if (s == null) return;
            var team = Team(s.TeamId);
            UiKit.Crest(card, s.TeamId, new Vector2(0, 1), new Vector2(80, -80), 110);
            UiKit.LabelAt(card, new Vector2(0, 1), new Vector2(380, -60), new Vector2(480, 44), team.Name.ToUpperInvariant(), 30, TextAnchor.MiddleLeft);
            UiKit.LabelAt(card, new Vector2(0, 1), new Vector2(380, -104), new Vector2(480, 34), $"AUCTION POINTS RANK #{s.Rank}  ·  {SeatName(s)}", 24, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
            UiKit.LabelAt(card, new Vector2(0.5f, 1), new Vector2(0, -170), new Vector2(620, 32), "HOW THE POINTS WERE EARNED", 22, TextAnchor.MiddleCenter, UiKit.Dim);
            var lines = s.Breakdown;
            var rowH = Mathf.Min(54f, 400f / Mathf.Max(1, lines.Count));
            for (var i = 0; i < lines.Count; i++)
            {
                var y = -215 - i * rowH;
                var bg = UiKit.Box(card, "Line", new Vector2(0.5f, 1), new Vector2(0, y - rowH / 2 + 4), new Vector2(620, rowH - 6),
                    i % 2 == 0 ? new Color(0.09f, 0.11f, 0.18f, 0.95f) : new Color(0.06f, 0.08f, 0.14f, 0.95f));
                UiKit.LabelAt(bg, new Vector2(0, 0.5f), new Vector2(225, 0), new Vector2(430, rowH - 8), lines[i].Label, 22, TextAnchor.MiddleLeft, Color.white, bold: false);
                UiKit.LabelAt(bg, new Vector2(1, 0.5f), new Vector2(-80, 0), new Vector2(140, rowH - 8), (lines[i].Points >= 0 ? "+" : "") + lines[i].Points.ToString("N0"), 24, TextAnchor.MiddleRight,
                    lines[i].Points >= 0 ? UiKit.Good : UiKit.Bad);
            }
            if (lines.Count == 0)
            {
                UiKit.LabelAt(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 40), "No breakdown from the server.", 24, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            }
            var total = UiKit.Box(card, "Total", new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(620, 70), new Color(0.95f, 0.76f, 0.31f, 0.18f));
            UiKit.LabelAt(total, new Vector2(0, 0.5f), new Vector2(160, 0), new Vector2(300, 60), "TOTAL", 30, TextAnchor.MiddleLeft, UiKit.Gold);
            UiKit.LabelAt(total, new Vector2(1, 0.5f), new Vector2(-120, 0), new Vector2(220, 60), s.Points.ToString("N0") + " PTS", 32, TextAnchor.MiddleRight, UiKit.Gold);
        }

        // ------------------------------------------------------------- awards

        /// <summary>The card's small print: the server's detail, or the buy it was for ("Name · ₹1.2 Cr (worth ₹6.4 Cr)").</summary>
        private string AwardDetail(RoomAward a)
        {
            var player = a.PlayerId != null ? _data.Players.FirstOrDefault(p => p.Id == a.PlayerId)?.Name : null;
            var buy = player == null ? "" : player + (a.Price is int price ? " · " + Money.Format(price) : "") + (a.Worth is int worth ? $" (worth {Money.Format(worth)})" : "");
            var detail = a.Detail.Trim();
            if (detail.Length == 0 || detail == "…") return buy;
            return buy.Length > 0 && !detail.Contains(player!) ? buy + "\n" + detail : detail;
        }

        private void ShowAwards()
        {
            var awards = _rewards!.Awards;
            if (awards.Count == 0)
            {
                UiKit.LabelAt(_body, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 50), "No awards this time.", 32, TextAnchor.MiddleCenter, UiKit.Dim);
                return;
            }
            // Up to eight cards, four to a row.
            var shown = Mathf.Min(8, awards.Count);
            var perRow = shown <= 4 ? shown : Mathf.CeilToInt(shown / 2f);
            const float w = 400, h = 330, gapX = 420, gapY = 350;
            var rows = Mathf.CeilToInt(Mathf.Min(8, awards.Count) / (float)perRow);
            for (var i = 0; i < awards.Count && i < 8; i++)
            {
                var a = awards[i];
                var row = i / perRow;
                var inRow = Mathf.Min(perRow, awards.Count - row * perRow);
                var x = (i % perRow - (inRow - 1) / 2f) * gapX;
                var y = ((rows - 1) / 2f - row) * gapY;
                var team = _data.Franchises.FirstOrDefault(f => f.Id == a.TeamId);
                var mine = a.TeamId == _me;
                var card = UiKit.EdgedPanel(_body, "Award_" + (a.Id.Length > 0 ? a.Id : i.ToString()), new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(w, h), UiKit.PanelSolid, UiKit.Gold);
                var head = UiKit.Box(card, "Head", new Vector2(0.5f, 1), new Vector2(0, -32), new Vector2(w, 64), UiKit.Gold);
                var title = UiKit.Label(head, a.Title, 28, TextAnchor.MiddleCenter, UiKit.Ink);
                title.resizeTextForBestFit = true;
                title.resizeTextMinSize = 18;
                title.resizeTextMaxSize = 28;
                if (team != null) UiKit.Crest(card, team.Id, new Vector2(0.5f, 1), new Vector2(0, -130), 110);
                var seat = _rewards.For(a.TeamId);
                var who = (team?.ShortName ?? a.TeamId) + (seat != null ? "  ·  " + SeatName(seat) : "");
                UiKit.LabelAt(card, new Vector2(0.5f, 1), new Vector2(0, -214), new Vector2(w - 20, 34), who, 24, TextAnchor.MiddleCenter, mine ? YouMark.Lime : Color.white);
                var detail = UiKit.LabelAt(card, new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(w - 30, 60), AwardDetail(a), 20, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
                detail.horizontalOverflow = HorizontalWrapMode.Wrap;
                if (a.Points != 0) UiKit.LabelAt(card, new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(w - 20, 34), $"+{a.Points:N0} PTS", 26, TextAnchor.MiddleCenter, UiKit.Good);
                if (mine) YouMark.Add(card, "YOU", 5);
            }
        }
    }
}
