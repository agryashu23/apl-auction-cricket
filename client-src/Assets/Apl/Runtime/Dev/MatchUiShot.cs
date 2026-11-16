using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apl.Client.Match;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Dev
{
    /// <summary>
    /// The match screens of owner v26 #19 / #20 on film: run with
    /// <c>-aplshots &lt;dir&gt; -aplmatchtest -aplmatchui &lt;dir2&gt;</c> (the normal match test plays
    /// itself; this rides along). About 45 s into the chase it pauses and shoots, into dir2:
    /// the scorecard on every tab (2nd innings BATTING / BOWLING / SUMMARY, then the 1st
    /// innings' three), the wagon wheel explorer (ALL, one batter, the 1st innings tab), a
    /// demo wheel with every kind of shot (ShowWagons with made-up shots: sixes' arcs, fours,
    /// all eight regions), the SOUND sliders from the pause list; then it resumes. (The
    /// end-of-over OVER IN TRACKING view is shot by the -aplshots run itself: "moment_over_tracking",
    /// AutoShot.Moment from MatchDirector.OverTracking.cs.) The log (matchui.txt) lists each click and any error.
    /// </summary>
    public sealed class MatchUiShot : MonoBehaviour
    {
        private string _dir = "";
        private int _shot;
        private StreamWriter _log = null!;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-aplmatchui");
            if (i < 0 || i + 1 >= args.Length || FindAnyObjectByType<MatchUiShot>() != null) return;
            var go = new GameObject("MatchUiShot");
            DontDestroyOnLoad(go);
            go.AddComponent<MatchUiShot>()._dir = args[i + 1];
        }

        private void Start()
        {
            Directory.CreateDirectory(_dir);
            _log = new StreamWriter(Path.Combine(_dir, "matchui.txt")) { AutoFlush = true };
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type == LogType.Exception || type == LogType.Error)
                    _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] {type}: {message}\n{stack}");
            };
            StartCoroutine(Run());
        }

        private IEnumerator Shot(string name, float after = 0.8f)
        {
            yield return new WaitForSecondsRealtime(after);
            var file = Path.Combine(_dir, $"{++_shot:000}_{name}.png");
            ScreenCapture.CaptureScreenshot(file);
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] shot {file}");
            yield return null;
        }

        private bool Click(string name)
        {
            var b = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == name && x.interactable && x.gameObject.activeInHierarchy);
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] click {name}: {(b != null ? "ok" : "MISSING")}");
            b?.onClick.Invoke();
            return b != null;
        }

        private static void Close(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) Destroy(go);
        }

        private IEnumerator Run()
        {
            var end = Time.realtimeSinceStartup + 240f;
            while (FindAnyObjectByType<MatchDirector>() == null && Time.realtimeSinceStartup < end) yield return new WaitForSecondsRealtime(0.5f);
            var hud = FindAnyObjectByType<MatchHud>();
            if (hud == null)
            {
                _log.WriteLine("no match HUD: nothing to shoot");
                yield break;
            }
            // Into the chase, then long enough for a few scoring shots.
            yield return new WaitForSecondsRealtime(45f);
            for (var tries = 0; tries < 40 && !hud.Paused; tries++)
            {
                if (!Click("Pause")) yield return new WaitForSecondsRealtime(1.5f);
                else yield return new WaitForSecondsRealtime(0.5f);
            }
            if (!hud.Paused)
            {
                _log.WriteLine("couldn't pause");
                yield break;
            }
            yield return Shot("pause_list", 0.6f);
            if (Click("Pause_Sound"))
            {
                yield return Shot("sound_sliders", 0.6f);
                Close("SoundSettings");
            }
            Click("Pause_Match");
            yield return Shot("pause_match", 0.6f);
            // The scorecard, every tab.
            if (Click("Tool_SCORECARD"))
            {
                foreach (var inn in new[] { "ScoreTab_Inn2", "ScoreTab_Inn1" })
                {
                    Click(inn);
                    foreach (var tab in new[] { "ScoreTab_Batting", "ScoreTab_Bowling", "ScoreTab_Summary" })
                    {
                        Click(tab);
                        yield return Shot($"scorecard_{inn.Substring(9).ToLowerInvariant()}_{tab.Substring(9).ToLowerInvariant()}", 0.5f);
                    }
                }
                Click("ScoreTab_Batting");
                Click("CloseScorecard");
            }
            // The wagon wheel explorer: ALL, one batter, the other innings.
            if (Click("Tool_WAGON WHEEL"))
            {
                yield return Shot("wagon_all", 1.8f);
                if (Click("Batter1")) yield return Shot("wagon_batter", 1.8f);
                if (Click("Inns1")) yield return Shot("wagon_innings1", 1.8f);
                Click("CloseWagon");
            }
            // A demo wheel: every kind of shot round the ground, both hands.
            var demo = new List<WagonShot>();
            var rng = new System.Random(7);
            for (var i = 0; i < 26; i++)
            {
                var runs = new[] { 1, 1, 1, 2, 2, 3, 4, 4, 6 }[rng.Next(9)];
                var angle = (float)(rng.NextDouble() * 340 - 170);
                demo.Add(new WagonShot(angle, runs >= 4 ? 1f : (float)(0.25 + rng.NextDouble() * 0.6), runs));
            }
            hud.ShowWagons(new[] { ((IReadOnlyList<WagonShot>)demo, false, "DEMO  ·  RIGHT-HANDER", "26 SHOTS") }, 0);
            yield return Shot("wagon_demo", 2.2f);
            hud.ShowWagons(new[]
            {
                ((IReadOnlyList<WagonShot>)demo.Take(13).ToList(), false, "TWO WHEELS  ·  RH", "13 SHOTS"),
                ((IReadOnlyList<WagonShot>)demo.Skip(13).Select(s => new WagonShot(s.Angle, s.Reach, s.Runs, true)).ToList(), true, "TWO WHEELS  ·  LH", "13 SHOTS"),
            }, 0);
            yield return Shot("wagon_demo_pair", 2.2f);
            hud.HideWagons();
            Click("Back");
            yield return new WaitForSecondsRealtime(0.3f);
            Click("Resume");
            _log.WriteLine($"DONE matchui shots={_shot}");
        }
    }
}
