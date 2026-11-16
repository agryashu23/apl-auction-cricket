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
    /// <summary>Match mode's laws: no-balls and free hits, the running, DRS, the user's bowling and the AI batter, the full match.</summary>
    public class RulesTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public RulesTests(ITestOutputHelper output) { _out = output; }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static List<string> Teams => Data.Franchises.Select(f => f.Id).ToList();

        private static Trajectory Line(double releaseX, double bounceX, double bounceZ = 5, double deviation = 0) => new Trajectory(new Delivery
        {
            Kind = DeliveryKind.Pace, SpeedKmh = 140, ReleaseX = releaseX, ReleaseY = 2.1, ReleaseZ = 19, BounceX = bounceX, BounceZ = bounceZ, Deviation = deviation,
            Length = Bowling.LengthOf(bounceZ, false), Line = Bowling.LineOf(bounceX),
        });

        // ------------------------------------------------------------ no-balls

        [Fact]
        public void A_no_ball_is_a_run_not_a_ball_and_the_free_hit_can_only_be_a_run_out()
        {
            var noBalls = 0; var freeHits = 0; var reprieves = 0; var outsOnFreeHit = 0;
            for (var g = 0; g < 40; g++)
            {
                var m = new QuickMatch(QuickMatch.LastSeasonXI(Data, Teams[g % 10]), QuickMatch.LastSeasonXI(Data, Teams[(g + 4) % 10]), 5, 300 + g);
                var rng = new Rng(g);
                var wasFree = false;
                while (!m.Finished)
                {
                    var t = m.NextDelivery();
                    // Plenty of no-balls: every eighth ball oversteps.
                    if (rng.Chance(0.12)) { t.Delivery.NoBall = true; t.Delivery.Wide = false; }
                    var balls = m.LegalBalls;
                    var shot = rng.Chance(0.3) ? ShotInput.Leave : new ShotInput(ShotType.Loft, Zone.StraightOn, Batting.IdealTap(t) + Bowling.Normal(rng) * 2 * Batting.Window(m.OnStrike.Player, t.Delivery));
                    var r = m.Play(shot);
                    Assert.Equal(wasFree, r.FreeHit);
                    if (r.NoBall)
                    {
                        noBalls++;
                        Assert.False(r.Legal);
                        Assert.Equal(balls, m.LegalBalls);
                        Assert.True(r.Runs >= 1 && r.Penalty == 1);
                        Assert.True(m.FreeHit || m.Finished);
                    }
                    if (r.FreeHit) freeHits++;
                    if (r.Reprieve != null) reprieves++;
                    if ((r.NoBall || r.FreeHit) && r.Wicket)
                    {
                        Assert.Equal("run out", r.HowOut);
                        outsOnFreeHit++;
                    }
                    wasFree = m.FreeHit;
                }
            }
            _out.WriteLine($"{noBalls} no-balls, {freeHits} free hits, {reprieves} reprieved, {outsOnFreeHit} run out on one");
            Assert.True(noBalls > 50 && freeHits > 40 && reprieves > 3);
        }

        [Fact]
        public void Bowlers_overstep_now_and_then_and_beamers_are_rare()
        {
            var bowlers = Data.Players.Where(p => p.BowlType != BowlType.None && p.Bowling > 0).ToList();
            var rng = new Rng(5);
            int n = 20000, nb = 0, beamers = 0;
            for (var i = 0; i < n; i++)
            {
                var d = Bowling.Bowl(bowlers[i % bowlers.Count], i % 3, rng);
                if (d.NoBall) nb++;
                if (d.Beamer) beamers++;
                if (d.NoBall) Assert.False(d.Wide);
                if (d.Beamer)
                {
                    var t = new Trajectory(d);
                    Assert.InRange(t.At(t.TimeAt(1.0)).y, 0.95, 1.6);
                }
            }
            _out.WriteLine($"no-balls {nb * 100.0 / n:0.00}% (beamers {beamers * 100.0 / n:0.00}%) — {nb * 120.0 / n:0.0} an innings");
            Assert.InRange(nb / (double)n, 0.002, 0.012);
            Assert.InRange(beamers / (double)n, 0.0002, 0.003);
        }

        // ------------------------------------------------------------- running

        [Fact]
        public void Pushing_for_a_run_the_throw_beats_is_a_run_out_the_safe_ones_are_not()
        {
            var rng = new Rng(3);
            var safeOuts = 0; var greedyOuts = 0;
            for (var i = 0; i < 400; i++)
            {
                var plan = new RunPlan { Back = 5.6, ThrowDistance = 20, Safe = 1, DirectHit = 0.3, ToKeeperEnd = true, Fielder = "cover" };
                var safe = new BallResult { Running = plan };
                Running.Settle(safe, 1, rng);
                if (safe.RunOut) safeOuts++;
                var greedy = new BallResult { Running = plan };
                Running.Settle(greedy, 2, rng);
                if (greedy.RunOut)
                {
                    greedyOuts++;
                    // Out going for the second: one completed, the striker running back to the keeper's end.
                    Assert.Equal(1, greedy.Ran);
                    Assert.True(greedy.RunOutStriker);
                    Assert.Equal("run out", greedy.HowOut);
                }
                else Assert.Equal(2, greedy.Ran);
            }
            _out.WriteLine($"safe single run out {safeOuts}/400, pushing for two {greedyOuts}/400; risk read-out 1: {new RunPlan { Back = 5.6, DirectHit = 0.3 }.Risk(1):0.00}, 2: {new RunPlan { Back = 5.6, DirectHit = 0.3 }.Risk(2):0.00}");
            Assert.Equal(0, safeOuts);
            Assert.True(greedyOuts > 380);
            var tight = new RunPlan { Back = RunPlan.ArrivesAt(1) + 0.05, DirectHit = 0.4 };
            Assert.True(tight.Risk(1) > 0.05 && tight.Risk(1) < 0.4, $"a tight single {tight.Risk(1):0.00}");
            Assert.True(tight.Risk(2) > 0.95);
        }

        [Fact]
        public void The_user_can_push_for_more_runs_and_be_run_out_or_stay_for_none()
        {
            var m = new QuickMatch(QuickMatch.LastSeasonXI(Data, "BLR"), QuickMatch.LastSeasonXI(Data, "MBH"), 20, 11);
            var rng = new Rng(8);
            int runOuts = 0, stays = 0, tries = 0;
            while (!m.Finished && tries < 300)
            {
                var t = m.NextDelivery();
                var r = m.Play(new ShotInput(ShotType.Push, (Zone)rng.NextInt(8), Batting.IdealTap(t)));
                if (r.Running == null || r.Wicket) continue;
                tries++;
                var before = (m.Runs - r.Runs, m.Wickets);
                if (tries % 2 == 0)
                {
                    var redo = m.SettleRuns(r.Running.Safe + 2)!;
                    if (redo.RunOut)
                    {
                        runOuts++;
                        Assert.Equal(before.Wickets + 1, m.Wickets);
                        Assert.NotNull(redo.OutBatter);
                    }
                }
                else
                {
                    var redo = m.SettleRuns(0)!;
                    stays++;
                    Assert.Equal(redo.Penalty, redo.Runs);
                    Assert.Equal(before.Item1 + redo.Penalty, m.Runs);
                    Assert.Equal(before.Wickets, m.Wickets);
                }
            }
            _out.WriteLine($"{tries} balls to run for: {runOuts} run out pushing for two more, {stays} stayed");
            Assert.True(runOuts > tries / 4);
        }

        // ---------------------------------------------------------------- DRS

        [Fact]
        public void Ball_tracking_reads_pitching_impact_and_wickets()
        {
            var plumb = BallTracking.Track(Line(0, 0), 1.6);
            Assert.Equal(PitchVerdict.InLine, plumb.Pitching);
            Assert.Equal(ImpactVerdict.InLine, plumb.Impact);
            Assert.Equal(WicketsVerdict.Hitting, plumb.Wickets);
            Assert.True(plumb.ClearlyOut(true));
            Assert.NotEmpty(plumb.Path);
            Assert.NotEmpty(plumb.Projected);

            // Pitched outside leg and turned back in: hitting, but not out.
            var legSide = BallTracking.Track(Line(-0.3, -0.3, 5, 0.25), 1.6);
            Assert.Equal(PitchVerdict.OutsideLeg, legSide.Pitching);
            Assert.True(legSide.ClearlyNotOut(true));

            // Clipping the outside of off stump: umpire's call on impact and wickets.
            var marginal = BallTracking.Track(Line(0.13, 0.13), 1.6);
            Assert.Equal(ImpactVerdict.UmpiresCall, marginal.Impact);
            Assert.Equal(WicketsVerdict.UmpiresCall, marginal.Wickets);

            // Well outside off: struck outside the line playing a shot, and missing.
            var wide = BallTracking.Track(Line(0.25, 0.25), 1.6);
            Assert.Equal(ImpactVerdict.OutsideOff, wide.Impact);
            Assert.Equal(WicketsVerdict.Missing, wide.Wickets);
            Assert.True(wide.ClearlyNotOut(true));
        }

        [Fact]
        public void Reviews_overturn_only_clear_errors_and_umpires_call_keeps_the_review()
        {
            LbwCase Case(Trajectory t, bool givenOut) => new LbwCase { Tracking = BallTracking.Track(t, 1.6), ShotOffered = true, OnFieldOut = givenOut, Appealed = true };

            var c = Case(Line(-0.3, -0.3, 5, 0.25), true);
            c.Review(true);
            Assert.True(c.Overturned && !c.Out && !c.ReviewLost);

            c = Case(Line(0, 0), false);
            c.Review(false);
            Assert.True(c.Overturned && c.Out);

            c = Case(Line(0, 0), true);
            c.Review(true);
            Assert.True(!c.Overturned && c.Out && c.ReviewLost);

            c = Case(Line(0.13, 0.13), true);
            c.Review(true);
            Assert.True(!c.Overturned && c.Out && c.UmpiresCall && !c.ReviewLost);

            c = Case(Line(0.13, 0.13), false);
            c.Review(false);
            Assert.True(!c.Overturned && !c.Out && c.UmpiresCall && !c.ReviewLost);

            c = Case(Line(0.25, 0.25), false);
            c.Review(false);
            Assert.True(!c.Overturned && !c.Out && c.ReviewLost);
        }

        [Fact]
        public void The_user_has_one_review_an_innings_and_it_changes_the_book()
        {
            int reviewed = 0, overturned = 0;
            for (var g = 0; g < 60 && reviewed < 12; g++)
            {
                var m = new QuickMatch(QuickMatch.LastSeasonXI(Data, Teams[g % 10]), QuickMatch.LastSeasonXI(Data, Teams[(g + 2) % 10]), 20, 900 + g);
                var rng = new Rng(g);
                var used = 0;
                while (!m.Finished)
                {
                    var t = m.NextDelivery();
                    var r = m.Play(new ShotInput(ShotType.Stroke, Zone.Cover, Batting.IdealTap(t) + Bowling.Normal(rng) * 2.5 * Batting.Window(m.OnStrike.Player, t.Delivery)));
                    if (!m.CanReview()) continue;
                    Assert.True(r.Wicket && r.HowOut == "lbw");
                    var wickets = m.Wickets;
                    m.Review();
                    used++;
                    reviewed++;
                    Assert.True(r.Lbw!.Reviewed && r.Lbw.ReviewedByBatting);
                    if (r.Lbw.Overturned)
                    {
                        overturned++;
                        Assert.False(r.Wicket);
                        Assert.Equal(wickets - 1, m.Wickets);
                    }
                    Assert.Equal(r.Lbw.ReviewLost ? 0 : 1, m.BattingReviews);
                }
                Assert.True(m.BattingReviews >= 0);
            }
            _out.WriteLine($"{reviewed} reviews, {overturned} overturned");
            Assert.True(reviewed >= 5);
        }

        // ------------------------------------------------------- the user bowls

        /// <summary>The user bowling: the best bowler allowed each over, mostly stock balls on the default marks, the release meter off by <paramref name="release"/> σ.</summary>
        private static (double rpo, double ballsPerWicket, string outs) BowlingInnings(double release, int games, bool aiChases)
        {
            int runs = 0, balls = 0, wickets = 0;
            var how = new Dictionary<string, int>();
            for (var g = 0; g < games; g++)
            {
                var user = QuickMatch.LastSeasonXI(Data, Teams[g % 10]);
                var m = QuickMatch.Full(user, QuickMatch.LastSeasonXI(Data, Teams[(g + 3) % 10]), 20, 500 + g, null, userBatsFirst: aiChases);
                var rng = new Rng(9 + g);
                if (aiChases)
                {
                    while (!m.Finished)
                    {
                        var t = m.NextDelivery();
                        var type = rng.Chance(0.3) ? ShotType.Loft : rng.Chance(0.3) ? ShotType.Push : ShotType.Stroke;
                        var zone = Enum.GetValues(typeof(Zone)).Cast<Zone>().OrderByDescending(z => Batting.Suitability(t.Delivery, type, z)).First();
                        m.Play(new ShotInput(type, zone, Batting.IdealTap(t) + Bowling.Normal(rng) * 0.8 * Batting.Window(m.OnStrike.Player, t.Delivery)));
                    }
                    m.StartSecondInnings();
                }
                Assert.False(m.UserBatting);
                var lastOver = -1;
                while (!m.Finished)
                {
                    if (m.Over != lastOver)
                    {
                        lastOver = m.Over;
                        var pick = m.BowlerOptions().Where(o => o.Allowed).OrderByDescending(o => o.Player.Bowling).First();
                        Assert.True(m.SetBowler(pick.Player));
                        m.Preset = m.Phase == 2 ? FieldPreset.Death : FieldPreset.Balanced;
                    }
                    var b = m.CurrentBowler;
                    var vars = Bowling.Variations(b);
                    var v = rng.Chance(0.55) ? Variation.Stock : vars[rng.NextInt(vars.Count)];
                    var (x, z) = Bowling.DefaultMark(b, v);
                    m.Bowl(v, x + Bowling.Normal(rng) * 0.05, z + Bowling.Normal(rng) * 0.4, Bowling.Normal(rng) * release);
                    var r = m.PlayAi();
                    if (r.Legal) balls++;
                    runs += r.Runs;
                    if (!r.Wicket) continue;
                    wickets++;
                    var k = r.HowOut + "/" + r.Shot.Type;
                    how[k] = how.TryGetValue(k, out var c) ? c + 1 : 1;
                }
                // Nobody over his quota, nobody twice running.
                var byOver = m.Log.Where(r => r.Legal).Select((r, i) => i / 6).Distinct().Count();
                Assert.True(byOver <= 20);
                foreach (var f in m.BowlingFigures) Assert.True(f.Balls <= m.MaxOversPerBowler * 6, $"{f.Player.Name} bowled {f.Overs}");
            }
            return (runs * 6.0 / balls, balls / (double)Math.Max(1, wickets), string.Join(", ", how.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key} {kv.Value}")));
        }

        [Fact]
        public void The_AI_batter_scores_like_a_t20_side_against_the_users_bowling()
        {
            // 200 innings, not 60: at 60 the balls-per-wicket figure moved +-0.9 (one standard error)
            // with any change to the player data and sat on the old bound of 18. Target ~18-20 balls a
            // wicket (IPL 2026 par 20.0, other T20s 17.6, docs/RATINGS.md); the bound is two standard
            // errors (~0.5 each at 200 innings) under 18.
            var tidy = BowlingInnings(0.4, 200, false);
            var wild = BowlingInnings(1.6, 60, false);
            var chase = BowlingInnings(0.6, 40, true);
            _out.WriteLine($"tidy release: {tidy.rpo:0.00} rpo, wicket every {tidy.ballsPerWicket:0.0} | {tidy.outs}");
            _out.WriteLine($"wild release: {wild.rpo:0.00} rpo, wicket every {wild.ballsPerWicket:0.0} | {wild.outs}");
            _out.WriteLine($"AI chasing:   {chase.rpo:0.00} rpo, wicket every {chase.ballsPerWicket:0.0} | {chase.outs}");
            Assert.InRange(tidy.rpo, 7, 11.5);
            Assert.InRange(tidy.ballsPerWicket, 17, 45);
            Assert.True(wild.rpo > tidy.rpo, "a wayward release goes for more");
        }

        [Fact]
        public void The_AI_bats_harder_as_the_rate_climbs_and_flat_out_on_a_free_hit()
        {
            double A(int need, int balls, int wickets = 3, bool free = false) => BatterBrain.Aggression(new Situation(100, wickets, balls, 20, 100 + need, 10, free, 1));
            Assert.True(A(30, 60) < A(60, 60));
            Assert.True(A(60, 60) < A(90, 60));
            Assert.True(A(60, 60, 8) < A(60, 60, 3));
            Assert.Equal(1, A(10, 60, 3, true));
        }

        [Fact]
        public void A_full_match_swaps_sides_at_the_break_and_the_chase_decides_it()
        {
            for (var g = 0; g < 10; g++)
            {
                var user = QuickMatch.LastSeasonXI(Data, Teams[g % 10]);
                var opp = QuickMatch.LastSeasonXI(Data, Teams[(g + 1) % 10]);
                var m = QuickMatch.Full(user, opp, 5, 70 + g, null, userBatsFirst: g % 2 == 0);
                Assert.Equal(g % 2 == 0, m.UserBatting);
                Assert.False(m.Chasing);
                Assert.Equal(0, m.Target);
                while (!m.Finished) m.PlayAi();
                Assert.False(m.MatchOver);
                var first = m.Runs;
                var firstBat = m.Batting;
                m.StartSecondInnings();
                Assert.Equal(first + 1, m.Target);
                Assert.Equal(first, m.FirstInnings.Runs);
                Assert.NotSame(firstBat, m.Batting);
                Assert.Equal(g % 2 != 0, m.UserBatting);
                Assert.Equal(0, m.LegalBalls);
                while (!m.Finished) m.PlayAi();
                Assert.True(m.MatchOver);
                Assert.NotEqual("", m.MatchResultText());
                if (m.Runs > first) Assert.Same(m.Batting, m.Winner);
                else if (m.Runs < first) Assert.Same(m.Bowling, m.Winner);
                _out.WriteLine($"{first} v {m.Runs}/{m.Wickets}: {m.MatchResultText()} (user won: {m.UserWon})");
            }
        }

        [Fact]
        public void The_user_picks_the_bowler_within_the_laws()
        {
            var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, "BLR"), QuickMatch.LastSeasonXI(Data, "MBH"), 5, 1, null, userBatsFirst: false);
            Assert.Equal(1, m.MaxOversPerBowler);
            var first = m.BowlerOptions().First(o => o.Allowed).Player;
            Assert.True(m.SetBowler(first));
            Assert.Same(first, m.CurrentBowler);
            m.PlayAi();
            Assert.False(m.SetBowler(first));
            while (m.Over == 0) m.PlayAi();
            var options = m.BowlerOptions();
            Assert.False(options.First(o => o.Player == first).Allowed);
            Assert.True(options.First(o => o.Player == first).BowledLast);
            Assert.False(m.SetBowler(first));
            var second = options.First(o => o.Allowed).Player;
            Assert.True(m.SetBowler(second));
            Assert.Same(second, m.CurrentBowler);
        }

        // -------------------------------------------------------------- fields

        [Fact]
        public void Field_presets_keep_the_powerplay_rule_and_nine_men()
        {
            foreach (FieldPreset p in Enum.GetValues(typeof(FieldPreset)))
            {
                foreach (var phase in new[] { 0, 1, 2 })
                {
                    foreach (var spin in new[] { false, true })
                    {
                        var f = Field.For(phase, spin, null, false, p);
                        Assert.Equal(9, f.Fielders.Count);
                        Assert.True(f.Fielders.Count(Field.OutsideRing) <= Field.MaxOutside(phase), $"{p} phase {phase}");
                        foreach (var v in Venue.All)
                        {
                            foreach (var x in Field.For(phase, spin, v, true, p).Fielders) Assert.False(v.OverFromBatter(x.Position.x, x.Position.f, true));
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------- bowlers

        [Fact]
        public void A_quarter_bowl_left_arm_and_the_release_and_turn_mirror()
        {
            var bowlers = Data.Players.Where(p => p.BowlType != BowlType.None).ToList();
            var share = bowlers.Count(Bowling.LeftArm) / (double)bowlers.Count;
            _out.WriteLine($"left-arm: {share:P0} of {bowlers.Count}");
            Assert.InRange(share, 0.17, 0.33);
            Assert.True(Bowling.ReleaseX(false, false, false, false) > 0);
            Assert.True(Bowling.ReleaseX(false, true, false, false) < 0);
            Assert.True(Bowling.ReleaseX(false, false, true, false) < 0);
            Assert.True(Bowling.ReleaseX(false, false, false, true) < 0);
            // Off-break into the right-hander, left-arm orthodox away from him, the other way for a left-hander.
            Assert.True(Bowling.TurnSign(DeliveryKind.OffSpin, false, false) < 0);
            Assert.True(Bowling.TurnSign(DeliveryKind.OffSpin, true, false) > 0);
            Assert.True(Bowling.TurnSign(DeliveryKind.OffSpin, false, true) > 0);
            Assert.True(Bowling.TurnSign(DeliveryKind.LegSpin, false, false) > 0);
            // Round the wicket more often to the batter he angles in on.
            Assert.True(Bowling.AroundOdds(DeliveryKind.Pace, false, true) > Bowling.AroundOdds(DeliveryKind.Pace, false, false));
            // A googly turns the other way.
            var leggie = bowlers.First(p => Bowling.KindFor(p) == DeliveryKind.LegSpin && !Bowling.LeftArm(p));
            var rng = new Rng(2);
            for (var i = 0; i < 20; i++)
            {
                Assert.True(Bowling.Deliver(leggie, Variation.Stock, 0.1, 4, 0, 1, rng).Deviation > 0);
                Assert.True(Bowling.Deliver(leggie, Variation.Wrong, 0.1, 4, 0, 1, rng).Deviation < 0);
            }
        }

        [Fact]
        public void A_bad_release_scatters_the_ball_and_takes_pace_off()
        {
            // Pace coming off is about a fast bowler.
            var b = TestBowlers.TopFastBowler(Data);
            double Spread(double release)
            {
                var rng = new Rng(4);
                var zs = Enumerable.Range(0, 400).Select(_ => Bowling.Deliver(b, Variation.Stock, 0.2, 5.5, release * (rng.Chance(0.5) ? 1 : -1), 1, rng).BounceZ).ToList();
                var mean = zs.Average();
                return Math.Sqrt(zs.Average(z => (z - mean) * (z - mean)));
            }
            var good = Spread(0);
            var bad = Spread(2);
            _out.WriteLine($"pitch-point spread: perfect release {good:0.00} m, bad {bad:0.00} m");
            Assert.True(bad > good * 2);
            var r = new Rng(1);
            Assert.True(Enumerable.Range(0, 100).Average(_ => Bowling.Deliver(b, Variation.Stock, 0.2, 5.5, 0, 1, r).SpeedKmh)
                > Enumerable.Range(0, 100).Average(_ => Bowling.Deliver(b, Variation.Stock, 0.2, 5.5, 2, 1, r).SpeedKmh) + 3);
        }
    }
}
