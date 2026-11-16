using System;
using System.Collections.Generic;

namespace Apl.Core.Play
{
    public enum DeliveryKind { Pace, OffSpin, LegSpin }

    /// <summary>Where it pitches, by distance in front of the batter's stumps.</summary>
    public enum Length { Yorker, Full, Good, Short, Bouncer }

    /// <summary>Line at the batter, from the off side to the leg side.</summary>
    public enum Line { WideOff, OutsideOff, Off, Middle, Leg, WideLeg }

    /// <summary>
    /// What the bowler meant to bowl. Pace: stock, yorker, bouncer, slower ball,
    /// in/outswinger (towards the batter's leg / off side), cutters. Spin: stock
    /// (the stock turn), arm ball (straight on, drifting), the wrong'un (googly
    /// from a wrist spinner, doosra from a finger spinner: turns the other way),
    /// flipper (quicker, skids on low), top-spinner (dips, kicks), carrom ball.
    /// </summary>
    public enum Variation { Stock, Yorker, Bouncer, Slower, Inswinger, Outswinger, OffCutter, LegCutter, ArmBall, Wrong, Flipper, TopSpinner, Carrom }

    /// <summary>
    /// One ball as bowled. Coordinates (metres) are batter-centred: z runs up the
    /// pitch from the batter's stumps (0) to the bowler's (20.12); x is across,
    /// positive towards the batter's off side (mirrored for left-handers by the
    /// view); y is height.
    /// </summary>
    public sealed class Delivery
    {
        public DeliveryKind Kind;
        public double SpeedKmh;
        public Length Length;
        public Line Line;

        /// <summary>Release point.</summary>
        public double ReleaseX, ReleaseY, ReleaseZ;

        /// <summary>Where it pitches (y = 0).</summary>
        public double BounceX, BounceZ;

        /// <summary>Sideways movement in the air before the bounce (swing), metres.</summary>
        public double Swing;

        /// <summary>Sideways movement off the pitch (spin or seam), metres by the batter.</summary>
        public double Deviation;

        /// <summary>Called wide: a run and the ball is bowled again.</summary>
        public bool Wide;

        /// <summary>
        /// A no-ball: overstepped the popping crease, or a full toss above the
        /// waist (<see cref="Beamer"/>). A run, bowled again, and the next ball is
        /// a free hit; he can't be out off it except run out.
        /// </summary>
        public bool NoBall, Beamer;

        /// <summary>A bouncer still climbing over the batter's head at the crease: a no-ball.</summary>
        public bool HeadHigh;

        /// <summary>
        /// The over's bouncer count this one makes (1, 2 …; 0: not a bouncer), and whether it was over the
        /// T20 limit of two an over — a no-ball (Extras.cs). Set by the match, not the bowling physics.
        /// </summary>
        public int BouncerNumber;
        public bool BouncerNoBall;

        /// <summary>The bowling arm and the side of the stumps he comes from (release point and angle follow).</summary>
        public bool LeftArm, AroundWicket;

        /// <summary>What he meant to bowl.</summary>
        public Variation Variation;

        /// <summary>Bounce off the pitch against the usual (a flipper skids on low, a top-spinner kicks).</summary>
        public double BounceFactor = 1;

        /// <summary>
        /// The user's release on the meter, graded (PERFECT / GOOD / EARLY / LATE …); null for the AI bowler's
        /// balls. A good release costs the batter (<see cref="Batting.ReleaseSting"/>): worse contact, beaten more.
        /// </summary>
        public Timing? Release;

        /// <summary>Doesn't pitch before it reaches the batter (its "bounce" is behind the stumps).</summary>
        public bool FullToss => BounceZ < 0.3;

        public double Speed => SpeedKmh / 3.6;
        public bool IsSpin => Kind != DeliveryKind.Pace;
    }

    /// <summary>
    /// The ball's flight, worked out once: a parabola to the bounce, energy lost
    /// in the bounce, then a second parabola to the batter. Deterministic, so the
    /// view shows exactly the ball the outcome is judged on.
    /// </summary>
    public sealed class Trajectory
    {
        public const double Gravity = 9.81;
        public const double PitchLength = 20.12;

