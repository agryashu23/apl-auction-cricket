using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core
{
    public sealed class Personality
    {
        public static readonly Personality Aggressive = new Personality("aggressive", 0.98, 1.1, 4.5, 0.25, 0.10, 0.7, 0.30);
        public static readonly Personality Balanced = new Personality("balanced", 1.0, 1.0, 4.0, 0.22, 0.10, 1.0, 0.18);
        public static readonly Personality ValueHunter = new Personality("valueHunter", 0.96, 0.95, 3.3, 0.18, 0.10, 1.3, 0.08);

        /// <summary>
        /// SIMULATE's stand-in for a human team: bids only to fill the squad (minimum size and
        /// role gaps), a little under the market, never caught up in a war. Not in <see cref="All"/>.
        /// </summary>
        public static readonly Personality AutoPilot = new Personality("autoPilot", 0.95, 0.95, 3.0, 0.15, 0.05, 1.5, 0);

        /// <summary>Same order as the Dart enum (createAiBidders indexes it).</summary>
        public static readonly IReadOnlyList<Personality> All = new[] { Aggressive, Balanced, ValueHunter };

        public const int StarImpact = 82;

        private Personality(string name, double valuation, double starValuation, double starFactor, double maxShare, double noise, double speed, double warChance)
        {
            Name = name;
            Valuation = valuation;
            StarValuation = starValuation;
            StarFactor = starFactor;
            MaxShare = maxShare;
            Noise = noise;
            Speed = speed;
            WarChance = warChance;
        }

        /// <summary>Franchise model: the chance, per contested lot, of being drawn past its limit (a bidding war).</summary>
        public double WarChance { get; }

        public string Name { get; }
        public double Valuation { get; }
        public double StarValuation { get; }
        public double StarFactor { get; }
        public double MaxShare { get; }
        public double Noise { get; }
        public double Speed { get; }
    }

    /// <summary>
    /// A franchise's temperament for one auction (franchise AI). Derived only from the auction's
    /// seed, the team and the player, never from a bidder's random stream, so the room server's
    /// rebuilt bidders and a resumed solo auction keep the same moods and targets.
    /// </summary>
    public sealed class FranchiseMood
    {
        /// <summary>No mood: SIMULATE's AutoPilot bids flat.</summary>
        public static readonly FranchiseMood Neutral = new FranchiseMood(0, "", 1.0, 1.0, neutral: true);

        private readonly long _seed;
        private readonly string _teamId;
        private readonly bool _neutral;

        private FranchiseMood(long seed, string teamId, double aggression, double warAppetite, bool neutral = false)
        {
            _seed = seed;
            _teamId = teamId;
            Aggression = aggression;
            WarAppetite = warAppetite;
            _neutral = neutral;
        }

        /// <summary>Multiplier on what it will pay this auction: 0.88 (cautious) to 1.08 (bullish).</summary>
        public double Aggression { get; }

        /// <summary>Multiplier on its personality's war chance: 0.5 to 1.5.</summary>
        public double WarAppetite { get; }

        public static FranchiseMood For(long seed, string teamId)
        {
            var r = new Rng(RoomRules.StableHash($"{seed}:{teamId}:mood"));
            var aggression = 0.88 + 0.20 * r.NextDouble();
            var appetite = 0.5 + r.NextDouble();
            return new FranchiseMood(seed, teamId, aggression, appetite);
        }

        /// <summary>
        /// How this franchise rates this player this auction, on top of the market: a good player
        /// (impact 72+ or a star name) is one of its targets one time in ten (1.15–1.40x); one
        /// player in twelve it cools on (0.85–0.92x); the rest 0.94–1.06x.
        /// </summary>
        public double Affinity(Player p)
        {
            if (_neutral) return 1.0;
            var r = new Rng(RoomRules.StableHash($"{_seed}:{_teamId}:{p.Id}"));
            var u = r.NextDouble();
            var v = r.NextDouble();
            var good = p.Impact >= 72 || p.StarPower >= Market.ReputationFrom;
            if (good && u < 0.10) return 1.15 + 0.25 * v;
            if (u > 1 - 1.0 / 12) return 0.85 + 0.07 * v;
            return 0.94 + 0.12 * v;
        }

        /// <summary>True when the player is one of this franchise's targets this auction.</summary>
        public bool IsTarget(Player p) => Affinity(p) >= 1.15;
    }

    public readonly struct AiDecision
    {
        public static readonly AiDecision Pass = new AiDecision(false, 0, 0);

        public AiDecision(bool bid, int amount, int delayMs)
        {
            Bid = bid;
            Amount = amount;
            DelayMs = delayMs;
        }

        public bool Bid { get; }
        public int Amount { get; }

        /// <summary>Thinking time before the bid lands.</summary>
        public int DelayMs { get; }
    }

    /// <summary>Which AI brain: Classic is the Dart port (golden files, room server); Franchise is solo's.</summary>
    public enum AiModel
    {
        Classic,

        /// <summary>
        /// Solo AI: star / reputation premium (Market.ReputationPrice), every franchise in for
        /// the marquee lots, a pull toward its own former players, money-rich teams bid harder,
        /// and IPL-like retentions (stars kept at slab prices).
        /// </summary>
        Franchise,
    }

    public sealed class AiBidder
    {
        private readonly Rng _rng;
        private int _limitLot = -1;
        private int _limit;
        private double _bar;

        /// <summary>Franchise model: how far this lot's bidding war (if it rolled one) carries it past its limit.</summary>
        private int _war;
        private FranchiseMood? _mood;

        public AiBidder(string teamId, Personality personality, long seed, AiModel model = AiModel.Classic)
        {
            TeamId = teamId;
            Personality = personality;
            Model = model;
            _rng = new Rng(seed);
        }

        public string TeamId { get; }
        public Personality Personality { get; }
        public AiModel Model { get; }

        /// <summary>Raised once per lot when this bidder sets its limit (the client logs it as [APL aibid]).</summary>
        public static event Action<AiBidder, Lot, int>? LimitSet;

        /// <summary>Highest price for the open lot; fixed once the lot opens.</summary>
        public int LimitFor(AuctionEngine engine)
        {
            var lot = engine.CurrentLot!;
            if (lot.Number != _limitLot)
            {
                _limitLot = lot.Number;
                if (Model == AiModel.Franchise)
                {
                    _bar = Market.StarBar(lot.Player, engine.Format, engine.MaxPrice);
                    _limit = ComputeFranchiseLimit(engine, lot, out _war);
                    LimitSet?.Invoke(this, lot, _limit);
                }
                else
                {
                    _bar = Market.MaxBar(lot.Player, engine.Format, engine.MaxPrice);
                    _limit = ComputeLimit(engine, lot);
                }
            }
            return _limit;
        }

        private int ComputeLimit(AuctionEngine engine, Lot lot)
        {
            var team = engine.Teams[TeamId];
            var player = lot.Player;
            var need = Market.NeedMultiplier(team, player, engine.Format);
            if (need <= 0) return 0;

            var noise = 1 + Personality.Noise * (2 * _rng.NextDouble() - 1);
            // A valued player's price is mostly the market's; squad need only sways it.
            var want = player.Value > 0 ? 0.55 + 0.3 * need : need;
            var value = Market.FairPrice(player, engine.Format) *
                        Market.Inflation(engine) *
                        want *
                        // Real prices already carry the star premium.
                        (player.Impact >= Personality.StarImpact && player.Value == 0 ? Personality.StarValuation : Personality.Valuation) *
                        noise;

            var targets = Market.SquadTargets(engine.Format);
            var lotsPerTeam = DartMath.Ceil((double)engine.LotsRemaining / engine.Teams.Count) + 1;
            var paceSlots = Math.Max(1, Math.Min(targets.Size - team.Size, lotsPerTeam));
            var spendable = team.Purse - Math.Max(0, engine.Format.MinSquad - team.Size) * engine.Rules.MinBasePrice;
            var cap = (double)spendable / paceSlots * Personality.StarFactor;
            var shareCap = engine.Format.Purse * Personality.MaxShare;

            var limit = Math.Min(Math.Min(Math.Min(Math.Min(value, cap), shareCap), _bar), engine.MaxBidFor(team));
            return DartMath.Floor(limit);
        }

        /// <summary>This franchise's temperament for this auction (from the auction's seed: a rebuilt bidder keeps it).</summary>
        public FranchiseMood MoodFor(AuctionEngine engine) =>
            _mood ??= Personality == Personality.AutoPilot ? FranchiseMood.Neutral : FranchiseMood.For(engine.Seed, TeamId);

        /// <summary>
        /// Franchise model. Worth = the franchise worth (reputation price, star premium, and a
        /// share of a proven player's rating over his market value), times the market's
        /// inflation, times how much this squad wants him: a marquee / star lot is wanted by every
        /// franchise with room (that's what makes the bidding war), a role gap more than a
        /// luxury. Former players get a homecoming nudge; a team sitting on more money per open
        /// slot than the league bids harder (and a poorer one softer), and late in the auction a
        /// franchise still holding real money spends it on depth, so nobody ends the auction with
        /// a fat unspent purse.
        /// Per auction, each franchise has a mood (aggression 0.90–1.12, appetite for a war) and
        /// its own likes: a few players it over-rates (a "target", 1.15–1.40x) and some it cools
        /// on, so the same player's price moves from one auction to the next. Star lots carry more
        /// per-lot noise, and a contested lot sometimes turns into a war that carries a bidder past
        /// its limit (never past 1.12x its ceiling, its purse pacing or the price cap). Limits are
        /// always a price the lot can reach (base, then the increments). Same purse pacing, share
        /// and price caps as Classic.
        /// </summary>
        private int ComputeFranchiseLimit(AuctionEngine engine, Lot lot, out int war)
        {
            war = 0;
            var team = engine.Teams[TeamId];
            var player = lot.Player;
            var format = engine.Format;
            var need = Market.NeedMultiplier(team, player, format);
            if (need <= 0) return 0;
            var autoPilot = Personality == Personality.AutoPilot;
            // SIMULATE for a human: role gaps first; any useful player only while the squad is
            // short of the minimum and the lots left are running out (else the base-price filler would do it).
            if (autoPilot && need < 1 &&
                (team.Size >= format.MinSquad || engine.LotsRemaining > 2 * (format.MinSquad - team.Size) * engine.Teams.Count)) return 0;

            var star = Market.IsMarqueeLot(player, format);
            var mood = MoodFor(engine);
            // Star lots swing more from one auction to the next (the rater: top prices within ±10% every run).
            var noise = 1 + Personality.Noise * (star ? 1.8 : 1.0) * (2 * _rng.NextDouble() - 1);
            var want = player.Value > 0 ? 0.55 + 0.3 * need : need;
            if (star && !autoPilot) want = Math.Max(want, 0.88);
            var drive = PurseDrive(engine, team);
            // A proven capped player draws every franchise with the money to want him, not just the
            // teams with a gap in his role (the rater's dead opening lots: a 78-rated opener at his
            // base, one bid). This adds bidders near his worth, not price.
            if (!autoPilot && player.Capped && player.Impact >= 72) want = Math.Max(want, drive >= 1.0 ? 0.95 : 0.88);
            var push = autoPilot ? 1.0 : LatePush(engine, team);
            // Late on, a franchise still holding real money buys depth, not only its gaps.
            if (push > 1.15) want = Math.Max(want, 0.8);
            var homecoming = player.PreviousTeam == TeamId ? 1.12 : 1.0;
            var worth = Market.FranchiseWorth(player, format);
            // The aggressive franchises chase a name the market undervalues (his star premium).
            var famous = Market.ReputationPrice(player, format) > Market.FairPrice(player, format);
            var affinity = mood.Affinity(player);
            var buzz = autoPilot ? 1.0 : Market.Buzz(engine.Seed, player, format);
            var value = worth *
                        buzz *
                        Market.Inflation(engine) *
                        want *
                        (famous ? Personality.StarValuation : Personality.Valuation) *
                        homecoming *
                        drive *
                        push *
                        mood.Aggression *
                        affinity *
                        noise;

            var targets = Market.SquadTargets(format);
            var lotsPerTeam = DartMath.Ceil((double)engine.LotsRemaining / engine.Teams.Count) + 1;
            var paceSlots = Math.Max(1, Math.Min(targets.Size - team.Size, lotsPerTeam));
            var spendable = team.Purse - Math.Max(0, format.MinSquad - team.Size) * engine.Rules.MinBasePrice;
            // The mood moves its purse pacing too: early star prices are set by these caps.
            var cap = (double)spendable / paceSlots * Personality.StarFactor * mood.Aggression;
            var shareCap = format.Purse * Personality.MaxShare * mood.Aggression;
            var priceCap = engine.MaxPrice ?? double.PositiveInfinity;
            // No uncapped player past the most a real auction has paid one (14.2 Cr, IPL 2026), buzz or war.
            if (!player.Capped) priceCap = Math.Min(priceCap, Market.UncappedRecord * format.Purse / 12000.0);
            var hard = Math.Min(Math.Min(Math.Min(cap, shareCap), engine.MaxBidFor(team)), priceCap);
            // The ceiling moves with the room's buzz (else every star lot ends at the same bar), and
            // the late push may stretch it a little; never past the price cap.
            var bar = Math.Min(_bar * buzz * (push > 1 ? 1 + 0.25 * (push - 1) : 1), priceCap);

            var limit = Math.Min(Math.Min(value, hard), bar);
            var result = engine.Rules.FloorToBid(DartMath.Floor(limit), player.BasePrice);
            war = result;
            if (!autoPilot && result > 0)
            {
                // A bidding war: once per lot, more likely on a star lot or a player it has its eye on.
                var chance = Personality.WarChance * mood.WarAppetite * (star ? 1.5 : 1.0) * (affinity > 1.1 ? 1.6 : 1.0);
                if (_rng.Chance(chance))
                {
                    var stretched = Math.Min(Math.Min(limit * _rng.Range(1.06, 1.25), hard), Math.Min(bar * 1.12, priceCap));
                    war = Math.Max(result, engine.Rules.FloorToBid(DartMath.Floor(stretched), player.BasePrice));
                }
            }
            return result;
        }

        /// <summary>
        /// This team's money per open squad slot against the league's: sqrt of the ratio,
        /// 0.85–1.3. A franchise sitting on cash bids up; one that's spent big holds back.
        /// </summary>
        public static double PurseDrive(AuctionEngine engine, TeamState team)
        {
            var size = Market.SquadTargets(engine.Format).Size;
            double PerSlot(TeamState t) => (double)t.Purse / Math.Max(1, size - t.Size);
            var open = engine.Teams.Values.Where(t => t.Size < size).ToList();
            if (open.Count == 0 || team.Size >= size) return 1.0;
            var avg = open.Average(PerSlot);
            if (avg <= 0) return 1.0;
            return DartMath.Clamp(Math.Sqrt(PerSlot(team) / avg), 0.85, 1.3);
        }

        /// <summary>
        /// 0 for most of the auction, rising to 1 over the last part of the main round (60% → 90%
        /// of its lots done) and 1 in the accelerated round.
        /// </summary>
        public static double Lateness(AuctionEngine engine)
        {
            if (engine.InAcceleratedRound) return 1;
            var done = engine.Results.Count;
            var progress = (double)done / Math.Max(1, done + engine.LotsRemaining + 1);
            return DartMath.Clamp((progress - 0.6) / 0.3, 0, 1);
        }

        /// <summary>
        /// The late-auction push, 1–1.6: a franchise still holding more than 5% of the purse late
        /// on (a third of them ended a mini auction with 10 Cr+ unspent) bids harder, in
        /// proportion to how late it is and how much it holds (full push at 18 Cr+ over that).
        /// </summary>
        public static double LatePush(AuctionEngine engine, TeamState team)
        {
            if (team.Size >= engine.Format.MaxSquad) return 1;
            var late = Lateness(engine);
            if (late <= 0) return 1;
            var excess = team.Purse - engine.Format.Purse * 0.05;
            if (excess <= 0) return 1;
            return 1 + 0.6 * late * DartMath.Clamp(excess / (engine.Format.Purse * 0.15), 0, 1);
        }

        public AiDecision Decide(AuctionEngine engine)
        {
            var lot = engine.CurrentLot;
            if (lot == null || engine.CheckBid(TeamId) != null) return AiDecision.Pass;
            var next = engine.NextBidAmount;
            var limit = LimitFor(engine);

            var willBid = next <= limit;
            if (Model == AiModel.Franchise)
            {
                // Caught up in this lot's war (rolled once per lot, see ComputeFranchiseLimit).
                if (!willBid && lot.LeaderId != null && next <= _war) willBid = true;
            }
            else if (!willBid &&
                Personality == Personality.Aggressive &&
                limit > 0 &&
                lot.LeaderId != null &&
                next <= limit * 1.1 &&
                next <= _bar &&
                _rng.Chance(0.25))
            {
                willBid = true; // caught up in a bidding war
            }
            if (!willBid) return AiDecision.Pass;

            var nearLimit = next > limit * 0.85;
            // Past its limit it's in a war: quick, heated raises rather than long thought.
            var thinkMs = Model == AiModel.Franchise && next > limit ? _rng.Range(1200, 3500)
                : nearLimit ? _rng.Range(2500, 6500) : _rng.Range(600, 2400);
            var maxDelay = engine.Rules.BidTimerSec * 1000 - 700;
            var delay = Math.Min(DartMath.Round(thinkMs * Personality.Speed), maxDelay);
            return new AiDecision(true, next, delay);
        }

        /// <summary>Use the RTM card only on a worthwhile player, at a price it would pay, with nobody better still to come.</summary>
        public bool WantsRtm(AuctionEngine engine, RtmOffer offer)
        {
            var player = engine.CurrentLot!.Player;
            if (Model == AiModel.Franchise)
            {
                // His own former star back at the winning price, if the squad can use him and the
                // price is within what it would have paid (plus the homecoming margin).
                if (player.Impact < 72 && player.StarPower < 40) return false;
                var worth = Retention.Value(player);
                if (engine.Upcoming.Any(p => p.PreviousTeam == TeamId && Retention.Value(p) > worth + 3 && Market.IsMarqueeLot(p, engine.Format))) return false;
                return offer.Price <= Math.Max(LimitFor(engine), 1) * 1.2;
            }
            if (player.Impact < 74 && player.StarPower < 40) return false;
            var value = Retention.Value(player);
            var betterLater = engine.Upcoming.Any(p => p.PreviousTeam == TeamId && Retention.Value(p) > value + 3);
            if (betterLater) return false;
            return offer.Price <= LimitFor(engine) * 1.15;
        }
    }

    public static class Ai
    {
        /// <summary>Settles the open lot between AI teams; null = waiting on a human's RTM answer.</summary>
        public static LotResult? ResolveLot(AuctionEngine engine, IReadOnlyList<AiBidder> bidders)
        {
            while (true)
            {
                AiBidder? fastest = null;
                AiDecision decision = AiDecision.Pass;
                foreach (var b in bidders)
                {
                    var d = b.Decide(engine);
                    if (d.Bid && (fastest == null || d.DelayMs < decision.DelayMs))
                    {
                        fastest = b;
                        decision = d;
                    }
                }
                if (fastest == null) return SettleRtm(engine, bidders);
                var rejection = engine.PlaceBid(fastest.TeamId, decision.Amount);
                if (rejection != null) throw new InvalidOperationException($"AI {fastest.TeamId} made an invalid bid: {rejection}");
            }
        }

        public static LotResult? SettleRtm(AuctionEngine engine, IReadOnlyList<AiBidder> bidders)
        {
            var offer = engine.EndBidding();
            if (offer == null) return engine.CloseLot();
            var holder = bidders.FirstOrDefault(b => b.TeamId == offer.TeamId);
            if (holder == null) return null;
            return engine.CloseLot(holder.WantsRtm(engine, offer));
        }

        public static void RunAuction(AuctionEngine engine, IReadOnlyList<AiBidder> bidders)
        {
            void Lot()
            {
                if (ResolveLot(engine, bidders) == null) engine.CloseLot();
            }

            if (engine.CurrentLot != null) Lot();
            while (engine.OpenNextLot() != null) Lot();
        }

        public static List<Player> ChooseRetentions(string teamId, IReadOnlyList<Player> pool, AuctionRules rules, AuctionFormat format, AiModel model = AiModel.Franchise)
        {
            if (model == AiModel.Franchise) return ChooseFranchiseRetentions(teamId, pool, rules, format);
            var candidates = pool.Where(p => p.PreviousTeam == teamId).ToList();
            candidates.Sort(Retention.ByValue);
            var kept = new List<Player>();
            foreach (var p in candidates)
            {
                if (kept.Count >= rules.RetentionFor(format).MaxPlayers) break;
                var trial = kept.Concat(new[] { p }).ToList();
                if (Retention.Problem(teamId, trial, rules, format) != null) continue;
                var price = Retention.Prices(trial, rules, format).First(kv => kv.Key.Equals(p)).Value;
                if (Market.FairPrice(p, format) * (p.Value > 0 ? 1.1 : 1.5) >= price) kept.Add(p);
            }
            return kept;
        }

        /// <summary>
        /// Franchise model: keep the core, as real franchises do. Best players first (Retention.Value);
        /// each must fit the rules (Retention.Problem) and a retention budget (half the purse on
        /// slabs, 72% at value, so there's money left to bid with).
        /// Slab retention (mega): a player is kept when his open-market worth, star premium included,
        /// is at least two thirds of his slab (the first capped keep: 53%, he's the franchise's face): the slab buys certainty over a bidding war he'd
        /// likely cost as much in (the old rule wanted worth ≥ slab, so only 16 Cr+ names ever
        /// cleared the 18 Cr first slab and most franchises kept 0–2, mostly uncapped).
        /// At value (mini auction): every regular worth his price is kept: impact 70+, or a
        /// 64+ uncapped, or anyone valued at 3 Cr+.
        /// </summary>
        public static List<Player> ChooseFranchiseRetentions(string teamId, IReadOnlyList<Player> pool, AuctionRules rules, AuctionFormat format)
        {
            var retention = rules.RetentionFor(format);
            var candidates = pool.Where(p => p.PreviousTeam == teamId).ToList();
            candidates.Sort(Retention.ByValue);
            var budget = format.Purse * (retention.AtValue ? 0.72 : 0.5);
            var scale = format.Purse / 12000.0;
            var kept = new List<Player>();
            foreach (var p in candidates)
            {
                if (kept.Count >= retention.MaxPlayers) break;
                var trial = kept.Concat(new[] { p }).ToList();
                if (Retention.Problem(teamId, trial, rules, format) != null) continue;
                var prices = Retention.Prices(trial, rules, format);
                if (prices.Sum(kv => kv.Value) > budget) continue;
                var price = prices.First(kv => kv.Key.Equals(p)).Value;
                var worth = Market.ReputationPrice(p, format);
                var keep = retention.AtValue
                    ? p.Impact >= 70 || (!p.Capped && p.Impact >= 64) || p.Value >= 300 * scale
                    // The face of the franchise is kept even at a stiff first slab (real teams paid
                    // 13-23 Cr to keep theirs); later slabs need worth within two thirds of the price.
                    : worth * (kept.Any(k => k.Capped) || !p.Capped ? 1.5 : 1.9) >= price;
                if (keep) kept.Add(p);
            }
            return kept;
        }

        /// <summary>SIMULATE's bidder for a human team: fills the squad to its minimum and its role gaps.</summary>
        public static AiBidder AutoPilot(string teamId, long seed) =>
            new AiBidder(teamId, Personality.AutoPilot, seed, AiModel.Franchise);

        /// <summary>
        /// SIMULATE: finishes the rest of the auction at once, every lot settled by the AI (pass
        /// AutoPilot bidders for human teams that should keep filling their squads; a human team
        /// without one stops bidding and declines any RTM). Same rules as live: purse, squad
        /// limits, overseas cap, price cap, RTM, the accelerated round and the auto-fill to the
        /// minimum squad at the end.
        /// </summary>
        public static void FinishAuction(AuctionEngine engine, IReadOnlyList<AiBidder> bidders) => RunAuction(engine, bidders);

        public static List<AiBidder> CreateBidders(AuctionEngine engine, long seed, ICollection<string>? humanTeamIds = null, AiModel model = AiModel.Franchise)
        {
            var rng = new Rng(seed);
            var personalities = Personality.All;
            var bidders = new List<AiBidder>();
            var i = rng.NextInt(personalities.Count);
            foreach (var id in engine.Teams.Keys)
            {
                if (humanTeamIds != null && humanTeamIds.Contains(id)) continue;
                bidders.Add(new AiBidder(id, personalities[i++ % personalities.Count], rng.NextUint32(), model));
            }
            return bidders;
        }
    }
}
