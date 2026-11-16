using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Apl.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Apl.Rooms;

/// <summary>One socket: reads messages, routes them to its room under the hub lock, and sends from a queue.</summary>
public sealed class Session
{
    private const int MaxMessageBytes = 8 * 1024;
    private const int MaxMessagesPerSecond = 30;

    private readonly Hub _hub;
    private readonly WebSocket _socket;
    private readonly Channel<string> _outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private long _windowStart;
    private int _windowCount;

    public Session(Hub hub, WebSocket socket)
    {
        _hub = hub;
        _socket = socket;
    }

    public string? PlayerId { get; private set; }
    public string Name { get; private set; } = "Player";
    public int? DataVersion { get; private set; }
    public Room? Room { get; set; }

    public void SendJson(string json) => _outbox.Writer.TryWrite(json);

    public void Send(JObject m) => SendJson(m.ToString(Formatting.None));

    public void Error(string code, string? forType = null, string? detail = null, int? need = null)
    {
        var m = new JObject { ["t"] = "error", ["code"] = code };
        if (forType != null) m["for"] = forType;
        if (detail != null) m["detail"] = detail;
        if (need != null) m["need"] = need.Value;
        Send(m);
    }

    public async Task RunAsync(CancellationToken aborted)
    {
        var writer = Task.Run(WriteLoop);
        try
        {
            await ReadLoop(aborted);
        }
        catch (Exception) when (_socket.State != WebSocketState.Open)
        {
            // Dropped: the room keeps the seat.
        }
        catch (WebSocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_hub.Sync) Room?.Disconnected(this);
            _outbox.Writer.TryComplete();
            try { await writer; } catch { /* closing anyway */ }
            if (_socket.State == WebSocketState.Open)
            {
                try { await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { /* gone */ }
            }
        }
    }

    private async Task WriteLoop()
    {
        await foreach (var json in _outbox.Reader.ReadAllAsync())
        {
            if (_socket.State != WebSocketState.Open) continue;
            try
            {
                await _socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception)
            {
                // The read loop notices the drop.
            }
        }
    }

