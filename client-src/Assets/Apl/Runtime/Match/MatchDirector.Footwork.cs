using System;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Footwork before the ball (the user batting): CHARGE down the track, or a step
    /// across (◄ / ► chips; in swipe mode a swipe before release — up to charge,
    /// sideways to move across). Open from the moment the bowler sets off on his
    /// run-up until <see cref="FootworkAfterRelease"/> s after he lets go, once each
    /// per ball. Charging moves the perfect tap earlier (he meets it sooner): the
    /// meter and the ring follow. The engine reads it off the ShotInput (Charged /
    /// Across) — a fuller ball, a different line, stumped if he misses.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>Real seconds after release footwork is still allowed.</summary>
        public const float FootworkAfterRelease = 0.3f;

        private bool _footworkWired;

        /// <summary>Hook the HUD's footwork controls up (once).</summary>
        private void WireFootwork()
        {
            if (_footworkWired) return;
            _footworkWired = true;
            _hud.FootworkPressed += OnFootwork;
            _hud.SwipeFootwork = SwipeFootwork;
        }

        /// <summary>The bowler's running in (not standing at his mark), the ball not yet past the window, no shot yet.</summary>
        private bool FootworkOpen()
        {
            if (!_match.UserBatting || _tap != null || _left || !_striker.UsesClips) return false;
            if (_released >= 0) return Time.time - _released <= FootworkAfterRelease;
            var a = _bowler.GetComponent<Animator>();
            return a != null && _ballView.InHand && a.GetCurrentAnimatorStateInfo(0).shortNameHash != Animator.StringToHash("Stand");
        }

        /// <summary>0: charge; −1 / +1: a step to the screen's left / right (the radar's sides: a right-hander's off side is on the left).</summary>
        private void OnFootwork(int what)
        {
            if (!FootworkOpen()) return;
            if (what == 0)
            {
                _striker.Charge();
                Retime();
                return;
            }
            var lh = _striker.LeftHanded;
            _striker.MoveAcross((what < 0) != lh ? 1 : -1);
        }

        /// <summary>A swipe before release is footwork (up: charge; sideways: across); true if it was taken as that.</summary>
        private bool SwipeFootwork(Vector2 dir)
        {
            if (_released >= 0 || !FootworkOpen()) return false;
            if (dir.y > Mathf.Abs(dir.x)) OnFootwork(0);
            else if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y)) OnFootwork(dir.x < 0 ? -1 : 1);
            return true;
        }

        /// <summary>Charged: the meter's window and (once it's flying) the ring move to the earlier tap.</summary>
        private void Retime()
        {
            var t = _flight ?? _match.NextDelivery();
            var d = t.Delivery;
            var window = Batting.Window(_match.OnStrike.Player, d);
            var contactAt = _contactAt > 0 ? _contactAt : t.TimeAt(Batting.ContactZ(d.Length));
            _hud.ShowMeter((float)(Batting.IdealTap(t, true) / contactAt), (float)(window / contactAt));
            if (_released >= 0) _ballView.TimeRing(RealAt(Batting.IdealTap(t, true)), (float)(window / _pace), (float)(0.35 * window / _pace));
        }
    }
}
