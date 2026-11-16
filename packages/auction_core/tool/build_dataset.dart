// Builds the publishable dataset from the source sheet.
//
//   dart run tool/build_dataset.dart [--version N] [--allow-placeholder-names]
//
// Reads  data/source/players_reference.csv + data/source/league.json
// Writes data/apl_dataset.json
//
// The reference sheet holds real names and real prices for research only.
// They are NEVER written out: every exported player uses `game_name`, and the
// build fails if any row is missing one. --allow-placeholder-names is for
// local testing only (names become "Player r001").
import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;

import 'package:auction_core/auction_core.dart';

const _source = '../../data/source/players_reference.csv';
const _league = '../../data/source/league.json';
const _research = '../../data/source/research/research_players.csv';
const _out = '../../data/apl_dataset.json';
const _marqueeCount = 12;

void main(List<String> args) {
  final placeholders = args.contains('--allow-placeholder-names');
  final versionIndex = args.indexOf('--version');
  final version = versionIndex >= 0 ? int.parse(args[versionIndex + 1]) : 1;

  // Excel saves CSVs with a byte-order mark; drop it so the header parses.
  final rows = parseCsv(File(_source).readAsStringSync().replaceFirst('﻿', ''));
  final header = rows.first;
  final records = [
    for (final r in rows.skip(1))
      if (r.any((c) => c.trim().isNotEmpty)) {for (var i = 0; i < header.length; i++) header[i]: i < r.length ? r[i].trim() : ''},
  ];

  final problems = <String>[];
  final missingNames = records.where((r) => r['game_name']!.isEmpty).map((r) => r['ref']).toList();
  if (missingNames.isNotEmpty && !placeholders) {
    problems.add('${missingNames.length} players have no game_name '
        '(first: ${missingNames.take(5).join(', ')}). Fill them in, or use '
        '--allow-placeholder-names for a local test build.');
  }
  final realNames = {for (final r in records) r['real_name']!.toLowerCase()};
  final seenGameNames = <String, String>{};
  for (final r in records) {
    final gameName = r['game_name']!.toLowerCase();
    if (gameName.isEmpty) continue;
    if (realNames.contains(gameName)) {
      problems.add('${r['ref']}: game_name is identical to a real name');
    }
    // Near-miss names must still be visibly different from the real one.
    final distance = _editDistance(gameName, r['real_name']!.toLowerCase());
    if (distance < _minNameDistance) {
      problems.add('${r['ref']}: game_name "${r['game_name']}" is only $distance edits from the real name (need $_minNameDistance)');
    }
    final clash = seenGameNames[gameName];
    if (clash != null) problems.add('${r['ref']}: game_name duplicates $clash');
    seenGameNames[gameName] = r['ref']!;
  }

  final league = jsonDecode(File(_league).readAsStringSync()) as Map<String, dynamic>;
  final rules = AuctionRules.fromJson(league['rules'] as Map<String, dynamic>);
  // Real team code -> franchise id, from each franchise's "sourceTeam".
  // Stays in the build: Franchise.toJson doesn't write it out.
  final franchiseFor = {
    for (final f in (league['franchises'] as List).cast<Map<String, dynamic>>())
      if (f['sourceTeam'] != null) f['sourceTeam'] as String: f['id'] as String,
  };

  final drafts = <Player>[];
  for (final r in records) {
    try {
      drafts.add(_player(r, placeholders, franchiseFor));
    } on FormatException catch (e) {
      problems.add('${r['ref']}: ${e.message}');
    }
  }
  if (problems.isNotEmpty) {
    stderr.writeln('Build failed:\n  ${problems.join('\n  ')}');
    exit(1);
  }

  final values = _marketValues(records, drafts, rules.formats['mega']!);
  final valued = [for (final p in drafts) Player.fromJson({...p.toJson(), 'value': values[p.id]})];

  // Marquee = anyone the sheet pins there ("marquee" = Y), then the most
  // valuable capped players up to the count; everyone else by role,
  // uncapped together.
  final pinned = {for (final r in records) if (r['marquee'] == 'Y') r['ref']!};
  final capped = valued.where((p) => p.capped && !pinned.contains(p.id)).toList()
    ..sort((a, b) => b.value.compareTo(a.value));
  final marquee = {...pinned, ...capped.take(_marqueeCount - pinned.length).map((p) => p.id)};
  final players = [
    for (final p in valued)
      Player.fromJson({
        ...p.toJson(),
        'set': marquee.contains(p.id) ? 'marquee' : _setFor(p),
      }),
  ];

  final dataset = Dataset(
    version: version,
    leagueName: (league['league'] as Map)['name'] as String,
    leagueShortName: (league['league'] as Map)['shortName'] as String,
    franchises: [
      for (final f in league['franchises'] as List) Franchise.fromJson(f as Map<String, dynamic>),
    ],
    players: players,
    rules: rules,
  );
  final validation = dataset.validate();
  if (validation.isNotEmpty) {
    stderr.writeln('Dataset invalid:\n  ${validation.join('\n  ')}');
    exit(1);
  }
  File(_out).writeAsStringSync(const JsonEncoder.withIndent('  ').convert(dataset.toJson()));
  final bySet = <String, int>{};
  for (final p in players) {
    bySet[p.set] = (bySet[p.set] ?? 0) + 1;
  }
  stdout.writeln('Wrote ${players.length} players (v$version) to $_out');
  stdout.writeln('  sets: $bySet');
  if (placeholders && missingNames.isNotEmpty) {
    stdout.writeln('  WARNING: ${missingNames.length} placeholder names. Not for release.');
  }
}

