using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Apl.Core
{
    /// <summary>Small shared pieces of the v2 room rules (docs/ROOMS_PROTOCOL.md).</summary>
    public static class RoomRules
    {
        /// <summary>The host's bid timer range, seconds (the client offers 10 / 15 / 20).</summary>
        public const int MinTimerSec = 5, MaxTimerSec = 30;

        /// <summary>A host's "unsold" close: the open lot loses its bids (and any RTM offer) before it closes.</summary>
        public static void DropBids(AuctionEngine engine)
        {
            var lot = engine.CurrentLot ?? throw new InvalidOperationException("No lot is open");
            lot.Bids.Clear();
            lot.RtmOffer = null;
        }

        /// <summary>A stable string hash (FNV-1a): string.GetHashCode differs per process.</summary>
        public static long StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (var c in s)
                {
                    h ^= c;
                    h *= 16777619;
                }
                return h;
            }
        }

        /// <summary>The wire name of a bid rejection: "staleAmount", "cannotAfford" …</summary>
        public static string Code(BidRejection r)
        {
            var name = r.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        public static string PhaseName(LivePhase p) => p switch
        {
            LivePhase.Revealing => "revealing",
            LivePhase.Bidding => "bidding",
            LivePhase.RtmDecision => "rtmDecision",
            LivePhase.Result => "result",
            _ => "finished",
        };
    }

    /// <summary>Who drives a team a human holds (or held) in a room.</summary>
    public enum SeatControl
    {
        /// <summary>The player bids himself.</summary>
        Human,

        /// <summary>He pressed SIMULATE: the Franchise-AI autopilot bids for him until he takes back.</summary>
        AutoPilot,

        /// <summary>Disconnected past the grace period (or left): autopilot until he rejoins.</summary>
        Away,

        /// <summary>Kicked by the host: a full Franchise-AI bot, for good.</summary>
        Ai,
    }

    public static class SeatControls
    {
        public static string Name(this SeatControl c) => c switch
        {
            SeatControl.Human => "human",
            SeatControl.AutoPilot => "autopilot",
            SeatControl.Away => "away",
            _ => "ai",
        };
    }

    /// <summary>Pacing for a room auction (the 3D hall's, as solo).</summary>
    public sealed class RoomAuctionOptions
    {
        public int RevealMs = 11000;
        public int ResultMs = 5500;
        public int AiRtmThinkMs = 6500;
        public int HumanRtmTimeoutMs = 10000;
        public double AiThinkScale = 3.6;

        /// <summary>The host's bid timer, seconds; null = the rules' (10 s).</summary>
        public int? BidTimerSec;
    }

    /// <summary>
    /// A friend room's auction under the v2 rules, with no networking (the server wraps it; tests
    /// drive it directly). Owns the LiveAuction (pass consensus on, no fast-forward), the action
    /// log phones replay, who drives each human-held team (human / autopilot / away / AI), pause,
    /// the host's timer, force close, undo, end, the all-simulated instant finish, and the rewards.
    /// </summary>
    public sealed class RoomAuction
    {
        private readonly Dataset _data;
        private readonly RoomAuctionOptions _options;
        private readonly List<RoomAction> _log = new List<RoomAction>();
        private readonly OrderedMap<string, SeatControl> _seats = new OrderedMap<string, SeatControl>();
        private bool _bulk;

        public RoomAuction(Dataset data, RoomSetup setup, IEnumerable<string> humanTeamIds, RoomAuctionOptions? options = null)
        {
            _data = data;
            Setup = setup;
            _options = options ?? new RoomAuctionOptions();
            foreach (var f in data.Franchises)
            {
                if (humanTeamIds.Contains(f.Id)) _seats[f.Id] = SeatControl.Human;
            }
            Live = Build(new List<RoomAction>());
        }

        public RoomSetup Setup { get; }
        public LiveAuction Live { get; private set; }
        public AuctionEngine Engine => Live.Engine;

        /// <summary>Every action so far, in order: what a joining phone replays.</summary>
        public IReadOnlyList<RoomAction> Log => _log;

        public bool Started { get; private set; }
        public bool Paused { get; private set; }
        public bool IsFinished => Live.Phase == LivePhase.Finished;

        /// <summary>The rewards, once the auction is over.</summary>
        public AwardsReport? Awards { get; private set; }

        /// <summary>The lot number the last Undo put back on the block.</summary>
        public int UndoneLot { get; private set; }

        /// <summary>One engine step, as it happens (not raised for the steps of an undo or instant finish).</summary>
        public event Action<RoomAction>? Acted;

        /// <summary>The log was rewritten or extended in one go ("undo", "instant"): resend it whole.</summary>
        public event Action<string>? Reloaded;

        /// <summary>The auction is over; Awards is set.</summary>
        public event Action? Finished;

        public int? BidTimerSec
        {
            get => _options.BidTimerSec;
            set
            {
                _options.BidTimerSec = value;
                Live.BidTimerSec = value;
            }
        }

        /// <summary>Teams a human holds (or held), in franchise order, and who drives each.</summary>
        public IEnumerable<KeyValuePair<string, SeatControl>> Seats => _seats.Keys.ToList().Select(k => new KeyValuePair<string, SeatControl>(k, _seats[k]));

        public SeatControl ControlOf(string teamId) => _seats.TryGetValue(teamId, out var c) ? c : SeatControl.Ai;

        public bool IsSeat(string teamId) => _seats.ContainsKey(teamId);

        // --------------------------------------------------------------- flow

        public void Start()
        {
            if (Started) return;
            Started = true;
            Live.Start();
            FinishIfNoHumans();
        }

        /// <summary>Game time passes (nothing moves while paused).</summary>
        public void Advance(int gameMs)
        {
            if (!Started || Paused || IsFinished) return;
            Live.Advance(gameMs);
        }

        // ------------------------------------------------------- a player's team

        public string? Bid(string teamId, int amount)
        {
            var problem = Playing(teamId);
            if (problem != null) return problem;
            var r = Live.HumanBidAt(teamId, amount);
            return r == null ? null : RoomRules.Code(r.Value);
        }

        public string? Pass(string teamId)
        {
            var problem = Playing(teamId);
            if (problem != null) return problem;
            if (Live.Phase != LivePhase.Bidding && Live.Phase != LivePhase.Revealing) return "biddingClosed";
            var lot = Engine.CurrentLot;
            if (lot == null) return "noOpenLot";
            if (lot.Passed.Contains(teamId)) return "alreadyPassed";
            if (lot.LeaderId == teamId) return "leading";
            Live.HumanPass(teamId);
            return null;
        }

        public string? Rtm(string teamId, bool use)
        {
            var problem = Playing(teamId);
            if (problem != null) return problem;
            var offer = Live.PendingRtm;
            if (offer == null || offer.TeamId != teamId) return "noRtm";
            Live.HumanRtm(teamId, use);
            return null;
        }

        /// <summary>SIMULATE: the autopilot takes the team; the auction carries on for everyone else.</summary>
        public string? Simulate(string teamId)
        {
            if (!Started || IsFinished) return "notAuction";
            if (ControlOf(teamId) != SeatControl.Human) return "notInControl";
            Hand(teamId, SeatControl.AutoPilot);
            FinishIfNoHumans();
            return null;
        }

        /// <summary>Gone past the grace period (or left): the autopilot covers until he's back.</summary>
        public string? GoAway(string teamId)
        {
            if (!IsSeat(teamId) || ControlOf(teamId) != SeatControl.Human) return "notInControl";
            Hand(teamId, SeatControl.Away);
            FinishIfNoHumans();
            return null;
        }

        /// <summary>Back in control (from SIMULATE's autopilot or from being away).</summary>
        public string? TakeBack(string teamId)
        {
            if (!Started || IsFinished) return "notAuction";
            var c = ControlOf(teamId);
            if (!IsSeat(teamId) || (c != SeatControl.AutoPilot && c != SeatControl.Away)) return "notOnAutopilot";
            _seats[teamId] = SeatControl.Human;
            Live.SetController(teamId, null);
            return null;
        }

        /// <summary>The host kicked the team's player: a full Franchise-AI bot takes it for good.</summary>
        public void Release(string teamId)
        {
            if (!IsSeat(teamId) || ControlOf(teamId) == SeatControl.Ai) return;
            Hand(teamId, SeatControl.Ai);
            FinishIfNoHumans();
        }

        // ---------------------------------------------------------------- host

        public string? Pause()
        {
            if (!Started || IsFinished) return "notAuction";
            Paused = true;
            return null;
        }

        public string? Resume()
        {
            if (!Started || IsFinished) return "notAuction";
            Paused = false;
            return null;
        }

        public string? ForceClose(bool sold)
        {
            if (!Started || IsFinished) return "notAuction";
            return Live.ForceClose(sold) ? null : "noOpenLot";
        }

        /// <summary>
        /// Puts the last closed lot (sold or unsold) back on the block: the engine is rebuilt from
        /// the log up to just before that lot opened, so squads, purses and RTM cards are as they
        /// were, and the same player comes up next. Raises Reloaded("undo") with the shortened log,
        /// then the lot reopens through Acted (an "open" action), as any lot does.
        /// </summary>
        public string? Undo()
        {
            if (!Started || IsFinished) return "notAuction";
            var lastClose = _log.FindLastIndex(a => a is CloseLotAction);
            if (lastClose < 0) return "nothingToUndo";
            var open = _log.FindLastIndex(lastClose, a => a is OpenLotAction);
            if (open < 0) return "nothingToUndo";
            UndoneLot = _log.Take(open + 1).Count(a => a is OpenLotAction o && o.PlayerId != null);
            var kept = _log.Take(open).ToList();
            Live = Build(kept);
            // Phones reload to just before the lot, then see it open again as a normal "act open".
            Reloaded?.Invoke("undo");
            Live.Start();
            return null;
        }

        /// <summary>The host ends early: every human team goes on autopilot and the rest is settled at once.</summary>
        public string? End()
        {
            if (!Started || IsFinished) return "notAuction";
            foreach (var team in _seats.Keys.ToList())
            {
                var c = _seats[team];
                if (c == SeatControl.Human || c == SeatControl.Away) Hand(team, SeatControl.AutoPilot);
            }
            FinishInstantly();
            return null;
        }

        // ------------------------------------------------------------- state

        /// <summary>Teams a human drives right now, sorted.</summary>
        public List<string> HumanDriven() => Live.HumanTeamIds.OrderBy(x => x, StringComparer.Ordinal).ToList();

        /// <summary>The "clock" message (docs/ROOMS_PROTOCOL.md §4.1).</summary>
        public JObject ClockJson()
        {
            var lot = Engine.CurrentLot;
            return new JObject
            {
                ["t"] = "clock",
                ["phase"] = RoomRules.PhaseName(Live.Phase),
                ["lot"] = lot?.Number ?? 0,
                ["bid"] = Live.BidTimeLeftMs,
                ["bidTotal"] = Live.BidTimerTotalMs,
                ["left"] = Live.PhaseTimeLeftMs,
                ["rtmTotal"] = Live.HumanRtmTimeoutMs,
                ["ff"] = Live.IsFastForwarding,
                ["paused"] = Paused,
                ["humans"] = new JArray(HumanDriven()),
                ["passed"] = new JArray(lot == null ? new List<string>() : lot.Passed.OrderBy(x => x, StringComparer.Ordinal).ToList()),
                ["timerSec"] = BidTimerSec ?? Engine.Rules.BidTimerSec,
            };
        }

        public JArray LogJson() => new JArray(_log.Select(a => a.ToJson()));

        // ---------------------------------------------------------- internals

        private string? Playing(string teamId)
        {
            if (!Started || IsFinished) return "notAuction";
            if (ControlOf(teamId) != SeatControl.Human) return "notInControl";
            if (Paused) return "paused";
            return null;
        }

        private void Hand(string teamId, SeatControl control)
        {
            _seats[teamId] = control;
            Live.SetController(teamId, BotFor(teamId, control));
        }

        private AiBidder BotFor(string teamId, SeatControl control)
        {
            var seed = Setup.Seed ^ RoomRules.StableHash(teamId) ^ ((long)_log.Count << 20);
            return control == SeatControl.Ai
                ? new AiBidder(teamId, Personality.Balanced, seed, AiModel.Franchise)
                : Ai.AutoPilot(teamId, seed);
        }

        /// <summary>Every seat is on autopilot or kicked (none human or away): nobody is left to wait for.</summary>
        private void FinishIfNoHumans()
        {
            if (!Started || IsFinished || _seats.Count == 0) return;
            if (_seats.Values.All(c => c == SeatControl.AutoPilot || c == SeatControl.Ai)) FinishInstantly();
        }

        private void FinishInstantly()
        {
            _bulk = true;
            try
            {
                Live.RunToEnd();
            }
            finally
            {
                _bulk = false;
            }
            Reloaded?.Invoke("instant");
            if (IsFinished) Finished?.Invoke();
        }

        private LiveAuction Build(List<RoomAction> log)
        {
            var engine = Setup.BuildEngine(_data);
            foreach (var a in log) RoomReplay.Apply(engine, a);
            _log.Clear();
            _log.AddRange(log);
            var held = _seats.Keys.ToList();
            var humans = held.Where(t => _seats[t] == SeatControl.Human).ToList();
            var bidders = Ai.CreateBidders(engine, (Setup.Seed ^ 0x5bd1e995) + log.Count, held, AiModel.Franchise);
            var live = new LiveAuction(
                engine,
                bidders,
                humans,
                revealMs: _options.RevealMs,
                resultMs: _options.ResultMs,
                aiRtmThinkMs: _options.AiRtmThinkMs,
                humanRtmTimeoutMs: _options.HumanRtmTimeoutMs,
                canHoldFastForward: false,
                aiThinkScale: _options.AiThinkScale)
            {
                PassConsensus = true,
                BidTimerSec = _options.BidTimerSec,
            };
            foreach (var t in held)
            {
                if (_seats[t] != SeatControl.Human) live.SetController(t, BotFor(t, _seats[t]));
            }
            live.OnEvent += OnEvent;
            return live;
        }

        private void OnEvent(LiveEvent e)
        {
            var action = RoomAction.ForEvent(e);
            _log.Add(action);
            if (e is AuctionFinished) Awards = AuctionAwards.Compute(Engine, _data.Rules);
            if (_bulk) return;
            Acted?.Invoke(action);
            if (e is AuctionFinished) Finished?.Invoke();
        }
    }
}
