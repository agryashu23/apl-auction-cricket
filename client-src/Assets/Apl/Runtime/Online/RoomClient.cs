using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Apl.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Apl.Client.Online
{
    /// <summary>
    /// The phone's side of a friend room (protocol v2, docs/ROOMS_PROTOCOL.md): one socket to the
    /// room server, reconnecting by itself, holding the lobby snapshot, the seats, the auction
    /// replica (RemoteAuction, rebuilt on every <c>start</c>), the per-lot passes, pause and timer
    /// from <c>clock</c>, and the end-of-auction rewards. Socket work runs on background tasks;
    /// every message is handled on the main thread in Update.
    /// </summary>
    public sealed class RoomClient : MonoBehaviour
    {
        /// <summary>Must equal the server's protocol (v2).</summary>
        public const int ProtocolVersion = 2;

        /// <summary>The host's bid-timer range (server: 5–30 s) and the offered choices.</summary>
        public const int MinBidTimer = 5, MaxBidTimer = 30;
        public static readonly int[] TimerChoices = { 10, 15, 20 };

        private const string IdKey = "apl.playerId";
        private const string NameKey = "apl.playerName";

        public string Url = RoomConfig.SocketUrl(RoomConfig.FallbackServer); // set in Create()

        /// <summary>Any change: lobby, retention, auction start, clock, connection.</summary>
        public event Action? Changed;
        public event Action<string, string>? Error;

        /// <summary>A one-off room event for a toast (kind, readable text, raw message).</summary>
        public event Action<string, string, JObject>? Notice;

        /// <summary>The auction was rebuilt from a fresh <c>start</c> (reason: "join", "undo", "instant").</summary>
        public event Action<string>? Rebuilt;

        public Dataset Data { get; private set; } = null!;
        public string PlayerId { get; private set; } = "";
        public string PlayerName { get; private set; } = "";

        public JObject? Room { get; private set; }
        public JObject? Retention { get; private set; }
        private float _retentionAt;

        /// <summary>Retention time left, counting down since the server's last word.</summary>
        public int RetentionLeftMs => Mathf.Max(0, ((int?)Retention?["leftMs"] ?? 0) - Mathf.RoundToInt((Time.unscaledTime - _retentionAt) * 1000));
        public RemoteAuction? Auction { get; private set; }
        public bool Connected { get; private set; }

        /// <summary>We were in a room and the socket dropped: the reconnect is under way.</summary>
        public bool Reconnecting => !Connected && Code != null;

        public string? Code => (string?)Room?["code"];
        public string Phase => (string?)Room?["phase"] ?? "none";
        public bool IsHost => (string?)Room?["host"] == PlayerId && PlayerId.Length > 0;
        public string? FormatId => (string?)Room?["format"];
        public bool RetentionMode => (bool?)Room?["retention"] ?? false;
        public int MinPlayers => (int?)Room?["minPlayers"] ?? 2;
        public int MaxPlayers => (int?)Room?["maxPlayers"] ?? 10;
        public bool Locked => (bool?)Room?["locked"] ?? false;

        /// <summary>Host pause: from the latest clock, else the room snapshot.</summary>
        public bool Paused => _clockPaused ?? ((bool?)Room?["paused"] ?? false);

        /// <summary>The host's bid timer (seconds).</summary>
        public int BidTimerSec => _clockTimer ?? ((int?)Room?["timerSec"] ?? 10);

        /// <summary>Teams that passed on the lot now open (clock.passed), plus our own pass until the server confirms it.</summary>
        public IReadOnlyCollection<string> PassedThisLot => _passed;

        /// <summary>Lot number from the clock (0 between lots).</summary>
        public int LotNumber { get; private set; }

        /// <summary>A lot is on the block (the host's force-close buttons).</summary>
        public bool LotOpen => Auction != null && Auction.IsLoaded && Auction.Engine.CurrentLot != null && Phase == "auction";

        /// <summary>At least one lot closed (undo has something to undo).</summary>
        public bool CanUndo => Auction != null && Auction.IsLoaded && Auction.Engine.Results.Count > 0;

        /// <summary>v2 servers all take <c>undo</c>.</summary>
        public bool SupportsUndo => true;

        /// <summary>The server tracks a READY flag per member (not in v2 yet: then a seated player counts as ready).</summary>
        public bool SupportsReady => Members.Any(m => m["ready"] != null);

        /// <summary>The end-of-auction rewards (results message or the finished room's snapshot).</summary>
        public RoomRewards? Rewards { get; private set; }

        public IEnumerable<JObject> Members => (Room?["members"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();

        /// <summary>Everyone in the room as seats (lobby and auction).</summary>
        public IReadOnlyList<RoomSeat> Seats => _seats;
        public RoomSeat? Me => _seats.FirstOrDefault(s => s.Id == PlayerId);
        public string? MyTeam => Me?.Team;

        /// <summary>A bot is playing our team (SIMULATE, or the autopilot after a drop-out).</summary>
        public bool MyTeamOnAutopilot => Me?.Simulating == true || Me?.State == SeatState.Bot;

        private List<RoomSeat> _seats = new List<RoomSeat>();
        private readonly HashSet<string> _passed = new HashSet<string>();
        private string? _myPendingPass;
        private int _passLot = -1;
        private bool? _clockPaused;
        private int? _clockTimer;

        private ClientWebSocket? _socket;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private readonly List<JObject> _pending = new List<JObject>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private bool _welcomed, _closed, _connecting;
        private int _attempt;

        /// <summary>We had a seat and lost the socket: the next room snapshot means "seat resumed".</summary>
        private bool _resuming;

        /// <summary>Why the last connection failed, for the player and the log.</summary>
        private string _lastFailure = "";

        /// <summary>Failed tries before a create/join gives up and says so.</summary>
        private const int GiveUpAfter = 3;
        private float _retryAt = -1, _pingAt;
        private JObject? _intent;

        public static RoomClient Create(Dataset data)
        {
            var go = new GameObject("RoomClient");
            var client = go.AddComponent<RoomClient>();
            client.Data = data;
            var args = Environment.GetCommandLineArgs();
            // The server: -aplroomserver / -aplroomtest <url>, the online page's SERVER field, or localhost.
            client.Url = RoomConfig.SocketUrl(RoomConfig.ServerAddress);
            // Older test runs: -aplroomurl ws://127.0.0.1:8787/ws (exact socket URL).
            var urlArg = Array.IndexOf(args, "-aplroomurl");
            if (urlArg >= 0 && urlArg + 1 < args.Length) client.Url = args[urlArg + 1];
            // Several windows on one PC share PlayerPrefs: -aplplayerid (or a room test) gives each its own seat.
            var idArg = Array.IndexOf(args, "-aplplayerid");
            if (idArg >= 0 && idArg + 1 < args.Length) client.PlayerId = args[idArg + 1];
            else if (Array.IndexOf(args, "-aplroomtest") >= 0) client.PlayerId = "test" + System.Diagnostics.Process.GetCurrentProcess().Id + Guid.NewGuid().ToString("N").Substring(0, 6);
            else
            {
                if (!PlayerPrefs.HasKey(IdKey))
                {
                    PlayerPrefs.SetString(IdKey, Guid.NewGuid().ToString("N"));
                    PlayerPrefs.Save();
                }
                client.PlayerId = PlayerPrefs.GetString(IdKey);
            }
            client.PlayerName = PlayerPrefs.GetString(NameKey, "Player " + client.PlayerId.Substring(Math.Max(0, client.PlayerId.Length - 4)).ToUpperInvariant());
            Debug.Log($"[APL room] client for {client.Url} (player {client.PlayerId})");
            return client;
        }

        public void SetName(string name, bool save = true)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return;
            PlayerName = name.Length > 20 ? name.Substring(0, 20) : name;
            if (!save) return;
            PlayerPrefs.SetString(NameKey, PlayerName);
            PlayerPrefs.Save();
        }

        /// <summary>Points the client at another server (the SERVER field); only between rooms.</summary>
        public void SetServer(string address)
        {
            if (Code != null) return;
            RoomConfig.Save(address);
            var url = RoomConfig.SocketUrl(RoomConfig.ServerAddress);
            if (url == Url) return;
            Url = url;
            DropSocket();
        }

        // ------------------------------------------------------------ commands

        /// <summary>New room; the host's franchise comes with it.</summary>
        public void CreateRoom(string formatId, bool retention, string teamId, int timerSec) =>
            Send(_intent = new JObject { ["t"] = "create", ["format"] = formatId, ["retention"] = retention, ["team"] = teamId, ["timer"] = timerSec });

        public void Join(string code) =>
            Send(_intent = new JObject { ["t"] = "join", ["code"] = code.Trim().ToUpperInvariant() });

        public void Pick(string teamId) => Send(new JObject { ["t"] = "pick", ["team"] = teamId });

        /// <summary>READY in the waiting room (servers that track it; see SupportsReady).</summary>
        public void SetReady(bool ready) => Send(new JObject { ["t"] = "ready", ["ready"] = ready });

        public void StartGame() => Send(new JObject { ["t"] = "start" });
        public void Retain(IEnumerable<string> playerIds) => Send(new JObject { ["t"] = "retain", ["ids"] = new JArray(playerIds) });

        /// <summary>PASS: out of this lot only. Shown as PASSED at once; the server's clock confirms it.</summary>
        public void PassLot()
        {
            if (MyTeam != null)
            {
                _myPendingPass = MyTeam;
                _passed.Add(MyTeam);
            }
            Send(new JObject { ["t"] = "pass" });
        }

        /// <summary>SIMULATE: a bot bids for our team for the rest of the auction.</summary>
        public void Simulate() => Send(new JObject { ["t"] = "simulate" });

        /// <summary>TAKE BACK CONTROL from the autopilot.</summary>
        public void TakeBack() => Send(new JObject { ["t"] = "takeBack" });

        // Host only.
        public void Kick(string playerId) => Send(new JObject { ["t"] = "kick", ["player"] = playerId });
        public void HostTransfer(string playerId) => Send(new JObject { ["t"] = "host", ["player"] = playerId });
        public void HostLock(bool locked) => Send(new JObject { ["t"] = "lock", ["locked"] = locked });
        public void HostPause(bool pause) => Send(new JObject { ["t"] = pause ? "pause" : "resume" });
        public void HostBidTimer(int seconds) => Send(new JObject { ["t"] = "settings", ["timer"] = Mathf.Clamp(seconds, MinBidTimer, MaxBidTimer) });
        public void HostSettings(string? format, bool? retention)
        {
            var m = new JObject { ["t"] = "settings" };
            if (format != null) m["format"] = format;
            if (retention != null) m["retention"] = retention.Value;
            Send(m);
        }
        public void HostForceClose(bool sold) => Send(new JObject { ["t"] = "forceClose", ["sold"] = sold });
        public void HostUndo() => Send(new JObject { ["t"] = "undo" });
        public void HostEndAuction() => Send(new JObject { ["t"] = "end" });

        public void Leave()
        {
            if (_welcomed) Send(new JObject { ["t"] = "leave" });
            _intent = null;
            Room = null;
            Retention = null;
            Auction = null;
            Rewards = null;
            _seats = new List<RoomSeat>();
            Changed?.Invoke();
        }

        /// <summary>Test runs (-aplroomtest … -aplroomdrop): cut the socket as a lost connection would; the client rejoins by itself.</summary>
        public void TestDrop()
        {
            Debug.Log("[APL room] test: dropping the connection");
            _cts?.Cancel();
            Dropped();
        }

        private void OnDestroy()
        {
            _closed = true;
            _cts?.Cancel();
            try { _socket?.Dispose(); } catch { /* closing anyway */ }
        }

        // ----------------------------------------------------------- transport

        /// <summary>Log every message in and out (test runs: -aplnetlog).</summary>
        private static readonly bool NetLog = Environment.GetCommandLineArgs().Contains("-aplnetlog");

        private void Send(JObject m)
        {
            if (NetLog) Debug.Log($"[APL net] send {(string?)m["t"]} (welcomed={_welcomed}, socket={(_socket != null)})");
            if (_welcomed && _socket != null)
            {
                _ = SendRaw(_socket, m.ToString(Newtonsoft.Json.Formatting.None));
            }
            else
            {
                // Pings and game moves aren't worth queueing through a reconnect.
                var t = (string?)m["t"];
                if (t == "ping") return;
                _pending.Add(m);
                Connect();
            }
        }

        private async Task SendRaw(ClientWebSocket socket, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await _sendLock.WaitAsync();
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            catch (Exception)
            {
                _mainThread.Enqueue(() => DroppedFor(socket));
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private void DropSocket()
        {
            _cts?.Cancel();
            try { _socket?.Dispose(); } catch { /* gone */ }
            _socket = null;
            _connecting = false;
            _welcomed = false;
            Connected = false;
        }

        private void Connect()
        {
            if (_closed || _connecting || _socket != null) return;
            _connecting = true;
            _cts = new CancellationTokenSource();
            var socket = new ClientWebSocket();
            _connectingSocket = socket;
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            var token = _cts.Token;
            var hello = new JObject
            {
                ["t"] = "hello",
                ["protocol"] = Math.Max(ProtocolVersion, RoomProtocol.Version),
                ["playerId"] = PlayerId,
                ["name"] = PlayerName,
                ["dataVersion"] = Data.Version,
            }.ToString(Newtonsoft.Json.Formatting.None);
            var url = Url;
            Task.Run(async () =>
            {
                try
                {
                    await socket.ConnectAsync(new Uri(url), token);
                    _mainThread.Enqueue(() =>
                    {
                        _socket = socket;
                        _connecting = false;
                    });
                    await SendRaw(socket, hello);
                    var buffer = new byte[64 * 1024];
                    var message = new StringBuilder();
                    while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                    {
                        var r = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        message.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                        if (!r.EndOfMessage) continue;
                        _inbox.Enqueue(message.ToString());
                        message.Clear();
                    }
                }
                catch (Exception ex) when (token.IsCancellationRequested || ex is ObjectDisposedException || ex.GetBaseException() is ObjectDisposedException || ex is OperationCanceledException)
                {
                    // We closed this socket ourselves (drop, rejoin, leave): its read loop ending is
                    // expected, not a failed connection. Nothing to report; DroppedFor ignores it once replaced.
                }
                catch (Exception ex)
                {
                    // Falls through to Dropped: the retry handles it.
                    var why = ex.GetBaseException().Message;
                    _mainThread.Enqueue(() =>
                    {
                        _lastFailure = why;
                        Debug.LogWarning($"[APL room] server connection failed ({url}): {why}");
                    });
                }
                _mainThread.Enqueue(() => DroppedFor(socket));
            });
        }

        private ClientWebSocket? _connectingSocket;

        /// <summary>A socket's task ended: only counts if it is still the current one (not an old, replaced socket).</summary>
        private void DroppedFor(ClientWebSocket socket)
        {
            if (_socket == socket || (_socket == null && _connecting && _connectingSocket == socket)) Dropped();
        }

        private void Dropped()
        {
            if (_socket == null && !_connecting) return;
            try { _socket?.Dispose(); } catch { /* already gone */ }
            _socket = null;
            _connecting = false;
            _welcomed = false;
            Connected = false;
            if (Code != null && !_resuming)
            {
                _resuming = true;
                Debug.Log($"[APL room] connection lost in room {Code}: reconnecting…");
            }
            Changed?.Invoke();
            // Only come back if we're in (or joining) a room.
            if (_closed || (Code == null && _pending.Count == 0)) return;
            if (Code == null && _attempt + 1 >= GiveUpAfter)
            {
                // Never got in: stop knocking and say why, instead of "Joining…" forever.
                _pending.Clear();
                _intent = null;
                _attempt = 0;
                Error?.Invoke("unreachable", Describe("unreachable") + (_lastFailure.Length > 0 ? $" ({_lastFailure})" : ""));
                return;
            }
            _retryAt = Time.unscaledTime + new[] { 1f, 2f, 4f, 8f, 10f }[Mathf.Clamp(_attempt, 0, 4)];
            _attempt++;
        }

        private void Update()
        {
            while (_mainThread.TryDequeue(out var a)) a();
            while (_inbox.TryDequeue(out var raw)) Handle(raw);
            if (_retryAt > 0 && Time.unscaledTime >= _retryAt)
            {
                _retryAt = -1;
                // Rejoin the room we were in (same playerId = same seat); the server resends room, start, clock, results.
                if (Code != null && !_pending.Any(m => (string?)m["t"] == "join" || (string?)m["t"] == "create"))
                {
                    _pending.Insert(0, new JObject { ["t"] = "join", ["code"] = Code });
                }
                Connect();
            }
            if (_welcomed && Time.unscaledTime >= _pingAt)
            {
                _pingAt = Time.unscaledTime + 15f;
                Send(new JObject { ["t"] = "ping" });
            }
        }

        private void Handle(string raw)
        {
            JObject m;
            try { m = JObject.Parse(raw); }
            catch (Exception) { return; }
            var type = (string?)m["t"];
            if (NetLog && type != "clock") Debug.Log($"[APL net] recv {type} {(type == "error" ? (string?)m["code"] : "")}");
            switch (type)
            {
                case "welcome":
                    _welcomed = true;
                    Connected = true;
                    _attempt = 0;
                    _pingAt = Time.unscaledTime + 15f;
                    Debug.Log($"[APL room] connected to {(string?)m["server"] ?? "server"} (protocol {(int?)m["protocol"]})");
                    var pending = _pending.ToList();
                    _pending.Clear();
                    foreach (var p in pending) Send(p);
                    break;
                case "room":
                    var wasHost = IsHost;
                    var hadCode = Code;
                    Room = (JObject?)m["room"];
                    _seats = Members.Select(RoomSeat.FromJson).ToList();
                    if (Phase != "retention") Retention = null;
                    if (Room?["results"] is JObject inRoom && inRoom.HasValues) Rewards = RoomRewards.FromJson(inRoom, _seats);
                    if (Room?["paused"] != null) _clockPaused = (bool?)Room["paused"];
                    if (Room?["timerSec"] != null) _clockTimer = (int?)Room["timerSec"];
                    if (hadCode == null && Code != null) Debug.Log($"[APL room] in room {Code} as {PlayerName} ({(IsHost ? "host" : "guest")}), phase {Phase}");
                    if (_resuming && Code != null)
                    {
                        _resuming = false;
                        Debug.Log($"[APL room] seat resumed in {Code} (team {MyTeam ?? "none"}, control {(string?)Members.FirstOrDefault(x => (string?)x["id"] == PlayerId)?["control"] ?? "?"})");
                        Notice?.Invoke("resumed", "Reconnected: your seat is back.", m);
                    }
                    if (!wasHost && IsHost && hadCode != null) Notice?.Invoke("youAreHost", "You are the host now.", m);
                    break;
                case "retention":
                    Retention = m;
                    _retentionAt = Time.unscaledTime;
                    break;
                case "start":
                    var setup = RoomSetup.FromJson((JObject)m["setup"]!);
                    var log = ((JArray)m["log"]!).OfType<JObject>().Select(RoomAction.FromJson).ToList();
                    var reason = (string?)m["reason"] ?? "join";
                    try
                    {
                        Auction ??= new RemoteAuction(Data, Send);
                        Auction.Load(setup, log);
                        Debug.Log($"[APL room] auction loaded ({reason}): {log.Count} actions, {Auction.Engine.Results.Count} lots closed");
                        Rebuilt?.Invoke(reason);
                    }
                    catch (RoomDesyncException ex)
                    {
                        Debug.LogWarning($"[APL room] replica desync on start: {ex.Message}");
                        Error?.Invoke("desync", "");
                    }
                    break;
                case "act":
                    try
                    {
                        var action = RoomAction.FromJson((JObject)m["a"]!);
                        // A new lot: last lot's passes are gone.
                        if (action is OpenLotAction) ClearPasses();
                        Auction?.Apply(action);
                    }
                    catch (RoomDesyncException ex)
                    {
                        // Rejoin: the server resends the whole log.
                        Debug.LogWarning($"[APL room] replica desync: {ex.Message}; rejoining");
                        Dropped();
                    }
                    break;
                case "clock":
                    Auction?.Clock(m);
                    ReadClock(m);
                    break;
                case "results":
                    if (m["results"] is JObject results)
                    {
                        Rewards = RoomRewards.FromJson(results, _seats);
                        Debug.Log($"[APL room] results: winner {Rewards.Standings.FirstOrDefault()?.TeamId}, {Rewards.Standings.Count} teams, {Rewards.Awards.Count} awards");
                    }
                    break;
                case "notice":
                    var kind = (string?)m["kind"] ?? "";
                    var text = NoticeText(kind, m);
                    Debug.Log($"[APL room] notice {kind}: {text}");
                    if (text.Length > 0) Notice?.Invoke(kind, text, m);
                    break;
                case "kicked":
                    // The host removed us: no auto-rejoin.
                    _intent = null;
                    Room = null;
                    Retention = null;
                    _seats = new List<RoomSeat>();
                    Debug.Log("[APL room] kicked by the host");
                    Error?.Invoke("kicked", Describe("kicked"));
                    break;
                case "error":
                    var errorCode = (string?)m["code"] ?? "error";
                    var need = (int?)m["need"];
                    var forType = (string?)m["for"];
                    // Our optimistic PASS was refused: the button comes back.
                    if (forType == "pass" && _myPendingPass != null)
                    {
                        _passed.Remove(_myPendingPass);
                        _myPendingPass = null;
                    }
                    // Server behind the app (it picks up new data every few minutes): not the player's fault.
                    var errorText = errorCode == "dataVersion" && need < Data.Version
                        ? "The room server is still updating to the new player data. Try again in a few minutes."
                        : Describe(errorCode, need);
                    Debug.Log($"[APL room] error {errorCode}{(forType != null ? " for " + forType : "")}: {errorText}");
                    // Kicked earlier: don't keep knocking.
                    if (errorCode == "kicked" || errorCode == "locked" || errorCode == "full" || errorCode == "noSuchRoom" || errorCode == "started")
                    {
                        if (Code == null)
                        {
                            _intent = null;
                            _pending.Clear();
                        }
                    }
                    Error?.Invoke(errorCode, errorText);
                    break;
                case "pong":
                    return;
            }
            Changed?.Invoke();
        }

        private void ClearPasses()
        {
            _passed.Clear();
            _myPendingPass = null;
        }

        private void ReadClock(JObject m)
        {
            if (m["paused"] != null) _clockPaused = (bool?)m["paused"];
            if (m["timerSec"] != null) _clockTimer = (int?)m["timerSec"];
            var lot = (int?)m["lot"] ?? LotNumber;
            if (lot != _passLot)
            {
                _passLot = lot;
                // Keep our own pass if it was sent for this lot and the server hasn't echoed it yet.
                var mine = _myPendingPass;
                ClearPasses();
                if (mine != null && lot > 0) _myPendingPass = mine;
            }
            LotNumber = lot;
            if (m["passed"] is JArray passed)
            {
                var server = passed.Select(x => (string?)x ?? "").Where(x => x.Length > 0).ToList();
                _passed.Clear();
                foreach (var t in server) _passed.Add(t);
                if (_myPendingPass != null)
                {
                    if (server.Contains(_myPendingPass)) _myPendingPass = null;
                    else _passed.Add(_myPendingPass);
                }
            }
        }

        private string TeamName(string? team) => team == null ? "" : Data.Franchises.FirstOrDefault(f => f.Id == team)?.ShortName ?? team;

        private string PlayerNameOf(string? id) =>
            id == null ? "Someone" : id == PlayerId ? "You" : _seats.FirstOrDefault(s => s.Id == id)?.Name ?? "A player";

        /// <summary>The toast for a notice; empty = no toast.</summary>
        private string NoticeText(string kind, JObject m)
        {
            var by = PlayerNameOf((string?)m["by"]);
            var player = PlayerNameOf((string?)m["player"]);
            var team = TeamName((string?)m["team"]);
            return kind switch
            {
                "paused" => $"{by} paused the auction.",
                "resumed" => $"{by} resumed the auction.",
                "timer" => $"Bid timer set to {(int?)m["timerSec"]} s.",
                "hostChanged" => (string?)m["player"] == PlayerId ? "You are the host now." : $"{player} is the host now.",
                "kicked" => $"{player} was removed{(team.Length > 0 ? $" ({team} is a bot now)" : "")}.",
                "locked" => (bool?)m["locked"] == true ? "The room is locked." : "The room is open to join.",
                "simulate" => (string?)m["player"] == PlayerId ? "A bot is bidding for you." : $"{player} handed {team} to a bot.",
                "away" => $"{player} dropped out: a bot is playing {team}.",
                "takeBack" => (string?)m["player"] == PlayerId ? "You're back in control." : $"{player} took back {team}.",
                "forceClose" => (bool?)m["sold"] == true ? $"{by} closed the lot: sold." : $"{by} closed the lot: unsold.",
                "undo" => $"{by} undid the last sale.",
                "ended" => $"{by} ended the auction.",
                "instantFinish" => "Every team is on autopilot: the rest of the auction is settled.",
                _ => "",
            };
        }

        public static string Describe(string code, int? need = null) => code switch
        {
            "noSuchRoom" => "No room with that code.",
            "full" => "That room is full.",
            "started" => "That auction has already started.",
            "locked" => "That room is locked by its host.",
            "teamTaken" => "Someone already picked that team.",
            "unknownTeam" => "That team isn't in this auction.",
            "notEnoughPlayers" => "Need at least 2 players with teams to start.",
            "unseatedPlayers" => "Everyone needs to pick a team first.",
            "notReady" => "Not everyone is ready yet: wait for every player to tap I'M READY.",
            "notHost" => "Only the host can do that.",
            "protocol" => "Please update the app to play in rooms.",
            "dataVersion" => $"This room uses newer player data (v{need}). Please update the app.",
            "serverFull" => "The server is busy; try again shortly.",
            "invalidRetention" => "Those retentions aren't allowed.",
            "staleAmount" => "Another team bid first: BID shows the new price.",
            "cannotAfford" => "You can't afford that bid.",
            "squadFull" => "Your squad is full.",
            "overseasLimit" => "Your overseas slots are full.",
            "priceCap" => "That's over the price cap.",
            "alreadyLeading" => "You already have the top bid.",
            "passed" => "You passed on this player.",
            "alreadyPassed" => "You already passed on this player.",
            "leading" => "You have the top bid: you can't pass.",
            "biddingClosed" => "Bidding is closed for this player.",
            "noOpenLot" => "No player on the block right now.",
            "paused" => "The auction is paused.",
            "notInControl" => "A bot is playing your team. Take back control first.",
            "notOnAutopilot" => "You're already in control.",
            "noRtm" => "No Right to Match card to answer.",
            "nothingToUndo" => "Nothing to undo yet.",
            "badTimer" => "The bid timer must be 5 to 30 seconds.",
            "notAllowedInRooms" => "Not available in rooms.",
            "slowDown" => "Too fast: slow down a little.",
            "desync" => "Reconnecting…",
            "kicked" => "The host removed you from that room.",
            "unreachable" => "Can't reach the room server. Check the server address and try again.",
            "notInRoom" => "You're not in that room any more.",
            "notAuction" => "The auction isn't running.",
            _ => $"Something went wrong ({code}).",
        };
    }
}
