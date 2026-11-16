using System;
using System.Collections.Generic;

namespace Apl.Core.Play
{
    /// <summary>Where it pitched, against the line of the stumps (a full toss didn't).</summary>
    public enum PitchVerdict { InLine, OutsideOff, OutsideLeg, FullToss }

    /// <summary>Where it struck the pad: in line, in the umpire's-call band, or outside.</summary>
    public enum ImpactVerdict { InLine, UmpiresCall, OutsideOff, OutsideLeg }

    /// <summary>The projected path at the stumps.</summary>
    public enum WicketsVerdict { Hitting, UmpiresCall, Missing }

    /// <summary>
    /// Ball-tracking for an lbw, worked out from the flight (the engine's
    /// <see cref="Trajectory"/> is deterministic, so this is the true path and
    /// the true projection). Batter frame: x across (+ off side), y up, z up the
    /// pitch from the stumps. The presentation draws Hawk-Eye from
    /// <see cref="Path"/> (release → pad) and <see cref="Projected"/> (pad → stumps).
    /// </summary>
    public sealed class BallTracking
    {
        /// <summary>The ball's radius, and the stumps' half-width to the outside of the outer stumps and height to the bottom of the bails.</summary>
        public const double BallRadius = 0.036, StumpsHalfWidth = 0.114, StumpsHeight = 0.711;

        public double PitchX, PitchZ;
        public PitchVerdict Pitching;
        public double ImpactX, ImpactY, ImpactZ, ImpactTime;
        public ImpactVerdict Impact;
        public double StumpsX, StumpsY;
        public WicketsVerdict Wickets;

        /// <summary>The actual flight, release to the pad, and the projection on from the pad to the stumps.</summary>
        public List<(double x, double y, double z)> Path = new List<(double x, double y, double z)>();
        public List<(double x, double y, double z)> Projected = new List<(double x, double y, double z)>();

        /// <summary>Tracks <paramref name="t"/> to the pad at <paramref name="impactZ"/> m in front of the stumps.</summary>
        public static BallTracking Track(Trajectory t, double impactZ)
        {
            var d = t.Delivery;
            var tr = new BallTracking { PitchX = d.BounceX, PitchZ = d.BounceZ, ImpactZ = impactZ };
            tr.Pitching = d.FullToss || d.BounceZ < impactZ ? PitchVerdict.FullToss
                : d.BounceX < -StumpsHalfWidth ? PitchVerdict.OutsideLeg
                : d.BounceX > StumpsHalfWidth ? PitchVerdict.OutsideOff
                : PitchVerdict.InLine;
            tr.ImpactTime = t.TimeAt(impactZ);
            var at = t.At(tr.ImpactTime);
            tr.ImpactX = at.x;
            tr.ImpactY = at.y;
            var ax = Math.Abs(at.x);
            tr.Impact = ax <= StumpsHalfWidth ? ImpactVerdict.InLine
                : ax <= StumpsHalfWidth + BallRadius ? ImpactVerdict.UmpiresCall
                : at.x > 0 ? ImpactVerdict.OutsideOff : ImpactVerdict.OutsideLeg;
            var stumps = t.At(t.TimeAt(0));
            tr.StumpsX = stumps.x;
            tr.StumpsY = stumps.y;
            tr.Wickets = Judge(stumps.x, stumps.y);
            // (A dead, dug-in ball can take "minutes" to reach the pad: the path is capped like the projection.)
            if (double.IsNaN(tr.ImpactTime) || double.IsInfinity(tr.ImpactTime) || tr.ImpactTime > 4) tr.ImpactTime = 4;
            tr.Path = t.Sample(0, tr.ImpactTime);
            // Cap the projection: a near-dead post-bounce ball makes TimeAt(0) huge (millions of
            // samples stalled the game for minutes in r12). Real projections reach the stumps in < 1.5 s.
            var end = t.TimeAt(0);
            if (double.IsNaN(end) || double.IsInfinity(end) || end > tr.ImpactTime + 1.5) end = tr.ImpactTime + 1.5;
            tr.Projected = t.Sample(tr.ImpactTime, end, 0.01);
            return tr;
        }

        /// <summary>
        /// Hitting: at least half the ball inside the zone (outer stumps, bottom of
        /// the bails). Umpire's call: it clips the stumps but less than half of it
        /// is inside. Missing: clear.
        /// </summary>
        public static WicketsVerdict Judge(double x, double y)
        {
            if (Math.Abs(x) <= StumpsHalfWidth && y <= StumpsHeight) return WicketsVerdict.Hitting;
            if (Math.Abs(x) <= StumpsHalfWidth + BallRadius && y <= StumpsHeight + BallRadius) return WicketsVerdict.UmpiresCall;
            return WicketsVerdict.Missing;
        }

        /// <summary>The tracking says out with no umpire's call in it.</summary>
        public bool ClearlyOut(bool shotOffered) =>
            Pitching != PitchVerdict.OutsideLeg
            && (Impact == ImpactVerdict.InLine || (!shotOffered && Impact == ImpactVerdict.OutsideOff))
            && Wickets == WicketsVerdict.Hitting;

