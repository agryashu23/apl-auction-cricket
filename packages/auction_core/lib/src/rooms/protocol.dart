import '../auction/engine.dart';
import '../auction/live.dart';
import '../models.dart';

/// Bump when the room messages or the replay rules change; the server and
/// app must agree (the app shows "update the app" otherwise).
const roomProtocolVersion = 1;

/// Everything a phone needs to build the same engine the server runs.
class RoomSetup {
  const RoomSetup({
    required this.seed,
    required this.seasonSeed,
    required this.formatId,
    required this.retention,
    required this.retentions,
    required this.dataVersion,
  });

  final int seed;
  final int seasonSeed;
  final String formatId;
  final bool retention;

  /// Team id → retained player ids (empty for a fresh auction).
  final Map<String, List<String>> retentions;
  final int dataVersion;

  AuctionEngine buildEngine(Dataset data) {
    final byId = {for (final p in data.players) p.id: p};
    return AuctionEngine(
      rules: data.rules,
      format: data.rules.formats[formatId]!,
      franchises: data.franchises,
      pool: data.players,
      seed: seed,
      retentions: {
        for (final e in retentions.entries) e.key: [for (final id in e.value) byId[id]!],
      },
      // Friend rooms play to their own (higher) price cap.
      maxPrice: data.rules.roomMaxPrice ?? data.rules.maxPrice,
    );
  }

  factory RoomSetup.fromJson(Map<String, dynamic> j) => RoomSetup(
        seed: j['seed'] as int,
        seasonSeed: j['seasonSeed'] as int,
        formatId: j['format'] as String,
        retention: j['retention'] as bool,
        retentions: {
          for (final e in (j['retentions'] as Map<String, dynamic>).entries)
            e.key: (e.value as List).cast<String>(),
        },
        dataVersion: j['dataVersion'] as int,
      );

  Map<String, dynamic> toJson() => {
        'seed': seed,
        'seasonSeed': seasonSeed,
        'format': formatId,
        'retention': retention,
        'retentions': retentions,
        'dataVersion': dataVersion,
      };
}

/// One change the server made to its engine. The server broadcasts these in
/// order; a phone applies them to its own engine and ends up identical.
sealed class RoomAction {
  const RoomAction();

  Map<String, dynamic> toJson();

  static RoomAction fromJson(Map<String, dynamic> j) => switch (j['a']) {
        'open' => OpenLotAction(j['p'] as String?),
        'bid' => BidAction(j['team'] as String, j['amount'] as int),
        'pass' => PassAction(j['team'] as String),
        'end' => const EndBiddingAction(),
        'close' => CloseLotAction(rtm: j['rtm'] as bool),
        final other => throw FormatException('Unknown room action $other'),
      };

  /// The action a [LiveEvent] stands for (every engine change emits one).
  static RoomAction forEvent(LiveEvent e) => switch (e) {
        LotRevealed(:final lot) => OpenLotAction(lot.player.id),
        AuctionFinished() => const OpenLotAction(null),
        BidPlaced(:final teamId, :final amount) => BidAction(teamId, amount),
        Passed(:final teamId) => PassAction(teamId),
        RtmOffered() => const EndBiddingAction(),
        LotClosed(:final result) => CloseLotAction(rtm: result.viaRtm),
      };
}

/// The next lot opened with player [playerId]; null means the auction ended.
class OpenLotAction extends RoomAction {
  const OpenLotAction(this.playerId);
  final String? playerId;

  @override
  Map<String, dynamic> toJson() => {'a': 'open', 'p': playerId};
}

class BidAction extends RoomAction {
  const BidAction(this.teamId, this.amount);
  final String teamId;
  final int amount;

  @override
  Map<String, dynamic> toJson() => {'a': 'bid', 'team': teamId, 'amount': amount};
}

class PassAction extends RoomAction {
  const PassAction(this.teamId);
  final String teamId;

  @override
  Map<String, dynamic> toJson() => {'a': 'pass', 'team': teamId};
}

/// Timer ran out and an RTM offer is pending.
class EndBiddingAction extends RoomAction {
  const EndBiddingAction();

  @override
  Map<String, dynamic> toJson() => {'a': 'end'};
}

class CloseLotAction extends RoomAction {
  const CloseLotAction({required this.rtm});
  final bool rtm;

  @override
  Map<String, dynamic> toJson() => {'a': 'close', 'rtm': rtm};
}

/// The replica disagrees with the server (different data or app version).
class RoomDesync implements Exception {
  RoomDesync(this.message);
  final String message;

  @override
  String toString() => 'RoomDesync: $message';
}

