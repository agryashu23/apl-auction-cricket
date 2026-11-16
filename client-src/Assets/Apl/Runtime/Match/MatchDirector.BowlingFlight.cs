using System.Collections;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The user's ball in the air: the marker at the real pitch point, the flight,
    /// and the AI batter — his shot is chosen at release (<see cref="QuickMatch.AiShot"/>)
    /// and settled at his tap so the clip played sends the ball where it goes.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private IEnumerator BowledFlight(Trajectory trajectory, Player batter)
        {
            var d = trajectory.Delivery;
            var lh = batter.LeftHanded;
            _pace = BattingPrefs.Factor(d);
            var p0 = trajectory.At(0);
            _pitchMarker.Emphasis(1f);
            _pitchMarker.AimStyle(false);
            if (!d.FullToss) _pitchMarker.Show(GroundSpace.Ball(d.BounceX, 0.012, d.BounceZ, lh), 0.1f);
            else _pitchMarker.Hide(0.1f);
            var contactAt = trajectory.TimeAt(Batting.ContactZ(d.Length));
            _contactAt = contactAt;
            _flight = trajectory;
            _released = Time.time;
            Released(d, RealAt(contactAt) - _released);
            _leaveShown = false;
            _hud.ShowBall(d);
            _releaseOffset = _ballView.InHand ? _ball.position - GroundSpace.Ball(p0.x, p0.y + 0.036, p0.z, lh) : Vector3.zero;
            if (_releaseOffset.magnitude > 1.5f) _releaseOffset = Vector3.zero;
            _ballView.Release(d, lh);
            SetBall(trajectory, 0, lh);
            _trail.Clear();
            _trail.emitting = false;
            _striker.ShapeUp((float)(Batting.IdealTap(trajectory) / _pace), d.Length >= Length.Short);

            // What he'll do with it.
            var shot = _match.AiShot();
            var window = Batting.Window(batter, d);
            // Test runs with -aplnudges: the evidence ball's shot (MatchDirector.TestNudges).
            if (Nudges || CatchLoop) shot = EvidenceShot(shot, trajectory, window); // (-aplcatchloop: MatchDirector.CatchLoop)
            shot = KeeperLoopShot(shot, trajectory, window); // -aplkeeperloop only (MatchDirector.KeeperRead)
            // He's down the track for it (batting's footwork), so the stumping's seen out of his ground.
            if (Nudges && _evidence == Evidence.Stumping && _striker.UsesClips) _striker.Charge();
            var decideAt = contactAt + 3.5 * window;
            StartKeeperRead(trajectory, lh, shot.IsLeave, contactAt); // the keeper reads it from the hand (MatchDirector.KeeperRead)
            while (true)
            {
                var t = FlightNow;
                SetBall(trajectory, KeeperFlight(t), lh); // (into the keeper's gloves once there)
                if (t >= trajectory.BounceTime) _pitchMarker.Hide(0.12f);
                // Spin: a gold streak off the pitch, so the turn reads (the trail's back to white for the hit).
                if (d.IsSpin && !d.FullToss && t >= trajectory.BounceTime && !_trail.emitting) TurnTrail(true);
                if (!shot.IsLeave && _tap == null && t >= shot.TapTime) AiSwing(shot, d);
                if (_tap != null && t >= _batMeets) break;
                if (_tap == null && !_leaveShown && t >= contactAt - AutoLeaveLead)
                {
                    _leaveShown = true;
                    _striker.Leave((float)((contactAt - t) / _pace));
                }
                if (_tap == null && t >= decideAt) break;
                // Left alone and in the keeper's gloves: settled now (it used to fly on through him: MatchDirector.KeeperRead).
                if (_tap == null && shot.IsLeave && KeeperHasIt) break;
                // Onto the pad / into the stumps by the next frame: settled now, so it stops there (MatchDirector.KeeperRead).
                if ((_tap == null ? shot.IsLeave : _result != null) && StopsNext(trajectory, t, shot.IsLeave)) break;
                yield return null;
            }
            var r = _result ?? _match.Play(ShotInput.Leave);
            _result = null;
            LogOutcome(r, "ai bats");   // [APL outcome] (MatchDirector.HitFeel.cs)
            TurnTrail(false);
            // The bar waits for the outcome (and any review) when the ball ends the innings or takes a
            // wicket: "Innings complete" used to go up before the DRS review was played (133–140).
            if (!_match.Finished && !r.Wicket) _hud.ShowScore(_match, BattingSide, FieldingSide);
            EvidenceMoment(r);
            _evidence = Evidence.None;
            yield return Outcome(r, FlightNow, lh, batter.Name);
            _hud.ShowScore(_match, BattingSide, FieldingSide);
        }

        private (Color start, Color end, float time, float width)? _trailColours;

        /// <summary>The ball's trail gold from the bounce (a spinner's turn made visible), or back to its own colours.</summary>
        private void TurnTrail(bool on)
        {
            _trailColours ??= (_trail.startColor, _trail.endColor, _trail.time, _trail.startWidth);
            if (on)
            {
                _trail.Clear();
                _trail.startColor = new Color(1f, 0.8f, 0.25f, 0.85f);
                _trail.endColor = new Color(1f, 0.8f, 0.25f, 0f);
                _trail.time = 0.45f;
                _trail.startWidth = 0.06f;
                _trail.emitting = true;
                return;
            }
            _trail.startColor = _trailColours.Value.start;
            _trail.endColor = _trailColours.Value.end;
            _trail.time = _trailColours.Value.time;
            _trail.startWidth = _trailColours.Value.width;
        }

        /// <summary>The AI batter's tap: the outcome is settled now and the clip picked to match it (as the user's OnShot does).</summary>
        private void AiSwing(ShotInput shot, Delivery d)
        {
            _tap = shot.TapTime;
            // The clip first, then the outcome along its line (as the user's OnShot does).
            var clip = _striker.UsesClips ? CricketClips.PickShot(shot.Type, Field.ZoneAngle(shot.Direction), d.Length, d.IsSpin, _visRng, shot.BackFoot, shot.Special, BallHeightAtBat(_flight!)) : null;
            if (clip != null) shot.AimDeg = clip.Angle > 180 ? clip.Angle - 360 : clip.Angle;
            _result = _match.Play(shot);
            var r = _result;
            var clean = r.ExitSpeed > 0 && !r.Edge && !r.Wide && !(r.Wicket && !r.Caught && !r.RunOut) && r.Timing != Timing.Miss;
            if (clean && clip != null) clip = FollowBall(clip, r.ExitAngle, c => CricketClips.PickShot(shot.Type, c, d.Length, d.IsSpin, _visRng, shot.BackFoot, shot.Special, BallHeightAtBat(_flight!)));
            // An edge or a miss plays batting's edge / play-and-miss clips (as the user's OnShot does).
            var onBat = BatOnBall(r);
            if (clip != null && !r.Wide && (r.Edge || !onBat))
            {
                var edgeClip = CricketClips.EdgeClip(r.Edge, !onBat && !r.Edge, r.Edge && r.Wicket && r.HowOut == "bowled", r.Edge && r.Launch > 30, r.ExitAngle, d.Line <= Line.Off);
                if (edgeClip != null) clip = edgeClip;
            }
            var angle = clean ? r.ExitAngle : shot.AimDeg ?? Field.ZoneAngle(shot.Direction);
            _batMeets = clip != null ? Meet(clip, shot.TapTime, clean) : shot.TapTime + Batting.SwingTime;
            var contactIn = (float)((_batMeets - shot.TapTime) / _pace);
            var dir = GroundSpace.Direction(angle, _striker.LeftHanded);
            _shotName = clip?.Name;
            if (clip != null) _striker.Play(clip, contactIn, dir);
            else _striker.Swing(shot.Type, dir, contactIn, d.Length <= Length.Good);
        }

        /// <summary>
        /// The umpire's extras signals on either side's ball: a no-ball (arm out,
        /// and the free hit to come — the presentation shows FREE HIT before it),
        /// byes, leg byes; wides and the bouncer count in ExtrasCalls.
        /// </summary>
        private void RulesSignals(BallResult r)
        {
            if (r.NoBall)
            {
                Extra(After(0.5f, () => Umpire("umpire_no_ball")));
                Extra(After(0.6f, () => Show("NO BALL", NoBallReason(r) + " · free hit next", UiKit.Accent, 1.8f)));
            }
            else if (r.Byes > 0) Extra(After(1.2f, () => Umpire("umpire_bye")));
            else if (r.LegByes > 0) Extra(After(1.2f, () => Umpire("umpire_leg_bye")));
            // Wides, the bouncer count, the extras' log lines (MatchDirector.Extras.cs).
            ExtrasCalls(r);
            TurnedDown(r);
            if (r.Reprieve != null) Extra(After(1.4f, () => Show("NOT OUT", $"{r.Reprieve} — {(r.NoBall ? "off a no-ball" : "it's a free hit")}", UiKit.Gold, 2f)));
        }
    }
}