Player _player(
  Map<String, String> r,
  bool placeholders,
  Map<String, String> franchiseFor,
) {
  int skill(String key) {
    final v = int.tryParse(r[key] ?? '');
    if (v == null || v < 0 || v > 100) throw FormatException('$key must be 0-100, got "${r[key]}"');
    return v;
  }

  // Unsold players have no team; everyone else maps to their franchise.
  final realTeam = r['real_team']!;
  final previousTeam = realTeam.isEmpty ? null : franchiseFor[realTeam];
  if (realTeam.isNotEmpty && previousTeam == null) {
    throw FormatException('real_team "$realTeam" has no franchise with that sourceTeam');
  }

  final role = Role.fromCode(r['role']!);
  final country = r['country']!;
  final capped = r['capped'] == 'Y';
  final keeping = skill('keep');
  final draft = Player(
    id: r['ref']!,
    name: r['game_name']!.isNotEmpty ? r['game_name']! : 'Player ${r['ref']}',
    country: country,
    overseas: country != 'India',
    capped: capped,
    role: role,
    leftHanded: r['hand'] == 'L',
    bowlType: BowlType.fromName(r['bowl']),
    batting: skill('bat'),
    bowling: skill('bowlskill'),
    fielding: role == Role.wicketKeeper ? keeping : 70,
    keeping: keeping,
    basePrice: 30,
    set: 'uncapped',
    tags: [for (final t in r['tags']!.split('|')) if (t.isNotEmpty) t],
    starPower: (r['star'] ?? '').isEmpty ? 0 : skill('star'),
    previousTeam: previousTeam,
  );
  final base = r['base']!.isEmpty ? _derivedBase(draft) : int.parse(r['base']!);
  return Player.fromJson({...draft.toJson(), 'basePrice': base});
}

/// Each player's market value in lakhs at a ₹120 Cr purse:
///  - anchored on his latest real price, blended with his older peak (2022 and
///    2024 prices scaled up to today's purse) where the research has it;
///  - tempered a quarter of the way towards what his skills are worth (more
///    after a breakout season), and scaled by last season's form and fitness;
///  - players nobody bought are worth 60% of their skill value.
Map<String, int> _marketValues(List<Map<String, String>> records, List<Player> drafts, AuctionFormat mega) {
  final research = <String, Map<String, String>>{};
  final file = File(_research);
  if (file.existsSync()) {
    final rows = parseCsv(file.readAsStringSync().replaceFirst('﻿', ''));
    for (final r in rows.skip(1)) {
      if (r.length < rows.first.length) continue;
      research[r[0]] = {for (var i = 0; i < rows.first.length; i++) rows.first[i]: r[i].trim()};
    }
  }
  final byId = {for (final p in drafts) p.id: p};
  final realPrice = {
    for (final r in records)
      if (int.tryParse(r['real_price'] ?? '') case final price? when price > 0) r['ref']!: price,
  };

  // Skill prices are on their own scale; put them on the real one.
  final ratios = [
    for (final e in realPrice.entries) e.value / fairPrice(byId[e.key]!, mega),
  ]..sort();
  final k = ratios.isEmpty ? 1.0 : ratios[ratios.length ~/ 2];

  final out = <String, int>{};
  for (final p in drafts) {
    final skillValue = fairPrice(p, mega) * k;
    final latest = realPrice[p.id];
    final r = research[p.id];
    double value;
    if (latest == null) {
      value = skillValue * 0.6;
    } else {
      var anchor = latest.toDouble();
      final old = [
        if (int.tryParse(r?['price_2024'] ?? '') case final v?) v * 1.2,
        if (int.tryParse(r?['price_2022'] ?? '') case final v?) v * 12 / 9,
      ];
      if (old.isNotEmpty) anchor = 0.8 * anchor + 0.2 * old.reduce(math.max);
      // A breakout season makes an old cheap price stale: lean on skills.
      final form = r == null ? 1.0 : _formFactor(p, r);
      final skillWeight = form >= 1.2 && skillValue > anchor ? 0.6 : 0.25;
      value = (1 - skillWeight) * anchor + skillWeight * skillValue;
    }
    if (r != null) value *= _formFactor(p, r) * (double.tryParse(r['availability'] ?? '') ?? 1.0);
    out[p.id] = math.max(p.basePrice, (value / 5).round() * 5);
  }
  return out;
}

