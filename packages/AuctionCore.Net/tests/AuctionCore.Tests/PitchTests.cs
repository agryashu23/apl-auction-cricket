using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;

namespace AuctionCore.Tests
{
    /// <summary>Owner v26 #23: pitch types per ground, and what each does to the ball (PitchPlay.cs).</summary>
    public class PitchTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static Delivery Ball(DeliveryKind kind, double deviation, Variation v = Variation.Stock) => new Delivery
        {
            Kind = kind, SpeedKmh = kind == DeliveryKind.Pace ? 138 : 88, ReleaseX = 0.2, ReleaseY = 2.1, ReleaseZ = 19,
            BounceX = 0.1, BounceZ = 6, Deviation = deviation, Swing = 0.1, Variation = v,
        };

        /// <summary>The ball's line and height as it reaches the batter (0.9 m out).</summary>
        private static (double x, double y) AtBatter(Delivery d)
        {
            var t = new Trajectory(d);
            var p = t.At(t.TimeAt(0.9));
            return (p.x, p.y);
        }

        [Fact]
        public void Green_pitch_helps_the_seamers()
        {
            var flatBase = AtBatter(Ball(DeliveryKind.Pace, 0.05));
            var green = AtBatter(PitchEffect.Apply(Ball(DeliveryKind.Pace, 0.05), Pitch.SeamFriendly));
            var balanced = AtBatter(PitchEffect.Apply(Ball(DeliveryKind.Pace, 0.05), Pitch.Balanced));
            Assert.Equal(flatBase.x, balanced.x, 9);
            // More movement off the seam and a little more bounce.
            Assert.True(Math.Abs(green.x - 0.1) > Math.Abs(balanced.x - 0.1) + 0.02, $"seam {green.x:0.000} vs {balanced.x:0.000}");
            Assert.True(green.y > balanced.y, $"bounce {green.y:0.000} vs {balanced.y:0.000}");
            // Spinners get less from it.
            Assert.True(Math.Abs(PitchEffect.Apply(Ball(DeliveryKind.OffSpin, 0.3), Pitch.SeamFriendly).Deviation) < 0.3);
        }

        [Fact]
        public void Dry_pitch_turns_and_takes_the_pace_bounce_out()
        {
            var spin = PitchEffect.Apply(Ball(DeliveryKind.LegSpin, -0.3), Pitch.SpinFriendly);
            Assert.True(spin.Deviation < -0.38, $"turn {spin.Deviation:0.000}");
            var pace = AtBatter(PitchEffect.Apply(Ball(DeliveryKind.Pace, 0.0), Pitch.SpinFriendly));
            var same = AtBatter(Ball(DeliveryKind.Pace, 0.0));
            Assert.True(pace.y < same.y, $"low bounce {pace.y:0.000} vs {same.y:0.000}");
            // The cutter grips.
            Assert.True(PitchEffect.Apply(Ball(DeliveryKind.Pace, 0.1, Variation.OffCutter), Pitch.SpinFriendly).Deviation > 0.13);
        }

        [Fact]
        public void Flat_pitch_gives_the_bowlers_less()
        {
            Assert.True(Math.Abs(PitchEffect.Apply(Ball(DeliveryKind.Pace, 0.06), Pitch.Flat).Deviation) < 0.05);
            Assert.True(Math.Abs(PitchEffect.Apply(Ball(DeliveryKind.OffSpin, 0.3), Pitch.Flat).Deviation) < 0.25);
            Assert.True(Math.Abs(PitchEffect.Apply(Ball(DeliveryKind.Pace, 0.0), Pitch.Flat).Swing) < 0.1);
        }

        [Fact]
        public void Every_ground_has_a_default_and_all_four_types_are_used()
        {
            Assert.Equal(Pitch.Flat, Venue.Find("seaface")!.DefaultPitch);
            Assert.Equal(Pitch.SpinFriendly, Venue.Find("marina")!.DefaultPitch);
            Assert.Equal(Pitch.SeamFriendly, Venue.Find("shivalik")!.DefaultPitch);
            Assert.Equal(Pitch.Balanced, Venue.Find("lakeside")!.DefaultPitch);
            Assert.Equal(4, Venue.All.Select(v => v.DefaultPitch).Distinct().Count());
            foreach (var p in Enum.GetValues(typeof(Pitch)).Cast<Pitch>()) Assert.False(string.IsNullOrEmpty(PitchEffect.Describe(p).name));
        }

        [Fact]
        public void The_full_match_keeps_the_toss_pitch_and_the_match_plays_on_it()
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            var venue = Venue.Find("marina");
            QuickMatch.NextPitch = Pitch.SeamFriendly;
            var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[0]), QuickMatch.LastSeasonXI(Data, teams[3]), 6, 4242, venue, userBatsFirst: false);
            Assert.Equal(Pitch.SeamFriendly, m.Pitch);
            Assert.Null(QuickMatch.NextPitch);
            // The same seed without the carry: the ground's own pick, and the dice are not moved by it.
            var free = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[0]), QuickMatch.LastSeasonXI(Data, teams[3]), 6, 4242, venue, userBatsFirst: false);
            Assert.Equal(m.NextDelivery().Delivery.BounceZ, free.NextDelivery().Delivery.BounceZ, 9);
        }
    }
}
