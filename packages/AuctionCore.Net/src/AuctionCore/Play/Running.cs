using System;

namespace Apl.Core.Play
{
    /// <summary>What the batters ran for: off the bat, byes (past the bat and the keeper), leg byes (off the pad).</summary>
    public enum RunKind { Bat, Byes, LegByes }

    /// <summary>
    /// The race between the wickets once a ball is fielded: the batters need
    /// <see cref="Start"/> s to get going and <see cref="RunTime"/> s a run; the
    /// throw gets back to the stumps <see cref="Back"/> s after the hit. The
    /// engine's proposal is <see cref="Safe"/> runs; pushing for more risks a run
    /// out: a fielder who sees it's tight shies at the stumps (a direct hit
    /// beats the batter on its own), otherwise it goes to the keeper or bowler,
    /// who needs a moment to gather and break the wicket.
    /// </summary>
    public sealed class RunPlan
    {
        /// <summary>Setting off (backing up, calling), and one run crease to crease (the running clip is 3.2 s).</summary>
        public const double Start = 0.4, RunTime = 3.3;

        /// <summary>
        /// Gathering a throw and breaking the stumps (a good throw into the gloves or hands over the stumps).
        /// Run-out owner (2026-11-13, owner: "easy run outs don't happen"): 0.3 → 0.15. The ball in his hands over
        /// the stumps and the bails whipped off is one movement; at 0.3 s a batter 1.5–2 m short when the ball
        /// arrived (0.22–0.3 s at a sprint) still got home — 38% of those "easy" chances were given not out.
        /// Now anyone ≥ 1 m short when the ball's in the hands is out (a wide throw still costs <see cref="WideExtra"/>
        /// to collect, but that's before the ball is in his hands).
        /// </summary>
        public const double Gather = 0.15;

        /// <summary>The bowler's stumps up the pitch from the striker's (m); the fielders' flat throw (m/s, as Field); the bowler's extra moment to be over his stumps (s).</summary>
        public const double BowlerEndF = 20.12, ThrowSpeed = 28, BowlerEndExtra = 0.15;

        /// <summary>The take beats the batter by more than this (s): an easy run out — no shy, gathered and broken.</summary>
        public const double EasyMargin = 0.25;

        /// <summary>A last-chance shy, on the run and off balance, hits this share as often as a set one.</summary>
        public const double OnTheRun = 0.55;

        /// <summary>A wide or bouncing throw (this share of them) takes this much longer to collect and break with.</summary>
        public const double WideShare = 0.12, WideExtra = 0.2;

        /// <summary>The gather for this throw (<paramref name="u"/>: its uniform draw).</summary>
        internal static double GatherFor(double u) => Gather + (u < WideShare ? WideExtra : 0);

        /// <summary>From the ring (a short throw) a fielder shies even when a throw to the keeper would do; from the deep, almost never.</summary>
        internal static double ShyFromRing(double distance) => DartMath.Clamp(0.4 - 0.02 * distance, 0.03, 0.25);

        /// <summary>The sharp throw's share (<see cref="Sharp"/>): at an average fielding side, per point of skill above it; within this throw (m) it's a ring fielder's, beyond it this share of that.</summary>
        public const double SharpBase = 0.015, SharpSkill = 0.015, SharpRing = 30, SharpDeep = 0.5;

        /// <summary>A shy at the stumps when the batter's this close (s) or short of it.</summary>
        public const double ShyWindow = 0.35;

        /// <summary>Random spread on the batter's arrival and the throw's (s).</summary>
        public const double Noise = 0.12;

        /// <summary>A timely dive gets the bat in this much sooner (s); turning back costs this (the stop and turn, s).</summary>
        public const double DiveGain = 0.18, TurnTime = 0.4;

        /// <summary>
        /// The race's random draws, one set per run (arrival and throw noise, the shy,
        /// an overthrow), kept so a rework (a late dive, a turn-back) replays the same race.
        /// </summary>
        internal readonly System.Collections.Generic.List<(double arrive, double down, double shy, double over)> Draws =
            new System.Collections.Generic.List<(double, double, double, double)>();

