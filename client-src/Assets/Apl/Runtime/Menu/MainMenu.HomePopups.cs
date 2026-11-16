using System;
using Apl.Client.Audio;
using Apl.Client.Match;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The home page's pop-ups: SETTINGS (the organised list: Menu/GameSettings.cs), PRACTICE NETS (bat or bowl),
    /// and the tournament hub opened over the page.
    /// </summary>
    public sealed partial class MainMenu
    {
        /// <summary>A glass pop-up over the page with its title and a close pill; returns the panel to fill.</summary>
        private RectTransform Popup(string title, Vector2 size, out Action close)
        {
            var dim = PanelKit.Backdrop(_page, 0.82f);
            var panel = PanelKit.GlassPanel(dim, "Popup", new Vector2(0.5f, 0.5f), new Vector2(0, -20), size);
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -90), new Vector2(-40, -20), title, 48, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            close = () => Destroy(dim.gameObject);
            var c = close;
            PanelKit.Pill(panel, "Close", new Vector2(1, 1), new Vector2(-110, -70), new Vector2(160, 120), "X", new Color(0.12f, 0.14f, 0.22f, 0.95f), c, 40);
            return panel;
        }

        /// <summary>The gear: the organised settings (owner v26 #14) — SOUND / GAMEPLAY lists, each with BACK (Menu/GameSettings.cs).</summary>
        private void ShowSettings() => GameSettings.Open();

        private void ShowNets()
        {
            var panel = Popup("PRACTICE NETS", new Vector2(1100, 560), out _);
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -150), new Vector2(-40, -96), "Endless balls, tips as you go, no score to chase.", 26, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
            var (mine, opp) = MatchMenu.SavedTeams(_data);
            var venue = PlayerPrefs.GetString(MatchMenu.VenueKey, "");
            if (Venue.Find(venue) == null) venue = Venue.HomeOf(mine).Id;
            PanelKit.Pill(panel, "NetsBat", new Vector2(0.5f, 0.5f), new Vector2(-240, -60), new Vector2(420, 200), "BATTING", UiKit.Accent,
                () => NetsLaunch.Open(NetsLaunch.Kind.Batting, mine, opp, venue), 52);
            PanelKit.Pill(panel, "NetsBowl", new Vector2(0.5f, 0.5f), new Vector2(240, -60), new Vector2(420, 200), "BOWLING", new Color(0.15f, 0.42f, 0.9f),
                () => NetsLaunch.Open(NetsLaunch.Kind.Bowling, mine, opp, venue), 52);
        }

        /// <summary>The tournament hub over the home page (your quick-match team and overs to start one).</summary>
        private void OpenTournament()
        {
            var (mine, _) = MatchMenu.SavedTeams(_data);
            TournamentHub.Open(_data, mine, PlayerPrefs.GetInt(MatchMenu.OversKey, 5));
        }
    }
}
