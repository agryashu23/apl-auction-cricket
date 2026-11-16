using System;

namespace Apl.Core.Play
{
    public sealed partial class BallResult
    {
        /// <summary>They set off for a run and were sent back (it doesn't count); they dived for the last one.</summary>
        public bool TurnedBack, Dived;

        /// <summary>The batter on strike for this ball (the view books simulated shots under him).</summary>
        public Player? Facing;

        /// <summary>A run out: when the wicket was broken (s after the hit) — a direct hit's arrival, or the take and the whip of the bails.</summary>
        public double BrokenAt;

        /// <summary>
        /// The deciding run's race as it fell (s after the hit; NaN: no race): the ball in the hands over
        /// the stumps (or a shy reaching them, hit or miss), and the bat grounded (a dive included).
        /// <c>BatIn − BallIn</c> is how much the ball beat him by — the run-out trace and the chance
        /// tests read it (deep area 3: no batter given home when the ball beat him by over 0.3 s).
        /// </summary>
        public double BallIn = double.NaN, BatIn = double.NaN;

        /// <summary>The deciding run's throw was a shy at the stumps (hit or missed).</summary>
        public bool Shied;

        /// <summary>
        /// The shy on the last run they went for missed and ran away: an overthrow run is on (counted for the AI and
        /// AUTO; the player's MANUAL RUN calls it — <see cref="Running.Settle"/>'s overthrowCalls).
        /// </summary>
        public bool Overthrow;
    }

    /// <summary>
    /// Settling the runs: how many they went for (the user's RUN / CANCEL / DIVE,
    /// or the AI's call), how many they made, and whether one of them was run
    /// out. The race's draws are kept on the <see cref="RunPlan"/>, so a rework
    /// (a dive on the final approach, a turn-back) replays the same race.
    /// </summary>
    public static class Running
    {
        /// <summary>
        /// A run out (or a let-off) this close goes upstairs (s). Deep area 3, PM decision D1: 0.1 → 0.02 — at 0.1
        /// a 20-over innings had 0.85 run-out referrals (each third-umpire show 8–10 s); real T20 has ~0.3–0.5 a
        /// match, which 0.02 gives. Out by more is given on the field (then the side-on wicket replay); not out by up to 0.3 s gets
        /// the client's "just in" replay. (Stumpings and catches have their own rules.) Run-out owner (2026-11-13): with the
        /// quicker take-and-break more races end within a whisker (0.50 a match at 0.02), so 0.018 (~0.45).
        /// </summary>
        public const double Refer = 0.018;

        /// <summary>
        /// The AI's call: the safe runs, and the extra one when it's close and the game calls for it.
        /// Batters judge the extra run against the throw being taken and the bails whipped off
        /// (<see cref="RunPlan.Gather"/>), not against the ball reaching the stumps — and they misjudge
        /// it now and then (the usual run out: a good throw, the keeper's gloves, the bat a foot short).
        /// </summary>
        public static int AiAttempt(RunPlan p, double aggression, Rng rng)
        {
            var k = p.Safe;
            // Run-out owner (2026-11-13): the last of the engine's "safe" runs can be a 50-50 (the throw due with him,
            // and the take-and-break now as quick as it really is). Read that tight, they often settle for one fewer
            // ("no!"); misread, they go and the throw decides it — the misjudged single that T20 run outs are made of.
            if (k >= 1 && p.Spare(k) + RunPlan.Gather + Bowling.Normal(rng) * Misjudge < Decline && rng.Chance(DeclineShare - 0.3 * aggression)) return k - 1;
            if (k >= 3) return k;
            var spare = p.Spare(k + 1);
            var judged = spare + RunPlan.Gather + Bowling.Normal(rng) * Misjudge;
            if (judged > Margin - 0.15 * aggression && rng.Chance(ChanceShare + 0.35 * aggression)) k++;
            return k;
        }

