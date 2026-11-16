import 'dart:math' as math;

import '../models.dart';
import '../rng.dart';

enum MatchStage { league, qualifier1, eliminator, qualifier2, finalMatch }

enum Pitch { balanced, flat, spinFriendly, seamFriendly }

enum _Phase { powerplay, middle, death }

class BatterLine {
  BatterLine(this.player);

  final Player player;
  int runs = 0;
  int balls = 0;
  int fours = 0;
  int sixes = 0;
  bool batted = false;

  /// e.g. "c Rao b Khan". Null while not out.
  String? dismissal;

  bool get out => dismissal != null;
}

class BowlerLine {
  BowlerLine(this.player);

  final Player player;
  int balls = 0;
  int runs = 0;
  int wickets = 0;
  int dots = 0;
  int wides = 0;

  String get overs => formatOvers(balls);
  double get economy => balls == 0 ? 0 : runs * 6 / balls;
}

String formatOvers(int balls) =>
    balls % 6 == 0 ? '${balls ~/ 6}' : '${balls ~/ 6}.${balls % 6}';

class Innings {
  Innings(this.battingTeamId, this.bowlingTeamId, this.batting);

  final String battingTeamId;
  final String bowlingTeamId;

  /// Full batting order; [BatterLine.batted] marks who got in.
  final List<BatterLine> batting;
  final List<BowlerLine> bowling = [];
  int runs = 0;
  int wickets = 0;
  int legalBalls = 0;
  int extras = 0;

  bool get allOut => wickets >= batting.length - 1;
  String get score => allOut ? '$runs' : '$runs/$wickets';
  String get overs => formatOvers(legalBalls);
}

class MatchResult {
  const MatchResult({
    required this.number,
    required this.stage,
    required this.pitch,
    required this.tossWinnerId,
    required this.first,
    required this.second,
    required this.winnerId,
    required this.loserId,
    required this.margin,
    required this.superOver,
    required this.playerOfMatch,
  });

  final int number;
  final MatchStage stage;
  final Pitch pitch;
  final String tossWinnerId;
  final Innings first;
  final Innings second;
  final String winnerId;
  final String loserId;

  /// "by 23 runs", "by 6 wickets (8 balls left)", "in the Super Over".
  final String margin;
  final bool superOver;
  final Player playerOfMatch;
}

class TeamSheet {
  const TeamSheet(this.teamId, this.xi);

  final String teamId;
  final List<Player> xi;
}

/// Openers first (tagged, else best batters), then by batting skill.
List<Player> battingOrder(List<Player> xi) {
  final sorted = [...xi]..sort((a, b) => _desc(a.batting, b.batting, a, b));
  final openers = sorted.where((p) => p.hasTag('opener')).take(2).toList();
  for (final p in sorted) {
    if (openers.length >= 2) break;
    if (!openers.contains(p)) openers.add(p);
  }
  return [...openers, ...sorted.where((p) => !openers.contains(p))];
}

/// Who bowls each of the 20 overs. Death overs are planned first (best death
/// bowlers), then the powerplay (pace), then the middle (spin). Max 4 overs
/// each, never two in a row.
List<Player> planOvers(List<Player> xi) {
  final pool = ([...xi]..sort((a, b) => _desc(a.bowling, b.bowling, a, b)))
      .take(6)
      .toList();
  final quota = {for (final p in pool) p.id: 4};
  final phaseCount = <String, int>{};
  final plan = List<Player?>.filled(20, null);
  const order = [19, 18, 17, 16, 15, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14];

  for (final over in order) {
    final phase = _phaseOf(over);
    final phaseCap = switch (phase) {
      _Phase.death => 2,
      _Phase.powerplay => 3,
      _Phase.middle => 4,
    };
    bool adjacent(Player p) =>
        (over > 0 && plan[over - 1] == p) || (over < 19 && plan[over + 1] == p);

    Player? pick;
    // Strictest first; relax only if nobody fits.
    for (var strictness = 2; strictness >= 0 && pick == null; strictness--) {
      var best = -1e9;
      for (var i = 0; i < pool.length; i++) {
        final p = pool[i];
        if (quota[p.id]! <= 0) continue;
        if (strictness >= 1 && adjacent(p)) continue;
        if (strictness >= 2 && (phaseCount['${p.id}:${phase.name}'] ?? 0) >= phaseCap) {
          continue;
        }
        final score = p.bowling + _phaseBonus(p, phase) - (i >= 5 ? 8 : 0);
        if (score > best) {
          best = score.toDouble();
          pick = p;
        }
      }
    }
    plan[over] = pick!;
    quota[pick.id] = quota[pick.id]! - 1;
    final key = '${pick.id}:${phase.name}';
    phaseCount[key] = (phaseCount[key] ?? 0) + 1;
  }
  final overs = plan.cast<Player>();
  _fixBackToBack(overs);
  return overs;
}

