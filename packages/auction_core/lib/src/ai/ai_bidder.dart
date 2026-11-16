import 'dart:math' as math;

import '../auction/engine.dart';
import '../auction/market.dart';
import '../models.dart';
import '../rng.dart';

enum Personality {
  /// Pays up for stars, bids fast, sometimes goes one bid past its limit.
  aggressive(valuation: 0.98, starValuation: 1.1, starFactor: 4.5, maxShare: 0.25, noise: 0.10, speed: 0.7),

  balanced(valuation: 1.0, starValuation: 1.0, starFactor: 4.0, maxShare: 0.22, noise: 0.10, speed: 1.0),

  /// Hunts bargains, rarely splashes out, takes its time.
  valueHunter(valuation: 0.96, starValuation: 0.95, starFactor: 3.3, maxShare: 0.18, noise: 0.10, speed: 1.3);

  const Personality({
    required this.valuation,
    required this.starValuation,
    required this.starFactor,
    required this.maxShare,
    required this.noise,
    required this.speed,
  });

  /// Never spends more than this share of the starting purse on one player.
  final double maxShare;

  /// Multiplies the player's market value.
  final double valuation;

  /// Used instead of [valuation] for stars (impact >= [starImpact]).
  final double starValuation;

  static const starImpact = 82;

  /// Most it will spend on one player, as a multiple of its budget per
  /// remaining slot.
  final double starFactor;

  /// ± random spread on each valuation, so auctions don't repeat.
  final double noise;

  /// Multiplies thinking time before a bid.
  final double speed;
}

class AiDecision {
  const AiDecision.pass()
      : bid = false,
        amount = 0,
        delayMs = 0;

  const AiDecision.bid(this.amount, this.delayMs) : bid = true;

  final bool bid;
  final int amount;

  /// How long to "think" before bidding. The live driver waits this long
  /// (and drops the bid if the lot changed meanwhile).
  final int delayMs;
}

class AiBidder {
  AiBidder({required this.teamId, required this.personality, required int seed})
      : _rng = Rng(seed);

  final String teamId;
  final Personality personality;
  final Rng _rng;

  int _limitLot = -1;
  int _limit = 0;
  double _bar = 0;

  /// Highest price this team will pay for the open lot. Worked out once when
  /// the lot opens, so the AI doesn't change its mind mid-lot.
  int limitFor(AuctionEngine engine) {
    final lot = engine.currentLot!;
    if (lot.number != _limitLot) {
      _limitLot = lot.number;
      _bar = maxBar(lot.player, engine.format, engine.maxPrice);
      _limit = _computeLimit(engine, lot);
    }
    return _limit;
  }

  int _computeLimit(AuctionEngine engine, Lot lot) {
    final team = engine.teams[teamId]!;
    final player = lot.player;
    final need = needMultiplier(team, player, engine.format);
    if (need <= 0) return 0;

    final noise = 1 + personality.noise * (2 * _rng.nextDouble() - 1);
    // A valued player's price is mostly the market's; squad need only sways it.
    final want = player.value > 0 ? 0.55 + 0.3 * need : need;
    final value = fairPrice(player, engine.format) *
        marketInflation(engine) *
        want *
        // Real prices already carry the star premium.
        (player.impact >= Personality.starImpact && player.value == 0
            ? personality.starValuation
            : personality.valuation) *
        noise;

    // Pace spending over the slots it can realistically still win.
    final targets = squadTargets(engine.format);
    final lotsPerTeam = (engine.lotsRemaining / engine.teams.length).ceil() + 1;
    final paceSlots = math.max(1, math.min(targets.size - team.size, lotsPerTeam));
    final spendable = team.purse -
        math.max(0, engine.format.minSquad - team.size) * engine.rules.minBasePrice;
    final cap = spendable / paceSlots * personality.starFactor;
    final shareCap = engine.format.purse * personality.maxShare;

    final limit = [value, cap, shareCap, _bar, engine.maxBidFor(team).toDouble()]
        .reduce(math.min);
    return limit.floor();
  }

  AiDecision decide(AuctionEngine engine) {
    final lot = engine.currentLot;
    if (lot == null || engine.checkBid(teamId) != null) {
      return const AiDecision.pass();
    }
    final next = engine.nextBidAmount;
    final limit = limitFor(engine);

    var willBid = next <= limit;
    if (!willBid &&
        personality == Personality.aggressive &&
        lot.leaderId != null &&
        next <= limit * 1.1 &&
        next <= _bar &&
        _rng.chance(0.25)) {
      willBid = true; // caught up in a bidding war
    }
    if (!willBid) return const AiDecision.pass();

    final nearLimit = next > limit * 0.85;
    final thinkMs = nearLimit ? _rng.range(2500, 6500) : _rng.range(600, 2400);
    final maxDelay = engine.rules.bidTimerSec * 1000 - 700;
    final delay = math.min((thinkMs * personality.speed).round(), maxDelay);
    return AiDecision.bid(next, delay);
  }

