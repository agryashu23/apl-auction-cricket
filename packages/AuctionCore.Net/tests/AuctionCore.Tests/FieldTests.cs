using System;
using Apl.Core;
using Apl.Core.Play;
using Xunit;
using Xunit.Abstractions;

namespace AuctionCore.Tests
{
    /// <summary>The ball's path after the hit: the view flies it from this, so it must end where the engine says.</summary>
    public class FieldTests
    {
        private readonly ITestOutputHelper _out;

        public FieldTests(ITestOutputHelper output) { _out = output; }

        [Fact]
        public void Ground_shots_leave_the_bat_fast_and_roll_on()
        {
            // An empty field: just the ball on the standard outfield (straight down the ground, rope at 75 m).
            var field = Field.For(1, false);
            foreach (var exit in new[] { 12.0, 22.0, 36.0 })
            {
                var path = field.Ground(180, exit, new Rng(1)).Path;
                double Speed(double t) => Math.Abs(path.At(t + 0.01).f - path.At(t).f) / 0.01;
                double When(double d)
                {
                    for (var t = 0.0; t < 20; t += 0.01) if (Math.Abs(path.At(t).f) >= d) return t;
                    return double.PositiveInfinity;
                }
                _out.WriteLine($"exit {exit:0} m/s: on screen {Speed(0.05):0.0} m/s off the bat, {Speed(path.RollAt + 0.02):0.0} after the first bounce, {Speed(1.5):0.0} at 1.5 s; 30 m in {When(30):0.00} s, 60 m in {When(60):0.00} s");
                Assert.Equal(exit, Speed(0.05), 1);
                if (exit >= 30) Assert.InRange(When(60), 2.5, 4.0);
                if (exit <= 12) Assert.True(Speed(path.RollAt + 0.02) > 8, "a push still rolls briskly");
            }
        }
        [Fact]
        public void Ball_path_ends_where_the_play_does()
        {
            var rng = new Rng(11);
            var drops = 0;
            for (var i = 0; i < 3000; i++)
            {
                var field = Field.For(i % 2, i % 3 == 0, i % 5 == 0 ? Venue.All[i % Venue.All.Count] : null, i % 4 == 0);
                var angle = rng.Range(-170, 170);
                var speed = rng.Range(8, 42);
                var air = i % 2 == 0;
                var p = air ? field.Air(angle, speed, rng.Range(10, 60), rng) : field.Ground(angle, speed, rng);
                var (x, f, h) = p.Path.At(p.Time);
                Assert.True(Math.Abs(x - p.End.x) < 1e-6 && Math.Abs(f - p.End.f) < 1e-6, $"{i}: path at {p.Time:0.00}s is ({x:0.00}, {f:0.00}), play ended at ({p.End.x:0.00}, {p.End.f:0.00})");
                // Catches and sixes end in the air (at the hands / over the rope); everything else on the ground.
                if (p.Caught) Assert.InRange(h, -1e-6, 1e-6);
                if (!p.Caught && !(p.Boundary && p.Runs == 6)) Assert.Equal(0, h);
                Assert.Equal(air, p.Path.InAir);
                if (p.Dropped.Length > 0)
                {
                    drops++;
                    Assert.False(p.Caught);
                }
            }
            Assert.True(drops > 0);
        }

        [Fact]
        public void A_lofted_drive_goes_further_than_it_goes_up()
        {
            var field = Field.For(1, false);
            var p = field.Air(0, 34, 30, new Rng(3));
            Assert.True(p.Path.Carry > 5 * p.Path.Apex, $"carry {p.Path.Carry:0.0} m, apex {p.Path.Apex:0.0} m");
        }

        [Fact]
        public void A_timed_drive_into_a_gap_races_to_the_rope()
        {
            // Between cover (52°) and mid-off (18°) in the ring, long-off (15°) and deep point (95°) out.
            var p = Field.For(1, false).Ground(36, 38, new Rng(5));
            Assert.True(p.Boundary, $"fielded by {p.Fielder} at {p.Time:0.00}s");
            Assert.InRange(p.Time, 2.0, 4.0);
        }

        [Fact]
        public void A_firm_hit_straight_at_a_fielder_is_stopped()
        {
            var p = Field.For(1, false).Ground(52, 30, new Rng(5));
            Assert.False(p.Boundary);
            Assert.Equal("cover", p.Fielder);
        }

        [Fact]
        public void Gaps_go_for_four_and_some_are_saved_with_a_dive()
        {
            var rng = new Rng(9);
            var field = Field.For(1, false);
            int fours = 0, dives = 0, n = 2000;
            for (var i = 0; i < n; i++)
            {
                var p = field.Ground(rng.Range(-170, 170), rng.Range(22, 40), rng);
                if (p.Boundary) fours++;
                if (p.Dived) dives++;
            }
            Assert.InRange(fours / (double)n, 0.25, 0.75);
            Assert.True(dives > n / 50, $"{dives} dives in {n}");
        }
    }
}
