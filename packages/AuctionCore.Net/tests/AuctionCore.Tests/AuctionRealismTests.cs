using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apl.Core;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Apl.Core.Tests
{
    /// <summary>
    /// Round-1 rater fixes (build/review/ratings/auction-r1.md, items 1, 11, 12, 17): the owner's bid
    /// increments in both dataset copies, AI limits on real bid steps, live opening lots, prices that
    /// move from one auction to the next, purses spent, and a season that follows the squads.
    /// </summary>
    public class AuctionRealismTests
    {
        private static readonly string Root = FindRoot();
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(Root, "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public AuctionRealismTests(ITestOutputHelper output) => _out = output;

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static AuctionEngine Solo(string formatId, long seed, bool retention = true)
        {
            var format = Data.Rules.Format(formatId);
            var keeps = retention ? Data.Franchises.ToDictionary(f => f.Id, f => Ai.ChooseRetentions(f.Id, Data.Players, Data.Rules, format)) : null;
            return new AuctionEngine(Data.Rules, format, Data.Franchises, Data.Players, seed, keeps, Data.Rules.MaxPrice, LotOrder.IplSets);
        }

        // ------------------------------------------------------------------ data + increments

        [Fact]
        public void Client_dataset_is_byte_identical_to_the_server_copy()
        {
            // The room server loads data/; the client ships client-src's copy. Same bytes or bids break (r1 #1).
            var server = File.ReadAllBytes(Path.Combine(Root, "data", "apl_dataset.json"));
            var client = File.ReadAllBytes(Path.Combine(Root, "client-src", "Assets", "Resources", "Data", "apl_dataset.json"));
            Assert.True(server.SequenceEqual(client),
                "client-src/Assets/Resources/Data/apl_dataset.json differs from data/apl_dataset.json: run tools/data/build_dataset.py (it writes both) and bump the version");
            Assert.True(Data.Version >= 8, $"dataset version {Data.Version}: the v8 increments need version 8+");
        }

        [Fact]
        public void Increments_follow_the_owners_rule()
        {
            // Owner: +10 L below 50 L, +25 L from 50 L to 2 Cr, +50 L from 2 Cr up.
            void Check(JArray inc, string where)
            {
                Assert.True(inc.Count == 3, $"{where}: {inc}");
                Assert.True((int?)inc[0]["below"] == 50 && (int)inc[0]["step"]! == 10, $"{where}: {inc}");
                Assert.True((int?)inc[1]["below"] == 200 && (int)inc[1]["step"]! == 25, $"{where}: {inc}");
                Assert.True(inc[2]["below"] == null && (int)inc[2]["step"]! == 50, $"{where}: {inc}");
            }
            Check((JArray)JObject.Parse(File.ReadAllText(Path.Combine(Root, "data", "apl_dataset.json")))["rules"]!["increments"]!, "data/apl_dataset.json");
            Check((JArray)JObject.Parse(File.ReadAllText(Path.Combine(Root, "data", "source", "league.json")))["rules"]!["increments"]!, "data/source/league.json");

            var r = Data.Rules;
            Assert.Equal(10, r.IncrementFor(30));
            Assert.Equal(10, r.IncrementFor(40));
            Assert.Equal(25, r.IncrementFor(50));
            Assert.Equal(25, r.IncrementFor(175));
            Assert.Equal(50, r.IncrementFor(200));
            Assert.Equal(50, r.IncrementFor(2750));
            // The ladder from a 30 L base: 30 40 50 75 … 175 200 250 …
            var ladder = new List<int> { 30 };
            while (ladder.Count < 12) ladder.Add(r.NextBid(ladder[ladder.Count - 1], 30));
            Assert.Equal(new[] { 30, 40, 50, 75, 100, 125, 150, 175, 200, 250, 300, 350 }, ladder);

            // Every base price, the price cap and the room cap are prices a lot can actually reach.
            foreach (var b in r.BasePrices)
            {
                Assert.True(r.IsReachableBid(b, r.MinBasePrice), $"base {b} is off the ladder from {r.MinBasePrice}");
                Assert.True(r.IsReachableBid(r.MaxPrice!.Value, b), $"the cap {r.MaxPrice} can't be reached from base {b}");
                Assert.True(r.IsReachableBid(r.RoomMaxPrice!.Value, b), $"the room cap {r.RoomMaxPrice} can't be reached from base {b}");
            }
        }

        [Fact]
        public void FloorToBid_lands_on_the_ladder()
        {
            var r = Data.Rules;
            Assert.Equal(0, r.FloorToBid(29, 30));
            Assert.Equal(30, r.FloorToBid(39, 30));
            Assert.Equal(50, r.FloorToBid(74, 30));
            Assert.Equal(75, r.FloorToBid(75, 30));
            Assert.Equal(175, r.FloorToBid(199, 50));
            Assert.Equal(200, r.FloorToBid(249, 200));
            Assert.Equal(2750, r.FloorToBid(2799, 200));
            Assert.Equal(2800, r.FloorToBid(2800, 30));
            for (var amount = 0; amount <= 3000; amount += 7)
            {
                foreach (var b in r.BasePrices)
                {
                    var f = r.FloorToBid(amount, b);
                    Assert.True(f == 0 ? amount < b : r.IsReachableBid(f, b) && f <= amount && r.NextBid(f, b) > amount, $"FloorToBid({amount}, {b}) = {f}");
                }
            }
        }

        [Fact]
        public void Ai_limits_and_prices_are_always_real_bid_steps()
        {
            foreach (var formatId in new[] { "standard", "mega" })
            {
                var e = Solo(formatId, 5);
                var bidders = Ai.CreateBidders(e, 5 * 31);
                var mine = new HashSet<AiBidder>(bidders);
                var bad = new List<string>();
                var count = 0;
                Action<AiBidder, Lot, int> check = (b, lot, limit) =>
                {
                    if (!mine.Contains(b)) return; // other tests' auctions share the static event
                    count++;
                    if (limit != 0 && !Data.Rules.IsReachableBid(limit, lot.Player.BasePrice)) bad.Add($"{b.TeamId} lot {lot.Number} base {lot.Player.BasePrice} limit {limit}");
                };
                AiBidder.LimitSet += check;
                try
                {
                    Ai.RunAuction(e, bidders);
                }
                finally
                {
                    AiBidder.LimitSet -= check;
                }
                Assert.True(count > 100);
                Assert.True(bad.Count == 0, string.Join("\n", bad.Take(10)));
                Assert.All(e.Results.Where(r => r.Sold), r => Assert.True(Data.Rules.IsReachableBid(r.Price!.Value, r.Player.BasePrice), $"{r.Player.Name} sold at {r.Price}"));
            }
        }

        [Fact]
        public void A_format_can_set_its_own_rtm_cards()
        {
            // r1 #25: the real IPL has RTM only at a mega auction; data may set formats.standard.rtmCards = 0.
            var json = JObject.Parse(File.ReadAllText(Path.Combine(Root, "data", "apl_dataset.json")));
            json["rules"]!["formats"]!["standard"]!["rtmCards"] = 0;
            var data = Dataset.FromJson(json);
            var mini = data.Rules.Format("standard");
            Assert.Equal(0, data.Rules.RtmCardsFor(mini));
            Assert.Equal(data.Rules.RtmCards, data.Rules.RtmCardsFor(data.Rules.Format("mega")));
            var e = new AuctionEngine(data.Rules, mini, data.Franchises, data.Players, 4, null, data.Rules.MaxPrice, LotOrder.IplSets);
            Assert.All(e.Teams.Values, t => Assert.Equal(0, t.RtmCards));
            Ai.RunAuction(e, Ai.CreateBidders(e, 4));
            Assert.DoesNotContain(e.Results, r => r.ViaRtm);
            // Unset (today's data): the league-wide count.
            Assert.Null(Data.Rules.Format("standard").RtmCards);
        }

        // ------------------------------------------------------------------ AI franchises

        [Fact]
        public void A_rebuilt_bidder_keeps_its_mood_and_targets()
        {
            // Rooms rebuild bidders on reload (new seeds); a solo resume too. The mood comes from the auction's seed.
            var e = Solo("standard", 42);
            var a = new AiBidder("MBH", Personality.Balanced, 1, AiModel.Franchise).MoodFor(e);
            var b = new AiBidder("MBH", Personality.Aggressive, 999, AiModel.Franchise).MoodFor(e);
            Assert.Equal(a.Aggression, b.Aggression);
            Assert.Equal(a.WarAppetite, b.WarAppetite);
            Assert.All(Data.Players, p => Assert.Equal(a.Affinity(p), b.Affinity(p)));
            Assert.InRange(a.Aggression, 0.88, 1.08);
            Assert.InRange(a.WarAppetite, 0.5, 1.5);
            // Another auction: another mood, other targets.
            var other = new AiBidder("MBH", Personality.Balanced, 1, AiModel.Franchise).MoodFor(Solo("standard", 43));
            Assert.Contains(Data.Players, p => other.Affinity(p) != a.Affinity(p));
            // Each franchise has a few targets among the good players, not dozens; AutoPilot has no mood.
            var good = Data.Players.Where(p => p.Impact >= 72).ToList();
            var targets = good.Count(p => a.IsTarget(p));
            _out.WriteLine($"MBH targets this auction: {targets} of {good.Count} good players");
            Assert.InRange(targets, 1, good.Count / 5);
            Assert.All(Data.Players, p => Assert.Equal(1.0, FranchiseMood.Neutral.Affinity(p)));
        }

        /// <summary>The rater's sim stats over a few seeds of one format.</summary>
        private sealed class Stats
        {
            public int Sold, OneBidAtBase, GoodOpeners, DeadOpeners, Teams, Over10Cr;
            public Dictionary<string, List<int>> Prices = new Dictionary<string, List<int>>();
        }

        private static Stats Run(string formatId, int seeds)
        {
            var s = new Stats();
            for (var seed = 1; seed <= seeds; seed++)
            {
                var e = Solo(formatId, seed);
                Ai.RunAuction(e, Ai.CreateBidders(e, seed * 7919));
                string? lastSet = null;
                foreach (var r in e.Results)
                {
                    var set = r.Round == LotRound.Accelerated ? AuctionSets.Accelerated : e.SetOf(r.Player);
                    var opener = set != lastSet;
                    lastSet = set;
                    if (opener && r.Round == LotRound.Main && r.Player.Capped && r.Player.Impact >= 72)
                    {
                        s.GoodOpeners++;
                        if (r.BidCount <= 1) s.DeadOpeners++;
                    }
                    if (!r.Sold) continue;
                    s.Sold++;
                    if (r.BidCount <= 1 && r.Price == r.Player.BasePrice) s.OneBidAtBase++;
                    if (!s.Prices.TryGetValue(r.Player.Id, out var l)) s.Prices[r.Player.Id] = l = new List<int>();
                    l.Add(r.Price!.Value);
                }
                foreach (var t in e.Teams.Values)
                {
                    s.Teams++;
                    if (t.Purse > 1000) s.Over10Cr++;
                }
            }
            return s;
        }

        [Theory]
        [InlineData("standard")]
        [InlineData("mega")]
        public void Lots_are_contested_prices_move_and_purses_get_spent(string formatId)
        {
            const int seeds = 10;
            var s = Run(formatId, seeds);
            var oneBid = (double)s.OneBidAtBase / s.Sold;
            var dead = (double)s.DeadOpeners / Math.Max(1, s.GoodOpeners);
            var rich = (double)s.Over10Cr / s.Teams;
            var top = s.Prices.Where(kv => kv.Value.Count >= seeds / 2).OrderByDescending(kv => kv.Value.Average()).Take(15).ToList();
            double Cv(List<int> l)
            {
                var m = l.Average();
                return Math.Sqrt(l.Sum(x => (x - m) * (x - m)) / l.Count) / m;
            }
            var cvs = top.Select(kv => Cv(kv.Value)).OrderBy(x => x).ToList();
            var medianCv = cvs[cvs.Count / 2];
            var within10 = (double)top.Sum(kv => kv.Value.Count(x => Math.Abs(x - kv.Value.Average()) <= 0.1 * kv.Value.Average())) / top.Sum(kv => kv.Value.Count);
            _out.WriteLine($"{formatId} x{seeds}: one bid at base {oneBid:P0}; dead 72+ capped set openers {dead:P0} of {s.GoodOpeners}; teams >10 Cr left {rich:P0}; top-15 median CV {medianCv:P0}, within ±10% {within10:P0}");

            // Round 1: 24% / 23% / 33% / 7% (CV) / 86% within ±10% (standard).
            Assert.True(oneBid <= 0.20, $"{oneBid:P0} of sales went at base on one bid");
            Assert.True(dead <= 0.18, $"{dead:P0} of good capped set openers drew a single bid");
            Assert.True(rich <= 0.15, $"{rich:P0} of teams ended with over 10 Cr");
            Assert.True(medianCv >= 0.06, $"top-15 prices barely move between auctions (median CV {medianCv:P0})");
            Assert.True(within10 <= 0.78, $"{within10:P0} of top-15 sales within ±10% of the player's mean");
        }

        // ------------------------------------------------------------------ season

        [Fact]
        public void The_season_follows_the_squads_with_upsets()
        {
            var spear = new List<double>();
            var champRanks = new List<int>();
            var format = Data.Rules.Format("standard");
            for (var seed = 1; seed <= 6; seed++)
            {
                var e = Solo("standard", seed);
                Ai.RunAuction(e, Ai.CreateBidders(e, seed * 7919));
                var ratings = Ratings.Rate(e.Teams.Values, Data.Rules.MaxOverseasInXI, format.MinSquad - 11, Data.Rules.RatingFor(format));
                var squads = new OrderedMap<string, List<Player>>();
                foreach (var t in e.Teams.Values) squads[t.Id] = t.Players;
                for (var k = 0; k < 4; k++)
                {
                    var season = Seasons.Simulate(squads, seed * 1000 + k, Data.Rules.MaxOverseasInXI);
                    var pos = season.Table.Select((row, i) => (row.TeamId, i)).ToDictionary(x => x.TeamId, x => x.i);
                    var n = ratings.Count;
                    var d2 = ratings.Sum(r => Math.Pow(r.Rank - 1 - pos[r.TeamId], 2));
                    spear.Add(1 - 6 * d2 / (n * (n * n - 1.0)));
                    champRanks.Add(ratings.First(r => r.TeamId == season.ChampionId).Rank);
                }
            }
            _out.WriteLine($"24 seasons: rank correlation squad rating vs table {spear.Average():0.00}; champion's squad rank {string.Join(",", champRanks)}");
            // Round 1 (classic season): ~0.35. Better squads usually finish higher …
            Assert.True(spear.Average() >= 0.5, $"rank correlation {spear.Average():0.00}");
            Assert.True(champRanks.Count(r => r <= 5) >= champRanks.Count * 0.8);
            // … but it isn't a formality: not every title goes to the top-rated squad.
            Assert.True(champRanks.Count(r => r == 1) < champRanks.Count);
        }

        [Fact]
        public void A_squad_of_fillers_is_a_long_shot()
        {
            // The rater's case: a team that bought nobody (auto-filled at base) won the league 7-2.
            var e = Solo("standard", 3);
            Ai.RunAuction(e, Ai.CreateBidders(e, 3 * 7919));
            var squads = new OrderedMap<string, List<Player>>();
            foreach (var t in e.Teams.Values) squads[t.Id] = t.Players;
            // BLR keeps only its four weakest players plus the weakest unsold / cheapest of the pool.
            var taken = new HashSet<string>(e.Teams.Values.SelectMany(t => t.Players).Select(p => p.Id));
            var fillers = Data.Players.Where(p => !taken.Contains(p.Id)).OrderBy(p => p.Impact).ToList();
            var weak = squads["BLR"].OrderBy(p => p.Impact).Take(4).ToList();
            weak.AddRange(fillers.Where(p => !p.Overseas).Take(14));
            if (!weak.Any(p => p.IsKeeper)) weak.Add(fillers.First(p => p.IsKeeper && !weak.Contains(p)));
            squads["BLR"] = weak;
            var edges = Seasons.SquadEdges(squads, Data.Rules.MaxOverseasInXI);
            _out.WriteLine("edges: " + string.Join(" ", edges.Select(kv => $"{kv.Key}:{kv.Value:+0.0;-0.0}")));
            Assert.Equal(edges.Values.Min(), edges["BLR"]);
            var titles = 0;
            var wins = 0.0;
            for (var k = 0; k < 20; k++)
            {
                var s = Seasons.Simulate(squads, 500 + k, Data.Rules.MaxOverseasInXI);
                if (s.ChampionId == "BLR") titles++;
                wins += s.Table.First(r => r.TeamId == "BLR").Won;
            }
            _out.WriteLine($"filler BLR: {titles} titles in 20 seasons, {wins / 20:0.0} league wins a season");
            Assert.True(titles <= 1, $"a squad of fillers won {titles} of 20 seasons");
            Assert.True(wins / 20 < 4.0, $"a squad of fillers won {wins / 20:0.0} of 9 league games a season");
        }

        [Fact]
        public void The_classic_season_is_unchanged()
        {
            // SeasonModel.Classic (the golden files) plays every team with no edge.
            var e = Solo("standard", 2);
            Ai.RunAuction(e, Ai.CreateBidders(e, 2));
            var squads = new OrderedMap<string, List<Player>>();
            foreach (var t in e.Teams.Values) squads[t.Id] = t.Players;
            var a = Seasons.Simulate(squads, 77, Data.Rules.MaxOverseasInXI, SeasonModel.Classic);
            var b = Seasons.Simulate(squads, 77, Data.Rules.MaxOverseasInXI, SeasonModel.Classic);
            Assert.Equal(a.AllMatches.Select(m => m.First.Runs * 1000 + m.Second.Runs), b.AllMatches.Select(m => m.First.Runs * 1000 + m.Second.Runs));
            var squad = Seasons.Simulate(squads, 77, Data.Rules.MaxOverseasInXI);
            Assert.NotEqual(a.AllMatches.Select(m => m.First.Runs), squad.AllMatches.Select(m => m.First.Runs));
        }
    }
}
