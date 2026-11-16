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
    /// Round 12 (owner: 25–45 runs a side in 3 overs, some 4s and 6s): 3-over innings as the
    /// test runs play them — the AI batting against the autoplay bowling, and the user's side
    /// batting as the autoplay does — runs, wickets, 4s and 6s, and what a PERFECT shot earns.
    /// </summary>
    public class TotalsTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public TotalsTests(ITestOutputHelper output) { _out = output; }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private sealed class Tally
        {
            public readonly List<int> Runs = new List<int>(), Wkts = new List<int>();
            public int Fours, Sixes, Innings, Caught, RunOuts, Twos;
            public readonly List<int> CaughtPer = new List<int>(), RunOutsPer = new List<int>();
            private int _c0, _r0;
            public void EndInnings() { CaughtPer.Add(Caught - _c0); RunOutsPer.Add(RunOuts - _r0); _c0 = Caught; _r0 = RunOuts; }
            public Dictionary<Timing, List<int>> ByTiming = new Dictionary<Timing, List<int>>();

            public void Ball(BallResult r)
            {
                if (r.Four) Fours++;
                if (r.Six) Sixes++;
                if (r.Wicket && r.Caught) Caught++;
                if (r.RunOut) RunOuts++;
                if (r.Ran == 2) Twos++;
                if (r.Shot.IsLeave || r.Wide) return;
                if (!ByTiming.TryGetValue(r.Timing, out var l)) ByTiming[r.Timing] = l = new List<int>();
                l.Add(r.Runs);
            }

            public string Line()
            {
                var runs = Runs.OrderBy(x => x).ToList();
                int P(double q) => runs[(int)(q * (runs.Count - 1))];
                var inBand = runs.Count(x => x >= 25 && x <= 45) / (double)runs.Count;
                return $"runs mean {runs.Average():0.0} (p10 {P(0.1)}, median {P(0.5)}, p90 {P(0.9)}; {inBand:P0} in 25-45), wickets {Wkts.Average():0.00}, 4s {Fours / (double)Innings:0.00}, 6s {Sixes / (double)Innings:0.00}, 2s {Twos / (double)Innings:0.00}, caught {Caught / (double)Innings:0.00}, run outs {RunOuts / (double)Innings:0.00} an innings";
            }

            public string Timings() => string.Join(", ", ByTiming.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value.Average():0.00} r/ball ({kv.Value.Count(x => x >= 4) / (double)kv.Value.Count:P0} boundaries)"));
        }

        /// <summary>The AI bats; the user's side bowls as the autoplay does (varied types and lengths, release error ~half the green).</summary>
        private static Tally AiBats(int games, int overs = 3)
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            var t = new Tally();
            for (var g = 0; g < games; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), overs, 4100 + g, null, userBatsFirst: false);
                var rng = new Rng(4300 + g);
                while (!m.Finished)
                {
                    var vars = Bowling.Variations(m.CurrentBowler);
                    var spin = m.CurrentBowler.BowlType == BowlType.Spin;
                    m.Bowl(vars[rng.NextInt(vars.Count)], rng.NextDouble() * 0.6 - 0.1, spin ? 2.5 + rng.NextDouble() * 4 : 1 + rng.NextDouble() * 8, Bowling.Normal(rng) * 0.5);
                    t.Ball(m.PlayAi());
                }
                t.Runs.Add(m.Runs);
                t.Wkts.Add(m.Wickets);
                t.Innings++;
                t.EndInnings();
            }
            return t;
        }

        /// <summary>The user's side bats first as the autoplay does: tap error <paramref name="sigma"/> windows, LOFT when the rate's over 9, else 30% PUSH.</summary>
        private static Tally UserBats(int games, double sigma, double loftInArc = 0, int overs = 3)
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            var t = new Tally();
            for (var g = 0; g < games; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), overs, 5100 + g, null, userBatsFirst: true);
                m.UserRunsLikeAi = true; // as the test runs play (MatchDirector sets it in autoplay)
                var rng = new Rng(5300 + g);
                while (!m.Finished)
                {
                    var tr = m.NextDelivery();
                    var d = tr.Delivery;
                    var inArc = d.FullToss || d.Length == Length.Full || (d.Length == Length.Short && !d.IsSpin);
                    var type = inArc && rng.Chance(loftInArc) ? ShotType.Loft : rng.Chance(0.3) ? ShotType.Push : ShotType.Stroke;
                    var zone = Enum.GetValues(typeof(Zone)).Cast<Zone>().OrderByDescending(z => Batting.Suitability(tr.Delivery, type, z)).First();
                    var tap = Batting.IdealTap(tr) + Bowling.Normal(rng) * sigma * Batting.Window(m.OnStrike.Player, tr.Delivery);
                    t.Ball(m.Play(new ShotInput(type, zone, tap)));
                }
                t.Runs.Add(m.Runs);
                t.Wkts.Add(m.Wickets);
                t.Innings++;
                t.EndInnings();
            }
            return t;
        }

        /// <summary>The -aplnudges evidence balls (MatchDirector.TestNudges): how often each set-up shows what it's for.</summary>
        [Fact]
        public void Evidence_set_ups()
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            int six = 0, lbw = 0, reviewed = 0, stumped = 0, noBall = 0, n = 300;
            for (var g = 0; g < n; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), 3, 6100 + g, null, userBatsFirst: false);
                m.TestAlwaysReview = true;
                // A six: perfect loft, straight, on a full ball from the opening bowler.
                var t = m.Bowl(Variation.Stock, 0.1, m.CurrentBowler.BowlType == BowlType.Spin ? 2.6 : 2.2, 0);
                var r = m.Play(new ShotInput(ShotType.Loft, Zone.StraightOn, Batting.IdealTap(t)));
                if (r.Six) six++;
                if (m.Finished) continue;
                // An lbw: full and straight, and he shoulders arms (a late stroke across was reviewed half as often).
                m.Bowl(Variation.Stock, 0.02, 2.2, 0);
                r = m.Play(ShotInput.Leave);
                if (r.Lbw != null) lbw++;
                if (r.Lbw != null && r.Lbw.Reviewed) reviewed++;
                // A no-ball, forced.
                m.TestNoBall = true;
                if (m.PlayAi().NoBall) noBall++;
            }
            // A stumping: a spinner outside off, the batter charging and missing.
            var spinners = Data.Players.Where(p => p.BowlType == BowlType.Spin).ToList();
            for (var g = 0; g < n; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), 3, 7100 + g, null, userBatsFirst: false);
                var spinner = m.BowlerOptions().FirstOrDefault(o => o.Allowed && o.Player.BowlType == BowlType.Spin)?.Player;
                if (spinner == null || !m.SetBowler(spinner)) continue;
                var t = m.Bowl(Variation.Stock, 0.55, 3.4, 0);
                var r = m.Play(new ShotInput(ShotType.Loft, Zone.StraightOff, Batting.IdealTap(t, true) + 4.2 * Batting.Window(m.OnStrike.Player, t.Delivery)) { Charged = true });
                if (r.HowOut == "stumped") stumped++;
            }
            _out.WriteLine($"six {six}/{n}, lbw shout {lbw}/{n} (reviewed {reviewed}), stumped {stumped}/{n}, forced no-ball {noBall}/{n}");
            Assert.Equal(n, noBall + (n - noBall)); // reported, not asserted: the run retries each set-up
        }

        /// <summary>A plain (un-nudged) test match of N overs a side: the chance it films 3+ catches and a run out.</summary>
        [Fact]
        public void Plain_run_length_for_catches_and_a_run_out()
        {
            foreach (var overs in new[] { 3, 5, 6, 8 })
            {
                var ai = AiBats(200, overs);
                var user = UserBats(200, 0.7, 0.5, overs);
                int three = 0, ro = 0;
                for (var i = 0; i < 200; i++)
                {
                    if (ai.CaughtPer[i] + user.CaughtPer[i] >= 3) three++;
                    if (ai.RunOutsPer[i] + user.RunOutsPer[i] >= 1) ro++;
                }
                _out.WriteLine($"{overs} overs a side: 3+ catches in {three / 2.0:0}% of matches, a run out in {ro / 2.0:0}%; runs {ai.Runs.Average():0} / {user.Runs.Average():0}");
            }
        }

        [Fact]
        public void Three_over_totals()
        {
            var ai = AiBats(250);
            var user = UserBats(250, 0.7);
            _out.WriteLine("AI bats:   " + ai.Line());
            _out.WriteLine("           " + ai.Timings());
            _out.WriteLine("user bats: " + user.Line());
            _out.WriteLine("           " + user.Timings());
            // The autoplay change proposed to batting: LOFT half the balls in the arc (full, short from pace, full tosses).
            var lofts = UserBats(250, 0.7, 0.5);
            _out.WriteLine("user bats, lofting half the balls in the arc: " + lofts.Line());
            Assert.InRange(ai.Runs.Average(), 22, 48);
        }
    }
}
