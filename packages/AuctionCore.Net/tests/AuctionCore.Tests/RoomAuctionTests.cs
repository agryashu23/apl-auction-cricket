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
    /// The v2 friend-room rules (docs/ROOMS_PROTOCOL.md): pass consensus, SIMULATE / take back,
    /// the all-simulated instant finish, host force close / undo / pause / timer, the new-rules
    /// setup, phone replay of everything, and the deterministic rewards.
    /// </summary>
    public class RoomAuctionTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static readonly RoomAuctionOptions Quick = new RoomAuctionOptions
        {
            RevealMs = 500,
            ResultMs = 300,
            AiRtmThinkMs = 300,
            HumanRtmTimeoutMs = 1000,
            AiThinkScale = 1.0,
        };

        private static RoomSetup Setup(long seed, string format = "mega") => new RoomSetup
        {
            Seed = seed,
            SeasonSeed = seed * 3,
            FormatId = format,
            Retention = false,
            DataVersion = Data.Version,
            Rules = RoomSetup.NewRules,
        };

        private static RoomAuction Room(long seed, IEnumerable<string> humans, int? timer = null)
        {
            var o = new RoomAuctionOptions
            {
                RevealMs = Quick.RevealMs,
                ResultMs = Quick.ResultMs,
                AiRtmThinkMs = Quick.AiRtmThinkMs,
                HumanRtmTimeoutMs = Quick.HumanRtmTimeoutMs,
                AiThinkScale = Quick.AiThinkScale,
                BidTimerSec = timer,
            };
            return new RoomAuction(Data, Setup(seed), humans, o);
        }

        private static void AdvanceTo(RoomAuction room, LivePhase phase, int limitMs = 120000)
        {
            for (var t = 0; t < limitMs && room.Live.Phase != phase; t += 50) room.Advance(50);
            Assert.Equal(phase, room.Live.Phase);
        }

        private static string[] AllTeams => Data.Franchises.Select(f => f.Id).ToArray();

        /// <summary>A phone replaying the room's log ends up with the same squads and purses.</summary>
        private static void AssertReplicaMatches(RoomAuction room)
        {
            var phone = new RemoteAuction(Data, _ => { });
            phone.Load(RoomSetup.FromJson(room.Setup.ToJson()), room.LogJson().OfType<JObject>().Select(RoomAction.FromJson));
            foreach (var t in room.Engine.Teams.Values)
            {
                var p = phone.Engine.Teams[t.Id];
                Assert.Equal(t.Purse, p.Purse);
                Assert.Equal(t.Squad.Select(s => s.Player.Id), p.Squad.Select(s => s.Player.Id));
                Assert.Equal(t.RtmCards, p.RtmCards);
            }
            Assert.Equal(room.Engine.IsFinished, phone.Engine.IsFinished);
        }

        [Fact]
        public void Setup_with_new_rules_round_trips_and_builds_the_solo_engine()
        {
            var setup = Setup(42);
            var back = RoomSetup.FromJson(setup.ToJson());
            Assert.Equal(RoomSetup.NewRules, back.Rules);
            Assert.True(back.PlaysNewRules);
            var engine = back.BuildEngine(Data);
            Assert.Equal(LotOrder.IplSets, engine.LotOrder);
            Assert.Equal(Data.Rules.MaxPrice, engine.MaxPrice);
            // Old JSON (no "rules") stays the classic room engine.
            var old = (JObject)setup.ToJson().DeepClone();
            old.Remove("rules");
            Assert.Equal(LotOrder.Classic, RoomSetup.FromJson(old).BuildEngine(Data).LotOrder);
            Assert.Equal(2, RoomProtocol.Version);
        }

        [Fact]
        public void One_pass_never_ends_the_lot_for_the_others()
        {
            // Every franchise human: no AI, so only the humans and the timer decide.
            var room = Room(7, AllTeams);
            room.Start();
            AdvanceTo(room, LivePhase.Bidding);
            var lot = room.Engine.CurrentLot!;
            foreach (var t in AllTeams.Skip(1)) Assert.Null(room.Pass(t));
            Assert.Equal(LivePhase.Bidding, room.Live.Phase);
            Assert.Same(lot, room.Engine.CurrentLot);
            Assert.Equal("alreadyPassed", room.Pass(AllTeams[1]));
            // The last one still has the full timer.
            room.Advance(9000);
            Assert.Equal(LivePhase.Bidding, room.Live.Phase);
            room.Advance(1100);
            Assert.Equal(LivePhase.Result, room.Live.Phase);
            Assert.False(room.Engine.Results.Last().Sold);
        }

        [Fact]
        public void The_lot_closes_early_when_every_human_has_passed_or_leads()
        {
            var room = Room(8, AllTeams);
            room.Start();
            AdvanceTo(room, LivePhase.Bidding);
            var amount = room.Engine.NextBidAmount;
            Assert.Equal("staleAmount", room.Bid(AllTeams[0], amount + 1));
            Assert.Null(room.Bid(AllTeams[0], amount));
            Assert.Equal("leading", room.Pass(AllTeams[0]));
            foreach (var t in AllTeams.Skip(1).Take(8)) room.Pass(t);
            Assert.Equal(LivePhase.Bidding, room.Live.Phase);
            room.Pass(AllTeams[9]);
            // Closed at once; the player's old team may still hold an RTM card (all human here: decline it).
            if (room.Live.Phase == LivePhase.RtmDecision) Assert.Null(room.Rtm(room.Live.PendingRtm!.TeamId, false));
            Assert.Equal(LivePhase.Result, room.Live.Phase);
            var r = room.Engine.Results.Last();
            Assert.Equal(AllTeams[0], r.TeamId);
            Assert.Equal(amount, r.Price);
        }

        [Fact]
        public void Bots_keep_bidding_after_humans_pass_then_the_lot_closes_at_once()
        {
            for (var seed = 1; seed <= 6; seed++)
            {
                var room = Room(seed, new[] { "MBH", "CHC" });
                room.Start();
                AdvanceTo(room, LivePhase.Bidding);
                room.Pass("MBH");
                Assert.Equal(LivePhase.Bidding, room.Live.Phase); // CHC is still in
                room.Pass("CHC");
                var bidAt = room.Live.NowMs;
                var bids = room.Engine.CurrentLot?.Bids.Count ?? 0;
                for (var t = 0; t < 60000 && room.Live.Phase == LivePhase.Bidding; t += 10)
                {
                    room.Advance(10);
                    var now = room.Engine.CurrentLot?.Bids.Count ?? 0;
                    if (now != bids)
                    {
                        bids = now;
                        bidAt = room.Live.NowMs;
                    }
                }
                Assert.NotEqual(LivePhase.Bidding, room.Live.Phase);
                // Closed right after the last bot bid, not a full timer later.
                Assert.True(room.Live.NowMs - bidAt < 2000, $"seed {seed}: closed {room.Live.NowMs - bidAt} ms after the last bid");
            }
        }

        /// <summary>
        /// With the server's real pacing (hall timings, AI think x3.6), a lot where one human passed
        /// and the other two sit still always closes: either a bot bid lands or the bid timer runs
        /// out, every time. Records how long the longest first lot (a marquee bot war) runs.
        /// </summary>
        [Fact]
        public void A_lot_with_one_pass_always_closes_with_real_pacing()
        {
            var longest = 0;
            for (var seed = 1; seed <= 40; seed++)
            {
                var room = new RoomAuction(Data, Setup(seed), new[] { "MBH", "CHC", "BLR" }, new RoomAuctionOptions());
                room.Start();
                AdvanceTo(room, LivePhase.Bidding);
                room.Pass("MBH");
                var start = room.Live.NowMs;
                var lastBid = start;
                var bids = 0;
                while (room.Live.Phase == LivePhase.Bidding && room.Live.NowMs - start < 3600000)
                {
                    room.Advance(50);
                    var n = room.Engine.CurrentLot?.Bids.Count ?? bids;
                    if (n != bids)
                    {
                        bids = n;
                        lastBid = room.Live.NowMs;
                    }
                    // Never stuck: the timer is always running out from the last bid.
                    Assert.True(room.Live.Phase != LivePhase.Bidding || room.Live.NowMs - lastBid <= 10000 + 100, $"seed {seed}: no close {room.Live.NowMs - lastBid} ms after the last bid");
                }
                Assert.NotEqual(LivePhase.Bidding, room.Live.Phase);
                longest = Math.Max(longest, room.Live.NowMs - start);
            }
            // A long marquee war is normal; it is what the harness must wait out (game ms; x10 in the harness).
            Assert.InRange(longest, 10000, 900000);
            Console.WriteLine($"longest first lot after one pass: {longest} game ms");
        }

        [Fact]
        public void Simulate_hands_the_team_to_the_autopilot_and_take_back_returns_it()
        {
            var room = Room(11, new[] { "MBH", "CHC" });
            room.Start();
            Assert.Null(room.Simulate("MBH"));
            Assert.Equal(SeatControl.AutoPilot, room.ControlOf("MBH"));
            Assert.True(room.Live.IsAi("MBH"));
            Assert.DoesNotContain("MBH", room.HumanDriven());
            Assert.False(room.IsFinished); // CHC still plays
            AdvanceTo(room, LivePhase.Bidding);
            Assert.Equal("notInControl", room.Bid("MBH", room.Engine.NextBidAmount));
            Assert.Null(room.TakeBack("MBH"));
            Assert.Equal(SeatControl.Human, room.ControlOf("MBH"));
            Assert.Contains("MBH", room.HumanDriven());
            Assert.Equal("notOnAutopilot", room.TakeBack("MBH"));
        }

        [Fact]
        public void Away_then_back_and_kicked_becomes_ai()
        {
            var room = Room(12, new[] { "MBH", "CHC", "BLR" });
            room.Start();
            Assert.Null(room.GoAway("CHC"));
            Assert.Equal(SeatControl.Away, room.ControlOf("CHC"));
            Assert.Null(room.TakeBack("CHC"));
            room.Release("BLR");
            Assert.Equal(SeatControl.Ai, room.ControlOf("BLR"));
            Assert.Equal("notOnAutopilot", room.TakeBack("BLR"));
            Assert.False(room.IsFinished);
        }

        [Fact]
        public void All_humans_simulating_finishes_at_once_and_phones_replay_it()
        {
            var room = Room(13, new[] { "MBH", "CHC", "BLR" });
            string? reload = null;
            var finished = 0;
            room.Reloaded += r => reload = r;
            room.Finished += () => finished++;
            room.Start();
            AdvanceTo(room, LivePhase.Bidding);
            Assert.Null(room.Bid("MBH", room.Engine.NextBidAmount));
            room.Simulate("MBH");
            room.Simulate("CHC");
            Assert.False(room.IsFinished);
            room.Simulate("BLR");
            Assert.True(room.IsFinished);
            Assert.Equal("instant", reload);
            Assert.Equal(1, finished);
            Assert.NotNull(room.Awards);
            foreach (var t in room.Engine.Teams.Values) Assert.True(t.Size >= room.Engine.Format.MinSquad, $"{t.Id} has {t.Size}");
            AssertReplicaMatches(room);
        }

        [Fact]
        public void Host_end_finishes_with_every_human_on_autopilot()
        {
            var room = Room(14, new[] { "MBH", "CHC" });
            room.Start();
            AdvanceTo(room, LivePhase.Result);
            Assert.Null(room.End());
            Assert.True(room.IsFinished);
            Assert.Equal(SeatControl.AutoPilot, room.ControlOf("MBH"));
            Assert.Equal("notAuction", room.End());
            AssertReplicaMatches(room);
        }

        [Fact]
        public void Force_close_sold_and_unsold_replay_on_phones()
        {
            var room = Room(15, AllTeams);
            room.Start();
            AdvanceTo(room, LivePhase.Bidding);
            room.Bid("MBH", room.Engine.NextBidAmount);
            room.Bid("CHC", room.Engine.NextBidAmount);
            Assert.Null(room.ForceClose(sold: false));
            Assert.False(room.Engine.Results.Last().Sold);
            Assert.Equal("noOpenLot", room.ForceClose(sold: true));
            Assert.True(((JObject)room.LogJson().Last()).Value<bool>("unsold"));
            AssertReplicaMatches(room);

            AdvanceTo(room, LivePhase.Bidding);
            var price = room.Engine.NextBidAmount;
            room.Bid("KOC", price);
            // Sold: bidding ends now at the high bid (an RTM offer, if any, goes to its holder).
            Assert.Null(room.ForceClose(sold: true));
            if (room.Live.Phase == LivePhase.RtmDecision) room.ForceClose(sold: true);
            var r = room.Engine.Results.Last();
            Assert.Equal("KOC", r.TeamId);
            Assert.Equal(price, r.Price);
            AssertReplicaMatches(room);
        }

        [Fact]
        public void Undo_puts_the_last_lot_back_exactly()
        {
            var room = Room(16, new[] { "MBH", "CHC" });
            room.Start();
            AdvanceTo(room, LivePhase.Bidding);
            var player = room.Engine.CurrentLot!.Player.Id;
            var purseBefore = room.Engine.Teams["MBH"].Purse;
            room.Bid("MBH", room.Engine.NextBidAmount);
            room.ForceClose(sold: true);
            if (room.Live.Phase == LivePhase.RtmDecision) room.ForceClose(sold: true);
            Assert.Equal("MBH", room.Engine.Results.Last().TeamId);
            Assert.True(room.Engine.Teams["MBH"].Purse < purseBefore);
            AdvanceTo(room, LivePhase.Bidding); // the next lot is open

            string? reload = null;
            var logAtReload = -1;
            var acted = new List<RoomAction>();
            room.Reloaded += r =>
            {
                reload = r;
                logAtReload = room.Log.Count;
            };
            room.Acted += acted.Add;
            Assert.Null(room.Undo());
            Assert.Equal("undo", reload);
            // The reloaded log stops before the lot; it reopens as a normal act.
            Assert.Equal(0, logAtReload);
            Assert.IsType<OpenLotAction>(Assert.Single(acted));
            Assert.Equal(player, ((OpenLotAction)acted[0]).PlayerId);
            Assert.Equal(1, room.UndoneLot);
            Assert.Equal(player, room.Engine.CurrentLot!.Player.Id);
            Assert.Equal(purseBefore, room.Engine.Teams["MBH"].Purse);
            Assert.Empty(room.Engine.Results);
            Assert.Equal("nothingToUndo", room.Undo());
            AssertReplicaMatches(room);
        }

        [Fact]
        public void Pause_freezes_time_and_bids_and_the_host_timer_applies()
        {
            var room = Room(17, new[] { "MBH", "CHC" }, timer: 15);
            room.Start();
            AdvanceTo(room, LivePhase.Bidding);
            Assert.Equal(15000, room.Live.BidTimerTotalMs);
            Assert.Null(room.Pause());
            var left = room.Live.BidTimeLeftMs;
            room.Advance(20000);
            Assert.Equal(left, room.Live.BidTimeLeftMs);
            Assert.Equal("paused", room.Bid("MBH", room.Engine.NextBidAmount));
            Assert.True((bool)room.ClockJson()["paused"]!);
            room.Resume();
            room.BidTimerSec = 20;
            Assert.Equal(20000, room.Live.BidTimerTotalMs);
            Assert.Equal(20, (int)room.ClockJson()["timerSec"]!);
        }

        [Fact]
        public void Solo_keeps_its_pass_rule()
        {
            var e = Setup(18).BuildEngine(Data);
            var live = new LiveAuction(e, Ai.CreateBidders(e, 5, new[] { "MBH" }), new[] { "MBH" }, revealMs: 100);
            Assert.False(live.PassConsensus);
            Assert.Null(live.BidTimerSec);
            Assert.Equal(Data.Rules.BidTimerSec * 1000, live.BidTimerTotalMs);
            live.Start();
            live.Advance(200);
            live.HumanPass("MBH");
            // Solo: the only human passing settles the lot at once (SettleAi closeNow: the next tick).
            live.Advance(1);
            Assert.NotEqual(LivePhase.Bidding, live.Phase);
        }

        [Fact]
        public void Rewards_are_deterministic_and_consistent()
        {
            JObject Run()
            {
                var room = Room(19, new[] { "MBH", "CHC" });
                room.Start();
                room.End();
                return room.Awards!.ToJson(t => t == "MBH" ? ("p1", "Asha", "autopilot") : ((string, string, string)?)null);
            }

            var a = Run();
            var b = Run();
            Assert.True(JToken.DeepEquals(a, b));
            var standings = (JArray)a["standings"]!;
            Assert.Equal(Data.Franchises.Count, standings.Count);
            var ranks = standings.Select(s => (int)s["rank"]!).ToList();
            Assert.Equal(Enumerable.Range(1, standings.Count), ranks);
            Assert.Equal((string)standings[0]["team"]!, (string)a["winner"]!);
            Assert.Equal("MBH", (string)a["topHuman"]!);
            var totals = standings.Select(s => (int)s["total"]!).ToList();
            Assert.Equal(totals.OrderByDescending(x => x), totals);
            foreach (JObject s in standings)
            {
                var sum = ((JObject)s["points"]!).Properties().Sum(p => (int)p.Value);
                Assert.Equal((int)s["total"]!, sum);
                Assert.InRange((int)s["points"]!["value"]!, 0, AuctionAwards.ValueMax);
                Assert.InRange((int)s["points"]!["steals"]!, 0, AuctionAwards.StealCap);
            }
            var ids = ((JArray)a["awards"]!).Select(x => (string)x["id"]!).ToList();
            foreach (var id in new[] { "bestSquad", "bigSpender", "mostBalanced", "valueKing" }) Assert.Contains(id, ids);
            Assert.NotEmpty((JArray)a["scoring"]!);
        }

        [Fact]
        public void Rewards_work_for_a_solo_engine_too()
        {
            var e = new RoomSetup { Seed = 20, SeasonSeed = 1, FormatId = "mega", DataVersion = Data.Version, Solo = true }.BuildEngine(Data);
            Ai.FinishAuction(e, Ai.CreateBidders(e, 9));
            var report = AuctionAwards.Compute(e, Data.Rules);
            Assert.Equal(e.Teams.Count, report.Standings.Count);
            var best = report.Awards.First(x => x.Id == "bestSquad");
            Assert.Equal(report.Standings.Max(s => s.Overall), report.For(best.TeamId).Overall);
        }
    }
}
