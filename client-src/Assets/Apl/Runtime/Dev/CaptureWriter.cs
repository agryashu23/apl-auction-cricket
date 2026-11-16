using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// Test captures written off the main thread. The r13 batcap runs froze the whole game for 34–75 s while
    /// their PNGs were written (File.WriteAllBytes / ScreenCapture.CaptureScreenshot on the main thread, with
    /// Windows Defender scanning each new file). Here the main thread only reads the pixels back and encodes
    /// the PNG; a background thread writes it, from a bounded queue (a full queue drops the shot and says so
    /// rather than block the game). Any single write over 1 s is logged as "[APL capture] slow write".
    /// </summary>
    public sealed class CaptureWriter : MonoBehaviour
    {
        private const int Capacity = 96;
        private static CaptureWriter? _host;
        private static BlockingCollection<(string file, byte[] png)>? _queue;
        private static Thread? _thread;

        private static void Start_()
        {
            if (_queue != null) return;
            _queue = new BlockingCollection<(string, byte[])>(new ConcurrentQueue<(string, byte[])>(), Capacity);
            _thread = new Thread(Drain) { IsBackground = true, Name = "APL capture writer", Priority = System.Threading.ThreadPriority.BelowNormal };
            _thread.Start();
        }

        private static void Drain()
        {
            foreach (var (file, png) in _queue!.GetConsumingEnumerable())
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    File.WriteAllBytes(file, png);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"[APL capture] write failed {file}: {e.Message}");
                }
                if (sw.ElapsedMilliseconds > 1000) UnityEngine.Debug.LogWarning($"[APL capture] slow write {sw.ElapsedMilliseconds} ms {file}");
            }
        }

        /// <summary>Hand an encoded PNG to the writer (never blocks: a full queue drops it, logged).</summary>
        public static void Write(string file, byte[] png)
        {
            Start_();
            if (!_queue!.TryAdd((file, png))) UnityEngine.Debug.LogWarning($"[APL capture] queue full ({Capacity}): dropped {file}");
        }

        /// <summary>The screen as it is at the end of this frame, encoded here and written off the main thread (replaces ScreenCapture.CaptureScreenshot).</summary>
        public static void Screen(string file)
        {
            if (_host == null)
            {
                var go = new GameObject("CaptureWriter");
                DontDestroyOnLoad(go);
                _host = go.AddComponent<CaptureWriter>();
            }
            _host.StartCoroutine(_host.Grab(file));
        }

        private IEnumerator Grab(string file)
        {
            yield return new WaitForEndOfFrame();
            Match.StallProbe.Note($"capture {Path.GetFileName(file)}");
            var t0 = Match.StallProbe.Start();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var png = tex.EncodeToPNG();
            Destroy(tex);
            Match.StallProbe.End("capture + encode", t0);
            Write(file, png);
        }

        private void OnApplicationQuit()
        {
            // Let what's queued reach the disk (the run's last shots), a few seconds at most.
            _queue?.CompleteAdding();
            _thread?.Join(5000);
        }
    }
}
