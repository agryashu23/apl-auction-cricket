using System;

namespace Apl.Client.Modes
{
    /// <summary>How a challenge went, for its stars (filled in by the director when it's over).</summary>
    public struct ChallengeOutcome
    {
        public bool Won;
        public int BallsLeft;
        /// <summary>Batting: wickets the user lost. Bowling: wickets he took.</summary>
        public int Wickets;
        /// <summary>Batting: runs he made. Bowling: runs he gave away.</summary>
        public int Runs;
        public int Sixes;
    }

    /// <summary>
    /// A scenario from the death overs of a 20-over game (round 8, modes): the user
    /// bats needing <see cref="Need"/> off <see cref="Balls"/> with
    /// <see cref="WicketsLeft"/> wickets in hand, or bowls defending it. One star for
    /// the win, two more for doing it in style (see each one's lines).
    /// </summary>
    public sealed class Challenge
    {
        public string Id = "", Title = "", Brief = "";
        public bool UserBats = true;
        public int Need, Balls, WicketsLeft;
        public string TwoStar = "", ThreeStar = "";
        public Func<ChallengeOutcome, bool> Two = _ => true, Three = _ => true;

        /// <summary>0–3 for how it went.</summary>
        public int Stars(ChallengeOutcome o) => !o.Won ? 0 : 1 + (Two(o) ? 1 : 0) + (Two(o) && Three(o) ? 1 : 0);

        /// <summary>"NEED 12 OFF 6 · 3 WICKETS LEFT" / "YOU BOWL · THEY NEED 10 OFF 6 · 4 WICKETS LEFT".</summary>
        public string Situation => UserBats
            ? $"NEED {Need} OFF {Balls} · {WicketsLeft} {(WicketsLeft == 1 ? "WICKET" : "WICKETS")} LEFT"
            : $"YOU BOWL · THEY NEED {Need} OFF {Balls} · {WicketsLeft} {(WicketsLeft == 1 ? "WICKET" : "WICKETS")} LEFT";
    }

    /// <summary>The challenge list, easiest first (ChallengesPage shows it; ChallengeLaunch plays one).</summary>
    public static class Challenges
    {
        public static readonly Challenge[] All =
        {
            new Challenge { Id = "c01", Title = "Last-Over Thriller", Brief = "Twelve to win off the final over. Keep your nerve.",
                Need = 12, Balls = 6, WicketsLeft = 3,
                TwoStar = "Win with a ball to spare", Two = o => o.BallsLeft >= 1, ThreeStar = "…without losing a wicket", Three = o => o.Wickets == 0 },
            new Challenge { Id = "c02", Title = "Defend Ten", Brief = "They need ten off the last over. You have the ball.",
                UserBats = false, Need = 10, Balls = 6, WicketsLeft = 4,
                TwoStar = "Concede 6 or fewer", Two = o => o.Runs <= 6, ThreeStar = "…and take 2 wickets", Three = o => o.Wickets >= 2 },
            new Challenge { Id = "c03", Title = "Death Overs", Brief = "24 off the last two overs, two wickets in hand.",
                Need = 24, Balls = 12, WicketsLeft = 2,
                TwoStar = "Win with 2 balls to spare", Two = o => o.BallsLeft >= 2, ThreeStar = "…without losing a wicket", Three = o => o.Wickets == 0 },
            new Challenge { Id = "c04", Title = "Tail-End Hero", Brief = "Last man in. Fifteen off two overs.",
                Need = 15, Balls = 12, WicketsLeft = 1,
                TwoStar = "Win with 3 balls to spare", Two = o => o.BallsLeft >= 3, ThreeStar = "…and hit a six", Three = o => o.Sixes >= 1 },
            new Challenge { Id = "c05", Title = "Death Bowling", Brief = "They need 18 off 12 with five wickets left. Close it out.",
                UserBats = false, Need = 18, Balls = 12, WicketsLeft = 5,
                TwoStar = "Concede 12 or fewer", Two = o => o.Runs <= 12, ThreeStar = "…and take 3 wickets", Three = o => o.Wickets >= 3 },
            new Challenge { Id = "c06", Title = "Twenty in the Last", Brief = "Twenty off the final over. Five wickets left, so swing.",
                Need = 20, Balls = 6, WicketsLeft = 5,
                TwoStar = "Win with a ball to spare", Two = o => o.BallsLeft >= 1, ThreeStar = "…hitting 2 sixes", Three = o => o.Sixes >= 2 },
            new Challenge { Id = "c07", Title = "The Big Ask", Brief = "36 off the last three overs. Four wickets left.",
                Need = 36, Balls = 18, WicketsLeft = 4,
                TwoStar = "Win with 3 balls to spare", Two = o => o.BallsLeft >= 3, ThreeStar = "…losing no more than 1 wicket", Three = o => o.Wickets <= 1 },
            new Challenge { Id = "c08", Title = "Last Pair", Brief = "Their last pair need 8 off 12. One wicket wins it.",
                UserBats = false, Need = 8, Balls = 12, WicketsLeft = 1,
                TwoStar = "Concede 4 or fewer", Two = o => o.Runs <= 4, ThreeStar = "…bowl them out inside 6 balls", Three = o => o.BallsLeft >= 6 },
            new Challenge { Id = "c09", Title = "Powerplay Blitz", Brief = "50 off the last five overs with six wickets in hand.",
                Need = 50, Balls = 30, WicketsLeft = 6,
                TwoStar = "Win with 6 balls to spare", Two = o => o.BallsLeft >= 6, ThreeStar = "…losing no more than 1 wicket", Three = o => o.Wickets <= 1 },
            new Challenge { Id = "c10", Title = "Super Defence", Brief = "Fifteen to defend in the last over. They have two wickets.",
                UserBats = false, Need = 15, Balls = 6, WicketsLeft = 2,
                TwoStar = "Concede 8 or fewer", Two = o => o.Runs <= 8, ThreeStar = "…and take a wicket", Three = o => o.Wickets >= 1 },
            new Challenge { Id = "c11", Title = "Rebuild", Brief = "45 off six overs, three wickets left. No panic.",
                Need = 45, Balls = 36, WicketsLeft = 3,
                TwoStar = "Lose no more than 1 wicket", Two = o => o.Wickets <= 1, ThreeStar = "…with 6 balls to spare", Three = o => o.BallsLeft >= 6 },
            new Challenge { Id = "c12", Title = "Six to Win", Brief = "Five off the last ball. Only a six will do.",
                Need = 5, Balls = 1, WicketsLeft = 1,
                TwoStar = "Win it", Two = o => true, ThreeStar = "Win it", Three = o => true },
        };

        public static Challenge? Find(string id) => Array.Find(All, c => c.Id == id);

        public static int MaxStars => All.Length * 3;
    }
}
