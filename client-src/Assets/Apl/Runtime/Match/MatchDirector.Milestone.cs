using System;
using System.Collections;
using System.Linq;
using Apl.Client.Ui;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The batter's milestone (focus batch 1, BAT-8: "very important", never seen — 3-over games never reach
    /// a fifty). Test runs can start the user's openers on 47* (<c>-aplmilestone 50</c>) or 97*
    /// (<c>-aplmilestone 100</c>) so a boundary brings it up. The celebration: he turns to the dressing room
    /// and raises the bat (a hundred: the leap with the helmet coming off), then acknowledges the crowd all
    /// round; the crowd stands, the box calls it, a FIFTY / HUNDRED caption, and a slow broadcast orbit on him.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>-aplmilestone 50 | 100: the user's openers start this close to it (test runs only).</summary>
        private static readonly int MilestoneTest = ParseMilestoneTest();

        private bool _milestoneSet;

        /// <summary>-aplcelebration &lt;id or stem&gt; (test runs): every milestone celebrated with this one (Modes/Celebrations.cs ids, or a clip stem).</summary>
        private static readonly string? CelebrationTest = ParseCelebrationTest();

        private static string? ParseCelebrationTest()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-aplcelebration");
            if (i < 0 || i + 1 >= args.Length) return null;
            var k = Apl.Client.Modes.Celebrations.Find(args[i + 1]);
            return k != null ? k.Stem : args[i + 1];
        }

        /// <summary>
        /// Which celebration for this milestone: the test flag's; the user's own pick from THE VAULT when it's for this
        /// milestone (a hundred-only pick at the hundred, the rest at 50 / 150 / 200, as before); else one of the clips
        /// for it (Celebrations.Defaults: the hundred's set, the fifty's set), picked at random.
        /// </summary>
        private string PickCelebration(string level)
        {
            if (CelebrationTest != null) return CelebrationTest;
            var hundred = level == "100";
            var mine = Apl.Client.Modes.Locker.CelebrationClip(BattingSide.Id, "");
            if (!string.IsNullOrEmpty(mine))
            {
                var kind = Apl.Client.Modes.Celebrations.Find(mine);
                var forHundred = kind != null && kind.Milestone == 100;
                if (forHundred == hundred) return mine;
            }
            var pool = Apl.Client.Modes.Celebrations.Defaults(hundred ? 100 : 50);
            return pool[UnityEngine.Random.Range(0, pool.Length)];
        }

        private static int ParseMilestoneTest()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-aplmilestone");
            if (i < 0) return 0;
            // (-aplmilestone 50 | 100; with -aplcelebration <id> to see one of them: e.g. century_somersault.)
            return i + 1 < args.Length && args[i + 1] == "100" ? 100 : 50;
        }

        /// <summary>First ball of the user's innings under -aplmilestone: both openers on 47* (97*), 30 balls faced.</summary>
        private void ApplyMilestoneTest()
        {
            if (MilestoneTest == 0 || _milestoneSet || _match.Batting.TeamId != _bat.Id) return;
            _milestoneSet = true;
            foreach (var b in new[] { _match.OnStrike, _match.OffStrike })
            {
                b.Runs = MilestoneTest - 3;
                // Already past what he starts beyond: those levels (and the box's fifty talk) are done.
                _level[b.Player.Id] = b.Runs / 50 * 50;
                if (b.Runs >= 50) _fiftyTalk.Add(b.Player.Id);
                b.Balls = Math.Max(b.Balls, MilestoneTest == 100 ? 60 : 30);
            }
            Debug.Log($"[APL match] -aplmilestone: {_match.OnStrike.Player.Name} and {_match.OffStrike.Player.Name} start on {MilestoneTest - 3}*");
        }

        /// <summary>The highest fifty-run level each batter has been celebrated for (50, 100, 150...).</summary>
        private readonly System.Collections.Generic.Dictionary<string, int> _level = new System.Collections.Generic.Dictionary<string, int>();

        /// <summary>
        /// A batter crossing 50 / 100 / 150...: the right call for the right one — "fifty" at 50 only (the box's own
        /// line says the hundred), nothing extra at 150 / 200 but the crowd and the caption — and his celebration.
        /// A level he was already past when he came in (the test flag's 97*) is never called.
        /// </summary>
        private void MilestoneLevels()
        {
            foreach (var b in _match.Order)
            {
                var level = b.Runs / 50 * 50;
                _level.TryGetValue(b.Player.Id, out var had);
                if (level <= had) continue;
                _level[b.Player.Id] = level;
                if (level >= 100) _hundreds.Add(b.Player.Id);
                _fifties.Add(b.Player.Id);
                if (level == 50) StartCoroutine(After(2.2f, () => _comm.Say("fifty", important: true)));
                Sfx.Applause(1f);
                // (The user's batters celebrate the way THE VAULT's CELEBRATION says: Modes/Celebrations.cs.)
                if (_batters.TryGetValue(b.Player.Id, out var rig)) _salute = (rig, level.ToString());
            }
        }

        /// <summary>The celebration itself (called from Salute once the running is over); <paramref name="level"/> "50", "100", "150"...</summary>
        private IEnumerator Celebration(BatterRig rig, string level)
        {
            var hundred = level != "50";
            var name = _match.Order.FirstOrDefault(b => _batters.TryGetValue(b.Player.Id, out var r) && r == rig);
            // To the dressing room first.
            var toPav = -rig.transform.position + PavilionDir * 60f;
            toPav.y = 0;
            // The hundred's set at 100, the fifty's at 50 / 150 / 200 (BatterRig.Celebration.cs: its bat and helmet).
            var clip = PickCelebration(level);
            var seconds = rig.CelebrateMilestone(clip, toPav);
            if (seconds < 0f)
            {
                Debug.LogWarning($"[APL celebrate] {clip} isn't in the controller: the raise instead");
                clip = "bat_fifty_raise_bat";
                seconds = rig.CelebrateMilestone(clip, toPav);
                if (seconds < 0f) yield break;
            }
            var info = BatterRig.Celebrations.TryGetValue(clip, out var ci) ? ci : null;
            Debug.Log($"[APL celebrate] {rig.name} {level}: {clip} ({seconds:0.0} s)");
            Sfx.Applause(1f);
            Sfx.Cheer(hundred ? 1f : 0.8f);
            // v26 celebrations review: the match HUD off (MatchHud.CleanFeed), the caption a lower third under him, not
            // the centre banner over his head and torso (MatchHud.Milestone.cs).
            _hud.CleanFeed(true);
            if (name != null) _hud.LowerThird(level == "50" ? "FIFTY" : level == "100" ? "HUNDRED" : level, $"{name.Player.Name.ToUpperInvariant()}  ·  {name.Runs} ({name.Balls})", UiKit.Gold, 4f);
            // Test runs: the celebration on film (-aplshots).
            StartCoroutine(After(0.3f, () => Apl.Client.Dev.AutoShot.Moment("milestone_caption")));
            StartCoroutine(After(info != null ? info.KeySeconds : 0.9f, () => Apl.Client.Dev.AutoShot.Moment("milestone_raise")));
            // Each celebration's own frames (-aplcelebration): the bat let go, the helmet in the hand, the end.
            if (info != null)
            {
                StartCoroutine(After(info.KeySeconds, () => Apl.Client.Dev.AutoShot.Moment($"milestone_{clip}_key")));
                if (info.Release > 0) StartCoroutine(After((info.Release + 12) / 30f, () => Apl.Client.Dev.AutoShot.Moment($"milestone_{clip}_bat_down")));
                if (info.HelmetOff > 0) StartCoroutine(After((info.HelmetOff + 15) / 30f, () => Apl.Client.Dev.AutoShot.Moment($"milestone_{clip}_helmet_in_hand")));
                StartCoroutine(After(seconds - 0.2f, () => Apl.Client.Dev.AutoShot.Moment($"milestone_{clip}_end")));
            }
            var hips = Hips(rig);
            FrontOn(hips, toPav, info != null && info.Travels);
            // The owner's clips play out whole (their own crowd moment, the bat picked up again); the raise is short,
            // so then the bat to the crowd all round, as before — under the orbit.
            var ownWhole = info != null && clip != "bat_fifty_raise_bat";
            yield return new WaitForSeconds(ownWhole ? seconds : hundred ? 3.6f : 3.0f);
            // Then the slow broadcast orbit on him (chest height, 4.5 m out).
            var t0 = Time.time;
            Camera.Track(() =>
            {
                var a = (Time.time - t0) * 18f + 200f;
                var c = hips.position;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                return (new Vector3(c.x, 0, c.z) + dir * 4.5f + Vector3.up * 1.6f, c + Vector3.up * 0.35f);
            }, fov: 34f, smooth: 0.35f, cut: true);
            StartCoroutine(After(1.0f, () => Apl.Client.Dev.AutoShot.Moment("milestone_orbit")));
            if (!ownWhole)
            {
                rig.Celebrate("bat_acknowledge_applause", -toPav);
                Sfx.Applause(0.8f);
                yield return new WaitForSeconds(2.4f);
            }
            else yield return new WaitForSeconds(2.2f);
            _hud.HideLowerThird();
            _hud.CleanFeed(false);
            Camera.Show(MatchCamera.Mode.Broadcast);
        }

        /// <summary>
        /// The celebration front-on (v26 review: the gameplay camera had him from behind): from in front of his chest
        /// (he's turned to the dressing room), swung 30° toward the bowler's end — the mid-on / bowler's side — 7.5 m out,
        /// the whole of him head to toe with headroom, framed high enough to clear the lower third; following him. A
        /// celebration that travels (the somersault, ~4.5 m along his chest) is filmed side-on to the travel from 9 m,
        /// on the middle of it, wide enough for all of it.
        /// </summary>
        private void FrontOn(Transform hips, Vector3 chest, bool travels)
        {
            var f = new Vector3(chest.x, 0, chest.z);
            f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.left;
            var start = new Vector3(hips.position.x, 0, hips.position.z);
            if (travels)
            {
                var mid = start + f * 2.3f;
                // Side-on to the travel, from the bowler's side of it.
                var side = Vector3.Cross(Vector3.up, f);
                if (side.z < 0) side = -side;
                Camera.Track(() => (mid + side * 9f + Vector3.up * 1.4f, mid + Vector3.up * 1.1f), fov: 36f, smooth: 0.5f, cut: true);
                return;
            }
            var toward = Vector3.RotateTowards(f, Vector3.forward, 30f * Mathf.Deg2Rad, 0f);
            Camera.Track(() =>
            {
                var c = new Vector3(hips.position.x, 0, hips.position.z);
                // Look below his middle (0.95 m): his feet sit ~0.65 m above the frame's bottom edge, clear of the lower third.
                return (c + toward * 7.5f + Vector3.up * 1.15f, c + Vector3.up * 0.95f);
            }, fov: 25f, smooth: 0.45f, cut: true);
        }
    }
}
