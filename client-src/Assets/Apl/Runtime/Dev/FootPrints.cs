using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Match;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// TEST ONLY (deep area 1, the sliding reviewer's tool; no behaviour): footprint discs. With <c>-aplshots &lt;dir&gt;</c>
    /// and <c>-aplfootprints</c> (or <c>-aplburst …</c>, whose frames need them), every player body gets a
    /// <see cref="FootPrintProbe"/>: each time a foot plants (the meters' rule: lowest of its ankle/toe within 4 cm of the
    /// ground line and not moving up or down faster than 0.35 m/s), a flat disc is laid on the turf under the contact
    /// point — white for a fielder, cyan a batter, yellow the bowler, green the keeper — and turns RED if that contact
    /// point drifts more than 3 cm from where it landed while the foot is still down. Discs go 3 s after the foot lifts.
    /// So any ordinary still shows slide: a foot that's left its disc, a red disc.
    /// Log: "[APL footprints] RED …" per drifting plant (capped) and a summary every 30 s and at the end.
    /// </summary>
    public sealed class FootPrints : MonoBehaviour
    {
        public const float RedCm = 3f;
        private const float Linger = 3f, Diameter = 0.10f;

        private static FootPrints? _me;

        /// <summary>The discs are on for this run (-aplshots plus -aplfootprints or -aplburst).</summary>
        public static readonly bool On = ReadOn();

        private static bool ReadOn()
        {
            var args = Environment.GetCommandLineArgs();
            return args.Contains("-aplshots") && (args.Contains("-aplfootprints") || args.Contains("-aplburst"));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!On || _me != null) return;
            var go = new GameObject("FootPrints");
            DontDestroyOnLoad(go);
            _me = go.AddComponent<FootPrints>();
            Debug.Log($"[APL footprints] on: a disc under every planted foot, red past {RedCm:0} cm of drift");
        }

        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        private static readonly Stack<GameObject> Free = new Stack<GameObject>();
        private static readonly List<(GameObject disc, float at)> Leaving = new List<(GameObject, float)>();
        private static GameObject? _root;

        private sealed class Stat
        {
            public int Plants, Red;
            public float MaxCm;
            public string Worst = "";
            public readonly List<float> Cm = new List<float>();
        }

        private static readonly Dictionary<string, Stat> ByRole = new Dictionary<string, Stat>();
        private static int _redLines;
        private float _scan, _report;

        private static Material Mat(string key, Color c)
        {
            if (Mats.TryGetValue(key, out var m)) return m;
            m = new Material(Shader.Find("Hidden/Internal-Colored")) { color = c };
            Mats[key] = m;
            return m;
        }

        public static Color RoleColor(string role) => role switch
        {
            "batter" => new Color(0.1f, 0.9f, 1f),
            "bowler" => new Color(1f, 0.9f, 0.1f),
            "keeper" => new Color(0.2f, 1f, 0.3f),
            _ => Color.white,
        };

        /// <summary>A disc on the turf at <paramref name="at"/> (ground height), in the role's colour.</summary>
        public static GameObject Take(Vector3 at, string role)
        {
            if (_root == null)
            {
                _root = new GameObject("FootPrintDiscs");
                DontDestroyOnLoad(_root);
            }
            GameObject d;
            if (Free.Count > 0 && Free.Peek() != null) d = Free.Pop();
            else
            {
                if (Free.Count > 0) Free.Pop();
                d = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                d.name = "FootPrintDisc";
                var col = d.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var r = d.GetComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                d.transform.localScale = new Vector3(Diameter, 0.002f, Diameter);
                d.transform.SetParent(_root.transform, true);
            }
            d.transform.position = new Vector3(at.x, 0.004f, at.z);
            d.GetComponent<MeshRenderer>().sharedMaterial = Mat(role, RoleColor(role));
            d.SetActive(true);
            return d;
        }

        public static void Move(GameObject? d, Vector3 at)
        {
            if (d != null) d.transform.position = new Vector3(at.x, 0.004f, at.z);
        }

        public static void Redden(GameObject? d)
        {
            if (d != null) d.GetComponent<MeshRenderer>().sharedMaterial = Mat("red", Color.red);
        }

        /// <summary>The foot lifted: the disc stays <see cref="Linger"/> s more, then goes back to the pool.</summary>
        public static void Release(GameObject? d)
        {
            if (d != null) Leaving.Add((d, Time.unscaledTime + Linger));
        }

        /// <summary>One plant over: booked against the role (for the summary) and, if it drifted, logged.</summary>
        public static void Book(string role, string who, string state, float cm, float secs)
        {
            if (!ByRole.TryGetValue(role, out var s)) ByRole[role] = s = new Stat();
            s.Plants++;
            s.Cm.Add(cm);
            if (cm > s.MaxCm)
            {
                s.MaxCm = cm;
                s.Worst = $"{who} {state} at {Time.time:0.0} s";
            }
            if (cm <= RedCm) return;
            s.Red++;
            if (_redLines++ < 400) Debug.Log($"[APL footprints] RED {role} {who} {state}: contact point drifted {cm:0.0} cm while planted ({secs:0.00} s)");
        }

        private static float P95(List<float> v)
        {
            if (v.Count == 0) return 0f;
            var s = v.OrderBy(x => x).ToList();
            return s[Mathf.Clamp(Mathf.CeilToInt(0.95f * s.Count) - 1, 0, s.Count - 1)];
        }

        public static void Report(string when)
        {
            if (ByRole.Count == 0) return;
            Debug.Log($"[APL footprints] SUMMARY {when} (role: plants, red >{RedCm:0} cm, p95 cm, max cm, worst): " + string.Join("; ", ByRole.OrderBy(k => k.Key).Select(k =>
                $"{k.Key} {k.Value.Plants}, {k.Value.Red} ({100f * k.Value.Red / Mathf.Max(1, k.Value.Plants):0.0}%), {P95(k.Value.Cm):0.0}, {k.Value.MaxCm:0.0}, {k.Value.Worst}")));
        }

        private void Update()
        {
            for (var i = Leaving.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < Leaving[i].at) continue;
                var d = Leaving[i].disc;
                Leaving.RemoveAt(i);
                if (d == null) continue;
                d.SetActive(false);
                Free.Push(d);
            }
            if (Time.unscaledTime >= _report)
            {
                if (_report > 0) Report("every 30 s");
                _report = Time.unscaledTime + 30f;
            }
            if (Time.unscaledTime < _scan) return;
            _scan = Time.unscaledTime + 1f;
            if (FindAnyObjectByType<MatchDirector>() == null) return;
            foreach (var a in FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                if (a == null || !a.isHuman || a.GetComponent<FootPrintProbe>() != null) continue;
                var player = a.GetComponent<BatterRig>() != null || a.GetComponent<BowlerRig>() != null || a.GetComponent<KeeperRig>() != null
                             || a.name.StartsWith("Field") || a.name.StartsWith("Bat");
                if (player) a.gameObject.AddComponent<FootPrintProbe>();
            }
        }

        private void OnApplicationQuit() => Report("at the end");
    }
}
