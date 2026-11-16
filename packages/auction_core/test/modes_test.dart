import 'package:auction_core/auction_core.dart';
import 'package:test/test.dart';

import 'helpers.dart';

/// A small pool where test players have known previous teams.
AuctionEngine _engine({
  required List<Player> players,
  Map<String, List<Player>> retentions = const {},
  String format = 'mega',
}) =>
    AuctionEngine(
      rules: devDataset.rules,
      format: devDataset.rules.formats[format]!,
      franchises: devDataset.franchises,
      pool: [...players, ...devDataset.players.where((p) => p.set != 'marquee')],
      seed: 1,
      retentions: retentions,
    );

Player _star(String id, {String? previousTeam, int impact = 90, bool capped = true, int star = 0}) =>
    Player.fromJson({
      ...testPlayer(id, batting: impact, set: 'marquee', basePrice: 200).toJson(),
      'capped': capped,
      'starPower': star,
      'previousTeam': ?previousTeam,
    });

void main() {
  group('RTM', () {
    test('previous team can take the player at the winning price', () {
      final player = _star('x1', previousTeam: 'MBH');
      final e = _engine(players: [player]);
      e.openNextLot();
      expect(e.currentLot!.player, player);
      e.placeBid('CHC', e.nextBidAmount);
      final price = e.nextBidAmount;
      e.placeBid('KOC', price);

      final offer = e.endBidding()!;
      expect(offer.teamId, 'MBH');
      expect(offer.winnerId, 'KOC');
      expect(offer.price, price);
      expect(e.placeBid('CHC', e.nextBidAmount), BidRejection.biddingClosed);

      final result = e.closeLot(useRtm: true);
      expect(result.teamId, 'MBH');
      expect(result.viaRtm, isTrue);
      expect(result.outbidTeamId, 'KOC');
      expect(e.teams['MBH']!.players, [player]);
      expect(e.teams['MBH']!.purse, e.format.purse - price);
      expect(e.teams['MBH']!.rtmCards, 0);
      expect(e.teams['KOC']!.purse, e.format.purse, reason: 'outbid team pays nothing');
    });

    test('declining leaves the player with the winner and keeps the card', () {
      final e = _engine(players: [_star('x1', previousTeam: 'MBH')]);
      e.openNextLot();
      e.placeBid('KOC', e.nextBidAmount);
      expect(e.endBidding(), isNotNull);
      final result = e.closeLot();
      expect(result.teamId, 'KOC');
      expect(e.teams['MBH']!.rtmCards, 1);
    });

    test('no offer when the previous team won, has no card, or the player is a free agent', () {
      final players = [
        _star('a', previousTeam: 'MBH'),
        _star('b', previousTeam: 'MBH'),
        _star('c', previousTeam: 'MBH'),
        _star('d'),
      ];
      final e = _engine(players: players);
      final offers = <String, RtmOffer?>{};
      while (offers.length < 4) {
        final lot = e.openNextLot()!;
        final id = lot.player.id;
        e.placeBid(id == 'a' ? 'MBH' : 'KOC', e.nextBidAmount);
        offers[id] = e.endBidding();
        // Use the card on the first offer we see, so later ones find none.
        e.closeLot(useRtm: offers[id] != null && e.teams['MBH']!.rtmCards > 0);
      }
      expect(offers['a'], isNull, reason: 'MBH won it outright');
      expect(offers['d'], isNull, reason: 'free agent');
      expect([offers['b'], offers['c']].whereType<RtmOffer>(), hasLength(1),
          reason: 'only one card');
    });

    test('closeLot(useRtm: true) without an offer is an error', () {
      final e = _engine(players: [_star('d')]);
      e.openNextLot();
      e.placeBid('KOC', e.nextBidAmount);
      expect(() => e.closeLot(useRtm: true), throwsStateError);
    });

    test('a human RTM decision pauses the AI resolver', () {
      final e = _engine(players: [_star('x1', previousTeam: 'MBH', impact: 95)]);
      final bidders = createAiBidders(e, seed: 3, humanTeamIds: {'MBH'});
      e.openNextLot();
      final result = resolveLotWithAi(e, bidders);
      expect(result, isNull);
      expect(e.currentLot!.rtmOffer!.teamId, 'MBH');
      expect(e.closeLot(useRtm: true).teamId, 'MBH');
    });
  });

  group('Pass', () {
    test('a team that passed cannot bid on that lot', () {
      final e = _engine(players: [_star('x1')]);
      e.openNextLot();
      e.pass('MBH', humanTeamIds: {'MBH'});
      expect(e.placeBid('MBH', e.nextBidAmount), BidRejection.passed);
    });

    test('fast-forward only once every human is out, never while one leads', () {
      final e = _engine(players: [_star('x1')]);
      e.openNextLot();
      final humans = {'MBH', 'CHC'};
      e.placeBid('CHC', e.nextBidAmount);
      expect(e.pass('MBH', humanTeamIds: humans), isFalse, reason: 'CHC leads');
      e.placeBid('KOC', e.nextBidAmount);
      expect(e.pass('CHC', humanTeamIds: humans), isTrue);
    });

    test('the leader cannot pass away its bid', () {
      final e = _engine(players: [_star('x1')]);
      e.openNextLot();
      e.placeBid('MBH', e.nextBidAmount);
      e.pass('MBH', humanTeamIds: {'MBH'});
      expect(e.closeLot().teamId, 'MBH');
    });
  });

  group('retention', () {
    final rules = devDataset.rules;
    final mega = rules.formats['mega']!;

    test('slabs go to the most valuable capped players; uncapped pay the flat price', () {
      final a = _star('a', previousTeam: 'MBH', impact: 92);
      final b = _star('b', previousTeam: 'MBH', impact: 80);
      final u = _star('u', previousTeam: 'MBH', impact: 75, capped: false);
      final prices = retentionPrices([b, u, a], rules, mega);
      expect(prices[a], 1800);
      expect(prices[b], 1400);
      expect(prices[u], 400);
    });

    test('prices scale with the format purse', () {
      final a = _star('a', previousTeam: 'MBH');
      expect(retentionPrices([a], rules, rules.formats['quick']!)[a], 900);
    });

    test('rules: max 3, own former players only', () {
      final mine = [for (var i = 0; i < 4; i++) _star('m$i', previousTeam: 'MBH')];
      expect(retentionProblem('MBH', mine.take(3).toList(), rules, mega), isNull);
      expect(retentionProblem('MBH', mine, rules, mega), contains('At most 3'));
      expect(retentionProblem('CHC', [mine.first], rules, mega), contains('did not play'));
    });

    test('retained players are charged, kept out of the auction, and RTM still works', () {
      final keep = _star('k', previousTeam: 'MBH', impact: 92);
      final other = _star('o', previousTeam: 'MBH', impact: 88);
      final e = _engine(players: [keep, other], retentions: {
        'MBH': [keep],
      });
      final mbh = e.teams['MBH']!;
      expect(mbh.players, [keep]);
      expect(mbh.squad.single.kind, PurchaseKind.retained);
      expect(mbh.purse, e.format.purse - 1800);
      expect(e.upcoming, isNot(contains(keep)));
      expect(mbh.rtmCards, 1);

      e.openNextLot();
      expect(e.currentLot!.player, other);
      e.placeBid('KOC', e.nextBidAmount);
      expect(e.endBidding()!.teamId, 'MBH');
    });

    test('invalid retentions are refused', () {
      final p = _star('p', previousTeam: 'CHC');
      expect(() => _engine(players: [p], retentions: {
            'MBH': [p],
          }), throwsArgumentError);
    });

    test('AI keeps up to 3 valuable former players and the auction still completes', () {
      final pool = [
        for (var i = 0; i < 5; i++) _star('s$i', previousTeam: 'MBH', impact: 86 + i),
        _star('weak', previousTeam: 'CHC', impact: 58),
      ];
      final kept = chooseAiRetentions('MBH', pool, rules, mega);
      expect(kept, hasLength(3));
      expect(kept.map((p) => p.id), containsAll(['s4', 's3', 's2']));
      expect(chooseAiRetentions('CHC', pool, rules, mega), isEmpty,
          reason: 'not worth ₹18 Cr');

      final e = _engine(players: pool, retentions: {'MBH': kept});
      runAuctionWithAi(e, createAiBidders(e, seed: 2));
      for (final t in e.teams.values) {
        expect(t.size, inInclusiveRange(e.format.minSquad, e.format.maxSquad));
        expect(t.purse, greaterThanOrEqualTo(0));
      }
    });
  });

  group('star power', () {
    test('raises market value but not match strength', () {
      final plain = _star('p', impact: 85);
      final famous = _star('f', impact: 85, star: 100);
      final mega = devDataset.rules.formats['mega']!;
      expect(fairPrice(famous, mega), closeTo(fairPrice(plain, mega) * 1.8, 1));
      expect(famous.impact, plain.impact);
      expect(battingStrength([famous]), battingStrength([plain]));
    });
  });
}