        /// <summary>The tracking says not out whatever the umpire said (no umpire's call can save it).</summary>
        public bool ClearlyNotOut(bool shotOffered) =>
            Pitching == PitchVerdict.OutsideLeg
            || Impact == ImpactVerdict.OutsideLeg
            || (shotOffered && Impact == ImpactVerdict.OutsideOff)
            || Wickets == WicketsVerdict.Missing;
    }

    /// <summary>
    /// An lbw shout: the tracking, the umpire's call on the field, and a review
    /// if there is one. Overturn rules (ICC): given out, overturned only when the
    /// tracking is clearly not out (pitched outside leg, struck outside the line —
    /// outside off counts when a shot was offered — or missing); given not out,
    /// overturned only when it's clearly out (in line and hitting, no umpire's
    /// call). Umpire's call leaves the decision standing and the review kept; any
    /// other failed review is lost. One review per side per innings.
    /// </summary>
    public sealed class LbwCase
    {
        public BallTracking Tracking = null!;
        public bool ShotOffered;

        /// <summary>The fielding side went up for it (every pad hit near the line is appealed).</summary>
        public bool Appealed;

        /// <summary>The finger went up.</summary>
        public bool OnFieldOut;

        public bool Reviewed, ReviewedByBatting, Overturned, UmpiresCall, ReviewLost;

        /// <summary>The decision after any review.</summary>
        public bool Out => Reviewed ? OnFieldOut != Overturned : OnFieldOut;

        /// <summary>
        /// The umpire's look: the true flight seen with an eye's errors (where it
        /// pitched, where it hit the pad, where it was going); he gives it when he
        /// sees it pitching in line, struck in line and hitting, with the benefit
        /// of the doubt on the line of the stumps to the batter.
        /// </summary>
        public static LbwCase Judge(Trajectory t, double impactZ, bool shotOffered, Rng rng)
        {
            var tr = BallTracking.Track(t, impactZ);
            var c = new LbwCase { Tracking = tr, ShotOffered = shotOffered };
            var pitchX = tr.PitchX + Bowling.Normal(rng) * 0.025;
            var impactX = tr.ImpactX + Bowling.Normal(rng) * 0.04;
            var stumpsX = tr.StumpsX + Bowling.Normal(rng) * 0.07;
            var stumpsY = tr.StumpsY + Bowling.Normal(rng) * 0.06;
            const double w = BallTracking.StumpsHalfWidth;
            var pitchOk = tr.Pitching == PitchVerdict.FullToss || pitchX >= -w;
            var impactOk = Math.Abs(impactX) <= w || (!shotOffered && impactX > 0);
            var hitting = Math.Abs(stumpsX) <= w * 0.85 && stumpsY <= BallTracking.StumpsHeight * 0.95;
            c.OnFieldOut = pitchOk && impactOk && hitting;
            // Anything near the stumps is shouted for.
            c.Appealed = c.OnFieldOut || (Math.Abs(impactX) < 0.3 && Math.Abs(stumpsX) < 0.35 && stumpsY < 1.0);
            return c;
        }

        /// <summary>Reviewed by the batting side (given out) or the fielding side (given not out): the third umpire's verdict.</summary>
        public void Review(bool byBatting)
        {
            Reviewed = true;
            ReviewedByBatting = byBatting;
            if (OnFieldOut)
            {
                Overturned = Tracking.ClearlyNotOut(ShotOffered);
                UmpiresCall = !Overturned && (Tracking.Impact == ImpactVerdict.UmpiresCall || Tracking.Wickets == WicketsVerdict.UmpiresCall);
            }
            else
            {
                Overturned = Tracking.ClearlyOut(ShotOffered);
                UmpiresCall = !Overturned && !Tracking.ClearlyNotOut(ShotOffered);
            }
            ReviewLost = !Overturned && !UmpiresCall;
        }

        /// <summary>
        /// Would an AI side review? It has a look of its own (worse than the
        /// tracking's): the batter goes upstairs when he thinks it was missing or
        /// outside, the fielders when they think it was plumb.
        /// </summary>
        public bool AiWantsReview(bool battingSide, Rng rng, double stake = 0)
        {
            var tr = Tracking;
            var impactX = tr.ImpactX + Bowling.Normal(rng) * 0.05;
            var stumpsX = tr.StumpsX + Bowling.Normal(rng) * 0.08;
            var stumpsY = tr.StumpsY + Bowling.Normal(rng) * 0.08;
            var pitchX = tr.PitchX + Bowling.Normal(rng) * 0.03;
            const double w = BallTracking.StumpsHalfWidth;
            var looksOut = (tr.Pitching == PitchVerdict.FullToss || pitchX >= -w)
                && (Math.Abs(impactX) <= w || (!ShotOffered && impactX > 0))
                && Math.Abs(stumpsX) <= w && stumpsY <= BallTracking.StumpsHeight;
            // A set batter (or the last pair) reviews a marginal one too.
            return battingSide ? !looksOut || rng.Chance(0.15 + stake) : looksOut && rng.Chance(0.8);
        }
    }
}
