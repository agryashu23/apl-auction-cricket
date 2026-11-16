using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// After the hit: the ball flies exactly the engine's path (carry and hang,
    /// the bounce and the roll, over the rope), and the field plays out what the
    /// engine decided — the catcher gets under it and the ball meets his hands on
    /// the catch frame, a dropped one pops out, a ball along the ground is cut off
    /// with the clip that fits (pick-up, dive, slide, relay, flick back from the
    /// rope) and thrown to the right end, a second fielder converges, another
    /// backs up — with the fielding camera on whoever is doing the work.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>This ball off the bat: its result, which way the batter stands, the bat at contact, and when.</summary>
        private BallResult? _hitResult;
        private bool _hitLeft;
        private Vector3 _hitFrom;
        private float _hitAt;

        /// <summary>The field play in progress, who went for the ball, and who came across to help.</summary>
        private FieldPlay? _play;
        private Animator? _chaser, _converger;

        /// <summary>A stand-in ball for a fielder going through the motions (the one it went through the hands of).</summary>
        private Transform? _ghostBall;

        private const float BallRadius = 0.036f;

        /// <summary>
        /// Where the ball is on its own <paramref name="t"/> s after the hit: the
        /// engine's path (batter-centred) in the ground's frame, starting from the
        /// bat's middle (its offset eased out by the landing), bouncing on landing.
        /// </summary>
        private Vector3 PathAt(float t)
        {
            var r = _hitResult!;
            var path = r.Path;
            var (x, f, h) = path.At(t);
            var p = GroundSpace.Field(x, f, _hitLeft, (float)h);
            var off = _hitFrom - GroundSpace.Field(0, 0, _hitLeft, BallRadius);
            var hang = (float)path.Hang;
            if (path.InAir)
            {
                // The landing takes the pace off smoothly (MatchDirector.BallVariety); the height stays the engine's.
                var landing = SmoothLanding(path, t);
                if (landing >= 0f)
                {
                    var flat = GroundSpace.Field(0, 0, _hitLeft) + GroundSpace.Direction(path.AngleDeg, _hitLeft) * landing;
                    p = new Vector3(flat.x, p.y, flat.z);
                }
                p += off * Mathf.Clamp01(1 - t / Mathf.Max(0.05f, hang));
                // Comes down at g·hang/2 and kicks up off the turf.
                if (t > hang && path.RollSpeed > 0) p.y += Hops(t - hang, Mathf.Min(5f, 0.3f * 9.81f * hang / 2), 0.4f);
            }
            else
            {
                // Along the ground: off the bat it skims down from the bat to its first bounce
                // (the engine's skim), then a couple of low bounces and a fast roll.
                var first = path.Skim > 0 ? Mathf.Max(0.05f, (float)path.RollAt) : 0.12f;
                var k = Mathf.Clamp01(t / first);
                // The first bounce takes its pace off smoothly, not in one frame (same place by 0.72 s; MatchDirector.BallVariety).
                var along = SmoothSkim(path, t);
                if (along >= 0f) p = GroundSpace.Field(0, 0, _hitLeft) + GroundSpace.Direction(path.AngleDeg, _hitLeft) * along;
                var fade = Mathf.Clamp01(1f - t / SkimSmooth);
                p += new Vector3(off.x, 0, off.z) * (fade * fade);
                p.y += off.y * (1 - k) * (1 - k) + (path.Skim > 0 ? 0.12f * 4 * k * (1 - k) : 0f);
                // This hit's own bounce and slice (MatchDirector.BallVariety).
                if (t > first) p.y += Hops(t - first, Mathf.Clamp((float)path.RollSpeed * _hopScale, 0.3f, 2.6f), _hopKeep);
                p += Drift(t);
            }
            // Put down: it pops up out of his hands and falls (MatchDirector.CatchVariety).
            p.y += DropPop(t);
            p.y = Mathf.Max(BallRadius, p.y + BallRadius);
            return p;
        }

        /// <summary>
        /// The drawn ball is scaled up for the phone and outlined (BallView): rest that, not
        /// the 7.5 cm ball, on the turf, so a rolling ball isn't drawn half under the grass.
        /// </summary>
        private Vector3 OnTurf(Vector3 p)
        {
            var drawn = _ball.localScale.x * 0.5f * BallView.OutlineScale;
            var lift = Mathf.Max(0f, drawn - BallRadius) * Mathf.Clamp01(1.5f - p.y);
            return p + Vector3.up * lift;
        }

        /// <summary>Height of a ball bouncing from the ground at <paramref name="vy"/> m/s, each bounce <paramref name="keep"/> of the last.</summary>
        private static float Hops(float tau, float vy, float keep)
        {
            const float g = 9.81f;
            while (vy > 0.4f)
            {
                var span = 2 * vy / g;
                if (tau < span) return vy * tau - 0.5f * g * tau * tau;
                tau -= span;
                vy *= keep;
            }
            return 0f;
        }

        /// <summary>Metres inside this ground's rope (negative: past it) of a world point.</summary>
        private float RopeGap(Vector3 world)
        {
            var x = _hitLeft ? -world.x : world.x;
            var f = world.z + GroundSpace.HalfPitch;
            var angle = Mathf.Atan2(x, f) * Mathf.Rad2Deg;
            var rope = _fieldNow?.Rope(angle) ?? Field.Boundary(angle);
            return (float)rope - Mathf.Sqrt(x * x + f * f);
        }

        private FieldBall Situation(BallResult r, Vector3 target, bool tight) => new FieldBall
        {
            At = PathAt,
            RopeGap = RopeGap,
            RunTime = SprintTime,
            From = GroundSpace.Field(0, 0, _hitLeft),
            Due = (float)r.EndTime,
            Hang = (float)r.Path.Hang,
            Apex = (float)r.Path.Apex,
            Target = target,
            Tight = tight,
            Dived = r.FieldPlay?.Dived ?? false,
            ReleaseBy = ThrowCraft.On && !r.Caught ? EngineRelease(r) : float.NaN, // (area 3: own throws picked to the engine's release)
            RunOut = r.RunOut,
            Variety = (float)_visRng.NextDouble(),
        };

        /// <summary>The body of the fielder the engine names (this ball's field), or null.</summary>
        private Animator? NamedFielder(string name)
        {
            if (name.Length == 0) return null;
            for (var i = 0; i < _fielders.Count && i < _fieldNames.Length; i++)
            {
                if (_fieldNames[i] == name && _fielders[i].a.gameObject.activeSelf) return _fielders[i].a;
            }
            return null;
        }

        /// <summary>
        /// Who goes for a ball along the ground: the fielder the result names,
        /// unless the bowler (in his follow-through) or the keeper is clearly
        /// quicker there — a push back down the pitch is the bowler's.
        /// </summary>
        private Animator? GroundChaser(BallResult r, Vector3 at)
        {
            var named = NamedFielder(r.Fielder);
            var candidates = _fielders.Select(f => f.a).Where(a => a.gameObject.activeSelf).ToList();
            if (Refs.Bowler != null) candidates.Add(Refs.Bowler);
            if (Refs.Keeper != null) candidates.Add(Refs.Keeper);
            Animator? best = null;
            var bestTime = float.MaxValue;
            foreach (var c in candidates)
            {
                var t = SprintTime(Vector3.Distance(Flat(c.transform.position), at));
                if (t < bestTime)
                {
                    bestTime = t;
                    best = c;
                }
            }
            return named != null && SprintTime(Vector3.Distance(Flat(named.transform.position), at)) < bestTime + 0.6f ? named : best;
        }

        /// <summary>Whoever is doing the work for the camera: the one holding or chasing the ball, then whoever takes the throw.</summary>
        private Vector3? Worker()
        {
            var w = _play != null && _play.Thrown && _receiver != null && Time.time - _throwStart > 0.4f ? _receiver : _play?.Thrower ?? _chaser;
            return w != null ? Hips(w).position : (Vector3?)null;
        }

        private IEnumerator RunFor(Animator a, Vector3 to, float byAfterHit, bool pullUp, Quaternion face) =>
            RunTo(a, to, byAfterHit - (Time.time - _hitAt), false, pullUp, 9f, face);

        private static IEnumerator NoThrow(FieldPlay p, Animator a, Vector3 from, float speed)
        {
            yield break;
        }

        /// <summary>The ball's off the bat: fly it, field it, score it.</summary>
        private IEnumerator Hit(BallResult r, bool leftHanded, string batterName)
        {
            // (The bat's sound is played at contact by ContactFeel, MatchDirector.HitFeel.cs: middled / plain / nick.)
            _marker.gameObject.SetActive(false);
            _hitAt = Time.time;
            // Test runs only: once an innings the batters chance a risky extra run (MatchDirector.TestNudges).
            AutoSuicideNudge(r);
            AutoRunOutNudge(r);
            RunOutLoopNudge(r); // -aplrunoutloop: a run out every runnable ball (TEC-1 repro, MatchDirector.RunOutLoop)
            _hitResult = r;
            _hitLeft = leftHanded;
            RollVariety(r);
            // Off the middle of the bat (the clip's contact frame is now), and the tracer follows it from here.
            _hitFrom = _striker.UsesClips ? _striker.BatMiddle : _ball.position;
            _ball.position = _hitFrom;
            _trail.Clear();
            _trail.emitting = true;
            if (_fieldNow == null)
            {
                _fieldNow = _match.Field;
                _fieldNames = _fieldNow.Fielders.Select(f => f.Name).ToArray();
            }
            var path = r.Path;
            var air = path.InAir;
            var endAt = GroundSpace.Field(r.End.x, r.End.f, leftHanded);
            if (Autoplay && !air)
            {
                // Test runs: the pace off the bat against what the ball does on screen.
                float Speed(float at) => Flat(PathAt(at + 0.02f) - PathAt(at)).magnitude / 0.02f;
                Debug.Log($"[APL match] ground shot: exit {r.ExitSpeed:0.0} m/s; on screen {Speed(0.03f):0.0} off the bat, {Speed((float)path.RollAt + 0.05f):0.0} after the first bounce, {Speed(1.5f):0.0} at 1.5 s; {(r.Four ? "four" : $"fielded at {r.EndTime:0.00}s")}");
            }

            // The batters set off for what they went for (the engine's race; a run out is short of the last).
            // (rules-bowling: MatchDirector.Running.cs — the user's RUN / STAY when he's batting.)
            var running = StartRunning(r, out var runTime);

            // Run out at the bowler's end off his fingertips (MatchDirector.BowlerEnd): that's the whole play.
            if (BowlerEndDeflection(r))
            {
                _chaser = Refs.Bowler;
                yield return DeflectionOntoStumps(r);
                yield return EnsureBroken(r);
                yield return Refer(r, RunOutMoment(r));
                yield return Wicket(r, r.OutBatter?.Name ?? batterName);
                yield break;
            }

            // Who does what.
            _chaser = _converger = _receiver = _backer = _relayCatcher = null;
            _dropAt = -1f;
            _receiverSpot = null;
            _receiverSpotFor = null; // (area 3, MatchDirector.Gather)
            _looseRun = null;
            _trace = null;
            _receiveBreakAt = -1f;
            FieldPlay? play = null, ghost = null;
            var keeperEnd = true;
            float? returnCatch = null;
            if (r.CaughtAndBowled)
            {
                // Caught and bowled: the bowler, from his follow-through (MatchDirector.ReturnCatch).
                _chaser = Refs.Bowler;
                returnCatch = ReturnCatch(r, r.Delivery.IsSpin);
            }
            else if (r.Caught)
            {
                _chaser = NamedFielder(r.Fielder) ?? GroundChaser(r, endAt);
                // Guard: the man the engine names must be able to get there (a fast man reaches ~7 m/s
                // for the ball's hang plus a dive). If not, the view and the engine disagree about where he
                // stands — logged as an error in test runs, and the nearest man takes it so it's never shown
                // as a catch 40 m from the ball.
                if (_chaser != null && Vector3.Distance(Flat(_chaser.transform.position), endAt) > 7.5f * ((float)path.Hang + 0.6f) + 3f)
                {
                    var near = NearestTo(endAt, null);
                    if (Autoplay) Debug.LogError($"FAIL [APL match] catch: {_chaser.name} ({r.Fielder}) is {Vector3.Distance(Flat(_chaser.transform.position), endAt):0.0} m from the catch, out of reach; {near?.name} takes it");
                    if (near != null) _chaser = near;
                }
                // The slip cordon's own catches only for the slips and gully (r11: "slip_catch_chest" in the outfield).
                var cordon = r.Fielder.Contains("slip") || r.Fielder.Contains("gully");
                if (_chaser != null) play = FieldPlay.PlanCatch(_chaser, Situation(r, KeeperTarget, false),
                    SeqClip.All.Where(c => c.IsCatch && !c.PartnerOnly && (cordon ? CordonCatch(c) : FieldCatch(c, r))));
                // No catch clip gets him there in time (r11: a late dive clip "caught" balls 10 m away and never
                // played): he runs under it instead, the flight timed to his arrival, the ball into his hands.
                if (play != null && play.LateBy > 0.15f)
                {
                    if (Autoplay) Debug.Log($"[APL match] catch plan {play.Clip.Name} late by {play.LateBy:0.00}s: running under it instead");
                    play = null;
                }
                // At the rope: now and then taken and lobbed in to a team-mate who completes it.
                if (play != null) RelayCatch(play);
                // Final rating 2026-11-14 (catch variety): counted for the long-dive cap, and logged for the PM.
                if (play != null)
                {
                    FieldPlay.NoteCatch(play.Clip.Name);
                    var (dives, of) = FieldPlay.LongDiveShare;
                    Debug.Log($"[APL catch] clip={play.Clip.Name} by {_chaser?.name}: ball y {play.BallAt.y:0.00} m, run {Vector3.Distance(Flat(_chaser != null ? _chaser.transform.position : play.BallAt), Flat(play.BallAt)):0.0} m, {play.Spare:0.00} s in hand, long dives {dives} of the last {of} catches");
                }
            }
            else if (!r.Four && !r.Six)
            {
                var fielder = GroundChaser(r, endAt);
                var (target, toKeeper, tight) = ThrowPlan(r, endAt, fielder);
                keeperEnd = toKeeper;
                var b = Situation(r, target, tight);
                var dropper = NamedFielder(r.Dropped);
                if (dropper != null)
                {
                    var spill = SeqClip.Named("seq_35_dropped_catch_pickup_quick_throw");
                    if (spill != null && (fielder == dropper || fielder == null || Vector3.Distance(Flat(dropper.transform.position), endAt) < 8f))
                    {
                        // Goes down, and he picks it up himself.
                        play = FieldPlay.PlanCatch(dropper, b, new[] { spill });
                        if (play != null) _chaser = dropper;
                    }
                    // It goes through his hands and runs on.
                    if (play == null) ghost = FieldPlay.PlanCatch(dropper, b);
                    if (ghost != null) _dropAt = ghost.EventAt;
                }
                if (play == null && fielder != null && fielder != dropper)
                {
                    _chaser = fielder;
                    if (fielder == Refs.Bowler) _bowler.Settle();
                    if (fielder != Refs.Bowler && fielder != Refs.Keeper)
                    {
                        var mates = _fielders.Select(f => f.a).Where(a => a.gameObject.activeSelf && a != fielder && a != dropper).ToList();
                        var pair = FieldPlay.PlanPair(fielder, b, mates);
                        if (pair != null && pair.Score < 0) play = pair;
                    }
                    play ??= FieldPlay.PlanGround(fielder, b);
                }
                else if (play == null && fielder != null)
                {
                    // He put it down and goes after it.
                    _chaser = fielder;
                }
                if (play != null)
                {
                    // Area 3: the throw timed to the engine's, the clip paced, test runs watch it (MatchDirector.Gather).
                    PrepareThrow(play, r);
                    var release = PlannedRelease(play);
                    var flight = Vector3.Distance(Flat(play.BallAt), Flat(target)) / 26f;
                    ReadyForThrow(play, keeperEnd, release + flight);
                    Converge(play);
                    ReachForStop(play); // (dev 2: hands down to the ball at contact — MatchDirector.CatchHands)
                }
            }
            _play = play;
            // The throw is turned at whoever will take it, decided live (MatchDirector.ThrowIn).
            if (play != null)
            {
                var aimed = play;
                play.Aim = () => ThrowAim(aimed);
            }
            if (Autoplay && play != null) Debug.Log($"[APL match] fielding {play.Clip.Name}{(play.Partner != null ? " + " + play.Partner.Clip.Name : "")} by {play.Fielder.name}: hands on it at {play.EventAt:0.00}s (engine {r.EndTime:0.00}s), {(r.Caught ? "caught" : r.Dropped.Length > 0 ? "dropped" : "throw " + (keeperEnd ? "keeper" : "bowler"))}");

            // The fielding camera, once any shot showcase has had its moment.
            var covered = r.ExitSpeed > 12 || air;
            // The shot's name, and a moment on the follow-through (presentation); the fielding camera waits behind it.
            Showcase(r, (float)r.EndTime, covered);
            if (covered) Camera.FieldingShot(() => _ball.position, Worker);

            if (ghost != null)
            {
                _ghostBall ??= new GameObject("GhostBall").transform;
                FieldRoutine(ghost.Play(this, _hitAt, _ghostBall, RunFor, NoThrow, false));
            }
            BeatenDive(r);
            if (play != null) FieldRoutine(play.Play(this, _hitAt, _ball, RunFor, ThrowIn, true));
            else if (r.Four || r.Six) ChaseBoundary(r, endAt);
            else if (_chaser != null)
            {
                // No clip fits: run there and pick it up.
                if (_chaser == Refs.Bowler) _bowler.Settle();
                // A catch: right under it (his hands, not his feet, on the ball's line).
                var stop = Vector3.MoveTowards(endAt, Flat(_chaser.transform.position), r.Caught ? 0.45f : 0.9f);
                _looseRun = FieldRoutine(RunTo(_chaser, stop, Mathf.Max(0.3f, (float)r.EndTime)));
                // Area 3: the man taking the throw to his stumps now, as for a planned play (MatchDirector.Gather).
                if (!r.Caught) ReadyForLoose(r, endAt, _chaser);
            }

            // The ball on its own until a hand meets it (or it's over the rope, or stops).
            var stopAt = returnCatch ?? (play != null ? play.EventAt
                : r.Six ? (float)path.Hang * 1.4f
                : r.Four ? (float)r.EndTime + 3f
                : r.Caught ? CatchArrival(r)
                : Mathf.Max((float)r.EndTime, 0.35f));
            // A catch is only ever taken on the full (r9: one was "caught" after it had bounced):
            // the flight is timed so the ball comes down to catching height as the hands get there,
            // and it never goes lower than that before they have it (MatchDirector.CatchClean).
            var clean = r.Caught && returnCatch == null ? CleanCatch(r, play != null ? play.EventAt : stopAt) : ((float warp, float low)?)null;
            // (Deep area 3: his palm cup, kept after the hand IK — it was the right wrist. MatchDirector.CatchHands.)
            var catchHand = clean != null && play == null && _chaser != null ? CatchHands.For(_chaser).CupTransform : null;
            var catchClip = false;
            // Test runs: the lowest the ball came before the hands (a catch must stay above the turf), and a shot at the take.
            var lowest = float.MaxValue;
            var tookShot = false;
            var blendGap = -1f;
            var flightT = 0f;
            var planMiss = -1f;
            var gone = false;
            // A fielder who's late doesn't freeze the ball: it rolls on (slowing) until his hands have it.
            var waitHands = play != null && returnCatch == null && !r.Four && !r.Six;
            // No clip: it rolls on (easing to rest) until the man running to it gets a hand to it.
            var chaserHand = play == null && _chaser != null && !r.Four && !r.Six && !r.Caught ? _chaser.GetBoneTransform(HumanBodyBones.RightHand) : null;
            var logged = false;
            if (Autoplay) FieldRoutine(BallWatch());
            while (true)
            {
                var t = Time.time - _hitAt;
                if (waitHands && play!.HasBall)
                {
                    // A ground stop: where its roll had it against his palms (measured only; MatchDirector.CatchHands).
                    LogStop(play, _ball.position);
                    break;
                }
                Vector3 p;
                if (clean is (float _, float low))
                {
                    // A catch: the flight runs on its own clock, paced so it's at the planned meeting point
                    // (the clip's hands) when the hands get there — re-paced every frame, as the take can be
                    // re-timed after planning (r12: it reached its hold point 1 s early, hung, then was dragged
                    // 4.8 m into the hands). Never below catching height before the hands.
                    var aimFlight = Mathf.Min(play != null && !float.IsNaN(play.BallTime) ? play.BallTime : low, low);
                    var aimReal = play != null ? play.EventAt : stopAt;
                    // (Up to 2x: a take re-timed earlier than planned must be caught up with — r12 run 2 lagged 0.13 s at 1.25x, 2.5 m.)
                    // (Dev 2, build 5: a 0.05 s floor here slowed the last frames to the 0.3× clamp — the ball "hung" just
                    // short of his hands. One frame's time is the floor: it arrives on the frame, at its pace.)
                    var rate = Mathf.Clamp((aimFlight - flightT) / Mathf.Max(Time.deltaTime, aimReal - t), 0.3f, 2f);
                    flightT = Mathf.Min(flightT + Time.deltaTime * rate, aimFlight);
                    p = PathAt(flightT);
                    // Deep area 3: the hands go to the ball (hand IK, CatchHands) and the flight keeps its own line —
                    // it used to be lerped onto the live WRIST over the last 0.8 s (and again over 0.2 s), dragging it
                    // 0.33–0.59 m sideways to the belly. Only what his arms can't reach shifts it (MatchDirector.CatchHands).
                    p = CatchFlight(p, aimFlight, aimReal, t, play, play != null ? play.Fielder : catchHand != null ? _chaser : null, ref planMiss, flightT);
                }
                else p = PathAt(waitHands || chaserHand != null ? t : Mathf.Min(t, stopAt));
                // A return catch: like a catch, only what his arms can't reach shifts the ball (MatchDirector.CatchHands).
                if (returnCatch is float rc) p = ReturnFlight(p, t, rc);
                // No catch clip planned: the man under it goes into his catch as it drops in.
                if (catchHand != null && !catchClip && t >= stopAt - 0.45f)
                {
                    catchClip = true;
                    // (By the ball's height and side where his hands will meet it — it read the height 0.45 s early.)
                    var underIt = CatchClipAt(_chaser!, _catchTruth, Vector3.Distance(Flat(_chaser!.transform.position), endAt));
                    _catchClip = underIt?.Name ?? "";
                    Event(_chaser!, underIt);
                    if (r.Caught)
                    {
                        FieldPlay.NoteCatch(_catchClip);
                        Debug.Log($"[APL catch] clip={(_catchClip.Length > 0 ? _catchClip : "(none)")} by {_chaser!.name}: ran under it, ball y {_catchTruth.y:0.00} m");
                    }
                }
                if (chaserHand != null && t >= stopAt && (Vector3.Distance(Flat(chaserHand.position), Flat(p)) < 1.2f || t >= stopAt + 2.5f)) break;
                if (r.Four && t > r.EndTime && RopeGap(p) < -0.4f)
                {
                    // Into the rope's cushion: it kicks up and back and comes to rest (MatchDirector.BallVariety).
                    Extra(RopeCushion(OnTurf(p), (float)r.Path.SpeedAt(t)));
                    break;
                }
                if (r.Six && t > r.EndTime && (RopeGap(p) < -16f || p.y < 4f))
                {
                    gone = true;
                    break;
                }
                var shown = OnTurf(p);
                _ballPhase = path.InAir && t < path.Hang ? "in the air"
                    : path.DeflectAt > 0 && t > path.DeflectAt ? "palmed on"
                    : t < SkimSmooth ? "smooth start" : "engine roll";
                // The last 0.15 s of its way: onto the hands that take it, never snapped to a hand
                // somewhere else (the plan already ends the flight at the clip's catch point).
                Transform? taker = returnCatch != null ? _returnHand : catchHand;
                var meetAt = returnCatch ?? (play != null ? play.EventAt : catchHand != null ? stopAt : 0f);
                var into = 1f - (meetAt - t) / HandBlend;
                if (into > 0f && !r.Four && !r.Six && (taker != null || play != null && play.Started))
                {
                    var hand = taker != null ? taker.position : play!.HandNow();
                    _ballPhase = "into the hands";
                    if (Autoplay && t >= stopAt && !logged)
                    {
                        logged = true;
                        LogCatchMiss(r, play, OnTurf(PathAt(stopAt)), hand);
                    }
                    if (CatchBlend(hand) is Vector3 left)
                    {
                        // A reached catch: only the cup's last miss is blended in (the ball still flies its own way in).
                        // "Blend carried" = the most it actually moved the ball (not the gap while the arms were still reaching).
                        var applied = left * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(into));
                        blendGap = Mathf.Max(blendGap, applied.magnitude);
                        shown += applied;
                    }
                    else
                    {
                        if (blendGap < 0f) blendGap = Vector3.Distance(shown, hand);
                        shown = Vector3.Lerp(shown, hand, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(into)));
                    }
                    if (r.Caught && into >= 1f && !tookShot)
                    {
                        tookShot = true;
                        Apl.Client.Dev.AutoShot.Moment("catch_hands");
                        // The drawn ball against the hand at the take, and how far the last 0.2 s had to carry it in.
                        CatchMeets(shown, hand, play?.Fielder ?? _chaser, lowest, blendGap);
                        LogPlanMiss(planMiss);
                        Extra(After(0.3f, () => Apl.Client.Dev.AutoShot.Moment("catch_held")));
                    }
                }
                // A catch is on the full: a diving take low to the turf is shown just above it, never touching
                // (the plan's hands for a forward dive can be at the grass — the ball went to 0.06 m, a "bump ball").
                if (r.Caught && returnCatch == null && !tookShot) shown.y = Mathf.Max(shown.y, MinCatchY);
                _ball.position = shown;
                if (r.Caught) NoteDrawn(shown); // (the drawn-ball check at contact: MatchDirector.CatchHands)
                if (r.Caught && !tookShot && t > 0.3f) lowest = Mathf.Min(lowest, shown.y);
                if (t >= stopAt && chaserHand == null && (!waitHands || t >= stopAt + 4f)) break;
                yield return null;
            }
            _trail.emitting = false;
            if (r.Caught && !tookShot && returnCatch == null)
            {
                // His clip had it in his hands first (the loop stops there): the shot now.
                Apl.Client.Dev.AutoShot.Moment("catch_hands");
                CatchMeets(_ball.position, play != null ? play.HandNow() : catchHand != null ? catchHand.position : _ball.position, play?.Fielder ?? _chaser, lowest, blendGap);
                LogPlanMiss(planMiss);
                Extra(After(0.3f, () => Apl.Client.Dev.AutoShot.Moment("catch_held")));
            }
            // No clip fitted: the man who ran to it picks it up (it doesn't lie there till he throws).
            // (Area 3: his chase stopped, the pick-up's pose kept under his hips, into his hands — MatchDirector.Gather.)
            if (play == null && _chaser != null && !r.Four && !r.Six && !r.Caught && ThrowCraft.On) LoosePickup(_chaser);
            else if (play == null && _chaser != null && !r.Four && !r.Six && !r.Caught)
            {
                var pickHand = _chaser.GetBoneTransform(HumanBodyBones.RightHand);
                if (pickHand != null)
                {
                    Event(_chaser, CricketClips.FieldClipNamed("field_13_two_hand_pickup"));
                    Extra(EaseHold(pickHand, 0.6f));
                }
            }
            if (gone) Extra(After(0.25f, () => _ball.gameObject.SetActive(false)));

            // (MANUAL running can still change a run out — a dive, a turn-back: RunsDone shows it then.)
            if (r.Wicket && !r.Caught && play != null && !RunState.Manual)
            {
                // Run out: the throw comes in and the wicket's broken (MatchDirector.RunOutBreak), the batter short, in shot.
                var until = Time.time + 9f;
                while (!_wicketBroken && !play.Done && Time.time < until) yield return null;
                yield return new WaitForSeconds(_wicketBroken ? 1.1f : 0.35f);
                Camera.EndFielding(false);
                // Never a referral without the wicket broken on screen; the replay on that moment.
                yield return EnsureBroken(r);
                yield return Refer(r, RunOutMoment(r));
                yield return Wicket(r, r.OutBatter?.Name ?? batterName);
            }
            // (A run out with no throw sequence, or one the user's RUN brought on later: RunsDone shows it.)
            else if (r.Wicket && !r.RunOut)
            {
                var ballUp = false;
                if (returnCatch != null && _returnHand != null)
                {
                    // In his hands; let his clip's own celebration play before the wicket's.
                    Extra(Hold(_returnHand, 4f));
                    yield return new WaitForSeconds(1.1f);
                }
                else if (play != null)
                {
                    // Show him take it (and, at the rope, toss it and come back in for it).
                    var shown = play.TossTo != null ? play.TossCaughtAt + 0.4f : !float.IsNaN(play.Clip.RecatchAt) ? play.HitTime(play.Clip.RecatchAt) + 0.3f : play.EventAt + 0.7f;
                    while (Time.time - _hitAt < shown) yield return null;
                    ballUp = !play.Clip.HasThrow;
                    // His until the celebration flings it up (BallInTheAir takes it from his hand 0.6 s into the wicket).
                    var held = play;
                    if (ballUp) Extra(After(0.6f, () => held.LetGo = true));
                }
                else if (_chaser != null)
                {
                    if (!catchClip) Event(_chaser, CricketClips.PickCatch(_ball.position.y, Vector3.Distance(Flat(_chaser.transform.position), endAt)));
                    // In his hands until the celebration flings it up.
                    if (catchHand != null) Extra(Hold(catchHand, 0.6f));
                    ballUp = true;
                }
                Camera.EndFielding(false);
                yield return Refer(r);
                yield return Wicket(r, batterName, r.Caught ? _relayCatcher ?? _chaser : null, ballUp);
            }
            else if (r.Six)
            {
                _comm.Say(SixLine(r), important: true); // (matched to the hit: audio-modes, CommentaryCalls)
                StadiumBuilder.Flash(350);
                StadiumBuilder.CrowdCheer(1f, BattingSide.Id);
                StadiumBuilder.CrowdBanners(true, _ball.position, BattingSide.Id);
                Sfx.Cheer(1f);
                Sfx.Applause(1f);
                CrowdChant(batting: true, 0.6f);
                Milestone();
                yield return new WaitForSeconds(0.8f);
                Camera.EndFielding(false);
                // The umpire's signal, the big number, a replay of the big ones (presentation).
                yield return BoundaryDirection(r);
            }
            else if (r.Four)
            {
                _comm.Say(FourLine(r), important: true); // (matched to the hit: audio-modes, CommentaryCalls)
                StadiumBuilder.Flash(150);
                StadiumBuilder.CrowdCheer(0.6f, BattingSide.Id);
                StadiumBuilder.CrowdBanners(false, _ball.position, BattingSide.Id);
                Sfx.Cheer(0.7f);
                Sfx.Applause(0.8f);
                CrowdChant(batting: true, 0.35f);
                Milestone();
                yield return new WaitForSeconds(0.8f);
                Camera.EndFielding(false);
                // The umpire's signal, the big number, a replay of the big ones (presentation).
                yield return BoundaryDirection(r);
            }
            else
            {
                if (r.Dropped.Length > 0)
                {
                    Show("DROPPED!", $"put down by {r.Dropped}", UiKit.Bad, 1.6f);
                    Sfx.Ooh(0.9f);
                }
                // The runs are called once the ball's back with the keeper or the bowler (RunsDone), not mid-play.
                if (r.Runs > 0) Sfx.Applause(0.25f);
                _comm.Say(r.Runs switch { 0 => "defended", 1 => "single", 2 => "two", _ => "three" }, r.Runs switch { 0 => 0.75f, 1 => 0.8f, 2 => 0.9f, _ => 1f }); // (a call on most balls: audio-modes, round 11)
                Milestone();
                if (play != null)
                {
                    // The play throws it in itself (MANUAL: and waits at the stumps for the call, then breaks them).
                    var until = Time.time + 12f;
                    while (!play.Done && Time.time < until) yield return null;
                }
                // Area 3: gathered, set and thrown from his hand to the right end, like a planned one (MatchDirector.Gather).
                else if (ThrowCraft.On) yield return LooseThrow(_chaser);
                else
                {
                    // Back to the keeper.
                    var thrower = _chaser;
                    var dummy = new FieldPlay { Fielder = thrower ?? Refs.Keeper!, Clip = new SeqClip { Name = "loose" }, Target = KeeperTarget, BallAt = _ball.position, EventAt = Time.time - _hitAt };
                    if (thrower != null)
                    {
                        // Turned to the man taking it first (not still facing the way he ran), then the throw.
                        dummy.Thrower = thrower;
                        Body.Rebase(thrower);
                        Extra(Face(thrower, ThrowAim(dummy), 0.3f));
                        Extra(After(0.45f, () => Event(thrower, CricketClips.FieldClipNamed("throw_18_overarm_throw"))));
                    }
                    yield return new WaitForSeconds(0.5f);
                    if (thrower != null) yield return ThrowIn(dummy, thrower, _ball.position + Vector3.up * 1.2f, 26f);
                }
                Camera.EndFielding();
            }
            // (After a wicket they're celebrating: the next ball puts them back.)
            if (!r.Wicket) ReturnToPositions(_play != null && !_play.Done ? _play.Fielders : Enumerable.Empty<Animator>());
            // The batters home (or one of them run out by the user's call): MatchDirector.Running.cs.
            if (running) yield return RunsDone(r, batterName);
            else if (!r.Wicket) yield return Refer(r);
            yield return new WaitForSeconds(r.Wicket ? 0.3f : r.Six || r.Four ? 0.3f : running ? 0.4f : 0.6f);
            yield return Salute();
        }

        /// <summary>A second fielder comes across to the ball's line and pulls up when the first has it.</summary>
        private void Converge(FieldPlay play)
        {
            Animator? best = null;
            var bestTime = play.EventAt + 1.2f;
            foreach (var (a, _) in _fielders)
            {
                if (!a.gameObject.activeSelf || play.Fielders.Contains(a)) continue;
                var d = Vector3.Distance(Flat(a.transform.position), Flat(play.BallAt));
                var t = SprintTime(d);
                if (d > 5f && t < bestTime)
                {
                    bestTime = t;
                    best = a;
                }
            }
            if (best == null) return;
            _converger = best;
            // Backing up, not a second chaser (v23 shot 74 had both going for it): he jogs in behind
            // the ball's line, ~7 m deeper than where it's fielded and off to his own side, and pulls up
            // there in case it's misfielded — never onto the ball itself.
            var ballAt = Flat(play.BallAt);
            var line = ballAt - new Vector3(0, 0, -GroundSpace.HalfPitch);
            line = line.sqrMagnitude > 0.01f ? line.normalized : Vector3.forward;
            var spot = ballAt + line * 7f + Vector3.ClampMagnitude(Flat(best.transform.position) - ballAt, 1f) * 2.5f;
            if (RopeGap(spot) < 2f) spot = Vector3.MoveTowards(ballAt, Flat(best.transform.position), 8f);
            FieldRoutine(RunTo(best, spot, play.EventAt + 0.8f, top: TopSpeed * 0.75f));
        }

        /// <summary>A boundary: the nearest one or two give chase along its line and don't get there (the engine said so).</summary>
        private void ChaseBoundary(BallResult r, Vector3 rope)
        {
            var bodies = _fielders.Select(f => f.a).Where(a => a.gameObject.activeSelf)
                .OrderBy(a => Vector3.Distance(Flat(a.transform.position), rope)).Take(r.Six ? 1 : 2).ToList();
            foreach (var a in bodies)
            {
                if (Vector3.Distance(Flat(a.transform.position), rope) > 45f) continue;
                var spot = Vector3.MoveTowards(rope, Vector3.zero, 1.5f);
                FieldRoutine(RunTo(a, spot, Mathf.Max(0.6f, (float)r.EndTime), chase: true));
            }
            _chaser = bodies.FirstOrDefault();
        }
    }
}
