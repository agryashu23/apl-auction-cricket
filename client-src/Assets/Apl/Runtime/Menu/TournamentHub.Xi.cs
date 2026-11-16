using System.Linq;
using Apl.Client.Match;
using UnityEngine.SceneManagement;

namespace Apl.Client.Menu
{
    /// <summary>
    /// A tournament match's PLAYING XI (owner v26 #7): your tournament squad (the auction's
    /// or last season's), the auto-picked XI pre-filled, 30 s to confirm — at 0 the XI as it
    /// stands plays. BACK returns to the table (the matches before yours stay simulated).
    /// </summary>
    public sealed partial class TournamentHub
    {
        private void PickXiThenPlay(Tournament t, Tournament.Fixture f)
        {
            var ids = t.Squads.TryGetValue(t.UserTeam, out var s) ? s.ToHashSet() : new System.Collections.Generic.HashSet<string>();
            var squad = _data.Players.Where(p => ids.Contains(p.Id)).ToList();
            var auto = t.Sheet(_data, t.UserTeam).XI;
            var opp = f.Other(t.UserTeam);
            var oppIds = t.Squads.TryGetValue(opp, out var o) ? o.ToHashSet() : new System.Collections.Generic.HashSet<string>();
            UnityEngine.Debug.Log($"[APL tourney] match {f.Number} ({t.Source} squads): {t.UserTeam} squad {squad.Count}, auto XI from squad: {(auto.All(p => ids.Contains(p.Id)) ? "yes" : "no")}; " +
                                  $"{opp} XI from squad: {(t.Sheet(_data, opp).XI.All(p => oppIds.Contains(p.Id)) ? "yes" : "no")}");
            // A squad short of eleven plays the fallback XI (Tournament.Sheet): nothing to choose.
            if (squad.Count < 11)
            {
                XiChoice.Clear();
                SceneManager.LoadScene(MatchLaunch.GroundScene);
                return;
            }
            PlayingXiScreen.Open(_data, t.UserTeam, f.Other(t.UserTeam), squad, auto, PlayingXiScreen.TimedSeconds,
                _ => SceneManager.LoadScene(MatchLaunch.GroundScene),
                () =>
                {
                    TournamentLaunch.Clear();
                    ShowTable();
                });
        }
    }
}
