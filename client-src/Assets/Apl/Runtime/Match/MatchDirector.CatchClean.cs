using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Catches on the full only (r9: the sound log had a bounce between the edge and
    /// "caught" — a bump ball given out). The engine's catch is in the air by its own
    /// rules; the picture has to agree: the flight is slowed, if the hands need it, so
    /// the ball comes down to catching height as they get there, and it is never drawn
    /// below that height before they have it — so it never touches the turf.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The lowest a ball may come before it's caught (a diving, ankle-high take at worst), m.</summary>
        private const float LowCatch = 0.3f;

        /// <summary>The lowest a caught ball is ever drawn (centre, m): a diving catch an inch or two off the grass, not on it.</summary>
        private const float MinCatchY = 0.12f;

        /// <summary>Test runs: the hand must be within this of the ball at the catch (m), or the run fails.</summary>
        private const float CatchMeetLimit = 0.5f;

        /// <summary>Over this last stretch before the take (s) the catch's flight bends onto his real hand.</summary>
        private const float CatchBend = 0.8f;

        /// <summary>
        /// Test runs: at the catch instant, how far the ball (where its flight has it) is from the catcher's
        /// hand, and the lowest it came. Over <see cref="CatchMeetLimit"/> or below catching height is a
        /// FAIL, logged as an error (AutoShot counts errors and fails the run) — r11 had "catches" 10 m off.
        /// </summary>
        private void CatchMeets(Vector3 ball, Vector3 hand, Animator? who, float lowest, float blendGap)
        {
            // The catch instant, for the catch replay to end on (presentation: MatchDirector.WicketReplay.cs).
            _caughtAt = Time.time;
            // Deep area 3: a reached catch is held in the palm cup from here, and its PASS/FAIL comes from the
            // moment of contact with the gap measured BEFORE any blend (MatchDirector.CatchHands) — this check was
            // circular (the ball measured after it had been dragged onto the hand).
            if (_catchReach != null && _catchReachHit == _hitAt)
            {
                HoldInCup(lowest, blendGap);
                return;
            }
            if (!Autoplay) return;
            var gap = Vector3.Distance(ball, hand);
            // On the full: the ball never down at the turf before the hands (a diving catch may be low, not grounded).
            var full = lowest >= MinCatchY - 0.005f;
            // The last 0.2 s may ease it into the hands, but not drag it across from metres away.
            var dragged = blendGap > 1.5f;
            var line = $"[APL match] catch by {who?.name}: ball {gap:0.00} m from the hand (eased in from {blendGap:0.00} m), lowest before it {lowest:0.00} m ({(full ? "on the full" : "LOW")})";
            if (gap > CatchMeetLimit || !full || dragged) Debug.LogError("FAIL " + line);
            else Debug.Log("PASS " + line);
        }

        /// <summary>Test runs: how far the plan's meeting point was from his real hand (the bend took up the difference).</summary>
        private void LogPlanMiss(float planMiss)
        {
            if (Autoplay) Debug.Log($"[APL match] catch: the plan's hands were {planMiss:0.00} m from his real hand at the take (bent onto it over the last {CatchBend} s)");
        }

        /// <summary>No catch clip fitted: when the man running to it (RunTo, Hit) is under it.</summary>
        private float CatchArrival(BallResult r) =>
            Mathf.Max((float)r.Path.Hang * 0.95f, Mathf.Max(0.3f, (float)r.EndTime) + Reaction + 0.1f);

        /// <summary>
        /// For a catch taken <paramref name="catchAt"/> s after the hit: the time warp on the
        /// flight (1: as the engine has it) and the flight time it's held at (the ball's last
        /// moment at <see cref="LowCatch"/> on the way down).
        /// </summary>
        private (float warp, float low) CleanCatch(BallResult r, float catchAt)
        {
            var hang = (float)r.Path.Hang;
            var low = hang;
            // Back from the landing to the last moment it's still above catching height.
            for (var t = hang; t > 0.02f; t -= 0.01f)
            {
                if (PathAt(t).y >= LowCatch + BallRadius)
                {
                    low = t;
                    break;
                }
            }
            if (low <= 0.05f) low = Mathf.Min(hang, 0.05f);
            var warp = catchAt > low ? Mathf.Max(0.35f, low / catchAt) : 1f;
            if (Autoplay && warp < 1f) Debug.Log($"[APL match] catch kept on the full: hands at {catchAt:0.00}s, ball at {LowCatch} m at {low:0.00}s -> flight x{warp:0.00}");
            return (warp, low);
        }
    }
}
