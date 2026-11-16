using System.Collections.Generic;
using Apl.Core;
using Apl.Core.Play;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The tournament match the ground scene is playing (null for a quick
    /// match), carried across the scene load like <c>MatchLaunch</c>; and the
    /// flag that sends the hall's menu straight back to the tournament hub.
    /// </summary>
    public static class TournamentLaunch
    {
        public static Tournament? Active;
        public static int Fixture;

        /// <summary>Set when leaving a tournament match: the menu reopens the hub.</summary>
        public static bool ReturnToHub;

        public static Tournament.Fixture? Current => Active?.Fixtures.Find(f => f.Number == Fixture);

        /// <summary>This side's XI for the match being played, or null (not a tournament match).</summary>
        public static TeamSheet? Sheet(Dataset data, string teamId) => Active?.Sheet(data, teamId);

        /// <summary>"TOURNAMENT · MATCH 12" / "TOURNAMENT · QUALIFIER 1".</summary>
        public static string? Title()
        {
            var f = Current;
            if (f == null) return null;
            return "TOURNAMENT · " + (f.League ? $"MATCH {f.Number} OF {Active!.LeagueCount}" : f.Stage.ToUpperInvariant());
        }

        /// <summary>His tournament numbers for the intro card, or null (no tournament, or not played yet).</summary>
        public static Tournament.Line? Stats(string playerId) =>
            Active != null && Active.Stats.TryGetValue(playerId, out var s) && s.Matches > 0 ? s : null;

        /// <summary>The user's match is over: into the table, saved.</summary>
        /// <param name="superOverWinner">A tie settled by a Super Over: its winner (null: no Super Over, or it was level too).</param>
        public static void Record(Dataset data, QuickMatch m, string? superOverWinner = null)
        {
            if (Active == null || Current == null || Current.Played) return;
            Active.RecordUserMatch(data, Fixture, m, superOverWinner);
            Active.Save();
        }

        public static void Clear()
        {
            Active = null;
            Fixture = 0;
        }
    }
}
