using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Apl.Core
{
    /// <summary>Port of the Dart room protocol (packages/auction_core/lib/src/rooms/protocol.dart).</summary>
    public static class RoomProtocol
    {
        /// <summary>Must equal the server's protocol (v2: the C# server, server-cs/AplRooms; docs/ROOMS_PROTOCOL.md).</summary>
        public const int Version = 2;
    }

    /// <summary>Everything a phone needs to build the same engine the server runs.</summary>
    public sealed class RoomSetup
    {
        public long Seed, SeasonSeed;
        public string FormatId = "";
        public bool Retention;

        /// <summary>Team id → retained player ids, in the server's (franchise) order.</summary>
        public OrderedMap<string, List<string>> Retentions = new OrderedMap<string, List<string>>();
        public int DataVersion;

        /// <summary>
        /// A solo auction (saved by the phone, never sent by the server): IPL-style sets and the
        /// solo price cap (28 Cr). Rooms keep the server's Classic order and the room cap.
        /// Older solo saves lack it and resume exactly as they were made.
        /// </summary>
        public bool Solo;

        /// <summary>
        /// The room rules: 0 = the Dart server's (Classic order, room cap); <see cref="NewRules"/> (2) =
        /// the C# server's, the same engine as solo (IPL sets, the 28 Cr cap). Missing in old JSON = 0.
        /// </summary>
        public int Rules;

        /// <summary>Protocol v2 rooms: IPL sets, Franchise AI, reputation prices, the 28 Cr cap.</summary>
        public const int NewRules = 2;

        /// <summary>Solo, or a v2 room: the same engine either way.</summary>
        public bool PlaysNewRules => Solo || Rules >= NewRules;

        public AuctionEngine BuildEngine(Dataset data)
        {
            var byId = data.Players.ToDictionary(p => p.Id);
            var retentions = new Dictionary<string, List<Player>>();
            foreach (var kv in Retentions) retentions[kv.Key] = kv.Value.Select(id => byId[id]).ToList();
            if (PlaysNewRules) return new AuctionEngine(data.Rules, data.Rules.Format(FormatId), data.Franchises, data.Players, Seed, retentions, data.Rules.MaxPrice, LotOrder.IplSets);
            // Friend rooms play to their own (higher) price cap.
            return new AuctionEngine(data.Rules, data.Rules.Format(FormatId), data.Franchises, data.Players, Seed, retentions, data.Rules.RoomMaxPrice ?? data.Rules.MaxPrice);
        }

        public static RoomSetup FromJson(JObject j)
        {
            var setup = new RoomSetup
            {
                Seed = (long)j["seed"]!,
                SeasonSeed = (long)j["seasonSeed"]!,
                FormatId = (string)j["format"]!,
                Retention = (bool)j["retention"]!,
                DataVersion = (int)j["dataVersion"]!,
                Solo = (bool?)j["solo"] ?? false,
                Rules = (int?)j["rules"] ?? 0,
            };
            foreach (var p in ((JObject)j["retentions"]!).Properties())
            {
                setup.Retentions[p.Name] = ((JArray)p.Value).Select(x => (string)x!).ToList();
            }
            return setup;
        }

        /// <summary>The server's "start" setup (and the solo save's). "solo" / "rules" only when set.</summary>
        public JObject ToJson()
        {
            var retentions = new JObject();
            foreach (var kv in Retentions) retentions[kv.Key] = new JArray(kv.Value);
            var j = new JObject
            {
                ["seed"] = Seed,
                ["seasonSeed"] = SeasonSeed,
                ["format"] = FormatId,
                ["retention"] = Retention,
                ["retentions"] = retentions,
                ["dataVersion"] = DataVersion,
            };
            if (Solo) j["solo"] = true;
            if (Rules != 0) j["rules"] = Rules;
            return j;
        }
    }

    /// <summary>One change the server made to its engine; phones replay these in order.</summary>
    public abstract class RoomAction
    {
        public abstract JObject ToJson();

        public static RoomAction FromJson(JObject j) => (string?)j["a"] switch
        {
            "open" => new OpenLotAction((string?)j["p"]),
            "bid" => new BidAction((string)j["team"]!, (int)j["amount"]!),
            "pass" => new PassAction((string)j["team"]!),
            "end" => new EndBiddingAction(),
            "close" => new CloseLotAction((bool)j["rtm"]!, (bool?)j["unsold"] ?? false),
            var other => throw new FormatException($"Unknown room action {other}"),
        };

        public static RoomAction ForEvent(LiveEvent e) => e switch
        {
            LotRevealed r => new OpenLotAction(r.Lot.Player.Id),
            AuctionFinished _ => new OpenLotAction(null),
            BidPlaced b => new BidAction(b.TeamId, b.Amount),
            Passed p => new PassAction(p.TeamId),
            RtmOffered _ => new EndBiddingAction(),
            LotClosed c => new CloseLotAction(c.Result.ViaRtm, c.ForcedUnsold),
            _ => throw new ArgumentException("Unknown event"),
        };
    }

    public sealed class OpenLotAction : RoomAction
    {
        public OpenLotAction(string? playerId) { PlayerId = playerId; }
        public string? PlayerId { get; }
        // A JSON null, not a null string: it goes over the wire to the Dart server.
        public override JObject ToJson() => new JObject { ["a"] = "open", ["p"] = PlayerId == null ? JValue.CreateNull() : new JValue(PlayerId) };
    }

    public sealed class BidAction : RoomAction
    {
        public BidAction(string teamId, int amount)
        {
            TeamId = teamId;
            Amount = amount;
        }

        public string TeamId { get; }
        public int Amount { get; }
        public override JObject ToJson() => new JObject { ["a"] = "bid", ["team"] = TeamId, ["amount"] = Amount };
    }

    public sealed class PassAction : RoomAction
    {
        public PassAction(string teamId) { TeamId = teamId; }
        public string TeamId { get; }
        public override JObject ToJson() => new JObject { ["a"] = "pass", ["team"] = TeamId };
    }

    public sealed class EndBiddingAction : RoomAction
    {
        public override JObject ToJson() => new JObject { ["a"] = "end" };
    }

    public sealed class CloseLotAction : RoomAction
    {
        public CloseLotAction(bool rtm, bool unsold = false)
        {
            Rtm = rtm;
            Unsold = unsold;
        }

        public bool Rtm { get; }

        /// <summary>v2: the host forced the lot unsold; replicas drop its bids before closing.</summary>
        public bool Unsold { get; }

        public override JObject ToJson()
        {
            var j = new JObject { ["a"] = "close", ["rtm"] = Rtm };
            if (Unsold) j["unsold"] = true;
            return j;
        }
    }

    /// <summary>The replica disagrees with the server (different data or version).</summary>
    public sealed class RoomDesyncException : Exception
    {
        public RoomDesyncException(string message) : base(message) { }
    }

    public static class RoomReplay
    {
        /// <summary>Applies one server action and returns the matching event for the UI.</summary>
        public static LiveEvent Apply(AuctionEngine engine, RoomAction action)
        {
            switch (action)
            {
                case OpenLotAction open:
                    var lot = engine.OpenNextLot();
                    if (lot?.Player.Id != open.PlayerId) throw new RoomDesyncException($"expected lot {open.PlayerId}, got {lot?.Player.Id}");
                    return lot == null ? (LiveEvent)new AuctionFinished() : new LotRevealed(lot);
                case BidAction bid:
                    var rejection = engine.PlaceBid(bid.TeamId, bid.Amount);
                    if (rejection != null) throw new RoomDesyncException($"bid {bid.TeamId} {bid.Amount} rejected: {rejection}");
                    return new BidPlaced(bid.TeamId, bid.Amount);
                case PassAction pass:
                    engine.Pass(pass.TeamId, Array.Empty<string>());
                    return new Passed(pass.TeamId);
                case EndBiddingAction _:
                    var offer = engine.EndBidding() ?? throw new RoomDesyncException("server had an RTM offer, replica has none");
                    return new RtmOffered(offer);
                case CloseLotAction close:
                    if (close.Unsold)
                    {
                        if (engine.CurrentLot == null) throw new RoomDesyncException("forced unsold with no open lot");
                        RoomRules.DropBids(engine);
                    }
                    return new LotClosed(engine.CloseLot(close.Rtm), close.Unsold);
                default:
                    throw new ArgumentException("Unknown action");
            }
        }
    }

    /// <summary>A phone's copy of the server's auction: server actions + clocks in, button intents out.</summary>
    public sealed class RemoteAuction : IAuctionDriver
    {
        private readonly Dataset _data;
        private readonly Action<JObject> _send;
        private AuctionEngine? _engine;
        private int _bidLeft, _phaseLeft;
        private bool _fast;

        public RemoteAuction(Dataset data, Action<JObject> send)
        {
            _data = data;
            _send = send;
        }

        public RoomSetup? Setup { get; private set; }
        public AuctionEngine Engine => _engine ?? throw new InvalidOperationException("Auction not started");
        public bool IsLoaded => _engine != null;
        public ICollection<string> HumanTeamIds { get; private set; } = new HashSet<string>();
        public LivePhase Phase { get; private set; } = LivePhase.Revealing;
        public LotResult? LastResult { get; private set; }
        public int BidTimerTotalMs { get; private set; } = 10000;
        public int BidTimeLeftMs => Phase == LivePhase.Bidding ? _bidLeft : 0;
        public int PhaseTimeLeftMs => _phaseLeft;
        public int HumanRtmTimeoutMs { get; private set; } = 10000;
        public RtmOffer? PendingRtm => Phase == LivePhase.RtmDecision ? _engine?.CurrentLot?.RtmOffer : null;
        public bool IsFastForwarding => _fast;
        public bool CanHoldFastForward => false;
        public bool HoldingFastForward { get => false; set { } }
        public event Action<LiveEvent>? OnEvent;

        /// <summary>(Re)builds from the setup and every action so far (start / reconnect).</summary>
        public void Load(RoomSetup setup, IEnumerable<RoomAction> log)
        {
            Setup = setup;
            var engine = setup.BuildEngine(_data);
            LotResult? last = null;
            foreach (var a in log)
            {
                if (RoomReplay.Apply(engine, a) is LotClosed closed) last = closed.Result;
            }
            _engine = engine;
            LastResult = last;
            Phase = engine.IsFinished ? LivePhase.Finished : LivePhase.Revealing;
        }

        public void Apply(RoomAction action)
        {
            var e = RoomReplay.Apply(Engine, action);
            switch (e)
            {
                case LotRevealed _: Phase = LivePhase.Revealing; break;
                case AuctionFinished _: Phase = LivePhase.Finished; break;
                case BidPlaced _: _bidLeft = BidTimerTotalMs; break;
                case RtmOffered _: Phase = LivePhase.RtmDecision; break;
                case LotClosed closed:
                    LastResult = closed.Result;
                    Phase = LivePhase.Result;
                    break;
            }
            OnEvent?.Invoke(e);
        }

        /// <summary>The server's authoritative phase and timers.</summary>
        public void Clock(JObject m)
        {
            Phase = (string)m["phase"]! switch
            {
                "revealing" => LivePhase.Revealing,
                "bidding" => LivePhase.Bidding,
                "rtmDecision" => LivePhase.RtmDecision,
                "result" => LivePhase.Result,
                _ => LivePhase.Finished,
            };
            _bidLeft = (int)m["bid"]!;
            BidTimerTotalMs = (int)m["bidTotal"]!;
            _phaseLeft = (int)m["left"]!;
            HumanRtmTimeoutMs = (int)m["rtmTotal"]!;
            _fast = (bool)m["ff"]!;
            HumanTeamIds = new HashSet<string>(((JArray)m["humans"]!).Select(x => (string)x!));
            // v2 (optional, so a v1 clock still parses).
            Paused = (bool?)m["paused"] ?? false;
            Passed = (m["passed"] as JArray)?.Select(x => (string)x!).ToList() ?? new List<string>();
            LotNumber = (int?)m["lot"] ?? 0;
            TimerSec = (int?)m["timerSec"] ?? TimerSec;
        }

        /// <summary>v2: the host paused the room; every countdown is frozen.</summary>
        public bool Paused { get; private set; }

        /// <summary>v2: teams that passed on the open lot (out of it only).</summary>
        public IReadOnlyList<string> Passed { get; private set; } = new List<string>();

        /// <summary>v2: the open lot's number (0 between lots).</summary>
        public int LotNumber { get; private set; }

        /// <summary>v2: the host's bid timer setting, seconds.</summary>
        public int TimerSec { get; private set; } = 10;

        public void Start() { }

        public void Advance(int realMs)
        {
            if (Paused) return;
            var ms = _fast ? realMs * 8 : realMs;
            if (Phase == LivePhase.Bidding) _bidLeft = Math.Max(0, Math.Min(BidTimerTotalMs, _bidLeft - ms));
            else _phaseLeft = Math.Max(0, _phaseLeft - ms);
        }

        public BidRejection? HumanBid(string teamId)
        {
            if (Phase != LivePhase.Bidding || !IsLoaded) return BidRejection.BiddingClosed;
            var local = Engine.CheckBid(teamId);
            if (local != null) return local;
            _send(new JObject { ["t"] = "bid", ["amount"] = Engine.NextBidAmount });
            return null;
        }

        public void HumanPass(string teamId) => _send(new JObject { ["t"] = "pass" });

        public void HumanRtm(string teamId, bool use) => _send(new JObject { ["t"] = "rtm", ["use"] = use });
    }
}
