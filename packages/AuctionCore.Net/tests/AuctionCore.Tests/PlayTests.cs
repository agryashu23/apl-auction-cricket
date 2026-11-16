using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;
using Xunit.Abstractions;

namespace AuctionCore.Tests
{
    /// <summary>Match mode's ball engine: flight, bowling, batting outcomes, and scoring balance.</summary>
    public class PlayTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public PlayTests(ITestOutputHelper output) { _out = output; }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static Delivery Straight(double bounceZ, double atX = 0) => new Delivery
        {
            Kind = DeliveryKind.Pace, SpeedKmh = 140, ReleaseX = atX, ReleaseY = 2.1, ReleaseZ = 19, BounceX = atX, BounceZ = bounceZ,
            Length = Bowling.LengthOf(bounceZ, false), Line = Bowling.LineOf(atX),
        };

        [Fact]
        public void Flight_bounces_where_aimed_and_arrives_later_closer_in()
        {
            var t = new Trajectory(Straight(5));
            var bounce = t.At(t.BounceTime);
            Assert.InRange(bounce.y, 0, 0.01);
            Assert.InRange(bounce.z, 4.99, 5.01);
            Assert.True(t.TimeAt(1) > t.TimeAt(3));
            // 140 km/h over ~18 m: about half a second.
            Assert.InRange(t.TimeAt(0), 0.4, 0.6);
        }

        [Fact]
        public void A_straight_full_ball_hits_the_stumps_a_wide_one_does_not()
        {
            Assert.True(new Trajectory(Straight(2.5)).HittingStumps());
            Assert.False(new Trajectory(Straight(2.5, 0.6)).HittingStumps());
        }

        [Fact]
        public void Good_bowlers_are_accurate_poor_ones_spray_it()
        {
            Player Bowler(int skill) => Data.Players.Where(p => p.BowlType == BowlType.Pace).OrderBy(p => Math.Abs(p.Bowling - skill)).First();
            double WideRate(Player b)
            {
                var rng = new Rng(7);
                return Enumerable.Range(0, 2000).Count(_ => Bowling.Bowl(b, 1, rng).Wide) / 2000.0;
            }
            var good = WideRate(Bowler(90));
            var poor = WideRate(Bowler(45));
            _out.WriteLine($"wides: good {good:P1}, poor {poor:P1}");
            Assert.True(good < 0.06, $"good bowler wides {good:P1}");
            Assert.True(poor > good);
        }

        [Fact]
        public void Perfect_timing_on_the_right_shot_beats_a_late_slog()
        {
            var batter = Data.Players.OrderByDescending(p => p.Batting).First();
            var bowler = Data.Players.Where(p => p.BowlType == BowlType.Pace).OrderBy(p => p.Bowling).First();
            var d = Straight(3);
            var t = new Trajectory(d);
            var field = Field.For(1, false);
            var perfect = Batting.Resolve(batter, bowler, d, t, new ShotInput(ShotType.Stroke, Zone.StraightOff, Batting.IdealTap(t)), field, new Rng(1));
            var late = Batting.Resolve(batter, bowler, d, t, new ShotInput(ShotType.Loft, Zone.Point, Batting.IdealTap(t) + 0.12), field, new Rng(1));
            Assert.Equal(Timing.Perfect, perfect.Timing);
            Assert.True(perfect.Quality > late.Quality);
        }

        [Fact]
        public void Leaving_a_ball_on_the_stumps_is_out_leaving_a_wide_is_a_wide()
        {
            var batter = Data.Players.First();
            var bowler = Data.Players.Last();
            var field = Field.For(0, false);
            var onStumps = Straight(2.5);
            Assert.True(Batting.Resolve(batter, bowler, onStumps, new Trajectory(onStumps), ShotInput.Leave, field, new Rng(1)).Wicket);
            var wide = Straight(5, 1.1);
            wide.Wide = true;
            var r = Batting.Resolve(batter, bowler, wide, new Trajectory(wide), ShotInput.Leave, field, new Rng(1));
            Assert.True(r.Wide);
            Assert.Equal(1, r.Runs);
        }

        [Fact]
        public void Quick_match_uses_last_seasons_squads_and_a_simulated_target()
        {
            var m = new QuickMatch(QuickMatch.LastSeasonXI(Data, "BLR"), QuickMatch.LastSeasonXI(Data, "MBH"), 5, 42);
            Assert.Equal(11, m.Batting.XI.Count);
            Assert.All(m.Batting.XI, p => Assert.Equal("BLR", p.PreviousTeam));
            Assert.InRange(m.Target, 20, 110);
            Assert.Equal(30, m.BallsLeft);
        }

