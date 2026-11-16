using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Buzzes on the phone: bat on ball (stronger the better it was timed, a tick for
    /// an edge), a wicket, a six. Android gets short shaped pulses through the
    /// Vibrator service (VibrationEffect on Android 8+); anything else falls back to
    /// Handheld.Vibrate (one fixed buzz). Off in the pause menu (BattingPrefs.Haptics).
    /// </summary>
    public static class Haptics
    {
        /// <summary>Pulse lengths (ms) and strengths (1–255).</summary>
        public const int PerfectMs = 45, GoodMs = 30, EdgeMs = 14, WicketMs = 140, SixMs = 90;

        private static AndroidJavaObject? _vibrator;
        private static int _sdk = -1;

        public static void Hit(Timing t, bool thin)
        {
            if (thin) Pulse(EdgeMs, 70);
            else if (t == Timing.Perfect) Pulse(PerfectMs, 255);
            else if (t == Timing.Good) Pulse(GoodMs, 170);
            else Pulse(20, 100);
        }

        public static void Wicket() => Pulse(WicketMs, 255);

        public static void Six() => Pulse(SixMs, 220);

        /// <summary>One pulse of <paramref name="ms"/> at <paramref name="amplitude"/> (1–255).</summary>
        public static void Pulse(int ms, int amplitude)
        {
            if (!BattingPrefs.Haptics) return;
            if (Application.platform == RuntimePlatform.Android && AndroidPulse(ms, amplitude)) return;
            // Handheld only exists in the mobile players (the Windows test build has no such class).
#if UNITY_ANDROID || UNITY_IOS
            if (Application.isMobilePlatform) Handheld.Vibrate();
#endif
        }

        private static bool AndroidPulse(int ms, int amplitude)
        {
            try
            {
                if (_vibrator == null)
                {
                    using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    using var version = new AndroidJavaClass("android.os.Build$VERSION");
                    _sdk = version.GetStatic<int>("SDK_INT");
                }
                if (_vibrator == null) return false;
                if (_sdk >= 26)
                {
                    using var effects = new AndroidJavaClass("android.os.VibrationEffect");
                    using var effect = effects.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, Mathf.Clamp(amplitude, 1, 255));
                    _vibrator.Call("vibrate", effect);
                }
                else _vibrator.Call("vibrate", (long)ms);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }
}
