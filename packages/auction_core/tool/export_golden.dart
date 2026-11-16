// Exports golden results the C# port (packages/AuctionCore.Net) must
// reproduce exactly: RNG sequences, lot order, full AI auctions (fresh and
// retention, every format), squad ratings, seasons, and a scripted live
// auction's action log. Re-run after any rule change, then run the C# tests.
//
//   dart run tool/export_golden.dart
import 'dart:convert';
import 'dart:io';

import 'package:auction_core/auction_core.dart';

const _out = '../AuctionCore.Net/golden';
const _dataPath = '../../data/apl_dataset.json';

void main() {
  final data = Dataset.fromJson(jsonDecode(File(_dataPath).readAsStringSync()) as Map<String, dynamic>);
  Directory(_out).createSync(recursive: true);
  void write(String name, Object json) =>
      File('$_out/$name.json').writeAsStringSync(const JsonEncoder.withIndent(' ').convert(json));

  // --- RNG
  write('rng', [
    for (final seed in [0, 1, 42, 2026, 0x7FFFFFFF]) _rng(seed),
  ]);

  // --- Lot order
  write('lots', [
    for (final f in data.rules.formats.values)
      for (final seed in [1, 2, 3])
        {
          'format': f.id,
          'seed': seed,
          'lots': [for (final p in selectLots(data.players, f, data.rules, Rng(seed))) p.id],
        },
  ]);

  // --- Full AI auctions + ratings + seasons
  final auctions = <Object>[];
  for (final f in data.rules.formats.values) {
    for (final retention in [false, true]) {
      for (final seed in [1, 7]) {
        final retentions = retention
            ? {for (final t in data.franchises) t.id: chooseAiRetentions(t.id, data.players, data.rules, f)}
            : <String, List<Player>>{};
        final e = AuctionEngine(
          rules: data.rules,
          format: f,
          franchises: data.franchises,
          pool: data.players,
          seed: seed,
          retentions: retentions,
        );
        runAuctionWithAi(e, createAiBidders(e, seed: seed * 31));
        final ratings = rateSquads(e.teams.values,
            maxOverseasInXI: data.rules.maxOverseasInXI,
            benchSlots: f.minSquad - 11,
            scale: data.rules.ratingFor(f));
        final season = simulateSeason(
          squads: {for (final t in e.teams.values) t.id: t.players},
          seed: seed * 7919,
          maxOverseasInXI: data.rules.maxOverseasInXI,
        );
        auctions.add({
          'format': f.id,
          'retention': retention,
          'seed': seed,
          'retentions': {for (final r in retentions.entries) r.key: [for (final p in r.value) p.id]},
          'results': [for (final r in e.results) _result(r)],
          'teams': {for (final t in e.teams.values) t.id: _team(t)},
          'ratings': {
            for (final r in ratings)
              r.teamId: [r.batting, r.bowling, r.balance, r.depth, r.value, r.overall, r.rank, r.grade],
          },
          'xi': {for (final r in ratings) r.teamId: [for (final p in r.lineup.xi) p.id]},
          'season': _season(season),
        });
      }
    }
  }
  write('auctions', auctions);

  // --- A live auction with two scripted humans (frame-by-frame like the app)
  write('live', [for (final seed in [3, 11]) _live(data, seed), _live(data, 5, scale: 1.8)]);

  stdout.writeln('Golden files written to $_out');
}

Map<String, Object> _rng(int seed) {
  final u = Rng(seed);
  final d = Rng(seed);
  final i = Rng(seed);
  final w = Rng(seed);
  final r = Rng(seed);
  return {
    'seed': seed,
    'uint32': [for (var k = 0; k < 40; k++) u.nextUint32()],
    'double': [for (var k = 0; k < 10; k++) d.nextDouble()],
    'int7': [for (var k = 0; k < 40; k++) i.nextInt(7)],
    'weighted': [for (var k = 0; k < 40; k++) w.pickWeighted([1.0, 2.5, 0.5, 3.0])],
    'range': [for (var k = 0; k < 10; k++) r.range(600, 2400)],
  };
}

String _result(LotResult r) =>
    '${r.lotNumber}:${r.player.id}:${r.round.name}:${r.teamId}:${r.price}:${r.bidCount}:${r.viaRtm}';

Map<String, Object> _team(TeamState t) => {
      'purse': t.purse,
      'rtm': t.rtmCards,
      'squad': [for (final p in t.squad) '${p.player.id}:${p.price}:${p.kind.name}'],
    };

Map<String, Object?> _season(SeasonResult s) => {
      'champion': s.championId,
      'runnerUp': s.runnerUpId,
      'table': [
        for (final r in s.table) '${r.teamId}:${r.won}:${r.points}:${(r.netRunRate * 1e6).round()}',
      ],
      'matches': [
        for (final m in s.allMatches)
          '${m.number}:${m.stage.name}:${m.first.battingTeamId}:${m.first.runs}/${m.first.wickets}/${m.first.legalBalls}'
              ':${m.second.battingTeamId}:${m.second.runs}/${m.second.wickets}/${m.second.legalBalls}'
              ':${m.winnerId}:${m.superOver}:${m.playerOfMatch.id}:${m.pitch.name}',
      ],
      'topRuns': '${s.topRunScorer.player.id}:${s.topRunScorer.runs}',
      'topWickets': '${s.topWicketTaker.player.id}:${s.topWicketTaker.wickets}',
      'mvp': '${s.mvp.player.id}:${(s.mvp.mvpPoints * 1000).round()}',
    };

/// Mirrors `LiveScript` in the C# tests: same RNG, same per-frame choices.
Map<String, Object> _live(Dataset data, int seed, {double scale = 1.0}) {
  const humans = ['MBH', 'CHC'];
  final f = data.rules.formats['standard']!;
  final e = AuctionEngine(rules: data.rules, format: f, franchises: data.franchises, pool: data.players, seed: seed);
  final log = <Object>[];
  final live = LiveAuction(
    engine: e,
    bidders: createAiBidders(e, seed: seed * 13, humanTeamIds: humans.toSet()),
    humanTeamIds: humans.toSet(),
    aiThinkScale: scale,
    onEvent: (ev) => log.add(RoomAction.forEvent(ev).toJson()),
  )..start();
  final script = Rng(seed + 1000);
  var frames = 0;
  while (live.phase != LivePhase.finished && frames < 2000000) {
    for (final h in humans) {
      final roll = script.nextDouble();
      if (live.phase == LivePhase.bidding && roll < 0.03) live.humanBid(h);
      if (live.phase == LivePhase.bidding && roll > 0.995) live.humanPass(h);
      if (live.pendingRtm?.teamId == h) live.humanRtm(h, use: script.chance(0.5));
    }
    live.holdingFastForward = frames % 50 < 10;
    live.advance(100);
    frames++;
  }
  return {
    'seed': seed,
    'scale': scale,
    'frames': frames,
    'log': log,
    'teams': {for (final t in e.teams.values) t.id: _team(t)},
  };
}
