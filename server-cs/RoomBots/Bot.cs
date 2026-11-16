using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using Apl.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Apl.RoomBots;

/// <summary>
/// A fake human: one socket, everything it receives (time-stamped), and a RemoteAuction replica
/// built exactly as the Unity client builds it, so every scenario also checks phone replay.
/// </summary>
public sealed class Bot
{
    public static readonly Stopwatch Clock = Stopwatch.StartNew();
    public static double Now => Clock.Elapsed.TotalSeconds;

    private readonly Dataset _data;
    private readonly Uri _url;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;

    public Bot(Dataset data, Uri url, string id, string name)
    {
        _data = data;
        _url = url;
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; }
    public List<(double At, JObject M)> Inbox { get; } = new();
    public JObject? Room { get; private set; }
    public JObject? LastClock { get; private set; }
    public JObject? Results { get; private set; }
    public RemoteAuction? Replica { get; private set; }
    public int Starts { get; private set; }
    public string? LastStartReason { get; private set; }
    public string? Desync { get; private set; }
    public bool Kicked { get; private set; }

    /// <summary>Every action since the last "start", with when it arrived.</summary>
    public List<(double At, JObject A)> Acts { get; } = new();

    public string? Code => (string?)Room?["code"];
    public string Phase => (string?)Room?["phase"] ?? "none";
    public string? Host => (string?)Room?["host"];
    public string ClockPhase => (string?)LastClock?["phase"] ?? "none";

    public JObject? MemberJson(string id) => (Room?["members"] as JArray)?.OfType<JObject>().FirstOrDefault(m => (string?)m["id"] == id);
    public JObject? TeamJson(string team) => (Room?["teams"] as JArray)?.OfType<JObject>().FirstOrDefault(m => (string?)m["team"] == team);
    public string? MyTeam => (string?)MemberJson(Id)?["team"];
    public string? ControlOf(string team) => (string?)TeamJson(team)?["control"];
    public List<string> Humans => (LastClock?["humans"] as JArray)?.Select(x => (string)x!).ToList() ?? new List<string>();

    public async Task ConnectAsync()
    {
        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(_url, _cts.Token);
        _ = Task.Run(() => ReceiveLoop(_ws, _cts.Token));
        var mark = Mark;
        Send(new JObject { ["t"] = "hello", ["protocol"] = RoomProtocol.Version, ["playerId"] = Id, ["name"] = Name, ["dataVersion"] = _data.Version });
        if (await WaitMsg(m => (string?)m["t"] == "welcome", 5000, mark) == null) throw new Exception($"{Name}: no welcome");
    }

    /// <summary>Pulls the plug without a close handshake (a phone losing signal).</summary>
    public void Drop()
    {
        _cts?.Cancel();
        try { _ws?.Abort(); } catch { /* gone */ }
    }

    public void Send(JObject m)
    {
        var ws = _ws;
        if (ws == null) return;
        var bytes = Encoding.UTF8.GetBytes(m.ToString(Formatting.None));
        _sendLock.Wait();
        try
        {
            if (ws.State == WebSocketState.Open) ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Dropped.
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Send(string type, params (string Key, JToken Value)[] fields)
    {
        var m = new JObject { ["t"] = type };
        foreach (var (k, v) in fields) m[k] = v;
        Send(m);
    }

    public int Mark
    {
        get
        {
            lock (_gate) return Inbox.Count;
        }
    }

    public int ActMark
    {
        get
        {
            lock (_gate) return Acts.Count;
        }
    }

    public T Read<T>(Func<Bot, T> f)
    {
        lock (_gate) return f(this);
    }

    public async Task<bool> WaitFor(Func<Bot, bool> cond, int timeoutMs)
    {
        var until = Now + timeoutMs / 1000.0;
        while (true)
        {
            lock (_gate)
            {
                if (cond(this)) return true;
            }
            if (Now > until) return false;
            await Task.Delay(10);
        }
    }

    /// <summary>The first message at or after <paramref name="from"/> matching, or null on timeout.</summary>
    public async Task<JObject?> WaitMsg(Func<JObject, bool> pred, int timeoutMs, int from = 0)
    {
        JObject? found = null;
        await WaitFor(b =>
        {
            for (var i = from; i < b.Inbox.Count; i++)
            {
                if (pred(b.Inbox[i].M))
                {
                    found = b.Inbox[i].M;
                    return true;
                }
            }
            return false;
        }, timeoutMs);
        return found;
    }

    /// <summary>Waits for an error answering <paramref name="forType"/> after the mark; returns its code.</summary>
    public async Task<string?> ErrorFor(string forType, int from, int timeoutMs = 2000)
    {
        var e = await WaitMsg(m => (string?)m["t"] == "error" && (string?)m["for"] == forType, timeoutMs, from);
        return (string?)e?["code"];
    }

    /// <summary>The first act at or after the act mark matching, with its arrival time.</summary>
    public async Task<(double At, JObject A)?> WaitAct(Func<JObject, bool> pred, int timeoutMs, int from)
    {
        (double, JObject)? found = null;
        await WaitFor(b =>
        {
            for (var i = from; i < b.Acts.Count; i++)
            {
                if (pred(b.Acts[i].A))
                {
                    found = b.Acts[i];
                    return true;
                }
            }
            return false;
        }, timeoutMs);
        return found;
    }

    private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        var sb = new StringBuilder();
        try
        {
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(buffer, token);
                if (r.MessageType == WebSocketMessageType.Close) break;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                if (!r.EndOfMessage) continue;
                Handle(sb.ToString());
                sb.Clear();
            }
        }
        catch (Exception)
        {
            // Dropped or closed.
        }
    }

    private void Handle(string raw)
    {
        var m = JObject.Parse(raw);
        var at = Now;
        lock (_gate)
        {
            Inbox.Add((at, m));
            switch ((string?)m["t"])
            {
                case "room":
                    Room = (JObject?)m["room"];
                    break;
                case "start":
                    try
                    {
                        Replica = new RemoteAuction(_data, _ => { });
                        Replica.Load(RoomSetup.FromJson((JObject)m["setup"]!), ((JArray)m["log"]!).OfType<JObject>().Select(RoomAction.FromJson));
                    }
                    catch (Exception e)
                    {
                        Desync = "load: " + e.Message;
                    }
                    Starts++;
                    LastStartReason = (string?)m["reason"];
                    Acts.Clear();
                    break;
                case "act":
                    var a = (JObject)m["a"]!;
                    Acts.Add((at, a));
                    try
                    {
                        Replica?.Apply(RoomAction.FromJson(a));
                    }
                    catch (Exception e)
                    {
                        Desync = "apply: " + e.Message;
                    }
                    break;
                case "clock":
                    LastClock = m;
                    Replica?.Clock(m);
                    break;
                case "results":
                    Results = (JObject?)m["results"];
                    break;
                case "kicked":
                    Kicked = true;
                    break;
            }
        }
    }
}
