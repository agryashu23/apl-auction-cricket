// Generates data/dev_dataset.json: a made-up player pool for building and
// tuning the game. Names are random first+last combinations, NOT the launch
// list; real content will come from the spreadsheet pipeline (phase 3).
//
// Run from packages/auction_core:  dart run tool/gen_dev_dataset.dart
import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;

import 'package:auction_core/auction_core.dart';

void main(List<String> args) {
  final out = args.isNotEmpty ? args.first : '../../data/dev_dataset.json';
  final gen = _Generator(Rng(2026));
  final dataset = Dataset(
    version: 1,
    leagueName: 'Auction Premier League',
    leagueShortName: 'APL',
    franchises: _franchises,
    players: gen.players(),
    rules: _rules,
  );
  final problems = dataset.validate();
  if (problems.isNotEmpty) {
    stderr.writeln(problems.join('\n'));
    exit(1);
  }
  File(out)
    ..createSync(recursive: true)
    ..writeAsStringSync(const JsonEncoder.withIndent('  ').convert(dataset.toJson()));
  stdout.writeln('Wrote ${dataset.players.length} players to $out');
}

// Placeholder franchise names; final names live in the server data.
const _franchises = [
  Franchise(id: 'MBH', name: 'Mumbai Heroes', shortName: 'MBH', city: 'Mumbai', primaryColor: '#1E5AA8', secondaryColor: '#F2C14E'),
  Franchise(id: 'CHC', name: 'Chennai Cyclones', shortName: 'CHC', city: 'Chennai', primaryColor: '#F5C518', secondaryColor: '#1B3A6B'),
  Franchise(id: 'BLB', name: 'Bengaluru Bolts', shortName: 'BLB', city: 'Bengaluru', primaryColor: '#C8102E', secondaryColor: '#111111'),
  Franchise(id: 'KOC', name: 'Kolkata Comets', shortName: 'KOC', city: 'Kolkata', primaryColor: '#4B2A84', secondaryColor: '#D4AF37'),
  Franchise(id: 'DLD', name: 'Delhi Defenders', shortName: 'DLD', city: 'Delhi', primaryColor: '#0B3D91', secondaryColor: '#E4312B'),
  Franchise(id: 'PJP', name: 'Punjab Pioneers', shortName: 'PJP', city: 'Mohali', primaryColor: '#D71920', secondaryColor: '#C0C0C0'),
  Franchise(id: 'RJR', name: 'Rajasthan Rangers', shortName: 'RJR', city: 'Jaipur', primaryColor: '#E6007E', secondaryColor: '#0A2A66'),
  Franchise(id: 'HYH', name: 'Hyderabad Hawks', shortName: 'HYH', city: 'Hyderabad', primaryColor: '#F26522', secondaryColor: '#000000'),
  Franchise(id: 'GJG', name: 'Gujarat Gladiators', shortName: 'GJG', city: 'Ahmedabad', primaryColor: '#0F2A44', secondaryColor: '#B8975A'),
  Franchise(id: 'LKL', name: 'Lucknow Lancers', shortName: 'LKL', city: 'Lucknow', primaryColor: '#00A3E0', secondaryColor: '#F7941D'),
];

const _rules = AuctionRules(
  increments: [
    IncrementStep(below: 100, step: 10),
    IncrementStep(below: 500, step: 20),
    IncrementStep(step: 25),
  ],
  basePrices: [200, 150, 100, 75, 50, 30],
  bidTimerSec: 10,
  acceleratedTimerSec: 6,
  maxOverseasInXI: 4,
  setOrder: ['marquee', 'batters', 'bowlers', 'allRounders', 'wicketKeepers', 'uncapped'],
  formats: {
    'quick': AuctionFormat(id: 'quick', label: 'Quick', purse: 6000, minSquad: 11, maxSquad: 15, maxOverseas: 5, maxLots: 70),
    'standard': AuctionFormat(id: 'standard', label: 'Standard', purse: 10000, minSquad: 15, maxSquad: 20, maxOverseas: 6, maxLots: 160),
    'mega': AuctionFormat(id: 'mega', label: 'Mega', purse: 12000, minSquad: 18, maxSquad: 25, maxOverseas: 8),
  },
);

