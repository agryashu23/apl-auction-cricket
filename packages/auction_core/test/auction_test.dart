import 'dart:io';

import 'package:auction_core/auction_core.dart';
import 'package:test/test.dart';

import 'helpers.dart';

void main() {
  group('money', () {
    test('formats lakhs and crores', () {
      expect(formatLakhs(30), '₹30 L');
      expect(formatLakhs(200), '₹2 Cr');
      expect(formatLakhs(1225), '₹12.25 Cr');
      expect(formatLakhs(1250), '₹12.5 Cr');
      expect(formatLakhs(12000), '₹120 Cr');
    });

    test('increments follow the table', () {
      final rules = devDataset.rules;
      expect(rules.nextBid(null, 200), 200);
      expect(rules.nextBid(50, 30), 60);
      expect(rules.nextBid(100, 30), 120);
      expect(rules.nextBid(480, 30), 500);
      expect(rules.nextBid(500, 30), 525);
    });
  });

  group('dataset', () {
    test('dev dataset is valid', () {
      expect(devDataset.validate(), isEmpty);
    });

    test('round-trips through JSON', () {
      final copy = Dataset.fromJson(devDataset.toJson());
      expect(copy.toJson(), devDataset.toJson());
    });

    test('rating scale is read from the data, with per-format override', () {
      final json = devDataset.toJson();
      final rules = json['rules'] as Map<String, dynamic>;
      rules['rating'] = {'battingBase': 50, 'bowlingBase': 55, 'spread': 34};
      (rules['formats'] as Map)['quick']['rating'] = {
        'battingBase': 46,
        'bowlingBase': 53,
        'spread': 34,
      };
      final parsed = Dataset.fromJson(json).rules;
      expect(parsed.ratingFor(parsed.formats['mega']!).battingBase, 50);
      expect(parsed.ratingFor(parsed.formats['quick']!).battingBase, 46);
    });

    test('real-data build (if present) ships no reference columns', () {
      final file = File('../../data/apl_dataset.json');
      if (!file.existsSync()) return;
      final text = file.readAsStringSync();
      for (final banned in ['real_name', 'real_price', 'real_team']) {
        expect(text.contains(banned), isFalse);
      }
    });
  });

  group('bidding', () {
    late AuctionEngine engine;
    setUp(() {
      engine = newEngine();
      engine.openNextLot();
    });

    test('first bid is the base price, then one increment at a time', () {
      final lot = engine.currentLot!;
      expect(engine.nextBidAmount, lot.player.basePrice);
      expect(engine.placeBid('MBH', lot.player.basePrice), isNull);
      expect(engine.nextBidAmount,
          lot.player.basePrice + devDataset.rules.incrementFor(lot.player.basePrice));
    });

    test('leader cannot outbid itself', () {
      engine.placeBid('MBH', engine.nextBidAmount);
      expect(engine.placeBid('MBH', engine.nextBidAmount), BidRejection.alreadyLeading);
    });

    test('a stale amount is rejected (two people tapped at once)', () {
      final amount = engine.nextBidAmount;
      expect(engine.placeBid('MBH', amount), isNull);
      expect(engine.placeBid('CHC', amount), BidRejection.staleAmount);
    });

    test('unknown team is rejected', () {
      expect(engine.placeBid('XXX', engine.nextBidAmount), BidRejection.unknownTeam);
    });

    test('sold lot charges the purse and adds the player', () {
      final player = engine.currentLot!.player;
      engine.placeBid('MBH', engine.nextBidAmount);
      final price = engine.nextBidAmount;
      engine.placeBid('CHC', price);
      final result = engine.closeLot();
      final chc = engine.teams['CHC']!;
      expect(result.sold, isTrue);
      expect(result.teamId, 'CHC');
      expect(chc.purse, engine.format.purse - price);
      expect(chc.players, [player]);
      expect(engine.available, isNot(contains(player)));
    });

    test('lot with no bids goes unsold', () {
      final result = engine.closeLot();
      expect(result.sold, isFalse);
      expect(engine.teams.values.every((t) => t.size == 0), isTrue);
    });

    test('cannot bid more than purse minus the minimum-squad reserve', () {
      final team = engine.teams['MBH']!;
      // Leave exactly enough for the rest of the minimum squad.
      team.purse = (engine.format.minSquad - 1) * devDataset.rules.minBasePrice;
      expect(engine.maxBidFor(team), 0);
      expect(engine.checkBid('MBH'), BidRejection.cannotAfford);
    });

    test('overseas limit blocks bids on overseas players', () {
      final format = devDataset.rules.formats['quick']!;
      final overseas = [
        for (var i = 0; i < 7; i++)
          testPlayer('o$i', overseas: true, set: 'marquee', basePrice: 30),
      ];
      final e = AuctionEngine(
        rules: devDataset.rules,
        format: format,
        franchises: devDataset.franchises.take(2).toList(),
        // The test players are the only marquee set, so they come up first.
        pool: [
          ...overseas,
          ...devDataset.players.where((p) => !p.overseas && p.set != 'marquee'),
        ],
        seed: 3,
      );
      // MBH buys overseas players until the cap.
      for (var i = 0; i < format.maxOverseas; i++) {
        e.openNextLot();
        e.placeBid('MBH', e.nextBidAmount);
        e.closeLot();
      }
      e.openNextLot();
      expect(e.currentLot!.player.overseas, isTrue);
      expect(e.checkBid('MBH'), BidRejection.overseasLimit);
      expect(e.checkBid('CHC'), isNull);
    });

    test('openNextLot refuses while a lot is open', () {
      expect(() => engine.openNextLot(), throwsStateError);
    });
  });

  group('lot selection', () {
    test('quick format: all marquee players first, lot count near maxLots', () {
      final format = devDataset.rules.formats['quick']!;
      final lots = selectLots(devDataset.players, format, devDataset.rules, Rng(1));
      final marquee = devDataset.players.where((p) => p.set == 'marquee').length;
      expect(lots.take(marquee).every((p) => p.set == 'marquee'), isTrue);
      expect(lots.length, closeTo(format.maxLots!, 5));
    });

    test('role sets rotate in tiers so keepers come up before overseas slots run out', () {
      final format = devDataset.rules.formats['mega']!;
      final lots = selectLots(devDataset.players, format, devDataset.rules, Rng(1));
      final firstKeeper = lots.indexWhere((p) => p.set == 'wicketKeepers');
      final firstUncapped = lots.indexWhere((p) => p.set == 'uncapped');
      // Batters, bowlers, all-rounders: one tier each before keepers start.
      expect(firstKeeper, lessThan(12 + 3 * tierSize + 1));
      // Every capped player comes before any uncapped one.
      expect(lots.take(firstUncapped).every((p) => p.capped), isTrue);
      // Within a set, tiers go best-first.
      final batters = lots.where((p) => p.set == 'batters').toList();
      final firstTierWorst = batters.take(tierSize).map((p) => p.impact).reduce((a, b) => a < b ? a : b);
      final secondTierBest = batters.skip(tierSize).take(tierSize).map((p) => p.impact).reduce((a, b) => a > b ? a : b);
      expect(firstTierWorst, greaterThanOrEqualTo(secondTierBest));
    });

    test('mega format puts every player up', () {
      final format = devDataset.rules.formats['mega']!;
      final lots = selectLots(devDataset.players, format, devDataset.rules, Rng(1));
      expect(lots.toSet(), devDataset.players.toSet());
    });
  });

  group('full AI auctions', () {
    for (final formatId in ['quick', 'standard', 'mega']) {
      test('$formatId: 25 seeds never break a rule', () {
        for (var seed = 1; seed <= 25; seed++) {
          final e = newEngine(format: formatId, seed: seed);
          runAuctionWithAi(e, createAiBidders(e, seed: seed));
          expect(e.isFinished, isTrue);
          final bought = <String>{};
          for (final t in e.teams.values) {
            expect(t.purse, greaterThanOrEqualTo(0), reason: '${t.id} seed $seed');
            expect(t.size, inInclusiveRange(e.format.minSquad, e.format.maxSquad),
                reason: '${t.id} seed $seed');
            expect(t.overseasCount, lessThanOrEqualTo(e.format.maxOverseas));
            expect(t.purse + t.spent, e.format.purse);
            for (final p in t.players) {
              expect(bought.add(p.id), isTrue, reason: '${p.id} bought twice');
            }
          }
        }
      });
    }

    test('same seed gives the same auction', () {
      List<String> run() {
        final e = newEngine(seed: 42);
        runAuctionWithAi(e, createAiBidders(e, seed: 42));
        return [for (final r in e.results) '${r.player.id}:${r.teamId}:${r.price}'];
      }

      expect(run(), run());
    });

    test('a human team that never bids still gets a minimum squad', () {
      final e = newEngine(format: 'quick', seed: 5);
      runAuctionWithAi(e, createAiBidders(e, seed: 5, humanTeamIds: {'MBH'}));
      final human = e.teams['MBH']!;
      expect(human.size, e.format.minSquad);
      expect(human.squad.every((p) => p.autoFilled), isTrue);
    });

    test('sitting out the auction never pays (auto-fill gives fillers, not stars)', () {
      for (final formatId in ['quick', 'standard', 'mega']) {
        for (var seed = 1; seed <= 8; seed++) {
          final e = newEngine(format: formatId, seed: seed);
          runAuctionWithAi(e, createAiBidders(e, seed: seed, humanTeamIds: {'MBH'}));
          final ratings = rateSquads(e.teams.values, maxOverseasInXI: 4, benchSlots: e.format.minSquad - 11);
          final idle = ratings.firstWhere((r) => r.teamId == 'MBH');
          final others = ratings.where((r) => r.teamId != 'MBH').map((r) => r.overall).toList()..sort();
          expect(idle.overall, lessThan(others[others.length ~/ 2]),
              reason: '$formatId seed $seed: idle ${idle.overall} vs median ${others[others.length ~/ 2]}');
        }
      }
    });

    test('skipping a lot settles it between AI teams only', () {
      final e = newEngine(seed: 9);
      final bidders = createAiBidders(e, seed: 9, humanTeamIds: {'MBH'});
      e.openNextLot();
      final result = resolveLotWithAi(e, bidders)!;
      expect(result.teamId, isNot('MBH'));
      expect(e.currentLot, isNull);
    });
  });

  group('AI bidder', () {
    test('never bids above its limit or for a team that cannot bid', () {
      final e = newEngine(seed: 11);
      final bidders = createAiBidders(e, seed: 11);
      while (e.openNextLot() != null) {
        for (final b in bidders) {
          final d = b.decide(e);
          if (d.bid) {
            expect(e.checkBid(b.teamId, amount: d.amount), isNull);
            expect(d.delayMs, lessThan(e.rules.bidTimerSec * 1000));
          }
        }
        resolveLotWithAi(e, bidders);
      }
    });
  });
}