        /// <summary>
        /// How far off (s, one sigma) a batter's read of a tight extra run is, and the margin he wants on
        /// it. (Deep area 3: once missed shies stopped gifting easy chances, run outs per 20-over innings
        /// rose from 0.8 to 1.1; a slightly truer read and a little more caution bring them back to ~0.8. With the
        /// slower, clip-true pick-ups — Outfield.Pickup 0.75, DiveRecover 1.85 — the extra run looks on more often
        /// and they rose to 1.05 again: the margin 0.16 → 0.26 s brings them back to ~0.8. Run-out owner, 2026-11-13: with
        /// the take-and-break <see cref="RunPlan.Gather"/> 0.3 → 0.15 the read moved by 0.15 s and the extra run was almost
        /// never chanced (0.97 → 0.24 an innings); on the 2026-11-13 30-yard ring (fewer tight singles) margin 0.26 → 0.18,
        /// with tight "safe" runs sometimes turned down (<see cref="Decline"/>): extra runs ~0.75 an innings, run outs ~0.77 for
        /// the AI side and ~0.78 for the user's real (AUTO) running, every chance with the ball in the hands ≥ 0.15 s before the
        /// bat converted.) Run-out owner, 2026-11-14 (owner: "proper run-out is required now"; 0 AI run outs in ~11 overs of
        /// real play, every AI run a comfortable single): the extra run was a misread coin flip (41% of them run out) and rare.
        /// Now the batters read it truer (Misjudge 0.16 → 0.10, Margin 0.18 → 0.16) and chance it more often (0.35 → 0.45 +
        /// 0.35 × aggression), so the risky second is seen and mostly made; the run outs come from the fielding — a direct hit or
        /// a sharp pick-up-and-throw (RunPlan.Sharp) beating a run they judged on — and tight last runs are turned down a
        /// little more (0.6 → 0.75 − 0.3 × aggression). See RunOutRateTests for the rates.
        /// </summary>
        public const double Misjudge = 0.10, Margin = 0.16;

        /// <summary>A safe run read tighter than this (s, the take and the break included) is often turned down (<see cref="AiAttempt"/>).</summary>
        public const double Decline = 0.1;

        /// <summary>Run-out owner (2026-11-14): how often (before aggression) a tight safe run is turned down, and a readable extra run chanced.</summary>
        public const double DeclineShare = 0.75, ChanceShare = 0.45;

        /// <summary>
        /// The moment (s) after grounding the bat on one run that a batter is committed to the next: with the ball
        /// in the hands at the danger end by then, he doesn't go (deep area 3).
        /// </summary>
        public const double Commit = 0.3;

        /// <summary>
        /// When the wicket's broken at the danger end on run <paramref name="k"/> (s after the hit), or
        /// null if a shy missed. Round 10 (owner: "only direct hits run anyone out"): the fielder reads
        /// the race. If a throw to the keeper / bowler, taken and the bails whipped off
        /// (<see cref="RunPlan.Gather"/>, a wide one a beat longer), beats the batter, that's what he
        /// does — the usual run out — though from the ring he sometimes shies anyway. Only when the
        /// take would be too late does he shy as the last chance (a direct hit or nothing).
        /// </summary>
        /// <param name="ballIn">When the ball is in the hands over the stumps (a wide one: once he has it), or a shy gets there.</param>
        /// <param name="shied">The throw was a shy at the stumps.</param>
        private static double? Down(RunPlan p, int k, double spare, Rng rng, ref bool directHit, out bool overthrow, out double ballIn, out bool shied)
        {
            var (_, nd, shy, over) = p.Draw(k, rng);
            overthrow = false;
            shied = false;
            var gather = RunPlan.GatherFor(over);
            // (Run-out owner, 2026-11-14: a sharp pick-up-and-throw is in quicker than the plan — RunPlan.Sharp — and the
            // fielder, ball in hand that much sooner, reads the race as it now is.)
            var quick = p.SharpGain(over);
            spare -= quick;
            var at = p.Back + nd * RunPlan.Noise - quick;
            // Taken: in his hands once he has it (a wide one a beat later), the bails off a whip after.
            ballIn = at + gather - RunPlan.Gather;
            var tight = spare < RunPlan.ShyWindow;
            // Deep area 3 (owner: "run-out misses too much"): a shy that misses goes on to the man over the
            // stumps at that end (he's there for the throw), who takes it like any throw and breaks the wicket
            // — it used to count as "missed, they're home" even with the batter yards short (0.4 an innings of
            // easy chances given not out). Only a miss that also beats him, with the batter home anyway on the
            // take, runs away for an overthrow.
            var takeBeats = nd * RunPlan.Noise + gather < -spare;
            if (tight && spare + RunPlan.Gather < 0)
            {
                // The take beats him: to the man over the stumps — unless, when it's close, he shies from
                // the ring. A batter well short (the take with time to spare) is always taken and broken.
                var pShy = spare + RunPlan.Gather < -RunPlan.EasyMargin ? 0 : RunPlan.ShyFromRing(p.ThrowDistance);
                if (shy >= pShy) return at + gather;
                shied = true;
                if (shy / pShy < p.DirectHit)
                {
                    directHit = true;
                    ballIn = at;
                    return at;
                }
                // Missed: past the stumps into the gloves / hands of the man over them, and broken.
                return at + gather;
            }
            if (tight && spare >= -0.25 - RunPlan.Gather)
            {
                // Only a direct hit can do it (as he reads it): he shies on the run, off balance (a lower hit
                // rate than a set shy from the ring). A miss is taken at the stumps — out only if the throw
                // came in quicker than he read it — and now and then runs away for an overthrow.
                shied = true;
                if (shy >= p.DirectHit * RunPlan.OnTheRun)
                {
                    if (over < 0.12 && !takeBeats)
                    {
                        overthrow = true;
                        return null;
                    }
                    return at + gather;
                }
                directHit = true;
                ballIn = at;
                return at;
            }
            return at + gather;
        }

