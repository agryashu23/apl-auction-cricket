using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>How fast the delivery plays on screen (the pace shown in km/h is always the real one).</summary>
    public enum DeliveryPace { Slow, Normal, Real }

    /// <summary>
    /// The batter's settings, kept across launches: how fast the ball comes down
    /// (a real 140 km/h ball is at the bat in under half a second — on a phone,
    /// from 60 m, that's unplayable, so by default the flight plays slower) and
    /// whether the foot is chosen for him from the length (AUTO, the default) or
    /// he picks FRONT / BACK himself.
    /// </summary>
    public static class BattingPrefs
    {
        private const string PaceKey = "apl.batting.pace";
        private const string FootKey = "apl.batting.autofoot";

        /// <summary>
        /// Share of real speed the flight plays at (trajectory seconds per real second).
        /// Normal: pace 0.72 — a 140 km/h ball takes ~0.62 s instead of 0.45 to reach the
        /// bat, the "good" window ±110 ms instead of ±80; spin 0.85 (it's slow already, and
        /// slowing it more makes the turn look floaty). Slow is for learning the timing.
        /// </summary>
        public const double PaceNormal = 0.72, SpinNormal = 0.85, PaceSlow = 0.6, SpinSlow = 0.72;

        private const string SwipeKey = "apl.batting.swipe";

        private const string HapticsKey = "apl.haptics";

        private static bool? _autoFoot, _swipe, _haptics;

        /// <summary>Buzz on hits, wickets and sixes (default on).</summary>
        public static bool Haptics
        {
            get => _haptics ??= PlayerPrefs.GetInt(HapticsKey, 1) == 1;
            set
            {
                _haptics = value;
                PlayerPrefs.SetInt(HapticsKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// LEGACY SWIPE controls: no stick, no foot buttons (the foot comes from the length);
        /// a swipe toward where he wants to hit IS the shot, its type picked on a 3-chip selector.
        /// Off: the buttons (and the aim stick).
        /// </summary>
        public static bool Swipe
        {
            get => _swipe ??= PlayerPrefs.GetInt(SwipeKey, 0) == 1;
            set
            {
                _swipe = value;
                PlayerPrefs.SetInt(SwipeKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// The ball speed, set by DIFFICULTY now (owner v26 #5: one setting, harder = faster ball):
        /// EASY plays the old SLOW, NORMAL the normal, HARD real pace. The old BALL SPEED pref
        /// ("apl.batting.pace") is only read once, by Difficulty, to migrate a saved choice.
        /// </summary>
        public static DeliveryPace Pace => Apl.Client.Modes.Difficulty.Current switch
        {
            Apl.Client.Modes.DifficultyLevel.Easy => DeliveryPace.Slow,
            Apl.Client.Modes.DifficultyLevel.Hard => DeliveryPace.Real,
            _ => DeliveryPace.Normal,
        };

        /// <summary>The old BALL SPEED key (migration only: Difficulty).</summary>
        public const string LegacyPaceKey = PaceKey;

        /// <summary>AUTO footwork: one tap per shot type, the foot read from the length.</summary>
        public static bool AutoFootwork
        {
            get => _autoFoot ??= PlayerPrefs.GetInt(FootKey, 1) == 1;
            set
            {
                _autoFoot = value;
                PlayerPrefs.SetInt(FootKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Trajectory seconds per real second for this delivery.</summary>
        public static double Factor(Delivery d) => Pace switch
        {
            DeliveryPace.Real => 1.0,
            DeliveryPace.Slow => d.IsSpin ? SpinSlow : PaceSlow,
            _ => d.IsSpin ? SpinNormal : PaceNormal,
        };

        public static string Label(DeliveryPace p) => p switch
        {
            DeliveryPace.Slow => "SLOW",
            DeliveryPace.Real => "REAL",
            _ => "NORMAL",
        };
    }
}
