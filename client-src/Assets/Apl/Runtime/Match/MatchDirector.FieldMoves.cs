using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The fielders' legs and arms: running to a spot (react, accelerate, pull
    /// up or run on into a clip), the locomotion loops matched to the ground
    /// speed, walking in with the bowler and back to their marks between balls,
    /// throws to either end (the keeper or the bowler takes them, a team-mate
    /// backs up), and the fielders coming in to the catcher.
    /// </summary>
    public sealed partial class MatchDirector
    {
        // Fielders: a beat to react, then accelerate to a sprint (as the engine's 7 m/s
        // with reach — never much faster); they pull up at Decel.
        private const float Reaction = 0.25f, Accel = 6f, TopSpeed = 7.5f, Decel = 7f;

        /// <summary>How far back from his mark a fielder waits, to walk in with the bowler.</summary>
        /// <remarks>Owner (2026-11-13, "fielders move too much during the ball release"): 1.2 m — a step or two — not 2.6.</remarks>
        /// <remarks>Owner (build 6): fielders don't move at all from the run-up to the release — no walk-in: they wait ON their marks.</remarks>
        private static float WalkIn => SlideFix.Round1 ? 0f : 2.6f;

        /// <summary>Beyond this from the middle (m) he's a boundary rider: he stays set through the run-up (no walk-in).</summary>
        private const float BoundaryRider = 45f;

        /// <summary>Fielder <paramref name="i"/> of this ball's field is out (the engine's Field.OutsideRing — exactly the out men);
        /// the 45 m distance only if the field isn't known.</summary>
        private bool Rider(int i)
        {
            if (!SlideFix.Round1) return false;
            if (_fieldNow != null && i >= 0 && i < _fieldNow.Fielders.Count) return Apl.Core.Play.Field.OutsideRing(_fieldNow.Fielders[i]);
            var home = i >= 0 && i < _fielders.Count ? _fielders[i].home : Vector3.zero;
            return new Vector2(home.x, home.z).magnitude > BoundaryRider;
        }

        // Ground speeds the loops were made at (m/s, fielding.json), to keep feet from sliding.
        private const float WalkClip = 1.27f, JogClip = 3.27f, SprintClip = 6.94f;
        private static readonly int ReadyHash = Animator.StringToHash("Ready");

        /// <summary>Where throws go: over the stumps at the batter's end into the keeper's gloves, or to the bowler at his.</summary>
        private static readonly Vector3 KeeperTarget = new Vector3(0, 0.9f, -GroundSpace.HalfPitch - 0.5f);
        private static readonly Vector3 BowlerTarget = new Vector3(0, 1.0f, GroundSpace.HalfPitch + 0.45f);

        /// <summary>Routines of the field play (runs, plays, throws, walks), all stopped for the next ball.</summary>
        private readonly List<Coroutine> _fieldRoutines = new List<Coroutine>();

        private Coroutine FieldRoutine(IEnumerator r)
        {
            var c = StartCoroutine(r);
            _fieldRoutines.Add(c);
            return c;
        }

        /// <summary>Seconds from the hit to cover <paramref name="dist"/> metres flat out.</summary>
        private static float SprintTime(float dist)
        {
            var tUp = TopSpeed / Accel;
            var dUp = 0.5f * Accel * tUp * tUp;
            return Reaction + (dist <= dUp ? Mathf.Sqrt(2 * dist / Accel) : tUp + (dist - dUp) / TopSpeed);
        }

        /// <summary>Moving or standing: the sprint / ready clips, or the walk flag on older controllers.</summary>
        private static void Loco(Animator a, bool moving) => Loco(a, moving ? TopSpeed : 0);

        /// <summary>Walk, jog or sprint for <paramref name="speed"/> m/s, the loop sped up or slowed to match.</summary>
        /// <param name="left">Metres still to go (deep area 1: a start clip only when the run is at least its travel).</param>
        /// <param name="clips">Between balls (deep area 1): the owner's start clips may lead into the gait. Never in a play —
        /// they take 2.8-3.5 s to reach the loop's speed, much slower than the engine's acceleration.</param>
        private static void Loco(Animator a, float speed, float left = float.MaxValue, bool clips = false)
        {
            // FLD-1 (characters): a man being walked across (Strides.Walk, feet locked) is that walk's — playing
            // "Ready" or a run over it popped his feet ("walk put back" x8 in the r13 plain log).
            if (Strides.Busy(a))
            {
                AnimTrace.Note(a, $"Loco({speed:0.0}) skipped"); // FLD-1 log only
                return;
            }
            AnimTrace.Note(a, $"Loco({speed:0.0})"); // FLD-1 log only
            foreach (var p in a.parameters)
            {
                if (p.nameHash != Walking) continue;
                a.SetBool(Walking, speed > 0);
                return;
            }
            // FLD-1 (characters): the loop whose own measured stride needs the least speed-up or slow-down
            // (0.65-1.45x), so the feet keep up — the old fixed bands put a 4.9 m/s run on the jog at 1.8x.
            var now = a.GetCurrentAnimatorStateInfo(0);
            // Owner v26 #17, the slidewatch run ("(none)>run_03_sprint_loop", 59 plants, mean 4.4 m/s): RunTo and the
            // walk-in call this EVERY frame, and while the 0.15 s fade into the loop was still on, the current state
            // was the old one, so the fade was started again each frame — an interrupted fade freezes the blended
            // pose ("(none)") and the body was carried along under still feet for the whole run. Now a fade already
            // heading for the loop is left to finish, and the loop it's heading for counts as the one he's in.
            var going = SlideFix.On && a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0).shortNameHash : 0;
            var state = going != 0 ? going : now.shortNameHash;
            // Deep area 1: off from a standing start, the start clip leads into the gait when it's installed
            // (Strides.StartStop.cs: fielder_start_walk/_jog/_sprint, travel as root motion that his feet carry); while it
            // plays it's left alone, and near its end the gait's loop takes over as usual.
            if (SlideFix.Round1 && clips && speed > 0.5f)
            {
                if (Strides.IsStart(state))
                {
                    var info = going != 0 ? a.GetNextAnimatorStateInfo(0) : now;
                    if (info.normalizedTime < 0.9f) return;
                }
                // (Iteration 6, reviewer I5-1: offering it here never fired — RunTo's speed starts near 0, so the first frame
                // already faded into the walk loop. RunTo decides the start once, from the planned cruise and the whole run.)
            }
            string? current = null;
            foreach (var g in new[] { "run_01_walk_in_loop", "run_02_jog_loop", "run_03_sprint_loop" })
                if (state == Animator.StringToHash(g)) current = g;
            var (clip, natural, playback) = speed <= 0.01f ? ("Ready", 1f, 1f) : Strides.PickGait(a, speed, current);
            a.speed = playback;
            var hash = Animator.StringToHash(clip);
            if (SlideFix.On)
            {
                // (Iteration 9, reviewer: "ready>run_01_walk_in_loop +feet" 310 plants 0.175 / 1.19 — from the stance the loop
                // fades in at the phase whose feet are where his are, Strides.StartPhase, as the Strides walks do.)
                if (state != hash)
                {
                    var standing = state == ReadyHash || state == SlipHash || state == Animator.StringToHash("run_00_ready_stance_idle_loop");
                    if (SlideFix.Round1 && standing && hash != ReadyHash) a.CrossFadeInFixedTime(clip, 0.15f, 0, Strides.StartPhase(a, clip));
                    else a.CrossFadeInFixedTime(clip, 0.15f);
                }
            }
            else if (a.GetCurrentAnimatorStateInfo(0).shortNameHash != hash || hash == ReadyHash) a.CrossFadeInFixedTime(clip, 0.15f);
        }

        /// <summary>
        /// Run to a spot by <paramref name="by"/> s from now: a beat to react
        /// (turning to go), speed up to the pace that gets him there in time (up
        /// to <paramref name="top"/>), then pull up — or, into a clip that joins
        /// him mid-stride, keep going. A chase (boundary) runs flat out and gives
        /// up when the time's gone. <paramref name="endFace"/>: over the last strides
        /// he turns to face this way (the facing a fielding clip starts in).
        /// </summary>
        /// <param name="betweenBalls">Deep area 1 (iteration 8): the caller's run is between balls (back to position, a
        /// celebration, a field change, the huddle) — the owner's start / stop / turn-and-go clips may carry it. Never a chase
        /// or anything in a play (b2_RC: a chase from Fielding took a 2.9 s sprint start and ended 27 m short).</param>
        private IEnumerator RunTo(Animator a, Vector3 to, float by, bool chase = false, bool pullUp = true, float top = TopSpeed, Quaternion? endFace = null, bool betweenBalls = false)
        {
            to = Flat(to);
            var from = Flat(a.transform.position);
            var total = Vector3.Distance(from, to);
            // Deep area 1 (log only): the caller's scenario, else a chase / field play.
            var scene = SlideScene.Keep(a, "fielder chase / field play");
            if (total < 0.05f)
            {
                if (pullUp) Loco(a, 0f);
                yield break;
            }
            var dir = (to - from) / total;
            var face0 = a.transform.rotation;
            var face1 = Quaternion.LookRotation(dir);
            // Deep area 1: the reaction's turn steps round (TurnStep), in the same 0.25 s — no spin over planted feet.
            // Already on the move (walking in with the bowler, crossing, jogging back: a gait loop playing), the reaction
            // is on the move too — his feet carry him on at the pace they had while FeetStep turns him (about the planted
            // foot) toward the spot. It used to stand him still for 0.25 s with the walk loop stepping under him (reviewer
            // RC3: untagged walk_in rows, 142 plants, mean 0.63 m/s), then start him from 0.
            var moving = 0f;
            // Between balls (not in a play): the owner's start / stop / turn-and-go clips may carry him (iteration 5).
            var between = SlideFix.Round1 && betweenBalls && !chase && !InPlay(a);
            var clipTime = 0f;
            if (SlideFix.Round1)
            {
                var t0 = Time.time;
                var v0 = Strides.Busy(a) ? 0f : Strides.FeetSpeed(a);
                // (The clip turns LEFT — Unity's yaw negative: used only for a spot behind him on that side, or within 15° of
                // dead behind; iteration 6, reviewer I5-3.)
                var signed = Vector3.SignedAngle(Flat(a.transform.forward), dir, Vector3.up);
                var behindOk = signed <= -150f || Mathf.Abs(signed) >= 165f;
                if (between && v0 <= 0.3f && behindOk && Body.Has(a, TurnAndGo) && total >= (ClipSidecars.Travel(ClipSidecars.Of(TurnAndGo)) ?? 4.22f) + 2f)
                {
                    // The spot's behind him and a way off: the owner's turn-and-go (a 180 on the planted foot, pushing off into
                    // a jog), his feet carrying him (FootLock, free: its travel is root motion), then straight into the jog loop
                    // under his turned body (Strides.RebaseInto). The run goes on from there.
                    var e = TurnAndGoRoutine(a, to, scene);
                    while (e.MoveNext()) yield return e.Current;
                    if (a == null) yield break;
                    clipTime = Time.time - t0;
                    moving = Strides.FeetSpeed(a);
                }
                else if (v0 > 0.3f)
                {
                    moving = v0;
                    var st0 = 0f;
                    while (a != null && Time.time - t0 < Reaction)
                    {
                        SlideScene.Hold(a, scene);
                        FeetStep(a, to, v0, endFace, ref st0);
                        yield return null;
                    }
                }
                else if (!between && Mathf.Abs(signed) < 120f && !Strides.Busy(a))
                {
                    // Iteration 12 (fielding Dev 2's catch trace: "run 8.4 m in 2.27 s (wanted by 1.97)" after a +70° turn — the
                    // 0.25 s reaction was spent turning on the spot and the run began from a standstill after it): in a play, a
                    // turn of under 120° is made ON THE RUN — from the first frame his feet carry him, accelerating as the plan
                    // does, while FeetStep turns him about the planted foot (540°/s at a walk). The reaction's time is no longer
                    // dead, so the planner's SprintTime (which counts the reaction, not a turn) holds.
                    var st0 = 0f;
                    var v = 0f;
                    while (a != null && Time.time - t0 < Reaction)
                    {
                        v = Mathf.Min(top, v + Accel * Time.deltaTime);
                        SlideScene.Hold(a, scene);
                        FeetStep(a, to, v, endFace, ref st0);
                        yield return null;
                    }
                    moving = Mathf.Max(0.3f, v);
                }
                else
                {
                    // (Never a clip here: a turn clip runs 0.6-1.1 s and the run's timing is the engine's. The reaction is kept whole.)
                    yield return TurnStep.Turn(a, face1, Reaction, clips: false);
                    while (a != null && Time.time - t0 < Reaction) yield return null;
                }
            }
            else for (var t = 0f; t < Reaction; t += Time.deltaTime)
            {
                // The body can be gone mid-run (the sides are recast between innings).
                if (a == null) yield break;
                a.transform.rotation = Quaternion.Slerp(face0, face1, t / Reaction);
                yield return null;
            }
            if (a == null) yield break;
            if (moving <= 0f) a.transform.rotation = face1;
            var span = by - Reaction - clipTime;
            float cruise;
            if (chase || span <= 0.05f) cruise = top;
            else
            {
                // Speeding up at Accel to a cruise v covers v·T − v²/2a in T.
                var disc = span * span - 2 * total / Accel;
                cruise = disc >= 0 ? Accel * (span - Mathf.Sqrt(disc)) : top;
                cruise = Mathf.Clamp(cruise * 1.04f, 1.0f, top);
            }
            var speed = moving;
            var elapsed = Reaction;
            var gone = 0f;
            // Iteration 6 (reviewer I5-1): the owner's start clip is decided ONCE, here, before the first Loco — between balls,
            // from a standstill, the planned cruise's gait, and only for a run at least the clip's travel + 0.5 m. It plays
            // through (his feet carrying him, steered gently at the spot about the planted foot), then the run goes on from
            // the loop's speed.
            if (between && moving <= 0f && cruise > 0.5f && !Strides.Busy(a)
                && Strides.StartClipFor(a, Strides.PickGait(a, cruise).clip) is (string startClip, float startTravel) && total >= startTravel + 0.5f)
            {
                if (Debug.isDebugBuild && _startLines++ < 60) Debug.Log($"[APL strides] {a.name} [{scene}] starts with {startClip}: run {total:0.0} m, clip travels {startTravel:0.0} m");
                var t2 = Time.time;
                var e = StartInto(a, to, startClip, scene);
                while (e.MoveNext()) yield return e.Current;
                if (a == null) yield break;
                clipTime += Time.time - t2;
                speed = Strides.FeetSpeed(a);
            }
            if (SlideFix.On)
            {
                // Owner v26 #17 round 3: the feet carry him (FeetStep) — he covers what the pose on screen covers,
                // along the way he faces, turning about his planted foot; the planned speed only drives the loop.
                var stalled = 0f;
                float wanted = 0f, got = 0f;
                var giveUp = Mathf.Max(by, Reaction + total / Mathf.Max(1f, cruise)) + 2.5f + clipTime;
                var walkedOver = 0f;
                elapsed += clipTime; // (the [APL feet] line's run time includes the clips)
                var stopped = false;
                while (!chase || elapsed < by)
                {
                    if (a == null) yield break;
                    // A replay on screen drives every body (reviewer RC8: runs kept steering and counting under it).
                    if (SlideFix.Round1 && Strides.IsPaused)
                    {
                        yield return null;
                        continue;
                    }
                    var left = Vector3.Distance(Flat(a.transform.position), to);
                    if (left < 0.03f) break;
                    // Iteration 7 (reviewer A3: a backer's run 2.5-2.8 s late, "feet gave 1.9 m/s", 11.7 m "put there", then the
                    // crossing "put back"): a Strides walk (a crossing begun when the ball went dead) has taken him over — the
                    // later mover wins; this run ends where he is, no teleport.
                    if (SlideFix.Round1 && Strides.Busy(a))
                    {
                        walkedOver += Time.deltaTime;
                        if (walkedOver > 0.2f)
                        {
                            if (Debug.isDebugBuild) Debug.Log($"[APL feet] {a.name} [{scene}] run {total:0.0} m handed over to a walk {left:0.0} m short after {elapsed:0.00} s");
                            yield break;
                        }
                    }
                    else walkedOver = 0f;
                    if (elapsed > giveUp)
                    {
                        Debug.LogWarning($"[APL strides] {a.name}: run still {left:0.0} m short after {elapsed:0.0} s: put there");
                        EndFeet(a);
                        a.transform.position = new Vector3(to.x, a.transform.position.y, to.z);
                        break;
                    }
                    // Between balls, the owner's stop clip out of the gait he's in, handed over to when the distance left comes
                    // down to its travel (never under its braking distance): it carries him onto the spot (StopInto).
                    // (Iteration 6, reviewer I5-2: only with the distance left within 0.3 m of its travel — the first frame under
                    // it — and his feet at 0.8-1.25x its entry speed; a short run, or a slowed gait, pulls up on RunTo's own.)
                    if (between && pullUp && endFace == null && !Strides.Busy(a)
                        && Strides.StopClipFor(a, Strides.FeetSpeed(a)) is (string stopClip, float stopTravel, float braking, float entry)
                        && left <= stopTravel + 0.05f && left >= Mathf.Max(braking, stopTravel - 0.3f))
                    {
                        if (Debug.isDebugBuild && _startLines++ < 60) Debug.Log($"[APL strides] {a.name} [{scene}] stops with {stopClip}: {left:0.00} m left (travel {stopTravel:0.00}), feet {Strides.FeetSpeed(a):0.00} m/s (entry {entry:0.00})");
                        var t1 = Time.time;
                        var e = StopInto(a, to, stopClip, scene);
                        while (e.MoveNext()) yield return e.Current;
                        elapsed += Time.time - t1;
                        stopped = true;
                        break;
                    }
                    var limit = pullUp ? Mathf.Max(0.25f, Mathf.Sqrt(2 * Decel * left)) : float.MaxValue;
                    speed = Mathf.Min(cruise, speed + Accel * Time.deltaTime, limit);
                    var before = Flat(a.transform.position);
                    SlideScene.Hold(a, scene);
                    FeetStep(a, to, speed, endFace, ref stalled, between);
                    wanted += speed * Time.deltaTime;
                    elapsed += Time.deltaTime;
                    yield return null;
                    if (a != null) got += Vector3.Distance(before, Flat(a.transform.position));
                }
                // Log only: what the plan asked of his legs against what his feet gave (round 4's measure).
                if (a != null && Debug.isDebugBuild && _feetLines++ < 200)
                    Debug.Log($"[APL feet] {a.name} [{scene}] run {total:0.0} m in {elapsed - Reaction:0.00} s (wanted by {by - Reaction:0.00}): planned {wanted / Mathf.Max(0.01f, elapsed - Reaction - clipTime):0.00} m/s, feet gave {got / Mathf.Max(0.01f, elapsed - Reaction - clipTime):0.00} m/s, {Vector3.Distance(Flat(a.transform.position), to):0.00} m short{(chase ? " (chase)" : "")}{(clipTime > 0f ? $" (+{clipTime:0.00} s in start/turn clips, not in the rates)" : "")}");
                EndFeet(a);
                if (a != null && (pullUp || chase) && !stopped) yield return Coast(a);
                yield break;
            }
            // The shared walk/run steering: his body faces where he's going, whatever loop plays.
            var steer = new Strides.Steer(0f); // the run loops face the rig's way; re-measured as he goes
            while (gone < total - 0.02f && (!chase || elapsed < by))
            {
                if (a == null) yield break;
                // Pulling up: down to a near stop (owner v26 #17 — at the old 0.8 m/s floor he arrived mid-stride and
                // the loop faded to "Ready" with the body stopped dead: the planted foot slid for the fade).
                var limit = pullUp ? Mathf.Max(SlideFix.On ? 0.25f : 0.8f, Mathf.Sqrt(2 * Decel * (total - gone))) : float.MaxValue;
                speed = Mathf.Min(cruise, speed + Accel * Time.deltaTime, limit);
                gone = Mathf.Min(total, gone + speed * Time.deltaTime);
                steer.Step(a, Vector3.MoveTowards(from, to, gone), 12f);
                if (endFace is Quaternion end)
                {
                    // Into the clip's facing over the last 2.5 m (or the whole of a short run).
                    var blend = Mathf.Min(2.5f, total * 0.8f);
                    a.transform.rotation = Quaternion.Slerp(a.transform.rotation, end, Mathf.SmoothStep(0, 1, 1 - (total - gone) / Mathf.Max(0.01f, blend)));
                }
                Loco(a, speed);
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (a != null && (pullUp || chase)) Loco(a, 0f);
        }

        /// <summary>
        /// Owner v26 #17 round 3 (slidewatch: run_03_sprint_loop mean 1.09, (none)&gt;sprint 2.42, ready&gt;walk_in 0.69):
        /// one frame of a kinematic run where the FEET carry him. The loop for <paramref name="speed"/> is played
        /// (Loco), and he travels exactly what the pose on screen covers (Strides.FeetSpeed: the gait's stride ×
        /// playback × its share of any fade — nothing while the stance is still fading out, all of it once the loop
        /// is in), along the way his body faces. He's steered by turning (a body's turn rate, about the planted
        /// foot so it stays put) toward the spot — toward a point behind it when he must arrive facing
        /// <paramref name="endFace"/> — and the last metre or so bends onto the spot. Moving the transform at a
        /// planned speed along a straight line while the body faced elsewhere or the legs were still fading in was
        /// the slide. A body nothing pushes for 0.4 s (an animator that isn't playing a gait) is carried as before.
        /// Returns the distance still to go.
        /// </summary>
        private static float FeetStep(Animator a, Vector3 to, float speed, Quaternion? endFace, ref float stalled, bool clips = false)
        {
            var dt = Time.deltaTime;
            var tr = a.transform;
            var pos = Flat(tr.position);
            var toGo = to - pos;
            var left = toGo.magnitude;
            if (left < 0.02f) return left;
            Loco(a, speed, left, clips);
            // Round 4: the feet carry him (FootLock, free of any one clip): the planted foot's travel IS his root
            // motion — the clips carry none (bake-into-pose: AnimatorBuilder logs ground speed 0.00 m/s), and three
            // rounds of matching a planned speed to a measured stride left ~1 m/s in the jog and sprint. This only steers.
            var feet = a.GetComponent<FootLock>() ?? a.gameObject.AddComponent<FootLock>();
            if (SlideFix.Round1) FootPin.On(a); // (iteration 7, B-3)
            if (feet.Active && !feet.Free) return left; // a Strides walk owns him
            if (!feet.Active) feet.BeginFree(to);
            feet.Target = to;
            feet.Beat = Time.frameCount;
            var v = feet.Velocity.magnitude;
            stalled = v < 0.05f && speed > 0.3f ? stalled + dt : 0f;
            var aim = endFace is Quaternion e ? to - Flat(e * Vector3.forward).normalized * Mathf.Min(left * 0.5f, 2f) : to;
            var want = Flat(aim - pos);
            if (want.sqrMagnitude < 1e-6f) want = toGo;
            // A body turns quickly at a walk, more slowly flat out — about his planted foot, so it stays put.
            var rate = Mathf.Lerp(540f, 220f, Mathf.Clamp01(Mathf.Max(v, speed) / 7f));
            var err = Vector3.SignedAngle(Flat(tr.forward), want, Vector3.up);
            var q = Quaternion.Euler(0, Mathf.Clamp(err, -rate * dt, rate * dt), 0);
            var pivot = feet.Planted ?? Strides.LowFoot(a) ?? tr.position;
            pivot.y = tr.position.y;
            tr.SetPositionAndRotation(pivot + q * (tr.position - pivot), q * tr.rotation);
            feet.Turned(q);
            // Nothing pushing him for 0.4 s (an animator that isn't playing a gait): carried as before, logged.
            if (stalled > 0.4f)
            {
                if (stalled - dt <= 0.4f) Debug.LogWarning($"[APL strides] {a.name}: feet not carrying him toward {left:0.0} m off: carried");
                tr.position += toGo / left * Mathf.Min(left, speed * dt);
            }
            return left;
        }

        private const string TurnAndGo = "fielder_turn_and_go_180";

        private static int _startLines;

        /// <summary>
        /// Deep area 1 (iteration 6): the owner's start clip from the stance — its travel is root motion his planted foot
        /// carries (FootLock, free, aimed at the spot, never past it), and he's turned at most 90°/s about the planted foot toward
        /// it (TurnStep.TurnTowards); near its end (90 %) the run's loop takes over (RunTo → Loco).
        /// </summary>
        private static IEnumerator StartInto(Animator a, Vector3 to, string clip, string? scene)
        {
            a.speed = 1f;
            a.SetBool(Animator.StringToHash("Mirror"), false);
            a.CrossFadeInFixedTime(clip, 0.1f);
            var feet = a.GetComponent<FootLock>() ?? a.gameObject.AddComponent<FootLock>();
            var mine = feet.BeginFree(to);
            var hash = Animator.StringToHash(clip);
            for (var t = 0f; t < 4f; t += Time.deltaTime)
            {
                if (a == null || feet.Owner != mine || !feet.Active) yield break;
                var info = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : a.GetCurrentAnimatorStateInfo(0);
                if (t > 0.2f && (info.shortNameHash != hash || info.normalizedTime >= 0.9f)) break;
                var toGo = Flat(to - a.transform.position);
                if (toGo.magnitude < 0.05f) break;
                if (!Strides.IsPaused) TurnStep.TurnTowards(a, toGo, 90f);
                feet.Beat = Time.frameCount;
                SlideScene.Hold(a, scene);
                yield return null;
            }
            if (a != null && feet.Owner == mine) feet.Beat = Time.frameCount; // (the run's FeetStep carries on with these feet)
        }

        /// <summary>
        /// Deep area 1 (iteration 5): the owner's turn-and-go, from the stance with the spot behind him: the clip turns the pose
        /// (baked) and its travel is root motion his planted foot carries (FootLock, free, aimed at the spot — never past it);
        /// near its end it goes straight into the jog loop under his turned body (Strides.RebaseInto). Up to 3.5 s.
        /// </summary>
        private static IEnumerator TurnAndGoRoutine(Animator a, Vector3 to, string? scene)
        {
            a.speed = 1f;
            a.SetBool(Animator.StringToHash("Mirror"), false);
            a.CrossFadeInFixedTime(TurnAndGo, 0.1f);
            var feet = a.GetComponent<FootLock>() ?? a.gameObject.AddComponent<FootLock>();
            var mine = feet.BeginFree(to);
            var hash = Animator.StringToHash(TurnAndGo);
            for (var t = 0f; t < 3.5f; t += Time.deltaTime)
            {
                if (a == null || feet.Owner != mine || !feet.Active) yield break;
                var info = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : a.GetCurrentAnimatorStateInfo(0);
                if (t > 0.2f && (info.shortNameHash != hash || info.normalizedTime >= 0.95f)) break;
                feet.Beat = Time.frameCount;
                SlideScene.Hold(a, scene);
                yield return null;
            }
            if (a == null) yield break;
            if (a.GetCurrentAnimatorStateInfo(0).shortNameHash == hash) Strides.RebaseInto(a, "run_02_jog_loop");
            feet.Beat = Time.frameCount;
        }

        /// <summary>
        /// Deep area 1 (iteration 5): the owner's stop clip, handed over to on the way in (RunTo) with the distance left equal
        /// to its travel: it decelerates over planted steps into the ready stance, his feet carrying him (FootLock, free, aimed
        /// at the spot — never past it); its last frame IS the stance's first (verify_report: 0.0 cm), so the stance plays from
        /// there without a fade.
        /// </summary>
        private static IEnumerator StopInto(Animator a, Vector3 to, string clip, string? scene)
        {
            a.speed = 1f;
            a.SetBool(Animator.StringToHash("Mirror"), false);
            a.CrossFadeInFixedTime(clip, 0.12f);
            var feet = a.GetComponent<FootLock>() ?? a.gameObject.AddComponent<FootLock>();
            var mine = feet.BeginFree(to);
            var hash = Animator.StringToHash(clip);
            for (var t = 0f; t < 4f; t += Time.deltaTime)
            {
                if (a == null || feet.Owner != mine || !feet.Active) yield break;
                var info = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : a.GetCurrentAnimatorStateInfo(0);
                if (t > 0.2f && (info.shortNameHash != hash || info.normalizedTime >= 0.97f)) break;
                feet.Beat = Time.frameCount;
                SlideScene.Hold(a, scene);
                yield return null;
            }
            if (feet != null && feet.Owner == mine) feet.End();
            if (a != null && !Strides.Busy(a) && a.GetCurrentAnimatorStateInfo(0).shortNameHash == hash) a.Play("Ready", 0, 0f);
        }

        /// <summary>
        /// Owner v26 #17 round 3 (slidewatch: walk_in&gt;ready_stance mean 1.44, jog&gt;ready 0.88): the stop. The loop
        /// fades to "Ready" at the playback it had (Loco(0) used to put it back to 1x, so the fading walk stepped at full
        /// speed under a body that had stopped), and while it fades the body goes on exactly as far as the fading
        /// legs carry it (a few cm), then the stance plays at 1x.
        /// </summary>
        private static IEnumerator Coast(Animator a)
        {
            var scene = SlideScene.Keep(a, null);
            var k = a.speed;
            // (Deep area 1, iteration 5: the stop clip is no longer handed over to here, after arrival — it carried him metres
            // past his spot. RunTo hands over to it on the way in, when the distance left is its travel: StopInto.)
            Loco(a, 0f);
            if (!SlideFix.On || a == null) yield break;
            a.speed = Mathf.Clamp(k, 0.5f, 1.5f);
            if (Strides.Busy(a)) yield break;
            // Round 4: the fading legs carry him (FootLock, free), up to a metre on — never past it.
            var feet = a.GetComponent<FootLock>() ?? a.gameObject.AddComponent<FootLock>();
            var mine = feet.BeginFree(a.transform.position + Flat(a.transform.forward).normalized);
            for (var t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                if (a == null || feet == null || feet.Owner != mine || !feet.Active) yield break;
                if (t > 0.05f && !a.IsInTransition(0)) break;
                feet.Beat = Time.frameCount;
                SlideScene.Hold(a, scene);
                yield return null;
            }
            if (feet != null && feet.Owner == mine) feet.End();
            if (a != null && !Strides.Busy(a) && a.GetCurrentAnimatorStateInfo(0).shortNameHash == ReadyHash) a.speed = 1f;
        }

        private static int _feetLines;

        /// <summary>A kinematic mover's feet (FootLock, free) stop carrying him.</summary>
        private static void EndFeet(Animator? a)
        {
            if (a != null && a.GetComponent<FootLock>() is FootLock f && f.Active && f.Free) f.End();
        }

        /// <summary>Somebody's play this ball owns him (he's fielding it, backing up, taking the throw, coming to the catcher).</summary>
        private bool InPlay(Animator a) =>
            a == _chaser || a == _backer || a == _receiver || a == _converger || (_play != null && _play.Fielders.Contains(a));

        /// <summary>Turn on the spot to face a point.</summary>
        /// <remarks>Deep area 1: it steps round (TurnStep: a turn clip, else the body turned about the pivot foot's ball while
        /// the other foot steps) — the transform slerped round its own origin dragged both planted feet round in an arc
        /// (slidewatch: "run_00_ready_stance_idle_loop +turn", 181 plants, mean 1.73 m/s). Same duration as before.</remarks>
        /// <remarks>Iteration 2 (reviewer B2/B3): two kinds of turn.
        /// <list type="bullet">
        /// <item>In a play (he's the chaser, thrower, receiver, backer, a run-out collector — <see cref="InPlay"/> — or in a
        /// fielding clip, not his stance or a gait loop), or whenever <paramref name="betweenBalls"/> is false and he isn't
        /// standing: rotation only, as Face always was, but about his planted foot and frame by frame
        /// (<see cref="TurnStep.PivotYaw"/>): only this frame's turn is applied, so whatever else places him meanwhile
        /// (FieldPlay, a Rebase at the throw, the receiver's run) keeps its placement. Never a clip. Same duration.</item>
        /// <item>Between balls (<paramref name="betweenBalls"/>: the crossings' and the walk back's last facing), standing:
        /// the stepping turn (TurnStep.Turn), a turn clip if one's installed, and at least 0.2 s + 1 s per 720° (a 180 in
        /// 0.45 s, not two 0.12 s twitches — F3).</item>
        /// </list></remarks>
        private IEnumerator Face(Animator a, Vector3 at, float seconds = 0.35f, bool betweenBalls = false)
        {
            var d = Flat(at - a.transform.position);
            if (d.sqrMagnitude < 0.01f) yield break;
            var q0 = a.transform.rotation;
            var q1 = Quaternion.LookRotation(d);
            if (SlideFix.Round1)
            {
                if (betweenBalls && !InPlay(a) && Standing(a))
                {
                    var deg = Mathf.Abs(Vector3.SignedAngle(Flat(a.transform.forward), d, Vector3.up));
                    yield return TurnStep.Turn(a, q1, Mathf.Max(seconds, 0.2f + deg / 720f), () => a == null || InPlay(a), clips: true);
                }
                else yield return TurnStep.PivotOver(a, d, seconds);
                yield break;
            }
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                a.transform.rotation = Quaternion.Slerp(q0, q1, t / seconds);
                yield return null;
            }
            a.transform.rotation = q1;
        }

        /// <summary>In his stance (Ready, the slip's crouch) or a gait loop, not a fielding clip: a stepping turn may be his.</summary>
        private static bool Standing(Animator a)
        {
            var st = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : a.GetCurrentAnimatorStateInfo(0);
            var h = st.shortNameHash;
            return h == ReadyHash || h == SlipHash || h == Animator.StringToHash("run_00_ready_stance_idle_loop")
                   || h == Animator.StringToHash("run_01_walk_in_loop") || h == Animator.StringToHash("run_02_jog_loop") || h == Animator.StringToHash("run_03_sprint_loop");
        }

        private static readonly int SlipHash = Animator.StringToHash("slip_stance_loop");

        /// <summary>Play a fielding clip so its event (catch, pick-up, release) lands now.</summary>
        private static void Event(Animator a, FieldClip? clip)
        {
            if (clip == null || !a.HasState(0, Animator.StringToHash(clip.Name))) return;
            a.speed = 1f;
            a.CrossFadeInFixedTime(clip.Name, 0.08f, 0, Mathf.Max(0, clip.EventTime - 0.12f));
        }

        // ------------------------------------------------------- between balls

        /// <summary>Names of this ball's field (the engine's order, as <see cref="_fielders"/>), and the field itself (its ropes).</summary>
        private Apl.Core.Play.Field? _fieldNow;
        private string[] _fieldNames = new string[0];

        /// <summary>A catcher beside the keeper (slips, gully, leg slip): he waits on his mark, at the keeper's depth.</summary>
        private bool IsSlip(int i) => i < _fieldNames.Length && Apl.Core.Play.Field.BesideKeeper(_fieldNames[i]);

        /// <summary>Where fielder <paramref name="i"/> waits for the run-up: a few strides back from his mark (the slip crouches on it).</summary>
        private Vector3 WaitSpot(int i)
        {
            var home = _fielders[i].home;
            if (IsSlip(i) || Rider(i)) return home;
            var toBat = Flat(new Vector3(0, 0, -GroundSpace.HalfPitch) - home);
            return home - toBat.normalized * WalkIn;
        }

        /// <summary>
        /// The next ball (called on the cut to the bowler): stop the last ball's
        /// field play and put everyone where he waits for the run-up, facing the batter.
        /// </summary>
        private void ResetFielders()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ResetFieldersNow();
            SlowLog("ResetFielders", sw);
        }

        private void ResetFieldersNow()
        {
            foreach (var c in _fieldRoutines) if (c != null) StopCoroutine(c);
            _fieldRoutines.Clear();
            _play = null;
            _receiver = null;
            _chaser = null;
            _fieldNow = _match.Field;
            _fieldNames = _fieldNow.Fielders.Select(f => f.Name).ToArray();
            // Everyone's spot for THIS ball's field, and who stands at each (the engine's share-out).
            var wasOn = SyncFieldBodies();
            FieldPlanLine();
            // Both wickets standing (the first call, at the start, finds them as built).
            WicketAt(true);
            RestoreWickets();
            // The replays see both wickets broken (the tape has the keeper's end already; it ignores repeats).
            if (_tape != null)
            {
                foreach (var (t, _, _) in WicketAt(false).Stumps.Concat(WicketAt(false).Bails)) _tape.AddProp(t);
            }
            for (var i = 0; i < _fielders.Count; i++)
            {
                var (a, home) = _fielders[i];
                if (!a.gameObject.activeSelf) continue;
                var spot = WaitSpot(i);
                var toBat = new Vector3(0, 0, -GroundSpace.HalfPitch) - home;
                var face = Quaternion.LookRotation(new Vector3(toBat.x, 0, toBat.z));
                // One owner per body: a man already crossing to his spot keeps walking (nothing else moves him).
                if (_crossing.TryGetValue(a, out var going) && Vector3.Distance(going.to, spot) < 0.5f) continue;
                EndFeet(a); // a stopped run's feet don't carry him on
                a.speed = 1f;
                Loco(a, false);
                Crouch(i, a);
                // A new spot: he walks or jogs there, feet locked, never a jump (the owner's rule). The first
                // ball of a spell still places everyone.
                if (!_walkOutOn && wasOn.Contains(a) && _fieldWalkReady && Vector3.Distance(Flat(a.transform.position), spot) > WalkIn + 0.6f)
                {
                    StartCross(a, spot, face);
                    continue;
                }
                StopCross(a);
                a.transform.SetPositionAndRotation(spot, face);
            }
            _fieldWalkReady = true;
            FieldRoutine(WarmUp());
            EnsureBatWatch(); // the orphan-bat fix and watch (MatchDirector.BatWatch)
        }

        /// <summary>Set once the field's been placed for a ball: after that a changed spot is walked to, not jumped to.</summary>
        private bool _fieldWalkReady;

        /// <summary>When the bowler last set off (FieldWalkIn): a fielder still crossing then has to be on his spot.</summary>
        private float _runUpFrom = -1f;

        /// <summary>The men walking to a new spot, each owned by his crossing (ResetFielders leaves them be).</summary>
        private readonly Dictionary<Animator, (Coroutine c, Vector3 to)> _crossing = new Dictionary<Animator, (Coroutine, Vector3)>();
        private int _crossGen;
        private int _shareLogOver = -1;

        private void StartCross(Animator a, Vector3 spot, Quaternion face, string scene = "fielder field change")
        {
            if (_crossing.TryGetValue(a, out var going) && Vector3.Distance(going.to, spot) < 0.5f) return;
            _movedWhy[a] = scene; // the "[APL fieldwait]" line: who walked before this ball, and why
            StopCross(a, settle: false);
            _crossing[a] = (StartCoroutine(CrossToSpot(a, spot, face, _crossGen, scene)), spot);
        }

        /// <summary>
        /// As a bowler does: he doesn't run in till his field has settled — up to <see cref="FieldSettle"/> s
        /// for anyone still walking more than <see cref="SettleGap"/> m from his spot (round 13: at 0.6 s
        /// test-run aims and a quick next ball, men crossing 30-45 m for a mirrored field were placed by a
        /// jump at the run-up). Anyone still short after that finishes off camera as before.
        /// </summary>
        private IEnumerator WaitForField()
        {
            var from = Time.time;
            _waitGaveUp = false;
            // Iteration 9 (reviewer b3 blocker 1: with the 4 s cap men were up to 9.9 / 14.6 m off their marks at the release):
            // the 4 s cap is for the near ones only — anyone more than 6 m out is waited for up to 7 s; past that the walk onto
            // the mark may run (OntoMark).
            var halted = 0;
            var short_ = 0f;
            if (SlideFix.Round1)
            {
                // Owner (2026-11-13: "once the bowler starts his run-up fielders shouldn't move"): the bowler waits until every
                // crossing has FINISHED (walked, coasted and turned to face).
                // Owner (final rating 2026-11-14: "15 is too much, 5 s is more than enough"): the walks start during the
                // replay / celebration / walk-out / over break (PreSetField), and the wait is capped at FieldWaitMax. Anyone
                // still walking at FieldWaitMax − HaltSettle stops where he is — his walk coasts into the stance and he turns
                // to face, all before the run-up (no jump onto the mark, and nobody moves once the bowler sets off).
                var haltAt = from + FieldWaitMax - HaltSettle;
                while (Time.time < haltAt && _crossing.Count > 0) yield return null;
                if (_crossing.Count > 0)
                {
                    foreach (var kv in _crossing.ToList())
                    {
                        if (kv.Key == null) continue;
                        halted++;
                        short_ = Mathf.Max(short_, Vector3.Distance(Flat(kv.Key.transform.position), kv.Value.to));
                        _haltCross.Add(kv.Key);
                    }
                    for (var until = from + FieldWaitMax; Time.time < until && _crossing.Count > 0;) yield return null;
                    // Last resort (a halt that didn't settle in time): his feet let go where he stands — never placed on the mark.
                    foreach (var kv in _crossing.ToList()) if (kv.Key != null) StopCross(kv.Key);
                }
                _haltCross.Clear();
            }
            else for (var until = Time.time + FieldSettle; Time.time < until; )
            {
                var far = 0;
                foreach (var kv in _crossing)
                {
                    if (kv.Key != null && Vector3.Distance(Flat(kv.Key.transform.position), kv.Value.to) > SettleGap) far++;
                }
                if (far == 0) break;
                yield return null;
            }
            _waitGaveUp = false;
            // Deep area 1, F4 (log only): what the settle costs the pace of play.
            var waited = Time.time - from;
            if (waited > 0.05f && Debug.isDebugBuild) Debug.Log($"[APL match] run-up waited {waited:0.0} s for the field to be set");
            // Final rating 2026-11-14: one line per ball for the PM (must stay ≤ 5 s).
            // Field rework (owner 2026-11-16: "only move when RHB/LHB changes"): how many walked since the last ball, and why
            // (back to his mark after fielding = "return to position"; a new field / the other hand = "field change").
            if (Debug.isDebugBuild)
            {
                var why = string.Join(", ", _movedWhy.Values.GroupBy(s => s.Replace("fielder ", "")).Select(g => $"{g.Count()} {g.Key}"));
                Debug.Log($"[APL fieldwait] {waited:0.0}s, {_movedWhy.Count} fielders moved{(_movedWhy.Count > 0 ? $" ({why})" : "")}{(halted > 0 ? $" ({halted} stopped short of the mark, max {short_:0.0} m)" : "")}");
            }
            _movedWhy.Clear();
        }

        /// <summary>Who started a walk since the last ball's run-up, and why (StartCross's scene).</summary>
        private readonly Dictionary<Animator, string> _movedWhy = new Dictionary<Animator, string>();

        /// <summary>The field the last ball was played to: its plan, the batter's hand, and the engine's share-out count.</summary>
        private (Apl.Core.Play.FieldPreset plan, bool lh, int reshares) _planWas = ((Apl.Core.Play.FieldPreset)(-1), false, -1);

        /// <summary>
        /// Per ball, "[APL fieldplan] ball N: plan NAME (kept|changed: reason)" (field rework 2026-11-16): the plan is the
        /// over's (the AI captain's, or the user's pick); the men move only when it changes or the striker's hand does.
        /// </summary>
        private void FieldPlanLine()
        {
            if (!Debug.isDebugBuild || _fieldNow == null) return;
            var now = (_fieldNow.Plan, _fieldNow.LeftHanded, _match.FieldReshares);
            var name = (_match.FieldPresetNow == Apl.Core.Play.FieldPreset.Auto ? "AUTO: " : "") + Apl.Core.Play.Field.PresetName(_fieldNow.Plan);
            var state = now.Equals(_planWas) ? "kept"
                : _planWas.reshares < 0 ? "changed: first ball"
                : now.Plan != _planWas.plan ? $"changed: {(_match.UserBatting ? "captain's new plan" : "the user's field")}{(now.LeftHanded != _planWas.lh ? " + batter hand" : "")}"
                : now.LeftHanded != _planWas.lh ? $"changed: batter hand ({(now.LeftHanded ? "LHB" : "RHB")})"
                : now.FieldReshares != _planWas.reshares ? $"changed: {_match.FieldChangeReason}" : "kept";
            Debug.Log($"[APL fieldplan] ball {_match.LegalBalls + 1}: plan {name}{(now.LeftHanded ? " (LHB mirror)" : "")} ({state})");
            _planWas = now;
        }

        /// <summary>The bowler waits at most this long for the field (owner, final rating 2026-11-14: "5 s is more than enough").</summary>
        private const float FieldWaitMax = 5f;

        /// <summary>A walk still going this long before <see cref="FieldWaitMax"/> is halted: time for its coast and turn to face.</summary>
        private const float HaltSettle = 1.2f;

        /// <summary>Crossings told to stop where they are (WaitForField's cap): each coasts and faces, then the run-up may start.</summary>
        private readonly HashSet<Animator> _haltCross = new HashSet<Animator>();

        /// <summary>
        /// Final rating 2026-11-14 (pace of play): the fielders start back to the next ball's marks now — at the over break,
        /// as the new batter walks out — instead of on the cut to the bowler, so the walk happens while there's something
        /// else on screen. Anyone a play or celebration still has (his feet driven by another walk) is left to it; the next
        /// ball's ResetFielders keeps a walk already headed for the right spot.
        /// </summary>
        private void PreSetField()
        {
            if (_walkOutOn || !_fieldWalkReady || _match.Finished || _fielders.Count == 0) return;
            var busy = _fielders.Select(f => f.a).Where(a => a != null && !Crossing(a) && (Strides.Busy(a) || Strides.FeetDriven(a) || (_play != null && !_play.Done && _play.Fielders.Contains(a))));
            ReturnToPositions(busy.ToList());
        }

        private int _walkInLines;

        /// <summary>The bowler stopped waiting with a man still out: his walk onto the mark may run.</summary>
        private bool _waitGaveUp;

        /// <summary>When the ball is due out of the bowler's hand (FieldWalkIn), for a crossing finished off onto the mark.</summary>
        private float _releaseDue = -1f;

        /// <summary>
        /// Deep area 1, B1 (log only): at the release, and 0.4 s on, how many fielders are off the mark the engine fields
        /// them from — "[APL match] at release: N fielders off their mark by &gt; 0.5 m (max X m, who)". Must read 0.
        /// </summary>
        /// <summary>Where each fielder stood as the run-up started (the run-up movement log).</summary>
        private readonly Dictionary<Animator, Vector3> _runUpAt = new Dictionary<Animator, Vector3>();

        private IEnumerator ReleaseCheck()
        {
            while (_released < 0) yield return null;
            OffMarkLine("at release");
            // Owner (2026-11-13), log only: who moved during the run-up — only the walk-in's step or two (≤ 1.5 m) is expected.
            if (Debug.isDebugBuild)
            {
                var n = 0;
                var max = 0f;
                var who = "";
                foreach (var kv in _runUpAt)
                {
                    if (kv.Key == null) continue;
                    var m = Vector3.Distance(kv.Value, Flat(kv.Key.transform.position));
                    if (m > max) { max = m; who = kv.Key.name; }
                    if (m > 0.2f) n++;
                }
                Debug.Log($"[APL match] run-up: {n} fielders moved > 0.2 m (max {max:0.00} m{(max > 0f ? ", " + who : "")})");
            }
            yield return new WaitForSeconds(0.4f);
            OffMarkLine("0.4 s after release");
        }

        private void OffMarkLine(string when)
        {
            if (!Debug.isDebugBuild) return;
            var n = 0;
            var max = 0f;
            var who = "";
            foreach (var (a, home) in _fielders)
            {
                if (a == null || !a.gameObject.activeSelf || InPlay(a)) continue;
                var off = Vector3.Distance(Flat(a.transform.position), Flat(home));
                if (off <= 0.5f) continue;
                n++;
                if (off > max)
                {
                    max = off;
                    who = $"{a.name}{(Crossing(a) ? " crossing" : "")}";
                }
            }
            Debug.Log($"[APL match] {when}: {n} fielders off their mark by > 0.5 m{(n > 0 ? $" (max {max:0.00} m, {who})" : "")}");
        }

        // (Deep area 1: SettleGap 6 → 2 m with the slide fixes on — a man 2-6 m short was jumped onto his mark; now he walks
        // on, and the bowler waits a moment longer for him.)
        // (Iteration 8, the reviewer's pace note: 79 s of waits and 5 "gave up" at 7 s in b2_RA — the wait is capped at 4 s;
        // a man still out then finishes on his feet onto his mark through the run-up, OntoMark, never faster than a fast jog.)
        private static float FieldSettle => SlideFix.Round1 ? 4f : FieldWaitMax; // (final rating 2026-11-14: never over 5 s)
        private static float SettleGap => SlideFix.Round1 ? 2f : 6f;

        /// <summary>The take-the-field walk-out is on (prematch moves the fielders; no crossings, no re-placing walkers).</summary>
        private bool _walkOutOn;

        private void BeginWalkOut()
        {
            _walkOutOn = true;
            StopAllCrossing();
        }

        /// <summary>For PlaceField: a man already on the field whose new spot is a walk away crosses to it; true if he does.</summary>
        private bool WalkToNewSpot(Animator a, HashSet<Animator> wasOn, Vector3 home)
        {
            if (_walkOutOn || !_fieldWalkReady || !wasOn.Contains(a) || Vector3.Distance(Flat(a.transform.position), home) <= WalkIn + 0.6f) return false;
            var toBat = new Vector3(0, 0, -GroundSpace.HalfPitch) - home;
            StartCross(a, home, Quaternion.LookRotation(new Vector3(toBat.x, 0, toBat.z)));
            return true;
        }

        /// <summary>He's walking to his spot (nobody else may move him).</summary>
        private bool Crossing(Animator a) => _crossing.ContainsKey(a);

        /// <param name="settle">Deep area 1 (iteration 8, reviewer A6: the treadmill signature "Loco(0.0) | Body.Rebase |
        /// Strides.StartLoop" and nothing after — ResetFielders' Loco(0) was skipped while the crossing owned him, then the
        /// crossing was cut and he was placed, his walk loop still stepping): a cut crossing lets go of his feet and his loop.</param>
        private void StopCross(Animator a, bool settle = true)
        {
            if (!_crossing.TryGetValue(a, out var going)) return;
            if (going.c != null) StopCoroutine(going.c);
            _crossing.Remove(a);
            if (!settle || !SlideFix.Round1 || a == null) return;
            if (a.GetComponent<FootLock>() is FootLock f && f.Active) f.End();
            Loco(a, false);
        }

        /// <summary>A crossing that ends without its coast (cut short, a slip at the run-up): the loop isn't left stepping.</summary>
        private static IEnumerator SettleIfStepping(Animator? a)
        {
            if (a == null || Strides.Busy(a) || Strides.FeetSpeed(a) <= 0.05f) yield break;
            yield return Coast(a);
        }

        /// <summary>Every crossing off (an innings over, the sides recast, the match done).</summary>
        private void StopAllCrossing()
        {
            _crossGen++;
            foreach (var kv in _crossing)
            {
                if (kv.Value.c != null) StopCoroutine(kv.Value.c);
                // (Iteration 9, TREADMILL signature (a): a crossing stopped this way left its loop stepping.)
                if (!SlideFix.Round1 || kv.Key == null) continue;
                if (kv.Key.GetComponent<FootLock>() is FootLock f && f.Active) f.End();
                Loco(kv.Key, false);
            }
            _crossing.Clear();
        }

        /// <summary>
        /// Across to his new spot on foot (Strides.Walk, feet locked), from the moment the ball's dead: a
        /// walk from near, a jog, a stride-out from 20 m and a run from 40 m (round 13: started at the next
        /// ball's opening at a jog, 135 men a match were still short at the run-up). If the run-up starts
        /// before he's there, he finishes with one quick move a moment later, while the camera is on the bowler.
        /// </summary>
        private IEnumerator CrossToSpot(Animator a, Vector3 spot, Quaternion face, int gen, string scene)
        {
            var started = Time.time;
            var dist = Vector3.Distance(Flat(a.transform.position), spot);
            // (Deep area 1, reviewer RC7: under 8 m it was 1.5 m/s, and men 2-6 m short at the run-up were jumped onto their
            // marks — 24 a match. A brisk walk now, and the walk is never cut short: see the stop below.)
            // (Iteration 7, reviewer A7: the sprint loop's toe slides 0.31 m/s over 583 plants — crossings stay on the jog,
            // at most 4.6 m/s, 1.4x its own stride.)
            var pace = SlideFix.Round1 ? (dist > 20f ? 4.6f : dist > 8f ? 3.8f : 2.0f) : dist > 40f ? 6.5f : dist > 20f ? 5f : dist > 8f ? 3.8f : 1.5f;
            // FLD-1 (characters): the loop that matches that pace on this body, not a fixed one per band.
            var (clip, natural, _) = Strides.PickGait(a, pace);
            if (SlideFix.Round1 && pace > 3f && Body.Has(a, "run_02_jog_loop"))
            {
                // (Iteration 9, reviewer: "run_03_sprint_loop +walk" 884 plants at 0.211 — crossings stay on the JOG loop, at
                // most 1.35x its stride on this body.)
                clip = "run_02_jog_loop";
                natural = Strides.StrideSpeed(a, clip, 3.27f);
                pace = Mathf.Min(pace, natural * 1.35f);
            }
            Body.Rebase(a);
            SlideScene.Tag(a, scene);
            // Deep area 1 (reviewer RC7/RC8): the run-up no longer ends his walk in a jump (the rest of it, 2-12 m, 0.25 s
            // later); the bowler waits for anyone over SettleGap away (WaitForField). Iteration 2 (reviewer B1: he then stood
            // at his WAIT spot, 2.6 m behind the mark the engine fields him from, as the ball was bowled — FieldWalkIn skips
            // a man who's crossing): once the run-up starts, his walk to the wait spot ends where he is and he goes on to his
            // MARK on his feet (OntoMark: there by the release, brisker if he must). The ball coming to him (a play begun
            // after this walk) takes him over at once: the play's run carries him from where he is.
            bool PlayTook() => _released > started && a != null && InPlay(a);
            bool RunUp() => _runUpFrom > started;
            if (SlideFix.Round1)
            {
                // (Owner 2026-11-13: the run-up waits for the crossing to finish — it no longer cuts it.)
                yield return Strides.Walk(a, spot, pace, clip, natural, () => gen != _crossGen || _match.Finished || PlayTook() || _haltCross.Contains(a)); // (halted: WaitForField's 5 s cap)
                if (a == null || gen != _crossGen || _match.Finished || PlayTook())
                {
                    if (a != null && gen == _crossGen) _crossing.Remove(a);
                    if (a != null && !PlayTook()) yield return SettleIfStepping(a); // (A6)
                    yield break;
                }
                // At his spot: the walk coasts into the stance, then he turns to face (the run-up waits for this too).
                yield return Coast(a);
                if (a == null) yield break;
                yield return Face(a, a.transform.position + face * Vector3.forward, 0.3f, betweenBalls: true);
                if (a == null || gen != _crossGen) yield break;
                if (a != null && gen == _crossGen) _crossing.Remove(a);
                yield break;
            }
            yield return Strides.Walk(a, spot, pace, clip, natural, () => _runUpFrom > started || gen != _crossGen || _match.Finished);
            if (a == null || gen != _crossGen || _match.Finished)
            {
                if (a != null && gen == _crossGen) _crossing.Remove(a);
                yield break;
            }
            var left = Vector3.Distance(Flat(a.transform.position), spot);
            if (left > 0.3f)
            {
                // Run-up under way, camera on the bowler: the last of it now (off camera), onto his mark.
                yield return new WaitForSeconds(0.25f);
                if (a == null) yield break;
                // (Under 2 m it's a settle onto his mark, not worth a line.)
                if (Autoplay && left >= 2f) Debug.Log($"[APL match] {a.name} still {left:0.0} m from his new spot at the run-up: placed while the camera's on the bowler");
                var i = _fielders.FindIndex(f => f.a == a);
                a.transform.position = i >= 0 ? _fielders[i].home : spot;
            }
            if (SlideFix.On && a != null && left <= 0.3f)
            {
                // Round 3: the walk coasts into the stance (no stop dead under stepping legs), then he turns to face.
                yield return Coast(a);
                if (a == null) yield break;
                yield return Face(a, a.transform.position + face * Vector3.forward, 0.3f, betweenBalls: true);
            }
            if (a == null) yield break;
            a.transform.rotation = face;
            Loco(a, false);
            _crossing.Remove(a);
        }

        /// <summary>
        /// Deep area 1, B1: a crossing man caught by the run-up goes on to his mark on his feet (FeetStep, as the walk-in),
        /// at the pace that gets him there by the release (1.3-7 m/s), easing in over the last metre or two; a play that
        /// takes him over ends it. Then the coast into the stance, and he faces the batter (rotation about the planted foot).
        /// </summary>
        private IEnumerator OntoMark(Animator a, Vector3 home, int gen, string scene)
        {
            home = Flat(home);
            var stalled = 0f;
            var from = Time.time;
            while (a != null && gen == _crossGen && !_match.Finished)
            {
                if (_released >= 0 && InPlay(a)) yield break; // the play carries him now
                if (Strides.IsPaused)
                {
                    yield return null;
                    continue;
                }
                var left = Vector3.Distance(Flat(a.transform.position), home);
                if (left < 0.03f) break;
                if (Time.time - from > 6f)
                {
                    Debug.LogWarning($"[APL match] OntoMark BACKSTOP: {a.name} still {left:0.0} m off his mark 6 s into the walk onto it: put there (must never happen)");
                    a.transform.position = new Vector3(home.x, a.transform.position.y, home.z);
                    break;
                }
                var due = Mathf.Max(0.3f, _releaseDue - Time.time);
                var speed = Mathf.Clamp(left / due, 1.3f, SlideFix.Round1 && !_waitGaveUp ? 4.6f : 7f);
                speed = Mathf.Min(speed, Mathf.Max(0.25f, Mathf.Sqrt(2f * Decel * left)));
                SlideScene.Tag(a, scene);
                FeetStep(a, home, speed, null, ref stalled);
                yield return null;
            }
            if (a == null || gen != _crossGen) yield break;
            EndFeet(a);
            yield return Coast(a);
            if (a == null) yield break;
            yield return Face(a, new Vector3(0, 0, -GroundSpace.HalfPitch), 0.3f);
        }

        /// <summary>
        /// This ball's field and who stands where: the engine's share-out of the positions among the nine
        /// (QuickMatch.SlotOf — the nearest man takes a changed position, and the scorecard names him), each
        /// body's home for it, and any beyond the field off. Returns the bodies that were on the field.
        /// </summary>
        private HashSet<Animator> SyncFieldBodies()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = SyncFieldBodiesNow();
            SlowLog("SyncFieldBodies", sw);
            return r;
        }

        /// <summary>Test runs: anything of the field's bookkeeping over 50 ms is logged (round 13: minute-long frame stalls).</summary>
        private void SlowLog(string what, System.Diagnostics.Stopwatch sw)
        {
            if (Autoplay && sw.Elapsed.TotalMilliseconds > 50) Debug.LogWarning($"[APL match] SLOW {what}: {sw.Elapsed.TotalMilliseconds:0} ms");
        }

        private HashSet<Animator> SyncFieldBodiesNow()
        {
            var wasOn = new HashSet<Animator>(_fielders.Where(f => f.a != null && f.a.gameObject.activeSelf).Select(f => f.a));
            if (!_cast.Ready) return wasOn;
            var field = _match.Field;
            var bowler = _match.CurrentBowler;
            var keeper = Keeper();
            var bodies = _match.Bowling.XI.Where(p => p.Id != bowler.Id && p.Id != keeper.Id && _side.ContainsKey(p.Id)).Select(p => _side[p.Id]).ToList();
            var lh = _match.OnStrike.Player.LeftHanded;
            var list = new List<(Animator a, Vector3 home)>();
            var used = new HashSet<Animator>();
            for (var i = 0; i < field.Fielders.Count; i++)
            {
                var slot = _match.SlotOf(i);
                if (slot < 0 || slot >= bodies.Count || used.Contains(bodies[slot])) continue;
                var a = bodies[slot];
                used.Add(a);
                a.gameObject.SetActive(true);
                var (x, f) = field.Fielders[i].Position;
                list.Add((a, GroundSpace.Field(x, f, lh)));
            }
            foreach (var a in bodies.Where(b => !used.Contains(b)))
            {
                StopCross(a);
                a.gameObject.SetActive(false);
                list.Add((a, a.transform.position));
            }
            // Test runs: the share-out for one whole over (who took which position, and how far he had to go).
            if (Autoplay && (_shareLogOver < 0 || _shareLogOver == _match.Over) && _match.Over >= 1)
            {
                _shareLogOver = _match.Over;
                var moves = new List<string>();
                for (var i = 0; i < list.Count && i < field.Fielders.Count; i++)
                {
                    var was = _fielders.FindIndex(x => x.a == list[i].a);
                    if (was == i && _fielders.Count > i && (_fielders[i].home - list[i].home).sqrMagnitude < 0.01f) continue;
                    moves.Add($"{field.Fielders[i].Name}<-{list[i].a.name} ({Vector3.Distance(Flat(list[i].a.transform.position), list[i].home):0} m)");
                }
                if (moves.Count > 0) Debug.Log($"[APL match] field share-out, over {_match.Over} ball {_match.BallInOver + 1}{(lh ? " (LHB)" : "")}: {string.Join(", ", moves)}");
            }
            _fielders.Clear();
            _fielders.AddRange(list);
            _fieldNow = field;
            _fieldNames = field.Fielders.Select(f => f.Name).ToArray();
            Refs.Fielders = _fielders.Select(f => f.a).ToArray();
            return wasOn;
        }

        /// <summary>Sample each fielder's clips, one body a frame, before anyone needs a plan.</summary>
        private IEnumerator WarmUp()
        {
            var bodies = _fielders.Select(f => f.a).ToList();
            if (Refs.Bowler != null) bodies.Add(Refs.Bowler);
            if (Refs.Keeper != null) bodies.Add(Refs.Keeper);
            foreach (var a in bodies)
            {
                yield return null;
                if (a != null && a.gameObject.activeInHierarchy && _released < 0 && !Strides.Busy(a)) FieldPlay.Warm(a);
            }
        }

        /// <summary>
        /// The bowler's on his way (<paramref name="untilRelease"/> s to the
        /// release): the fielders walk in with him and are set, in the ready
        /// stance, as he lets it go.
        /// </summary>
        private void FieldWalkIn(float untilRelease)
        {
            _runUpFrom = Time.time;
            // (Iteration 7, A2: the bowler's own due time — the run-up plays faster than the clip's clock.)
            if (SlideFix.Round1 && _bowler != null && _bowler.ReleaseDueAt > Time.time)
            {
                if (Debug.isDebugBuild && _walkInLines++ < 12) Debug.Log($"[APL match] walk-in: release due in {_bowler.ReleaseDueAt - Time.time:0.00} s (the clip's clock said {untilRelease:0.00})");
                untilRelease = _bowler.ReleaseDueAt - Time.time;
            }
            _releaseDue = Time.time + untilRelease;
            FieldRoutine(ReleaseCheck()); // deep area 1, B1: log only
            // (Play is on: whatever happened to the walk-out, it's over.)
            _walkOutOn = false;
            _runUpAt.Clear();
            foreach (var (fa, _) in _fielders) if (fa != null && fa.gameObject.activeSelf) _runUpAt[fa] = Flat(fa.transform.position);
            for (var i = 0; i < _fielders.Count; i++)
            {
                // (A man still crossing is put on his mark by his crossing a moment from now: not walked in too.)
                // (Owner, build 6: no walk-in at all — he stands still on his mark from the run-up to the release.)
                if (SlideFix.Round1 || IsSlip(i) || Rider(i) || !_fielders[i].a.gameObject.activeSelf || _crossing.ContainsKey(_fielders[i].a)) continue;
                FieldRoutine(WalkInRoutine(_fielders[i].a, _fielders[i].home, untilRelease, i));
            }
        }

        private IEnumerator WalkInRoutine(Animator a, Vector3 home, float untilRelease, int i)
        {
            var walk = WalkIn / WalkClip;
            // Staggered a touch, so they don't move as one.
            var start = Mathf.Max(0, untilRelease - walk - 0.15f) + (i % 3) * 0.08f;
            // Owner (2026-11-13): the walk-in is the LAST moment of the run-up — a step or two in the final ~1.2 s, set at the
            // release (the ease in and out cost ~0.4 s of it: ~1.5 m/s for the 1.2 m).
            if (SlideFix.Round1) start = Mathf.Max(0f, untilRelease - 1.2f) + (i % 3) * 0.05f;
            for (var t = 0f; t < start && _released < 0; t += Time.deltaTime) yield return null;
            var from = Flat(a.transform.position);
            var total = Vector3.Distance(from, home);
            var gone = 0f;
            var steer = new Strides.Steer(0f); // the run loops face the rig's way; re-measured as he goes
            // On his mark by the release, always (the engine fields from there): a brisker walk if the run-up's short,
            // and the last stride or two quickly if the ball's already on its way.
            // Owner v26 #17: he eases into the walk over the first 0.3 s and out of it over the last ~0.4 m (the loop's
            // playback follows, Strides.PickGait), so the feet never skate as the stance fades into the walk or back:
            // the pace allows for the time that costs (≈0.15 s in, ≈0.2 s out).
            var ease = SlideFix.On;
            var pace = Mathf.Max(WalkClip, total / Mathf.Max(0.4f, untilRelease - start - 0.05f - (ease ? 0.35f : 0f)));
            var released = -1f;
            var walked = 0f;
            if (ease)
            {
                // Round 3: the feet carry him (FeetStep), and the stop is a coast (Coast) — no planned glide.
                var stalled = 0f;
                while (a != null)
                {
                    var left = Vector3.Distance(Flat(a.transform.position), home);
                    if (left < 0.03f) break;
                    if (_released >= 0 && released < 0) released = Time.time;
                    // The ball's on its way: the last of it quickly, and then he's set wherever he is (never a jump).
                    if (released >= 0 && (Time.time - released > 0.4f || InPlay(a))) break;
                    var speed = released < 0 ? pace : Mathf.Clamp(left / Mathf.Max(0.05f, 0.3f - (Time.time - released)), pace, 5f);
                    walked += Time.deltaTime;
                    SlideScene.Tag(a, "fielder walk-in with the bowler");
                    if (released < 0) speed *= Mathf.Min(Mathf.SmoothStep(0.15f, 1f, walked / 0.3f), Mathf.Clamp(Mathf.Sqrt(2f * 2.5f * left) / Mathf.Max(0.1f, pace), 0.2f, 1f));
                    FeetStep(a, home, speed, null, ref stalled);
                    yield return null;
                }
                // Set before the ball: the coast into the stance. After it, the play may already own him (a chase): leave him be.
                if (a != null && (_released < 0 || !InPlay(a)))
                {
                    SlideScene.Tag(a, "fielder walk-in with the bowler");
                    EndFeet(a);
                    yield return Coast(a);
                }
                else if (a != null && SlideFix.Round1)
                {
                    // (Iteration 9, TREADMILL signature (b): the play took him but nothing moved him yet — a catcher waiting for
                    // his ball, a chase that starts a few frames on — his walk loop stepped on with nobody carrying him. If no run
                    // has his feet two frames on and he's still in a gait loop, he coasts into the stance.)
                    EndFeet(a);
                    yield return null;
                    yield return null;
                    if (a != null && !Strides.FeetDriven(a) && !Strides.Busy(a) && Strides.FeetSpeed(a) > 0.05f) yield return Coast(a);
                }
                yield break;
            }
            while (gone < total)
            {
                if (_released >= 0 && released < 0) released = Time.time;
                var speed = released < 0 ? pace : Mathf.Clamp((total - gone) / Mathf.Max(0.05f, 0.3f - (Time.time - released)), pace, 5f);
                walked += Time.deltaTime;
                if (ease && released < 0) speed *= Mathf.Min(Mathf.SmoothStep(0.15f, 1f, walked / 0.3f), Mathf.Clamp(Mathf.Sqrt(2f * 2.5f * (total - gone)) / Mathf.Max(0.1f, pace), 0.2f, 1f));
                gone = Mathf.Min(total, gone + speed * Time.deltaTime);
                steer.Step(a, Vector3.MoveTowards(from, home, gone), 6f);
                Loco(a, speed);
                yield return null;
            }
            Loco(a, 0f);
        }

        /// <summary>The ball's dead: everyone who moved jogs (or walks) back to where he waits for the next one.</summary>
        private void ReturnToPositions(IEnumerable<Animator> skip)
        {
            StallProbe.Mark("field: back to positions");
            var busy = new HashSet<Animator>(skip);
            // Straight to the NEXT ball's spots (the field may have changed with the strike): the walk starts
            // the moment the ball's dead, not at the next ball's opening (round 13). Field rework (2026-11-16): the
            // engine no longer re-shares from where everyone stands — each man keeps his own mark, so only the men
            // the play moved (chaser, backer, catchers coming in) walk back; the field re-sets only for the other
            // hand, a new plan or a new bowler.
            if (_cast.Ready)
            {
                var stand = System.Diagnostics.Stopwatch.StartNew();
                _match.FieldersStand(_side.Where(kv => kv.Value != null && kv.Value.gameObject.activeSelf && _fielders.Any(f => f.a == kv.Value))
                    .Select(kv => (kv.Key, (double)kv.Value.transform.position.x, (double)(kv.Value.transform.position.z + GroundSpace.HalfPitch))));
                SlowLog("FieldersStand", stand);
            }
            SyncFieldBodies();
            for (var i = 0; i < _fielders.Count; i++)
            {
                var (a, home) = _fielders[i];
                if (busy.Contains(a) || !a.gameObject.activeSelf) continue;
                var spot = WaitSpot(i);
                if (Vector3.Distance(Flat(a.transform.position), spot) < 1.2f) continue;
                var toBat = new Vector3(0, 0, -GroundSpace.HalfPitch) - home;
                StartCross(a, spot, Quaternion.LookRotation(new Vector3(toBat.x, 0, toBat.z)), "fielder return to position");
            }
        }

        private IEnumerator GoBack(Animator a, Vector3 spot, float wait)
        {
            yield return new WaitForSeconds(wait);
            Body.Rebase(a);
            var dist = Vector3.Distance(Flat(a.transform.position), spot);
            // A jog back from the deep, a walk back from close.
            var pace = dist > 15f ? 3.2f : 1.6f;
            SlideScene.Tag(a, "fielder return to position");
            yield return RunTo(a, spot, dist / pace + Reaction, top: pace, betweenBalls: true);
            yield return Face(a, new Vector3(0, 0, -GroundSpace.HalfPitch), betweenBalls: true);
        }

        // ------------------------------------------------------------ throws

        /// <summary>Who takes the throw at this end (keeper, or the bowler at his stumps).</summary>
        private Animator? _receiver;

        /// <summary>
        /// Where the ball goes in from <paramref name="at"/>, from the engine's race
        /// between the wickets when it has one (the run-out end, else the end it
        /// sends the throw to; a shy when it's within the shy window), otherwise:
        /// the shorter throw on a tight run, else back to the keeper — or to the
        /// bowler from in front of the wicket when they didn't run.
        /// </summary>
        private (Vector3 target, bool keeperEnd, bool tight) ThrowPlan(Apl.Core.Play.BallResult r, Vector3 at, Animator? fielder)
        {
            var bowlerFree = Refs.Bowler != null && fielder != Refs.Bowler;
            bool keeperEnd, tight;
            var plan = r.Running;
            if (plan != null)
            {
                keeperEnd = r.RunOut ? r.RunOutAtKeeperEnd : plan.ToKeeperEnd;
                var spare = plan.Spare(System.Math.Max(r.Attempted, 1));
                tight = r.RunOut || r.DirectHit || (r.Attempted > 0 && spare < Apl.Core.Play.RunPlan.ShyWindow && spare >= -0.25);
            }
            else
            {
                var (ex, ef) = r.End;
                // The engine's numbers: the ball back at (fielded + pick-up + throw at 28 m/s), a run every 3.3 s.
                var back = r.EndTime + 0.55 + System.Math.Sqrt(ex * ex + ef * ef) / 28;
                tight = r.Runs > 0 && back - 0.4 - r.Runs * 3.3 < 0.8;
                var toKeeper = Vector3.Distance(Flat(at), Flat(KeeperTarget));
                var toBowler = Vector3.Distance(Flat(at), Flat(BowlerTarget));
                keeperEnd = tight ? toKeeper <= toBowler * 1.1f : r.Runs > 0 || at.z < 6f;
            }
            // Nobody at the bowler's end (he fielded it): the keeper's. The keeper fielding it himself: the bowler's.
            if (!bowlerFree) keeperEnd = true;
            else if (fielder != null && fielder == Refs.Keeper) keeperEnd = false;
            else if (r.Fumbled && !r.RunOut) keeperEnd = true;
            return (keeperEnd ? KeeperTarget : BowlerTarget, keeperEnd, tight);
        }

        /// <summary>The receiver gets to the stumps, facing the throw; a team-mate backs up behind them.</summary>
        private void ReadyForThrow(FieldPlay play, bool keeperEnd, float arriveBy)
        {
            var stumps = new Vector3(0, 0, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch);
            var thrower = Flat(play.BallAt);
            var line = Flat(stumps - thrower).normalized;
            Animator? receiver = keeperEnd ? Refs.Keeper : Refs.Bowler;
            if (receiver != null && !play.Fielders.Contains(receiver))
            {
                if (!keeperEnd) _bowler.Settle();
                else _keeper?.UpToStumps();
                // Behind the stumps from the throw, a little to the side.
                var spot = stumps + line * 0.65f + Vector3.Cross(Vector3.up, line) * 0.25f;
                // (Area 3, build 2: where his receive-and-break clip has him at the take — its guards refused every one when he
                // stood 0.4-0.85 m off it.)
                if (!keeperEnd && ReceivePlace(receiver, false, thrower) is ReceiveFit fit) spot = fit.Spot;
                _receiver = receiver;
                _receiverSpot = spot;
                _receiverSpotFor = receiver; // (area 3: the spot is his — ThrowIn)
                _receiverInAt = float.NaN;
                FieldRoutine(GetThere(receiver, spot, Mathf.Max(0.8f, arriveBy - 0.6f), thrower));
            }
            // The backing-up fielder: in line behind the stumps.
            var back = stumps + line * 14f;
            Animator? best = null;
            var bestDist = 28f;
            foreach (var (a, _) in _fielders)
            {
                if (!a.gameObject.activeSelf || play.Fielders.Contains(a) || a == _converger) continue;
                var d = Vector3.Distance(Flat(a.transform.position), back);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = a;
                }
            }
            _backer = best;
            if (best != null) FieldRoutine(GetThere(best, back, Mathf.Max(1f, arriveBy), stumps));
        }

        /// <summary>The throw has arrived (the director waits on it for a run out).</summary>
        private bool _throwHome;

        /// <summary>When the last throw left the hand (the camera stays on the thrower for the release).</summary>
        private float _throwStart;

        private IEnumerator GetThere(Animator a, Vector3 spot, float by, Vector3 face)
        {
            Body.Rebase(a);
            SlideScene.Tag(a, a == Refs.Keeper ? "keeper" : a == Refs.Bowler ? "bowler to the stumps for the throw" : "fielder backing up / receiving");
            yield return RunTo(a, spot, by);
            // (Area 3, log only: when the man taking the throw was at his spot, s after the hit.)
            if (a == _receiverSpotFor) _receiverInAt = Time.time - _hitAt;
            yield return Face(a, face, 0.25f);
        }

        /// <summary>The ball runs on from <paramref name="from"/> along <paramref name="dir"/>, dropping and skipping to a stop.</summary>
        private IEnumerator Roll(Vector3 from, Vector3 dir, float metres, float seconds)
        {
            var away = from + dir * metres;
            away.y = BallRadius;
            for (var t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                var p = Vector3.Lerp(from, away, 1 - (1 - t) * (1 - t));
                p.y = Mathf.Lerp(from.y, BallRadius, Mathf.Min(1, t * 3)) + Mathf.Abs(Mathf.Sin(t * 7)) * 0.25f * (1 - t);
                _ball.position = p;
                yield return null;
            }
        }

        // ------------------------------------------------------- celebrations

        /// <summary>A catch in the field: the nearest team-mates run in to the catcher (a high five, applause). False if it wasn't one.</summary>
        private bool CelebrateCatch(Animator? catcher)
        {
            if (catcher == null || catcher == Refs.Bowler || catcher == Refs.Keeper || !_fielders.Any(f => f.a == catcher)) return false;
            var at = Flat(Hips(catcher).position);
            var mates = _fielders.Select(f => f.a).Where(a => a != catcher && a.gameObject.activeSelf)
                .OrderBy(a => Vector3.Distance(Flat(a.transform.position), at)).Take(4).ToList();
            for (var i = 0; i < mates.Count; i++)
            {
                var a = mates[i];
                if (Vector3.Distance(Flat(a.transform.position), at) > 45f) continue;
                var angle = (i * 80 + 30) * Mathf.Deg2Rad;
                var spot = at + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * (i == 0 ? 0.85f : 1.8f);
                Extra(Converge(a, spot, at, i == 0 ? "high_five_standing" : "applause_loop"));
            }
            // The rest applaud where they are.
            foreach (var (a, _) in _fielders)
            {
                if (a != catcher && !mates.Contains(a) && a.gameObject.activeSelf) Body.Play(a, "applause_loop");
            }
            return true;
        }

        private IEnumerator Converge(Animator a, Vector3 spot, Vector3 catcher, string clip)
        {
            Body.Rebase(a);
            var dist = Vector3.Distance(Flat(a.transform.position), spot);
            SlideScene.Tag(a, "fielder celebrating (run to the catcher)");
            // Deep area 1: he arrives facing the catcher (RunTo steers onto it over the last strides) — Body.Play's facing
            // then has nothing left to snap round in one frame.
            var faceCatcher = Flat(catcher - spot).sqrMagnitude > 0.01f ? Quaternion.LookRotation(Flat(catcher - spot)) : (Quaternion?)null;
            yield return RunTo(a, spot, Mathf.Max(1.0f, dist / 5.5f + Reaction), endFace: SlideFix.Round1 ? faceCatcher : null, betweenBalls: true);
            Body.Play(a, Body.Has(a, clip) ? clip : "applause_loop", catcher - spot);
        }

        /// <summary>
        /// The ball in the keeper's gloves: held (an appeal, and the umpire's
        /// not-out, if he went up), then lobbed back to the bowler.
        /// </summary>
        private IEnumerator KeeperHolds(bool appealed)
        {
            var gloves = _keeper!.Gloves;
            var hold = Time.time + (appealed ? 1.6f : 0.8f);
            if (appealed)
            {
                StartCoroutine(After(0.9f, () => Umpire("umpire_not_out")));
                // The bowler goes up with him, then can't believe it.
                _bowler.Act("bowler_appeal_caught_behind", Vector3.back);
                Extra(After(1.5f, () => _bowler.Act("appeal_turned_down_hands_on_head")));
            }
            while (Time.time < hold)
            {
                if (gloves != null) _ball.position = gloves.position;
                yield return null;
            }
            var release = Time.time + _keeper.ReturnBall();
            while (Time.time < release)
            {
                if (gloves != null) _ball.position = gloves.position;
                yield return null;
            }
            var from = _ball.position;
            var to = new Vector3(0.3f, 1.3f, GroundSpace.HalfPitch - 1f);
            var seconds = Vector3.Distance(from, to) / 14f;
            for (var t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                var p = Vector3.Lerp(from, to, t);
                p.y += 1.5f * 4 * t * (1 - t);
                _ball.position = p;
                yield return null;
            }
            _ball.gameObject.SetActive(false);
        }
    }
}
