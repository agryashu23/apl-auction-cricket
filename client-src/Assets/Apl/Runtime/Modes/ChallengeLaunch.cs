using System;
using Apl.Client.Match;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Modes
{
    /// <summary>
    /// Plays a challenge: its book (ChallengeBook) handed to the ground as a match to
    /// pick up at the next ball (MatchResume's path: no ceremony, straight in), with
    /// <see cref="Active"/> telling the director it's a challenge (its banner, no
    /// saving over the user's real match, the stars at the end). <see cref="ReturnToList"/>
    /// opens the challenges page again when the home page comes back.
    /// </summary>
    public static class ChallengeLaunch
    {
        /// <summary>The challenge the ground is playing (null: a normal match).</summary>
        public static Challenge? Active;

        /// <summary>Back on the home page: open the challenges page (set by the result's CHALLENGES button).</summary>
        public static bool ReturnToList;

        /// <summary>Your side and the opposition for challenges: the quick match's saved pair.</summary>
        public static string MyTeam = "BLR", Opponent = "MBH";

        public static bool Play(Dataset data, Challenge ch)
        {
            try
            {
                var seed = DateTime.Now.Ticks & 0x7FFFFFFF;
                var save = ChallengeBook.Build(data, ch, MyTeam, Opponent, seed);
                // Proves it loads before the scene goes (a dataset that's lost a player throws here).
                QuickMatch.Restore(save, data, 1);
                Active = ch;
                NetsLaunch.Mode = NetsLaunch.Kind.None;
                MatchResume.Launch(data, new MatchResume.Record
                {
                    MyTeam = MyTeam,
                    Opponent = Opponent,
                    VenueId = Venue.HomeOf(MyTeam).Id,
                    Overs = ChallengeBook.Overs,
                    UserBatsFirst = !ch.UserBats,
                    TossWinner = MyTeam,
                    Match = save,
                });
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL] challenge {ch.Id} wouldn't start: {ex.Message}");
                Active = null;
                return false;
            }
        }
    }
}
