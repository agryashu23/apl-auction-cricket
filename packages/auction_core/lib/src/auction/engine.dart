import 'dart:collection';
import 'dart:math' as math;

import '../models.dart';
import '../rng.dart';
import 'market.dart';

enum PurchaseKind {
  auction,

  /// Taken with a Right-to-Match card at the winning bid.
  rtm,

  /// Kept from last season before the auction (retention mode).
  retained,

  /// Added after the auction to reach the minimum squad, at base price.
  autoFill,
}

class Purchase {
  const Purchase(this.player, this.price, {this.kind = PurchaseKind.auction});

  final Player player;

  /// Lakhs.
  final int price;
  final PurchaseKind kind;

  bool get autoFilled => kind == PurchaseKind.autoFill;
}

class TeamState {
  TeamState(this.franchise, this.purse, {this.rtmCards = 0});

  final Franchise franchise;

  /// Lakhs left to spend.
  int purse;
  int rtmCards;
  final List<Purchase> squad = [];

  String get id => franchise.id;
  int get size => squad.length;
  int get overseasCount => squad.where((p) => p.player.overseas).length;
  int get spent => squad.fold(0, (sum, p) => sum + p.price);
  List<Player> get players => [for (final p in squad) p.player];
  int countRole(Role role) => squad.where((p) => p.player.role == role).length;
}

enum LotRound { main, accelerated }

class Bid {
  const Bid(this.teamId, this.amount);

  final String teamId;
  final int amount;
}

class Lot {
  Lot(this.number, this.player, this.round);

  final int number;
  final Player player;
  final LotRound round;
  final List<Bid> bids = [];

  /// The timer ran out; only an RTM decision (if any) is left.
  bool biddingClosed = false;

  /// Teams that pressed Pass: out of this lot for good.
  final Set<String> passed = {};
  RtmOffer? rtmOffer;

  int? get currentBid => bids.isEmpty ? null : bids.last.amount;
  String? get leaderId => bids.isEmpty ? null : bids.last.teamId;
}

/// The player's previous team may take him at the winning price.
class RtmOffer {
  const RtmOffer({required this.teamId, required this.winnerId, required this.price});

  /// Team holding the card (the player's previous team).
  final String teamId;

  /// Team that won the bidding and loses the player if the card is used.
  final String winnerId;
  final int price;
}

class LotResult {
  const LotResult({
    required this.lotNumber,
    required this.player,
    required this.round,
    required this.bidCount,
    this.teamId,
    this.price,
    this.viaRtm = false,
    this.outbidTeamId,
  });

  final int lotNumber;
  final Player player;
  final LotRound round;
  final int bidCount;
  final String? teamId;
  final int? price;

  /// Bought back with an RTM card; [outbidTeamId] had the winning bid.
  final bool viaRtm;
  final String? outbidTeamId;

  bool get sold => teamId != null;
}

enum BidRejection {
  noOpenLot,
  unknownTeam,
  alreadyLeading,
  squadFull,
  overseasLimit,
  cannotAfford,

  /// Someone else bid first; the client's amount is out of date.
  staleAmount,

  /// The timer has run out on this lot.
  biddingClosed,

  /// This team pressed Pass on the lot.
  passed,

  /// The bid would go past the league's price cap.
  priceCap,
}

/// Why this set of retentions isn't allowed, or null if it is. Use it on the
/// retention screen before starting the auction.
String? retentionProblem(
  String teamId,
  List<Player> players,
  AuctionRules rules,
  AuctionFormat format,
) {
  final max = rules.retentionFor(format).maxPlayers;
  if (players.length > max) {
    return 'At most $max players can be retained';
  }
  if (players.toSet().length != players.length) return 'A player is listed twice';
  for (final p in players) {
    if (p.previousTeam != teamId) return '${p.name} did not play for this team last season';
  }
  if (players.where((p) => p.overseas).length > format.maxOverseas) {
    return 'Too many overseas players';
  }
  final cost = retentionPrices(players, rules, format).values.fold(0, (a, b) => a + b);
  final reserve = math.max(0, format.minSquad - players.length) * rules.minBasePrice;
  if (cost > format.purse - reserve) return 'Retentions cost more than the purse allows';
  return null;
}

