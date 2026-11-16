using System.Collections;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// One ball the user bowls: he moves the marker with the stick and taps a ball
    /// type on the wheel — that starts the run-up (no BOWL button, and no default:
    /// owner v26 #15 — the old 10 s timeout that sent a STOCK ball down by itself is
    /// gone; he aims as long as he likes). In the delivery stride the meter fills; the tap's
    /// error against the green goes into the engine's ball (<see cref="QuickMatch.Bowl"/>):
    /// PERFECT is the ball as planned with all its movement, off the green it
    /// scatters, loses movement, and well off can be a full toss, a wide or a
    /// no-ball. A non-striker backing up too far can be run out instead.
    /// The flight and the AI batter's swing: MatchDirector.BowlingFlight.cs.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The meter runs this long before the release frame; the green's centre is this long before it, ± the half-width (real s).</summary>
        /// <remarks>Round 13: the green's centre 0.3 s before the release (was 0.2), so the meter's top carries the red no-ball zone (release error 2–3).</remarks>
        private const float MeterFor = 0.9f, GreenLead = 0.3f, GreenHalf = 0.1f;

        private int _releaseShots;

        public void PauseFromControls() => _hud.SetPaused(true);

        /// <summary>The match camera, for the bowling controls' marker (screen ↔ pitch).</summary>
        public UnityEngine.Camera? ViewCamera => Camera != null ? Camera.GetComponent<UnityEngine.Camera>() : null;

        private IEnumerator BowlBall()
        {
            _impactOffer = false;
            // The last ball's cards go before this one's controls come up (MatchHud.Overlays.cs, presentation).
            _hud.NextBall();
            var ui = _bowlUi!;
            var bowler = _match.CurrentBowler;
            var batter = _match.OnStrike.Player;
            var lh = batter.LeftHanded;
            ui.SetBowler(bowler);
            ui.SetBatter(lh);
            ui.ClearMeter();
            ui.SetMankad(false);
            var around = RoundWicketDev || _visRng.Chance(   // (non-striker dev) -aplroundwicket: MatchDirector.BowlerEnd.cs
Bowling.AroundOdds(Bowling.KindFor(bowler), Bowling.LeftArm(bowler), lh));
            var trajectory = _match.Bowl(Variation.Stock, ui.Mark.x, ui.Mark.y, 0, around);
            var p0 = trajectory.At(0);
            var releaseAt = GroundSpace.Ball(p0.x, p0.y, p0.z, lh);
            BallSetUp(trajectory, releaseAt, lh);
            yield return BallOpens(releaseAt, false);
            Camera.Show(MatchCamera.Mode.Broadcast);
            if (Nudges && _match.FreeHit) Extra(After(0.2f, () => Apl.Client.Dev.AutoShot.Moment("free_hit")));
            if (_fieldRequested) yield return FieldScreen();

            // The field settles before he runs in (the aim can go on meanwhile; this waits only in test runs, where the aim is instant).
            if (Autoplay || SlideFix.Round1) yield return WaitForField(); // (owner 2026-11-13: the field is set before every run-up)
            // Aim: the marker follows the stick; a ball type tapped starts him off. The bowler's
            // card is done by now (its stats sat under the stick and the hint).
            _hud.HidePlayerCard();
            // And the last ball's "2 RUNS" card is gone before the controls come up.
            _hud.ClearRunCard();
            // Let the intro card's fade finish before the stick and chips come up over it (r9 shot 302).
            yield return new WaitForSeconds(0.25f);
            ui.SetAround(around);
            ui.ResetBall();
            // The over's bouncers so far, on the wheel's hub (two allowed: owner v26 #18).
            ui.SetBouncers(_match.BouncersThisOver);
            ShowAim(ui.Mark, lh);
            var aimFrom = Time.time;
            while (!ui.Started)
            {
                // FIELD while aiming (any ball): the presets, the men jog to the new field, then aim on.
                if (_fieldRequested && !Autoplay)
                {
                    yield return FieldScreen();
                    ShowAim(ui.Mark, lh);
                    aimFrom = Time.time;
                    continue;
                }
                if (Autoplay && Time.time - aimFrom > 0.6f)
                {
                    // Test runs bowl the whole repertoire on varied lengths (spin's turn and the variations show up in the shots).
                    var vars = Bowling.Variations(bowler);
                    var spin = bowler.BowlType == BowlType.Spin;
                    // With -aplnudges, some balls are set up to show something (MatchDirector.TestNudges).
                    _evidence = PlanEvidence(bowler);
                    if (_evidence == Evidence.NoBall) _match.TestNoBall = true;
                    var mark = KeeperLoopMark(spin) ?? EvidenceMark(_evidence, spin) ?? new Vector2((float)(_autoRng.NextDouble() * 0.6 - 0.1), (float)(spin ? 2.5 + _autoRng.NextDouble() * 4 : 1 + _autoRng.NextDouble() * 8));
                    ui.SetMark(mark);
                    ui.AutoPick(EvidenceMark(_evidence, spin) != null || KeeperLoop ? Variation.Stock : vars[_autoRng.NextInt(vars.Count)]);
                }
                // OVER / ROUND THE WICKET flipped: the non-striker crosses to the other side.
                if (ui.Around != around)
                {
                    around = ui.Around;
                    var sideT = _match.Bowl(Variation.Stock, ui.Mark.x, ui.Mark.y, 0, around);
                    var side = sideT.At(0);
                    _nonStrikerX = NonStrikerSpotX(sideT.Delivery, GroundSpace.Ball(side.x, side.y, side.z, lh));   // (non-striker) MatchDirector.BowlerEnd.cs
                    _other.NonStriker(new Vector3(_nonStrikerX, 0, GroundSpace.HalfPitch - 1.3f));
                }
                yield return null;
            }
            yield return NonStrikerSettled();   // (non-striker) on his side before the run-up: MatchDirector.BowlerEnd.cs

            // The run-up, with the ball as chosen (the side of the wicket sets the release point).
            trajectory = _match.Bowl(ui.Variation, ui.Mark.x, ui.Mark.y, 0, around);
            var d = trajectory.Delivery;
            p0 = trajectory.At(0);
            releaseAt = GroundSpace.Ball(p0.x, p0.y, p0.z, lh);
            var bowlClip = _bowler.UsesClips ? CricketClips.PickBowl(bowler, d, _visRng) : null;
            HoldBall(bowlClip);
            _striker.Ritual((bowlClip?.ReleaseTime ?? 1.9f) - 1.1f);   // (batting) the AI batter's between-ball ritual: BatterRig.Ritual.cs
            Umpire("umpire_watch_delivery");
            UmpireSide(releaseAt);
            var released = false;
            var bowlFrom = Time.time;
            StartCoroutine(NonStrikerGapWatch(d));   // (non-striker) "[APL nonstriker]" gap line: MatchDirector.BowlerEnd.cs
            if (bowlClip != null) _bowler.Bowl(bowlClip, releaseAt, () => released = true);
            else _bowler.Bowl(d.IsSpin, () => released = true);
            FieldWalkIn(bowlClip?.ReleaseTime ?? 1.6f);
            var other = _other;
            StartCoroutine(After(0.4f, () => { if (_other == other) other.BackUp(); }));
            FollowRunUp(releaseAt);
            ui.SetMankad(_match.NonStrikerBackingUp);

            // The meter through the delivery stride; RUN HIM OUT until then.
            yield return null;
            var releaseReal = Time.time + SecondsToRelease(bowlClip, Time.time - bowlFrom);
            var meterFrom = releaseReal - MeterFor;
            var greenAt = releaseReal - GreenLead;
            var metering = false;
            while (!released)
            {
                if (ui.MankadPressed && !metering)
                {
                    yield return Mankad();
                    yield break;
                }
                if (!metering && Time.time >= meterFrom)
                {
                    metering = true;
                    ui.SetMankad(false);
                    ui.StartMeter((greenAt - meterFrom) / MeterFor, GreenHalf / MeterFor);
                }
                if (metering) ui.MoveNeedle((Time.time - meterFrom) / MeterFor);
                // (batting) the AI batter picks the bat up in the delivery stride too: BatterRig.PickUp.
                if (releaseReal - Time.time <= PickUpLead) _striker.PickUp(releaseReal - Time.time);
                if (Autoplay && metering && float.IsNaN(ui.TapAt) && Time.time >= greenAt + Bowling.Normal(_autoRng) * 0.05f) ui.EndMeter();
                yield return null;
            }
            ui.EndMeter();
            // No tap at all: a late, loose release (orange) — not the red no-ball, which takes a tap in the red.
            double e = Autoplay ? Bowling.Normal(_autoRng) * 0.5 : float.IsNaN(ui.TapAt) ? 1.7 : (ui.TapAt - greenAt) / GreenHalf;
            // Test runs with -aplnudges: the no-ball ball is released in the red, as a user would overstep.
            if (Nudges && _evidence == Evidence.NoBall) e = 2.5;
            ui.ShowRelease(e);
            // Test runs: the meter with the needle where the release landed (the first few balls, and the red one).
            if (Autoplay && (_releaseShots++ < 3 || Bowling.ReleaseNoBall(e))) Apl.Client.Dev.AutoShot.Moment("release_tap");

            // The ball as it actually came out.
            trajectory = _match.Bowl(ui.Variation, ui.Mark.x, ui.Mark.y, e, around);
            yield return BowledFlight(trajectory, batter);
        }

        /// <summary>Everyone in place for a ball.</summary>
        private void BallSetUp(Trajectory trajectory, Vector3 releaseAt, bool lh)
        {
            var d = trajectory.Delivery;
            _pace = BattingPrefs.Factor(d);
            _nonStrikerX = NonStrikerSpotX(d, releaseAt);   // (non-striker) clear of the bowler's line: MatchDirector.BowlerEnd.cs
            LogCrease();   // (batting) where the last ball left the batters: MatchDirector.Crease.cs
            Batters();
            Camera.LeftHanded = lh;
            _striker.TakeGuard();
            _hud.SetBatter(lh);
            _hud.ShowScore(_match, BattingSide, FieldingSide);
            PlaceOtherBatter();
            _bowler.Mark(d.IsSpin);
            _pitchMarker.Hide(0f);
            _tap = null;
            _result = null;
            _left = false;
            _flight = null;
            _released = -1;
            _releaseOffset = Vector3.zero;
            _hud.ShowBall(null);
        }

        /// <summary>
        /// The non-striker caught backing up: the bowler stops and breaks the stumps (the
        /// fielding view), run out. Also for the AI bowler when the user bats: before the
        /// delivery, <c>if (_match.AiMankads()) { yield return Mankad(); yield break; }</c>.
        /// </summary>
        private IEnumerator Mankad()
        {
            _bowlUi?.SetMankad(false);
            _bowlUi?.EndMeter();
            var r = _match.Mankad();
            if (r == null) yield break;
            yield return BreakNonStrikerEnd();
            yield return Wicket(r, r.OutBatter?.Name ?? _match.OffStrike.Player.Name);
            _hud.ShowScore(_match, BattingSide, FieldingSide);
        }

        private void UmpireSide(Vector3 releaseAt)
        {
            if (Refs.Umpire == null) return;
            var u = Refs.Umpire.transform.position;
            Refs.Umpire.transform.position = new Vector3(releaseAt.x >= 0 ? -0.35f : 0.35f, u.y, u.z);
        }

        private void ShowAim(Vector2 mark, bool lh)
        {
            // A little bigger while aiming (seen from behind the bowler; owner v26 #15: 1.6x was "too big");
            // the flight's own marker is normal size.
            var aiming = _bowlUi != null && _bowlUi.Now <= BowlingControls.Phase.RunUp;
            _pitchMarker.Emphasis(aiming ? 1.15f : 1f);
            _pitchMarker.AimStyle(aiming);
            _pitchMarker.Show(GroundSpace.Ball(mark.x, 0.012, mark.y, lh), 0.1f);
        }
    }
}
