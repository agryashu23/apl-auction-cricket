using System;
using System.Collections;
using System.IO;
using System.Linq;
using Apl.Client.Menu;
using Apl.Client.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Dev
{
    /// <summary>
    /// The tournament test run (round 13): <c>APL.exe -apltourneyshots &lt;dir&gt; -aplmatchtest</c>.
    /// From the home page's TOURNAMENT card: last season's squads, 2 overs, START → the table →
    /// PLAY MATCH → the user's first fixture on the ground (the director plays it under
    /// -aplmatchtest), screenshots every 12 s to the result and the rewards screen →
    /// the rewards' CONTINUE → the table again with the result in it → the hub's sims to the end
    /// (<see cref="Season"/>: SIM MATCH card, SIM TO MY NEXT MATCH, SIM REST OF SEASON confirm, the
    /// champions page, the final standings). Writes tourneyshot.txt (clicks, the modes / sound /
    /// [APL tourney] / [APL xi] log lines, "DONE played=… finished=… champion=…").
    /// </summary>
    public sealed class TourneyShot : MonoBehaviour
    {
        private string _dir = "";
        private int _shot;
        private StreamWriter _log = null!;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-apltourneyshots");
            if (i < 0 || i + 1 >= args.Length || FindAnyObjectByType<TourneyShot>() != null) return;
            var go = new GameObject("TourneyShot");
            DontDestroyOnLoad(go);
            go.AddComponent<TourneyShot>()._dir = args[i + 1];
        }

        private void Start()
        {
            Directory.CreateDirectory(_dir);
            Application.runInBackground = true;
            _log = new StreamWriter(Path.Combine(_dir, "tourneyshot.txt")) { AutoFlush = true };
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type == LogType.Exception || type == LogType.Error || message.StartsWith("[APL modes]") || message.StartsWith("[APL sfx]") || message.StartsWith("[APL tourney]") || message.StartsWith("[APL xi]"))
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

        private static Button? Find(string name) => FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == name && b.interactable);

        private bool Click(string name)
        {
            var b = Find(name);
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] click {name}: {(b != null ? "ok" : "MISSING")}");
            b?.onClick.Invoke();
            return b != null;
        }

        /// <summary>A button inside the object named <paramref name="parent"/> (the home page's mode cards).</summary>
        private bool ClickIn(string parent)
        {
            var b = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(x => x.interactable && x.transform.parent != null && x.transform.parent.name == parent);
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] click in {parent}: {(b != null ? "ok" : "MISSING")}");
            b?.onClick.Invoke();
            return b != null;
        }

        private IEnumerator Run()
        {
            yield return Shot("home", 3f);
            if (!ClickIn("Mode_TOURNAMENT")) TournamentHub.Open(Dataset(), MatchMenu.SavedTeams(Dataset()).mine, 2);
            yield return Shot("hub_setup", 1.2f);
            Click("SrcSeason");
            Click("Overs_2");
            yield return Shot("hub_setup_2_overs", 0.8f);
            Click("Start");
            yield return Shot("table_start", 1.2f);
            Click("PlayNext");
            // The PLAYING XI with its 30 s clock (owner v26 #7): a shot, then CONFIRM.
            yield return Shot("playing_xi", 1.5f);
            Click("XiConfirm");
            // The ground: the director plays the fixture under -aplmatchtest; the target card's START if it's up.
            // The result card's CONTINUE is pressed by the test run itself (presentation-ui) and opens the
            // rewards screen; a CONTINUE still waiting after 8 s is pressed here.
            var deadline = Time.realtimeSinceStartup + 600f;
            var next = 0f;
            var continueSeen = -1f;
            while (FindAnyObjectByType<RewardCard>() == null && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                Click2("StartChase");
                if (Find("Continue") != null)
                {
                    if (continueSeen < 0) continueSeen = Time.realtimeSinceStartup;
                    else if (Time.realtimeSinceStartup - continueSeen > 8f) Click("Continue");
                }
                if (Time.realtimeSinceStartup < next) continue;
                next = Time.realtimeSinceStartup + 12f;
                yield return Shot("match", 0f);
            }
            var played = FindAnyObjectByType<RewardCard>() != null;
            yield return Shot("match_rewards", 3.5f);
            // The rewards screen's CONTINUE: a tournament fixture goes back to the table.
            Click("RewardCONTINUE ►");
            for (var t = 0f; t < 20f && FindAnyObjectByType<TournamentHub>() == null; t += 0.5f) yield return new WaitForSecondsRealtime(0.5f);
            var back = FindAnyObjectByType<TournamentHub>() != null;
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] back at the tournament table: {back}");
            yield return Shot("table_after_match", 2f);
            yield return Season((n, a) => Shot(n, a), Click, s => _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] {s}"));
            var t2 = Tournament.Load(Dataset());
            _log.WriteLine($"DONE played={played} table={FindAnyObjectByType<TournamentHub>() != null} fixturesPlayed={t2?.Fixtures.Count(f => f.Played) ?? -1} of {t2?.Fixtures.Count ?? 0} " +
                           $"finished={t2?.Finished ?? false} champion={t2?.Champion} you={t2?.UserFinish()} shots={_shot}");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(played && t2 != null && t2.Finished ? 0 : 2);
        }

        /// <summary>
        /// The hub's sims to the season's end (auction-r1 #4), from the table: the four buttons (shot) →
        /// SIM MATCH → its result card → CONTINUE → SIM TO MY NEXT MATCH → the table → SIM REST OF SEASON →
        /// the confirm → SIM ALL → the champions page → VIEW TABLE → the final standings. Knocked out
        /// already: SIM NEXT MATCH's card, then SIM REST OF SEASON (no confirm). Shared with AutoShot's
        /// -apltourneyafter (the same run on the auction's squads).
        /// </summary>
        public static IEnumerator Season(Func<string, float, IEnumerator> shot, Func<string, bool> click, Action<string> log)
        {
            if (FindAnyObjectByType<TournamentHub>() == null)
            {
                log("season: no tournament hub open");
                yield break;
            }
            yield return shot("table_sim_buttons", 0.8f);
            if (click("SimMatch"))
            {
                yield return shot("sim_match_card", 1f);
                click("SimCardOk");
                yield return shot("table_after_sim", 0.8f);
                if (click("SimToNext")) yield return shot("table_sim_to_next", 1f);
                if (click("SimRest"))
                {
                    yield return shot("sim_rest_confirm", 0.8f);
                    click("ConfirmOk");
                }
            }
            else if (click("SimNext"))
            {
                yield return shot("sim_next_card", 1f);
                click("SimCardOk");
                click("SimAll");
            }
            else click("SimAll");
            yield return shot("champions", 1.5f);
            click("ChampTable");
            yield return shot("final_standings", 1f);
        }

        /// <summary>A click that doesn't log when the button isn't there (polled).</summary>
        private void Click2(string name) => Find(name)?.onClick.Invoke();

        private static Apl.Core.Dataset? _data;
        private static Apl.Core.Dataset Dataset() => _data ??= Apl.Core.Dataset.FromJson(Resources.Load<TextAsset>("Data/apl_dataset").text);
    }
}