/// Cost of each retained player. Capped players take the slabs from most to
/// least valuable; uncapped pay the flat price. Scaled to the format's purse
/// and rounded to ₹25 L. In a mini auction ([RetentionRules.atValue]) each
/// keeps his current price instead, rounded to ₹5 L.
Map<Player, int> retentionPrices(
  List<Player> players,
  AuctionRules rules,
  AuctionFormat format,
) {
  final retention = rules.retentionFor(format);
  if (retention.atValue) {
    return {
      for (final p in players) p: math.max(p.basePrice, (fairPrice(p, format) / 5).round() * 5),
    };
  }
  final scale = format.purse / 12000;
  int scaled(int lakhs) => (lakhs * scale / 25).round() * 25;
  final slabs = retention.slabs;
  final capped = players.where((p) => p.capped).toList()
    ..sort(byRetentionValue);
  return {
    for (var i = 0; i < capped.length; i++)
      capped[i]: scaled(slabs[math.min(i, slabs.length - 1)]),
    for (final p in players.where((p) => !p.capped))
      p: scaled(retention.uncappedPrice),
  };
}

/// Ranks players for retention: skill first, fame second.
double retentionValue(Player p) => p.impact + p.starPower / 5;

/// Most valuable first; ties by id so every platform sorts identically.
int byRetentionValue(Player a, Player b) {
  final c = retentionValue(b).compareTo(retentionValue(a));
  return c != 0 ? c : a.id.compareTo(b.id);
}

/// The auction rules as a state machine. It knows nothing about time: the
/// caller (app, room server or headless sim) runs the bid timer and, when it
/// expires, calls [endBidding]; if that returns an RTM offer, the previous
/// team decides and the caller passes the answer to [closeLot]. That keeps
/// solo and friend rooms on exactly the same rules.
///
/// Fresh auction: leave [retentions] empty. Retention mode: pass each team's
/// kept players (checked with [retentionProblem]); they're charged and taken
/// out of the pool before the first lot.
class AuctionEngine {
  /// No bid above this (lakhs); null = no cap. The rules' cap unless the
  /// caller set another (friend rooms play to a higher one).
  final int? maxPrice;

  AuctionEngine({
    required this.rules,
    required this.format,
    required List<Franchise> franchises,
    required List<Player> pool,
    required int seed,
    Map<String, List<Player>> retentions = const {},
    int? maxPrice,
  }) : maxPrice = maxPrice ?? rules.maxPrice {
    final rng = Rng(seed);
    for (final f in franchises) {
      teams[f.id] = TeamState(f, format.purse, rtmCards: rules.rtmCards);
    }
    final retained = <String>{};
    for (final entry in retentions.entries) {
      final team = teams[entry.key];
      if (team == null) throw ArgumentError('Unknown team ${entry.key}');
      final problem = retentionProblem(entry.key, entry.value, rules, format);
      if (problem != null) throw ArgumentError('${entry.key}: $problem');
      for (final MapEntry(key: player, value: price)
          in retentionPrices(entry.value, rules, format).entries) {
        if (!retained.add(player.id)) throw ArgumentError('${player.id} retained twice');
        team.purse -= price;
        team.squad.add(Purchase(player, price, kind: PurchaseKind.retained));
      }
    }
    final auctionPool = pool.where((p) => !retained.contains(p.id)).toList();
    for (final p in auctionPool) {
      _available[p.id] = p;
    }
    _queue.addAll(selectLots(auctionPool, format, rules, rng));
  }

  final AuctionRules rules;
  final AuctionFormat format;

  /// In franchise order.
  final Map<String, TeamState> teams = {};
  final List<LotResult> results = [];

