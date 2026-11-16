import 'dart:math' as math;

enum Role {
  batter('BAT'),
  bowler('BOWL'),
  allRounder('AR'),
  wicketKeeper('WK');

  const Role(this.code);
  final String code;

  static Role fromCode(String code) => Role.values.firstWhere(
        (r) => r.code == code,
        orElse: () => throw FormatException('Unknown role "$code"'),
      );
}

enum BowlType {
  pace,
  spin,
  none;

  static BowlType fromName(String? name) => BowlType.values.firstWhere(
        (b) => b.name == name,
        orElse: () => BowlType.none,
      );
}

class Player {
  const Player({
    required this.id,
    required this.name,
    required this.country,
    required this.overseas,
    required this.capped,
    required this.role,
    required this.leftHanded,
    required this.bowlType,
    required this.batting,
    required this.bowling,
    required this.fielding,
    required this.keeping,
    required this.basePrice,
    required this.set,
    this.form = 0,
    this.tags = const [],
    this.image,
    this.starPower = 0,
    this.previousTeam,
    this.value = 0,
  });

  /// Market value in lakhs at a full ₹120 Cr purse, from real auction prices
  /// and recent form (0 = unknown: valued from skills instead). AI teams scale
  /// it to the format's purse and never bid past [maxBarFactor] times it.
  final int value;

  /// Fame / captaincy / brand, 0–100. Makes AI teams bid more; has no
  /// effect on how the player performs in the season.
  final int starPower;

  /// Franchise id this player belonged to last season (null for free
  /// agents). Decides who may retain him or use an RTM card on him.
  final String? previousTeam;

  final String id;
  final String name;
  final String country;
  final bool overseas;
  final bool capped;
  final Role role;
  final bool leftHanded;
  final BowlType bowlType;

  /// Skills, 0–100.
  final int batting;
  final int bowling;
  final int fielding;
  final int keeping;

  /// Current form, -5..+5, added to skills.
  final int form;

  /// Lakhs.
  final int basePrice;

  /// Auction set, e.g. "marquee", "batters", "uncapped".
  final String set;

  /// e.g. "opener", "finisher", "powerplay", "death".
  final List<String> tags;

  /// Image key on the content CDN. Null means show the silhouette avatar.
  final String? image;

  /// How good this player is in their role, 0–100. Drives valuation.
  int get impact {
    final hi = math.max(batting, bowling);
    final lo = math.min(batting, bowling);
    final raw = switch (role) {
      Role.batter => batting.toDouble(),
      Role.bowler => bowling.toDouble(),
      Role.allRounder => 0.55 * hi + 0.45 * lo + 4,
      Role.wicketKeeper => 0.85 * batting + 0.15 * keeping,
    };
    return (raw + form).round().clamp(0, 100);
  }

  /// Counts towards the five bowling options a playing XI needs.
  bool get isBowlingOption =>
      role == Role.bowler || role == Role.allRounder || bowling >= 55;

  bool get isKeeper => role == Role.wicketKeeper || keeping >= 75;

  bool hasTag(String tag) => tags.contains(tag);

  factory Player.fromJson(Map<String, dynamic> j) => Player(
        id: j['id'] as String,
        name: j['name'] as String,
        country: j['country'] as String,
        overseas: j['overseas'] as bool,
        capped: j['capped'] as bool? ?? true,
        role: Role.fromCode(j['role'] as String),
        leftHanded: j['leftHanded'] as bool? ?? false,
        bowlType: BowlType.fromName(j['bowlType'] as String?),
        batting: j['batting'] as int,
        bowling: j['bowling'] as int,
        fielding: j['fielding'] as int,
        keeping: j['keeping'] as int? ?? 0,
        form: j['form'] as int? ?? 0,
        basePrice: j['basePrice'] as int,
        set: j['set'] as String,
        tags: [...?(j['tags'] as List?)?.cast<String>()],
        image: j['image'] as String?,
        starPower: j['starPower'] as int? ?? 0,
        previousTeam: j['previousTeam'] as String?,
        value: j['value'] as int? ?? 0,
      );

