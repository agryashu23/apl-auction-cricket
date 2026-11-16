using System.Linq;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Deep area 3, developer 2 (catches): the ball keeps its own line and pace to the take and the hands
    /// come to it (<see cref="CatchHands"/>: palm cup, hand IK over the last 0.25 s, held in the cup after).
    /// The flight used to be re-timed and lerped onto the WRIST over its last 0.8 s and again over 0.2 s
    /// (0.33–0.59 m dragged sideways at the end, the ball drawn at the belly on a cup catch). Now the only
    /// thing that moves the ball off its line is what the arms can't reach (<see cref="CatchHands.Residual"/>),
    /// and test runs log, per catch / keeper take / return catch:
    /// <c>[APL catch] ball 1.3 rt=… kind clip by who: hand-ball gap at catch X cm (…), band …, ik weight …,
    /// blend carried … cm, held max gap … cm, body-hit Y/N, side …</c> — the gap measured before any blend.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The last stretch (s) over which a residual the arms can't reach is taken up by the flight.</summary>
        private const float ResidualBend = 0.5f;

        /// <summary>Test runs: a catch's hand–ball gap over this (m) fails the run (the brief: ≤ 5 cm).</summary>
        private const float CatchGapLimit = 0.05f;

        /// <summary>Test runs: a field catch's flight may drift onto the hands by at most this (m) over its last 0.5 s.</summary>
        private const float CatchDriftLimit = 0.15f;

        /// <summary>This ball's catch reach (null: none), when it was started (the hit), and where the ball's own flight has it at the take.</summary>
        private CatchHands? _catchReach;
        private float _catchReachHit = -1f;
        private Vector3 _catchTruth;
        private string _catchClip = "", _catchKind = "catch";

        /// <summary>Filled by CatchMeets for the catch's log: the lowest it came, and how far the last blend carried it.</summary>
        private float _catchLowest = float.MaxValue, _catchBlend = -1f;

        /// <summary>
        /// A catch's flight point this frame (Hit's clean-catch branch): the reach is started on the first call,
        /// and the flight is shifted (over the last <see cref="ResidualBend"/> s) only by what his arms can't make up.
        /// </summary>
        private float _catchPrevT = -1f, _catchPrevFlight, _catchPaceMin = float.MaxValue, _catchPaceMax = float.MinValue;
        private bool _catchLateLogged;

        private Vector3 CatchFlight(Vector3 p, float aimFlight, float aimReal, float t, FieldPlay? play, Animator? catcher, ref float planMiss, float flightT)
        {
            var truth = OnTurf(PathAt(aimFlight));
            truth.y = Mathf.Max(truth.y, MinCatchY);
            _catchTruth = truth;
            if (catcher == null) return p;
            if (_catchReachHit != _hitAt || _catchReach == null)
            {
                var clip = play?.Clip;
                StartCatchReach(catcher, _hitAt + aimReal, clip?.Name ?? "", "catch", clip != null && clip.LeftHand, clip != null && clip.RightHand);
            }
            var bend = Mathf.Clamp01(1f - (aimReal - t) / ResidualBend);
            // The flight's pace (its own clock against game time) over the last 0.5 s, for the catch rule (§5c: 0.8–1.25×).
            if (play != null && !play.Started && aimReal - t < 0.3f && !_catchLateLogged && Autoplay)
            {
                // (A late clip start leaves his hands nowhere near: say so, it's not the catch planner's sum.)
                _catchLateLogged = true;
                Debug.Log($"[APL match] catch clip {play.Clip.Name} not started 0.3 s before the take: his run there is late");
            }
            if (bend > 0f && t <= aimReal && _catchPrevT >= 0f && t > _catchPrevT)
            {
                var pace = (flightT - _catchPrevFlight) / (t - _catchPrevT);
                _catchPaceMin = Mathf.Min(_catchPaceMin, pace);
                _catchPaceMax = Mathf.Max(_catchPaceMax, pace);
            }
            _catchPrevT = t;
            _catchPrevFlight = flightT;
            if (bend <= 0f || play != null && !play.Started) return p;
            // (For the log: the plan's hands vs the ball's real line, before the arms reach.)
            planMiss = Vector3.Distance(play != null ? play.HandNow() : _catchReach!.Cup, truth);
            _catchResidual = _catchReach!.Residual(truth);
            return p + _catchResidual * Mathf.SmoothStep(0f, 1f, bend);
        }

        // ------------------------------------------------------- the stumps take (final rating 2026-11-14, Fielding #1)

        /// <summary>A stumps take's contact waits until the drawn ball is this close to the cup (m).</summary>
        private const float TakeWithin = 0.3f;

        /// <summary>The fastest the flight is bent sideways onto hands that aren't where the throw was aimed (m/s).</summary>
        private const float HomeSpeed = 4f;

        /// <summary>A throw's homing onto the hands that take it: when it started (s into the flight) and over how long.</summary>
        private sealed class TakeHoming
        {
            public float From = -1f, Span;
        }

        /// <summary>
        /// The throw in, flying into the hands that take it (ThrowIn's loop, in place of OntoHand's 0.2 s blend): the
        /// receiver still running to the stumps had his hands 1.2–2.2 m from where the throw was aimed (log "stumps take …
        /// 2.15 m"), and the last 0.2 s pulled the ball across that gap — a snap. The miss is now taken up over as long as
        /// it needs at no more than <see cref="HomeSpeed"/> (never under 0.2 s, never more than the whole flight), so the
        /// ball curves into the hands wherever they are and ends in them on the arrival.
        /// </summary>
        private static Vector3 IntoHands(Vector3 flying, Vector3 aimedAt, Transform? hand, float t, float seconds, TakeHoming h)
        {
            if (hand == null) return flying;
            var off = hand.position - aimedAt;
            var left = seconds - t;
            if (h.From < 0f)
            {
                var span = Mathf.Clamp(off.magnitude / HomeSpeed + 0.1f, HandBlend, seconds);
                if (left > span) return flying;
                h.From = t;
                h.Span = Mathf.Max(0.05f, left);
            }
            var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - h.From) / h.Span));
            return flying + off * k;
        }

        /// <summary>
        /// The take clip at the stumps, held until the ball is nearly there (it used to be fired on the planned arrival
        /// less 0.12 s, whatever the ball was doing): when the ball, at its own speed, is 0.12 s from the hands — or
        /// within <see cref="TakeWithin"/> — the clip's catch frame lands on it. Gives up at <paramref name="latest"/>.
        /// </summary>
        private System.Collections.IEnumerator TakeWhenClose(Animator who, FieldClip? clip, Transform? hand, float latest)
        {
            var prev = _ball.position;
            yield return null;
            while (who != null && hand != null && Time.time < latest)
            {
                var now = _ball.position;
                var speed = Time.deltaTime > 0f ? Vector3.Distance(now, prev) / Time.deltaTime : 0f;
                prev = now;
                var gap = Vector3.Distance(now, hand.position);
                if (gap <= TakeWithin || speed > 0.5f && gap / speed <= 0.12f) break;
                yield return null;
            }
            if (who != null) Event(who, clip);
        }

        /// <summary>A return catch's flight point (Hit): shifted over the last stretch by what the bowler's arms can't reach.</summary>
        private Vector3 ReturnFlight(Vector3 p, float t, float takeAt)
        {
            if (_catchReach == null || _catchReachHit != _hitAt) return p;
            var bend = Mathf.Clamp01(1f - (takeAt - t) / ResidualBend);
            // The pace rule (§5c) for a return catch too: its flight runs on the game clock up to the take.
            var flightT = Mathf.Min(t, takeAt);
            if (bend > 0f && t <= takeAt && _catchPrevT >= 0f && t > _catchPrevT)
            {
                var pace = (flightT - _catchPrevFlight) / (t - _catchPrevT);
                _catchPaceMin = Mathf.Min(_catchPaceMin, pace);
                _catchPaceMax = Mathf.Max(_catchPaceMax, pace);
            }
            _catchPrevT = t;
            _catchPrevFlight = flightT;
            if (bend <= 0f) return p;
            _catchResidual = _catchReach.Residual(_catchTruth);
            return p + _catchResidual * Mathf.SmoothStep(0f, 1f, bend);
        }

        /// <summary>The shift CatchFlight has given the flight's end (what the arms couldn't reach).</summary>
        private Vector3 _catchResidual;

        /// <summary>
        /// The last 0.2 s of a reached catch (Hit's into-the-hands blend): what's left between where the drawn
        /// flight ends and the cup now — blended in on top of the flight, not the flight pulled onto the hand.
        /// Null when this ball has no reach (the old blend stands).
        /// </summary>
        private Vector3? CatchBlend(Vector3 hand)
        {
            if (_catchReach == null || _catchReachHit != _hitAt) return null;
            return hand - (_catchTruth + _catchResidual);
        }

        /// <summary>
        /// The take (CatchMeets): the ball is his, in the cup after the animation and IK — until the celebration
        /// takes it (a planned clip's LetGo), his throw starts (the transfer to the throwing hand is dev 1's), or the
        /// plain catch's 0.6 s (BallInTheAir flings it then).
        /// </summary>
        private void HoldInCup(float lowest, float blendGap)
        {
            if (_catchReach == null || _catchReachHit != _hitAt) return;
            _catchLowest = lowest;
            _catchBlend = blendGap;
            var play = _play;
            var hitAt = _hitAt;
            // A catch that turns into a throw (seq_36/37): no cup hold — the transfer to the throwing hand starts at the
            // catch frame (dev 1's), and the "held" count was measuring that transfer (review b2: 20–30 cm).
            if (play != null && play.Clip.IsCatch && play.Clip.HasThrow) return;
            if (play != null && play.Clip.IsCatch)
            {
                // (Out of the cup when his clip moves it on: the throw's transfer, or the boundary catch's toss.)
                var until = play.Clip.HasThrow ? play.EventAt + 0.1f : !float.IsNaN(play.Clip.TossAt) ? play.HitTime(play.Clip.TossAt) - 0.02f : float.MaxValue;
                _catchReach.Hold(_ball, 8f, () => !play.LetGo && !play.Thrown && Time.time - hitAt < until);
            }
            else _catchReach.Hold(_ball, _catchKind == "return" ? 1.0f : 0.6f);
        }

        /// <summary>Start a reach for this ball's catch (and its log at the moment of contact).</summary>
        private void StartCatchReach(Animator who, float at, string clip, string kind, bool leftOnly, bool rightOnly)
        {
            _catchReach = CatchHands.For(who);
            _catchReachHit = _hitAt;
            _catchClip = clip;
            _catchKind = kind;
            _catchLowest = float.MaxValue;
            _catchBlend = -1f;
            _catchResidual = Vector3.zero;
            _catchPrevT = -1f;
            _catchLateLogged = false;
            _catchPaceMin = float.MaxValue;
            _catchPaceMax = float.MinValue;
            var name = who.name;
            _catchReach.Reach(() => _catchTruth, at, _ball, leftOnly, rightOnly, c => CatchContact(kind, _catchClip, who, name, c), CatchDrawn);
        }

        /// <summary>
        /// A point in his BODY's frame (+x his right, +z the way his chest faces, from his hips on the ground). Not the
        /// transform's: a sequence clip's transform is its origin, metres from his body (review r1: "side ±5.8 m").
        /// </summary>
        private static Vector3 BodyLocal(Animator who, Vector3 p)
        {
            var hips = Hips(who).position;
            var yaw = ThrowCraft.ChestYaw(who);
            return Quaternion.Inverse(Quaternion.Euler(0, yaw, 0)) * new Vector3(p.x - hips.x, p.y, p.z - hips.z);
        }

        /// <summary>
        /// The slip cordon's catches: its own clips, the standing takes and the dives to the side — never the run-ins,
        /// run-backs or the sprint dive (F6 ball 2: first slip "sprint-dived" at an edge 1.35 s after the hit — the
        /// clip's run-up started before he could move, and his hands were 0.6 m off the ball at the take).
        /// </summary>
        /// (By name: the loader styles catch_12–15 "Stand" though 12/13 run and 15 is a long skier wait.)
        private static bool CordonCatch(SeqClip c) =>
            c.Name.StartsWith("slip") || CordonNames.Any(n => c.Name.StartsWith(n));

        private static readonly string[] CordonNames =
            { "catch_01", "catch_02", "catch_03", "catch_04", "catch_05", "catch_06", "catch_10", "catch_14", "catch_16" };

        /// <summary>
        /// A catch clip for anyone outside the cordon: not the slip clips — except the chest-high two-hand take for a
        /// flat hit straight at him (review b2: no chest catches at all; the only chest-height catch clip was slips-only,
        /// and the planner meets the ball at the clip's hand height, so the band comes from the clips on offer).
        /// </summary>
        private static bool FieldCatch(SeqClip c, Apl.Core.Play.BallResult r) =>
            !c.Name.StartsWith("slip") || c.Name == "slip_catch_chest" && r.Path.Apex < 6.0;

        /// <summary>No catch clip planned (he ran under it): the clip for the ball's height and side where his hands will meet it.</summary>
        private FieldClip? CatchClipAt(Animator who, Vector3 arrival, float ran)
        {
            var local = BodyLocal(who, arrival);
            // (PM run, ball 3.3: the side was read from where he WAS, 0.45 s out, still running under it — 2.18 m "to
            // his right" — so he ran under it AND dived 2 m right: hands 1.71 m off, the flight dragged onto them.
            // What's left after his run, which takes him toward the ball, is the side his hands meet it at.)
            var flat = new Vector2(local.x, local.z);
            if (ran > 0f && flat.magnitude > 1e-3f) local.x *= Mathf.Max(0f, flat.magnitude - ran) / flat.magnitude;
            var y = arrival.y;
            var name = ran > 6f ? "catch_08_front_running_catch"
                : Mathf.Abs(local.x) > 1.1f && y < 1.4f ? (local.x < 0 ? "catch_05_diving_left" : "catch_06_diving_right")
                : Mathf.Abs(local.x) > 0.45f ? (local.x < 0 ? "catch_03_one_hand_left" : "catch_04_one_hand_right")
                : y > 2.1f ? "catch_10_jumping_catch"
                : y > 1.35f ? "catch_02_reverse_cup_catch"
                : y < 0.35f && ran > 2.5f ? "catch_07_diving_forward"
                : "catch_01_cup_catch";
            return CricketClips.FieldClipNamed(name) ?? CricketClips.PickCatch(y, ran);
        }

        /// <summary>
        /// The keeper takes (a delivery, a throw): the gloves reach for where it arrives, the ball held in the cup
        /// (KeeperRig.Gloves), and the take logged. Wired once a ball (KeeperRig calls it from every take).
        /// </summary>
        private void WireKeeperTakes()
        {
            if (_keeper == null || _keeper.OnTake != null) return;
            KeeperRig.LogShifts = Autoplay;
            _keeper.OnTake = (clip, ball, inSeconds) =>
            {
                var a = _keeper.GetComponent<Animator>();
                if (a == null) return;
                // (A delivery's stumping after the plain take — the ball already in his cup: no second reach. KeeperRead.)
                if (_keeperDelivery && _keeperRetake) return;
                var hands = CatchHands.For(a);
                var name = a.name;
                // A delivery is a "keeper" take (a caught behind: a "catch", judged as one); a throw in to him is a "receive".
                var kind = _keeperDelivery ? (_keeperCatch ? "catch" : "keeper") : "receive";
                if (kind == "catch")
                {
                    // (Its own catch numbers: nothing left over from an earlier ball's catch.)
                    _catchLowest = float.MaxValue;
                    _catchBlend = -1f;
                }
                // (Review R4: a re-take with the ball already in his gloves — the whip onto the bails, EnsureBroken —
                // isn't a second take: no second reach, log or "taken" frame.)
                if (kind == "receive" && Vector3.Distance(_ball.position, hands.Cup) < 0.25f) return;
                hands.Reach(() => ball, Time.time + inSeconds, _ball, false, false, c => CatchContact(kind, clip, a, name, c), () => _ball.position, waitArrival: true);
            };
        }

        /// <summary>Where the catch's flight drew the ball this frame (Hit's loop), for the drawn-ball check at contact.</summary>
        private Vector3 _catchDrawn;
        private int _catchDrawnFrame = -1;

        /// <summary>Hit's loop: the ball as drawn this frame.</summary>
        private void NoteDrawn(Vector3 shown)
        {
            _catchDrawn = shown;
            _catchDrawnFrame = Time.frameCount;
        }

        /// <summary>
        /// The ball as drawn at contact (review R1): the flight's frame if Hit drew it this frame, else wherever
        /// the take's own hold put it this frame — before CatchHands puts it in the cup.
        /// </summary>
        private Vector3 CatchDrawn() => _catchDrawnFrame == Time.frameCount ? _catchDrawn : _ball.position;

        /// <summary>
        /// A ground stop / pick-up (Hit, as it's planned): his hands reach down to where the ball will be at the clip's
        /// contact frame over the last 0.25 s — hands, not shins (review b2: 87/92 stops over 5 cm, median 18). The
        /// gather and throw after it are dev 1's; the reach fades 0.3 s after contact. Logged by LogStop.
        /// </summary>
        private void ReachForStop(FieldPlay play)
        {
            if (!play.Clip.Collects) return;
            var at = play.BallAt;
            CatchHands.For(play.Fielder).Reach(() => at, _hitAt + play.EventAt, _ball, play.Clip.LeftHand, play.Clip.RightHand);
        }

        /// <summary>The keeper's take now being chosen is the delivery's (KeeperTakes), not a throw's; and it's a caught behind.</summary>
        private bool _keeperDelivery, _keeperCatch;

        /// <summary>
        /// A ground stop / pick-up by a planned clip (Hit, as his hands get it): the ball where its own roll had it
        /// against his palm cup — measured only (the gather itself is dev 1's) — with the review's evidence frame.
        /// </summary>
        private void LogStop(FieldPlay play, Vector3 ballWas)
        {
            if (!play.Clip.Collects) return;
            var who = play.Fielder;
            var cup = HandPoint.Cup(who, play.Clip.LeftHand, play.Clip.RightHand);
            var batterAt = _striker != null ? Hips(_striker).position : Vector3.zero;
            Apl.Client.Dev.FieldEvidence.Moment(Apl.Client.Dev.FieldEvidence.Stop, ballWas, batterAt);
            if (!Autoplay) return;
            var gap = Vector3.Distance(ballWas, cup);
            var band = CatchBand(who, ballWas, play.Clip.Name);
            var side = BodyLocal(who, ballWas).x;
            // "Body" for a stop: the ball nearer his shins than his hands (owner: "the ball meets the hands, not the shins").
            var shin = Mathf.Min(Shin(who, ballWas, true), Shin(who, ballWas, false));
            var body = shin < gap && shin < 0.12f;
            Debug.Log($"[APL catch] ball {Cricket.FormatOvers(_match.LegalBalls)} rt={Time.realtimeSinceStartup:0.0} stop {play.Clip.Name} by {who.name}: hand-ball gap at catch {gap * 100f:0.0} cm (ball centre → palm cup, measured BEFORE any blend), band {band} (ball y {ballWas.y:0.00}), ik weight 0.00 over 0.00 s, blend carried n/a (no blend) cm, held max gap - cm, body-hit {(body ? "Y" : "N")}, side {side:+0.00;-0.00} m");
        }

        /// <summary>Distance (m) from a point to one shin (knee to foot).</summary>
        private static float Shin(Animator a, Vector3 p, bool left)
        {
            var knee = a.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            var foot = a.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            if (knee == null || foot == null) return float.MaxValue;
            var ab = foot.position - knee.position;
            var k = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - knee.position, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, knee.position + ab * k);
        }

        /// <summary>The height band of a take, from where the ball met him (his own frame) and the clip.</summary>
        private static string CatchBand(Animator who, Vector3 ball, string clip)
        {
            var local = BodyLocal(who, ball);
            if (clip.Contains("dive") || clip.Contains("diving")) return "dive";
            if (Mathf.Abs(local.x) > 0.45f) return "side";
            return ball.y > 2.0f ? "high" : ball.y > 1.45f ? "reverse" : ball.y > 0.9f ? "chest" : ball.y > 0.25f ? "low" : "ground";
        }

        /// <summary>The moment of contact (CatchHands, after the animation and IK): the log once the hold has had a moment.</summary>
        private void CatchContact(string kind, string clip, Animator who, string name, CatchHands.Contact c)
        {
            // The review's evidence frame: a throw taken (the keeper's or a receiver's), at the moment of contact.
            if (kind == "receive") Apl.Client.Dev.FieldEvidence.Moment(Apl.Client.Dev.FieldEvidence.Taken, c.Cup, null);
            // A caught-behind's evidence frame: the catch close-ups hang off "catch_hands" (FieldCloseUp).
            if (kind == "catch" && who == Refs.Keeper) Apl.Client.Dev.AutoShot.Moment("catch_hands");
            // Final rating 2026-11-14 (stumps-take snaps of 1.2–2.2 m): the drawn ball against his cup at the take. Must be small.
            if (kind == "receive" && Debug.isDebugBuild)
                Debug.Log($"[APL take] ball-to-hand gap at take = {c.Drawn:0.00} m ({name}, {(clip.Length > 0 ? clip : "(none)")}, the ball's line {c.Gap:0.00} m from the cup{(CatchHands.For(who).LateContact ? ", timed out waiting" : "")})");
            if (!Autoplay) return;
            var over = Cricket.FormatOvers(_match.LegalBalls);
            var rt = Time.realtimeSinceStartup;
            var band = CatchBand(who, c.Truth, clip);
            var side = BodyLocal(who, c.Truth).x;
            var catching = kind == "catch" || kind == "return";
            var hands = CatchHands.For(who);
            var arrived = hands.LateContact ? "N" : "Y";
            // A field catch is logged after its hold's first 0.5 s (for the held gap); anything else at once. Not an
            // Extra: the next ball's clean-up stopped them, and keeper takes went unlogged (review b2: 1 line in 5 runs).
            var fieldCatch = catching && _catchReachHit == _hitAt && _catchReach == hands;
            StartCoroutine(After(fieldCatch ? 0.5f : 0f, () =>
            {
                // A keeper's take has no blend: the ball goes from its flight straight into the cup (the jump is the gap).
                var blend = _catchBlend >= 0f && catching ? _catchBlend : c.Gap;
                var heldText = fieldCatch ? $"{hands.HeldMax * 100f:0.0}" : "-";
                Debug.Log($"[APL catch] ball {over} rt={rt:0.0} {kind} {(clip.Length > 0 ? clip : "(none)")} by {name}: hand-ball gap at catch {c.Gap * 100f:0.0} cm (ball centre → palm cup, measured BEFORE any blend), band {band} (ball y {c.Truth.y:0.00}), ik weight {c.Weight:0.00} over {CatchHands.Window:0.00} s, blend carried {blend * 100f:0.0} cm, held max gap {heldText} cm, body-hit {(c.BodyHit ? "Y" : "N")}, drawn ball → cup {c.Drawn * 100f:0.0} cm, side {side:+0.00;-0.00} m, arrived {arrived}");
                if (kind == "receive")
                {
                    // A throw taken at the stumps: strict — the hands on the ball's line and the drawn ball in them (§5c).
                    var take = $"[APL match] stumps take by {name}: ball {c.Gap:0.00} m from the hands, drawn ball {c.Drawn:0.00} m from the cup ({clip})";
                    if (c.Gap > CatchGapLimit || c.Drawn > CatchGapLimit) Debug.LogError("FAIL " + take);
                    else Debug.Log("PASS " + take);
                }
                if (!catching) return;
                var lowest = _catchLowest;
                // The catch check (aggregator format), fed the honest gap — no longer the ball measured after it was dragged onto the hand.
                var full = lowest >= MinCatchY - 0.005f;
                var line = $"[APL match] catch by {name}: ball {c.Gap:0.00} m from the hand (eased in from {blend:0.00} m), lowest before it {(lowest == float.MaxValue ? c.Truth.y : lowest):0.00} m ({(full ? "on the full" : "LOW")}){(c.BodyHit ? " body" : "")}";
                // Judged on what's SEEN (review b4: 1/10 passed the log, 6/8 looked right): the drawn ball in the cup at
                // the catch (≤ 5 cm), no visible drag in its last 0.2 s (blend ≤ 10 cm), and — for a field catch, whose
                // flight takes up the arms' miss over its last 0.5 s — that drift no more than 15 cm (≤ 0.3 m/s, unseen).
                // A keeper's take has no such drift: its miss is a jump into the gloves, judged as the blend.
                var drift = fieldCatch ? c.Gap : 0f;
                // §5c(4): the flight's pace over its last 0.5 s within 0.8–1.25× (a ball that hangs or rushes shows even on line).
                var paced = !fieldCatch || _catchPaceMin == float.MaxValue || _catchPaceMin >= 0.8f && _catchPaceMax <= 1.25f;
                var paceText = fieldCatch && _catchPaceMin != float.MaxValue ? $"{_catchPaceMin:0.00}–{_catchPaceMax:0.00}x" : "-";
                line += $", flight drift {drift * 100f:0.0} cm over the last {ResidualBend:0.0} s, pace {paceText}";
                // Keeper catches (caught behind) stay strict: no drift to hide a miss — the hands must be on it.
                var strict = !fieldCatch && c.Gap > CatchGapLimit;
                if (c.Drawn > CatchGapLimit || drift > CatchDriftLimit || strict || !paced || !full || c.BodyHit || blend > 0.1f) Debug.LogError("FAIL " + line);
                else Debug.Log("PASS " + line);
            }));
        }
    }
}
