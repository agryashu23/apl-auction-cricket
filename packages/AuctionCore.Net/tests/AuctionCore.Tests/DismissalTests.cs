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
    /// <summary>Every normal mode of dismissal comes up, the TV umpire gets the close ones, and the on-field umpire misses the odd one.</summary>
    public class DismissalTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public DismissalTests(ITestOutputHelper output) { _out = output; }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        /// <summary>Many AI-v-AI innings (both sides the AI batter), every ball's result.</summary>
        private static List<BallResult> Balls(int games)
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            var all = new List<BallResult>();
            for (var g = 0; g < games; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), 20, 40 + g, null, userBatsFirst: false);
                while (!m.Finished) all.Add(m.PlayAi());
            }
            return all;
        }

        [Fact]
        public void Every_mode_of_dismissal_comes_up()
        {
            // Hit wicket is a 0.1–0.3% chance on back-foot attacking shots (1–2 in 150 innings): 600 innings
            // so it doesn't hang on which random numbers this version of the engine happens to draw.
            var balls = Balls(600);
            var how = balls.Where(r => r.Wicket).GroupBy(r => r.CaughtAndBowled ? "caught and bowled" : r.HowOut!).ToDictionary(g => g.Key, g => g.Count());
            _out.WriteLine(string.Join(", ", how.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
            foreach (var mode in new[] { "bowled", "caught", "caught behind", "caught and bowled", "lbw", "run out", "stumped", "hit wicket" })
            {
                Assert.True(how.ContainsKey(mode), $"no {mode} in 600 innings");
            }
            // Run outs fall at both ends, to both batters.
            var runOuts = balls.Where(r => r.RunOut).ToList();
            Assert.Contains(runOuts, r => r.RunOutAtKeeperEnd);
            Assert.Contains(runOuts, r => !r.RunOutAtKeeperEnd);
            Assert.Contains(runOuts, r => r.RunOutStriker);
            Assert.Contains(runOuts, r => !r.RunOutStriker);
        }

        [Fact]
        public void Close_calls_go_to_the_third_umpire_and_he_gets_them_right()
        {
            var balls = Balls(150);
            var referred = balls.Where(r => r.Referral != TvCheck.None).ToList();
            var byKind = referred.GroupBy(r => r.Referral).ToDictionary(g => g.Key, g => (g.Count(), g.Count(r => r.TvOut)));
            _out.WriteLine(string.Join(", ", byKind.Select(kv => $"{kv.Key} {kv.Value.Item1} ({kv.Value.Item2} out)")) + $" — {referred.Count / 150.0:0.00} an innings");
            foreach (var r in referred)
            {
                if (!r.NoBall && !r.FreeHit) Assert.Equal(r.TvOut, r.Wicket);
                if (r.Referral == TvCheck.RunOut)
                {
                    Assert.True(Math.Abs(r.Margin) < Running.Refer);
                    Assert.Equal(r.Margin < 0, r.RunOut);
                }
            }
            Assert.True(byKind.ContainsKey(TvCheck.RunOut) && byKind.ContainsKey(TvCheck.Stumping) && byKind.ContainsKey(TvCheck.Catch));
            // Not every referral is out.
            Assert.Contains(referred, r => !r.TvOut);
        }

        [Fact]
        public void The_umpire_on_the_field_misses_the_odd_genuine_one()
        {
            var balls = Balls(150);
            var edges = balls.Count(r => r.HowOut == "caught behind");
            var missedEdges = balls.Count(r => r.NotGiven == "caught behind");
            var lbws = balls.Count(r => r.HowOut == "lbw");
            var missedLbws = balls.Count(r => r.NotGiven == "lbw");
            _out.WriteLine($"caught behind {edges} given, {missedEdges} missed; lbw {lbws} given, {missedLbws} plumb ones missed; walked {balls.Count(r => r.Walked)}");
            Assert.True(missedEdges > 0 && missedEdges < edges / 4);
            Assert.True(missedLbws < Math.Max(3, lbws / 4));
            Assert.All(balls.Where(r => r.NotGiven != null), r => Assert.False(r.Wicket && !r.Lbw?.Reviewed == true));
        }
    }
}
