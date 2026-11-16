using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Apl.Client.Online
{
    /// <summary>A seat's connection: a person playing, a bot playing for him, or gone (a bot covers).</summary>
    public enum SeatState { Online, Bot, Disconnected }

    /// <summary>One person in the room (lobby and auction), as the server last described him.</summary>
    public sealed class RoomSeat
    {
        public string Id = "", Name = "";
        public string? Team;
        public bool Ready, Host;
        public SeatState State = SeatState.Online;

        /// <summary>The server's word for who drives the team: human, autopilot, away or ai.</summary>
        public string Control = "human";

        /// <summary>He asked a bot to bid for him (SIMULATE); he can take back control.</summary>
        public bool Simulating;

        /// <summary>A bot plays this seat right now (simulating, or disconnected).</summary>
        public bool BotPlaying => Simulating || State != SeatState.Online;

        public static RoomSeat FromJson(JObject m)
        {
            var seat = new RoomSeat
            {
                Id = Str(m, "id", "playerId") ?? "",
                Name = Str(m, "name") ?? "Player",
                Team = Str(m, "team", "teamId"),
                Ready = Bool(m, "ready"),
                Host = Bool(m, "host", "isHost"),
                Simulating = Bool(m, "simulating", "auto", "botControl"),
            };
            // v2: control human / autopilot / away / ai, plus connected.
            var control = Str(m, "control");
            var state = Str(m, "state", "conn", "status");
            if (control != null)
            {
                seat.Simulating = control == "autopilot";
                var connected = m["connected"] == null || m["connected"]!.Type != JTokenType.Boolean || (bool)m["connected"]!;
                seat.State = control == "autopilot" || control == "away" || control == "ai" ? SeatState.Bot
                    : connected ? SeatState.Online : SeatState.Disconnected;
                seat.Control = control;
            }
            else if (state != null)
            {
                seat.State = state.ToLowerInvariant() switch
                {
                    "bot" => SeatState.Bot,
                    "disconnected" => SeatState.Disconnected,
                    "offline" => SeatState.Disconnected,
                    _ => SeatState.Online,
                };
            }
            else if (m["connected"] != null && m["connected"]!.Type == JTokenType.Boolean)
            {
                seat.State = (bool)m["connected"]! ? SeatState.Online : SeatState.Disconnected;
            }
            if (seat.Simulating && seat.State == SeatState.Online) seat.State = SeatState.Bot;
            return seat;
        }

        internal static string? Str(JObject m, params string[] keys)
        {
            foreach (var k in keys)
            {
                var t = m[k];
                if (t != null && t.Type != JTokenType.Null) return (string?)t;
            }
            return null;
        }

        internal static bool Bool(JObject m, params string[] keys)
        {
            foreach (var k in keys)
            {
                var t = m[k];
                if (t != null && t.Type == JTokenType.Boolean) return (bool)t;
            }
            return false;
        }
    }

    /// <summary>One line of how a team earned its points ("Squad rating 82 → +410").</summary>
    public sealed class PointLine
    {
        public string Label = "";
        public int Points;
    }

    /// <summary>A team's place at the end of a room auction.</summary>
    public sealed class RoomStanding
    {
        public int Rank, Points;
        public string TeamId = "", Name = "";
        public string? PlayerId;

        /// <summary>A person's team (false: a bot franchise).</summary>
        public bool Human;
        public string Grade = "", Control = "";
        public int Overall;
        public List<PointLine> Breakdown = new List<PointLine>();
    }

    /// <summary>An award card: Best Squad, Steal of the Auction, Big Spender, Most Balanced, Value King …</summary>
    public sealed class RoomAward
    {
        public string Id = "", Title = "", TeamId = "", Detail = "";
        public string? PlayerId;
        public int Points;

        /// <summary>Steal / biggest buy: what was paid and the player's reputation price (lakhs).</summary>
        public int? Price, Worth;
    }

    /// <summary>The end-of-auction rewards: standings with points, award cards, each team's breakdown.</summary>
    public sealed class RoomRewards
    {
        /// <summary>Unique per finished auction (career totals count it once).</summary>
        public string AuctionId = "";
        public List<RoomStanding> Standings = new List<RoomStanding>();
        public List<RoomAward> Awards = new List<RoomAward>();

        /// <summary>The scoring rules, one line each (shown under the breakdown).</summary>
        public List<string> Scoring = new List<string>();
        public string WinnerId = "";

        /// <summary>The best-ranked team that had a person at the end.</summary>
        public string? TopHumanId;

        public RoomStanding? For(string teamId) => Standings.FirstOrDefault(s => s.TeamId == teamId);

        /// <summary>
        /// The place to show. The engine ranks by total, then squad rating (overall), then team id; a tie on
        /// total AND rating is only split by the team id, so those teams share the place (Shared = true).
        /// </summary>
        public (int Place, bool Shared) Place(RoomStanding s)
        {
            var better = Standings.Count(o => o.Points > s.Points || (o.Points == s.Points && o.Overall > s.Overall));
            var shared = Standings.Any(o => o != s && o.Points == s.Points && o.Overall == s.Overall);
            return (better + 1, shared);
        }

        /// <summary>"1ST", "=2ND" …</summary>
        public string PlaceLabel(RoomStanding s)
        {
            var (place, shared) = Place(s);
            var suffix = (place % 100) is 11 or 12 or 13 ? "TH" : (place % 10) switch { 1 => "ST", 2 => "ND", 3 => "RD", _ => "TH" };
            return (shared ? "=" : "") + place + suffix;
        }

        /// <summary>Why a team tied on points is placed where it is; null when nobody has its total.</summary>
        public string? TieNote(RoomStanding s)
        {
            var tied = Standings.Where(o => o != s && o.Points == s.Points).ToList();
            if (tied.Count == 0) return null;
            if (tied.Any(o => o.Overall == s.Overall)) return $"Tie on {s.Points:N0} pts and squad rating {s.Overall}: shared place";
            var above = tied.Where(o => o.Overall > s.Overall).OrderBy(o => o.Overall).FirstOrDefault();
            var below = tied.Where(o => o.Overall < s.Overall).OrderByDescending(o => o.Overall).FirstOrDefault();
            return below != null
                ? $"Tie on {s.Points:N0} pts: higher squad rating ({s.Overall} vs {below.Overall})"
                : $"Tie on {s.Points:N0} pts: lower squad rating ({s.Overall} vs {above!.Overall})";
        }

        public static RoomRewards FromJson(JObject j, IEnumerable<RoomSeat>? seats = null)
        {
            var r = new RoomRewards { AuctionId = RoomSeat.Str(j, "auctionId", "id") ?? "" };
            var seatByTeam = (seats ?? Enumerable.Empty<RoomSeat>()).Where(s => s.Team != null).GroupBy(s => s.Team!).ToDictionary(g => g.Key, g => g.First());
            if ((j["standings"] ?? j["table"]) is JArray table)
            {
                foreach (var t in table.OfType<JObject>())
                {
                    var s = new RoomStanding
                    {
                        TeamId = RoomSeat.Str(t, "team", "teamId") ?? "",
                        Rank = (int?)t["rank"] ?? 0,
                        Points = Int(t["total"]) ?? Int(t["points"]) ?? 0,
                        PlayerId = RoomSeat.Str(t, "owner", "playerId", "player"),
                        Name = RoomSeat.Str(t, "name") ?? "",
                        Human = RoomSeat.Bool(t, "human") || RoomSeat.Str(t, "owner") != null,
                        Grade = RoomSeat.Str(t, "grade") ?? "",
                        Overall = Int(t["overall"]) ?? 0,
                        Control = RoomSeat.Str(t, "control") ?? "",
                    };
                    if (seatByTeam.TryGetValue(s.TeamId, out var seat))
                    {
                        s.Human = true;
                        s.PlayerId ??= seat.Id;
                        if (s.Name.Length == 0) s.Name = seat.Name;
                    }
                    // v2: "points": {"squad": 324, "balance": 90, …} (category → points).
                    if (t["points"] is JObject cats)
                    {
                        foreach (var p in cats.Properties()) s.Breakdown.Add(new PointLine { Label = CategoryLabel(p.Name), Points = Int(p.Value) ?? 0 });
                    }
                    else if (t["breakdown"] is JArray lines)
                    {
                        foreach (var l in lines.OfType<JObject>())
                        {
                            s.Breakdown.Add(new PointLine { Label = RoomSeat.Str(l, "label", "reason", "id") ?? "", Points = (int?)(l["points"] ?? l["pts"]) ?? 0 });
                        }
                    }
                    else if (t["breakdown"] is JObject map)
                    {
                        foreach (var p in map.Properties()) s.Breakdown.Add(new PointLine { Label = p.Name, Points = (int?)p.Value ?? 0 });
                    }
                    r.Standings.Add(s);
                }
            }
            // No ranks given: by points.
            if (r.Standings.Count > 0 && r.Standings.All(s => s.Rank == 0))
            {
                var ordered = r.Standings.OrderByDescending(s => s.Points).ToList();
                for (var i = 0; i < ordered.Count; i++) ordered[i].Rank = i + 1;
            }
            r.Standings = r.Standings.OrderBy(s => s.Rank).ThenByDescending(s => s.Points).ToList();
            if (j["awards"] is JArray awards)
            {
                foreach (var a in awards.OfType<JObject>())
                {
                    var id = RoomSeat.Str(a, "id", "key") ?? "";
                    r.Awards.Add(new RoomAward
                    {
                        Id = id,
                        Title = (RoomSeat.Str(a, "title", "name") ?? TitleFor(id)).ToUpperInvariant(),
                        TeamId = RoomSeat.Str(a, "team", "teamId") ?? "",
                        PlayerId = RoomSeat.Str(a, "player", "playerId"),
                        Detail = RoomSeat.Str(a, "detail", "text", "why") ?? "",
                        Points = Int(a["points"]) ?? Int(a["bonus"]) ?? 0,
                        Price = Int(a["price"]),
                        Worth = Int(a["worth"]),
                    });
                }
            }
            if (j["scoring"] is JArray scoring) r.Scoring = scoring.Select(x => (string?)x ?? "").Where(x => x.Length > 0).ToList();
            r.WinnerId = RoomSeat.Str(j, "winner") ?? r.Standings.FirstOrDefault()?.TeamId ?? "";
            r.TopHumanId = RoomSeat.Str(j, "topHuman");
            return r;
        }

        /// <summary>A number, or null for anything else (an object, null, text).</summary>
        private static int? Int(JToken? t) =>
            t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (int)System.Math.Round((double)t) : (int?)null;

        /// <summary>The v2 scoring categories, as the breakdown names them.</summary>
        public static string CategoryLabel(string key) => key switch
        {
            "squad" => "Squad rating × 4",
            "balance" => "Squad balance",
            "value" => "Value for money",
            "steals" => "Steals (15 each)",
            "starBuy" => "Star buy",
            "purse" => "Purse used",
            "roles" => "Role cover",
            "overseas" => "Overseas stars",
            _ => key,
        };

        /// <summary>A readable title for an award id the server sent without one.</summary>
        public static string TitleFor(string id) => id switch
        {
            "bestSquad" => "BEST SQUAD",
            "steal" => "STEAL OF THE AUCTION",
            "bigSpender" => "BIG SPENDER",
            "mostBalanced" => "MOST BALANCED",
            "valueKing" => "VALUE KING",
            _ => id.ToUpperInvariant(),
        };
    }
}
