using System;
using System.Collections;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The keeper's take of a delivery past the bat (owner: "when a ball is left alone it looks like it's going
    /// somewhere, but suddenly the keeper has it"). The cause: a leave was only settled 3.5 timing windows after the
    /// bat (0.2–0.4 s), by when the ball had flown on THROUGH the keeper (pace: ~8 m behind the stumps); the take was
    /// then chosen with a negative lead and the ball snapped from there into his gloves. His take spot was a fixed
    /// depth 0.55 m in front of his stance, the drawn ball and the take target 0.04–0.15 m apart in height, and his
    /// sideways shuffle clamped to 0.6 m (a wide ball was pulled onto the gloves).
    /// Now, from the release: the arrival is predicted on the ball's own post-bounce flight at the depth of the
    /// chosen take's gloves; he reads it (feet across, eased, from the bounce — from the hand for a wide one), commits
    /// to the take clip (low / waist / chest / head / off / leg / dive) once it's his ball, timed so its take frame is
    /// the arrival, with the hand IK on that point; the ball flies its flight right into the gloves and is held in
    /// the cup from that frame. A leave is settled when it's in his gloves (nobody can play it after). Test runs log
    /// <c>[APL keeper] ball N: kind arrival offset …, keeper moved …, clip …, max ball snap S, take gap G</c>.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private sealed class KeeperRead
        {
            public Trajectory Path = null!;
            public bool Lh, AiLeave;
            public double ContactT, ReadT, TakeT;
            public Vector3 At, Stance, BodyTo;
            public string Clip = "";
            public float Budget, PlanGap;
            public bool Takeable, Read, Committed, Taken, Off, Miss, LetGo, Rearmed;
            public string? Fired;
            public BallResult? R;
            public string Kind = "leave";
            public int Shots;
        }

        private KeeperRead? _kr;

        /// <summary>The keeper commits to the take at most this long (s) before the ball's there, and at least this long.</summary>
        private const float KeeperCommitLead = 0.4f, KeeperMinLead = 0.12f;

        /// <summary>The ball's in the keeper's gloves (this ball): the bat can't play it now.</summary>
        private bool KeeperHasIt => _kr != null && _kr.Taken && !_kr.Off && !_kr.Miss;

        /// <summary>The ball as drawn at flight time <paramref name="t"/> (SetBall's point, less the release ease).</summary>
        private static Vector3 FlightPoint(Trajectory tr, double t, bool lh)
        {
            var p = tr.At(t);
            return GroundSpace.Ball(p.x, p.y + 0.036, p.z, lh);
        }

        /// <summary>From the release (both flights): the take predicted on the flight, the read and the take watched.</summary>
        private void StartKeeperRead(Trajectory tr, bool lh, bool aiLeave, double contactAt)
        {
            if (_kr != null) _kr.LetGo = true;
            _kr = null;
            _leaveStopT = LeaveStop(tr);
            if (_keeper == null || !_keeper.UsesClips || Refs.Keeper == null) return;
            var d = tr.Delivery;
            var kr = new KeeperRead { Path = tr, Lh = lh, AiLeave = aiLeave, ContactT = contactAt, Stance = Refs.Keeper.transform.position };
            // A leave: does it get to him (the engine's own geometry for a leave — Rules.Missed: the pad 0.9 m out, the stumps)?
            var pad = tr.At(tr.TimeAt(0.9));
            var back = d.Length >= Length.Short;
            var onPad = pad.y <= 0.85 && pad.x >= -0.3 && pad.x <= (back ? 0.15 : 0.22);
            kr.Takeable = d.Wide || !tr.HittingStumps() && !onPad;
            PlanKeeperTake(kr, false);
            _kr = kr;
            KeeperTakeProbe.For(Refs.Keeper.gameObject).Cancel();
            Extra(KeeperWatch(kr));
        }

        /// <summary>
        /// Where and when his gloves meet it: the ball's flight at the depth of the take clip's gloves (iterated: the
        /// clip is chosen on the ball's height and side there), and how far across his feet must take him.
        /// </summary>
        private void PlanKeeperTake(KeeperRead kr, bool appeal)
        {
            var tr = kr.Path;
            var up = _keeper!.StandingUp;
            var z = kr.Stance.z + GroundSpace.HalfPitch + 0.55;
            var bounce = tr.Delivery.FullToss ? kr.ContactT : Math.Min(tr.BounceTime, kr.ContactT);
            for (var i = 0; i < 3; i++)
            {
                var t = tr.TimeAt(z);
                var p = FlightPoint(tr, t, kr.Lh);
                // He reads a wide one out of the hand; anything else off the pitch.
                var wide = Mathf.Abs(p.x - kr.Stance.x) > 0.6f;
                kr.ReadT = wide ? 0.5 * tr.BounceTime : bounce;
                var time = (float)((t - kr.ReadT) / _pace) - 0.05f;
                kr.Budget = Mathf.Clamp((up ? 3f : 4f) * time, 0f, up ? 1.0f : 1.6f);
                var clip = _keeper.PlanDelivery(p, kr.Stance, kr.Budget, appeal, out var body, out var glove);
                kr.TakeT = t;
                kr.At = p;
                if (clip == null) return;
                kr.Clip = clip;
                kr.BodyTo = body;
                kr.PlanGap = Vector3.Distance(glove, p);
                var next = glove.z + GroundSpace.HalfPitch;
                if (Math.Abs(next - z) < 0.01) break;
                z = next;
            }
            kr.TakeT = tr.TimeAt(z);
            kr.At = FlightPoint(tr, kr.TakeT, kr.Lh);
        }

        /// <summary>Past the bat, his: not hit, not the slip's catch, not bowled, not stopped by the pad (Outcome's own tests).</summary>
        private bool KeepersBall(BallResult r)
        {
            var hit = r.ExitSpeed > 0 && !(r.Wicket && !r.Caught && !r.RunOut) && !r.Wide && r.Timing != Timing.Leave && r.Timing != Timing.Miss;
            if (hit) return false;
            if (r.Wicket && r.HowOut == "caught behind" && r.Fielder == "slip" && NamedFielder("slip") != null) return false;
            if (r.Wicket && r.HowOut == "bowled") return false;
            if (r.PadHit && r.Lbw != null && r.LegByes == 0 && !r.Wide) return false;
            return true;
        }

        private static bool CaughtBehind(BallResult r) => r.Wicket && (r.HowOut == "caught behind" || r.Caught && r.Fielder == "keeper");

        private static bool Stumped(BallResult r) => (r.Wicket && r.HowOut == "stumped") || r.Referral == TvCheck.Stumping;

        /// <summary>The read, the commit, the take — every frame from the release until it's in his gloves (or not his).</summary>
        private IEnumerator KeeperWatch(KeeperRead kr)
        {
            while (_kr == kr && !kr.Taken && !kr.Off && !kr.Miss)
            {
                var t = FlightNow;
                var left = RealAt(kr.TakeT) - Time.time;
                // The read: across to the line, from the bounce (from the hand for a wide one) — whatever the batter does.
                if (!kr.Read && t >= kr.ReadT && kr.Clip.Length > 0)
                {
                    kr.Read = true;
                    _keeper!.MoveTo(kr.BodyTo, Mathf.Max(0.05f, left - 0.05f));
                    WatchKeeperTake(kr);
                }
                var res = kr.R ?? _result;
                if (!kr.Committed)
                {
                    if (res != null)
                    {
                        if (!KeepersBall(res)) KeeperOff(kr);
                        else if (left <= KeeperCommitLead) KeeperCommit(kr, res);
                    }
                    // No shot (yet): a leave he'll get, once it's past the bat (the AI's leave is known from the hand).
                    else if (_tap == null && kr.Takeable && (left <= KeeperMinLead || left <= KeeperCommitLead && (kr.AiLeave || t >= kr.ContactT)))
                        KeeperCommit(kr, null);
                }
                else if (res != null)
                {
                    // Played at after he'd gone for it: hit, it's not his; nicked behind, his take is a catch.
                    if (!KeepersBall(res)) KeeperOff(kr);
                    else if (kr.Fired != null && !kr.Rearmed && CaughtBehind(res))
                    {
                        kr.Rearmed = true;
                        ArmKeeperReach(kr, kr.Fired, res);
                    }
                }
                // -aplkeeperloop: the last frames before the take, filmed.
                if (KeeperLoop && kr.Committed && !kr.Off && left <= 10.5f * Mathf.Max(0.01f, Time.smoothDeltaTime)) KeeperFrame(kr, "pre");
                yield return null;
            }
            if (_kr == kr && kr.Taken && KeeperLoop) KeeperFrame(kr, "take");
        }

        /// <summary>He goes for it: the take clip timed onto the arrival, the gloves reaching for that point.</summary>
        private void KeeperCommit(KeeperRead kr, BallResult? r)
        {
            kr.Committed = true;
            var left = Mathf.Max(0f, RealAt(kr.TakeT) - Time.time);
            if (r != null && Stumped(r) && KeeperStumping(kr, left)) return;
            // A beaten ball, now and then: the take and the appeal (a clip of its own, so the take is re-planned on it).
            var appeal = r != null && !r.Wicket && !r.Wide && r.Timing != Timing.Leave && UnityEngine.Random.value < 0.3f;
            if (appeal)
            {
                var stance = kr.Stance;
                var clip = _keeper!.PlanDelivery(kr.At, stance, kr.Budget, true, out var body, out _);
                if (clip != null && clip != kr.Clip)
                {
                    kr.Clip = clip;
                    kr.BodyTo = body;
                    if (kr.Read) _keeper.MoveTo(body, Mathf.Max(0.05f, left - 0.05f));
                }
            }
            if (kr.Clip.Length == 0) return;
            _keeper!.FireTake(kr.Clip, left);
            kr.Fired = kr.Clip;
            // Out of his reach even at full stretch (feet, dive and arms): he goes for it and it's past him — the
            // ball keeps its flight; it never arrives in his hands from where it isn't.
            if (kr.PlanGap > CatchHands.MaxReach)
            {
                kr.Miss = true;
                if (Refs.Keeper != null) KeeperTakeProbe.For(Refs.Keeper.gameObject).Cancel();
                if (Autoplay) Debug.Log($"[APL keeper] ball {Cricket.FormatOvers(_match.LegalBalls)}: out of his reach ({kr.Clip} gloves {kr.PlanGap:0.00} m off the ball after a {kr.Budget:0.00} m move): it goes past him");
                return;
            }
            if (r != null) kr.Rearmed = true;
            ArmKeeperReach(kr, kr.Clip, r);
        }

        /// <summary>The gloves' reach (MatchDirector.CatchHands.WireKeeperTakes): a "catch" for a caught behind, else a "keeper" take.</summary>
        private void ArmKeeperReach(KeeperRead kr, string clip, BallResult? r)
        {
            if (kr.Taken) return;
            WireKeeperTakes();
            _keeperDelivery = true;
            _keeperCatch = r != null && CaughtBehind(r);
            try
            {
                _keeper!.OnTake?.Invoke(clip, kr.At, Mathf.Max(0f, RealAt(kr.TakeT) - Time.time));
            }
            finally
            {
                _keeperDelivery = false;
            }
        }

        /// <summary>Not his after all (the bat found it): the gloves let go of the reach; his clip plays out.</summary>
        private void KeeperOff(KeeperRead kr)
        {
            kr.Off = true;
            kr.LetGo = true;
            if (Refs.Keeper == null) return;
            KeeperTakeProbe.For(Refs.Keeper.gameObject).Cancel();
            if (kr.Committed) CatchHands.For(Refs.Keeper).Stop();
        }

        /// <summary>
        /// The flight's time for the ball drawn now: its own flight, up to the take point once he's committed to it —
        /// where, on the frame it gets there, it goes into his cup (held after the animation and IK from then on).
        /// </summary>
        private double KeeperFlight(double t)
        {
            var kr = _kr;
            if (kr == null || !kr.Committed || kr.Fired == null || kr.Off || kr.Miss || t < kr.TakeT) return t;
            if (!kr.Taken && Refs.Keeper != null && _keeper != null)
            {
                kr.Taken = true;
                var keeper = _keeper;
                CatchHands.For(Refs.Keeper).Hold(_ball, 3.5f, () => _kr == kr && !kr.LetGo && !keeper.Released);
            }
            return kr.TakeT;
        }

        /// <summary>
        /// Outcome, past the bat and his (not bowled, not off the pad): the take the read set up, or — when nothing
        /// committed yet — set up now; a stumping's own clip. Sets <paramref name="end"/> to the flight time of the take.
        /// Returns the clip, or null (no clips: the old plain take).
        /// </summary>
        private string? KeeperOnBall(BallResult r, double since, bool lh, ref double end)
        {
            if (_keeper == null) return null;
            var kr = _kr;
            if (kr == null)
            {
                StartKeeperRead(r.Trajectory, lh, r.Timing == Timing.Leave, r.Trajectory.TimeAt(Batting.ContactZ(r.Delivery.Length)));
                kr = _kr;
                if (kr == null) return null;
            }
            kr.R = r;
            kr.Kind = r.Timing == Timing.Leave ? "leave" : r.Edge ? "edge" : "beaten";
            if (kr.Off)
            {
                // (The watch had it as the bat's; the engine says his: set it up afresh.)
                kr.Off = false;
                kr.Committed = false;
                kr.Rearmed = false;
            }
            if (!kr.Read && kr.Clip.Length > 0)
            {
                kr.Read = true;
                _keeper.MoveTo(kr.BodyTo, Mathf.Max(0.05f, RealAt(kr.TakeT) - Time.time - 0.05f));
                WatchKeeperTake(kr);
            }
            if (!kr.Committed) KeeperCommit(kr, r);
            if (kr.Miss && !kr.Taken)
            {
                end = kr.TakeT + 0.7 * _pace;
                return kr.Fired;
            }
            if (kr.Committed && Stumped(r) && (kr.Fired == null || !kr.Fired.Contains("stumping")))
            {
                // Out of his ground after he'd gone for a plain take: the stumping clip (the bails whipped off); the
                // gloves already have it, so no second reach.
                KeeperStumping(kr, Mathf.Max(0f, RealAt(kr.TakeT) - Time.time));
            }
            else if (kr.Fired != null && !kr.Rearmed && CaughtBehind(r))
            {
                kr.Rearmed = true;
                ArmKeeperReach(kr, kr.Fired, r);
            }
            // Four byes past him to the rope: he goes for it and it's past him — the ball flies on, no hands on it.
            if (r.ExtrasBoundary && !kr.Taken)
            {
                kr.Miss = true;
                if (Refs.Keeper != null)
                {
                    CatchHands.For(Refs.Keeper).Stop();
                    KeeperTakeProbe.For(Refs.Keeper.gameObject).Cancel();
                }
                end = kr.TakeT + 0.7 * _pace;
                if (Autoplay) Debug.Log($"[APL keeper] ball {Cricket.FormatOvers(_match.LegalBalls)}: {kr.Kind} beat him for four byes (no take: the ball flies on past the gloves)");
                return kr.Fired;
            }
            end = kr.TakeT;
            if (since > kr.TakeT && !kr.Taken && Autoplay)
                Debug.LogWarning($"[APL keeper] ball {Cricket.FormatOvers(_match.LegalBalls)}: take set up {since - kr.TakeT:0.00} s after the ball was there (late commit)");
            return kr.Fired;
        }

        /// <summary>A re-take with the ball already in his gloves (the stumping after a plain take): no second reach.</summary>
        private bool _keeperRetake;

        /// <summary>The stumping clip (the take, the bails whipped off a beat after): false if he has none.</summary>
        private bool KeeperStumping(KeeperRead kr, float left)
        {
            WireKeeperTakes();
            _keeperDelivery = true;
            _keeperCatch = false;
            _keeperRetake = kr.Taken;
            try
            {
                var clip = _keeper!.Stumping(kr.At, left);
                if (clip == null) return false;
                Extra(After(left + 0.25f, () => BreakWicket(true, Vector3.back)));
                kr.Fired = clip;
                kr.Rearmed = true;
                return true;
            }
            finally
            {
                _keeperDelivery = false;
                _keeperRetake = false;
            }
        }

        /// <summary>The take is measured (test runs): the drawn ball against its flight from the read to the take.</summary>
        private void WatchKeeperTake(KeeperRead kr)
        {
            if (!Autoplay || Refs.Keeper == null || _keeper == null) return;
            var keeper = Refs.Keeper;
            var rig = _keeper;
            var from = keeper.transform.position;
            var hands = CatchHands.For(keeper);
            KeeperTakeProbe.For(keeper.gameObject).Watch(_ball,
                () =>
                {
                    // Exactly as SetBall draws it (the release ease included).
                    var ft = Math.Min(FlightNow, kr.TakeT);
                    return FlightPoint(kr.Path, ft, kr.Lh) + _releaseOffset * (1f - Mathf.SmoothStep(0f, 1f, (float)(ft / ReleaseEase)));
                },
                () => _kr == kr && kr.Taken,
                () => hands.Cup,
                (snap, gap) =>
                {
                    var moved = Vector3.Distance(Flat(keeper.transform.position), Flat(from));
                    var local = Quaternion.Inverse(keeper.transform.rotation) * (kr.At - kr.Stance);
                    var ball = Cricket.FormatOvers(_match.LegalBalls);
                    // (Logged a beat on: a leave's kind is settled the frame it's taken.)
                    StartCoroutine(After(0.05f, () =>
                    {
                        var line = $"[APL keeper] ball {ball}: {kr.Kind} arrival offset {local.x:+0.00;-0.00} m side, {kr.At.y:0.00} m height, keeper moved {moved:0.00} m, clip {kr.Fired ?? "(none)"}, max ball snap {snap:0.00} m, take gap {gap:0.00} m ({(rig.StandingUp ? "up" : "back")}, {(kr.Path.Delivery.IsSpin ? "spin" : "pace")})";
                        if (snap > 0.05f || gap > 0.05f) Debug.LogWarning("FAIL " + line);
                        else Debug.Log(line);
                    }));
                });
        }

        // ------------------------------------------------------------ the pad and the stumps (PM: no flying through)

        /// <summary>A leave's first contact (flight time): the pad, 0.9 m out, if it's on the pad; else the stumps if it's hitting; else NaN.</summary>
        private double _leaveStopT = double.NaN;

        /// <summary>The engine's own geometry for a leave (Rules.Missed): onto the pad first, else into the stumps.</summary>
        private static double LeaveStop(Trajectory tr)
        {
            var d = tr.Delivery;
            if (d.Wide) return double.NaN;
            var pad = tr.At(tr.TimeAt(0.9));
            var back = d.Length >= Length.Short;
            if (pad.y <= 0.85 && pad.x >= -0.3 && pad.x <= (back ? 0.15 : 0.22)) return tr.TimeAt(0.9);
            return tr.HittingStumps() ? tr.TimeAt(0) : double.NaN;
        }

        /// <summary>
        /// Where the ball's flight ends at the batter (flight time), if it does: a settled miss that the engine has
        /// bowled (not chopped on) or dying on the pad; or, nothing played yet and <paramref name="leaveGeometry"/>, a
        /// leave's pad or stumps. The flight loops settle the ball the frame BEFORE it gets there, so Outcome's own
        /// flight stops it on the contact — it used to settle at decideAt, the ball through the batter by then, then
        /// snapped back to the pad or the stumps.
        /// </summary>
        private double? BallStopsAt(Trajectory tr, bool leaveGeometry)
        {
            var r = _result;
            if (r != null)
            {
                if (BatOnBall(r)) return null;
                if (r.Wicket && r.HowOut == "bowled" && !r.Edge) return tr.TimeAt(0);
                if (r.PadHit && r.Lbw != null && r.LegByes == 0 && !r.Wide) return r.Lbw.Tracking.ImpactTime;
                return null;
            }
            if (_tap == null && leaveGeometry && !double.IsNaN(_leaveStopT)) return _leaveStopT;
            return null;
        }

        /// <summary>The flight loops: settle now — the ball would get to the pad / stumps by the next frame.</summary>
        private bool StopsNext(Trajectory tr, double t, bool leaveGeometry) =>
            BallStopsAt(tr, leaveGeometry) is double s && t + Time.deltaTime * _pace >= s;

        /// <summary>Test runs: the pad / stumps contact, against where the ball was drawn when Outcome took over.</summary>
        private void LogPadStump(BallResult r, bool pad, double end, Vector3 entry, double entryT, bool lh)
        {
            if (!Autoplay) return;
            var at = FlightPoint(r.Trajectory, end, lh);
            var snap = Vector3.Distance(_ball.position, at);
            // (Settled after the contact: the ball was drawn past it and comes back — the old fly-through.)
            if (entryT > end) snap = Mathf.Max(snap, Vector3.Distance(entry, at));
            var line = $"[APL padstump] ball {Cricket.FormatOvers(_match.LegalBalls)}: {(pad ? "pad" : "stumps")} contact at {end:0.000} s, max snap {snap:0.00} m";
            if (snap > 0.05f) Debug.LogWarning("FAIL " + line);
            else Debug.Log(line);
        }

        // ------------------------------------------------------------ -aplkeeperloop (test runs only)

        private bool? _keeperLoop;
        private int _keeperTries;

        /// <summary>
        /// -aplkeeperloop on top of -aplmatchtest: every ball the user's side bowls is a keeper's ball — the AI batter
        /// leaves it or plays and misses (well early), on lines from leg-side wide to off-side wide and lengths full to
        /// short, pace (keeper back) and spin / medium (keeper up) as the overs come; the last ~10 frames before each take
        /// are saved (with -aplshots) as NN_closeup_keeper_&lt;ball&gt;_&lt;n&gt;.png.
        /// </summary>
        private bool KeeperLoop => _keeperLoop ??= Autoplay && Environment.GetCommandLineArgs().Contains("-aplkeeperloop");

        private static readonly float[] KeeperLoopLines = { 0.35f, 0.6f, 0.85f, 1.15f, 1.45f, -0.4f, -0.7f };

        /// <summary>The keeper loop's aim (batter frame: x across, + off; z from his stumps), or null.</summary>
        private Vector2? KeeperLoopMark(bool spin)
        {
            if (!KeeperLoop) return null;
            var x = KeeperLoopLines[_keeperTries % KeeperLoopLines.Length];
            var lengths = spin ? new[] { 2.8f, 4.2f, 5.6f } : new[] { 3.5f, 5.5f, 7.5f, 9.5f };
            return new Vector2(x, lengths[(_keeperTries / KeeperLoopLines.Length) % lengths.Length]);
        }

        /// <summary>The keeper loop's AI shot: alternately a leave and a play-and-miss well early (a clean beaten ball).</summary>
        private ShotInput KeeperLoopShot(ShotInput shot, Trajectory t, double window)
        {
            if (!KeeperLoop) return shot;
            var leave = _keeperTries++ % 2 == 0;
            return leave ? ShotInput.Leave : new ShotInput(ShotType.Push, Zone.Cover, Batting.IdealTap(t) - 4.5 * window);
        }

        /// <summary>A frame of the take, filmed (keeper loop with -aplshots).</summary>
        private void KeeperFrame(KeeperRead kr, string what)
        {
            if (!Apl.Client.Dev.AutoShot.Active || kr.Shots >= 12) return;
            var file = Apl.Client.Dev.AutoShot.CloseUpFile($"keeper_{EvidenceBall}_{++kr.Shots:00}_{what}");
            if (file != null) Apl.Client.Dev.CaptureWriter.Screen(file);
        }
    }
}
