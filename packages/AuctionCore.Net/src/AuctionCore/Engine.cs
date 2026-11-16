using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core
{
    public enum PurchaseKind { Auction, Rtm, Retained, AutoFill }

    public sealed class Purchase
    {
        public Purchase(Player player, int price, PurchaseKind kind = PurchaseKind.Auction)
        {
            Player = player;
            Price = price;
            Kind = kind;
        }

        public Player Player { get; }

        /// <summary>Lakhs.</summary>
        public int Price { get; }
        public PurchaseKind Kind { get; }
        public bool AutoFilled => Kind == PurchaseKind.AutoFill;
    }

    public sealed class TeamState
    {
        public TeamState(Franchise franchise, int purse, int rtmCards = 0)
        {
            Franchise = franchise;
            Purse = purse;
            RtmCards = rtmCards;
        }

        public Franchise Franchise { get; }

        /// <summary>Lakhs left.</summary>
        public int Purse;
        public int RtmCards;
        public readonly List<Purchase> Squad = new List<Purchase>();

        public string Id => Franchise.Id;
        public int Size => Squad.Count;
        public int OverseasCount => Squad.Count(p => p.Player.Overseas);
        public int Spent => Squad.Sum(p => p.Price);
        public List<Player> Players => Squad.Select(p => p.Player).ToList();
        public int CountRole(Role role) => Squad.Count(p => p.Player.Role == role);
    }

    public enum LotRound { Main, Accelerated }

    public sealed class Bid
    {
        public Bid(string teamId, int amount)
        {
            TeamId = teamId;
            Amount = amount;
        }

        public string TeamId { get; }
        public int Amount { get; }
    }

    public sealed class Lot
    {
        public Lot(int number, Player player, LotRound round)
        {
            Number = number;
            Player = player;
            Round = round;
        }

        public int Number { get; }
        public Player Player { get; }
        public LotRound Round { get; }
        public readonly List<Bid> Bids = new List<Bid>();

        /// <summary>Timer ran out; only an RTM decision (if any) is left.</summary>
        public bool BiddingClosed;
        public RtmOffer? RtmOffer;

        /// <summary>Teams that pressed Pass: out of this lot for good.</summary>
        public readonly HashSet<string> Passed = new HashSet<string>();

        public int? CurrentBid => Bids.Count == 0 ? (int?)null : Bids[Bids.Count - 1].Amount;
        public string? LeaderId => Bids.Count == 0 ? null : Bids[Bids.Count - 1].TeamId;
    }

    /// <summary>The player's previous team may take him at the winning price.</summary>
    public sealed class RtmOffer
    {
        public RtmOffer(string teamId, string winnerId, int price)
        {
            TeamId = teamId;
            WinnerId = winnerId;
            Price = price;
        }

        public string TeamId { get; }
        public string WinnerId { get; }
        public int Price { get; }
    }

    public sealed class LotResult
    {
        public int LotNumber;
        public Player Player = null!;
        public LotRound Round;
        public int BidCount;
        public string? TeamId;
        public int? Price;
        public bool ViaRtm;
        public string? OutbidTeamId;
        public bool Sold => TeamId != null;
    }

    public enum BidRejection
    {
        NoOpenLot,
        UnknownTeam,
        AlreadyLeading,
        SquadFull,
        OverseasLimit,
        CannotAfford,
        StaleAmount,
        BiddingClosed,
        Passed,
        PriceCap,
    }

    public static class Retention
    {
        /// <summary>Why these retentions aren't allowed, or null.</summary>
        public static string? Problem(string teamId, IReadOnlyList<Player> players, AuctionRules rules, AuctionFormat format)
        {
            var max = rules.RetentionFor(format).MaxPlayers;
            if (players.Count > max) return $"At most {max} players can be retained";
            if (players.Distinct().Count() != players.Count) return "A player is listed twice";
            foreach (var p in players)
            {
                if (p.PreviousTeam != teamId) return $"{p.Name} did not play for this team last season";
            }
            if (players.Count(p => p.Overseas) > format.MaxOverseas) return "Too many overseas players";
            var cost = Prices(players, rules, format).Sum(kv => kv.Value);
            var reserve = Math.Max(0, format.MinSquad - players.Count) * rules.MinBasePrice;
            if (cost > format.Purse - reserve) return "Retentions cost more than the purse allows";
            return null;
        }

        /// <summary>Capped players take the slabs most-valuable first; uncapped pay the flat price.</summary>
        public static List<KeyValuePair<Player, int>> Prices(IReadOnlyList<Player> players, AuctionRules rules, AuctionFormat format)
        {
            var retention = rules.RetentionFor(format);
            // Mini auction: each keeps his current price, rounded to ₹5 L.
            if (retention.AtValue)
            {
                return players
                    .Select(p => new KeyValuePair<Player, int>(p, Math.Max(p.BasePrice, DartMath.Round(Market.FairPrice(p, format) / 5) * 5)))
                    .ToList();
            }
            var scale = format.Purse / 12000.0;
            int Scaled(int lakhs) => DartMath.Round(lakhs * scale / 25) * 25;
            var slabs = retention.Slabs;
            var capped = players.Where(p => p.Capped).ToList();
            capped.Sort(ByValue);
            var result = new List<KeyValuePair<Player, int>>();
            for (var i = 0; i < capped.Count; i++)
            {
                result.Add(new KeyValuePair<Player, int>(capped[i], Scaled(slabs[Math.Min(i, slabs.Count - 1)])));
            }
            foreach (var p in players.Where(p => !p.Capped))
            {
                result.Add(new KeyValuePair<Player, int>(p, Scaled(retention.UncappedPrice)));
            }
            return result;
        }

        public static double Value(Player p) => p.Impact + p.StarPower / 5.0;

        public static int ByValue(Player a, Player b)
        {
            var c = Value(b).CompareTo(Value(a));
            return c != 0 ? c : DartMath.Compare(a.Id, b.Id);
        }
    }

    /// <summary>
    /// The auction rules as a state machine (port of the Dart AuctionEngine).
    /// No clock: the driver runs the timer, calls EndBidding when it expires,
    /// then CloseLot with the RTM answer.
    /// </summary>
    public sealed class AuctionEngine
    {
        public const int TierSize = 8;

        private readonly LinkedList<Player> _queue = new LinkedList<Player>();
        private readonly OrderedMap<string, Player> _available = new OrderedMap<string, Player>();
        private readonly List<Player> _unsoldMain = new List<Player>();
        private Lot? _current;
        private int _lotCounter;
        private bool _acceleratedStarted;
        private bool _finished;

        /// <summary>No bid above this (lakhs); null = no cap. The rules' cap unless set (friend rooms play higher).</summary>
        public int? MaxPrice { get; }

        public AuctionEngine(
            AuctionRules rules,
            AuctionFormat format,
            IReadOnlyList<Franchise> franchises,
            IReadOnlyList<Player> pool,
            long seed,
            IReadOnlyDictionary<string, List<Player>>? retentions = null,
            int? maxPrice = null,
            LotOrder lotOrder = LotOrder.Classic)
        {
            LotOrder = lotOrder;
            Seed = seed;
            Rules = rules;
            Format = format;
            MaxPrice = maxPrice ?? rules.MaxPrice;
            var rng = new Rng(seed);
            foreach (var f in franchises) Teams[f.Id] = new TeamState(f, format.Purse, rules.RtmCardsFor(format));

            var retained = new HashSet<string>();
            if (retentions != null)
            {
                foreach (var key in retentions.Keys)
                {
                    if (!Teams.ContainsKey(key)) throw new ArgumentException($"Unknown team {key}");
                }
                // Franchise order, as every Dart caller builds its retentions map.
                foreach (var teamId in Teams.Keys.ToList())
                {
                    if (!retentions.TryGetValue(teamId, out var players)) continue;
                    var team = Teams[teamId];
                    var problem = Retention.Problem(teamId, players, rules, format);
                    if (problem != null) throw new ArgumentException($"{teamId}: {problem}");
                    foreach (var kv in Retention.Prices(players, rules, format))
                    {
                        if (!retained.Add(kv.Key.Id)) throw new ArgumentException($"{kv.Key.Id} retained twice");
                        team.Purse -= kv.Value;
                        team.Squad.Add(new Purchase(kv.Key, kv.Value, PurchaseKind.Retained));
                    }
                }
            }
            var auctionPool = pool.Where(p => !retained.Contains(p.Id)).ToList();
            foreach (var p in auctionPool) _available[p.Id] = p;
            if (lotOrder == LotOrder.IplSets)
            {
                foreach (var set in AuctionSets.Build(auctionPool, format, rules, rng))
                {
                    foreach (var p in set.Players)
                    {
                        _queue.AddLast(p);
                        _setOf[p.Id] = set.Code;
                    }
                }
            }
            else
            {
                foreach (var p in SelectLots(auctionPool, format, rules, rng)) _queue.AddLast(p);
            }
        }

        /// <summary>The auction's seed: the franchise AI derives each team's per-auction mood and targets from it, so a rebuilt bidder keeps them.</summary>
        public long Seed { get; }

        /// <summary>How the lots are grouped and ordered: Classic (Dart parity, friend rooms) or IplSets (solo).</summary>
        public LotOrder LotOrder { get; }

        private readonly Dictionary<string, string> _setOf = new Dictionary<string, string>();

        /// <summary>
        /// The main-round set a player belongs to: an IPL set code ("M1", "BA2", "UFA1") in IplSets
        /// order, the data's set name ("batters", "uncapped") in Classic order. Accelerated lots: see CurrentSet.
        /// </summary>
        public string SetOf(Player p) =>
            _setOf.TryGetValue(p.Id, out var code) ? code : p.Set;

        /// <summary>The set of the lot on the block (or of the next one, between lots); null when none is left.</summary>
        public string? CurrentSet
        {
            get
            {
                if (_current != null) return _current.Round == LotRound.Accelerated ? AcceleratedSet : SetOf(_current.Player);
                if (_queue.Count == 0) return null;
                return _acceleratedStarted ? AcceleratedSet : SetOf(_queue.First!.Value);
            }
        }

        /// <summary>The accelerated round's set name: "ACC" (IplSets) or "accelerated" (Classic).</summary>
        public string AcceleratedSet => LotOrder == LotOrder.IplSets ? AuctionSets.Accelerated : "accelerated";

        /// <summary>Unsold main-round players still available: the pool the accelerated round draws from.</summary>
        public IReadOnlyList<Player> UnsoldPool => _unsoldMain.Where(p => _available.ContainsKey(p.Id)).ToList();

        public AuctionRules Rules { get; }
        public AuctionFormat Format { get; }

        /// <summary>In franchise order.</summary>
        public OrderedMap<string, TeamState> Teams { get; } = new OrderedMap<string, TeamState>();
        public List<LotResult> Results { get; } = new List<LotResult>();

        public Lot? CurrentLot => _current;
        public bool IsFinished => _finished;
        public bool InAcceleratedRound => _acceleratedStarted;
        public IReadOnlyList<Player> Upcoming => _queue.ToList();

        public int LotsRemaining => _queue.Count;
        public IEnumerable<Player> Available => _available.Values;

        public int NextBidAmount
        {
            get
            {
                var lot = _current ?? throw new InvalidOperationException("No lot is open");
                return Rules.NextBid(lot.CurrentBid, lot.Player.BasePrice);
            }
        }

        public int ReserveAfterBuying(TeamState t) => Math.Max(0, Format.MinSquad - (t.Size + 1)) * Rules.MinBasePrice;

        public int MaxBidFor(TeamState t) => t.Purse - ReserveAfterBuying(t);

        public Lot? OpenNextLot()
        {
            if (_current != null) throw new InvalidOperationException($"Lot {_current.Number} is still open");
            while (!_finished)
            {
                if (_queue.Count == 0 && !_acceleratedStarted)
                {
                    _acceleratedStarted = true;
                    foreach (var p in AcceleratedLots()) _queue.AddLast(p);
                }
                if (_queue.Count == 0 || Teams.Values.All(t => t.Size >= Format.MaxSquad))
                {
                    Finish();
                    return null;
                }
                var player = _queue.First!.Value;
                _queue.RemoveFirst();
                var round = _acceleratedStarted ? LotRound.Accelerated : LotRound.Main;
                var lot = new Lot(_lotCounter + 1, player, round);
                if (!Teams.Values.Any(t => Eligible(t, lot) == null))
                {
                    if (round == LotRound.Main) _unsoldMain.Add(player);
                    continue;
                }
                _lotCounter++;
                return _current = lot;
            }
            return null;
        }

        public BidRejection? CheckBid(string teamId, int? amount = null)
        {
            var lot = _current;
            if (lot == null) return BidRejection.NoOpenLot;
            if (lot.BiddingClosed) return BidRejection.BiddingClosed;
            if (!Teams.TryGetValue(teamId, out var t)) return BidRejection.UnknownTeam;
            if (lot.Passed.Contains(teamId)) return BidRejection.Passed;
            if (lot.LeaderId == teamId) return BidRejection.AlreadyLeading;
            var rejection = Eligible(t, lot);
            if (rejection != null) return rejection;
            if (amount != null && amount.Value != NextBidAmount) return BidRejection.StaleAmount;
            return null;
        }

        /// <summary>Team sits this lot out. True once no human can act on it any more.</summary>
        public bool Pass(string teamId, ICollection<string> humanTeamIds)
        {
            var lot = _current ?? throw new InvalidOperationException("No lot is open");
            if (lot.LeaderId != teamId) lot.Passed.Add(teamId);
            return humanTeamIds.All(id =>
            {
                if (lot.Passed.Contains(id)) return true;
                var reason = CheckBid(id);
                return reason != null && reason != BidRejection.AlreadyLeading;
            });
        }

        public BidRejection? PlaceBid(string teamId, int amount)
        {
            var rejection = CheckBid(teamId, amount);
            if (rejection == null) _current!.Bids.Add(new Bid(teamId, amount));
            return rejection;
        }

        public RtmOffer? EndBidding()
        {
            var lot = _current ?? throw new InvalidOperationException("No lot is open");
            if (lot.BiddingClosed) return lot.RtmOffer;
            lot.BiddingClosed = true;

            var winner = lot.LeaderId;
            var previous = lot.Player.PreviousTeam;
            if (winner == null || previous == null || previous == winner) return null;
            if (!Teams.TryGetValue(previous, out var t) || t.RtmCards <= 0) return null;
            if (t.Size >= Format.MaxSquad) return null;
            if (lot.Player.Overseas && t.OverseasCount >= Format.MaxOverseas) return null;
            if (lot.CurrentBid!.Value > MaxBidFor(t)) return null;
            return lot.RtmOffer = new RtmOffer(previous, winner, lot.CurrentBid.Value);
        }

        public LotResult CloseLot(bool useRtm = false)
        {
            var lot = _current ?? throw new InvalidOperationException("No lot is open");
            var offer = EndBidding();
            if (useRtm && offer == null) throw new InvalidOperationException("No RTM offer on this lot");
            _current = null;

            var buyer = useRtm ? offer!.TeamId : lot.LeaderId;
            if (buyer != null)
            {
                var t = Teams[buyer];
                var price = lot.CurrentBid!.Value;
                t.Purse -= price;
                if (useRtm) t.RtmCards--;
                t.Squad.Add(new Purchase(lot.Player, price, useRtm ? PurchaseKind.Rtm : PurchaseKind.Auction));
                _available.Remove(lot.Player.Id);
            }
            else if (lot.Round == LotRound.Main)
            {
                _unsoldMain.Add(lot.Player);
            }
            var result = new LotResult
            {
                LotNumber = lot.Number,
                Player = lot.Player,
                Round = lot.Round,
                BidCount = lot.Bids.Count,
                TeamId = buyer,
                Price = lot.CurrentBid,
                ViaRtm = useRtm,
                OutbidTeamId = useRtm ? offer!.WinnerId : null,
            };
            Results.Add(result);
            return result;
        }

        private BidRejection? Eligible(TeamState t, Lot lot)
        {
            if (t.Size >= Format.MaxSquad) return BidRejection.SquadFull;
            if (lot.Player.Overseas && t.OverseasCount >= Format.MaxOverseas) return BidRejection.OverseasLimit;
            var next = Rules.NextBid(lot.CurrentBid, lot.Player.BasePrice);
            if (MaxPrice != null && next > MaxPrice.Value) return BidRejection.PriceCap;
            if (next > MaxBidFor(t)) return BidRejection.CannotAfford;
            return null;
        }

        private List<Player> AcceleratedLots()
        {
            var targetSize = Market.SquadTargets(Format).Size;
            var wanted = Teams.Values.Sum(t => Math.Max(0, targetSize - t.Size));
            var leftovers = _unsoldMain.Where(p => _available.ContainsKey(p.Id)).ToList();
            leftovers.Sort(ByImpactDesc);
            return leftovers.Take(wanted).ToList();
        }

        private void Finish()
        {
            AutoFill();
            _finished = true;
        }

        private void AutoFill()
        {
            var order = Teams.Keys.ToList();
            while (true)
            {
                var shortTeams = Teams.Values.Where(t => t.Size < Format.MinSquad).ToList();
                shortTeams.Sort((a, b) =>
                {
                    var bySize = a.Size.CompareTo(b.Size);
                    return bySize != 0 ? bySize : order.IndexOf(a.Id).CompareTo(order.IndexOf(b.Id));
                });
                if (shortTeams.Count == 0) return;
                var progressed = false;
                foreach (var t in shortTeams)
                {
                    var pick = FillerFor(t);
                    if (pick == null) continue;
                    var price = Math.Min(pick.BasePrice, t.Purse);
                    t.Purse -= price;
                    t.Squad.Add(new Purchase(pick, price, PurchaseKind.AutoFill));
                    _available.Remove(pick.Id);
                    progressed = true;
                }
                if (!progressed) return;
            }
        }

        /// <summary>A squad filler, not a prize: cheapest, lowest-rated player for a missing role.</summary>
        private Player? FillerFor(TeamState t)
        {
            var eligible = _available.Values.Where(p => !p.Overseas || t.OverseasCount < Format.MaxOverseas).ToList();
            if (eligible.Count == 0) return null;

            var squad = t.Players;
            Func<Player, bool> wanted;
            if (!squad.Any(p => p.IsKeeper)) wanted = p => p.IsKeeper;
            else if (squad.Count(p => p.IsBowlingOption) < 5) wanted = p => p.IsBowlingOption;
            else if (squad.Count(p => p.Batting >= 55) < 6) wanted = p => p.Batting >= 55;
            else wanted = _ => true;

            var needed = eligible.Where(wanted).ToList();
            var pool = needed.Count == 0 ? eligible : needed;
            pool.Sort((a, b) =>
            {
                var byPrice = a.BasePrice.CompareTo(b.BasePrice);
                if (byPrice != 0) return byPrice;
                var byImpact = a.Impact.CompareTo(b.Impact);
                return byImpact != 0 ? byImpact : DartMath.Compare(a.Id, b.Id);
            });
            return pool[0];
        }

        public static int ByImpactDesc(Player a, Player b)
        {
            var c = b.Impact.CompareTo(a.Impact);
            return c != 0 ? c : DartMath.Compare(a.Id, b.Id);
        }

        /// <summary>Marquee first, then role sets rotating in tiers of 8, uncapped last.</summary>
        public static List<Player> SelectLots(IReadOnlyList<Player> pool, AuctionFormat format, AuctionRules rules, Rng rng)
        {
            var bySet = new OrderedMap<string, List<Player>>();
            foreach (var p in pool)
            {
                if (!bySet.TryGetValue(p.Set, out var list)) bySet[p.Set] = list = new List<Player>();
                list.Add(p);
            }
            var setNames = rules.SetOrder.Where(bySet.ContainsKey)
                .Concat(bySet.Keys.Where(s => !rules.SetOrder.Contains(s)))
                .ToList();

            var maxLots = format.MaxLots;
            var marqueeCount = bySet.TryGetValue("marquee", out var mq) ? mq.Count : 0;
            var rest = pool.Count - marqueeCount;
            int? budget = maxLots == null ? (int?)null : Math.Max(0, maxLots.Value - marqueeCount);
            var chosen = new OrderedMap<string, List<Player>>();
            foreach (var s in setNames)
            {
                var players = bySet[s].ToList();
                players.Sort(ByImpactDesc);
                var quota = budget == null || s == "marquee"
                    ? players.Count
                    : DartMath.Round((double)(budget.Value * players.Count) / Math.Max(1, rest));
                chosen[s] = players.Take(quota).ToList();
            }

            var lots = new List<Player>();
            void AddTier(List<Player> tier)
            {
                var shuffled = tier.ToList();
                rng.Shuffle(shuffled);
                lots.AddRange(shuffled);
            }

            if (chosen.ContainsKey("marquee")) AddTier(chosen["marquee"]);
            var groups = new List<List<string>>
            {
                setNames.Where(s => s != "marquee" && s != "uncapped").ToList(),
                chosen.ContainsKey("uncapped") ? new List<string> { "uncapped" } : new List<string>(),
            };
            foreach (var group in groups)
            {
                var longest = group.Aggregate(0, (m, s) => Math.Max(m, chosen[s].Count));
                for (var start = 0; start < longest; start += TierSize)
                {
                    foreach (var s in group)
                    {
                        var players = chosen[s];
                        if (start < players.Count)
                        {
                            AddTier(players.GetRange(start, Math.Min(start + TierSize, players.Count) - start));
                        }
                    }
                }
            }
            return lots;
        }
    }
}
