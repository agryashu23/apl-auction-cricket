using System;

namespace Apl.Core.Play
{
    /// <summary>
    /// What the pitch does to the ball (owner v26 #23: "every ground's pitch looks the same").
    /// The four types the season engine already knows (<see cref="Pitch"/>; the quick match picks
    /// one per game from the ground's weights, <see cref="Venue.PickPitch"/>) now play differently
    /// once the ball lands — no dice, so every other roll in the match is untouched:
    /// GREEN (SeamFriendly): the seamers get movement off the deck and a little extra bounce,
    /// the spinners less grip. DRY (SpinFriendly): the spinners turn it more and the cutters grip;
    /// the quick men's bounce is lower and slower. FLAT (a belter): less of everything for the
    /// bowlers, true bounce. BALANCED: as it was.
    /// </summary>
    public static class PitchEffect
    {
        /// <summary>The factors for a pitch: off-the-pitch movement for pace and spin, swing, and bounce for pace and spin.</summary>
        public static (double seam, double turn, double swing, double paceBounce, double spinBounce) Factors(Pitch p) => p switch
        {
            Pitch.SeamFriendly => (1.8, 0.8, 1.15, 1.08, 0.97),
            Pitch.SpinFriendly => (0.8, 1.35, 1.0, 0.93, 1.03),
            Pitch.Flat => (0.7, 0.75, 0.85, 1.0, 0.97),
            _ => (1, 1, 1, 1, 1),
        };

        /// <summary>The ball after it meets this pitch (changes <paramref name="d"/> and returns it).</summary>
        public static Delivery Apply(Delivery d, Pitch p)
        {
            var (seam, turn, swing, paceBounce, spinBounce) = Factors(p);
            if (d.IsSpin)
            {
                d.Deviation *= turn;
                d.BounceFactor *= spinBounce;
                return d;
            }
            // A cutter grips like a slow spinner: on a dry pitch it bites (the turn), on a green one it's the seam.
            var cutter = d.Variation == Variation.OffCutter || d.Variation == Variation.LegCutter;
            d.Deviation *= cutter && p == Pitch.SpinFriendly ? 1.4 : seam;
            d.Swing *= swing;
            d.BounceFactor *= paceBounce;
            return d;
        }

        /// <summary>The pre-match line: "GREEN · SEAMING PITCH", and what it means for the bowlers.</summary>
        public static (string name, string note) Describe(Pitch p) => p switch
        {
            Pitch.SeamFriendly => ("GREEN · SEAMING PITCH", "Grass on it: seam movement and bounce for the quicks"),
            Pitch.SpinFriendly => ("DRY · TURNING PITCH", "Cracks and rough: the spinners will turn it, the pace dies"),
            Pitch.Flat => ("FLAT · BATTING PITCH", "Hard and true: a belter, the bowlers get little help"),
            _ => ("BALANCED · WORN PITCH", "A bit for everyone: true bounce, some seam, some grip"),
        };
    }

    public sealed partial class QuickMatch
    {
        /// <summary>Test runs only (-aplpitch): every match made from now on is played on this pitch.</summary>
        [ThreadStatic] public static Pitch? ForcePitch;

        /// <summary>
        /// The next match made gets this pitch (one-shot): the full match made after the toss keeps the
        /// pitch the toss was decided on (the director's quick chase at Start picked it).
        /// </summary>
        [ThreadStatic] public static Pitch? NextPitch;

        /// <summary>The ground's pick, unless a test run or the toss's match has already said (no extra dice).</summary>
        private static Pitch PitchFor(Pitch picked)
        {
            var p = NextPitch ?? ForcePitch ?? picked;
            NextPitch = null;
            return p;
        }

        /// <summary>The pending ball meets this match's pitch (both the AI's and the user's: Chase.cs, MatchFlow.cs).</summary>
        private Delivery OnPitch(Delivery d) => PitchEffect.Apply(d, Pitch);
    }
}
