using System;

namespace Apl.Core.Play
{
    /// <summary>How quick a bowler is: the pace bowlers by their bowling rating, and the spinners.</summary>
    public enum PaceClass { Express, Fast, FastMedium, Medium, SlowMedium, FingerSpin, WristSpin }

    public static partial class Bowling
    {
        /// <summary>Pace bowlers by rating: 85+ express, 77+ fast, 68+ fast-medium, 58+ medium, else slow-medium; a known
        /// fast-medium or medium style caps the class (bowlStyle in the dataset).</summary>
        public static PaceClass PaceOf(Player bowler)
        {
            if (bowler.BowlType == BowlType.Spin) return KindFor(bowler) == DeliveryKind.LegSpin ? PaceClass.WristSpin : PaceClass.FingerSpin;
            var b = bowler.Bowling;
            var byRating = b >= 85 ? PaceClass.Express : b >= 77 ? PaceClass.Fast : b >= 68 ? PaceClass.FastMedium : b >= 58 ? PaceClass.Medium : PaceClass.SlowMedium;
            // A known medium-pacer is never "express" however good he is: the style caps the class.
            var cap = bowler.BowlStyle switch { "medium" => PaceClass.Medium, "fast-medium" => PaceClass.FastMedium, _ => PaceClass.Express };
            return (PaceClass)Math.Max((int)byRating, (int)cap);
        }

        /// <summary>His stock-ball speed range (km/h): where he sits in it goes with his rating inside the class.</summary>
        public static (double lo, double hi) SpeedRange(PaceClass c) => c switch
        {
            PaceClass.Express => (144, 153),
            PaceClass.Fast => (137, 145),
            PaceClass.FastMedium => (130, 138),
            PaceClass.Medium => (122, 130),
            PaceClass.SlowMedium => (112, 121),
            PaceClass.FingerSpin => (82, 93),
            _ => (76, 88),
        };

        /// <summary>The rating band each class covers (for where in the speed range he sits).</summary>
        private static (int lo, int hi) RatingBand(PaceClass c) => c switch
        {
            PaceClass.Express => (85, 100),
            PaceClass.Fast => (77, 85),
            PaceClass.FastMedium => (68, 77),
            PaceClass.Medium => (58, 68),
            PaceClass.SlowMedium => (30, 58),
            _ => (30, 95),
        };

        /// <summary>
        /// His stock speed for one ball: the class's range by his rating inside the
        /// class, ±2 km/h (spinners ±1.5) ball to ball.
        /// </summary>
        public static double StockSpeed(Player bowler, Rng rng)
        {
            var c = PaceOf(bowler);
            var (lo, hi) = SpeedRange(c);
            var (rlo, rhi) = RatingBand(c);
            var k = DartMath.Clamp((bowler.Bowling - rlo) / (double)Math.Max(1, rhi - rlo), 0, 1);
            var spin = c == PaceClass.FingerSpin || c == PaceClass.WristSpin;
            return lo + (hi - lo) * k + rng.Range(-1, 1) * (spin ? 1.5 : 2);
        }

        /// <summary>The release meter: |error| inside this is PERFECT (the planned ball, full movement); 1 is the edge of the green.</summary>
        public const double PerfectRelease = 0.35;

        /// <summary>
        /// The meter's zones (round 13, owner: "tap-to-release zones … if in red, make it a no-ball"), in
        /// release error: green |e| ≤ 1 (PERFECT in its middle), orange to ±<see cref="OrangeEdge"/> (a
        /// weaker ball), then early: dark (a floated, loose ball); late past <see cref="NoBallRelease"/>:
        /// RED — he's over-stridden and overstepped: a NO-BALL, every time.
        /// </summary>
        public const double OrangeEdge = 2.0, NoBallRelease = 2.0;

        /// <summary>The release graded like a shot: PERFECT, GOOD (the green), EARLY / LATE (orange), and well off (VERY; late, that's the red no-ball).</summary>
        public static Timing ReleaseGrade(double e)
        {
            var a = Math.Abs(e);
            if (a <= PerfectRelease) return Timing.Perfect;
            if (a <= 1) return Timing.Good;
            if (a <= OrangeEdge) return e < 0 ? Timing.Early : Timing.Late;
            return e < 0 ? Timing.VeryEarly : Timing.VeryLate;
        }

        /// <summary>A release in the meter's red: overstepped, a no-ball.</summary>
        public static bool ReleaseNoBall(double e) => e > NoBallRelease;

        /// <summary>Share of the swing / spin a release keeps: all of it in the PERFECT band, down to 30% way off.</summary>
        public static double MovementKept(double e) => 1 - 0.7 * DartMath.Clamp((Math.Abs(e) - PerfectRelease) / 2.0, 0, 1);

        /// <summary>A pace bouncer's extra bounce, and the height (m, at the batter's crease) over which it's a no-ball.</summary>
        public const double BouncerLift = 1.2, HeadHeight = 2.1;

        /// <summary>Pace from the length he's going for: the effort ball (bouncer) a touch quicker, the yorker a touch slower.</summary>
        public static double LengthPace(Length l, bool spin) => spin ? 0 : l == Length.Bouncer ? 3 : l == Length.Yorker ? -2 : 0;

        /// <summary>"Express", "Fast-medium", … for cards.</summary>
        public static string PaceName(PaceClass c) => c switch
        {
            PaceClass.FastMedium => "Fast-medium",
            PaceClass.SlowMedium => "Slow-medium",
            PaceClass.FingerSpin => "Finger spin",
            PaceClass.WristSpin => "Wrist spin",
            _ => c.ToString(),
        };

        /// <summary>The run-up's pace against a fast-medium bowler's (1): express men charge in, medium pacers jog.</summary>
        public static double RunUpPace(PaceClass c) => c switch
        {
            PaceClass.Express => 1.3,
            PaceClass.Fast => 1.15,
            PaceClass.FastMedium => 1.0,
            PaceClass.Medium => 0.88,
            PaceClass.SlowMedium => 0.8,
            // Spinners walk in at their own pace (their clips' run-ups are short already).
            _ => 1.0,
        };
    }
}
