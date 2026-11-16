using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Apl.Client.Match
{
    /// <summary>
    /// Test runs' live stall detective (TEC-1, the 23.6 s run-out freeze). The main thread beats
    /// several times a frame (<see cref="StallTick"/>, <see cref="StallTickLate"/>: frame start,
    /// scripts' Updates done, coroutines + animation done, LateUpdates done, rendered); a background
    /// thread checks every 100 ms. Main thread silent for over 3 s: it logs "[APL stall] MAIN THREAD
    /// STUCK" WHILE it is stuck — the frame phase it's stuck after, the last breadcrumb
    /// (<see cref="StallProbe.Mark"/>) and its age, GC counts, memory — again every 10 s, and once
    /// more when it comes back. If the watcher's OWN sleep overran too, the whole process (or the
    /// machine: paging, an antivirus scan) was frozen, not a game loop — it says so.
    /// Written to &lt;-aplshots dir&gt;/stall_watch.txt as well as the player log.
    /// Off (no thread) outside test runs.
    /// </summary>
    public static class StallWatch
    {
        private const double StuckAfter = 3.0, RepeatEvery = 10.0, MemEvery = 30.0;

        private static Thread? _thread;
        private static StreamWriter? _file;
        private static readonly object FileLock = new object();

        // Written by the main thread, read by the watcher.
        private static long _beat;
        private static volatile string _phase = "-";
        private static volatile int _frame;
        private static long _markAt;
        private static volatile string _mark = "-";

        private static double Secs(long ticks) => ticks / (double)Stopwatch.Frequency;

        /// <summary>Start the watcher (once; only when the probe is on).</summary>
        public static void Ensure()
        {
            if (!StallProbe.On || _thread != null) return;
            try
            {
                var args = Environment.GetCommandLineArgs();
                var i = Array.IndexOf(args, "-aplshots");
                if (i >= 0 && i + 1 < args.Length)
                {
                    Directory.CreateDirectory(args[i + 1]);
                    _file = new StreamWriter(Path.Combine(args[i + 1], "stall_watch.txt"), true) { AutoFlush = true };
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[APL stall] no stall_watch.txt: {e.Message}");
            }
            Beat("start", 0);
            StallTick.Create();
            _thread = new Thread(Watch) { IsBackground = true, Name = "APL stall watch", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
            Log($"watching (main thread silent > {StuckAfter:0} s is reported live)");
        }

        /// <summary>Main thread: alive, in <paramref name="phase"/> of frame <paramref name="frame"/>.</summary>
        public static void Beat(string phase, int frame)
        {
            _phase = phase;
            _frame = frame;
            Interlocked.Exchange(ref _beat, Stopwatch.GetTimestamp());
        }

        /// <summary>A breadcrumb was left (StallProbe.Mark).</summary>
        public static void Marked(string what)
        {
            _mark = what;
            Interlocked.Exchange(ref _markAt, Stopwatch.GetTimestamp());
        }

        private static long _noteAt;
        private static volatile string _note = "-";

        /// <summary>A frequent step was entered (StallProbe.Note: the test captures).</summary>
        public static void Noted(string what)
        {
            _note = what;
            Interlocked.Exchange(ref _noteAt, Stopwatch.GetTimestamp());
        }

        private static void Log(string line, bool error = false)
        {
            var text = "[APL stall] " + line;
            lock (FileLock)
            {
                try { _file?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {text}"); }
                catch { /* the log must never take the watcher down */ }
            }
            if (error) UnityEngine.Debug.LogError(text);
            else UnityEngine.Debug.Log(text);
        }

        private static string Memory()
        {
            var gc = $"GC gen0/1/2 {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}, managed {GC.GetTotalMemory(false) / 1048576} MB";
            try
            {
                using var p = Process.GetCurrentProcess();
                return $"{gc}, working set {p.WorkingSet64 / 1048576} MB, private {p.PrivateMemorySize64 / 1048576} MB";
            }
            catch
            {
                return gc;
            }
        }

        private static void Watch()
        {
            var last = Stopwatch.GetTimestamp();
            var lastMem = last;
            var stuckSince = 0L;
            var reported = 0.0;
            var frozenToo = 0.0;
            var gc2AtStuck = 0;
            var lastGcs = 0;
            while (true)
            {
                Thread.Sleep(100);
                var now = Stopwatch.GetTimestamp();
                // The watcher itself slept far past its 100 ms: the process (or the machine) was frozen.
                var own = Secs(now - last) - 0.1;
                last = now;
                // A garbage collection stops every managed thread (this one too): told apart by the GC counts.
                var gcs = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
                if (own > 1.5)
                {
                    frozenToo += own;
                    var why = gcs != lastGcs ? "a GARBAGE COLLECTION ran in it (a GC pause: look for a huge or growing heap)" : "no GC ran: the machine (paging, disk, antivirus, another heavy process), not the game's code";
                    Log($"the WHOLE PROCESS was frozen {own:0.0} s (the watcher's own thread too): {why}; {Memory()}", true);
                }
                lastGcs = gcs;
                if (Secs(now - lastMem) >= MemEvery)
                {
                    lastMem = now;
                    Log($"mem: {Memory()}");
                }
                var beat = Interlocked.Read(ref _beat);
                var silent = Secs(now - beat);
                if (silent < StuckAfter)
                {
                    if (stuckSince != 0)
                    {
                        Log($"main thread BACK after {Secs(now - stuckSince) + StuckAfter:0.0} s (frame {_frame}, now in '{_phase}'); "
                            + $"gen2 GCs meanwhile {GC.CollectionCount(2) - gc2AtStuck}; process-wide freeze within it {frozenToo:0.0} s; {Memory()}", true);
                        stuckSince = 0;
                    }
                    frozenToo = 0;
                    continue;
                }
                if (stuckSince == 0)
                {
                    stuckSince = now;
                    reported = -RepeatEvery;
                    gc2AtStuck = GC.CollectionCount(2);
                }
                var stuck = silent;
                if (stuck - reported < RepeatEvery) continue;
                reported = stuck;
                var markAge = Secs(now - Interlocked.Read(ref _markAt));
                Log($"MAIN THREAD STUCK {stuck:0.0} s in frame {_frame}, after phase '{_phase}' ({PhaseHint(_phase)}); "
                    + $"last step '{_mark}' ({markAge:0.0} s ago); last capture '{_note}' ({Secs(now - Interlocked.Read(ref _noteAt)):0.0} s ago); "
                    + $"{Memory()}; last steps:{StallProbe.Recent()}", true);
            }
        }

        /// <summary>Where in the frame the main thread is, given the last beat it gave.</summary>
        private static string PhaseHint(string phase) => phase switch
        {
            "frame start" => "inside some script's Update",
            "updates done" => "inside a coroutine step (yield null / WaitFor…) or the animation update",
            "late start" => "inside some script's LateUpdate",
            "late done" => "in rendering / the frame's end",
            "fixed step" => "inside some script's FixedUpdate or the physics step",
            "rendered" => "between frames: the physics step, input, or the frame's first scripts",
            _ => "before the first frame",
        };
    }
}
