using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;

namespace AuctionCore.Tests
{
    /// <summary>Focus batch 1: simulated overs keep the book straight (runs, wickets, balls, figures).</summary>
    public class SimTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static void Consistent(QuickMatch m)
        {
            Assert.Equal(m.Log.Sum(b => b.Runs), m.Runs);
            Assert.Equal(m.Log.Count(b => b.Wicket), m.Wickets);
            Assert.Equal(m.Log.Count(b => b.Legal), m.LegalBalls);
            Assert.Equal(m.LegalBalls, m.BowlingFigures.Sum(f => f.Balls));
            Assert.Equal(m.Runs - m.Log.Sum(b => b.Byes + b.LegByes), m.BowlingFigures.Sum(f => f.Runs));
            Assert.Equal(m.Log.Count(b => b.Wicket && b.HowOut != "run out"), m.BowlingFigures.Sum(f => f.Wickets));
            Assert.Equal(m.Order.Sum(b => b.Runs), m.Log.Where(b => !b.Wide && !b.DeadBall).Sum(b => b.BatRuns));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Simulating_overs_keeps_the_book_straight(bool userBatsFirst)
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            for (var g = 0; g < 20; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 5) % 10]), 20, 15000 + g, null, userBatsFirst);
                // A few balls played, then the rest of the over at once: it ends on an over boundary.
                for (var i = 0; i < 3 && !m.Finished; i++) m.PlayAi();
                m.SimulateOver();
                Assert.True(m.Finished || m.BallInOver == 0);
                Consistent(m);
                // Up to the last two overs, then the rest of the innings.
                m.SimulateTo(m.Overs - 2);
                Assert.True(m.Finished || m.Over == m.Overs - 2);
                Consistent(m);
                m.SimulateInnings();
                Assert.True(m.Finished);
                Consistent(m);
                // The second innings, all of it.
                if (!m.MatchOver)
                {
                    m.StartSecondInnings();
                    m.SimulateInnings();
                    Assert.True(m.MatchOver);
                    Consistent(m);
                }
                // Each bowler within his quota of overs.
                Assert.All(m.BowlingFigures, f => Assert.True(f.Balls <= m.MaxOversPerBowler * 6, $"{f.Player.Name} {f.Balls} balls"));
            }
        }
    }
}
