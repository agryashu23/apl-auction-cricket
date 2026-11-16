using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Deep area 1: turning on the spot WITHOUT spinning over planted feet (slidewatch, the 3-over baseline:
    /// "fielder run_00_ready_stance_idle_loop +turn" 181 plants, mean 1.73 m/s — the transform slerped round its own
    /// origin under a standing pose, both feet dragged round in an arc). A turn now steps:
    /// <list type="bullet">
    /// <item>With a turn clip on the body (the owner's turns delivery: fielder_turn_90_left/right,
    /// fielder_turn_180_left/right, rhb_/lhb_turn_90_settle, rhb_/lhb_turn_180_after_run…), the clip turns the pose
    /// (its root's turn is baked into the pose, like every clip's travel), the transform stays still, and at its end the
    /// transform is put under the turned body and the end state picks up from the same pose; whatever the clip didn't
    /// turn is finished by the step below. The clip's sidecar ("turn_deg", + = left; "turn_end_frame";
    /// "pivot_foot") is read from Resources/Data/cricket/*.json when it's there, else the name says it.</item>
    /// <item>Without one, a procedural step: the body turns about the pivot foot's ball (FootPin holds it there and
    /// lets it swivel), and the other foot is lifted and put down where the turned stance has it (FootPin.Swing);
    /// more than 100° is two steps, the pivot changing feet.</item>
    /// </list>
    /// Callers: MatchDirector.Face / RunTo's reaction / the crossings' last facing, the walk-outs' last facing
    /// (PreMatchWalk), Strides.Walk's first heading. "[APL turn]" lines (log only, first 60).
    /// </summary>
    public static class TurnStep
    {
        /// <summary>For the log: turns made, and how many by a clip.</summary>
        public static int Turns, ByClip;

        private static int _lines;

        /// <summary>A turn clip: its state, the turn it makes (° about up, Unity's sign: + = to his right), when the turn's done (s), its pivot foot (0 left, 1 right, -1 unknown).</summary>
        public sealed class Clip
        {
            public string Name = "";
            public float Deg;
            public float EndSeconds = -1f;
            public int Pivot = -1;
        }

        private static Dictionary<string, (float deg, int endFrame, int pivot)>? _meta;

        /// <summary>
        /// The turns' sidecars (ClipSidecars: any Data/cricket/*.json entry with a "turn_deg"), parsed as JSON — iteration 5
        /// (reviewer S-1): the old regex read none of the owner's entries (their nested "travel_m" object).
        /// </summary>
        private static Dictionary<string, (float deg, int endFrame, int pivot)> Meta()
        {
            if (_meta != null) return _meta;
            _meta = new Dictionary<string, (float, int, int)>();
            foreach (var kv in ClipSidecars.All())
            {
                if (!(ClipSidecars.Num(kv.Value, "turn_deg") is float deg)) continue;
                var pivot = (string?)kv.Value["pivot_foot"] ?? "";
                // The sidecar's sign: + = left (counter-clockwise from above); Unity's yaw: + = right.
                _meta[kv.Key] = (-deg, (int)(ClipSidecars.Num(kv.Value, "turn_end_frame") ?? -1f), pivot.StartsWith("left") ? 0 : pivot.StartsWith("right") ? 1 : -1);
            }
            if (Debug.isDebugBuild) Debug.Log($"[APL turn] turn clip sidecars: {_meta.Count} entries");
            return _meta;
        }

        /// <summary>The turn clip for this body and turn (null: none on his animator — the procedural step).</summary>
        public static Clip? Find(Animator a, float deg)
        {
            var abs = Mathf.Abs(deg);
            if (abs < 55f) return null;
            var big = abs > 135f;
            var right = deg > 0f;
            var names = new List<string>();
            if (a.GetComponent<BatterRig>() is BatterRig rig)
            {
                // (rhb_/lhb_turn_90_settle is PARKED: its last frame is 35 cm off our guard stance — verify_report, 2026-11-13 —
                // so it can't hand over to the guard; only the turn after a run is offered.)
                if (!big) return null;
                names.Add((rig.LeftHanded ? "lhb_" : "rhb_") + "turn_180_after_run");
            }
            else names.Add($"fielder_turn_{(big ? 180 : 90)}_{(right ? "right" : "left")}");
            foreach (var n in names)
            {
                if (!Body.Has(a, n)) continue;
                var c = new Clip { Name = n, Deg = big ? (right ? 180f : -180f) : right ? 90f : -90f };
                if (Meta().TryGetValue(n, out var m))
                {
                    if (Mathf.Abs(m.deg) > 1f) c.Deg = m.deg;
                    // (turn_end_frame is where the yaw is done, not the clip: the frames after it settle into the stance the
                    // clip hands over to, so the whole clip plays.)
                    c.Pivot = m.pivot;
                }
                // A 90 that turns the other way isn't this turn (a batter's single-direction settle).
                if (Mathf.Sign(c.Deg) != Mathf.Sign(deg) && Mathf.Abs(c.Deg) < 170f) continue;
                return c;
            }
            return null;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        /// <summary>
        /// Turn <paramref name="a"/> to face <paramref name="to"/> (transform rotation) over about
        /// <paramref name="seconds"/>, stepping. <paramref name="stop"/>: abandon it where it is. <paramref name="endState"/>:
        /// the state a turn clip hands over to ("Ready" when null). <paramref name="clips"/>: a turn clip may be used (it takes its
        /// own time, 0.6-1.1 s) — off by default (reviewer B3: never in a play); only between-ball callers ask for it, and never
        /// for a turn asked in under 0.3 s.
        /// </summary>
        public static IEnumerator Turn(Animator a, Quaternion to, float seconds, Func<bool>? stop = null, string? endState = null, bool clips = false)
        {
            if (a == null) yield break;
            var started = Time.time;
            var shift = 0f;
            var pivotOff = 0f;
            var tr = a.transform;
            var deg = Vector3.SignedAngle(Flat(tr.forward), Flat(to * Vector3.forward), Vector3.up);
            if (Mathf.Abs(deg) < 1f)
            {
                tr.rotation = to;
                yield break;
            }
            Turns++;
            var scene = SlideScene.Keep(a, null);
            var clip = SlideFix.Round1 && clips && seconds >= 0.3f ? Find(a, deg) : null;
            if (SlideFix.Round1) FootPin.On(a)?.Force(seconds + 2f); // (iteration 8, A1: a batter's turn is pinned too)
            if (clip != null)
            {
                ByClip++;
                var e = PlayClip(a, clip, endState ?? "Ready", stop, scene);
                while (e.MoveNext()) yield return e.Current;
                if (a == null) yield break;
                deg = Vector3.SignedAngle(Flat(a.transform.forward), Flat(to * Vector3.forward), Vector3.up);
                if (Mathf.Abs(deg) < 3f) yield break;
                seconds = Mathf.Max(0.15f, Mathf.Abs(deg) / 300f);
            }
            var pin = FootPin.On(a); // (iteration 7, B-3: on demand, working at once)
            var steps = Mathf.Abs(deg) > 100f ? 2 : 1;
            if (Debug.isDebugBuild && _lines++ < 60)
                Debug.Log($"[APL turn] {a.name} turns {deg:+0;-0}° in {seconds:0.00} s: {(clip != null ? $"clip {clip.Name} then " : "")}{(pin != null && pin.Working ? $"{steps} step(s) about the pivot foot" : "swivel (no pin)")}");
            var per = deg / steps;
            var dur = Mathf.Max(0.08f, seconds / steps);
            for (var k = 0; k < steps; k++)
            {
                if (a == null || (stop != null && stop())) yield break;
                tr = a.transform;
                // The pivot: the foot on the side he turns toward (then the other, for a second step); the other steps round.
                int leg;
                Vector3 pivot;
                if (pin != null && pin.Working)
                {
                    leg = pin.LegToward(tr.right * Mathf.Sign(per));
                    if (k % 2 == 1) leg = 1 - leg;
                    var c = pin.Contact(leg);
                    if (c == null && pin.Contact(1 - leg) is Vector3 other)
                    {
                        // (That foot's in the air: the one on the ground is the pivot.)
                        leg = 1 - leg;
                        c = other;
                    }
                    pivot = c ?? tr.position;
                    pivotOff = Mathf.Max(pivotOff, PivotOff(a, pivot));
                    pin.BeginPivot(leg, dur + 0.05f);
                    pin.Swing(1 - leg, dur * 0.9f, 0.07f);
                }
                else pivot = Strides.LowFoot(a) ?? tr.position;
                pivot = Sane(a, pivot);
                pivot.y = tr.position.y;
                // Iteration 2 (reviewer B2): frame by frame, only this frame's share of the turn, about the pivot, applied to
                // wherever he is NOW — a move by anyone else meanwhile is kept (and carries the pivot with it).
                var done = 0f;
                var mine = tr.position;
                for (var t = 0f; ; t += Time.deltaTime)
                {
                    if (a == null || (stop != null && stop())) break;
                    var want = t >= dur ? per : per * Mathf.SmoothStep(0f, 1f, t / dur);
                    var other = tr.position - mine;
                    other.y = 0f;
                    pivot += other;
                    // (Iteration 9, reviewer b3 blocker 3: 4.15 m swings — the bowler's follow-through kept carrying his body off the
                    // pivot chosen at the step's start: re-checked every frame, re-picked under his hips past 0.6 m.)
                    if (PivotOff(a, pivot) > 0.6f)
                    {
                        StalePivots++;
                        var hp = a.GetBoneTransform(HumanBodyBones.Hips);
                        if (hp != null) pivot = new Vector3(hp.position.x, tr.position.y, hp.position.z);
                    }
                    pivotOff = Mathf.Max(pivotOff, PivotOff(a, pivot));
                    var hipsB = a.GetBoneTransform(HumanBodyBones.Hips);
                    var before = hipsB != null ? hipsB.position : tr.position;
                    Rotate(tr, pivot, want - done);
                    done = want;
                    // (Iteration 11, reviewer's 6.96 m: the transform can sit metres from the body in a clip that carries its travel
                    // in the pose; turning it about the body's foot swings the TRANSFORM a long arc while the body only turns. What's
                    // logged is now the body's (hips') own shift.)
                    shift += Flat((hipsB != null ? hipsB.position : tr.position) - before).magnitude;
                    mine = tr.position;
                    if (t >= dur) break;
                    SlideScene.Hold(a, scene);
                    yield return null;
                }
                if (a == null || (stop != null && stop())) break;
            }
            if (a != null && Debug.isDebugBuild && _doneLines++ < 120)
                Debug.Log($"[APL turn] {a.name} done: {deg:+0;-0}° in {Time.time - started:0.00} s, shifted {shift:0.00} m by the pivot (pivot up to {pivotOff:0.00} m from the hips)");
        }

        /// <summary>How far (flat, m) a pivot is from his hips — never more than about 0.5 (iteration 7, reviewer B-1).</summary>
        private static float PivotOff(Animator a, Vector3 pivot)
        {
            var h = a.GetBoneTransform(HumanBodyBones.Hips);
            return h != null ? Flat(pivot - h.position).magnitude : 0f;
        }

        /// <summary>
        /// A pivot further than 0.5 m from his hips isn't under him (a stale hold, a placement): the ground under his hips
        /// instead (iteration 7, note 7-1: not his origin — in a clip that carries its travel in the pose that can be metres off).
        /// </summary>
        private static Vector3 Sane(Animator a, Vector3 pivot)
        {
            if (PivotOff(a, pivot) <= 0.5f) return pivot;
            StalePivots++;
            var h = a.GetBoneTransform(HumanBodyBones.Hips);
            return h != null ? new Vector3(h.position.x, a.transform.position.y, h.position.z) : a.transform.position;
        }

        /// <summary>For the log: pivots refused as not under him.</summary>
        public static int StalePivots;

        private static int _doneLines;

        /// <summary>Turn <paramref name="tr"/> by <paramref name="deg"/>° about the vertical through <paramref name="pivot"/> (position and rotation, from where he is now).</summary>
        private static void Rotate(Transform tr, Vector3 pivot, float deg)
        {
            if (Mathf.Abs(deg) < 1e-4f) return;
            var q = Quaternion.AngleAxis(deg, Vector3.up);
            var p = tr.position;
            var off = p - pivot;
            off.y = 0f;
            var np = pivot + q * off;
            tr.SetPositionAndRotation(new Vector3(np.x, p.y, np.z), q * tr.rotation);
        }

        // ------------------------------------------------------------ in play

        /// <summary>
        /// IN-PLAY TURN, one frame (for FieldPlay / ThrowCraft / Face during a play): turn <paramref name="a"/>'s body by
        /// <paramref name="deltaDeg"/>° (+ = to his right) about the foot he's standing on — FootPin's held contact point
        /// (the ball of the foot), else his lower foot, else his origin. Only this frame's turn is applied, to wherever he
        /// is now, so it composes with anything else that places him this frame. That foot pivots (FootPin lets it swivel
        /// on its ball for the next 0.12 s, refreshed each call); the other, held, foot steps round when the turn leaves it
        /// behind (FootPin's re-plant). Never plays a clip, never changes his animator. Safe to call every frame.
        /// Call it from Update or a coroutine (before the animator and FootPin run that frame), not from LateUpdate.
        /// Returns the pivot used (world).
        /// </summary>
        public static Vector3 PivotYaw(Animator a, float deltaDeg)
        {
            var tr = a.transform;
            if (Mathf.Abs(deltaDeg) < 1e-4f) return tr.position;
            if (!SlideFix.Round1)
            {
                tr.rotation = Quaternion.AngleAxis(deltaDeg, Vector3.up) * tr.rotation;
                return tr.position;
            }
            Vector3 pivot;
            var pin = FootPin.On(a); // (iteration 7, B-3: on demand, working at once)
            if (pin != null && pin.Working)
            {
                // The foot already pivoting keeps pivoting; else the one on the side he turns to, if it's down.
                var leg = pin.PivotingLeg ?? pin.LegToward(tr.right * Mathf.Sign(deltaDeg));
                var c = pin.Contact(leg);
                if (c == null && pin.Contact(1 - leg) is Vector3 o)
                {
                    leg = 1 - leg;
                    c = o;
                }
                if (c is Vector3 cp)
                {
                    pin.BeginPivot(leg, 0.12f);
                    pivot = cp;
                }
                else pivot = tr.position;
            }
            else pivot = Strides.LowFoot(a) ?? tr.position;
            pivot = Sane(a, pivot);
            pivot.y = tr.position.y;
            Rotate(tr, pivot, deltaDeg);
            // Iteration 9 (reviewer b3 blocker 2: in-play turns — receivers, backers, the keeper, ThrowCraft — spun the body
            // about the planted foot at up to ~1000°/s and the other foot never stepped; "ready +turn" 134 plants 0.21 / 0.86):
            // every 40° turned (within a turn: calls less than 0.2 s apart) the other foot steps round — FootPin.Swing, lifted
            // 6 cm over 0.16 s — as the between-ball turn's steps do. The turn's own timing is untouched.
            if (pin != null && pin.Working && pin.PivotingLeg is int pl)
            {
                var id = a.GetInstanceID();
                Acc.TryGetValue(id, out var st);
                if (Time.time - st.at > 0.2f) st.deg = 0f;
                st.deg += deltaDeg;
                st.at = Time.time;
                if (Mathf.Abs(st.deg) >= 40f)
                {
                    pin.Swing(1 - pl, 0.16f, 0.06f);
                    st.deg = 0f;
                    PlaySteps++;
                }
                Acc[id] = st;
            }
            return pivot;
        }

        private static readonly Dictionary<int, (float deg, float at)> Acc = new Dictionary<int, (float, float)>();

        /// <summary>For the log: steps the in-play turns took (FootPinScanner's line).</summary>
        public static int PlaySteps;

        /// <summary>Overload for a body's transform (its Animator is on it).</summary>
        public static Vector3 PivotYaw(Transform body, float deltaDeg)
        {
            var a = body.GetComponent<Animator>();
            if (a != null) return PivotYaw(a, deltaDeg);
            body.rotation = Quaternion.AngleAxis(deltaDeg, Vector3.up) * body.rotation;
            return body.position;
        }

        /// <summary>
        /// IN-PLAY TURN toward a direction, one frame: at most <paramref name="maxDegPerSec"/> × this frame's time, about the
        /// planted foot (<see cref="PivotYaw(Animator, float)"/>). Returns the angle still to turn (°, signed; 0 = facing it).
        /// </summary>
        public static float TurnTowards(Animator a, Vector3 dir, float maxDegPerSec)
        {
            var err = Vector3.SignedAngle(Flat(a.transform.forward), Flat(dir), Vector3.up);
            var most = maxDegPerSec * Time.deltaTime;
            var step = Mathf.Clamp(err, -most, most);
            PivotYaw(a, step);
            return err - step;
        }

        /// <summary>Overload for a body's transform.</summary>
        public static float TurnTowards(Transform body, Vector3 dir, float maxDegPerSec)
        {
            var a = body.GetComponent<Animator>();
            if (a != null) return TurnTowards(a, dir, maxDegPerSec);
            var err = Vector3.SignedAngle(Flat(body.forward), Flat(dir), Vector3.up);
            var step = Mathf.Clamp(err, -maxDegPerSec * Time.deltaTime, maxDegPerSec * Time.deltaTime);
            body.rotation = Quaternion.AngleAxis(step, Vector3.up) * body.rotation;
            return err - step;
        }

        /// <summary>
        /// IN-PLAY TURN as a coroutine (MatchDirector.Face during a play): face <paramref name="dir"/> over
        /// <paramref name="seconds"/> (the old Face's timing: each frame the remaining angle × this frame's share of the time
        /// left), by <see cref="PivotYaw(Animator, float)"/> each frame — rotation about the planted foot, composing with
        /// any other mover. Ends exactly facing it.
        /// </summary>
        public static IEnumerator PivotOver(Animator a, Vector3 dir, float seconds)
        {
            if (a == null) yield break;
            var scene = SlideScene.Keep(a, null);
            var deg0 = Vector3.SignedAngle(Flat(a.transform.forward), Flat(dir), Vector3.up);
            var shift = 0f;
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (a == null) yield break;
                var err = Vector3.SignedAngle(Flat(a.transform.forward), Flat(dir), Vector3.up);
                var left = Mathf.Max(seconds - t, Time.deltaTime);
                var before = a.transform.position;
                PivotYaw(a, err * Mathf.Clamp01(Time.deltaTime / left));
                shift += Flat(a.transform.position - before).magnitude;
                SlideScene.Hold(a, scene);
                yield return null;
            }
            if (a == null) yield break;
            var last = a.transform.position;
            PivotYaw(a, Vector3.SignedAngle(Flat(a.transform.forward), Flat(dir), Vector3.up));
            shift += Flat(a.transform.position - last).magnitude;
            // Iteration 3 (reviewer N9, log only): what the pivot moved him by (a receiver, a run-out collector) — his own turn
            // only, not anyone else's placing.
            if (Debug.isDebugBuild && Mathf.Abs(deg0) >= 1f && _playLines++ < 200)
                Debug.Log($"[APL turn] {a.name} in-play {deg0:+0;-0}° in {seconds:0.00} s, in-play shifted {shift:0.00} m by the pivot");
        }

        private static int _playLines;

        /// <summary>
        /// The clip turns the pose; the transform stays. At the turn's end the transform is put under the turned body
        /// (its hips, its facing, as <paramref name="endState"/> stands on its own origin) and the end state starts from there.
        /// </summary>
        private static IEnumerator PlayClip(Animator a, Clip c, string endState, Func<bool>? stop, string? scene)
        {
            a.speed = 1f;
            a.SetBool(Animator.StringToHash("Mirror"), false);
            // (Iteration 9, reviewer b3 blocker 4: the 0.12 s fade from the idle at a random phase into the clip slid — 80 plants at
            // 0.25-0.45. The fielder turns' first frame IS the ready stance's first (verify_report 0.0 cm): played straight in.)
            var from = a.GetCurrentAnimatorStateInfo(0).shortNameHash;
            if (SlideFix.Round1 && !a.IsInTransition(0) && (from == Animator.StringToHash("Ready") || from == Animator.StringToHash("run_00_ready_stance_idle_loop")))
                a.Play(c.Name, 0, 0f);
            else a.CrossFadeInFixedTime(c.Name, 0.12f, 0, 0f);
            yield return null;
            if (a == null) yield break;
            var length = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0).length : a.GetCurrentAnimatorStateInfo(0).length;
            var end = c.EndSeconds > 0f ? Mathf.Min(c.EndSeconds, length) : length - 1f / 30f;
            // Iteration 8 (reviewer T-1): the clip's own pivot foot pivots (FootPin holds its ball and lets it swivel) and any
            // held foot may swivel as the clip turns it — no twist taken out, no forced "twisted" re-plant.
            var pin = FootPin.On(a);
            if (pin != null)
            {
                pin.Force(length + 0.5f);
                pin.FreeSwivel(length + 0.1f);
                if (c.Pivot >= 0) pin.BeginPivot(c.Pivot, length);
            }
            if (Debug.isDebugBuild && _lines++ < 60) Debug.Log($"[APL turn] {a.name} plays {c.Name} ({c.Deg:+0;-0}°, {end:0.00} s of {length:0.00})");
            var cut = false;
            for (var t = 1f / 30f; t < end; t += Time.deltaTime)
            {
                if (a == null) yield break;
                if (stop != null && stop())
                {
                    cut = true;
                    // Iteration 3 (reviewer N10): a play took him mid-clip — he's put under his body and into the end state NOW
                    // (the chase starts from there), not at the clip's end, which would reset him under the chase.
                    if (Debug.isDebugBuild) Debug.Log($"[APL turn] {a.name}: {c.Name} cut at {t:0.00} s — a play took him");
                    break;
                }
                SlideScene.Hold(a, scene);
                yield return null;
            }
            if (a == null || !Body.Has(a, endState)) yield break;
            // Where the end state's hips and facing sit on its own origin (measured once per body).
            var (hipsLocal, yaw) = Stance(a, endState);
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) yield break;
            // Iteration 5: handed over by a short fade with the transform kept under the blended hips (Strides.FadeUnderBody):
            // the fielders' turns end exactly in the ready stance (0.0 cm, verify_report), the batters' turn after a run some
            // 30 cm off our non-striker idle — a hard cut would pop that.
            // Iteration 8 (reviewer T-1 / A1): re-seated in ONE frame into the end state at the phase whose pose is nearest the
            // clip's last (Strides.RebaseInto: the fielders' turns end exactly on the ready stance; the batter's after-run turn
            // ends 31 cm off our non-striker idle, so the idle's nearest phase), the transform under the turned body — and the
            // feet's holds carried through (FootPin.Reseated): the 0.1/0.3 s fade turned the transform while two facings blended,
            // and the hand-over read as a placement (the feet snapped 14-29 cm). A play cutting the clip short (I5-5) gets the same.
            if (cut && Debug.isDebugBuild) Debug.Log($"[APL turn] {a.name}: re-seated into {endState} at the cut");
            _ = hipsLocal;
            _ = yaw;
            Strides.RebaseInto(a, endState);
            pin?.Reseated();
        }

        private static readonly Dictionary<string, (Vector3, float)> Stances = new Dictionary<string, (Vector3, float)>();

        /// <summary>A state's first-frame hips (flat, in the body's own frame) and its facing against the transform (°).</summary>
        private static (Vector3 hips, float yaw) Stance(Animator a, string state)
        {
            var key = $"{state}|{a.avatar?.name}|{a.transform.lossyScale.x:0.000}";
            if (Stances.TryGetValue(key, out var known)) return known;
            a.Update(0f); // (a play asked this frame applied first: RC1)
            var was = a.GetCurrentAnimatorStateInfo(0);
            a.Play(state, 0, 0f);
            a.Update(0f);
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            var local = hips != null ? Quaternion.Inverse(a.transform.rotation) * Flat(hips.position - a.transform.position) : Vector3.zero;
            var yaw = Strides.ClipYaw(a);
            a.Play(was.fullPathHash, 0, was.normalizedTime);
            a.Update(0f);
            return Stances[key] = (local, yaw);
        }
    }
}
