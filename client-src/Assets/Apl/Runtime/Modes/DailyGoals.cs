using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Modes
{
    /// <summary>
    /// Three goals a day, picked from a pool by the date so everyone's day is the same. Each goal
    /// pays its XP once, the moment it's done. Kept in PlayerPrefs; a new day starts a new three.
    ///
    /// AUCTION goals since the auction-first home (UI/UX dev, rater r1 §10: "Hit 6 fours", "Play 2
    /// matches" filled a third of an auction game's home): "Buy a player at his base price",
    /// "Win a bidding war over ₹10 Cr", "Finish top 3 in an auction", "Complete a room auction"…
    /// Progress comes from every finished auction (solo or room: <see cref="ApplyAuction"/>, called
    /// by AuctionRewards.Record). Matches still pass through <see cref="Apply"/> (Progression), but
    /// no auction goal counts a match.
    /// </summary>
    public static class DailyGoals
    {
        /// <summary>What one finished auction did, for the goals (AuctionGoalFacts.From).</summary>
        public sealed class AuctionFacts
        {
            public int Bought, BoughtAtBase, WarsOver10Cr, RtmUsed, Steals, Place = 99, PurseLeftLakhs;
            public bool Room, Mega;
        }

        public sealed class Goal
        {
            public string Id = "", Text = "";
            public int Target, Xp;
            public Func<MatchStats, int> Count = _ => 0;
            public Func<AuctionFacts, int> Auction = _ => 0;
        }

        private static readonly Goal[] Pool =
        {
            new Goal { Id = "base", Text = "Buy a player at his base price", Target = 1, Xp = 50, Auction = a => a.BoughtAtBase },
            new Goal { Id = "war10", Text = "Win a bidding war over ₹10 Cr", Target = 1, Xp = 80, Auction = a => a.WarsOver10Cr },
            new Goal { Id = "top3", Text = "Finish top 3 in an auction", Target = 1, Xp = 80, Auction = a => a.Place <= 3 ? 1 : 0 },
            new Goal { Id = "room", Text = "Complete a room auction", Target = 1, Xp = 60, Auction = a => a.Room ? 1 : 0 },
            new Goal { Id = "rtm", Text = "Use a Right to Match card", Target = 1, Xp = 50, Auction = a => a.RtmUsed },
            new Goal { Id = "steal", Text = "Land 2 steals (bargain buys)", Target = 2, Xp = 60, Auction = a => a.Steals },
            new Goal { Id = "buys", Text = "Buy 8 players at auction", Target = 8, Xp = 50, Auction = a => a.Bought },
            new Goal { Id = "two", Text = "Finish 2 auctions", Target = 2, Xp = 60, Auction = _ => 1 },
        };

        private const string Key = "apl.daily.auction";

        private static string Today => DateTime.Now.ToString("yyyy-MM-dd");

        /// <summary>Today's three (the same for everyone on a date).</summary>
        public static Goal[] Current
        {
            get
            {
                var rng = new System.Random(DateTime.Now.Year * 1000 + DateTime.Now.DayOfYear);
                return Pool.OrderBy(_ => rng.Next()).Take(3).ToArray();
            }
        }

        /// <summary>Progress on today's goals (reset when the date changes).</summary>
        private static int[] Progress()
        {
            var raw = PlayerPrefs.GetString(Key, "");
            var parts = raw.Split('|');
            if (parts.Length == 4 && parts[0] == Today) return parts.Skip(1).Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
            return new int[3];
        }

        private static void Save(int[] p)
        {
            PlayerPrefs.SetString(Key, Today + "|" + string.Join("|", p));
            PlayerPrefs.Save();
        }

        /// <summary>Today's goals with their progress (for the home page's goals card: ModesInfo).</summary>
        public static IEnumerable<(Goal goal, int progress, bool done)> Status()
        {
            var p = Progress();
            var goals = Current;
            for (var i = 0; i < goals.Length; i++) yield return (goals[i], Math.Min(p[i], goals[i].Target), p[i] >= goals[i].Target);
        }

        /// <summary>"Hit 3 sixes  2/3" lines, done ones ticked.</summary>
        public static IEnumerable<(string text, bool done)> Lines()
        {
            var p = Progress();
            var goals = Current;
            for (var i = 0; i < goals.Length; i++)
                yield return ($"{goals[i].Text}  {Math.Min(p[i], goals[i].Target)}/{goals[i].Target}  (+{goals[i].Xp} XP)", p[i] >= goals[i].Target);
        }

        /// <summary>A match's stats into today's goals; the XP for those it completed (none of today's are match goals).</summary>
        public static List<(int xp, string line)> Apply(MatchStats m) => Advance(g => g.Count(m));

        /// <summary>A finished auction into today's goals; the XP for those it completed.</summary>
        public static List<(int xp, string line)> ApplyAuction(AuctionFacts a) => Advance(g => g.Auction(a));

        private static List<(int xp, string line)> Advance(Func<Goal, int> count)
        {
            var p = Progress();
            var goals = Current;
            var done = new List<(int, string)>();
            for (var i = 0; i < goals.Length; i++)
            {
                if (p[i] >= goals[i].Target) continue;
                p[i] = Math.Min(goals[i].Target, p[i] + Math.Max(0, count(goals[i])));
                if (p[i] >= goals[i].Target) done.Add((goals[i].Xp, "Daily goal: " + goals[i].Text));
            }
            Save(p);
            return done;
        }
    }
}
