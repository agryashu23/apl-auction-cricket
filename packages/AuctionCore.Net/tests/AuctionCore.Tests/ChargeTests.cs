using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;
using Xunit.Abstractions;

namespace AuctionCore.Tests
{
    /// <summary>Footwork before the ball: down the track (a fuller ball, met sooner, stumped if he misses) and across the crease.</summary>
    public class ChargeTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public ChargeTests(ITestOutputHelper output) { _out = output; }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static Delivery Ball(DeliveryKind kind, double bounceZ, double atX) => new Delivery
        {
            Kind = kind, SpeedKmh = kind == DeliveryKind.Pace ? 138 : 86, ReleaseX = atX, ReleaseY = 2.0, ReleaseZ = 19,
            BounceX = atX, BounceZ = bounceZ, Length = Bowling.LengthOf(bounceZ, kind != DeliveryKind.Pace), Line = Bowling.LineOf(atX),
        };

        [Fact]
        public void Charging_makes_a_good_length_ball_a_half_volley_to_loft()
        {
            var d = Ball(DeliveryKind.Pace, 5.5, 0);
            Assert.Equal(Length.Good, d.Length);
            var stay = new ShotInput(ShotType.Loft, Zone.StraightOff, 0);
            var charge = new ShotInput(ShotType.Loft, Zone.StraightOff, 0) { Charged = true };
            Assert.Equal(Length.Full, Batting.EffectiveLength(d, charge));
            Assert.True(Batting.Suitability(d, charge, Zone.StraightOff) > Batting.Suitability(d, stay, Zone.StraightOff));
        }

        [Fact]
        public void Charged_he_meets_it_sooner_so_the_perfect_tap_comes_earlier()
        {
            var d = Ball(DeliveryKind.Pace, 5.5, 0);
            var t = new Trajectory(d);
            var batter = Data.Players.OrderByDescending(p => p.Batting).First();
            var bowler = Data.Players.Where(p => p.BowlType == BowlType.Pace).OrderBy(p => p.Bowling).First();
            Assert.True(Batting.IdealTap(t, true) < Batting.IdealTap(t));
            var r = Batting.Resolve(batter, bowler, d, t, new ShotInput(ShotType.Loft, Zone.StraightOff, Batting.IdealTap(t, true)) { Charged = true }, Field.For(1, false), new Rng(3));
            Assert.Equal(Timing.Perfect, r.Timing);
        }

        [Fact]
        public void Moving_across_to_the_off_side_brings_the_ball_onto_his_legs()
        {
            var d = Ball(DeliveryKind.Pace, 5.5, 0.12);
            Assert.Equal(Line.Off, d.Line);
            Assert.Equal(Line.Middle, Batting.EffectiveLine(d, new ShotInput(ShotType.Stroke, Zone.MidWicket, 0) { Across = 1 }));
            Assert.Equal(Line.OutsideOff, Batting.EffectiveLine(d, new ShotInput(ShotType.Stroke, Zone.Cover, 0) { Across = -1 }));
        }

        [Fact]
        public void Charging_a_spinner_and_missing_gets_him_stumped_most_times()
        {
            // Turning away outside off: it misses the stumps, so a miss can't be bowled.
            var d = Ball(DeliveryKind.OffSpin, 4.5, 0.7);
            var t = new Trajectory(d);
            Assert.False(t.HittingStumps());
            var batter = Data.Players.OrderBy(p => Math.Abs(p.Batting - 70)).First();
            var bowler = Data.Players.Where(p => p.BowlType == BowlType.Spin).OrderByDescending(p => p.Bowling).First();
            var field = Field.For(1, true);
            int Stumped(bool charged) => Enumerable.Range(0, 400).Count(i =>
            {
                var shot = new ShotInput(ShotType.Loft, Zone.StraightOff, Batting.IdealTap(t, charged) + 0.6) { Charged = charged };
                return Batting.Resolve(batter, bowler, d, t, shot, field, new Rng(100 + i)).HowOut == "stumped";
            });
            var charged = Stumped(true);
            var stayed = Stumped(false);
            _out.WriteLine($"missed charging: stumped {charged}/400; missed in the crease: {stayed}/400");
            Assert.True(charged > 200, $"stumped {charged}/400");
            Assert.True(stayed < charged / 2);
        }
    }
}
