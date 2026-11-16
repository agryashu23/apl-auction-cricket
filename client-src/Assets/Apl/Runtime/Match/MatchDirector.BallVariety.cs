using System.Collections;
using System.Collections.Generic;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// How the ball moves along the ground, varied (the look only — where and when the
    /// engine has it fielded is unchanged): bounce height and count with the pace, some
    /// that skid flat, a slight sideways drift from slice on cuts and edges, the rope's
    /// cushion kicking a four up and back. And for test runs a watch for any frame the
    /// ball stops dead while nobody has it.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>This hit's bounce (vertical pace per m/s of roll, and what each bounce keeps) and sideways drift (m by the end of its roll).</summary>
        private float _hopScale = 0.1f, _hopKeep = 0.45f, _drift;

        private void RollVariety(BallResult r)
        {
            var rng = _visRng;
            // A third of ground balls skid flat; the rest bounce, firmer ones higher.
            var skid = rng.NextDouble() < 0.33;
            _hopScale = skid ? 0.025f : (float)(0.07 + 0.07 * rng.NextDouble());
            _hopKeep = skid ? 0.3f : (float)(0.38 + 0.17 * rng.NextDouble());
            // Slice: cuts, glances and edges curl away towards the finer side; drives barely.
            var fine = r.Edge || System.Math.Abs(r.ExitAngle) > 70;
            var sign = r.ExitAngle >= 0 ? 1f : -1f;
            _drift = fine ? sign * (float)(0.6 + 1.0 * rng.NextDouble()) : (float)(rng.NextDouble() - 0.5) * 0.6f;
        }

        /// <summary>Seconds over which a ground ball's first-bounce slowdown is spread.</summary>
        private const float SkimSmooth = 0.72f;

        /// <summary>
        /// Distance along its line <paramref name="t"/> s after the hit for a ball hit into the
        /// ground, with the engine's step (full pace to the first bounce, then the turf's share)
        /// spread into a smooth decay: it leaves at the exit pace, eases down onto the engine's roll,
        /// and is exactly where the engine has it from <see cref="SkimSmooth"/> on. −1: use the engine's.
        /// </summary>
        private static float SmoothSkim(Field.BallPath path, float t)
        {
            var R = (float)path.RollAt;
            var v0 = (float)path.Skim;
            var v1 = (float)path.RollSpeed;
            var a = (float)path.Decel;
            if (v0 <= 0f || t >= SkimSmooth || path.DeflectAt > 0 && path.DeflectAt < SkimSmooth || v1 - a * (SkimSmooth - R) < 0.5f) return -1f;
            var u0 = v1 + a * R;
            // The extra distance the skim covers over the roll's own line, given back as a decaying surplus.
            var area = (v0 - v1) * R - a * R * R / 2f;
            if (area <= 0.01f || v0 <= u0) return -1f;
            var n1 = (v0 - u0) * SkimSmooth / area;
            if (n1 < 1.5f) return -1f;
            return u0 * t - a * t * t / 2f + area * (1f - Mathf.Pow(1f - t / SkimSmooth, n1));
        }

        /// <summary>
        /// A ball in the air that lands and runs on: the engine's step from its carry pace to its
        /// (much slower) run after the bounce, spread over the landing (from just before it to 0.5 s
        /// after) as a smooth curve meeting the engine's distance and pace at both ends. −1: the engine's.
        /// </summary>
        private static float SmoothLanding(Field.BallPath path, float t)
        {
            if (!path.InAir || path.RollSpeed <= 0) return -1f;
            var hang = (float)path.Hang;
            var t0 = Mathf.Max(0f, hang - 0.08f);
            var t1 = hang + 0.5f;
            if (t < t0 || t > t1 || path.DeflectAt > 0 && path.DeflectAt < t1 || path.SpeedAt(t1) < 0.3) return -1f;
            float Along(double at)
            {
                var (x, f, _) = path.At(at);
                return Mathf.Sqrt((float)(x * x + f * f));
            }
            var h = t1 - t0;
            var u = (t - t0) / h;
            var s0 = Along(t0);
            var s1 = Along(t1);
            var v0 = (float)path.SpeedAt(t0);
            var v1 = (float)path.SpeedAt(t1);
            // Cubic Hermite: distance and pace match the engine's at both ends.
            var u2 = u * u;
            var u3 = u2 * u;
            return (2 * u3 - 3 * u2 + 1) * s0 + (u3 - 2 * u2 + u) * h * v0 + (-2 * u3 + 3 * u2) * s1 + (u3 - u2) * h * v1;
        }

        /// <summary>The drift off the line <paramref name="t"/> s after the hit (grows as it rolls; ground balls only).</summary>
        private Vector3 Drift(float t)
        {
            var path = _hitResult!.Path;
            if (path.InAir || _drift == 0f) return Vector3.zero;
            var u = Mathf.Clamp01((t - (float)path.RollAt) / Mathf.Max(0.3f, (float)path.StopsAt - (float)path.RollAt));
            var dir = GroundSpace.Direction(path.AngleDeg, _hitLeft);
            var side = new Vector3(dir.z, 0, -dir.x) * (_hitLeft ? -1f : 1f);
            return side * (_drift * u * u);
        }

        /// <summary>A four into the boundary cushion: it kicks up and back off it, drops and lies by the rope.</summary>
        private IEnumerator RopeCushion(Vector3 at, float speed)
        {
            _ballPhase = "rope cushion";
            var dir = Flat(at).normalized;
            var v = -dir * Mathf.Clamp(speed * 0.18f, 0.5f, 3f) + Vector3.up * Mathf.Clamp(speed * 0.12f, 0.6f, 2.6f);
            var p = at;
            for (var t = 0f; t < 2f && (p.y > BallRadius || v.y > 0 || v.sqrMagnitude > 0.01f); t += Time.deltaTime)
            {
                v += Physics.gravity * Time.deltaTime;
                p += v * Time.deltaTime;
                if (p.y < BallRadius)
                {
                    p.y = BallRadius;
                    v = new Vector3(v.x * 0.5f, Mathf.Abs(v.y) * 0.3f, v.z * 0.5f);
                    if (v.y < 0.4f) v = Vector3.MoveTowards(new Vector3(v.x, 0, v.z), Vector3.zero, 3f * Time.deltaTime);
                }
                _ball.position = p;
                yield return null;
            }
        }

        /// <summary>Into his hand from wherever it is (eased), then with it for a while.</summary>
        private IEnumerator EaseHold(Transform hand, float seconds)
        {
            var from = _ball.position;
            _ballPhase = "held";
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                _ball.position = Vector3.Lerp(from, hand.position, Mathf.SmoothStep(0f, 1f, t / 0.15f));
                yield return null;
            }
        }

        /// <summary>
        /// Test runs: logs any moment the ball's speed falls by more than 70% within 0.1 s while
        /// it's in nobody's hands (a freeze, a parked ball, a ball waiting for a fielder).
        /// </summary>
        private IEnumerator BallWatch()
        {
            var trail = new List<(float t, Vector3 p)>();
            for (var end = Time.time + 12f; Time.time < end && _ball.gameObject.activeSelf;)
            {
                trail.Add((Time.time, _ball.position));
                trail.RemoveAll(x => Time.time - x.t > 0.2f);
                if (trail.Count > 4 && Time.time - trail[0].t > 0.19f)
                {
                    var mid = trail.FindIndex(x => Time.time - x.t <= 0.1f);
                    // Both windows must really span time: after one long frame (a stall) the "now" window
                    // can hold a single sample and read as a stop that never happened on the ball's path.
                    if (mid > 0 && Time.time - trail[mid].t >= 0.08f && trail[mid].t - trail[0].t >= 0.08f)
                    {
                        var before = Vector3.Distance(trail[0].p, trail[mid].p) / Mathf.Max(0.01f, trail[mid].t - trail[0].t);
                        var now = Vector3.Distance(trail[mid].p, _ball.position) / Mathf.Max(0.01f, Time.time - trail[mid].t);
                        if (_play != null && _play.HasBall && (!_play.Thrown || _throwHome)) _ballPhase = "held";
                        var (who, reach) = NearestHand();
                        // (The rope's cushion stopping a four is meant to; so is a hand taking it.)
                        // (And a throw leaving the hand: it's still in it at the release.)
                        var taken = (_ballPhase == "held" || _ballPhase == "into the hands" || _ballPhase == "throw") && reach < 0.8f;
                        if (before > 3f && now < 0.3f * before && !taken && RopeGap(_ball.position) > 2f)
                        {
                            var r = _hitResult;
                            var kind = r == null ? "?" : r.Edge ? "edge" : r.Caught ? "caught" : r.FieldPlay?.Dived == true ? "dive stop"
                                : (r.FieldPlay?.Beaten ?? "").Length > 0 ? "dive-palm" : r.Four ? "four" : "fielded";
                            Debug.Log($"[APL match] ball stopped dead at ({_ball.position.x:0.0}, {_ball.position.z:0.0}) {Time.time - _hitAt:0.00}s after the hit: {before:0.0} -> {now:0.0} m/s; phase {_ballPhase}, nearest hand {who} {reach:0.00} m, ball {kind}, frame {Time.unscaledDeltaTime * 1000f:0} ms, time x{Time.timeScale:0.00}");
                            trail.Clear();
                        }
                    }
                }
                // Sampled at the end of the frame, after everything has placed the ball (round 13: sampled
                // mid-frame, at the capture's ~10 fps the watcher could read last frame's position against
                // this frame's clock — 21 false "stopped dead" lines, all on 90-98 ms frames).
                yield return EndOfFrame;
            }
        }

        private static readonly WaitForEndOfFrame EndOfFrame = new WaitForEndOfFrame();

        /// <summary>The fielder (keeper, bowler) whose hand is nearest the ball, and how near.</summary>
        private (string who, float dist) NearestHand()
        {
            var best = ("nobody", float.MaxValue);
            void Check(Animator? a)
            {
                if (a == null || !a.gameObject.activeSelf) return;
                foreach (var b in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                {
                    var h = a.GetBoneTransform(b);
                    var d = h != null ? Vector3.Distance(h.position, _ball.position) : float.MaxValue;
                    if (d < best.Item2) best = (a.name, d);
                }
            }
            Check(Refs.Keeper);
            Check(Refs.Bowler);
            foreach (var (a, _) in _fielders) Check(a);
            return best;
        }
    }
}
