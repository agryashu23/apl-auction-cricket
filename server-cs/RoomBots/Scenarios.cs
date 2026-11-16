using Apl.Core;
using Newtonsoft.Json.Linq;

namespace Apl.RoomBots;

public sealed class ScenarioFailed : Exception
{
    public ScenarioFailed(string message) : base(message) { }
}

/// <summary>The room scenarios (docs/ROOMS_PROTOCOL.md), each in its own room.</summary>
public sealed class Scenarios
{
    // The in-process server's test timings: game time x10, so the 11 s reveal is 1.1 s,
    // the 10 s bid timer 1 s, the result 0.55 s.
    public const double TimeScale = 10;
    public const double TakeoverSec = 1.5;
    public const double RetentionSec = 4;
    private const double RevealReal = 11.0 / TimeScale;
    private const double BidTimerReal = 10.0 / TimeScale;

    // A marquee lot can run a long bot war: up to ~200 s game time (~20 s here) in the engine test
    // A_lot_with_one_pass_always_closes_with_real_pacing; wait well past that, never a fixed 20 s.
    private const int LotTimeoutMs = 90000;

    private readonly Dataset _data;
    private readonly Uri _url;
    private int _n;

    public Scenarios(Dataset data, Uri url)
    {
        _data = data;
        _url = url;
    }

    public IEnumerable<(string Name, Func<Task<string>> Body)> All()
    {
        yield return ("three humans on different teams, team clash rejected", ThreeHumans);
        yield return ("FAST / SKIP rejected", FastSkipRejected);
        yield return ("pass consensus", PassConsensus);
        yield return ("simulate hands the team to a bot, then take back", SimulateAndTakeBack);
        yield return ("disconnect -> bot -> rejoin (and host passes on)", DisconnectRejoin);
        yield return ("host lobby controls: lock, settings, timer", HostLobby);
        yield return ("host pause, force close, undo, kick, transfer, end", HostAuction);
        yield return ("retention round (mini auction)", RetentionRound);
        yield return ("all simulate -> instant finish + rewards consistent", AllSimulate);
        yield return ("idle room cleanup", IdleCleanup);
    }

    // ------------------------------------------------------------- helpers

    private static void Check(bool ok, string what)
    {
        if (!ok) throw new ScenarioFailed(what);
    }

    private async Task<Bot> NewBot(string name)
    {
        var b = new Bot(_data, _url, $"bot{Interlocked.Increment(ref _n)}-{Guid.NewGuid():N}".Substring(0, 24), name);
        await b.ConnectAsync();
        return b;
    }

    /// <summary>A room: the first bot hosts with teams[0], the others join and pick theirs.</summary>
    private async Task<List<Bot>> Room(string[] teams, string format = "mega", bool retention = false, int? timer = null)
    {
        var bots = new List<Bot>();
        var host = await NewBot("Host");
        bots.Add(host);
        var create = new JObject { ["t"] = "create", ["format"] = format, ["retention"] = retention, ["team"] = teams[0] };
        if (timer != null) create["timer"] = timer.Value;
        host.Send(create);
        Check(await host.WaitFor(b => b.Code != null && b.MyTeam == teams[0], 3000), "host got no room");
        var code = host.Code!;
        for (var i = 1; i < teams.Length; i++)
        {
            var b = await NewBot("P" + (i + 1));
            b.Send("join", ("code", code.ToLowerInvariant()));
            Check(await b.WaitFor(x => x.Code == code, 3000), $"P{i + 1} could not join");
            b.Send("pick", ("team", teams[i]));
            Check(await b.WaitFor(x => x.MyTeam == teams[i], 3000), $"P{i + 1} could not pick {teams[i]}");
            b.Send("ready", ("ready", true));
            Check(await b.WaitFor(x => (bool?)x.MemberJson(x.Id)?["ready"] == true, 3000), $"P{i + 1} not ready");
            bots.Add(b);
        }
        Check(await host.WaitFor(b => ((JArray)b.Room!["members"]!).Count == teams.Length, 3000), "host doesn't see everyone");
        return bots;
    }

    private static async Task StartAuction(List<Bot> bots)
    {
        bots[0].Send("start");
        foreach (var b in bots) Check(await b.WaitFor(x => x.Phase == "auction" && x.Replica != null, 5000), $"{b.Name} never saw the auction start");
    }

    private static void NoDesync(IEnumerable<Bot> bots)
    {
        foreach (var b in bots) Check(b.Desync == null, $"{b.Name} replica desync: {b.Desync}");
    }

