using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// After the hammer: squad ratings, the human's squad, and the simulated
    /// season that decides the winner (SPEC: rating + season).
    /// </summary>
    public sealed partial class ResultsScreen : MonoBehaviour
    {
        public event Action? PlayAgainPressed;

        private Dataset _data = null!;
        private AuctionEngine _engine = null!;
        private string _me = "";
        private long _seasonSeed;
        private RectTransform _root = null!, _body = null!;
        private readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();
        private List<SquadRating>? _ratings;
        private SeasonResult? _season;

        /// <summary>
        /// <paramref name="rewards"/>: a room's end-of-auction rewards (podium, standings with points,
        /// award cards, breakdowns; ResultsScreen.Rewards.cs). Null keeps the solo screen as it was.
        /// </summary>
        public static ResultsScreen Create(Dataset data, AuctionEngine engine, string humanTeamId, long seasonSeed, Online.RoomRewards? rewards = null, string? auctionId = null)
        {
            var canvas = UiKit.Canvas("Results", 30);
            var screen = canvas.gameObject.AddComponent<ResultsScreen>();
            screen._data = data;
            screen._engine = engine;
            screen._me = humanTeamId;
            screen._seasonSeed = seasonSeed;
            screen._rewards = rewards;
            screen._auctionId = auctionId ?? "seed:" + seasonSeed;
            screen._root = UiKit.Stretch((RectTransform)canvas.transform, "Dim", new Color(0.02f, 0.03f, 0.07f, 0.82f));
            screen.Build();
            return screen;
        }

        private List<SquadRating> RatingsList => _ratings ??= Ratings.Rate(
            _engine.Teams.Values,
            _data.Rules.MaxOverseasInXI,
            _engine.Format.MinSquad - 11,
            _data.Rules.RatingFor(_engine.Format)).OrderBy(r => r.Rank).ToList();

        private SeasonResult Season
        {
            get
            {
                if (_season != null) return _season;
                var squads = new OrderedMap<string, List<Player>>();
                foreach (var t in _engine.Teams.Values) squads[t.Id] = t.Players;
                return _season = Seasons.Simulate(squads, _seasonSeed, _data.Rules.MaxOverseasInXI);
            }
        }

        private Franchise Team(string id) => _data.Franchises.First(f => f.Id == id);

        private void Build()
        {
            UiKit.LabelAt(_root, new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(1400, 70), "AUCTION COMPLETE", 52, TextAnchor.MiddleCenter, UiKit.Gold);
            // Each rank names what it ranks (rater r1 #3): SQUAD RATING, SEASON SIM; the podium is AUCTION POINTS.
            var tabs = new[] { ("ratings", "SQUAD RATINGS"), ("squad", "MY SQUAD"), ("season", "SEASON SIM ►") };
            // A room's rewards come first: PODIUM, STANDINGS, AWARDS, then the usual three.
            if (_rewards != null) tabs = RewardTabs().Concat(new[] { ("ratings", "SQUAD RATING"), ("squad", "MY SQUAD"), ("season", "SEASON SIM ►") }).ToArray();
            var step = _rewards != null ? 290f : 420f;
            for (var i = 0; i < tabs.Length; i++)
            {
                var (id, label) = tabs[i];
                _tabs[id] = UiKit.Btn(_root, "Tab_" + id, new Vector2(0.5f, 1), new Vector2((i - (tabs.Length - 1) / 2f) * step, -150), new Vector2(step - 20, 76), label, UiKit.Muted, () => Show(id), _rewards != null ? 26 : 30);
            }
            var body = new GameObject("Body", typeof(RectTransform));
            _body = (RectTransform)body.transform;
            _body.SetParent(_root, false);
            _body.anchorMin = _body.anchorMax = new Vector2(0.5f, 0.5f);
            _body.anchoredPosition = new Vector2(0, -50);
            _body.sizeDelta = new Vector2(1700, 720);
            UiKit.Btn(_root, "Again", new Vector2(1, 0), new Vector2(-200, 70), new Vector2(340, 90), "PLAY AGAIN", UiKit.Accent, () => PlayAgainPressed?.Invoke(), 32);
            // The squads just bought, kept for a tournament now or later (match menu → TOURNAMENT).
            Tournament.SaveAuction(_me, _engine.Teams.Values, _auctionId);
            _tournamentButton = UiKit.Btn(_root, "Tournament", new Vector2(0, 0), new Vector2(250, 70), new Vector2(440, 90), "PLAY A TOURNAMENT ►", new Color(0.36f, 0.16f, 0.66f, 0.98f), () =>
            {
                // The squads straight from the auction when the kept file won't do (owner v26 #6: this used to do nothing).
                // This screen's engine is the truth (the kept file could be an older auction's if saving failed).
                TournamentHub.Open(_data, _me, 5, SquadsNow());
            }, 28);
            CareerLine();
            Show(_rewards != null ? "podium" : "ratings");
        }

        private void Show(string tab)
        {
            foreach (var kv in _tabs)
            {
                var on = kv.Key == tab;
                kv.Value.GetComponent<Image>().color = on ? UiKit.Gold : UiKit.Muted;
                kv.Value.GetComponentInChildren<Text>().color = on ? UiKit.Ink : Color.white;
            }
            UiKit.Clear(_body);
            switch (tab)
            {
                case "ratings": Caption("SQUAD RATING RANK: the squads ranked on playing-XI strength alone (not auction points)"); ShowRatings(); break;
                case "squad": ShowSquad(); break;
                case "podium": Caption("AUCTION POINTS RANK: squad rating plus balance, value, steals and more (how: STANDINGS)"); ShowPodium(); break;
                case "standings": Caption("AUCTION POINTS RANK: tap a team to see how its points were earned"); ShowStandings(); break;
                case "awards": ShowAwards(); break;
                default: Caption("SEASON SIM POSITION: a simulated league season played with these squads"); ShowSeason(); break;
            }
            // UI/UX dev (r1 §8): opaque panels, podium margins, standings points bars, award head-shots (ResultsScreen.Style.cs).
            StyleTab(tab);
        }

        /// <summary>What this tab's rank means, in one dim line just above the body (rater r1 #3).</summary>
        private void Caption(string text)
        {
            var t = UiKit.LabelAt(_body, new Vector2(0.5f, 1), new Vector2(0, 24), new Vector2(1600, 30), text, 21, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            t.name = "RankCaption";
        }

        /// <summary>Your three ranks, each named: the podium's headline says them side by side.</summary>
        private (int squadRank, int seasonPos) OtherRanks() =>
            (RatingsList.FirstOrDefault(r => r.TeamId == _me)?.Rank ?? 0, Season.Table.FindIndex(r => r.TeamId == _me) + 1);

        // ------------------------------------------------------------ tables

        private const float RowH = 58;

        /// <summary>One table row; columns are (width, text, alignment).</summary>
        private RectTransform Row(RectTransform parent, float y, float width, Color background, Color ink, int size, params (float w, string text, TextAnchor align)[] cols)
        {
            var row = UiKit.Box(parent, "Row", new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(width, RowH - 6), background);
            var x = -width / 2;
            foreach (var (w, text, align) in cols)
            {
                UiKit.LabelAt(row, new Vector2(0.5f, 0.5f), new Vector2(x + w / 2, 0), new Vector2(w - 16, RowH - 10), text, size, align, ink);
                x += w;
            }
            return row;
        }

        /// <summary>Plain alternating rows: yours is picked out by the YOU outline (<see cref="MarkMine"/>), not a colour.</summary>
        private Color RowColor(string teamId, int i) =>
            i % 2 == 0 ? new Color(0.09f, 0.11f, 0.18f, 0.95f) : new Color(0.06f, 0.08f, 0.14f, 0.95f);

        private void MarkMine(RectTransform row, string teamId)
        {
            if (teamId == _me) YouMark.Add(row, "", 4);
        }

        private void CrestAt(RectTransform parent, string teamId, float x, float y) =>
            UiKit.Crest(parent, teamId, new Vector2(0.5f, 1), new Vector2(x, y), RowH - 10);

        private void ShowRatings()
        {
            const float width = 1600;
            var cols = new (float, string, TextAnchor)[]
            {
                (80, "RANK", TextAnchor.MiddleCenter), (70, "", TextAnchor.MiddleCenter), (480, "FRANCHISE", TextAnchor.MiddleLeft),
                (130, "GRADE", TextAnchor.MiddleCenter), (130, "OVR", TextAnchor.MiddleCenter), (130, "BAT", TextAnchor.MiddleCenter),
                (130, "BOWL", TextAnchor.MiddleCenter), (130, "BAL", TextAnchor.MiddleCenter), (170, "SPENT", TextAnchor.MiddleCenter),
                (150, "SQUAD", TextAnchor.MiddleCenter),
            };
            Row(_body, -RowH / 2, width, new Color(0, 0, 0, 0), UiKit.Dim, 24, cols);
            var list = RatingsList;
            for (var i = 0; i < list.Count; i++)
            {
                var r = list[i];
                var t = _engine.Teams[r.TeamId];
                var y = -RowH / 2 - (i + 1) * RowH;
                var mine = r.TeamId == _me;
                var row = Row(_body, y, width, RowColor(r.TeamId, i), Color.white, 28,
                    (80, r.Rank.ToString(), TextAnchor.MiddleCenter), (70, "", TextAnchor.MiddleCenter),
                    (480, Team(r.TeamId).Name + (mine ? "  · YOU" : ""), TextAnchor.MiddleLeft), (130, r.Grade, TextAnchor.MiddleCenter),
                    (130, r.Overall.ToString(), TextAnchor.MiddleCenter), (130, r.Batting.ToString(), TextAnchor.MiddleCenter),
                    (130, r.Bowling.ToString(), TextAnchor.MiddleCenter), (130, r.Balance.ToString(), TextAnchor.MiddleCenter),
                    (170, Money.Format(t.Spent), TextAnchor.MiddleCenter), (150, t.Size.ToString(), TextAnchor.MiddleCenter));
                MarkMine(row, r.TeamId);
                CrestAt(_body, r.TeamId, -width / 2 + 80 + 35, y);
            }
        }

        private void ShowSquad()
        {
            var team = _engine.Teams[_me];
            var rating = RatingsList.First(r => r.TeamId == _me);
            var xi = new HashSet<string>(rating.Lineup.XI.Select(p => p.Id));
            UiKit.LabelAt(_body, new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(1600, 44),
                $"{Team(_me).Name.ToUpperInvariant()} · GRADE {rating.Grade} · SQUAD RATING RANK #{rating.Rank} OF {RatingsList.Count} · SPENT {Money.Format(team.Spent)} · LEFT {Money.Format(team.Purse)}",
                30, TextAnchor.MiddleCenter, YouMark.Lime);
            UiKit.LabelAt(_body, new Vector2(0.5f, 1), new Vector2(0, -64), new Vector2(1600, 32),
                "XI = in your best playing XI", 22, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);

            var squad = team.Squad.OrderByDescending(p => xi.Contains(p.Player.Id)).ThenByDescending(p => p.Price).ToList();
            var perColumn = (squad.Count + 1) / 2;
            const float colW = 790;
            for (var i = 0; i < squad.Count; i++)
            {
                var p = squad[i];
                var col = i / Math.Max(1, perColumn);
                var row = i % Math.Max(1, perColumn);
                var y = -110 - row * 46f;
                var box = UiKit.Box(_body, "Pick", new Vector2(0.5f, 1), new Vector2(col == 0 ? -colW / 2 - 10 : colW / 2 + 10, y), new Vector2(colW, 42),
                    row % 2 == 0 ? new Color(0.09f, 0.11f, 0.18f, 0.95f) : new Color(0.06f, 0.08f, 0.14f, 0.95f));
                var tag = p.Kind switch
                {
                    PurchaseKind.Retained => " · RETAINED",
                    PurchaseKind.Rtm => " · RTM",
                    PurchaseKind.AutoFill => " · AUTO-FILL",
                    _ => "",
                };
                UiKit.LabelAt(box, new Vector2(0, 0.5f), new Vector2(34, 0), new Vector2(52, 40), xi.Contains(p.Player.Id) ? "XI" : "", 26, TextAnchor.MiddleCenter, UiKit.Gold);
                UiKit.LabelAt(box, new Vector2(0, 0.5f), new Vector2(300, 0), new Vector2(500, 40), $"{p.Player.Name}  <color=#B8BFD1><size=20>{MainMenu.RoleName(p.Player)}{(p.Player.Overseas ? " · OVERSEAS" : "")}{tag}</size></color>", 26, TextAnchor.MiddleLeft)
                    .supportRichText = true;
                UiKit.LabelAt(box, new Vector2(1, 0.5f), new Vector2(-90, 0), new Vector2(160, 40), Money.Format(p.Price), 26, TextAnchor.MiddleRight);
            }
        }

        private void ShowSeason()
        {
            var s = Season;
            const float width = 1000;
            var left = UiKit.Box(_body, "Table", new Vector2(0, 1), new Vector2(width / 2, -360), new Vector2(width, 720), new Color(0, 0, 0, 0));
            var cols = new (float, string, TextAnchor)[]
            {
                (70, "#", TextAnchor.MiddleCenter), (70, "", TextAnchor.MiddleCenter), (380, "TEAM", TextAnchor.MiddleLeft),
                (80, "P", TextAnchor.MiddleCenter), (80, "W", TextAnchor.MiddleCenter), (80, "L", TextAnchor.MiddleCenter),
                (110, "PTS", TextAnchor.MiddleCenter), (130, "NRR", TextAnchor.MiddleCenter),
            };
            Row(left, -RowH / 2, width, new Color(0, 0, 0, 0), UiKit.Dim, 24, cols);
            for (var i = 0; i < s.Table.Count; i++)
            {
                var r = s.Table[i];
                var y = -RowH / 2 - (i + 1) * RowH;
                var mine = r.TeamId == _me;
                var row = Row(left, y, width, RowColor(r.TeamId, i), Color.white, 28,
                    (70, (i + 1).ToString(), TextAnchor.MiddleCenter), (70, "", TextAnchor.MiddleCenter),
                    (380, Team(r.TeamId).ShortName + (i < 4 ? "  ·  Q" : "") + (mine ? "  · YOU" : ""), TextAnchor.MiddleLeft),
                    (80, r.Played.ToString(), TextAnchor.MiddleCenter), (80, r.Won.ToString(), TextAnchor.MiddleCenter),
                    (80, r.Lost.ToString(), TextAnchor.MiddleCenter), (110, r.Points.ToString(), TextAnchor.MiddleCenter),
                    (130, r.NetRunRate.ToString("+0.000;-0.000"), TextAnchor.MiddleCenter));
                MarkMine(row, r.TeamId);
                CrestAt(left, r.TeamId, -width / 2 + 70 + 35, y);
            }

            // Right: champion and awards.
            var right = UiKit.Box(_body, "Awards", new Vector2(1, 1), new Vector2(-330, -360), new Vector2(660, 720), UiKit.Panel);
            var champion = Team(s.ChampionId);
            UiKit.Crest(right, champion.Id, new Vector2(0.5f, 1), new Vector2(0, -130), 200);
            UiKit.LabelAt(right, new Vector2(0.5f, 1), new Vector2(0, -260), new Vector2(620, 50), "CHAMPIONS", 30, TextAnchor.MiddleCenter, UiKit.Gold);
            UiKit.LabelAt(right, new Vector2(0.5f, 1), new Vector2(0, -310), new Vector2(620, 54), champion.Name.ToUpperInvariant(), 40, TextAnchor.MiddleCenter);
            var final = s.Playoffs.LastOrDefault();
            if (final != null)
            {
                UiKit.LabelAt(right, new Vector2(0.5f, 1), new Vector2(0, -356), new Vector2(620, 34),
                    $"beat {Team(s.RunnerUpId).ShortName} {final.Margin} in the final", 24, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            }
            var awards = new[]
            {
                ("ORANGE CAP", s.TopRunScorer, $"{s.TopRunScorer.Runs} runs"),
                ("PURPLE CAP", s.TopWicketTaker, $"{s.TopWicketTaker.Wickets} wickets"),
                ("MVP", s.Mvp, $"{s.Mvp.MvpPoints:0} pts"),
            };
            for (var i = 0; i < awards.Length; i++)
            {
                var (title, who, stat) = awards[i];
                UiKit.LabelAt(right, new Vector2(0.5f, 1), new Vector2(0, -430 - i * 84), new Vector2(620, 32), title, 22, TextAnchor.MiddleCenter, UiKit.Dim);
                UiKit.LabelAt(right, new Vector2(0.5f, 1), new Vector2(0, -464 - i * 84), new Vector2(620, 40),
                    $"{who.Player.Name} ({Team(who.TeamId).ShortName}) · {stat}", 28, TextAnchor.MiddleCenter);
            }
            UiKit.LabelAt(right, new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(620, 40), Verdict(s), 28, TextAnchor.MiddleCenter, UiKit.Gold);
        }

        private string Verdict(SeasonResult s)
        {
            if (s.ChampionId == _me) return "YOUR FRANCHISE WON THE TITLE!";
            if (s.RunnerUpId == _me) return "RUNNERS-UP. So close.";
            var pos = s.Table.FindIndex(r => r.TeamId == _me) + 1;
            return pos <= 4 ? $"SEASON SIM POSITION #{pos}: made the playoffs" : $"SEASON SIM POSITION #{pos} of {s.Table.Count}";
        }
    }
}
