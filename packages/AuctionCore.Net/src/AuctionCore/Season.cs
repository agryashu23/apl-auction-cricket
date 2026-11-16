using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core
{
    public sealed class TableRow
    {
        public TableRow(string teamId) { TeamId = teamId; }

        public string TeamId { get; }
        public int Played, Won, Lost, Points, RunsFor, BallsFaced, RunsAgainst, BallsBowled;

        public double NetRunRate => BallsFaced == 0 || BallsBowled == 0
            ? 0
            : (double)(RunsFor * 6) / BallsFaced - (double)(RunsAgainst * 6) / BallsBowled;
    }

    public sealed class PlayerSeasonStats
    {
        public PlayerSeasonStats(Player player, string teamId)
        {
            Player = player;
            TeamId = teamId;
        }

        public Player Player { get; }
        public string TeamId { get; }
        public int Matches, InningsCount, NotOuts, Runs, Balls, Fours, Sixes, Fifties, Hundreds, HighScore;
        public int BallsBowled, RunsConceded, Wickets;
        public double MvpPoints;

        public double StrikeRate => Balls == 0 ? 0 : (double)(Runs * 100) / Balls;
        public double Economy => BallsBowled == 0 ? 0 : (double)(RunsConceded * 6) / BallsBowled;
    }

    public sealed class SeasonResult
    {
        public List<MatchResult> LeagueMatches = new List<MatchResult>();
        public List<MatchResult> Playoffs = new List<MatchResult>();

        /// <summary>Final standings, top first.</summary>
        public List<TableRow> Table = new List<TableRow>();
        public string ChampionId = "", RunnerUpId = "";
        public OrderedMap<string, PlayerSeasonStats> PlayerStats = new OrderedMap<string, PlayerSeasonStats>();

        public IEnumerable<MatchResult> AllMatches => LeagueMatches.Concat(Playoffs);

        public PlayerSeasonStats TopRunScorer => Best((a, b) =>
        {
            var c = b.Runs.CompareTo(a.Runs);
            return c != 0 ? c : b.StrikeRate.CompareTo(a.StrikeRate);
        });

        public PlayerSeasonStats TopWicketTaker => Best((a, b) =>
        {
            var c = b.Wickets.CompareTo(a.Wickets);
            return c != 0 ? c : a.Economy.CompareTo(b.Economy);
        });

        public PlayerSeasonStats Mvp => Best((a, b) => b.MvpPoints.CompareTo(a.MvpPoints));

        private PlayerSeasonStats Best(Comparison<PlayerSeasonStats> compare)
        {
            var list = PlayerStats.Values.ToList();
            list.Sort((a, b) =>
            {
                var c = compare(a, b);
                return c != 0 ? c : DartMath.Compare(a.Player.Id, b.Player.Id);
            });
            return list[0];
        }
    }

    /// <summary>How much the squad decides a simulated season.</summary>
    public enum SeasonModel
    {
        /// <summary>Per-player odds only (the Dart engine's season; golden files).</summary>
        Classic,

        /// <summary>
        /// Plus a team edge from the squad assembled (Seasons.SquadEdges): the better XI usually
        /// finishes higher, upsets still happen. The rater saw a team that bought nobody win 7-2.
        /// </summary>
        Squad,
    }

    public static class Seasons
    {
        /// <summary>Rating points of team edge per point of squad score above the league's mean.</summary>
        public const double EdgePerPoint = 1.2;

        /// <summary>
        /// A squad's season score: its best XI's strength (batting and bowling, as the squad
        /// rating), its balance (a keeper, five bowling options, six real batters, spin and pace
        /// in the attack) and its use of the overseas slots.
        /// </summary>
        public static double SquadScore(IReadOnlyList<Player> squad, int maxOverseasInXI = 4)
        {
            var xi = Lineups.PickBestXI(squad, maxOverseasInXI).XI;
            var score = (Lineups.BattingStrength(xi) + Lineups.BowlingStrength(xi)) / 2;
            if (!xi.Any(p => p.IsKeeper)) score -= 3;
            var options = xi.Count(p => p.IsBowlingOption);
            if (options < 5) score -= 2.5 * (5 - options);
            var batters = xi.Count(p => p.Batting >= 55);
            if (batters < 6) score -= 1.5 * (6 - batters);
            var attack = xi.Where(p => p.IsBowlingOption).OrderByDescending(p => p.Bowling).Take(5).ToList();
            if (!attack.Any(p => p.BowlType == BowlType.Spin)) score -= 1;
            if (attack.Count(p => p.BowlType == BowlType.Pace) < 2) score -= 1;
            // Overseas slots are the league's extra quality: an XI that fills them gets the most of it.
            var overseas = xi.Count(p => p.Overseas);
            score += 0.4 * Math.Min(overseas, maxOverseasInXI) - 0.8;
            return score;
        }

        /// <summary>Each team's edge: EdgePerPoint × (its SquadScore − the league's mean), capped at ±12.</summary>
        public static Dictionary<string, double> SquadEdges(OrderedMap<string, List<Player>> squads, int maxOverseasInXI = 4)
        {
            var scores = squads.Keys.ToDictionary(id => id, id => SquadScore(squads[id], maxOverseasInXI));
            var mean = scores.Values.Average();
            return scores.ToDictionary(kv => kv.Key, kv => DartMath.Clamp(EdgePerPoint * (kv.Value - mean), -12, 12));
        }

        /// <summary>Round-robin then Q1/Eliminator/Q2/Final. Same squads + seed = same season.</summary>
        public static SeasonResult Simulate(OrderedMap<string, List<Player>> squads, long seed, int maxOverseasInXI = 4, SeasonModel model = SeasonModel.Squad)
        {
            if (squads.Count < 4) throw new ArgumentException("A season needs at least 4 teams");
            var rng = new Rng(seed);
            var sim = new MatchSimulator(rng);
            var sheets = new Dictionary<string, TeamSheet>();
            foreach (var kv in squads) sheets[kv.Key] = new TeamSheet(kv.Key, Lineups.PickBestXI(kv.Value, maxOverseasInXI).XI);
            if (model == SeasonModel.Squad)
            {
                foreach (var kv in SquadEdges(squads, maxOverseasInXI)) sheets[kv.Key].Edge = kv.Value;
            }
            var table = new OrderedMap<string, TableRow>();
            foreach (var id in squads.Keys) table[id] = new TableRow(id);
            var stats = new OrderedMap<string, PlayerSeasonStats>();
            var matchNo = 0;

            MatchResult PlayOne(string a, string b, MatchStage stage)
            {
                var result = sim.Play(sheets[a], sheets[b], ++matchNo, stage);
                AddPlayerStats(result, sheets, stats);
                return result;
            }

            var fixtures = RoundRobin(squads.Keys.ToList());
            rng.Shuffle(fixtures);
            var season = new SeasonResult();
            foreach (var (a, b) in fixtures)
            {
                var result = PlayOne(a, b, MatchStage.League);
                season.LeagueMatches.Add(result);
                AddToTable(result, table);
            }

            var standings = table.Values.ToList();
            standings.Sort((x, y) =>
            {
                var c = y.Points.CompareTo(x.Points);
                if (c != 0) return c;
                c = y.NetRunRate.CompareTo(x.NetRunRate);
                if (c != 0) return c;
                c = y.Won.CompareTo(x.Won);
                return c != 0 ? c : DartMath.Compare(x.TeamId, y.TeamId);
            });

            var ids = standings.Take(4).Select(r => r.TeamId).ToList();
            var q1 = PlayOne(ids[0], ids[1], MatchStage.Qualifier1);
            var elim = PlayOne(ids[2], ids[3], MatchStage.Eliminator);
            var q2 = PlayOne(q1.LoserId, elim.WinnerId, MatchStage.Qualifier2);
            var final = PlayOne(q1.WinnerId, q2.WinnerId, MatchStage.FinalMatch);

            season.Playoffs.AddRange(new[] { q1, elim, q2, final });
            season.Table = standings;
            season.ChampionId = final.WinnerId;
            season.RunnerUpId = final.LoserId;
            season.PlayerStats = stats;
            return season;
        }

        private static List<(string, string)> RoundRobin(List<string> teams)
        {
            var list = teams.ToList();
            if (list.Count % 2 == 1) list.Add("");
            var n = list.Count;
            var pairs = new List<(string, string)>();
            for (var round = 0; round < n - 1; round++)
            {
                for (var i = 0; i < n / 2; i++)
                {
                    var a = list[i];
                    var b = list[n - 1 - i];
                    if (a.Length > 0 && b.Length > 0) pairs.Add(round % 2 == 0 ? (a, b) : (b, a));
                }
                var last = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                list.Insert(1, last);
            }
            return pairs;
        }

        private static void AddToTable(MatchResult m, OrderedMap<string, TableRow> table)
        {
            foreach (var inn in new[] { m.First, m.Second })
            {
                var balls = inn.AllOut ? 120 : inn.LegalBalls;
                var bat = table[inn.BattingTeamId];
                var bowl = table[inn.BowlingTeamId];
                bat.RunsFor += inn.Runs;
                bat.BallsFaced += balls;
                bowl.RunsAgainst += inn.Runs;
                bowl.BallsBowled += balls;
            }
            var winner = table[m.WinnerId];
            var loser = table[m.LoserId];
            winner.Played++;
            winner.Won++;
            winner.Points += 2;
            loser.Played++;
            loser.Lost++;
        }

        private static void AddPlayerStats(MatchResult m, Dictionary<string, TeamSheet> sheets, OrderedMap<string, PlayerSeasonStats> stats)
        {
            PlayerSeasonStats Of(Player p, string teamId)
            {
                if (!stats.TryGetValue(p.Id, out var s)) stats[p.Id] = s = new PlayerSeasonStats(p, teamId);
                return s;
            }

            foreach (var teamId in new[] { m.First.BattingTeamId, m.Second.BattingTeamId })
            {
                foreach (var p in sheets[teamId].XI) Of(p, teamId).Matches++;
            }
            foreach (var inn in new[] { m.First, m.Second })
            {
                foreach (var b in inn.Batting.Where(b => b.Batted))
                {
                    var s = Of(b.Player, inn.BattingTeamId);
                    s.InningsCount++;
                    s.Runs += b.Runs;
                    s.Balls += b.Balls;
                    s.Fours += b.Fours;
                    s.Sixes += b.Sixes;
                    s.MvpPoints += Cricket.BattingPoints(b);
                    if (!b.Out) s.NotOuts++;
                    if (b.Runs >= 100) s.Hundreds++;
                    else if (b.Runs >= 50) s.Fifties++;
                    if (b.Runs > s.HighScore) s.HighScore = b.Runs;
                }
                foreach (var w in inn.Bowling)
                {
                    var s = Of(w.Player, inn.BowlingTeamId);
                    s.BallsBowled += w.Balls;
                    s.RunsConceded += w.Runs;
                    s.Wickets += w.Wickets;
                    s.MvpPoints += Cricket.BowlingPoints(w);
                }
            }
        }
    }
}