  Map<String, dynamic> toJson() => {
        'id': id,
        'name': name,
        'country': country,
        'overseas': overseas,
        'capped': capped,
        'role': role.code,
        'leftHanded': leftHanded,
        'bowlType': bowlType.name,
        'batting': batting,
        'bowling': bowling,
        'fielding': fielding,
        'keeping': keeping,
        'form': form,
        'basePrice': basePrice,
        'set': set,
        'tags': tags,
        if (image != null) 'image': image,
        if (starPower != 0) 'starPower': starPower,
        if (previousTeam != null) 'previousTeam': previousTeam,
        if (value != 0) 'value': value,
      };

  @override
  bool operator ==(Object other) => other is Player && other.id == id;

  @override
  int get hashCode => id.hashCode;

  @override
  String toString() => '$name (${role.code}, $impact)';
}

class Franchise {
  const Franchise({
    required this.id,
    required this.name,
    required this.shortName,
    required this.city,
    required this.primaryColor,
    required this.secondaryColor,
  });

  final String id;
  final String name;
  final String shortName;
  final String city;

  /// Hex, e.g. "#1E5AA8".
  final String primaryColor;
  final String secondaryColor;

  factory Franchise.fromJson(Map<String, dynamic> j) => Franchise(
        id: j['id'] as String,
        name: j['name'] as String,
        shortName: j['shortName'] as String,
        city: j['city'] as String,
        primaryColor: j['primaryColor'] as String,
        secondaryColor: j['secondaryColor'] as String,
      );

  Map<String, dynamic> toJson() => {
        'id': id,
        'name': name,
        'shortName': shortName,
        'city': city,
        'primaryColor': primaryColor,
        'secondaryColor': secondaryColor,
      };
}

/// While the current bid is below [below] lakhs, raises go up by [step].
/// The last step has no [below] and applies to everything above.
class IncrementStep {
  const IncrementStep({this.below, required this.step});

  final int? below;
  final int step;

  factory IncrementStep.fromJson(Map<String, dynamic> j) =>
      IncrementStep(below: j['below'] as int?, step: j['step'] as int);

  Map<String, dynamic> toJson() => {
        if (below != null) 'below': below,
        'step': step,
      };
}

/// One auction length (quick / standard / mega).
class AuctionFormat {
  const AuctionFormat({
    required this.id,
    required this.label,
    required this.purse,
    required this.minSquad,
    required this.maxSquad,
    required this.maxOverseas,
    this.maxLots,
    this.rating,
    this.retention,
    this.retentionOnly = false,
  });

  /// Overrides [AuctionRules.retention] for this format.
  final RetentionRules? retention;

  /// Always played with retentions (a mini auction): no fresh-auction option.
  final bool retentionOnly;

  /// Overrides [AuctionRules.rating] for this format (Quick squads are
  /// weaker by design, so they're graded on their own curve).
  final RatingScale? rating;

  final String id;
  final String label;

  /// Lakhs.
  final int purse;
  final int minSquad;
  final int maxSquad;
  final int maxOverseas;

  /// Players put up for auction; null means the whole pool. Squads still
  /// short after the auction are filled from the rest of the pool.
  final int? maxLots;

  factory AuctionFormat.fromJson(String id, Map<String, dynamic> j) =>
      AuctionFormat(
        id: id,
        label: j['label'] as String,
        purse: j['purse'] as int,
        minSquad: j['minSquad'] as int,
        maxSquad: j['maxSquad'] as int,
        maxOverseas: j['maxOverseas'] as int,
        maxLots: j['maxLots'] as int?,
        rating: j['rating'] == null
            ? null
            : RatingScale.fromJson(j['rating'] as Map<String, dynamic>),
        retention: j['retention'] == null
            ? null
            : RetentionRules.fromJson(j['retention'] as Map<String, dynamic>),
        retentionOnly: j['retentionOnly'] as bool? ?? false,
      );

  Map<String, dynamic> toJson() => {
        'label': label,
        'purse': purse,
        'minSquad': minSquad,
        'maxSquad': maxSquad,
        'maxOverseas': maxOverseas,
        if (maxLots != null) 'maxLots': maxLots,
        if (rating != null) 'rating': rating!.toJson(),
        if (retention != null) 'retention': retention!.toJson(),
        if (retentionOnly) 'retentionOnly': true,
      };
}

/// Maps a playing XI's raw weighted skill to a 0–100 rating. Lives in the
/// server data so grades can be re-tuned when the player pool changes.
class RatingScale {
  const RatingScale({
    this.battingBase = 45,
    this.bowlingBase = 50,
    this.spread = 36,
  });

