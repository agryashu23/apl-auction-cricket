using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The ball at the hands: catches, collects and takes end the ball's flight in
    /// the hands that take it — the last <see cref="HandBlend"/> s ease it onto them —
    /// and test runs log how far the flight's own end was from those hands (the plan's
    /// accuracy; aim: under 0.25 m).
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>Seconds over which a flying ball is eased onto the hands that take it.</summary>
        private const float HandBlend = 0.2f;

        /// <summary>What the ball's doing now (test runs' stopped-dead watch reports it).</summary>
        private string _ballPhase = "";

        private void LogCatchMiss(BallResult r, FieldPlay? play, Vector3 flight, Vector3 hand)
        {
            var kind = r.CaughtAndBowled ? "caught and bowled" : r.Caught ? "catch" : r.Dropped.Length > 0 ? "drop" : "collect";
            var clip = play != null ? play.Clip.Name : "return catch";
            var miss = Vector3.Distance(flight, hand);
            var steered = play != null && play.Corrected > 0.05f ? $", steered {play.Corrected:0.00} m onto it" : "";
            Debug.Log($"[APL match] {kind} {clip}: ball-to-hand at the catch frame {miss:0.00} m{(miss > 0.25f ? "  (over 0.25)" : "")}{steered}");
        }

        /// <summary>Ease a ball flying to <paramref name="to"/> onto a hand that's somewhere near it, over the flight's last moments.</summary>
        private static Vector3 OntoHand(Vector3 flying, Transform? hand, float left)
        {
            if (hand == null || left > HandBlend) return flying;
            return Vector3.Lerp(flying, hand.position, Mathf.SmoothStep(0f, 1f, 1f - left / HandBlend));
        }
    }
}
