using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    /// <summary>
    /// The user's playing XI before a match (owner v26 #7): the auto-picked XI
    /// (<see cref="Lineups.PickBestXI"/>) to start from, swaps with the bench, and
    /// the same rules the auto-pick keeps — the overseas cap, a keeper when the
    /// squad has one, enough bowling options for the overs.
    /// </summary>
    public static class PlayingXi
    {
        public const int Size = 11;

        /// <summary>The most overseas players an XI from <paramref name="squad"/> may have (more only when there aren't eleven locals).</summary>
        public static int OverseasCap(IReadOnlyList<Player> squad, int maxOverseas) =>
            Math.Max(maxOverseas, Size - squad.Count(p => !p.Overseas));

        /// <summary>The bowling options an XI needs: five, or all the squad has when it has fewer.</summary>
        public static int BowlingOptionsNeeded(IReadOnlyList<Player> squad) => Math.Min(5, squad.Count(p => p.IsBowlingOption));

        /// <summary>The auto-picked XI and the bench (best first) — the screen's starting point.</summary>
        public static Lineup Auto(IReadOnlyList<Player> squad, int maxOverseas) => Lineups.PickBestXI(squad, maxOverseas);

        /// <summary>
        /// Why <paramref name="xi"/> can't play (null: it can): the first broken rule, in words for the screen.
        /// A squad of eleven or fewer plays whoever it has.
        /// </summary>
        public static string? Problem(IReadOnlyList<Player> xi, IReadOnlyList<Player> squad, int maxOverseas)
        {
            if (squad.Count <= Size) return null;
            if (xi.Count != Size || xi.Select(p => p.Id).Distinct().Count() != Size) return "PICK ELEVEN PLAYERS";
            if (xi.Any(p => squad.All(s => s.Id != p.Id))) return "THAT PLAYER IS NOT IN THE SQUAD";
            var cap = OverseasCap(squad, maxOverseas);
            if (xi.Count(p => p.Overseas) > cap) return $"ONLY {cap} OVERSEAS PLAYERS IN AN XI";
            if (squad.Any(p => p.IsKeeper) && !xi.Any(p => p.IsKeeper)) return "THE XI NEEDS A WICKETKEEPER";
            var need = BowlingOptionsNeeded(squad);
            if (xi.Count(p => p.IsBowlingOption) < need) return $"THE XI NEEDS {need} BOWLING OPTIONS";
            return null;
        }

        /// <summary>
        /// <paramref name="xi"/> with <paramref name="outgoing"/> replaced by <paramref name="incoming"/> in the
        /// same place, or null and the reason when the swap would break a rule.
        /// </summary>
        public static List<Player>? Swap(IReadOnlyList<Player> xi, Player outgoing, Player incoming, IReadOnlyList<Player> squad, int maxOverseas, out string? problem)
        {
            problem = null;
            var at = -1;
            for (var i = 0; i < xi.Count; i++)
            {
                if (xi[i].Id == outgoing.Id) at = i;
            }
            if (at < 0)
            {
                problem = "THAT PLAYER IS NOT IN THE XI";
                return null;
            }
            if (xi.Any(p => p.Id == incoming.Id))
            {
                problem = "ALREADY IN THE XI";
                return null;
            }
            var trial = xi.ToList();
            trial[at] = incoming;
            problem = Problem(trial, squad, maxOverseas);
            return problem == null ? trial : null;
        }

        /// <summary>
        /// The chosen XI as a team sheet (players from <paramref name="squad"/> by <paramref name="ids"/>), or
        /// null when it no longer fits the squad or the rules (the auto-pick then plays).
        /// </summary>
        public static TeamSheet? Resolve(string teamId, IReadOnlyList<Player> squad, IReadOnlyList<string>? ids, int maxOverseas)
        {
            if (ids == null || ids.Count != Size || squad.Count < Size) return null;
            var byId = new Dictionary<string, Player>();
            foreach (var p in squad) byId[p.Id] = p;
            var xi = new List<Player>();
            foreach (var id in ids)
            {
                if (!byId.TryGetValue(id, out var p)) return null;
                xi.Add(p);
            }
            return Problem(xi, squad, maxOverseas) == null ? new TeamSheet(teamId, xi) : null;
        }

        /// <summary>"BAT" / "BOWL" / "AR" / "WK".</summary>
        public static string RoleTag(Player p) => p.Role switch
        {
            Role.Batter => "BAT",
            Role.Bowler => "BOWL",
            Role.AllRounder => "AR",
            _ => "WK",
        };
    }

    /// <summary>
    /// The tournament table's qualification mark (owner v26 #9, decision (a)): "Q" only for a side in
    /// the TOP FOUR of the table (points, then net run rate) that has also won MORE than half of its
    /// league matches (14 → 8 wins, 9 → 5). Everyone else is just ranked, without a Q.
    /// </summary>
    public static class LeagueTable
    {
        /// <summary>Wins that clinch the Q: more than half of <paramref name="leagueMatches"/>.</summary>
        public static int WinsToQualify(int leagueMatches) => leagueMatches / 2 + 1;

        /// <summary>The playoff places: the top four play off.</summary>
        public const int PlayoffPlaces = 4;

        /// <summary>Q: <paramref name="rank"/> (1 = top of the table) in the top four AND more than half the league won.</summary>
        public static bool Qualified(int rank, int won, int leagueMatches) =>
            rank >= 1 && rank <= PlayoffPlaces && leagueMatches > 0 && won >= WinsToQualify(leagueMatches);
    }
}