/// Greedy planning can corner itself into giving someone consecutive overs;
/// swap one of them with an over elsewhere (same phase first) to fix it.
void _fixBackToBack(List<Player> plan) {
  bool clash(int i) =>
      (i > 0 && plan[i] == plan[i - 1]) || (i < plan.length - 1 && plan[i] == plan[i + 1]);

  for (var i = 1; i < plan.length; i++) {
    if (plan[i] != plan[i - 1]) continue;
    final candidates = [for (var j = 0; j < plan.length; j++) j]
      ..sort((a, b) {
        final samePhaseA = _phaseOf(a) == _phaseOf(i) ? 0 : 1;
        final samePhaseB = _phaseOf(b) == _phaseOf(i) ? 0 : 1;
        return samePhaseA != samePhaseB ? samePhaseA - samePhaseB : a - b;
      });
    for (final j in candidates) {
      if (plan[j] == plan[i]) continue;
      _swap(plan, i, j);
      if (!clash(i) && !clash(j)) break;
      _swap(plan, i, j);
    }
  }
}

void _swap(List<Player> plan, int i, int j) {
  final tmp = plan[i];
  plan[i] = plan[j];
  plan[j] = tmp;
}

_Phase _phaseOf(int over) => over < 6
    ? _Phase.powerplay
    : over < 15
        ? _Phase.middle
        : _Phase.death;

int _phaseBonus(Player p, _Phase phase) => switch (phase) {
      _Phase.powerplay => (p.bowlType == BowlType.pace ? 4 : -2) +
          (p.hasTag('powerplay') ? 6 : 0),
      _Phase.middle => p.bowlType == BowlType.spin ? 6 : 0,
      _Phase.death =>
        (p.bowlType == BowlType.pace ? 4 : -4) + (p.hasTag('death') ? 10 : 0),
    };

/// Ball outcomes: runs, or -1 for a wicket.
const _outcomes = [0, 1, 2, 3, 4, 6, -1];

/// Per-ball odds for evenly matched batter and bowler, before phase effects.
const _baseOdds = [0.305, 0.355, 0.07, 0.005, 0.138, 0.078, 0.049];
const _wideChance = 0.035;
const _legByeChance = 0.012;

class MatchSimulator {
  MatchSimulator(this._rng);

  final Rng _rng;

  MatchResult play(
    TeamSheet a,
    TeamSheet b, {
    required int number,
    required MatchStage stage,
  }) {
    final pitch = Pitch.values[_rng.pickWeighted([0.4, 0.25, 0.175, 0.175])];
    final tossWinner = _rng.chance(0.5) ? a : b;
    final tossLoser = identical(tossWinner, a) ? b : a;
    // Captains mostly prefer to chase.
    final batFirst = _rng.chance(0.4) ? tossWinner : tossLoser;
    final batSecond = identical(batFirst, a) ? b : a;

    final first = _innings(batFirst, batSecond, pitch);
    final second = _innings(batSecond, batFirst, pitch, target: first.runs + 1);

    final String winnerId;
    final String margin;
    var superOver = false;
    if (second.runs > first.runs) {
      winnerId = batSecond.teamId;
      final wicketsLeft = batSecond.xi.length - 1 - second.wickets;
      final ballsLeft = 120 - second.legalBalls;
      margin = 'by $wicketsLeft ${wicketsLeft == 1 ? 'wicket' : 'wickets'}'
          '${ballsLeft > 0 ? ' ($ballsLeft ${ballsLeft == 1 ? 'ball' : 'balls'} left)' : ''}';
    } else if (first.runs > second.runs) {
      winnerId = batFirst.teamId;
      final runs = first.runs - second.runs;
      margin = 'by $runs ${runs == 1 ? 'run' : 'runs'}';
    } else {
      winnerId = _superOver(batFirst, batSecond, pitch);
      margin = 'in the Super Over';
      superOver = true;
    }
    final winnerSheet = winnerId == a.teamId ? a : b;

    return MatchResult(
      number: number,
      stage: stage,
      pitch: pitch,
      tossWinnerId: tossWinner.teamId,
      first: first,
      second: second,
      winnerId: winnerId,
      loserId: winnerId == a.teamId ? b.teamId : a.teamId,
      margin: margin,
      superOver: superOver,
      playerOfMatch: _playerOfMatch(winnerSheet, [first, second]),
    );
  }

