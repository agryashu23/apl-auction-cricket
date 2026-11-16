using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core
{
    public enum LivePhase { Revealing, Bidding, RtmDecision, Result, Finished }

    public abstract class LiveEvent { }

    public sealed class LotRevealed : LiveEvent
    {
        public LotRevealed(Lot lot) { Lot = lot; }
        public Lot Lot { get; }
    }

    public sealed class BidPlaced : LiveEvent
    {
        public BidPlaced(string teamId, int amount)
        {
            TeamId = teamId;
            Amount = amount;
        }

        public string TeamId { get; }
        public int Amount { get; }
    }

    public sealed class RtmOffered : LiveEvent
    {
        public RtmOffered(RtmOffer offer) { Offer = offer; }
        public RtmOffer Offer { get; }
    }

    public sealed class LotClosed : LiveEvent
    {
        public LotClosed(LotResult result, bool forcedUnsold = false)
        {
            Result = result;
            ForcedUnsold = forcedUnsold;
        }

        public LotResult Result { get; }

        /// <summary>A room host closed the lot unsold whatever the bids (replicas drop the bids first).</summary>
        public bool ForcedUnsold { get; }
    }

    public sealed class AuctionFinished : LiveEvent { }

    public sealed class Passed : LiveEvent
    {
        public Passed(string teamId) { TeamId = teamId; }
        public string TeamId { get; }
    }

    /// <summary>What the hall drives: LiveAuction (solo) or RemoteAuction (friend room).</summary>
    public interface IAuctionDriver
    {
        AuctionEngine Engine { get; }
        ICollection<string> HumanTeamIds { get; }
        LivePhase Phase { get; }
        LotResult? LastResult { get; }
        RtmOffer? PendingRtm { get; }
        int BidTimerTotalMs { get; }
        int BidTimeLeftMs { get; }
        int PhaseTimeLeftMs { get; }
        int HumanRtmTimeoutMs { get; }
        bool IsFastForwarding { get; }
        bool CanHoldFastForward { get; }
        bool HoldingFastForward { get; set; }
        event Action<LiveEvent>? OnEvent;
        void Start();
        void Advance(int realMs);
        BidRejection? HumanBid(string teamId);
        void HumanPass(string teamId);
        void HumanRtm(string teamId, bool use);
    }

    /// <summary>
    /// Runs an AuctionEngine in game time (port of the Dart LiveAuction). No
    /// clock of its own: call Advance with elapsed ms. Timings are fields so
    /// the 3D hall can slow the pacing to match its camera and voice cues.
    /// </summary>
    public sealed class LiveAuction : IAuctionDriver
    {
        private readonly OrderedMap<string, AiBidder> _bidders = new OrderedMap<string, AiBidder>();
        private readonly OrderedMap<string, (int DueAt, int Amount)> _scheduled = new OrderedMap<string, (int, int)>();
        private bool _holding;
        private bool _lotAutoFast;

        /// <summary>After SettleAi: the AI teams have had their say on this lot.</summary>
        private bool _aiSilent;

        /// <summary>AI bids waiting on their thinking time (game ms when each lands). Read-only view for the hall.</summary>
        public IEnumerable<(string TeamId, int DueAt)> ScheduledBids => _scheduled.Keys.ToList().Select(k => (k, _scheduled[k].DueAt));

        /// <summary>Share of the bid timer given to the closing after SettleAi (a full timer: time to bid).</summary>
        public const double SettleTimerShare = 1.0;
        private bool _started;
        private int _phaseEndsAt;
        private int _bidDeadline;

        public LiveAuction(
            AuctionEngine engine,
            IEnumerable<AiBidder> bidders,
            IEnumerable<string> humanTeamIds,
            int revealMs = 2500,
            int resultMs = 2200,
            int aiRtmThinkMs = 1500,
            int humanRtmTimeoutMs = 10000,
            double fastForwardSpeed = 4,
            bool canHoldFastForward = true,
            double aiThinkScale = 1.0)
        {
            AiThinkScale = aiThinkScale;
            Engine = engine;
            foreach (var b in bidders) _bidders[b.TeamId] = b;
            HumanTeamIds = new HashSet<string>(humanTeamIds);
            RevealMs = revealMs;
            ResultMs = resultMs;
            AiRtmThinkMs = aiRtmThinkMs;
            HumanRtmTimeoutMs = humanRtmTimeoutMs;
            FastForwardSpeed = fastForwardSpeed;
            CanHoldFastForward = canHoldFastForward;
        }

        public AuctionEngine Engine { get; }
        public ICollection<string> HumanTeamIds { get; }
        public int RevealMs { get; }
        public int ResultMs { get; }
        public int AiRtmThinkMs { get; }
        public int HumanRtmTimeoutMs { get; }
        public double FastForwardSpeed { get; }

        /// <summary>Stretches AI thinking time (1.0 = as decided); kept inside the bid timer. Same rule as Dart.</summary>
        public double AiThinkScale { get; }
        public bool CanHoldFastForward { get; }
        public event Action<LiveEvent>? OnEvent;

        public int NowMs { get; private set; }
        public LivePhase Phase { get; private set; } = LivePhase.Revealing;
        public LotResult? LastResult { get; private set; }
        public RtmOffer? PendingRtm => Phase == LivePhase.RtmDecision ? Engine.CurrentLot?.RtmOffer : null;

        public bool HoldingFastForward
        {
            get => _holding;
            set => _holding = CanHoldFastForward && value;
        }

        public bool IsFastForwarding => HoldingFastForward || _lotAutoFast;

        public double Speed => _lotAutoFast ? Math.Max(FastForwardSpeed * 2, 8) : (HoldingFastForward ? FastForwardSpeed : 1);

        public int BidTimerTotalMs
        {
            get
            {
                var rules = Engine.Rules;
                if (BidTimerSec == null) return (Engine.InAcceleratedRound ? rules.AcceleratedTimerSec : rules.BidTimerSec) * 1000;
                if (!Engine.InAcceleratedRound) return BidTimerSec.Value * 1000;
                // The accelerated round keeps the rules' ratio (10 s -> 6 s): 15 s -> 9 s, 20 s -> 12 s.
                return Math.Max(3, DartMath.Round(BidTimerSec.Value * (double)rules.AcceleratedTimerSec / Math.Max(1, rules.BidTimerSec))) * 1000;
            }
        }

        /// <summary>Friend rooms: the host's bid timer (seconds); null = the rules' timer (solo).</summary>
        public int? BidTimerSec { get; set; }

        /// <summary>
        /// Friend rooms (protocol v2): a PASS only takes that team out of this lot. The lot closes
        /// early once every human team is settled (passed, leading or unable to bid) and no AI bid
        /// is pending; otherwise the bid timer runs out as usual. Off (solo): a PASS by the last
        /// human settles the lot at once (SettleAi).
        /// </summary>
        public bool PassConsensus { get; set; }
        public int BidTimeLeftMs => Phase == LivePhase.Bidding ? Math.Max(0, _bidDeadline - NowMs) : 0;
        public int PhaseTimeLeftMs => Math.Max(0, _phaseEndsAt - NowMs);

        public bool IsAi(string teamId) => _bidders.ContainsKey(teamId);

        public void SetController(string teamId, AiBidder? ai)
        {
            if (ai != null)
            {
                HumanTeamIds.Remove(teamId);
                _bidders[teamId] = ai;
            }
            else
            {
                _bidders.Remove(teamId);
                _scheduled.Remove(teamId);
                HumanTeamIds.Add(teamId);
            }
            if (Phase == LivePhase.Bidding)
            {
                ScheduleAi();
                CloseOnConsensus();
            }
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            OpenNext();
        }

        public void Advance(int realMs)
        {
            if (!_started || Phase == LivePhase.Finished) return;
            var remaining = DartMath.Round(realMs * Speed);
            var guard = 0;
            while (remaining > 0 && Phase != LivePhase.Finished)
            {
                var next = NextDueAt();
                if (next == null)
                {
                    NowMs += remaining;
                    return;
                }
                var step = Math.Min(remaining, Math.Max(0, next.Value - NowMs));
                NowMs += step;
                remaining -= step;
                if (NowMs < next.Value) return;
                FireDue();
                if (++guard > 100000) throw new InvalidOperationException("LiveAuction made no progress");
            }
        }

        public BidRejection? HumanBid(string teamId) => HumanBidAt(teamId, null);

        public BidRejection? HumanBidAt(string teamId, int? amount)
        {
            if (Phase != LivePhase.Bidding) return BidRejection.BiddingClosed;
            if (!HumanTeamIds.Contains(teamId)) return BidRejection.UnknownTeam;
            var next = Engine.NextBidAmount;
            if (amount != null && amount.Value != next) return BidRejection.StaleAmount;
            var rejection = Engine.PlaceBid(teamId, next);
            if (rejection == null)
            {
                _aiSilent = false;
                AfterBid(teamId, next);
                CloseOnConsensus();
            }
            return rejection;
        }

        /// <summary>
        /// Solo SKIP: the AI teams bid the lot out at once to where they'd
        /// settle (each raise to the quickest willing bidder, as live), then
        /// fall silent and the closing calls follow. Only a human can still
        /// bid; doing so reopens the lot to everyone. closeNow (solo PASS) skips
        /// the closing calls and ends the lot at once, at normal speed.
        /// </summary>
        public void SettleAi(bool closeNow = false)
        {
            if (Phase == LivePhase.Revealing)
            {
                Phase = LivePhase.Bidding;
                CheckHumansOut();
            }
            if (Phase != LivePhase.Bidding) return;
            _scheduled.Clear();
            while (true)
            {
                string? fastest = null;
                var decision = AiDecision.Pass;
                foreach (var id in _bidders.Keys.ToList())
                {
                    var d = _bidders[id].Decide(Engine);
                    if (d.Bid && (fastest == null || d.DelayMs < decision.DelayMs))
                    {
                        fastest = id;
                        decision = d;
                    }
                }
                if (fastest == null) break;
                var rejection = Engine.PlaceBid(fastest, decision.Amount);
                if (rejection != null) throw new InvalidOperationException($"AI {fastest} made an invalid bid: {rejection}");
                Emit(new BidPlaced(fastest, decision.Amount));
            }
            _aiSilent = true;
            _bidDeadline = closeNow ? NowMs : NowMs + DartMath.Round(BidTimerTotalMs * SettleTimerShare);
            if (closeNow) _lotAutoFast = false;
        }

        /// <summary>
        /// SIMULATE (solo): finishes the whole auction now with the AI. Human teams get an
        /// AutoPilot bidder (fills to the minimum squad and role gaps within their purse) when
        /// <paramref name="autoBidForHumans"/>, else they stop bidding and decline any RTM.
        /// No per-lot events: just AuctionFinished at the end (the engine's Results carry every lot).
        /// </summary>
        public void SimulateToEnd(bool autoBidForHumans, long seed)
        {
            if (Phase == LivePhase.Finished) return;
            _started = true;
            _scheduled.Clear();
            var bidders = _bidders.Values.ToList();
            if (autoBidForHumans)
            {
                var rng = new Rng(seed);
                foreach (var h in HumanTeamIds) bidders.Add(Ai.AutoPilot(h, rng.NextUint32()));
            }
            Ai.FinishAuction(Engine, bidders);
            LastResult = Engine.Results.Count > 0 ? Engine.Results[Engine.Results.Count - 1] : null;
            Phase = LivePhase.Finished;
            Emit(new AuctionFinished());
        }

        public void HumanPass(string teamId)
        {
            if (Phase != LivePhase.Bidding && Phase != LivePhase.Revealing) return;
            var lot = Engine.CurrentLot;
            if (lot == null || lot.Passed.Contains(teamId)) return;
            var everyoneOut = Engine.Pass(teamId, HumanTeamIds);
            if (lot.Passed.Contains(teamId)) Emit(new Passed(teamId));
            if (PassConsensus)
            {
                // Rooms: out of this lot only; the lot ends early only by consensus (or the timer).
                CloseOnConsensus();
                return;
            }
            // Every human is out: the AI teams settle him at once, straight to the result.
            if (everyoneOut) SettleAi(closeNow: true);
        }

        public void HumanRtm(string teamId, bool use)
        {
            var offer = PendingRtm;
            if (offer == null || offer.TeamId != teamId) return;
            Close(use);
        }

        private int? NextDueAt()
        {
            switch (Phase)
            {
                case LivePhase.Revealing:
                case LivePhase.RtmDecision:
                case LivePhase.Result:
                    return _phaseEndsAt;
                case LivePhase.Bidding:
                    var min = _bidDeadline;
                    foreach (var s in _scheduled.Values) min = Math.Min(min, s.DueAt);
                    return min;
                default:
                    return null;
            }
        }

        private void FireDue()
        {
            switch (Phase)
            {
                case LivePhase.Revealing:
                    Phase = LivePhase.Bidding;
                    _bidDeadline = NowMs + BidTimerTotalMs;
                    CheckHumansOut();
                    ScheduleAi();
                    CloseOnConsensus();
                    break;
                case LivePhase.Bidding:
                    var due = _scheduled.Where(e => e.Value.DueAt <= NowMs).ToList();
                    due.Sort((a, b) =>
                    {
                        var c = a.Value.DueAt.CompareTo(b.Value.DueAt);
                        return c != 0 ? c : DartMath.Compare(a.Key, b.Key);
                    });
                    if (due.Count > 0)
                    {
                        var entry = due[0];
                        _scheduled.Remove(entry.Key);
                        if (Engine.PlaceBid(entry.Key, entry.Value.Amount) == null) AfterBid(entry.Key, entry.Value.Amount);
                        else ScheduleOne(entry.Key);
                        CloseOnConsensus();
                        return;
                    }
                    if (NowMs >= _bidDeadline) TimerExpired();
                    break;
                case LivePhase.RtmDecision:
                    var offer = PendingRtm!;
                    var bidder = _bidders.TryGetValue(offer.TeamId, out var bd) ? bd : null;
                    Close(bidder != null && bidder.WantsRtm(Engine, offer));
                    break;
                case LivePhase.Result:
                    OpenNext();
                    break;
            }
        }

        private void AfterBid(string teamId, int amount)
        {
            _bidDeadline = NowMs + BidTimerTotalMs;
            Emit(new BidPlaced(teamId, amount));
            ScheduleAi();
        }

        private void TimerExpired()
        {
            _scheduled.Clear();
            var offer = Engine.EndBidding();
            if (offer == null)
            {
                Close(false);
                return;
            }
            Phase = LivePhase.RtmDecision;
            _phaseEndsAt = NowMs + (_bidders.ContainsKey(offer.TeamId) ? AiRtmThinkMs : HumanRtmTimeoutMs);
            Emit(new RtmOffered(offer));
        }

        private void Close(bool useRtm, bool forcedUnsold = false)
        {
            var result = Engine.CloseLot(useRtm);
            LastResult = result;
            Phase = LivePhase.Result;
            _phaseEndsAt = NowMs + ResultMs;
            Emit(new LotClosed(result, forcedUnsold));
        }

        // ------------------------------------------------------------ friend rooms

        /// <summary>Rooms: every human team is settled and no AI bid is pending, so the lot ends now.</summary>
        public bool ConsensusReached =>
            Phase == LivePhase.Bidding && _scheduled.Count == 0 && HumanTeamIds.All(id => Engine.CheckBid(id) != null);

        private void CloseOnConsensus()
        {
            if (PassConsensus && ConsensusReached) TimerExpired();
        }

        /// <summary>
        /// Room host: close the open lot now. Sold: bidding ends at the current high bid (an RTM
        /// offer still follows when the rules give one; in the RTM phase the offer is declined).
        /// Unsold: the lot closes with no buyer whatever the bids. False when no lot is open.
        /// </summary>
        public bool ForceClose(bool sold)
        {
            if (!_started || Engine.CurrentLot == null) return false;
            switch (Phase)
            {
                case LivePhase.Revealing:
                case LivePhase.Bidding:
                case LivePhase.RtmDecision:
                    break;
                default:
                    return false;
            }
            _scheduled.Clear();
            if (!sold)
            {
                RoomRules.DropBids(Engine);
                Close(false, forcedUnsold: true);
                return true;
            }
            if (Phase == LivePhase.RtmDecision) Close(false);
            else
            {
                Phase = LivePhase.Bidding;
                TimerExpired();
            }
            return true;
        }

        /// <summary>
        /// Rooms: finish the auction now, every lot settled by the AI bidders (give human teams an
        /// AutoPilot via SetController first, or they stop bidding and decline RTM). Unlike
        /// SimulateToEnd it emits every event (open, bids, RTM, close), so replicas stay in step.
        /// </summary>
        public void RunToEnd()
        {
            _started = true;
            var guard = 0;
            while (Phase != LivePhase.Finished)
            {
                if (++guard > 1000000) throw new InvalidOperationException("RunToEnd made no progress");
                switch (Phase)
                {
                    case LivePhase.Revealing:
                        Phase = LivePhase.Bidding;
                        break;
                    case LivePhase.Bidding:
                        _scheduled.Clear();
                        while (true)
                        {
                            string? fastest = null;
                            var decision = AiDecision.Pass;
                            foreach (var id in _bidders.Keys.ToList())
                            {
                                var d = _bidders[id].Decide(Engine);
                                if (d.Bid && (fastest == null || d.DelayMs < decision.DelayMs))
                                {
                                    fastest = id;
                                    decision = d;
                                }
                            }
                            if (fastest == null) break;
                            var rejection = Engine.PlaceBid(fastest, decision.Amount);
                            if (rejection != null) throw new InvalidOperationException($"AI {fastest} made an invalid bid: {rejection}");
                            Emit(new BidPlaced(fastest, decision.Amount));
                        }
                        var offer = Engine.EndBidding();
                        if (offer == null) Close(false);
                        else
                        {
                            Phase = LivePhase.RtmDecision;
                            Emit(new RtmOffered(offer));
                        }
                        break;
                    case LivePhase.RtmDecision:
                        var pending = PendingRtm!;
                        var holder = _bidders.TryGetValue(pending.TeamId, out var h) ? h : null;
                        Close(holder != null && holder.WantsRtm(Engine, pending));
                        break;
                    case LivePhase.Result:
                        OpenNext();
                        break;
                }
            }
        }

        private void OpenNext()
        {
            _scheduled.Clear();
            _lotAutoFast = false;
            _aiSilent = false;
            var lot = Engine.OpenNextLot();
            if (lot == null)
            {
                Phase = LivePhase.Finished;
                Emit(new AuctionFinished());
                return;
            }
            Phase = LivePhase.Revealing;
            _phaseEndsAt = NowMs + RevealMs;
            Emit(new LotRevealed(lot));
        }

        private void ScheduleAi()
        {
            _scheduled.Clear();
            if (_aiSilent) return;
            foreach (var id in _bidders.Keys.ToList()) ScheduleOne(id);
        }

        private void ScheduleOne(string teamId)
        {
            var d = _bidders[teamId].Decide(Engine);
            if (!d.Bid) return;
            var delay = AiThinkScale == 1.0
                ? d.DelayMs
                : Math.Min(DartMath.Round(d.DelayMs * AiThinkScale), BidTimerTotalMs - 700);
            _scheduled[teamId] = (NowMs + delay, d.Amount);
        }

        private void CheckHumansOut()
        {
            if (HumanTeamIds.Count == 0) return;
            var lot = Engine.CurrentLot!;
            var outAll = HumanTeamIds.All(id =>
            {
                if (lot.Passed.Contains(id)) return true;
                var reason = Engine.CheckBid(id);
                return reason != null && reason != BidRejection.AlreadyLeading;
            });
            if (outAll) _lotAutoFast = true;
        }

        private void Emit(LiveEvent e) => OnEvent?.Invoke(e);
    }
}
