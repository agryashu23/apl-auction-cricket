using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Newtonsoft.Json;
using Xunit;

namespace AuctionCore.Tests
{
    /// <summary>Resume a match: the book saved between balls comes back the same, and the match plays on.</summary>
    public class MatchSaveTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static QuickMatch RoundTrip(QuickMatch m)
        {
            var json = JsonConvert.SerializeObject(m.Save());
            return QuickMatch.Restore(JsonConvert.DeserializeObject<MatchSave>(json)!, Data, 99);
        }

        private static void Same(QuickMatch a, QuickMatch b)
        {
            Assert.Equal(a.Batting.TeamId, b.Batting.TeamId);
            Assert.Equal(a.Runs, b.Runs);
            Assert.Equal(a.Wickets, b.Wickets);
            Assert.Equal(a.Extras, b.Extras);
            Assert.Equal(a.LegalBalls, b.LegalBalls);
            Assert.Equal(a.Target, b.Target);
            Assert.Equal(a.Chasing, b.Chasing);
            Assert.Equal(a.InningsNumber, b.InningsNumber);
            Assert.Equal(a.Pitch, b.Pitch);
            Assert.Equal(a.OnStrike.Player.Id, b.OnStrike.Player.Id);
            Assert.Equal(a.OffStrike.Player.Id, b.OffStrike.Player.Id);
            Assert.Equal(a.CurrentBowler.Id, b.CurrentBowler.Id);
            Assert.Equal(a.FreeHit, b.FreeHit);
            Assert.Equal(a.Log.Count, b.Log.Count);
            Assert.Equal(a.Log.Select(r => (r.Runs, r.Wicket, r.Legal)), b.Log.Select(r => (r.Runs, r.Wicket, r.Legal)));
            Assert.Equal(a.Order.Select(x => (x.Player.Id, x.Runs, x.Balls, x.HowOut)), b.Order.Select(x => (x.Player.Id, x.Runs, x.Balls, x.HowOut)));
            Assert.Equal(a.BowlingFigures.Select(f => (f.Player.Id, f.Balls, f.Runs, f.Wickets)).OrderBy(x => x.Item1),
                b.BowlingFigures.Select(f => (f.Player.Id, f.Balls, f.Runs, f.Wickets)).OrderBy(x => x.Item1));
            Assert.Equal(a.FirstInnings.Runs, b.FirstInnings.Runs);
            Assert.Equal(a.FirstInnings.Batting.Select(x => (x.Player.Id, x.Runs)), b.FirstInnings.Batting.Select(x => (x.Player.Id, x.Runs)));
        }

        private static void PlayOn(QuickMatch m, int balls)
        {
            for (var i = 0; i < balls && !m.Finished; i++) m.PlayAi();
        }

        [Fact]
        public void A_quick_chase_comes_back_mid_innings_and_plays_to_the_end()
        {
            var m = new QuickMatch(QuickMatch.LastSeasonXI(Data, "BLR"), QuickMatch.LastSeasonXI(Data, "MBH"), 5, 7);
            var squad = Data.Players.Where(p => p.PreviousTeam == "BLR").ToList();
            m.NameSubstitutes("BLR", QuickMatch.DefaultSubstitutes(squad, m.Batting));
            PlayOn(m, 14);
            var back = RoundTrip(m);
            Same(m, back);
            Assert.Equal(m.Substitutes("BLR").Select(p => p.Id), back.Substitutes("BLR").Select(p => p.Id));
            PlayOn(back, 200);
            Assert.True(back.Finished);
        }

        [Fact]
        public void A_full_match_comes_back_in_either_innings()
        {
            var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, "CHC"), QuickMatch.LastSeasonXI(Data, "DLD"), 3, 11, Venue.All[0], userBatsFirst: true);
            PlayOn(m, 9);
            Same(m, RoundTrip(m));
            PlayOn(m, 200);
            m.StartSecondInnings();
            PlayOn(m, 5);
            var back = RoundTrip(m);
            Same(m, back);
            Assert.Equal(m.FirstOrder!.Count, back.FirstOrder!.Count);
            PlayOn(back, 200);
            Assert.True(back.MatchOver);
        }
    }
}