  Innings _innings(TeamSheet bat, TeamSheet bowl, Pitch pitch, {int? target}) {
    final order = battingOrder(bat.xi);
    final inn = Innings(bat.teamId, bowl.teamId, [for (final p in order) BatterLine(p)]);
    final plan = planOvers(bowl.xi);
    final bowlerLines = <String, BowlerLine>{};

    var striker = 0;
    var nonStriker = 1;
    var nextIn = 2;
    inn.batting[0].batted = true;
    inn.batting[1].batted = true;
    bool chased() => target != null && inn.runs >= target;

    innings:
    for (var over = 0; over < 20; over++) {
      final bowler = plan[over];
      final line = bowlerLines.putIfAbsent(bowler.id, () => BowlerLine(bowler));
      final phase = _phaseOf(over);
      var legal = 0;
      while (legal < 6) {
        if (_rng.chance(_wideChance)) {
          inn.runs++;
          inn.extras++;
          line.runs++;
          line.wides++;
          if (chased()) break innings;
          continue;
        }
        legal++;
        inn.legalBalls++;
        line.balls++;
        final batter = inn.batting[striker];
        batter.balls++;

        if (_rng.chance(_legByeChance)) {
          inn.runs++;
          inn.extras++;
          (striker, nonStriker) = (nonStriker, striker);
          if (chased()) break innings;
          continue;
        }

        final outcome = _ball(
          batter,
          bowler,
          phase,
          pitch,
          runsNeeded: target == null ? null : target - inn.runs,
          ballsLeft: 120 - inn.legalBalls + 1,
        );
        if (outcome < 0) {
          final (text, bowlerCredit) = _dismissal(bowler, bowl.xi);
          batter.dismissal = text;
          line.dots++;
          if (bowlerCredit) line.wickets++;
          inn.wickets++;
          if (inn.allOut) break innings;
          striker = nextIn++;
          inn.batting[striker].batted = true;
          continue;
        }

        batter.runs += outcome;
        inn.runs += outcome;
        line.runs += outcome;
        if (outcome == 0) line.dots++;
        if (outcome == 4) batter.fours++;
        if (outcome == 6) batter.sixes++;
        if (outcome.isOdd) (striker, nonStriker) = (nonStriker, striker);
        if (chased()) break innings;
      }
      (striker, nonStriker) = (nonStriker, striker);
    }
    inn.bowling.addAll(bowlerLines.values);
    return inn;
  }

  int _ball(
    BatterLine b,
    Player bowler,
    _Phase phase,
    Pitch pitch, {
    int? runsNeeded,
    int ballsLeft = 0,
  }) {
    final batter = b.player;
    var batEff = batter.batting + batter.form + (pitch == Pitch.flat ? 5 : 0);
    if (b.balls <= 6) {
      batEff -= 7; // not set yet
    } else if (b.balls > 30) {
      batEff += 3;
    }
    final bowlEff = bowler.bowling + bowler.form + _pitchBowling(pitch, bowler.bowlType);
    final d = ((batEff - bowlEff) / 100).clamp(-0.8, 0.8);

    final w = [..._baseOdds];
    w[0] *= _m(1 - 1.0 * d);
    w[2] *= _m(1 + 0.4 * d);
    w[4] *= _m(1 + 1.3 * d);
    w[5] *= _m(1 + 1.8 * d);
    w[6] *= _m(1 - 1.6 * d);

    switch (phase) {
      case _Phase.powerplay:
        w[0] *= 1.05;
        w[4] *= 1.25;
        w[5] *= 0.85;
        w[6] *= 0.95;
      case _Phase.middle:
        w[1] *= 1.15;
        w[4] *= 0.8;
        w[5] *= 0.95;
        w[6] *= 0.95;
      case _Phase.death:
        w[0] *= 0.8;
        w[2] *= 1.2;
        w[4] *= 1.1;
        w[5] *= 1.6;
        w[6] *= 1.45;
    }

    if (runsNeeded != null) {
      final requiredRate = runsNeeded * 6 / math.max(1, ballsLeft);
      final a = ((requiredRate - 9) / 6).clamp(-0.5, 1.0);
      w[0] *= _m(1 - 0.2 * a);
      w[4] *= _m(1 + 0.3 * a);
      w[5] *= _m(1 + 0.6 * a);
      w[6] *= _m(1 + 0.7 * a);
    }
    return _outcomes[_rng.pickWeighted(w)];
  }

