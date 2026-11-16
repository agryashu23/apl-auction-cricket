import 'dart:convert';
import 'dart:io';

import 'package:auction_core/auction_core.dart';
import 'package:test/test.dart';

/// The built dataset: market values and the price cap live there.
final Dataset _apl = Dataset.fromJson(
  jsonDecode(File('../../data/apl_dataset.json').readAsStringSync()) as Map<String, dynamic>,
);

LiveAuction _live({int seed = 1, List<LiveEvent>? events}) {
  final e = AuctionEngine(
    rules: _apl.rules,
    format: _apl.rules.formats['mega']!,
    franchises: _apl.franchises,
    pool: _apl.players,
    seed: seed,
  );
  return LiveAuction(
    engine: e,
    bidders: createAiBidders(e, seed: seed, humanTeamIds: {'MBH'}),
    humanTeamIds: {'MBH'},
    onEvent: events?.add,
  )..start();
}

void main() {
  test('SKIP settles the AI bidding at once, then the AI stays quiet', () {
    final events = <LiveEvent>[];
    final live = _live(events: events);
    live.settleAi();
    expect(live.phase, LivePhase.bidding);
    final lot = live.engine.currentLot!;
    final settled = lot.currentBid;
    expect(settled, isNotNull, reason: 'a marquee player draws AI bids');
    expect(live.bidTimeLeftMs, live.bidTimerTotalMs);
    // No AI bid after the settle: the lot just closes at that price.
    final bidsBefore = events.whereType<BidPlaced>().length;
    while (live.phase != LivePhase.result) {
      live.advance(100);
    }
    expect(events.whereType<BidPlaced>().length, bidsBefore);
    expect(live.engine.results.last.price, settled);
  });

  test('a human bid after SKIP reopens the lot to the AI', () {
    final live = _live(seed: 3);
    live.settleAi();
    if (live.engine.checkBid('MBH') != null) return; // can't bid on this one
    expect(live.humanBid('MBH'), isNull);
    expect(live.bidTimeLeftMs, live.bidTimerTotalMs);
  });

  test('nobody ever pays more than the league cap', () {
    final cap = _apl.rules.maxPrice!;
    for (var seed = 1; seed <= 10; seed++) {
      final e = AuctionEngine(
        rules: _apl.rules,
        format: _apl.rules.formats['mega']!,
        franchises: _apl.franchises,
        pool: _apl.players,
        seed: seed,
      );
      runAuctionWithAi(e, createAiBidders(e, seed: seed));
      for (final r in e.results) {
        expect(r.price ?? 0, lessThanOrEqualTo(cap));
      }
    }
  });

  test('a bid past the cap is refused', () {
    final live = _live();
    final e = live.engine;
    live.advance(live.revealMs);
    // Walk the bid up to the cap with two teams taking turns.
    var turn = 0;
    while (e.nextBidAmount <= e.rules.maxPrice!) {
      final team = (turn++).isEven ? 'MBH' : 'CHC';
      expect(e.placeBid(team, e.nextBidAmount), isNull);
    }
    final other = e.currentLot!.leaderId == 'MBH' ? 'CHC' : 'MBH';
    expect(e.checkBid(other), BidRejection.priceCap);
  });

  test('friend rooms play to their own, higher cap', () {
    final room = RoomSetup(seed: 5, seasonSeed: 1, formatId: 'mega', retention: false, retentions: const {}, dataVersion: _apl.version)
        .buildEngine(_apl);
    expect(room.maxPrice, 3500);
    expect(_live().engine.maxPrice, 2800);
  });
}
