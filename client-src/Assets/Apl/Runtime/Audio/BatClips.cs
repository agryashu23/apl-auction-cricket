using System;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Audio
{
    /// <summary>
    /// The recorded match sounds as pools of takes (tools/build_match_sfx.py →
    /// Resources/Sfx and Sfx/match): a pool's name lists its clips, the first
    /// found on disk are used, and a pick never repeats the pool's last take.
    /// Missing clips are skipped, so an older install still plays what it has.
    /// </summary>
    public static class BatClips
    {
        /// <summary>Pool → clip paths under Resources/Sfx.</summary>
        private static readonly Dictionary<string, string[]> Pools = new Dictionary<string, string[]>
        {
            // Round 13 (owner: "sounds like a steel bat"): three wooden kinds from the owner's knock
            // (tools/build_bat_r13.py); the old metallic-ringing bat_* takes are retired.
            ["edge"] = new[] { "bat/edge_1", "bat/edge_2", "bat/edge_3" },
            ["stroke"] = new[] { "bat/stroke_1", "bat/stroke_2", "bat/stroke_3" },
            ["loft"] = new[] { "bat/loft_1", "bat/loft_2", "bat/loft_3" },            ["pad"] = new[] { "match/pad_1", "match/pad_2", "match/pad_3", "match/pad_4", "match/pad_5" },
            ["glove"] = new[] { "match/glove_1", "match/glove_2", "match/glove_3", "match/glove_4", "match/glove_5" },
            ["bounce"] = new[] { "match/bounce_1", "match/bounce_2", "match/bounce_3", "match/bounce_4", "match/bounce_5" },
            ["rope"] = new[] { "match/rope_1", "match/rope_2", "match/rope_3", "match/rope_4", "match/rope_5" },
            ["stumps"] = new[] { "match/stumps_1", "match/stumps_2", "match/stumps_3", "match/stumps_4", "match/stumps_5" },
            ["bails"] = new[] { "match/bails_1", "match/bails_2", "match/bails_3", "match/bails_4" },
            // The DJ's stings at a boundary (the PA chime is gone for good — owner, r13).
            ["sting"] = new[] { "match/sting_3", "match/sting_4" },
        };

        private static readonly Dictionary<string, List<AudioClip>> Loaded = new Dictionary<string, List<AudioClip>>();
        private static readonly Dictionary<string, int> Last = new Dictionary<string, int>();

        /// <summary>How many takes a pool has installed (0: none — the caller's fallback plays).</summary>
        public static int Count(string pool, Func<string, AudioClip?> load) => Clips(pool, load).Count;

        /// <summary>A take from <paramref name="pool"/>, not the one played last; null if none is installed.</summary>
        public static AudioClip? Pick(string pool, Func<string, AudioClip?> load)
        {
            var clips = Clips(pool, load);
            if (clips.Count == 0) return null;
            var last = Last.TryGetValue(pool, out var l) ? l : -1;
            var i = UnityEngine.Random.Range(0, clips.Count);
            if (clips.Count > 1 && i == last) i = (i + 1 + UnityEngine.Random.Range(0, clips.Count - 1)) % clips.Count;
            Last[pool] = i;
            return clips[i];
        }

        private static List<AudioClip> Clips(string pool, Func<string, AudioClip?> load)
        {
            if (Loaded.TryGetValue(pool, out var list)) return list;
            list = new List<AudioClip>();
            if (Pools.TryGetValue(pool, out var names))
                foreach (var n in names)
                    if (load(n) is AudioClip c) list.Add(c);
            Loaded[pool] = list;
            return list;
        }
    }
}