    private async Task ReadLoop(CancellationToken aborted)
    {
        var buffer = new byte[16 * 1024];
        var message = new MemoryStream();
        var tooLarge = false;
        while (_socket.State == WebSocketState.Open && !aborted.IsCancellationRequested)
        {
            var r = await _socket.ReceiveAsync(buffer, aborted);
            if (r.MessageType == WebSocketMessageType.Close) break;
            if (!tooLarge)
            {
                message.Write(buffer, 0, r.Count);
                if (message.Length > MaxMessageBytes) tooLarge = true;
            }
            if (!r.EndOfMessage) continue;
            if (tooLarge) Error("tooLarge");
            else if (r.MessageType == WebSocketMessageType.Text) OnMessage(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
            message.SetLength(0);
            tooLarge = false;
        }
    }

    private void OnMessage(string raw)
    {
        var now = Environment.TickCount64;
        if (now - _windowStart >= 1000)
        {
            _windowStart = now;
            _windowCount = 0;
        }
        if (++_windowCount > MaxMessagesPerSecond)
        {
            Error("slowDown");
            return;
        }
        JObject m;
        try
        {
            m = JObject.Parse(raw);
        }
        catch (Exception)
        {
            Error("badJson");
            return;
        }
        var type = (string?)m["t"] ?? "";
        try
        {
            lock (_hub.Sync)
            {
                var error = Route(type, m);
                if (error != null) Error(error, type);
            }
        }
        catch (Exception e)
        {
            Error("badRequest", type, e.Message);
        }
    }

    private string? Route(string type, JObject m)
    {
        if (type == "ping")
        {
            Send(new JObject { ["t"] = "pong" });
            return null;
        }
        if (type == "hello") return Hello(m);
        if (PlayerId == null) return "helloFirst";
        var me = PlayerId;
        switch (type)
        {
            case "create":
                return Create(m);
            case "join":
                return Join((string?)m["code"] ?? "");
            case "leave":
                Room?.Leave(me);
                Room = null;
                return null;
            case "fast":
            case "skip":
            case "ff":
                return "notAllowedInRooms";
        }
        var r = Room;
        if (r == null) return "notInRoom";
        return type switch
        {
            "pick" => r.Pick(me, (string)m["team"]!),
            "start" => r.Start(me),
            "ready" => r.SetReady(me, (bool?)m["ready"] ?? true),
            "retain" => r.Retain(me, ((JArray)m["ids"]!).Select(x => (string)x!).ToList()),
            "settings" => r.Settings(me, m),
            "lock" => r.Lock(me, (bool)m["locked"]!),
            "kick" => r.Kick(me, (string)m["player"]!),
            "host" => r.TransferHost(me, (string)m["player"]!),
            "bid" => r.Bid(me, (int)m["amount"]!),
            "pass" => r.Pass(me),
            "rtm" => r.Rtm(me, (bool)m["use"]!),
            "simulate" => r.Simulate(me),
            "takeBack" => r.TakeBack(me),
            "pause" => r.Pause(me),
            "resume" => r.Resume(me),
            "forceClose" => r.ForceClose(me, (bool?)m["sold"] ?? true),
            "undo" => r.Undo(me),
            "end" => r.End(me),
            _ => "unknownMessage",
        };
    }

    private string? Hello(JObject m)
    {
        var protocol = (int?)m["protocol"];
        if (protocol != RoomProtocol.Version)
        {
            Error("protocol", "hello", need: RoomProtocol.Version);
            return null;
        }
        var id = ((string?)m["playerId"] ?? "").Trim();
        if (id.Length == 0 || id.Length > 64) return "badPlayerId";
        PlayerId = id;
        Name = CleanName((string?)m["name"] ?? "");
        DataVersion = (int?)m["dataVersion"];
        Send(new JObject { ["t"] = "welcome", ["protocol"] = RoomProtocol.Version, ["dataVersion"] = _hub.Data.Version, ["server"] = "apl-rooms-cs" });
        return null;
    }

    private string? Create(JObject m)
    {
        if (DataVersion != _hub.Data.Version)
        {
            Error("dataVersion", "create", need: _hub.Data.Version);
            return null;
        }
        if (_hub.Rooms.Count >= _hub.Options.MaxRooms) return "serverFull";
        var format = Room.FindFormat(_hub.Data, (string?)m["format"]);
        if (format == null) return "unknownFormat";
        var timer = (int?)m["timer"] ?? _hub.Data.Rules.BidTimerSec;
        if (timer < RoomRules.MinTimerSec || timer > RoomRules.MaxTimerSec) return "badTimer";
        Room?.Leave(PlayerId!);
        var room = _hub.CreateRoom(PlayerId!, format, (bool?)m["retention"] ?? false, timer);
        Room = room;
        room.Join(this, PlayerId!, Name);
        // The host's franchise comes with the room, before anyone else can join.
        if ((string?)m["team"] is string team)
        {
            var error = room.Pick(PlayerId!, team);
            if (error != null) Error(error, "create");
        }
        return null;
    }

    private string? Join(string code)
    {
        if (!_hub.Rooms.TryGetValue(code.Trim().ToUpperInvariant(), out var r)) return "noSuchRoom";
        if (DataVersion != _hub.Data.Version)
        {
            Error("dataVersion", "join", need: _hub.Data.Version);
            return null;
        }
        if (!ReferenceEquals(Room, r)) Room?.Leave(PlayerId!);
        var error = r.Join(this, PlayerId!, Name);
        if (error != null) return error;
        Room = r;
        return null;
    }

    private static string CleanName(string raw)
    {
        var cleaned = Regex.Replace(raw, @"[\x00-\x1F<>]", "").Trim();
        if (cleaned.Length == 0) return "Player";
        return cleaned.Length > 20 ? cleaned.Substring(0, 20) : cleaned;
    }
}
