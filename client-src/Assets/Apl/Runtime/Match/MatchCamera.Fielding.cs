using System;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The fielding camera (the reference's side / high cam after the hit): it
    /// follows the ball out, and once a fielder is closing on it, frames him and
    /// the ball together from inside the ground, high and to one side — the
    /// chase, the dive, the pick-up, the catch — and swings round with the throw
    /// back to the stumps. Held at <see cref="Field"/> priority, so the broadcast
    /// moments and replays take over when they want and hand back after.
    /// </summary>
    public sealed partial class MatchCamera
    {
        private Func<Vector3>? _fieldBall;
        private Func<Vector3?>? _fieldWorker;
        private Func<(Vector3 pos, Vector3 look)>? _fieldShot;
        private float _fieldSide, _workerWeight;
        private Vector3 _worker, _lastBall, _ballVel;

        /// <summary>Our request, if it's waiting behind a moment (withdrawn if the ball dies first).</summary>
        private Action? _fieldQueued;

        /// <summary>
        /// Cover the field play: <paramref name="ball"/> is asked every frame, and
        /// <paramref name="worker"/> for whoever is doing the work (null: nobody yet).
        /// </summary>
        public void FieldingShot(Func<Vector3> ball, Func<Vector3?> worker)
        {
            _fieldBall = ball;
            _fieldWorker = worker;
            _fieldSide = 0;
            _workerWeight = 0;
            _lastBall = ball();
            _ballVel = Vector3.zero;
            _fieldShot ??= FieldingGoal;
            var before = _pending;
            Track(_fieldShot, 36f, 0.5f, false, Field);
            _fieldQueued = _pending != before ? _pending : null;
        }

        /// <summary>The fielding camera has the camera now.</summary>
        public bool FieldingOnAir => Current == Mode.Custom && _custom == _fieldShot && _fieldShot != null;

        /// <summary>The ball's dead: let go (back to broadcast if nobody else wants it).</summary>
        public void EndFielding(bool toBroadcast = true)
        {
            var stumps = StumpsOnAir;
            _fieldBall = null;
            _fieldWorker = null;
            if (_fieldQueued != null && _pending == _fieldQueued) _pending = null;
            _fieldQueued = null;
            // Whatever was asked for meanwhile takes over; else back to the broadcast view.
            if (Hold == Field) Release(Field);
            if ((FieldingOnAir || stumps) && toBroadcast) Show(Mode.Broadcast);
            EndStumps();
        }

        private (Vector3 pos, Vector3 look) FieldingGoal()
        {
            if (_fieldBall == null) return (transform.position, _look);
            var dt = Mathf.Max(1e-4f, Unscaled ? Time.unscaledDeltaTime : Time.deltaTime);
            var b = _fieldBall();
            _ballVel = Vector3.Lerp(_ballVel, (b - _lastBall) / dt, 1 - Mathf.Exp(-dt * 6f));
            _lastBall = b;
            var w = _fieldWorker?.Invoke();
            // The fielder comes into the frame early, as he starts to close on the ball, and
            // the shot eases over to him (so you see the whole pick-up / dive / slide / catch).
            var want = w.HasValue ? Mathf.InverseLerp(60f, 22f, Vector3.Distance(Flat(b), Flat(w.Value))) : 0f;
            _workerWeight = Mathf.MoveTowards(_workerWeight, want, dt * (want > _workerWeight ? 0.9f : 1.6f));
            if (w.HasValue) _worker = w.Value;
            var lead = b + Vector3.ClampMagnitude(_ballVel, 30f) * 0.25f;
            var focus = Vector3.Lerp(lead, Vector3.Lerp(b, _worker, 0.6f), _workerWeight);
            var spread = Vector3.Distance(Flat(b), Flat(_worker)) * _workerWeight;

            // Inside the ground from the action, looking out at it (the crowd behind), off to one side.
            var outward = Flat(focus);
            if (outward.magnitude < 12f)
            {
                // Near the pitch (a throw coming in, a push): from square of the wicket.
                outward = Vector3.Lerp(new Vector3(LeftHanded ? -1 : 1, 0, 0), outward.normalized, outward.magnitude / 12f);
            }
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.forward;
            var side = new Vector3(outward.z, 0, -outward.x);
            if (_fieldSide == 0) _fieldSide = Vector3.Dot(side, transform.position - focus) >= 0 ? 1 : -1;
            // Close in on him (he fills a good part of the frame), wider while the ball's still far or high.
            var near = Mathf.Lerp(17f, 10f, _workerWeight);
            var dist = Mathf.Clamp(near + spread * 0.7f + Mathf.Max(0, b.y - 4f) * 0.6f, near, 44f);
            var pos = Flat(focus) - outward * dist * 0.75f + side * _fieldSide * dist * 0.55f + Vector3.up * (Mathf.Lerp(4.5f, 2.6f, _workerWeight) + dist * 0.28f);
            // High balls: keep it in the frame on the way up and down.
            var look = new Vector3(focus.x, Mathf.Lerp(Mathf.Lerp(0.8f, 1.0f, _workerWeight), focus.y, 0.75f), focus.z);
            return (pos, look);
        }

        private static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0, p.z);
    }
}
