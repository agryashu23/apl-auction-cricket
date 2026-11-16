using System.Diagnostics;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Test runs' frame-stall detective (round 13: a 23.6 s frame during a run-out's wicket package).
    /// <see cref="Mark"/> leaves a breadcrumb (the last 24 are kept, with the real time); <see cref="Start"/> /
    /// <see cref="End"/> time a step and log "[APL match] SLOW &lt;step&gt;: N ms" over 50 ms. The HUD's watchdog
    /// prints <see cref="Recent"/> with every FRAME STALL, and <see cref="StallWatch"/>'s thread prints it WHILE
    /// the main thread is stuck, so the next run names the step that stalled.
    /// Off (no cost) outside test runs.
    /// </summary>
    public static class StallProbe
    {
        public static bool On = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-aplshots") >= 0;

        private static readonly string[] Marks = new string[24];
        private static int _next;

        // The watcher thread reads the marks while the main thread may be writing them.
        private static readonly object Lock = new object();

        public static void Mark(string what)
        {
            if (!On) return;
            var line = $"{Time.realtimeSinceStartup:0.00}s f{Time.frameCount} {what}";
            lock (Lock)
            {
                Marks[_next] = line;
                _next = (_next + 1) % Marks.Length;
            }
            StallWatch.Marked(line);
        }

        /// <summary>A frequent step (the test captures): named to the live watcher only, not kept in the trail.</summary>
        public static void Note(string what)
        {
            if (On) StallWatch.Noted($"{Time.realtimeSinceStartup:0.00}s f{Time.frameCount} {what}");
        }

        public static long Start() => On ? Stopwatch.GetTimestamp() : 0;

        public static void End(string what, long t0)
        {
            if (!On || t0 == 0) return;
            var ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            if (ms > 50) UnityEngine.Debug.LogWarning($"[APL match] SLOW {what}: {ms:0} ms");
        }

        /// <summary>The breadcrumbs, oldest first (any thread).</summary>
        public static string Recent()
        {
            var s = "";
            lock (Lock)
            {
                for (var i = 0; i < Marks.Length; i++)
                {
                    var m = Marks[(_next + i) % Marks.Length];
                    if (m != null) s += "\n    " + m;
                }
            }
            return s;
        }
    }
}
