import 'dart:convert';

import 'package:auction_core/auction_core.dart';
import 'package:test/test.dart';

import 'helpers.dart';

/// Fingerprint of everything a phone shows: purses, squads (with how each
/// player was acquired), RTM cards, the open lot and its bids/passes.
String _state(AuctionEngine e) {
  final teams = [
    for (final t in e.teams.values)
      '${t.id}:${t.purse}:${t.rtmCards}:${t.squad.map((p) => '${p.player.id}/${p.price}/${p.kind.name}').join(',')}',
  ];
  final lot = e.currentLot;
  final lotText = lot == null
      ? '-'
      : '${lot.number}:${lot.player.id}:${lot.bids.map((b) => '${b.teamId}${b.amount}').join(',')}'
          ':${(lot.passed.toList()..sort()).join(',')}:${lot.biddingClosed}';
  return '${teams.join('|')}#$lotText#${e.isFinished}';
}

void main() {
  for (final mode in ['fresh', 'retention']) {
    test('$mode: replaying the server log rebuilds the exact same auction', () {
      final rules = devDataset.rules;
      final format = rules.formats['quick']!;
      const humans = {'MBH', 'CHC'};
      final retentions = mode == 'retention'
          ? {
              for (final f in devDataset.franchises)
                f.id: chooseAiRetentions(f.id, devDataset.players, rules, format).map((p) => p.id).toList(),
            }
          : <String, List<String>>{};
      final setup = RoomSetup(
        seed: 77,
        seasonSeed: 78,
        formatId: 'quick',
        retention: mode == 'retention',
        retentions: retentions,
        dataVersion: devDataset.version,
      );

      // "Server": the real LiveAuction with two humans acting randomly.
      final serverEngine = setup.buildEngine(devDataset);
      final log = <Map<String, dynamic>>[];
      final states = <String>[];
      final server = LiveAuction(
        engine: serverEngine,
        bidders: createAiBidders(serverEngine, seed: 5, humanTeamIds: humans),
        humanTeamIds: humans,
        onEvent: (e) {
          // Over the wire as JSON, like the real server.
          log.add(jsonDecode(jsonEncode(RoomAction.forEvent(e).toJson())) as Map<String, dynamic>);
          states.add(_state(serverEngine));
        },
      )..start();
      final rng = Rng(9);
      var guard = 0;
      while (server.phase != LivePhase.finished && guard++ < 2000000) {
        for (final h in humans) {
          final roll = rng.nextDouble();
          if (server.phase == LivePhase.bidding && roll < 0.03) server.humanBid(h);
          if (server.phase == LivePhase.bidding && roll > 0.995) server.humanPass(h);
          if (server.pendingRtm?.teamId == h) server.humanRtm(h, use: rng.chance(0.5));
        }
        server.advance(100);
      }
      expect(server.phase, LivePhase.finished);
      expect(log.where((a) => a['a'] == 'bid').length, greaterThan(50));

      // "Phone": a replica fed the same actions, checked after every one.
      final phone = RemoteAuction(data: devDataset, send: (_) {})..load(setup, const []);
      for (var i = 0; i < log.length; i++) {
        phone.apply(RoomAction.fromJson(log[i]));
        expect(_state(phone.engine), states[i], reason: 'diverged at action $i: ${log[i]}');
      }
      expect(phone.phase, LivePhase.finished);

      // Reconnecting mid-auction: replaying a prefix lands on the same state.
      final cut = log.length ~/ 2;
      final rejoined = RemoteAuction(data: devDataset, send: (_) {})
        ..load(setup, [for (final a in log.take(cut)) RoomAction.fromJson(a)]);
      expect(_state(rejoined.engine), states[cut - 1]);

      // Both sides get the same season from the same squads + seed.
      SeasonResult season(AuctionEngine e) => simulateSeason(
            squads: {for (final t in e.teams.values) t.id: t.players},
            seed: setup.seasonSeed,
          );
      expect(season(phone.engine).championId, season(serverEngine).championId);
    });
  }

  test('a wrong lot (different data) is detected, not silently applied', () {
    final setup = RoomSetup(seed: 1, seasonSeed: 2, formatId: 'quick', retention: false, retentions: const {}, dataVersion: 1);
    final phone = RemoteAuction(data: devDataset, send: (_) {})..load(setup, const []);
    expect(() => phone.apply(const OpenLotAction('no-such-player')), throwsA(isA<RoomDesync>()));
  });

  test('a disconnected human can be handed to the AI and back', () {
    final e = newEngine(format: 'quick', seed: 4);
    final live = LiveAuction(
      engine: e,
      bidders: createAiBidders(e, seed: 4, humanTeamIds: {'MBH'}),
      humanTeamIds: {'MBH'},
    )..start();
    live.setController('MBH', ai: AiBidder(teamId: 'MBH', personality: Personality.balanced, seed: 1));
    expect(live.humanTeamIds, isEmpty);
    expect(live.isAi('MBH'), isTrue);
    for (var i = 0; i < 20000 && live.phase != LivePhase.finished; i++) {
      live.advance(250);
    }
    expect(e.results.any((r) => r.teamId == 'MBH' && !r.viaRtm), isTrue, reason: 'AI bought for MBH');
    live.setController('MBH');
    expect(live.humanTeamIds, {'MBH'});
  });

  test('the remote replica sends intents instead of acting locally', () {
    final sent = <Map<String, dynamic>>[];
    final setup = RoomSetup(seed: 1, seasonSeed: 2, formatId: 'quick', retention: false, retentions: const {}, dataVersion: 1);
    final phone = RemoteAuction(data: devDataset, send: sent.add)..load(setup, const []);
    final first = setup.buildEngine(devDataset).upcoming.first;
    phone.apply(OpenLotAction(first.id));
    phone.clock({'t': 'clock', 'phase': 'bidding', 'bid': 9000, 'bidTotal': 10000, 'left': 0, 'rtmTotal': 10000, 'ff': false, 'humans': ['MBH']});
    expect(phone.humanBid('MBH'), isNull);
    expect(sent.single, {'t': 'bid', 'amount': first.basePrice});
    expect(phone.engine.currentLot!.bids, isEmpty, reason: 'nothing changes until the server confirms');
    phone.humanPass('MBH');
    expect(sent.last, {'t': 'pass'});
  });
}
