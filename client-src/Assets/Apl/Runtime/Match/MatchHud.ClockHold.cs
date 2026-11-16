using System;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Who has the clock stopped, and whether they're still there (TEC-1). The frozen moments (replays,
    /// the third umpire, Hawk-Eye, ball tracking, the impact pick) call <see cref="ClockHeld"/> every
    /// frame they keep time at 0. The watchdog (MatchHud.Overlays) only counts a stopped clock nobody is
    /// holding: a moment whose coroutine was stopped or bailed out early without putting time back.
    /// A run-out referral legitimately runs ~15–17 s frozen (r13 combined run: the old 15 s watchdog
    /// fired in the middle of it and started the game under the still-running show).
    /// </summary>
    public sealed partial class MatchHud
    {
        private float _heldAt = -10f;

        /// <summary>A frozen moment is alive and keeping time at 0 this frame.</summary>
        public void ClockHeld() => _heldAt = Time.unscaledTime;

        /// <summary>Held within the last second (a moment's steps may leave a frame or two between beats).</summary>
        private bool ClockIsHeld => Time.unscaledTime - _heldAt < 1f;

        /// <summary>The watchdog restarted an abandoned stopped clock: whoever owns frozen state cleans it up (the director: the replay).</summary>
        public event Action? ClockRestarted;

        private void RaiseClockRestarted()
        {
            try { ClockRestarted?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
