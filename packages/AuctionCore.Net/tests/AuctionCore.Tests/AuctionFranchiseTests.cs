using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apl.Core;
using Xunit;
using Xunit.Abstractions;

namespace Apl.Core.Tests
{
    /// <summary>
    /// The solo auction (franchise AI, IPL sets, 28 Cr cap) on the live dataset: every franchise
    /// retains and bids like a real one (owner: "Mumbai Heroes kept one player and didn't bid"),
    /// SIMULATE finishes the auction inside the rules, the set list follows the IPL's, and star
    /// names fetch star prices without lifting the rest of the market.
    /// </summary>
    public class AuctionFranchiseTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public AuctionFranchiseTests(ITestOutputHelper output) => _out = output;

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static Dictionary<string, List<Player>> Retentions(AuctionFormat format) =>
            Data.Franchises.ToDictionary(f => f.Id, f => Ai.ChooseRetentions(f.Id, Data.Players, Data.Rules, format));

        private static AuctionEngine Solo(string formatId, long seed, bool retention)
        {
            var format = Data.Rules.Format(formatId);
            return new AuctionEngine(Data.Rules, format, Data.Franchises, Data.Players, seed,
                retention ? Retentions(format) : null, Data.Rules.MaxPrice, LotOrder.IplSets);
        }

        /// <summary>Runs the AI auction, counting each team's bids.</summary>
        private static Dictionary<string, int> RunCounting(AuctionEngine e, IReadOnlyList<AiBidder> bidders)
        {
            var bids = e.Teams.Keys.ToDictionary(k => k, _ => 0);
            while (e.OpenNextLot() != null)
            {
                while (true)
                {
                    AiBidder? fastest = null;
                    var decision = AiDecision.Pass;
                    foreach (var b in bidders)
                    {
                        var d = b.Decide(e);
                        if (d.Bid && (fastest == null || d.DelayMs < decision.DelayMs))
                        {
                            fastest = b;
                            decision = d;
                        }
                    }
                    if (fastest == null) break;
                    Assert.Null(e.PlaceBid(fastest.TeamId, decision.Amount));
                    bids[fastest.TeamId]++;
                }
                if (Ai.SettleRtm(e, bidders) == null) e.CloseLot();
            }
            return bids;
        }

        [Fact]
        public void Every_franchise_retains_its_core()
        {
            foreach (var formatId in new[] { "mega", "standard" })
            {
                var format = Data.Rules.Format(formatId);
                var max = Data.Rules.RetentionFor(format).MaxPlayers;
                foreach (var kv in Retentions(format))
                {
                    var kept = kv.Value;
                    _out.WriteLine($"{formatId} {kv.Key}: {kept.Count} [{string.Join(", ", kept.Select(p => p.Name))}]");
                    Assert.Null(Retention.Problem(kv.Key, kept, Data.Rules, format));
                    // Mega (3 slabs): the full three, like a real franchise keeping its stars.
                    // Mini auction (keep up to 11): most of the core, never a near-empty table.
                    if (formatId == "mega") Assert.Equal(max, kept.Count);
                    else Assert.InRange(kept.Count, 7, max);
                    // One of his three best players is always kept (the slabs go best-first, so a
                    // top name worth far less than 18 Cr can lose out to the next one).
                    var own = Data.Players.Where(p => p.PreviousTeam == kv.Key).ToList();
                    own.Sort(Retention.ByValue);
                    Assert.Contains(kept, p => own.Take(3).Contains(p));
                }
            }
            // The owner's case: Mumbai keeps its stars in a mega auction (was two uncapped players).
            var mbh = Ai.ChooseRetentions("MBH", Data.Players, Data.Rules, Data.Rules.Format("mega"));
            Assert.Equal(3, mbh.Count(p => p.Capped));
            Assert.Contains(mbh, p => p.Id == "r129");
        }

