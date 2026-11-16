using System;
using System.Collections;
using System.IO;
using System.Linq;
using Apl.Client.Audio;
using Apl.Client.Modes;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Dev
{
    /// <summary>
    /// The modes' test run (round 9): <c>APL.exe -aplmodesshots &lt;dir&gt; -aplmatchtest</c>
    /// screenshots the home page, the tournament hub, CHALLENGES, the LOCKER's four tabs,
    /// SOUND (sliders, one moved), the challenge detail panel (card tapped, CAREER tab),
    /// then plays challenge "Last-Over Thriller" from its detail panel's PLAY (the director auto-bats under
    /// -aplmatchtest) and shoots it every 12 s until the rewards screen, which it
    /// shoots before quitting. <c>-aplmodesunlock</c> first grants enough XP to unlock
    /// the locker and equips a design in each slot, so the user's batters show them.
    /// Every sound played is in the player log ("[APL sfx]", SoundLog).
    /// </summary>
    public sealed class ModesShot : MonoBehaviour
    {
        private string _dir = "";
        private int _shot;
        private StreamWriter _log = null!;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-aplmodesshots");
            if (i < 0 || i + 1 >= args.Length || FindAnyObjectByType<ModesShot>() != null) return;
            var go = new GameObject("ModesShot");
            DontDestroyOnLoad(go);
            go.AddComponent<ModesShot>()._dir = args[i + 1];
        }

        private void Start()
        {
            Directory.CreateDirectory(_dir);
            Application.runInBackground = true;
            _log = new StreamWriter(Path.Combine(_dir, "modesshot.txt")) { AutoFlush = true };
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type == LogType.Exception || type == LogType.Error || message.StartsWith("[APL sfx]") || message.StartsWith("[APL modes]") || message.StartsWith("[APL locker]") || message.StartsWith("[APL vault]"))
                    _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] {type}: {message}");
            };
            StartCoroutine(Run());
        }

        private IEnumerator Shot(string name, float after = 1f)
        {
            yield return new WaitForSecondsRealtime(after);
            var file = Path.Combine(_dir, $"{++_shot:000}_{name}.png");
            ScreenCapture.CaptureScreenshot(file);
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] shot {file}");
            yield return null;
        }

        private void Click(string name)
        {
            var b = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == name && x.interactable);
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] click {name}: {(b != null ? "ok" : "MISSING")}");
            b?.onClick.Invoke();
        }

        /// <summary>
        /// Closes every root canvas of that name (v26test4: GameObject.Find("Locker") found the career
        /// panel's OPEN LOCKER button on the challenges page, not the vault's canvas, so the vault
        /// stayed on top for the challenge shots). Deactivated at once, destroyed at the frame's end.
        /// </summary>
        private static void Close(string canvas)
        {
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.name != canvas) continue;
                c.gameObject.SetActive(false);
                Destroy(c.gameObject);
            }
        }

        private IEnumerator Run()
        {
            var data = Dataset.FromJson(Resources.Load<TextAsset>("Data/apl_dataset").text);
            if (Environment.GetCommandLineArgs().Contains("-aplmodesunlock"))
            {
                Progression.RecordEvent((6000, "Test run: unlock the locker"));
                foreach (var id in new[] { "bat_flame", "helmet_chrome", "pads_black", "celebrate_salute", "title_matchwinner" })
                    if (Cosmetics.Find(id) is Cosmetic c) Progression.Equip(c);
                // Then to 15 XP short of the next level that unlocks something, so the challenge's XP
                // levels up and the rewards screen plays the full reveal (LEVEL UP, the new item).
                var next = Cosmetics.All.Where(c => c.Level > Progression.Level).Select(c => c.Level).DefaultIfEmpty(0).Min();
                if (next > 0)
                {
                    var at = 0;
                    for (var l = 1; l < next; l++) at += Progression.XpToNext(l);
                    if (at - 15 > Progression.Xp) Progression.RecordEvent((at - 15 - Progression.Xp, "Test run: to the edge of a level"));
                }
                RewardCard.Pending = null;
                // The home page was built before the XP: build it again so its LEVEL chip shows it.
                UnityEngine.SceneManagement.SceneManager.LoadScene(0);
                yield return new WaitForSecondsRealtime(3f);
            }
            yield return Shot("home", 3f);
            // The tournament hub (spot check), then away.
            Apl.Client.Menu.TournamentHub.Open(data, "BLR", 5);
            yield return Shot("tournament_hub", 1.5f);
            Close("TournamentHub");
            ChallengesPage.Open(data);
            yield return Shot("challenges");
            LockerPage.Open();
            yield return Shot("locker_bat");
            foreach (var tab in new[] { "Tab_Helmet", "Tab_Pads", "Tab_Celebration", "Tab_Title" })
            {
                Click(tab);
                yield return Shot("locker_" + tab.Substring(4).ToLowerInvariant(), 0.6f);
            }
            Close("Locker");
            SoundSettings.Open();
            yield return Shot("sound_levels");
            // The sliders (owner v26 #4): one moved as a finger would, then put back.
            var music = FindObjectsByType<Apl.Client.Ui.VolumeSlider>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == "Slider_Music");
            var before = music != null ? music.Value : 1f;
            music?.Set(0.37f, true);
            yield return Shot("sound_slider_37", 0.5f);
            music?.Set(before, true);
            if (music != null) AudioPrefs.SetLevel(AudioPrefs.Channel.Music, before, true);
            Close("SoundSettings");
            // The home gear's organised settings (owner v26 #14): the list, then GAMEPLAY.
            Apl.Client.Menu.GameSettings.Open();
            yield return Shot("settings_list");
            Click("Settings_Gameplay");
            yield return Shot("settings_gameplay");
            Close("GameSettings");
            // The challenge from the home page's CHALLENGES page, as a player starts it (owner v26 #12):
            // tap its card (selects it, details on the left), then the left panel's PLAY. The page opened
            // fresh, so it's on top with nothing over it.
            foreach (var l in FindObjectsByType<LockerPage>(FindObjectsSortMode.None)) { l.gameObject.SetActive(false); Destroy(l.gameObject); }
            ChallengesPage.Open(data);
            yield return null;
            Click("Card_c02");
            yield return Shot("challenge_selected_c02", 0.8f);
            Click("Tab_Career");
            yield return Shot("challenge_career_tab", 0.8f);
            Click("Card_c01");
            yield return Shot("challenge_selected_c01", 0.8f);
            Click("ChallengePlay");
            yield return Shot("challenge_banner", 4f);
            var deadline = Time.realtimeSinceStartup + 420f;
            var last = -1f;
            var nextShot = Time.realtimeSinceStartup + 12f;
            while (FindAnyObjectByType<RewardCard>() == null && Time.realtimeSinceStartup < deadline)
            {
                // Watch for the rewards screen every quarter second (its reveal lasts ~4 s), a play shot every 12 s.
                yield return new WaitForSecondsRealtime(0.25f);
                if (Time.realtimeSinceStartup < nextShot) continue;
                nextShot = Time.realtimeSinceStartup + 12f;
                yield return Shot("challenge_play", 0f);
                // A clock that isn't moving is the round-10 freeze: say so in the log.
                if (Mathf.Approximately(Time.time, last)) _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] CLOCK STOPPED at {Time.time:0.00}s (time x{Time.timeScale:0.00})");
                last = Time.time;
            }
            var rewarded = FindAnyObjectByType<RewardCard>() != null;
            // The reveal mid-way (XP counting, bar filling), then settled.
            yield return Shot("reward_reveal", 0.6f);
            yield return Shot("reward_reveal", 1.2f);
            yield return Shot("reward_reveal", 1.2f);
            yield return Shot("reward_screen", 3f);
            // The batting nets last (auto-batting under -aplmatchtest): two looks.
            Apl.Client.Match.NetsLaunch.Open(Apl.Client.Match.NetsLaunch.Kind.Batting, "BLR", "MBH", Apl.Core.Play.Venue.HomeOf("BLR").Id);
            yield return Shot("nets_batting", 8f);
            yield return Shot("nets_batting", 12f);
            _log.WriteLine($"DONE reward={rewarded} level={Progression.Level} xp={Progression.Xp} shots={_shot}");
            yield return new WaitForSecondsRealtime(1f);            Application.Quit(rewarded ? 0 : 2);
        }
    }
}
