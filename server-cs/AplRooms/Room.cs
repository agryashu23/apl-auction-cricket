using Apl.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Apl.Rooms;

public sealed class Member
{
    public Member(string id, string name, long order)
    {
        Id = id;
        Name = name;
        Order = order;
    }

    public string Id { get; }
    public string Name { get; set; }
    public long Order { get; }
    public string? Team { get; set; }

    /// <summary>Lobby: pressed READY. The host's START is his own ready.</summary>
    public bool Ready { get; set; }
    public Session? Conn { get; set; }
    public DateTime? DroppedAt { get; set; }
    public bool Connected => Conn != null;
}

/// <summary>
/// A friend room (protocol v2, docs/ROOMS_PROTOCOL.md): lobby (distinct franchises, host
/// settings) → retention → auction (RoomAuction: the engine's v2 room rules) → finished with
/// the rewards. Every method runs under Hub.Sync and returns an error code or null.
/// </summary>
public sealed class Room
{
    private static long _joins;

    private readonly Hub _hub;
    private readonly List<Member> _members = new();
    private readonly HashSet<string> _removed = new();
    private readonly Dictionary<string, List<string>> _retentionPicks = new();
    private DateTime? _retentionDeadline;
    private double _sinceClock;

    public Room(Hub hub, string code, string formatId, bool retention, string hostId, int timerSec)
    {
        _hub = hub;
        Code = code;
        FormatId = formatId;
        Retention = retention;
        HostId = hostId;
        TimerSec = timerSec;
        LastActivity = DateTime.UtcNow;
    }

    public string Code { get; }
    public string FormatId { get; private set; }
    public bool Retention { get; private set; }
    public int TimerSec { get; private set; }
    public bool Locked { get; private set; }
    public string HostId { get; private set; }

    /// <summary>lobby, retention, auction, finished.</summary>
    public string Phase { get; private set; } = "lobby";
    public RoomAuction? Auction { get; private set; }
    public JObject? Results { get; private set; }
    public DateTime LastActivity { get; private set; }
    public DateTime? FinishedAt { get; private set; }

    private Dataset Data => _hub.Data;
    private ServerOptions Options => _hub.Options;
    private AuctionFormat Format => Data.Rules.Format(FormatId);
    public int ConnectedCount => _members.Count(m => m.Connected);
    private IEnumerable<Member> Seated => _members.Where(m => m.Team != null);
    private Member? MemberOf(string playerId) => _members.FirstOrDefault(m => m.Id == playerId);
    private Member? OwnerOf(string team) => _members.FirstOrDefault(m => m.Team == team);

    public static AuctionFormat? FindFormat(Dataset data, string? id)
    {
        if (id == "mini") id = "standard";
        return data.Rules.FormatList.FirstOrDefault(f => f.Id == id);
    }

    // ------------------------------------------------------------------ lobby

    /// <summary>Adds a player, or reconnects him to his seat (and hands an away team back).</summary>
    public string? Join(Session conn, string playerId, string name)
    {
        var m = MemberOf(playerId);
        if (m == null)
        {
            if (_removed.Contains(playerId)) return "kicked";
            if (Phase != "lobby") return "started";
            if (Locked) return "locked";
            if (_members.Count >= Options.MaxPlayers) return "full";
            m = new Member(playerId, name, Interlocked.Increment(ref _joins));
            _members.Add(m);
        }
        m.Conn = conn;
        m.Name = name;
        m.DroppedAt = null;
        conn.Room = this;
        LastActivity = DateTime.UtcNow;
        if (Phase == "auction" && m.Team != null && Auction!.ControlOf(m.Team) == SeatControl.Away && Auction.TakeBack(m.Team) == null)
        {
            Broadcast(Notice("takeBack", ("player", m.Id), ("team", m.Team)));
            BroadcastClock();
        }
        BroadcastRoom();
        if (Phase == "retention") conn.Send(RetentionMessage());
        if (Phase == "auction" || Phase == "finished") SendAuction(conn, null);
        if (Phase == "finished" && Results != null) conn.Send(new JObject { ["t"] = "results", ["results"] = Results });
        return null;
    }

