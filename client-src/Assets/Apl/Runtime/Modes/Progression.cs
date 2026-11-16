using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Apl.Client.Modes
{
    /// <summary>What one finished match (or challenge) earned: the lines of the XP breakdown and what it unlocked.</summary>
    public sealed class Award
    {
        public int Xp;
        public readonly List<string> Lines = new List<string>();
        public int LevelBefore, LevelAfter;
        public readonly List<Cosmetic> Unlocked = new List<Cosmetic>();
        public bool LevelledUp => LevelAfter > LevelBefore;
    }

    /// <summary>What the user's side did in a match, for <see cref="Progression.Record"/>.</summary>
    public struct MatchStats
    {
        public bool Won, Tied, Challenge;
        public int RunsScored, Sixes, Fours, WicketsTaken;
        /// <summary>A challenge's stars this time, and how many it had before (only the gain earns XP).</summary>
        public int Stars, StarsBefore;
    }

    /// <summary>
    /// The player's career (round 8, modes): XP from every match and challenge,
    /// levels, and the cosmetics they unlock for the user's own batters (Locker):
    /// bat designs, helmet and pads-and-gloves colourways, celebrations, titles (THE VAULT). Kept locally (PlayerPrefs, JSON);
    /// nothing touches the season or the auction.
    /// </summary>
    public static class Progression
    {
        private const string Key = "apl.career";

        private sealed class State
        {
            public int Xp, Matches, Wins, Sixes, Wickets;
            /// <summary>Slot → the cosmetic equipped there (Locker).</summary>
            public Dictionary<string, string> Equipped = new Dictionary<string, string>();
            public Dictionary<string, int> Stars = new Dictionary<string, int>();
        }

        private static State? _state;

        private static State S
        {
            get
            {
                if (_state != null) return _state;
                try { _state = JsonConvert.DeserializeObject<State>(PlayerPrefs.GetString(Key, "")); }
                catch (Exception ex) { Debug.LogWarning($"[APL] career unreadable, starting afresh: {ex.Message}"); }
                return _state ??= new State();
            }
        }

        private static void Save()
        {
            PlayerPrefs.SetString(Key, JsonConvert.SerializeObject(S));
            PlayerPrefs.Save();
        }

        public static int Xp => S.Xp;
        public static int Matches => S.Matches;
        public static int Wins => S.Wins;

        /// <summary>XP to climb from <paramref name="level"/> to the next: 200, 300, 400…</summary>
        /// <remarks>Round 12: the step stops growing at level 10 (1,100 a level), so the ladder to 50 is a long climb, not a wall.</remarks>
        public static int XpToNext(int level) => 200 + 100 * Math.Min(level - 1, 9);

        public static int LevelOf(int xp)
        {
            var level = 1;
            while (xp >= XpToNext(level)) xp -= XpToNext(level++);
            return level;
        }

        public static int Level => LevelOf(S.Xp);

        /// <summary>Where <paramref name="xp"/> total XP stands: the level, XP into it, and the level's size.</summary>
        public static (int level, int into, int size) ProgressAt(int xp)
        {
            var level = 1;
            while (xp >= XpToNext(level)) xp -= XpToNext(level++);
            return (level, xp, XpToNext(level));
        }

        /// <summary>XP into the current level, and the level's size (for the bar).</summary>
        public static (int into, int size) LevelProgress()
        {
            var xp = S.Xp;
            var level = 1;
            while (xp >= XpToNext(level)) xp -= XpToNext(level++);
            return (xp, XpToNext(level));
        }

        // ------------------------------------------------------------ challenges

        public static int StarsFor(string challengeId) => S.Stars.TryGetValue(challengeId, out var n) ? n : 0;
        public static int TotalStars => S.Stars.Values.Sum();

        // ------------------------------------------------------------ the locker

        /// <summary>What the user's batters wear in <paramref name="slot"/> (the default if nothing unlocked is equipped).</summary>
        public static Cosmetic Equipped(Slot slot) =>
            S.Equipped.TryGetValue(slot.ToString(), out var id) && Cosmetics.Find(id) is Cosmetic c && c.Slot == slot && Unlocked(c) ? c : Cosmetics.Default(slot);

        public static bool Unlocked(Cosmetic c) => c.Level > 0 ? Level >= c.Level : TotalStars >= c.Stars;

        public static void Equip(Cosmetic c)
        {
            if (!Unlocked(c)) return;
            S.Equipped[c.Slot.ToString()] = c.Id;
            Save();
        }

        // ------------------------------------------------------------ other awards

        /// <summary>XP for something other than a match (an auction finished, a season's result): the lines and the total.</summary>
        public static Award RecordEvent(params (int xp, string line)[] items)
        {
            var a = new Award { LevelBefore = Level };
            var before = Cosmetics.All.Where(Unlocked).Select(c => c.Id).ToHashSet();
            foreach (var (xp, line) in items)
            {
                if (xp <= 0) continue;
                a.Xp += xp;
                a.Lines.Add($"+{xp}  {line}");
            }
            S.Xp += a.Xp;
            Save();
            a.LevelAfter = Level;
            a.Unlocked.AddRange(Cosmetics.All.Where(c => Unlocked(c) && !before.Contains(c.Id)));
            return a;
        }

        // ------------------------------------------------------------ the award

        /// <summary>"1 six", "2 sixes", "1 run" (round 13: "1 sixes" on the rewards screen).</summary>
        private static string Count(int n, string one, string? many = null) => $"{n} {(n == 1 ? one : many ?? one + "s")}";

        /// <summary>A match is over: its XP (by what the side did, the result, the difficulty) into the career.</summary>
        public static Award Record(MatchStats m, string? challengeId = null)
        {
            var a = new Award { LevelBefore = Level };
            var unlockedBefore = Cosmetics.All.Where(Unlocked).Select(c => c.Id).ToHashSet();
            var xp = 0;
            void Add(int n, string line)
            {
                if (n <= 0) return;
                xp += n;
                a.Lines.Add($"+{n}  {line}");
            }
            Add(m.Challenge ? 20 : 40, m.Challenge ? "Challenge played" : "Match played");
            if (m.Won) Add(m.Challenge ? 30 : 60, "Win");
            else if (m.Tied) Add(30, "Tie");
            Add(Mathf.Min(60, m.RunsScored / 2), Count(m.RunsScored, "run"));
            Add(Mathf.Min(40, m.Sixes * 3), Count(m.Sixes, "six", "sixes"));
            Add(Mathf.Min(60, m.WicketsTaken * 8), Count(m.WicketsTaken, "wicket"));
            if (m.Challenge && challengeId != null && m.Stars > m.StarsBefore)
            {
                Add((m.Stars - m.StarsBefore) * 40, $"{m.Stars - m.StarsBefore} new {(m.Stars - m.StarsBefore == 1 ? "star" : "stars")}");
                S.Stars[challengeId] = m.Stars;
            }
            var scale = Difficulty.XpScale(Difficulty.Current);
            if (!Mathf.Approximately(scale, 1f))
            {
                var bonus = Mathf.RoundToInt(xp * scale) - xp;
                a.Lines.Add($"{(bonus >= 0 ? "+" : "")}{bonus}  {Difficulty.Label(Difficulty.Current)} difficulty");
                xp += bonus;
            }
            // Today's goals it completed (DailyGoals): their XP on top, not scaled.
            foreach (var (gx, line) in DailyGoals.Apply(m)) Add(gx, line);
            a.Xp = Mathf.Max(0, xp);
            S.Xp += a.Xp;
            S.Matches++;
            if (m.Won) S.Wins++;
            S.Sixes += m.Sixes;
            S.Wickets += m.WicketsTaken;
            Save();
            a.LevelAfter = Level;
            a.Unlocked.AddRange(Cosmetics.All.Where(c => Unlocked(c) && !unlockedBefore.Contains(c.Id)));
            return a;
        }
    }
}