  /// Dismissal text, and whether the bowler gets the wicket.
  (String, bool) _dismissal(Player bowler, List<Player> fielding) {
    final keeper = fielding.where((p) => p.isKeeper).firstOrNull;
    final r = _rng.nextDouble();
    if (bowler.bowlType == BowlType.spin && keeper != null && r < 0.05) {
      return ('st ${keeper.name} b ${bowler.name}', true);
    }
    if (r < 0.65) {
      final catcher = _fielder(fielding, keeper, bowler);
      if (catcher == bowler) return ('c & b ${bowler.name}', true);
      return ('c ${catcher.name} b ${bowler.name}', true);
    }
    if (r < 0.83) return ('b ${bowler.name}', true);
    if (r < 0.94) return ('lbw b ${bowler.name}', true);
    return ('run out (${_fielder(fielding, keeper, bowler).name})', false);
  }

  Player _fielder(List<Player> fielding, Player? keeper, Player bowler) {
    final weights = [
      for (final p in fielding)
        p.fielding.toDouble() *
            (p == keeper && bowler.bowlType == BowlType.pace ? 3 : 1),
    ];
    return fielding[_rng.pickWeighted(weights)];
  }

  String _superOver(TeamSheet first, TeamSheet second, Pitch pitch) {
    for (var attempt = 0; attempt < 3; attempt++) {
      final r1 = _superOverInnings(first, second, pitch);
      final r2 = _superOverInnings(second, first, pitch, target: r1 + 1);
      if (r2 > r1) return second.teamId;
      if (r1 > r2) return first.teamId;
    }
    return _rng.chance(0.5) ? first.teamId : second.teamId;
  }

  /// Six balls, two wickets, best three batters against the best death bowler.
  int _superOverInnings(TeamSheet bat, TeamSheet bowl, Pitch pitch, {int? target}) {
    final batters = [
      for (final p in ([...bat.xi]..sort((a, b) => _desc(a.batting, b.batting, a, b)))
          .take(3))
        // Treated as already set: no slow start in a one-over shootout.
        BatterLine(p)..balls = 10,
    ];
    final bowler = ([...bowl.xi]
          ..sort((a, b) => _desc(
                a.bowling + _phaseBonus(a, _Phase.death),
                b.bowling + _phaseBonus(b, _Phase.death),
                a,
                b,
              )))
        .first;

    var runs = 0;
    var wickets = 0;
    var striker = 0;
    var nonStriker = 1;
    var legal = 0;
    while (legal < 6 && wickets < 2 && (target == null || runs < target)) {
      if (_rng.chance(_wideChance)) {
        runs++;
        continue;
      }
      legal++;
      final outcome = _ball(
        batters[striker],
        bowler,
        _Phase.death,
        pitch,
        runsNeeded: target == null ? null : target - runs,
        ballsLeft: 7 - legal,
      );
      if (outcome < 0) {
        wickets++;
        striker = 2;
        continue;
      }
      runs += outcome;
      if (outcome.isOdd) (striker, nonStriker) = (nonStriker, striker);
    }
    return runs;
  }

  Player _playerOfMatch(TeamSheet winner, List<Innings> innings) {
    final points = <Player, double>{};
    for (final inn in innings) {
      for (final b in inn.batting.where((b) => b.batted)) {
        if (inn.battingTeamId != winner.teamId) continue;
        points[b.player] = (points[b.player] ?? 0) + battingPoints(b);
      }
      for (final w in inn.bowling) {
        if (inn.bowlingTeamId != winner.teamId) continue;
        points[w.player] = (points[w.player] ?? 0) + bowlingPoints(w);
      }
    }
    final ranked = points.entries.toList()
      ..sort((x, y) {
        final c = y.value.compareTo(x.value);
        return c != 0 ? c : x.key.id.compareTo(y.key.id);
      });
    return ranked.isEmpty ? winner.xi.first : ranked.first.key;
  }
}

/// Match-impact points, used for Player of the Match and season MVP.
double battingPoints(BatterLine b) =>
    b.runs +
    b.fours +
    2.0 * b.sixes +
    (b.runs >= 50 ? 10 : 0) +
    (b.runs >= 100 ? 15 : 0);

double bowlingPoints(BowlerLine w) {
  var points = 25.0 * w.wickets + w.dots;
  if (w.balls >= 12) points += (8 - w.economy) * 3;
  return points;
}

double _m(double x) => x.clamp(0.15, 3.0);

int _pitchBowling(Pitch pitch, BowlType type) => switch (pitch) {
      Pitch.spinFriendly => type == BowlType.spin ? 6 : -2,
      Pitch.seamFriendly => type == BowlType.pace ? 6 : -2,
      _ => 0,
    };

int _desc(int x, int y, Player a, Player b) {
  final c = y.compareTo(x);
  return c != 0 ? c : a.id.compareTo(b.id);
}
