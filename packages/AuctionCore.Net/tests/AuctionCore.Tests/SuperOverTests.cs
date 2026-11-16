using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;
using Xunit.Abstractions;

namespace AuctionCore.Tests
{
    /// <summary>The Super Over: the picked three and bowler, one over a side, and the boundary count if it's tied again.</summary>
    public class SuperOverTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(Round4Tests.Root(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public SuperOverTests(ITestOutputHelper output) { _out = output; }

        [Fact]
        public void One_over_each_with_the_picked_batters_and_bowlers_and_a_winner()
        {
            var decided = 0;
            for (var g = 0; g < 30; g++)
            {
                var main = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, "BLR"), QuickMatch.LastSeasonXI(Data, "CHC"), 2, 100 + g, null, userBatsFirst: g % 2 == 0);
                while (!main.Finished) main.PlayAi();
                main.StartSecondInnings();
                while (!main.Finished) main.PlayAi();
                var userXI = main.UserTeam.XI;
                var batters = userXI.OrderByDescending(p => p.Batting).Take(3).ToList();
                var bowler = userXI.OrderByDescending(p => p.Bowling).First();
                var so = new SuperOver(main, 7 + g, batters, bowler);
                var m = so.Match;
                Assert.True(m.IsSuperOver);
                // The side that batted second goes first.
                Assert.Equal(main.Batting.TeamId, m.Batting.TeamId);
                for (var inn = 1; inn <= 2; inn++)
                {
                    var picked = m.Batting == main.UserTeam ? batters : SuperOver.AiBatters(m.Batting);
                    Assert.Equal(picked, m.Order.Select(b => b.Player).ToList());
                    var expectBowler = m.Bowling == main.UserTeam ? bowler : SuperOver.AiBowler(m.Bowling);
                    Assert.Same(expectBowler, m.CurrentBowler);
                    while (!m.Finished) m.PlayAi();
                    Assert.True(m.LegalBalls <= 6 && m.Wickets <= 2);
                    if (inn == 1) m.StartSecondInnings();
                }
                Assert.True(so.Over);
                if (so.Winner != null) decided++;
                Assert.NotEqual("", so.ResultText());
                if (so.OnBoundaries) _out.WriteLine($"tied again: {so.ResultText()}");
            }
            _out.WriteLine($"{decided}/30 decided");
            Assert.True(decided >= 28);
        }

        [Fact]
        public void A_tied_full_match_needs_one()
        {
            var main = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, "BLR"), QuickMatch.LastSeasonXI(Data, "CHC"), 1, 3, null, true);
            Assert.False(SuperOver.Needed(main));
        }
    }
}
