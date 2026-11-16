import 'dart:convert';
import 'dart:io';

import 'package:auction_core/auction_core.dart';

/// The generated dev pool (run tool/gen_dev_dataset.dart if missing).
final Dataset devDataset = Dataset.fromJson(
  jsonDecode(File('../../data/dev_dataset.json').readAsStringSync())
      as Map<String, dynamic>,
);

AuctionEngine newEngine({String format = 'mega', int seed = 1}) => AuctionEngine(
      rules: devDataset.rules,
      format: devDataset.rules.formats[format]!,
      franchises: devDataset.franchises,
      pool: devDataset.players,
      seed: seed,
    );

Player testPlayer(
  String id, {
  Role role = Role.batter,
  int batting = 60,
  int bowling = 30,
  int keeping = 0,
  bool overseas = false,
  BowlType bowlType = BowlType.none,
  int basePrice = 30,
  String set = 'uncapped',
}) =>
    Player(
      id: id,
      name: 'Player $id',
      country: overseas ? 'Australia' : 'India',
      overseas: overseas,
      capped: false,
      role: role,
      leftHanded: false,
      bowlType: bowlType,
      batting: batting,
      bowling: bowling,
      fielding: 70,
      keeping: keeping,
      basePrice: basePrice,
      set: set,
    );
