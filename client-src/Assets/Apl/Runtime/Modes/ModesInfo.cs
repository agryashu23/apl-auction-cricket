using System.Collections.Generic;
using System.Linq;
using Apl.Client.Audio;
using Apl.Core;

namespace Apl.Client.Modes
{
    /// <summary>One of today's goals as the home page shows it.</summary>
    public readonly struct GoalInfo
    {
        public readonly string Text;
        public readonly int Progress, Target, Xp;
        public readonly bool Done;

        public GoalInfo(string text, int progress, int target, int xp, bool done)
        {
            Text = text;
            Progress = progress;
            Target = target;
            Xp = xp;
            Done = done;
        }

        /// <summary>0–1 for a progress bar.</summary>
        public float Fraction => Target <= 0 ? 1f : Progress / (float)Target;
    }

    /// <summary>
    /// Read-only career numbers and the modes' entry points for the home page
    /// (round 10: presentation-ui's new layout reads these; nothing here changes state
    /// except the Open* calls, which only open pages). Read them each time the page is
    /// built — they're cheap (PlayerPrefs, cached).
    /// </summary>
    public static class ModesInfo
    {
        public static int Level => Progression.Level;

        /// <summary>The player's title (round 12), e.g. "MATCH WINNER": the one equipped in the locker.</summary>
        public static string Title => Progression.Equipped(Slot.Title).Name;

        /// <summary>XP into the current level, and the level's size (for the top bar's XP bar).</summary>
        public static int XpInLevel => Progression.LevelProgress().into;
        public static int XpLevelSize => Progression.LevelProgress().size;
        public static float XpFraction => XpInLevel / (float)System.Math.Max(1, XpLevelSize);
        public static int TotalXp => Progression.Xp;

        public static int Stars => Progression.TotalStars;
        public static int MaxStars => Challenges.MaxStars;
        public static int ChallengesCount => Challenges.All.Length;
        public static int ChallengesCleared => Challenges.All.Count(c => Progression.StarsFor(c.Id) > 0);

        public static int Matches => Progression.Matches;
        public static int Wins => Progression.Wins;

        /// <summary>Today's three goals, in order (the "chain" of the goals row).</summary>
        public static IReadOnlyList<GoalInfo> Goals => DailyGoals.Status().Select(s => new GoalInfo(s.goal.Text, s.progress, s.goal.Target, s.goal.Xp, s.done)).ToList();
        public static int GoalsDone => DailyGoals.Status().Count(s => s.done);

        public static string Difficulty => Modes.Difficulty.Label(Modes.Difficulty.Current);

        /// <summary>Locker: how many cosmetics are unlocked, of how many.</summary>
        public static int Unlocked => Cosmetics.All.Count(Progression.Unlocked);
        public static int CosmeticsCount => Cosmetics.All.Length;

        /// <summary>A reward screen is waiting (a match left before CONTINUE, an auction).</summary>
        public static bool HasPendingReward => RewardCard.Pending != null;

        public static void OpenChallenges(Dataset data) => ChallengesPage.Open(data);
        public static void OpenLocker() => LockerPage.Open();
        public static void OpenSound() => SoundSettings.Open();
        public static bool ShowPendingReward() => RewardCard.ShowPending();
    }
}