        [Theory]
        [InlineData("mega", true)]
        [InlineData("mega", false)]
        [InlineData("standard", true)]
        public void No_franchise_is_passive(string formatId, bool retention)
        {
            const int seeds = 8;
            var format = Data.Rules.Format(formatId);
            var totals = Data.Franchises.ToDictionary(f => f.Id, _ => new double[4]);
            foreach (var seed in Enumerable.Range(1, seeds))
            {
                var e = Solo(formatId, seed, retention);
                var bids = RunCounting(e, Ai.CreateBidders(e, seed * 31));
                Assert.True(e.IsFinished);
                foreach (var t in e.Teams.Values)
                {
                    var tot = totals[t.Id];
                    tot[0] += bids[t.Id];
                    tot[1] += t.Squad.Count(p => p.Kind == PurchaseKind.Auction || p.Kind == PurchaseKind.Rtm);
                    tot[2] += t.Purse;
                    tot[3] += t.Squad.Count(p => p.Kind == PurchaseKind.AutoFill);
                    Assert.InRange(t.Size, format.MinSquad, format.MaxSquad);
                    Assert.True(t.OverseasCount <= format.MaxOverseas);
                    Assert.True(t.Purse >= 0);
                    Assert.True(t.Players.Count(p => p.IsKeeper) >= 1, $"{t.Id} has no keeper");
                    Assert.True(t.Players.Count(p => p.IsBowlingOption) >= 5, $"{t.Id} has under 5 bowling options");
                }
            }
            _out.WriteLine($"{formatId} {(retention ? "retention" : "fresh")} x{seeds}: team | bids | bought | purse left | auto-fill");
            foreach (var kv in totals)
            {
                var m = kv.Value.Select(x => x / seeds).ToArray();
                _out.WriteLine($"  {kv.Key} | {m[0]:0} | {m[1]:0.0} | {m[2] / 100:0.0} Cr | {m[3]:0.0}");
                Assert.True(m[0] >= 40, $"{kv.Key} made only {m[0]:0} bids an auction");
                Assert.True(m[1] >= 8, $"{kv.Key} bought only {m[1]:0.0} players an auction");
                // Nobody sits on a fat purse: at most a fifth of it unspent, on average.
                Assert.True(m[2] <= format.Purse * 0.2, $"{kv.Key} left {m[2] / 100:0.0} Cr unspent");
                Assert.True(m[3] <= 1.5, $"{kv.Key} needed {m[3]:0.0} auto-fills");
            }
        }

        [Fact]
        public void Star_names_fetch_star_prices_and_the_market_stays_put()
        {
            const int seeds = 20;
            int Median(List<int> l) => l.OrderBy(x => x).ElementAt(l.Count / 2);
            // The same auctions (IPL sets, 28 Cr cap) with the old AI are the "before".
            (Dictionary<string, List<int>> byTier, List<int> rohit, double over20, int max) Run(AiModel model)
            {
                var byTier = new Dictionary<string, List<int>>();
                var rohit = new List<int>();
                var over20 = new List<int>();
                var max = 0;
                foreach (var seed in Enumerable.Range(1, seeds))
                {
                    var e = Solo("mega", seed, retention: false);
                    Ai.RunAuction(e, Ai.CreateBidders(e, seed * 31, null, model));
                    var sold = e.Results.Where(r => r.Sold).ToList();
                    over20.Add(sold.Count(r => r.Price!.Value > 2000));
                    foreach (var r in sold)
                    {
                        max = Math.Max(max, r.Price!.Value);
                        var p = r.Player;
                        var tier = p.Set == "marquee" ? "marquee" : Market.ReputationPrice(p, e.Format) >= 1000 ? "star" : p.Capped ? "capped" : "uncapped";
                        if (!byTier.TryGetValue(tier, out var l)) byTier[tier] = l = new List<int>();
                        l.Add(r.Price!.Value);
                        // The ordinary end of the market: rated under 70, capped or not.
                        if (tier == "capped" || tier == "uncapped")
                        {
                            var weak = p.Impact < 70 ? tier + "<70" : tier + "70+";
                            if (!byTier.TryGetValue(weak, out var w)) byTier[weak] = w = new List<int>();
                            w.Add(r.Price!.Value);
                        }
                        if (p.Id == "r129") rohit.Add(r.Price!.Value);
                    }
                }
                return (byTier, rohit, over20.Average(), max);
            }
            var before = Run(AiModel.Classic);
            var after = Run(AiModel.Franchise);
            foreach (var tier in new[] { "marquee", "star", "capped", "capped70+", "capped<70", "uncapped", "uncapped70+", "uncapped<70" })
            {
                if (!before.byTier.ContainsKey(tier) || !after.byTier.ContainsKey(tier)) continue;
                _out.WriteLine($"{tier,-11} median {Median(before.byTier[tier]) / 100.0:0.00} -> {Median(after.byTier[tier]) / 100.0:0.00} Cr ({after.byTier[tier].Count} sales)");
            }
            _out.WriteLine($"veteran Mumbai opener (r129): median {Median(before.rohit) / 100.0:0.00} -> {Median(after.rohit) / 100.0:0.00} Cr; over-20 Cr per auction {before.over20:0.0} -> {after.over20:0.0}; dearest {before.max / 100.0:0.00} -> {after.max / 100.0:0.00} Cr");

            Assert.True(after.max <= Data.Rules.MaxPrice!.Value);
            // The veteran star opener (value 12.4 Cr, star power 95): a marquee price, not 12-13 Cr.
            Assert.Equal(seeds, after.rohit.Count);
            Assert.InRange(Median(after.rohit), 1700, 2400);
            Assert.InRange(Median(after.byTier["marquee"]), 1700, 2300);
            // Weak players are not inflated: the under-70 capped and uncapped medians within 15% of before.
            foreach (var tier in new[] { "capped<70", "uncapped<70", "uncapped" })
            {
                Assert.True(Median(after.byTier[tier]) <= Median(before.byTier[tier]) * 1.15 + 5, $"{tier} median {Median(before.byTier[tier])} -> {Median(after.byTier[tier])}");
            }
            // Proven (70+) capped players now draw a contest instead of one bid at base (round-1 rater:
            // dead opening lots, 24% sold at base on one bid), so their median rises a step or three
            // (2 Cr base → 3-3.5 Cr; Conway 6.25, Rachin 4, Hasaranga 5.25 Cr in 2025), never past +50%.
            Assert.True(Median(after.byTier["capped"]) <= Median(before.byTier["capped"]) * 1.5 + 5, $"capped median {Median(before.byTier["capped"])} -> {Median(after.byTier["capped"])}");
            Assert.InRange(after.over20, 2.5, 8.0);
        }

