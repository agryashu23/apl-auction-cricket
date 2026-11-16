using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The tournament's AUCTION path (owner v26 #6): TOURNAMENT → AUCTION → NEW AUCTION sends
    /// you to the auction's setup (format, fresh / retention) as the team you picked; when the
    /// hammer falls the results screen's START TOURNAMENT ► starts the league on the squads
    /// everyone just bought (no second setup) and opens its table. The request survives a
    /// resumed auction: it is tied to that auction's seed in PlayerPrefs.
    /// <para>
    /// Before v26 the only way in was an auction already played ("NO AUCTION YET" was a dead,
    /// greyed button on a fresh install), so choosing AUCTION from the tournament did nothing.
    /// </para>
    /// </summary>
    public static class TournamentAuction
    {
        private const string PrefKey = "apl.tourney.auction";

        /// <summary>Asked for from the hub; bound to the auction when it starts (<see cref="Bind"/>).</summary>
        public static string? PendingTeam { get; private set; }
        public static int PendingOvers { get; private set; } = 5;

        /// <summary>The main menu, when it's up, opens the auction setup for this team (MainMenu.TournamentAuction.cs).</summary>
        public static event Action<string>? Requested;

        /// <summary>No menu to hear it (the hub over the results screen): the hall reloads and the new menu opens the setup.</summary>
        public static bool OpenOnLoad;

        public static void Request(string team, int overs)
        {
            PendingTeam = team;
            PendingOvers = overs;
            Debug.Log($"[APL tourney] auction requested for a tournament: {team}, {overs} overs");
            if (Requested != null)
            {
                Requested.Invoke(team);
                return;
            }
            OpenOnLoad = true;
            SceneManager.LoadScene(0);
        }

        /// <summary>The hub opened again: whatever was asked before is dropped.</summary>
        public static void Cancel() => PendingTeam = null;

        /// <summary>
        /// A solo auction starts (AplBoot.StartGame): it's the tournament's when one was asked for (as whichever
        /// team you settled on in the setup); any other auction clears an old request.
        /// </summary>
        public static void Bind(long seed, string team)
        {
            try
            {
                if (PendingTeam != null) PlayerPrefs.SetString(PrefKey, $"{team}|{PendingOvers}|{seed}");
                else PlayerPrefs.DeleteKey(PrefKey);
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL tourney] could not keep the tournament auction: {ex.Message}");
            }
            PendingTeam = null;
        }

        /// <summary>The overs of the tournament this auction (by its seed) was started for, or null.</summary>
        public static int? OversFor(long seed, string team)
        {
            var v = PlayerPrefs.GetString(PrefKey, "");
            var parts = v.Split('|');
            if (parts.Length != 3 || parts[0] != team || parts[2] != seed.ToString()) return null;
            return int.TryParse(parts[1], out var overs) ? overs : 5;
        }

        /// <summary>The tournament has started (or was turned down): forget the request.</summary>
        public static void Done()
        {
            PlayerPrefs.DeleteKey(PrefKey);
            PlayerPrefs.Save();
        }
    }
}