    public string? Pick(string playerId, string team)
    {
        if (Phase != "lobby") return "started";
        var m = MemberOf(playerId);
        if (m == null) return "notInRoom";
        if (!Data.Franchises.Any(f => f.Id == team)) return "unknownTeam";
        var owner = OwnerOf(team);
        if (owner != null && owner != m) return "teamTaken";
        m.Team = team;
        Touch();
        BroadcastRoom();
        return null;
    }

    public string? SetReady(string playerId, bool ready)
    {
        if (Phase != "lobby") return "started";
        var m = MemberOf(playerId);
        if (m == null) return "notInRoom";
        m.Ready = ready;
        Touch();
        BroadcastRoom();
        return null;
    }

    public string? Settings(string playerId, JObject m)
    {
        if (playerId != HostId) return "notHost";
        var timer = (int?)m["timer"];
        if (timer != null && (timer < RoomRules.MinTimerSec || timer > RoomRules.MaxTimerSec)) return "badTimer";
        var formatId = (string?)m["format"];
        var retention = (bool?)m["retention"];
        if ((formatId != null || retention != null) && Phase != "lobby") return "started";
        AuctionFormat? format = null;
        if (formatId != null)
        {
            format = FindFormat(Data, formatId);
            if (format == null) return "unknownFormat";
        }
        if (format != null) FormatId = format.Id;
        if (retention != null) Retention = retention.Value;
        if (Format.RetentionOnly) Retention = true;
        if (timer != null)
        {
            TimerSec = timer.Value;
            if (Auction != null)
            {
                Auction.BidTimerSec = TimerSec;
                Broadcast(Notice("timer", ("by", playerId), ("timerSec", TimerSec)));
                BroadcastClock();
            }
        }
        Touch();
        BroadcastRoom();
        return null;
    }

    public string? Lock(string playerId, bool locked)
    {
        if (playerId != HostId) return "notHost";
        Locked = locked;
        Broadcast(Notice("locked", ("by", playerId), ("locked", locked)));
        BroadcastRoom();
        return null;
    }

    public string? TransferHost(string playerId, string target)
    {
        if (playerId != HostId) return "notHost";
        var m = MemberOf(target);
        if (m == null || !m.Connected) return "notInRoom";
        if (target == HostId) return null;
        SetHost(target, "transfer");
        return null;
    }

    public string? Kick(string playerId, string target)
    {
        if (playerId != HostId) return "notHost";
        if (target == HostId) return "badRequest";
        var m = MemberOf(target);
        if (m == null) return "notInRoom";
        _members.Remove(m);
        _removed.Add(target);
        if (m.Team != null)
        {
            _retentionPicks.Remove(m.Team);
            if (Phase == "auction") Auction!.Release(m.Team);
        }
        if (m.Conn != null)
        {
            m.Conn.Send(new JObject { ["t"] = "kicked", ["code"] = Code });
            m.Conn.Room = null;
        }
        Touch();
        Broadcast(Notice("kicked", ("player", target), ("team", m.Team)));
        BroadcastRoom();
        if (Phase == "auction") BroadcastClock();
        if (Phase == "retention") CheckRetentionDone();
        return null;
    }

    public string? Start(string playerId)
    {
        if (Phase != "lobby") return "started";
        if (playerId != HostId) return "notHost";
        if (Seated.Count() < Options.MinPlayers) return "notEnoughPlayers";
        if (_members.Any(m => m.Team == null)) return "unseatedPlayers";
        if (_members.Any(m => m.Id != HostId && !m.Ready)) return "notReady";
        Touch();
        if (Retention)
        {
            Phase = "retention";
            _retentionDeadline = DateTime.UtcNow.AddSeconds(Options.RetentionSec);
            BroadcastRoom();
            Broadcast(RetentionMessage());
        }
        else
        {
            StartAuction();
        }
        return null;
    }

