using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;
using Xunit.Abstractions;

namespace AuctionCore.Tests
{
    /// <summary>Pace by bowler, the release meter, fields, the bowler's-end run outs, the timeout, the impact player.</summary>
    public class Round4Tests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(Root(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public Round4Tests(ITestOutputHelper output) { _out = output; }

        internal static string Root()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static QuickMatch Full(int overs = 20, bool userBatsFirst = true) =>
            QuickMatch.Full(QuickMatch.LastSeasonXI(Data, "BLR"), QuickMatch.LastSeasonXI(Data, "MBH"), overs, 5, null, userBatsFirst);

        [Fact]
        public void Express_bowlers_bowl_quicker_than_medium_pacers_and_spinners_slower_still()
        {
            double Mean(PaceClass c)
            {
                var b = Data.Players.Where(p => p.BowlType != BowlType.None && Bowling.PaceOf(p) == c).ToList();
                var rng = new Rng(3);
                return b.Count == 0 ? double.NaN : Enumerable.Range(0, 400).Average(i => Bowling.Bowl(b[i % b.Count], 1, rng).SpeedKmh);
            }
            var classes = new[] { PaceClass.Express, PaceClass.Fast, PaceClass.FastMedium, PaceClass.Medium, PaceClass.SlowMedium, PaceClass.FingerSpin };
            var speeds = classes.Select(Mean).ToList();
            _out.WriteLine(string.Join(", ", classes.Zip(speeds, (c, s) => $"{c} {s:0.0}")));
            for (var i = 1; i < speeds.Count; i++) if (!double.IsNaN(speeds[i]) && !double.IsNaN(speeds[i - 1])) Assert.True(speeds[i] < speeds[i - 1]);
            Assert.True(Bowling.RunUpPace(PaceClass.Express) > Bowling.RunUpPace(PaceClass.Medium));
        }

        [Fact]
        public void A_perfect_release_lands_near_the_mark_with_full_movement_a_bad_one_sprays_and_loses_it()
        {
            var b = TestBowlers.TopPacer(Data);
            (double spread, double swing, int bad) Run(double e)
            {
                var rng = new Rng(9);
                var ds = Enumerable.Range(0, 600).Select(i => Bowling.Deliver(b, Variation.Outswinger, 0.2, 5.5, i % 2 == 0 ? e : -e, 0, rng)).ToList();
                var mean = ds.Average(d => d.BounceZ);
                return (Math.Sqrt(ds.Average(d => (d.BounceZ - mean) * (d.BounceZ - mean))), ds.Average(d => Math.Abs(d.Swing)), ds.Count(d => d.Wide || d.NoBall || d.FullToss));
            }
            var perfect = Run(0.1);
            var poor = Run(2.8);
            _out.WriteLine($"perfect: spread {perfect.spread:0.00} m, swing {perfect.swing:0.000}, wides/no-balls/full tosses {perfect.bad}; poor: {poor.spread:0.00} m, {poor.swing:0.000}, {poor.bad}");
            Assert.True(poor.spread > perfect.spread * 3);
            Assert.True(poor.swing < perfect.swing * 0.6);
            Assert.True(poor.bad > perfect.bad + 20);
            Assert.Equal(Timing.Perfect, Bowling.ReleaseGrade(0.2));
            Assert.Equal(Timing.Good, Bowling.ReleaseGrade(-0.8));
            Assert.Equal(Timing.Early, Bowling.ReleaseGrade(-1.5));
            Assert.Equal(Timing.VeryLate, Bowling.ReleaseGrade(2.6));
        }

        [Fact]
        public void The_AI_captain_sets_a_death_field_and_saves_the_last_ball_single()
        {
            Assert.Equal(FieldPreset.Death, Field.AiPreset(new Situation(150, 4, 18, 20, 190, 10, false, 2), false, 1));
            Assert.Equal(FieldPreset.SaveOne, Field.AiPreset(new Situation(189, 6, 1, 20, 190, 10, false, 2), false, 1));
            // Owner v26 #13: Auto and nineteen named fields (FieldMore.cs), the custom slots apart.
            Assert.Equal(22, Field.Presets.Length); // field rework 2026-11-16: Auto and 21 researched fields
            Assert.All(Field.Presets, p => Assert.False(string.IsNullOrEmpty(Field.PresetNote(p))));
        }

        [Fact]
        public void A_deflected_straight_drive_runs_out_the_backing_up_non_striker_now_and_then()
        {
            var rng = new Rng(1);
            int deflected = 0, outs = 0;
            for (var i = 0; i < 4000; i++)
            {
                var r = new BallResult { ExitAngle = 4, ExitSpeed = 26, Runs = 2, Running = new RunPlan { Safe = 2 } };
                BowlersEnd.Deflect(r, rng);
                if (!r.Deflected) continue;
                deflected++;
                Assert.Null(r.Running);
                if (!r.RunOut) continue;
                outs++;
                Assert.False(r.RunOutAtKeeperEnd);
                Assert.False(r.RunOutStriker);
            }
            _out.WriteLine($"{deflected}/4000 deflected, {outs} run out");
            Assert.InRange(deflected, 100, 260);
            Assert.InRange(outs, deflected / 4, deflected * 3 / 4);
        }

        [Fact]
        public void Running_out_the_backing_up_non_striker_is_a_wicket_not_a_ball()
        {
            // Until a non-striker backs up too far (a fresh match if an innings ends first: the
            // engine's scoring changes move the random draws, round 12).
            var m = Full(userBatsFirst: false);
            for (var seed = 6; !m.NonStrikerBackingUp && seed < 40; seed++)
            {
                for (var i = 0; i < 400 && !m.NonStrikerBackingUp && !m.Finished; i++)
                {
                    m.Bowl(Variation.Stock, 0.2, 5.5, 0);
                    if (!m.NonStrikerBackingUp) m.PlayAi();
                }
                if (!m.NonStrikerBackingUp) m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, "BLR"), QuickMatch.LastSeasonXI(Data, "MBH"), 20, seed, null, false);
            }
            Assert.True(m.NonStrikerBackingUp);
            var balls = m.LegalBalls;
            var off = m.OffStrike.Player;
            var r = m.Mankad()!;
            Assert.True(r.Wicket && r.RunOut && !r.RunOutStriker && r.DeadBall && !r.Legal);
            Assert.Equal(balls, m.LegalBalls);
            Assert.Same(off, r.OutBatter);
        }

