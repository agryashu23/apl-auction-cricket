import '../models.dart';
import '../rating/lineup.dart';
import '../rng.dart';
import 'match.dart';

class TableRow {
  TableRow(this.teamId);

  final String teamId;
  int played = 0;
  int won = 0;
  int lost = 0;
  int points = 0;
  int runsFor = 0;
  int ballsFaced = 0;
  int runsAgainst = 0;
  int ballsBowled = 0;

  double get netRunRate => ballsFaced == 0 || ballsBowled == 0
      ? 0
      : runsFor * 6 / ballsFaced - runsAgainst * 6 / ballsBowled;
}

class PlayerSeasonStats {
  PlayerSeasonStats(this.player, this.teamId);

  final Player player;
  final String teamId;
  int matches = 0;
  int innings = 0;
  int notOuts = 0;
  int runs = 0;
  int balls = 0;
  int fours = 0;
  int sixes = 0;
  int fifties = 0;
  int hundreds = 0;
  int highScore = 0;
  int ballsBowled = 0;
  int runsConceded = 0;
  int wickets = 0;
  double mvpPoints = 0;

  double get strikeRate => balls == 0 ? 0 : runs * 100 / balls;
  double get economy => ballsBowled == 0 ? 0 : runsConceded * 6 / ballsBowled;
}

class SeasonResult {
  const SeasonResult({
    required this.leagueMatches,
    required this.playoffs,
    required this.table,
    required this.championId,
    required this.runnerUpId,
    required this.playerStats,
  });

  final List<MatchResult> leagueMatches;
  final List<MatchResult> playoffs;

  /// Final league standings, top first.
  final List<TableRow> table;
  final String championId;
  final String runnerUpId;
  final Map<String, PlayerSeasonStats> playerStats;

  List<MatchResult> get allMatches => [...leagueMatches, ...playoffs];

  PlayerSeasonStats get topRunScorer => _best((a, b) {
        final c = b.runs.compareTo(a.runs);
        return c != 0 ? c : b.strikeRate.compareTo(a.strikeRate);
      });

  PlayerSeasonStats get topWicketTaker => _best((a, b) {
        final c = b.wickets.compareTo(a.wickets);
        return c != 0 ? c : a.economy.compareTo(b.economy);
      });

  PlayerSeasonStats get mvp => _best((a, b) => b.mvpPoints.compareTo(a.mvpPoints));

  PlayerSeasonStats _best(int Function(PlayerSeasonStats, PlayerSeasonStats) compare) {
    final list = playerStats.values.toList()
      ..sort((a, b) {
        final c = compare(a, b);
        return c != 0 ? c : a.player.id.compareTo(b.player.id);
      });
    return list.first;
  }
}

/// Plays a full season from the squads: single round-robin, then
/// Qualifier 1 (1v2), Eliminator (3v4), Qualifier 2, Final. The same
/// [seed] and squads always give the same season.
SeasonResult simulateSeason({
  required Map<String, List<Player>> squads,
  required int seed,
  int maxOverseasInXI = 4,
}) {
  if (squads.length < 4) throw ArgumentError('A season needs at least 4 teams');
  final rng = Rng(seed);
  final sim = MatchSimulator(rng);
  final sheets = {
    for (final e in squads.entries)
      e.key: TeamSheet(e.key, pickBestXI(e.value, maxOverseas: maxOverseasInXI).xi),
  };
  final table = {for (final id in squads.keys) id: TableRow(id)};
  final stats = <String, PlayerSeasonStats>{};
  var matchNo = 0;

  MatchResult play(String a, String b, MatchStage stage) {
    final result = sim.play(sheets[a]!, sheets[b]!, number: ++matchNo, stage: stage);
    _addPlayerStats(result, sheets, stats);
    return result;
  }

  final fixtures = _roundRobin(squads.keys.toList());
  rng.shuffle(fixtures);
  final league = <MatchResult>[];
  for (final (a, b) in fixtures) {
    final result = play(a, b, MatchStage.league);
    league.add(result);
    _addToTable(result, table);
  }

  final standings = table.values.toList()
    ..sort((x, y) {
      var c = y.points.compareTo(x.points);
      if (c != 0) return c;
      c = y.netRunRate.compareTo(x.netRunRate);
      if (c != 0) return c;
      c = y.won.compareTo(x.won);
      return c != 0 ? c : x.teamId.compareTo(y.teamId);
    });

  final ids = [for (final r in standings.take(4)) r.teamId];
  final q1 = play(ids[0], ids[1], MatchStage.qualifier1);
  final elim = play(ids[2], ids[3], MatchStage.eliminator);
  final q2 = play(q1.loserId, elim.winnerId, MatchStage.qualifier2);
  final finalMatch = play(q1.winnerId, q2.winnerId, MatchStage.finalMatch);

  return SeasonResult(
    leagueMatches: league,
    playoffs: [q1, elim, q2, finalMatch],
    table: standings,
    championId: finalMatch.winnerId,
    runnerUpId: finalMatch.loserId,
    playerStats: stats,
  );
}

