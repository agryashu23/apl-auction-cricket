import 'package:auction_core/auction_core.dart';
import 'package:test/test.dart';

import 'helpers.dart';

Map<String, List<Player>> _squadsAfterAuction(int seed, {String format = 'mega'}) {
  final e = newEngine(format: format, seed: seed);
  runAuctionWithAi(e, createAiBidders(e, seed: seed));
  return {for (final t in e.teams.values) t.id: t.players};
}

void main() {
  group('best XI', () {
    test('respects keeper, bowling options and overseas cap', () {
      for (var seed = 1; seed <= 10; seed++) {
        for (final squad in _squadsAfterAuction(seed).values) {
          final xi = pickBestXI(squad, maxOverseas: 4).xi;
          expect(xi, hasLength(11));
          expect(xi.toSet(), hasLength(11));
          expect(xi.where((p) => p.overseas).length, lessThanOrEqualTo(4));
          if (squad.any((p) => p.isKeeper)) {
            expect(xi.any((p) => p.isKeeper), isTrue);
          }
          expect(xi.where((p) => p.isBowlingOption).length, greaterThanOrEqualTo(5));
        }
      }
    });

    test('squad of exactly 11 plays everyone', () {
      final squad = [for (var i = 0; i < 11; i++) testPlayer('t$i')];
      expect(pickBestXI(squad).xi, hasLength(11));
    });
  });

  group('squad rating', () {
    test('scores are in range and ranks are 1..n', () {
      final e = newEngine(seed: 3);
      runAuctionWithAi(e, createAiBidders(e, seed: 3));
      final ratings = rateSquads(e.teams.values,
          maxOverseasInXI: 4, benchSlots: e.format.minSquad - 11);
      expect(ratings.map((r) => r.rank).toSet(), {for (var i = 1; i <= 10; i++) i});
      for (final r in ratings) {
        for (final v in [r.batting, r.bowling, r.balance, r.depth, r.value, r.overall]) {
          expect(v, inInclusiveRange(0, 100));
        }
        expect(['A+', 'A', 'B+', 'B', 'C+', 'C', 'D'], contains(r.grade));
      }
    });
  });

  group('overs plan', () {
    test('20 overs, max 4 each, never two in a row', () {
      final squads = [
        for (var seed = 1; seed <= 15; seed++)
          for (final format in ['quick', 'mega'])
            ..._squadsAfterAuction(seed, format: format).values,
      ];
      for (final squad in squads) {
        final plan = planOvers(pickBestXI(squad).xi);
        expect(plan, hasLength(20));
        final counts = <String, int>{};
        for (var i = 0; i < 20; i++) {
          counts[plan[i].id] = (counts[plan[i].id] ?? 0) + 1;
          if (i > 0) expect(plan[i], isNot(plan[i - 1]), reason: 'over $i');
        }
        expect(counts.values.every((c) => c <= 4), isTrue);
      }
    });
  });

  group('season', () {
    late SeasonResult season;
    late Map<String, List<Player>> squads;
    setUpAll(() {
      squads = _squadsAfterAuction(7);
      season = simulateSeason(squads: squads, seed: 99);
    });

    test('round-robin: 45 league games, 9 each, 90 points total', () {
      expect(season.leagueMatches, hasLength(45));
      for (final row in season.table) {
        expect(row.played, 9);
        expect(row.won + row.lost, 9);
      }
      expect(season.table.fold(0, (s, r) => s + r.points), 90);
    });

    test('table is sorted by points then net run rate', () {
      for (var i = 1; i < season.table.length; i++) {
        final a = season.table[i - 1];
        final b = season.table[i];
        expect(a.points > b.points || (a.points == b.points && a.netRunRate >= b.netRunRate),
            isTrue);
      }
    });

    test('playoffs follow the bracket', () {
      final [q1, elim, q2, fin] = season.playoffs;
      final top4 = season.table.take(4).map((r) => r.teamId).toList();
      expect({q1.first.battingTeamId, q1.second.battingTeamId}, {top4[0], top4[1]});
      expect({elim.first.battingTeamId, elim.second.battingTeamId}, {top4[2], top4[3]});
      expect({q2.first.battingTeamId, q2.second.battingTeamId},
          {q1.loserId, elim.winnerId});
      expect({fin.first.battingTeamId, fin.second.battingTeamId},
          {q1.winnerId, q2.winnerId});
      expect(season.championId, fin.winnerId);
      expect(season.runnerUpId, fin.loserId);
    });

    test('every innings is a legal T20 innings and adds up', () {
      for (final m in season.allMatches) {
        for (final inn in [m.first, m.second]) {
          expect(inn.legalBalls, lessThanOrEqualTo(120));
          expect(inn.wickets, lessThanOrEqualTo(10));
          final batterRuns = inn.batting.fold(0, (s, b) => s + b.runs);
          expect(batterRuns + inn.extras, inn.runs);
          final bowlerBalls = inn.bowling.fold(0, (s, b) => s + b.balls);
          expect(bowlerBalls, inn.legalBalls);
          for (final b in inn.bowling) {
            expect(b.balls, lessThanOrEqualTo(24));
          }
        }
        // The chase stops as soon as the target is passed.
        if (m.second.runs > m.first.runs) {
          expect(m.second.runs - m.first.runs, lessThanOrEqualTo(7));
        }
      }
    });

    test('winner matches the scores', () {
      for (final m in season.allMatches) {
        if (m.superOver) {
          expect(m.first.runs, m.second.runs);
        } else if (m.first.runs > m.second.runs) {
          expect(m.winnerId, m.first.battingTeamId);
        } else {
          expect(m.winnerId, m.second.battingTeamId);
        }
      }
    });

    test('awards come from real stats', () {
      final top = season.topRunScorer;
      expect(season.playerStats.values.every((s) => s.runs <= top.runs), isTrue);
      final wk = season.topWicketTaker;
      expect(season.playerStats.values.every((s) => s.wickets <= wk.wickets), isTrue);
    });

    test('same squads + seed replay identically (friend rooms rely on this)', () {
      final again = simulateSeason(squads: squads, seed: 99);
      String fingerprint(SeasonResult s) => [
            for (final m in s.allMatches)
              '${m.first.score}-${m.second.score}-${m.winnerId}-${m.playerOfMatch.id}',
          ].join('|');
      expect(fingerprint(again), fingerprint(season));
      expect(simulateSeason(squads: squads, seed: 100).championId, isNotNull);
    });
  });
}
