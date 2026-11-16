// End-to-end room scenarios against a real server: N fake human clients over WebSockets, each
// with a phone replica (RemoteAuction). Prints PASS/FAIL per scenario; exit code 1 on any FAIL.
//
//   dotnet run --project server-cs\RoomBots                      # hosts its own server on :8790 (test timings)
//   dotnet run --project server-cs\RoomBots -- --only pass       # scenarios whose name contains "pass"
//   dotnet run --project server-cs\RoomBots -- --url ws://localhost:8787/ws
//        (a running server started with: --time-scale 10 --takeover-sec 1.5 --retention-sec 4)
using Apl.Core;
using Apl.RoomBots;
using Apl.Rooms;
using Newtonsoft.Json.Linq;

string? Arg(string name)
{
    var i = Array.IndexOf(args, "--" + name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var dataFile = Arg("data-file") ?? ServerOptions.FindDataFile();
var data = Dataset.FromJson(File.ReadAllText(dataFile));
RoomServer? server = null;
Uri url;
if (Arg("url") is string external)
{
    url = new Uri(external);
}
else
{
    var options = new ServerOptions
    {
        Port = int.Parse(Arg("port") ?? "8790"),
        DataFile = dataFile,
        MinPlayers = 2,
        TimeScale = Scenarios.TimeScale,
        TakeoverSec = Scenarios.TakeoverSec,
        RetentionSec = Scenarios.RetentionSec,
        AbandonedSec = 3,
        CleanupEverySec = 0.5,
    };
    RoomServer.Quiet = true;
    server = RoomServer.Create(options, data);
    await server.StartAsync();
    url = new Uri($"ws://localhost:{options.Port}/ws");
    Console.WriteLine($"[harness] in-process server on {url} (time x{options.TimeScale}, takeover {options.TakeoverSec}s, retention {options.RetentionSec}s)");
}

var scenarios = new Scenarios(data, url);
var only = Arg("only");
var results = new List<(string Name, bool Ok, string Note, double Secs)>();
foreach (var (name, body) in scenarios.All())
{
    if (only != null && !name.Contains(only, StringComparison.OrdinalIgnoreCase)) continue;
    var started = Bot.Now;
    string note;
    bool ok;
    try
    {
        note = await body();
        ok = true;
    }
    catch (Exception e)
    {
        ok = false;
        note = e is ScenarioFailed ? e.Message : e.ToString();
    }
    var secs = Bot.Now - started;
    results.Add((name, ok, note, secs));
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}  ({secs:0.0}s){(note.Length > 0 ? "  -- " + note : "")}");
}

Console.WriteLine();
Console.WriteLine($"{results.Count(r => r.Ok)}/{results.Count} scenarios passed");
if (server != null) await server.StopAsync();
return results.All(r => r.Ok) ? 0 : 1;