    public string? Retain(string playerId, List<string> ids)
    {
        if (Phase != "retention") return "notRetention";
        var team = MemberOf(playerId)?.Team;
        if (team == null) return "notInRoom";
        var byId = Data.Players.ToDictionary(p => p.Id);
        if (ids.Any(id => !byId.ContainsKey(id))) return "unknownPlayer";
        var problem = Apl.Core.Retention.Problem(team, ids.Select(id => byId[id]).ToList(), Data.Rules, Format);
        if (problem != null)
        {
            MemberOf(playerId)!.Conn?.Error("invalidRetention", "retain", problem);
            return null;
        }
        _retentionPicks[team] = ids.ToList();
        Touch();
        Broadcast(RetentionMessage());
        CheckRetentionDone();
        return null;
    }

    private void CheckRetentionDone()
    {
        if (Phase == "retention" && Seated.All(m => _retentionPicks.ContainsKey(m.Team!))) StartAuction();
    }

    // ---------------------------------------------------------------- auction

    private string? Seat(string playerId, out string team)
    {
        team = "";
        if (Phase != "auction") return "notAuction";
        var t = MemberOf(playerId)?.Team;
        if (t == null) return "notInRoom";
        team = t;
        return null;
    }

    public string? Bid(string playerId, int amount) => Seat(playerId, out var team) ?? Auction!.Bid(team, amount);

    public string? Pass(string playerId) => Seat(playerId, out var team) ?? Auction!.Pass(team);

    public string? Rtm(string playerId, bool use) => Seat(playerId, out var team) ?? Auction!.Rtm(team, use);

    public string? Simulate(string playerId)
    {
        var error = Seat(playerId, out var team) ?? Auction!.Simulate(team);
        if (error != null) return error;
        AfterControlChange("simulate", playerId, team);
        return null;
    }

    public string? TakeBack(string playerId)
    {
        var error = Seat(playerId, out var team) ?? Auction!.TakeBack(team);
        if (error != null) return error;
        AfterControlChange("takeBack", playerId, team);
        return null;
    }

    private void AfterControlChange(string kind, string playerId, string team)
    {
        Touch();
        if (Phase != "auction") return; // finished at once: the room is already sent
        Broadcast(Notice(kind, ("player", playerId), ("team", team)));
        BroadcastRoom();
        BroadcastClock();
    }

    private string? HostAuction(string playerId)
    {
        if (playerId != HostId) return "notHost";
        if (Phase != "auction") return "notAuction";
        Touch();
        return null;
    }

    public string? Pause(string playerId)
    {
        var error = HostAuction(playerId) ?? Auction!.Pause();
        if (error != null) return error;
        Broadcast(Notice("paused", ("by", playerId)));
        BroadcastRoom();
        BroadcastClock();
        return null;
    }

    public string? Resume(string playerId)
    {
        var error = HostAuction(playerId) ?? Auction!.Resume();
        if (error != null) return error;
        Broadcast(Notice("resumed", ("by", playerId)));
        BroadcastRoom();
        BroadcastClock();
        return null;
    }

    public string? ForceClose(string playerId, bool sold)
    {
        var error = HostAuction(playerId) ?? Auction!.ForceClose(sold);
        if (error != null) return error;
        Broadcast(Notice("forceClose", ("by", playerId), ("sold", sold)));
        return null;
    }

    public string? Undo(string playerId)
    {
        var error = HostAuction(playerId) ?? Auction!.Undo();
        if (error != null) return error;
        Broadcast(Notice("undo", ("by", playerId), ("lot", Auction!.UndoneLot)));
        return null;
    }

    public string? End(string playerId)
    {
        var error = HostAuction(playerId);
        if (error != null) return error;
        Broadcast(Notice("ended", ("by", playerId)));
        return Auction!.End();
    }

    // ------------------------------------------------------- comings and goings

    /// <summary>The socket closed. The seat is kept; in an auction the autopilot covers after the grace period.</summary>
    public void Disconnected(Session conn)
    {
        var m = _members.FirstOrDefault(x => ReferenceEquals(x.Conn, conn));
        if (m == null) return;
        m.Conn = null;
        m.DroppedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
        if (m.Id == HostId) PassHost("disconnect");
        BroadcastRoom();
    }

