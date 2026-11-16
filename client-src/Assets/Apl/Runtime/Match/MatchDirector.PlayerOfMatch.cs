using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;

namespace Apl.Client.Match
{
    /// <summary>
    /// Player of the match: everyone's whole match scored the way a panel would
    /// lean — runs (and how quickly they came), wickets (and how cheaply), the
    /// winners ahead on a tie — and his card's numbers.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private sealed class MatchLine
        {
            public Player Player = null!;
            public int Runs, Balls, Fours, Sixes, Wickets, Conceded, Bowled;
            public bool Batted;

            public double Score =>
                Runs + (Balls >= 8 ? (Runs * 100.0 / Balls - 120) * 0.12 : 0) + Sixes * 1.5
                + Wickets * 22 + (Bowled >= 6 ? (7.5 - Conceded * 6.0 / Bowled) * 3 : 0);
        }

        /// <summary>Both innings' batting and bowling, by player.</summary>
        private Dictionary<string, MatchLine> MatchLines()
        {
            var lines = new Dictionary<string, MatchLine>();
            MatchLine Of(Player p)
            {
                if (!lines.TryGetValue(p.Id, out var l)) lines[p.Id] = l = new MatchLine { Player = p };
                return l;
            }
            void Bat(Player p, int runs, int balls, int fours, int sixes, bool batted)
            {
                var l = Of(p);
                l.Runs += runs;
                l.Balls += balls;
                l.Fours += fours;
                l.Sixes += sixes;
                l.Batted |= batted;
            }
            void Bowl(Player p, int balls, int runs, int wickets)
            {
                var l = Of(p);
                l.Bowled += balls;
                l.Conceded += runs;
                l.Wickets += wickets;
            }
            foreach (var b in _match.Order) Bat(b.Player, b.Runs, b.Balls, b.Fours, b.Sixes, b.Batted);
            foreach (var f in _match.BowlingFigures) Bowl(f.Player, f.Balls, f.Runs, f.Wickets);
            // The first innings: played ball by ball (a full match), or simulated (a chase).
            if (_match.FirstOrder != null)
            {
                foreach (var b in _match.FirstOrder) Bat(b.Player, b.Runs, b.Balls, b.Fours, b.Sixes, b.Batted);
                foreach (var f in _match.FirstBowling ?? new List<ChaseBowler>()) Bowl(f.Player, f.Balls, f.Runs, f.Wickets);
            }
            else
            {
                foreach (var b in _match.FirstInnings.Batting) Bat(b.Player, b.Runs, b.Balls, b.Fours, b.Sixes, b.Batted);
                foreach (var f in _match.FirstInnings.Bowling) Bowl(f.Player, f.Balls, f.Runs, f.Wickets);
            }
            return lines;
        }

        /// <summary>The pick, and his card (side, numbers that earned it).</summary>
        private PlayerCardInfo? PlayerOfTheMatch()
        {
            var lines = MatchLines().Values.ToList();
            if (lines.Count == 0) return null;
            var winners = _match.Winner?.TeamId;
            bool OnWinners(Player p) => winners != null && TeamOf(p) == winners;
            var best = lines.OrderByDescending(l => l.Score + (OnWinners(l.Player) ? 8 : 0)).First();
            var side = Side(TeamOf(best.Player), _bat);
            var info = new PlayerCardInfo { Player = best.Player, Role = "PLAYER OF THE MATCH", Team = side.ShortName, Tint = UiKit.ParseHex(side.PrimaryColor) };
            if (best.Batted && (best.Runs > 0 || best.Wickets == 0))
            {
                info.Stats.Add(new CardStat("RUNS", best.Runs.ToString()));
                info.Stats.Add(new CardStat("BALLS", best.Balls.ToString()));
                info.Stats.Add(new CardStat("4s / 6s", $"{best.Fours} / {best.Sixes}"));
                info.Stats.Add(new CardStat("S/R", best.Balls > 0 ? (best.Runs * 100.0 / best.Balls).ToString("0.0", CultureInfo.InvariantCulture) : "-"));
            }
            if (best.Bowled > 0)
            {
                info.Stats.Add(new CardStat("BOWLING", $"{best.Wickets}-{best.Conceded}"));
                info.Stats.Add(new CardStat("OVERS", Cricket.FormatOvers(best.Bowled)));
            }
            return info;
        }

        /// <summary>Which side a player is on (the two XIs).</summary>
        private string TeamOf(Player p) => _match.Batting.XI.Any(x => x.Id == p.Id) ? _match.Batting.TeamId : _match.Bowling.TeamId;
    }
}