    private static async Task Finish(List<Bot> bots)
    {
        bots[0].Send("end");
        await bots[0].WaitFor(b => b.Phase == "finished", 10000);
        foreach (var b in bots) b.Drop();
    }

    /// <summary>The next lot to open after the act mark: (arrival, player id).</summary>
    private static async Task<(double At, string Player)> NextOpen(Bot b, int from, int timeoutMs = 15000)
    {
        var open = await b.WaitAct(a => (string?)a["a"] == "open" && a["p"]!.Type != JTokenType.Null, timeoutMs, from);
        Check(open != null, $"{b.Name}: no lot opened");
        return (open!.Value.At, (string)open.Value.A["p"]!);
    }

    /// <summary>Waits until the bot's replica shows an open lot in bidding.</summary>
    private static async Task WaitBidding(Bot b, int timeoutMs = 15000) =>
        Check(await b.WaitFor(x => x.ClockPhase == "bidding" && x.Replica?.Engine.CurrentLot != null && !x.Replica.Engine.CurrentLot.BiddingClosed, timeoutMs), $"{b.Name}: never bidding");

    /// <summary>Bids the board's next amount until a bid by this bot's team lands (other bids can make it stale).</summary>
    private static async Task<int> BidOnce(Bot b, string team)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await WaitBidding(b);
            var amount = b.Read(x => x.Replica!.Engine.CurrentLot != null && x.Replica.Engine.CheckBid(team) == null ? x.Replica.Engine.NextBidAmount : -1);
            if (amount < 0)
            {
                await Task.Delay(150);
                continue;
            }
            var mark = b.ActMark;
            var errMark = b.Mark;
            b.Send("bid", ("amount", amount));
            var landed = await b.WaitAct(a => (string?)a["a"] == "bid" && (string?)a["team"] == team && (int)a["amount"]! == amount, 800, mark);
            if (landed != null) return amount;
            await b.ErrorFor("bid", errMark, 300);
        }
        throw new ScenarioFailed($"{b.Name} could not land a bid");
    }

    // ----------------------------------------------------------- scenarios

    private async Task<string> ThreeHumans()
    {
        var host = await NewBot("Host");
        host.Send("create", ("format", "mega"), ("retention", false), ("team", "MBH"));
        Check(await host.WaitFor(b => b.MyTeam == "MBH", 3000), "host has no team");
        var code = host.Code!;
        Check(code.Length == 6 && code.All(c => "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".Contains(c)), $"bad room code {code}");

        var b2 = await NewBot("P2");
        b2.Send("join", ("code", code));
        Check(await b2.WaitFor(x => x.Code == code, 3000), "P2 couldn't join");
        var mark = b2.Mark;
        b2.Send("pick", ("team", "MBH"));
        Check(await b2.ErrorFor("pick", mark) == "teamTaken", "P2 took the host's team");
        b2.Send("pick", ("team", "CHC"));
        Check(await b2.WaitFor(x => x.MyTeam == "CHC", 3000), "P2 couldn't pick CHC");

        var b3 = await NewBot("P3");
        b3.Send("join", ("code", code));
        Check(await b3.WaitFor(x => x.Code == code, 3000), "P3 couldn't join");
        mark = b3.Mark;
        b3.Send("pick", ("team", "CHC"));
        Check(await b3.ErrorFor("pick", mark) == "teamTaken", "P3 took P2's team");
        b3.Send("pick", ("team", "BLR"));
        Check(await b3.WaitFor(x => x.MyTeam == "BLR", 3000), "P3 couldn't pick BLR");

        mark = b2.Mark;
        b2.Send("start");
        Check(await b2.ErrorFor("start", mark) == "notHost", "a non-host started the auction");

        // Everyone (but the host, whose START is his ready) must be ready.
        b2.Send("ready", ("ready", true));
        Check(await host.WaitFor(x => (bool?)x.MemberJson(b2.Id)?["ready"] == true, 3000), "P2's ready not shown");
        mark = host.Mark;
        host.Send("start");
        Check(await host.ErrorFor("start", mark) == "notReady", "started with P3 not ready");
        b3.Send("ready", ("ready", true));
        Check(await host.WaitFor(x => (bool?)x.MemberJson(b3.Id)?["ready"] == true, 3000), "P3's ready not shown");

        var bots = new List<Bot> { host, b2, b3 };
        await StartAuction(bots);
        Check(host.Read(b => b.ControlOf("MBH") == "human" && b.ControlOf("CHC") == "human" && b.ControlOf("BLR") == "human"), "seats aren't human");
        Check(host.Read(b => ((JArray)b.Room!["teams"]!).Count(t => (string?)t["control"] == "ai") == _data.Franchises.Count - 3), "the empty franchises aren't AI");
        Check(await host.WaitFor(b => string.Join(",", b.Humans) == "BLR,CHC,MBH", 3000), "clock.humans isn't the three seats");
        Check(host.Read(b => b.Inbox.Where(m => (string?)m.M["t"] == "clock").All(m => m.M["passed"] != null && m.M["lot"] != null && m.M["paused"] != null && m.M["timerSec"] != null)), "a clock lacks passed/lot/paused/timerSec");
        Check(host.Read(b => (int?)b.Replica!.Setup!.Rules == RoomSetup.NewRules), "setup isn't on the new rules");
        Check(host.Read(b => b.Replica!.Engine.LotOrder == LotOrder.IplSets && b.Replica.Engine.MaxPrice == _data.Rules.MaxPrice), "engine isn't IPL sets + 28 Cr");

        var late = await NewBot("Late");
        mark = late.Mark;
        late.Send("join", ("code", code));
        Check(await late.ErrorFor("join", mark) == "started", "a newcomer joined a running auction");

        // Server-side bid validation: a wrong amount is refused, the right one lands for everyone.
        await WaitBidding(host);
        mark = host.Mark;
        var wrong = host.Read(b => b.Replica!.Engine.NextBidAmount) + 7;
        host.Send("bid", ("amount", wrong));
        var code1 = await host.ErrorFor("bid", mark);
        Check(code1 == "staleAmount" || code1 == "biddingClosed", $"a wrong bid amount got {code1 ?? "no error"}");
        var actMark3 = b3.ActMark;
        var amount = await BidOnce(host, "MBH");
        Check(await b3.WaitAct(a => (string?)a["a"] == "bid" && (string?)a["team"] == "MBH" && (int)a["amount"]! == amount, 3000, 0) != null, "P3 didn't see the host's bid");
        _ = actMark3;
        await Task.Delay(300);
        NoDesync(bots);
        late.Drop();
        await Finish(bots);
        return $"room {code}, bid {Money.Format(amount)} landed";
    }

    private async Task<string> FastSkipRejected()
    {
        var bots = await Room(new[] { "KOC", "DLD" });
        await StartAuction(bots);
        var b = bots[1];
        foreach (var t in new[] { "fast", "skip", "ff" })
        {
            var mark = b.Mark;
            b.Send(t);
            var code = await b.ErrorFor(t, mark);
            Check(code == "notAllowedInRooms", $"{t} got {code ?? "no error"}");
        }
        Check(bots[0].Read(x => !x.Replica!.CanHoldFastForward), "replica allows hold-fast-forward");
        await Finish(bots);
        return "fast, skip, ff -> notAllowedInRooms";
    }

    private async Task<string> PassConsensus()
    {
        var bots = await Room(new[] { "MBH", "CHC", "BLR" });
        var (a, b, c) = (bots[0], bots[1], bots[2]);
        await StartAuction(bots);

        // 1. One pass: the lot runs to the bid timer for the others.
        var (opened, _) = await NextOpen(a, 0);
        var from = a.Read(x => x.Acts.Count - 1);
        a.Send("pass");
        Check(await b.WaitAct(x => (string?)x["a"] == "pass" && (string?)x["team"] == "MBH", 3000, 0) != null, "the others didn't see the pass");
        var mark = a.Mark;
        a.Send("pass");
        Check(await a.ErrorFor("pass", mark) == "alreadyPassed", "a second pass wasn't refused");
        var end1 = await a.WaitAct(x => (string?)x["a"] == "end" || (string?)x["a"] == "close", LotTimeoutMs, from);
        Check(end1 != null, "lot 1 never closed");
        var lastBid1 = a.Read(x => x.Acts.Skip(from).Where(t => (string?)t.A["a"] == "bid" && t.At <= end1!.Value.At).Select(t => t.At).DefaultIfEmpty(0).Max());
        var quiet1 = end1!.Value.At - Math.Max(lastBid1, opened + RevealReal);
        Check(quiet1 >= 0.7 * BidTimerReal, $"one pass ended the lot early ({quiet1:0.00}s after the last bid; timer {BidTimerReal}s)");

        // 2. Everyone passes (during the reveal): the lot ends as soon as no bot wants him.
        var (opened2, _) = await NextOpen(a, a.ActMark);
        var from2 = a.Read(x => x.Acts.Count - 1);
        foreach (var x in bots) x.Send("pass");
        var end2 = await a.WaitAct(x => (string?)x["a"] == "end" || (string?)x["a"] == "close", LotTimeoutMs, from2);
        Check(end2 != null, "lot 2 never closed");
        var passes = a.Read(x => x.Acts.Skip(from2).Count(t => (string?)t.A["a"] == "pass"));
        Check(passes == 3, $"expected 3 passes, saw {passes}");
        var lastBid2 = a.Read(x => x.Acts.Skip(from2).Where(t => (string?)t.A["a"] == "bid" && t.At <= end2!.Value.At).Select(t => t.At).DefaultIfEmpty(0).Max());
        var quiet2 = end2!.Value.At - Math.Max(lastBid2, opened2 + RevealReal);
        Check(quiet2 < 0.4 * BidTimerReal, $"all passed but the lot waited {quiet2:0.00}s after the last bid");
        var bids2 = a.Read(x => x.Acts.Skip(from2).Count(t => (string?)t.A["a"] == "bid" && t.At <= end2!.Value.At));
        NoDesync(bots);
        await Finish(bots);
        return $"one pass: closed {quiet1:0.00}s after the last bid (timer {BidTimerReal}s); all passed: {quiet2:0.00}s after the last of {bids2} bot bids";
    }

    private async Task<string> SimulateAndTakeBack()
    {
        var bots = await Room(new[] { "MBH", "CHC" });
        var (a, b) = (bots[0], bots[1]);
        await StartAuction(bots);
        await NextOpen(a, 0);
        var mark = b.Mark;
        a.Send("simulate");
        Check(await b.WaitMsg(m => (string?)m["t"] == "notice" && (string?)m["kind"] == "simulate" && (string?)m["team"] == "MBH", 3000, mark) != null, "no simulate notice");
        Check(await b.WaitFor(x => x.ControlOf("MBH") == "autopilot" && (string?)x.MemberJson(a.Id)!["control"] == "autopilot", 3000), "MBH isn't on autopilot");
        Check(await a.WaitFor(x => !x.Humans.Contains("MBH") && x.Humans.Contains("CHC"), 3000), "clock.humans still has MBH");
        Check(a.Phase == "auction", "the auction stopped for the other player");
        await WaitBidding(a);
        mark = a.Mark;
        a.Send("bid", ("amount", a.Read(x => x.Replica!.Engine.NextBidAmount)));
        var err = await a.ErrorFor("bid", mark);
        Check(err == "notInControl" || err == "biddingClosed", $"a bid on autopilot got {err ?? "no error"}");
        // The auction carries on: more lots come and go.
        // (A marquee lot can run a long bot war, so allow time; CHC passes to keep it moving.)
        var before = a.Read(x => x.Replica!.Engine.Results.Count);
        var acts = a.ActMark;
        Check(await a.WaitFor(x => x.ActMark > acts, 5000), "nothing happened while on autopilot");
        b.Send("pass");
        Check(await a.WaitFor(x => x.Replica!.Engine.Results.Count > before, 60000), "no lot closed while on autopilot");
        a.Send("takeBack");
        Check(await b.WaitFor(x => x.ControlOf("MBH") == "human", 3000), "take back didn't return control");
        Check(await a.WaitFor(x => x.Humans.Contains("MBH"), 3000), "clock.humans lacks MBH after take back");
        mark = a.Mark;
        a.Send("takeBack");
        Check(await a.ErrorFor("takeBack", mark) == "notOnAutopilot", "a second take back wasn't refused");
        NoDesync(bots);
        await Finish(bots);
        return "autopilot <-> human, auction carried on";
    }

    private async Task<string> DisconnectRejoin()
    {
        var bots = await Room(new[] { "MBH", "CHC", "BLR" });
        var (a, b, c) = (bots[0], bots[1], bots[2]);
        await StartAuction(bots);
        await NextOpen(a, 0);
        var dropAt = Bot.Now;
        c.Drop();
        Check(await a.WaitFor(x => (bool?)x.MemberJson(c.Id)?["connected"] == false, 3000), "the drop wasn't seen");
        Check(a.Read(x => x.ControlOf("BLR")) == "human", "botted before the grace period");
        Check(await a.WaitFor(x => x.ControlOf("BLR") == "away", 8000), "no bot took the dropped team");
        var botted = Bot.Now - dropAt;
        Check(botted >= TakeoverSec * 0.9, $"botted after only {botted:0.0}s");
        Check(await a.WaitFor(x => !x.Humans.Contains("BLR"), 3000), "clock.humans still has BLR");

        // Rejoin with the same player id: same seat, control back, full log replayed.
        var c2 = new Bot(_data, _url, c.Id, c.Name);
        await c2.ConnectAsync();
        var mark = a.Mark;
        c2.Send("join", ("code", a.Code!));
        Check(await c2.WaitFor(x => x.Replica != null && x.MyTeam == "BLR", 5000), "rejoin didn't restore the seat");
        Check(await a.WaitFor(x => x.ControlOf("BLR") == "human", 3000), "control didn't come back on rejoin");
        Check(await a.WaitMsg(m => (string?)m["t"] == "notice" && (string?)m["kind"] == "takeBack", 3000, mark) != null, "no takeBack notice");
        await Task.Delay(400);
        Check(c2.Read(x => x.Replica!.Engine.Results.Count) == a.Read(x => x.Replica!.Engine.Results.Count) ||
              Math.Abs(c2.Read(x => x.Replica!.Engine.Results.Count) - a.Read(x => x.Replica!.Engine.Results.Count)) <= 1, "the rejoined replica is behind");

        // The host drops: host passes to the next human; he doesn't get it back on rejoin.
        a.Drop();
        Check(await b.WaitFor(x => x.Host == b.Id, 3000), "host didn't pass on");
        var a2 = new Bot(_data, _url, a.Id, a.Name);
        await a2.ConnectAsync();
        a2.Send("join", ("code", b.Code!));
        Check(await a2.WaitFor(x => x.Replica != null && x.MyTeam == "MBH", 5000), "old host couldn't rejoin");
        Check(a2.Read(x => x.Host) == b.Id, "old host took host back");
        NoDesync(new[] { b, c2, a2 });
        var rest = new List<Bot> { b, c2, a2 };
        await Finish(rest);
        return $"bot took over after {botted:0.0}s; rejoin restored the seat; host passed to P2";
    }

    private async Task<string> HostLobby()
    {
        var host = await NewBot("Host");
        host.Send("create", ("format", "mega"), ("retention", false), ("team", "RJR"), ("timer", 10));
        Check(await host.WaitFor(b => b.MyTeam == "RJR", 3000), "no room");
        var code = host.Code!;
        host.Send("lock", ("locked", true));
        Check(await host.WaitFor(b => (bool?)b.Room!["locked"] == true, 3000), "not locked");
        var d = await NewBot("D");
        var mark = d.Mark;
        d.Send("join", ("code", code));
        Check(await d.ErrorFor("join", mark) == "locked", "joined a locked room");
        host.Send("lock", ("locked", false));
        Check(await host.WaitFor(b => (bool?)b.Room!["locked"] == false, 3000), "not unlocked");
        d.Send("join", ("code", code));
        Check(await d.WaitFor(x => x.Code == code, 3000), "couldn't join after unlock");

        mark = d.Mark;
        d.Send("settings", ("timer", 15));
        Check(await d.ErrorFor("settings", mark) == "notHost", "a guest changed settings");
        mark = host.Mark;
        host.Send("settings", ("timer", 3));
        Check(await host.ErrorFor("settings", mark) == "badTimer", "a 3 s timer was accepted");
        host.Send("settings", ("timer", 20), ("format", "mini"));
        Check(await host.WaitFor(b => (int?)b.Room!["timerSec"] == 20 && (string?)b.Room!["format"] == "standard" && (bool?)b.Room!["retention"] == true, 3000), "settings didn't apply (mini forces retention)");
        host.Send("settings", ("format", "mega"), ("retention", false));
        Check(await host.WaitFor(b => (string?)b.Room!["format"] == "mega" && (bool?)b.Room!["retention"] == false, 3000), "format back to mega failed");

        // Kick in the lobby: the seat frees, he can't come back.
        d.Send("pick", ("team", "PJP"));
        Check(await host.WaitFor(b => b.TeamJson("PJP")?["owner"]?.ToString() == d.Id, 3000), "D didn't pick");
        host.Send("kick", ("player", d.Id));
        Check(await d.WaitFor(x => x.Kicked, 3000), "D wasn't told he was kicked");
        Check(await host.WaitFor(b => b.MemberJson(d.Id) == null && (string?)b.TeamJson("PJP")!["owner"] == null, 3000), "D's seat wasn't freed");
        mark = d.Mark;
        d.Send("join", ("code", code));
        Check(await d.ErrorFor("join", mark) == "kicked", "a kicked player got back in");

        // Max humans = number of franchises.
        var bots = new List<Bot>();
        for (var i = 0; i < _data.Franchises.Count; i++)
        {
            var x = await NewBot("F" + i);
            mark = x.Mark;
            x.Send("join", ("code", code));
            if (i < _data.Franchises.Count - 1) Check(await x.WaitFor(y => y.Code == code, 3000), $"F{i} couldn't join");
            else Check(await x.ErrorFor("join", mark) == "full", "an 11th human got in");
            bots.Add(x);
        }
        foreach (var x in bots) x.Drop();
        host.Drop();
        d.Drop();
        return "lock/unlock, timer 5-30 enforced, mini forces retention, lobby kick, max 10 humans";
    }

    private async Task<string> HostAuction()
    {
        var bots = await Room(new[] { "MBH", "CHC", "BLR" }, timer: 15);
        var (a, b, c) = (bots[0], bots[1], bots[2]);
        await StartAuction(bots);
        await WaitBidding(a);
        Check(a.Read(x => (int)x.LastClock!["bidTotal"]!) == 15000 && a.Read(x => (int)x.LastClock!["timerSec"]!) == 15, "the 15 s host timer isn't in the clock");

        // Pause: only the host; time and bids stop.
        var mark = b.Mark;
        b.Send("pause");
        Check(await b.ErrorFor("pause", mark) == "notHost", "a guest paused");
        a.Send("pause");
        Check(await b.WaitFor(x => (bool?)x.LastClock?["paused"] == true && (bool?)x.Room?["paused"] == true, 3000), "not paused");
        await Task.Delay(100);
        var acts = b.ActMark;
        var left = b.Read(x => (int)x.LastClock!["bid"]! + (int)x.LastClock!["left"]!);
        await Task.Delay(800);
        Check(b.ActMark == acts, "the auction moved while paused");
        Check(b.Read(x => (int)x.LastClock!["bid"]! + (int)x.LastClock!["left"]!) == left, "the clock ran while paused");
        mark = b.Mark;
        b.Send("bid", ("amount", b.Read(x => x.Replica!.Engine.CurrentLot != null ? x.Replica.Engine.NextBidAmount : 0)));
        Check(await b.ErrorFor("bid", mark) == "paused", "a bid landed while paused");
        a.Send("resume");
        Check(await b.WaitFor(x => (bool?)x.LastClock?["paused"] == false, 3000), "not resumed");

        // Force close unsold (whatever the bids).
        string forcedNote = "";
        for (var attempt = 0; attempt < 4 && forcedNote == ""; attempt++)
        {
            await WaitBidding(a);
            var from = a.ActMark;
            mark = a.Mark;
            a.Send("forceClose", ("sold", false));
            var close = await a.WaitAct(x => (string?)x["a"] == "close", 3000, from);
            if (close != null && (bool?)close.Value.A["unsold"] == true) forcedNote = "unsold";
            else await a.ErrorFor("forceClose", mark, 300);
        }
        Check(forcedNote == "unsold", "force close unsold never produced an unsold close");
        Check(a.Read(x => !x.Replica!.Engine.Results.Last().Sold), "the forced lot was sold");

        // Force close sold: the host bids, then closes at his bid.
        var amount = await BidOnce(a, "MBH");
        var from2 = a.ActMark;
        a.Send("forceClose", ("sold", true));
        var closed = await a.WaitAct(x => (string?)x["a"] == "close" || (string?)x["a"] == "end", 3000, from2);
        Check(closed != null, "force close sold did nothing");
        if ((string?)closed!.Value.A["a"] == "end") a.Send("forceClose", ("sold", true)); // an RTM offer: close it declined
        Check(await a.WaitFor(x => x.Replica!.Engine.CurrentLot == null || x.Replica.Engine.Results.Count > 0 && x.Replica.Engine.Results.Last().Price != null, 3000), "no result");
        Check(await a.WaitFor(x => x.Replica!.Engine.Results.Any(r => r.Price != null && r.Sold), 3000), "nothing sold");
        var sold = a.Read(x => x.Replica!.Engine.Results.Last());
        Check(sold.Sold && sold.Price != null, "force close sold left it unsold");
        Check(sold.TeamId == "MBH" || sold.ViaRtm, $"sold to {sold.TeamId}, not the high bidder");

        // Undo the last sale: the same player comes back, the purse is restored.
        var results = a.Read(x => x.Replica!.Engine.Results.Count);
        var lastPlayer = a.Read(x => x.Replica!.Engine.Results.Last().Player.Id);
        var starts = b.Starts;
        mark = b.Mark;
        a.Send("undo");
        Check(await b.WaitFor(x => x.Starts > starts && x.LastStartReason == "undo", 3000), "no start{reason:undo}");
        Check(await b.WaitMsg(m => (string?)m["t"] == "notice" && (string?)m["kind"] == "undo", 3000, mark) != null, "no undo notice");
        Check(b.Read(x => x.Replica!.Engine.Results.Count) == results - 1, "undo didn't remove the last result");
        Check(await b.WaitAct(x => (string?)x["a"] == "open" && (string?)x["p"] == lastPlayer, 3000, 0) != null, "no act open for the re-auctioned lot after start{undo}");
        Check(b.Read(x => x.Replica!.Engine.CurrentLot?.Player.Id) == lastPlayer, "the undone player isn't back on the block");
        var purse = b.Read(x => x.Replica!.Engine.Teams[sold.TeamId!].Purse);
        Check(purse >= sold.Price!.Value, "purse not restored");

        // Kick in the auction: the team becomes an AI bot and he can't come back.
        a.Send("kick", ("player", c.Id));
        Check(await c.WaitFor(x => x.Kicked, 3000), "C wasn't kicked");
        Check(await a.WaitFor(x => x.ControlOf("BLR") == "ai" && x.MemberJson(c.Id) == null, 3000), "BLR isn't an AI bot after the kick");
        Check(await a.WaitFor(x => !x.Humans.Contains("BLR"), 3000), "clock.humans still has BLR");

        // Transfer host.
        a.Send("host", ("player", b.Id));
        Check(await a.WaitFor(x => x.Host == b.Id, 3000), "host not transferred");
        mark = a.Mark;
        a.Send("pause");
        Check(await a.ErrorFor("pause", mark) == "notHost", "the old host can still pause");
        b.Send("pause");
        Check(await a.WaitFor(x => (bool?)x.LastClock?["paused"] == true, 3000), "the new host can't pause");
        b.Send("resume");

        // End early: everything settles at once, results arrive.
        b.Send("end");
        Check(await a.WaitFor(x => x.Phase == "finished" && x.Results != null, 15000), "end didn't finish the auction");
        Check(a.Read(x => x.Replica!.Engine.IsFinished), "replica not finished after end");
        NoDesync(new[] { a, b });
        a.Drop();
        b.Drop();
        c.Drop();
        return $"pause froze {left}ms; forced unsold; sold at {Money.Format(amount)}; undo restored {lastPlayer}; kick->ai; transfer; end";
    }

    private async Task<string> RetentionRound()
    {
        var bots = await Room(new[] { "MBH", "CHC" }, format: "standard", retention: true);
        var (a, b) = (bots[0], bots[1]);
        a.Send("start");
        Check(await b.WaitFor(x => x.Phase == "retention", 3000), "no retention phase");
        Check(await b.WaitMsg(m => (string?)m["t"] == "retention" && (int)m["leftMs"]! > 0, 3000) != null, "no retention countdown");
        var format = _data.Rules.Format("standard");
        var wrong = _data.Players.First(p => p.PreviousTeam != "CHC" && p.PreviousTeam != null).Id;
        var mark = b.Mark;
        b.Send("retain", ("ids", new JArray(wrong)));
        Check(await b.ErrorFor("retain", mark) == "invalidRetention", "an invalid retention was accepted");
        var keeps = Ai.ChooseRetentions("MBH", _data.Players, _data.Rules, format).Select(p => p.Id).ToList();
        a.Send("retain", ("ids", new JArray(keeps)));
        Check(await a.WaitMsg(m => (string?)m["t"] == "retention" && ((JArray)m["submitted"]!).Any(x => (string?)x == "MBH"), 3000) != null, "MBH's keeps weren't recorded");
        // CHC never submits: the deadline starts the auction with the AI's picks for it.
        Check(await a.WaitFor(x => x.Phase == "auction" && x.Replica != null, (int)(RetentionSec * 1000) + 5000), "the deadline didn't start the auction");
        var setup = a.Read(x => x.Replica!.Setup!);
        Check(setup.Retention && setup.Retentions.Count == _data.Franchises.Count, "not every franchise has retentions");
        Check(setup.Retentions["MBH"].SequenceEqual(keeps), "MBH's keeps aren't the submitted ones");
        Check(setup.Retentions["CHC"].Count > 0, "CHC (timed out) kept nobody in a mini auction");
        NoDesync(bots);
        await Finish(bots);
        return $"MBH kept {keeps.Count}, CHC timed out -> AI kept {setup.Retentions["CHC"].Count}";
    }

    private async Task<string> AllSimulate()
    {
        var bots = await Room(new[] { "MBH", "CHC", "BLR" });
        await StartAuction(bots);
        await NextOpen(bots[0], 0);
        bots[0].Send("simulate");
        bots[1].Send("simulate");
        Check(await bots[2].WaitFor(x => x.ControlOf("MBH") == "autopilot" && x.ControlOf("CHC") == "autopilot", 3000), "two simulates not seen");
        Check(bots[2].Phase == "auction", "finished before the last human simulated");
        var t0 = Bot.Now;
        bots[2].Send("simulate");
        foreach (var b in bots)
        {
            Check(await b.WaitFor(x => x.Phase == "finished" && x.Results != null && x.LastStartReason == "instant", 10000), $"{b.Name}: no instant finish");
        }
        var took = Bot.Now - t0;
        Check(took < 3, $"instant finish took {took:0.0}s");
        NoDesync(bots);
        foreach (var b in bots) Check(b.Read(x => x.Replica!.Engine.IsFinished), $"{b.Name}'s replica isn't finished");

        // Rewards: present, consistent with the squads every phone holds, and deterministic.
        var r = bots[0].Results!;
        var engine = bots[0].Replica!.Engine;
        var standings = ((JArray)r["standings"]!).OfType<JObject>().ToList();
        Check(standings.Count == _data.Franchises.Count, "standings don't cover every team");
        Check(standings.Select(s => (int)s["rank"]!).SequenceEqual(Enumerable.Range(1, standings.Count)), "ranks aren't 1..N");
        Check((string?)r["winner"] == (string?)standings[0]["team"], "winner isn't rank 1");
        for (var i = 1; i < standings.Count; i++) Check((int)standings[i - 1]["total"]! >= (int)standings[i]["total"]!, "standings not sorted by total");
        foreach (var s in standings)
        {
            var team = (string)s["team"]!;
            var sum = ((JObject)s["points"]!).Properties().Sum(p => (int)p.Value);
            Check(sum == (int)s["total"]!, $"{team}: points don't add up to the total");
            Check((int)s["stats"]!["squadSize"]! == engine.Teams[team].Size, $"{team}: squad size differs from the replica");
            Check((int)s["stats"]!["purseLeft"]! == engine.Teams[team].Purse, $"{team}: purse differs from the replica");
            Check(engine.Teams[team].Size >= engine.Format.MinSquad, $"{team}: below the minimum squad");
        }
        var mine = standings.First(s => (string?)s["team"] == "MBH");
        Check((string?)mine["owner"] == bots[0].Id && (string?)mine["control"] == "autopilot", "MBH's owner/control wrong");
        Check(standings.Count(s => (string?)s["control"] == "ai") == _data.Franchises.Count - 3, "bot teams not marked ai");
        var ids = ((JArray)r["awards"]!).Select(x => (string)x["id"]!).ToList();
        foreach (var id in new[] { "bestSquad", "bigSpender", "mostBalanced", "valueKing", "biggestBuy" }) Check(ids.Contains(id), $"award {id} missing");
        var best = ((JArray)r["awards"]!).First(x => (string?)x["id"] == "bestSquad");
        Check((int)standings.First(s => (string?)s["team"] == (string?)best["team"])["overall"]! == standings.Max(s => (int)s["overall"]!), "Best Squad isn't the top overall");
        Check(((JArray)r["scoring"]!).Count > 0, "no scoring rules");
        // A phone computing the rewards itself (solo path) gets the same numbers.
        var local = AuctionAwards.Compute(engine, _data.Rules);
        foreach (var s in standings) Check(local.For((string)s["team"]!).Total == (int)s["total"]!, $"{s["team"]}: local total differs");
        Check(bots[1].Read(x => JToken.DeepEquals(x.Results, r)), "players got different results");
        foreach (var b in bots) b.Drop();
        var award = (JObject)((JArray)r["awards"]!).First(x => (string?)x["id"] == "steal" || (string?)x["id"] == "bestSquad");
        return $"finished in {took:0.00}s after the last simulate; winner {r["winner"]} ({standings[0]["total"]} pts); {award["title"]}: {award["team"]}";
    }

    private async Task<string> IdleCleanup()
    {
        var a = await NewBot("Lonely");
        a.Send("create", ("format", "mega"), ("retention", false), ("team", "GJG"));
        Check(await a.WaitFor(x => x.Code != null, 3000), "no room");
        var code = a.Code!;
        a.Drop();
        await Task.Delay(4500); // the in-process server drops rooms with nobody connected after 3 s
        var b = await NewBot("Late");
        var mark = b.Mark;
        b.Send("join", ("code", code));
        var err = await b.ErrorFor("join", mark);
        b.Drop();
        Check(err == "noSuchRoom", $"the abandoned room is still there ({err ?? "joined"})");
        return "abandoned room dropped";
    }
}
