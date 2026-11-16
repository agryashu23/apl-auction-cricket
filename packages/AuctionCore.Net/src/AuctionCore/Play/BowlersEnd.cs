using System;

namespace Apl.Core.Play
{
    public sealed partial class BallResult
    {
        /// <summary>No ball bowled (the non-striker run out backing up): it isn't one of the six.</summary>
        public bool DeadBall;

        /// <summary>A straight drive the bowler got a finger to, onto the stumps at his end.</summary>
        public bool Deflected;
    }

    /// <summary>
    /// Run outs at the bowler's end: the straight drive the bowler deflects onto
    /// the stumps with the non-striker backing up out of his ground, and the
    /// bowler running out a non-striker who leaves too early (before release).
    /// </summary>
    public static class BowlersEnd
    {
        /// <summary>A hard straight drive the bowler gets a fingertip to, and it goes on to the stumps.</summary>
        public const double Deflection = 0.04;

        /// <summary>The non-striker's out of his ground when it hits (else just the stumps broken).</summary>
        public const double BackingUpOut = 0.5;

        /// <summary>Along the ground, within 12° of straight, hit hard: the odd one is deflected onto the stumps.</summary>
        public static void Deflect(BallResult r, Rng rng)
        {
            if (r.Wicket || r.Six || r.Path.InAir || Math.Abs(r.ExitAngle) > 12 || r.ExitSpeed < 18) return;
            if (!rng.Chance(Deflection)) return;
            r.Deflected = true;
            r.Four = false;
            r.Running = null;
            r.Ran = r.Attempted = 0;
            r.Runs = r.Penalty;
            r.Fielder = "bowler";
            r.End = (0, Trajectory.PitchLength);
            if (!rng.Chance(BackingUpOut)) return;
            r.Wicket = true;
            r.HowOut = "run out";
            r.RunOut = true;
            r.RunOutStriker = false;
            r.RunOutAtKeeperEnd = false;
            // Is he in? The TV umpire looks at the bat and the frame the bails come off.
            r.Referral = TvCheck.RunOut;
            r.TvOut = true;
            // (Inside the referral window, whatever it is: deep area 3 narrowed it to 0.02 s.)
            r.Margin = -rng.Range(Running.Refer * 0.2, Running.Refer * 0.9);
        }
    }

    public sealed partial class QuickMatch
    {
        /// <summary>This ball, the non-striker is leaving his crease before the bowler lets go (decided when the ball's set up).</summary>
        public bool NonStrikerBackingUp { get; private set; }

        private int _backingUpFor = -1;

        /// <summary>Once per ball: is the non-striker leaving early (more when chasing)?</summary>
        private void BackingUp()
        {
            if (_backingUpFor == Log.Count) return;
            _backingUpFor = Log.Count;
            NonStrikerBackingUp = _rng.Chance(Chasing ? 0.05 : 0.03);
        }

        /// <summary>The AI bowler runs him out for it (rare): call before the delivery, and if true, <see cref="Mankad"/> instead.</summary>
        public bool AiMankads() => NonStrikerBackingUp && UserBatting && _rng.Chance(0.02);

        /// <summary>
        /// The bowler stops in his delivery stride and breaks the stumps with the
        /// non-striker out of his ground: run out, no ball bowled. False if he
        /// wasn't backing up too far (the bowler just aborts, a dead ball isn't
        /// logged).
        /// </summary>
        public BallResult? Mankad()
        {
            if (Finished || !NonStrikerBackingUp) return null;
            var traj = NextDelivery();
            var r = new BallResult
            {
                Delivery = traj.Delivery, Trajectory = traj, Shot = ShotInput.Leave, Timing = Timing.Leave,
                DeadBall = true, Wicket = true, HowOut = "run out", RunOut = true, RunOutStriker = false, RunOutAtKeeperEnd = false,
                Fielder = "bowler", FreeHit = FreeHit,
            };
            var bowler = CurrentBowler;
            _pending = null;
            _pendingTrajectory = null;
            NonStrikerBackingUp = false;
            _memo = Snapshot();
            Apply(r, bowler);
            return r;
        }
    }
}
