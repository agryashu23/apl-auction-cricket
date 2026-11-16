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
    /// Price sanity on the live dataset (area 2): full AI auctions over 20 seeds.
    /// Mega: about four players a year above 20 Cr, nobody past the 28 Cr cap, and the
    /// top prices go to top or famous players. Standard (mini auction, retention at
    /// value): nobody past the cap, and retained players keep their market value.
    /// The table it prints is the evidence for the hand-back.
    /// </summary>
    public class AuctionPriceTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public AuctionPriceTests(ITestOutputHelper output) => _out = output;

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static List<LotResult> Run(string formatId, long seed, bool retention)
        {
            var format = Data.Rules.Format(formatId);
            var retentions = new Dictionary<string, List<Player>>();
            if (retention)
            {
                foreach (var t in Data.Franchises) retentions[t.Id] = Ai.ChooseRetentions(t.Id, Data.Players, Data.Rules, format);
            }
            // As solo plays it: IPL sets, the 28 Cr cap, the franchise AI.
            var e = new AuctionEngine(Data.Rules, format, Data.Franchises, Data.Players, seed, retentions, Data.Rules.MaxPrice, LotOrder.IplSets);
            Ai.RunAuction(e, Ai.CreateBidders(e, seed * 31));
            return e.Results.ToList();
        }

        [Fact]
        public void Mega_auction_top_prices_are_believable()
        {
            const int seeds = 20;
            var over20 = new List<int>();
            var top = new Dictionary<string, List<int>>();
            var max = 0;
            foreach (var seed in Enumerable.Range(1, seeds))
            {
                var sold = Run("mega", seed, retention: false).Where(r => r.Sold).ToList();
                over20.Add(sold.Count(r => r.Price!.Value > 2000));
                max = Math.Max(max, sold.Max(r => r.Price!.Value));
                foreach (var r in sold.OrderByDescending(r => r.Price).Take(10))
                {
                    if (!top.TryGetValue(r.Player.Id, out var l)) top[r.Player.Id] = l = new List<int>();
                    l.Add(r.Price!.Value);
                }
            }
            var avg = over20.Average();
            _out.WriteLine($"mega x{seeds}: players over 20 Cr per auction {string.Join(",", over20)} (mean {avg:0.0}); dearest {max / 100.0:0.00} Cr");
            _out.WriteLine("most frequent top-10 buys (player, impact, star, value L: times in top 10, mean price):");
            foreach (var kv in top.OrderByDescending(kv => kv.Value.Count).ThenByDescending(kv => kv.Value.Average()).Take(20))
            {
                var p = Data.Players.First(x => x.Id == kv.Key);
                _out.WriteLine($"  {p.Id} {p.Name,-24} {p.Role.Code(),-4} imp {p.Impact,3} star {p.StarPower,3} value {p.Value,5}: {kv.Value.Count,2}x  {kv.Value.Average() / 100.0:0.00} Cr");
            }
            Assert.InRange(avg, 2.5, 7.0);
            Assert.True(max <= Data.Rules.MaxPrice!.Value, $"a sale above the cap: {max}");
            // The usual top-10 buys are top or famous players, not fringe ones.
            foreach (var kv in top.Where(kv => kv.Value.Count >= seeds / 2))
            {
                var p = Data.Players.First(x => x.Id == kv.Key);
                Assert.True(p.Impact >= 78 || p.StarPower >= 50, $"{p.Name} (impact {p.Impact}, star {p.StarPower}) is a regular top-10 buy");
            }
        }

        [Fact]
        public void Standard_mini_auction_stays_under_the_cap_and_retains_at_value()
        {
            var format = Data.Rules.Format("standard");
            var max = 0;
            var dearUncapped = 0;
            var maxUncapped = 0;
            foreach (var seed in Enumerable.Range(1, 20))
            {
                var sold = Run("standard", seed, retention: true).Where(r => r.Sold).ToList();
                max = Math.Max(max, sold.Max(r => r.Price!.Value));
                foreach (var r in sold.Where(r => !r.Player.Capped))
                {
                    maxUncapped = Math.Max(maxUncapped, r.Price!.Value);
                    if (r.Player.BasePrice > 30 || r.Price!.Value <= 1000) continue;
                    // A 30 L uncapped player going for 10 Cr+ is only fine when the market backs it:
                    // Prashant Veer and Kartik Sharma really went for 14.2 Cr each at the 2026 auction.
                    var backed = r.Player.Value >= 800;
                    if (!backed) dearUncapped++;
                    _out.WriteLine($"  {(backed ? "exempt" : "SILLY ")} seed {seed}: {r.Player.Id} {r.Player.Name} uncapped base {r.Player.BasePrice} value {r.Player.Value} impact {r.Player.Impact} sold {r.Price} to {r.TeamId}");
                }
            }
            _out.WriteLine($"standard x20: dearest {max / 100.0:0.00} Cr; dearest uncapped {maxUncapped / 100.0:0.00} Cr; unbacked 30 L uncapped sold over 10 Cr: {dearUncapped}");
            Assert.True(max <= Data.Rules.MaxPrice!.Value);
            Assert.Equal(0, dearUncapped);
            // No uncapped player beyond the most a real mini auction has paid one (14.2 Cr, IPL 2026).
            Assert.True(maxUncapped <= 1420, $"an uncapped player went for {maxUncapped}");

            // Retention at value: every retained player costs his market value (scaled to the purse).
            foreach (var t in Data.Franchises)
            {
                var kept = Ai.ChooseRetentions(t.Id, Data.Players, Data.Rules, format);
                var prices = Retention.Prices(kept, Data.Rules, format);
                foreach (var p in kept)
                {
                    var expected = Math.Max(p.BasePrice, p.Value * format.Purse / 12000);
                    Assert.True(Math.Abs(prices.First(kv => kv.Key.Equals(p)).Value - expected) <= 5,
                        $"{p.Name} retained at {prices.First(kv => kv.Key.Equals(p)).Value}, value {expected}");
                }
            }
        }
    }
}
