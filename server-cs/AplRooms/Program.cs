// APL room server v2 (local). See docs/ROOMS_PROTOCOL.md.
//
//   dotnet run --project server-cs\AplRooms
//   dotnet run --project server-cs\AplRooms -- --port 8787 --host 0.0.0.0 --min-players 2
//
// Flags: --port (8787) --host (localhost; 0.0.0.0 for phones on the LAN) --data-file (repo data/apl_dataset.json)
//        --min-players (2) --takeover-sec (30) --retention-sec (150) --time-scale (1, tests only)
using Apl.Rooms;

var options = ServerOptions.Parse(args);
var server = RoomServer.Create(options);
await server.RunAsync();