  /// Raw strength that scores 0.
  final double battingBase;
  final double bowlingBase;

  /// Raw points between a score of 0 and 100.
  final double spread;

  factory RatingScale.fromJson(Map<String, dynamic>? j) => j == null
      ? const RatingScale()
      : RatingScale(
          battingBase: (j['battingBase'] as num).toDouble(),
          bowlingBase: (j['bowlingBase'] as num).toDouble(),
          spread: (j['spread'] as num).toDouble(),
        );

  Map<String, dynamic> toJson() => {
        'battingBase': battingBase,
        'bowlingBase': bowlingBase,
        'spread': spread,
      };
}

/// Retention mode: before the auction each team may keep up to
/// [maxPlayers] of last season's squad. Capped players cost [slabs] in order
/// (most valuable pays the first slab); uncapped cost [uncappedPrice].
/// Prices are for a ₹120 Cr purse and scale with the format's purse.
class RetentionRules {
  const RetentionRules({
    this.maxPlayers = 3,
    this.slabs = const [1800, 1400, 1100],
    this.uncappedPrice = 400,
    this.atValue = false,
  });

  final int maxPlayers;
  final List<int> slabs;
  final int uncappedPrice;

  /// Retained players keep their current price (market value) instead of
  /// paying the slabs, as in a mini auction.
  final bool atValue;

  factory RetentionRules.fromJson(Map<String, dynamic>? j) => j == null
      ? const RetentionRules()
      : RetentionRules(
          maxPlayers: j['maxPlayers'] as int,
          slabs: [...?(j['slabs'] as List?)?.cast<int>()],
          uncappedPrice: j['uncappedPrice'] as int? ?? 0,
          atValue: j['atValue'] as bool? ?? false,
        );

  Map<String, dynamic> toJson() => {
        'maxPlayers': maxPlayers,
        'slabs': slabs,
        'uncappedPrice': uncappedPrice,
        if (atValue) 'atValue': true,
      };
}

class AuctionRules {
  const AuctionRules({
    required this.increments,
    required this.basePrices,
    required this.bidTimerSec,
    required this.acceleratedTimerSec,
    required this.maxOverseasInXI,
    required this.setOrder,
    required this.formats,
    this.rating = const RatingScale(),
    this.retention = const RetentionRules(),
    this.rtmCards = 1,
    this.maxPrice,
    this.roomMaxPrice,
  });

  /// No player may sell above this (lakhs), whatever the format. Null = no cap.
  final int? maxPrice;

  /// The cap in friend rooms, if different (null = [maxPrice]).
  final int? roomMaxPrice;

  final RatingScale rating;
  final RetentionRules retention;

  /// Right-to-Match cards each team gets per auction.
  final int rtmCards;

  final List<IncrementStep> increments;

  /// Allowed base prices in lakhs, highest first.
  final List<int> basePrices;
  final int bidTimerSec;
  final int acceleratedTimerSec;
  final int maxOverseasInXI;
  final List<String> setOrder;
  final Map<String, AuctionFormat> formats;

  int get minBasePrice => basePrices.reduce(math.min);

  RatingScale ratingFor(AuctionFormat format) => format.rating ?? rating;

  RetentionRules retentionFor(AuctionFormat format) => format.retention ?? retention;

  int incrementFor(int currentBid) {
    for (final s in increments) {
      if (s.below == null || currentBid < s.below!) return s.step;
    }
    return increments.last.step;
  }

  /// The only amount a team may bid next: the base price if nobody has bid,
  /// otherwise the current bid plus one increment.
  int nextBid(int? currentBid, int basePrice) =>
      currentBid == null ? basePrice : currentBid + incrementFor(currentBid);

  factory AuctionRules.fromJson(Map<String, dynamic> j) => AuctionRules(
        increments: [
          for (final s in j['increments'] as List)
            IncrementStep.fromJson(s as Map<String, dynamic>),
        ],
        basePrices: (j['basePrices'] as List).cast<int>(),
        bidTimerSec: j['bidTimerSec'] as int,
        acceleratedTimerSec: j['acceleratedTimerSec'] as int,
        maxOverseasInXI: j['maxOverseasInXI'] as int,
        setOrder: (j['setOrder'] as List).cast<String>(),
        formats: {
          for (final e in (j['formats'] as Map<String, dynamic>).entries)
            e.key: AuctionFormat.fromJson(e.key, e.value as Map<String, dynamic>),
        },
        rating: RatingScale.fromJson(j['rating'] as Map<String, dynamic>?),
        retention: RetentionRules.fromJson(j['retention'] as Map<String, dynamic>?),
        rtmCards: j['rtmCards'] as int? ?? 1,
        maxPrice: j['maxPrice'] as int?,
        roomMaxPrice: j['roomMaxPrice'] as int?,
      );

