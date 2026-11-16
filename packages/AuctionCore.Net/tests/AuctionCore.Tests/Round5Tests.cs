using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;
using Xunit.Abstractions;

namespace AuctionCore.Tests
{
    /// <summary>Ball types are types; yorkers and bouncers come from the length.</summary>
    public class Round5Tests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(Round4Tests.Root(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public Round5Tests(ITestOutputHelper output) { _out = output; }

        [Fact]
        public void The_ball_types_are_types_not_lengths()
        {
            foreach (var p in Data.Players.Where(p => p.BowlType != BowlType.None))
            {
                var types = Bowling.Variations(p);
                Assert.DoesNotContain(Variation.Yorker, types);
                Assert.DoesNotContain(Variation.Bouncer, types);
                Assert.Equal(Variation.Stock, types[0]);
            }
            var finger = Data.Players.First(p => Bowling.KindFor(p) == DeliveryKind.OffSpin && !Bowling.LeftArm(p));
            // Left-arm orthodox (round 9): no doosra or carrom ball, and the card and chips agree on the style.
            var sla = Data.Players.FirstOrDefault(p => Bowling.KindFor(p) == DeliveryKind.OffSpin && Bowling.LeftArm(p));
            if (sla != null)
            {
                Assert.DoesNotContain(Variation.Wrong, Bowling.Variations(sla));
                Assert.DoesNotContain(Variation.Carrom, Bowling.Variations(sla));
                Assert.Equal("Left-arm orthodox", Bowling.StyleName(sla));
                Assert.Equal("Flighted", Bowling.VariationName(sla, Variation.Slower));
            }
            var wrist = Data.Players.First(p => Bowling.KindFor(p) == DeliveryKind.LegSpin);
            Assert.Equal("Off-break", Bowling.VariationName(finger, Variation.Stock));
            Assert.Equal("Leg-break", Bowling.VariationName(wrist, Variation.Stock));
            Assert.Equal("Doosra", Bowling.VariationName(finger, Variation.Wrong));
            Assert.Equal("Googly", Bowling.VariationName(wrist, Variation.Wrong));
            // The AI never picks a length as a type either.
            var quick = TestBowlers.TopPacer(Data);
            var rng = new Rng(4);
            Assert.All(Enumerable.Range(0, 600).Select(i => Bowling.Bowl(quick, i % 3, rng)), d => Assert.True(d.Variation != Variation.Yorker && d.Variation != Variation.Bouncer));
        }

        [Fact]
        public void A_bouncer_is_the_length_quicker_it_climbs_and_over_head_height_it_is_a_no_ball()
        {
            // A genuinely fast bowler: a medium-pacer's style caps his pace class (BowlerPace.PaceOf).
            var quick = TestBowlers.TopFastBowler(Data);
            double Speed(double z)
            {
                var rng = new Rng(2);
                return Enumerable.Range(0, 300).Average(_ => Bowling.Deliver(quick, Variation.Stock, 0.2, z, 0, 1, rng).SpeedKmh);
            }
            Assert.True(Speed(10.5) > Speed(5.5) + 2);
            Assert.True(Speed(0.9) < Speed(5.5) - 1);
            var r = new Rng(5);
            var bouncers = Enumerable.Range(0, 400).Select(_ => Bowling.Deliver(quick, Variation.Stock, 0.2, 10.5, 0, 1, r)).Where(d => d.Length == Length.Bouncer).ToList();
            Assert.All(bouncers, d => Assert.True(d.BounceFactor > 1.1));
            var headHigh = Enumerable.Range(0, 400).Select(_ => Bowling.Deliver(quick, Variation.Stock, 0.2, 11.8, 0, 1, r)).Count(d => d.HeadHigh);
            var chest = Enumerable.Range(0, 400).Select(_ => Bowling.Deliver(quick, Variation.Stock, 0.2, 9.8, 0, 1, r)).Count(d => d.HeadHigh);
            _out.WriteLine($"over head height: {headHigh}/400 at 11.8 m, {chest}/400 at 9.8 m");
            Assert.True(headHigh > 20 && chest < 5);
            var aiHead = Enumerable.Range(0, 4000).Select(i => Bowling.Bowl(quick, 1, r)).Count(d => d.HeadHigh);
            Assert.True(aiHead < 40, $"AI head-high no-balls {aiHead}/4000");
            Assert.All(Enumerable.Range(0, 200).Select(_ => Bowling.Deliver(quick, Variation.Stock, 0.2, 11.8, 0, 1, r)).Where(d => d.HeadHigh), d => Assert.True(d.NoBall && !d.Wide));
        }
    }
}
