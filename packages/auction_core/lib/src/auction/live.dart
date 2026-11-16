import 'dart:math' as math;

import '../ai/ai_bidder.dart';
import 'engine.dart';

enum LivePhase {
  /// Player drawn from the box; card animation playing.
  revealing,
  bidding,

  /// Timer ran out and the previous team may use its RTM card.
  rtmDecision,

  /// SOLD / UNSOLD on screen before the next draw.
  result,
  finished,
}

sealed class LiveEvent {
  const LiveEvent();
}

class LotRevealed extends LiveEvent {
  const LotRevealed(this.lot);
  final Lot lot;
}

class BidPlaced extends LiveEvent {
  const BidPlaced(this.teamId, this.amount);
  final String teamId;
  final int amount;
}

class RtmOffered extends LiveEvent {
  const RtmOffered(this.offer);
  final RtmOffer offer;
}

class LotClosed extends LiveEvent {
  const LotClosed(this.result);
  final LotResult result;
}

class AuctionFinished extends LiveEvent {
  const AuctionFinished();
}

/// A team pressed Pass on the current lot.
class Passed extends LiveEvent {
  const Passed(this.teamId);
  final String teamId;
}

/// What the auction hall drives: [LiveAuction] in solo, or a replica of the
/// server's auction in a friend room. Same screen either way.
abstract interface class AuctionDriver {
  AuctionEngine get engine;
  Set<String> get humanTeamIds;
  LivePhase get phase;
  LotResult? get lastResult;
  RtmOffer? get pendingRtm;
  int get bidTimerTotalMs;
  int get bidTimeLeftMs;
  int get phaseTimeLeftMs;
  int get humanRtmTimeoutMs;
  bool get isFastForwarding;

  /// Solo only; a friend room shares one timeline.
  bool get canHoldFastForward;
  bool get holdingFastForward;
  set holdingFastForward(bool value);

  void Function(LiveEvent event)? get onEvent;
  set onEvent(void Function(LiveEvent event)? callback);

  void start();
  void advance(int realMs);
  BidRejection? humanBid(String teamId);
  void humanPass(String teamId);
  void humanRtm(String teamId, {required bool use});
}

/// Runs an [AuctionEngine] in (game) time: reveal pause, bid timer, AI teams
/// bidding after their thinking delay, Pass, hold-to-fast-forward, RTM
/// prompts. It has no clock of its own; call [advance] with elapsed time
/// (the app from its frame ticker, the room server from a periodic timer,
/// tests with fixed steps), so it behaves identically everywhere.
class LiveAuction implements AuctionDriver {
  LiveAuction({
    required this.engine,
    required List<AiBidder> bidders,
    required Set<String> humanTeamIds,
    this.revealMs = 2500,
    this.resultMs = 2200,
    this.aiRtmThinkMs = 1500,
    this.humanRtmTimeoutMs = 10000,
    this.fastForwardSpeed = 4,
    this.canHoldFastForward = true,
    this.aiThinkScale = 1.0,
    this.onEvent,
  })  : humanTeamIds = {...humanTeamIds},
        _bidders = {for (final b in bidders) b.teamId: b};

  @override
  final AuctionEngine engine;

  /// Teams controlled by people. Changes when a disconnected player's team
  /// is handed to the AI and back ([setController]).
  @override
  final Set<String> humanTeamIds;
  @override
  final bool canHoldFastForward;
  final Map<String, AiBidder> _bidders;
  final int revealMs;
  final int resultMs;
  final int aiRtmThinkMs;
  @override
  final int humanRtmTimeoutMs;
  final double fastForwardSpeed;

  /// Stretches AI thinking time (1.0 = as decided). The 3D hall uses ~1.8 so
  /// bids land at a watchable pace; always kept inside the bid timer.
  final double aiThinkScale;
  @override
  void Function(LiveEvent event)? onEvent;

  /// Game time elapsed, in ms.
  int nowMs = 0;
  @override
  LivePhase phase = LivePhase.revealing;
  @override
  LotResult? lastResult;
  @override
  RtmOffer? get pendingRtm => phase == LivePhase.rtmDecision ? engine.currentLot?.rtmOffer : null;

  /// Set while the player holds the fast-forward button.
  bool _holding = false;
  @override
  bool get holdingFastForward => _holding;
  @override
  set holdingFastForward(bool value) => _holding = canHoldFastForward && value;

  /// Every human is out of this lot, so it plays out at speed.
  bool _lotAutoFast = false;

  /// After [settleAi]: AI teams have had their say on this lot.
  bool _aiSilent = false;

  /// AI bids waiting on their thinking time (game ms when each lands).
  Iterable<({String teamId, int dueAt})> get scheduledBids =>
      [for (final e in _scheduled.entries) (teamId: e.key, dueAt: e.value.dueAt)];

