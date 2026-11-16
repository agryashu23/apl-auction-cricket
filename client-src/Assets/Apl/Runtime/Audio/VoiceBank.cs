using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Audio
{
    /// <summary>
    /// The pre-generated voice library (tools/gen_voice.ps1): Arjun the
    /// auctioneer and Kajal the assistant, one clip per line, loaded from
    /// Resources/Voice/&lt;voice&gt;/&lt;key&gt;.
    /// </summary>
    public sealed class VoiceBank
    {
        private readonly Dictionary<string, AudioClip?> _cache = new Dictionary<string, AudioClip?>();

        public AudioClip? Get(string voice, string key)
        {
            var path = $"Voice/{voice}/{key}";
            if (!_cache.TryGetValue(path, out var clip))
            {
                clip = Resources.Load<AudioClip>(path);
                if (clip == null) Debug.LogWarning($"Voice clip missing: {path}");
                _cache[path] = clip;
            }
            return clip;
        }

        /// <summary>Keys that say an amount: one clip up to ₹35 Cr, crore + lakh pieces above.</summary>
        public IEnumerable<string> AmountKeys(int lakhs)
        {
            if (Resources.Load<AudioClip>($"Voice/arjun/amount_{lakhs}") != null)
            {
                yield return $"amount_{lakhs}";
                yield break;
            }
            var crore = lakhs / 100;
            var lakh = lakhs % 100;
            if (crore > 0) yield return $"crore_{crore}";
            if (lakh > 0) yield return $"lakh_{(lakh / 5) * 5}";
        }
    }

    /// <summary>One line in the queue: which voice, which clip, pause after.</summary>
    public readonly struct VoiceLine
    {
        public VoiceLine(string voice, string key, float gapAfter = 0.06f)
        {
            Voice = voice;
            Key = key;
            GapAfter = gapAfter;
        }

        public string Voice { get; }
        public string Key { get; }
        public float GapAfter { get; }
    }
}
