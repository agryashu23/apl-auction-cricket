using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;
using Xunit.Abstractions;

namespace AuctionCore.Tests
{
    /// <summary>
    /// Deep area 3 (owner: "run-out misses too much, I don't even see it"): over many 20-over AI innings
    /// against the test-run bowling, the run-out chances as they fell — run outs an innings (target
    /// 0.6–0.9), the stumps hit by a shy (out or not; at least one a match of two innings), and NO batter
    /// given home when the ball was in the hands at the stumps more than 0.3 s before his bat (an easy
    /// chance must be converted).
    /// </summary>
    public class RunOutChanceTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public RunOutChanceTests(ITestOutputHelper output) { _out = output; }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        /// <summary>
        /// What drives the run-out rate, side by side for the frozen golden dataset and the live one (print only):
        /// raced balls, extra runs chanced (more than the safe ones), run outs on those and on the safe runs,
        /// the fields' fielding average and the throw's time back.
        /// </summary>
        [Fact]
        public void Run_out_drivers_by_dataset()
        {
            foreach (var file in new[] { Path.Combine(FindRoot(), "packages", "AuctionCore.Net", "golden", "dataset.json"), Path.Combine(FindRoot(), "data", "apl_dataset.json") })
            {
                if (!File.Exists(file)) continue;
                var data = Dataset.FromJson(File.ReadAllText(file));
                var teams = data.Franchises.Select(f => f.Id).ToList();
                int games = 300, raced = 0, extra = 0, extraOut = 0, safeOut = 0, turned = 0, tightSafe = 0, safeOutDirect = 0, push = 0, pushOut = 0;
                double fielding = 0, back = 0, aggr = 0, spare = 0;
                for (var g = 0; g < games; g++)
                {
                    var m = QuickMatch.Full(QuickMatch.LastSeasonXI(data, teams[g % teams.Count]), QuickMatch.LastSeasonXI(data, teams[(g + 3) % teams.Count]), 20, 2300 + g, null, userBatsFirst: false);
                    var rng = new Rng(2500 + g);
                    while (!m.Finished)
                    {
                        var vars = Bowling.Variations(m.CurrentBowler);
                        m.Bowl(vars[rng.NextInt(vars.Count)], rng.NextDouble() * 0.6 - 0.1, 2 + rng.NextDouble() * 6, Bowling.Normal(rng) * 0.5);
                        var a = m.Aggression();
                        var f = m.Field.Fielding;
                        var r = m.PlayAi();
                        if (r.Running == null || r.Attempted == 0 && !r.TurnedBack) continue;
                        raced++;
                        fielding += f;
                        back += r.Running.Back;
                        if (r.TurnedBack) turned++;
                        if (r.Attempted > r.Running.Safe)
                        {
                            extra++;
                            aggr += a;
                            spare += r.Running.Spare(r.Attempted);
                            if (r.RunOut) extraOut++;
                        }
                        else
                        {
                            // (The push single: the engine sets it tight on purpose, Rules.Resolve, and marks it with a 0.7× shy.)
                            if (r.FieldPlay is Field.Play fp && r.Attempted > fp.Runs) { push++; if (r.RunOut) pushOut++; }
                            if (r.Running.Spare(Math.Max(1, r.Attempted)) < RunPlan.ShyWindow) tightSafe++;
                            if (r.RunOut) safeOut++;
                            if (r.RunOut && r.DirectHit) safeOutDirect++;
                        }
                    }
                }
                _out.WriteLine($"{Path.GetFileName(Path.GetDirectoryName(file))}/{Path.GetFileName(file)}: raced {raced / (double)games:0.0} an innings, fielding {fielding / Math.Max(1, raced):0.0}, throw back {back / Math.Max(1, raced):0.00} s; "
                    + $"extra runs chanced {extra / (double)games:0.00} an innings (aggression {aggr / Math.Max(1, extra):0.00}, spare {spare / Math.Max(1, extra):+0.00;-0.00} s), run out on them {extraOut / (double)games:0.00}, on safe runs {safeOut / (double)games:0.00} ({safeOutDirect / (double)games:0.00} direct hits; tight safe runs {tightSafe / (double)games:0.00} an innings; push singles {push / (double)games:0.00}, run out {pushOut / (double)games:0.00}), sent back {turned / (double)games:0.00}");
            }
        }

        /// <summary>
        /// The owner's easy run out (2026-11-13): the ball in the hands over the stumps (or a shy there) with the batter
        /// still about 1.5 m short — 0.22 s at a sprint. Out, every time.
        /// </summary>
        private const double OwnerEasy = 0.22;