        internal (double arrive, double down, double shy, double over) Draw(int k, Rng rng)
        {
            while (Draws.Count < k) Draws.Add((Bowling.Normal(rng), Bowling.Normal(rng), rng.NextDouble(), rng.NextDouble()));
            return Draws[k - 1];
        }

        /// <summary>
        /// The last run's throw was drawn wide (it costs <see cref="WideExtra"/> to collect) — read only, for the view
        /// (deep area 3: ThrowIn shows the throw off line on exactly the engine's wide ones).
        /// </summary>
        /// <remarks>Run-out owner: the deciding run's draw (<see cref="Decided"/>), not the last one drawn — a rework keeps the
        /// draws of runs it no longer goes for (the AI's 3 re-settled as the player's 2), and a run refused under
        /// Running.Commit was drawn but doesn't decide anything.</remarks>
        public bool LastThrowWide => Decided >= 1 && Decided <= Draws.Count && Draws[Decided - 1].over < WideShare;

        /// <summary>The run whose race decided the last settle (1…; 0: none) — the one he's out on, or the last one made.</summary>
        public int Decided;

        /// <summary>
        /// After a settle: how the last run they went for stood without a dive
        /// (s; + the bat was in, − the wicket was broken first). A dive matters when
        /// it's between −<see cref="DiveGain"/> and a whisker over 0.
        /// </summary>
        public double LastMargin = double.MaxValue;
        public bool DiveMatters => LastMargin > -DiveGain && LastMargin < 0.12;

        public RunKind Kind;
        public double Back;
        public double ThrowDistance;
        public int Safe;

        /// <summary>A shy from this far hits the stumps this often.</summary>
        public double DirectHit;

        /// <summary>Which end the throw goes to: the keeper's (striker's) end, or the bowler's.</summary>
        public bool ToKeeperEnd = true;
        public string Fielder = "";

        /// <summary>Seconds to spare on the <paramref name="k"/>-th run (− the ball's there first, before any gathering).</summary>
        public double Spare(int k) => Back - (Start + k * RunTime);

        /// <summary>The batters reach the crease on the <paramref name="k"/>-th run (s after the hit).</summary>
        public double Arrives(int k) => ArrivesAt(k);

        public static double ArrivesAt(int k) => Start + k * RunTime;

        /// <summary>The odds of a run out going for <paramref name="k"/> runs (for the HUD's RUN button).</summary>
        /// <remarks>Run-out owner (2026-11-14): with the fielder's sharp pick-up-and-throw (<see cref="Sharp"/>) mixed in.</remarks>
        public double Risk(int k)
        {
            if (k <= 0) return 0;
            var spare = Spare(k);
            return (1 - Sharp) * RiskAt(spare) + Sharp * RiskAt(spare - (SharpMin + SharpMax) / 2);
        }

        private double RiskAt(double spare)
        {
            var sigma = Noise * Math.Sqrt(2);
            // As RunCalls.Down: the take-and-break when it beats him (a few shy from the ring), else the shy.
            double Take(double extra) => Phi(-(spare + extra) / sigma);
            var take = (1 - WideShare) * Take(Gather) + WideShare * Take(Gather + WideExtra);
            // (Deep area 3: a missed shy is taken by the man over the stumps, like any throw.)
            if (spare < ShyWindow && spare + Gather < 0)
            {
                var pShy = spare + Gather < -EasyMargin ? 0 : ShyFromRing(ThrowDistance);
                return (1 - pShy) * take + pShy * (DirectHit * Phi(-spare / sigma) + (1 - DirectHit) * take);
            }
            if (spare < ShyWindow && spare >= -0.25 - Gather)
            {
                var hit = DirectHit * OnTheRun;
                return hit * Phi(-spare / sigma) + (1 - hit) * take;
            }
            return take;
        }