const _indianFirst = [
  'Aarav', 'Abhay', 'Aditya', 'Ajay', 'Akash', 'Aman', 'Anand', 'Ankit', 'Anirudh',
  'Arnav', 'Bharat', 'Chetan', 'Daksh', 'Deepak', 'Dev', 'Dhruv', 'Gaurav', 'Harsh',
  'Ishaan', 'Jatin', 'Karan', 'Kartik', 'Kunal', 'Lakshya', 'Madhav', 'Manav', 'Mihir',
  'Mohit', 'Naveen', 'Neeraj', 'Nikhil', 'Om', 'Parth', 'Pranav', 'Pratik', 'Raghav',
  'Rajat', 'Rohan', 'Sahil', 'Samar', 'Sanket', 'Saurabh', 'Shaurya', 'Siddharth',
  'Sumit', 'Tanmay', 'Tarun', 'Uday', 'Varun', 'Vedant', 'Vikram', 'Vinay', 'Yash', 'Zubin',
];
const _indianLast = [
  'Agarwal', 'Bajaj', 'Bhatt', 'Bhosale', 'Bisht', 'Chauhan', 'Desai', 'Dubey', 'Garg',
  'Ghosh', 'Goswami', 'Hegde', 'Jha', 'Joshi', 'Kamath', 'Kapoor', 'Kulkarni', 'Malhotra',
  'Mehta', 'Menon', 'Naidu', 'Negi', 'Pawar', 'Pillai', 'Rathore', 'Rawat', 'Reddy',
  'Saxena', 'Sen', 'Sethi', 'Shetty', 'Sinha', 'Sodhi', 'Tiwari', 'Trivedi', 'Verma', 'Walia',
];
const _overseasNames = <String, (List<String>, List<String>)>{
  'Australia': (
    ['Lachlan', 'Riley', 'Angus', 'Hamish', 'Callum', 'Jack', 'Ethan', 'Noah'],
    ['Hartley', 'Whitlock', 'Calloway', 'Fairbairn', 'Kingsley', 'Merriman', 'Norcott', 'Radley'],
  ),
  'England': (
    ['Oliver', 'Harry', 'Alfie', 'George', 'Freddie', 'Toby', 'Rory', 'Felix'],
    ['Pemberton', 'Ashby', 'Holloway', 'Langford', 'Sutcliffe', 'Wakefield', 'Ellery', 'Thorne'],
  ),
  'South Africa': (
    ['Thabo', 'Sipho', 'Ruan', 'Pieter', 'Jaco', 'Lwazi', 'Wian', 'Tumelo'],
    ['Vermaak', 'Steenkamp', 'Olivier', 'Mokoena', 'Dlamini', 'Fourie', 'Coetzer', 'Ngubane'],
  ),
  'New Zealand': (
    ['Finn', 'Hamish', 'Logan', 'Blair', 'Corey', 'Tane', 'Ryder', 'Quinn'],
    ['Brockett', 'Dunmore', 'Fenwick', 'Galbraith', 'Harwood', 'Kerridge', 'Mcleish', 'Paton'],
  ),
  'West Indies': (
    ['Jermaine', 'Andre', 'Marlon', 'Devon', 'Tyrell', 'Keon', 'Darnell', 'Romario'],
    ['Bramble', 'Greaves', 'Alleyne', 'Prescod', 'Gittens', 'Toppin', 'Cumberbatch', 'Austin'],
  ),
  'Afghanistan': (
    ['Hazrat', 'Farid', 'Naveed', 'Qais', 'Sami', 'Wafa', 'Zia', 'Bilal'],
    ['Safi', 'Shinwari', 'Omari', 'Hotak', 'Ahmadzai', 'Noori', 'Kakar', 'Wardak'],
  ),
  'Sri Lanka': (
    ['Kasun', 'Dilan', 'Nuwan', 'Chamika', 'Lahiru', 'Sadeera', 'Tharindu', 'Isuru'],
    ['Wickramasinghe', 'Gunawardena', 'Rathnayake', 'Bandara', 'Dissanayake', 'Wijesinghe', 'Ekanayake', 'Kumarage'],
  ),
  'Bangladesh': (
    ['Tanvir', 'Arif', 'Nayeem', 'Rakib', 'Sabbir', 'Jubair', 'Fahim', 'Rafsan'],
    ['Hossain', 'Rahman', 'Chowdhury', 'Kabir', 'Sarkar', 'Haque', 'Mridha', 'Talukder'],
  ),
};

class _Generator {
  _Generator(this.rng);

  final Rng rng;
  final _usedNames = <String>{};
  final _players = <Player>[];

