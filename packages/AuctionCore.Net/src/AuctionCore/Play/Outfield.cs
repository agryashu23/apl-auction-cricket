using System;

namespace Apl.Core.Play
{
    /// <summary>
    /// A fast T20 outfield and the fielders on it. Off the bat a ball along the
    /// ground loses some pace into the turf, then the grass slows it gently (a
    /// timed drive into a gap races to the rope in about three seconds). A fielder
    /// reacts, accelerates to a sprint, and stops it if he can get his hands to it
    /// — or dives at one going past him within a dive's length, which doesn't
    /// always come off. Deep fielders cover a lot of ground but can't catch a ball
    /// that's already past them and running away.
    /// </summary>
    public static class Outfield
    {
        /// <summary>Rolling deceleration (m/s²): the standard ground, a quick one, a slow one.</summary>
        public const double Standard = 1.5, Quick = 1.25, Slow = 1.75;

        /// <summary>Of the pace off the bat, what a hard-hit ball along the ground keeps after its first bounce into the turf.</summary>
        public const double Turf = 0.62;

        /// <summary>Off the bat it skims, low, at full pace for this long (s) before that first bounce (≈4 m for a firm drive).</summary>
        public const double SkimTime = 0.12;

        /// <summary>What the first bounce leaves of <paramref name="exitSpeed"/>: a soft push (under ~16 m/s) is already rolling and loses little, anything firmer digs in.</summary>
        public static double TurfFor(double exitSpeed) => 0.85 - (0.85 - Turf) * DartMath.Clamp((exitSpeed - 8) / 8, 0, 1);

        /// <summary>
        /// Fielders: reaction (s), acceleration (m/s²), sprint (m/s); reach on his feet
        /// (a step and a long arm, or the long barrier), and with a full-length dive (m).
        /// </summary>
        public const double Reaction = 0.25, Accel = 6.0, Sprint = 7.0, Hands = 1.2, Dive = 3.5;

        /// <summary>A dive that gets a hand to it but can't hold it: the pace the ball keeps as it's palmed on.</summary>
        public const double Deflect = 0.5;

        /// <summary>
        /// Picking it up cleanly, or getting up from a dive with it (s). Deep area 3 (dev 1's gather → set → throw,
        /// timed to the clips): 0.55 → 0.75 and 1.3 → 1.85, so the engine's ball-back time is what the picture shows.
        /// </summary>
        public const double Pickup = 0.75, DiveRecover = 1.85;

        /// <summary>The ground's outfield (the venue's own character), null: the standard oval.</summary>
        public static double DecelFor(Venue? venue) => venue?.Id switch
        {
            "seaface" or "gardencity" or "hooghly" or "brahmaputra" or "dhauladhar" or "oldfort" => Quick,
            "marina" or "awadh" or "pinkcity" => Slow,
            _ => Standard,
        };

        /// <summary>How far a fielder gets in <paramref name="t"/> s after the hit (react, accelerate, sprint).</summary>
        public static double Run(double t)
        {
            var tau = t - Reaction;
            if (tau <= 0) return 0;
            var tUp = Sprint / Accel;
            return tau < tUp ? 0.5 * Accel * tau * tau : 0.5 * Sprint * tUp + Sprint * (tau - tUp);
        }

        /// <summary>
        /// The odds a dive stops it: most do for a good side; the further he has to
        /// stretch and the harder it's going, the more slip through (palmed on) —
        /// a full-length dive at a rasping drive is about two in three.
        /// <paramref name="fielding"/> is the side's fielding (0–100, 70 typical).
        /// </summary>
        public static double DiveStops(double stretch, double ballSpeed, double fielding = 70) =>
            DartMath.Clamp(0.9 - 0.22 * DartMath.Clamp(stretch / (Dive - Hands), 0, 1) - Math.Max(0, ballSpeed - 15) * 0.008
                + (fielding - 70) * 0.003, 0.45, 0.95);
    }
}
