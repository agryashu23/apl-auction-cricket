using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apl.Client.Match;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// The bat-grip proof (round 14): <c>-aplbatgrip &lt;dir&gt; [all]</c>. The striker alone at the crease (the
    /// gallery set-up), right-handed then left-handed: the stance, one stroke per family (every stroke with
    /// <c>all</c>) at lift / downswing / contact / follow-through, the non-striker and backing-up loops, the
    /// ceremony bat clips (walk-outs, guard, settles, warm-ups), the running clips (one hand) and the bat
    /// celebrations — each held still, measured from the final bones and photographed close up from the front
    /// (bowler's end) and the off side into <c>&lt;dir&gt;/&lt;hand&gt;_&lt;clip&gt;_&lt;moment&gt;_front|side.png</c>.
    /// Every pose logs <c>[APL batgrip] clip=.. frame=.. moment=.. bottomHandGap=.. topHandGap=.. wristDeg=..
    /// bendDeg=.. elbowDeg=.. palm=.. index=.. pen=.. PASS|FAIL</c> to <c>&lt;dir&gt;/batgrip.txt</c> and the player log;
    /// the last lines are the per-hand totals. Add <c>-aploldgrip</c> for the round-10 grip (A/B).
    /// </summary>
    public sealed class BatGripCapture : MonoBehaviour
    {
        private string _dir = "";
        private bool _all;
        private StreamWriter _log = null!;
        private int _errors;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-aplbatgrip");
            if (i < 0 || i + 1 >= args.Length || FindAnyObjectByType<BatGripCapture>() != null) return;
            var go = new GameObject("BatGripCapture");
            DontDestroyOnLoad(go);
            var c = go.AddComponent<BatGripCapture>();
            c._dir = args[i + 1];
            c._all = i + 2 < args.Length && args[i + 2] == "all";
        }

        private void Start()
        {
            Directory.CreateDirectory(_dir);
            Application.runInBackground = true;
            _log = new StreamWriter(Path.Combine(_dir, "batgrip.txt")) { AutoFlush = true };
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type != LogType.Exception && type != LogType.Error) return;
                _errors++;
                _log.WriteLine($"{type}: {message}\n{stack}");
            };
            StartCoroutine(Run());
        }

        private void Line(string s)
        {
            _log.WriteLine(s);
            Debug.Log(s);
        }

        private IEnumerator Run()
        {
            MatchLaunch.MyTeam = "MBH";
            MatchLaunch.Opponent = "CHC";
            UnityEngine.SceneManagement.SceneManager.LoadScene(MatchLaunch.GroundScene);
            yield return new WaitForSecondsRealtime(3f);
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
            var mc = FindAnyObjectByType<MatchCamera>();
            var director = FindAnyObjectByType<MatchDirector>();
            if (mc == null || director == null)
            {
                Line("[APL batgrip] FAILED: no director in the ground scene");
                Application.Quit(2);
                yield break;
            }
            Line($"[APL batgrip] grip model: {(BatterRig.PalmGrip ? "palm (round 14)" : "old (round 10, -aploldgrip)")}");
            foreach (var lhb in new[] { false, true })
            {
                var rig = director.GalleryBatter(lhb);
                director.enabled = false;
                mc.enabled = false;
                var cam = mc.GetComponent<Camera>();
                var an = rig.GetComponent<Animator>();
                var hand = lhb ? "lhb" : "rhb";
                int pass = 0, total = 0;
                var fails = new List<string>();
                foreach (var (state, moments) in Poses(an, lhb))
                {
                    foreach (var (label, normalized) in moments)
                    {
                        rig.GalleryPose(state, normalized);
                        for (var w = 0; w < 4; w++) yield return null;
                        rig.GalleryPose(state, normalized);
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        var m = rig.MeasureGrip();
                        var len = an.GetCurrentAnimatorStateInfo(0).length;
                        var frame = Mathf.RoundToInt(normalized * len * 30f) + 1;
                        Line($"[APL batgrip] hand={hand} clip={state} frame={frame} moment={label} {m}");
                        total++;
                        if (m.Pass) pass++;
                        else fails.Add($"{state}@{label}");
                        // Close up on his hands: from the bowler's end and from the off side.
                        var hips = an.GetBoneTransform(HumanBodyBones.Hips);
                        var p = hips != null ? hips.position : rig.transform.position;
                        var off = lhb ? -1f : 1f;
                        var look = p + new Vector3(0, 0.1f, 0.1f);
                        foreach (var (view, at) in new[] { ("front", p + new Vector3(0.5f * off, 0.35f, 2.9f)), ("side", p + new Vector3(2.9f * off, 0.35f, 0.35f)) })
                        {
                            cam.transform.position = at;
                            cam.transform.LookAt(look);
                            cam.fieldOfView = 32f;
                            yield return new WaitForEndOfFrame();
                            CaptureWriter.Screen(Path.Combine(_dir, $"{hand}_{state}_{label}_{view}.png"));
                            yield return null;
                        }
                    }
                }
                Line($"[APL batgrip] TOTAL hand={hand} pass={pass}/{total} ({(total > 0 ? 100f * pass / total : 0f):0}%) fails: {string.Join(", ", fails.Take(60))}");
            }
            Line($"[APL batgrip] DONE errors={_errors}");
            _log.Flush();
            yield return new WaitForSecondsRealtime(1.5f);
            Application.Quit(_errors == 0 ? 0 : 2);
        }

        /// <summary>The animator states to hold and the moments (normalized time) of each, for one hand.</summary>
        private IEnumerable<(string state, (string label, float t)[] moments)> Poses(Animator an, bool lhb)
        {
            bool Has(string s) => an.HasState(0, Animator.StringToHash(s));
            string Shot(string n) => lhb && Has("lhb_" + n) ? "lhb_" + n : n;
            var prefix = lhb ? "lhb_" : "rhb_";
            var thirds = new[] { ("a", 0.2f), ("b", 0.5f), ("c", 0.8f) };
            foreach (var s in new[] { Shot("00_stance_idle_loop"), lhb ? "StanceLeft" : "StanceRight", "Stance" })
            {
                if (!Has(s)) continue;
                yield return (s, new[] { ("still", 0f), ("mid", 0.5f) });
                break;
            }
            var shots = _all
                ? CricketClips.Shots.ToList()
                : CricketClips.Shots.GroupBy(c => c.Family).Select(g => g.First()).ToList();
            foreach (var c in shots)
            {
                var state = Shot(c.Name);
                if (!Has(state) || c.Length <= 0f) continue;
                float N(float sec) => Mathf.Clamp01(sec / c.Length);
                yield return (state, new[] { ("lift", N(c.ContactTime - 0.45f)), ("down", N(c.ContactTime - 0.15f)), ("contact", N(c.ContactTime)), ("follow", N(c.ContactTime + 0.35f)) });
            }
            foreach (var n in new[] { "75_non_striker_idle_loop", "76_backing_up" })
                if (Has(Shot(n))) yield return (Shot(n), thirds);
            foreach (var n in new[] { "take_guard", "walk_out_bat_carried_loop", "walk_out_bat_tucked_loop", "settle_after_run_single", "settle_after_dive", "warmup_shadow_shots",
                         "run_loop_with_bat", "run_single_crease_to_crease", "dive_into_crease", "drag_bat_into_crease", "turn_180_after_run",
                         "bat_fifty_raise_bat", "bat_acknowledge_applause", "bat_winning_runs_celebration" })
            {
                if (Has(prefix + n)) yield return (prefix + n, thirds);
            }
        }
    }
}
