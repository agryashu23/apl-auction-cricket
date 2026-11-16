using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Apl.Client.Match;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// TEST ONLY (deep area 1; no behaviour): the authored-slide table. <c>-aplclipslide [csv path]</c> (optional
    /// <c>-aplclipfilter &lt;substring&gt;</c>, <c>-aplclipmodels N</c>, default 2): loads the ground, spawns N cricket
    /// bodies far off the field (renderers off) with the cricket controller, and samples EVERY clip of the controller that
    /// has a state of its name — 60 samples a second, in place, the transform fixed. The imports bake the root's XZ into
    /// the pose, so a planted foot should not move at all in an authored clip; an in-place gait loop's planted foot moves
    /// back at its stride speed by design (kind "inplace-gait": judged in game, not here). Per foot, a plant is the meters'
    /// rule (lowest of ankle/toe within 4 cm of the clip's ground and not moving up or down faster than 0.35 m/s, 0.08 s+);
    /// its slide is the contact point's ground travel (moves over 12 cm in a sample are pops, counted apart), its drift the
    /// furthest the contact point gets from where it landed (each point anchored when it first becomes the lowest, as
    /// FootPrintProbe). Also: the clip's net body yaw (hips+shoulders, first to last frame) and the hips' ground travel —
    /// for the turn clips (90/180, left/right) and start/stop clips. Writes the CSV, logs "[APL clipslide]" lines, quits.
    /// New clips are picked up automatically (whatever the controller holds).
    /// </summary>
    public sealed class ClipSlide : MonoBehaviour
    {
        private const float Hz = 60f, Band = 0.04f, Rise = 0.35f, PopM = 0.12f, CutM = 0.5f, MinSecs = 0.08f;

        private string _csv = "";
        private string? _filter;
        private int _models = 2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-aplclipslide");
            if (i < 0 || FindAnyObjectByType<ClipSlide>() != null) return;
            var go = new GameObject("ClipSlide");
            DontDestroyOnLoad(go);
            var me = go.AddComponent<ClipSlide>();
            me._csv = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Path.Combine(Application.persistentDataPath, "clipslide.csv");
            var f = Array.IndexOf(args, "-aplclipfilter");
            if (f >= 0 && f + 1 < args.Length) me._filter = args[f + 1];
            var m = Array.IndexOf(args, "-aplclipmodels");
            if (m >= 0 && m + 1 < args.Length && int.TryParse(args[m + 1], out var n) && n > 0) me._models = n;
        }

        private void Start()
        {
            Application.runInBackground = true;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(2f);
            // Straight to the ground (unless an autoplay is already taking the menus there).
            if (FindAnyObjectByType<GroundRefs>() == null && !Environment.GetCommandLineArgs().Contains("-aplmatchtest"))
            {
                MatchLaunch.MyTeam = "MBH";
                MatchLaunch.Opponent = "CHC";
                UnityEngine.SceneManagement.SceneManager.LoadScene(MatchLaunch.GroundScene);
            }
            GroundRefs? refs = null;
            for (var until = Time.realtimeSinceStartup + 90f; Time.realtimeSinceStartup < until; )
            {
                refs = FindAnyObjectByType<GroundRefs>();
                if (refs != null && refs.Cricket != null && refs.CastModels.Length > 0) break;
                refs = null;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (refs == null)
            {
                Debug.LogError("[APL clipslide] no ground (GroundRefs with the cricket controller) within 90 s: nothing measured");
                Application.Quit(2);
                yield break;
            }
            yield return new WaitForSecondsRealtime(1f);
            var clips = refs.Cricket!.animationClips.Where(c => c != null).GroupBy(c => c.name).Select(g => g.First())
                .Where(c => _filter == null || c.name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(c => c.name).ToList();
            var models = new List<GameObject>();
            foreach (var p in refs.CastModels)
            {
                if (p == null || CricketCast.Dropped.Contains(p.name) || CricketCast.NotPlayers.Contains(p.name)) continue;
                if (models.Count >= _models) break;
                models.Add(p);
            }
            var rows = new List<Row>();
            var skipped = new HashSet<string>();
            for (var mi = 0; mi < models.Count; mi++)
            {
                var go = Instantiate(models[mi]);
                go.name = "ClipSlideBody";
                go.transform.SetPositionAndRotation(new Vector3(400f + mi * 5f, 0f, 400f), Quaternion.identity);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.enabled = false;
                var a = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
                a.runtimeAnimatorController = refs.Cricket;
                a.applyRootMotion = false;
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                a.speed = 1f;
                if (!a.isHuman)
                {
                    Debug.LogWarning($"[APL clipslide] {models[mi].name}: not a humanoid, skipped");
                    Destroy(go);
                    continue;
                }
                foreach (var p in a.parameters)
                    if (p.type == AnimatorControllerParameterType.Bool) a.SetBool(p.nameHash, false);
                yield return null;
                foreach (var clip in clips)
                {
                    var hash = Animator.StringToHash(clip.name);
                    if (!a.HasState(0, hash))
                    {
                        skipped.Add(clip.name);
                        continue;
                    }
                    rows.Add(Measure(a, clip, hash, models[mi].name));
                    yield return null; // one clip a frame: no long stall
                }
                Destroy(go);
            }
            Write(rows, skipped, clips.Count, models.Count);
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        private sealed class Row
        {
            public string Model = "", Clip = "", Kind = "";
            public float Length, StrideMps, StrideCv, Mean, P95, Max, MaxDriftCm, MaxPopCm, WorstAt, LongestSecs, LongestDriftCm, YawDeg, HipsTravel;
            public bool Loop;
            public int Samples, Plants, Pops;
        }

        private sealed class Plant
        {
            public bool Down, HasAnkle, HasToe;
            public Vector3 AnkleAt, ToeAt, Last;
            public int LastIdx = -1;
            public float Secs, Dist, MaxCm, Start;
            public float PrevY = float.MinValue;
        }

        private static Row Measure(Animator a, AnimationClip clip, int hash, string model)
        {
            var len = Mathf.Max(clip.length, 1f / Hz);
            var n = Mathf.Clamp(Mathf.CeilToInt(len * Hz), 2, 3600);
            var dt = len / n;
            var t0 = a.transform;
            var pts = new[]
            {
                a.GetBoneTransform(HumanBodyBones.LeftFoot), a.GetBoneTransform(HumanBodyBones.LeftToes),
                a.GetBoneTransform(HumanBodyBones.RightFoot), a.GetBoneTransform(HumanBodyBones.RightToes),
            };
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            var pos = new Vector3[n + 1, 4];
            var hipsAt = new Vector3[2];
            float yaw0 = 0f, yaw1 = 0f;
            var ground = float.MaxValue;
            for (var i = 0; i <= n; i++)
            {
                // The last sample of a loop is its first again: take it a hair before the end.
                var nt = i == n ? (clip.isLooping ? (n - 0.001f) / n : 1f) : i / (float)n;
                a.Play(hash, 0, nt);
                a.Update(0f);
                for (var k = 0; k < 4; k++)
                {
                    pos[i, k] = pts[k] != null ? Quaternion.Inverse(t0.rotation) * (pts[k]!.position - t0.position) : new Vector3(0, 9, 0);
                    ground = Mathf.Min(ground, pos[i, k].y);
                }
                if (i == 0 || i == n)
                {
                    var f = Strides.BodyForward(a);
                    var y = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                    if (i == 0) yaw0 = y;
                    else yaw1 = y;
                    if (hips != null) hipsAt[i == 0 ? 0 : 1] = hips.position - t0.position;
                }
            }
            var row = new Row
            {
                Model = model, Clip = clip.name, Length = len, Loop = clip.isLooping, Samples = n + 1,
                YawDeg = Mathf.DeltaAngle(yaw0, yaw1),
                HipsTravel = new Vector2(hipsAt[1].x - hipsAt[0].x, hipsAt[1].z - hipsAt[0].z).magnitude,
            };
            var rates = new List<float>();
            var speeds = new List<float>();
            for (var foot = 0; foot < 2; foot++)
            {
                var pl = new Plant();
                void Close()
                {
                    if (pl.Down && pl.Secs >= MinSecs)
                    {
                        var r = pl.Dist / pl.Secs;
                        rates.Add(r);
                        row.Plants++;
                        if (r > row.Max)
                        {
                            row.Max = r;
                            row.WorstAt = pl.Start;
                        }
                        row.MaxDriftCm = Mathf.Max(row.MaxDriftCm, pl.MaxCm);
                        if (pl.Secs > row.LongestSecs)
                        {
                            row.LongestSecs = pl.Secs;
                            row.LongestDriftCm = pl.MaxCm;
                        }
                    }
                    pl.Down = false;
                    pl.LastIdx = -1;
                }
                for (var i = 0; i <= n; i++)
                {
                    var ank = pos[i, foot * 2];
                    var toe = pos[i, foot * 2 + 1];
                    var idx = toe.y < ank.y ? 1 : 0;
                    var p = idx == 1 ? toe : ank;
                    var vy = pl.PrevY > float.MinValue ? Mathf.Abs(p.y - pl.PrevY) / dt : 0f;
                    pl.PrevY = p.y;
                    var down = p.y < ground + Band && vy < Rise;
                    if (!down)
                    {
                        Close();
                        continue;
                    }
                    if (pl.Down && pl.LastIdx == idx)
                    {
                        var step = new Vector2(p.x - pl.Last.x, p.z - pl.Last.z).magnitude;
                        if (step > CutM) Close();
                        else if (step > PopM)
                        {
                            row.Pops++;
                            row.MaxPopCm = Mathf.Max(row.MaxPopCm, step * 100f);
                        }
                        else
                        {
                            pl.Dist += step;
                            speeds.Add(step / dt);
                        }
                    }
                    if (!pl.Down)
                    {
                        pl.Down = true;
                        pl.HasAnkle = pl.HasToe = false;
                        pl.Secs = pl.Dist = pl.MaxCm = 0f;
                        pl.Start = i * dt;
                    }
                    else pl.Secs += dt;
                    Vector3 anchor;
                    if (idx == 0)
                    {
                        if (!pl.HasAnkle) { pl.HasAnkle = true; pl.AnkleAt = p; }
                        anchor = pl.AnkleAt;
                    }
                    else
                    {
                        if (!pl.HasToe) { pl.HasToe = true; pl.ToeAt = p; }
                        anchor = pl.ToeAt;
                    }
                    pl.MaxCm = Mathf.Max(pl.MaxCm, new Vector2(p.x - anchor.x, p.z - anchor.z).magnitude * 100f);
                    pl.Last = p;
                    pl.LastIdx = idx;
                }
                Close();
            }
            if (rates.Count > 0)
            {
                row.Mean = rates.Average();
                var s = rates.OrderBy(x => x).ToList();
                row.P95 = s[Mathf.Clamp(Mathf.CeilToInt(0.95f * s.Count) - 1, 0, s.Count - 1)];
            }
            if (speeds.Count > 1)
            {
                var m = speeds.Average();
                row.StrideMps = m;
                row.StrideCv = m > 1e-4f ? Mathf.Sqrt(speeds.Sum(v => (v - m) * (v - m)) / speeds.Count) / m : 0f;
            }
            // Travel clips imported with their travel as root motion (fielder_start_*/fielder_stop_*, AssetImportRules v8) are
            // in place like the loops: their planted feet move back at the stride's speed by design ("inplace-travel"; judged by
            // hips_travel_m ≈ 0 here and in game). Baked by mistake, the hips carry the travel (hips_travel_m ≈ the clip's travel).
            var travelClip = clip.name.StartsWith("fielder_start_") || clip.name.StartsWith("fielder_stop_");
            row.Kind = travelClip ? "inplace-travel" : row.Loop && row.StrideMps > 0.4f ? "inplace-gait" : "authored";
            return row;
        }

        private void Write(List<Row> rows, HashSet<string> skipped, int clipCount, int modelCount)
        {
            string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.AppendLine("model,clip,kind,length_s,loop,samples,plants,mean_mps,p95_mps,max_mps,max_drift_cm,pops,max_pop_cm,worst_at_s,longest_plant_s,longest_plant_drift_cm,stride_mps,stride_cv,net_yaw_deg,hips_travel_m");
            foreach (var r in rows)
                sb.AppendLine(string.Join(",", r.Model, r.Clip, r.Kind, F(r.Length), r.Loop ? "1" : "0", r.Samples.ToString(CultureInfo.InvariantCulture), r.Plants.ToString(CultureInfo.InvariantCulture),
                    F(r.Mean), F(r.P95), F(r.Max), F(r.MaxDriftCm), r.Pops.ToString(CultureInfo.InvariantCulture), F(r.MaxPopCm), F(r.WorstAt), F(r.LongestSecs), F(r.LongestDriftCm),
                    F(r.StrideMps), F(r.StrideCv), F(r.YawDeg), F(r.HipsTravel)));
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(_csv));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_csv, sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError($"[APL clipslide] could not write {_csv}: {e.Message}");
            }
            // The worst authored clips (p95, then drift) for the log; the CSV has everything.
            foreach (var r in rows.Where(r => r.Kind == "authored").OrderByDescending(r => r.P95).ThenByDescending(r => r.MaxDriftCm).Take(60))
                Debug.Log($"[APL clipslide] {r.Clip} ({r.Model}): plants {r.Plants}, mean {r.Mean:0.000} p95 {r.P95:0.000} max {r.Max:0.000} m/s, drift max {r.MaxDriftCm:0.0} cm, pops {r.Pops} ({r.MaxPopCm:0} cm), yaw {r.YawDeg:0}°, hips travel {r.HipsTravel:0.00} m");
            foreach (var r in rows.Where(r => r.Kind == "inplace-travel"))
                Debug.Log($"[APL clipslide] travel {r.Clip} ({r.Model}): hips travel {r.HipsTravel:0.00} m (want ~0: root motion extracted), stride {r.StrideMps:0.00} m/s, yaw {r.YawDeg:0}°, {r.Length:0.00} s");
            foreach (var r in rows.Where(r => r.Kind == "inplace-gait"))
                Debug.Log($"[APL clipslide] gait {r.Clip} ({r.Model}): stride {r.StrideMps:0.00} m/s, cv {r.StrideCv:0.00}, pops {r.Pops}");
            var authored = rows.Where(r => r.Kind == "authored" && r.Plants > 0).ToList();
            var over = authored.Count(r => r.Mean > 0.15f || r.P95 > 0.5f);
            Debug.Log($"[APL clipslide] done: {rows.Count} rows ({clipCount} clips x {modelCount} bodies), {skipped.Count} clips with no state of their name skipped; "
                      + $"authored clips over mean 0.15 / p95 0.5: {over} of {authored.Count}; CSV {Path.GetFullPath(_csv)}");
            if (skipped.Count > 0) Debug.Log("[APL clipslide] skipped (no state): " + string.Join(", ", skipped.OrderBy(s => s)));
        }
    }
}