  final Queue<Player> _queue = Queue();
  final Map<String, Player> _available = {};
  final List<Player> _unsoldMain = [];
  Lot? _current;
  int _lotCounter = 0;
  bool _acceleratedStarted = false;
  bool _finished = false;

  Lot? get currentLot => _current;
  bool get isFinished => _finished;
  bool get inAcceleratedRound => _acceleratedStarted;

  /// Players still waiting in this round, in order.
  List<Player> get upcoming => List.unmodifiable(_queue);
  int get lotsRemaining => _queue.length;

  /// Players nobody has bought yet.
  Iterable<Player> get available => _available.values;

  int get nextBidAmount {
    final lot = _current;
    if (lot == null) throw StateError('No lot is open');
    return rules.nextBid(lot.currentBid, lot.player.basePrice);
  }

  /// Money a team must keep back after this purchase so it can still reach
  /// the minimum squad at the lowest base price.
  int reserveAfterBuying(TeamState t) =>
      math.max(0, format.minSquad - (t.size + 1)) * rules.minBasePrice;

  /// The most this team may bid right now.
  int maxBidFor(TeamState t) => t.purse - reserveAfterBuying(t);

  /// Opens the next lot, or returns null once the auction is over (squads are
  /// auto-filled at that point). Players nobody is allowed to bid on are
  /// skipped.
  Lot? openNextLot() {
    if (_current != null) {
      throw StateError('Lot ${_current!.number} is still open');
    }
    while (!_finished) {
      if (_queue.isEmpty && !_acceleratedStarted) {
        _acceleratedStarted = true;
        _queue.addAll(_acceleratedLots());
      }
      if (_queue.isEmpty || teams.values.every((t) => t.size >= format.maxSquad)) {
        _finish();
        return null;
      }
      final player = _queue.removeFirst();
      final round = _acceleratedStarted ? LotRound.accelerated : LotRound.main;
      final lot = Lot(_lotCounter + 1, player, round);
      if (!teams.values.any((t) => _eligible(t, lot) == null)) {
        if (round == LotRound.main) _unsoldMain.add(player);
        continue;
      }
      _lotCounter++;
      return _current = lot;
    }
    return null;
  }

  /// Why [teamId] can't bid on the open lot, or null if it can. Pass
  /// [amount] to also check the bid isn't stale.
  BidRejection? checkBid(String teamId, {int? amount}) {
    final lot = _current;
    if (lot == null) return BidRejection.noOpenLot;
    if (lot.biddingClosed) return BidRejection.biddingClosed;
    final t = teams[teamId];
    if (t == null) return BidRejection.unknownTeam;
    if (lot.passed.contains(teamId)) return BidRejection.passed;
    if (lot.leaderId == teamId) return BidRejection.alreadyLeading;
    final rejection = _eligible(t, lot);
    if (rejection != null) return rejection;
    if (amount != null && amount != nextBidAmount) return BidRejection.staleAmount;
    return null;
  }

  /// [teamId] sits this lot out. A leading team can't pass (its bid stands).
  /// Returns true once no human can act on the lot any more (each has passed
  /// or can't bid, and none is leading — a leader must stay able to answer
  /// if outbid), so the driver can fast-forward it between AI teams.
  bool pass(String teamId, {required Set<String> humanTeamIds}) {
    final lot = _current;
    if (lot == null) throw StateError('No lot is open');
    if (lot.leaderId != teamId) lot.passed.add(teamId);
    return humanTeamIds.every((id) {
      if (lot.passed.contains(id)) return true;
      final reason = checkBid(id);
      return reason != null && reason != BidRejection.alreadyLeading;
    });
  }

  BidRejection? placeBid(String teamId, int amount) {
    final rejection = checkBid(teamId, amount: amount);
    if (rejection == null) _current!.bids.add(Bid(teamId, amount));
    return rejection;
  }

