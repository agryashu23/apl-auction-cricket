import 'dart:math' as math;

import '../models.dart';

class Lineup {
  const Lineup(this.xi, this.bench);

  final List<Player> xi;
  final List<Player> bench;
}

const _batWeights = [1.0, 1.0, 0.95, 0.9, 0.85, 0.75, 0.6, 0.4, 0.25, 0.15, 0.1];
const _bowlWeights = [1.0, 1.0, 1.0, 1.0, 0.9, 0.3];

/// Weighted batting average of an XI, best batters counting most. 0–100.
double battingStrength(List<Player> xi) =>
    _weighted([for (final p in xi) p.batting + p.form], _batWeights);

/// Weighted average of the six best bowlers in an XI. 0–100.
double bowlingStrength(List<Player> xi) =>
    _weighted([for (final p in xi) p.bowling + p.form], _bowlWeights);

double _weighted(List<int> values, List<double> weights) {
  final sorted = [...values]..sort((a, b) => b.compareTo(a));
  var sum = 0.0;
  var total = 0.0;
  for (var i = 0; i < weights.length; i++) {
    sum += weights[i] * (i < sorted.length ? sorted[i].clamp(0, 100) : 0);
    total += weights[i];
  }
  return sum / total;
}

/// Picks the strongest valid XI: a keeper if the squad has one, at least five
/// bowling options, and no more than [maxOverseas] overseas players (unless
/// the squad doesn't have enough local players to make eleven).
Lineup pickBestXI(List<Player> squad, {int maxOverseas = 4}) {
  if (squad.length <= 11) return Lineup([...squad], const []);

  final locals = squad.where((p) => !p.overseas).length;
  final overseasCap = math.max(maxOverseas, 11 - locals);
  final squadHasKeeper = squad.any((p) => p.isKeeper);
  final optionsNeeded = math.min(5, squad.where((p) => p.isBowlingOption).length);

  bool valid(List<Player> xi) =>
      xi.where((p) => p.overseas).length <= overseasCap &&
      (!squadHasKeeper || xi.any((p) => p.isKeeper)) &&
      xi.where((p) => p.isBowlingOption).length >= optionsNeeded;

  double score(List<Player> xi) => battingStrength(xi) + bowlingStrength(xi);

  final xi = <Player>[];
  bool fits(Player p) =>
      !xi.contains(p) &&
      (!p.overseas || xi.where((x) => x.overseas).length < overseasCap);

  // Greedy start: keeper, then bowling options, then the best batters.
  final keepers = squad.where((p) => p.isKeeper).toList()
    ..sort((a, b) {
      final c = _keeperValue(b).compareTo(_keeperValue(a));
      return c != 0 ? c : a.id.compareTo(b.id);
    });
  if (keepers.isNotEmpty) xi.add(keepers.first);

  final bowlers = squad.where((p) => p.isBowlingOption).toList()
    ..sort((a, b) => _desc(a.bowling, b.bowling, a, b));
  for (final p in bowlers) {
    if (xi.where((x) => x.isBowlingOption).length >= optionsNeeded) break;
    if (fits(p)) xi.add(p);
  }

  final batters = [...squad]..sort((a, b) => _desc(a.batting, b.batting, a, b));
  for (final p in batters) {
    if (xi.length >= 11) break;
    if (fits(p)) xi.add(p);
  }
  for (final p in batters) {
    if (xi.length >= 11) break;
    if (!xi.contains(p)) xi.add(p); // only if locals ran out
  }

  // Improve with single swaps until none helps.
  var best = score(xi);
  for (var round = 0; round < 100; round++) {
    var improved = false;
    for (var i = 0; i < xi.length && !improved; i++) {
      for (final candidate in squad) {
        if (xi.contains(candidate)) continue;
        final trial = [...xi]..[i] = candidate;
        if (!valid(trial)) continue;
        final s = score(trial);
        if (s > best + 1e-9) {
          xi[i] = candidate;
          best = s;
          improved = true;
          break;
        }
      }
    }
    if (!improved) break;
  }

  final bench = squad.where((p) => !xi.contains(p)).toList()
    ..sort((a, b) => _desc(a.impact, b.impact, a, b));
  return Lineup(xi, bench);
}

double _keeperValue(Player p) => p.batting * 0.85 + p.keeping * 0.15;

int _desc(int x, int y, Player a, Player b) {
  final c = y.compareTo(x);
  return c != 0 ? c : a.id.compareTo(b.id);
}