  /// Whether to spend the RTM card on this offer: only for a player worth
  /// the card, only at a price it would have paid (a little more for a
  /// former player it knows), and not while a better former player is still
  /// to come up.
  bool wantsRtm(AuctionEngine engine, RtmOffer offer) {
    final player = engine.currentLot!.player;
    if (player.impact < 74 && player.starPower < 40) return false;
    final value = retentionValue(player);
    final betterLater = engine.upcoming
        .any((p) => p.previousTeam == teamId && retentionValue(p) > value + 3);
    if (betterLater) return false;
    return offer.price <= limitFor(engine) * 1.15;
  }
}

/// Settles the open lot between AI teams only: used by the headless sim and
/// when a human passes. Like the live auction, the quickest willing bidder
/// gets each raise. Returns null when the lot is waiting on a human's RTM
/// decision; the caller then asks them and calls `closeLot(useRtm: …)`.
LotResult? resolveLotWithAi(AuctionEngine engine, Iterable<AiBidder> bidders) {
  while (true) {
    AiBidder? fastest;
    AiDecision? decision;
    for (final b in bidders) {
      final d = b.decide(engine);
      if (d.bid && (decision == null || d.delayMs < decision.delayMs)) {
        fastest = b;
        decision = d;
      }
    }
    if (fastest == null) return settleRtm(engine, bidders);
    final rejection = engine.placeBid(fastest.teamId, decision!.amount);
    if (rejection != null) {
      throw StateError('AI ${fastest.teamId} made an invalid bid: $rejection');
    }
  }
}

/// Ends bidding and, if an AI team holds the RTM offer, lets it decide.
/// Returns null if the offer belongs to a human.
LotResult? settleRtm(AuctionEngine engine, Iterable<AiBidder> bidders) {
  final offer = engine.endBidding();
  if (offer == null) return engine.closeLot();
  final holder = bidders.where((b) => b.teamId == offer.teamId).firstOrNull;
  if (holder == null) return null;
  return engine.closeLot(useRtm: holder.wantsRtm(engine, offer));
}

/// Runs the rest of the auction with AI deciding every lot. Human RTM
/// offers are declined (a headless run has nobody to ask).
void runAuctionWithAi(AuctionEngine engine, Iterable<AiBidder> bidders) {
  void lot() => resolveLotWithAi(engine, bidders) ?? engine.closeLot();
  if (engine.currentLot != null) lot();
  while (engine.openNextLot() != null) {
    lot();
  }
}

/// Which of last season's players an AI team keeps in retention mode: the
/// most valuable ones, as long as their market value justifies the slab.
List<Player> chooseAiRetentions(
  String teamId,
  List<Player> pool,
  AuctionRules rules,
  AuctionFormat format,
) {
  final candidates = pool.where((p) => p.previousTeam == teamId).toList()
    ..sort(byRetentionValue);
  final kept = <Player>[];
  for (final p in candidates) {
    if (kept.length >= rules.retentionFor(format).maxPlayers) break;
    final trial = [...kept, p];
    if (retentionProblem(teamId, trial, rules, format) != null) continue;
    final price = retentionPrices(trial, rules, format)[p]!;
    // Skill-based prices run ~1.5x "fair" when the room is flush with money;
    // market values are real prices already.
    if (fairPrice(p, format) * (p.value > 0 ? 1.1 : 1.5) >= price) kept.add(p);
  }
  return kept;
}

/// One AI per franchise (except [humanTeamIds]) with mixed personalities.
List<AiBidder> createAiBidders(
  AuctionEngine engine, {
  required int seed,
  Set<String> humanTeamIds = const {},
}) {
  final rng = Rng(seed);
  final personalities = Personality.values;
  final bidders = <AiBidder>[];
  var i = rng.nextInt(personalities.length);
  for (final id in engine.teams.keys) {
    if (humanTeamIds.contains(id)) continue;
    bidders.add(AiBidder(
      teamId: id,
      personality: personalities[i++ % personalities.length],
      seed: rng.nextUint32(),
    ));
  }
  return bidders;
}
