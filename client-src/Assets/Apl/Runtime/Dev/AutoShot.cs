using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Dev
{
    /// <summary>
    /// Scripted play-through for checking a build without a person: start the
    /// player with <c>-aplshots &lt;dir&gt;</c> and it clicks through the menus,
    /// watches the ceremony, plays the auction (fast-forwarded), opens every
    /// results tab, saves screenshots and quits. Exceptions are counted into
    /// <c>&lt;dir&gt;/autoshot.txt</c>.
    /// </summary>
    public sealed class AutoShot : MonoBehaviour
    {
        private string _dir = "";
        private int _shot;
        private int _errors;
        private StreamWriter _log = null!;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-aplshots");
            if (i < 0 || i + 1 >= args.Length || FindAnyObjectByType<AutoShot>() != null) return;
            var go = new GameObject("AutoShot");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoShot>()._dir = args[i + 1];
        }

        private void Start()
        {
            _instance = this;
            Directory.CreateDirectory(_dir);
            // Keep running when the window loses focus (Windows players pause otherwise).
            Application.runInBackground = true;
            _log = new StreamWriter(Path.Combine(_dir, "autoshot.txt")) { AutoFlush = true };
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type != LogType.Exception && type != LogType.Error) return;
                _errors++;
                _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] {type}: {message}\n{stack}");
            };
            // Who ends the run (a quit nobody asked for shows its caller here).
            Application.quitting += () => _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] QUITTING (scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, timeScale {Time.timeScale}, focus {Application.isFocused})\n{Environment.StackTrace}");
            // Auction rater r1: every voice line logged v0.00 (the capture machine had VOICE at 0), so the
            // audio couldn't be judged. Captures run with MASTER and VOICE at 100%.
            Audio.AudioPrefs.SetLevel(Audio.AudioPrefs.Channel.Master, 1f);
            Audio.AudioPrefs.SetLevel(Audio.AudioPrefs.Channel.Voice, 1f);
            PlayerPrefs.SetInt("apl.voice", 1);
            // The ceremony / rules skip is remembered for players (HallDirector.SkipCeremonyKey): a capture always films them.
            PlayerPrefs.DeleteKey(Hall.HallDirector.SkipCeremonyKey);
            PlayerPrefs.DeleteKey(Hall.HallDirector.SkipRulesKey);
            PlayerPrefs.Save();
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] audio for capture: master {Audio.AudioPrefs.Level(Audio.AudioPrefs.Channel.Master):0.00}, voice {Audio.AudioPrefs.Level(Audio.AudioPrefs.Channel.Voice):0.00}; ceremony/rules skip memory cleared");
            StartCoroutine(Run());
        }

        private static AutoShot? _instance;

        /// <summary>
        /// A shot at a moment the game picks (match-play: the ball reaching a catcher's hands, the bails
        /// coming off) — the 1.3 s interval shots kept missing them. Nothing outside a -aplshots run.
        /// </summary>
        public static void Moment(string name)
        {
            var me = _instance;
            if (me == null || me._log == null) return;
            var file = Path.Combine(me._dir, $"{++me._shot:00}_moment_{name}.png");
            CaptureWriter.Screen(file);
            me._log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] moment {file}");
            // Area 3 evidence (-aplfieldcloseup): the existing catch / bails moments also get their close-ups.
            FieldCloseUp.OnMoment(name);
        }

        /// <summary>A -aplshots run is filming (moments are taken).</summary>
        public static bool Active => _instance != null && _instance._log != null;

        /// <summary>
        /// Area 3 close-ups (FieldCloseUp): the next file in this run's numbering, <c>NN_closeup_&lt;name&gt;.png</c>,
        /// logged in autoshot.txt like a moment so it maps to the log by time. Null outside a -aplshots run.
        /// </summary>
        public static string? CloseUpFile(string name)
        {
            var me = _instance;
            if (me == null || me._log == null) return null;
            var file = Path.Combine(me._dir, $"{++me._shot:00}_closeup_{name}.png");
            me._log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] closeup {file}");
            return file;
        }

        private void OnDestroy() => _log?.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] AutoShot destroyed\n{Environment.StackTrace}");

        /// <summary>
        /// Wait (real time, capped) until the hall camera is on <paramref name="shot"/>; with
        /// <paramref name="leaveFirst"/>, first wait for it to leave that shot (the next time round).
        /// </summary>
        private IEnumerator WaitShot(string shot, bool leaveFirst, float timeout)
        {
            var cam = FindAnyObjectByType<Apl.Client.Hall.HallCamera>();
            var end = Time.realtimeSinceStartup + timeout;
            if (cam == null) yield break;
            if (leaveFirst)
            {
                while (cam.Current == shot && Time.realtimeSinceStartup < end) yield return null;
            }
            while (cam.Current != shot && Time.realtimeSinceStartup < end) yield return null;
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] camera on {cam.Current} (waited for {shot})");
        }

        /// <summary>The player name on the SOLD strip while it is up, else null.</summary>
        private static string? SoldStripName()
        {
            foreach (var t in FindObjectsByType<Text>(FindObjectsSortMode.None))
            {
                if (!t.isActiveAndEnabled || !t.text.EndsWith("SOLD") || t.text == "UNSOLD") continue;
                // Stripe -> strip face: the name is the face's longest label.
                var face = t.transform.parent != null ? t.transform.parent.parent : null;
                if (face == null) continue;
                return face.GetComponentsInChildren<Text>().Where(x => x != t && !x.text.StartsWith("TO ")).Select(x => x.text).OrderByDescending(x => x.Length).FirstOrDefault();
            }
            return null;
        }

        private IEnumerator WaitSoldStrip(float timeout)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (SoldStripName() == null && Time.realtimeSinceStartup < end) yield return null;
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] SOLD strip {(SoldStripName() ?? "not seen")}");
        }

        private IEnumerator WaitShotPrefix(string prefix, float timeout)
        {
            var cam = FindAnyObjectByType<Apl.Client.Hall.HallCamera>();
            var end = Time.realtimeSinceStartup + timeout;
            if (cam == null) yield break;
            while (!cam.Current.StartsWith(prefix) && Time.realtimeSinceStartup < end) yield return null;
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] camera on {cam.Current} (waited for {prefix}*)");
        }

        private IEnumerator Shot(string name, float after = 0.8f)
        {
            yield return new WaitForSecondsRealtime(after);
            var file = Path.Combine(_dir, $"{++_shot:00}_{name}.png");
            CaptureWriter.Screen(file);
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] shot {file}");
            yield return null;
        }

        /// <summary>Second run after a short one: the save is there, RESUME picks it up.</summary>
        private IEnumerator ResumeRun()
        {
            yield return Shot("resume_title", 2.5f);
            ClickAuction();
            yield return new WaitForSecondsRealtime(0.5f);
            Click("Team_BLR");
            yield return new WaitForSecondsRealtime(0.5f);
            Click("Next");
            yield return Shot("resume_prompt", 1f);
            var resumed = Click("Resume");
            yield return Shot("resume_hall", 6f);
            yield return Shot("resume_lot", 8f);
            _log.WriteLine($"DONE resume={resumed} errors={_errors} shots={_shot}");
            _log.Flush();
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(resumed && _errors == 0 ? 0 : 2);
        }

        /// <summary>
        /// Friend room against a live server, with tools/room_bot.dart as the
        /// host: join with the code it writes to <paramref name="codeFile"/>,
        /// pick BLR, lock in retentions, PASS on the first lot, bid on the next.
        /// </summary>
        private IEnumerator OnlineRun(string codeFile)
        {
            var end = Time.realtimeSinceStartup + 60f;
            while (!File.Exists(codeFile) && Time.realtimeSinceStartup < end) yield return new WaitForSecondsRealtime(0.5f);
            var code = File.Exists(codeFile) ? File.ReadAllText(codeFile).Trim() : "";
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] room code '{code}'");
            yield return Shot("online_title", 2f);
            // The home page's PLAY ONLINE card (round 7+ home; the old ROOM button before it).
            if (!FindObjectsByType<Button>(FindObjectsSortMode.None).Any(b => b.name == "Room")) ClickIn("Mode_PLAY ONLINE");
            else Click("Room");
            yield return new WaitForSecondsRealtime(1.5f);
            var input = FindObjectsByType<InputField>(FindObjectsSortMode.None)
                .FirstOrDefault(f => f.placeholder is Text t && t.text == "ROOM CODE");
            if (input != null) input.text = code;
            yield return Shot("online_home", 0.5f);
            Click("Join");
            yield return WaitClickable("Team_BLR", 20f);
            Click("Team_BLR");
            yield return Shot("online_lobby", 1.5f);
            // The bot (host) starts once we're seated: retention page next.
            yield return WaitClickable("SubmitKeeps", 30f);
            yield return Shot("online_retention", 1f);
            Click("SubmitKeeps");
            end = Time.realtimeSinceStartup + 200f;
            while (FindAnyObjectByType<Hall.HallHud>() == null && Time.realtimeSinceStartup < end) yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("online_hall", 4f);
            // Lot 1: we pass (the room's SKIP button is PASS); the bot bids on.
            yield return WaitClickable("Skip", 40f);
            Click("Skip");
            yield return Shot("online_passed", 1.5f);
            yield return Shot("online_after_pass", 6f);
            // Lot 2: we bid.
            yield return WaitClickable("Bid", 60f);
            Click("Bid");
            yield return Shot("online_bid", 1.5f);
            yield return Shot("online_lot2", 10f);
            yield return Shot("online_later", 15f);
            _log.WriteLine($"DONE online errors={_errors} shots={_shot}");
            _log.Flush();
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(_errors == 0 ? 0 : 2);
        }

        /// <summary>
        /// -aplsimulate (after the first three lots of the normal run): the hub's REMAINING tab
        /// (current set first, then the sets to come), then SIMULATE >>| -> the confirm dialog ->
        /// SIMULATE, straight to the results. Log lines: [APL sets], [APL simulate].
        /// </summary>
        private IEnumerator SimulateRun()
        {
            Click("Hub");
            yield return new WaitForSecondsRealtime(0.5f);
            Click("HubTab_remaining");
            yield return Shot("hub_remaining", 0.8f);
            Click("HubClose");
            yield return new WaitForSecondsRealtime(0.5f);
            Click("Simulate");
            yield return Shot("simulate_confirm", 0.8f);
            Click("ConfirmOk");
            var end = Time.realtimeSinceStartup + 60f;
            while (FindAnyObjectByType<Menu.ResultsScreen>() == null && Time.realtimeSinceStartup < end) yield return null;
            yield return Shot("simulate_results", 1.5f);
            Click("Tab_squad");
            yield return Shot("simulate_squad", 1f);
            var finished = FindAnyObjectByType<Menu.ResultsScreen>() != null;
            // -apltourneyafter: PLAY A TOURNAMENT on these squads (log: [APL tourney] squads from auction …).
            if (Environment.GetCommandLineArgs().Contains("-apltourneyafter") && Click("Tournament"))
            {
                yield return Shot("tourney_setup_from_auction", 1.2f);
                Click("Start");
                yield return Shot("tourney_table_from_auction", 1.5f);
                if (Click("PlayNext")) yield return Shot("tourney_playing_xi", 1.5f);
                // Tournament dev (auction-r1 #4): back to the table, then the hub's sims on these auction squads to
                // the champions page (Dev/TourneyShot.Season: SIM MATCH card, SIM TO MY NEXT MATCH, SIM REST confirm).
                Click("XiBack");
                yield return new WaitForSecondsRealtime(0.8f);
                yield return TourneyShot.Season((n, a) => Shot("tourney_" + n, a), Click, s => _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] {s}"));
            }
            _log.WriteLine($"DONE simulate finished={finished} errors={_errors} shots={_shot}");
            _log.Flush();
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(finished && _errors == 0 ? 0 : 2);
        }

        /// <summary>
        /// Straight to the end: a whole Standard auction (retentions + every
        /// lot) settled between AI teams in an instant, then the results
        /// screen for BLR — squad ratings, squads, the simulated season.
        /// </summary>
        private IEnumerator ResultsDemo()
        {
            yield return new WaitForSecondsRealtime(2f);
            var data = Core.Dataset.FromJson(Resources.Load<TextAsset>("Data/apl_dataset").text);
            var format = data.Rules.Format("standard");
            const long seed = 20261021;
            var retentions = data.Franchises.ToDictionary(f => f.Id, f => Core.Ai.ChooseRetentions(f.Id, data.Players, data.Rules, format));
            var engine = new Core.AuctionEngine(data.Rules, format, data.Franchises, data.Players, seed, retentions);
            Core.Ai.RunAuction(engine, Core.Ai.CreateBidders(engine, seed * 31));
            foreach (var t in engine.Teams.Values)
            {
                _log.WriteLine($"  {t.Id}: {t.Size} players, purse left {t.Purse} L");
            }
            foreach (var m in FindObjectsByType<Menu.MainMenu>(FindObjectsSortMode.None)) Destroy(m.gameObject);
            Menu.ResultsScreen.Create(data, engine, "BLR", seed * 7919 + 13);
            yield return Shot("results_ratings", 2f);
            Click("Tab_squad");
            yield return Shot("results_squad", 1f);
            Click("Tab_season");
            yield return Shot("results_season", 2f);
            _log.WriteLine($"DONE results errors={_errors} shots={_shot}");
            _log.Flush();
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(_errors == 0 ? 0 : 2);
        }

        /// <summary>
        /// Quick match: PLAY MATCH → 2 overs → the ground; the director bats by
        /// itself (-aplmatchtest), shots taken along the way until the result card.
        /// </summary>
        private IEnumerator MatchRun()
        {
            yield return Shot("match_title", 2.5f);
            // Home page → QUICK MATCH card → the teams page → NEXT → match setup (2 overs) → PLAY.
            if (!ClickIn("Mode_QUICK MATCH")) Click("Match");
            yield return Shot("match_teams", 1.2f);
            // Owner v26 team pick: step the opponent's carousel once, peek at its XI, close it.
            if (Click("Car_Opp_Next")) yield return Shot("match_teams_next", 0.8f);
            if (Click("Xi_Opp"))
            {
                yield return Shot("match_xi_opp", 1f);
                Click("XiClose");
            }
            Click("Next");
            yield return Shot("match_setup", 1.2f);
            if (!Click("Opt_OVERS_0")) Click("Overs_2");
            yield return null;
            // -aplovers N (batting capture: 30+ balls for the shot-direction log) overrides the 2 overs picked above
            var args = Environment.GetCommandLineArgs();
            var ov = Array.IndexOf(args, "-aplovers");
            var overs = ov >= 0 && ov + 1 < args.Length && int.TryParse(args[ov + 1], out var o) && o > 0 ? o : 0;
            if (!Click("Play")) Click("MatchPlay");
            if (overs > 0) Match.MatchLaunch.Overs = overs;      // after PLAY (it stores the menu's overs), before the scene loads
            // -aplvenue <id> (round 13: a second venue in the match test): the ground, set the same way.
            var vi = Array.IndexOf(args, "-aplvenue");
            if (vi >= 0 && vi + 1 < args.Length && Apl.Core.Play.Venue.Find(args[vi + 1]) != null) Match.MatchLaunch.VenueId = args[vi + 1];
            var end = Time.realtimeSinceStartup + 30f;
            while (FindAnyObjectByType<Match.MatchDirector>() == null && Time.realtimeSinceStartup < end) yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("match_target", 2f);
            Click("StartChase");
            end = Time.realtimeSinceStartup + 480f * Math.Max(1f, overs / 2f);
            var n = 0; var paused = false;
            while (Time.realtimeSinceStartup < end && !FindObjectsByType<Button>(FindObjectsSortMode.None).Any(b => b.name == "PlayAgain"))
            {
                yield return Shot($"match_play_{n++:00}", 1.3f);
                if (!paused && n >= 30 && Click("Pause"))
                {
                    paused = true;
                    yield return Shot("match_pause", 0.8f);
                    // Owner v26 #14 / #21: every pause page on film, and the quit confirm (cancelled).
                    if (Click("Pause_Gameplay")) { yield return Shot("match_pause_gameplay", 0.6f); Click("Back"); }
                    if (Click("Pause_Match")) { yield return Shot("match_pause_match", 0.6f); Click("Back"); }
                    if (Click("Pause_Sound")) { yield return Shot("match_pause_sound", 0.6f); var snd = GameObject.Find("SoundSettings"); if (snd != null) Destroy(snd); }
                    if (Click("QuitToMenu")) { yield return Shot("match_quit_confirm", 0.6f); Click("ConfirmCancel"); }
                    yield return Shot("match_pause_back", 0.4f);
                    Click("Resume");
                }
            }
            yield return Shot("match_result", 1.5f);
            // The page behind CONTINUE and the reward card over it (round 13 coverage; MatchHud.ResultCard.cs).
            if (Click("Continue"))
            {
                yield return Shot("after_match", 1.2f);
                yield return Shot("after_match_reward", 2.5f);
            }
            _log.WriteLine($"DONE match errors={_errors} shots={_shot}");
            _log.Flush();
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(_errors == 0 ? 0 : 2);
        }

        /// <summary>
        /// Every ground (-aplstadiums): the menu's ground picker, then each
        /// stadium from above, the opening wide shot, the broadcast view, the
        /// pavilion from the middle and the view beyond the stands.
        /// </summary>
        private IEnumerator StadiumRun()
        {
            yield return Shot("tour_title", 2.5f);
            if (!ClickIn("Mode_QUICK MATCH")) Click("Match");
            yield return Shot("tour_teams", 1f);
            Click("Next");
            yield return Shot("tour_menu", 1f);
            if (!Click("Next")) Click("GroundNext");
            yield return Shot("tour_menu_next", 0.6f);
            foreach (var v in Apl.Core.Play.Venue.All)
            {
                Match.MatchLaunch.MyTeam = "BLR";
                Match.MatchLaunch.Opponent = "MBH";
                Match.MatchLaunch.VenueId = v.Id;
                UnityEngine.SceneManagement.SceneManager.LoadScene(Match.MatchLaunch.GroundScene);
                yield return new WaitForSecondsRealtime(2.5f);
                foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
                var mc = FindAnyObjectByType<Match.MatchCamera>();
                if (mc == null) continue;
                mc.enabled = false;
                var cam = mc.GetComponent<Camera>();
                void Put(Vector3 pos, Vector3 look, float fov)
                {
                    cam.transform.position = pos;
                    cam.transform.LookAt(look);
                    cam.fieldOfView = fov;
                }
                var at = Match.StadiumLook.For(v.Id).SceneryAt * Mathf.Deg2Rad;
                var view = new Vector3(Mathf.Sin(at), 0, Mathf.Cos(at));
                Put(new Vector3(-120, 85, -160), new Vector3(0, 0, 10), 45);
                yield return Shot(v.Id + "_aerial", 0.5f);
                // The Match Setup picker's image (tools/stadium_previews.py → Resources/Ui/Stadiums): a high
                // three-quarter aerial from opposite the signature view — midway between two floodlight
                // towers, so no pole crosses the middle, and the scenery is beyond the bowl, not in front —
                // with the lamp glare off so it doesn't wash the frame.
                var from = -view;
                Put(from * 200 + Vector3.up * 130, new Vector3(0, -2, 0) + view * 6, 46); // ~32° down: the whole bowl, the far skyline at the top
                var glare = GameObject.Find("FloodGlare");
                if (glare != null) glare.SetActive(false);
                yield return Shot(v.Id + "_preview", 0.4f);
                if (glare != null) glare.SetActive(true);
                Put(new Vector3(0, 26, -62), new Vector3(0, 0, 4), 40);
                yield return Shot(v.Id + "_wide", 0.4f);
                Put(new Vector3(0.5f, 9.5f, 64), new Vector3(0, 1.2f, -7.5f), 11);
                yield return Shot(v.Id + "_broadcast", 0.4f);
                Put(new Vector3(8, 1.7f, 0), new Vector3(-60, 9, 0), 62);
                yield return Shot(v.Id + "_pavilion", 0.4f);
                Put(-view * 30 + Vector3.up * 14, view * 500 + Vector3.up * 70, 58);
                yield return Shot(v.Id + "_view", 0.4f);
            }
            _log.WriteLine($"DONE stadiums errors={_errors} shots={_shot}");
            _log.Flush();
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(_errors == 0 ? 0 : 2);
        }

        /// <summary>
        /// The kits (-aplcast): both sides and the umpire lined up on the
        /// square in their kits and gear, photographed in groups and close.
        /// </summary>
        private IEnumerator CastRun()
        {
            Match.MatchLaunch.MyTeam = "MBH";
            Match.MatchLaunch.Opponent = "CHC";
            UnityEngine.SceneManagement.SceneManager.LoadScene(Match.MatchLaunch.GroundScene);
            yield return new WaitForSecondsRealtime(3f);
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
            var mc = FindAnyObjectByType<Match.MatchCamera>();
            var director = FindAnyObjectByType<Match.MatchDirector>();
            var cast = GameObject.Find("Cast");
            if (mc == null || director == null || cast == null)
            {
                _log.WriteLine("Exception: no cast in the ground scene");
                _errors++;
                Application.Quit(2);
                yield break;
            }
            director.StopAllCoroutines();
            director.enabled = false;
            mc.enabled = false;
            var cam = mc.GetComponent<Camera>();
            var people = new System.Collections.Generic.List<Animator>();
            foreach (Transform t in cast.transform) people.Add(t.GetComponent<Animator>());
            // Two rows across the square, facing the camera (at -z).
            for (var i = 0; i < people.Count; i++)
            {
                var a = people[i];
                a.gameObject.SetActive(true);
                var row = i < 12 ? 0 : 1;
                var col = row == 0 ? i : i - 12;
                a.transform.SetPositionAndRotation(new Vector3((col - 5.5f) * 1.1f, 0, 20 + row * 2.2f), Quaternion.LookRotation(Vector3.back));
                a.Play("Ready", 0, 0.3f);
            }
            yield return new WaitForSecondsRealtime(0.5f);
            void Put(Vector3 pos, Vector3 look, float fov)
            {
                cam.transform.position = pos;
                cam.transform.LookAt(look);
                cam.fieldOfView = fov;
            }
            Put(new Vector3(0, 2.2f, 8), new Vector3(0, 1.2f, 21), 52);
            yield return Shot("cast_all", 0.4f);
            for (var g = 0; g < 4; g++)
            {
                var x = (g % 2 == 0 ? -3.3f : 3.3f);
                var z = g < 2 ? 20f : 22.2f;
                Put(new Vector3(x, 1.5f, z - 4.2f), new Vector3(x, 1.1f, z), 40);
                yield return Shot($"cast_group_{g}", 0.4f);
            }
            // Every body there is, in one kit, three rows (to spot hats and odd clothes).
            foreach (var a in people) a.gameObject.SetActive(false);
            var all = new Match.CricketCast(FindAnyObjectByType<Match.GroundRefs>()!);
            var data = Apl.Core.Dataset.FromJson(Resources.Load<TextAsset>("Data/apl_dataset").text);
            var team = data.Franchises.First(f => f.Id == "KOC");
            var bodies = new System.Collections.Generic.List<Animator>();
            for (var i = 0; i < all.Models; i++) bodies.Add(all.Model(i, team));
            // A row of twelve at a time, the rest out of the way.
            for (var row = 0; row * 12 < bodies.Count; row++)
            {
                for (var i = 0; i < bodies.Count; i++)
                {
                    var a = bodies[i];
                    a.gameObject.SetActive(i / 12 == row);
                    a.transform.SetPositionAndRotation(new Vector3((i % 12 - 5.5f) * 1.1f, 0, 20), Quaternion.LookRotation(Vector3.back));
                    a.Play("Ready", 0, 0.3f);
                }
                yield return new WaitForSecondsRealtime(0.4f);
                foreach (var x in new[] { -3.3f, 3.3f })
                {
                    Put(new Vector3(x, 1.5f, 15.8f), new Vector3(x, 1.1f, 20), 40);
                    yield return Shot($"bodies_{row}_{(x < 0 ? "left" : "right")}", 0.4f);
                }
            }
            _log.WriteLine($"DONE cast errors={_errors} shots={_shot}");
            _log.Flush();
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(_errors == 0 ? 0 : 2);
        }

        /// <summary>
        /// The shot gallery (-aplgallery all|name,name,…): the striker alone at the
        /// crease, every batting clip held just before, at and just after its
        /// contact frame (the stance loop at three points), photographed from his
        /// off side and from the bowler's end — the motion reviewer's evidence.
        /// tools/motions/gallery_sheet.py tiles each clip's six shots into one sheet.
        /// </summary>
        private IEnumerator GalleryRun(string list)
        {
            Match.MatchLaunch.MyTeam = "MBH";
            Match.MatchLaunch.Opponent = "CHC";
            UnityEngine.SceneManagement.SceneManager.LoadScene(Match.MatchLaunch.GroundScene);
            yield return new WaitForSecondsRealtime(3f);
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
            var mc = FindAnyObjectByType<Match.MatchCamera>();
            var director = FindAnyObjectByType<Match.MatchDirector>();
            if (mc == null || director == null)
            {
                _log.WriteLine("Exception: no director in the ground scene");
                _errors++;
                Application.Quit(2);
                yield break;
            }
            var leftHanded = list.StartsWith("lhb:");
            if (leftHanded) list = list.Substring(4);
            var rig = director.GalleryBatter(leftHanded);
            director.enabled = false;
            mc.enabled = false;
            var cam = mc.GetComponent<Camera>();
            var home = rig.transform.position;
            var off = leftHanded ? -1f : 1f;
            void Put(Vector3 pos, Vector3 look, float fov)
            {
                cam.transform.position = pos;
                cam.transform.LookAt(look);
                cam.fieldOfView = fov;
            }
            var names = list == "all"
                ? new[] { "00_stance_idle_loop" }.Concat(Match.CricketClips.Shots.Select(s => s.Name)).ToArray()
                : list.Split(',');
            foreach (var raw in names)
            {
                var name = raw.Trim();
                if (name.Length == 0) continue;
                var clipName = leftHanded && !name.StartsWith("lhb_") ? "lhb_" + name : name;
                var shot = Match.CricketClips.Find(name.StartsWith("lhb_") ? name.Substring(4) : name);
                var times = shot != null
                    ? new[] { (shot.ContactTime - 0.3f) / shot.Length, shot.ContactTime / shot.Length, (shot.ContactTime + 0.3f) / shot.Length }
                    : new[] { 0.25f, 0.5f, 0.75f };
                for (var i = 0; i < times.Length; i++)
                {
                    rig.GalleryPose(clipName, times[i]);
                    // A few frames so IK, the bat and the first sample of a clip have all settled.
                    for (var w = 0; w < 4; w++) yield return null;
                    rig.GalleryPose(clipName, times[i]);
                    yield return null;
                    yield return null;
                    // What survived the retargeting: knee bend and hip height as rendered, to set
                    // against the generator's numbers (tools/motions/body_metrics.py) for the same frame.
                    var an = rig.GetComponent<Animator>();
                    float Knee(HumanBodyBones up, HumanBodyBones low, HumanBodyBones foot)
                    {
                        var a = an.GetBoneTransform(up);
                        var b = an.GetBoneTransform(low);
                        var c = an.GetBoneTransform(foot);
                        return a == null || b == null || c == null ? -1f : Vector3.Angle(b.position - a.position, c.position - b.position);
                    }
                    var hipsT = an.GetBoneTransform(HumanBodyBones.Hips);
                    // The bat checkpoint numbers: blade angle (toe below horizontal +), where the face
                    // points (0 = at the bowler, + toward the batter's off side), bottom hand height and
                    // reach from the hips, head nod (neck→head tipped from vertical).
                    var bat = rig.Bat;
                    var toe = bat.rotation * Vector3.down;
                    var face = bat.forward;
                    var offSign = leftHanded ? -1f : 1f;
                    var faceYaw = Mathf.Atan2(face.x * offSign, face.z) * Mathf.Rad2Deg;
                    var bottom = an.GetBoneTransform(leftHanded ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                    var neck = an.GetBoneTransform(HumanBodyBones.Neck);
                    var headB = an.GetBoneTransform(HumanBodyBones.Head);
                    var reach = bottom != null && hipsT != null ? Vector3.Distance(new Vector3(bottom.position.x, 0, bottom.position.z), new Vector3(hipsT.position.x, 0, hipsT.position.z)) : -1f;
                    var nod = neck != null && headB != null ? Vector3.Angle(headB.position - neck.position, Vector3.up) : -1f;
                    _log.WriteLine($"[pose] {clipName} t{i} ({times[i]:0.000}): knees L {Knee(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot):0} R {Knee(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot):0} deg, hips y {(hipsT != null ? hipsT.position.y : -1):0.000} | bat toe-down {Mathf.Asin(Mathf.Clamp(-toe.y, -1, 1)) * Mathf.Rad2Deg:0} deg, face {faceYaw:0} deg, hand y {(bottom != null ? bottom.position.y : -1):0.00} reach {reach:0.00} | head nod {nod:0} deg");
                    // From his off side (face and bat), and from the bowler's end.
                    Put(home + new Vector3(4.2f * off, 1.35f, 0.3f), home + new Vector3(0, 0.95f, 0.1f), 30);
                    yield return Shot($"{clipName}_t{i}_side", 0.2f);
                    Put(home + new Vector3(0.9f * off, 1.7f, 6.5f), home + new Vector3(0, 0.9f, 0), 26);
                    yield return Shot($"{clipName}_t{i}_front", 0.2f);
                }
            }
            _log.WriteLine($"DONE gallery errors={_errors} shots={_shot} clips={names.Length}");
            _log.Flush();
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(_errors == 0 ? 0 : 2);
        }

        /// <summary>
        /// Keep raising on the lot on the block until it closes (up to 25 bids), filming the call chip
        /// (GOING ONCE… / GOING TWICE…) as it comes up, then the SOLD strip. Logs whether we won it.
        /// </summary>
        private IEnumerator WinLot(float timeout)
        {
            var end = Time.realtimeSinceStartup + timeout;
            var bids = 1;
            var nextBidAt = 0f;
            var calls = new System.Collections.Generic.HashSet<string>();
            while (Time.realtimeSinceStartup < end && SoldStripName() == null && !TextShown("UNSOLD", exact: true, skipChip: true))
            {
                var chip = GameObject.Find("CallChip");
                var call = chip != null ? chip.GetComponentInChildren<Text>()?.text ?? "" : "";
                if (call.StartsWith("GOING") && calls.Add(call))
                {
                    yield return Shot(call.Contains("ONCE") ? "call_going_once" : "call_going_twice", 0f);
                }
                if (Time.realtimeSinceStartup >= nextBidAt && bids < 25)
                {
                    var bid = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == "Bid" && b.interactable && b.gameObject.activeInHierarchy);
                    var label = bid != null ? bid.GetComponentInChildren<Text>().text : "";
                    if (bid != null && label.StartsWith("BID "))
                    {
                        bid.onClick.Invoke();
                        bids++;
                        _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] win-lot: raised ({label}), bid #{bids}");
                    }
                    nextBidAt = Time.realtimeSinceStartup + 0.3f;
                }
                yield return null;
            }
            var won = TextShown("TO YOU", exact: false, skipChip: true);
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] win-lot: {bids} bids, SOLD strip {SoldStripName() ?? "not seen"}, human win: {(won ? "yes" : "no")}");
            yield return Shot(won ? "lot_won" : "lot_lost", 0.15f);
        }

        /// <summary>An active label reading <paramref name="s"/> (exactly, or starting with it).</summary>
        private static bool TextShown(string s, bool exact, bool skipChip) =>
            FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.isActiveAndEnabled && (exact ? t.text == s : t.text.StartsWith(s)) &&
                !(skipChip && t.transform.parent != null && t.transform.parent.name == "CallChip"));

        /// <summary>The Auction Hub, every tab in order (SETS / REMAINING / MY SQUAD / TEAMS / SOLD / UNSOLD …), then back.</summary>
        private IEnumerator HubTabs()
        {
            yield return WaitClickable("Hub", 10f);
            if (!Click("Hub")) yield break;
            yield return new WaitForSecondsRealtime(0.6f);
            var tabs = FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b => b.name.StartsWith("HubTab_"))
                .OrderBy(b => b.transform.GetSiblingIndex()).Select(b => b.name).ToList();
            foreach (var t in tabs)
            {
                Click(t);
                yield return Shot("hub_" + t.Substring("HubTab_".Length), 0.6f);
            }
            Click("HubClose");
            yield return new WaitForSecondsRealtime(0.5f);
        }

        private IEnumerator WaitClickable(string name, float timeout)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < end &&
                   !FindObjectsByType<Button>(FindObjectsSortMode.None).Any(b => b.name == name && b.interactable && b.gameObject.activeInHierarchy))
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        /// <summary>A card's button (on its "Face") found by the card's own name.</summary>
        private bool ClickIn(string parent)
        {
            var button = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.interactable && b.transform.parent != null && b.transform.parent.name == parent);
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] click in {parent}: {(button != null ? "ok" : "MISSING")}");
            if (button == null) return false;
            button.onClick.Invoke();
            return true;
        }

        /// <summary>THE AUCTION from the home page (round 7's mode card; the old SOLO button before it).</summary>
        private bool ClickAuction() => FindObjectsByType<Button>(FindObjectsSortMode.None).Any(b => b.name == "Solo") ? Click("Solo") : ClickIn("Mode_THE AUCTION");

        private bool Click(string name)
        {
            var button = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == name && b.interactable);
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] click {name}: {(button != null ? "ok" : "MISSING")}");
            if (button == null) return false;
            button.onClick.Invoke();
            return true;
        }

        private IEnumerator Run()
        {
            // A room test drives the game itself (Dev/RoomTest.cs); AutoShot only counts errors.
            if (RoomTest.Active) yield break;
            if (Environment.GetCommandLineArgs().Contains("-aplresumetest"))
            {
                yield return ResumeRun();
                yield break;
            }
            if (Environment.GetCommandLineArgs().Contains("-aplhomeart"))
            {
                // The home page's art from our own cast (Dev/HomeArt.cs, presentation-ui).
                yield return HomeArt.Run(_dir, _log);
                _log.Flush();
                Application.Quit(0);
                yield break;
            }
            if (Environment.GetCommandLineArgs().Contains("-aplcast"))
            {
                yield return CastRun();
                yield break;
            }
            if (Environment.GetCommandLineArgs().Contains("-aplstadiums"))
            {
                yield return StadiumRun();
                yield break;
            }
            var gallery = Array.IndexOf(Environment.GetCommandLineArgs(), "-aplgallery");
            if (gallery >= 0)
            {
                var galleryArgs = Environment.GetCommandLineArgs();
                yield return GalleryRun(gallery + 1 < galleryArgs.Length ? galleryArgs[gallery + 1] : "all");
                yield break;
            }
            if (Environment.GetCommandLineArgs().Contains("-aplmatchtest"))
            {
                yield return MatchRun();
                yield break;
            }
            if (Environment.GetCommandLineArgs().Contains("-aplresultsdemo"))
            {
                yield return ResultsDemo();
                yield break;
            }
            var online = Array.IndexOf(Environment.GetCommandLineArgs(), "-aplonline");
            if (online >= 0)
            {
                yield return OnlineRun(Environment.GetCommandLineArgs()[online + 1]);
                yield break;
            }
            // A save from an earlier run would put the resume prompt in the way.
            SoloSave.Delete();
            yield return Shot("title", 2.5f);
            // -aplteam MBH / -aplformat mega|mini: play as another franchise / format (auction checks).
            // The format is picked ONCE, on the home page's MEGA AUCTION / MINI AUCTION buttons (rater r1 #10):
            // MINI goes franchise → retention; MEGA goes franchise → options (format shown, not asked) → retention.
            var cmd = Environment.GetCommandLineArgs();
            var ti = Array.IndexOf(cmd, "-aplteam");
            var fi = Array.IndexOf(cmd, "-aplformat");
            var formatArg = fi >= 0 && fi + 1 < cmd.Length ? cmd[fi + 1].ToLowerInvariant() : "mini";
            var homeButton = formatArg == "mega" ? "SoloMega" : formatArg == "mini" || formatArg == "standard" ? "SoloMini" : null;
            if (homeButton == null || !Click(homeButton)) ClickAuction();
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] solo format: {formatArg} (home button {homeButton ?? "none"})");
            yield return Shot("teams");
            Click(ti >= 0 && ti + 1 < cmd.Length ? "Team_" + cmd[ti + 1] : "Team_BLR");
            yield return new WaitForSecondsRealtime(0.5f);
            if (FindObjectsByType<Button>(FindObjectsSortMode.None).Any(b => b.name == "Next"))
            {
                yield return Shot("options", 0.3f);
                // Only the hero card's route (no home format) shows the format buttons.
                if (homeButton == null) Click("Format_" + formatArg);
                Click("RETENTION");
                Click("Next");
            }
            else _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] options page skipped (format chosen on the home page)");
            yield return Shot("retention_empty");
            var keeps = FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b => b.name.StartsWith("Keep_")).Select(b => b.name).OrderBy(n => n).Take(3).ToList();
            foreach (var k in keeps)
            {
                Click(k);
                yield return null;
            }
            // A mini auction starts from the AI's picks: toggling three more can push it over the limit and
            // grey START out (the run then stalls on this page) — undo them if so (audio-modes, round 13).
            if (!FindObjectsByType<Button>(FindObjectsSortMode.None).Any(b => b.name == "Start" && b.interactable))
            {
                foreach (var k in keeps)
                {
                    Click(k);
                    yield return null;
                }
                _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] retention: the extra keeps broke the limit, undone");
            }
            yield return Shot("retention_picked");
            Click("Start");

            // Ceremony: a few beats, then skip.
            // The owners' welcome speech runs ~16 s before the first team walks on.
            yield return Shot("ceremony_intro", 3f);
            yield return Shot("ceremony_walk", 16f);
            yield return Shot("ceremony_pose", 3.5f);
            // A second team's photo too (the pose framing must hold for every group size).
            yield return WaitShot("ceremony_hero", leaveFirst: true, 40f);
            yield return Shot("ceremony_pose2", 1.5f);
            Click("SkipCeremony");

            // Rules briefing: let two rules be read, then skip.
            yield return Shot("rules", 9f);
            Click("SkipRules");

            // First lots at real speed: reveal, card, bidding.
            yield return Shot("lot_reveal", 2f);
            // Mid-glide frame of stage -> auctioneer (the purse boards must not vanish on screen).
            yield return WaitShot("auctioneer", leaveFirst: false, 6f);
            yield return Shot("lot_glide_mid", 0.3f);
            yield return Shot("lot_card", 0.6f);
            // BID is live during the reveal now; our opening bid ends it (rater r1 #2). Then we keep
            // raising until we WIN the lot (rater r1 test gap: no human win was ever filmed), filming the
            // GOING ONCE / GOING TWICE chip on the way (r1 #9).
            yield return WaitClickable("Bid", 20f);
            Click("Bid");
            yield return Shot("lot_my_bid", 0.6f);
            yield return WinLot(120f);
            yield return Shot("lot_result", 2.5f);

            // The TEAMS drop-down (purses of every franchise), then every Auction Hub tab (r1 test gaps).
            if (Click("TeamsTab"))
            {
                yield return Shot("teams_dropdown", 0.8f);
                Click("TeamsTab");
            }
            yield return HubTabs();

            // PASS on the next lot: settled and SOLD at once (hammer, card, buyers).
            yield return WaitClickable("Pass", 40f);
            Click("Pass");
            // The SOLD moment itself: the hammer with the strip up, then the buyers' table.
            yield return WaitSoldStrip(20f);
            yield return Shot("pass_hammer", 0.15f);
            yield return Shot("pass_card", 0.5f);
            yield return WaitShotPrefix("buyers_", 6f);
            yield return Shot("pass_buyers", 0.9f);
            // SKIP on the one after: jumps to the settled price, then the closing calls.
            yield return WaitClickable("Skip", 40f);
            Click("Skip");
            yield return Shot("skip_settled", 1.2f);
            yield return Shot("skip_calls", 3f);
            yield return Shot("skip_result", 4f);
            if (Environment.GetCommandLineArgs().Contains("-aplsimulate"))
            {
                yield return SimulateRun();
                yield break;
            }
            if (Environment.GetCommandLineArgs().Contains("-aplshort"))
            {
                _log.WriteLine($"DONE short errors={_errors} shots={_shot}");
                _log.Flush();
                yield return new WaitForSecondsRealtime(1f);
                Application.Quit(_errors == 0 ? 0 : 2);
                yield break;
            }

            // Fast-forward the rest, peeking now and then.
            Time.timeScale = 16f;
            Time.maximumDeltaTime = 2f;
            // A mega auction has ~284 lots (mini ~121): more time before giving up.
            var deadline = Time.realtimeSinceStartup + (formatArg == "mega" ? 1500f : 600f);
            var peek = 0;
            var longName = false;
            var rtmFilmed = false;
            while (FindAnyObjectByType<Menu.ResultsScreen>() == null && Time.realtimeSinceStartup < deadline)
            {
                // Watch every frame for one SOLD strip with a long name (the shrink-to-fit check).
                for (var until = Time.realtimeSinceStartup + 1f; Time.realtimeSinceStartup < until;)
                {
                    // Our own RTM prompt (one of our released players sold elsewhere): film it at normal
                    // speed and use the card (r1 test gap: no human RTM prompt was filmed).
                    if (!rtmFilmed && GameObject.Find("RtmPrompt") is GameObject rtm && rtm.activeInHierarchy)
                    {
                        rtmFilmed = true;
                        Time.timeScale = 1f;
                        yield return Shot("rtm_prompt", 0.4f);
                        Click("Yes");
                        yield return Shot("rtm_used", 1.6f);
                        Time.timeScale = 16f;
                    }
                    if (!longName && SoldStripName() is string n && n.Length >= 18)
                    {
                        longName = true;
                        _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] long-name SOLD strip: {n}");
                        yield return Shot("sold_long_name", 0f);
                    }
                    yield return null;
                }
                if (peek++ % 25 == 0) yield return Shot("auction_ff", 0f);
            }
            Time.timeScale = 1f;
            Time.maximumDeltaTime = 0.3333f;
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] rtm prompt filmed: {(rtmFilmed ? "yes" : "no (no released player of ours went elsewhere while we held a card)")}");
            // Every results tab, each rank labelled (r1 #3; the RATINGS, STANDINGS and AWARDS tabs were never filmed).
            yield return Shot("results_podium", 1.5f);
            foreach (var tab in new[] { "standings", "awards", "ratings", "squad", "season" })
            {
                if (Click("Tab_" + tab)) yield return Shot("results_" + tab);
            }

            var finished = FindAnyObjectByType<Menu.ResultsScreen>() != null;
            // The auction's career XP (audio-modes: Modes/AuctionRewards.cs) on its rewards screen, for the rater.
            if (Apl.Client.Modes.RewardCard.ShowPending()) yield return Shot("auction_rewards", 3f);
            _log.WriteLine($"DONE finished={finished} errors={_errors} shots={_shot}");
            _log.Flush();
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(finished && _errors == 0 ? 0 : 2);
        }
    }
}
