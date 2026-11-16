using System;
using System.Collections;
using System.IO;
using System.Linq;
using Apl.Client.Hall;
using Apl.Client.Menu;
using Apl.Client.Online;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Dev
{
    /// <summary>
    /// Multiplayer room test (TEST RUNS ONLY): <c>-aplroomtest &lt;url&gt; &lt;role&gt; [team]</c>, role <c>host</c> or
    /// <c>join:&lt;code&gt;</c> (<c>join:auto</c> reads the code the host wrote to the code file). It clicks through
    /// PLAY ONLINE, creates or joins a room, picks the team, readies up, (host) starts, locks in retentions,
    /// then plays by itself: bids up to about a player's worth, otherwise PASSes; SIMULATE and TAKE BACK
    /// CONTROL at set lots; the host opens the host panel and ends the auction at a set lot. With
    /// <c>-aplshots &lt;dir&gt;</c> it saves room_NN_&lt;step&gt;.png along the way. Every step is logged as
    /// <c>[APL room] test: …</c>. Several windows run at once against one local server.
    /// Options: -aplroomplayers N (host waits for N people, default 2), -aplroomformat mega|standard,
    /// -aplroomsim LOT, -aplroomtakeback LOT, -aplroomend LOT (host), -aplroomdrop LOT (reconnect test),
    /// -aplroomname NAME, -aplroomcodefile PATH, -aplroomstay (don't quit at the end).
    /// </summary>
    public sealed class RoomTest : MonoBehaviour
    {
        private string _url = "", _role = "host", _code = "";
        private string? _team;
        private string _dir = "";
        private int _shot;
        private bool _host;
        private int _players = 2, _simLot, _takeBackLot, _endLot, _dropLot;
        private string _format = "standard";
        private string _name = "";
        private string _codeFile = "";
        private RoomClient? _client;
        private bool _passedShot, _botShot, _otherBotShot, _hostPanelShot, _dropped, _simulated, _tookBack, _ended, _controlsShot, _badgeShot;
        private int _lastLot = -1;
        private float _actAt;
        private readonly System.Random _rng = new System.Random();

        /// <summary>A room test run (AutoShot steps aside for it).</summary>
        public static bool Active => Array.IndexOf(Environment.GetCommandLineArgs(), "-aplroomtest") >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Active || FindAnyObjectByType<RoomTest>() != null) return;
            var go = new GameObject("RoomTest");
            DontDestroyOnLoad(go);
            go.AddComponent<RoomTest>();
        }

        private static string? Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : null;
        }

        private static int IntArg(string name, int fallback) => int.TryParse(Arg(name), out var v) ? v : fallback;

        private void Log(string text) => Debug.Log("[APL room] test: " + text);

        private void Start()
        {
            Application.runInBackground = true;
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-aplroomtest");
            _url = i + 1 < args.Length ? args[i + 1] : RoomConfig.LocalDefault;
            _role = i + 2 < args.Length && !args[i + 2].StartsWith("-") ? args[i + 2] : "host";
            _team = i + 3 < args.Length && !args[i + 3].StartsWith("-") ? args[i + 3].ToUpperInvariant() : null;
            _host = _role.Equals("host", StringComparison.OrdinalIgnoreCase);
            if (!_host && _role.StartsWith("join", StringComparison.OrdinalIgnoreCase))
            {
                var colon = _role.IndexOf(':');
                _code = colon >= 0 ? _role.Substring(colon + 1).Trim().ToUpperInvariant() : "AUTO";
            }
            _dir = Arg("-aplshots") ?? "";
            if (_dir.Length > 0) Directory.CreateDirectory(_dir);
            _players = IntArg("-aplroomplayers", 2);
            _format = Arg("-aplroomformat") ?? "standard";
            // Defaults: a guest simulates at lot 3 and takes back at lot 5; the host ends the auction at lot 8.
            _simLot = IntArg("-aplroomsim", _host ? 0 : 3);
            _takeBackLot = IntArg("-aplroomtakeback", _simLot > 0 ? _simLot + 2 : 0);
            _endLot = IntArg("-aplroomend", _host ? 8 : 0);
            _dropLot = IntArg("-aplroomdrop", 0);
            // Names a real player would type (the rater read "Guest CHC" as the game's default).
            var pool = new[] { "Kabir", "Meera", "Rohan", "Priya", "Vikram", "Ananya", "Dev", "Isha", "Neel" };
            var pick = (_team ?? UnityEngine.Random.Range(0, 99).ToString()).Sum(ch => ch) % pool.Length;
            _name = Arg("-aplroomname") ?? (_host ? "Arjun" : pool[pick]);
            _codeFile = Arg("-aplroomcodefile") ?? Path.Combine(Path.GetTempPath(), "apl_room_code.txt");
            Log($"start role={_role} team={_team ?? "auto"} url={_url} server={RoomConfig.SocketUrl(RoomConfig.ServerAddress)} players={_players} format={_format} sim={_simLot} takeBack={_takeBackLot} end={_endLot} drop={_dropLot} shots={(_dir.Length > 0 ? _dir : "off")}");
            StartCoroutine(Run());
        }

        // ------------------------------------------------------------ helpers

        private IEnumerator Shot(string name, float after = 0.8f)
        {
            yield return new WaitForSecondsRealtime(after);
            if (_dir.Length == 0)
            {
                Log($"step {name} (no -aplshots)");
                yield break;
            }
            var file = Path.Combine(_dir, $"room_{++_shot:00}_{name}.png");
            CaptureWriter.Screen(file);
            Log($"shot {name} -> {file}");
            yield return null;
        }

        private static Button? Find(string name) =>
            FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == name && b.gameObject.activeInHierarchy);

        private bool Click(string name)
        {
            var b = Find(name);
            var ok = b != null && b.interactable;
            Log($"click {name}: {(ok ? "ok" : b == null ? "MISSING" : "disabled")}");
            if (ok) b!.onClick.Invoke();
            return ok;
        }

        private bool ClickIn(string parent)
        {
            var b = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(x => x.interactable && x.transform.parent != null && x.transform.parent.name == parent);
            Log($"click in {parent}: {(b != null ? "ok" : "MISSING")}");
            if (b == null) return false;
            b.onClick.Invoke();
            return true;
        }

        private IEnumerator WaitFor(Func<bool> done, float timeout, string what)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!done() && Time.realtimeSinceStartup < end) yield return new WaitForSecondsRealtime(0.2f);
            if (!done()) Log($"TIMEOUT waiting for {what} ({timeout:0}s)");
        }

        private static InputField? Field(string placeholder) =>
            FindObjectsByType<InputField>(FindObjectsSortMode.None).FirstOrDefault(f => f.placeholder is Text t && t.text == placeholder);

        private bool HiddenFastSkip()
        {
            var visible = new[] { "Skip", "Hold" }.Where(n => Find(n) != null).ToList();
            Log($"check FAST/SKIP hidden: {(visible.Count == 0 ? "yes" : "NO, visible: " + string.Join(",", visible))}");
            return visible.Count == 0;
        }

        // ------------------------------------------------------------- script

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(2.5f);
            // Home → PLAY ONLINE.
            if (Find("Room") != null) Click("Room");
            else ClickIn("Mode_PLAY ONLINE");
            yield return WaitFor(() => FindAnyObjectByType<OnlineLobby>() != null, 10, "the online page");
            _client = FindAnyObjectByType<RoomClient>();
            var lobby = FindAnyObjectByType<OnlineLobby>();
            if (_client == null || lobby == null)
            {
                Log("FAILED: no online page");
                yield break;
            }
            Log($"online page up, player {_client.PlayerId}, socket {_client.Url}");
            var nameField = Field("Your name");
            if (nameField != null) nameField.text = _name;
            _client.SetName(_name, save: false);

            if (_host) yield return HostLobby(lobby);
            else yield return GuestLobby();
            if (_client.Code == null)
            {
                Log("FAILED: not in a room");
                yield break;
            }

            // Retention (mini auction / retention on): lock in the suggested keeps.
            yield return WaitFor(() => _client.Phase == "retention" || _client.Phase == "auction" || FindAnyObjectByType<HallHud>() != null, 120, "the start");
            if (_client.Phase == "retention")
            {
                yield return WaitFor(() => Find("SubmitKeeps") != null, 20, "the retention page");
                yield return Shot("retention", 0.8f);
                Click("SubmitKeeps");
            }
            yield return WaitFor(() => FindAnyObjectByType<HallHud>() != null, 240, "the hall");
            var director = FindAnyObjectByType<HallDirector>();
            Log($"hall up as {_client.MyTeam} (host={_client.IsHost})");
            yield return WaitFor(() => _client.Auction?.Engine.CurrentLot != null, 60, "the first lot");
            yield return Shot("auction_bid_controls", 2f);
            HiddenFastSkip();

            // Play until the results screen.
            var end = Time.realtimeSinceStartup + 3600f;
            while (FindAnyObjectByType<ResultsScreen>() == null && Time.realtimeSinceStartup < end)
            {
                yield return Play(director);
                yield return new WaitForSecondsRealtime(0.25f);
            }
            yield return Results();
        }

        private IEnumerator HostLobby(OnlineLobby lobby)
        {
            try { if (File.Exists(_codeFile)) File.Delete(_codeFile); } catch { /* another window's */ }
            Click("Create");
            yield return new WaitForSecondsRealtime(0.6f);
            lobby.SetCreateChoices(_format, _format == "mega" ? false : (bool?)null, 10, _team ?? "MBH");
            yield return Shot("create_room", 0.6f);
            Click("CreateRoom");
            yield return WaitFor(() => _client!.Code != null, 30, "the room");
            if (_client!.Code == null) yield break;
            Log($"room created: code {_client.Code} (written to {_codeFile})");
            try { File.WriteAllText(_codeFile, _client.Code); } catch (Exception ex) { Log("could not write the code file: " + ex.Message); }
            yield return Shot("lobby", 1.2f);
            // Wait for the others to come in and pick.
            yield return WaitFor(() => _client.Seats.Count >= _players && _client.Seats.All(s => s.Team != null), 240, $"{_players} seated players");
            Log($"lobby: {string.Join(", ", _client.Seats.Select(s => $"{s.Name}={s.Team}"))}");
            yield return Shot("lobby_team_picker", 1.2f);
            // The host's lobby controls: lock the room (nobody else needed), the 15 s timer, back to 10 s.
            Click("HostLock");
            // The "room is locked" toast in the lobby slot (under the host controls, clear of the code card and tiles).
            yield return WaitFor(() => RoomToast.Showing, 3, "the lock toast");
            Log($"toast '{RoomToast.Last}' showing={RoomToast.Showing} at {RoomToast.Where}");
            yield return Shot("lobby_host_controls_locked", 0.4f);
            Click("HostLock");
            if (_client.SupportsReady && Find("Ready") is Button ready && ready.interactable)
            {
                Click("Ready");
                yield return WaitFor(() => _client.Seats.All(s => s.Ready), 120, "everyone ready");
            }
            yield return WaitFor(() => Find("StartRoom")?.interactable == true, 180, "START enabled (everyone ready)");
            Log($"lobby ready: {string.Join(", ", _client.Seats.Select(s => $"{s.Name}={s.Team}{(s.Ready ? " READY" : "")}"))}");
            yield return Shot("lobby_all_ready_start", 1.2f);
            Click("StartRoom");
        }

        private IEnumerator GuestLobby()
        {
            var code = _code;
            if (code == "AUTO" || code.Length == 0)
            {
                // Wait for the host's code file (written after it creates the room).
                yield return WaitFor(() => File.Exists(_codeFile), 180, "the host's code file");
                yield return new WaitForSecondsRealtime(0.5f);
                try { code = File.ReadAllText(_codeFile).Trim(); } catch { code = ""; }
            }
            Log($"joining {code}");
            var field = Field("ROOM CODE");
            if (field != null) field.text = code;
            yield return Shot("join_code", 0.5f);
            Click("Join");
            yield return WaitFor(() => _client!.Code != null, 30, "the room");
            if (_client!.Code == null)
            {
                // Stale code file (an older room): read it again once.
                yield return new WaitForSecondsRealtime(3f);
                try { code = File.ReadAllText(_codeFile).Trim(); } catch { /* keep */ }
                field = Field("ROOM CODE");
                if (field != null) field.text = code;
                Click("Join");
                yield return WaitFor(() => _client.Code != null, 30, "the room (second try)");
                if (_client.Code == null) yield break;
            }
            yield return Shot("lobby", 1.2f);
            // Our team, or the first free one; a taken team shows its toast instead.
            var team = _team;
            if (team == null || _client.Seats.Any(s => s.Team == team && s.Id != _client.PlayerId))
            {
                if (team != null) Click("Team_" + team);
                team = _client.Data.Franchises.Select(f => f.Id).FirstOrDefault(id => _client.Seats.All(s => s.Team != id));
            }
            yield return new WaitForSecondsRealtime(0.5f + (float)_rng.NextDouble());
            Click("Team_" + team);
            yield return WaitFor(() => _client.MyTeam != null, 15, "our team");
            if (_client.MyTeam == null && _client.Seats.Count > 0)
            {
                // Lost a race for that team: the next free one.
                team = _client.Data.Franchises.Select(f => f.Id).FirstOrDefault(id => _client.Seats.All(s => s.Team != id));
                Click("Team_" + team);
                yield return WaitFor(() => _client.MyTeam != null, 15, "our team (second pick)");
            }
            Log($"picked {_client.MyTeam}");
            yield return Shot("lobby_team_picker", 1.2f);
            // Tap a team someone else holds: its "taken" toast, in the lobby slot.
            var takenTeam = _client.Seats.FirstOrDefault(s => s.Team != null && s.Id != _client.PlayerId)?.Team;
            if (takenTeam != null && Click("Team_" + takenTeam))
            {
                Log($"toast '{RoomToast.Last}' showing={RoomToast.Showing} at {RoomToast.Where}");
                yield return Shot("lobby_toast_taken", 0.4f);
            }
            if (_client.SupportsReady)
            {
                yield return WaitFor(() => Find("Ready")?.interactable == true, 10, "READY enabled");
                Click("Ready");
                yield return WaitFor(() => _client.Me?.Ready == true, 10, "our READY");
                Log($"ready = {_client.Me?.Ready}");
                yield return Shot("lobby_ready", 0.8f);
            }
        }

        /// <summary>One look at the auction: new lot bookkeeping, scripted moments, then bid or pass.</summary>
        private IEnumerator Play(HallDirector? director)
        {
            var client = _client!;
            var auction = client.Auction;
            if (auction == null || !auction.IsLoaded) yield break;
            var engine = auction.Engine;
            var lot = engine.CurrentLot;
            var me = client.MyTeam;
            if (me == null) yield break;
            var number = lot?.Number ?? _lastLot;
            if (lot != null && number != _lastLot)
            {
                _lastLot = number;
                _actAt = Time.realtimeSinceStartup + 1.2f + (float)_rng.NextDouble() * 1.5f;
                Log($"lot {number}: {lot.Player.Id} base {lot.Player.BasePrice}, worth {Worth(engine, lot.Player)}");
            }

            // Host: the host panel on film once (both pages), early in the auction.
            if (client.IsHost && !_hostPanelShot && director?.HostControls != null && lot != null)
            {
                _hostPanelShot = true;
                director.HostControls.SetOpen(true);
                director.HostControls.Page("controls");
                yield return Shot("host_panel_controls", 0.8f);
                director.HostControls.Page("players");
                yield return Shot("host_panel_players", 0.8f);
                director.HostControls.Page("controls");
                yield return new WaitForSecondsRealtime(0.5f);
                // Bid timer 15 s: its room-news toast on film in the hall slot (under the seat strip), then back to 10 s.
                if (Click("HostTimer_15"))
                {
                    yield return WaitFor(() => RoomToast.Showing && RoomToast.Last.Contains("15"), 4, "the timer toast");
                    Log($"toast '{RoomToast.Last}' showing={RoomToast.Showing} at {RoomToast.Where}");
                    yield return Shot("hall_toast_under_seat_strip", 0.3f);
                    yield return new WaitForSecondsRealtime(0.5f);
                    Click("HostTimer_10");
                }
                // Pause and resume once, on film, with the panel's own PAUSE ALL button.
                yield return new WaitForSecondsRealtime(0.5f);
                if (!Click("HostPause")) client.HostPause(true);
                yield return WaitFor(() => client.Paused, 5, "the pause");
                Log($"paused: toast '{RoomToast.Last}' at {RoomToast.Where}; HUD menu button reads '{Find("Pause")?.GetComponentInChildren<Text>().text}'");
                yield return Shot("host_paused", 0.8f);
                yield return new WaitForSecondsRealtime(0.5f);
                if (!Click("HostPause")) client.HostPause(false);
                yield return WaitFor(() => !client.Paused, 5, "the resume");
                director.HostControls.SetOpen(false);
            }

            // The seat strip with stacked badges (HOST + a status on one chip, another status elsewhere), once; else at lot 7.
            if (!_badgeShot && director?.SeatStrip != null && lot != null)
            {
                var seats = client.Seats.Where(s => s.Team != null).ToList();
                bool Status(RoomSeat s) => s.State != SeatState.Online || client.PassedThisLot.Contains(s.Team!) || lot.Passed.Contains(s.Team!) || lot.LeaderId == s.Team;
                var hostWithStatus = seats.Any(s => (s.Host || (string?)client.Room?["host"] == s.Id) && Status(s));
                var others = seats.Count(Status);
                if ((hostWithStatus && others >= 2) || number >= 7)
                {
                    _badgeShot = true;
                    Log($"seat strip: {director.SeatStrip.Describe()}");
                    yield return Shot("seat_strip_badges", 0.3f);
                }
            }

            // Scripted moments.
            if (_simLot > 0 && !_simulated && number >= _simLot)
            {
                _simulated = true;
                if (Click("Simulate"))
                {
                    yield return Shot("simulate_confirm", 0.6f);
                    Click("ConfirmOk");
                    yield return WaitFor(() => client.MyTeamOnAutopilot, 10, "the autopilot");
                    yield return new WaitForSecondsRealtime(0.5f);
                    LogBotPanel(director);
                    yield return Shot("bot_is_bidding_for_you", 0.7f);
                    _botShot = true;
                }
                yield break;
            }
            if (_takeBackLot > 0 && _simulated && !_tookBack && number >= _takeBackLot)
            {
                _tookBack = true;
                Click("TakeBack");
                yield return WaitFor(() => !client.MyTeamOnAutopilot, 10, "control back");
                yield return Shot("took_back_control", 1f);
                yield break;
            }
            if (_dropLot > 0 && !_dropped && number >= _dropLot)
            {
                _dropped = true;
                client.TestDrop();
                yield return Shot("reconnecting", 0.4f);
                yield return WaitFor(() => client.Connected && !client.Reconnecting, 30, "the reconnect");
                yield return Shot("seat_resumed", 1f);
                yield break;
            }
            if (client.IsHost && _endLot > 0 && !_ended && number >= _endLot)
            {
                _ended = true;
                Log($"host ends the auction at lot {number}");
                // Through the panel: the END AUCTION confirm (what really happens) on film.
                var ended = false;
                if (director?.HostControls != null)
                {
                    director.HostControls.SetOpen(true);
                    director.HostControls.Page("controls");
                    yield return new WaitForSecondsRealtime(0.6f);
                    if (Click("HostEnd"))
                    {
                        yield return Shot("host_end_confirm", 0.6f);
                        ended = Click("ConfirmOk");
                    }
                }
                if (!ended) client.HostEndAuction();
                yield break;
            }
            // Someone else's team on a bot: their badge on the seat strip, once.
            if (!_otherBotShot && client.Seats.Any(s => s.Id != client.PlayerId && s.Team != null && s.BotPlaying))
            {
                _otherBotShot = true;
                yield return Shot("bot_badge_other_team", 0.8f);
            }
            if (lot == null || client.MyTeamOnAutopilot || client.Paused) yield break;
            if (auction.Phase != LivePhase.Bidding || Time.realtimeSinceStartup < _actAt) yield break;
            if (!_controlsShot)
            {
                _controlsShot = true;
                yield return Shot("bid_controls_bidding", 0.1f);
            }

            var passed = client.PassedThisLot.Contains(me) || lot.Passed.Contains(me);
            if (passed || lot.LeaderId == me) yield break;
            var reason = engine.CheckBid(me);
            var worth = Worth(engine, lot.Player);
            // A guest passes the first lot outright (the PASSED state on film); otherwise bid up to ~worth.
            var mustPass = !_host && !_passedShot;
            var limit = worth * (0.75 + 0.4 * ((lot.Player.Id.GetHashCode() & 0xff) / 255.0));
            if (!mustPass && reason == null && engine.NextBidAmount <= limit)
            {
                Log($"lot {number}: BID {engine.NextBidAmount} (limit {limit:0})");
                Click("Bid");
                _actAt = Time.realtimeSinceStartup + 0.8f + (float)_rng.NextDouble() * 2.2f;
                yield break;
            }
            Log($"lot {number}: PASS ({(mustPass ? "scripted" : reason != null ? reason.ToString() : $"next {engine.NextBidAmount} over limit {limit:0}")})");
            if (Click("Pass") && !_passedShot)
            {
                _passedShot = true;
                yield return WaitFor(() => Find("Pass")?.GetComponentInChildren<Text>().text == "PASSED", 3, "PASSED");
                Log($"lot {number}: my controls read '{Find("Bid")?.GetComponentInChildren<Text>().text}' / '{Find("Pass")?.GetComponentInChildren<Text>().text}', lot still open = {client.Auction?.Engine.CurrentLot?.Number == number}");
                yield return Shot("passed_state", 0.6f);
            }
            _actAt = Time.realtimeSinceStartup + 2f;
        }

        /// <summary>Where the BOT IS BIDDING panel sits, and that it covers neither the bidder box nor the current bid.</summary>
        private void LogBotPanel(HallDirector? director)
        {
            var panel = director?.RoomBotPanel;
            if (panel == null || !panel.gameObject.activeInHierarchy)
            {
                Log("bot panel: MISSING");
                return;
            }
            var mine = WorldRect(panel);
            var hud = FindAnyObjectByType<HallHud>();
            var clashes = new[] { "Bidder", "CurrentBid", "Card", "Balance" }
                .Where(n => hud != null && hud.Root.Find(n) is RectTransform t && t.gameObject.activeInHierarchy && WorldRect(t).Overlaps(mine))
                .ToList();
            Log($"bot panel at {mine} (screen px); clear of bidder / current bid / card: {(clashes.Count == 0 ? "yes" : "NO, overlaps " + string.Join(",", clashes))}; BID/PASS hidden: {Find("Bid") == null && Find("Pass") == null}");
        }

        private static Rect WorldRect(RectTransform t)
        {
            var c = new Vector3[4];
            t.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        private static int Worth(AuctionEngine engine, Player p)
        {
            try { return (int)Math.Round(Market.ReputationPrice(p, engine.Format)); }
            catch { return p.BasePrice * 2; }
        }

        private IEnumerator Results()
        {
            var results = FindAnyObjectByType<ResultsScreen>();
            if (results == null)
            {
                Log("FAILED: no results screen");
                yield break;
            }
            var rewards = _client!.Rewards;
            var mine = rewards?.For(_client.MyTeam ?? "");
            Log($"results up: rewards {(rewards != null ? "yes" : "NO")}, winner {rewards?.WinnerId}, me #{mine?.Rank} {mine?.Points} pts, awards {rewards?.Awards.Count}");
            yield return Shot("results_podium", 2f);
            if (Click("Tab_standings")) yield return Shot("results_standings", 1f);
            if (Click("Tab_awards")) yield return Shot("results_awards", 1f);
            if (Click("Tab_ratings")) yield return Shot("results_ratings", 1f);
            if (Click("Tab_squad")) yield return Shot("results_my_squad", 1f);
            Log($"career: {RoomCareer.Load().Line()}");
            // -aplroomtourney: PLAY A TOURNAMENT on the room's squads (log: [APL tourney] squads from auction room:…).
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-aplroomtourney") >= 0 && Click("Tournament"))
            {
                yield return Shot("tourney_setup_from_room", 1.2f);
                Click("Start");
                yield return Shot("tourney_table_from_room", 1.5f);
                // Tournament dev (owner decision on auction-r1 #4): a room's tournament has no sim buttons; expect "none".
                var sims = new[] { "SimMatch", "SimToNext", "SimRest" }.Where(n => Find(n) != null).ToList();
                Log($"tourney sim buttons on screen: {(sims.Count == 0 ? "none" : string.Join(",", sims))}");
                if (Click("PlayNext")) yield return Shot("tourney_playing_xi", 1.5f);
            }
            Log($"DONE shots={_shot} passedShot={_passedShot} botShot={_botShot} otherBotShot={_otherBotShot} hostPanel={_hostPanelShot}");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-aplroomstay") >= 0) yield break;
            yield return new WaitForSecondsRealtime(2f);
            Application.Quit(0);
        }
    }
}