        /// <summary>Height of the stumps and half the width of the three of them plus the ball.</summary>
        public const double StumpHeight = 0.711, StumpHalfWidth = 0.115 + 0.036;

        private readonly Delivery _d;
        private readonly double _t1, _vy1, _speed2, _vy2;

        public Trajectory(Delivery d)
        {
            _d = d;
            var v = d.Speed;
            _t1 = (d.ReleaseZ - d.BounceZ) / v;
            _vy1 = (0.5 * Gravity * _t1 * _t1 - d.ReleaseY) / _t1;
            var impact = Math.Abs(_vy1 - Gravity * _t1);
            // Pace skids on and keeps more speed; spin grips, bounces less, slows more.
            _speed2 = v * (d.IsSpin ? 0.88 : 0.9);
            _vy2 = impact * (d.IsSpin ? 0.42 : 0.5) * d.BounceFactor;
        }

        public Delivery Delivery => _d;

        /// <summary>Seconds from release to the bounce.</summary>
        public double BounceTime => _t1;

        /// <summary>Seconds from release until the ball reaches distance <paramref name="z"/> from the stumps.</summary>
        public double TimeAt(double z) => z >= _d.BounceZ
            ? (_d.ReleaseZ - z) / _d.Speed
            : _t1 + (_d.BounceZ - z) / _speed2;

        /// <summary>Ball position <paramref name="t"/> seconds after release.</summary>
        public (double x, double y, double z) At(double t)
        {
            if (t <= _t1)
            {
                var u = t / _t1;
                var z = _d.ReleaseZ - _d.Speed * t;
                var y = _d.ReleaseY + _vy1 * t - 0.5 * Gravity * t * t;
                // Swing: the ball holds its line, then moves late towards the bounce point.
                var x = _d.ReleaseX + (_d.BounceX - _d.ReleaseX) * u - _d.Swing * u * (1 - u);
                return (x, Math.Max(0, y), z);
            }
            var tau = t - _t1;
            var z2 = _d.BounceZ - _speed2 * tau;
            var y2 = _vy2 * tau - 0.5 * Gravity * tau * tau;
            // Off the pitch it carries on along its line, plus the turn or seam.
            var slope = (_d.BounceX - _d.ReleaseX) / (_d.ReleaseZ - _d.BounceZ);
            var travelled = _d.BounceZ - z2;
            var x2 = _d.BounceX + slope * travelled + _d.Deviation * Math.Min(1, travelled / Math.Max(0.5, _d.BounceZ));
            return (x2, Math.Max(0, y2), z2);
        }

        /// <summary>Where the ball is when it reaches the stumps' plane.</summary>
        public (double x, double y) AtStumps()
        {
            var p = At(TimeAt(0));
            return (p.x, p.y);
        }

        /// <summary>Would it hit the stumps if nothing got in the way?</summary>
        public bool HittingStumps()
        {
            var (x, y) = AtStumps();
            return Math.Abs(x) <= StumpHalfWidth && y <= StumpHeight;
        }

        /// <summary>
        /// Points along the flight from <paramref name="from"/> to <paramref name="to"/>
        /// seconds after release, every <paramref name="step"/> s (for Hawk-Eye's path).
        /// </summary>
        public List<(double x, double y, double z)> Sample(double from, double to, double step = 0.02)
        {
            var pts = new List<(double x, double y, double z)>();
            // Bounded, whatever the times (round 13: a red-zone no-ball dug in short left a near-dead ball whose
            // pad impact was minutes away — an lbw tracking path of millions of points stalled the game for 112 s).
            if (double.IsNaN(from) || double.IsInfinity(from)) from = 0;
            if (double.IsNaN(to) || double.IsInfinity(to) || to < from) to = from;
            if (!(step > 0)) step = 0.02;
            if ((to - from) / step > MaxSamples) to = from + MaxSamples * step;
            for (var i = 0; i < MaxSamples; i++)
            {
                var t = from + i * step;
                if (t >= to) break;
                pts.Add(At(t));
            }
            pts.Add(At(to));
            return pts;
        }

        /// <summary>The most points a sampled path may hold (a few seconds of flight at the finest step).</summary>
        public const int MaxSamples = 600;
    }
}