  /// The timer ran out: no more bids. Returns an RTM offer if the player's
  /// previous team (not the winner) still holds a card and could take him at
  /// this price; that team's answer goes to [closeLot]. Safe to call twice.
  RtmOffer? endBidding() {
    final lot = _current;
    if (lot == null) throw StateError('No lot is open');
    if (lot.biddingClosed) return lot.rtmOffer;
    lot.biddingClosed = true;

    final winner = lot.leaderId;
    final previous = lot.player.previousTeam;
    if (winner == null || previous == null || previous == winner) return null;
    final t = teams[previous];
    if (t == null || t.rtmCards <= 0) return null;
    if (t.size >= format.maxSquad) return null;
    if (lot.player.overseas && t.overseasCount >= format.maxOverseas) return null;
    if (lot.currentBid! > maxBidFor(t)) return null;
    return lot.rtmOffer = RtmOffer(teamId: previous, winnerId: winner, price: lot.currentBid!);
  }

  /// Ends the open lot: sold to the highest bidder, to the previous team if
  /// it uses its RTM card ([useRtm]), or unsold. Ends bidding first if the
  /// caller hasn't (an RTM offer is then declined unless [useRtm]).
  LotResult closeLot({bool useRtm = false}) {
    final lot = _current;
    if (lot == null) throw StateError('No lot is open');
    final offer = endBidding();
    if (useRtm && offer == null) throw StateError('No RTM offer on this lot');
    _current = null;

    final buyer = useRtm ? offer!.teamId : lot.leaderId;
    if (buyer != null) {
      final t = teams[buyer]!;
      final price = lot.currentBid!;
      t.purse -= price;
      if (useRtm) t.rtmCards--;
      t.squad.add(Purchase(
        lot.player,
        price,
        kind: useRtm ? PurchaseKind.rtm : PurchaseKind.auction,
      ));
      _available.remove(lot.player.id);
    } else if (lot.round == LotRound.main) {
      _unsoldMain.add(lot.player);
    }
    final result = LotResult(
      lotNumber: lot.number,
      player: lot.player,
      round: lot.round,
      bidCount: lot.bids.length,
      teamId: buyer,
      price: lot.currentBid,
      viaRtm: useRtm,
      outbidTeamId: useRtm ? offer!.winnerId : null,
    );
    results.add(result);
    return result;
  }

  BidRejection? _eligible(TeamState t, Lot lot) {
    if (t.size >= format.maxSquad) return BidRejection.squadFull;
    if (lot.player.overseas && t.overseasCount >= format.maxOverseas) {
      return BidRejection.overseasLimit;
    }
    final next = rules.nextBid(lot.currentBid, lot.player.basePrice);
    if (maxPrice != null && next > maxPrice!) return BidRejection.priceCap;
    if (next > maxBidFor(t)) return BidRejection.cannotAfford;
    return null;
  }

  /// Unsold players get one more, faster round: best first, only as many as
  /// teams still want.
  List<Player> _acceleratedLots() {
    final targetSize = squadTargets(format).size;
    final wanted = teams.values.fold<int>(
      0,
      (sum, t) => sum + math.max(0, targetSize - t.size),
    );
    final leftovers = _unsoldMain.where((p) => _available.containsKey(p.id)).toList()
      ..sort(_byImpactDesc);
    return leftovers.take(wanted).toList();
  }

  void _finish() {
    _autoFill();
    _finished = true;
  }

  /// Takes turns (fewest players first) giving each short team a filler
  /// (see [_fillerFor]) at base price, until every squad is at the minimum.
  void _autoFill() {
    final order = teams.keys.toList();
    while (true) {
      final short = teams.values.where((t) => t.size < format.minSquad).toList()
        ..sort((a, b) {
          final bySize = a.size.compareTo(b.size);
          return bySize != 0 ? bySize : order.indexOf(a.id).compareTo(order.indexOf(b.id));
        });
      if (short.isEmpty) return;
      var progressed = false;
      for (final t in short) {
        final pick = _fillerFor(t);
        if (pick == null) continue;
        final price = math.min(pick.basePrice, t.purse);
        t.purse -= price;
        t.squad.add(Purchase(pick, price, kind: PurchaseKind.autoFill));
        _available.remove(pick.id);
        progressed = true;
      }
      if (!progressed) return;
    }
  }

