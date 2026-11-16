using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Apl.Core
{
    public sealed class SquadTargets
    {
        public int Size, Keepers, Batters, AllRounders, Bowlers;

        public int ForRole(Role role) => role switch
        {
            Role.WicketKeeper => Keepers,
            Role.Batter => Batters,
            Role.AllRounder => AllRounders,
            _ => Bowlers,
        };
    }

    public static class Market
    {
        /// <summary>Most a player's star power can add to his price (100 → +80%).</summary>
        public const double StarPremium = 0.8;

        /// <summary>AI teams never bid more than this multiple of a valued player's worth.</summary>
        public const double MaxBarFactor = 1.35;

        public static SquadTargets SquadTargets(AuctionFormat format)
        {
            var size = Math.Min(format.MaxSquad, format.MinSquad + 2);
            var keepers = size <= 13 ? 1 : 2;
            var batters = DartMath.Round(size * 0.30);
            var allRounders = DartMath.Round(size * 0.25);
            return new SquadTargets
            {
                Size = size,
                Keepers = keepers,
                Batters = batters,
                AllRounders = allRounders,
                Bowlers = size - keepers - batters - allRounders,
            };
        }

        /// <summary>Above 1 fills a gap, below 1 is a luxury, 0 = can't use them.</summary>
        public static double NeedMultiplier(TeamState team, Player player, AuctionFormat format)
        {
            if (team.Size >= format.MaxSquad) return 0;
            var targets = SquadTargets(format);

            var have = team.CountRole(player.Role);
            var want = targets.ForRole(player.Role);
            double m;
            if (have < want)
            {
                m = 1.0 + 0.6 * (want - have) / want;
                if (have == 0 && player.Role == Role.WicketKeeper) m = 1.9;
            }
            else
            {
                m = team.Size >= targets.Size ? 0.25 : 0.45;
            }

            if (player.Role == Role.Bowler || player.Role == Role.AllRounder)
            {
                var sameType = team.Players.Count(p => p.BowlType == player.BowlType && p.IsBowlingOption);
                if (player.BowlType == BowlType.Spin && sameType == 0) m *= 1.15;
                if (player.BowlType == BowlType.Pace && sameType < 2) m *= 1.1;
            }

            if (player.Overseas)
            {
                var slotsLeft = format.MaxOverseas - team.OverseasCount;
                if (slotsLeft <= 0) return 0;
                if (slotsLeft == 1 && player.Impact < 82) m *= 0.7;
            }
            return m;
        }

        /// <summary>Worth in lakhs before market conditions; never below base price.</summary>
        public static double FairPrice(Player player, AuctionFormat format)
        {
            if (player.Value > 0) return Math.Max(player.BasePrice, (double)(player.Value * format.Purse) / 12000);
            var a = (double)Math.Max(0, player.Impact - 55);
            // x^1.75 as x·√x·√√x — exactly as the Dart code (sqrt is exact everywhere, pow isn't).
            var raw = 2.5 * (a * Math.Sqrt(a) * Math.Sqrt(Math.Sqrt(a))) * format.Purse / 12000;
            var withStar = raw * (1 + StarPremium * player.StarPower / 100);
            return Math.Max(player.BasePrice, withStar);
        }

        private sealed class InflationEntry
        {
            public int Lot = -1;
            public double Value;
        }

        private static readonly ConditionalWeakTable<AuctionEngine, InflationEntry> Cache =
            new ConditionalWeakTable<AuctionEngine, InflationEntry>();

        /// <summary>&gt;1 when teams hold more money than the players left are worth. Fixed per lot.</summary>
        public static double Inflation(AuctionEngine engine)
        {
            var lotNumber = engine.CurrentLot?.Number ?? -1;
            var entry = Cache.GetOrCreateValue(engine);
            if (entry.Lot == lotNumber && lotNumber != -1) return entry.Value;
            entry.Lot = lotNumber;
            entry.Value = Compute(engine);
            return entry.Value;
        }

        private static double Compute(AuctionEngine engine)
        {
            var targetSize = SquadTargets(engine.Format).Size;
            var slots = 0;
            var money = 0;
            foreach (var t in engine.Teams.Values)
            {
                slots += Math.Max(0, targetSize - t.Size);
                money += t.Purse;
            }
            if (slots == 0) return 1.0;

            var values = engine.Upcoming.Select(p => FairPrice(p, engine.Format)).ToList();
            var lot = engine.CurrentLot;
            if (lot != null) values.Insert(0, FairPrice(lot.Player, engine.Format));
            values.Sort((x, y) => y.CompareTo(x));
            var supply = 0.0;
            foreach (var v in values.Take(slots)) supply += v;
            if (supply <= 0) return 1.0;
            // Market values are real prices already: nudge them, don't rescale.
            var anchored = (lot?.Player.Value ?? 0) > 0;
            return DartMath.Clamp(money * 0.9 / supply, anchored ? 0.75 : 0.5, anchored ? 1.15 : 2.5);
        }

        /// <summary>Most any AI team pays for this player: MaxBarFactor × worth if valued, never past the price cap.</summary>
        public static double MaxBar(Player player, AuctionFormat format, int? cap)
        {
            var bar = player.Value > 0 ? FairPrice(player, format) * MaxBarFactor : double.PositiveInfinity;
            return cap == null ? bar : Math.Min(bar, cap.Value);
        }

        // ------------------------------------------------- franchise model (solo)

        /// <summary>Star power below this earns no reputation premium.</summary>
        public const int ReputationFrom = 60;

        /// <summary>
        /// What a famous name fetches on the open market, in lakhs at a ₹120 Cr purse: 6 Cr at star
        /// power 60 up to 22 Cr at 100 (real IPL tiers: a household-name captain or opener goes for
        /// 18–27 Cr at a mega auction, whatever his last retention slab said).
        /// </summary>
        public static double ReputationFloor(Player p, AuctionFormat format) =>
            (600 + (p.StarPower - ReputationFrom) / 40.0 * 1600) * format.Purse / 12000;

        /// <summary>
        /// Franchise model: the player's worth with a star / reputation premium. A capped star
        /// (star power 60+) is pulled up toward his reputation floor, in proportion to how good he
        /// still is (impact 70 → none, 85+ → all of it), so a fading legend doesn't fetch 20 Cr.
        /// Never below FairPrice, so the rest of the market is untouched.
        /// </summary>
        public static double ReputationPrice(Player p, AuctionFormat format)
        {
            var fair = FairPrice(p, format);
            if (!p.Capped || p.StarPower < ReputationFrom) return fair;
            var q = DartMath.Clamp((p.Impact - 70) / 15.0, 0, 1);
            var floor = ReputationFloor(p, format);
            return floor > fair ? fair + (floor - fair) * q : fair;
        }

        /// <summary>A lot every franchise wants a go at: the marquee set or a big-money star (12 Cr+ at a ₹120 Cr purse).</summary>
        public static bool IsMarqueeLot(Player p, AuctionFormat format) =>
            p.Set == "marquee" || ReputationPrice(p, format) >= 1200.0 * format.Purse / 12000;

        /// <summary>Franchise model's ceiling: MaxBarFactor × the franchise worth, never past the cap.</summary>
        public static double StarBar(Player player, AuctionFormat format, int? cap)
        {
            var bar = player.Value > 0 ? FranchiseWorth(player, format) * MaxBarFactor : double.PositiveInfinity;
            // An uncapped player: at most 1.25x his worth (14.2 Cr is the most a real auction has paid one).
            if (!player.Capped && player.Value > 0) bar = Math.Min(bar, FranchiseWorth(player, format) * 1.25);
            return cap == null ? bar : Math.Min(bar, cap.Value);
        }

        /// <summary>
        /// The room's read of this player this auction, shared by every franchise: 0.75–1.25
        /// (a star: 0.85–1.15), triangular around 1. The second-best bid sets a price, so team-only
        /// noise averaged out and the same names sold within ±10% every run; this is the "who's
        /// hot this year" swing real auctions have. From the auction's seed and the player only.
        /// </summary>
        public static double Buzz(long seed, Player p, AuctionFormat format)
        {
            var r = new Rng(RoomRules.StableHash($"{seed}:buzz:{p.Id}"));
            var amp = IsMarqueeLot(p, format) ? 0.15 : 0.25;
            return 1 + amp * (r.NextDouble() + r.NextDouble() - 1);
        }

        /// <summary>The most a real auction has paid an uncapped player, in lakhs at a ₹120 Cr purse (IPL 2026).</summary>
        public const int UncappedRecord = 1420;

        /// <summary>Most a proven player's quality can add over his market value (see FranchiseWorth).</summary>
        public const double QualityWeight = 0.25;

        /// <summary>What his rating alone would fetch (the unvalued-player formula, no star premium), in lakhs.</summary>
        public static double QualityPrice(Player p, AuctionFormat format)
        {
            var a = (double)Math.Max(0, p.Impact - 55);
            return 2.5 * (a * Math.Sqrt(a) * Math.Sqrt(Math.Sqrt(a))) * format.Purse / 12000;
        }

        /// <summary>
        /// The franchise AI's worth of a player: the reputation price, plus a share of what his
        /// rating says he is worth when that is more than his market value. A 78-rated overseas
        /// opener valued at his 2 Cr base drew one bid at base every run (the rater's dead opening
        /// lots); real teams fight over him (Conway went for 6.25 Cr in 2025). The share grows
        /// from nothing at impact 70 to QualityWeight at 82+, half that for an uncapped player,
        /// so players rated under 70 are not inflated at all. Unvalued players: the reputation price.
        /// </summary>
        public static double FranchiseWorth(Player p, AuctionFormat format)
        {
            var rep = ReputationPrice(p, format);
            if (p.Value <= 0) return rep;
            var quality = QualityPrice(p, format);
            if (quality <= rep) return rep;
            var w = QualityWeight * DartMath.Clamp((p.Impact - 70) / 12.0, 0, 1) * (p.Capped ? 1 : 0.5);
            return rep + (quality - rep) * w;
        }
    }
}
