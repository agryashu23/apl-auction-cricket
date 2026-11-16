using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;

namespace AuctionCore.Tests
{
    /// <summary>The user's batting choices: who opens, and who comes in after each wicket.</summary>
    public class PreMatchTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static QuickMatch Match() => new QuickMatch(QuickMatch.LastSeasonXI(Data, "BLR"), QuickMatch.LastSeasonXI(Data, "MBH"), 5, 42);

        [Fact]
        public void Chosen_openers_take_strike_and_the_rest_keep_their_order()
        {
            var m = Match();
            var before = m.Order.Select(b => b.Player.Id).ToList();
            var s = m.Order[6].Player;
            var n = m.Order[3].Player;
            m.SetOpeners(s, n);
            Assert.Equal(s.Id, m.OnStrike.Player.Id);
            Assert.Equal(n.Id, m.OffStrike.Player.Id);
            Assert.Equal(before.Where(id => id != s.Id && id != n.Id), m.Order.Skip(2).Select(b => b.Player.Id));
            Assert.Equal(2, m.Order.Count(b => b.Batted));
            Assert.Throws<ArgumentException>(() => m.SetOpeners(s, s));
        }

        [Fact]
        public void The_users_pick_comes_in_after_a_wicket()
        {
            var m = Match();
            Assert.Null(m.Incoming);
            // Swing at everything early until a wicket falls.
            var guard = 0;
            while (m.Wickets == 0 && !m.Finished && guard++ < 400) m.Play(new ShotInput(ShotType.Loft, Zone.MidWicket, 0.05));
            Assert.Equal(1, m.Wickets);
            var incoming = m.Incoming;
            Assert.NotNull(incoming);
            var pick = m.Waiting.Last().Player;
            Assert.True(m.SendIn(pick));
            Assert.Contains(pick.Id, new[] { m.OnStrike.Player.Id, m.OffStrike.Player.Id });
            Assert.False(incoming!.Batted);
            Assert.Contains(m.Waiting, b => b.Player.Id == incoming.Player.Id);
            Assert.Equal(m.Order.Count - 3, m.Waiting.Count);
            Assert.Equal(3, m.Order.Count(b => b.Batted));
            Assert.Equal(m.Order.Select(b => b.Player.Id).Distinct().Count(), m.Order.Count);
        }
    }
}
