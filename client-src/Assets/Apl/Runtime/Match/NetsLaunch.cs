using UnityEngine.SceneManagement;

namespace Apl.Client.Match
{
    /// <summary>
    /// PRACTICE from the match menu: the ground opens in nets mode
    /// (MatchDirector.Nets.cs) — endless balls, tips step by step, no score to
    /// chase — batting against the opponent's attack, or bowling at its batters.
    /// </summary>
    public static class NetsLaunch
    {
        public enum Kind { None, Batting, Bowling }

        /// <summary>What the ground scene is for (None: a match).</summary>
        public static Kind Mode = Kind.None;

        public static void Open(Kind kind, string myTeam, string opponent, string venueId)
        {
            Mode = kind;
            MatchLaunch.MyTeam = myTeam;
            MatchLaunch.Opponent = opponent;
            MatchLaunch.VenueId = venueId;
            // Long enough never to run out; the session restarts the innings if it does.
            MatchLaunch.Overs = 20;
            Apl.Client.Menu.TournamentLaunch.Clear();
            MatchResume.Pending = null;
            SceneManager.LoadScene(MatchLaunch.GroundScene);
        }
    }
}
