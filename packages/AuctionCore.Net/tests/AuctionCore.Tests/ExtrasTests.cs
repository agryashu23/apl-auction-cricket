using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace AuctionCore.Tests
{
    /// <summary>
    /// Owner v26 #18: wides, no-balls, free hits and the bouncer limit (two an over in the T20 league,
    /// the third a no-ball) — for the user's bowling and the AI's — and the AI batter leaving clear wides.
    /// </summary>
    public class ExtrasTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;
        public ExtrasTests(ITestOutputHelper output) { _out = output; }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static QuickMatch UserBowls(int g, int overs = 20)
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            return QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), overs, 777 + g, null, userBatsFirst: false);
        }

        /// <summary>At the start of an over, a pace bowler for it (the user's pick).</summary>
        private static void PickPace(QuickMatch m)
        {
            if (m.BallInOver != 0) return;
            var opt = m.BowlerOptions().FirstOrDefault(o => o.Allowed && o.Player.BowlType != BowlType.Spin);
            if (opt != null) m.SetBowler(opt.Player);
        }

        [Fact]
        public void Bouncer_counts_only_pace_balls_that_pitch_on_the_bouncer_length()
        {
            Assert.True(Bowling.CountsAsBouncer(new Delivery { Kind = DeliveryKind.Pace, Length = Length.Bouncer, BounceZ = 10 }));
            Assert.False(Bowling.CountsAsBouncer(new Delivery { Kind = DeliveryKind.LegSpin, Length = Length.Bouncer, BounceZ = 10 }));
            Assert.False(Bowling.CountsAsBouncer(new Delivery { Kind = DeliveryKind.Pace, Length = Length.Short, BounceZ = 8 }));
            Assert.False(Bowling.CountsAsBouncer(new Delivery { Kind = DeliveryKind.Pace, Length = Length.Bouncer, BounceZ = 10, HeadHigh = true }));
            Assert.False(Bowling.CountsAsBouncer(new Delivery { Kind = DeliveryKind.Pace, Length = Length.Bouncer, BounceZ = 0.1 }));
        }

        [Fact]
        public void The_third_bouncer_of_an_over_is_a_no_ball_and_the_next_ball_a_free_hit()
        {
            var m = UserBowls(1);
            int thirds = 0, counted = 0;
            var lastOver = -1;
            var bouncersInOver = 0;
            for (var i = 0; i < 60 && !m.Finished; i++)
            {
                PickPace(m);
                if (m.Over != lastOver)
                {
                    lastOver = m.Over;
                    bouncersInOver = 0;
                }
                Assert.Equal(bouncersInOver, m.BouncersThisOver);
                // Bang it in (three tries an over: the third that counts is over the limit), else a good length.
                var t = m.Bowl(Variation.Stock, 0.15, m.BouncersThisOver < 3 ? 10.6 : 5.5, 0);
                var d = t.Delivery;
                var r = m.PlayAi();
                if (!Bowling.CountsAsBouncer(d))
                {
                    Assert.Equal(0, r.BouncerNumber);
                    continue;
                }
                counted++;
                bouncersInOver++;
                Assert.Equal(bouncersInOver, r.BouncerNumber);
                if (r.BouncerNumber > QuickMatch.BouncersPerOver)
                {
                    thirds++;
                    Assert.True(r.NoBall, "a third bouncer is called a no-ball");
                    Assert.True(d.BouncerNoBall);
                    Assert.False(r.Legal);
                    Assert.False(r.Wicket && r.HowOut != "run out");
                    if (!m.Finished) Assert.True(m.FreeHit, "a free hit follows the bouncer no-ball");
                }
                else Assert.False(d.BouncerNoBall);
            }
            _out.WriteLine($"{counted} bouncers, {thirds} over the limit");
            Assert.True(counted >= 10);
            Assert.True(thirds >= 3);
        }

        [Fact]
        public void The_AI_bowler_mostly_keeps_to_two_bouncers_an_over()
        {
            int overLimit = 0, bouncers = 0, balls = 0;
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            for (var g = 0; g < 20; g++)
            {
                // The user's side bats: the AI bowls.
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), 20, 900 + g, null, userBatsFirst: true);
                while (!m.Finished)
                {
                    var r = m.PlayAi();
                    balls++;
                    if (r.BouncerNumber > 0) bouncers++;
                    if (r.BouncerNumber > QuickMatch.BouncersPerOver)
                    {
                        overLimit++;
                        Assert.True(r.NoBall);
                    }
                }
            }
            _out.WriteLine($"AI: {bouncers} bouncers in {balls} balls, {overLimit} over the limit");
            Assert.True(bouncers > 0);
            Assert.True(overLimit <= bouncers / 10 + 1);
        }

        [Fact]
        public void The_AI_batter_leaves_a_clear_wide_and_it_is_called_and_bowled_again()
        {
            var m = UserBowls(2);
            var wides = 0;
            for (var i = 0; i < 40 && !m.Finished; i++)
            {
                var balls = m.LegalBalls;
                var runs = m.Runs;
                // Way outside off, a perfect release: past the wide guideline.
                var d = m.Bowl(Variation.Stock, 1.1, 5.5, 0).Delivery;
                var shot = m.AiShot();
                if (!d.Wide) { m.Play(shot); continue; }
                Assert.True(shot.IsLeave, "a clear wide is left alone");
                var r = m.Play(shot);
                wides++;
                Assert.True(r.Wide);
                Assert.False(r.Legal);
                Assert.True(r.Runs >= 1);
                Assert.Equal(balls, m.LegalBalls);
                Assert.Equal(runs + r.Runs, m.Runs);
            }
            _out.WriteLine($"{wides} wides called");
            Assert.True(wides >= 10);
        }

        [Fact]
        public void A_red_zone_overstep_is_a_no_ball_worth_a_run_and_a_free_hit()
        {
            var m = UserBowls(3);
            var runs = m.Runs;
            var r = PlayBowl(m, 2.5);
            Assert.True(r.NoBall);
            Assert.True(r.Runs >= 1);
            Assert.Equal(runs + r.Runs, m.Runs);
            Assert.True(m.FreeHit);
        }

        private static BallResult PlayBowl(QuickMatch m, double release)
        {
            m.Bowl(Variation.Stock, 0.2, 5.5, release);
            return m.PlayAi();
        }

        [Fact]
        public void Extras_break_down_into_wides_no_balls_byes_and_leg_byes_that_add_up()
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            int wd = 0, nb = 0;
            for (var g = 0; g < 10; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 5) % 10]), 20, 4000 + g, null, userBatsFirst: true);
                while (!m.Finished) m.PlayAi();
                var (w, n, b, lb) = m.ExtrasBreakdown();
                Assert.Equal(m.Extras, w + n + b + lb);
                Assert.Equal(m.Log.Count(r => r.Wide), m.BowlingFigures.Sum(f => f.Wides));
                Assert.Equal(m.Log.Count(r => r.NoBall), m.BowlingFigures.Sum(f => f.NoBalls));
                Assert.StartsWith($"Extras {m.Extras}", m.ExtrasText());
                var inn = m.Summary();
                Assert.Equal(m.BowlingFigures.Sum(f => f.NoBalls), inn.Bowling.Sum(x => x.NoBalls));
                wd += w;
                nb += n;
            }
            _out.WriteLine($"10 AI-bowled innings: {wd} wides, {nb} no-balls");
            Assert.True(wd > 0, "the AI bowls the odd wide (and the batter leaves it)");
        }

        [Fact]
        public void The_over_s_bouncer_count_survives_a_save_and_resume()
        {
            var m = UserBowls(4);
            PickPace(m);
            for (var i = 0; i < 12 && !m.Finished && m.BouncersThisOver == 0; i++)
            {
                m.Bowl(Variation.Stock, 0.15, 10.6, 0);
                m.PlayAi();
                if (m.BallInOver == 0) PickPace(m);
            }
            Assert.True(m.BouncersThisOver > 0);
            var json = JsonConvert.SerializeObject(m.Save());
            var back = QuickMatch.Restore(JsonConvert.DeserializeObject<MatchSave>(json)!, Data, 99);
            Assert.Equal(m.BouncersThisOver, back.BouncersThisOver);
        }
    }
}
