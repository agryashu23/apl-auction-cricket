using System.Collections.Generic;
using System.Linq;
using Apl.Client.Match;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The hub's sims (round auction-r1 #4) and the season's end. SIM MATCH: the matches before
    /// yours, then yours, at once, and its result card. SIM TO MY NEXT MATCH: only the others,
    /// back to the table with yours up next. SIM REST OF SEASON (asks first): every match left,
    /// yours too, through the playoffs to the champions page. Every sim plays the tournament
    /// squads (the auction's, when it was on the auction): your side the XI you last confirmed
    /// on the PLAYING XI screen when it still fits, else the auto-pick. Log: [APL tourney] sim match …
    /// </summary>
    public sealed partial class TournamentHub
    {
        /// <summary>Your XI for a simulated match: the one last confirmed for this squad, or null (the auto-pick).</summary>
        private TeamSheet? UserSheet(Tournament t)
        {
            var ids = t.Squads.TryGetValue(t.UserTeam, out var s) ? s.ToHashSet() : new HashSet<string>();
            var squad = _data.Players.Where(p => ids.Contains(p.Id)).ToList();
            return squad.Count >= 11 ? XiChoice.SheetFor(_data, t.UserTeam, squad) : null;
        }

        /// <summary>SIM MATCH: the others first, then yours; the result card over the table.</summary>
        private void SimMine(Tournament t)
        {
            var before = t.Fixtures.Count(f => f.Played);
            t.SimulateUntilUser(_data);
            var f = t.NextForUser;
            if (f == null)
            {
                t.Save();
                ShowTable();
                return;
            }
            var others = t.Fixtures.Count(x => x.Played) - before;
            t.Simulate(_data, f, UserSheet(t));
            t.Save();
            Debug.Log($"[APL tourney] SIM MATCH {f.Number}: {Team(f.Winner).ShortName} won {f.Margin} ({others} other matches simulated first)");
            ShowTable();
            ResultCard(t, f, others);
        }

        /// <summary>SIM TO MY NEXT MATCH: the others only; yours is next on the table.</summary>
        private void SimToMine(Tournament t)
        {
            var before = t.Fixtures.Count(f => f.Played);
            t.SimulateUntilUser(_data);
            t.Save();
            Debug.Log($"[APL tourney] SIM TO MY NEXT MATCH: {t.Fixtures.Count(f => f.Played) - before} simulated, next for {t.UserTeam}: {t.NextForUser?.Number.ToString() ?? "none"}");
            if (t.Finished) ShowChampions();
            else ShowTable();
        }

        /// <summary>Knocked out: the next match only, with its card.</summary>
        private void SimOne(Tournament t)
        {
            var f = t.Next;
            if (f == null)
            {
                ShowTable();
                return;
            }
            t.Simulate(_data, f);
            t.Save();
            ShowTable();
            ResultCard(t, f, 0);
        }

        /// <summary>SIM REST OF SEASON: every match left, yours too, to the champions page (asks first while you're still in).</summary>
        private void SimRest(Tournament t, bool confirm)
        {
            void Run()
            {
                var before = t.Fixtures.Count(f => f.Played);
                t.SimulateToEnd(_data, UserSheet(t));
                t.Save();
                Debug.Log($"[APL tourney] SIM REST OF SEASON: {t.Fixtures.Count(f => f.Played) - before} simulated, {t.Fixtures.Count(f => f.Played)} of {t.Fixtures.Count} played, champion {t.Champion}, {t.UserTeam} {t.UserFinish()}");
                if (t.Finished) ShowChampions();
                else ShowTable();
            }
            if (!confirm)
            {
                Run();
                return;
            }
            var left = t.Fixtures.Count(f => !f.Played);
            var mine = t.Fixtures.Count(f => !f.Played && f.Involves(t.UserTeam));
            ConfirmDialog.Show("SIM REST OF SEASON?",
                $"The {left} {(left == 1 ? "match" : "matches")} left ({mine} of yours) and the playoffs still to be drawn are simulated on these squads straight to the final. You won't play any of them.",
                "SIM ALL", "CANCEL", Run);
        }

        // ---------------------------------------------------------- the result card

        /// <summary>A simulated match's result over the table: both scores, who won, the stars, where it leaves you.</summary>
        private void ResultCard(Tournament t, Tournament.Fixture f, int others)
        {
            var dim = PanelKit.Backdrop(_page, 0.8f);
            dim.name = "SimCard";
            dim.gameObject.AddComponent<Button>(); // swallow taps on the table behind
            var card = PanelKit.GlassPanel(dim, "Card", Centre, new Vector2(0, 20), new Vector2(1240, 780));
            var mineMatch = f.Involves(t.UserTeam);
            var won = f.Winner == t.UserTeam;
            var stage = f.League ? $"MATCH {f.Number} OF {t.LeagueCount}" : f.Stage.ToUpperInvariant();
            UiKit.LabelAt(card, new Vector2(0.5f, 1), new Vector2(0, -50), new Vector2(1100, 40), $"SIMULATED · {stage} · {Venue.Find(f.Venue)?.Name ?? ""}", 26, TextAnchor.MiddleCenter, UiKit.Dim);
            var headline = !mineMatch ? $"{Team(f.Winner).Name.ToUpperInvariant()} WIN" : won ? "YOU WON!" : "YOU LOST";
            UiKit.LabelAt(card, new Vector2(0.5f, 1), new Vector2(0, -116), new Vector2(1100, 80), headline, 64, TextAnchor.MiddleCenter, !mineMatch ? UiKit.Gold : won ? YouMark.Lime : UiKit.Bad);

            // The two innings: crest, side, score (overs).
            var innings = new[] { f.First!, f.Second! };
            for (var i = 0; i < 2; i++)
            {
                var s = innings[i];
                var y = 150 - i * 115;
                var winner = s.Team == f.Winner;
                var row = UiKit.Box(card, "Inn" + i, Centre, new Vector2(0, y), new Vector2(1000, 100), winner ? new Color(0.14f, 0.2f, 0.12f, 0.95f) : new Color(0.08f, 0.1f, 0.17f, 0.95f));
                UiKit.Crest(row, s.Team, new Vector2(0, 0.5f), new Vector2(70, 0), 80);
                UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(380, 0), new Vector2(480, 60), Team(s.Team).Name.ToUpperInvariant() + (s.Team == t.UserTeam ? "  · YOU" : ""), 32, TextAnchor.MiddleLeft, s.Team == t.UserTeam ? YouMark.Lime : Color.white);
                UiKit.LabelAt(row, new Vector2(1, 0.5f), new Vector2(-180, 0), new Vector2(320, 70), $"{s}  <size=24>({Cricket.FormatOvers(s.Balls)} ov)</size>", 46, TextAnchor.MiddleRight, UiKit.Gold).supportRichText = true;
            }
            UiKit.LabelAt(card, Centre, new Vector2(0, -60), new Vector2(1100, 44), $"{Team(f.Winner).ShortName} won {f.Margin}", 32, TextAnchor.MiddleCenter, Color.white);

            // The stars, and where it leaves you.
            var stars = new List<string>();
            if (t.LastTopBat.Length > 0) stars.Add("TOP BAT  " + t.LastTopBat);
            if (t.LastTopBowl.Length > 0) stars.Add("TOP BOWL  " + t.LastTopBowl);
            UiKit.LabelAt(card, Centre, new Vector2(0, -120), new Vector2(1100, 36), string.Join("      ", stars), 26, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            var table = t.Table();
            var rank = table.FindIndex(r => r.Team == t.UserTeam) + 1;
            var me = rank > 0 ? table[rank - 1] : null;
            var standing = t.Finished ? $"{Team(t.Champion).Name.ToUpperInvariant()} ARE THE CHAMPIONS"
                : me != null && !t.LeagueDone ? $"YOU'RE {Tournament.Ordinal(rank)} · {me.Points} PTS · {me.Won}-{me.Lost}"
                : t.UserStillIn ? "YOU'RE STILL IN THE PLAYOFFS" : $"YOUR SIDE: {t.UserFinish()}";
            if (others > 0) standing += $"   ·   {others} other {(others == 1 ? "match" : "matches")} simulated first";
            UiKit.LabelAt(card, Centre, new Vector2(0, -180), new Vector2(1100, 36), standing, 26, TextAnchor.MiddleCenter, UiKit.Gold);

            UiKit.Btn(card, "SimCardOk", new Vector2(0.5f, 0), new Vector2(0, 80), new Vector2(460, 100), t.Finished ? "SEE THE CHAMPIONS ►" : "CONTINUE ►", UiKit.Accent, () =>
            {
                if (t.Finished) ShowChampions();
                else Destroy(dim.gameObject);
            }, 34);
        }

        // ------------------------------------------------------------- champions

        /// <summary>
        /// The season's end: the champions (crest, the final), how your side finished and its league
        /// record, the tournament's top run-scorer and wicket-taker; VIEW TABLE / NEW TOURNAMENT.
        /// </summary>
        private void ShowChampions()
        {
            var t = _t!;
            if (!t.Finished)
            {
                ShowTable();
                return;
            }
            if (!t.ChampionShown)
            {
                t.ChampionShown = true;
                t.Save();
            }
            var page = NewPage();
            var champ = Team(t.Champion);
            var won = t.Champion == t.UserTeam;
            var colour = UiKit.ParseHex(champ.PrimaryColor);
            // The champion's colours behind the crest.
            var glow = UiKit.Box(page, "ChampGlow", new Vector2(0.5f, 1), new Vector2(0, -330), new Vector2(1920, 420), UiKit.WithAlpha(colour, 0.28f));
            glow.SetAsFirstSibling();
            UiKit.Btn(page, "HubBack", new Vector2(0, 1), new Vector2(150, -70), new Vector2(200, 80), "◄ BACK", UiKit.Muted, Close, 30);
            var source = t.Source == "auction" ? "AUCTION SQUADS" : "LAST SEASON'S SQUADS";
            UiKit.LabelAt(page, new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(1400, 34), $"TOURNAMENT OVER · {source} · {t.Overs} OVERS · {t.Fixtures.Count(f => f.Played)} MATCHES", 24, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            UiKit.Crest(page, champ.Id, new Vector2(0.5f, 1), new Vector2(0, -238), 220);
            UiKit.LabelAt(page, new Vector2(0.5f, 1), new Vector2(0, -400), new Vector2(1600, 80), won ? "CHAMPIONS! YOUR SIDE WON IT" : "CHAMPIONS", 68, TextAnchor.MiddleCenter, won ? YouMark.Lime : UiKit.Gold);
            UiKit.LabelAt(page, new Vector2(0.5f, 1), new Vector2(0, -466), new Vector2(1600, 56), champ.Name.ToUpperInvariant(), 46, TextAnchor.MiddleCenter, Color.white);
            var final = t.Fixtures.FirstOrDefault(f => f.Stage == "Final" && f.Played);
            if (final != null)
                UiKit.LabelAt(page, new Vector2(0.5f, 1), new Vector2(0, -518), new Vector2(1600, 36),
                    $"FINAL · {Team(final.First!.Team).ShortName} {final.First}  v  {Team(final.Second!.Team).ShortName} {final.Second} · {Team(final.Winner).ShortName} won {final.Margin}", 26, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);

            // Two cards: your season | the tournament's best.
            var mine = PanelKit.GlassPanel(page, "YourSeason", Centre, new Vector2(-420, -230), new Vector2(760, 300));
            UiKit.LabelAt(mine, new Vector2(0.5f, 1), new Vector2(0, -36), new Vector2(700, 36), "YOUR SEASON", 26, TextAnchor.MiddleCenter, UiKit.Dim);
            UiKit.Crest(mine, t.UserTeam, new Vector2(0, 0.5f), new Vector2(110, -20), 130);
            UiKit.LabelAt(mine, new Vector2(0, 0.5f), new Vector2(450, 30), new Vector2(520, 56), t.UserFinish(), 40, TextAnchor.MiddleLeft, won ? YouMark.Lime : UiKit.Gold);
            var table = t.Table();
            var rank = table.FindIndex(r => r.Team == t.UserTeam) + 1;
            var row = rank > 0 ? table[rank - 1] : null;
            if (row != null)
                UiKit.LabelAt(mine, new Vector2(0, 0.5f), new Vector2(450, -30), new Vector2(520, 40),
                    $"League: {Tournament.Ordinal(rank).ToLowerInvariant()} · {row.Won} won, {row.Lost} lost · {row.Points} pts", 24, TextAnchor.MiddleLeft, Color.white, bold: false);
            var playoff = t.Fixtures.Where(f => !f.League && f.Played && f.Involves(t.UserTeam)).Select(f => (f.Winner == t.UserTeam ? "won " : "lost ") + f.Stage).ToList();
            UiKit.LabelAt(mine, new Vector2(0, 0.5f), new Vector2(450, -76), new Vector2(520, 36),
                playoff.Count > 0 ? "Playoffs: " + string.Join(", ", playoff) : "Playoffs: didn't make the top four", 22, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);

            var best = PanelKit.GlassPanel(page, "TopPlayers", Centre, new Vector2(420, -230), new Vector2(760, 300));
            UiKit.LabelAt(best, new Vector2(0.5f, 1), new Vector2(0, -36), new Vector2(700, 36), "TOURNAMENT'S BEST", 26, TextAnchor.MiddleCenter, UiKit.Dim);
            string Who(string id, Tournament.Line l) => (_data.Players.FirstOrDefault(p => p.Id == id)?.Name ?? id).ToUpperInvariant() + $"  ({Team(l.Team).ShortName})";
            var runs = t.Stats.OrderByDescending(kv => kv.Value.Runs).ThenByDescending(kv => kv.Value.StrikeRate).FirstOrDefault();
            var wkts = t.Stats.OrderByDescending(kv => kv.Value.Wickets).ThenBy(kv => kv.Value.Economy).FirstOrDefault();
            if (runs.Value != null)
            {
                UiKit.LabelAt(best, new Vector2(0, 0.5f), new Vector2(390, 44), new Vector2(700, 34), "MOST RUNS", 22, TextAnchor.MiddleLeft, UiKit.Gold);
                UiKit.LabelAt(best, new Vector2(0, 0.5f), new Vector2(390, 8), new Vector2(700, 38), $"{Who(runs.Key, runs.Value)} · {runs.Value.Runs} runs, SR {runs.Value.StrikeRate:0}", 26, TextAnchor.MiddleLeft, Color.white, bold: false);
            }
            if (wkts.Value != null)
            {
                UiKit.LabelAt(best, new Vector2(0, 0.5f), new Vector2(390, -50), new Vector2(700, 34), "MOST WICKETS", 22, TextAnchor.MiddleLeft, UiKit.Gold);
                UiKit.LabelAt(best, new Vector2(0, 0.5f), new Vector2(390, -86), new Vector2(700, 38), $"{Who(wkts.Key, wkts.Value)} · {wkts.Value.Wickets} wkts, econ {wkts.Value.Economy:0.0}", 26, TextAnchor.MiddleLeft, Color.white, bold: false);
            }

            UiKit.Btn(page, "ChampTable", new Vector2(0.5f, 0), new Vector2(-260, 80), new Vector2(460, 104), "VIEW TABLE", UiKit.Muted, ShowTable, 32);
            UiKit.Btn(page, "Again", new Vector2(0.5f, 0), new Vector2(260, 80), new Vector2(460, 104), "NEW TOURNAMENT ►", UiKit.Accent, ShowSetup, 32);
            Debug.Log($"[APL tourney] champions page: {t.Champion} ({t.Source} squads), {t.UserTeam} {t.UserFinish()}, {t.Fixtures.Count(f => f.Played)} of {t.Fixtures.Count} played");
        }
    }
}