  /// A squad filler, not a prize: the cheapest, lowest-rated player who
  /// covers a role the team is missing. Handing out the best leftovers made
  /// sitting out the auction a winning strategy.
  Player? _fillerFor(TeamState t) {
    final eligible = _available.values
        .where((p) => !p.overseas || t.overseasCount < format.maxOverseas)
        .toList();
    if (eligible.isEmpty) return null;

    final squad = t.players;
    bool Function(Player) wanted;
    if (!squad.any((p) => p.isKeeper)) {
      wanted = (p) => p.isKeeper;
    } else if (squad.where((p) => p.isBowlingOption).length < 5) {
      wanted = (p) => p.isBowlingOption;
    } else if (squad.where((p) => p.batting >= 55).length < 6) {
      wanted = (p) => p.batting >= 55;
    } else {
      wanted = (_) => true;
    }
    final needed = eligible.where(wanted).toList();
    final pool = needed.isEmpty ? eligible : needed;
    pool.sort((a, b) {
      final byPrice = a.basePrice.compareTo(b.basePrice);
      if (byPrice != 0) return byPrice;
      final byImpact = a.impact.compareTo(b.impact);
      return byImpact != 0 ? byImpact : a.id.compareTo(b.id);
    });
    return pool.first;
  }
}

int _byImpactDesc(Player a, Player b) {
  final byImpact = b.impact.compareTo(a.impact);
  return byImpact != 0 ? byImpact : a.id.compareTo(b.id);
}

/// Players per tier when sets rotate (like the real auction's BA1, AL1, WK1…).
const tierSize = 8;

/// Players put up for auction, in order. Marquee first; then the role sets
/// rotate in quality tiers (best 8 batters, best 8 bowlers, …, then the next
/// 8 of each), so no role is left until every team's overseas slots are gone;
/// uncapped players last, also in tiers. Shuffled within each tier. With
/// [AuctionFormat.maxLots]: every marquee player plus the best of each other
/// set in proportion to its size.
List<Player> selectLots(
  List<Player> pool,
  AuctionFormat format,
  AuctionRules rules,
  Rng rng,
) {
  final bySet = <String, List<Player>>{};
  for (final p in pool) {
    bySet.putIfAbsent(p.set, () => []).add(p);
  }
  final setNames = [
    ...rules.setOrder.where(bySet.containsKey),
    ...bySet.keys.where((s) => !rules.setOrder.contains(s)),
  ];

  final maxLots = format.maxLots;
  final marqueeCount = bySet['marquee']?.length ?? 0;
  final rest = pool.length - marqueeCount;
  final budget = maxLots == null ? null : math.max(0, maxLots - marqueeCount);
  final chosen = <String, List<Player>>{};
  for (final s in setNames) {
    final players = [...bySet[s]!]..sort(_byImpactDesc);
    final quota = budget == null || s == 'marquee'
        ? players.length
        : (budget * players.length / math.max(1, rest)).round();
    chosen[s] = players.take(quota).toList();
  }

  final lots = <Player>[];
  void addTier(List<Player> tier) {
    final shuffled = [...tier];
    rng.shuffle(shuffled);
    lots.addAll(shuffled);
  }

  if (chosen.containsKey('marquee')) addTier(chosen['marquee']!);
  for (final group in [
    [for (final s in setNames) if (s != 'marquee' && s != 'uncapped') s],
    [if (chosen.containsKey('uncapped')) 'uncapped'],
  ]) {
    final longest = group.fold(0, (m, s) => math.max(m, chosen[s]!.length));
    for (var start = 0; start < longest; start += tierSize) {
      for (final s in group) {
        final players = chosen[s]!;
        if (start < players.length) {
          addTier(players.sublist(start, math.min(start + tierSize, players.length)));
        }
      }
    }
  }
  return lots;
}