/// Last season against par (28 runs a game at a strike rate of 150; 1.1
/// wickets a game at an economy of 8.8), square-rooted and held to 0.8–1.3.
/// Fewer than 5 games tells us nothing.
double _formFactor(Player p, Map<String, String> r) {
  double? n(String key) => double.tryParse(r[key] ?? '');
  final games = n('t20_matches') ?? 14;
  if (games < 5) return 1.0;
  final runs = n('runs');
  final wickets = n('wickets');
  final bat = runs == null ? null : (runs / games / 28) * ((n('strike_rate') ?? 145) / 150);
  final bowl = wickets == null ? null : (wickets / games / 1.1) * (8.8 / (n('economy') ?? 8.8));
  final index = switch (p.role) {
    Role.batter || Role.wicketKeeper => bat,
    Role.bowler => bowl,
    Role.allRounder => [?bat, ?bowl].fold<double?>(null, (m, v) => m == null ? v : math.max(m, v)),
  };
  if (index == null) return 1.0;
  return math.sqrt(index).clamp(0.8, 1.3);
}

/// Base price for players who weren't in the real auction (retained), by
/// game quality — the same tiers a mega auction would use.
int _derivedBase(Player p) {
  final i = p.impact;
  if (!p.capped) return i >= 72 ? 50 : (i >= 66 ? 40 : 30);
  if (i >= 84) return 200;
  if (i >= 78) return 150;
  if (i >= 72) return 100;
  if (i >= 66) return 75;
  return 50;
}

String _setFor(Player p) {
  if (!p.capped) return 'uncapped';
  return switch (p.role) {
    Role.batter => 'batters',
    Role.bowler => 'bowlers',
    Role.allRounder => 'allRounders',
    Role.wicketKeeper => 'wicketKeepers',
  };
}

/// Fewest letter changes between a game name and the real one.
const _minNameDistance = 3;

/// Levenshtein distance: insertions, deletions and substitutions.
int _editDistance(String a, String b) {
  var prev = List<int>.generate(b.length + 1, (j) => j);
  for (var i = 1; i <= a.length; i++) {
    final cur = List<int>.filled(b.length + 1, 0)..[0] = i;
    for (var j = 1; j <= b.length; j++) {
      final cost = a[i - 1] == b[j - 1] ? 0 : 1;
      cur[j] = [prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + cost].reduce((x, y) => x < y ? x : y);
    }
    prev = cur;
  }
  return prev[b.length];
}

/// Minimal RFC-4180 CSV: quoted fields, doubled quotes, CRLF or LF.
List<List<String>> parseCsv(String text) {
  final rows = <List<String>>[];
  var row = <String>[];
  final field = StringBuffer();
  var inQuotes = false;
  for (var i = 0; i < text.length; i++) {
    final c = text[i];
    if (inQuotes) {
      if (c == '"') {
        if (i + 1 < text.length && text[i + 1] == '"') {
          field.write('"');
          i++;
        } else {
          inQuotes = false;
        }
      } else {
        field.write(c);
      }
    } else if (c == '"') {
      inQuotes = true;
    } else if (c == ',') {
      row.add(field.toString());
      field.clear();
    } else if (c == '\n' || c == '\r') {
      if (c == '\r' && i + 1 < text.length && text[i + 1] == '\n') i++;
      row.add(field.toString());
      field.clear();
      rows.add(row);
      row = <String>[];
    } else {
      field.write(c);
    }
  }
  if (field.isNotEmpty || row.isNotEmpty) {
    row.add(field.toString());
    rows.add(row);
  }
  return rows;
}
