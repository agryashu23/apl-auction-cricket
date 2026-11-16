using System;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The run-out view: side-on at the crease the throw is coming to, low and
    /// square like the TV's run-out camera — the stumps and the popping crease in
    /// the middle, the batter racing in along the pitch, and, while it's still a
    /// long way off, the ball flying in (the frame widens to take it in, then
    /// tightens as it arrives). Held at <see cref="Field"/> priority like the
    /// fielding camera, and ended with it (<see cref="EndFielding"/>).
    /// </summary>
    public sealed partial class MatchCamera
    {
        private Func<Vector3>? _stumpBall;
        private Func<Vector3?>? _stumpBatter;
        private Func<(Vector3 pos, Vector3 look)>? _stumpShot;
        private Vector3 _stumps;
        private float _stumpSide = 1f;

        /// <summary>
        /// Watch the throw come in to the stumps at <paramref name="stumps"/> from
        /// the <paramref name="side"/> (+1: +x) of the pitch, with the batter
        /// <paramref name="batter"/> making for that crease.
        /// </summary>
        public void StumpsShot(Func<Vector3> ball, Vector3 stumps, Func<Vector3?> batter, float side, bool cut = false)
        {
            _stumpBall = ball;
            _stumpBatter = batter;
            _stumps = stumps;
            _stumpSide = side >= 0 ? 1f : -1f;
            _stumpShot ??= StumpsGoal;
            Track(_stumpShot, StumpsFov, 0.3f, cut, Field);
        }

        /// <summary>The run-out view's field of view (vertical, deg).</summary>
        private const float StumpsFov = 34f;

        /// <summary>The run-out view has the camera.</summary>
        public bool StumpsOnAir => Current == Mode.Custom && _stumpShot != null && _custom == _stumpShot;

        private (Vector3 pos, Vector3 look) StumpsGoal()
        {
            if (_stumpBall == null) return (transform.position, _look);
            // The popping crease, 1.22 m in front of the stumps towards the middle of the pitch.
            var toward = _stumps.z < 0 ? 1f : -1f;
            var crease = _stumps + new Vector3(0, 0, 1.22f * toward);
            var focus = crease + Vector3.up * 0.7f;
            var bat = _stumpBatter?.Invoke();
            // Deep area 3 (owner: "run-out ... I don't even see it"; R1: a runner 4–5 m short was out of the 4.6 m
            // half-width frame in 7 of 12 bails-off shots): the stumps, the crease and the runner all in frame —
            // centred between the stumps and his body, and pulled back until the span fits with a margin.
            var lo = _stumps.z;
            var hi = _stumps.z;
            if (bat.HasValue && Mathf.Abs(bat.Value.z - crease.z) < 22f && Mathf.Abs(bat.Value.x - crease.x) < 6f)
            {
                lo = Mathf.Min(lo, bat.Value.z);
                hi = Mathf.Max(hi, bat.Value.z);
                focus.z = (lo + hi) * 0.5f;
            }
            var need = (hi - lo) * 0.5f + 1.6f;
            // Wider while the ball's far off (it flies into the frame), tight as it arrives.
            var b = _stumpBall();
            var far = Flat(b - crease).magnitude;
            var open = Mathf.Clamp01((far - 4f) / 35f);
            var look = Vector3.Lerp(focus, b, open * 0.4f);
            // Side-on, the pitch runs across the screen: the frame's horizontal half-angle.
            var halfV = Mathf.Tan(StumpsFov * 0.5f * Mathf.Deg2Rad);
            var aspect = _cam != null ? _cam.aspect : 16f / 9f;
            var half = halfV * aspect;
            var dist = Mathf.Max(Mathf.Lerp(8.5f, 17f, open), need / Mathf.Max(0.1f, half));
            // Square on to what it frames (the span's middle), a touch toward the pitch.
            var pos = new Vector3(crease.x + _stumpSide * dist, 1.6f + dist * 0.12f, focus.z + 1.8f * toward);
            return (pos, look);
        }

        /// <summary>Stop the run-out view (called by <see cref="EndFielding"/>).</summary>
        private void EndStumps()
        {
            _stumpBall = null;
            _stumpBatter = null;
        }
    }
}
