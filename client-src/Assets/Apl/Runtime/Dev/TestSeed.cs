using System;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// TEST RUNS ONLY: <c>-aplseed N</c> (with <c>-aplmatchtest</c>) fixes the match seed, so a before/after pair of
    /// runs plays from the same engine and visual random draws (area 3 review). The one call site is
    /// MatchDirector.cs, where the seed is taken (<c>var seed = TestSeed.Or(clock)</c>). Without both flags the clock's
    /// seed is returned untouched (a real match never changes). Frame timing can still drift a run apart.
    /// </summary>
    public static class TestSeed
    {
        public static long Or(long clock)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-aplseed");
            if (i < 0 || i + 1 >= args.Length || !args.Contains("-aplmatchtest") || !long.TryParse(args[i + 1], out var n)) return clock;
            Debug.Log($"[APL seed] match seed {n & 0x7FFFFFFF} (-aplseed)");
            return n & 0x7FFFFFFF;
        }
    }
}
