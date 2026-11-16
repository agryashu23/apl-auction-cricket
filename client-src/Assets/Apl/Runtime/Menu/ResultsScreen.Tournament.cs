using System.Linq;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The auction's results as the way into a tournament (owner v26 #6): the squads just
    /// bought, and — when the auction was started from TOURNAMENT → AUCTION — START TOURNAMENT ►,
    /// which starts the league on them at once (the overs chosen in the hub) and opens its table.
    /// </summary>
    public sealed partial class ResultsScreen
    {
        private Button? _tournamentButton;
        private string _auctionId = "";

        /// <summary>Every side's squad as the auction left it (player ids by team).</summary>
        private Tournament.AuctionSquads SquadsNow() => new Tournament.AuctionSquads
        {
            Id = _auctionId,
            Team = _me,
            SavedAt = System.DateTime.UtcNow.Ticks,
            Squads = _engine.Teams.Values.ToDictionary(t => t.Id, t => t.Players.Select(p => p.Id).ToList()),
        };

        /// <summary>This auction was the tournament's: START TOURNAMENT ► (<paramref name="overs"/> a side).</summary>
        public void OfferTournament(int overs, long seed)
        {
            if (_tournamentButton == null) return;
            _tournamentButton.GetComponentInChildren<Text>().text = "START TOURNAMENT ►";
            _tournamentButton.GetComponent<Image>().color = UiKit.Accent;
            _tournamentButton.onClick.RemoveAllListeners();
            _tournamentButton.onClick.AddListener(() => StartTournament(overs, seed));
            UiKit.LabelAt(_root, new Vector2(0, 0), new Vector2(250, 136), new Vector2(560, 34), $"Your tournament · {overs} overs a side · on these squads", 22, TextAnchor.MiddleCenter, UiKit.Gold, bold: false);
        }

        private void StartTournament(int overs, long seed)
        {
            var squads = SquadsNow();
            var t = Tournament.Create(_data, squads.Squads, _me, overs, "auction", (seed * 31 + 17) & 0x7FFFFFFF, _auctionId);
            t.Save();
            TournamentAuction.Done();
            Debug.Log($"[APL tourney] tournament started from the auction: {_me}, {overs} overs, {t.Fixtures.Count} fixtures, squads {string.Join(" ", squads.Squads.Select(kv => kv.Key + ":" + kv.Value.Count))}");
            // The table (the hub loads the tournament just saved).
            TournamentHub.Open(_data, _me, overs);
        }
    }
}
