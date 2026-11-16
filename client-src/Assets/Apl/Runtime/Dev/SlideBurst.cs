using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Apl.Client.Match;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// TEST ONLY (deep area 1, the sliding reviewer's tool; no behaviour): scenario bursts at EXACTLY 1/30 s of game time a
    /// frame. With <c>-aplshots &lt;dir&gt;</c> and <c>-aplburst &lt;list&gt;</c> (comma-separated, or <c>all</c>):
    /// walkout, newbatter, runbetween, fieldchange, wicketcelebration, victory. The first time each scenario happens in the
    /// run, its subject is filmed from a side camera at shin height that tracks his root (never turns), N frames
    /// (<c>-aplburstframes N</c>, default 60 = 2 s) into &lt;dir&gt;/burst_&lt;scenario&gt;/NN.png. While filming,
    /// <c>Time.captureDeltaTime</c> = 1/30, so consecutive frames are 33 ms of game time apart however long the capture
    /// takes (walkburst's were 0.14-0.23 s). The footprint discs (FootPrints) are on with -aplburst, so a planted foot must
    /// stay on its disc frame to frame. Each frame logs "[APL burst] …" with the dt, state, root, both ankles and toes and
    /// each foot's drift from its disc. Found by watching the bodies (no hooks in game code):
    /// walkout = a batter on a Strides walk-out (walk_out loop, 5 m+ to go); newbatter = the same for a third batter body;
    /// runbetween = a batter in a run-with-bat / run-single clip; fieldchange = a fielder crossing 3 m+ on a Strides walk
    /// within 20 s of the over changing; wicketcelebration = the first celebration / high five / applause on a fielder or
    /// the bowler, filming the nearest team-mate still running in (else the celebrator); victory = "[APL victory] … walking
    /// in" in the log, filming the first walker.
    /// </summary>
    public sealed class SlideBurst : MonoBehaviour
    {
        private static readonly string[] Scenarios = { "walkout", "newbatter", "runbetween", "fieldchange", "wicketcelebration", "victory" };

        private string _dir = "";
        private int _frames = 60;
        private readonly HashSet<string> _want = new HashSet<string>();
        private readonly HashSet<string> _done = new HashSet<string>();
        private readonly List<string> _walkers = new List<string>();
        private bool _filming;
        private float _scan;
        private Animator[] _bodies = new Animator[0];
        private int _over = -1;
        private float _overAt = -100f;
        private string? _victoryHolder;
        private float _victoryAt = -100f;
        private MatchDirector? _director;
        private static FieldInfo? _matchField;
        private readonly List<AnimatorClipInfo> _clips = new List<AnimatorClipInfo>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var b = Array.IndexOf(args, "-aplburst");
            var s = Array.IndexOf(args, "-aplshots");
            if (b < 0 || s < 0 || s + 1 >= args.Length || FindAnyObjectByType<SlideBurst>() != null) return;
            var go = new GameObject("SlideBurst");
            DontDestroyOnLoad(go);
            var me = go.AddComponent<SlideBurst>();
            me._dir = args[s + 1];
            var list = b + 1 < args.Length && !args[b + 1].StartsWith("-") ? args[b + 1] : "all";
            foreach (var w in list.Split(',').Select(x => x.Trim().ToLowerInvariant()))
            {
                if (w == "all") foreach (var k in Scenarios) me._want.Add(k);
                else if (Scenarios.Contains(w)) me._want.Add(w);
                else Debug.LogWarning($"[APL burst] unknown scenario '{w}' (know: {string.Join(", ", Scenarios)})");
            }
            var f = Array.IndexOf(args, "-aplburstframes");
            if (f >= 0 && f + 1 < args.Length && int.TryParse(args[f + 1], out var n) && n > 0) me._frames = Mathf.Min(n, 300);
            Application.logMessageReceived += me.OnLog;
            Debug.Log($"[APL burst] armed: {string.Join(", ", me._want)}; {me._frames} frames at 1/30 s game time each");
        }

        private void OnDestroy() => Application.logMessageReceived -= OnLog;

        private void OnLog(string message, string stack, LogType type)
        {
            // "[APL victory] 4 team-mates walking in round Trophy_r129"
            if (!message.StartsWith("[APL victory]") || !message.Contains("walking in round ")) return;
            _victoryHolder = message.Substring(message.IndexOf("walking in round ", StringComparison.Ordinal) + "walking in round ".Length).Trim();
            _victoryAt = Time.time;
        }

        private string Clip(Animator a)
        {
            _clips.Clear();
            a.GetCurrentAnimatorClipInfo(0, _clips);
            var best = "";
            var w = -1f;
            foreach (var c in _clips)
            {
                if (c.clip == null || c.weight <= w) continue;
                w = c.weight;
                best = c.clip.name;
            }
            if (a.IsInTransition(0))
            {
                _clips.Clear();
                a.GetNextAnimatorClipInfo(0, _clips);
                foreach (var c in _clips) if (c.clip != null) best += ">" + c.clip.name;
            }
            return best;
        }

        private static float ToGo(Animator a)
        {
            var f = a.GetComponent<FootLock>();
            if (f == null || !f.Active) return 0f;
            var d = f.Target - a.transform.position;
            d.y = 0;
            return d.magnitude;
        }

        private int Over()
        {
            if (_director == null) return -1;
            try
            {
                _matchField ??= typeof(MatchDirector).GetField("_match", BindingFlags.NonPublic | BindingFlags.Instance);
                var m = _matchField?.GetValue(_director);
                var p = m?.GetType().GetProperty("Over");
                return p != null ? (int)p.GetValue(m) : -1;
            }
            catch
            {
                return -1;
            }
        }

        private void Update()
        {
            if (_filming || _want.Count == _done.Count) return;
            if (Time.unscaledTime >= _scan)
            {
                _scan = Time.unscaledTime + 0.5f;
                _director = FindAnyObjectByType<MatchDirector>();
                _bodies = _director == null ? new Animator[0]
                    : FindObjectsByType<Animator>(FindObjectsSortMode.None).Where(a => a != null && a.isHuman && (a.GetComponent<BatterRig>() != null || a.name.StartsWith("Field") || a.name.StartsWith("Bat") || a.name.StartsWith("Trophy"))).ToArray();
            }
            if (_director == null || Strides.IsPaused) return;
            var over = Over();
            if (over != _over)
            {
                if (_over >= 0) _overAt = Time.time;
                _over = over;
            }
            foreach (var a in _bodies)
            {
                if (a == null || !a.gameObject.activeInHierarchy) continue;
                var batter = a.GetComponent<BatterRig>() != null;
                var clip = Clip(a);
                if (batter && Strides.Busy(a) && clip.Contains("walk_out") && ToGo(a) > 5f)
                {
                    // (b3: the "third batter" rule never fired — the openers' second walk can be under 5 m — so the new batter is
                    // known by the walk's own scenario tag, "batter walk-out (new batter)".)
                    var scene = SlideScene.Of(a) ?? "";
                    if (scene.Contains("new batter")) { if (Go("newbatter", a, clip)) return; }
                    else if (!_walkers.Contains(a.name))
                    {
                        _walkers.Add(a.name);
                        if (_walkers.Count <= 2 && Go("walkout", a, clip)) return;
                    }
                }
                if (batter && (clip.Contains("run_loop_with_bat") || clip.Contains("run_single")) && Go("runbetween", a, clip)) return;
                // (b2_RC: the end-of-match handshake walk passed for a field change — only the fielders' gait loops count.)
                if (!batter && a.name.StartsWith("Field") && Strides.Busy(a) && ToGo(a) > 3f && Time.time - _overAt < 20f && clip.Contains("run_0") && !clip.Contains("handshake")
                    && Go("fieldchange", a, clip)) return;
                if (!batter && a.name.StartsWith("Field") && Time.time - _victoryAt > 30f
                    && (clip.Contains("high_five") || clip.Contains("celebration") || clip.StartsWith("applause")))
                {
                    // The nearest team-mate still running in to him (feet-driven), else the celebrator himself.
                    var mate = _bodies.Where(o => o != null && o != a && o.gameObject.activeInHierarchy && Strides.FeetDriven(o))
                        .OrderBy(o => Vector3.Distance(o.transform.position, a.transform.position)).FirstOrDefault();
                    var who = mate != null && Vector3.Distance(mate.transform.position, a.transform.position) < 30f ? mate : a;
                    if (Go("wicketcelebration", who, Clip(who))) return;
                }
            }
            if (_victoryHolder != null && Time.time - _victoryAt < 3f && _want.Contains("victory") && !_done.Contains("victory"))
            {
                var holder = _bodies.FirstOrDefault(o => o != null && o.name == _victoryHolder);
                var at = holder != null ? holder.transform.position : Vector3.zero;
                var walker = _bodies.Where(o => o != null && o != holder && Strides.Busy(o) && (holder == null || Vector3.Distance(o.transform.position, at) < 12f))
                    .OrderBy(o => Vector3.Distance(o.transform.position, at)).FirstOrDefault();
                if (walker != null) Go("victory", walker, Clip(walker));
            }
        }

        private bool Go(string scenario, Animator a, string clip)
        {
            if (!_want.Contains(scenario) || _done.Contains(scenario) || _filming) return false;
            _done.Add(scenario);
            StartCoroutine(Film(scenario, a, clip));
            return true;
        }

        private IEnumerator Film(string scenario, Animator a, string clip)
        {
            _filming = true;
            var outDir = Path.Combine(_dir, "burst_" + scenario);
            Directory.CreateDirectory(outDir);
            var far = scenario == "runbetween" || scenario == "fieldchange";
            var side = a.transform.right;
            side.y = 0;
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            var off = far ? 4.5f : 3.2f;
            var cam = new GameObject("SlideBurstCam").AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = far ? 42f : 38f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            var points = new[]
            {
                a.GetBoneTransform(HumanBodyBones.LeftFoot), a.GetBoneTransform(HumanBodyBones.LeftToes),
                a.GetBoneTransform(HumanBodyBones.RightFoot), a.GetBoneTransform(HumanBodyBones.RightToes),
            };
            var probe = a.GetComponent<FootPrintProbe>() ?? a.gameObject.AddComponent<FootPrintProbe>();
            var was = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 30f;
            Debug.Log($"[APL burst] {scenario}: {a.name} in {clip}: {_frames} frames at 1/30 s game time to {outDir}");
            float maxL = 0f, maxR = 0f;
            var filmed = 0;
            try
            {
                for (var f = 0; f < _frames; f++)
                {
                    yield return new WaitForEndOfFrame();
                    if (a == null || !a.gameObject.activeInHierarchy)
                    {
                        Debug.Log($"[APL burst] {scenario}: subject gone at frame {f}");
                        break;
                    }
                    if (Strides.IsPaused)
                    {
                        Debug.Log($"[APL burst] {scenario}: a replay took over at frame {f}: stopped");
                        break;
                    }
                    var root = a.transform.position;
                    root.y = 0;
                    cam.transform.position = root + side * off + Vector3.up * 0.45f;
                    cam.transform.rotation = Quaternion.LookRotation(root + Vector3.up * 0.62f - cam.transform.position);
                    // Nobody between the camera and him.
                    var hidden = new List<Renderer>();
                    var eye = cam.transform.position;
                    var him = root + Vector3.up * 0.6f;
                    foreach (var other in FindObjectsByType<Animator>(FindObjectsSortMode.None))
                    {
                        if (other == a || other == null) continue;
                        var p = other.transform.position + Vector3.up * 0.6f;
                        var t = Mathf.Clamp01(Vector3.Dot(p - eye, him - eye) / Mathf.Max(1e-4f, (him - eye).sqrMagnitude));
                        if (t <= 0f || t >= 0.98f || Vector3.Distance(p, eye + (him - eye) * t) > 0.9f) continue;
                        foreach (var r in other.GetComponentsInChildren<Renderer>())
                            if (r.enabled) { r.enabled = false; hidden.Add(r); }
                    }
                    // The subject himself drawn even if the match camera hid him (R-A: the fieldchange burst filmed empty turf —
                    // the bowler-end shot hides the bodies in its way); put back as found after the render.
                    var shown = new List<SkinnedMeshRenderer>();
                    foreach (var r in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        if (r != null && !r.enabled) { r.enabled = true; shown.Add(r); }
                    cam.Render();
                    foreach (var r in hidden) if (r != null) r.enabled = true;
                    foreach (var r in shown) if (r != null) r.enabled = false;
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                    tex.Apply(false);
                    RenderTexture.active = prev;
                    CaptureWriter.Write(Path.Combine(outDir, $"{scenario}_{f:00}.png"), tex.EncodeToPNG());
                    filmed++;
                    string P(Transform? tr) => tr == null ? "-" : $"({tr.position.x:0.000},{tr.position.y:0.000},{tr.position.z:0.000})";
                    var dl = probe != null ? probe.DriftCm(0) : -1f;
                    var dr = probe != null ? probe.DriftCm(1) : -1f;
                    maxL = Mathf.Max(maxL, dl);
                    maxR = Mathf.Max(maxR, dr);
                    var feet = a.GetComponent<FootLock>();
                    var lockInfo = feet != null && feet.Active ? $"lock {(feet.Free ? "free" : "walk")} pt={feet.LastPoint} v={feet.Velocity.magnitude:0.00}" : "no lock";
                    Debug.Log($"[APL burst] {scenario} f={f:00} t={Time.time:0.000} dt={Time.deltaTime:0.0000} {a.name} state={Clip(a)} speed={a.speed:0.00} root=({a.transform.position.x:0.000},{a.transform.position.z:0.000}) yaw={a.transform.eulerAngles.y:0.0} {lockInfo}"
                              + $" | driftL={(dl < 0 ? "-" : dl.ToString("0.0"))} cm driftR={(dr < 0 ? "-" : dr.ToString("0.0"))} cm | ankleL={P(points[0])} toeL={P(points[1])} ankleR={P(points[2])} toeR={P(points[3])}");
                }
            }
            finally
            {
                Time.captureDeltaTime = was;
                Destroy(cam.gameObject);
                rt.Release();
                Destroy(tex);
                _filming = false;
            }
            Debug.Log($"[APL burst] {scenario} done: {filmed} frames; worst planted drift in the burst L {maxL:0.0} cm, R {maxR:0.0} cm (disc red over {FootPrints.RedCm:0} cm)");
        }
    }
}