        [Fact]
        public void Reputation_premium_only_lifts_capped_stars()
        {
            var format = Data.Rules.Format("mega");
            foreach (var p in Data.Players)
            {
                var fair = Market.FairPrice(p, format);
                var rep = Market.ReputationPrice(p, format);
                Assert.True(rep >= fair);
                if (!p.Capped || p.StarPower < Market.ReputationFrom || p.Impact <= 70) Assert.Equal(fair, rep);
            }
        }

        [Theory]
        [InlineData("standard", true, true)]
        [InlineData("standard", true, false)]
        [InlineData("mega", false, true)]
        public void Simulate_to_end_keeps_every_rule(string formatId, bool retention, bool autoBid)
        {
            foreach (var seed in new long[] { 3, 11 })
            {
                var e = Solo(formatId, seed, retention);
                var humans = new[] { "MBH" };
                var live = new LiveAuction(e, Ai.CreateBidders(e, seed * 31, humans), humans);
                var finished = 0;
                live.OnEvent += ev => { if (ev is AuctionFinished) finished++; };
                live.Start();
                // A few lots by hand (the human bids now and then), then SIMULATE mid-lot.
                var rng = new Rng(seed);
                for (var i = 0; i < 4000 && e.Results.Count < 12; i++)
                {
                    if (live.Phase == LivePhase.Bidding && rng.Chance(0.02)) live.HumanBid("MBH");
                    if (live.PendingRtm?.TeamId == "MBH") live.HumanRtm("MBH", false);
                    live.Advance(200);
                }
                var mineBefore = e.Teams["MBH"].Squad.Count(p => p.Kind == PurchaseKind.Auction || p.Kind == PurchaseKind.Rtm);
                live.SimulateToEnd(autoBid, seed);

                Assert.Equal(LivePhase.Finished, live.Phase);
                Assert.True(e.IsFinished);
                Assert.Equal(1, finished);
                var f = e.Format;
                foreach (var t in e.Teams.Values)
                {
                    Assert.InRange(t.Size, f.MinSquad, f.MaxSquad);
                    Assert.True(t.OverseasCount <= f.MaxOverseas, $"{t.Id} overseas {t.OverseasCount}");
                    Assert.True(t.Purse >= 0, $"{t.Id} purse {t.Purse}");
                    Assert.True(t.RtmCards >= 0);
                    Assert.Equal(f.Purse - t.Spent, t.Purse);
                }
                Assert.All(e.Results.Where(r => r.Sold), r => Assert.True(r.Price!.Value <= Data.Rules.MaxPrice!.Value));
                // Each player sold at most once.
                var bought = e.Teams.Values.SelectMany(t => t.Players).ToList();
                Assert.Equal(bought.Count, bought.Distinct().Count());

                var me = e.Teams["MBH"];
                var mineAfter = me.Squad.Count(p => p.Kind == PurchaseKind.Auction || p.Kind == PurchaseKind.Rtm);
                var auto = me.Squad.Count(p => p.Kind == PurchaseKind.AutoFill);
                _out.WriteLine($"{formatId} seed {seed} autoBid {autoBid}: MBH bought {mineBefore} by hand, {mineAfter - mineBefore} simulated, {auto} auto-fill; squad {me.Size}, purse {me.Purse}, keepers {me.Players.Count(p => p.IsKeeper)}, bowling options {me.Players.Count(p => p.IsBowlingOption)}");
                if (autoBid)
                {
                    // The stand-in bidder filled the squad at auction, not from the base-price filler.
                    Assert.True(mineAfter > mineBefore);
                    Assert.True(me.Players.Count(p => p.IsKeeper) >= 1);
                    Assert.True(me.Players.Count(p => p.IsBowlingOption) >= 5);
                    // It stops near the minimum: no splurge to a full 25.
                    Assert.True(me.Size <= Market.SquadTargets(f).Size + 1, $"autopilot bought to {me.Size}");
                }
                else
                {
                    // Stopped bidding: nothing more bought at auction, the rest auto-filled to the minimum.
                    Assert.Equal(mineBefore, mineAfter);
                }
            }
        }

