using System;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Bat meets ball where the clip's bat is. The engine grades the tap against
    /// one contact distance per length (0.75 m from the stumps for a short ball),
    /// but every shot clip meets the ball at its own sweet spot (a drive 1.84 m
    /// out, a punch nearer the crease) and on its own line — so with the engine's
    /// moment the bat swung through empty air and the ball jumped onto it. Here
    /// the swing is timed to the moment the ball reaches the clip's sweet spot
    /// (the tap's error kept: in full for a miss, a quarter of it for a clean hit,
    /// so a timed shot looks timed) and he steps across to the ball's line
    /// during the swing.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>Feet: his guard is this far to leg of middle (engine x, either hand).</summary>
        private const double GuardX = -0.26;

        /// <summary>Furthest he moves across to the line during a swing (m).</summary>
        private const float MaxReach = 0.45f;

        /// <summary>Trajectory time the ball leaves the bat (or passes it) this ball.</summary>
        private double _batMeets;

        /// <summary>Trajectory time the ball itself reaches the clip's sweet spot (the swing lands at <see cref="_batMeets"/>).</summary>
        private double _ballMeets;

        /// <summary>Furthest the ball may leave off the swung clip's line before another clip swings it (°).</summary>
        private const double FollowAfter = 15;

        /// <summary>
        /// A mistimed (early / late) ball leaves off the chosen clip's line: then the clip that goes
        /// where the ball actually went, if it's nearer — the bat swings the way the ball goes.
        /// </summary>
        private static ShotClip FollowBall(ShotClip clip, double exitDeg, Func<double, ShotClip?> pick)
        {
            if (AngleOff(clip.Angle, exitDeg) <= FollowAfter) return clip;
            var other = pick(exitDeg);
            return other != null && AngleOff(other.Angle, exitDeg) < AngleOff(clip.Angle, exitDeg) ? other : clip;
        }

        /// <summary>Degrees between two directions (either convention: 0–360 or signed).</summary>
        private static double AngleOff(double a, double b) { var d = Math.Abs(((a - b) % 360 + 360) % 360); return d > 180 ? 360 - d : d; }

        /// <summary>The ball's height (m) as it reaches him, about where a shot meets it (1.5 m in front of the stumps).</summary>
        private static float BallHeightAtBat(Trajectory f) => (float)f.At(f.TimeAt(1.5)).y;

        /// <summary>
        /// When the bat meets the ball for <paramref name="clip"/> tapped at
        /// <paramref name="tap"/> (trajectory s), and the striker sent across to its line.
        /// </summary>
        private double Meet(ShotClip clip, double tap, bool clean, bool onBat = false)
        {
            var f = _flight!;
            var lh = _striker.LeftHanded;
            // The clip's origin is between his feet: at his guard (GuardBack behind the popping crease), or wherever
            // his footwork took him (down the track, across) — MatchDirector.Footwork.cs.
            var shift = _striker.Shift;
            var sx = lh ? -shift.x : shift.x;
            var sz = shift.z;
            // Charged but playing an ordinary shot (the down-the-track ones carry him themselves): the
            // swing takes him the rest of the way to where he meets it.
            var extraZ = _striker.Charged && clip.Family != "down the track" ? Math.Max(0, Batting.ChargeDistance - sz) : 0;
            var meet = f.TimeAt(_striker.HomeZ + sz + extraZ + clip.Sweet.z);
            var err = tap - Batting.IdealTap(f, _striker.Charged);
            // Round 10: when the engine has bat on ball, the swing lands as the ball reaches the sweet spot (the
            // timing shows in where it goes and in the stamp); the old quarter of the tap's error left the ball
            // up to 4.7 m off the bat at the contact frame, then drawn onto it.
            var at = onBat ? Math.Max(tap + 0.07, meet) : Math.Max(tap + 0.07, meet + err * (clean ? 0.25 : 1.0));
            var p = f.At(meet);
            var across = Mathf.Clamp((float)(p.x - (GuardX + sx + clip.Sweet.x)), -MaxReach, MaxReach);
            _striker.Reach(new Vector3(lh ? -across : across, 0, (float)extraZ), (float)((at - tap) / _pace));
            _ballMeets = meet;
            return at;
        }

        /// <summary>Trajectory s before the meeting over which the ball is drawn onto the blade.</summary>
        private const double OntoBat = 0.07;

        /// <summary>
        /// The ball after the tap, when the engine says bat hit ball (a hit, an edge, a catch): drawn
        /// so it is ON the blade at the swing's contact frame. The swing lands at
        /// <see cref="_batMeets"/>, but the ball reaches the clip's sweet spot at
        /// <see cref="_ballMeets"/> — later for an early tap, earlier for a late one (the swing can't
        /// be quicker than its last 0.07 s), and a short ball's height is never quite the clip's: the
        /// round-8 capture had the two 0.57–0.82 m apart at contact on a third of the hits (the hit
        /// ball then jumped onto the bat). So from the tap its clock is eased to arrive at the sweet
        /// spot as the bat does (held where it is if it is already past it), and over the last
        /// <see cref="OntoBat"/> s it is drawn onto the bat's middle, where the hit then starts.
        /// </summary>
        private void SetBallMeeting(Trajectory f, double t, double tap, bool lh)
        {
            var at = _batMeets;
            var w0 = Math.Max(tap, Math.Min(at, _ballMeets) - 0.15);
            double shown;
            if (t <= w0) shown = t;
            else if (_ballMeets > w0) shown = w0 + (Math.Min(t, at) - w0) * (_ballMeets - w0) / Math.Max(1e-4, at - w0);
            else shown = w0;
            SetBall(f, shown, lh);
            if (!_striker.UsesClips) return;
            // Round 10b: meet half-way, ACROSS the ball's line only. Along its line the ball needs no help (its
            // clock above brings it to the sweet spot as the bat gets there; what is left is under one frame of
            // its flight). Across it — the clip's bat is a little high, wide or short of the ball's line — over
            // the last BatReach s the BAT is drawn onto the line (up to MaxBatNudge, the hands with it:
            // BatterRig.BatNudge), and only what is still left is drawn on the ball, over the last OntoBat s.
            var own = _ball.position;
            Vector3 P(double tt) { var q = f.At(tt); return GroundSpace.Ball(q.x, q.y + 0.036, q.z, lh); }
            var meetAt = P(Math.Max(_ballMeets, tap));                              // the ball where it meets the bat
            var ahead = P(Math.Max(_ballMeets, tap) + 0.01) - meetAt;
            var dir = ahead.sqrMagnitude > 1e-8f ? ahead.normalized : Vector3.forward;
            // Where the clip's bat will be at contact (its track, from where he stands now), not where it is mid-swing.
            var clipName = _shotName;
            var shot = clipName != null ? CricketClips.Find(clipName) : null;
            var batAt = shot != null ? _striker.TrackMiddleAt(shot.Name, shot.ContactTime) : null;
            var clipBat = batAt ?? (_striker.BatMiddle - _striker.BatNudgeNow);
            var across = Vector3.ProjectOnPlane(meetAt - clipBat, dir);         // the bat onto the ball's line
            var kb = Mathf.SmoothStep(0f, 1f, (float)((t - (at - BatReach)) / BatReach));
            _striker.BatNudge = Vector3.ClampMagnitude(across, MaxBatNudge) * kb;
            var left = across - _striker.BatNudgeNow;                              // still off the line after the bat's move
            var k = Mathf.SmoothStep(0f, 1f, (float)((t - (at - OntoBat)) / OntoBat));
            left = -Vector3.ProjectOnPlane(left, dir);
            if (k > 0f) _ball.position = own + left * k;
            _ballDrawn = Mathf.Max(_ballDrawn, k * left.magnitude);
        }

        /// <summary>Trajectory s before the meeting over which the bat is drawn toward the ball, and how far at most (m).</summary>
        private const double BatReach = 0.16;
        private const float MaxBatNudge = 0.45f;

        /// <summary>This ball: the most the ball was drawn off its own path onto the blade (m; the capture logs it).</summary>
        private float _ballDrawn;

        /// <summary>The engine's verdict at the tap: the ball meets the bat (a hit, an edge, a catch; not a miss, wide or bowled/lbw).</summary>
        private static bool BatOnBall(BallResult r) =>
            r.ExitSpeed > 0 && !(r.Wicket && !r.Caught && !r.RunOut) && !r.Wide && r.Timing != Timing.Leave && r.Timing != Timing.Miss;
    }
}
