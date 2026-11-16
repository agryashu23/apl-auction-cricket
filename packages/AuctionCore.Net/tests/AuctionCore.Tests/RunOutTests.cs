using Apl.Core;
using Apl.Core.Play;
using Xunit;
using Xunit.Abstractions;

namespace AuctionCore.Tests
{
    /// <summary>
    /// The owner (round 13): "run out not happening if the ball is passed to the keeper or bowler and it's
    /// an easy run out". The user's MANUAL running settles through the same race (QuickMatch.SettleRuns →
    /// Running.Settle / TurnBack): a batter clearly short when the throw's taken is out, taken and broken.
    /// </summary>
    public class RunOutTests
    {
        private readonly ITestOutputHelper _out;
        public RunOutTests(ITestOutputHelper output) { _out = output; }

        private static BallResult Ball(double back, double distance = 25) => new BallResult
        {
            Running = new RunPlan { Back = back, ThrowDistance = distance, Safe = 1, DirectHit = 0.3, ToKeeperEnd = true, Fielder = "cover" },
        };

        /// <summary>Running speed between the creases (m/s): 20 m less the two creases' grounding in RunTime.</summary>
        private const double Pace = 6.5;

        [Fact]
        public void Three_metres_short_when_the_keeper_gathers_is_out_taken_and_broken()
        {
            int outs = 0, direct = 0, n = 400;
            for (var i = 0; i < n; i++)
            {
                // A second run: the throw's in so that, gathered, the bails are off when he's 3 m from the line.
                var r = Ball(RunPlan.ArrivesAt(2) - RunPlan.Gather - 3 / Pace);
                Running.Settle(r, 2, new Rng(100 + i));
                if (r.RunOut) outs++;
                if (r.DirectHit) direct++;
            }
            _out.WriteLine($"3 m short on the second run: {outs}/{n} run out ({direct} direct hits)");
            Assert.True(outs >= n * 0.97, $"{outs}/{n}");
            Assert.True(direct < outs / 4, "mostly taken and broken");
        }

        [Fact]
        public void A_late_call_on_a_single_leaves_him_short_and_out()
        {
            var outs = 0;
            for (var i = 0; i < 400; i++)
            {
                // A comfortable single (0.4 s to spare) — but RUN pressed 1.4 s late (some 7 m short).
                var r = Ball(RunPlan.ArrivesAt(1) + 0.4);
                Running.Settle(r, 1, new Rng(300 + i), late: 1.4);
                if (r.RunOut) outs++;
            }
            _out.WriteLine($"single called 1.4 s late: {outs}/400 run out");
            Assert.True(outs >= 390);
        }

        [Fact]
        public void Stranded_after_cancel_is_out()
        {
            var outs = 0;
            for (var i = 0; i < 400; i++)
            {
                // Sent back 1.5 s into the run, the throw already in: he has 3.4 s to get back, the ball 2.5.
                var r = Ball(2.5);
                Running.TurnBack(r, 1, 1.5, new Rng(500 + i));
                if (r.RunOut) outs++;
            }
            _out.WriteLine($"stranded after CANCEL: {outs}/400 run out");
            Assert.True(outs >= 390);
        }

        [Fact]
        public void An_easy_single_is_not_out()
        {
            var outs = 0;
            for (var i = 0; i < 400; i++)
            {
                var r = Ball(RunPlan.ArrivesAt(1) + 1.0);
                Running.Settle(r, 1, new Rng(700 + i));
                if (r.RunOut) outs++;
            }
            Assert.True(outs <= 2, $"{outs}/400");
        }
    }
}
