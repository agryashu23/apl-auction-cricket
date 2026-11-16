using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    /// <summary>
    /// A tied full match goes to a Super Over: each side picks three batters (two
    /// wickets end it) and one bowler; the side that batted second bats first, one
    /// over each. Tied again, it goes to the side with more boundaries (fours and
    /// sixes off the bat) over the match and the Super Over.
    /// </summary>
    public sealed class SuperOver
    {
        public const int Batters = 3;

        public QuickMatch Main { get; }

        /// <summary>The one-over-a-side match itself (a full match of one over, its orders and bowlers set).</summary>
        public QuickMatch Match { get; }

        public static bool Needed(QuickMatch main) => main.TwoInnings && !main.IsSuperOver && main.MatchOver && main.Winner == null;

        /// <param name="userBatters">The user's three (best first); <paramref name="userBowler"/> his bowler.</param>
        public SuperOver(QuickMatch main, long seed, IReadOnlyList<Player> userBatters, Player userBowler)
        {
            Main = main;
            var user = main.UserTeam;
            var opp = main.Opponent;
            var orders = new Dictionary<string, List<Player>>
            {
                [user.TeamId] = userBatters.Take(Batters).ToList(),
                [opp.TeamId] = AiBatters(opp),
            };
            var bowlers = new Dictionary<string, Player> { [user.TeamId] = userBowler, [opp.TeamId] = AiBowler(opp) };
            // The side that batted second in the match bats first now.
            Match = QuickMatch.ForSuperOver(user, opp, userBatsFirst: main.Batting == user, seed, main.Venue, orders, bowlers);
        }

        /// <summary>The AI's three: its best batters.</summary>
        public static List<Player> AiBatters(TeamSheet side) =>
            side.XI.OrderByDescending(p => p.Batting + (p.HasTag("finisher") ? 6 : 0)).ThenBy(p => p.Id, StringComparer.Ordinal).Take(Batters).ToList();

        /// <summary>The AI's bowler: its best at the death.</summary>
        public static Player AiBowler(TeamSheet side) =>
            side.XI.Where(p => p.IsBowlingOption).DefaultIfEmpty(side.XI.OrderByDescending(p => p.Bowling).First())
                .OrderByDescending(p => p.Bowling + (p.HasTag("death") ? 8 : 0) + (p.BowlType == BowlType.Pace ? 3 : 0)).ThenBy(p => p.Id, StringComparer.Ordinal).First();

        /// <summary>Fours and sixes off the bat for a side, in the match and the Super Over.</summary>
        public int Boundaries(string teamId)
        {
            var n = 0;
            static int Count(List<BallResult> log) => log.Count(r => (r.Four && !r.ExtrasBoundary) || r.Six);
            foreach (var m in new[] { Main, Match })
            {
                if (m.FirstLog != null && m.FirstInnings.BattingTeamId == teamId) n += Count(m.FirstLog);
                if (m.Batting.TeamId == teamId) n += Count(m.Log);
            }
            return n;
        }

        public bool Over => Match.MatchOver;

        /// <summary>Who won: the Super Over, or on boundaries if that's tied too (null only if the boundaries are level as well).</summary>
        public TeamSheet? Winner
        {
            get
            {
                if (!Over) return null;
                if (Match.Winner != null) return Match.Winner;
                var a = Boundaries(Main.UserTeam.TeamId);
                var b = Boundaries(Main.Opponent.TeamId);
                return a > b ? Main.UserTeam : b > a ? Main.Opponent : null;
            }
        }

        public bool OnBoundaries => Over && Match.Winner == null;

        /// <summary>"BLR won the Super Over" / "MBH won on boundaries (14–11)" (team ids).</summary>
        public string ResultText()
        {
            var w = Winner;
            if (!Over) return "";
            if (w == null) return "Super Over tied, boundaries level: match tied";
            if (!OnBoundaries) return $"{w.TeamId} won the Super Over";
            var other = w == Main.UserTeam ? Main.Opponent : Main.UserTeam;
            return $"{w.TeamId} won on boundaries ({Boundaries(w.TeamId)}–{Boundaries(other.TeamId)})";
        }
    }
}
