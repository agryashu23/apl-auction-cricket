using UnityEngine;

namespace Apl.Client.Audio
{
    /// <summary>
    /// The player's sound levels, kept across launches (round 9: levels, not only
    /// on/off): MASTER, MUSIC (anthem, menu and ground music), VOICE (commentary,
    /// auctioneer, the PA), EFFECTS (bat, ball, stumps, gavel, players' calls) and
    /// CROWD (the bed, the roar, applause and chants). Each is a level 0–1 (owner v26: a
    /// slider, whole percent; the old five steps OFF … 100% still map onto it). MusicOn / VoiceOn stay for the old switches (OFF ⇔ 0).
    /// </summary>
    public static class AudioPrefs
    {
        public enum Channel { Master, Music, Voice, Effects, Crowd }

        /// <summary>The steps a level can take, and their labels.</summary>
        public static readonly float[] Steps = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        public static readonly string[] StepLabels = { "OFF", "25%", "50%", "75%", "100%" };

        private static readonly float?[] Cache = new float?[5];

        private static string Key(Channel c) => "apl.vol." + c.ToString().ToLowerInvariant();

        /// <summary>A channel's level, 0–1 (default 100%; the old MUSIC / VOICE OFF switches carry over as 0).</summary>
        public static float Level(Channel c)
        {
            var i = (int)c;
            if (Cache[i] is float v) return v;
            var legacy = c == Channel.Music ? "apl.music" : c == Channel.Voice ? "apl.voice" : null;
            var fallback = legacy != null && PlayerPrefs.GetInt(legacy, 1) == 0 ? 0f : 1f;
            return (Cache[i] = Mathf.Clamp01(PlayerPrefs.GetFloat(Key(c), fallback))).Value;
        }

        public static void SetLevel(Channel c, float level) => SetLevel(c, level, true);

        /// <summary>
        /// A level now; <paramref name="save"/> false is the slider's live preview while the finger
        /// moves (the scene hears it at once, the disk write waits for the finger to lift).
        /// </summary>
        public static void SetLevel(Channel c, float level, bool save)
        {
            Cache[(int)c] = Mathf.Clamp01(level);
            if (c == Channel.Master) AudioListener.volume = Cache[(int)c]!.Value;
            if (!save) return;
            PlayerPrefs.SetFloat(Key(c), Cache[(int)c]!.Value);
            PlayerPrefs.Save();
        }

        /// <summary>The nearest step for a settings row.</summary>
        public static int StepOf(Channel c)
        {
            var v = Level(c);
            var best = 0;
            for (var i = 1; i < Steps.Length; i++) if (Mathf.Abs(Steps[i] - v) < Mathf.Abs(Steps[best] - v)) best = i;
            return best;
        }

        public static void SetStep(Channel c, int step) => SetLevel(c, Steps[Mathf.Clamp(step, 0, Steps.Length - 1)]);

        /// <summary>Puts MASTER on the listener (the scene's sounds read the rest each frame).</summary>
        public static void Apply() => AudioListener.volume = Level(Channel.Master);

        public static float Music => Level(Channel.Music);
        public static float Voice => Level(Channel.Voice);
        public static float Effects => Level(Channel.Effects);
        public static float Crowd => Level(Channel.Crowd);

        public static bool MusicOn
        {
            get => Music > 0f;
            set => SetLevel(Channel.Music, value ? Mathf.Max(Music, 1f) : 0f);
        }

        public static bool VoiceOn
        {
            get => Voice > 0f;
            set => SetLevel(Channel.Voice, value ? Mathf.Max(Voice, 1f) : 0f);
        }
    }
}
