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
    /// Focus batch 1 (owner: "running is very important"; audit BAT-1: 0 twos in a match): the shape of
    /// scoring per ball over 20-over innings — dots, 1s, 2s, 3s, 4s, 6s — against real T20 rates
    /// (about 35–40% dots, 35% ones, 7–10% twos, ~1% threes, 10–12% fours, 4–6% sixes).
    /// </summary>
    public class RunsShapeTests
    {
        private static readonly Dataset Data = Dataset.FromJson(File.ReadAllText(Path.Combine(FindRoot(), "data", "apl_dataset.json")));
        private readonly ITestOutputHelper _out;
        public RunsShapeTests(ITestOutputHelper output) { _out = output; }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "data", "apl_dataset.json"))) dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        internal static (double dot, double one, double two, double three, double four, double six, double rpo, double perWicket) Shape(bool userBats, int games)
        {
            var teams = Data.Franchises.Select(f => f.Id).ToList();
            int balls = 0, dot = 0, one = 0, two = 0, three = 0, four = 0, six = 0, runs = 0, wkts = 0;
            for (var g = 0; g < games; g++)
            {
                var m = QuickMatch.Full(QuickMatch.LastSeasonXI(Data, teams[g % 10]), QuickMatch.LastSeasonXI(Data, teams[(g + 3) % 10]), 20, 12000 + g, null, userBatsFirst: userBats);
                var rng = new Rng(13000 + g);
                while (!m.Finished)
                {
                    BallResult r;
                    if (userBats)
                    {
                        // The user's side as a decent player bats: tap error ~0.7 windows, the AI's shot choice.
                        var t = m.NextDelivery();
                        var ai = m.AiShot();
                        var shot = ai.IsLeave ? ai : new ShotInput(ai.Type, ai.Direction, Batting.IdealTap(t) + Bowling.Normal(rng) * 0.7 * Batting.Window(m.OnStrike.Player, t.Delivery));
                        r = m.Play(shot);
                    }
                    else
                    {
                        var vars = Bowling.Variations(m.CurrentBowler);
                        var spin = m.CurrentBowler.BowlType == BowlType.Spin;
                        m.Bowl(vars[rng.NextInt(vars.Count)], rng.NextDouble() * 0.5 - 0.05, spin ? 2.8 + rng.NextDouble() * 3 : 2 + rng.NextDouble() * 6, Bowling.Normal(rng) * 0.6);
                        r = m.PlayAi();
                    }
                    runs += r.Runs;
                    if (r.Wicket) wkts++;
                    if (!r.Legal) continue;
                    balls++;
                    if (r.Six) six++;
                    else if (r.Four) four++;
                    else if (r.Runs == 0) dot++;
                    else if (r.Runs == 1) one++;
                    else if (r.Runs == 2) two++;
                    else three++;
                }
            }
            double P(int n) => n * 100.0 / balls;
            return (P(dot), P(one), P(two), P(three), P(four), P(six), runs * 6.0 / balls, balls / (double)Math.Max(1, wkts));
        }

        [Fact]
        public void Twenty_over_scoring_shape()
        {
            foreach (var user in new[] { false, true })
            {
                var s = Shape(user, 60);
                _out.WriteLine($"{(user ? "user side bats" : "AI bats")}: dots {s.dot:0.0}%, 1s {s.one:0.0}%, 2s {s.two:0.0}%, 3s {s.three:0.0}%, 4s {s.four:0.0}%, 6s {s.six:0.0}% | {s.rpo:0.00} rpo, wicket every {s.perWicket:0.0} balls");
                if (user) continue;
                // The AI side against ordinary bowling: a T20 shape.
                Assert.InRange(s.dot, 30, 45);
                Assert.InRange(s.one, 27, 40);
                Assert.InRange(s.two, 6, 12);
                Assert.InRange(s.three, 0.3, 2.5);
                Assert.InRange(s.four, 9, 17);
                Assert.InRange(s.six, 3, 8);
                Assert.InRange(s.rpo, 7.5, 10.5);
            }
        }
    }
}
