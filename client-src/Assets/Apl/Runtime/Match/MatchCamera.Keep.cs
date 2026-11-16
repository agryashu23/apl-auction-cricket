using System;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Keep the ball in the picture (owner, round 2: "you can't tell where the
    /// ball went"). While a hit is live, whatever shot is on air at the plain or
    /// fielding level — the follow, the fielding camera, the broadcast view (never
    /// someone's close-up) — is
    /// corrected after it's placed: if the ball is near the frame's edge or out of
    /// it, the camera turns toward it and the lens opens up, then eases back once
    /// it's comfortably inside. Moments and replays (higher holds) are left alone.
    /// </summary>
    public sealed partial class MatchCamera
    {
        /// <summary>The ball while it's live after the hit (null: nothing to keep in frame).</summary>
        public Func<Vector3?>? KeepInFrame;

        /// <summary>How far the lens is opened, and how far the aim is pulled toward the ball, right now.</summary>
        private float _keepWiden, _keepPull;

        /// <summary>The safe box in the viewport (x, y from 0 to 1): inside it nothing is corrected.</summary>
        private const float SafeX = 0.14f, SafeY = 0.16f;

        /// <summary>Called last in LateUpdate: turn and widen so the ball stays in the safe box.</summary>
        private void Keep(float dt)
        {
            Vector3? ball = null;
            if (KeepInFrame != null && Hold <= Field && !Unscaled && _cam != null && (FieldingOnAir || Current == Mode.Follow || Current == Mode.Broadcast)) ball = KeepInFrame();
            if (ball == null)
            {
                // Ease off whatever correction was on.
                _keepWiden = Mathf.MoveTowards(_keepWiden, 0, dt * 20f);
                _keepPull = Mathf.MoveTowards(_keepPull, 0, dt * 2f);
                if (_cam != null && _keepWiden > 0.01f) _cam.fieldOfView = Fov + _keepWiden;
                return;
            }
            var b = ball.Value;
            var v = _cam!.WorldToViewportPoint(b);
            var outside = v.z <= 0 || v.x < SafeX || v.x > 1 - SafeX || v.y < SafeY || v.y > 1 - SafeY;
            // How far out it is (0 at the safe box's edge, ~1 at the frame's edge or behind).
            var over = v.z <= 0 ? 1f : Mathf.Max(Mathf.Max(SafeX - v.x, v.x - (1 - SafeX)) / SafeX, Mathf.Max(SafeY - v.y, v.y - (1 - SafeY)) / SafeY);
            over = Mathf.Clamp01(over);
            var wantPull = outside ? Mathf.Lerp(0.35f, 1f, over) : 0f;
            var wantWiden = outside ? Mathf.Lerp(4f, 16f, over) : 0f;
            _keepPull = Mathf.MoveTowards(_keepPull, wantPull, dt * (outside ? 6f : 1.2f));
            _keepWiden = Mathf.MoveTowards(_keepWiden, wantWiden, dt * (outside ? 40f : 8f));
            if (_keepPull > 0.001f)
            {
                // Aim between the shot's own look point and the ball.
                _look = Vector3.Lerp(_look, b, _keepPull * (1 - Mathf.Exp(-dt * 14f)));
                transform.LookAt(_look);
            }
            _cam.fieldOfView = Mathf.Min(70f, _cam.fieldOfView + Mathf.Max(0, Fov + _keepWiden - _cam.fieldOfView) * (1 - Mathf.Exp(-dt * 10f)));
        }
    }
}
