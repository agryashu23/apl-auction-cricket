using System;
using System.Collections;
using System.Linq;
using Apl.Client.Dev;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// A ball from the batter's end: the ball in the bowler's hand through the
    /// run-up, the pitch marker up ~1.5 s before release, the flight (played
    /// slower than real — see <see cref="BattingPrefs"/>), the shape-up, the tap
    /// and the shot clip timed onto the ball.
    /// <para>
    /// Two clocks: the engine judges everything in trajectory seconds (the
    /// ball's own time since release); the screen runs in real seconds. One
    /// ball's trajectory time is <c>(Time.time − _released) × _pace</c>
    /// (<see cref="FlightNow"/>): the needle, the taps, auto-play, the keeper's
    /// take and the stumps all go through it, so the timing window is the same
    /// in trajectory terms and wider in real ms. Anything that needs the real
    /// moment of a trajectory time uses <see cref="RealAt"/>.
    /// </para>
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>Real seconds before release the pitch marker starts fading in, and how long it takes.</summary>
        public const float MarkerLead = 1.5f, MarkerFade = 0.4f;

        /// <summary>Trajectory seconds over which the ball eases from where it sat in the hand onto the engine's path.</summary>
        public const double ReleaseEase = 0.3;

        /// <summary>(Unused now: he shoulders arms once the GOOD window has closed — see Ball().)</summary>
        public const double AutoLeaveLead = 0.22;

        /// <summary>This ball's trajectory seconds per real second.</summary>
        private double _pace = 1;

        /// <summary>The ball's own clock: trajectory seconds since release (0 before it).</summary>
        private double FlightNow => _released < 0 ? 0 : (Time.time - _released) * _pace;

        /// <summary>The real time (Time.time) at which this ball's flight reaches trajectory second <paramref name="t"/>.</summary>
        public float RealAt(double t) => _released + (float)(t / _pace);

        private BallView _ballView = null!;
        private PitchMarker _pitchMarker = null!;
        /// <summary>Where the ball was in the hand at release, against the engine's release point; eased away over <see cref="ReleaseEase"/>.</summary>
        private Vector3 _releaseOffset;

        private IEnumerator Ball()
        {
            _impactOffer = false;
            StallProbe.Mark($"ball {_match.Over}.{_match.BallInOver + 1} (innings {_match.InningsNumber})");
            var trajectory = _match.NextDelivery();
            var d = trajectory.Delivery;
            var batter = _match.OnStrike.Player;
            _pace = BattingPrefs.Factor(d);
            WireFootwork();
            var lh = batter.LeftHanded;
            ForceRoundWicket(d, lh);   // (non-striker dev) -aplroundwicket: MatchDirector.BowlerEnd.cs
            var p0 = trajectory.At(0);
            var releaseAt = GroundSpace.Ball(p0.x, p0.y, p0.z, lh);
            // The non-striker waits on the side of the stumps the bowler isn't running down, clear of his line (MatchDirector.BowlerEnd.cs).
            _nonStrikerX = NonStrikerSpotX(d, releaseAt);
            LogCrease();   // where the last ball left them (batting: MatchDirector.Crease.cs)
            Batters();
            Camera.LeftHanded = lh;
            _striker.TakeGuard();
            _hud.SetBatter(lh);
            _hud.ShowScore(_match, _bat, _bowl);
            // The last ball's cards go before this one (MatchHud.Overlays.cs, presentation).
            _hud.NextBall();
            PlaceOtherBatter();
            _bowler.Mark(d.IsSpin);
            _pitchMarker.Hide(0f);
            _tap = null;
            _result = null;
            ApplyMilestoneTest();   // -aplmilestone (test runs): MatchDirector.Milestone.cs
            _left = false;
            _flight = null;
            _released = -1;
            _releaseOffset = Vector3.zero;
            _hud.ShowBall(null);
            _hud.ShowShot(null);
            // Test runs film the second ball from the batter's close-up camera (stance, shot, bat).
            var closeUp = Autoplay && _match.LegalBalls == 1;
            // Intros, the camera behind the bowler at his mark (presentation).
            yield return BallOpens(releaseAt, closeUp);

            // The meter: needle travels from release to contact; the zones are the engine's grades.
            var contactAt = trajectory.TimeAt(Batting.ContactZ(d.Length));
            _contactAt = contactAt;
            var window = Batting.Window(batter, d);
            _hud.ShowMeter((float)(Batting.IdealTap(trajectory) / contactAt), (float)(window / contactAt));
            _hud.ShotControls(true);
            var bowlClip = _bowler.UsesClips ? CricketClips.PickBowl(_match.CurrentBowler, d, _visRng) : null;
            // The ball's in his hand from his mark, all through the run-up.
            HoldBall(bowlClip);
            // Between balls he does something at the crease, done in good time for the pick-up (BatterRig.Ritual.cs).
            _striker.Ritual(0.6f + (bowlClip?.ReleaseTime ?? 1.9f) - 1.1f);
            yield return new WaitForSeconds(0.6f);
            if (Autoplay) LogBatter("guard");
            // Now and then the AI bowler catches the user's non-striker backing up too far (a dead ball).
            if (_match.AiMankads())
            {
                yield return Mankad();
                yield break;
            }
            if (BatCapture.On)
            {
                BatCapture.Begin($"{batter.Name} {(lh ? "LH" : "RH")}, {d.Length} {(d.IsSpin ? "spin" : "pace")}, bowl clip {bowlClip?.Name}");
                BatCapture.Moment("1guard", _striker.transform);
            }

            // The bowler waits for his field to settle (match-play: MatchDirector.FieldMoves WaitForField).
            yield return WaitForField();
            yield return NonStrikerSettled();   // (non-striker) on his side before the run-up: MatchDirector.BowlerEnd.cs
            Umpire("umpire_watch_delivery");
            var released = false;
            // The umpire stands on the side of the stumps the bowler isn't using.
            if (Refs.Umpire != null)
            {
                var u = Refs.Umpire.transform.position;
                Refs.Umpire.transform.position = new Vector3(releaseAt.x >= 0 ? -0.35f : 0.35f, u.y, u.z);
            }
            var bowlFrom = Time.time;
            StartCoroutine(NonStrikerGapWatch(d));   // (non-striker) "[APL nonstriker]" gap line: MatchDirector.BowlerEnd.cs
            if (bowlClip != null)
            {
                _bowler.Bowl(bowlClip, releaseAt, () => released = true);
                FieldWalkIn(bowlClip.ReleaseTime);
                // The non-striker backs up with the run-up (3 s of clip, ending about at the release).
                var other = _other;
                StartCoroutine(After(0.4f, () => { if (_other == other && _tap == null) other.BackUp(); }));
                if (Autoplay) Debug.Log($"[APL match] bowl {bowlClip.Name}: hand at release {_bowler.HandAtRelease(bowlClip)}, release point {releaseAt}, pace x{_pace:0.00}");
            }
            else
            {
                _bowler.Bowl(d.IsSpin, () => released = true);
                FieldWalkIn(1.6f);
            }
            // The camera runs in with him (presentation).
            if (!closeUp) FollowRunUp(releaseAt);
            // Where it's going to pitch, up well before he lets go (the length is known from the
            // start: it's the delivery's planned bounce point), until it does.
            var bounceAt = GroundSpace.Ball(d.BounceX, 0.012, d.BounceZ, lh);
            var marked = false;
            var picked = false;
            while (!released)
            {
                var toRelease = SecondsToRelease(bowlClip, Time.time - bowlFrom);
                if (!marked && toRelease <= MarkerLead)
                {
                    marked = true;
                    _pitchMarker.Show(bounceAt, MarkerFade);
                }
                // The bat comes up as the bowler gathers, at the top by the release (BatterRig.PickUp).
                if (!picked && toRelease <= PickUpLead)
                {
                    picked = true;
                    _striker.PickUp(toRelease);
                }
                yield return null;
            }
            if (!marked) _pitchMarker.Show(bounceAt, 0.15f);
            _flight = trajectory;
            _released = Time.time;
            Released(d, RealAt(contactAt) - _released);
            _leaveShown = false;
            _hud.ShowBall(d);
            BatCapture.Moment("2release", _striker.transform);
            BatCapture.Moment("2up", _striker.transform, 0.1f);
            // From the hand onto its path: where the ball sat this frame against the engine's release point.
            _releaseOffset = _ballView.InHand ? _ball.position - GroundSpace.Ball(p0.x, p0.y + 0.036, p0.z, lh) : Vector3.zero;
            if (_releaseOffset.magnitude > 1.5f) _releaseOffset = Vector3.zero;
            _ballView.Release(d, lh);
            SetBall(trajectory, 0, lh);
            _trail.Clear();
            _trail.emitting = false;
            // He reads the length: forward press to a full ball, back and across to a short one,
            // the backlift at its top as the perfect tap's moment comes.
            // (Down the track already: he meets it sooner — MatchDirector.Footwork.cs.)
            _striker.ShapeUp((float)(Batting.IdealTap(trajectory, _striker.Charged) / _pace), d.Length >= Length.Short && !_striker.Charged);
            // The ring on the ball closes at the perfect tap (the grades' windows, in real seconds).
            _ballView.TimeRing(RealAt(Batting.IdealTap(trajectory, _striker.Charged)), (float)(window / _pace), (float)(0.35 * window / _pace));

            // Fly the ball until the shot is played (or it's past the batter).
            var decideAt = contactAt + 3.5 * window;
            StartKeeperRead(trajectory, lh, false, contactAt); // the keeper reads it from the hand (MatchDirector.KeeperRead)
            var autoTap = Autoplay ? AutoShotTime(trajectory, window) : (double?)null;
            var autoType = Autoplay ? AutoShotType(d) : ShotType.Stroke;
            var ballCam = closeUp;
            while (true)
            {
                var t = FlightNow;
                // Once he's played at it and the engine has him hitting it, the ball is drawn onto the blade (MatchDirector.Meet.cs).
                if (_tap is double tapAt && _result != null && BatOnBall(_result)) SetBallMeeting(trajectory, t, tapAt, lh);
                else SetBall(trajectory, KeeperFlight(t), lh); // (into the keeper's gloves once there: MatchDirector.KeeperRead)
                // The last stretch to the bat: the camera pushes in on the batter (the hit hands it on).
                if (!ballCam && RealAt(contactAt) - Time.time <= BallCamLead)
                {
                    ballCam = true;
                    BallCam(RealAt(contactAt));
                }
                if (t >= trajectory.BounceTime) _pitchMarker.Hide(0.12f);
                _hud.MoveNeedle((float)(t / contactAt));
                if (autoTap != null && _tap == null && t >= autoTap.Value) OnShot(autoType, BatCapture.On ? BatCapture.Foot : null, false);
                if (BatCapture.On && _tap != null) CaptureSwing(t);
                if (_tap != null && t >= _batMeets) break;
                // Not before the GOOD window has closed: it used to start 60 ms BEFORE the perfect tap, so
                // every timed shot cross-faded out of the leave (bat up behind his head, hands elsewhere).
                if (_tap == null && !_leaveShown && t >= Math.Min(contactAt - 0.04, Batting.IdealTap(trajectory, _striker.Charged) + window))
                {
                    _leaveShown = true;
                    _striker.Leave((float)((contactAt - t) / _pace));
                }
                if (_tap == null && t >= decideAt) break;
                // Left, and in the keeper's gloves: settled now (MatchDirector.KeeperRead) — it used to fly on through him.
                if (_tap == null && KeeperHasIt) break;
                // Onto the pad / into the stumps by the next frame: settled now, so it stops there (MatchDirector.KeeperRead).
                if ((_tap == null || _result != null) && StopsNext(trajectory, t, true)) break;
                yield return null;
            }

            // Played (or past him): the shot controls go until the next ball (the RUN controls stay).
            _hud.ShotControls(false);
            // The bat eases back onto its own path after the meeting (BatterRig.BatNudge).
            _striker.BatNudge = Vector3.zero;
            if (BatCapture.On && _tap != null) BatCapture.Note($"   meeting: ball drawn {_ballDrawn:0.00} m across its line, bat drawn {_striker.BatNudgeNow.magnitude:0.00} m; last frame ball→bat {Vector3.Distance(_ball.position, _striker.BatMiddle):0.00} m");
            // The outcome was settled at the tap (so the shot clip matched it); a leave is settled now.
            var r = _result ?? _match.Play(ShotInput.Leave);
            _result = null;
            LogOutcome(r, "user bats");   // [APL outcome] (MatchDirector.HitFeel.cs)
            _hud.ShowGrade(r, _pace, _striker.transform.position);
            _hud.ShowShot(r);
            // Not on a wicket: the bar waits for the outcome (and any review) to play out, then refreshes below —
            // "innings complete" used to show while the DRS review was still running.
            if (!_match.Finished && !r.Wicket) _hud.ShowScore(_match, _bat, _bowl);
            CatchLoopCount(r); // -aplcatchloop only (MatchDirector.CatchLoop)
            yield return Outcome(r, FlightNow, lh, batter.Name);
            _hud.ShowScore(_match, _bat, _bowl);
        }

        /// <summary>The ball into the bowler's bowling hand (it follows the fingers until release).</summary>
        private void HoldBall(BowlClip? clip)
        {
            var a = _bowler.GetComponent<Animator>();
            var left = clip != null && clip.Arm == "left";
            var hand = a != null ? a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand) : null;
            var fingers = a != null ? a.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal) : null;
            _ballView.Hold(hand != null ? hand : _bowler.transform, fingers);
        }

        /// <summary>
        /// Real seconds until he lets go, read off the bowling clip's clock (a touch early
        /// while the run-up is sped up — fine for a marker that fades in); the code-drawn
        /// action is ~1.9 s from the start.
        /// </summary>
        private float SecondsToRelease(BowlClip? clip, float elapsed)
        {
            if (clip == null) return Mathf.Max(0f, 1.9f - elapsed);
            var a = _bowler.GetComponent<Animator>();
            if (a == null) return 0f;
            var info = a.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash != Animator.StringToHash(clip.Name)) return elapsed > 1.5f ? 0f : float.MaxValue;
            var left = clip.ReleaseTime - info.normalizedTime * Mathf.Max(0.01f, clip.Length);
            return Mathf.Max(0f, left) / Mathf.Max(0.5f, a.speed);
        }

        /// <summary>Seconds before the release the batter starts picking the bat up (the bowler's gather / front foot).</summary>
        private const float PickUpLead = 0.5f;

        private void OnShot(ShotType type) => OnShot(type, null, false);

        /// <summary>
        /// The tap: <paramref name="back"/> is the foot he chose (null: whichever
        /// suits the length — AUTO footwork); <paramref name="special"/> a scoop /
        /// ramp / switch hit — lofted, and behind square unless he's aimed it somewhere.
        /// </summary>
        private void OnShot(ShotType type, bool? back, bool special)
        {
            if (_flight == null || _tap != null || _released < 0 || _left) return;
            // Already in the keeper's gloves: nothing left to play (MatchDirector.KeeperRead).
            if (KeeperHasIt) return;
            var t = FlightNow;
            _tap = t;
            _tapType = type;
            _tapBack = back;
            _tapSpecial = special;
            _ballView.RingTapped();
            var d = _flight.Delivery;
            _tapZone = _hud.Aim ?? (BatCapture.On ? BatCapture.Aim : CatchLoop && !CatchForce ? CatchLoopZone() : special ? (_visRng.Chance(0.5) ? Zone.ThirdMan : Zone.FineLeg) : BestZone(d, type));
            // The motion first: the shot he ASKED for — its type, his foot (or the length's) and the
            // clip nearest his aim (or AUTO's pick). Then the outcome, settled now (the engine only
            // needs the tap time), along that clip's own authored line: a timed ball leaves within
            // 10° of where the bat swings (early / late within 25°, to leg / off); only a mistimed or
            // edged one flies off it. The face turns to where it actually goes.
            var aim = Field.ZoneAngle(_tapZone);
            // Footwork first (MatchDirector.Footwork.cs): the shot starts from where he's got to, to the ball as he meets it.
            // The tap as graded: the difficulty's timing window (audio-modes: Modes/Difficulty.cs; NORMAL leaves it as is).
            var graded = Apl.Client.Modes.Difficulty.Tap(t, Batting.IdealTap(_flight, _striker.Charged));
            var foot = new ShotInput(type, _tapZone, graded, back, special) { Charged = _striker.Charged, Across = _striker.Across };
            _striker.SettleFootwork();
            var clip = _striker.UsesClips ? CricketClips.PickShot(type, aim, Batting.EffectiveLength(d, foot), d.IsSpin, _visRng, back, special, BallHeightAtBat(_flight), _striker.Charged && !_striker.HasChargeClip) : null;
            if (clip != null) aim = clip.Angle > 180 ? clip.Angle - 360 : clip.Angle;
            foot.AimDeg = clip != null ? aim : (double?)null;
            _result = _match.Play(foot);
            var clean = _result.ExitSpeed > 0 && !_result.Edge && !_result.Wide && !(_result.Wicket && !_result.Caught && !_result.RunOut) && _result.Timing != Timing.Miss;
            // Mistimed (early / late) or edged, it leaves off the clip's line: swing the shot that goes that way
            // instead — round 10: edges too (a late edge to third man swung a square drive at point, face and all),
            // and from any shot type when his own type has nothing near the ball's line (a late chip goes fine).
            var onBat = BatOnBall(_result);
            if (onBat && clip != null)
            {
                var len = Batting.EffectiveLength(d, foot);
                var height = BallHeightAtBat(_flight);
                var charged = _striker.Charged && !_striker.HasChargeClip;
                ShotClip? Nearest(double c)
                {
                    ShotClip? best = null;
                    foreach (var ty in new[] { type, ShotType.Stroke, ShotType.Push })
                    {
                        var k = CricketClips.PickShot(ty, c, len, d.IsSpin, _visRng, back, special, height, charged);
                        if (k != null && (best == null || AngleOff(k.Angle, c) < AngleOff(best.Angle, c))) best = k;
                        if (best != null && AngleOff(best.Angle, c) <= FollowAfter) break;
                    }
                    return best;
                }
                clip = FollowBall(clip, _result.ExitAngle, Nearest);
            }
            // Round 12: a thin edge or a miss swings the edge / play-and-miss clips, not the whole stroke.
            if (clip != null && !_result.Wide && (_result.Edge || !onBat))
            {
                var playedOn = _result.Edge && _result.Wicket && _result.HowOut == "bowled";
                var skied = _result.Edge && _result.Launch > 30;
                var edgeClip = CricketClips.EdgeClip(_result.Edge, !onBat && !_result.Edge, playedOn, skied, _result.ExitAngle, d.Line <= Line.Off);
                if (edgeClip != null) clip = edgeClip;
            }
            var face = onBat ? _result.ExitAngle : aim;
            // Bat meets ball where the clip's bat is (MatchDirector.Meet.cs); in real seconds, longer.
            _ballMeets = _batMeets = clip != null ? 0 : t + Batting.SwingTime;
            _ballDrawn = 0f;
            _striker.BatNudge = Vector3.zero;
            if (clip != null) _batMeets = Meet(clip, t, clean, BatOnBall(_result));
            var contactIn = (float)((_batMeets - t) / _pace);
            _shotName = clip?.Name;
            if (BatCapture.On)
            {
                BatCapture.Note($"tap t={t:0.000} {type} aim {_tapZone} ({aim:0}°) foot {(back == null ? "auto" : back.Value ? "back" : "front")} -> clip {clip?.Name} [{(clip == null ? "-" : CricketClips.IsBackFoot(clip) ? "back-foot" : "front-foot")}{(clip != null && clip.Lofted ? ", lofted" : "")}]; exit {_result.ExitAngle:0}° {_result.ExitSpeed:0.0} m/s {_result.Timing} edge {_result.Edge} wicket {_result.Wicket}; face {face:0}°, contact in {contactIn:0.00} s");
                _capPre = _capMeet = false;
                _capGap = float.MaxValue;
                foreach (var (tag, at) in new[] { ("5after", contactIn + 0.25f), ("6follow", contactIn + 0.6f), ("7finish", contactIn + 1.2f) })
                    BatCapture.Moment(tag, _striker.transform, Mathf.Max(0f, at));
            }
            if (clip != null)
            {
                _striker.Play(clip, contactIn, GroundSpace.Direction(face, _striker.LeftHanded));
                return;
            }
            var dir = GroundSpace.Direction(face, _striker.LeftHanded);
            _striker.Swing(type, dir, contactIn, back != null ? !back.Value : d.Length <= Length.Good);
        }

        private bool _capPre, _capMeet;
        private float _capGap;

        /// <summary>Test films (-aplbatcap): the frame before the bat meets the ball and the meeting itself, with how far apart they are.</summary>
        private void CaptureSwing(double t)
        {
            var gap = Vector3.Distance(_ball.position, _striker.BatMiddle);
            _capGap = Mathf.Min(_capGap, gap);
            // Where the ball would be on its own path (before MatchDirector.Meet draws it onto the blade).
            var own = _flight != null ? _flight.At(t) : default;
            var rawGap = _flight != null ? Vector3.Distance(GroundSpace.Ball(own.x, own.y + 0.036, own.z, _striker.LeftHanded), _striker.BatMiddle) : -1f;
            string Info() => $"flight {t:0.000} (meets {_batMeets:0.000}, ball at sweet {_ballMeets:0.000}), ball {_ball.position:F2}, bat middle {_striker.BatMiddle:F2}, gap {gap:0.00} m (on its own path {rawGap:0.00} m; closest so far {_capGap:0.00} m); clip {(_shotName != null ? _striker.DebugClock(_shotName) + $" contact@{CricketClips.Find(_shotName)?.ContactTime:0.000}s" : "-")}";
            if (t <= _batMeets + 0.05) BatCapture.Note($"   frame t={t:0.000} dt={Time.deltaTime:0.000} {(_shotName != null ? _striker.DebugClock(_shotName) : "")}");
            if (!_capPre && t >= _batMeets - 0.06)
            {
                _capPre = true;
                BatCapture.Note("3pre " + Info());
                BatCapture.Moment("3pre", _striker.transform);
            }
            if (!_capMeet && t >= _batMeets)
            {
                _capMeet = true;
                BatCapture.Note("4contact " + Info());
                BatCapture.Moment("4contact", _striker.transform);
            }
        }

        /// <summary>LEAVE: shoulder arms now and let it through.</summary>
        private void OnLeave()
        {
            if (_flight == null || _tap != null || _released < 0 || _left) return;
            _left = true;
            _leaveShown = true;
            _ballView.StopRing();
            _striker.Leave((float)Math.Max(0.05, (_contactAt - FlightNow) / _pace));
        }

        /// <summary>AUTO aim: the best direction for this ball and shot.</summary>
        public static Zone BestZone(Delivery d, ShotType type) =>
            Enum.GetValues(typeof(Zone)).Cast<Zone>().OrderByDescending(z => Batting.Suitability(d, type, z)).First();

        /// <summary>
        /// Test runs only (Autoplay): balls 1 and 3 of each over are lofted a little late (+1.2 windows) on purpose,
        /// so the capture shows user-side wickets — mostly caught (+2.5 gave mostly bowled; match-play measured it).
        /// </summary>
        private bool AutoRisky => CatchLoop || Nudges && (_match.BallInOver == 1 || _match.BallInOver == 3); // evidence-only (PM, r12): plain runs judge totals

        private double AutoShotTime(Trajectory t, double window) => AutoRisky
            ? Batting.IdealTap(t) + (CatchLoop ? CatchLoopLate() : 1.2) * window
            : Batting.IdealTap(t) + Bowling.Normal(_autoRng) * 0.7 * window;

        private ShotType AutoShotType(Delivery d)
        {
            if (BatCapture.On) return BatCapture.Type;
            if (AutoRisky) return CatchLoop ? CatchLoopType() : ShotType.Loft;
            var rate = _match.BallsLeft == 0 ? 99 : _match.RunsNeeded * 6.0 / _match.BallsLeft;
            if (rate > 9) return ShotType.Loft;
            // Test runs (match-play's totals work): in his arc — a full ball, a full toss, or a short one from
            // pace — he goes over the top about half the time, so the user side's innings sees sixes.
            var inArc = d.Length == Length.Full || d.FullToss || (!d.IsSpin && d.Length >= Length.Short);
            if (inArc && _autoRng.Chance(0.5)) return ShotType.Loft;
            return _autoRng.Chance(0.3) ? ShotType.Push : ShotType.Stroke;
        }

        /// <summary>The ball <paramref name="time"/> trajectory seconds after release, eased off the hand at first.</summary>
        private void SetBall(Trajectory t, double time, bool leftHanded)
        {
            var p = t.At(time);
            _lineFlight = t; _lineLeft = leftHanded; // the replay's ball line (MatchDirector.ReplayLine.cs)
            var hand = 1f - Mathf.SmoothStep(0f, 1f, (float)(time / ReleaseEase));
            _ball.position = GroundSpace.Ball(p.x, p.y + 0.036, p.z, leftHanded) + _releaseOffset * hand;
        }

        // ------------------------------------------------------------ the ball and the marker

        /// <summary>
        /// A white ball (it's T20: white ball, dark sightscreens) with a grey seam, a
        /// little self-lit so it holds its brightness in shadow — it reads against the
        /// pitch and the outfield from 60 m where the red one disappeared.
        /// </summary>
        private static Texture2D LeatherBall()
        {
            const int w = 256, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "ball_leather_white", wrapMode = TextureWrapMode.Repeat };
            var px = new Color[w * h];
            var white = new Color(0.97f, 0.96f, 0.93f);
            var groove = new Color(0.5f, 0.52f, 0.55f);
            var thread = new Color(0.72f, 0.74f, 0.78f);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                // A little grain; the seam's groove with two rows of stitches either side.
                var grain = 0.94f + 0.06f * Mathf.PerlinNoise(x * 0.3f, y * 0.3f);
                var c = white * grain;
                var dv = Mathf.Abs(y - h / 2f);
                if (dv < 1.5f) c = groove;
                else if (dv >= 3 && dv <= 6 && (x / 6) % 2 == 0) c = thread;
                c.a = 1f;
                px[y * w + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        private Transform MakeMarker()
        {
            _pitchMarker = PitchMarker.Create();
            return _pitchMarker.transform;
        }

        private Transform MakeBall()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Ball";
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * BallView.Size;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var tex = LeatherBall();
            var m = new Material(shader) { color = Color.white };
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            m.SetFloat("_Smoothness", 0.6f);
            // A little self-lit through its own texture (seam darker than the leather) so it holds up
            // in shadow, but lit enough by the sun to look round — fully self-lit it was a flat dot of light.
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", tex);
            m.SetColor("_EmissionColor", new Color(0.16f, 0.16f, 0.15f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            var r = go.GetComponent<Renderer>();
            r.material = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // The tracer after the hit: short and faint (the long warm-white one read as a streak of
            // light instead of a ball). None in the delivery; the outline and shadow do that job.
            _trail = go.AddComponent<TrailRenderer>();
            _trail.time = 0.12f;
            _trail.startWidth = 0.035f;
            _trail.endWidth = 0f;
            _trail.material = new Material(Shader.Find("Sprites/Default") ?? shader);
            _trail.startColor = new Color(1f, 1f, 1f, 0.22f);
            _trail.endColor = new Color(1f, 1f, 1f, 0);
            _trail.emitting = false;
            _ballView = BallView.Add(go);
            go.SetActive(false);
            return go.transform;
        }
    }
}