  /// Share of the bid timer given to the closing after [settleAi]: a full
  /// timer, so the auctioneer can ask for more, announce the sale and call
  /// going once, twice, sold — time for a human to bid.
  static const settleTimerShare = 1.0;
  bool _started = false;
  int _phaseEndsAt = 0;
  int _bidDeadline = 0;
  final Map<String, ({int dueAt, int amount})> _scheduled = {};

  @override
  bool get isFastForwarding => holdingFastForward || _lotAutoFast;
  double get speed => _lotAutoFast ? math.max(fastForwardSpeed * 2, 8) : (holdingFastForward ? fastForwardSpeed : 1);

  @override
  int get bidTimerTotalMs =>
      (engine.inAcceleratedRound ? engine.rules.acceleratedTimerSec : engine.rules.bidTimerSec) * 1000;
  @override
  int get bidTimeLeftMs => phase == LivePhase.bidding ? math.max(0, _bidDeadline - nowMs) : 0;

  /// Time left in the current reveal / RTM decision / result pause.
  @override
  int get phaseTimeLeftMs => math.max(0, _phaseEndsAt - nowMs);

  /// Hands [teamId] to an AI ([ai] given) or back to its human (null) —
  /// e.g. after a player's connection drops for too long, and on return.
  void setController(String teamId, {AiBidder? ai}) {
    if (ai != null) {
      humanTeamIds.remove(teamId);
      _bidders[teamId] = ai;
    } else {
      _bidders.remove(teamId);
      _scheduled.remove(teamId);
      humanTeamIds.add(teamId);
    }
    if (phase == LivePhase.bidding) _scheduleAi();
  }

  bool isAi(String teamId) => _bidders.containsKey(teamId);

  @override
  void start() {
    if (_started) return;
    _started = true;
    _openNext();
  }

  /// Moves time forward by [realMs] of wall-clock time (scaled by fast-forward).
  @override
  void advance(int realMs) {
    if (!_started || phase == LivePhase.finished) return;
    var remaining = (realMs * speed).round();
    var guard = 0;
    while (remaining > 0 && phase != LivePhase.finished) {
      final next = _nextDueAt();
      if (next == null) {
        nowMs += remaining;
        return;
      }
      final step = math.min(remaining, math.max(0, next - nowMs));
      nowMs += step;
      remaining -= step;
      if (nowMs < next) return;
      _fireDue();
      if (++guard > 100000) throw StateError('LiveAuction made no progress');
    }
  }

  /// The human's Bid button: always bids exactly the next amount.
  @override
  BidRejection? humanBid(String teamId) => humanBidAt(teamId, null);

  /// A bid at [amount] (null = the next amount). A friend-room client sends
  /// the amount it saw, so a bid that crossed someone else's is rejected as
  /// stale instead of silently costing more.
  BidRejection? humanBidAt(String teamId, int? amount) {
    if (phase != LivePhase.bidding) return BidRejection.biddingClosed;
    if (!humanTeamIds.contains(teamId)) return BidRejection.unknownTeam;
    final next = engine.nextBidAmount;
    if (amount != null && amount != next) return BidRejection.staleAmount;
    final rejection = engine.placeBid(teamId, next);
    if (rejection == null) {
      _aiSilent = false;
      _afterBid(teamId, next);
    }
    return rejection;
  }

  /// Solo SKIP: the AI teams bid the lot out at once to where they'd settle
  /// (each raise goes to the quickest willing bidder, as live), then fall
  /// silent and the closing calls follow. Only a human can still bid; doing
  /// so reopens the lot to everyone. [closeNow] (solo PASS) skips the closing
  /// calls and ends the lot at once, at normal speed so the result shows.
  void settleAi({bool closeNow = false}) {
    if (phase == LivePhase.revealing) {
      phase = LivePhase.bidding;
      _checkHumansOut();
    }
    if (phase != LivePhase.bidding) return;
    _scheduled.clear();
    while (true) {
      String? fastest;
      AiDecision? decision;
      for (final e in _bidders.entries) {
        final d = e.value.decide(engine);
        if (d.bid && (decision == null || d.delayMs < decision.delayMs)) {
          fastest = e.key;
          decision = d;
        }
      }
      if (fastest == null) break;
      final rejection = engine.placeBid(fastest, decision!.amount);
      if (rejection != null) throw StateError('AI $fastest made an invalid bid: $rejection');
      _emit(BidPlaced(fastest, decision.amount));
    }
    _aiSilent = true;
    _bidDeadline = closeNow ? nowMs : nowMs + (bidTimerTotalMs * settleTimerShare).round();
    if (closeNow) _lotAutoFast = false;
  }