        [Fact]
        public void Lots_come_in_ipl_sets()
        {
            var format = Data.Rules.Format("mega");
            var sets = AuctionSets.Build(Data.Players, format, Data.Rules, new Rng(5));
            var codes = sets.Select(s => s.Code).ToList();
            _out.WriteLine(string.Join(" ", sets.Select(s => $"{s.Code}({s.Players.Count})")));
            Assert.Equal(new[] { "M1", "M2", "BA1", "AL1", "WK1", "FA1", "SP1", "UBA1", "UAL1", "UWK1", "UFA1", "USP1", "BA2" }, codes.Take(13));
            Assert.All(sets, s => Assert.InRange(s.Players.Count, 1, AuctionSets.SetSize));
            var all = sets.SelectMany(s => s.Players).ToList();
            Assert.Equal(Data.Players.Count, all.Count);
            Assert.Equal(all.Count, all.Distinct().Count());
            // The early sets hold the big names: BA1's least base price is at least BA2's most.
            var ba1 = sets.First(s => s.Code == "BA1").Players;
            var ba2 = sets.First(s => s.Code == "BA2").Players;
            Assert.True(ba1.Min(p => p.BasePrice) >= ba2.Max(p => p.BasePrice));
            // Sets only ever use the existing voice keys.
            var voices = new HashSet<string> { "marquee", "batters", "bowlers", "allRounders", "wicketKeepers", "uncapped", "accelerated" };
            Assert.All(codes.Append(AuctionSets.Accelerated), c => Assert.Contains(AuctionSets.VoiceSet(c), voices));

            // The engine follows that order and labels each lot with its set, accelerated round included.
            var e = Solo("standard", 9, retention: true);
            Assert.Equal(LotOrder.IplSets, e.LotOrder);
            var seen = new List<string>();
            Ai.RunAuction(e, Ai.CreateBidders(e, 9));
            foreach (var r in e.Results)
            {
                var code = r.Round == LotRound.Accelerated ? AuctionSets.Accelerated : e.SetOf(r.Player);
                if (seen.Count == 0 || seen[seen.Count - 1] != code) seen.Add(code);
            }
            _out.WriteLine("standard run: " + string.Join(" ", seen));
            Assert.Equal(seen.Count, seen.Distinct().Count());
            // In the list's order (sets with no eligible lot are skipped), the accelerated round last.
            var order = new[] { "M", "BA", "AL", "WK", "FA", "SP", "UBA", "UAL", "UWK", "UFA", "USP" };
            int Rank(string c)
            {
                var digits = new string(c.Where(char.IsDigit).ToArray());
                var head = c.Substring(0, c.Length - digits.Length);
                return head == "M" ? 0 : int.Parse(digits) * 100 + Array.IndexOf(order, head);
            }
            var main = seen.Where(c => c != AuctionSets.Accelerated).ToList();
            Assert.Equal(main, main.OrderBy(Rank).ToList());
            if (seen.Contains(AuctionSets.Accelerated)) Assert.Equal(AuctionSets.Accelerated, seen[seen.Count - 1]);
        }
    }
}
