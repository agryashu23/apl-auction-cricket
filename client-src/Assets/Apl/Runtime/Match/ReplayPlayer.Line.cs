using System;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The replay's ball line (owner, deep areas: "the line seems to be shivering and not accurate"). It was a
    /// 16-point tail re-sampled off the tape every frame at t − i·0.03 s: its corners slid along the ball's path
    /// as the replay clock moved (worst at the bat and the bounce, where the tape's curve turns), and at 6 cm it
    /// was under a pixel wide once the ball was 40 m off, so it twinkled. Now the line is built once per replay
    /// from the engine's path (<see cref="ReplayLine"/>, MatchDirector.ReplayLine.cs), its points fixed in the
    /// world and drawn from the hand up to the ball, the replayed ball riding on it (ReplayRecorder.SetLine),
    /// and at least a few pixels wide however far off it is.
    /// </summary>
    public sealed partial class ReplayPlayer
    {
        /// <summary>This replay's ball line from the tape's span (oldest, newest); null: none (the ball isn't on a path).</summary>
        public Func<float, float, ReplayLine?>? LineFor;

        private ReplayLine? _line;
        private bool _lineShot;
        private Vector3[] _lineBuf = new Vector3[256];
        private Camera? _lineCam;

        /// <summary>Seconds of game time the line stays up after the ball has left it (a throw-in, a run-out pass: gone).</summary>
        private const float LineLinger = 1.0f;

        /// <summary>The line at least this many pixels wide, and never thinner than this in metres.</summary>
        private const float LinePixels = 3f, LineMinWidth = 0.045f;

        private void BeginLine()
        {
            _lineShot = false;
            _line = null;
            try { _line = LineFor?.Invoke(_tape.Oldest, _tape.Newest); }
            catch (Exception e) { Debug.LogWarning($"[APL replayline] not built: {e.Message}"); }
            if (_line != null && _line.Count < 2) _line = null;
            _tape.SetLine(_ball, _line);
            _tracer.positionCount = 0;
        }

        private void EndLine()
        {
            _tape.SetLine(null, null);
            _line = null;
            _tracer.positionCount = 0;
        }

        /// <summary>The line from where the ball left the hand up to where it is on the tape now (<paramref name="t"/> is unused: the tape's own time is).</summary>
        private void Tracer(float t, float focus)
        {
            var line = _line;
            var now = _tape.PlayTime;
            if (line == null || now < line.Start || now > line.End + LineLinger)
            {
                _tracer.positionCount = 0;
                return;
            }
            var n = line.Upto(now);
            if (n < 1)
            {
                _tracer.positionCount = 0;
                return;
            }
            if (_lineBuf.Length < n + 1) _lineBuf = new Vector3[Mathf.NextPowerOfTwo(n + 1)];
            for (var i = 0; i < n; i++) _lineBuf[i] = line.Points[i];
            var count = n;
            // The head exactly where the ball is drawn (between two of the line's points).
            if (now < line.End && line.At(now, out var head) && (head - _lineBuf[n - 1]).sqrMagnitude > 1e-6f) _lineBuf[count++] = head;
            if (count < 2)
            {
                _tracer.positionCount = 0;
                return;
            }
            _tracer.positionCount = count;
            for (var i = 0; i < count; i++) _tracer.SetPosition(i, _lineBuf[i]);
            Widths(count);
            // Test runs: the line in its frame, once a replay (six / wicket).
            if (!_lineShot && now >= line.MomentAt && Apl.Client.Dev.AutoShot.Active)
            {
                _lineShot = true;
                var name = line.Kind == "six" ? "replay_line_six" : line.Kind.StartsWith("wicket") ? "replay_line_wicket" : null;
                if (name != null) Apl.Client.Dev.AutoShot.Moment(name);
            }
        }

        /// <summary>
        /// Width along the line for the camera now: <see cref="LinePixels"/> on screen wherever that is more
        /// than <see cref="LineMinWidth"/> (a sub-pixel line twinkles as it moves), tapering a little to its start.
        /// </summary>
        private void Widths(int count)
        {
            if (_lineCam == null) _lineCam = _cam != null ? _cam.GetComponent<Camera>() : null;
            var cam = _lineCam != null ? _lineCam : Camera.main;
            var perMetre = 0f;
            Vector3 eye = Vector3.zero, fwd = Vector3.forward;
            if (cam != null)
            {
                perMetre = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(240, Screen.height) * LinePixels;
                eye = cam.transform.position;
                fwd = cam.transform.forward;
            }
            // Keys at even fractions of the line's length (LineRenderer reads its width curve along the length).
            var total = 0f;
            for (var i = 1; i < count; i++) total += Vector3.Distance(_lineBuf[i - 1], _lineBuf[i]);
            const int keys = 9;
            var curve = new AnimationCurve();
            var walked = 0f;
            var j = 0;
            for (var k = 0; k < keys; k++)
            {
                var want = total * k / (keys - 1);
                while (j < count - 1 && walked + Vector3.Distance(_lineBuf[j], _lineBuf[j + 1]) < want)
                {
                    walked += Vector3.Distance(_lineBuf[j], _lineBuf[j + 1]);
                    j++;
                }
                var p = _lineBuf[Mathf.Min(j, count - 1)];
                var depth = Mathf.Max(0.5f, Vector3.Dot(p - eye, fwd));
                var w = Mathf.Max(LineMinWidth, depth * perMetre);
                var frac = keys == 1 ? 1f : (float)k / (keys - 1);
                curve.AddKey(new Keyframe(frac, w * Mathf.Lerp(0.6f, 1f, frac)));
            }
            _tracer.widthCurve = curve;
        }
    }
}
