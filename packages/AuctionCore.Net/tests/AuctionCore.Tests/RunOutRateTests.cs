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
    /// Run-out owner (2026-11-14; owner: "proper run-out is required now"). Seeded innings, played the way the game
    /// plays them (the AI's shot and running; the test-run bowling): how often the AI side is run out, how often it
    /// chances a risky run (an extra one, or the second to the deep) and gets away with it, and the close calls.
    /// </summary>
    public class RunOutRateTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public RunOutRateTests(ITestOutputHelper output) { _out = output; }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        internal sealed class Rates
        {
            public readonly System.Collections.Generic.Dictionary<string, int> How = new System.Collections.Generic.Dictionary<string, int>();
            public int Innings, Raced, RunOuts, Wickets, Risky, RiskyOut, CloseSafe, Twos, Fake, DirectOuts, Extra, ExtraOut, SharpOuts;
            public double PerInnings => RunOuts / (double)Innings;
            public double Share => RunOuts / (double)Math.Max(1, Wickets);
            public override string ToString() =>
                $"{Innings} innings: run outs {PerInnings:0.00} an innings ({Share:P1} of {Wickets / (double)Innings:0.00} wickets; direct hits {DirectOuts}, sharp throws {SharpOuts}), extra runs chanced {Extra / (double)Innings:0.00} (run out {ExtraOut / (double)Innings:0.00}), raced {Raced / (double)Innings:0.0}, "
                + $"risky runs chanced {Risky / (double)Innings:0.00} (run out on {RiskyOut / (double)Innings:0.00}), close calls safe {CloseSafe / (double)Innings:0.00}, 2s {Twos / (double)Innings:0.00}, fake {Fake} | " + string.Join(", ", How.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / (double)Innings:0.00}"));
        }

        /// <summary>The AI batting a seeded innings of <paramref name="overs"/> overs against the test-run bowling.</summary>
        internal static Rates AiInnings(int overs, int games, int seed)
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            var s = new Rates();
            for (var g = 0; g < games; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % teams.Count]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % teams.Count]), overs, seed + g, null, userBatsFirst: false);
                var rng = new Rng(seed + 7919 + g);
                while (!m.Finished)
                {
                    var vars = Bowling.Variations(m.CurrentBowler);
                    m.Bowl(vars[rng.NextInt(vars.Count)], rng.NextDouble() * 0.6 - 0.1, 2 + rng.NextDouble() * 6, Bowling.Normal(rng) * 0.5);
                    var r = m.PlayAi();
                    if (r.Wicket)
                    {
                        s.Wickets++;
                        var how = r.HowOut ?? "?";
                        s.How[how] = s.How.TryGetValue(how, out var n) ? n + 1 : 1;
                    }
                    if (r.Ran == 2) s.Twos++;
                    if (r.Running == null || r.Attempted == 0 && !r.TurnedBack) continue;
                    s.Raced++;
                    var risky = r.Attempted > r.Running.Safe || r.Running.Spare(r.Attempted) < RunPlan.Gather;
                    if (risky) s.Risky++;
                    var extra = r.Attempted > r.Running.Safe;
                    if (extra) s.Extra++;
                    if (r.RunOut)
                    {
                        s.RunOuts++;
                        if (risky) s.RiskyOut++;
                        if (extra) s.ExtraOut++;
                        if (r.Running.LastThrowSharp) s.SharpOuts++;
                        if (r.DirectHit) s.DirectOuts++;
                        // Never a fake one: the wicket broken before his bat was in.
                        if (!(r.BrokenAt < r.BatIn)) s.Fake++;
                    }
                    else if (!double.IsNaN(r.BallIn) && r.BatIn - (r.BallIn + (r.Shied ? 0 : RunPlan.Gather)) > -0.3) s.CloseSafe++;
                }
                s.Innings++;
            }
            return s;
        }

        [Fact]
        public void Ai_side_is_run_out_now_and_then_and_never_falsely()
        {
            var t20 = AiInnings(20, 300, 9100);
            var six = AiInnings(6, 600, 9500);
            _out.WriteLine("20 overs: " + t20);
            _out.WriteLine(" 6 overs: " + six);
            Assert.Equal(0, t20.Fake + six.Fake);
            // T20-like: a run out every one to two 20-over innings in the test-run sim (real play races fewer balls), never
            // the main way out; in 6 overs one every two to four innings.
            Assert.InRange(t20.PerInnings, 0.5, 1.15);
            Assert.InRange(six.PerInnings, 0.2, 0.5);
            Assert.True(t20.Share < 0.16, $"run outs {t20.Share:P0} of wickets");
            // The risky run is seen and mostly made: the extra run chanced, and more close calls safe than run outs.
            Assert.True(t20.Extra > t20.ExtraOut * 2, "extra runs mostly made");
            Assert.True(t20.CloseSafe > 3 * t20.RunOuts, "close calls safe outnumber run outs");
            // The fielding makes them: some by a sharp pick-up-and-throw, some by a direct hit.
            Assert.True(t20.SharpOuts > 0 && t20.DirectOuts > 0);
        }

        /// <summary>
        /// The owner's rules on the user's own running: AUTO is never run out; MANUAL can be — a late call or a greedy extra
        /// run that the throw beats — and a timely dive saves some of the close ones.
        /// </summary>
        [Fact]
        public void User_auto_is_never_out_manual_late_or_greedy_calls_can_be_and_a_dive_saves_some()
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            int autoOut = 0, autoRaced = 0, lateOut = 0, greedyOut = 0, greedy = 0, saved = 0, games = 120;
            for (var g = 0; g < games; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % teams.Count]), QuickMatch.LastSeasonXI(Data, teams[(g + 5) % teams.Count]), 20, 8800 + g, null, userBatsFirst: true);
                Assert.True(m.UserBatting && m.UserSafeRunsOnly && !m.UserRunsLikeAi);
                while (!m.Finished)
                {
                    var r = m.PlayAi();
                    if (r.Running == null || r.Four || r.Six || r.Caught || (r.Wicket && !r.RunOut)) continue;
                    // AUTO as the engine played it.
                    var autoGo = r.Attempted;
                    if (autoGo > 0) autoRaced++;
                    if (r.RunOut) autoOut++;
                    var p = r.Running;
                    if (p.Safe < 1) continue;
                    // MANUAL, a late call (0.6 s) on the safe runs: tried and put back (the same draws; the book rewound each time).
                    var late = m.SettleRuns(p.Safe, late: 0.6, playerCalled: true);
                    if (late != null && late.RunOut)
                    {
                        lateOut++;
                        if (m.SettleRuns(p.Safe, dive: true, late: 0.6, playerCalled: true) is BallResult d && !d.RunOut) saved++;
                    }
                    // MANUAL, greedy: one more than safe when the HUD reads it under even money.
                    if (p.Safe < 3 && p.Risk(p.Safe + 1) < 0.5)
                    {
                        greedy++;
                        if (m.SettleRuns(p.Safe + 1, playerCalled: true) is BallResult x && x.RunOut) greedyOut++;
                    }
                    // Back to what AUTO ran, so the innings goes on as it would.
                    m.SettleRuns(autoGo);
                }
            }
            _out.WriteLine($"{games} user innings: AUTO raced {autoRaced}, run out {autoOut}; MANUAL late call (0.6 s) run out {lateOut} (a dive saved {saved}); greedy extra run {greedy}, run out {greedyOut}");
            Assert.Equal(0, autoOut);
            Assert.True(lateOut > 0, "a late MANUAL call can be run out");
            Assert.True(greedyOut > 0 && greedyOut < greedy, "a greedy MANUAL run is sometimes out, not always");
            Assert.True(saved > 0, "a timely dive saves some");
        }
    }
}