  List<Player> players() {
    // set, count, role mix, impact range, overseas share
    _set('marquee', [Role.batter, Role.batter, Role.batter, Role.batter, Role.bowler, Role.bowler, Role.bowler, Role.allRounder, Role.allRounder, Role.allRounder, Role.wicketKeeper, Role.wicketKeeper], 86, 95, 0.42, capped: true, skew: 1.0);
    _set('batters', List.filled(50, Role.batter), 62, 88, 0.35, capped: true);
    _set('bowlers', List.filled(55, Role.bowler), 62, 88, 0.35, capped: true);
    _set('allRounders', List.filled(45, Role.allRounder), 62, 87, 0.35, capped: true);
    _set('wicketKeepers', List.filled(22, Role.wicketKeeper), 62, 86, 0.3, capped: true);
    _set('uncapped', [
      ...List.filled(40, Role.batter),
      ...List.filled(50, Role.bowler),
      ...List.filled(35, Role.allRounder),
      ...List.filled(15, Role.wicketKeeper),
    ], 40, 68, 0, capped: false, skew: 1.3);
    return _players;
  }

  void _set(String set, List<Role> roles, int lo, int hi, double overseasShare,
      {required bool capped, double skew = 1.6}) {
    for (final role in roles) {
      final target = (lo + (hi - lo) * math.pow(rng.nextDouble(), skew)).round();
      final overseas = rng.chance(overseasShare);
      final country = overseas ? rng.pick(_overseasNames.keys.toList()) : 'India';
      _players.add(_player(set, role, target, country, capped));
    }
  }

  Player _player(String set, Role role, int t, String country, bool capped) {
    var batting = 0;
    var bowling = 0;
    var keeping = 0;
    var bowlType = BowlType.none;
    final tags = <String>[];

    switch (role) {
      case Role.batter:
        batting = t;
        final partTimer = rng.chance(0.25);
        bowling = partTimer ? _int(45, 60) : _int(10, 35);
        if (partTimer) bowlType = rng.chance(0.7) ? BowlType.spin : BowlType.pace;
        if (rng.chance(0.4)) {
          tags.add('opener');
        } else if (rng.chance(0.4)) {
          tags.add('finisher');
        }
      case Role.bowler:
        bowling = t;
        batting = _int(10, 40);
        bowlType = rng.chance(0.6) ? BowlType.pace : BowlType.spin;
        if (bowlType == BowlType.pace) {
          if (rng.chance(0.3)) tags.add('death');
          if (rng.chance(0.3)) tags.add('powerplay');
        }
      case Role.allRounder:
        final primary = math.min(98, t + _int(0, 5));
        final secondary = math.max(25, t - _int(4, 16));
        final batHeavy = rng.chance(0.5);
        batting = batHeavy ? primary : secondary;
        bowling = batHeavy ? secondary : primary;
        bowlType = rng.chance(0.5) ? BowlType.pace : BowlType.spin;
        if (batHeavy && rng.chance(0.4)) tags.add('finisher');
        if (bowlType == BowlType.pace && rng.chance(0.15)) tags.add('death');
      case Role.wicketKeeper:
        batting = t;
        keeping = _int(72, 92);
        bowling = _int(5, 15);
        if (rng.chance(0.3)) {
          tags.add('opener');
        } else if (rng.chance(0.3)) {
          tags.add('finisher');
        }
    }

    final draft = Player(
      id: 'p${(_players.length + 1).toString().padLeft(3, '0')}',
      name: _name(country),
      country: country,
      overseas: country != 'India',
      capped: capped,
      role: role,
      leftHanded: rng.chance(0.3),
      bowlType: bowlType,
      batting: batting,
      bowling: bowling,
      fielding: role == Role.wicketKeeper ? keeping : _int(50, 90),
      keeping: keeping,
      form: [0, 0, 0, 0, 1, -1, 2, -2][rng.nextInt(8)],
      basePrice: 30,
      set: set,
      tags: tags,
    );
    return Player.fromJson({
      ...draft.toJson(),
      'basePrice': capped ? _cappedBase(draft.impact) : 30,
    });
  }

  int _cappedBase(int impact) {
    if (impact >= 84) return 200;
    if (impact >= 78) return 150;
    if (impact >= 72) return 100;
    if (impact >= 66) return 75;
    return 50;
  }

  String _name(String country) {
    final (first, last) = country == 'India'
        ? (_indianFirst, _indianLast)
        : _overseasNames[country]!;
    for (var attempt = 0; attempt < 200; attempt++) {
      final name = '${rng.pick(first)} ${rng.pick(last)}';
      if (_usedNames.add(name)) return name;
    }
    throw StateError('Ran out of unique names for $country');
  }

  int _int(int lo, int hi) => lo + rng.nextInt(hi - lo + 1);
}
