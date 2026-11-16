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
    /// <summary>
    /// Final gameplay rating (2026-11-14), owner-approved: the scoring shape (17 sixes v 11 fours at 14.3 rpo
    /// in a short match: "approx same sixes and fours"), natural bowled / lbw (7 of 7 wickets were catches:
    /// "why not LBW, it should be"), and the user's bowling containing the AI (a PERFECT / GOOD release costs
    /// the batter). Many seeded innings, played the way the client plays them: the user side's autoplay
    /// batting (MatchDirector.Batting AutoShotType / AutoShotTime) and bowling (MatchDirector.BowlingBall:
    /// varied marks, every variation, release error N(0, 0.5)), and AI v AI.
    /// </summary>
    public class OutcomeMixTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;
        public OutcomeMixTests(ITestOutputHelper output) { _out = output; }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        internal sealed class Tally
        {
            public int Balls, Runs, Fours, Sixes, Dots, Innings, Contacts, Beaten;
            public readonly Dictionary<string, int> How = new Dictionary<string, int>();
            public int Wickets => How.Values.Sum();
            public double Rpo => Runs * 6.0 / Math.Max(1, Balls);
            public double SixFour => Sixes / (double)Math.Max(1, Fours);
            public double Share(params string[] kinds) => kinds.Sum(k => How.TryGetValue(k, out var n) ? n : 0) * 100.0 / Math.Max(1, Wickets);
            public double Caught => Share("caught", "caught behind");

            public void Add(BallResult r)
            {
                Runs += r.Runs;
                if (r.Wicket)
                {
                    var k = r.HowOut ?? "?";
                    How[k] = How.TryGetValue(k, out var n) ? n + 1 : 1;
                }
                if (!r.Legal) return;
                Balls++;
                if (r.Six) Sixes++;
                else if (r.Four) Fours++;
                if (r.Runs == 0) Dots++;
                if (!r.Shot.IsLeave)
                {
                    Contacts++;
                    if (r.ExitSpeed <= 0 || r.Edge) Beaten++;
                }
                if (r.Shot.Type == ShotType.Loft && !r.Shot.IsLeave)
                {
                    var g = r.Timing == Timing.VeryEarly ? Timing.VeryLate : r.Timing == Timing.Early ? Timing.Late : r.Timing;
                    if (!Lofts.TryGetValue(g, out var c)) Lofts[g] = c = new int[4];
                    c[0]++;
                    if (r.Six) c[1]++;
                    else if (r.Four) c[2]++;
                    else if (r.Caught) c[3]++;
                }
            }

            /// <summary>Lofts by timing grade (early folded into late): count, sixes, fours, caught.</summary>
            public readonly Dictionary<Timing, int[]> Lofts = new Dictionary<Timing, int[]>();
            public string LoftLine() => "lofts " + string.Join(", ", Lofts.OrderBy(k => k.Key).Select(kv => $"{kv.Key} {kv.Value[0]}: 6 {kv.Value[1] * 100.0 / kv.Value[0]:0}% 4 {kv.Value[2] * 100.0 / kv.Value[0]:0}% c {kv.Value[3] * 100.0 / kv.Value[0]:0}%"));

            public override string ToString() =>
                $"{Innings} inns, {Balls} balls: 6s {Sixes} 4s {Fours} (6:4 {SixFour:0.00}), {Rpo:0.00} rpo, dots {Dots * 100.0 / Math.Max(1, Balls):0}%, beaten/edged {Beaten * 100.0 / Math.Max(1, Contacts):0}% of shots, " +
                $"{Wickets / (double)Math.Max(1, Innings):0.0} wkts/inns | " + string.Join(", ", How.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value * 100.0 / Math.Max(1, Wickets):0}%"));
        }

        /// <summary>The client's autoplay batter (MatchDirector.Batting.AutoShotType / AutoShotTime, not risky).</summary>
        private static BallResult UserBats(QuickMatch m, Rng rng)
        {
            var t = m.NextDelivery();
            var d = t.Delivery;
            if (d.Wide) return m.Play(ShotInput.Leave);
            var rate = m.BallsLeft == 0 ? 99 : m.RunsNeeded * 6.0 / m.BallsLeft;
            var inArc = d.Length == Length.Full || d.FullToss || (!d.IsSpin && d.Length >= Length.Short);
            var type = rate > 9 || (inArc && rng.Chance(0.5)) ? ShotType.Loft : rng.Chance(0.3) ? ShotType.Push : ShotType.Stroke;
            var ai = m.AiShot();   // his direction (the client's BestZone is the same idea)
            var tap = Batting.IdealTap(t) + Bowling.Normal(rng) * 0.7 * Batting.Window(m.OnStrike.Player, d);
            return m.Play(new ShotInput(type, ai.IsLeave ? Zone.StraightOff : ai.Direction, tap));
        }

        /// <summary>The client's autoplay bowler (MatchDirector.BowlingBall): any variation, varied marks, the release given.</summary>
        private static BallResult UserBowls(QuickMatch m, Rng rng, double release)
        {
            var bowler = m.CurrentBowler;
            var vars = Bowling.Variations(bowler);
            var spin = bowler.BowlType == BowlType.Spin;
            m.Bowl(vars[rng.NextInt(vars.Count)], rng.NextDouble() * 0.6 - 0.1, spin ? 2.5 + rng.NextDouble() * 4 : 1 + rng.NextDouble() * 8, release);
            return m.PlayAi();
        }

        /// <summary>
        /// Full matches of <paramref name="overs"/> a side. aiOnly: nobody sets a ball (AI v AI). Else the user's
        /// side bats with autoplay and bowls with autoplay, the release error from <paramref name="release"/>.
        /// </summary>
        internal static (Tally userBat, Tally aiBat, Tally aiAi) Play(int overs, int games, long seed, Func<Rng, double>? release = null, Dictionary<Timing, Tally>? byRelease = null)
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            Tally ub = new Tally(), ab = new Tally(), aa = new Tally();
            release ??= r => Bowling.Normal(r) * 0.5;
            for (var g = 0; g < games; g++)
            {
                for (var mode = 0; mode < 2; mode++)
                {
                    var aiOnly = mode == 1;
                    var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), overs, seed + g * 7 + mode, Venue.All[g % Venue.All.Count], userBatsFirst: g % 2 == 0);
                    m.UserRunsLikeAi = true;
                    var rng = new Rng(seed * 3 + g * 11 + mode);
                    for (var inns = 0; inns < 2; inns++)
                    {
                        if (inns == 1) m.StartSecondInnings();
                        var tally = aiOnly ? aa : m.UserBatting ? ub : ab;
                        tally.Innings++;
                        while (!m.Finished)
                        {
                            BallResult r;
                            if (aiOnly) r = m.PlayAi();
                            else if (m.UserBatting) r = UserBats(m, rng);
                            else
                            {
                                var e = release(rng);
                                r = UserBowls(m, rng, e);
                                if (byRelease != null)
                                {
                                    var gr = Bowling.ReleaseGrade(e);
                                    if (!byRelease.TryGetValue(gr, out var bt)) byRelease[gr] = bt = new Tally();
                                    bt.Add(r);
                                }
                            }
                            tally.Add(r);
                        }
                    }
                }
            }
            return (ub, ab, aa);
        }

        /// <summary>Sixes never outnumber fours; RPO in a T20 range; only PERFECT lofts reliably clear the rope.</summary>
        [Fact]
        public void Scoring_shape_sixes_do_not_outnumber_fours()
        {
            var (ub, ab, aa) = Play(20, 50, 91000);
            _out.WriteLine($"user bats (autoplay): {ub}\n   {ub.LoftLine()}");
            _out.WriteLine($"user bowls, AI bats:  {ab}\n   {ab.LoftLine()}");
            _out.WriteLine($"AI v AI:              {aa}\n   {aa.LoftLine()}");
            foreach (var t in new[] { ub, ab, aa }) Assert.InRange(t.SixFour, 0.25, 1.0);
            Assert.InRange(aa.Rpo, 8.5, 10.8);
            Assert.InRange(ab.Rpo, 7.0, 10.5);
            foreach (var t in new[] { ab, aa })
            {
                var perfect = t.Lofts[Timing.Perfect];
                var good = t.Lofts[Timing.Good];
                // PERFECT clears most of the time; GOOD clearly less; early / late never.
                Assert.True(perfect[1] > 0.5 * perfect[0], t.LoftLine());
                Assert.True(good[1] < 0.3 * good[0], t.LoftLine());
                Assert.True(t.Lofts[Timing.Late][1] == 0, t.LoftLine());
            }
        }

        /// <summary>Bowled and lbw come up in natural play, at T20 shares; catches still lead.</summary>
        [Fact]
        public void Dismissal_mix_has_bowled_and_lbw()
        {
            var (ub, ab, aa) = Play(20, 50, 93000);
            _out.WriteLine($"user bats (autoplay): {ub}");
            _out.WriteLine($"user bowls, AI bats:  {ab}");
            _out.WriteLine($"AI v AI:              {aa}");
            foreach (var t in new[] { ab, aa })
            {
                Assert.InRange(t.Share("bowled"), 9, 22);
                Assert.InRange(t.Share("lbw"), 4, 13);
                Assert.InRange(t.Share("caught", "caught behind"), 45, 75);
            }
            // The user's own batting: a good player (tap error ~0.7 windows) still loses some to straight ones.
            Assert.True(ub.Share("bowled", "lbw") >= 8, ub.ToString());
        }

        /// <summary>The user's PERFECT release costs the AI batter: fewer runs, more dots and beaten, than a loose one.</summary>
        [Fact]
        public void A_good_release_contains_the_ai_batter()
        {
            var by = new Dictionary<Timing, Tally>();
            Play(20, 50, 92000, r => Bowling.Normal(r) * 1.0, by);
            foreach (var kv in by.OrderBy(k => k.Key)) _out.WriteLine($"release {kv.Key}: {kv.Value}");
            Tally perfect = by[Timing.Perfect], good = by[Timing.Good], late = by[Timing.Late];
            Assert.True(perfect.Rpo < late.Rpo - 1.5, $"perfect {perfect.Rpo:0.00} v late {late.Rpo:0.00}");
            Assert.True(good.Rpo < late.Rpo, $"good {good.Rpo:0.00} v late {late.Rpo:0.00}");
            Assert.True(perfect.Beaten / (double)perfect.Contacts > 1.3 * late.Beaten / late.Contacts);
            Assert.True(perfect.Dots / (double)perfect.Balls > late.Dots / (double)late.Balls);
        }
    }
}