    /// <summary>Leaving: a lobby seat is freed; in an auction the autopilot takes the team at once.</summary>
    public void Leave(string playerId)
    {
        var m = MemberOf(playerId);
        if (m == null) return;
        if (Phase == "lobby")
        {
            _members.Remove(m);
        }
        else
        {
            m.Conn = null;
            m.DroppedAt = DateTime.UtcNow;
            if (Phase == "auction" && m.Team != null && Auction!.GoAway(m.Team) == null && Phase == "auction")
            {
                Broadcast(Notice("away", ("player", m.Id), ("team", m.Team)));
                BroadcastClock();
            }
        }
        Touch();
        if (playerId == HostId) PassHost("leave");
        BroadcastRoom();
    }

    /// <summary>Lobby players gone longer than LobbyPruneSec lose their seat.</summary>
    public void PruneLobby()
    {
        if (Phase != "lobby") return;
        var now = DateTime.UtcNow;
        var gone = _members.Where(m => !m.Connected && m.DroppedAt != null && (now - m.DroppedAt.Value).TotalSeconds > Options.LobbyPruneSec).ToList();
        if (gone.Count == 0) return;
        foreach (var m in gone) _members.Remove(m);
        if (MemberOf(HostId) == null && _members.Count > 0) SetHost(_members.OrderBy(m => m.Order).First().Id, "leave");
        BroadcastRoom();
    }

    /// <summary>The host is gone: the next connected human (join order) takes over.</summary>
    private void PassHost(string reason)
    {
        var next = _members.Where(m => m.Connected && m.Id != HostId).OrderBy(m => m.Order).FirstOrDefault();
        if (next != null) SetHost(next.Id, reason);
    }

    private void SetHost(string playerId, string reason)
    {
        HostId = playerId;
        Broadcast(Notice("hostChanged", ("player", playerId), ("reason", reason)));
        BroadcastRoom();
    }

    // ------------------------------------------------------------------- time

    public void Tick(double realMs)
    {
        if (Phase == "retention")
        {
            if (DateTime.UtcNow >= _retentionDeadline) StartAuction();
            return;
        }
        if (Phase != "auction") return;
        var now = DateTime.UtcNow;
        foreach (var m in _members.ToList())
        {
            if (m.Team == null || m.Connected || m.DroppedAt == null) continue;
            if (Auction!.ControlOf(m.Team) != SeatControl.Human) continue;
            if ((now - m.DroppedAt.Value).TotalSeconds < Options.TakeoverSec) continue;
            if (Auction.GoAway(m.Team) == null && Phase == "auction")
            {
                Broadcast(Notice("away", ("player", m.Id), ("team", m.Team)));
                BroadcastRoom();
                BroadcastClock();
            }
        }
        if (Phase != "auction") return;
        Auction!.Advance((int)Math.Round(realMs * Options.TimeScale));
        _sinceClock += realMs;
        if (_sinceClock >= Options.ClockEveryMs && Phase == "auction")
        {
            _sinceClock = 0;
            BroadcastClock();
        }
    }

    // -------------------------------------------------------------- internals

    private void StartAuction()
    {
        var format = Format;
        var humans = Seated.Select(m => m.Team!).ToList();
        var setup = new RoomSetup
        {
            Seed = Random.Shared.Next(int.MaxValue),
            SeasonSeed = Random.Shared.Next(int.MaxValue),
            FormatId = format.Id,
            Retention = Retention,
            DataVersion = Data.Version,
            Rules = RoomSetup.NewRules,
        };
        if (Retention)
        {
            foreach (var f in Data.Franchises)
            {
                // Bots, and a human who ran out of time, keep what the Franchise AI would.
                setup.Retentions[f.Id] = humans.Contains(f.Id) && _retentionPicks.TryGetValue(f.Id, out var picks)
                    ? picks
                    : Ai.ChooseRetentions(f.Id, Data.Players, Data.Rules, format).Select(p => p.Id).ToList();
            }
        }
        var pacing = Options.Pacing();
        pacing.BidTimerSec = TimerSec;
        var auction = new RoomAuction(Data, setup, humans, pacing);
        auction.Acted += a =>
        {
            Broadcast(new JObject { ["t"] = "act", ["a"] = a.ToJson() });
            BroadcastClock();
        };
        auction.Reloaded += reason =>
        {
            foreach (var m in _members.Where(m => m.Connected)) SendAuction(m.Conn!, reason);
            if (reason == "instant" && !auction.IsFinished) return;
            if (reason == "instant") Broadcast(Notice("instantFinish"));
        };
        auction.Finished += Finish;
        Auction = auction;
        Phase = "auction";
        _retentionDeadline = null;
        RoomServer.Log("auction_started", new { code = Code, format = format.Id, humans });
        BroadcastRoom();
        foreach (var m in _members.Where(m => m.Connected)) SendAuction(m.Conn!, null);
        auction.Start();
    }

