using System;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Xunit;
using Xunit.Abstractions;

namespace AuctionCore.Tests
{
    /// <summary>
    /// The fielding in whole AI matches: how often the ball reaches the rope, the
    /// dots, and the stops and dives — a ball within a fielder's reach is stopped
    /// (or he dives at it), so boundaries come from the gaps and over the top.
    /// </summary>
    public class FieldStatsTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;

        public FieldStatsTests(ITestOutputHelper output) { _out = output; }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        [Fact]
        public void Fielders_stop_what_they_can_reach()
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            int chances = 0, drops = 0, balls = 0, fours = 0, groundFours = 0, sixes = 0, dots = 0, stops = 0, dives = 0, palmed = 0, ones = 0, twos = 0;
            for (var g = 0; g < 60; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), 20, 900 + g, null, userBatsFirst: false);
                while (!m.Finished)
                {
                    var r = m.PlayAi();
                    if (!r.Legal) continue;
                    balls++;
                    if (r.Four) fours++;
                    if (r.Four && !r.Path.InAir) groundFours++;
                    if (r.Six) sixes++;
                    if (r.Runs == 0 && !r.Wicket) dots++;
                    if (r.BatRuns == 1) ones++;
                    if (r.BatRuns == 2) twos++;
                    if (r.Caught && r.HowOut == "caught") chances++;
                    if (r.Dropped.Length > 0)
                    {
                        chances++;
                        drops++;
                    }
                    if (r.FieldPlay is Field.Play p)
                    {
                        if (!p.Boundary && !p.Caught && p.Fielder.Length > 0) stops++;
                        if (p.Dived) dives++;
                        if (p.Beaten.Length > 0) palmed++;
                    }
                }
            }
            double Per100(int n) => 100.0 * n / balls;
            _out.WriteLine($"{balls} balls: fours {Per100(fours):0.0}% ({Per100(groundFours):0.0}% along the ground), sixes {Per100(sixes):0.0}%, dots {Per100(dots):0.0}%, singles {Per100(ones):0.0}%, twos {Per100(twos):0.0}%");
            _out.WriteLine($"per 100 balls: {Per100(stops):0.0} stopped in the field, {Per100(dives):0.0} diving stops, {Per100(palmed):0.0} dives beaten (palmed on)");
            _out.WriteLine($"catches in the field: {chances} chances, {drops} dropped ({100.0 * drops / Math.Max(1, chances):0.0}%)");
            Assert.InRange(Per100(fours), 5, 20);
            Assert.InRange(100.0 * drops / Math.Max(1, chances), 5, 15);
            Assert.True(dives > 0 && palmed > 0);
            Assert.True(stops > fours, "more stopped in the field than reach the rope");
        }
    }
}
