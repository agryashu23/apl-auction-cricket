using System;
using System.IO;
using Apl.Client.Menu;
using Apl.Core;
using Apl.Core.Play;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Apl.Client.Match
{
    /// <summary>
    /// The match in progress, kept on disk between balls (persistentDataPath/
    /// match_save.json) so closing the app doesn't lose it: who's playing whom,
    /// where, how long, the toss, a tournament fixture if it is one, and the
    /// engine's book (QuickMatch.Save). RESUME MATCH on the menus opens the
    /// ground straight at the next ball (MatchDirector.Resume.cs).
    /// </summary>
    public static class MatchResume
    {
        public sealed class Record
        {
            public string MyTeam = "", Opponent = "", VenueId = "";
            public int Overs;
            public int TournamentFixture;
            public bool UserBatsFirst;
            public string TossWinner = "";
            public long SavedAt;
            public MatchSave Match = new MatchSave();
        }

        /// <summary>Set by <see cref="Launch"/>: the ground scene restores this instead of starting a new match.</summary>
        public static Record? Pending;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "match_save.json");

        public static void Write(Record r)
        {
            try
            {
                r.SavedAt = DateTime.UtcNow.Ticks;
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(r));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL] could not save the match: {ex.Message}");
            }
        }

        /// <summary>The saved match, or null (none, unreadable, or for players this data no longer has).</summary>
        public static Record? Load(Dataset data)
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var r = JsonConvert.DeserializeObject<Record>(File.ReadAllText(FilePath));
                if (r == null || r.Match == null || r.Match.Order.Count == 0) return null;
                // Rebuilding it here proves it still fits the data (a new dataset can drop players).
                QuickMatch.Restore(r.Match, data, 1);
                return r;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL] ignoring a saved match: {ex.Message}");
                return null;
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL] could not delete the saved match: {ex.Message}");
            }
        }

        /// <summary>"MBH 54/2 (4.3) v CHC" for the menu button.</summary>
        public static string Describe(Record r)
        {
            var m = r.Match;
            var overs = $"{m.LegalBalls / 6}.{m.LegalBalls % 6}";
            return $"{m.BattingTeam} {m.Runs}/{m.Wickets} ({overs}) v {m.BowlingTeam}";
        }

        /// <summary>Open the ground on the saved match (its tournament too, when it's a fixture).</summary>
        public static void Launch(Dataset data, Record r)
        {
            Pending = r;
            MatchLaunch.MyTeam = r.MyTeam;
            MatchLaunch.Opponent = r.Opponent;
            MatchLaunch.Overs = r.Overs;
            MatchLaunch.VenueId = r.VenueId;
            TournamentLaunch.Clear();
            // The saved match has its XIs; no PLAYING XI choice carries into it (owner v26 #7).
            XiChoice.Clear();
            if (r.TournamentFixture > 0 && Tournament.Load(data) is Tournament t)
            {
                TournamentLaunch.Active = t;
                TournamentLaunch.Fixture = r.TournamentFixture;
            }
            SceneManager.LoadScene(MatchLaunch.GroundScene);
        }
    }
}
