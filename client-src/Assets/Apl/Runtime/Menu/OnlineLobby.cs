using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Online;
using Apl.Client.Ui;
using Apl.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// Friend rooms (protocol v2): name + server → create (format mega / mini, retention, bid
    /// timer, your franchise) or join by code → the waiting room: the code to share, everyone's
    /// name, team, READY and connection, a team picker where taken teams are greyed out, the
    /// host's controls (lock, bid timer, make host, kick) and START → retention picks → the
    /// server starts the auction and <see cref="Started"/> hands over to the hall. Empty
    /// franchises are played by the server's Franchise-AI bots.
    /// </summary>
    public sealed class OnlineLobby : MonoBehaviour
    {
        public event Action? Back;
        public event Action<RoomClient, string>? Started;

        private Dataset _data = null!;
        private RoomClient _client = null!;
        private RectTransform _root = null!, _page = null!;
        private string _view = "home";
        private string _formatId = "standard";
        private bool _retention;
        private int _timer = 10;

        /// <summary>The franchise the host takes with the room.</summary>
        private string _createTeam = "";
        private readonly List<Player> _keeps = new List<Player>();
        private string _codeEntry = "";
        private bool _handedOver;

        /// <summary>Our READY (servers without a ready flag: picking a team is being ready).</summary>
        private bool _ready;

        public static OnlineLobby Create(Dataset data, RoomClient client)
        {
            var canvas = UiKit.Canvas("Online", 20);
            var lobby = canvas.gameObject.AddComponent<OnlineLobby>();
            lobby._data = data;
            lobby._client = client;
            lobby._root = (RectTransform)canvas.transform;
            lobby._formatId = data.Rules.FormatList.FirstOrDefault(f => f.Id == "standard")?.Id ?? data.Rules.FormatList[0].Id;
            client.Changed += lobby.OnChanged;
            client.Error += lobby.OnError;
            client.Notice += lobby.OnNotice;
            // Home's MULTIPLAYER card: CREATE opens the new-room page, JOIN the home page (code entry).
            var start = client.Code != null ? "room" : StartView == "create" ? "create" : "home";
            StartView = null;
            lobby.Show(start);
            return lobby;
        }

        /// <summary>The page the next lobby opens on ("create" or null for home); set by the home page.</summary>
        public static string? StartView;

        /// <summary>The page on screen: home, create, joining or room (test runs read it).</summary>
        public string View => _view;

        private void OnDestroy()
        {
            if (_client == null) return;
            _client.Changed -= OnChanged;
            _client.Error -= OnError;
            _client.Notice -= OnNotice;
            RoomToast.SetReconnecting(false);
        }

        private void OnChanged()
        {
            if (_handedOver) return;
            // The server started the auction: hand over to the hall.
            if (_client.Auction != null && _client.Auction.IsLoaded && (_client.Phase == "auction" || _client.Phase == "finished") && _client.MyTeam != null)
            {
                _handedOver = true;
                Debug.Log($"[APL room] auction started: handing over to the hall as {_client.MyTeam}");
                Started?.Invoke(_client, _client.MyTeam);
                Destroy(gameObject);
                return;
            }
            if (_client.Code != null && (_view == "home" || _view == "create" || _view == "joining")) _view = "room";
            if (StillPicking()) return;
            // Re-draw live pages; the home page keeps its text fields.
            if (_view != "home" && _view != "create") Show(_view);
        }

        private void OnError(string code, string message)
        {
            // Couldn't get in, or the host removed us: back to the start.
            if (_view == "joining" || code == "kicked" || code == "unreachable" || (_client.Code == null && (code == "locked" || code == "full" || code == "noSuchRoom" || code == "started")))
            {
                Show("home");
            }
            if (code == "desync") return;
            Toast(message);
        }

        private void OnNotice(string kind, string text, JObject m)
        {
            if (kind == "resumed" || kind == "youAreHost" || kind == "hostChanged" || kind == "locked" || kind == "kicked" || kind == "timer") RoomToast.Show(text, bad: false);
        }

        private void Update()
        {
            if (_redrawPending && Time.unscaledTime - _lastDraw >= 0.5f && !StillPicking()) Show(_view);
            RoomToast.SetReconnecting(_client.Reconnecting);
            if (_view == "room" && _client.Phase == "retention" && _client.Retention != null)
            {
                // Countdown ticks without a server message.
                if (!StillPicking()) Show("room");
            }
        }

        private static void Toast(string message) => RoomToast.Show(message);

        // --------------------------------------------------------------- pages

        private float _lastDraw;

        /// <summary>The view actually on screen (_view can move ahead of it).</summary>
        private string _drawnView = "";
        private bool _redrawPending;

        private void Show(string view)
        {
            // Room redraws at most twice a second — but only once the room page
            // is up: the first room message often lands within half a second of
            // "Joining…", and skipping that draw left the player stuck there.
            if (view == _drawnView && view == "room" && Time.unscaledTime - _lastDraw < 0.5f && _page != null)
            {
                // Not dropped: Update draws it once the half second is up.
                _redrawPending = true;
                return;
            }
            _redrawPending = false;
            _lastDraw = Time.unscaledTime;
            _view = view;
            _drawnView = view;
            if (_page != null) Destroy(_page.gameObject);
            _page = UiKit.Stretch(_root, "Page", new Color(0.02f, 0.03f, 0.07f, 0.72f));
            // Toasts go where this page has room: under the host controls in the waiting room, under the header elsewhere.
            RoomToast.Place(view == "room" && _client.Code != null && _client.Phase != "retention" && _client.Phase != "auction" && _client.Phase != "finished"
                ? RoomToast.Slot.Lobby
                : RoomToast.Slot.Top);
            switch (view)
            {
                case "home": Home(); break;
                case "create": CreatePage(); break;
                case "joining": Waiting("Joining room…"); break;
                default: RoomPage(); break;
            }
            // UI/UX dev (r1 §6 see-through): opaque players card and tiles (Menu/LobbyStyle.cs).
            LobbyStyle.Apply(_page);
        }

        private Text Header(string text, string sub)
        {
            var t = UiKit.LabelAt(_page, new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(1500, 76), text, 56, TextAnchor.MiddleCenter, UiKit.Gold);
            UiKit.LabelAt(_page, new Vector2(0.5f, 1), new Vector2(0, -140), new Vector2(1500, 40), sub, 26, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            return t;
        }

        private void BackButton(Action action) =>
            UiKit.Btn(_page, "Back", new Vector2(0, 1), new Vector2(150, -80), new Vector2(200, 80), "◄ BACK", UiKit.Muted, action, 30);

        private void Home()
        {
            Header("PLAY ONLINE WITH FRIENDS", "Up to 10 players. Empty franchises are played by AI bots.");
            BackButton(() => { Back?.Invoke(); Destroy(gameObject); });
            UiKit.LabelAt(_page, new Vector2(0.5f, 0.5f), new Vector2(0, 230), new Vector2(700, 36), "YOUR NAME", 24, TextAnchor.MiddleCenter, UiKit.Dim);
            var name = UiKit.Input(_page, new Vector2(0, 175), new Vector2(620, 80), "Your name", _client.PlayerName, 20);
            name.onEndEdit.AddListener(v => _client.SetName(v));

            UiKit.Btn(_page, "Create", new Vector2(0.5f, 0.5f), new Vector2(-320, 10), new Vector2(560, 120), "CREATE A ROOM", UiKit.Accent, () =>
            {
                _client.SetName(name.text);
                Show("create");
            }, 40);

            UiKit.LabelAt(_page, new Vector2(0.5f, 0.5f), new Vector2(320, 90), new Vector2(560, 34), "HAVE A CODE?", 24, TextAnchor.MiddleCenter, UiKit.Dim);
            var code = UiKit.Input(_page, new Vector2(320, 30), new Vector2(560, 84), "ROOM CODE", _codeEntry, 8);
            code.characterValidation = InputField.CharacterValidation.Alphanumeric;
            code.onValueChanged.AddListener(v => _codeEntry = v.ToUpperInvariant());
            UiKit.Btn(_page, "Join", new Vector2(0.5f, 0.5f), new Vector2(320, -70), new Vector2(560, 90), "JOIN ROOM ►", UiKit.Gold, () =>
            {
                _client.SetName(name.text);
                if (_codeEntry.Trim().Length < 4)
                {
                    Toast("Enter the room code your friend shared.");
                    return;
                }
                _client.Join(_codeEntry);
                Show("joining");
            }, 34);

            // Lifetime totals, kept on this phone (RoomCareer).
            var career = UiKit.EdgedPanel(_page, "Career", new Vector2(0.5f, 0.5f), new Vector2(0, -205), new Vector2(1180, 96), UiKit.PanelSolid, UiKit.Gold);
            UiKit.LabelAt(career, new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(1100, 28), "YOUR AUCTION CAREER", 20, TextAnchor.MiddleCenter, UiKit.Dim);
            UiKit.LabelAt(career, new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1100, 40), RoomCareer.Load().Line(), 30, TextAnchor.MiddleCenter, UiKit.Gold);

            // The server address is a setting, not something players need to see: it sits behind
            // a small SERVER ▸ button (bottom right). A -aplroomserver flag still wins.
            UiKit.LabelAt(_page, new Vector2(0.5f, 0), new Vector2(0, 48), new Vector2(1500, 30),
                _client.Connected ? "Connected to the room server." : "Rooms run on the APL room server; everyone needs an internet connection.", 20, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            UiKit.Btn(_page, "ServerSettings", new Vector2(1, 0), new Vector2(-130, 48), new Vector2(220, 52), _serverOpen ? "SERVER ▾" : "SERVER ▸", UiKit.Muted, () =>
            {
                _serverOpen = !_serverOpen;
                Show("home");
            }, 20);
            if (!_serverOpen) return;
            var box = UiKit.EdgedPanel(_page, "ServerBox", new Vector2(1, 0), new Vector2(-420, 150), new Vector2(800, 120), UiKit.PanelSolid, UiKit.Gold);
            UiKit.LabelAt(box, new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(760, 28), "ROOM SERVER (advanced: leave as is unless told otherwise)", 18, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            var server = UiKit.Input(box, new Vector2(0, -16), new Vector2(760, 60), RoomConfig.LocalDefault, RoomConfig.ServerAddress, 120);
            server.interactable = !RoomConfig.FromFlag;
            server.onEndEdit.AddListener(v =>
            {
                _client.SetServer(v);
                Toast("Room server: " + RoomConfig.SocketUrl(RoomConfig.ServerAddress));
            });
        }

        /// <summary>The SERVER ▸ settings box is open on the home page.</summary>
        private bool _serverOpen;

        private void CreatePage()
        {
            Header("NEW ROOM", "You'll be the host. Pick the auction, the bid timer and your franchise, then share the code.");
            BackButton(() => Show("home"));
            UiKit.LabelAt(_page, new Vector2(0.5f, 0.5f), new Vector2(0, 330), new Vector2(1400, 40), "FORMAT", 28, TextAnchor.MiddleCenter, UiKit.Dim);
            var formats = _data.Rules.FormatList;
            for (var i = 0; i < formats.Count; i++)
            {
                var f = formats[i];
                var x = (i - (formats.Count - 1) / 2f) * 470;
                var b = UiKit.Btn(_page, "Format_" + f.Id, new Vector2(0.5f, 0.5f), new Vector2(x, 240), new Vector2(440, 110), "",
                    f.Id == _formatId ? UiKit.Gold : UiKit.Muted, () => { _formatId = f.Id; Show("create"); });
                var t = b.GetComponentInChildren<Text>();
                t.supportRichText = true;
                t.text = $"{FormatTitle(f)}\n<size=22>{MainMenu.FormatBlurb(_data, f)}</size>";
                if (f.Id == _formatId) t.color = UiKit.Ink;
            }
            var chosen = formats.First(f => f.Id == _formatId);
            var keep = _data.Rules.RetentionFor(chosen).MaxPlayers;
            UiKit.LabelAt(_page, new Vector2(0.5f, 0.5f), new Vector2(-250, 150), new Vector2(700, 40), "AUCTION", 28, TextAnchor.MiddleCenter, UiKit.Dim);
            if (chosen.RetentionOnly)
            {
                // The server always runs a mini auction with retentions.
                _retention = true;
                UiKit.EdgedPanel(_page, "MiniAuction", new Vector2(0.5f, 0.5f), new Vector2(-250, 70), new Vector2(680, 110), UiKit.PanelSolid, UiKit.Gold);
                UiKit.LabelAt(_page, new Vector2(0.5f, 0.5f), new Vector2(-250, 70), new Vector2(660, 100),
                    $"RETENTION ON\n<size=22>Everyone keeps up to {keep} of last season's squad.</size>",
                    32, TextAnchor.MiddleCenter, UiKit.Gold).supportRichText = true;
            }
            else
            {
                Choice(new Vector2(-420, 70), false, "RETENTION OFF", "Every player under the hammer");
                Choice(new Vector2(-80, 70), true, "RETENTION ON", $"Keep up to {keep}");
            }
            // The bid timer: 10 / 15 / 20 s (the host can change it during the auction too).
            UiKit.LabelAt(_page, new Vector2(0.5f, 0.5f), new Vector2(450, 150), new Vector2(560, 40), "BID TIMER", 28, TextAnchor.MiddleCenter, UiKit.Dim);
            for (var i = 0; i < RoomClient.TimerChoices.Length; i++)
            {
                var s = RoomClient.TimerChoices[i];
                var on = s == _timer;
                var b = UiKit.Btn(_page, "Timer_" + s, new Vector2(0.5f, 0.5f), new Vector2(300 + i * 150, 70), new Vector2(136, 110), s + " s", on ? UiKit.Gold : UiKit.Muted, () => { _timer = s; Show("create"); }, 36);
                if (on) b.GetComponentInChildren<Text>().color = UiKit.Ink;
            }
            // Your franchise comes with the room: friends pick from the rest.
            if (_createTeam.Length == 0) _createTeam = PlayerPrefs.GetString(TeamKey, _data.Franchises[0].Id);
            UiKit.LabelAt(_page, new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(1400, 40), "YOUR FRANCHISE", 28, TextAnchor.MiddleCenter, UiKit.Dim);
            for (var i = 0; i < _data.Franchises.Count; i++)
            {
                var f = _data.Franchises[i];
                var picked = f.Id == _createTeam;
                var tile = UiKit.Box(_page, "Own_" + f.Id, new Vector2(0.5f, 0.5f), new Vector2((i - (_data.Franchises.Count - 1) / 2f) * 150, -150), new Vector2(138, 132),
                    UiKit.WithAlpha(UiKit.ParseHex(f.PrimaryColor), picked ? 1f : 0.7f));
                var id = f.Id;
                tile.gameObject.AddComponent<Button>().onClick.AddListener(() => { _createTeam = id; Show("create"); });
                if (picked) YouMark.Add(tile, "YOU", 7);
                UiKit.Crest(tile, f.Id, new Vector2(0.5f, 1), new Vector2(0, -50), 76);
                UiKit.LabelAt(tile, new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(130, 28), f.ShortName, 22, TextAnchor.MiddleCenter,
                    UiKit.ParseHex(f.PrimaryColor).grayscale > 0.55f ? UiKit.Ink : Color.white);
            }
            UiKit.Btn(_page, "CreateRoom", new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(620, 104), "CREATE ROOM ►", UiKit.Accent, () =>
            {
                PlayerPrefs.SetString(TeamKey, _createTeam);
                Debug.Log($"[APL room] create room: format {_formatId}, retention {_retention}, timer {_timer}s, team {_createTeam}");
                _client.CreateRoom(_formatId, _retention, _createTeam, _timer);
                Show("joining");
            }, 40);
        }

        /// <summary>"MEGA AUCTION" / "MINI AUCTION" (the standard format is the mini auction).</summary>
        public static string FormatTitle(AuctionFormat f) => f.RetentionOnly || f.Id == "standard" ? "MINI AUCTION" : f.Id == "mega" ? "MEGA AUCTION" : f.Label.ToUpperInvariant();

        private const string TeamKey = "apl.roomTeam";

        /// <summary>Test runs: the create page's choices, set directly.</summary>
        public void SetCreateChoices(string? format, bool? retention, int? timer, string? team)
        {
            if (format != null && _data.Rules.FormatList.Any(f => f.Id == format)) _formatId = format;
            if (retention != null) _retention = retention.Value;
            if (timer != null) _timer = timer.Value;
            if (team != null && _data.Franchises.Any(f => f.Id == team)) _createTeam = team;
            if (_view == "create") Show("create");
        }

        private void Choice(Vector2 pos, bool retention, string title, string sub)
        {
            var on = _retention == retention;
            var b = UiKit.Btn(_page, title.Replace(' ', '_'), new Vector2(0.5f, 0.5f), pos, new Vector2(320, 110), "", on ? UiKit.Gold : UiKit.Muted,
                () => { _retention = retention; Show("create"); });
            var t = b.GetComponentInChildren<Text>();
            t.supportRichText = true;
            t.text = $"{title}\n<size=20>{sub}</size>";
            t.fontSize = 28;
            if (on) t.color = UiKit.Ink;
        }

        private void Waiting(string text)
        {
            Header("PLAY ONLINE", "");
            UiKit.LabelAt(_page, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 60), text, 36, TextAnchor.MiddleCenter);
            BackButton(() => { _client.Leave(); Show("home"); });
        }

        private bool IsReady(RoomSeat s) => _client.SupportsReady ? s.Ready : s.Team != null;

        private void RoomPage()
        {
            if (_client.Code == null)
            {
                Waiting(_client.Connected ? "Joining room…" : "Connecting…");
                return;
            }
            if (_client.Phase == "retention")
            {
                RetentionPage();
                return;
            }
            if (_client.Phase == "auction" || _client.Phase == "finished")
            {
                Waiting("Starting the auction…");
                return;
            }
            var format = _data.Rules.FormatList.FirstOrDefault(f => f.Id == _client.FormatId);
            var auctionType = format == null ? _client.FormatId : FormatTitle(format);
            var retentionText = format?.RetentionOnly == true || _client.RetentionMode ? "retention on" : "retention off";
            Header("WAITING ROOM", $"{auctionType} · {retentionText} · {_client.BidTimerSec} s bid timer{(_client.Locked ? " · LOCKED" : "")} · Empty franchises are AI bots");
            BackButton(() => { _client.Leave(); Show("home"); });
            var hostId = (string?)_client.Room?["host"];
            var seats = _client.Seats.ToList();

            // Room code card: what friends type in. Copy, or share straight to WhatsApp.
            var card = UiKit.EdgedPanel(_page, "CodeCard", new Vector2(0, 1), new Vector2(340, -290), new Vector2(600, 200), UiKit.PanelSolid, UiKit.Gold);
            UiKit.LabelAt(card, new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(560, 30), _client.Locked ? "ROOM CODE · LOCKED" : "ROOM CODE", 22, TextAnchor.MiddleCenter, _client.Locked ? UiKit.Bad : UiKit.Dim);
            UiKit.LabelAt(card, new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(580, 76), Spaced(_client.Code!), 62, TextAnchor.MiddleCenter, UiKit.Gold);
            var code = _client.Code!;
            UiKit.Btn(card, "CopyCode", new Vector2(0.5f, 0), new Vector2(-140, 40), new Vector2(260, 60), "COPY", UiKit.Muted, () =>
            {
                Share.Copy(code);
                RoomToast.Show("Room code copied.", bad: false);
            }, 26);
            UiKit.Btn(card, "ShareCode", new Vector2(0.5f, 0), new Vector2(140, 40), new Vector2(260, 60), "SHARE ►", UiKit.Accent, () =>
            {
                var text = Invite(code, format);
                if (!Share.Text(text, "Invite friends to your APL room"))
                {
                    Share.Copy(text);
                    RoomToast.Show("Invite copied — paste it into WhatsApp.", bad: false);
                }
            }, 26);

            PlayerList(seats, hostId);
            TeamPicker(seats);
            HostControls();

            var seated = seats.Count(s => s.Team != null);
            var everyoneSeated = seats.All(s => s.Team != null);
            var everyoneReady = seats.All(IsReady);
            var me = _client.Me;
            // READY (guests): a toggle where the server tracks it; else picking a team is being ready.
            // The host has no READY: his START is his ready (the server always reports him ready).
            if (_client.SupportsReady && !_client.IsHost)
            {
                _ready = me?.Ready ?? false;
                var ready = UiKit.Btn(_page, "Ready", new Vector2(0.5f, 0), new Vector2(280, 90), new Vector2(560, 104),
                    _ready ? "READY" : "I'M READY", _ready ? UiKit.Good : UiKit.Muted, () =>
                    {
                        Debug.Log($"[APL room] ready {!_ready}");
                        _client.SetReady(!_ready);
                    }, 34);
                ready.interactable = me?.Team != null;
                if (_ready) ready.GetComponentInChildren<Text>().color = UiKit.Ink;
            }
            if (_client.IsHost)
            {
                var canStart = seated >= _client.MinPlayers && everyoneSeated && everyoneReady;
                var label = canStart ? "START AUCTION ►"
                    : seated < _client.MinPlayers ? "WAITING FOR FRIENDS…"
                    : !everyoneSeated ? "EVERYONE PICK A TEAM"
                    : "WAITING FOR READY…";
                var start = UiKit.Btn(_page, "StartRoom", new Vector2(0.5f, 0), new Vector2(280, 90), new Vector2(560, 104),
                    label, UiKit.Accent, () =>
                    {
                        Debug.Log($"[APL room] host START ({seated} seated)");
                        _client.StartGame();
                    }, 34);
                start.interactable = canStart;
            }
            else if (!_client.SupportsReady)
            {
                UiKit.LabelAt(_page, new Vector2(0.5f, 0), new Vector2(280, 90), new Vector2(900, 44),
                    me?.Team == null ? "Pick a franchise to be ready." : "Ready · waiting for the host to start…", 30, TextAnchor.MiddleCenter, UiKit.Dim);
            }
        }

        /// <summary>Who's here: name, franchise, READY, connection, host badge; the host gets MAKE HOST and KICK per row.</summary>
        private void PlayerList(List<RoomSeat> seats, string? hostId)
        {
            UiKit.LabelAt(_page, new Vector2(0, 1), new Vector2(340, -420), new Vector2(600, 34), $"PLAYERS  {seats.Count}/{_client.MaxPlayers}", 24, TextAnchor.MiddleLeft, UiKit.Dim);
            const float rowH = 56;
            for (var i = 0; i < seats.Count; i++)
            {
                var s = seats[i];
                var me = s.Id == _client.PlayerId;
                var row = UiKit.Box(_page, "Member_" + i, new Vector2(0, 1), new Vector2(340, -466 - i * rowH), new Vector2(600, rowH - 6),
                    me ? new Color(0.95f, 0.76f, 0.31f, 0.25f) : new Color(0.09f, 0.11f, 0.18f, 0.95f));
                // Connection dot: green online, amber bot, red offline.
                var (state, dot) = Hall.HostPanel.StateLabel(s);
                UiKit.Box(row, "Conn", new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(12, 12), dot);
                if (s.Team != null) UiKit.Crest(row, s.Team, new Vector2(0, 0.5f), new Vector2(46, 0), 40);
                var tags = (s.Id == hostId ? "  · HOST" : "") + (me ? "  · YOU" : "") + (s.State != SeatState.Online ? "  · " + state : "");
                var name = UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(76 + 110, 0), new Vector2(220, 40), $"{s.Name}{tags}", 21, TextAnchor.MiddleLeft);
                name.resizeTextForBestFit = true;
                name.resizeTextMinSize = 14;
                name.resizeTextMaxSize = 21;
                var teamName = s.Team == null ? "choosing…" : _data.Franchises.FirstOrDefault(f => f.Id == s.Team)?.ShortName ?? s.Team;
                UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(335, 0), new Vector2(80, 40), teamName, 20, TextAnchor.MiddleCenter, s.Team == null ? UiKit.Dim : UiKit.Gold);
                var ready = IsReady(s);
                var chip = UiKit.Box(row, "ReadyChip", new Vector2(0, 0.5f), new Vector2(420, 0), new Vector2(76, 28), ready ? UiKit.Good : new Color(0.3f, 0.32f, 0.38f));
                UiKit.Label(chip, ready ? "READY" : "NOT READY", 13, TextAnchor.MiddleCenter, ready ? UiKit.Ink : Color.white);
                if (!_client.IsHost || me) continue;
                var who = s.Id;
                var nameText = s.Name;
                UiKit.Btn(row, "MakeHost_" + i, new Vector2(1, 0.5f), new Vector2(-100, 0), new Vector2(62, 40), "HOST", UiKit.Muted, () =>
                    ConfirmDialog.Show($"MAKE {nameText.ToUpperInvariant()} HOST?", "They get the host controls and the START button.", "MAKE HOST", "CANCEL", () =>
                    {
                        Debug.Log($"[APL room] host transfer to {nameText}");
                        _client.HostTransfer(who);
                    }), 16);
                UiKit.Btn(row, "Remove_" + i, new Vector2(1, 0.5f), new Vector2(-34, 0), new Vector2(58, 40), "KICK", new Color(0.45f, 0.12f, 0.12f, 0.96f), () =>
                    ConfirmDialog.Show($"KICK {nameText.ToUpperInvariant()}?", "They leave the room and can't come back with this code.", "KICK", "CANCEL", () =>
                    {
                        Debug.Log($"[APL room] host kick {nameText}");
                        _client.Kick(who);
                    }), 16);
            }
        }

        /// <summary>Franchises: free ones can be picked; taken ones are greyed with who has them and can't be picked.</summary>
        private void TeamPicker(List<RoomSeat> seats)
        {
            var taken = seats.Where(s => s.Team != null).GroupBy(s => s.Team!).ToDictionary(g => g.Key, g => g.First());
            UiKit.LabelAt(_page, new Vector2(0.5f, 1), new Vector2(280, -200), new Vector2(1100, 34),
                _client.MyTeam == null ? "PICK YOUR FRANCHISE" : "YOUR FRANCHISE · tap another free one to switch", 24, TextAnchor.MiddleCenter, UiKit.Dim);
            for (var i = 0; i < _data.Franchises.Count; i++)
            {
                var f = _data.Franchises[i];
                var mine = _client.MyTeam == f.Id;
                var owner = taken.TryGetValue(f.Id, out var who) && !mine ? who : null;
                var pos = new Vector2(280 + (i % 5 - 2) * 212, -310 - i / 5 * 196);
                var colour = UiKit.ParseHex(f.PrimaryColor);
                var fill = mine ? UiKit.WithAlpha(colour, 0.95f) : owner != null ? new Color(0.17f, 0.17f, 0.2f, 0.92f) : UiKit.WithAlpha(colour, 0.85f);
                var tile = UiKit.Box(_page, "Team_" + f.Id, new Vector2(0.5f, 1), pos, new Vector2(198, 182), fill);
                var id = f.Id;
                var button = tile.gameObject.AddComponent<Button>();
                if (owner != null)
                {
                    // Greyed out: two people can never share a team.
                    var ownerName = owner.Name;
                    button.onClick.AddListener(() => Toast($"{f.ShortName} is taken by {ownerName}."));
                }
                else if (!mine)
                {
                    button.onClick.AddListener(() =>
                    {
                        Debug.Log($"[APL room] pick team {id}");
                        _client.Pick(id);
                    });
                }
                var crest = UiKit.Crest(tile, f.Id, new Vector2(0.5f, 1), new Vector2(0, -60), 96);
                if (owner != null) crest.color = new Color(1, 1, 1, 0.3f);
                var ink = owner != null ? UiKit.Dim : fill.grayscale > 0.55f ? UiKit.Ink : Color.white;
                UiKit.LabelAt(tile, new Vector2(0.5f, 0), new Vector2(0, 46), new Vector2(190, 32), f.ShortName, 28, TextAnchor.MiddleCenter, ink);
                var sub = UiKit.LabelAt(tile, new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(190, 26), mine ? "YOUR FRANCHISE" : owner != null ? "TAKEN · " + owner.Name : "FREE · AI BOT", 17, TextAnchor.MiddleCenter, ink, bold: owner != null);
                sub.resizeTextForBestFit = true;
                sub.resizeTextMinSize = 11;
                sub.resizeTextMaxSize = 17;
                if (owner != null)
                {
                    var lockTag = UiKit.Box(tile, "Taken", new Vector2(1, 1), new Vector2(-40, -16), new Vector2(76, 26), new Color(0.45f, 0.12f, 0.12f, 0.96f));
                    UiKit.Label(lockTag, "TAKEN", 14, TextAnchor.MiddleCenter);
                }
                if (mine) YouMark.Add(tile, "YOU", 7);
            }
        }

        /// <summary>The host's panel in the waiting room: lock the room, the bid timer (others see the settings read-only).</summary>
        private void HostControls()
        {
            var panel = UiKit.EdgedPanel(_page, "HostControls", new Vector2(0.5f, 1), new Vector2(280, -700), new Vector2(1060, 110), UiKit.PanelSolid, _client.IsHost ? UiKit.Gold : new Color(0.3f, 0.33f, 0.4f));
            UiKit.LabelAt(panel, new Vector2(0, 0.5f), new Vector2(120, 18), new Vector2(220, 30), _client.IsHost ? "HOST CONTROLS" : "ROOM SETTINGS", 20, TextAnchor.MiddleCenter, _client.IsHost ? UiKit.Gold : UiKit.Dim);
            UiKit.LabelAt(panel, new Vector2(0, 0.5f), new Vector2(120, -16), new Vector2(220, 28), _client.IsHost ? "only you see these" : "set by the host", 17, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            var locked = _client.Locked;
            var sec = _client.BidTimerSec;
            if (!_client.IsHost)
            {
                UiKit.LabelAt(panel, new Vector2(0.5f, 0.5f), new Vector2(110, 0), new Vector2(780, 40),
                    $"BID TIMER {sec} s   ·   ROOM {(locked ? "LOCKED" : "OPEN TO JOIN")}", 26, TextAnchor.MiddleCenter, Color.white);
                return;
            }
            var lockBtn = UiKit.Btn(panel, "HostLock", new Vector2(0, 0.5f), new Vector2(370, 0), new Vector2(240, 76), locked ? "UNLOCK ROOM" : "LOCK ROOM", locked ? UiKit.Gold : UiKit.Muted, () =>
            {
                Debug.Log($"[APL room] host {(locked ? "unlock" : "lock")} room");
                _client.HostLock(!locked);
            }, 26);
            if (locked)
            {
                lockBtn.GetComponentInChildren<Text>().color = UiKit.Ink;
            }
            UiKit.LabelAt(panel, new Vector2(0, 0.5f), new Vector2(600, 0), new Vector2(160, 40), "BID TIMER", 22, TextAnchor.MiddleCenter, UiKit.Dim);
            for (var i = 0; i < RoomClient.TimerChoices.Length; i++)
            {
                var s = RoomClient.TimerChoices[i];
                var on = s == sec;
                var b = UiKit.Btn(panel, "HostTimer_" + s, new Vector2(0, 0.5f), new Vector2(740 + i * 104, 0), new Vector2(94, 70), s + "s", on ? UiKit.Gold : UiKit.Muted, () =>
                {
                    Debug.Log($"[APL room] host bid timer {s}s");
                    _client.HostBidTimer(s);
                }, 26);
                if (on) b.GetComponentInChildren<Text>().color = UiKit.Ink;
            }
        }

        /// <summary>"AB12CD" as "AB1 2CD": easier to read out over a call.</summary>
        private static string Spaced(string code) => code.Length == 6 ? code.Substring(0, 3) + " " + code.Substring(3) : code;

        /// <summary>The invite friends get on WhatsApp (or wherever it's shared).</summary>
        private static string Invite(string code, AuctionFormat? format) =>
            "🏏 Join my APL auction room!\n\n" +
            $"Room code: *{code}*\n" +
            (format != null ? $"Format: {FormatTitle(format)} · {Money.Format(format.Purse)} purse\n" : "") +
            "Pick your franchise before I start the bidding — empty teams are played by AI bots.\n\n" +
            "Open APL → PLAY ONLINE → enter the code → JOIN ROOM.";

        private void RetentionPage()
        {
            var team = _client.MyTeam;
            var submitted = ((_client.Retention?["submitted"] as JArray) ?? new JArray()).Select(x => (string)x!).ToList();
            var left = Mathf.CeilToInt(_client.RetentionLeftMs / 1000f);
            var format = _data.Rules.FormatList.First(f => f.Id == _client.FormatId);
            if (team == null || submitted.Contains(team))
            {
                var waiting = _client.Seats.Where(s => s.Team != null && !submitted.Contains(s.Team)).Select(s => s.Name).ToList();
                Header("RETENTIONS LOCKED IN", waiting.Count == 0 ? "Starting…" : "Waiting for: " + string.Join(", ", waiting));
                UiKit.LabelAt(_page, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 60), $"Auction starts in {left}s at the latest", 32, TextAnchor.MiddleCenter);
                return;
            }
            var rules = _data.Rules;
            var retention = rules.RetentionFor(format);
            if (_keepsFor != team)
            {
                // First look at this room's picks: a mini auction starts from the AI's choice.
                _keepsFor = team;
                _keeps.Clear();
                if (format.RetentionOnly) _keeps.AddRange(Ai.ChooseRetentions(team, _data.Players, rules, format));
            }
            _pickHeader = Header(RetentionTitle(retention.MaxPlayers, left), format.RetentionOnly
                ? $"Last season's {team} squad at current prices. Out of time = these picks."
                : $"Last season's {team} squad. Not choosing in time means the AI picks for you.");
            _pickMax = retention.MaxPlayers;
            var picker = new RetentionPicker(_page, _data, team, format, _keeps);
            var go = UiKit.Btn(_page, "SubmitKeeps", new Vector2(0.5f, 0), new Vector2(0, 105), new Vector2(620, 104), "", UiKit.Accent,
                () =>
                {
                    Debug.Log($"[APL room] retain {_keeps.Count}: {string.Join(",", _keeps.Select(k => k.Id))}");
                    _client.Retain(_keeps.Select(k => k.Id));
                }, 38);
            var label = go.GetComponentInChildren<Text>();
            picker.Changed += problem =>
            {
                go.interactable = problem == null;
                label.text = _keeps.Count == 0 ? "NO KEEPS ►" : "LOCK IN ►";
            };
            picker.Refresh();
        }

        private string? _keepsFor;
        private Text? _pickHeader;
        private int _pickMax;

        private static string RetentionTitle(int max, int left) => $"RETAIN UP TO {max} · {left}s";

        /// <summary>
        /// The retention list is open and this player hasn't locked in: only the
        /// countdown changes, so update it in place instead of rebuilding.
        /// </summary>
        private bool StillPicking()
        {
            if (_view != "room" || _client.Phase != "retention" || _pickHeader == null || _page == null) return false;
            var team = _client.MyTeam;
            var submitted = ((_client.Retention?["submitted"] as JArray) ?? new JArray()).Select(x => (string)x!).ToList();
            if (team == null || submitted.Contains(team) || !_pickHeader.transform.IsChildOf(_page)) return false;
            var left = Mathf.CeilToInt(_client.RetentionLeftMs / 1000f);
            _pickHeader.text = RetentionTitle(_pickMax, left);
            return true;
        }
    }
}