        /// <summary>
        /// The user's own side (owner, 2026-11-13: "AUTO running shouldn't have run outs — only MANUAL"). AUTO: the safe
        /// runs, never run out. MANUAL, as a decent player calls it (the runs on offer; the extra one about half the time
        /// when the HUD's risk reads under 0.35; a call up to 0.3 s late; a dive half the time): run outs happen, and every
        /// easy chance (ball in the hands, the batter ~1.5 m short) is out.
        /// </summary>
        [Fact]
        public void User_side_auto_is_never_run_out_and_manual_can_be()
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            int games = 300, autoOut = 0, manualOut = 0, easy = 0, easySafe = 0, extra = 0, overthrows = 0;
            for (var g = 0; g < games; g++)
            {
                foreach (var manual in new[] { false, true })
                {
                    var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), 20, 4300 + g, null, userBatsFirst: true);
                    Assert.True(m.UserBatting && m.UserSafeRunsOnly && !m.UserRunsLikeAi);
                    var rng = new Rng(4700 + g);
                    while (!m.Finished)
                    {
                        var r = m.PlayAi();
                        if (manual && r.Running != null && !r.Four && !r.Six && !r.Caught && !(r.Wicket && !r.RunOut))
                        {
                            var p = r.Running;
                            var k = p.Safe;
                            if (k < 3 && p.Risk(k + 1) < 0.35 && rng.Chance(0.5)) k++;
                            r = m.SettleRuns(k, dive: rng.Chance(0.5), late: rng.NextDouble() * 0.3, playerCalled: true, overthrowCalls: 1) ?? r;
                            if (r.Attempted > p.Safe) extra++;
                            if (r.Overthrow) overthrows++;
                        }
                        if (r.Running == null || r.Attempted == 0 || double.IsNaN(r.BallIn)) continue;
                        if (r.RunOut) { if (manual) manualOut++; else autoOut++; }
                        if (manual && r.BatIn - r.BallIn >= OwnerEasy) { easy++; if (!r.RunOut) easySafe++; }
                    }
                }
            }
            _out.WriteLine($"user side {games} innings each: AUTO run outs {autoOut / (double)games:0.00} an innings; MANUAL run outs {manualOut / (double)games:0.00} (extra runs called {extra / (double)games:0.00}, overthrows {overthrows / (double)games:0.00}), easy chances {easy} (given safe {easySafe})");
            Assert.Equal(0, autoOut);
            Assert.Equal(0, easySafe);
            Assert.InRange(manualOut / (double)games, 0.4, 2.0);
        }

        [Fact]
        public void Twenty_over_run_out_chances_are_real_and_converted()
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            int games = 300, runOuts = 0, directOuts = 0, stumpsHit = 0, shies = 0, chances = 0, easySafe = 0, nearSafe = 0, referred = 0, raced = 0;
            int easyShied = 0, easyPlannedTake = 0, within02 = 0, within03 = 0;
            string? easy = null;
            for (var g = 0; g < games; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), 20, 2300 + g, null, userBatsFirst: false);
                var rng = new Rng(2500 + g);
                while (!m.Finished)
                {
                    var vars = Bowling.Variations(m.CurrentBowler);
                    m.Bowl(vars[rng.NextInt(vars.Count)], rng.NextDouble() * 0.6 - 0.1, 2 + rng.NextDouble() * 6, Bowling.Normal(rng) * 0.5);
                    var r = m.PlayAi();
                    if (r.Running == null || r.Attempted == 0 || double.IsNaN(r.BallIn)) continue;
                    raced++;
                    // How much the ball (in the hands at the stumps, or a shy getting there) beat the bat by.
                    var beat = r.BatIn - r.BallIn;
                    var breakAt = r.BallIn + (r.Shied ? 0 : RunPlan.Gather);
                    var chance = r.RunOut || Math.Abs(breakAt - r.BatIn) < 1.0;
                    if (!chance) continue;
                    chances++;
                    if (r.Shied) shies++;
                    if (r.DirectHit) stumpsHit++;
                    if (r.RunOut) runOuts++;
                    if (r.RunOut && r.DirectHit) directOuts++;
                    if (r.Referral == TvCheck.RunOut)
                    {
                        referred++;
                        if (Math.Abs(r.Margin) < 0.02) within02++;
                        if (Math.Abs(r.Margin) < 0.03) within03++;
                    }
                    if (!r.RunOut && beat >= OwnerEasy)
                    {
                        easySafe++;
                        if (r.Shied) easyShied++;
                        if (r.Running.Back - RunPlan.ArrivesAt(r.Attempted) + RunPlan.Gather < 0) easyPlannedTake++;
                        easy ??= $"beat by {beat:0.00} s, shied {r.Shied}, hit {r.DirectHit}, dived {r.Dived}, throw {r.Running.ThrowDistance:0} m";
                    }
                    if (!r.RunOut && breakAt - r.BatIn < 0.3) nearSafe++;
                }
            }
            _out.WriteLine($"{games} innings, {raced} runs raced: chances {chances} ({chances / (double)games:0.00} an innings), run outs {runOuts} ({runOuts / (double)games:0.00} an innings), direct-hit run outs {directOuts}");
            _out.WriteLine($"shies {shies}, stumps hit {stumpsHit} ({2 * stumpsHit / (double)games:0.00} a match of two innings), referred {referred}, safe within 0.3 s {nearSafe}");
            _out.WriteLine($"run-out referrals (window {Running.Refer} s): {referred} = {2 * referred / (double)games:0.00} a match of two innings; within 0.03 s {2 * within03 / (double)games:0.00}, within 0.02 s {2 * within02 / (double)games:0.00} a match");
            _out.WriteLine($"easy chances given safe (ball beat the bat by >= {OwnerEasy} s, ~1.5 m): {easySafe}{(easy != null ? " — e.g. " + easy : "")} (missed shies {easyShied}; where the plan had the take beating him {easyPlannedTake})");
            Assert.Equal(0, easySafe);
            Assert.InRange(runOuts / (double)games, 0.6, 1.1); // PM 2026-11-16: owner wants run-outs seen; ~1 per 20-over innings (sharp throws + mistimed lofts along the ground)
            Assert.True(2 * stumpsHit / (double)games >= 1.0, $"stumps hit {2 * stumpsHit / (double)games:0.00} a match");
        }
    }
}