  Map<String, dynamic> toJson() => {
        'increments': [for (final s in increments) s.toJson()],
        'basePrices': basePrices,
        'bidTimerSec': bidTimerSec,
        'acceleratedTimerSec': acceleratedTimerSec,
        'maxOverseasInXI': maxOverseasInXI,
        'setOrder': setOrder,
        'formats': {for (final f in formats.values) f.id: f.toJson()},
        'rating': rating.toJson(),
        'retention': retention.toJson(),
        'rtmCards': rtmCards,
        if (maxPrice != null) 'maxPrice': maxPrice,
        if (roomMaxPrice != null) 'roomMaxPrice': roomMaxPrice,
      };
}

/// Everything the server publishes: league branding, teams, players, rules.
class Dataset {
  const Dataset({
    required this.version,
    required this.leagueName,
    required this.leagueShortName,
    required this.franchises,
    required this.players,
    required this.rules,
  });

  final int version;
  final String leagueName;
  final String leagueShortName;
  final List<Franchise> franchises;
  final List<Player> players;
  final AuctionRules rules;

  factory Dataset.fromJson(Map<String, dynamic> j) {
    final league = j['league'] as Map<String, dynamic>;
    return Dataset(
      version: j['version'] as int,
      leagueName: league['name'] as String,
      leagueShortName: league['shortName'] as String,
      franchises: [
        for (final f in j['franchises'] as List)
          Franchise.fromJson(f as Map<String, dynamic>),
      ],
      players: [
        for (final p in j['players'] as List)
          Player.fromJson(p as Map<String, dynamic>),
      ],
      rules: AuctionRules.fromJson(j['rules'] as Map<String, dynamic>),
    );
  }

  Map<String, dynamic> toJson() => {
        'version': version,
        'league': {'name': leagueName, 'shortName': leagueShortName},
        'franchises': [for (final f in franchises) f.toJson()],
        'players': [for (final p in players) p.toJson()],
        'rules': rules.toJson(),
      };

  /// Problems that would break an auction. Empty means the data is usable.
  List<String> validate() {
    final problems = <String>[];
    final ids = <String>{};
    for (final p in players) {
      if (!ids.add(p.id)) problems.add('Duplicate player id ${p.id}');
      if (!rules.basePrices.contains(p.basePrice)) {
        problems.add('${p.id}: base price ${p.basePrice} is not in basePrices');
      }
      for (final skill in [p.batting, p.bowling, p.fielding, p.keeping]) {
        if (skill < 0 || skill > 100) problems.add('${p.id}: skill out of 0–100');
      }
    }
    final teamIds = <String>{};
    for (final f in franchises) {
      if (!teamIds.add(f.id)) problems.add('Duplicate franchise id ${f.id}');
    }
    for (final p in players) {
      if (p.previousTeam != null && !teamIds.contains(p.previousTeam)) {
        problems.add('${p.id}: previousTeam ${p.previousTeam} is not a franchise');
      }
      if (p.starPower < 0 || p.starPower > 100) problems.add('${p.id}: starPower out of 0–100');
    }
    for (final f in rules.formats.values) {
      if (f.minSquad < 11) problems.add('${f.id}: minSquad must be at least 11');
      if (f.maxSquad < f.minSquad) problems.add('${f.id}: maxSquad < minSquad');
      final needed = franchises.length * f.minSquad;
      if (players.length < needed) {
        problems.add('${f.id}: pool has ${players.length} players, needs $needed');
      }
      if (f.purse < f.minSquad * rules.minBasePrice) {
        problems.add('${f.id}: purse cannot cover a minimum squad');
      }
    }
    if (rules.increments.isEmpty || rules.increments.last.below != null) {
      problems.add('increments must end with an open-ended step');
    }
    return problems;
  }
}
