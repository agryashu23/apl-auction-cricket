import 'dart:math' as math;

import '../auction/engine.dart';
import '../models.dart';
import 'lineup.dart';

class SquadRating {
  SquadRating({
    required this.teamId,
    required this.lineup,
    required this.batting,
    required this.bowling,
    required this.balance,
    required this.depth,
    required this.value,
    required this.overall,
  });

  final String teamId;
  final Lineup lineup;

  /// Each 0–100.
  final int batting;
  final int bowling;
  final int balance;
  final int depth;
  final int value;
  final int overall;

  /// 1 = best squad in the league.
  int rank = 0;

  String get grade => gradeFor(overall);
}

String gradeFor(int overall) {
  if (overall >= 85) return 'A+';
  if (overall >= 80) return 'A';
  if (overall >= 75) return 'B+';
  if (overall >= 70) return 'B';
  if (overall >= 65) return 'C+';
  if (overall >= 60) return 'C';
  return 'D';
}

/// Raw strength (an XI's weighted skill average) to a 0–100 score. Bowling
/// only counts the top six, so its raw numbers run higher than batting's.
int _scaled(double raw, double base, double spread) =>
    ((raw - base) * 100 / spread).round().clamp(0, 100);

/// Rates every squad. "Value" compares strength against money spent, so it
/// needs the whole league. [benchSlots] is how many bench players the format
/// gives room for (minSquad - 11, max 4); with none, depth isn't scored.
List<SquadRating> rateSquads(
  Iterable<TeamState> teams, {
  required int maxOverseasInXI,
  required int benchSlots,
  RatingScale scale = const RatingScale(),
}) {
  final slots = benchSlots.clamp(0, 4);
  final list = teams.toList();
  final lineups = [
    for (final t in list) pickBestXI(t.players, maxOverseas: maxOverseasInXI),
  ];

  final strengths = [
    for (final l in lineups) (battingStrength(l.xi) + bowlingStrength(l.xi)) / 2,
  ];
  final spends = [for (final t in list) t.spent.toDouble()];
  final strengthZ = _zScores(strengths);
  final spendZ = _zScores(spends);

  final ratings = <SquadRating>[];
  for (var i = 0; i < list.length; i++) {
    final xi = lineups[i].xi;
    final bench = lineups[i].bench;

    final batting = _scaled(battingStrength(xi), scale.battingBase, scale.spread);
    final bowling =
        (_scaled(bowlingStrength(xi), scale.bowlingBase, scale.spread) + _varietyBonus(xi))
            .clamp(0, 100);
    final balance = _balance(xi, list[i].players);

    var depth = 0;
    if (slots > 0) {
      final benchTop = [for (final p in bench.take(slots)) p.impact.toDouble()];
      while (benchTop.length < slots) {
        benchTop.add(35);
      }
      final benchAvg = benchTop.reduce((a, b) => a + b) / slots;
      depth = ((benchAvg - 45) * 100 / 40).round().clamp(0, 100);
    }

    final value = (50 + 20 * (strengthZ[i] - spendZ[i])).round().clamp(0, 100);

    final depthWeight = slots > 0 ? 0.10 : 0.0;
    final coreWeight = (1 - 0.12 - 0.06 - depthWeight) / 2;
    final overall = (coreWeight * batting +
            coreWeight * bowling +
            0.12 * balance +
            depthWeight * depth +
            0.06 * value)
        .round();

    ratings.add(SquadRating(
      teamId: list[i].id,
      lineup: lineups[i],
      batting: batting,
      bowling: bowling,
      balance: balance,
      depth: depth,
      value: value,
      overall: overall,
    ));
  }

  final ranked = [...ratings]..sort((a, b) {
      final byOverall = b.overall.compareTo(a.overall);
      if (byOverall != 0) return byOverall;
      final byCore = (b.batting + b.bowling).compareTo(a.batting + a.bowling);
      return byCore != 0 ? byCore : a.teamId.compareTo(b.teamId);
    });
  for (var i = 0; i < ranked.length; i++) {
    ranked[i].rank = i + 1;
  }
  return ratings;
}

/// Spin and pace both covered among the main bowlers.
int _varietyBonus(List<Player> xi) {
  final bowlers = xi.where((p) => p.isBowlingOption).toList()
    ..sort((a, b) {
      final c = b.bowling.compareTo(a.bowling);
      return c != 0 ? c : a.id.compareTo(b.id);
    });
  final top = bowlers.take(5);
  final spinners = top.where((p) => p.bowlType == BowlType.spin).length;
  final pacers = top.where((p) => p.bowlType == BowlType.pace).length;
  var bonus = 0;
  bonus += spinners >= 1 ? 2 : -4;
  bonus += pacers >= 2 ? 2 : -4;
  return bonus;
}

/// How well the squad is put together, 0–100: points off for each gap.
/// Keepers, left-handers and size count across the whole squad (a season
/// needs a spare keeper); bowling options and batters in the playing XI.
int _balance(List<Player> xi, List<Player> squad) {
  var score = 100;
  final keepers = squad.where((p) => p.isKeeper).length;
  if (keepers == 0) score -= 25;
  if (keepers == 1) score -= 10;
  final options = xi.where((p) => p.isBowlingOption).length;
  if (options < 5) score -= 15 * (5 - options);
  final realBatters = xi.where((p) => p.batting >= 55).length;
  if (realBatters < 6) score -= 8 * (6 - realBatters);
  if (!squad.any((p) => p.leftHanded)) score -= 4;
  if (squad.length < 18) score -= 10 * (18 - squad.length);
  return score.clamp(0, 100);
}

List<double> _zScores(List<double> xs) {
  if (xs.isEmpty) return const [];
  final mean = xs.reduce((a, b) => a + b) / xs.length;
  final variance = xs.fold(0.0, (s, x) => s + (x - mean) * (x - mean)) / xs.length;
  final sd = math.sqrt(variance);
  if (sd < 1e-9) return [for (final _ in xs) 0.0];
  return [for (final x in xs) (x - mean) / sd];
}