/// Applies [action] to [engine] and returns the matching event for the UI.
LiveEvent applyRoomAction(AuctionEngine engine, RoomAction action) {
  switch (action) {
    case OpenLotAction(:final playerId):
      final lot = engine.openNextLot();
      if (lot?.player.id != playerId) {
        throw RoomDesync('expected lot $playerId, got ${lot?.player.id}');
      }
      return lot == null ? const AuctionFinished() : LotRevealed(lot);
    case BidAction(:final teamId, :final amount):
      final rejection = engine.placeBid(teamId, amount);
      if (rejection != null) throw RoomDesync('bid $teamId $amount rejected: $rejection');
      return BidPlaced(teamId, amount);
    case PassAction(:final teamId):
      engine.pass(teamId, humanTeamIds: const {});
      return Passed(teamId);
    case EndBiddingAction():
      final offer = engine.endBidding();
      if (offer == null) throw RoomDesync('server had an RTM offer, replica has none');
      return RtmOffered(offer);
    case CloseLotAction(:final rtm):
      return LotClosed(engine.closeLot(useRtm: rtm));
  }
}

/// A phone's copy of the server's auction. It never decides anything:
/// server actions update the engine, server clocks set the phase and
/// timers, and the player's buttons are sent to the server via [send].
class RemoteAuction implements AuctionDriver {
  RemoteAuction({required this.data, required this.send});

  final Dataset data;
  final void Function(Map<String, dynamic> message) send;

  RoomSetup? setup;
  AuctionEngine? _engine;

  @override
  AuctionEngine get engine => _engine ?? (throw StateError('Auction not started'));

  bool get isLoaded => _engine != null;

  @override
  Set<String> humanTeamIds = {};
  @override
  LivePhase phase = LivePhase.revealing;
  @override
  LotResult? lastResult;
  @override
  int bidTimerTotalMs = 10000;
  int _bidLeft = 0;
  int _phaseLeft = 0;
  @override
  int humanRtmTimeoutMs = 10000;
  bool _fast = false;

  @override
  void Function(LiveEvent event)? onEvent;

  @override
  int get bidTimeLeftMs => phase == LivePhase.bidding ? _bidLeft : 0;
  @override
  int get phaseTimeLeftMs => _phaseLeft;
  @override
  RtmOffer? get pendingRtm => phase == LivePhase.rtmDecision ? _engine?.currentLot?.rtmOffer : null;
  @override
  bool get isFastForwarding => _fast;
  @override
  bool get canHoldFastForward => false;
  @override
  bool get holdingFastForward => false;
  @override
  set holdingFastForward(bool value) {}

  /// (Re)builds the replica from the setup and every action so far. Used
  /// when the auction starts and after a reconnect.
  void load(RoomSetup s, List<RoomAction> log) {
    setup = s;
    final engine = s.buildEngine(data);
    LotResult? last;
    for (final a in log) {
      final e = applyRoomAction(engine, a);
      if (e is LotClosed) last = e.result;
    }
    _engine = engine;
    lastResult = last;
    phase = engine.isFinished ? LivePhase.finished : LivePhase.revealing;
  }

  /// One live server action.
  void apply(RoomAction action) {
    final e = applyRoomAction(engine, action);
    switch (e) {
      case LotRevealed():
        phase = LivePhase.revealing;
      case AuctionFinished():
        phase = LivePhase.finished;
      case BidPlaced():
        _bidLeft = bidTimerTotalMs;
      case RtmOffered():
        phase = LivePhase.rtmDecision;
      case LotClosed(:final result):
        lastResult = result;
        phase = LivePhase.result;
      case Passed():
        break;
    }
    onEvent?.call(e);
  }

  /// The server's clock: authoritative phase and timers.
  void clock(Map<String, dynamic> m) {
    phase = LivePhase.values.byName(m['phase'] as String);
    _bidLeft = m['bid'] as int;
    bidTimerTotalMs = m['bidTotal'] as int;
    _phaseLeft = m['left'] as int;
    humanRtmTimeoutMs = m['rtmTotal'] as int;
    _fast = m['ff'] as bool;
    humanTeamIds = {...(m['humans'] as List).cast<String>()};
  }

  @override
  void start() {}

  /// Counts the timers down between server clocks so they move smoothly.
  @override
  void advance(int realMs) {
    final ms = _fast ? realMs * 8 : realMs;
    if (phase == LivePhase.bidding) {
      _bidLeft = (_bidLeft - ms).clamp(0, bidTimerTotalMs);
    } else {
      _phaseLeft = (_phaseLeft - ms).clamp(0, 1 << 30);
    }
  }

  @override
  BidRejection? humanBid(String teamId) {
    if (phase != LivePhase.bidding || !isLoaded) return BidRejection.biddingClosed;
    final local = engine.checkBid(teamId);
    if (local != null) return local;
    send({'t': 'bid', 'amount': engine.nextBidAmount});
    return null;
  }

  @override
  void humanPass(String teamId) => send({'t': 'pass'});

  @override
  void humanRtm(String teamId, {required bool use}) => send({'t': 'rtm', 'use': use});
}

/// The server's clock message for [live].
Map<String, dynamic> clockMessage(LiveAuction live) => {
      't': 'clock',
      'phase': live.phase.name,
      'bid': live.bidTimeLeftMs,
      'bidTotal': live.bidTimerTotalMs,
      'left': live.phaseTimeLeftMs,
      'rtmTotal': live.humanRtmTimeoutMs,
      'ff': live.isFastForwarding,
      'humans': live.humanTeamIds.toList()..sort(),
    };