        /// <summary>
        /// They went for <paramref name="attempted"/> runs, diving for the last if
        /// <paramref name="dive"/> (a timely dive is <see cref="RunPlan.DiveGain"/> s
        /// sooner). Each run completes unless the ball beats them to the danger end.
        /// </summary>
        /// <paramref name="late"/>: they set off this many seconds after they could have (the user's
        /// RUN came late), so every run arrives that much later.
        /// <paramref name="refuse"/>: batters don't set off for another run with the ball already in at that end
        /// (<see cref="Commit"/>) — the AI's call; false for the player's own RUN, which is run as called.
        /// <paramref name="hold"/>: (the player's MANUAL RUN) they stood this long at the crease before setting off on the last
        /// run — called once the run before was done, the ball still live (owner, 2026-11-13: RUN stays live until the ball's back).
        /// <paramref name="overthrowCalls"/>: −1, an overthrow's run is taken (the AI, AUTO); else the player's call (0 or 1).
        public static void Settle(BallResult r, int attempted, Rng rng, bool dive = false, double late = 0, bool refuse = true, double hold = 0, int overthrowCalls = -1)
        {
            var p = r.Running;
            if (p == null) return;
            attempted = Math.Max(0, Math.Min(4, attempted));
            Reset(r, attempted, dive);
            var made = attempted;
            var overthrows = 0;
            var closest = double.MaxValue;
            // The run before's race (for a refusal: they stay where they are).
            double prevRaw = double.NaN, prevBallIn = double.NaN, prevBatIn = double.NaN, prevMargin = double.MaxValue;
            bool prevShied = false, prevHit = false;
            for (var k = 1; k <= attempted; k++)
            {
                var raw = p.Arrives(k) + Math.Max(0, late) + (k == attempted ? Math.Max(0, hold) : 0) + p.Draw(k, rng).arrive * RunPlan.Noise;
                var arrive = raw - (dive && k == attempted ? RunPlan.DiveGain : 0);
                var hit = r.DirectHit;
                // The fielder reads the race as it is: a late call (the user's RUN pressed late)
                // changes it (it used to read the plan's spare, so a batter yards short on a late call drew a
                // last-chance shy — and a miss let him home). Deep area 3: and how quick the batter really is
                // (his arrival draw — a slow one is seen to be slow; it read the plan's time and shied at a man
                // yards short, 114 of 121 easy chances given not out in 300 innings).
                var down = Down(p, k, p.Back - raw, rng, ref hit, out var overthrow, out var ballIn, out var shied);
                // Deep area 3 (Unity review: "out" on run 2 with the ball back before run 1 was finished — he never
                // left his ground): nobody sets off for another run with the ball already in the hands at the end
                // he'd be running to. They stay; the runs so far stand.
                if (refuse && k > 1 && ballIn <= prevRaw + Commit)
                {
                    p.Decided = k - 1;
                    made = k - 1;
                    r.Attempted = k - 1;
                    r.DirectHit = prevHit;
                    r.BallIn = prevBallIn;
                    r.BatIn = prevBatIn;
                    r.Shied = prevShied;
                    p.LastMargin = prevMargin;
                    break;
                }
                p.Decided = k;
                prevRaw = raw;
                prevHit = hit;
                prevBallIn = ballIn;
                prevBatIn = arrive;
                prevShied = shied;
                prevMargin = down == null ? double.MaxValue : down.Value - raw;
                r.DirectHit = hit;
                // The deciding run's race (the last one they went for, or the one he's out on).
                r.BallIn = ballIn;
                r.BatIn = arrive;
                r.Shied = shied;
                if (down == null)
                {
                    if (k == attempted && overthrow)
                    {
                        r.Overthrow = true;
                        overthrows = overthrowCalls < 0 ? 1 : Math.Min(1, overthrowCalls);
                    }
                    if (k == attempted) p.LastMargin = double.MaxValue;
                    continue;
                }
                if (k == attempted || down < arrive) p.LastMargin = down.Value - raw;
                if (Math.Abs(down.Value - arrive) < Math.Abs(closest)) closest = down.Value - arrive;
                if (down >= arrive) continue;
                made = k - 1;
                r.BrokenAt = down.Value;
                // On odd runs the striker heads for the bowler's end; the man heading for the danger end is out.
                OutAt(r, p, strikerOut: (k % 2 == 1) != p.ToKeeperEnd);
                break;
            }
            Finish(r, p, made + overthrows, closest);
        }