/// Every pair once (circle method), so each round nobody plays twice.
List<(String, String)> _roundRobin(List<String> teams) {
  final list = [...teams];
  if (list.length.isOdd) list.add('');
  final n = list.length;
  final pairs = <(String, String)>[];
  for (var round = 0; round < n - 1; round++) {
    for (var i = 0; i < n ~/ 2; i++) {
      final a = list[i];
      final b = list[n - 1 - i];
      if (a.isNotEmpty && b.isNotEmpty) pairs.add(round.isEven ? (a, b) : (b, a));
    }
    list.insert(1, list.removeLast());
  }
  return pairs;
}

void _addToTable(MatchResult m, Map<String, TableRow> table) {
  for (final inn in [m.first, m.second]) {
    // An all-out side is charged the full 20 overs, as in real NRR.
    final balls = inn.allOut ? 120 : inn.legalBalls;
    final bat = table[inn.battingTeamId]!;
    final bowl = table[inn.bowlingTeamId]!;
    bat.runsFor += inn.runs;
    bat.ballsFaced += balls;
    bowl.runsAgainst += inn.runs;
    bowl.ballsBowled += balls;
  }
  final winner = table[m.winnerId]!;
  final loser = table[m.loserId]!;
  winner
    ..played += 1
    ..won += 1
    ..points += 2;
  loser
    ..played += 1
    ..lost += 1;
}

void _addPlayerStats(
  MatchResult m,
  Map<String, TeamSheet> sheets,
  Map<String, PlayerSeasonStats> stats,
) {
  PlayerSeasonStats of(Player p, String teamId) =>
      stats.putIfAbsent(p.id, () => PlayerSeasonStats(p, teamId));

  for (final teamId in [m.first.battingTeamId, m.second.battingTeamId]) {
    for (final p in sheets[teamId]!.xi) {
      of(p, teamId).matches++;
    }
  }
  for (final inn in [m.first, m.second]) {
    for (final b in inn.batting.where((b) => b.batted)) {
      final s = of(b.player, inn.battingTeamId)
        ..innings += 1
        ..runs += b.runs
        ..balls += b.balls
        ..fours += b.fours
        ..sixes += b.sixes
        ..mvpPoints += battingPoints(b);
      if (!b.out) s.notOuts++;
      if (b.runs >= 100) {
        s.hundreds++;
      } else if (b.runs >= 50) {
        s.fifties++;
      }
      if (b.runs > s.highScore) s.highScore = b.runs;
    }
    for (final w in inn.bowling) {
      of(w.player, inn.bowlingTeamId)
        ..ballsBowled += w.balls
        ..runsConceded += w.runs
        ..wickets += w.wickets
        ..mvpPoints += bowlingPoints(w);
    }
  }
}
