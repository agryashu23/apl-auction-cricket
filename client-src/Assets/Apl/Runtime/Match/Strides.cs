using System;
using System.Collections;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The one way anybody walks, jogs or runs across the ground: the transform
    /// is carried along and turned so the BODY faces the way he's going — the
    /// heading comes from his velocity (smoothed), and the clip's own facing
    /// (the packs aren't all made facing the same way: batting-frame loops face
    /// sideways) is measured from the pose (hips and shoulders) and taken out.
    /// So nobody can walk backwards or sideways whatever clip is playing.
    /// </summary>
    public static partial class Strides
    {
        /// <summary>Which way a pose faces on the ground: square to the line across the hips and shoulders.</summary>
        public static Vector3 BodyForward(Animator a)
        {
            Vector3 Across(HumanBodyBones l, HumanBodyBones r)
            {
                var lt = a.GetBoneTransform(l);
                var rt = a.GetBoneTransform(r);
                return lt != null && rt != null ? rt.position - lt.position : Vector3.zero;
            }
            var right = a.isHuman ? Across(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg) + Across(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm) : Vector3.zero;
            right.y = 0;
            if (right.sqrMagnitude < 1e-4f) return a.transform.forward;
            return Vector3.Cross(right.normalized, Vector3.up).normalized;
        }

        /// <summary>How far (degrees) the pose's facing is turned from the transform's: what the clip adds.</summary>
        public static float ClipYaw(Animator a)
        {
            var f = a.transform.forward;
            f.y = 0;
            return Vector3.SignedAngle(f, BodyForward(a), Vector3.up);
        }

        /// <summary>
        /// Start a loop and measure its facing at once: the clip's pose is sampled
        /// (then the old state put back) so the walker turns right from the first stride,
        /// then it cross-fades in.
        /// </summary>
        public static float StartLoop(Animator a, string clip, float speed, float fade = 0.15f)
        {
            a.SetBool(Animator.StringToHash("Mirror"), false);
            a.speed = speed;
            AnimTrace.Note(a, "Strides.StartLoop " + clip, true); // log only
            if (!Body.Has(a, clip)) return ClipYaw(a);
            // Deep area 1 (reviewer RC1, the 123 "[APL snap]" lurches of 0.5-1.1 m a frame): a Play asked for this same frame
            // (FieldPlay.Settle's Body.Rebase + Play "Ready" after a throw) hadn't taken effect, so "was" was still the
            // finished throw — its end pose, travel baked in, was put back on a body Rebase had already moved under its hips,
            // and the cross-fade into the jog dragged it back over 11 frames. Now the pending play is applied first, and the
            // clip's facing is measured once per body and clip (no sampling, no putting back, after that).
            var key = $"{clip}|{a.avatar?.name}|{a.transform.lossyScale.x:0.000}";
            if (!SlideFix.Round1)
            {
                // (-aplslideround1off: as it was.)
                var was0 = a.GetCurrentAnimatorStateInfo(0);
                a.Play(clip, 0, UnityEngine.Random.value);
                a.Update(0f);
                var yaw0 = ClipYaw(a);
                a.Play(was0.fullPathHash, 0, was0.normalizedTime);
                a.Update(0f);
                a.CrossFadeInFixedTime(clip, fade, 0, UnityEngine.Random.value);
                return yaw0;
            }
            if (!LoopYaw.TryGetValue(key, out var yaw))
            {
                var before = a.GetCurrentAnimatorStateInfo(0).fullPathHash;
                a.Update(0f);
                var was = a.GetCurrentAnimatorStateInfo(0);
                if (was.fullPathHash != before && Debug.isDebugBuild && _pendingLines++ < 40)
                    Debug.Log($"[APL strides] StartLoop {clip} on {a.name}: a play asked this frame was pending (state {before} -> {was.fullPathHash}); applied before the measure, that state put back");
                var mirror = a.GetBool(Animator.StringToHash("Mirror"));
                a.SetBool(Animator.StringToHash("Mirror"), false);
                a.Play(clip, 0, 0f);
                a.Update(0f);
                yaw = ClipYaw(a);
                a.SetBool(Animator.StringToHash("Mirror"), mirror);
                a.Play(was.fullPathHash, 0, was.normalizedTime);
                a.Update(0f);
                LoopYaw[key] = yaw;
            }
            // (A play asked this frame and not yet applied would be dropped by the cross-fade below, which would then fade
            // from the stale state's pose: applied now, so the fade starts from the pose he's really in.)
            else a.Update(0f);
            // Iteration 7 (reviewer A1: the batter's crease walk start slid 24-60 cm — the loop faded in at a RANDOM phase, its
            // feet somewhere else from his): it fades in at the phase whose feet are nearest where his are now (24 sampled,
            // once per body, clip and the state he starts from), so the blend moves the feet as little as it can and the first
            // step lifts a foot from where it stands.
            a.CrossFadeInFixedTime(clip, fade, 0, StartPhase(a, clip));
            return yaw;
        }

        private static readonly System.Collections.Generic.Dictionary<string, float> StartPhases = new System.Collections.Generic.Dictionary<string, float>();

        /// <summary>The loop's time (s, for CrossFadeInFixedTime) whose feet (and shins), relative to the hips, best match the pose drawn now.</summary>
        internal static float StartPhase(Animator a, string clip)
        {
            var was = a.GetCurrentAnimatorStateInfo(0);
            // (Note 7-3: from a loop that's moving — walk into jog — the feet depend on where in it he is: cached per eighth of
            // its cycle, not once per state.)
            var bucket = was.loop ? Mathf.FloorToInt(Mathf.Repeat(was.normalizedTime, 1f) * 8f) : 0;
            var key = $"{was.shortNameHash}@{bucket}>{clip}|{a.avatar?.name}|{a.transform.lossyScale.x:0.000}";
            if (StartPhases.TryGetValue(key, out var known)) return known;
            var now = BodyPose(a);
            var best = float.MaxValue;
            var phase = 0f;
            var length = 1f;
            var mirror = a.GetBool(Animator.StringToHash("Mirror"));
            a.SetBool(Animator.StringToHash("Mirror"), false);
            for (var k = 0; k < 24; k++)
            {
                a.Play(clip, 0, k / 24f);
                a.Update(0f);
                length = a.GetCurrentAnimatorStateInfo(0).length;
                var p = BodyPose(a);
                var gap = 0f;
                for (var i = 0; i < 4; i++) gap = Mathf.Max(gap, (p[i] - now[i]).magnitude); // feet and shins
                if (gap >= best) continue;
                best = gap;
                phase = k / 24f;
            }
            a.SetBool(Animator.StringToHash("Mirror"), mirror);
            a.Play(was.fullPathHash, 0, was.normalizedTime);
            a.Update(0f);
            if (Debug.isDebugBuild && StartPhases.Count < 60) Debug.Log($"[APL strides] {a.name}: {clip} starts at phase {phase:0.00} (feet {best * 100:0} cm from the stance's)");
            return StartPhases[key] = phase * length;
        }

        /// <summary>Each walk loop's own facing against the transform (°), per body (StartLoop).</summary>
        private static readonly System.Collections.Generic.Dictionary<string, float> LoopYaw = new System.Collections.Generic.Dictionary<string, float>();
        private static int _pendingLines;

        /// <summary>A walker's heading and his clip's facing, step by step.</summary>
        public sealed class Steer
        {
            private Vector3 _heading;
            private bool _moving;
            private float _yaw;

            /// <param name="clipYaw">The clip's facing from <see cref="StartLoop"/> (it's re-measured as he goes).</param>
            public Steer(float clipYaw = 0f) { _yaw = clipYaw; }

            /// <summary>Put him at <paramref name="to"/> and turn his body to where he's going (<paramref name="rate"/>: how quickly he turns).</summary>
            public void Step(Animator a, Vector3 to, float rate = 9f)
            {
                var dt = Mathf.Max(Time.deltaTime, 1e-4f);
                var v = to - a.transform.position;
                v.y = 0;
                a.transform.position = to;
                if (v.sqrMagnitude > 1e-8f)
                {
                    var want = v.normalized;
                    _heading = _moving ? Vector3.Slerp(_heading, want, 1 - Mathf.Exp(-dt * rate)).normalized : want;
                    _moving = true;
                }
                if (!_moving) return;
                // The clip's own turn, kept up to date slowly (a stride swings the shoulders).
                _yaw = Mathf.LerpAngle(_yaw, ClipYaw(a), 1 - Mathf.Exp(-dt * 2f));
                a.transform.rotation = Quaternion.LookRotation(_heading) * Quaternion.Euler(0, -_yaw, 0);
            }

            /// <summary>The way he's heading (zero before he's moved).</summary>
            public Vector3 Heading => _moving ? _heading : Vector3.zero;
        }

        /// <summary>
        /// Walk (or jog) <paramref name="a"/> to <paramref name="to"/> at <paramref name="pace"/> m/s in
        /// <paramref name="clip"/> (made at <paramref name="clipSpeed"/> m/s on the pack's rig — only the
        /// fallback: the feet are measured on this body, Strides.Stride.cs); <paramref name="stop"/> ends
        /// it early where he is. He's left there, still in the loop.
        /// The body is moved by exactly what the feet cover every frame — natural stride speed ×
        /// the animator speed — and the animator speed eases up from a standstill over the first
        /// half second (while the loop fades in) and down over the last metre, so the feet never
        /// skate at the start, the end or in between.
        /// </summary>
        public static IEnumerator Walk(Animator a, Vector3 to, float pace, string clip, float clipSpeed, Func<bool>? stop = null)
        {
            to.y = 0;
            var from = a.transform.position;
            from.y = 0;
            StallProbe.Mark($"walk: {a.name} {clip} {Vector3.Distance(from, to):0.0} m");
            var measureT = StallProbe.Start();
            var natural = StrideSpeed(a, clip, clipSpeed);
            StallProbe.End($"stride measure {clip}", measureT);
            var scene = SlideScene.Keep(a, null); // deep area 1, log only: the caller's scenario, kept while he walks
            // Deep area 1 (reviewer RC2): facing well away from where he's going, he turns there first, stepping (TurnStep) —
            // the walk used to set his heading in one frame, the whole stance swung round its origin (a pop of both feet).
            if (SlideFix.Round1 && !IsPaused && (to - from).sqrMagnitude > 0.25f && !(a.GetComponent<FootLock>() is FootLock fl && fl.Active))
            {
                var turn = Vector3.SignedAngle(BodyForward(a), to - from, Vector3.up);
                if (Mathf.Abs(turn) > 35f)
                {
                    yield return TurnStep.Turn(a, Quaternion.AngleAxis(turn, Vector3.up) * a.transform.rotation, Mathf.Lerp(0.3f, 0.55f, Mathf.Abs(turn) / 180f), stop,
                        clips: a.GetComponent<BatterRig>() == null); // (a batter's turn clips end in his stance, not a walk)
                    if (a == null || (stop != null && stop())) yield break;
                    from = a.transform.position;
                    from.y = 0;
                }
            }
            var cruise = Mathf.Clamp(pace / natural, 0.7f, 1.4f);
            var yaw = StartLoop(a, clip, cruise * 0.35f);
            var meter = new SlideMeter(a);
            // The feet carry him (FootLock): along the way they push, turned until that's toward the target.
            var feet = a.GetComponent<FootLock>() ?? a.gameObject.AddComponent<FootLock>();
            if (SlideFix.Round1) FootPin.On(a); // (iteration 7, B-3: the pin from the first step, not the scanner's next pass)
            var mine = feet.Begin(clip, to);
            WalkBurst.Maybe(a, clip, from, to); // -aplwalkburst: frames from the side over a ground grid
            var first = (to - from).sqrMagnitude > 1e-6f ? (to - from).normalized : a.transform.forward;
            var rot = Quaternion.LookRotation(first) * Quaternion.Euler(0, -yaw, 0);
            a.transform.SetPositionAndRotation(from, rot);
            var stalled = 0f;
            var giveUp = 6f + 3f * Vector3.Distance(from, to) / Mathf.Max(0.5f, pace);
            var expect = from;
            var bumped = false;
            // FootLock moves him (in LateUpdate, by his planted foot); this steers, eases and ends the walk.
            for (var t = 0f; ; t += Time.deltaTime)
            {
                if (a == null || feet == null || feet.Owner != mine) yield break; // another walk took him over
                if (stop != null && stop())
                {
                    feet.End();
                    meter.Report(clip);
                    yield break;
                }
                feet.Beat = Time.frameCount;
                SlideScene.Hold(a, scene);
                if (IsPaused)
                {
                    // Held while the replay plays; then on from wherever the replay leaves him.
                    expect = a.transform.position;
                    feet.ClearStep();
                    yield return null;
                    continue;
                }
                // Nobody else moves him mid-walk (r10c walk-burst: the opener's rig, woken as he set off,
                // put him at the crease and he jogged out from there): put back where his feet had him.
                var predicted = expect + feet.LastStep;
                var moved = a.transform.position - predicted;
                moved.y = 0;
                if (moved.magnitude > 0.5f)
                {
                    if (!bumped) Debug.LogWarning($"[APL strides] {a.name}: moved {moved.magnitude:0.0} m by something else mid-{clip}: put back");
                    bumped = true;
                    a.transform.position = new Vector3(predicted.x, a.transform.position.y, predicted.z);
                }
                // The meter skips the same blend frames FootLock does (they aren't strides).
                if (a.IsInTransition(0)) meter.Skip();
                else meter.Sample();
                // A loop imported as a one-shot would freeze on its last frame while he moves on: keep it cycling.
                var st = a.GetCurrentAnimatorStateInfo(0);
                if (!st.loop && st.IsName(clip) && st.normalizedTime >= 1f) a.Play(clip, 0, Mathf.Repeat(st.normalizedTime, 1f));
                var pos = a.transform.position;
                var toGo = to - pos;
                toGo.y = 0;
                var left = toGo.magnitude;
                if (left < 0.02f) break;
                // Never an endless walk (TEC-1): a target his feet can't close on (circling it, held, pushed back)
                // ends at three times the time the walk should take — he's put there, and it's logged.
                if (t > giveUp)
                {
                    Debug.LogWarning($"[APL strides] {a.name}: {clip} still {left:0.0} m short after {t:0.0} s (of {Vector3.Distance(from, to):0.0} m): put there");
                    break;
                }
                var want = toGo / left;
                var ease = Mathf.Min(Mathf.Clamp01(0.35f + t / 0.5f), 0.45f + 0.55f * Mathf.Clamp01(left / 1.0f));
                a.speed = cruise * ease;
                var dt = Time.deltaTime;
                // Held in a standing pose by something else for a second: don't hang the scene (FootLock logs it).
                stalled = feet.Velocity.magnitude < 0.1f ? stalled + dt : 0f;
                if (stalled > 1f) pos += want * Mathf.Min(left, natural * a.speed * dt);
                // Log only (the slide line's tail).
                meter.Pace = Mathf.Max(meter.Pace, feet.Velocity.magnitude);
                meter.Playback = a.speed;
                // Turn him (about the planted foot, so it stays put) until his feet push toward the target.
                var course = feet.Course;
                if (course.magnitude > 0.2f && left > 0.6f)
                {
                    var err = Vector3.SignedAngle(new Vector3(course.x, 0, course.z), want, Vector3.up);
                    var q = Quaternion.Euler(0, Mathf.Clamp(err, -120f * dt, 120f * dt), 0);
                    var pivot = feet.Planted ?? pos;
                    pivot.y = pos.y;
                    pos = pivot + q * (pos - pivot);
                    rot = q * a.transform.rotation;
                    feet.Turned(q);
                    a.transform.SetPositionAndRotation(pos, rot);
                }
                else a.transform.position = pos;
                expect = a.transform.position;
                feet.ClearStep();
                yield return null;
            }
            if (a != null && feet != null && feet.Owner == mine)
            {
                a.transform.position = to;
                feet.End();
            }
            meter.Report(clip);
        }

        /// <summary>
        /// The proof: how fast the planted foot actually moves over the ground while he walks
        /// (world space, the lower foot while it's within 4 cm of the lowest it's been), logged
        /// per walk as "[APL strides] slide …". Zero is perfect; a skating walk shows 0.2+ m/s.
        /// </summary>
        private sealed class SlideMeter
        {
            /// <summary>One measure: a set of points, the lowest of them taken as "the foot on the ground".</summary>
            private sealed class Track
            {
                private readonly Transform?[] _pts;
                private Vector3 _last;
                private int _lastIdx = -1;
                private bool _was;
                private float _low = float.MaxValue, _prevY = float.MinValue;
                public float Sum, Secs;
                public int N, Jumps;

                public Track(params Transform?[] pts) { _pts = pts; }

                public void Skip() => _was = false;


                public void Sample(float dt)
                {
                    var idx = -1;
                    var y = float.MaxValue;
                    for (var i = 0; i < _pts.Length; i++)
                    {
                        if (_pts[i] == null || _pts[i]!.position.y >= y) continue;
                        y = _pts[i]!.position.y;
                        idx = i;
                    }
                    if (idx < 0) return;
                    var p = _pts[idx]!.position;
                    _low = Mathf.Min(p.y, _low + 0.03f * dt);
                    // Same rule as FootLock: low and not moving vertically (a run's toe-off isn't a planted foot).
                    var vy = _prevY > float.MinValue && idx == _lastIdx && dt > 0 ? Mathf.Abs(p.y - _prevY) / dt : 0f;
                    _prevY = p.y;
                    var down = p.y < _low + 0.04f && vy < 0.35f;
                    if (down && _was && idx == _lastIdx && dt > 0)
                    {
                        var d = p - _last;
                        d.y = 0;
                        // Half a metre in a frame is somebody placing him (a cut), not his foot sliding.
                        if (d.magnitude > 0.5f) Jumps++;
                        else
                        {
                            Sum += d.magnitude;
                            Secs += dt;
                            N++;
                        }
                    }
                    _was = down;
                    _last = p;
                    _lastIdx = idx;
                }

                public float Rate => Sum / Mathf.Max(Secs, 1e-4f);
            }

            // Contact: the point actually on the ground (the lowest of both ankles and toes — the ball
            // of the foot through the stance). Ankle: the old figure; it also counts the heel lifting
            // and rolling over the planted toe, which the walk-burst frames showed is not slide
            // (pinned toe 0.0 cm while its ankle moved 1-5 cm between frames).
            private readonly Track _contact, _ankle;

            /// <summary>For the report only: the top speed his feet gave, the playback at the end.</summary>
            public float Pace, Playback;

            public SlideMeter(Animator a)
            {
                var la = a.GetBoneTransform(HumanBodyBones.LeftFoot);
                var ra = a.GetBoneTransform(HumanBodyBones.RightFoot);
                _contact = new Track(la, a.GetBoneTransform(HumanBodyBones.LeftToes), ra, a.GetBoneTransform(HumanBodyBones.RightToes));
                _ankle = new Track(la, ra);
            }

            /// <summary>A frame that isn't a stride (a cross-fade): breaks the comparison, counts nothing.</summary>
            public void Skip()
            {
                _contact.Skip();
                _ankle.Skip();
            }

            public void Sample()
            {
                if (!Debug.isDebugBuild) return;
                _contact.Sample(Time.deltaTime);
                _ankle.Sample(Time.deltaTime);
            }

            public void Report(string clip)
            {
                if (_contact.N < 10 || !Debug.isDebugBuild) return;
                Debug.Log($"[APL strides] slide {clip}: {_contact.Rate:0.000} m/s contact-point drift over {_contact.N} frames; ankle {_ankle.Rate:0.000} m/s{(_contact.Jumps > 0 ? $" ({_contact.Jumps} placement jumps left out)" : "")}"
                          + $" | feet top {Pace:0.00} m/s, playback at end {Playback:0.00}");
            }
        }

        /// <summary>
        /// Deep area 1 (reviewer RC5): out of a clip that carried him off his transform (a celebration, an appeal, a
        /// follow-through) into <paramref name="state"/> (a stance authored on its origin) WITHOUT a Rebase first. Body.Rebase
        /// moved the transform under the hips while the old pose was still drawn from it — the pose jumped by the clip's
        /// travel and the fade dragged it back, planted feet and all. Here the transform stays under the BLEND instead: as
        /// the fade's weight w goes 0 → 1, the old pose's hips offset (its travel) shrinks by (1 − w) and the transform is
        /// put so the blended hips stay where they are, turning to the body's own facing. (As BatterRig.FadeTo does.)
        /// FootPin holds the feet through it and steps any the blend moves too far.
        /// </summary>
        public static IEnumerator FadeUnderBody(Animator a, string state, float fade) => FadeUnderBody(a, state, fade, 0f, Vector3.zero);

        /// <summary>
        /// The same into a state whose own pose faces <paramref name="endYaw"/>° off its transform with its hips at
        /// <paramref name="endHips"/> (flat, in its own frame): the transform ends where that state's pose has the hips and
        /// facing the body has now (TurnStep's hand-over out of a turn clip).
        /// </summary>
        public static IEnumerator FadeUnderBody(Animator a, string state, float fade, float endYaw, Vector3 endHips)
        {
            var tr = a.transform;
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null)
            {
                a.CrossFadeInFixedTime(state, fade);
                yield break;
            }
            var r0 = tr.rotation;
            var r1 = Quaternion.LookRotation(BodyForward(a)) * Quaternion.Euler(0, -endYaw, 0);
            var at = hips.position;
            at.y = 0f;
            var p0 = tr.position;
            var offset = Quaternion.Inverse(r0) * new Vector3(at.x - p0.x, 0f, at.z - p0.z);
            // (Iteration 9, reviewer: "bowler_celebration_aeroplane>ready +turn" mean 1.145 — the transform turns to the body's
            // facing through the fade: held feet may swivel on their contact meanwhile, held, not twist-fought.)
            if (SlideFix.Round1 && Quaternion.Angle(r0, r1) > 20f) FootPin.On(a)?.FreeSwivel(fade + 0.1f);
            a.CrossFadeInFixedTime(state, fade);
            for (var t = 0f; ; t += Time.deltaTime)
            {
                if (a == null) yield break;
                var w = Mathf.Clamp01(t / Mathf.Max(0.01f, fade));
                var rot = Quaternion.Slerp(r0, r1, w);
                var p = at - rot * Vector3.Lerp(offset, endHips, w);
                tr.SetPositionAndRotation(new Vector3(p.x, tr.position.y, p.z), rot);
                if (w >= 1f) yield break;
                yield return null;
            }
        }

        /// <summary>Foot slide (m/s) if <paramref name="clip"/> on <paramref name="a"/> were carried at <paramref name="pace"/> at animator speed <paramref name="animSpeed"/>: the hand-over's proof number.</summary>
        public static float Slide(Animator a, string clip, float fallback, float pace, float animSpeed) =>
            Mathf.Abs(pace - StrideSpeed(a, clip, fallback) * animSpeed);

        /// <summary>Walk on along <paramref name="direction"/> until <paramref name="stop"/> (a walk-off).</summary>
        public static IEnumerator Along(Animator a, Vector3 direction, float pace, Func<bool> stop, string? clip = null)
        {
            direction.y = 0;
            direction.Normalize();
            StallProbe.Mark($"walk along: {(a != null ? a.name : "-")}");
            // Owner v26 #17 (the walk-off slid: 0.54 m/s whatever the loop's own stride): the loop's stride is
            // measured on this body and the pace and playback agree — playback kept within 0.75-1.25x, the pace
            // follows it exactly, so the feet carry him.
            if (clip != null && SlideFix.On && a != null && Body.Has(a, clip))
            {
                var natural = StrideSpeed(a, clip, pace);
                var k = Mathf.Clamp(pace / Mathf.Max(0.1f, natural), 0.75f, 1.25f);
                a.speed = k;
                pace = natural * k;
            }
            // The pose as the clip has it now (a clip just started hasn't been sampled yet).
            a!.Update(0f);
            var steer = new Steer(ClipYaw(a));
            var scene = SlideScene.Keep(a, null); // deep area 1, log only
            while (a != null && !stop())
            {
                SlideScene.Hold(a, scene);
                steer.Step(a, a.transform.position + direction * (pace * Time.deltaTime));
                yield return null;
            }
        }
    }
}