        /// <summary>
        /// A bot batter: reads the ball (best shot for it), lofts when the rate
        /// demands it, leaves wide ones, and taps with a timing error of
        /// <paramref name="sigma"/> windows (0.6 = good player, 2.5 = random).
        /// </summary>
        private static (double rpo, double ballsPerWicket, double winRate, string outs) Simulate(double sigma, int overs, int games, Venue? venue = null)
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            var runs = 0; var wickets = 0; var balls = 0; var wins = 0;
            var how = new System.Collections.Generic.Dictionary<string, int>();
            var targets = 0;
            for (var g = 0; g < games; g++)
            {
                var m = new QuickMatch(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), overs, 1000 + g, venue);
                var rng = new Rng(77 + g);
                while (!m.Finished)
                {
                    var t = m.NextDelivery();
                    var d = t.Delivery;
                    ShotInput shot;
                    if ((d.Line == Line.WideOff || d.Line == Line.OutsideOff) && !t.HittingStumps() && rng.Chance(0.5))
                    {
                        shot = ShotInput.Leave;
                    }
                    else
                    {
                        var rate = m.BallsLeft == 0 ? 99 : m.RunsNeeded * 6.0 / m.BallsLeft;
                        var type = rate > 10 || (rate > 7.5 && rng.Chance(0.4)) ? ShotType.Loft : rng.Chance(0.3) ? ShotType.Push : ShotType.Stroke;
                        var best = Enum.GetValues(typeof(Zone)).Cast<Zone>().OrderByDescending(z => Batting.Suitability(d, type, z)).First();
                        var zone = rng.Chance(0.8) ? best : (Zone)rng.NextInt(8);
                        var window = Batting.Window(m.OnStrike.Player, d);
                        shot = new ShotInput(type, zone, Batting.IdealTap(t) + Bowling.Normal(rng) * sigma * window);
                    }
                    var r = m.Play(shot);
                    if (r.Legal) balls++;
                    runs += r.Runs;
                    if (r.Wicket)
                    {
                        wickets++;
                        var k = r.HowOut + (r.Shot.IsLeave ? "/leave" : "/" + r.Shot.Type) + (r.Edge ? "/edge" : "");
                        how[k] = how.TryGetValue(k, out var c) ? c + 1 : 1;
                    }
                }
                targets += m.Target;
                if (m.Won) wins++;
            }
            var outs = string.Join(", ", how.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")) + $"; avg target {targets / games}";
            return (runs * 6.0 / balls, balls / (double)Math.Max(1, wickets), wins / (double)games, outs);
        }

        [Fact]
        public void Venue_ropes_match_the_specs_and_fielders_stay_inside()
        {
            foreach (var v in Venue.All)
            {
                Assert.InRange(v.FromBatter(0, false), v.Straight + Venue.HalfPitch - 0.1, v.Straight + Venue.HalfPitch + 0.1);
                Assert.InRange(v.FromBatter(180, false), v.Behind - Venue.HalfPitch - 0.1, v.Behind - Venue.HalfPitch + 0.1);
                Assert.InRange(v.RopeFromCentre(1, 0), v.Off - 0.01, v.Off + 0.01);
                Assert.InRange(v.RopeFromCentre(-1, 0), v.Leg - 0.01, v.Leg + 0.01);
                foreach (var lh in new[] { false, true })
                {
                    foreach (var phase in new[] { 0, 1 })
                    {
                        foreach (var f in Field.For(phase, false, v, lh).Fielders)
                        {
                            Assert.False(v.OverFromBatter(f.Position.x, f.Position.f, lh), $"{v.Id} {f.Name} outside the rope");
                        }
                    }
                }
            }
        }

        [Fact]
        public void Small_grounds_score_more_than_big_ones()
        {
            var small = Simulate(0.6, 20, 60, Venue.Find("gardencity"));
            var big = Simulate(0.6, 20, 60, Venue.Find("lakeside"));
            _out.WriteLine($"Garden City: {small.rpo:0.00} rpo, wins {small.winRate:P0} | {small.outs}");
            _out.WriteLine($"Lakeside:    {big.rpo:0.00} rpo, wins {big.winRate:P0} | {big.outs}");
            foreach (var v in Venue.All)
            {
                var r = Simulate(0.6, 20, 30, v);
                _out.WriteLine($"{v.Name,-22} x{v.SizeFactor:0.00} {r.rpo:0.00} rpo, wins {r.winRate:P0}, {r.outs.Substring(r.outs.LastIndexOf(';') + 2)}");
            }
            Assert.True(small.rpo > big.rpo + 0.3, $"small {small.rpo:0.00} vs big {big.rpo:0.00}");
        }

        [Fact]
        public void Balance_good_timing_scores_like_a_t20_side_random_timing_does_not()
        {
            var good = Simulate(0.6, 20, 60);
            var average = Simulate(1.2, 20, 60);
            var random = Simulate(3.0, 20, 60);
            _out.WriteLine($"good:    {good.rpo:0.00} rpo, wicket every {good.ballsPerWicket:0.0} balls, wins {good.winRate:P0} | {good.outs}");
            _out.WriteLine($"average: {average.rpo:0.00} rpo, wicket every {average.ballsPerWicket:0.0} balls, wins {average.winRate:P0} | {average.outs}");
            _out.WriteLine($"random:  {random.rpo:0.00} rpo, wicket every {random.ballsPerWicket:0.0} balls, wins {random.winRate:P0}");
            var shortGood = Simulate(0.6, 5, 200);
            var shortAverage = Simulate(1.2, 5, 200);
            _out.WriteLine($"5 overs good:    {shortGood.rpo:0.00} rpo, wicket every {shortGood.ballsPerWicket:0.0} balls, wins {shortGood.winRate:P0} | {shortGood.outs}");
            _out.WriteLine($"5 overs average: {shortAverage.rpo:0.00} rpo, wicket every {shortAverage.ballsPerWicket:0.0} balls, wins {shortAverage.winRate:P0} | {shortAverage.outs}");
            Assert.InRange(good.rpo, 7.5, 11.5);
            Assert.InRange(good.ballsPerWicket, 12, 60);
            Assert.True(average.rpo < good.rpo);
            Assert.True(random.rpo < average.rpo);
            Assert.True(random.ballsPerWicket < good.ballsPerWicket);
        }
    }
}