        [Fact]
        public void The_strategic_timeout_is_once_an_innings_in_its_window()
        {
            var m = Full();
            Assert.Equal((6, 15), m.TimeoutWindow);
            while (m.Over < 6) m.PlayAi();
            Assert.True(m.CanTakeTimeout);
            Assert.True(m.TakeTimeout());
            Assert.False(m.TakeTimeout());
            while (!m.Finished) m.PlayAi();
            m.StartSecondInnings();
            while (m.Over < 15 && !m.Finished) m.PlayAi();
            if (!m.Finished) Assert.True(m.TimeoutDue);
        }

        [Fact]
        public void One_impact_substitution_a_side_and_the_AI_makes_its_own_at_the_break()
        {
            var m = Full();
            var squad = Data.Players.Where(p => p.PreviousTeam == "MBH").ToList();
            m.NameSubstitutes("MBH", QuickMatch.DefaultSubstitutes(squad, m.Bowling));
            m.NameSubstitutes("BLR", QuickMatch.DefaultSubstitutes(Data.Players.Where(p => p.PreviousTeam == "BLR"), m.Batting));
            Assert.Equal(QuickMatch.MaxSubstitutes, m.Substitutes("MBH").Count);
            while (!m.Finished) m.PlayAi();
            m.StartSecondInnings();
            // The user's side (now in the field) brings on a sub for a man who's done.
            var off = m.Bowling.XI.First(p => !p.IsKeeper && p != m.CurrentBowler);
            var on = m.Substitutes("BLR")[0];
            Assert.True(m.Substitute("BLR", off, on));
            Assert.Contains(on, m.Bowling.XI);
            Assert.DoesNotContain(off, m.Bowling.XI);
            Assert.False(m.Substitute("BLR", on, m.Substitutes("BLR")[1]));
            var aiMade = m.AiImpact("MBH");
            _out.WriteLine($"AI impact at the break: {aiMade} {m.ImpactMade("MBH")}");
            Assert.False(m.AiImpact("MBH") && aiMade);
        }
    }
}
