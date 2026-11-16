// Runs many AI-only auctions on the built dataset and reports what each
// player sold for, to check the AI's prices against market values.
//
//   dart run tool/sim_prices.dart [--format mega] [--runs 40] [--retain]
//
// Prints one line per player: id, value, base, median price, max price,
// times sold (CSV, highest median first), then a summary.
import 'dart:convert';
import 'dart:io';

import 'package:auction_core/auction_core.dart';

void main(List<String> args) {
  String arg(String name, String fallback) {
    final i = args.indexOf(name);
    return i >= 0 ? args[i + 1] : fallback;
  }

  final formatId = arg('--format', 'mega');
  final runs = int.parse(arg('--runs', '40'));
  final retain = args.contains('--retain');
  final data = Dataset.fromJson(
    jsonDecode(File('../../data/apl_dataset.json').readAsStringSync()) as Map<String, dynamic>,
  );
  final format = data.rules.formats[formatId]!;

  final prices = <String, List<int>>{};
  var over20 = 0;
  for (var seed = 1; seed <= runs; seed++) {
    final retentions = retain
        ? {
            for (final f in data.franchises)
              f.id: chooseAiRetentions(f.id, data.players, data.rules, format),
          }
        : <String, List<Player>>{};
    final engine = AuctionEngine(
      rules: data.rules,
      format: format,
      franchises: data.franchises,
      pool: data.players,
      seed: seed,
      retentions: retentions,
    );
    runAuctionWithAi(engine, createAiBidders(engine, seed: seed * 7919));
    for (final t in engine.teams.values) {
      for (final s in t.squad) {
        if (s.kind != PurchaseKind.auction && s.kind != PurchaseKind.rtm) continue;
        prices.putIfAbsent(s.player.id, () => []).add(s.price);
        if (s.price > 2000) over20++;
      }
    }
  }

  final byId = {for (final p in data.players) p.id: p};
  int median(List<int> v) => (v.toList()..sort())[v.length ~/ 2];
  final ids = prices.keys.toList()..sort((a, b) => median(prices[b]!).compareTo(median(prices[a]!)));
  stdout.writeln('id,value,base,median,max,sold');
  for (final id in ids) {
    final v = prices[id]!;
    final p = byId[id]!;
    stdout.writeln('$id,${p.value},${p.basePrice},${median(v)},${v.reduce((a, b) => a > b ? a : b)},${v.length}');
  }
  stderr.writeln('$formatId x$runs: ${(over20 / runs).toStringAsFixed(1)} players over 20 Cr per auction');
}
