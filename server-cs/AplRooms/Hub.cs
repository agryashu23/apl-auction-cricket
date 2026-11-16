using System.Diagnostics;
using System.Security.Cryptography;
using Apl.Core;

namespace Apl.Rooms;

/// <summary>
/// Every room on this server. One lock (Sync) guards all room state: sessions route messages
/// under it and the tick loop advances rooms under it; sends are queued per socket, so nothing
/// blocks while it is held.
/// </summary>
public sealed class Hub
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O, 1/I

    private readonly CancellationTokenSource _stop = new();

    public Hub(Dataset data, ServerOptions options)
    {
        Data = data;
        Options = options;
    }

    public object Sync { get; } = new();
    public Dataset Data { get; }
    public ServerOptions Options { get; }
    public Dictionary<string, Room> Rooms { get; } = new();

    public object Health()
    {
        lock (Sync)
        {
            return new
            {
                ok = true,
                server = "apl-rooms-cs",
                protocol = RoomProtocol.Version,
                rooms = Rooms.Count,
                players = Rooms.Values.Sum(r => r.ConnectedCount),
                dataVersion = Data.Version,
            };
        }
    }

    public Room CreateRoom(string hostId, AuctionFormat format, bool retention, int timerSec)
    {
        var code = NewCode();
        var room = new Room(this, code, format.Id, retention || format.RetentionOnly, hostId, timerSec);
        Rooms[code] = room;
        RoomServer.Log("room_created", new { code, format = format.Id, retention = room.Retention });
        return room;
    }

    private string NewCode()
    {
        while (true)
        {
            var chars = new char[6];
            for (var i = 0; i < chars.Length; i++) chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            var code = new string(chars);
            if (!Rooms.ContainsKey(code)) return code;
        }
    }

    public void StartLoops()
    {
        _ = Task.Run(TickLoop);
        _ = Task.Run(CleanupLoop);
    }

    public void Stop()
    {
        _stop.Cancel();
    }

    private async Task TickLoop()
    {
        var watch = Stopwatch.StartNew();
        var last = watch.Elapsed.TotalMilliseconds;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Options.TickMs));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                var now = watch.Elapsed.TotalMilliseconds;
                var elapsed = now - last;
                last = now;
                lock (Sync)
                {
                    foreach (var room in Rooms.Values.ToList())
                    {
                        try
                        {
                            room.Tick(elapsed);
                        }
                        catch (Exception e)
                        {
                            RoomServer.Log("tick_failed", new { code = room.Code, error = e.ToString() });
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CleanupLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(0.2, Options.CleanupEverySec)));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                lock (Sync) Cleanup();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Frees stale lobby seats; drops finished rooms after 30 min and abandoned ones after 10.</summary>
    public void Cleanup()
    {
        var now = DateTime.UtcNow;
        foreach (var room in Rooms.Values.ToList())
        {
            room.PruneLobby();
            var finished = room.FinishedAt != null && (now - room.FinishedAt.Value).TotalSeconds > Options.FinishedKeepSec;
            var abandoned = room.ConnectedCount == 0 && (now - room.LastActivity).TotalSeconds > Options.AbandonedSec;
            if (finished || abandoned)
            {
                Rooms.Remove(room.Code);
                RoomServer.Log("room_dropped", new { code = room.Code, reason = finished ? "finished" : "abandoned" });
            }
        }
    }
}
