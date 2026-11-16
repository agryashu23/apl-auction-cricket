using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core
{
    public enum MatchStage { League, Qualifier1, Eliminator, Qualifier2, FinalMatch }

    public enum Pitch { Balanced, Flat, SpinFriendly, SeamFriendly }

    internal enum Phase { Powerplay, Middle, Death }

    public sealed class BatterLine
    {
        public BatterLine(Player player) { Player = player; }

        public Player Player { get; }
        public int Runs, Balls, Fours, Sixes;
        public bool Batted;

        /// <summary>e.g. "c Rao b Khan"; null while not out.</summary>
        public string? Dismissal;
        public bool Out => Dismissal != null;
    }

    public sealed class BowlerLine
    {
        public BowlerLine(Player player) { Player = player; }

        public Player Player { get; }
        public int Balls, Runs, Wickets, Dots, Wides, NoBalls;
        public string Overs => Cricket.FormatOvers(Balls);
        public double Economy => Balls == 0 ? 0 : (double)(Runs * 6) / Balls;
    }

    public sealed class Innings
    {
        public Innings(string battingTeamId, string bowlingTeamId, List<BatterLine> batting)
        {
            BattingTeamId = battingTeamId;
            BowlingTeamId = bowlingTeamId;
            Batting = batting;
        }

        public string BattingTeamId { get; }
        public string BowlingTeamId { get; }
        public List<BatterLine> Batting { get; }
        public List<BowlerLine> Bowling { get; } = new List<BowlerLine>();
        public int Runs, Wickets, LegalBalls, Extras;
        public bool AllOut => Wickets >= Batting.Count - 1;
        public string Score => AllOut ? $"{Runs}" : $"{Runs}/{Wickets}";
        public string Overs => Cricket.FormatOvers(LegalBalls);
    }

    public sealed class MatchResult
    {
        public int Number;
        public MatchStage Stage;
        public Pitch Pitch;
        public string TossWinnerId = "";
        public Innings First = null!, Second = null!;
        public string WinnerId = "", LoserId = "";

        /// <summary>"by 23 runs", "by 6 wickets (8 balls left)", "in the Super Over".</summary>
        public string Margin = "";
        public bool SuperOver;
        public Player PlayerOfMatch = null!;
    }

    public sealed class TeamSheet
    {
        public TeamSheet(string teamId, List<Player> xi)
        {
            TeamId = teamId;
            XI = xi;
        }

        public string TeamId { get; }
        public List<Player> XI { get; }

        /// <summary>
        /// Team-level edge in rating points, added to every ball this side bats (to the batter) and
        /// bowls (to the bowler). 0 = the classic per-player odds (Dart parity, match mode).
        /// The season sim sets it from the squad's strength (Seasons.SquadEdges).
        /// </summary>
        public double Edge { get; set; }
    }

    public static class Cricket
    {
        public static string FormatOvers(int balls) => balls % 6 == 0 ? $"{balls / 6}" : $"{balls / 6}.{balls % 6}";

        /// <summary>Openers first (tagged, else best batters), then by batting skill.</summary>
        public static List<Player> BattingOrder(IReadOnlyList<Player> xi)
        {
            var sorted = xi.ToList();
            sorted.Sort((a, b) => Lineups.Desc(a.Batting, b.Batting, a, b));
            var openers = sorted.Where(p => p.HasTag("opener")).Take(2).ToList();
            foreach (var p in sorted)
            {
                if (openers.Count >= 2) break;
                if (!openers.Contains(p)) openers.Add(p);
            }
            return openers.Concat(sorted.Where(p => !openers.Contains(p))).ToList();
        }

        internal static Phase PhaseOf(int over) => over < 6 ? Phase.Powerplay : over < 15 ? Phase.Middle : Phase.Death;

        internal static string PhaseName(Phase p) => p switch
        {
            Phase.Powerplay => "powerplay",
            Phase.Middle => "middle",
            _ => "death",
        };

        internal static int PhaseBonus(Player p, Phase phase) => phase switch
        {
            Phase.Powerplay => (p.BowlType == BowlType.Pace ? 4 : -2) + (p.HasTag("powerplay") ? 6 : 0),
            Phase.Middle => p.BowlType == BowlType.Spin ? 6 : 0,
            _ => (p.BowlType == BowlType.Pace ? 4 : -4) + (p.HasTag("death") ? 10 : 0),
        };

        private static readonly int[] OverOrder = { 19, 18, 17, 16, 15, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };

        /// <summary>Who bowls each over: death first, then powerplay, then middle. Max 4, never consecutive.</summary>
        public static List<Player> PlanOvers(IReadOnlyList<Player> xi)
        {
            var sortedPool = xi.ToList();
            sortedPool.Sort((a, b) => Lineups.Desc(a.Bowling, b.Bowling, a, b));
            var pool = sortedPool.Take(6).ToList();
            var quota = pool.ToDictionary(p => p.Id, _ => 4);
            var phaseCount = new Dictionary<string, int>();
            var plan = new Player?[20];

            foreach (var over in OverOrder)
            {
                var phase = PhaseOf(over);
                var phaseCap = phase switch { Phase.Death => 2, Phase.Powerplay => 3, _ => 4 };
                bool Adjacent(Player p) =>
                    (over > 0 && p.Equals(plan[over - 1])) || (over < 19 && p.Equals(plan[over + 1]));

                Player? pick = null;
                for (var strictness = 2; strictness >= 0 && pick == null; strictness--)
                {
                    var best = -1e9;
                    for (var i = 0; i < pool.Count; i++)
                    {
                        var p = pool[i];
                        if (quota[p.Id] <= 0) continue;
                        if (strictness >= 1 && Adjacent(p)) continue;
                        if (strictness >= 2 && (phaseCount.TryGetValue(p.Id + ":" + PhaseName(phase), out var n) ? n : 0) >= phaseCap) continue;
                        var score = p.Bowling + PhaseBonus(p, phase) - (i >= 5 ? 8 : 0);
                        if (score > best)
                        {
                            best = score;
                            pick = p;
                        }
                    }
                }
                plan[over] = pick!;
                quota[pick!.Id]--;
                var key = pick.Id + ":" + PhaseName(phase);
                phaseCount[key] = (phaseCount.TryGetValue(key, out var c) ? c : 0) + 1;
            }
            var overs = plan.Select(p => p!).ToList();
            FixBackToBack(overs);
            return overs;
        }

        private static void FixBackToBack(List<Player> plan)
        {
            bool Clash(int i) =>
                (i > 0 && plan[i].Equals(plan[i - 1])) || (i < plan.Count - 1 && plan[i].Equals(plan[i + 1]));

            for (var i = 1; i < plan.Count; i++)
            {
                if (!plan[i].Equals(plan[i - 1])) continue;
                var candidates = Enumerable.Range(0, plan.Count).ToList();
                var phaseI = PhaseOf(i);
                candidates.Sort((a, b) =>
                {
                    var samePhaseA = PhaseOf(a) == phaseI ? 0 : 1;
                    var samePhaseB = PhaseOf(b) == phaseI ? 0 : 1;
                    return samePhaseA != samePhaseB ? samePhaseA - samePhaseB : a - b;
                });
                foreach (var j in candidates)
                {
                    if (plan[j].Equals(plan[i])) continue;
                    Swap(plan, i, j);
                    if (!Clash(i) && !Clash(j)) break;
                    Swap(plan, i, j);
                }
            }
        }

        private static void Swap(List<Player> plan, int i, int j)
        {
            var tmp = plan[i];
            plan[i] = plan[j];
            plan[j] = tmp;
        }

        public static double BattingPoints(BatterLine b) =>
            b.Runs + b.Fours + 2.0 * b.Sixes + (b.Runs >= 50 ? 10 : 0) + (b.Runs >= 100 ? 15 : 0);

        public static double BowlingPoints(BowlerLine w)
        {
            var points = 25.0 * w.Wickets + w.Dots;
            if (w.Balls >= 12) points += (8 - w.Economy) * 3;
            return points;
        }
    }

    public sealed class MatchSimulator
    {
        private static readonly int[] Outcomes = { 0, 1, 2, 3, 4, 6, -1 };
        private static readonly double[] BaseOdds = { 0.305, 0.355, 0.07, 0.005, 0.138, 0.078, 0.049 };
        private static readonly double[] PitchWeights = { 0.4, 0.25, 0.175, 0.175 };
        private const double WideChance = 0.035;
        private const double LegByeChance = 0.012;

        private readonly Rng _rng;

        public MatchSimulator(Rng rng) { _rng = rng; }

        public MatchResult Play(TeamSheet a, TeamSheet b, int number, MatchStage stage)
        {
            var pitch = (Pitch)_rng.PickWeighted(PitchWeights);
            var tossWinner = _rng.Chance(0.5) ? a : b;
            var tossLoser = ReferenceEquals(tossWinner, a) ? b : a;
            var batFirst = _rng.Chance(0.4) ? tossWinner : tossLoser;
            var batSecond = ReferenceEquals(batFirst, a) ? b : a;

            var first = PlayInnings(batFirst, batSecond, pitch, null);
            var second = PlayInnings(batSecond, batFirst, pitch, first.Runs + 1);

            string winnerId;
            string margin;
            var superOver = false;
            if (second.Runs > first.Runs)
            {
                winnerId = batSecond.TeamId;
                var wicketsLeft = batSecond.XI.Count - 1 - second.Wickets;
                var ballsLeft = 120 - second.LegalBalls;
                margin = $"by {wicketsLeft} {(wicketsLeft == 1 ? "wicket" : "wickets")}" +
                         (ballsLeft > 0 ? $" ({ballsLeft} {(ballsLeft == 1 ? "ball" : "balls")} left)" : "");
            }
            else if (first.Runs > second.Runs)
            {
                winnerId = batFirst.TeamId;
                var runs = first.Runs - second.Runs;
                margin = $"by {runs} {(runs == 1 ? "run" : "runs")}";
            }
            else
            {
                winnerId = SuperOverWinner(batFirst, batSecond, pitch);
                margin = "in the Super Over";
                superOver = true;
            }
            var winnerSheet = winnerId == a.TeamId ? a : b;

            return new MatchResult
            {
                Number = number,
                Stage = stage,
                Pitch = pitch,
                TossWinnerId = tossWinner.TeamId,
                First = first,
                Second = second,
                WinnerId = winnerId,
                LoserId = winnerId == a.TeamId ? b.TeamId : a.TeamId,
                Margin = margin,
                SuperOver = superOver,
                PlayerOfMatch = PlayerOfMatch(winnerSheet, new[] { first, second }),
            };
        }

        /// <summary>
        /// One innings of <paramref name="overs"/> overs on the season engine's odds
        /// (match mode's simulated first innings). C#-only: the Dart core doesn't need it.
        /// </summary>
        public Innings SimulateInnings(TeamSheet bat, TeamSheet bowl, Pitch pitch, int overs, IReadOnlyList<Player> plan, int? target = null, double ground = 1)
        {
            _ground = ground;
            try
            {
                return PlayInnings(bat, bowl, pitch, target, overs, plan);
            }
            finally
            {
                _ground = 1;
            }
        }

        /// <summary>
        /// Match mode's ground size: above 1 a small ground (more fours, many
        /// more sixes), below 1 a big one. Always 1 on the season engine, so
        /// its odds stay identical to the Dart core's.
        /// </summary>
        private double _ground = 1;

        /// <summary>The batting and bowling sides' TeamSheet.Edge for the innings being played.</summary>
        private double _batEdge, _bowlEdge;

        private Innings PlayInnings(TeamSheet bat, TeamSheet bowl, Pitch pitch, int? target, int overs = 20, IReadOnlyList<Player>? overPlan = null)
        {
            _batEdge = bat.Edge;
            _bowlEdge = bowl.Edge;
            var order = Cricket.BattingOrder(bat.XI);
            var inn = new Innings(bat.TeamId, bowl.TeamId, order.Select(p => new BatterLine(p)).ToList());
            var plan = overPlan ?? Cricket.PlanOvers(bowl.XI);
            var bowlerLines = new OrderedMap<string, BowlerLine>();

            var striker = 0;
            var nonStriker = 1;
            var nextIn = 2;
            inn.Batting[0].Batted = true;
            inn.Batting[1].Batted = true;
            bool Chased() => target != null && inn.Runs >= target.Value;

            for (var over = 0; over < overs; over++)
            {
                var bowler = plan[over];
                if (!bowlerLines.TryGetValue(bowler.Id, out var line)) bowlerLines[bowler.Id] = line = new BowlerLine(bowler);
                // Short games keep the shape of a T20: powerplay, middle, death.
                var phase = Cricket.PhaseOf(over * 20 / overs);
                var legal = 0;
                while (legal < 6)
                {
                    if (_rng.Chance(WideChance))
                    {
                        inn.Runs++;
                        inn.Extras++;
                        line.Runs++;
                        line.Wides++;
                        if (Chased()) goto done;
                        continue;
                    }
                    legal++;
                    inn.LegalBalls++;
                    line.Balls++;
                    var batter = inn.Batting[striker];
                    batter.Balls++;

                    if (_rng.Chance(LegByeChance))
                    {
                        inn.Runs++;
                        inn.Extras++;
                        (striker, nonStriker) = (nonStriker, striker);
                        if (Chased()) goto done;
                        continue;
                    }

                    var outcome = Ball(batter, bowler, phase, pitch, target == null ? (int?)null : target.Value - inn.Runs, overs * 6 - inn.LegalBalls + 1);
                    if (outcome < 0)
                    {
                        var (text, bowlerCredit) = Dismissal(bowler, bowl.XI);
                        batter.Dismissal = text;
                        line.Dots++;
                        if (bowlerCredit) line.Wickets++;
                        inn.Wickets++;
                        if (inn.AllOut) goto done;
                        striker = nextIn++;
                        inn.Batting[striker].Batted = true;
                        continue;
                    }

                    batter.Runs += outcome;
                    inn.Runs += outcome;
                    line.Runs += outcome;
                    if (outcome == 0) line.Dots++;
                    if (outcome == 4) batter.Fours++;
                    if (outcome == 6) batter.Sixes++;
                    if (outcome % 2 == 1) (striker, nonStriker) = (nonStriker, striker);
                    if (Chased()) goto done;
                }
                (striker, nonStriker) = (nonStriker, striker);
            }
            done:
            inn.Bowling.AddRange(bowlerLines.Values);
            return inn;
        }

        private static double M(double x) => DartMath.Clamp(x, 0.15, 3.0);

        private static int PitchBowling(Pitch pitch, BowlType type) => pitch switch
        {
            Pitch.SpinFriendly => type == BowlType.Spin ? 6 : -2,
            Pitch.SeamFriendly => type == BowlType.Pace ? 6 : -2,
            _ => 0,
        };

        private int Ball(BatterLine b, Player bowler, Phase phase, Pitch pitch, int? runsNeeded, int ballsLeft)
        {
            var batter = b.Player;
            var batEff = batter.Batting + batter.Form + (pitch == Pitch.Flat ? 5 : 0);
            if (b.Balls <= 6) batEff -= 7;
            else if (b.Balls > 30) batEff += 3;
            var bowlEff = bowler.Bowling + bowler.Form + PitchBowling(pitch, bowler.BowlType);
            // Edges are 0 outside the season sim: exactly the classic (Dart) odds.
            var d = DartMath.Clamp((batEff - bowlEff + _batEdge - _bowlEdge) / 100.0, -0.8, 0.8);

            var w = (double[])BaseOdds.Clone();
            w[0] *= M(1 - 1.0 * d);
            w[2] *= M(1 + 0.4 * d);
            w[4] *= M(1 + 1.3 * d);
            w[5] *= M(1 + 1.8 * d);
            w[6] *= M(1 - 1.6 * d);
            if (_ground != 1)
            {
                w[4] *= _ground;
                w[5] *= _ground * _ground * _ground;
            }

            switch (phase)
            {
                case Phase.Powerplay:
                    w[0] *= 1.05;
                    w[4] *= 1.25;
                    w[5] *= 0.85;
                    w[6] *= 0.95;
                    break;
                case Phase.Middle:
                    w[1] *= 1.15;
                    w[4] *= 0.8;
                    w[5] *= 0.95;
                    w[6] *= 0.95;
                    break;
                default:
                    w[0] *= 0.8;
                    w[2] *= 1.2;
                    w[4] *= 1.1;
                    w[5] *= 1.6;
                    w[6] *= 1.45;
                    break;
            }

            if (runsNeeded != null)
            {
                var requiredRate = (double)(runsNeeded.Value * 6) / Math.Max(1, ballsLeft);
                var a = DartMath.Clamp((requiredRate - 9) / 6, -0.5, 1.0);
                w[0] *= M(1 - 0.2 * a);
                w[4] *= M(1 + 0.3 * a);
                w[5] *= M(1 + 0.6 * a);
                w[6] *= M(1 + 0.7 * a);
            }
            return Outcomes[_rng.PickWeighted(w)];
        }

        private (string, bool) Dismissal(Player bowler, List<Player> fielding)
        {
            var keeper = fielding.FirstOrDefault(p => p.IsKeeper);
            var r = _rng.NextDouble();
            if (bowler.BowlType == BowlType.Spin && keeper != null && r < 0.05)
                return ($"st {keeper.Name} b {bowler.Name}", true);
            if (r < 0.65)
            {
                var catcher = Fielder(fielding, keeper, bowler);
                if (catcher.Equals(bowler)) return ($"c & b {bowler.Name}", true);
                return ($"c {catcher.Name} b {bowler.Name}", true);
            }
            if (r < 0.83) return ($"b {bowler.Name}", true);
            if (r < 0.94) return ($"lbw b {bowler.Name}", true);
            return ($"run out ({Fielder(fielding, keeper, bowler).Name})", false);
        }

        private Player Fielder(List<Player> fielding, Player? keeper, Player bowler)
        {
            var weights = fielding
                .Select(p => (double)p.Fielding * (p.Equals(keeper) && bowler.BowlType == BowlType.Pace ? 3 : 1))
                .ToList();
            return fielding[_rng.PickWeighted(weights)];
        }

        private string SuperOverWinner(TeamSheet first, TeamSheet second, Pitch pitch)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var r1 = SuperOverInnings(first, second, pitch, null);
                var r2 = SuperOverInnings(second, first, pitch, r1 + 1);
                if (r2 > r1) return second.TeamId;
                if (r1 > r2) return first.TeamId;
            }
            return _rng.Chance(0.5) ? first.TeamId : second.TeamId;
        }

        private int SuperOverInnings(TeamSheet bat, TeamSheet bowl, Pitch pitch, int? target)
        {
            _batEdge = bat.Edge;
            _bowlEdge = bowl.Edge;
            var bySkill = bat.XI.ToList();
            bySkill.Sort((a, b) => Lineups.Desc(a.Batting, b.Batting, a, b));
            var batters = bySkill.Take(3).Select(p => new BatterLine(p) { Balls = 10 }).ToList();
            var bowlers = bowl.XI.ToList();
            bowlers.Sort((a, b) => Lineups.Desc(
                a.Bowling + Cricket.PhaseBonus(a, Phase.Death),
                b.Bowling + Cricket.PhaseBonus(b, Phase.Death),
                a,
                b));
            var bowler = bowlers[0];

            var runs = 0;
            var wickets = 0;
            var striker = 0;
            var nonStriker = 1;
            var legal = 0;
            while (legal < 6 && wickets < 2 && (target == null || runs < target.Value))
            {
                if (_rng.Chance(WideChance))
                {
                    runs++;
                    continue;
                }
                legal++;
                var outcome = Ball(batters[striker], bowler, Phase.Death, pitch, target == null ? (int?)null : target.Value - runs, 7 - legal);
                if (outcome < 0)
                {
                    wickets++;
                    striker = 2;
                    continue;
                }
                runs += outcome;
                if (outcome % 2 == 1) (striker, nonStriker) = (nonStriker, striker);
            }
            return runs;
        }

        private static Player PlayerOfMatch(TeamSheet winner, Innings[] innings)
        {
            var points = new OrderedMap<string, (Player Player, double Points)>();
            void Add(Player p, double pts)
            {
                points[p.Id] = (p, (points.TryGetValue(p.Id, out var cur) ? cur.Points : 0) + pts);
            }

            foreach (var inn in innings)
            {
                foreach (var b in inn.Batting.Where(b => b.Batted))
                {
                    if (inn.BattingTeamId != winner.TeamId) continue;
                    Add(b.Player, Cricket.BattingPoints(b));
                }
                foreach (var w in inn.Bowling)
                {
                    if (inn.BowlingTeamId != winner.TeamId) continue;
                    Add(w.Player, Cricket.BowlingPoints(w));
                }
            }
            var ranked = points.Values.ToList();
            ranked.Sort((x, y) =>
            {
                var c = y.Points.CompareTo(x.Points);
                return c != 0 ? c : DartMath.Compare(x.Player.Id, y.Player.Id);
            });
            return ranked.Count == 0 ? winner.XI[0] : ranked[0].Player;
        }
    }
}
