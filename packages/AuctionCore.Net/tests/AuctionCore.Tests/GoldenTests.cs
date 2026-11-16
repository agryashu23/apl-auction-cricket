using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apl.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Apl.Core.Tests
{
    /// <summary>
    /// The C# port must reproduce the Dart engine exactly. Golden files come
    /// from packages/auction_core/tool/export_golden.dart; regenerate them
    /// after any rule change, then run these.
    /// </summary>
    public class GoldenTests
    {
        private static readonly string Root = FindRoot();
        // The goldens were exported by the Dart engine on this exact dataset (the
        // 329-player v3 file of 2026-11-12). It is frozen here so data updates
        // (tools/data/build_dataset.py) don't need a Dart SDK to re-export them.
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(Root, "packages", "AuctionCore.Net", "golden", "dataset.json")));

        private static string FindRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "data", "apl_dataset.json"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }

        private static JToken Golden(string name) =>
            JToken.Parse(File.ReadAllText(Path.Combine(Root, "packages", "AuctionCore.Net", "golden", name + ".json")));

        private static string Lower(string s) => char.ToLowerInvariant(s[0]) + s.Substring(1);
        private static string B(bool b) => b ? "true" : "false";
        private static string N(object? o) => o?.ToString() ?? "null";

        [Fact]
        public void Rng_matches_dart()
        {
            foreach (var g in Golden("rng"))
            {
                var seed = (long)g["seed"]!;
                var u = new Rng(seed);
                Assert.Equal(g["uint32"]!.Select(x => (long)x).ToList(), Enumerable.Range(0, 40).Select(_ => (long)u.NextUint32()).ToList());
                var d = new Rng(seed);
                Assert.Equal(g["double"]!.Select(x => (double)x).ToList(), Enumerable.Range(0, 10).Select(_ => d.NextDouble()).ToList());
                var i = new Rng(seed);
                Assert.Equal(g["int7"]!.Select(x => (int)x).ToList(), Enumerable.Range(0, 40).Select(_ => i.NextInt(7)).ToList());
                var w = new Rng(seed);
                var weights = new[] { 1.0, 2.5, 0.5, 3.0 };
                Assert.Equal(g["weighted"]!.Select(x => (int)x).ToList(), Enumerable.Range(0, 40).Select(_ => w.PickWeighted(weights)).ToList());
                var r = new Rng(seed);
                Assert.Equal(g["range"]!.Select(x => (double)x).ToList(), Enumerable.Range(0, 10).Select(_ => r.Range(600, 2400)).ToList());
            }
        }

        [Fact]
        public void Lot_order_matches_dart()
        {
            foreach (var g in Golden("lots"))
            {
                var format = Data.Rules.Format((string)g["format"]!);
                var lots = AuctionEngine.SelectLots(Data.Players, format, Data.Rules, new Rng((long)g["seed"]!));
                Assert.Equal(g["lots"]!.Select(x => (string)x!).ToList(), lots.Select(p => p.Id).ToList());
            }
        }

        public static IEnumerable<object[]> AuctionCases() =>
            Golden("auctions").Select((g, i) => new object[] { i, $"{g["format"]}/{((bool)g["retention"]! ? "retention" : "fresh")}/seed{g["seed"]}" });

        [Theory]
        [MemberData(nameof(AuctionCases))]
        public void Full_ai_auction_rating_and_season_match_dart(int index, string label)
        {
            var g = Golden("auctions")[index]!;
            var format = Data.Rules.Format((string)g["format"]!);
            var seed = (long)g["seed"]!;
            var retention = (bool)g["retention"]!;

            var retentions = new Dictionary<string, List<Player>>();
            if (retention)
            {
                foreach (var t in Data.Franchises) retentions[t.Id] = Ai.ChooseRetentions(t.Id, Data.Players, Data.Rules, format, AiModel.Classic);
            }
            var expectedRetentions = (JObject)g["retentions"]!;
            Assert.Equal(expectedRetentions.Count, retentions.Count);
            foreach (var p in expectedRetentions.Properties())
            {
                Assert.Equal(p.Value.Select(x => (string)x!).ToList(), retentions[p.Name].Select(x => x.Id).ToList());
            }

            var e = new AuctionEngine(Data.Rules, format, Data.Franchises, Data.Players, seed, retentions);
            Ai.RunAuction(e, Ai.CreateBidders(e, seed * 31, null, AiModel.Classic));

            var expectedResults = g["results"]!.Select(x => (string)x!).ToList();
            var actualResults = e.Results.Select(r =>
                $"{r.LotNumber}:{r.Player.Id}:{Lower(r.Round.ToString())}:{N(r.TeamId)}:{N(r.Price)}:{r.BidCount}:{B(r.ViaRtm)}").ToList();
            for (var k = 0; k < Math.Min(expectedResults.Count, actualResults.Count); k++)
            {
                Assert.True(expectedResults[k] == actualResults[k], $"{label}: lot {k} differs\n  dart: {expectedResults[k]}\n  c#:   {actualResults[k]}");
            }
            Assert.Equal(expectedResults.Count, actualResults.Count);

            foreach (var t in e.Teams.Values)
            {
                var gt = g["teams"]![t.Id]!;
                Assert.Equal((int)gt["purse"]!, t.Purse);
                Assert.Equal((int)gt["rtm"]!, t.RtmCards);
                Assert.Equal(gt["squad"]!.Select(x => (string)x!).ToList(),
                    t.Squad.Select(p => $"{p.Player.Id}:{p.Price}:{Lower(p.Kind.ToString())}").ToList());
            }

            var ratings = Ratings.Rate(e.Teams.Values, Data.Rules.MaxOverseasInXI, format.MinSquad - 11, Data.Rules.RatingFor(format));
            foreach (var r in ratings)
            {
                var exp = (JArray)g["ratings"]![r.TeamId]!;
                Assert.Equal(
                    exp.Take(7).Select(x => (int)x).Concat(new[] { 0 }).ToList(),
                    new[] { r.Batting, r.Bowling, r.Balance, r.Depth, r.Value, r.Overall, r.Rank, 0 }.ToList());
                Assert.Equal((string)exp[7]!, r.Grade);
                Assert.Equal(g["xi"]![r.TeamId]!.Select(x => (string)x!).ToList(), r.Lineup.XI.Select(p => p.Id).ToList());
            }

            var squads = new OrderedMap<string, List<Player>>();
            foreach (var t in e.Teams.Values) squads[t.Id] = t.Players;
            var season = Seasons.Simulate(squads, seed * 7919, Data.Rules.MaxOverseasInXI, SeasonModel.Classic);
            var gs = g["season"]!;
            var matches = season.AllMatches.Select(m =>
                $"{m.Number}:{Lower(m.Stage.ToString())}:{m.First.BattingTeamId}:{m.First.Runs}/{m.First.Wickets}/{m.First.LegalBalls}" +
                $":{m.Second.BattingTeamId}:{m.Second.Runs}/{m.Second.Wickets}/{m.Second.LegalBalls}" +
                $":{m.WinnerId}:{B(m.SuperOver)}:{m.PlayerOfMatch.Id}:{Lower(m.Pitch.ToString())}").ToList();
            Assert.Equal(gs["matches"]!.Select(x => (string)x!).ToList(), matches);
            Assert.Equal(gs["table"]!.Select(x => (string)x!).ToList(),
                season.Table.Select(r => $"{r.TeamId}:{r.Won}:{r.Points}:{DartMath.Round(r.NetRunRate * 1e6)}").ToList());
            Assert.Equal((string)gs["champion"]!, season.ChampionId);
            Assert.Equal((string)gs["runnerUp"]!, season.RunnerUpId);
            Assert.Equal((string)gs["topRuns"]!, $"{season.TopRunScorer.Player.Id}:{season.TopRunScorer.Runs}");
            Assert.Equal((string)gs["topWickets"]!, $"{season.TopWicketTaker.Player.Id}:{season.TopWicketTaker.Wickets}");
            Assert.Equal((string)gs["mvp"]!, $"{season.Mvp.Player.Id}:{DartMath.Round(season.Mvp.MvpPoints * 1000)}");
        }

        [Fact]
        public void Live_auction_with_scripted_humans_matches_dart()
        {
            foreach (var g in Golden("live"))
            {
                var seed = (long)g["seed"]!;
                var humans = new[] { "MBH", "CHC" };
                var e = new AuctionEngine(Data.Rules, Data.Rules.Format("standard"), Data.Franchises, Data.Players, seed);
                var log = new List<JObject>();
                var scale = (double?)g["scale"] ?? 1.0;
                var live = new LiveAuction(e, Ai.CreateBidders(e, seed * 13, humans, AiModel.Classic), humans, aiThinkScale: scale);
                live.OnEvent += ev => log.Add(RoomAction.ForEvent(ev).ToJson());
                live.Start();
                var script = new Rng(seed + 1000);
                var frames = 0;
                while (live.Phase != LivePhase.Finished && frames < 2000000)
                {
                    foreach (var h in humans)
                    {
                        var roll = script.NextDouble();
                        if (live.Phase == LivePhase.Bidding && roll < 0.03) live.HumanBid(h);
                        if (live.Phase == LivePhase.Bidding && roll > 0.995) live.HumanPass(h);
                        if (live.PendingRtm?.TeamId == h) live.HumanRtm(h, script.Chance(0.5));
                    }
                    live.HoldingFastForward = frames % 50 < 10;
                    live.Advance(100);
                    frames++;
                }

                var expected = (JArray)g["log"]!;
                for (var k = 0; k < Math.Min(expected.Count, log.Count); k++)
                {
                    Assert.True(JToken.DeepEquals(expected[k], log[k]), $"seed {seed}: action {k} differs\n  dart: {expected[k]}\n  c#:   {log[k]}");
                }
                Assert.Equal(expected.Count, log.Count);
                Assert.Equal((int)g["frames"]!, frames);
                foreach (var t in e.Teams.Values)
                {
                    Assert.Equal((int)g["teams"]![t.Id]!["purse"]!, t.Purse);
                }

                // And a phone replica replaying that log ends up identical.
                var setup = new RoomSetup { Seed = seed, SeasonSeed = 1, FormatId = "standard", DataVersion = Data.Version };
                var phone = new RemoteAuction(Data, _ => { });
                phone.Load(setup, Array.Empty<RoomAction>());
                foreach (var a in expected) phone.Apply(RoomAction.FromJson((JObject)a));
                Assert.Equal(LivePhase.Finished, phone.Phase);
                foreach (var t in e.Teams.Values) Assert.Equal(t.Purse, phone.Engine.Teams[t.Id].Purse);
            }
        }
    }
}
