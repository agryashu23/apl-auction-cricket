using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;

namespace AuctionCore.Tests
{
    /// <summary>Owner v26 #7 (the playing XI and its swaps) and #9 (the tournament table's Q).</summary>
    public class PlayingXiTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static int Cap => Data.Rules.MaxOverseasInXI;

        private static System.Collections.Generic.List<Player> Squad(string team) => Data.Players.Where(p => p.PreviousTeam == team).ToList();

        [Theory]
        [InlineData("BLR")]
        [InlineData("MBH")]
        [InlineData("CHC")]
        public void The_auto_xi_is_valid_and_matches_last_seasons_xi(string team)
        {
            var squad = Squad(team);
            var auto = PlayingXi.Auto(squad, Cap);
            Assert.Null(PlayingXi.Problem(auto.XI, squad, Cap));
            Assert.Equal(QuickMatch.LastSeasonXI(Data, team).XI.Select(p => p.Id), auto.XI.Select(p => p.Id));
            Assert.Equal(squad.Count - 11, auto.Bench.Count);
        }

        [Fact]
        public void A_like_for_like_swap_keeps_the_place_and_resolves()
        {
            var squad = Squad("BLR");
            var auto = PlayingXi.Auto(squad, Cap);
            // A local batter off for a local batter from the bench: always fine.
            var outgoing = auto.XI.First(p => !p.Overseas && p.Role == Role.Batter && !p.IsKeeper && !p.IsBowlingOption);
            var incoming = auto.Bench.First(p => !p.Overseas && !p.IsKeeper);
            var xi = PlayingXi.Swap(auto.XI, outgoing, incoming, squad, Cap, out var problem);
            Assert.Null(problem);
            Assert.NotNull(xi);
            Assert.Equal(auto.XI.IndexOf(outgoing), xi!.IndexOf(incoming));
            var sheet = PlayingXi.Resolve("BLR", squad, xi.Select(p => p.Id).ToList(), Cap);
            Assert.NotNull(sheet);
            Assert.Equal(xi.Select(p => p.Id), sheet!.XI.Select(p => p.Id));
        }

        [Fact]
        public void One_overseas_too_many_is_refused()
        {
            foreach (var f in Data.Franchises)
            {
                var squad = Squad(f.Id);
                var auto = PlayingXi.Auto(squad, Cap);
                var cap = PlayingXi.OverseasCap(squad, Cap);
                if (auto.XI.Count(p => p.Overseas) < cap) continue;
                var incoming = auto.Bench.FirstOrDefault(p => p.Overseas);
                var outgoing = auto.XI.FirstOrDefault(p => !p.Overseas && !p.IsKeeper);
                if (incoming == null || outgoing == null) continue;
                Assert.Null(PlayingXi.Swap(auto.XI, outgoing, incoming, squad, Cap, out var problem));
                Assert.Contains("OVERSEAS", problem);
                return;
            }
            throw new Exception("no squad with a full overseas quota and an overseas bench player to test with");
        }

        [Fact]
        public void The_only_keeper_cannot_be_dropped_for_a_non_keeper()
        {
            var squad = Squad("MBH");
            var auto = PlayingXi.Auto(squad, Cap);
            var keepers = auto.XI.Where(p => p.IsKeeper).ToList();
            if (keepers.Count != 1) return; // two keepers in the XI: nothing to prove here
            var incoming = auto.Bench.FirstOrDefault(p => !p.IsKeeper && !p.Overseas);
            Assert.NotNull(incoming);
            Assert.Null(PlayingXi.Swap(auto.XI, keepers[0], incoming!, squad, Cap, out var problem));
            Assert.Contains("WICKETKEEPER", problem);
        }

        [Fact]
        public void A_stale_or_broken_choice_does_not_resolve()
        {
            var squad = Squad("BLR");
            var ids = PlayingXi.Auto(squad, Cap).XI.Select(p => p.Id).ToList();
            Assert.Null(PlayingXi.Resolve("BLR", squad, ids.Take(10).ToList(), Cap));
            Assert.Null(PlayingXi.Resolve("BLR", squad, ids.Take(10).Append("nobody").ToList(), Cap));
            Assert.Null(PlayingXi.Resolve("BLR", squad, null, Cap));
            Assert.NotNull(PlayingXi.Resolve("BLR", squad, ids, Cap));
        }

        [Theory]
        // Top four with more than half won: Q.
        [InlineData(1, 14, 8, true)]
        [InlineData(4, 14, 8, true)]
        [InlineData(4, 9, 5, true)]
        [InlineData(1, 1, 1, true)]
        // Top four with exactly half or fewer: no Q.
        [InlineData(1, 14, 7, false)]
        [InlineData(2, 9, 4, false)]
        [InlineData(1, 9, 1, false)]
        [InlineData(3, 0, 0, false)]
        // Fifth or lower, however many won: no Q.
        [InlineData(5, 14, 9, false)]
        [InlineData(5, 9, 5, false)]
        [InlineData(10, 14, 14, false)]
        [InlineData(0, 14, 9, false)]
        public void Q_only_in_the_top_four_after_winning_more_than_half_the_league(int rank, int matches, int won, bool q)
        {
            Assert.Equal(q, LeagueTable.Qualified(rank, won, matches));
        }

        [Fact]
        public void Wins_needed_is_more_than_half()
        {
            Assert.Equal(8, LeagueTable.WinsToQualify(14));
            Assert.Equal(5, LeagueTable.WinsToQualify(9));
            Assert.Equal(6, LeagueTable.WinsToQualify(10));
        }
    }
}
