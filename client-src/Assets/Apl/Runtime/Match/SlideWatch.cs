using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Owner v26 #17 ("there must be no sliding for ANYONE"): the switch for the slide fixes, so one build
    /// measures both — <c>-aplnoslidefix</c> runs with every v26 slide fix off (the "before"), without it
    /// they're on (the "after"). Compare the two runs' "[APL slidewatch]" tables.
    /// </summary>
    public static class SlideFix
    {
        public static readonly bool On = !Environment.GetCommandLineArgs().Contains("-aplnoslidefix");

        /// <summary>
        /// Deep area 1, round 1's fixes (FootPin's contact hold / no swivel / stepped re-plant, TurnStep turns, the moving
        /// reaction, crossings that walk on through the run-up, the StartLoop pending-play fix, the victory walk, the warm-up
        /// filter…): on with <see cref="On"/>; <c>-aplslideround1off</c> turns just these off — the same build's "before",
        /// with the scenario labels, for the before/after tables.
        /// </summary>
        public static readonly bool Round1 = On && !Environment.GetCommandLineArgs().Contains("-aplslideround1off");
    }

    /// <summary>
    /// LOG ONLY (owner v26 #17; no behaviour): how fast every body's planted foot moves over the ground, for
    /// EVERY actor — batters, bowler, keeper, fielders, the others (umpire, captains) — whoever moves him
    /// (a Strides walk, a kinematic run, a clip, a glide). Strides' own meter only sees its walks; this sees
    /// everything. A "plant" is a stretch of frames with the same contact point (the lowest of both ankles and
    /// toes) on the ground — low and not moving up or down — and its slide is the point's ground travel over
    /// the plant's time. Each plant is booked against the actor's role and the clip(s) playing (A&gt;B while the
    /// animator blends), "+walk" when a Strides walk owns him (FootLock), "+turn" when he turned over 90°/s.
    /// One-frame moves of 12–50 cm (faster than 4 m/s) while planted are booked as POPS (somebody placed him), more than 50 cm as
    /// jumps (cuts, not counted). Replays (Strides.IsPaused) are left out.
    /// Lines: "[APL slidewatch] SLIDE …" for a plant over 0.25 m/s (rate-limited), and every 30 s and at the end
    /// the table "[APL slidewatch] role state: plants, max, mean, worst" plus "[APL slidewatch] SUMMARY …".
    /// Debug builds only (the PM's test runs); add <c>-aplslidewatch off</c> to silence it.
    /// </summary>
    public sealed class SlideWatch : MonoBehaviour
    {
        private static SlideWatch? _me;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Debug.isDebugBuild || _me != null) return;
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-aplslidewatch");
            if (i >= 0 && i + 1 < args.Length && args[i + 1] == "off") return;
            var go = new GameObject("SlideWatch");
            DontDestroyOnLoad(go);
            _me = go.AddComponent<SlideWatch>();
            Debug.Log($"[APL slidewatch] on; v26 slide fixes {(SlideFix.On ? "ON" : "OFF (-aplnoslidefix)")}; deep-area-1 round 1 {(SlideFix.Round1 ? "ON" : "OFF (-aplslideround1off)")}; footpin {(FootPin.Off ? "OFF (-aplnofootpin)" : "on")}");
        }

        /// <summary>Per role and state: plants counted, the worst and the sum of their slide rates.</summary>
        private sealed class Stat
        {
            public int Plants, Pops;
            public float Max, Sum, MaxPop;
            public string Worst = "";
            /// <summary>Every plant's rate (for the p95: one placement spike shouldn't read as the row's slide).</summary>
            public readonly List<float> Rates = new List<float>();
        }

        private static float P95(List<float> v)
        {
            if (v.Count == 0) return 0f;
            var s = v.OrderBy(x => x).ToList();
            return s[Mathf.Clamp(Mathf.CeilToInt(0.95f * s.Count) - 1, 0, s.Count - 1)];
        }

        private static readonly Dictionary<string, List<float>> RoleRates = new Dictionary<string, List<float>>();

        private static readonly Dictionary<string, Stat> Stats = new Dictionary<string, Stat>();
        private static readonly Dictionary<string, float> RoleMax = new Dictionary<string, float>();
        private static int _lines;

        /// <summary>SLIDE and POP lines per run (deep area 1 reviewer G9: 300 was hit).</summary>
        private const int LineCap = 2000;
        private float _scan, _report;

        private void Update()
        {
            if (Time.unscaledTime >= _scan)
            {
                _scan = Time.unscaledTime + 1f;
                // The match scene only (the auction hall's seated crowd isn't anybody's walk).
                if (FindAnyObjectByType<MatchDirector>() == null) return;
                foreach (var a in FindObjectsByType<Animator>(FindObjectsSortMode.None))
                {
                    if (a == null || !a.isHuman || a.runtimeAnimatorController == null || a.GetComponent<SlideProbe>() != null) continue;
                    a.gameObject.AddComponent<SlideProbe>();
                }
            }
            if (Time.unscaledTime >= _report)
            {
                if (_report > 0) Report("every 30 s");
                _report = Time.unscaledTime + 30f;
            }
        }

        private void OnApplicationQuit() => Report("at the end");

        internal static void Book(string role, string state, string who, float rate, float secs)
        {
            var key = role + " " + state;
            if (!Stats.TryGetValue(key, out var s)) Stats[key] = s = new Stat();
            s.Plants++;
            s.Sum += rate;
            s.Rates.Add(rate);
            if (!RoleRates.TryGetValue(role, out var rr)) RoleRates[role] = rr = new List<float>();
            rr.Add(rate);
            if (rate > s.Max)
            {
                s.Max = rate;
                s.Worst = $"{who} at {Time.time:0.0} s";
            }
            RoleMax.TryGetValue(role, out var m);
            if (rate > m) RoleMax[role] = rate;
            if (rate > 0.25f && secs >= 0.12f && _lines++ < LineCap)
                Debug.Log($"[APL slidewatch] SLIDE {role} {who} {state}: {rate:0.00} m/s over {secs:0.00} s ({rate * secs * 100:0} cm)");
        }

        internal static void BookPop(string role, string state, string who, float metres)
        {
            var key = role + " " + state;
            if (!Stats.TryGetValue(key, out var s)) Stats[key] = s = new Stat();
            s.Pops++;
            if (metres > s.MaxPop) s.MaxPop = metres;
            if (_lines++ < LineCap) Debug.Log($"[APL slidewatch] POP {role} {who} {state}: planted foot moved {metres * 100:0} cm in one frame (placed)");
        }

        /// <summary>Deep area 1: per owner scenario (SlideScene), the same figures plus jumps, and which states fed it.</summary>
        private sealed class SceneStat
        {
            public readonly List<float> Rates = new List<float>();
            public float Sum, Max, MaxPop, MaxJump;
            public int Pops, Jumps;
            /// <summary>Per role+state+tags: plants and summed rate (the scenario's worst contributors).</summary>
            public readonly Dictionary<string, (int n, float sum)> States = new Dictionary<string, (int, float)>();
        }

        private static readonly Dictionary<string, SceneStat> Scenes = new Dictionary<string, SceneStat>();

        private static SceneStat SceneOf(string scene)
        {
            if (!Scenes.TryGetValue(scene, out var s)) Scenes[scene] = s = new SceneStat();
            return s;
        }

        internal static void BookScene(string scene, string state, float rate)
        {
            var s = SceneOf(scene);
            s.Rates.Add(rate);
            s.Sum += rate;
            if (rate > s.Max) s.Max = rate;
            s.States.TryGetValue(state, out var st);
            s.States[state] = (st.n + 1, st.sum + rate);
        }

        internal static void BookScenePop(string scene, float metres)
        {
            var s = SceneOf(scene);
            s.Pops++;
            if (metres > s.MaxPop) s.MaxPop = metres;
        }

        internal static void BookJump(string scene, float metres)
        {
            var s = SceneOf(scene);
            s.Jumps++;
            if (metres > s.MaxJump) s.MaxJump = metres;
        }

        /// <summary>
        /// "[APL slidewatch] SCENARIO name | plants | p95 | mean | max | pops (max cm) | jumps (max m) | top states" per scenario,
        /// then "[APL slidewatch] SCENARIO TARGET" (10+ plants: mean &lt;= 0.15 and p95 &lt;= 0.5, turns included).
        /// </summary>
        private static void ReportScenes()
        {
            if (Scenes.Count == 0) return;
            Debug.Log("[APL slidewatch] SCENARIO table: scenario | plants | p95 m/s | mean m/s | max m/s | pops (max cm) | jumps>0.5m (max m) | worst states (plants, mean)");
            foreach (var kv in Scenes.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var s = kv.Value;
                var n = s.Rates.Count;
                var top = string.Join("; ", s.States.OrderByDescending(x => x.Value.sum).Take(4).Select(x => $"{x.Key} ({x.Value.n}, {x.Value.sum / Mathf.Max(1, x.Value.n):0.00})"));
                Debug.Log($"[APL slidewatch] SCENARIO {kv.Key} | {n} | {P95(s.Rates):0.000} | {s.Sum / Mathf.Max(1, n):0.000} | {s.Max:0.000} | {s.Pops} ({s.MaxPop * 100:0}) | {s.Jumps} ({s.MaxJump:0.00}) | {top}");
            }
            var bad = Scenes.Where(k => k.Value.Rates.Count >= 10 && (P95(k.Value.Rates) > 0.5f || k.Value.Sum / k.Value.Rates.Count > 0.15f)).Select(k => k.Key).ToList();
            Debug.Log($"[APL slidewatch] SCENARIO TARGET (10+ plants, turns included: mean <= 0.15 and p95 <= 0.5): {(bad.Count == 0 ? "ALL MET" : bad.Count + " over: " + string.Join("; ", bad))}");
        }

        public static void Report(string when)
        {
            if (Stats.Count == 0) return;
            ReportScenes();
            Debug.Log($"[APL slidewatch] TABLE {when} (fixes {(SlideFix.On ? "on" : "off")}): role state | plants | p95 m/s | max m/s | mean m/s | pops (max cm) | worst");
            // (The end-of-run table prints every row — deep area 1 reviewer G6; the 30 s tables the top 70.)
            foreach (var kv in Stats.OrderByDescending(k => k.Value.Plants >= 10 ? P95(k.Value.Rates) : P95(k.Value.Rates) * 0.5f).Take(when == "at the end" ? int.MaxValue : 70))
            {
                var s = kv.Value;
                Debug.Log($"[APL slidewatch] {kv.Key} | {s.Plants} | {P95(s.Rates):0.000} | {s.Max:0.000} | {s.Sum / Mathf.Max(1, s.Plants):0.000} | {s.Pops} ({s.MaxPop * 100:0}) | {s.Worst}");
            }
            Debug.Log("[APL slidewatch] SUMMARY by role (plants, p95, mean, max m/s): " + string.Join(", ", RoleMax.OrderBy(k => k.Key).Select(k =>
                $"{k.Key} {RoleRates[k.Key].Count}, {P95(RoleRates[k.Key]):0.000}, {RoleRates[k.Key].Average():0.000}, {k.Value:0.000}")));
            var bad = Stats.Where(k => k.Value.Plants >= 10 && !k.Key.Contains("+turn") && (P95(k.Value.Rates) > 0.5f || k.Value.Sum / k.Value.Plants > 0.15f)).Select(k => k.Key).ToList();
            Debug.Log($"[APL slidewatch] TARGET (rows with 10+ plants, not +turn: mean <= 0.15 and p95 <= 0.5): {(bad.Count == 0 ? "ALL MET" : bad.Count + " rows over: " + string.Join("; ", bad))}");
            // Deep area 1: turns count too (the turn-on-the-spot steps, TurnStep / the turn clips).
            var turns = Stats.Where(k => k.Value.Plants >= 10 && k.Key.Contains("+turn") && (P95(k.Value.Rates) > 0.5f || k.Value.Sum / k.Value.Plants > 0.15f)).Select(k => k.Key).ToList();
            Debug.Log($"[APL slidewatch] TARGET +turn rows (10+ plants): {(turns.Count == 0 ? "ALL MET" : turns.Count + " rows over: " + string.Join("; ", turns))}");
        }

    }
}
