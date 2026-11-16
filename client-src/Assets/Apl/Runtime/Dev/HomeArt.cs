using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apl.Client.Match;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// The home page's art (round 10), rendered from OUR OWN cast (-aplhomeart): for every
    /// side, the ground scene loaded with that side batting, and its players in its kit,
    /// posed from the game's own clips — a batter at the finish of his cover drive (the
    /// match's own rig, so the bat is where the clip puts it), the star raising his bat, a
    /// bowler's appeal, the captain with the trophy, the keeper — and the squad hero (five,
    /// the middle one in front). Each figure fills ~88% of its frame, from a low 3/4 front.
    /// PNGs (true alpha, from a black/white matte) go to &lt;shots&gt;/homeart/, a contact
    /// sheet to &lt;shots&gt;/homeart_sheet.png; the PM copies the PNGs to Resources/Ui/HomeArt/.
    /// </summary>
    public static partial class HomeArt
    {
        private const int Layer = 31;

        public static IEnumerator Run(string dir, StreamWriter log)
        {
            var outDir = Path.Combine(dir, "homeart");
            Directory.CreateDirectory(outDir);
            var data = Dataset.FromJson(Resources.Load<TextAsset>("Data/apl_dataset").text);
            var made = new List<string>();
            foreach (var f in data.Franchises)
            {
                MatchLaunch.MyTeam = f.Id;
                MatchLaunch.Opponent = data.Franchises.First(o => o.Id != f.Id).Id;
                UnityEngine.SceneManagement.SceneManager.LoadScene(MatchLaunch.GroundScene);
                yield return new WaitForSecondsRealtime(3f);
                // The ground's Start can outlast 3 s while the cast loads; wait until its HUD exists (max 30 s).
                for (var t = 0f; t < 30f; t += 0.25f)
                {
                    var d = UnityEngine.Object.FindAnyObjectByType<MatchDirector>();
                    if (d != null && d.Ready) break;
                    yield return new WaitForSecondsRealtime(0.25f);
                }
                foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
                var mc = UnityEngine.Object.FindAnyObjectByType<MatchCamera>();
                var director = UnityEngine.Object.FindAnyObjectByType<MatchDirector>();
                var refs = UnityEngine.Object.FindAnyObjectByType<GroundRefs>();
                if (mc == null || director == null || refs == null)
                {
                    log.WriteLine($"Exception: no ground scene for {f.Id}");
                    continue;
                }
                var cam = mc.GetComponent<Camera>();
                // The batter: the match's own rig at the finish of a cover drive (its bat placed by the clip's track).
                var rig = director.GalleryBatter(false);
                director.enabled = false;
                mc.enabled = false;
                cam.cullingMask = 1 << Layer;
                // No bloom / grading on the matte: the white pass must differ from the black one only by coverage.
                cam.allowHDR = false;
                // (Post-process volumes found by name: the compile check has no render-pipeline assembly.)
                foreach (var v in UnityEngine.Object.FindObjectsByType<Behaviour>(FindObjectsSortMode.None))
                    if (v.GetType().Name == "Volume") v.enabled = false;
                foreach (var extra in cam.GetComponents<Component>())
                    extra.GetType().GetProperty("renderPostProcessing")?.SetValue(extra, false);
                rig.GalleryPose(rig.UsesClips ? "05_cover_drive" : "Ready", 0.86f);
                yield return null;
                yield return null;
                Prepare(rig.gameObject);
                Prepare(rig.Bat.gameObject);
                var batAnim = rig.GetComponent<Animator>();
                yield return Snap(cam, new[] { batAnim }, new[] { rig.Bat }, Turn(Chest(batAnim), -35f), 400, 520, Path.Combine(outDir, $"mode_bat_{f.Id}.png"), made);
                // The home page's QUICK MATCH hero (owner v26): the same rig, taller and sharper, at the high
                // finish of a lofted straight drive (bat over the shoulder, chest open) — the cover drive's if
                // that clip isn't in this build. Home falls back to mode_bat until these are copied in.
                var lofted = rig.UsesClips && CricketClips.Find("09_lofted_straight_drive") != null;
                rig.GalleryPose(lofted ? "09_lofted_straight_drive" : rig.UsesClips ? "05_cover_drive" : "Ready", lofted ? 0.62f : 0.86f);
                for (var w = 0; w < 4; w++) yield return null;
                rig.GalleryPose(lofted ? "09_lofted_straight_drive" : rig.UsesClips ? "05_cover_drive" : "Ready", lofted ? 0.62f : 0.86f);
                yield return null;
                yield return Snap(cam, new[] { batAnim }, new[] { rig.Bat }, Turn(Chest(batAnim), -28f), 640, 800, Path.Combine(outDir, $"hero_bat_{f.Id}.png"), made, fill: 0.94f);
                rig.gameObject.SetActive(false);

                var xi = QuickMatch.LastSeasonXI(data, f.Id).XI;
                var stars = xi.OrderByDescending(p => p.StarPower).ThenByDescending(p => p.Impact).Take(5).ToList();
                var keeper = xi.FirstOrDefault(p => p.Role == Role.WicketKeeper) ?? stars[0];
                var bowler = xi.Where(p => p.Role == Role.Bowler).OrderByDescending(p => p.StarPower).FirstOrDefault() ?? stars[0];
                var cast = new CricketCast(refs);
                var bodies = cast.Side(stars.Concat(new[] { keeper, bowler }).Distinct(), f, "Art");
                foreach (var b in bodies.Values) Prepare(b.gameObject);
                Animator Of(Player p) => bodies[p.Id];

                // One figure a card; whatever he holds is made for the shot and destroyed after it.
                var cards = new (string key, Player who, Func<Animator, List<Transform>> pose)[]
                {
                    ("raise", stars[0], a => { Pose(a, 0.55f, "rhb_bat_fifty_raise_bat", "bat_fifty_raise_bat", "bat_acknowledge_applause"); return Props(Bat(a, true)); }),
                    ("bowl", bowler, a => { Pose(a, 0.42f, "bowler_appeal_lbw", "fielder_appeal_standing", "bowler_celebration_fist_pump_roar"); return Props(); }),
                    ("trophy", stars[0], a => { Pose(a, 0.6f, "trophy_raise", "rhb_bat_fifty_raise_bat"); return Props(Trophy(a)); }),
                    ("keeper", keeper, a => { cast.KeeperGear(a, f); Prepare(a.gameObject); Pose(a, 0.3f, "wk_stance_up_loop", "wk_stance_back_loop"); return Props(); }),
                };
                foreach (var (key, who, pose) in cards)
                {
                    var a = Of(who);
                    Solo(bodies.Values, a);
                    a.transform.SetPositionAndRotation(new Vector3(0, 0, 30), Quaternion.identity);
                    var props = pose(a);
                    yield return null;
                    yield return Snap(cam, new[] { a }, props, Turn(Chest(a), -30f), 400, 520, Path.Combine(outDir, $"mode_{key}_{f.Id}.png"), made);
                    foreach (var p in props) if (p != null) UnityEngine.Object.Destroy(p.gameObject);
                }
                yield return Hero(cam, stars.Select(Of).ToList(), bodies.Values, Path.Combine(outDir, $"hero_{f.Id}.png"), made);
                foreach (var b in bodies.Values) UnityEngine.Object.Destroy(b.gameObject);
                yield return null;
            }
            Sheet(made, Path.Combine(dir, "homeart_sheet.png"));
            log.WriteLine($"DONE homeart files={made.Count} dir={outDir}");
        }

        /// <summary>
        /// Five in a shallow arc, the middle one in front (nearest, so largest), the rest half a step
        /// behind and overlapping; relaxed — bats grounded at their sides — and the middle one with
        /// his bat raised. All turned to the camera.
        /// </summary>
        private static IEnumerator Hero(Camera cam, List<Animator> line, IEnumerable<Animator> all, string path, List<string> made)
        {
            Solo(all, null);
            var spots = new[] { (0f, 0f), (-0.78f, 0.55f), (0.78f, 0.55f), (-1.5f, 1.15f), (1.5f, 1.15f) };
            var props = new List<Transform>();
            for (var i = 0; i < line.Count; i++)
            {
                var a = line[i];
                a.gameObject.SetActive(true);
                a.transform.SetPositionAndRotation(new Vector3(spots[i].Item1, 0, 30 + spots[i].Item2), Quaternion.identity);
                if (i == 0)
                {
                    Pose(a, 0.5f, "bat_acknowledge_applause", "rhb_bat_acknowledge_applause", "captain_listen_nod_loop");
                    props.AddRange(Props(Bat(a, true)));
                }
                else
                {
                    Pose(a, 0.15f + 0.2f * i, "75_non_striker_idle_loop", "captain_listen_nod_loop", "captain_idle_relaxed_loop");
                    props.AddRange(Props(Bat(a, false, grounded: true)));
                }
                // Each one square to the camera (at −z), whatever frame his clip was made in.
                FaceTo(a, Vector3.back + new Vector3(-spots[i].Item1 * 0.15f, 0, 0));
            }
            // Props follow the turned bodies: placed again now they've turned.
            foreach (var p in props) if (p != null) UnityEngine.Object.Destroy(p.gameObject);
            props.Clear();
            for (var i = 0; i < line.Count; i++) props.AddRange(Props(i == 0 ? Bat(line[i], true) : Bat(line[i], false, grounded: true)));
            yield return null;
            yield return Snap(cam, line, props, Vector3.back, 900, 700, path, made, fill: 0.92f);
            foreach (var p in props) if (p != null) UnityEngine.Object.Destroy(p.gameObject);
        }
    }
}