        /// <summary>
        /// Run-out owner (2026-11-14; owner: "proper run-out is required now" — a run-out comes from a good direct hit or a
        /// fast gather-and-throw beating a run the batters judged on). This share of throws (<see cref="For"/>: by the
        /// fielding side's skill, more from the ring where a swoop-and-flick is on) is a sharp one: the pick-up, the
        /// release and the flat throw in one movement, back <see cref="SharpMin"/>–<see cref="SharpMax"/> s quicker than
        /// the plan the batters read. They never see it coming (the AI's call reads the plan); it's what turns a run they
        /// judged "on" into a run out, and a dive the difference on the close ones. 0 on a hand-built plan.
        /// </summary>
        public double Sharp;

        /// <summary>How much sooner a sharp throw is in (s).</summary>
        public const double SharpMin = 0.2, SharpMax = 0.5;

        /// <summary>The sharp throw's gain for a throw's draw <paramref name="u"/> (the same draw as the wide one: the top <see cref="Sharp"/> of it; the wide ones are the bottom).</summary>
        internal double SharpGain(double u) => Sharp > 0 && u >= 1 - Sharp ? SharpMin + (SharpMax - SharpMin) * (u - (1 - Sharp)) / Sharp : 0;

        /// <summary>The deciding run's throw was a sharp one (<see cref="Sharp"/>) — read only, for the trace and the view.</summary>
        public bool LastThrowSharp => Decided >= 1 && Decided <= Draws.Count && SharpGain(Draws[Decided - 1].over) > 0;

        public static RunPlan For(Field.Play p, Field field, RunKind kind = RunKind.Bat)
        {
            var skill = DartMath.Clamp((field.Fielding - 50) / 50.0, -0.6, 1);
            var dist = Math.Max(1, p.ThrowDistance);
            // Round 11: the throw goes to the end where the run out is likeliest — the one the ball reaches
            // first (both batters are the same time from their creases). The keeper's end unless the bowler's
            // is clearly quicker: shorter by more than the bowler's extra moment to get over his stumps.
            var (x, f) = p.End;
            var toBowler = Math.Max(1, Math.Sqrt(x * x + (f - BowlerEndF) * (f - BowlerEndF)));
            var gain = (dist - toBowler) / ThrowSpeed - BowlerEndExtra;
            // (Never at the cost of the runs they'd safely make: those stay safe at either end.)
            var keeperEnd = kind != RunKind.Bat || gain <= 0 || p.Back - gain - ArrivesAt(p.Runs) < 0.15;
            return new RunPlan
            {
                Kind = kind,
                Back = keeperEnd ? p.Back : p.Back - gain,
                ThrowDistance = keeperEnd ? dist : toBowler,
                // The batters judge the safe runs off the throw they expect (to the keeper).
                Safe = p.Runs,
                // Round 10: a shy hits less often than it did (direct hits were nearly every run out).
                DirectHit = DartMath.Clamp(0.38 - 0.009 * (keeperEnd ? dist : toBowler), 0.05, 0.32) * (0.75 + 0.4 * skill),
                ToKeeperEnd = keeperEnd,
                Fielder = p.Fielder,
                // Run-out owner (2026-11-14): the sharp pick-up-and-throw — a good fielding side's, and from the ring
                // (the swoop and flick at the stumps) more than from the deep (a long throw is a long throw).
                Sharp = DartMath.Clamp(SharpBase + SharpSkill * skill, 0.02, 0.2) * ((keeperEnd ? dist : toBowler) <= SharpRing ? 1 : SharpDeep),
            };
        }

        /// <summary>Normal CDF (Abramowitz–Stegun), for the risk read-out.</summary>
        private static double Phi(double x)
        {
            var t = 1 / (1 + 0.2316419 * Math.Abs(x));
            var d = 0.3989423 * Math.Exp(-x * x / 2);
            var p = d * t * (0.3193815 + t * (-0.3565638 + t * (1.781478 + t * (-1.821256 + t * 1.330274))));
            return x > 0 ? 1 - p : p;
        }
    }
}
