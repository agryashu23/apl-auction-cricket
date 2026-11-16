using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;

namespace Apl.Client.Match
{
    /// <summary>
    /// The numbers behind the result screen: every player's match in one line
    /// (runs, balls, wickets, runs conceded, balls bowled) from both innings, the
    /// top batter, the top bowler and the Player of the Match.
    /// </summary>
    public sealed partial class MatchHud
    {
        /// <summary>One player's match: what he made with the bat and the ball, and for which side.</summary>
        private sealed class Performer
        {
            public Player Player = null!;
            public string TeamId = "";
            public int Runs, Balls, Fours, Sixes, Wickets, Conceded, Bowled;
            public bool NotOut, Batted;

            /// <summary>Runs, plus 25 a wicket, plus a little for a tight spell; the winners get a nudge.</summary>
            public double Impact(string? winnerId)
            {
                var spell = Bowled >= 6 ? (1.5 * Bowled - Conceded) * 0.6 : 0;
                var strike = Balls >= 6 ? (Runs - Balls) * 0.25 : 0;
                var score = Runs + 25 * Wickets + spell + strike;
                return TeamId == winnerId ? score * 1.15 + 5 : score;
            }

            public string BatLine => $"{Runs}{(NotOut ? "*" : "")} ({Balls})";
            public string BowlLine => $"{Wickets}-{Conceded} ({Cricket.FormatOvers(Bowled)})";

            /// <summary>The figures that made his day: both if he did both.</summary>
            public string Figures => Batted && Bowled > 0 ? BatLine + "  ·  " + BowlLine : Bowled > 0 && !Batted ? BowlLine : BatLine;
        }

        /// <summary>Everyone's match from the first innings' card and the second innings (this QuickMatch).</summary>
        private static List<Performer> Performers(QuickMatch m)
        {
            var all = new Dictionary<string, Performer>();
            Performer Of(Player p, string team)
            {
                if (!all.TryGetValue(p.Id, out var x)) all[p.Id] = x = new Performer { Player = p, TeamId = team };
                return x;
            }
            var first = m.FirstInnings;
            var firstBat = first.BattingTeamId;
            var secondBat = m.Batting.TeamId;
            // First innings: a full match keeps its chase-style order; the quick chase has the simulated card.
            if (m.FirstOrder != null)
            {
                foreach (var b in m.FirstOrder) AddBat(Of(b.Player, firstBat), b.Runs, b.Balls, b.Fours, b.Sixes, b.Batted || b.Balls > 0, b.HowOut == null);
            }
            else
            {
                foreach (var b in first.Batting) AddBat(Of(b.Player, firstBat), b.Runs, b.Balls, b.Fours, b.Sixes, b.Batted || b.Balls > 0, true);
            }
            if (m.FirstBowling != null)
            {
                foreach (var f in m.FirstBowling) AddBowl(Of(f.Player, secondBat), f.Wickets, f.Runs, f.Balls);
            }
            else
            {
                foreach (var f in first.Bowling) AddBowl(Of(f.Player, secondBat), f.Wickets, f.Runs, f.Balls);
            }
            foreach (var b in m.Order) AddBat(Of(b.Player, secondBat), b.Runs, b.Balls, b.Fours, b.Sixes, b.Batted || b.Balls > 0, b.HowOut == null);
            foreach (var f in m.BowlingFigures) AddBowl(Of(f.Player, firstBat), f.Wickets, f.Runs, f.Balls);
            return all.Values.ToList();
        }

        private static void AddBat(Performer p, int runs, int balls, int fours, int sixes, bool batted, bool notOut)
        {
            if (!batted) return;
            p.Batted = true;
            p.Runs += runs;
            p.Balls += balls;
            p.Fours += fours;
            p.Sixes += sixes;
            p.NotOut = notOut;
        }

        private static void AddBowl(Performer p, int wickets, int conceded, int balls)
        {
            p.Wickets += wickets;
            p.Conceded += conceded;
            p.Bowled += balls;
        }

        private static Performer? TopBatter(List<Performer> all) =>
            all.Where(p => p.Batted).OrderByDescending(p => p.Runs).ThenBy(p => p.Balls).FirstOrDefault();

        private static Performer? TopBowler(List<Performer> all) =>
            all.Where(p => p.Bowled > 0).OrderByDescending(p => p.Wickets).ThenBy(p => p.Conceded * 6.0 / p.Bowled).FirstOrDefault();

        private static Performer? PlayerOfTheMatch(List<Performer> all, string? winnerId) =>
            all.OrderByDescending(p => p.Impact(winnerId)).FirstOrDefault();
    }
}
