using System.Collections;
using System.Collections.Generic;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Deep area 3, developer 1 (gather and throw), the director's side:
    /// a planned play's throw is timed to the engine's (its release when the engine has the ball leave his hand:
    /// fielded + pick-up + the deep's turn, Field.Fielded), and its clip's snap turns are paced (FieldPlay.PlanPace);
    /// a ball no clip fits is picked up where he stands (the pick-up clip's pose kept under his hips, no lurch) and
    /// thrown like a planned one (FieldPlay.SetAndThrow) — not 0.5 s later from 1.2 m above the turf, with the
    /// receiver sent to the stumps first, as for a planned one.
    /// Test runs log every pick-up and throw ("[APL pickup]", "[APL throw]", the reviewer's format,
    /// build\review\deep\fielding_testplan.md §3), with or without <c>-aplnothrowfix</c>.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The no-clip chase in progress (stopped as he picks it up), when his hands met it (s after the hit) and where his throw goes.</summary>
        private Coroutine? _looseRun;
        private float _looseTakenAt = float.NaN;
        private Vector3? _looseTarget;

        /// <summary>The man the receiver's spot was set for (ThrowIn moves the throw onto that spot only for him), and when he got there (s after the hit).</summary>
        private Animator? _receiverSpotFor;
        private float _receiverInAt = float.NaN;

        /// <summary>When the engine has the ball leave the fielder's hand (s after the hit): its Back less the throw, less any fumble.</summary>
        private static float EngineRelease(BallResult r)
        {
            if (r.FieldPlay is Field.Play p && p.Back > 0)
                return (float)(p.Back - p.ThrowDistance / RunPlan.ThrowSpeed - (p.Fumbled ? RunPlan.RunTime : 0));
            return (float)r.EndTime + (float)Outfield.Pickup;
        }

        /// <summary>A planned play: its throw timed to the engine's, its clip paced, and (test runs) watched.</summary>
        private void PrepareThrow(FieldPlay play, BallResult r)
        {
            if (ThrowCraft.On)
            {
                play.ReleaseBy = EngineRelease(r);
                play.PlanPace();
            }
            if (Autoplay) FieldRoutine(ThrowWatch(play, r, play.Partner != null ? "relay" : play.Clip.HasThrow ? "own" : "chained"));
        }

        /// <summary>When the planned play's throw should leave his hand (s after the hit), for the receiver's run.</summary>
        private static float PlannedRelease(FieldPlay play)
        {
            if (!ThrowCraft.On || play.Partner != null || play.Clip.Kind == "drop" || play.Clip.IsCatch)
                return float.IsNaN(play.ReleaseAt) ? play.EventAt + 1.5f : play.ReleaseAt;
            if (play.Clip.HasThrow) return play.ReleaseAt + play.PaceDelay;
            return float.IsNaN(play.ReleaseBy) ? play.EventAt + 1.5f : Mathf.Max(play.ReleaseBy, play.EventAt + ThrowCraft.GatherClean + 0.6f);
        }

        /// <summary>No clip fits (he runs to it): the receiver to his stumps now, as for a planned throw.</summary>
        private void ReadyForLoose(BallResult r, Vector3 endAt, Animator chaser)
        {
            _looseTakenAt = float.NaN;
            _looseTarget = null;
            if (!ThrowCraft.On) return;
            var (target, keeperEnd, _) = ThrowPlan(r, endAt, chaser);
            _looseTarget = target;
            var dummy = new FieldPlay { Fielder = chaser, Clip = new SeqClip(), Target = target, BallAt = endAt, Thrower = chaser };
            var release = Mathf.Max((float)r.EndTime + 0.6f, EngineRelease(r));
            ReadyForThrow(dummy, keeperEnd, release + Vector3.Distance(Flat(endAt), Flat(target)) / 26f);
        }

        /// <summary>
        /// He's run to it: the two-hand pick-up (its collect in 0.3 s), the clip's pose kept under his hips as it fades
        /// in (it carries its own run-in), the ball eased off the turf into his hands as they get to it.
        /// </summary>
        private void LoosePickup(Animator chaser)
        {
            if (_looseRun != null) StopCoroutine(_looseRun);
            _looseRun = null;
            EndFeet(chaser);
            Extra(LoosePickupRoutine(chaser));
        }

        private const float LooseLead = 0.3f, LooseFade = 0.15f;

        private IEnumerator LoosePickupRoutine(Animator a)
        {
            var pick = SeqClip.Named("field_13_two_hand_pickup");
            var hand = a.GetBoneTransform(HumanBodyBones.RightHand);
            if (pick == null || !FieldPlay.Has(a, pick))
            {
                // (No pick-up clip on this body: as before.)
                _looseTakenAt = Time.time - _hitAt + 0.12f;
                Event(a, CricketClips.FieldClipNamed("field_13_two_hand_pickup"));
                if (hand != null) yield return EaseHold(hand, 0.6f);
                yield break;
            }
            Body.Rebase(a);
            var startAt = Mathf.Max(0f, pick.EventTime - LooseLead);
            var lead = pick.EventTime - startAt;
            _looseTakenAt = Time.time - _hitAt + lead;
            var hips0 = a.transform.rotation * Flat(FieldPlay.HipsAt(a, pick, startAt));
            var base0 = a.transform.position;
            a.speed = 1f;
            AnimTrace.Note(a, "LoosePickup"); // FLD-1 log only
            a.CrossFadeInFixedTime(pick.Name, LooseFade, 0, startAt);
            var lying = _ball.position;
            // Off the turf into his hands over the last moments before the collect — after the animation every frame
            // (BallView.Follow), so it meets the hands where they are drawn, not a frame behind them.
            var t0 = Time.time;
            ThrowCraft.Follow(_ball, () => Vector3.Lerp(lying, HandsMid(a), Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - (lead - (Time.time - t0)) / 0.15f))));
            for (var t = 0f; t < lead + 0.02f; t += Time.deltaTime)
            {
                // Hips held where they are through the fade (the clip's pose is ahead of its root by its run-in).
                a.transform.position = base0 - hips0 * Mathf.Clamp01(t / LooseFade);
                yield return null;
            }
            a.transform.position = base0 - hips0;
        }

        /// <summary>The keeper / bowler takes a throw at stumps nobody sent him to (the end changed after the plan): he goes now, behind them from the throw.</summary>
        private void SendReceiver(Animator receiver, bool keeperEnd, Vector3 from, float seconds)
        {
            var stumps = new Vector3(0, 0, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch);
            var line = Flat(stumps - from);
            line = line.sqrMagnitude > 0.01f ? line.normalized : Vector3.forward;
            var spot = stumps + line * 0.65f + Vector3.Cross(Vector3.up, line) * 0.25f;
            // (Area 3, build 2: where his receive-and-break clip has him at the take — its guards refused every one when he
            // stood 0.4-0.85 m off it.)
            if (!keeperEnd && ReceivePlace(receiver, false, from) is ReceiveFit fit) spot = fit.Spot;
            _receiverSpot = spot;
            _receiverSpotFor = receiver;
            _receiverInAt = float.NaN;
            if (Vector3.Distance(Flat(receiver.transform.position), spot) < 1.5f) return;
            if (receiver == Refs.Keeper) _keeper?.UpToStumps();
            else _bowler.Settle();
            FieldRoutine(GetThere(receiver, spot, Mathf.Max(0.5f, seconds - 0.3f), from));
        }

        /// <summary>
        /// A close run-out taken by the bowler / an infielder over the stumps: when his receive-and-break clip (owner ITEM 4)
        /// is playing, the moment (game time) its hands bring the ball down through the bails; -1 otherwise.
        /// AtTheStumps (dev 2) breaks the wicket on it instead of WhipBails' sweep.
        /// </summary>
        private float _receiveBreakAt = -1f;

        /// <summary>The receive clips: take at catch_frame, the bails broken 9 frames (0.30 s, RunPlan.Gather) later (fielding.json).</summary>
        private const float ReceiveBreakAfter = 9f / 30f;

        /// <summary>
        /// The take and the break in one clip, for a close run-out at his end (owner ITEM 4: bowler_receive_break_stumps,
        /// fielder_receive_break_stumps; the stumps at the clip's origin, the throw from beyond them). Turned so the throw
        /// comes from where it really does, placed on the real stumps, started so its catch frame is the ball's arrival
        /// (<paramref name="left"/> s from now); his hips' difference from the clip's is eased out over 0.3 s (a short
        /// step, never a pop). False (nothing done) without the clip, or if he's more than 1.5 m from where it would have him.
        /// </summary>
        private bool ReceiveAndBreak(Animator rcv, bool keeperEnd, Vector3 from, float left)
        {
            if (!(ReceivePlace(rcv, keeperEnd, from) is ReceiveFit f)) return false;
            var clip = new SeqClip { Name = f.Name, Length = f.Meta.Length };
            var startT = Mathf.Max(0f, f.Meta.EventTime - left);
            var atStart = Flat(FieldPlay.HipsAt(rcv, clip, startT));
            var y = rcv.transform.position.y;
            var origin = f.Origin + Vector3.up * y;
            var hipsNow = Flat(Hips(rcv).position) + Vector3.up * y;
            var hipsThen = origin + f.Turn * atStart;
            var offset = Flat(hipsNow - hipsThen);
            // His chest against the clip's at its start; he's eased from how he stands onto the clip's place and facing,
            // about his hips at no more than 600 deg/s and done before the take; past that, or his hips over 0.4 m off,
            // the sweep (WhipBails) breaks it as before.
            var clipChest = ThrowCraft.Yaw(f.Turn * Vector3.forward) + FieldPlay.FacingAt(rcv, clip, startT);
            var jump = Mathf.DeltaAngle(ThrowCraft.ChestYaw(rcv), clipChest);
            var ease = Mathf.Max(0.3f, Mathf.Abs(jump) / 600f);
            var refuse = ease > left - 0.05f || offset.magnitude > 0.4f;
            if (Autoplay)
            {
                // (Build 3 review: both directions on every decision, to see the facing maths.)
                Debug.Log($"[APL receive] {f.Name} for {rcv.name}: his chest {ThrowCraft.ChestYaw(rcv):0}, the clip's {clipChest:0} (jump {jump:+0;-0} deg); " +
                    $"to the thrower {ThrowCraft.Yaw(f.ToThrow):0}, clip chest at the take {f.ChestAtCatch:0} (his frame), turn {ThrowCraft.Yaw(f.Turn * Vector3.forward):0}; " +
                    $"clip stumps (hands at the break, his frame) ({f.StumpsLocal.x:+0.00;-0.00}, {f.StumpsLocal.z:+0.00;-0.00}), hips at the take ({f.AtCatch.x:+0.00;-0.00}, {f.AtCatch.z:+0.00;-0.00}); " +
                    $"his hips ({hipsNow.x:0.00}, {hipsNow.z:0.00}) vs the clip's ({hipsThen.x:0.00}, {hipsThen.z:0.00}) = {offset.magnitude:0.00} m, spot ({f.Spot.x:0.00}, {f.Spot.z:0.00}); {left:0.00} s to the take" +
                    (refuse ? " - refused, the sweep breaks it" : $", eased over {ease:0.00} s"));
            }
            if (refuse) return false;
            var fromRot = Quaternion.Euler(0, -jump, 0) * f.Turn;
            rcv.transform.SetPositionAndRotation(hipsNow - fromRot * atStart, fromRot);
            rcv.speed = 1f;
            AnimTrace.Note(rcv, "ReceiveAndBreak"); // FLD-1 log only
            rcv.CrossFadeInFixedTime(f.Name, 0.1f, 0, startT);
            Extra(EaseOut(rcv, atStart, hipsNow, hipsThen, fromRot, f.Turn, ease));
            var catchAt = Time.time + left;
            _receiveBreakAt = catchAt + ReceiveBreakAfter;
            // The engine breaks it at its own moment (as AtTheStumps reads it: BrokenAt, else Back + the gather). Later than
            // the clip's: he holds the ball just over the bails until then — paused on the clip frame where its hands are
            // just above them (build 3: a fixed two frames short left them 51 cm off at the break).
            var r = _hitResult;
            if (r?.Running != null)
            {
                var engineAt = _hitAt + (float)(r.RunOut && r.BrokenAt > 0 ? r.BrokenAt : r.Running.Back + (r.DirectHit ? 0 : RunPlan.Gather));
                if (engineAt > _receiveBreakAt + 0.02f)
                {
                    var toBreak = ReceiveBreakAfter - (f.PauseAt - f.Meta.EventTime);
                    Extra(HoldOverBails(rcv, catchAt + (f.PauseAt - f.Meta.EventTime), engineAt - toBreak));
                    _receiveBreakAt = engineAt;
                }
            }
            if (Autoplay)
            {
                var bails = new Vector3(0, 0.71f, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch);
                var who = rcv;
                var name = f.Name;
                Extra(After(Mathf.Max(0f, _receiveBreakAt - Time.time), () => Debug.Log($"[APL receive] {name} by {who.name}: hands→bails at the break {Vector3.Distance(HandPoint.Cup(who), bails) * 100f:0} cm")));
                Debug.Log($"[APL receive] receive-and-break {name} by {rcv.name}: from frame {startT * 30f + 1f:0}, catch in {left:0.00} s, bails at +{_receiveBreakAt - Time.time:0.00} s");
            }
            return true;
        }

        /// <summary>His receive clip paused (ball just over the bails) from <paramref name="from"/> to <paramref name="until"/>, then on through the break.</summary>
        private static IEnumerator HoldOverBails(Animator a, float from, float until)
        {
            while (a != null && Time.time < from) yield return null;
            if (a == null) yield break;
            a.speed = 0f;
            while (a != null && Time.time < until) yield return null;
            if (a != null) a.speed = 1f;
        }

        /// <summary>
        /// His clip's hips (<paramref name="atStart"/>, his frame) moved in a straight line from <paramref name="hipsFrom"/>
        /// to <paramref name="hipsTo"/> and his facing from <paramref name="from"/> to <paramref name="to"/> about them,
        /// over <paramref name="seconds"/>: the transform follows (it's the clip's origin, on the stumps by the end).
        /// </summary>
        private static IEnumerator EaseOut(Animator a, Vector3 atStart, Vector3 hipsFrom, Vector3 hipsTo, Quaternion from, Quaternion to, float seconds)
        {
            for (var t = 0f; t < seconds && a != null; t += Time.deltaTime)
            {
                var k = Mathf.SmoothStep(0f, 1f, t / seconds);
                var r = Quaternion.Slerp(from, to, k);
                a.transform.SetPositionAndRotation(Vector3.Lerp(hipsFrom, hipsTo, k) - r * atStart, r);
                yield return null;
            }
            if (a != null) a.transform.SetPositionAndRotation(hipsTo - to * atStart, to);
        }

        /// <summary>A receive-and-break clip fitted to these stumps and this throw (ReceivePlace).</summary>
        private sealed class ReceiveFit
        {
            public string Name = "";
            public FieldClip Meta = null!;
            public Quaternion Turn;
            public Vector3 Origin, Spot, ToThrow, StumpsLocal, AtCatch;
            public float ChestAtCatch, PauseAt;
        }

        /// <summary>
        /// The receive-and-break clip for him at these stumps, fitted from the clip itself rather than its file's origin:
        /// turned (a yaw) so his chest at the take faces the throw, and placed so his hands at the break (the clip's own
        /// stumps) are on the real bails. Spot: where his hips are at the take (send him there). PauseAt: the clip time
        /// his hands are just above the bails (held there when the engine's break is later). Null: no clip / fix off.
        /// (Build 3: from the file origin and the hips-to-stumps line, refusals still read +-119..155 deg and his hands
        /// were 51 cm from the bails at the one break it played.)
        /// </summary>
        private static readonly Dictionary<string, (Vector3 stumps, Vector3 atCatch, float chest, float pause)> ReceiveGeo =
            new Dictionary<string, (Vector3, Vector3, float, float)>();

        private ReceiveFit? ReceivePlace(Animator rcv, bool keeperEnd, Vector3 from)
        {
            if (!ThrowCraft.On) return null;
            var name = rcv == Refs.Bowler ? "bowler_receive_break_stumps" : "fielder_receive_break_stumps";
            var meta = CricketClips.FieldClipNamed(name);
            if (meta == null || !rcv.HasState(0, Animator.StringToHash(name))) return null;
            var clip = new SeqClip { Name = name, Length = meta.Length };
            var stumps = new Vector3(0, 0, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch);
            var toThrow = Flat(from - stumps);
            if (toThrow.sqrMagnitude < 1f) return null;
            // (Sampled once a match per clip — every body has the same rig — so sending him there doesn't re-pose him.)
            if (!ReceiveGeo.TryGetValue(name, out var geo))
            {
                var breakT = meta.EventTime + ReceiveBreakAfter;
                // The last frame before the break with his hands still above the bails (by 8 cm or more).
                var pauseT = breakT - 2f / 30f;
                for (var k = 1; k <= 9; k++)
                {
                    var t = breakT - k / 30f;
                    if (t <= meta.EventTime) break;
                    if (FieldPlay.CupAt(rcv, clip, t).y >= 0.79f) { pauseT = t; break; }
                }
                geo = (Flat(FieldPlay.CupAt(rcv, clip, breakT)), Flat(FieldPlay.HipsAt(rcv, clip, meta.EventTime)), FieldPlay.FacingAt(rcv, clip, meta.EventTime), pauseT);
                ReceiveGeo[name] = geo;
            }
            var (stumpsLocal, atCatch, chest, pause) = geo;
            var turn = Quaternion.Euler(0, ThrowCraft.Yaw(toThrow) - chest, 0);
            var origin = stumps - turn * stumpsLocal;
            return new ReceiveFit
            {
                Name = name, Meta = meta, Turn = turn, Origin = origin, Spot = origin + turn * atCatch, ToThrow = toThrow,
                StumpsLocal = stumpsLocal, AtCatch = atCatch, ChestAtCatch = chest, PauseAt = pause,
            };
        }

        /// <summary>A long throw in from the deep: flat and down to bounce 80% of the way, then up into his hands (same time end to end).</summary>
        private static Vector3 OneBounce(Vector3 from, Vector3 to, float t, float seconds, float g)
        {
            const float landShare = 0.8f;
            var u = t / seconds;
            var land = Vector3.Lerp(Flat(from), Flat(to), landShare) + Vector3.up * BallRadius;
            if (u < landShare)
            {
                var span = seconds * landShare;
                var p = Vector3.Lerp(from, land, u / landShare);
                p.y += 0.5f * g * t * (span - t) * 0.6f;
                return p;
            }
            var t2 = t - seconds * landShare;
            var span2 = seconds * (1f - landShare);
            var q = Vector3.Lerp(land, to, (u - landShare) / (1f - landShare));
            q.y += 0.5f * g * t2 * (span2 - t2) * 0.6f;
            return q;
        }

        /// <summary>Between his palms (together), or the right palm (apart): where a two-hand pick-up has it (HandPoint's cup, never the wrists).</summary>
        private static Vector3 HandsMid(Animator a) => HandPoint.Cup(a);

        /// <summary>The loose ball he's picked up: gathered, set and thrown to the right end like a planned one.</summary>
        private IEnumerator LooseThrow(Animator? thrower)
        {
            if (thrower == null) yield break;
            var r = _hitResult;
            var play = new FieldPlay
            {
                Fielder = thrower,
                Clip = new SeqClip { Name = "loose" },
                Target = _looseTarget ?? KeeperTarget,
                BallAt = _ball.position,
                Thrower = thrower,
                EventAt = float.IsNaN(_looseTakenAt) ? Time.time - _hitAt : _looseTakenAt,
            };
            if (r != null) play.ReleaseBy = EngineRelease(r);
            var aimed = play;
            play.Aim = () => ThrowAim(aimed);
            if (Autoplay && r != null) FieldRoutine(ThrowWatch(play, r, "loose"));
            while (!float.IsNaN(_looseTakenAt) && Time.time - _hitAt < _looseTakenAt) yield return null;
            play.HasBall = true;
            var takenAt = Time.time - _hitAt;
            var gather = ThrowCraft.GatherClean + (ThrowCraft.Tempo(thrower.name) - 0.5f) * 0.08f;
            Vector3 Held() => Vector3.Lerp(HandsMid(thrower), ThrowCraft.Palm(thrower), Mathf.SmoothStep(0f, 1f, (Time.time - _hitAt - takenAt) / ThrowCraft.Transfer));
            yield return play.SetAndThrow(thrower, _ball, _hitAt, takenAt, gather, Held, ThrowIn);
        }

        // ------------------------------------------------------------ test-run trace (log only)

        /// <summary>One throw's trace from the hands on it to the release (the watch), finished in ThrowIn.</summary>
        private sealed class ThrowTrace
        {
            public FieldPlay Play = null!;
            public Animator? Who;
            public string Path = "";
            public float HandsOn = float.NaN, Yaw0, YawLast, MaxRate, Whip, Turned, MaxGap, TYawLast;
            public bool HaveT;
            public readonly List<(float t, Vector3 p)> Palm = new List<(float, Vector3)>();
            public bool Set;
        }

        private ThrowTrace? _trace;
        private static int _throws, _throwsFacing, _throwsGather, _throwsReceiver;

        /// <summary>Test runs: from his hands on it to the release — his turn (chest), the ball against his hand, a shot at the take and the set.</summary>
        private IEnumerator ThrowWatch(FieldPlay play, BallResult r, string path)
        {
            var tr = new ThrowTrace { Play = play, Path = path };
            _trace = tr;
            for (var until = Time.time + 12f; Time.time < until && !play.HasBall;) yield return null;
            if (!play.HasBall || _trace != tr) yield break;
            var tight = ThrowPlanTight(r);
            var engineRel = EngineRelease(r);
            var onAt = float.IsNaN(play.TakenAt) ? play.EventAt : play.TakenAt;
            Debug.Log($"[APL pickup] ball {Cricket.FormatOvers(_match.LegalBalls)} rt={Time.realtimeSinceStartup:0.0} {play.Clip.Name} by {play.Fielder.name}: hands on at {onAt:0.00}s (engine fielded {r.EndTime:0.00}s), path {path}, tight {(tight ? "Y" : "N")}, engine dive {(r.FieldPlay?.Dived == true ? "Y" : "N")}, plan {(play.PlanNote.Length > 0 ? play.PlanNote : "-")}, clip {play.Clip.Name} ({(play.Clip.Style == FieldStyle.Dive ? "dive" : play.Clip.Kind)}), engine release {engineRel:0.00}s (pick-up {engineRel - r.EndTime:0.00}s), planned release {PlannedRelease(play):0.00}s, pace +{play.PaceDelay:0.00}s, fix {(ThrowCraft.On ? "on" : "off")}");
            Apl.Client.Dev.FieldEvidence.Moment(Apl.Client.Dev.FieldEvidence.Gather, _ball.position, play.Aim?.Invoke() ?? play.Target);
            while (!play.Thrown && _trace == tr)
            {
                yield return EndOfFrame;
                var who = play.Thrower;
                if (who == null) continue;
                if (who != tr.Who)
                {
                    // (A relay: the man who throws it home, from his hands on it.)
                    tr.Who = who;
                    tr.HandsOn = who == play.Fielder ? onAt : play.Partner?.EventAt ?? Time.time - _hitAt;
                    tr.Yaw0 = tr.YawLast = ThrowCraft.ChestYaw(who);
                    tr.MaxRate = tr.Turned = tr.MaxGap = 0f;
                    tr.Palm.Clear();
                }
                var yaw = ThrowCraft.ChestYaw(who);
                var step = Mathf.DeltaAngle(tr.YawLast, yaw);
                // (Build 4: one 4,241°/s frame on a bowler-as-fielder throw.) A chest jump over 1,500°/s gets its own line:
                // did his transform turn (code) or only the pose (a cut, or the shoulder/hip line misread)?
                var tYaw = ThrowCraft.Yaw(who.transform.forward);
                if (Time.deltaTime > 0f && Mathf.Abs(step) / Time.deltaTime > 1500f && tr.HaveT)
                {
                    var st = who.GetCurrentAnimatorStateInfo(0);
                    Debug.Log($"[APL throwspike] {who.name}{(who == Refs.Bowler ? " (bowler)" : "")}: chest {step:+0;-0}° in {Time.deltaTime * 1000f:0} ms, transform {Mathf.DeltaAngle(tr.TYawLast, tYaw):+0;-0}°, state {st.shortNameHash} t {st.normalizedTime:0.00}{(who.IsInTransition(0) ? " (in transition)" : "")}, ball {Cricket.FormatOvers(_match.LegalBalls)}");
                }
                tr.TYawLast = tYaw;
                tr.HaveT = true;
                // The turn to the target is judged up to 0.15 s before the release; inside that it's the throw's own
                // shoulder whip (the clips' own 1-frame chest turn at release is 1,000–1,150°/s), reported apart.
                if (Time.deltaTime > 0f)
                {
                    var rate = Mathf.Abs(step) / Time.deltaTime;
                    if (!float.IsNaN(play.ReleaseEta) && Time.time - _hitAt > play.ReleaseEta - 0.15f) tr.Whip = Mathf.Max(tr.Whip, rate);
                    else tr.MaxRate = Mathf.Max(tr.MaxRate, rate);
                }
                tr.Turned += step;
                tr.YawLast = yaw;
                var palm = ThrowCraft.Palm(who);
                tr.Palm.Add((Time.time, palm));
                tr.Palm.RemoveAll(x => Time.time - x.t > 0.12f);
                // The ball against his hands once it's in them (after the take's own 0.12 s ease in).
                if (Time.time - _hitAt > tr.HandsOn + 0.12f)
                    tr.MaxGap = Mathf.Max(tr.MaxGap, Mathf.Min(Vector3.Distance(_ball.position, palm), Vector3.Distance(_ball.position, HandsMid(who))));
                // The set: 0.1 s before the release he's heading for (the clip's release frame; FieldPlay.ReleaseEta).
                if (!tr.Set && !float.IsNaN(play.ReleaseEta) && Time.time - _hitAt >= play.ReleaseEta - 0.1f)
                {
                    tr.Set = true;
                    Apl.Client.Dev.FieldEvidence.Moment(Apl.Client.Dev.FieldEvidence.Set, _ball.position, play.Aim?.Invoke() ?? play.Target);
                }
            }
        }

        /// <summary>The engine had it a tight run (a shy's on): as ThrowPlan reads it.</summary>
        private static bool ThrowPlanTight(BallResult r)
        {
            var plan = r.Running;
            if (plan == null) return false;
            var spare = plan.Spare(System.Math.Max(r.Attempted, 1));
            return r.RunOut || r.DirectHit || (r.Attempted > 0 && spare < RunPlan.ShyWindow && spare >= -0.25);
        }

        /// <summary>What ThrowIn knew at the release, for the line at the arrival.</summary>
        private struct ThrowStart
        {
            public float At, Facing, Chest, LineErr, Gather, Set, Turned, TurnSpan, MaxRate, Whip, Gap, Dist, Seconds, Release, ReceiverGap;
            public string Clip, Who, Type, Path, Rise;
            public bool Traced;
        }

        /// <summary>At the release (ThrowIn): his facing against the line of the throw, the gather, the turn; a shot.</summary>
        private ThrowStart BeginThrowTrace(FieldPlay play, Animator thrower, Vector3 from, Vector3 to, float seconds, bool bounce, Animator? receiver)
        {
            var now = Time.time - _hitAt;
            var aimYaw = ThrowCraft.Yaw(Flat(to - from));
            var tr = _trace != null && _trace.Play == play && _trace.Who == thrower ? _trace : null;
            var handsOn = tr?.HandsOn ?? (float.IsNaN(play.TakenAt) ? play.EventAt : play.TakenAt);
            var line = float.NaN;
            if (tr != null && tr.Palm.Count >= 2)
            {
                var d = Flat(tr.Palm[tr.Palm.Count - 1].p - tr.Palm[0].p);
                if (d.magnitude > 0.05f) line = Mathf.Abs(Mathf.DeltaAngle(ThrowCraft.Yaw(d), aimYaw));
            }
            var clip = play.ThrowClip.Length > 0 ? play.ThrowClip : play.Clip.HasThrow ? play.Clip.Name : "(no throw clip)";
            if (play.Clip.Name != clip && play.Clip.Name.Length > 0) clip = $"{play.Clip.Name}>{clip}";
            var n = clip.ToLowerInvariant();
            // (Unity review r1 I1: the owner's crow hop and turn-and-throw named as such, so "type by distance" can be read.)
            var type = bounce ? "onebounce" : n.Contains("underarm") || n.Contains("flick") ? "underarm"
                : n.Contains("crow_hop") ? "crowhop" : n.Contains("turn_and_throw") ? "turn"
                : n.Contains("sidearm") || n.Contains("snap") || n.Contains("on_the_run") || n.Contains("knees") ? "sidearm" : "overarm";
            Apl.Client.Dev.FieldEvidence.Moment(Apl.Client.Dev.FieldEvidence.Release, from, to);
            return new ThrowStart
            {
                Traced = true,
                At = now,
                Facing = Mathf.Abs(Mathf.DeltaAngle(ThrowCraft.HipsYaw(thrower), aimYaw)),
                Chest = Mathf.Abs(Mathf.DeltaAngle(ThrowCraft.ChestYaw(thrower), aimYaw)),
                LineErr = line,
                Gather = now - handsOn,
                Set = float.IsNaN(play.GatherDone) ? float.NaN : now - play.GatherDone,
                Turned = tr?.Turned ?? float.NaN,
                TurnSpan = now - handsOn,
                MaxRate = tr?.MaxRate ?? float.NaN,
                Whip = tr?.Whip ?? float.NaN,
                Gap = tr?.MaxGap ?? float.NaN,
                Dist = Vector3.Distance(Flat(from), Flat(to)),
                Seconds = seconds,
                Release = now,
                Clip = clip,
                Who = thrower.name,
                Type = type,
                Path = tr?.Path ?? "untraced",
                Rise = (float.IsNaN(play.RiseTurn) ? "-" : $"{play.RiseTurn:+0;-0}° cut {(play.RiseCut ? "Y" : "N")}") + (float.IsNaN(play.TakeTurn) ? "" : $", turn at take {play.TakeTurn:0}°"),
                ReceiverGap = receiver != null && _receiverSpot is Vector3 s && receiver == _receiverSpotFor ? Vector3.Distance(Flat(receiver.transform.position), s) : float.NaN,
            };
        }

        /// <summary>At the arrival (ThrowIn): where the receiver is, when it's home against the engine; the line, and the running totals.</summary>
        private void EndThrowTrace(ThrowStart s, bool keeperEnd, Animator? receiver)
        {
            if (!s.Traced) return;
            var r = _hitResult;
            var now = Time.time - _hitAt;
            bool? engineEnd = r?.Running == null ? (bool?)null : r.RunOut ? r.RunOutAtKeeperEnd : r.Running.ToKeeperEnd;
            var stumps = new Vector3(0, 0, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch);
            var spot = receiver != null && receiver == _receiverSpotFor && _receiverSpot is Vector3 sp ? sp : stumps;
            var recv = receiver != null ? Vector3.Distance(Flat(receiver.transform.position), spot) : float.NaN;
            var back = r?.Running?.Back ?? r?.FieldPlay?.Back ?? 0;
            var engineRel = r != null && !r.Caught ? EngineRelease(r) : float.NaN; // (a catch then a throw-in: no engine release)
            string F(float v, string f = "0.00") => float.IsNaN(v) ? "-" : v.ToString(f);
            var inPlace = receiver != null && receiver == _receiverSpotFor && !float.IsNaN(_receiverInAt) ? $"{_receiverInAt:0.00}s" : recv < 1f ? "there" : "not yet";
            _throws++;
            if (s.Facing <= 15f) _throwsFacing++;
            if (s.Gather >= 0.25f) _throwsGather++;
            if (recv < 1f) _throwsReceiver++;
            // (The line is printed at the arrival — it carries the receiver and the engine's Back — but the release was
            // filmed AT the release (BeginThrowTrace): the close-up camera's log fallback must stand down for this line.)
            Apl.Client.Dev.FieldCloseUp.Explicit(Apl.Client.Dev.FieldEvidence.Release);
            Debug.Log($"[APL throw] ball {Cricket.FormatOvers(_match.LegalBalls)} rt={Time.realtimeSinceStartup:0.0} {s.Clip} by {s.Who}: facing err {s.Facing:0}° (hips fwd vs aim), hand travel err {F(s.LineErr, "0")}° (palm travel vs aim), " +
                $"gather {s.Gather:0.00} s (hands on → release), set {F(s.Set)} s, yaw turned {F(s.Turned, "0")}° over {s.TurnSpan:0.00} s (max {F(s.MaxRate, "0")}/s, release whip {F(s.Whip, "0")}/s), ball-hand max {F(s.Gap * 100f, "0")} cm (gather→release), " +
                $"type {s.Type}, dist {s.Dist:0} m, flight {s.Seconds:0.00} s (= {s.Dist / Mathf.Max(0.01f, s.Seconds):0.0} m/s), end {(keeperEnd ? "keeper" : "bowler")} (engine {(engineEnd == null ? "-" : engineEnd.Value ? "keeper" : "bowler")}), " +
                $"receiver {F(recv)} m from his spot at arrival, visual at-stumps {now:0.00} s vs engine Back {back:0.00} s (Δ {(back > 0 ? (now - back).ToString("+0.00;-0.00") : "-")}), " +
                $"chest err {s.Chest:0}°, release {s.Release:0.00} s vs engine {F(engineRel)} s, receiver {receiver?.name ?? "-"} in place {inPlace} (spot gap at release {F(s.ReceiverGap)} m), path {s.Path}, rise turn {s.Rise}, fix {(ThrowCraft.On ? "on" : "off")}");
            Debug.Log($"[APL throwtotal] {_throws}: facing ≤15° {_throwsFacing}/{_throws}, gather ≥0.25 s {_throwsGather}/{_throws}, receiver at his spot {_throwsReceiver}/{_throws}");
        }
    }
}