        /// <summary>
        /// CANCEL early in run <paramref name="k"/>, <paramref name="secondsIn"/> s
        /// after they set off on it: they turn and go back to the creases they left.
        /// The run doesn't count, and the man going back to the danger end can still
        /// be beaten by the throw.
        /// </summary>
        public static void TurnBack(BallResult r, int k, double secondsIn, Rng rng)
        {
            var p = r.Running;
            if (p == null || k < 1) return;
            Reset(r, k - 1, false);
            r.TurnedBack = true;
            var home = RunPlan.ArrivesAt(k - 1) + 2 * secondsIn + RunPlan.TurnTime + p.Draw(k, rng).arrive * RunPlan.Noise;
            var hit = false;
            var down = Down(p, k, p.Back - home, rng, ref hit, out _, out var ballIn, out var shied);
            p.Decided = k;
            r.DirectHit = hit;
            r.BallIn = ballIn;
            r.BatIn = home;
            r.Shied = shied;
            var closest = double.MaxValue;
            if (down != null)
            {
                p.LastMargin = closest = down.Value - home;
                // Going back, each heads for the end he left: on an odd run the striker for his own.
                if (down < home)
                {
                    r.BrokenAt = down.Value;
                    OutAt(r, p, strikerOut: (k % 2 == 1) == p.ToKeeperEnd);
                }
            }
            Finish(r, p, k - 1, closest);
        }

        private static void Reset(BallResult r, int attempted, bool dive)
        {
            r.Attempted = attempted;
            r.Dived = dive;
            r.TurnedBack = r.RunOut = r.DirectHit = r.Shied = r.Overthrow = false;
            r.BrokenAt = 0;
            r.BallIn = r.BatIn = double.NaN;
            r.Running!.LastMargin = double.MaxValue;
            r.Running.Decided = 0;
            if (r.Referral == TvCheck.RunOut) r.Referral = TvCheck.None;
        }

        private static void OutAt(BallResult r, RunPlan p, bool strikerOut)
        {
            r.RunOut = true;
            r.RunOutAtKeeperEnd = p.ToKeeperEnd;
            r.RunOutStriker = strikerOut;
        }

        private static void Finish(BallResult r, RunPlan p, int ran, double closest)
        {
            r.Ran = ran;
            r.Byes = p.Kind == RunKind.Byes ? r.Ran : 0;
            r.LegByes = p.Kind == RunKind.LegByes ? r.Ran : 0;
            r.Runs = r.Penalty + r.Ran;
            // Close either way: the TV umpire's call.
            if (closest != double.MaxValue && Math.Abs(closest) < Refer)
            {
                r.Referral = TvCheck.RunOut;
                r.TvOut = r.RunOut;
                r.Margin = closest;
            }
            if (r.RunOut)
            {
                r.Wicket = true;
                r.HowOut = "run out";
                r.Fielder = p.Fielder;
            }
            else if (r.HowOut == "run out")
            {
                // A rework of an earlier call (a dive that made it, a CANCEL in time).
                r.Wicket = false;
                r.HowOut = null;
            }
        }
    }
}
