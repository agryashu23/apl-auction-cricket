using System.Collections;
using System.Linq;

namespace Apl.Client.Menu
{
    /// <summary>
    /// TOURNAMENT → AUCTION → NEW AUCTION (owner v26 #6, Menu/TournamentAuction.cs): the
    /// auction's setup (format, fresh / retention) for the team picked in the hub. Heard while
    /// the menu is up; after a hall reload (the hub was over the results screen) on its first frame.
    /// </summary>
    public sealed partial class MainMenu
    {
        private void OnEnable()
        {
            TournamentAuction.Requested += OpenTournamentAuction;
            if (TournamentAuction.OpenOnLoad) StartCoroutine(OpenTournamentAuctionSoon());
        }

        private void OnDisable() => TournamentAuction.Requested -= OpenTournamentAuction;

        private IEnumerator OpenTournamentAuctionSoon()
        {
            // Create() fills the menu in after AddComponent (this OnEnable): one frame for it.
            yield return null;
            if (!TournamentAuction.OpenOnLoad) yield break;
            TournamentAuction.OpenOnLoad = false;
            if (TournamentAuction.PendingTeam != null) OpenTournamentAuction(TournamentAuction.PendingTeam);
        }

        private void OpenTournamentAuction(string team)
        {
            if (_data == null) return;
            var f = _data.Franchises.FirstOrDefault(x => x.Id == team);
            if (f == null) return;
            _setup.Team = f;
            _setup.Retained.Clear();
            _formatFromHome = false;
            ShowOptions();
        }
    }
}
