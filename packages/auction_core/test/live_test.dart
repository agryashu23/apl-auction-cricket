import 'package:auction_core/auction_core.dart';
import 'package:test/test.dart';

import 'helpers.dart';

LiveAuction _live({
  int seed = 1,
  Set<String> humans = const {'MBH'},
  String format = 'quick',
  List<LiveEvent>? events,
}) {
  final e = newEngine(format: format, seed: seed);
  final live = LiveAuction(
    engine: e,
    bidders: createAiBidders(e, seed: seed, humanTeamIds: humans),
    humanTeamIds: humans,
    onEvent: events?.add,
  );
  return live..start();
}

/// Steps in 100 ms frames until [done] or the auction ends.
void _runUntil(LiveAuction live, bool Function() done, {int maxFrames = 2000000}) {
  for (var i = 0; i < maxFrames && !done() && live.phase != LivePhase.finished; i++) {
    live.advance(100);
  }
}

void main() {
  test('reveal, then bidding with a running timer', () {
    final live = _live();
    expect(live.phase, LivePhase.revealing);
    live.advance(live.revealMs);
    expect(live.phase, LivePhase.bidding);
    expect(live.bidTimeLeftMs, greaterThan(0));
    expect(live.bidTimeLeftMs, lessThanOrEqualTo(live.bidTimerTotalMs));
  });

  test('a full auction with an idle human finishes and auto-fills them', () {
    final events = <LiveEvent>[];
    final live = _live(events: events);
    _runUntil(live, () => false);
    expect(live.phase, LivePhase.finished);
    expect(events.last, isA<AuctionFinished>());
    final human = live.engine.teams['MBH']!;
    expect(human.size, live.engine.format.minSquad);
    expect(events.whereType<LotClosed>().length, live.engine.results.length);
  });

  test('a human bid resets the timer and can win the player', () {
    final events = <LiveEvent>[];
    final live = _live(events: events);
    live.advance(live.revealMs);
    live.advance(3000);
    expect(live.humanBid('MBH'), isNull);
    expect(live.bidTimeLeftMs, live.bidTimerTotalMs);
    expect(events.whereType<BidPlaced>().last.teamId, 'MBH');
    // Keep bidding whenever outbid, until the lot closes.
    _runUntil(live, () {
      if (live.phase == LivePhase.bidding && live.engine.currentLot!.leaderId != 'MBH') {
        live.humanBid('MBH');
      }
      return live.phase == LivePhase.result;
    });
    // Either we won, or we ran out of money first.
    final result = live.lastResult!;
    if (result.teamId != 'MBH') {
      expect(live.engine.checkBid('MBH'), isNotNull);
    }
  });

  test('when every human passes, the lot is settled at once and goes to its result', () {
    final live = _live();
    live.advance(live.revealMs);
    final lot = live.engine.currentLot!.number;
    live.humanPass('MBH');
    expect(live.humanBid('MBH'), isNot(isNull), reason: 'no bidding after passing');
    // Straight to the result (or the RTM question): no waiting out the timer.
    live.advance(1);
    if (live.phase == LivePhase.rtmDecision) live.advance(live.aiRtmThinkMs);
    expect(live.phase, LivePhase.result);
    expect(live.lastResult!.lotNumber, lot);
    // The result shows at normal pace.
    expect(live.isFastForwarding, isFalse);
  });

  test('one human passing leaves the lot running for the other', () {
    final e = newEngine(format: 'quick', seed: 2);
    final live = LiveAuction(
      engine: e,
      bidders: createAiBidders(e, seed: 2, humanTeamIds: {'MBH', 'CHC'}),
      humanTeamIds: {'MBH', 'CHC'},
    )..start();
    live.advance(live.revealMs);
    live.humanPass('MBH');
    expect(live.phase, LivePhase.bidding);
    expect(live.isFastForwarding, isFalse);
    expect(live.humanBid('CHC'), isNull, reason: 'the other human can still bid');
  });

  test('holding fast-forward speeds up game time; releasing restores it', () {
    final live = _live();
    live.advance(live.revealMs);
    final t0 = live.nowMs;
    live.holdingFastForward = true;
    live.advance(500);
    final fast = live.nowMs - t0;
    live.holdingFastForward = false;
    final t1 = live.nowMs;
    live.advance(500);
    expect(fast, greaterThanOrEqualTo(4 * (live.nowMs - t1)));
  });

  test('an unanswered human RTM offer times out as declined', () {
    final events = <LiveEvent>[];
    final engine = AuctionEngine(
      rules: devDataset.rules,
      format: devDataset.rules.formats['quick']!,
      franchises: devDataset.franchises,
      pool: [
        Player.fromJson({
          ...testPlayer('star', batting: 95, set: 'marquee', basePrice: 200).toJson(),
          'capped': true,
          'previousTeam': 'MBH',
        }),
        ...devDataset.players.where((p) => p.set != 'marquee'),
      ],
      seed: 1,
    );
    final live = LiveAuction(
      engine: engine,
      bidders: createAiBidders(engine, seed: 1, humanTeamIds: {'MBH'}),
      humanTeamIds: {'MBH'},
      onEvent: events.add,
    )..start();
    _runUntil(live, () => live.phase == LivePhase.rtmDecision || live.phase == LivePhase.result);
    expect(live.phase, LivePhase.rtmDecision);
    expect(live.pendingRtm!.teamId, 'MBH');
    expect(events.whereType<RtmOffered>(), hasLength(1));
    live.advance(live.humanRtmTimeoutMs);
    expect(live.lastResult!.teamId, isNot('MBH'));
    expect(live.engine.teams['MBH']!.rtmCards, 1);
  });

  test('accepting a human RTM offer takes the player', () {
    final engine = AuctionEngine(
      rules: devDataset.rules,
      format: devDataset.rules.formats['quick']!,
      franchises: devDataset.franchises,
      pool: [
        Player.fromJson({
          ...testPlayer('star', batting: 95, set: 'marquee', basePrice: 200).toJson(),
          'capped': true,
          'previousTeam': 'MBH',
        }),
        ...devDataset.players.where((p) => p.set != 'marquee'),
      ],
      seed: 1,
    );
    final live = LiveAuction(
      engine: engine,
      bidders: createAiBidders(engine, seed: 1, humanTeamIds: {'MBH'}),
      humanTeamIds: {'MBH'},
    )..start();
    _runUntil(live, () => live.phase == LivePhase.rtmDecision);
    live.humanRtm('MBH', use: true);
    expect(live.lastResult!.teamId, 'MBH');
    expect(live.lastResult!.viaRtm, isTrue);
  });

  test('same seed and same inputs replay identically', () {
    List<String> run() {
      final events = <LiveEvent>[];
      final live = _live(seed: 7, events: events);
      _runUntil(live, () => false);
      return [
        for (final e in events)
          switch (e) {
            BidPlaced(:final teamId, :final amount) => 'b$teamId$amount',
            LotClosed(:final result) => 'c${result.teamId}${result.price}',
            _ => e.runtimeType.toString(),
          },
      ];
    }

    expect(run(), run());
  });

  test('frame size does not change the outcome', () {
    List<String?> run(int frameMs) {
      final live = _live(seed: 3);
      for (var i = 0; i < 5000000 && live.phase != LivePhase.finished; i++) {
        live.advance(frameMs);
      }
      return [for (final r in live.engine.results) '${r.player.id}:${r.teamId}:${r.price}'];
    }

    expect(run(16), run(250));
  });
}
