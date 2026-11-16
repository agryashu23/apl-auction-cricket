using System;
using System.Collections;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Deep area 3, developer 1 (gather and throw) — the throwing half of a play (ThrowCraft has the rules):
    /// a clip with its own throw lets go on ITS release frame (the animator's clock, not a planned second),
    /// with any whole-body snap turn after the take slowed to a body's pace and never out of his hand sooner
    /// than <see cref="ThrowCraft.MinRelease"/>; a stop / pick-up without one gathers, transfers to the throwing
    /// hand and sets (chest coming round at a body's pace), then plays the throw for the distance, started so
    /// its release frame lands on the engine's moment. In both the ball is in the throwing hand (BallView,
    /// after the animation) from the transfer to the release, and the flight starts from there.
    /// </summary>
    public sealed partial class FieldPlay
    {
        /// <summary>Test-run log: the turn put on as he rose into the throw clip (deg), and whether it was a cut (over 90°).</summary>
        public float RiseTurn = float.NaN;
        public bool RiseCut;

        /// <summary>Test-run log: how far (deg) his chest was from the target as the gather began (the turn-and-throw's test).</summary>
        public float TakeTurn = float.NaN;

        /// <summary>Test-run log: how the planner chose this clip (in time / the late fallback, and its cost).</summary>
        public string PlanNote = "";

        /// <summary>The clip's own time now (s), if it's the state playing; NaN otherwise.</summary>
        private static float ClipTime(Animator a, SeqClip clip)
        {
            var s = a.GetCurrentAnimatorStateInfo(0);
            return s.shortNameHash == Animator.StringToHash(clip.Name) ? s.normalizedTime * LengthOf(a, clip) : float.NaN;
        }

        /// <summary>
        /// Plan the playback from the take to the release of a clip with its own throw, on this body: frames where
        /// his whole body turns faster than <see cref="ThrowCraft.SnapRate"/> (seq_21/22 spin 183° in 4 frames after
        /// the pick-up, up to 1,890°/s) are slowed to it, and the whole stretch is eased (to 0.75×) so it's never out
        /// of his hand sooner than <see cref="ThrowCraft.MinRelease"/>. <see cref="PaceDelay"/> is what that adds.
        /// Call once at planning, before <see cref="Play"/>.
        /// </summary>
        public void PlanPace()
        {
            _pace = null;
            PaceDelay = 0f;
            if (!ThrowCraft.On || !Clip.HasThrow || Clip.IsCatch || Clip.Kind == "drop" || Partner != null || Clip.ReleaseTime <= Clip.EventTime) return;
            var pace = (float[])SnapPace(Fielder, Clip).Clone();
            var n = pace.Length;
            var real = 0f;
            for (var i = 0; i < n; i++) real += 1f / 30f / pace[i];
            // Toward the engine's release (Unity review r1 item 2: released on the clip's own frame up to 1.4 s from the
            // engine's, the flight was stretched to 6–14 m/s), within 0.75–1.3× and never under MinRelease; a sped-up
            // stretch leaves the slowed snap frames as they are (they stay under SnapRate).
            var want = Mathf.Max(ThrowCraft.MinRelease, float.IsNaN(ReleaseBy) ? real : ReleaseBy - EventAt);
            var w = Mathf.Clamp(real / want, 0.75f, 1.3f);
            if (Mathf.Abs(w - 1f) > 0.01f)
            {
                real = 0f;
                for (var i = 0; i < n; i++)
                {
                    pace[i] = w < 1f ? pace[i] * w : pace[i] < 1f ? pace[i] : w;
                    real += 1f / 30f / pace[i];
                }
            }
            _pace = pace;
            PaceDelay = real - (Clip.ReleaseTime - Clip.EventTime);
        }

        /// <summary>
        /// Per frame from the take to the release, the playback that keeps the clip's whole-body turn under
        /// <see cref="ThrowCraft.SnapRate"/> — sampled once per clip (every fielder has the same rig) and kept:
        /// up to 90 animator evaluations, so <see cref="Warm"/> does them between balls (reviewer R5).
        /// </summary>
        private static float[] SnapPace(Animator a, SeqClip clip)
        {
            if (Snaps.TryGetValue(clip.Name, out var known)) return known;
            var n = Mathf.Max(1, Mathf.RoundToInt((clip.ReleaseTime - clip.EventTime) * 30f));
            var pace = new float[n];
            var prev = Sample(a, clip, clip.EventTime).Facing;
            for (var i = 0; i < n; i++)
            {
                var f = Sample(a, clip, clip.EventTime + (i + 1) / 30f).Facing;
                var rate = Mathf.Abs(Mathf.DeltaAngle(prev, f)) * 30f;
                prev = f;
                pace[i] = rate > ThrowCraft.PaceRate ? Mathf.Max(0.3f, ThrowCraft.PaceRate / rate) : 1f;
            }
            Snaps[clip.Name] = pace;
            return pace;
        }

        private static readonly System.Collections.Generic.Dictionary<string, float[]> Snaps = new System.Collections.Generic.Dictionary<string, float[]>();

        /// <summary>Between balls: one own-throw clip's snap pace not yet known, measured on this body (one a call, so no single frame carries them all).</summary>
        private static void WarmPace(Animator a)
        {
            if (!ThrowCraft.On) return;
            foreach (var c in SeqClip.All)
            {
                if (!c.HasThrow || c.IsCatch || c.Kind == "drop" || c.PartnerOnly || c.Pair || c.ReleaseTime <= c.EventTime || Snaps.ContainsKey(c.Name) || !Has(a, c)) continue;
                SnapPace(a, c);
                return;
            }
        }

        /// <summary>The playback rate at clip time <paramref name="t"/> (1 outside the planned stretch).</summary>
        private float PaceAt(float t)
        {
            if (_pace == null || t < Clip.EventTime) return 1f;
            var i = Mathf.FloorToInt((t - Clip.EventTime) * 30f);
            return i >= 0 && i < _pace.Length ? _pace[i] : 1f;
        }

        /// <summary>His own throw: held to the clip's release frame (paced), turned onto the man taking it, in his hand, then thrown.</summary>
        private IEnumerator ThrowOwn(Animator a, Transform ball, float hitAt, Func<Vector3> held, Func<FieldPlay, Animator, Vector3, float, IEnumerator> throwTo)
        {
            float Now() => Time.time - hitAt;
            TakenAt = EventAt;
            ThrowClip = Clip.Name;
            var release = ReleaseAt + PaceDelay;
            ReleaseEta = release;
            var attached = false;
            // In his hands after the animation every frame, not a frame behind them (Unity review r1, item 1).
            ThrowCraft.Follow(ball, held);
            while (true)
            {
                var ct = ClipTime(a, Clip);
                if (float.IsNaN(ct) ? Now() >= release : ct >= Clip.ReleaseTime - 0.5f / 30f) break;
                if (Now() > release + 0.4f) break;
                if (!float.IsNaN(ct)) a.speed = PaceAt(ct);
                TurnToAim(a, Clip, Now(), release - 0.08f);
                // In the throwing hand once it's been moved across (the clip's own gather end if it says).
                if (!attached && Now() - EventAt >= Mathf.Min(ThrowCraft.Transfer, Mathf.Max(0.05f, release - EventAt - 0.05f)))
                {
                    attached = true;
                    GatherDone = Now();
                    ThrowCraft.InHand(ball, a);
                }
                if (!attached) ball.position = held();
                yield return null;
            }
            a.speed = 1f;
            if (!attached)
            {
                GatherDone = Now();
                ball.position = ThrowCraft.Palm(a);
            }
            Thrown = true;
            yield return throwTo(this, a, ball.position, Clip.ThrowSpeed);
        }

        /// <summary>
        /// Up from a stop / pick-up (or a loose ball he's picked up, <c>MatchDirector.Gather</c>): the gather
        /// (<paramref name="gather"/> s from <paramref name="takenAt"/>, s after the hit) with the ball in the collecting
        /// hands (<paramref name="held"/>), into the throwing hand, the chest coming round to the target at a body's
        /// pace; then the throw for the distance and the turn (ThrowCraft.PickThrow), started so its release frame
        /// lands on <see cref="ReleaseBy"/> — no sooner than the gather allows (played up to 1.3× quicker, its still
        /// first frames skipped), no more than 1.2 s later — released on its frame, and he settles.
        /// </summary>
        public IEnumerator SetAndThrow(Animator a, Transform ball, float hitAt, float takenAt, float gather, Func<Vector3> held,
            Func<FieldPlay, Animator, Vector3, float, IEnumerator> throwTo)
        {
            float Now() => Time.time - hitAt;
            Thrower = a;
            TakenAt = takenAt;
            var gatherEnd = takenAt + gather;
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 AimNow() => Aim?.Invoke() ?? Target;
            var toAim = Flat(AimNow() - (hips != null ? hips.position : a.transform.position));
            TakeTurn = Mathf.Abs(Mathf.DeltaAngle(ThrowCraft.ChestYaw(a), Yaw(toAim)));
            var clip = ThrowCraft.PickThrow(a, toAim.magnitude, TakeTurn);
            var speed = 1f;
            // A throw clip with its own gather (owner ITEM 4's gather_end_frame: the side-arm snap from the crouch, the
            // turn-and-throw's take facing away) starts from its gather's end — the pick-up already did the gather.
            var head = clip != null && !float.IsNaN(clip.GatherEnd) ? Mathf.Clamp(clip.GatherEnd, 0f, clip.ReleaseTime - 0.2f) : 0f;
            var skip = head;
            var lead = clip != null ? clip.ReleaseTime - head : 0.3f;
            var start = (float.IsNaN(ReleaseBy) ? gatherEnd + lead : ReleaseBy) - lead;
            if (clip != null && start < gatherEnd)
            {
                // Behind the engine's moment: skip the clip's still start, then play it a little quicker.
                var want = Mathf.Max(0.05f, (float.IsNaN(ReleaseBy) ? gatherEnd + lead : ReleaseBy) - gatherEnd);
                skip = Mathf.Max(head, Mathf.Min(StillHead(a, clip), Mathf.Max(0f, clip.ReleaseTime - want)));
                speed = Mathf.Clamp((clip.ReleaseTime - skip) / want, 1f, ThrowCraft.MaxSpeedUp);
                lead = (clip.ReleaseTime - skip) / speed;
                start = gatherEnd;
            }
            start = Mathf.Min(start, gatherEnd + 1.2f);
            // The gather and the set: into the throwing hand, turned toward the target (the turn-and-throw turns itself).
            var turnIn = clip == null || clip.Name != ThrowCraft.TurnThrow;
            var attached = false;
            ThrowCraft.Follow(ball, held);
            while (Now() < start)
            {
                if (turnIn) ThrowCraft.TurnChest(a, AimNow(), ThrowCraft.TurnRate);
                if (!attached && Now() >= Mathf.Min(takenAt + ThrowCraft.Transfer, start - 0.02f))
                {
                    attached = true;
                    GatherDone = Now();
                    ThrowCraft.InHand(ball, a);
                }
                if (!attached) ball.position = held();
                yield return null;
            }
            if (!attached)
            {
                GatherDone = Now();
                ThrowCraft.InHand(ball, a);
            }
            // (Build 4: a 5 m throw went side-arm — the end or the man taking it had moved since the gather began.) The
            // throw is re-picked for where it goes now; a different clip starts from its own gather end at 1x.
            if (clip != null)
            {
                var aimNow = Flat(AimNow() - (hips != null ? hips.position : a.transform.position));
                var now = ThrowCraft.PickThrow(a, aimNow.magnitude, Mathf.Abs(Mathf.DeltaAngle(ThrowCraft.ChestYaw(a), Yaw(aimNow))));
                if (now != null && now != clip)
                {
                    clip = now;
                    skip = !float.IsNaN(clip.GatherEnd) ? Mathf.Clamp(clip.GatherEnd, 0f, clip.ReleaseTime - 0.2f) : 0f;
                    speed = 1f;
                    lead = clip.ReleaseTime - skip;
                }
            }
            if (clip == null)
            {
                ThrowClip = "(no throw clip)";
                Thrown = true;
                yield return throwTo(this, a, ThrowCraft.Palm(a), 25f);
                Done = true;
                yield break;
            }
            ThrowClip = clip.Name;
            // He rises into the throw (a blend, not a cut — reviewer T8): the gather clip's pose is ahead of his transform
            // by its run-in, the throw's starts over it, so through the blend the transform slides from where it is to
            // under his hips — the drawn hips stay put. Facing stays as the gather left it (turned to the target).
            var under = hips != null ? new Vector3(hips.position.x, a.transform.position.y, hips.position.z) : a.transform.position;
            // The clip's start faces where his chest does. (Unity review r1 item 5 / I2: this turn used to be put on in one
            // frame — the 1,038–1,257°/s spikes on field_14/17>throw_18 — it's now spread over the rise, slower than a
            // body turns: the blend lasts at least |turn| / 600°/s. Only a turn over 90°, which only the turn-and-throw
            // asks (its start faces AWAY from its own forward, owner ITEM 4), is a cut — its new pose already faces
            // where his chest does, so the facing doesn't jump; a blend between poses facing apart would twist him.)
            var turnBy = Mathf.DeltaAngle(Yaw(a.transform.rotation * Vector3.forward) + Sample(a, clip, skip).Facing, ThrowCraft.ChestYaw(a));
            var cut = Mathf.Abs(turnBy) > 90f;
            RiseTurn = turnBy;
            RiseCut = cut;
            var rise = Mathf.Max(ThrowCraft.RiseBlend, Mathf.Abs(turnBy) / 600f);
            var t0 = a.transform.position;
            var q0 = a.transform.rotation;
            // (Build 2 / R1: the rise's turn pivots on his planted foot, not his hips — sliding's rule.)
            var lowFoot = Strides.LowFoot(a);
            var foot = lowFoot is Vector3 lf ? new Vector3(lf.x, t0.y, lf.z) : under;
            a.speed = speed;
            a.SetBool(MirrorParam, false);
            AnimTrace.Note(a, "FieldPlay play"); // FLD-1 log only
            if (cut)
            {
                // (About his planted foot: his hips swing round it, the new pose under them.)
                var spinAll = Quaternion.Euler(0, turnBy, 0);
                a.transform.SetPositionAndRotation(foot + spinAll * (under - foot), spinAll * q0);
                a.Play(clip.Name, 0, skip / LengthOf(a, clip));
            }
            else a.CrossFadeInFixedTime(clip.Name, rise, 0, skip);
            var blendFrom = Time.time;
            var blended = cut;
            var go = Now() + lead;
            ReleaseEta = go;
            // Round to the man taking it through the throw (the clip's own throw line, measured), released on its frame.
            while (true)
            {
                var w = Mathf.Clamp01((Time.time - blendFrom) / rise);
                if (!blended)
                {
                    // Turned by w of the turn about his planted foot, and under his (turned) hips by the end of the blend:
                    // the drawn hips stay where the blend of the two poses puts them (the gather's is ahead by its run-in).
                    var spin = Quaternion.Euler(0, turnBy * w, 0);
                    a.transform.SetPositionAndRotation(foot + spin * (under - foot) - spin * (under - t0) * (1f - w), spin * q0);
                    blended = w >= 1f;
                }
                var ct = ClipTime(a, clip);
                if (float.IsNaN(ct) ? Now() >= go : ct >= clip.ReleaseTime - 0.5f / 30f) break;
                if (Now() > go + 0.4f) break;
                // (Turned about his hips once the blend has him; before that the hips are still the gather's.)
                if (w >= 1f) TurnToAim(a, clip, Now(), go - 0.08f);
                yield return null;
            }
            a.speed = 1f;
            Thrown = true;
            var released = Now();
            yield return throwTo(this, a, ball.position, clip.ThrowSpeed);
            var settle = Time.time + Mathf.Max(0, Mathf.Min(1.6f, clip.Length - clip.ReleaseTime - 0.05f) - (Now() - released));
            while (Time.time < settle) yield return null;
            Settle(a);
            Done = true;
        }

        /// <summary>
        /// Area 3 (Unity review r1, items 2–4): what a ground clip's own throw costs beyond its fit —
        /// its release from where the engine has the ball leave his hand (what PlanPace's 0.75–1.3× can't absorb,
        /// 25 a second: an early release stretched the flight to 6–14 m/s); an overarm or side-arm throw under
        /// <see cref="ThrowCraft.UnderarmMax"/> (an underarm flick there, on every path); the backhand flick (seq_34,
        /// released facing away — the owner's complaint word for word) for anything but a shy from under 5 m.
        /// Clips with no throw of their own are timed to the engine anyway (SetAndThrow): nothing here.
        /// </summary>
        private static float ThrowFitCost(SeqClip clip, FieldBall b, float t, float toTarget, float turn)
        {
            var cost = 0f;
            // (Build 3: the engine dived for 13 balls and all 13 got a clean pick-up, the lateness cost outweighing the dive
            // bonus.) The engine's dive is the picture: anything but a dive clip for it costs more than any lateness can.
            if (b.Dived && clip.Style != FieldStyle.Dive) cost += 200f;
            if (!clip.HasThrow)
            {
                // (Build 2: dive / slide / barrier stops chained into a throw released 0.8–2 s after the engine — the get-up
                // is in the clip.) Up and set (the gather, its get-up) plus the shortest throw lead (~0.5 s) against the
                // engine's release: later costs 60 a second; earlier is fine, he holds it set (SetAndThrow).
                if (!float.IsNaN(b.ReleaseBy) && clip.Collects)
                {
                    var up = clip.Style == FieldStyle.Dive || clip.Style == FieldStyle.Chase || clip.Style == FieldStyle.Stand
                        ? (float.IsNaN(clip.ChainAt) ? 0.5f : clip.ChainAt - clip.EventTime - ThrowCraft.RiseBlend)
                        : ThrowCraft.GatherClean;
                    // (A run-out: on screen the throw mustn't leave after the engine's — the batter's clip carries him home.)
                    cost += Mathf.Max(0f, t + Mathf.Max(ThrowCraft.GatherClean, up) + 0.5f - b.ReleaseBy - 0.15f) * (b.RunOut ? 240f : 60f);
                }
                return cost;
            }
            if (!float.IsNaN(b.ReleaseBy))
            {
                // (Build 2: 57/100 off the engine by > 0.15 s with 25 a second — weighed harder.)
                var natural = clip.ReleaseTime - clip.EventTime;
                var lo = Mathf.Max(ThrowCraft.MinRelease, natural / 1.3f);
                var hi = Mathf.Max(lo, natural / 0.75f);
                var want = b.ReleaseBy - t;
                // (Build 3: the shies on the run — seq_23 x15, seq_24 x6 — still let go early; they can't hold it set, so
                // an early release costs them far more.)
                cost += (want < lo ? lo - want : 0f) * (b.RunOut ? 240f : 60f) + (want > hi ? want - hi : 0f) * (clip.Direct ? 150f : 60f);
            }
            var n = clip.Name;
            var flick = n.Contains("underarm") || n.Contains("flick");
            if (toTarget < ThrowCraft.UnderarmMax && !flick) cost += 60f;
            // (Build 3: seq_24's underarm flick at the stumps used at 25–36 m.) A flick is for under UnderarmMax only.
            if (flick && toTarget > ThrowCraft.UnderarmMax) cost += 300f;
            // (Build 3: seq_23 thrown at a man a metre away, 104 deg off.) Under 3 m he doesn't throw his own clip's throw:
            // a stop / pick-up and a lob (SetAndThrow's underarm).
            if (toTarget < 3f) cost += 300f;
            if (n.StartsWith("seq_34") && toTarget > 5f) cost += 500f;
            // The shy on the run (seq_23; build 2: 36% of plays, facing fails to 121°, side-arm at 0–5 m and 26–41 m, +turn
            // slide): only a genuine one — a tight run, 9–25 m, and no more than 30° to turn.
            if (n.Contains("on_the_run") && (!b.Tight || toTarget < ThrowCraft.UnderarmMax || toTarget > ThrowCraft.SnapMax || turn > 30f)) cost += 300f;
            return cost;
        }

        /// <summary>His palm cup (HandPoint) at <paramref name="time"/> in a clip, in his own frame (sampled on this body, cached).</summary>
        public static Vector3 CupAt(Animator a, SeqClip clip, float time)
        {
            var p = Sample(a, clip, time);
            return HandPoint.Cup(p.LeftPalm, p.RightPalm);
        }

        /// <summary>Which way his chest faces at <paramref name="time"/> in a clip (yaw, deg, his own frame; sampled, cached).</summary>
        public static float FacingAt(Animator a, SeqClip clip, float time) => Sample(a, clip, time).Facing;

        /// <summary>His hips at <paramref name="time"/> in a clip, in his own frame (sampled on this body, cached).</summary>
        public static Vector3 HipsAt(Animator a, SeqClip clip, float time) => Sample(a, clip, time).Hips;

        /// <summary>How long a clip stands nearly still at its start (s, up to 0.2): skippable when time is short.</summary>
        private static float StillHead(Animator a, SeqClip clip)
        {
            var p0 = Sample(a, clip, 0f);
            for (var k = 1; k <= 6; k++)
            {
                var p = Sample(a, clip, k / 30f);
                if (Vector3.Distance(p.Hips, p0.Hips) > 0.03f || Vector3.Distance(p.Right, p0.Right) > 0.05f) return (k - 1) / 30f;
            }
            return 0.2f;
        }
    }
}
