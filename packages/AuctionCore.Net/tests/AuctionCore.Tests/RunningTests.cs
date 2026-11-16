using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;
using Xunit.Abstractions;

namespace AuctionCore.Tests
{
    /// <summary>Manual running's calls: a dive on the final approach, and CANCEL turning them back.</summary>
    public class RunningTests
    {
        private readonly ITestOutputHelper _out;

        public RunningTests(ITestOutputHelper output) { _out = output; }

        private static RunPlan Plan(double back, bool keeperEnd = true) =>
            new RunPlan { Back = back, ThrowDistance = 25, Safe = 1, DirectHit = 0.3, ToKeeperEnd = keeperEnd, Fielder = "cover" };

        [Fact]
        public void A_timely_dive_turns_a_close_run_out_safe_and_never_the_other_way()
        {
            int outWithout = 0, saved = 0, stillOut = 0;
            for (var seed = 0; seed < 500; seed++)
            {
                var rng = new Rng(seed);
                // Going for two, the throw (gathered at the keeper's end) is about a tenth of a second too quick.
                var r = new BallResult { Running = Plan(RunPlan.ArrivesAt(2) - RunPlan.Gather - 0.4) };
                r.Running.Back = RunPlan.ArrivesAt(2) - RunPlan.Gather - 0.1;
                Running.Settle(r, 2, rng);
                var margin = r.Running.LastMargin;
                var wasOut = r.RunOut;
                // The same race, replayed with a dive at the line.
                Running.Settle(r, 2, rng, dive: true);
                Assert.True(r.Dived);
                if (!wasOut)
                {
                    Assert.False(r.RunOut);
                    continue;
                }
                outWithout++;
                Assert.Equal(margin, r.Running.LastMargin, 6);
                if (margin > -RunPlan.DiveGain)
                {
                    Assert.False(r.RunOut);
                    Assert.Equal(2, r.Ran);
                    saved++;
                }
                else
                {
                    Assert.True(r.RunOut);
                    stillOut++;
                }
            }
            _out.WriteLine($"{outWithout} run out without a dive: {saved} saved by it, {stillOut} still short");
            Assert.True(saved > 50 && stillOut > 5);
        }

        [Fact]
        public void Dive_matters_only_when_it_is_close()
        {
            var rng = new Rng(1);
            var easy = new BallResult { Running = Plan(9) };
            Running.Settle(easy, 1, rng);
            Assert.False(easy.Running.DiveMatters);
            var lost = new BallResult { Running = Plan(RunPlan.ArrivesAt(2) - 1.5) };
            Running.Settle(lost, 2, rng);
            Assert.True(lost.RunOut);
            Assert.False(lost.Running.DiveMatters);
        }

        [Fact]
        public void Cancel_early_turns_them_back_the_run_does_not_count_and_a_quick_throw_can_still_get_one()
        {
            var rng = new Rng(2);
            // A slow throw: back in time, nothing scored.
            var r = new BallResult { Running = Plan(8) };
            Running.Settle(r, 1, rng);
            Running.TurnBack(r, 1, 0.8, rng);
            Assert.True(r.TurnedBack);
            Assert.False(r.RunOut);
            Assert.Equal(0, r.Ran);
            Assert.Equal(0, r.Runs);

            // A direct-hit-quick throw at the keeper's end, sent back late: the striker is short of his own crease.
            var outs = 0;
            for (var seed = 0; seed < 200; seed++)
            {
                var q = new BallResult { Running = Plan(2.2) };
                Running.TurnBack(q, 1, 1.4, new Rng(seed));
                if (!q.RunOut) continue;
                outs++;
                Assert.True(q.RunOutStriker);
                Assert.True(q.RunOutAtKeeperEnd);
                Assert.Equal(0, q.Ran);
            }
            Assert.True(outs > 150, $"{outs}/200");
            // The second run turned back: the non-striker heads home to the keeper's end.
            var second = new BallResult { Running = Plan(5.5) };
            Running.TurnBack(second, 2, 1.4, new Rng(3));
            Assert.True(second.RunOut);
            Assert.False(second.RunOutStriker);
            Assert.Equal(1, second.Ran);
        }

        [Fact]
        public void The_innings_book_follows_a_turn_back_and_a_dive()
        {
            var data = Dataset.FromJson(File.ReadAllText(Path.Combine(Root(), "data", "apl_dataset.json")));
            var m = new QuickMatch(QuickMatch.LastSeasonXI(data, "BLR"), QuickMatch.LastSeasonXI(data, "MBH"), 20, 21);
            var rng = new Rng(4);
            var checkedTurns = 0;
            while (!m.Finished && checkedTurns < 30)
            {
                var t = m.NextDelivery();
                var r = m.Play(new ShotInput(ShotType.Push, (Zone)rng.NextInt(8), Batting.IdealTap(t)));
                if (r.Running == null || r.Wicket || r.Running.Back < 4) continue;
                var before = m.Runs - r.Runs;
                m.TurnBack(1, 0.6);
                Assert.Equal(before + r.Penalty, m.Runs);
                Assert.True(r.TurnedBack);
                m.SettleRuns(1, dive: true);
                Assert.False(r.TurnedBack);
                Assert.True(r.Dived);
                checkedTurns++;
            }
            Assert.True(checkedTurns > 10);
        }

        private static string Root()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }
    }
}
