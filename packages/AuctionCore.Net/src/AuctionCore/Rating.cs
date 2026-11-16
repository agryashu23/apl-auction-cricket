using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core
{
    public sealed class Lineup
    {
        public Lineup(List<Player> xi, List<Player> bench)
        {
            XI = xi;
            Bench = bench;
        }

        public List<Player> XI { get; }
        public List<Player> Bench { get; }
    }

    public static class Lineups
    {
        private static readonly double[] BatWeights = { 1.0, 1.0, 0.95, 0.9, 0.85, 0.75, 0.6, 0.4, 0.25, 0.15, 0.1 };
        private static readonly double[] BowlWeights = { 1.0, 1.0, 1.0, 1.0, 0.9, 0.3 };

        public static double BattingStrength(IReadOnlyList<Player> xi) =>
            Weighted(xi.Select(p => p.Batting + p.Form).ToList(), BatWeights);

        public static double BowlingStrength(IReadOnlyList<Player> xi) =>
            Weighted(xi.Select(p => p.Bowling + p.Form).ToList(), BowlWeights);

        private static double Weighted(List<int> values, double[] weights)
        {
            var sorted = values.ToList();
            sorted.Sort((a, b) => b.CompareTo(a));
            var sum = 0.0;
            var total = 0.0;
            for (var i = 0; i < weights.Length; i++)
            {
                sum += weights[i] * (i < sorted.Count ? DartMath.Clamp(sorted[i], 0, 100) : 0);
                total += weights[i];
            }
            return sum / total;
        }

        internal static int Desc(int x, int y, Player a, Player b)
        {
            var c = y.CompareTo(x);
            return c != 0 ? c : DartMath.Compare(a.Id, b.Id);
        }

        private static double KeeperValue(Player p) => p.Batting * 0.85 + p.Keeping * 0.15;

        /// <summary>Strongest valid XI: keeper, ≥5 bowling options, overseas cap.</summary>
        public static Lineup PickBestXI(IReadOnlyList<Player> squad, int maxOverseas = 4)
        {
            if (squad.Count <= 11) return new Lineup(squad.ToList(), new List<Player>());

            var locals = squad.Count(p => !p.Overseas);
            var overseasCap = Math.Max(maxOverseas, 11 - locals);
            var squadHasKeeper = squad.Any(p => p.IsKeeper);
            var optionsNeeded = Math.Min(5, squad.Count(p => p.IsBowlingOption));

            bool Valid(List<Player> x) =>
                x.Count(p => p.Overseas) <= overseasCap &&
                (!squadHasKeeper || x.Any(p => p.IsKeeper)) &&
                x.Count(p => p.IsBowlingOption) >= optionsNeeded;

            double Score(List<Player> x) => BattingStrength(x) + BowlingStrength(x);

            var xi = new List<Player>();
            bool Fits(Player p) => !xi.Contains(p) && (!p.Overseas || xi.Count(x => x.Overseas) < overseasCap);

            var keepers = squad.Where(p => p.IsKeeper).ToList();
            keepers.Sort((a, b) =>
            {
                var c = KeeperValue(b).CompareTo(KeeperValue(a));
                return c != 0 ? c : DartMath.Compare(a.Id, b.Id);
            });
            if (keepers.Count > 0) xi.Add(keepers[0]);

            var bowlers = squad.Where(p => p.IsBowlingOption).ToList();
            bowlers.Sort((a, b) => Desc(a.Bowling, b.Bowling, a, b));
            foreach (var p in bowlers)
            {
                if (xi.Count(x => x.IsBowlingOption) >= optionsNeeded) break;
                if (Fits(p)) xi.Add(p);
            }

            var batters = squad.ToList();
            batters.Sort((a, b) => Desc(a.Batting, b.Batting, a, b));
            foreach (var p in batters)
            {
                if (xi.Count >= 11) break;
                if (Fits(p)) xi.Add(p);
            }
            foreach (var p in batters)
            {
                if (xi.Count >= 11) break;
                if (!xi.Contains(p)) xi.Add(p);
            }

            var best = Score(xi);
            for (var round = 0; round < 100; round++)
            {
                var improved = false;
                for (var i = 0; i < xi.Count && !improved; i++)
                {
                    foreach (var candidate in squad)
                    {
                        if (xi.Contains(candidate)) continue;
                        var trial = xi.ToList();
                        trial[i] = candidate;
                        if (!Valid(trial)) continue;
                        var s = Score(trial);
                        if (s > best + 1e-9)
                        {
                            xi[i] = candidate;
                            best = s;
                            improved = true;
                            break;
                        }
                    }
                }
                if (!improved) break;
            }

            var bench = squad.Where(p => !xi.Contains(p)).ToList();
            bench.Sort((a, b) => Desc(a.Impact, b.Impact, a, b));
            return new Lineup(xi, bench);
        }
    }

    public sealed class SquadRating
    {
        public string TeamId = "";
        public Lineup Lineup = null!;
        public int Batting, Bowling, Balance, Depth, Value, Overall;

        /// <summary>1 = best squad in the league.</summary>
        public int Rank;

        public string Grade => Ratings.GradeFor(Overall);
    }

    public static class Ratings
    {
        public static string GradeFor(int overall)
        {
            if (overall >= 85) return "A+";
            if (overall >= 80) return "A";
            if (overall >= 75) return "B+";
            if (overall >= 70) return "B";
            if (overall >= 65) return "C+";
            if (overall >= 60) return "C";
            return "D";
        }

        private static int Scaled(double raw, double @base, double spread) =>
            DartMath.Clamp(DartMath.Round((raw - @base) * 100 / spread), 0, 100);

        public static List<SquadRating> Rate(IEnumerable<TeamState> teams, int maxOverseasInXI, int benchSlots, RatingScale? scale = null)
        {
            scale ??= new RatingScale();
            var slots = DartMath.Clamp(benchSlots, 0, 4);
            var list = teams.ToList();
            var lineups = list.Select(t => Lineups.PickBestXI(t.Players, maxOverseasInXI)).ToList();

            var strengths = lineups.Select(l => (Lineups.BattingStrength(l.XI) + Lineups.BowlingStrength(l.XI)) / 2).ToList();
            var spends = list.Select(t => (double)t.Spent).ToList();
            var strengthZ = ZScores(strengths);
            var spendZ = ZScores(spends);

            var ratings = new List<SquadRating>();
            for (var i = 0; i < list.Count; i++)
            {
                var xi = lineups[i].XI;
                var bench = lineups[i].Bench;

                var batting = Scaled(Lineups.BattingStrength(xi), scale.BattingBase, scale.Spread);
                var bowling = DartMath.Clamp(Scaled(Lineups.BowlingStrength(xi), scale.BowlingBase, scale.Spread) + VarietyBonus(xi), 0, 100);
                var balance = BalanceScore(xi, list[i].Players);

                var depth = 0;
                if (slots > 0)
                {
                    var benchTop = bench.Take(slots).Select(p => (double)p.Impact).ToList();
                    while (benchTop.Count < slots) benchTop.Add(35);
                    var sum = benchTop[0];
                    for (var k = 1; k < benchTop.Count; k++) sum += benchTop[k];
                    var benchAvg = sum / slots;
                    depth = DartMath.Clamp(DartMath.Round((benchAvg - 45) * 100 / 40), 0, 100);
                }

                var value = DartMath.Clamp(DartMath.Round(50 + 20 * (strengthZ[i] - spendZ[i])), 0, 100);

                var depthWeight = slots > 0 ? 0.10 : 0.0;
                var coreWeight = (1 - 0.12 - 0.06 - depthWeight) / 2;
                var overall = DartMath.Round(coreWeight * batting +
                                             coreWeight * bowling +
                                             0.12 * balance +
                                             depthWeight * depth +
                                             0.06 * value);

                ratings.Add(new SquadRating
                {
                    TeamId = list[i].Id,
                    Lineup = lineups[i],
                    Batting = batting,
                    Bowling = bowling,
                    Balance = balance,
                    Depth = depth,
                    Value = value,
                    Overall = overall,
                });
            }

            var ranked = ratings.ToList();
            ranked.Sort((a, b) =>
            {
                var byOverall = b.Overall.CompareTo(a.Overall);
                if (byOverall != 0) return byOverall;
                var byCore = (b.Batting + b.Bowling).CompareTo(a.Batting + a.Bowling);
                return byCore != 0 ? byCore : DartMath.Compare(a.TeamId, b.TeamId);
            });
            for (var i = 0; i < ranked.Count; i++) ranked[i].Rank = i + 1;
            return ratings;
        }

        private static int VarietyBonus(List<Player> xi)
        {
            var bowlers = xi.Where(p => p.IsBowlingOption).ToList();
            bowlers.Sort((a, b) =>
            {
                var c = b.Bowling.CompareTo(a.Bowling);
                return c != 0 ? c : DartMath.Compare(a.Id, b.Id);
            });
            var top = bowlers.Take(5).ToList();
            var spinners = top.Count(p => p.BowlType == BowlType.Spin);
            var pacers = top.Count(p => p.BowlType == BowlType.Pace);
            var bonus = 0;
            bonus += spinners >= 1 ? 2 : -4;
            bonus += pacers >= 2 ? 2 : -4;
            return bonus;
        }

        /// <summary>Squad shape, 0–100: keepers, left-handers and size across the squad; bowlers and batters in the XI.</summary>
        private static int BalanceScore(List<Player> xi, IReadOnlyCollection<Player> squad)
        {
            var score = 100;
            var keepers = squad.Count(p => p.IsKeeper);
            if (keepers == 0) score -= 25;
            if (keepers == 1) score -= 10;
            var options = xi.Count(p => p.IsBowlingOption);
            if (options < 5) score -= 15 * (5 - options);
            var realBatters = xi.Count(p => p.Batting >= 55);
            if (realBatters < 6) score -= 8 * (6 - realBatters);
            if (!squad.Any(p => p.LeftHanded)) score -= 4;
            if (squad.Count < 18) score -= 10 * (18 - squad.Count);
            return DartMath.Clamp(score, 0, 100);
        }

        private static List<double> ZScores(List<double> xs)
        {
            if (xs.Count == 0) return new List<double>();
            var total = xs[0];
            for (var i = 1; i < xs.Count; i++) total += xs[i];
            var mean = total / xs.Count;
            var variance = 0.0;
            foreach (var x in xs) variance += (x - mean) * (x - mean);
            variance /= xs.Count;
            var sd = Math.Sqrt(variance);
            if (sd < 1e-9) return xs.Select(_ => 0.0).ToList();
            return xs.Select(x => (x - mean) / sd).ToList();
        }
    }
}
