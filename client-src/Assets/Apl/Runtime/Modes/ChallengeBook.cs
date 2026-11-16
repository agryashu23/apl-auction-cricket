using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;

namespace Apl.Client.Modes
{
    /// <summary>
    /// A challenge as a match book (no Unity here, so it can be checked outside the
    /// game): a 20-over game written up to the scenario's ball — the first innings
    /// totalling one less than the target, the chasing side's score, wickets, balls,
    /// batters' and bowlers' lines all adding up — as a <see cref="MatchSave"/> the
    /// ground restores like a resumed match (QuickMatch.Restore). The engine itself
    /// is untouched. The user bats the chase, or (UserBats false) bowls at it after
    /// his side's simulated innings, as in a full match's second innings.
    /// </summary>
    public static class ChallengeBook
    {
        public const int Overs = 20;

        public static MatchSave Build(Dataset data, Challenge ch, string myTeam, string opponent, long seed)
        {
            var mine = QuickMatch.LastSeasonXI(data, myTeam);
            var theirs = QuickMatch.LastSeasonXI(data, opponent);
            var venue = Venue.HomeOf(myTeam);
            var rng = new Random((int)(seed & 0x7FFFFFFF));
            MatchSave s;
            if (ch.UserBats) s = new QuickMatch(mine, theirs, Overs, seed, venue).Save();
            else
            {
                var full = QuickMatch.Full(mine, theirs, Overs, seed, venue, userBatsFirst: true);
                full.StartSecondInnings();
                s = full.Save();
                // His side's innings, simulated: what the opponent is chasing.
                s.First = new QuickMatch(theirs, mine, Overs, seed ^ 0x5BD1E995, venue).Save().First;
            }
            var names = data.Players.ToDictionary(p => p.Id, p => p.Name);
            var done = Overs * 6 - ch.Balls;
            var wickets = s.Order.Count - 1 - ch.WicketsLeft;
            // The chase so far at a death-overs-ish 7.4–8.8 an over.
            var runs = (int)Math.Round(done * (7.4 + rng.NextDouble() * 1.4) / 6);
            SetTotal(s.First!, runs + ch.Need - 1);
            if (!ch.UserBats)
            {
                s.FirstOrder = s.First!.Batters;
                s.FirstBowling = s.First.Bowlers;
                s.FirstLog = new List<BallSave>();
            }
            WriteChase(s, runs, wickets, done, rng, names);
            return s;
        }

        /// <summary>The first innings made to total <paramref name="total"/>: the difference on its top scorer(s) and its most expensive bowler(s).</summary>
        private static void SetTotal(InningsSave f, int total)
        {
            var delta = total - f.Runs;
            f.Runs = total;
            Spread(f.Batters.Where(b => b.Batted).OrderByDescending(b => b.Runs).ToList(), delta, b => b.Runs, (b, v) => b.Runs = v);
            Spread(f.Bowlers.OrderByDescending(b => b.Runs).ToList(), delta, b => b.Runs, (b, v) => b.Runs = v);
        }

        /// <summary>Adds <paramref name="delta"/> (or takes it away, never below 0) down the list.</summary>
        private static void Spread<T>(List<T> list, int delta, Func<T, int> get, Action<T, int> set)
        {
            foreach (var x in list)
            {
                if (delta == 0) return;
                var v = Math.Max(0, get(x) + delta);
                delta -= v - get(x);
                set(x, v);
            }
        }

        private static void WriteChase(MatchSave s, int runs, int wickets, int done, Random rng, Dictionary<string, string> names)
        {
            s.Runs = runs;
            s.Wickets = wickets;
            s.LegalBalls = done;
            s.Extras = Math.Min(runs, (int)Math.Round(runs * 0.05));
            s.Log = new List<BallSave>();
            s.FreeHit = false;
            // Who bowled each over so far (the plan's), what each over cost and where the wickets fell.
            var overs = (done + 5) / 6;
            var overRuns = Split(runs - s.Extras, overs, rng);
            var wides = s.Extras / 2;
            var figures = new Dictionary<string, BowlerSave>();
            var overOfWicket = Enumerable.Range(0, wickets).Select(_ => rng.Next(overs)).OrderBy(o => o).ToList();
            for (var o = 0; o < overs; o++)
            {
                var id = s.Plan[Math.Min(o, s.Plan.Count - 1)];
                if (!figures.TryGetValue(id, out var f)) figures[id] = f = new BowlerSave { Id = id };
                var balls = Math.Min(6, done - 6 * o);
                f.Balls += balls;
                f.Runs += overRuns[o];
                f.Wickets += overOfWicket.Count(w => w == o);
                f.Dots += Math.Max(0, balls - (overRuns[o] + 1) / 2);
            }
            foreach (var f in figures.Values.OrderByDescending(x => x.Runs))
            {
                if (wides == 0) break;
                f.Wides++;
                f.Runs++;
                wides--;
            }
            // (Wides above what the overs carry go in as byes: the extras stay as written.)
            s.Bowlers = figures.Values.ToList();
            WriteBatters(s, runs - s.Extras, wickets, done, rng, names, overOfWicket);
        }

        private static void WriteBatters(MatchSave s, int batRuns, int wickets, int done, Random rng, Dictionary<string, string> names, List<int> overOfWicket)
        {
            var batted = wickets + 2;
            // Top order with more of the runs and the balls; the men in now with a start.
            var weights = Enumerable.Range(0, batted).Select(i => (0.5 + rng.NextDouble()) / (1 + 0.3 * i)).ToList();
            var runs = Split(batRuns, batted, rng, weights);
            var balls = Split(done, batted, rng, runs.Select(r => 0.6 + r).ToList());
            for (var i = 0; i < s.Order.Count; i++)
            {
                var b = s.Order[i];
                b.Batted = i < batted;
                b.Runs = b.Batted ? runs[i] : 0;
                b.Balls = b.Batted ? Math.Max(runs[i] > 0 ? 1 : 0, balls[i]) : 0;
                b.Sixes = b.Runs / 16;
                b.Fours = Math.Max(0, (b.Runs - 6 * b.Sixes) / 9);
                b.HowOut = i < wickets ? Dismissal(s, overOfWicket[i], rng, names) : null;
            }
            s.Striker = wickets;
            s.NonStriker = wickets + 1;
            s.NextIn = wickets + 2;
        }

        private static string Dismissal(MatchSave s, int over, Random rng, Dictionary<string, string> names)
        {
            string N(string id) => names.TryGetValue(id, out var n) ? n : id;
            var bowler = N(s.Plan[Math.Min(over, s.Plan.Count - 1)]);
            var fielder = N(s.BowlingXI[rng.Next(s.BowlingXI.Count)]);
            return rng.Next(10) switch
            {
                < 5 => $"c {fielder} b {bowler}",
                < 7 => $"b {bowler}",
                < 9 => $"lbw b {bowler}",
                _ => $"c †keeper b {bowler}",
            };
        }

        /// <summary><paramref name="total"/> in <paramref name="parts"/> whole parts by weight (random if none), summing exactly.</summary>
        private static int[] Split(int total, int parts, Random rng, List<double>? weights = null)
        {
            var out1 = new int[Math.Max(0, parts)];
            if (parts <= 0) return out1;
            weights ??= Enumerable.Range(0, parts).Select(_ => 0.4 + rng.NextDouble()).ToList();
            var sum = weights.Sum();
            var given = 0;
            for (var i = 0; i < parts; i++) given += out1[i] = (int)Math.Floor(total * weights[i] / sum);
            for (var i = 0; given < total; i = (i + 1) % parts, given++) out1[i]++;
            return out1;
        }
    }
}
