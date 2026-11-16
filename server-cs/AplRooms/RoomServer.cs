using System.Net.WebSockets;
using System.Text.Json;
using Apl.Core;

namespace Apl.Rooms;

/// <summary>Server settings: command-line flags (--name value) or env vars, else the defaults.</summary>
public sealed class ServerOptions
{
    public int Port = 8787;
    public string Host = "localhost";
    public string? DataFile;
    public int MinPlayers = 2;

    /// <summary>Max humans in a room: the number of franchises (set from the data).</summary>
    public int MaxPlayers = 10;
    public int MaxRooms = 500;

    /// <summary>A dropped player's team goes on autopilot after this (real seconds).</summary>
    public double TakeoverSec = 30;
    public double RetentionSec = 150;

    /// <summary>Lobby seats of players gone this long are freed.</summary>
    public double LobbyPruneSec = 120;
    public double FinishedKeepSec = 30 * 60;
    public double AbandonedSec = 10 * 60;
    public double CleanupEverySec = 60;

    /// <summary>Game ms per real ms (tests run auctions fast).</summary>
    public double TimeScale = 1;
    public int TickMs = 50;
    public int ClockEveryMs = 250;

    /// <summary>The 3D hall's pacing (same as solo, AplBoot).</summary>
    public RoomAuctionOptions Pacing() => new RoomAuctionOptions
    {
        RevealMs = 11000,
        ResultMs = 5500,
        AiRtmThinkMs = 6500,
        HumanRtmTimeoutMs = 10000,
        AiThinkScale = 3.6,
    };

    public static ServerOptions Parse(string[] args)
    {
        string? Arg(string name, string env)
        {
            var i = Array.IndexOf(args, "--" + name);
            if (i >= 0 && i + 1 < args.Length) return args[i + 1];
            return Environment.GetEnvironmentVariable(env);
        }

        var o = new ServerOptions();
        if (Arg("port", "APL_ROOMS_PORT") is string port) o.Port = int.Parse(port);
        if (Arg("host", "APL_ROOMS_HOST") is string host) o.Host = host;
        o.DataFile = Arg("data-file", "APL_ROOMS_DATA");
        if (Arg("min-players", "APL_ROOMS_MIN_PLAYERS") is string min) o.MinPlayers = int.Parse(min);
        if (Arg("takeover-sec", "APL_ROOMS_TAKEOVER_SEC") is string takeover) o.TakeoverSec = double.Parse(takeover, System.Globalization.CultureInfo.InvariantCulture);
        if (Arg("retention-sec", "APL_ROOMS_RETENTION_SEC") is string retention) o.RetentionSec = double.Parse(retention, System.Globalization.CultureInfo.InvariantCulture);
        if (Arg("time-scale", "APL_ROOMS_TIME_SCALE") is string scale) o.TimeScale = double.Parse(scale, System.Globalization.CultureInfo.InvariantCulture);
        return o;
    }

    /// <summary>The repo's data/apl_dataset.json (the client's Resources copy is the same file).</summary>
    public static string FindDataFile()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = start;
            while (dir != null)
            {
                var candidate = Path.Combine(dir, "data", "apl_dataset.json");
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
        }
        throw new FileNotFoundException("data/apl_dataset.json not found; pass --data-file");
    }
}

/// <summary>The web host: /health and the /ws WebSocket, around one Hub.</summary>
public sealed class RoomServer
{
    private RoomServer(WebApplication app, Hub hub, ServerOptions options)
    {
        App = app;
        Hub = hub;
        Options = options;
    }

    public WebApplication App { get; }
    public Hub Hub { get; }
    public ServerOptions Options { get; }
    public string Url => $"http://{Options.Host}:{Options.Port}";

    public static RoomServer Create(ServerOptions options, Dataset? data = null)
    {
        var path = options.DataFile ?? ServerOptions.FindDataFile();
        data ??= Dataset.FromJson(File.ReadAllText(path));
        options.MaxPlayers = data.Franchises.Count;

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var hub = new Hub(data, options);

        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.MapGet("/health", () => Results.Json(hub.Health()));
        app.Map("/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("websocket only");
                return;
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await new Session(hub, socket).RunAsync(context.RequestAborted);
        });
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            hub.StartLoops();
            Log("listening", new { url = $"http://{options.Host}:{options.Port}", ws = $"ws://{options.Host}:{options.Port}/ws", dataVersion = data.Version, dataFile = path, minPlayers = options.MinPlayers, timeScale = options.TimeScale });
        });
        app.Lifetime.ApplicationStopping.Register(hub.Stop);
        return new RoomServer(app, hub, options);
    }

    public Task RunAsync() => App.RunAsync();

    public Task StartAsync() => App.StartAsync();

    public Task StopAsync() => App.StopAsync();

    public static bool Quiet;

    public static void Log(string evt, object fields)
    {
        if (Quiet) return;
        var json = JsonSerializer.Serialize(fields);
        Console.WriteLine($"{{\"ts\":\"{DateTime.UtcNow:O}\",\"event\":\"{evt}\",{json.Substring(1)}");
    }
}
