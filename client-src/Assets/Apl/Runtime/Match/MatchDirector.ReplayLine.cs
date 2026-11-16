using System;
using System.Collections.Generic;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The replay's ball line (<see cref="ReplayLine"/>; owner, deep areas: "whenever a shot is hit for six or a
    /// wicket happens we show the ball replay — the line seems to be shivering and not accurate").
    ///
    /// Built when a replay starts, in world space, every <see cref="ReplayLine.Step"/> s of game time, in two
    /// pieces: the delivery (the engine's <see cref="Trajectory"/> from the hand, exactly as
    /// <see cref="SetBall"/> draws it) and, off the bat, the flight (the engine's <see cref="Field.BallPath"/> as
    /// <see cref="PathAt"/> draws it). Each point's place ON that path is the one nearest the tape's ball at that
    /// moment (so a catch's re-paced flight, the meeting's eased clock, keep their timing and the bodies stay in
    /// step), its clock smoothed; the little the drawn ball was eased off the path (onto the bat, into the
    /// hands, onto the turf) is added back, smoothed over ~40 ms. A piece ends where the tape's ball left the
    /// path (in a hand, a throw, over the rope). The replayed ball rides the line (ReplayRecorder.SetLine).
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>This ball's delivery as <see cref="SetBall"/> last drew it (the line's first piece).</summary>
        private Trajectory? _lineFlight;
        private bool _lineLeft;

        /// <summary>Past this (m) the tape's ball is off the engine's path (held, thrown, gone): the piece ends.</summary>
        private const float LineAccept = 0.6f;

        /// <summary>The ball as <see cref="SetBall"/> draws it at trajectory second <paramref name="tau"/> (no side effects).</summary>
        private Vector3 DeliveryAt(Trajectory f, double tau)
        {
            var p = f.At(tau);
            var hand = 1f - Mathf.SmoothStep(0f, 1f, (float)(tau / ReleaseEase));
            return GroundSpace.Ball(p.x, p.y + 0.036, p.z, _lineLeft) + _releaseOffset * hand;
        }

        /// <summary>The line for a replay of the tape's span <paramref name="from"/>–<paramref name="to"/>; null if this ball has none.</summary>
        private ReplayLine? ReplayLineNow(float from, float to)
        {
            var f = _lineFlight;
            if (f == null || _released < 0 || _closeUp || _tape == null) return null;
            BallResult? r = _match.Log.Count > 0 ? _match.Log[_match.Log.Count - 1] : null;
            if (r != null && r.Trajectory != f) r = null;
            var hit = _hitResult != null && _hitResult.Trajectory == f && _hitAt > _released && _hitAt < to;
            if (hit) r = _hitResult;
            var kind = r == null ? "ball"
                : r.Wicket ? "wicket " + (r.HowOut ?? "out")
                : r.Six ? "six" : r.Four ? "four" : hit ? "hit" : "ball";
            var line = new ReplayLine(kind);
            var stats = new LineStats();
            var start = Mathf.Max(from, _released);
            var pace = (float)_pace;
            // 1. The delivery: from the hand to the bat (or on to the stumps / the gloves).
            BuildPiece(line, stats, start, hit ? _hitAt - ReplayLine.Step * 0.5f : to,
                s => DeliveryAt(f, s), (start - _released) * pace, pace);
            // 2. Off the bat.
            if (hit && line.Count > 0 && line.End <= _hitAt) BuildPiece(line, stats, _hitAt, to, s => PathAt(s), 0f, 1f);
            if (line.Count < 2) return null;
            line.MomentAt = MomentFor(line, r, hit);
            Debug.Log($"[APL replayline] {kind} points {line.Count}, max deviation from engine path {stats.Deviation * 100f:0.0} cm, " +
                      $"jitter (max 2nd difference) {stats.Jitter * 100f:0.00} cm ({stats.Corners} corners skipped); " +
                      $"tape off the line max {stats.TapeOff * 100f:0.0} cm, tape jitter {stats.TapeJitter * 100f:0.00} cm; " +
                      $"{line.Start - _released:0.00}-{line.End - _released:0.00} s after release{(hit ? $", bat at {_hitAt - _released:0.00} s" : "")}, " +
                      $"pieces {stats.Pieces}{(stats.EndedOff ? " (left the path)" : "")}");
            return line;
        }

        private sealed class LineStats
        {
            public float Deviation, Jitter, TapeOff, TapeJitter;
            public int Corners, Pieces;
            public bool EndedOff;
        }

        /// <summary>
        /// One piece of the line, <paramref name="t0"/>–<paramref name="t1"/> game seconds, on the path
        /// <paramref name="model"/> (by its own clock: <paramref name="s0"/> at t0, about <paramref name="rate"/> per second).
        /// </summary>
        private void BuildPiece(ReplayLine line, LineStats stats, float t0, float t1, Func<float, Vector3> model, float s0, float rate)
        {
            const float dt = ReplayLine.Step;
            var times = new List<float>();
            var clock = new List<float>();
            var tape = new List<Vector3>();
            var s = 0f;
            var first = true;
            var off = 0;
            for (var t = t0; t <= t1 + 1e-4f; t += dt)
            {
                if (!_tape.PropAt(_ball, t, out var b, raw: true)) break;
                // Where on the path the tape's ball is: near where the clock says on the first point, then a little on
                // from the last (a catch's flight is re-paced 0.3–2×, the meeting's clock eased — up to 4× the rate).
                var guess = Mathf.Max(0f, s0) + (t - t0) * rate;
                var lo = first ? guess - 0.06f * rate : s - 0.25f * dt * rate;
                var hi = first ? guess + 0.06f * rate : s + 4f * dt * rate + 1e-4f;
                var at = Nearest(model, b, Mathf.Max(0f, lo), Mathf.Max(0f, hi));
                if ((model(at) - b).magnitude > LineAccept)
                {
                    // Not on it yet: the tape's frames either side of the bat blend the ball coming in with it going
                    // out — the piece starts at the first one that is on the path (within 0.15 s).
                    if (first)
                    {
                        if (t - t0 > 0.15f) break;
                        continue;
                    }
                    // Twice running: the ball has left the path (not one odd tape frame).
                    if (++off >= 2) { stats.EndedOff = true; break; }
                    continue;
                }
                off = 0;
                first = false;
                s = at;
                times.Add(t);
                clock.Add(at);
                tape.Add(b);
            }
            var n = times.Count;
            if (n < 3) return;
            stats.Pieces++;
            // The clock smoothed (~25 ms), never running backwards; then what's left off the path (~40 ms).
            var sm = Smooth(clock, 3f);
            for (var i = 1; i < n; i++) sm[i] = Mathf.Max(sm[i], sm[i - 1]);
            var onPath = new Vector3[n];
            var rest = new Vector3[n];
            for (var i = 0; i < n; i++)
            {
                onPath[i] = model(sm[i]);
                rest[i] = tape[i] - onPath[i];
            }
            var restS = Smooth(rest, 5f);
            var pts = new Vector3[n];
            for (var i = 0; i < n; i++)
            {
                pts[i] = onPath[i] + restS[i];
                line.Add(times[i], pts[i]);
                stats.Deviation = Mathf.Max(stats.Deviation, restS[i].magnitude);
                stats.TapeOff = Mathf.Max(stats.TapeOff, (tape[i] - pts[i]).magnitude);
            }
            // Jitter: the line's (and the tape's) second difference, against the path's own, skipping the path's
            // real corners (the bounce, the stumps): what the line wobbles on its own, in cm.
            for (var i = 1; i < n - 1; i++)
            {
                var path2 = onPath[i + 1] - 2f * onPath[i] + onPath[i - 1];
                if (path2.magnitude > 0.01f)
                {
                    stats.Corners++;
                    continue;
                }
                stats.Jitter = Mathf.Max(stats.Jitter, (pts[i + 1] - 2f * pts[i] + pts[i - 1]).magnitude);
                stats.TapeJitter = Mathf.Max(stats.TapeJitter, (tape[i + 1] - 2f * tape[i] + tape[i - 1]).magnitude);
            }
        }

        /// <summary>The path's clock in [lo, hi] nearest <paramref name="b"/>: a scan, then halving steps.</summary>
        private static float Nearest(Func<float, Vector3> model, Vector3 b, float lo, float hi)
        {
            if (hi <= lo) return lo;
            const int scan = 12;
            var best = lo;
            var bd = float.MaxValue;
            for (var i = 0; i <= scan; i++)
            {
                var s = lo + (hi - lo) * i / scan;
                var d = (model(s) - b).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = s;
                }
            }
            var h = (hi - lo) / scan * 0.5f;
            for (var k = 0; k < 10; k++)
            {
                foreach (var s in new[] { best - h, best + h })
                {
                    if (s < lo || s > hi) continue;
                    var d = (model(s) - b).sqrMagnitude;
                    if (d < bd)
                    {
                        bd = d;
                        best = s;
                    }
                }
                h *= 0.5f;
            }
            return best;
        }

        /// <summary>A Gaussian (sigma <paramref name="sigma"/> samples), weights renormalised at the ends.</summary>
        private static float[] Smooth(List<float> v, float sigma)
        {
            var n = v.Count;
            var o = new float[n];
            var r = Mathf.CeilToInt(sigma * 2.5f);
            for (var i = 0; i < n; i++)
            {
                float sum = 0f, w = 0f;
                for (var j = Mathf.Max(0, i - r); j <= Mathf.Min(n - 1, i + r); j++)
                {
                    var k = Mathf.Exp(-0.5f * (j - i) * (j - i) / (sigma * sigma));
                    sum += v[j] * k;
                    w += k;
                }
                o[i] = sum / w;
            }
            return o;
        }

        private static Vector3[] Smooth(Vector3[] v, float sigma)
        {
            var n = v.Length;
            var o = new Vector3[n];
            var r = Mathf.CeilToInt(sigma * 2.5f);
            for (var i = 0; i < n; i++)
            {
                var sum = Vector3.zero;
                var w = 0f;
                for (var j = Mathf.Max(0, i - r); j <= Mathf.Min(n - 1, i + r); j++)
                {
                    var k = Mathf.Exp(-0.5f * (j - i) * (j - i) / (sigma * sigma));
                    sum += v[j] * k;
                    w += k;
                }
                o[i] = sum / w;
            }
            return o;
        }

        /// <summary>
        /// When a test run films the line: a six well on its way (after the side-on shot, on the follow), a catch
        /// just before the hands, any other wicket just before the ball stops (the stumps, the pad, the gloves).
        /// A run-out's replays are of the throw, not the ball's line: none.
        /// </summary>
        private float MomentFor(ReplayLine line, BallResult? r, bool hit)
        {
            if (r == null) return float.MaxValue;
            if (r.Six && hit) return Mathf.Min(_hitAt + 0.9f, line.End - 0.05f);
            if (!r.Wicket || r.HowOut == "run out" || r.HowOut == "stumped") return float.MaxValue;
            if (r.Caught && _caughtAt > line.Start) return Mathf.Clamp(_caughtAt - 0.12f, line.Start, line.End);
            // The last moment it was still moving.
            var p = line.Points;
            for (var i = line.Count - 1; i > 0; i--)
            {
                var v = (p[i] - p[i - 1]).magnitude / Mathf.Max(1e-4f, line.Times[i] - line.Times[i - 1]);
                if (v > 1f) return Mathf.Max(line.Start, line.Times[i] - 0.03f);
            }
            return line.End;
        }
    }
}
