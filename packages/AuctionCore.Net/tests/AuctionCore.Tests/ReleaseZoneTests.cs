using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;

namespace AuctionCore.Tests
{
    /// <summary>Round 13 (owner): the release meter's zones — green, orange, red — and what each one bowls.</summary>
    public class ReleaseZoneTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static Player Quick => TestBowlers.TopFastBowler(Data);

        [Fact]
        public void The_zones_grade_as_shown()
        {
            Assert.Equal(Timing.Perfect, Bowling.ReleaseGrade(0.1));
            Assert.Equal(Timing.Good, Bowling.ReleaseGrade(-0.8));
            Assert.Equal(Timing.Early, Bowling.ReleaseGrade(-1.5));
            Assert.Equal(Timing.Late, Bowling.ReleaseGrade(1.5));
            Assert.Equal(Timing.VeryLate, Bowling.ReleaseGrade(2.5));
            Assert.False(Bowling.ReleaseNoBall(1.9));
            Assert.True(Bowling.ReleaseNoBall(2.1));
        }

        [Fact]
        public void Green_keeps_the_movement_orange_loses_some_red_is_always_a_no_ball()
        {
            var rng = new Rng(3);
            double Swing(double e) => Enumerable.Range(0, 300).Average(_ => Math.Abs(Bowling.Deliver(Quick, Variation.Outswinger, 0.2, 5.5, e, 0, rng).Swing));
            Assert.True(Swing(0) > Swing(1.6) * 1.2, "an orange release swings less");
            var greenNoBalls = Enumerable.Range(0, 500).Count(_ => Bowling.Deliver(Quick, Variation.Stock, 0.2, 5.5, 0, 1, rng).NoBall);
            Assert.True(greenNoBalls < 10, $"green no-balls {greenNoBalls}/500");
            Assert.All(Enumerable.Range(0, 200).Select(_ => Bowling.Deliver(Quick, Variation.Stock, 0.2, 5.5, 2.5, 1, rng)), d => Assert.True(d.NoBall && !d.Wide));
        }

        /// <summary>Round 13 blocker (a 111 s frame after the red no-ball in test runs): every engine step of a red release and the free hit after it is quick.</summary>
        [Fact]
        public void A_thousand_red_releases_and_their_free_hits_are_quick()
        {
            var sw = new System.Diagnostics.Stopwatch();
            var rng = new Rng(9);
            var balls = 0;
            for (var g = 0; balls < 1000; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, "CHC"), QuickMatch.LastSeasonXI(Data, "MBH"), 3, 600 + g, null, userBatsFirst: false);
                m.TestAlwaysReview = true;
                while (!m.Finished && balls < 1000)
                {
                    sw.Start();
                    // The red one (as the evidence run bowls it: a random length, the overstep forced)…
                    m.TestNoBall = true;
                    m.Bowl(Bowling.Variations(m.CurrentBowler)[0], rng.NextDouble() * 0.6 - 0.1, 1 + rng.NextDouble() * 8, 2.5);
                    m.PlayAi();
                    for (var i = 0; i < m.Field.Fielders.Count; i++) m.SlotOf(i);
                    balls++;
                    if (m.Finished) break;
                    // …and the free hit.
                    m.Bowl(Variation.Stock, 0.1, 2.2, 0);
                    m.PlayAi();
                    for (var i = 0; i < m.Field.Fielders.Count; i++) m.SlotOf(i);
                    sw.Stop();
                }
                sw.Stop();
            }
            Assert.True(sw.ElapsedMilliseconds < 1500, $"{sw.ElapsedMilliseconds} ms for 1000 red releases and their free hits");
        }

        /// <summary>The 112 s stall's cause: a dead, dug-in ball's lbw tracking — every tracked path is now bounded.</summary>
        [Fact]
        public void Lbw_tracking_paths_stay_bounded_for_red_releases()
        {
            var rng = new Rng(11);
            var longest = 0;
            for (var i = 0; i < 3000; i++)
            {
                var d = Bowling.Deliver(Quick, Variation.Stock, rng.NextDouble() * 0.6 - 0.1, 1 + rng.NextDouble() * 12, 2.5, 1, rng);
                var tr = BallTracking.Track(new Trajectory(d), 1.6);
                longest = Math.Max(longest, Math.Max(tr.Path.Count, tr.Projected.Count));
            }
            Assert.True(longest <= Trajectory.MaxSamples + 1, $"{longest} points");
        }

        [Fact]
        public void A_red_release_in_a_match_is_an_extra_and_a_free_hit()
        {
            var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, "CHC"), QuickMatch.LastSeasonXI(Data, "MBH"), 3, 21, null, userBatsFirst: false);
            var balls = m.LegalBalls;
            m.Bowl(Variation.Stock, 0.2, 5.5, 2.6);
            var r = m.PlayAi();
            Assert.True(r.NoBall);
            Assert.True(r.Runs >= 1);
            Assert.Equal(balls, m.LegalBalls);
            Assert.True(m.FreeHit);
        }
    }
}
