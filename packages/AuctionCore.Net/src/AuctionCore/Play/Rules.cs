using System;

namespace Apl.Core.Play
{
    /// <summary>The laws' side of a ball: extras, the free hit, the lbw shout, the running.</summary>
    public sealed partial class BallResult
    {
        /// <summary>
        /// A no-ball (a run, bowled again); this ball was a free hit; and what he'd
        /// have been out to if it hadn't been one of those ("bowled", "caught"…),
        /// null if nothing.
        /// </summary>
        public bool NoBall, FreeHit;
        public string? Reprieve;

        /// <summary>This ball's place in the over's bouncer count (0: not a bouncer); kept in the save (Extras.cs).</summary>
        public int BouncerNumber;

        /// <summary>The wide's or no-ball's run, byes, leg byes; runs run between the wickets, and how many they went for.</summary>
        public int Penalty, Byes, LegByes, Ran, Attempted;

        /// <summary>Byes to the rope (no running); the keeper fumbled it (byes); he missed a stumping.</summary>
        public bool ExtrasBoundary, KeeperFumble, MissedStumping;

        /// <summary>It struck the pad, and the fielders went up for it.</summary>
        public bool PadHit, Appeal;

        /// <summary>The lbw case (ball-tracking, the umpire's call, any review), when it hit the pad.</summary>
        public LbwCase? Lbw;
        public BallTracking? Tracking => Lbw?.Tracking;

        /// <summary>The race between the wickets (null: nothing to run for — a boundary, a catch, dead at the keeper).</summary>
        public RunPlan? Running;

        /// <summary>The engine's own fielding of the ball (the source of <see cref="Running"/>).</summary>
        public Field.Play? FieldPlay;

        /// <summary>
        /// Run out: the striker (else the non-striker), at the keeper's end (else
        /// the bowler's), and whether a direct hit did it (else the keeper or
        /// bowler gathered and broke the wicket).
        /// </summary>
        public bool RunOut, RunOutStriker, RunOutAtKeeperEnd, DirectHit;

        /// <summary>Who's out (null: nobody) — the striker, or the non-striker run out.</summary>
        public Player? OutBatter;

        public int Extras => Penalty + Byes + LegByes;

        /// <summary>Runs to the batter's name.</summary>
        public int BatRuns => Runs - Extras;

        /// <summary>Counts as one of the over's six.</summary>
        public bool Legal => !Wide && !NoBall && !DeadBall;
    }

    public static partial class Batting
    {
        /// <summary>
        /// One ball: the shot (or the leave) against the delivery, what came of it,
        /// and the laws on top — the no-ball's run, the wide's, the race between
        /// the wickets for anything fielded. The free-hit reprieve and the score
        /// are the innings' (<see cref="QuickMatch"/>).
        /// </summary>
        /// <summary>The push single's race: the ball back this much (s) before to 0.9 s after the batter (− = a run out on).</summary>
        public const double PushTight = -0.2;

        public static BallResult Resolve(Player batter, Player bowler, Delivery d, Trajectory traj, ShotInput shot, Field field, Rng rng)
        {
            var r = ResolveCore(batter, bowler, d, traj, shot, field, rng);
            r.NoBall = d.NoBall;
            r.BouncerNumber = d.BouncerNumber;
            if (r.FieldPlay is Field.Play p && !p.Caught && !p.Boundary && !r.Wicket)
            {
                var plan = RunPlan.For(p, field);
                // A quick single to a push: they set off at once, and it's tight — a shy can find him short.
                if (r.Runs > plan.Safe)
                {
                    plan.Safe = r.Runs;
                    // Round 13: sometimes too tight — the ring fielder's quick to it and the keeper's waiting
                    // (the commonest run out in short games; they were all comfortably made before).
                    // Deep area 3 (dev 2): −0.4 → <see cref="PushTight"/>. With missed shies now taken at the stumps,
                    // and dataset v4's batting/form making more pushes good enough for the quick single (3.75 → 4.69
                    // an innings), the push single alone ran out 0.57 an innings (was 0.36).
                    plan.Back = RunPlan.ArrivesAt(r.Runs) + rng.Range(PushTight, 0.9);
                    plan.DirectHit *= 0.7;
                }
                r.Running = plan;
                r.Ran = r.Attempted = r.Runs;
            }
            if (r.Wide)
            {
                r.Penalty = 1;
                if (r.Runs < 1) r.Runs = 1;
            }
            if (r.NoBall)
            {
                r.Penalty = 1;
                r.Runs += 1;
            }
            // Caught and bowled, the umpire's misses, a batter walking, referrals (Umpiring.cs).
            BowlersEnd.Deflect(r, rng);
            Umpiring.Apply(r, rng);
            return r;
        }

