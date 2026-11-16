using System;
using System.Linq;
using Apl.Client.Match;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Apl.Client.Menu
{
    /// <summary>
    /// QUICK MATCH (owner v26): TEAMS — your side on the left, the opponent on the
    /// right, each with its own carousel, the ground and overs between, PLAY
    /// (MatchMenu.Teams.cs) — and MATCH OPTIONS, the old MATCH SETUP page — the ground with its picture,
    /// overs, commentary, controls, running (MatchMenu.Setup.cs) — then PLAY.
    /// Last season's squads; they bat first and you chase, unless the toss says otherwise.
    /// </summary>
    public sealed partial class MatchMenu : MonoBehaviour
    {
        public event Action? Back;

        public const string MineKey = "apl.matchTeam", OppKey = "apl.matchOpp", OversKey = "apl.matchOvers", VenueKey = "apl.matchVenue";
        private static readonly int[] OverChoices = { 2, 5, 10, 20 };

        private Dataset _data = null!;
        private RectTransform _root = null!, _page = null!;
        private string _mine = "", _opp = "", _venue = "";
        private int _overs;

        public static MatchMenu Create(Dataset data)
        {
            var canvas = UiKit.Canvas("MatchMenu", 20);
            var menu = canvas.gameObject.AddComponent<MatchMenu>();
            menu._data = data;
            menu._root = (RectTransform)canvas.transform;
            var (mine, opp) = SavedTeams(data);
            menu._mine = mine;
            menu._opp = opp;
            menu._overs = PlayerPrefs.GetInt(OversKey, 2);
            menu._venue = PlayerPrefs.GetString(VenueKey, "");
            if (Venue.Find(menu._venue) == null) menu._venue = Venue.HomeOf(menu._mine).Id;
            menu.ShowTeams();
            return menu;
        }

        /// <summary>The last picked pair (your team, the opponent), never the same side twice.</summary>
        public static (string mine, string opp) SavedTeams(Dataset data)
        {
            var mine = PlayerPrefs.GetString(MineKey, data.Franchises[0].Id);
            if (data.Franchises.All(f => f.Id != mine)) mine = data.Franchises[0].Id;
            var opp = PlayerPrefs.GetString(OppKey, data.Franchises[1].Id);
            if (opp == mine || data.Franchises.All(f => f.Id != opp)) opp = data.Franchises.First(f => f.Id != mine).Id;
            return (mine, opp);
        }

        private Franchise Team(string id) => _data.Franchises.First(f => f.Id == id);

        private RectTransform NewPage()
        {
            if (_page != null) Destroy(_page.gameObject);
            _page = PanelKit.Backdrop(_root, 0.86f); // dark enough that the hall's banners don't read through the glass
            return _page;
        }

        private void Close()
        {
            Back?.Invoke();
            Destroy(gameObject);
        }

        /// <summary>A side's rating: its last-season XI's mean card rating.</summary>
        private int Rating(string teamId) => PlayerCardKit.TeamRating(QuickMatch.LastSeasonXI(_data, teamId).XI);

        /// <summary>A small "◄ BACK" pill, top left.</summary>
        private void BackButton(RectTransform page, Action act) =>
            PanelKit.Pill(page, "Back", new Vector2(0, 1), new Vector2(150, -70), new Vector2(220, 120), "◄ BACK", new Color(0.12f, 0.14f, 0.22f, 0.95f), act, 32);

        private void Play()
        {
            TournamentLaunch.Clear();
            PlayerPrefs.SetString(MineKey, _mine);
            PlayerPrefs.SetString(OppKey, _opp);
            PlayerPrefs.SetInt(OversKey, _overs);
            PlayerPrefs.SetString(VenueKey, _venue);
            PlayerPrefs.Save();
            MatchLaunch.VenueId = _venue;
            MatchLaunch.MyTeam = _mine;
            MatchLaunch.Opponent = _opp;
            MatchLaunch.Overs = _overs;
            SceneManager.LoadScene(MatchLaunch.GroundScene);
        }
    }
}
