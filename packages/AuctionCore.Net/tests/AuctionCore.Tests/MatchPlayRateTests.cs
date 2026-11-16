using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;
using Xunit.Abstractions;

namespace AuctionCore.Tests
{
    /// <summary>
    /// Round 8 (match-play): how often wickets fall in a short match, with the
    /// AI batting against the user's bowling as the test runs bowl it (every ball
    /// type, varied lengths, a release error about half the green) — so a
    /// 2-over capture shows dismissals — and the totals stay believable.
    /// </summary>
    public class MatchPlayRateTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public MatchPlayRateTests(ITestOutputHelper output) { _out = output; }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        /// <summary>The user bowls (the test-run autoplay), the AI bats: one innings of <paramref name="overs"/> per game.</summary>
        private static (double ballsPerWicket, double rpo, double wicketsPerInnings, string outs) UserBowls(int overs, int games)
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            int balls = 0, runs = 0, wickets = 0;
            var how = new Dictionary<string, int>();
            for (var g = 0; g < games; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), overs, 300 + g, null, userBatsFirst: false);
                var rng = new Rng(500 + g);
                while (!m.Finished)
                {
                    var bowler = m.CurrentBowler;
                    var vars = Bowling.Variations(bowler);
                    var spin = bowler.BowlType == BowlType.Spin;
                    var x = rng.NextDouble() * 0.6 - 0.1;
                    var z = spin ? 2.5 + rng.NextDouble() * 4 : 1 + rng.NextDouble() * 8;
                    m.Bowl(vars[rng.NextInt(vars.Count)], x, z, Bowling.Normal(rng) * 0.5);
                    var r = m.PlayAi();
                    if (r.Legal) balls++;
                    runs += r.Runs;
                    if (!r.Wicket) continue;
                    wickets++;
                    var k = r.HowOut ?? "?";
                    how[k] = how.TryGetValue(k, out var c) ? c + 1 : 1;
                }
            }
            var outs = string.Join(", ", how.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"));
            return (balls / (double)Math.Max(1, wickets), runs * 6.0 / Math.Max(1, balls), wickets / (double)games, outs);
        }

        /// <summary>
        /// The user's side batting as the client's test-run autoplay does (MatchDirector.Batting
        /// AutoShotTime/AutoShotType): tap error <paramref name="sigma"/> windows, LOFT when the rate
        /// is over <paramref name="loftOver"/> (else 30% PUSH, 70% STROKE). Wickets per 2-over chase,
        /// and the share of chases with none.
        /// </summary>
        private static (double perInnings, double none) UserBats(double sigma, double loftOver, int games, bool blunders = false, double late = 2.5) => UserBatsHow(sigma, loftOver, games, blunders, late, out _);

        private static (double perInnings, double none) UserBatsHow(double sigma, double loftOver, int games, bool blunders, double late, out double caughtPerInnings)
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            int wickets = 0, none = 0, caught = 0;
            for (var g = 0; g < games; g++)
            {
                var m = new QuickMatch(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), 2, 700 + g);
                var rng = new Rng(900 + g);
                var w = 0;
                while (!m.Finished)
                {
                    var t = m.NextDelivery();
                    var rate = m.BallsLeft == 0 ? 99 : m.RunsNeeded * 6.0 / m.BallsLeft;
                    var type = rate > loftOver ? ShotType.Loft : rng.Chance(0.3) ? ShotType.Push : ShotType.Stroke;
                    var zone = Enum.GetValues(typeof(Zone)).Cast<Zone>().OrderByDescending(z => Batting.Suitability(t.Delivery, type, z)).First();
                    var tap = Batting.IdealTap(t) + Bowling.Normal(rng) * sigma * Batting.Window(m.OnStrike.Player, t.Delivery);
                    // The proposed test-run blunder: the 2nd and 4th ball of each over swung at late and in the air.
                    if (blunders && (m.BallInOver == 1 || m.BallInOver == 3))
                    {
                        type = ShotType.Loft;
                        tap = Batting.IdealTap(t) + late * Batting.Window(m.OnStrike.Player, t.Delivery);
                    }
                    var res = m.Play(new ShotInput(type, zone, tap));
                    if (res.Wicket) w++;
                    if (res.Wicket && res.Caught) caught++;
                }
                wickets += w;
                if (w == 0) none++;
            }
            caughtPerInnings = caught / (double)games;
            return (wickets / (double)games, none / (double)games);
        }

        [Fact]
        public void Autoplay_batting_wicket_rates()
        {
            var now = UserBats(0.7, 9, 400);
            var proposed = UserBats(0.7, 9, 400, blunders: true);
            _out.WriteLine($"autoplay now (sigma 0.7, loft > 9 rpo): {now.perInnings:0.00} wickets per 2-over chase, {now.none:P0} with none");
            _out.WriteLine($"proposed (late loft on balls 2 and 4):   {proposed.perInnings:0.00} wickets per 2-over chase, {proposed.none:P0} with none");
            Assert.True(proposed.perInnings > now.perInnings);
            foreach (var late in new[] { 1.2, 1.8, 2.5 })
            {
                var x = UserBatsHow(0.7, 9, 400, true, late, out var c);
                _out.WriteLine($"late loft +{late} windows on balls 2 and 4: {x.perInnings:0.00} wickets, {c:0.00} caught per 2-over chase");
            }
        }

        /// <summary>
        /// The client's test-run nudge (MatchDirector.TestNudges): once an innings, the first ball
        /// with a 30–97% risky extra run, they go for it. How many 2-over innings then film a run out.
        /// </summary>
        [Fact]
        public void Test_run_nudge_films_a_run_out_most_innings()
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            int runOuts = 0, nudged = 0, games = 400;
            for (var g = 0; g < games; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), 2, 1300 + g, null, userBatsFirst: false);
                var rng = new Rng(1500 + g);
                var done = false;
                while (!m.Finished)
                {
                    var vars = Bowling.Variations(m.CurrentBowler);
                    m.Bowl(vars[rng.NextInt(vars.Count)], rng.NextDouble() * 0.6 - 0.1, 1 + rng.NextDouble() * 8, Bowling.Normal(rng) * 0.5);
                    var r = m.PlayAi();
                    var plan = r.Running;
                    if (done || plan == null || r.Wicket || r.Four || r.Six || r.Caught || r.Attempted > plan.Safe) continue;
                    var risk = plan.Risk(plan.Safe + 1);
                    if (risk < 0.3 || risk > 0.97) continue;
                    done = true;
                    nudged++;
                    m.SettleRuns(plan.Safe + 1);
                    if (r.RunOut) runOuts++;
                }
            }
            _out.WriteLine($"nudge fired in {nudged}/{games} innings; run outs {runOuts} ({runOuts / (double)games:P0} of innings)");
            Assert.True(nudged > games / 3);
        }

        /// <summary>
        /// Round 10 (owner: "only direct-hit run outs"): over many 20-over AI innings against the
        /// test-run bowling, how the run outs fall — direct hits vs the keeper / bowler taking the
        /// throw and whipping the bails off — and how many an innings.
        /// </summary>
        [Fact]
        public void Run_outs_are_mostly_take_and_break()
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            int runOuts = 0, direct = 0, bowlerEnd = 0, games = 300;
            var positions = new[] { "slip", "keeper)", "cover", "point", "mid-off", "mid-on", "fine leg", "third man", "midwicket", "square leg", "gully", "long" };
            string? sample = null;
            for (var g = 0; g < games; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), 20, 2300 + g, null, userBatsFirst: false);
                var rng = new Rng(2500 + g);
                while (!m.Finished)
                {
                    var vars = Bowling.Variations(m.CurrentBowler);
                    m.Bowl(vars[rng.NextInt(vars.Count)], rng.NextDouble() * 0.6 - 0.1, 2 + rng.NextDouble() * 6, Bowling.Normal(rng) * 0.5);
                    var r = m.PlayAi();
                    if (!r.RunOut) continue;
                    runOuts++;
                    if (r.DirectHit) direct++;
                    if (!r.RunOutAtKeeperEnd) bowlerEnd++;
                    // Credited by name (round 11: "run out (slip)" while the keeper broke the stumps).
                    var line = m.Order.First(b => b.Player == r.OutBatter).HowOut!;
                    sample ??= line;
                    Assert.DoesNotContain(positions, pos => line.Contains("(" + pos) || line.Contains("/" + pos));
                }
            }
            _out.WriteLine($"at the bowler's end {bowlerEnd}; e.g. \"{sample}\"");
            _out.WriteLine($"run outs {runOuts} in {games} innings ({runOuts / (double)games:0.00} an innings): direct hits {direct}, take-and-break {runOuts - direct} ({(runOuts - direct) / (double)System.Math.Max(1, runOuts):P0})");
            Assert.True(runOuts > 0);
        }

        /// <summary>
        /// The test-run catch nudge (MatchDirector.BowlingFlight): when the AI bats, chosen balls are lofted
        /// late. How many catches that films per nudged ball, for a few lateness / zone choices.
        /// </summary>
        [Fact]
        public void Catch_nudge_rates()
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            foreach (var (late, randomZone) in new[] { (1.2, true), (1.2, false), (0.9, false), (1.5, false) })
            {
                int nudges = 0, caught = 0;
                for (var g = 0; g < 300; g++)
                {
                    var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), 3, 3300 + g, null, userBatsFirst: false);
                    var rng = new Rng(3500 + g);
                    while (!m.Finished)
                    {
                        var vars = Bowling.Variations(m.CurrentBowler);
                        var t = m.Bowl(vars[rng.NextInt(vars.Count)], rng.NextDouble() * 0.6 - 0.1, 2 + rng.NextDouble() * 6, Bowling.Normal(rng) * 0.5);
                        var shot = m.AiShot();
                        if (!shot.IsLeave && m.BallInOver % 2 == 0)
                        {
                            nudges++;
                            var zone = randomZone ? (Zone)rng.NextInt(8) : shot.Direction;
                            shot = new ShotInput(ShotType.Loft, zone, Batting.IdealTap(t) + late * Batting.Window(m.OnStrike.Player, t.Delivery));
                        }
                        var r = m.Play(shot);
                        if (r.Caught) caught++;
                    }
                }
                _out.WriteLine($"late +{late} windows, {(randomZone ? "random zone" : "his own zone")}: {caught}/{nudges} caught ({caught / (double)System.Math.Max(1, nudges):P0} a nudge)");
            }
        }

        [Fact]
        public void Ai_batting_loses_wickets_in_a_two_over_match()
        {
            var two = UserBowls(2, 400);
            var five = UserBowls(5, 200);
            _out.WriteLine($"2 overs: wicket every {two.ballsPerWicket:0.0} balls, {two.wicketsPerInnings:0.00} per innings, {two.rpo:0.00} rpo | {two.outs}");
            _out.WriteLine($"5 overs: wicket every {five.ballsPerWicket:0.0} balls, {five.wicketsPerInnings:0.00} per innings, {five.rpo:0.00} rpo | {five.outs}");
            // A 2-over innings should usually see a wicket (T20 death-over hitting loses one every 8-15 balls).
            Assert.InRange(two.wicketsPerInnings, 0.7, 3.5);
            // Believable short-match scoring: not a procession, not a slog-fest.
            Assert.InRange(two.rpo, 6, 14);
        }
    }
}