        /// <summary>
        /// Past the bat: onto the pad (an lbw shout, judged on the tracking), into
        /// the stumps, or through to the keeper — who now and then misses it (byes)
        /// or, to spin, has the bails off with the batter out of his ground.
        /// </summary>
        private static void Missed(BallResult r, Player batter, Delivery d, Trajectory traj, ShotInput shot, Field field, bool hitting, Rng rng)
        {
            var offered = !shot.IsLeave;
            var z = PadZ(d, shot);
            var at = traj.At(traj.TimeAt(z));
            var back = shot.BackFoot ?? d.Length >= Length.Short;
            // Hitting, it often beats bat and pad both — through the gate to a drive, under it to a leave.
            if (hitting && rng.Chance(offered ? (d.Length <= Length.Full ? 0.35 : 0.22) : 0.25))
            {
                Out(r, "bowled");
                return;
            }
            var onPad = at.y <= 0.85 && at.x >= -0.3 && at.x <= (back ? 0.15 : 0.22);
            if (onPad)
            {
                r.PadHit = true;
                var lbw = LbwCase.Judge(traj, z, offered, rng);
                r.Lbw = lbw;
                r.Appeal = lbw.Appealed;
                if (lbw.OnFieldOut)
                {
                    Out(r, "lbw");
                    return;
                }
                // Off the pad and away: leg byes, if he played at it and nobody's shouting.
                if (offered && !lbw.Appealed && rng.Chance(0.4))
                {
                    r.Running = new RunPlan
                    {
                        Kind = RunKind.LegByes,
                        Back = RunPlan.ArrivesAt(1) + rng.Range(0.2, 1.6) + (rng.Chance(0.1) ? RunPlan.RunTime : 0),
                        ThrowDistance = 12,
                        Safe = 1,
                        DirectHit = 0.3,
                        ToKeeperEnd = true,
                        Fielder = "keeper",
                    };
                    r.Running.Safe = Math.Max(1, (int)Math.Floor((r.Running.Back - RunPlan.Start) / RunPlan.RunTime));
                    r.Ran = r.Attempted = r.LegByes = r.Runs = r.Running.Safe;
                }
                return;
            }
            if (hitting)
            {
                Out(r, "bowled");
                return;
            }
            if (Stumping(r, batter, d, shot, field, rng)) return;
            Byes(r, d, field, rng);
        }

        /// <summary>How far in front of the stumps the ball meets the pad: a stride forward to a full one, back in the crease to a short one, just out of it leaving.</summary>
        private static double PadZ(Delivery d, ShotInput shot)
        {
            if (shot.IsLeave) return 0.9;
            // Down the track: struck metres out (rarely given lbw from there).
            if (shot.Charged) return 1.22 + Batting.ChargeDistance + 0.6;
            var back = shot.BackFoot ?? d.Length >= Length.Short;
            return back ? 0.55 : d.Length == Length.Yorker ? 1.1 : 1.6;
        }

        /// <summary>
        /// Beaten by spin out of his ground — charging (a loft), stretching forward
        /// (a stroke), rarely on a push — and the keeper, standing up, takes it
        /// cleanly and whips the bails off (or fumbles the chance).
        /// </summary>
        private static bool Stumping(BallResult r, Player batter, Delivery d, ShotInput shot, Field field, Rng rng)
        {
            // Only with the keeper up to the stumps (always to spin, sometimes to a medium pacer), or a charge.
            if ((!field.KeeperUp && !shot.Charged) || shot.IsLeave) return false;
            var outOfCrease = shot.Type == ShotType.Loft ? 0.4 : shot.Type == ShotType.Stroke ? (shot.BackFoot == true ? 0.02 : 0.12) : 0.03;
            outOfCrease *= 1.25 - 0.5 * DartMath.Clamp((batter.Batting - 40) / 55.0, 0, 1);
            // He charged and missed: he's metres out — to spin, the keeper's standing up; to pace,
            // only now and then can the keeper (standing back) get it to the stumps in time.
            if (shot.Charged) outOfCrease = field.KeeperUp ? 0.9 : 0.12;
            if (!rng.Chance(outOfCrease)) return false;
            var keep = DartMath.Clamp((field.Keeping - 50) / 50.0, 0, 1);
            if (!rng.Chance(0.7 + 0.25 * keep))
            {
                r.MissedStumping = true;
                return false;
            }
            r.Fielder = "keeper";
            Out(r, "stumped");
            return true;
        }

        /// <summary>Byes: the keeper misses it — more down the leg side to pace, and to big turn; now and then all the way to the rope.</summary>
        private static void Byes(BallResult r, Delivery d, Field field, Rng rng)
        {
            var keep = DartMath.Clamp((field.Keeping - 50) / 50.0, 0, 1);
            var chance = 0.012 + 0.03 * (1 - keep);
            if (!d.IsSpin && d.Line >= Line.Leg) chance += 0.03;
            if (d.IsSpin && Math.Abs(d.Deviation) > 0.3) chance += 0.015;
            if (!rng.Chance(chance)) return;
            r.KeeperFumble = true;
            if (!d.IsSpin && rng.Chance(0.15))
            {
                r.Byes = r.Runs = 4;
                r.ExtrasBoundary = true;
                return;
            }
            r.Running = new RunPlan
            {
                Kind = RunKind.Byes,
                Back = RunPlan.ArrivesAt(1) + rng.Range(0.3, 2.2) + (rng.Chance(0.15) ? RunPlan.RunTime : 0),
                ThrowDistance = 10,
                DirectHit = 0.25,
                ToKeeperEnd = rng.Chance(0.5),
                Fielder = "keeper",
            };
            r.Running.Safe = Math.Max(1, (int)Math.Floor((r.Running.Back - RunPlan.Start) / RunPlan.RunTime));
            r.Ran = r.Attempted = r.Byes = r.Runs = r.Running.Safe;
        }

        /// <summary>Deep in his crease to a short one, swinging hard, he treads on (or clips) his stumps.</summary>
        private static bool HitWicket(Delivery d, ShotInput shot, Rng rng)
        {
            var back = shot.BackFoot ?? d.Length >= Length.Short;
            if (!back || shot.Type == ShotType.Push || d.Wide) return false;
            return rng.Chance(d.IsSpin ? 0.001 : 0.003);
        }
    }
}
