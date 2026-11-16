using System;
using System.Collections;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Replays put together by the caller (the third umpire): enter the frozen
    /// replay once, play stretches of the tape from cameras of its own, hold a
    /// frame, and leave — no camera bar, the caller's own captions.
    /// </summary>
    public sealed partial class ReplayPlayer
    {
        private bool _trailWas;

        /// <summary>The tape's span: game times of the oldest and newest frames.</summary>
        public (float oldest, float newest) Span => (_tape.Oldest, _tape.Newest);

        /// <summary>Freeze the game and start writing the tape onto the scene. False if a replay's already on.</summary>
        public bool Enter()
        {
            if (Running) return false;
            Running = true;
            _skip = false;
            _trailWas = _trail.enabled;
            _trail.emitting = false;
            _trail.Clear();
            _trail.enabled = false;
            _hud.Broadcasting(true);
            _hud.SetSkippable(true);
            Time.timeScale = 0f;
            BeginLine();
            _tape.Begin();
            _cam.Unscaled = true;
            _tracer.gameObject.SetActive(true);
            return true;
        }

        /// <summary>A tap since <see cref="Enter"/> (or the last <see cref="ClearSkip"/>).</summary>
        public bool Skipped => _skip;

        public void ClearSkip() => _skip = false;

        /// <summary>
        /// Play <paramref name="from"/>–<paramref name="to"/> from <paramref name="lens"/> (asked with
        /// the tape time), slowed to <paramref name="slow"/> within 0.5 s of <paramref name="focus"/>.
        /// </summary>
        public IEnumerator Segment(float from, float to, float focus, Func<float, (Vector3 pos, Vector3 look, float fov)> lens, float slow = 0.3f)
        {
            from = Mathf.Max(from, _tape.Oldest);
            to = Mathf.Min(to, _tape.Newest);
            StallProbe.Mark($"replay segment {from:0.00}-{to:0.00} (focus {focus:0.00})");
            var t = from;
            _tape.PlayTime = t;
            ResetGuard();
            _cam.TrackLens(() => Guard(lens(_tape.PlayTime)), smooth: 0.06f, cut: true, priority: MatchCamera.Replay);
            while (!_skip && t < to)
            {
                _hud.ClockHeld();
                if (!_hud.Paused)
                {
                    if (Time.timeScale != 0f) Time.timeScale = 0f;
                    t += Time.unscaledDeltaTime * (Mathf.Abs(t - focus) < 0.5f ? slow : 1f);
                }
                _tape.PlayTime = Mathf.Min(t, to);
                Tracer(t, focus);
                yield return null;
            }
        }

        /// <summary>Hold the frame at <paramref name="at"/> for <paramref name="seconds"/> (real time).</summary>
        public IEnumerator Hold(float at, float seconds, Func<float, (Vector3 pos, Vector3 look, float fov)> lens)
        {
            _tape.PlayTime = Mathf.Clamp(at, _tape.Oldest, _tape.Newest);
            StallProbe.Mark($"replay hold at {at:0.00} for {seconds:0.0} s");
            _cam.TrackLens(() => Guard(lens(_tape.PlayTime)), smooth: 0.06f, cut: true, priority: MatchCamera.Replay);
            for (var waited = 0f; waited < seconds && !_skip; waited += _hud.Paused ? 0 : Time.unscaledDeltaTime)
            {
                _hud.ClockHeld();
                if (!_hud.Paused && Time.timeScale != 0f) Time.timeScale = 0f;
                Tracer(_tape.PlayTime, at); // the line up to the held frame (it was the last segment's)
                yield return null;
            }
        }

        /// <summary>Back to the live frame and game time.</summary>
        public void Exit()
        {
            if (!Running) return;
            StallProbe.Mark("replay exit");
            _tracer.gameObject.SetActive(false);
            EndLine();
            var endT = StallProbe.Start();
            _tape.End();
            StallProbe.End("tape end", endT);
            _cam.Unscaled = false;
            _cam.Release(MatchCamera.Replay);
            if (!_hud.Paused) Time.timeScale = 1f;
            _trail.enabled = _trailWas;
            _hud.SetSkippable(false);
            _hud.Broadcasting(false);
            Running = false;
        }

        /// <summary>Where the ball was on the tape at <paramref name="time"/> (null: not in play).</summary>
        public Vector3? BallAt(float time) => _tape.PropAt(_ball, time, out var p) ? p : (Vector3?)null;
    }
}
