using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>Staged poses for close-ups of the cast.</summary>
    public sealed partial class CricketCast
    {
        /// <summary>
        /// The bowler on his intro card: the ball-polish loop from its upright half
        /// (frame 57 of 126 on): the first half rubs the ball low on the thigh with
        /// the head bowed, which on a 30° close-up read as a broken pose (rating r7,
        /// `163`). The card is up ~2.4 s, so the bowed half never comes round.
        /// Falls back to the plain stand.
        /// </summary>
        public static void BowlerIntroPose(Animator? a, bool leftArm)
        {
            if (a == null) return;
            var polish = (leftArm ? "la_" : "ra_") + "intro_polish_ball_loop";
            a.speed = 1f;
            if (Body.Has(a, polish)) a.CrossFadeInFixedTime(polish, 0.3f, 0, 57f / 30f); // start 1.9 s in (30 fps)
            else if (Body.Has(a, "Stand")) a.CrossFadeInFixedTime("Stand", 0.3f);
        }
    }
}
