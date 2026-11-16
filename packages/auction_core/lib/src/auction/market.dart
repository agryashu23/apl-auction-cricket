import 'dart:math' as math;

import '../models.dart';
import 'engine.dart';

/// The squad shape a team aims for: a little above the minimum, split by role.
class SquadTargets {
  const SquadTargets({
    required this.size,
    required this.keepers,
    required this.batters,
    required this.allRounders,
    required this.bowlers,
  });

  final int size;
  final int keepers;
  final int batters;
  final int allRounders;
  final int bowlers;

  int forRole(Role role) => switch (role) {
        Role.wicketKeeper => keepers,
        Role.batter => batters,
        Role.allRounder => allRounders,
        Role.bowler => bowlers,
      };
}

SquadTargets squadTargets(AuctionFormat format) {
  final size = math.min(format.maxSquad, format.minSquad + 2);
  final keepers = size <= 13 ? 1 : 2;
  final batters = (size * 0.30).round();
  final allRounders = (size * 0.25).round();
  return SquadTargets(
    size: size,
    keepers: keepers,
    batters: batters,
    allRounders: allRounders,
    bowlers: size - keepers - batters - allRounders,
  );
}

/// How much this team wants this player compared with an average buyer:
/// above 1 fills a gap, below 1 is a luxury, 0 means it can't use them.
double needMultiplier(TeamState team, Player player, AuctionFormat format) {
  if (team.size >= format.maxSquad) return 0;
  final targets = squadTargets(format);

  final have = team.countRole(player.role);
  final want = targets.forRole(player.role);
  double m;
  if (have < want) {
    m = 1.0 + 0.6 * (want - have) / want;
    if (have == 0 && player.role == Role.wicketKeeper) m = 1.9;
  } else {
    m = team.size >= targets.size ? 0.25 : 0.45;
  }

  if (player.role == Role.bowler || player.role == Role.allRounder) {
    final sameType = team.players
        .where((p) => p.bowlType == player.bowlType && p.isBowlingOption)
        .length;
    if (player.bowlType == BowlType.spin && sameType == 0) m *= 1.15;
    if (player.bowlType == BowlType.pace && sameType < 2) m *= 1.1;
  }

  if (player.overseas) {
    final slotsLeft = format.maxOverseas - team.overseasCount;
    if (slotsLeft <= 0) return 0;
    if (slotsLeft == 1 && player.impact < 82) m *= 0.7;
  }
  return m;
}

/// Most a player's star power can add to his price (100 → +80%).
const starPremium = 0.8;

/// AI teams never bid more than this multiple of a valued player's worth.
const maxBarFactor = 1.35;

/// What a player is "worth" in lakhs before market conditions, scaled to the
/// format's purse and never below base price: his market [Player.value] when
/// the dataset has one, otherwise from skills (rising steeply with quality,
/// plus a premium for star power).
double fairPrice(Player player, AuctionFormat format) {
  if (player.value > 0) {
    return math.max(player.basePrice.toDouble(), player.value * format.purse / 12000);
  }
  final above = math.max(0, player.impact - 55);
  // Absolute scale doesn't matter much (marketInflation rescales to the money
  // in the room); the exponent sets how much pricier stars are than regulars.
  // x^1.75 as x·√x·√√x: sqrt is exactly rounded everywhere, pow isn't, and
  // the C# client must compute the same prices bit for bit.
  final a = above.toDouble();
  final raw = 2.5 * (a * math.sqrt(a) * math.sqrt(math.sqrt(a))) * format.purse / 12000;
  final withStar = raw * (1 + starPremium * player.starPower / 100);
  return math.max(player.basePrice.toDouble(), withStar);
}

final _inflationCache = Expando<(int, double)>();

/// Above 1 when teams hold more money than the players left are worth (prices
/// rise), below 1 when money is tight. Fixed for the duration of a lot.
double marketInflation(AuctionEngine engine) {
  final lotNumber = engine.currentLot?.number ?? -1;
  final cached = _inflationCache[engine];
  if (cached != null && cached.$1 == lotNumber && lotNumber != -1) return cached.$2;
  final value = _computeInflation(engine);
  _inflationCache[engine] = (lotNumber, value);
  return value;
}

double _computeInflation(AuctionEngine engine) {
  final targetSize = squadTargets(engine.format).size;
  var slots = 0;
  var money = 0;
  for (final t in engine.teams.values) {
    slots += math.max(0, targetSize - t.size);
    money += t.purse;
  }
  if (slots == 0) return 1.0;

  final lot = engine.currentLot;
  final values = [
    if (lot != null) fairPrice(lot.player, engine.format),
    for (final p in engine.upcoming) fairPrice(p, engine.format),
  ]..sort((a, b) => b.compareTo(a));
  final supply = values.take(slots).fold(0.0, (s, v) => s + v);
  if (supply <= 0) return 1.0;
  // Market values are real prices already: let the room's money nudge them,
  // not rescale them.
  final anchored = (lot?.player.value ?? 0) > 0;
  return (money * 0.9 / supply).clamp(anchored ? 0.75 : 0.5, anchored ? 1.15 : 2.5);
}

/// The most any AI team will pay for [player]: [maxBarFactor] times his
/// worth for a valued player, and never past the price [cap].
double maxBar(Player player, AuctionFormat format, int? cap) {
  final bar = player.value > 0 ? fairPrice(player, format) * maxBarFactor : double.infinity;
  return cap == null ? bar : math.min(bar, cap.toDouble());
}
