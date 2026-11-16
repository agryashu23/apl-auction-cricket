using System;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Test measure for the keeper's take of a delivery (MatchDirector.KeeperRead): every frame from his read to the
    /// take, AFTER everything (the flight, the animation, the hand IK and CatchHands' cup hold — this runs after
    /// CatchHands), the ball as drawn against its own flight there. The most it was ever off its flight, or jumped
    /// further than its flight did in a frame, is the "max ball snap"; the drawn ball against the take point on the
    /// take frame is the "take gap". Added to the keeper when first used.
    /// </summary>
    [DefaultExecutionOrder(10001)]
    [DisallowMultipleComponent]
    public sealed class KeeperTakeProbe : MonoBehaviour
    {
        private Transform? _ball;
        private Func<Vector3>? _flight, _cup;
        private Func<bool>? _taken;
        private Action<float, float>? _done;
        private Vector3 _prevDrawn, _prevFlight;
        private bool _has;
        private float _max;

        public static KeeperTakeProbe For(GameObject keeper)
        {
            var p = keeper.GetComponent<KeeperTakeProbe>();
            return p != null ? p : keeper.AddComponent<KeeperTakeProbe>();
        }

        /// <summary>Watch <paramref name="ball"/> against <paramref name="flight"/> (its own path, held at the take point once there) until <paramref name="taken"/>.</summary>
        public void Watch(Transform ball, Func<Vector3> flight, Func<bool> taken, Func<Vector3> cup, Action<float, float> done)
        {
            _ball = ball;
            _flight = flight;
            _taken = taken;
            _cup = cup;
            _done = done;
            _has = false;
            _max = 0f;
        }

        /// <summary>Not the keeper's ball after all (hit, or a new ball): nothing to report.</summary>
        public void Cancel() => _ball = null;

        private void LateUpdate()
        {
            if (_ball == null || _flight == null || _taken == null) return;
            var drawn = _ball.position;
            var flight = _flight();
            _max = Mathf.Max(_max, Vector3.Distance(drawn, flight));
            if (_has) _max = Mathf.Max(_max, Vector3.Distance(drawn - _prevDrawn, flight - _prevFlight));
            _prevDrawn = drawn;
            _prevFlight = flight;
            _has = true;
            if (!_taken()) return;
            var gap = Vector3.Distance(_cup != null ? _cup() : drawn, flight);
            var done = _done;
            _ball = null;
            done?.Invoke(_max, gap);
        }
    }
}