  /// The human's Pass button.
  @override
  void humanPass(String teamId) {
    if (phase != LivePhase.bidding && phase != LivePhase.revealing) return;
    final lot = engine.currentLot;
    if (lot == null || lot.passed.contains(teamId)) return;
    final everyoneOut = engine.pass(teamId, humanTeamIds: humanTeamIds);
    if (lot.passed.contains(teamId)) _emit(Passed(teamId));
    // Every human is out: no need to watch the rest — the AI teams settle
    // him at once and the lot goes straight to its result.
    if (everyoneOut) settleAi(closeNow: true);
  }

  /// The human's answer to "Use RTM?".
  @override
  void humanRtm(String teamId, {required bool use}) {
    final offer = pendingRtm;
    if (offer == null || offer.teamId != teamId) return;
    _close(useRtm: use);
  }

  int? _nextDueAt() => switch (phase) {
        LivePhase.revealing || LivePhase.rtmDecision || LivePhase.result => _phaseEndsAt,
        LivePhase.bidding => [
            _bidDeadline,
            for (final s in _scheduled.values) s.dueAt,
          ].reduce(math.min),
        LivePhase.finished => null,
      };

  void _fireDue() {
    switch (phase) {
      case LivePhase.revealing:
        phase = LivePhase.bidding;
        _bidDeadline = nowMs + bidTimerTotalMs;
        _checkHumansOut();
        _scheduleAi();
      case LivePhase.bidding:
        final due = _scheduled.entries
            .where((e) => e.value.dueAt <= nowMs)
            .toList()
          ..sort((a, b) {
            final c = a.value.dueAt.compareTo(b.value.dueAt);
            return c != 0 ? c : a.key.compareTo(b.key);
          });
        if (due.isNotEmpty) {
          final entry = due.first;
          _scheduled.remove(entry.key);
          if (engine.placeBid(entry.key, entry.value.amount) == null) {
            _afterBid(entry.key, entry.value.amount);
          } else {
            _scheduleOne(entry.key); // someone beat it to that amount
          }
          return;
        }
        if (nowMs >= _bidDeadline) _timerExpired();
      case LivePhase.rtmDecision:
        final offer = pendingRtm!;
        final bidder = _bidders[offer.teamId];
        // AI decides after a dramatic pause; a human who doesn't answer declines.
        _close(useRtm: bidder != null && bidder.wantsRtm(engine, offer));
      case LivePhase.result:
        _openNext();
      case LivePhase.finished:
        break;
    }
  }

  void _afterBid(String teamId, int amount) {
    _bidDeadline = nowMs + bidTimerTotalMs;
    _emit(BidPlaced(teamId, amount));
    _scheduleAi();
  }

  void _timerExpired() {
    _scheduled.clear();
    final offer = engine.endBidding();
    if (offer == null) {
      _close(useRtm: false);
      return;
    }
    phase = LivePhase.rtmDecision;
    _phaseEndsAt = nowMs + (_bidders.containsKey(offer.teamId) ? aiRtmThinkMs : humanRtmTimeoutMs);
    _emit(RtmOffered(offer));
  }

  void _close({required bool useRtm}) {
    final result = engine.closeLot(useRtm: useRtm);
    lastResult = result;
    phase = LivePhase.result;
    _phaseEndsAt = nowMs + resultMs;
    _emit(LotClosed(result));
  }

  void _openNext() {
    _scheduled.clear();
    _lotAutoFast = false;
    _aiSilent = false;
    final lot = engine.openNextLot();
    if (lot == null) {
      phase = LivePhase.finished;
      _emit(const AuctionFinished());
      return;
    }
    phase = LivePhase.revealing;
    _phaseEndsAt = nowMs + revealMs;
    _emit(LotRevealed(lot));
  }

  void _scheduleAi() {
    _scheduled.clear();
    if (_aiSilent) return;
    for (final id in _bidders.keys) {
      _scheduleOne(id);
    }
  }

  void _scheduleOne(String teamId) {
    final d = _bidders[teamId]!.decide(engine);
    if (!d.bid) return;
    final delay = aiThinkScale == 1.0
        ? d.delayMs
        : math.min((d.delayMs * aiThinkScale).round(), bidTimerTotalMs - 700);
    _scheduled[teamId] = (dueAt: nowMs + delay, amount: d.amount);
  }

  /// No human can act on this lot (all passed, or none can bid): speed it up.
  void _checkHumansOut() {
    if (humanTeamIds.isEmpty) return;
    final lot = engine.currentLot!;
    final out = humanTeamIds.every((id) {
      if (lot.passed.contains(id)) return true;
      final reason = engine.checkBid(id);
      return reason != null && reason != BidRejection.alreadyLeading;
    });
    if (out) _lotAutoFast = true;
  }

  void _emit(LiveEvent e) => onEvent?.call(e);
}