    private void Finish()
    {
        Phase = "finished";
        FinishedAt = DateTime.UtcNow;
        Results = Auction!.Awards!.ToJson(team =>
        {
            var owner = OwnerOf(team);
            return owner == null ? null : (owner.Id, owner.Name, Auction.ControlOf(team).Name());
        });
        RoomServer.Log("auction_finished", new { code = Code, winner = (string?)Results["winner"] });
        BroadcastClock();
        BroadcastRoom();
        Broadcast(new JObject { ["t"] = "results", ["results"] = Results });
    }

    private void SendAuction(Session conn, string? reason)
    {
        var a = Auction!;
        var start = new JObject { ["t"] = "start", ["setup"] = a.Setup.ToJson(), ["log"] = a.LogJson() };
        if (reason != null) start["reason"] = reason;
        conn.Send(start);
        conn.Send(a.ClockJson());
    }

    private JObject RetentionMessage() => new()
    {
        ["t"] = "retention",
        ["leftMs"] = _retentionDeadline == null ? 0 : Math.Max(0, (int)(_retentionDeadline.Value - DateTime.UtcNow).TotalMilliseconds),
        ["submitted"] = new JArray(_retentionPicks.Keys.OrderBy(k => k, StringComparer.Ordinal)),
    };

    private string ControlName(Member m) =>
        m.Team != null && Auction != null ? Auction.ControlOf(m.Team).Name() : "human";

    public JObject Json()
    {
        var members = new JArray();
        foreach (var m in _members.OrderBy(m => m.Order))
        {
            members.Add(new JObject
            {
                ["id"] = m.Id,
                ["name"] = m.Name,
                ["team"] = m.Team,
                ["connected"] = m.Connected,
                ["host"] = m.Id == HostId,
                ["ready"] = m.Ready || m.Id == HostId,
                ["control"] = ControlName(m),
            });
        }
        var teams = new JArray();
        foreach (var f in Data.Franchises)
        {
            var owner = OwnerOf(f.Id);
            teams.Add(new JObject
            {
                ["team"] = f.Id,
                ["owner"] = owner?.Id,
                ["name"] = owner?.Name,
                ["control"] = owner != null ? ControlName(owner) : "ai",
            });
        }
        return new JObject
        {
            ["code"] = Code,
            ["host"] = HostId,
            ["phase"] = Phase,
            ["format"] = FormatId,
            ["retention"] = Retention,
            ["timerSec"] = TimerSec,
            ["locked"] = Locked,
            ["paused"] = Auction?.Paused ?? false,
            ["dataVersion"] = Data.Version,
            ["protocol"] = RoomProtocol.Version,
            ["rules"] = RoomSetup.NewRules,
            ["minPlayers"] = Options.MinPlayers,
            ["maxPlayers"] = Options.MaxPlayers,
            ["members"] = members,
            ["teams"] = teams,
            ["results"] = Results,
        };
    }

    private static JObject Notice(string kind, params (string Key, object? Value)[] fields)
    {
        var j = new JObject { ["t"] = "notice", ["kind"] = kind };
        foreach (var (k, v) in fields) j[k] = v == null ? JValue.CreateNull() : JToken.FromObject(v);
        return j;
    }

    private void Touch() => LastActivity = DateTime.UtcNow;

    private void BroadcastRoom() => Broadcast(new JObject { ["t"] = "room", ["room"] = Json() });

    private void BroadcastClock()
    {
        if (Auction != null) Broadcast(Auction.ClockJson());
    }

    private void Broadcast(JObject message)
    {
        var json = message.ToString(Formatting.None); // once, not once per player
        foreach (var m in _members)
        {
            m.Conn?.SendJson(json);
        }
    }
}
