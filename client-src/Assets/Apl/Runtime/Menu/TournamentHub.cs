using System;
using System.Linq;
using Apl.Client.Match;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The tournament's home: start one (your team, last season's squads or
    /// the last auction's, how many overs), then the points table with NRR,
    /// fixtures and results, and NEXT MATCH — the other games before yours are
    /// simulated, yours opens on the ground. Opened from the match menu, from
    /// the auction's results, and on the way back from a tournament match.
    /// </summary>
    public sealed partial class TournamentHub : MonoBehaviour
    {
        public event Action? Back;

        private Dataset _data = null!;
        private RectTransform _root = null!, _page = null!;
        private Tournament? _t;

        // The new-tournament choices.
        private string _team = "";
        private int _overs = 5;
        private bool _auction;
        /// <summary>AUCTION: run a new one first (true), or the last one's squads.</summary>
        private bool _newAuction = true;
        private Tournament.AuctionSquads? _lastAuction;
        private static readonly int[] OverChoices = { 2, 5, 10, 20 };

        private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// The hub over whatever is open. <paramref name="team"/> / <paramref name="overs"/>
        /// pre-fill a new tournament; <paramref name="fromAuction"/> starts one on those
        /// auction squads (asking first if one is under way).
        /// </summary>
        /// <param name="auctionFirst">Home's AUCTION ► TOURNAMENT: AUCTION and NEW AUCTION FIRST chosen on the setup page.</param>
        public static TournamentHub Open(Dataset data, string team, int overs, Tournament.AuctionSquads? fromAuction = null, bool auctionFirst = false)
        {
            var canvas = UiKit.Canvas("TournamentHub", 40);
            var hub = canvas.gameObject.AddComponent<TournamentHub>();
            hub._data = data;
            hub._root = (RectTransform)canvas.transform;
            hub._team = team;
            hub._overs = OverChoices.Contains(overs) ? overs : 5;
            hub._lastAuction = fromAuction ?? Tournament.LoadAuction(data);
            hub._auction = fromAuction != null;
            hub._newAuction = fromAuction == null;
            if (fromAuction != null) hub._team = fromAuction.Team;
            // A fresh visit: an AUCTION asked for before and not started is dropped (owner v26 #6).
            TournamentAuction.Cancel();
            if (auctionFirst && fromAuction == null)
            {
                hub._auction = true;
                hub._newAuction = true;
            }
            hub._t = Tournament.Load(data);
            // A tournament just won or lost in its last match (the Final on the ground): the champions page, once.
            if (hub._t != null && fromAuction == null && !auctionFirst) { if (hub._t.Finished && !hub._t.ChampionShown) hub.ShowChampions(); else hub.ShowTable(); }
            else hub.ShowSetup();
            return hub;
        }

        private RectTransform NewPage()
        {
            // Opaque, and the old page hidden at once (round 11: the hub showed the home page through it).
            if (_page != null)
            {
                _page.gameObject.SetActive(false);
                Destroy(_page.gameObject);
            }
            _page = UiKit.Stretch(_root, "Page", new Color(0.02f, 0.03f, 0.07f, 1f));
            return _page;
        }

        private Franchise Team(string id) => _data.Franchises.First(f => f.Id == id);

        private void Close()
        {
            Back?.Invoke();
            Destroy(gameObject);
        }

        // -------------------------------------------------------------- setup

        private void ShowSetup()
        {
            var page = NewPage();
            UiKit.LabelAt(page, new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(1500, 76), "TOURNAMENT", 56, TextAnchor.MiddleCenter, UiKit.Gold);
            UiKit.LabelAt(page, new Vector2(0.5f, 1), new Vector2(0, -140), new Vector2(1500, 40),
                "Every side plays every other once; the top four play off. Play your matches or sim them; the rest are simulated.", 26, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            UiKit.Btn(page, "HubBack", new Vector2(0, 1), new Vector2(150, -80), new Vector2(200, 80), "◄ BACK", UiKit.Muted, () =>
            {
                if (_t != null) ShowTable();
                else Close();
            }, 30);

            // Squads (owner v26 #6): last season's, or an AUCTION — a new one first (then the league on the
            // squads everyone bought: Menu/TournamentAuction.cs), or the last auction's squads when there is one.
            UiKit.LabelAt(page, Centre, new Vector2(0, 335), new Vector2(900, 36), "SQUADS", 28, TextAnchor.MiddleCenter, UiKit.Dim);
            UiKit.Btn(page, "SrcSeason", Centre, new Vector2(-260, 272), new Vector2(480, 80), "LAST SEASON'S SQUADS", _auction ? UiKit.Muted : UiKit.Accent, () => { _auction = false; ShowSetup(); }, 28);
            UiKit.Btn(page, "SrcAuction", Centre, new Vector2(260, 272), new Vector2(480, 80), "AUCTION", _auction ? UiKit.Accent : UiKit.Muted, () =>
            {
                _auction = true;
                if (_lastAuction == null) _newAuction = true;
                if (!_newAuction && _lastAuction != null) _team = _lastAuction.Team;
                ShowSetup();
            }, 28);
            var fixedTeam = _auction && !_newAuction;
            if (_auction)
            {
                UiKit.Btn(page, "AuctionNew", Centre, new Vector2(_lastAuction != null ? 105 : 260, 205), new Vector2(_lastAuction != null ? 300 : 480, 50),
                    "NEW AUCTION FIRST", _newAuction ? UiKit.Gold : UiKit.Muted, () => { _newAuction = true; ShowSetup(); }, 22);
                if (_lastAuction != null)
                    UiKit.Btn(page, "AuctionLast", Centre, new Vector2(415, 205), new Vector2(300, 50),
                        $"LAST AUCTION · {Team(_lastAuction.Team).ShortName}", _newAuction ? UiKit.Muted : UiKit.Gold, () =>
                        {
                            _newAuction = false;
                            _team = _lastAuction!.Team;
                            ShowSetup();
                        }, 22);
            }

            // Your team (the last auction's is fixed: it's the side you bought; a new auction: the side you'll bid as).
            UiKit.LabelAt(page, Centre, new Vector2(0, 155), new Vector2(1400, 36),
                fixedTeam ? "YOUR TEAM · the side you bought" : _auction ? "YOUR TEAM · the side you'll bid as" : "YOUR TEAM", 26, TextAnchor.MiddleCenter, UiKit.Dim);
            for (var i = 0; i < _data.Franchises.Count; i++)
            {
                var f = _data.Franchises[i];
                var on = f.Id == _team;
                var colour = UiKit.WithAlpha(UiKit.ParseHex(f.PrimaryColor), on ? 1f : 0.7f);
                var tile = UiKit.Box(page, "Team_" + f.Id, Centre, new Vector2((i - 4.5f) * 160, 60), new Vector2(148, 150), colour);
                var id = f.Id;
                if (!fixedTeam) tile.gameObject.AddComponent<Button>().onClick.AddListener(() => { _team = id; ShowSetup(); });
                UiKit.Crest(tile, f.Id, new Vector2(0.5f, 1), new Vector2(0, -56), 86);
                UiKit.LabelAt(tile, new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(140, 30), f.ShortName, 24, TextAnchor.MiddleCenter, colour.grayscale > 0.55f ? UiKit.Ink : Color.white);
                if (on) YouMark.Add(tile, "YOU", 7);
            }

            UiKit.LabelAt(page, Centre, new Vector2(0, -80), new Vector2(700, 36), "OVERS A SIDE", 28, TextAnchor.MiddleCenter, UiKit.Dim);
            for (var i = 0; i < OverChoices.Length; i++)
            {
                var o = OverChoices[i];
                UiKit.Btn(page, "Overs_" + o, Centre, new Vector2((i - 1.5f) * 170, -150), new Vector2(150, 90), o.ToString(), o == _overs ? UiKit.Accent : UiKit.Muted, () => { _overs = o; ShowSetup(); }, 40);
            }
            if (_t != null && _t.Finished)
            {
                // Finished (auction-r1 #19: it said "under way (49 of 49 played)"): who won, and VIEW.
                UiKit.LabelAt(page, Centre, new Vector2(0, -250), new Vector2(1400, 36),
                    $"Last tournament: {Team(_t.Champion).Name} were the champions · your side: {_t.UserFinish().ToLowerInvariant()}.", 24, TextAnchor.MiddleCenter, UiKit.Gold, bold: false);
                UiKit.Btn(page, "ViewLast", new Vector2(0.5f, 0), new Vector2(-340, 100), new Vector2(560, 112), "VIEW LAST TOURNAMENT", UiKit.Muted, ShowChampions, 32);
            }
            else if (_t != null)
            {
                UiKit.LabelAt(page, Centre, new Vector2(0, -250), new Vector2(1400, 36),
                    $"A tournament is under way ({_t.Fixtures.Count(f => f.Played)} of {_t.Fixtures.Count} played). Starting a new one replaces it.", 24, TextAnchor.MiddleCenter, UiKit.Bad, bold: false);
                UiKit.Btn(page, "Resume", new Vector2(0.5f, 0), new Vector2(-340, 100), new Vector2(560, 112), "CONTINUE CURRENT", UiKit.Muted, ShowTable, 36);
            }
            var go = _auction && _newAuction ? "GO TO THE AUCTION ►" : "START TOURNAMENT ►";
            UiKit.Btn(page, "Start", new Vector2(0.5f, 0), new Vector2(_t != null ? 340 : 0, 100), new Vector2(560, 112), go, UiKit.Accent, StartNew, 38);
        }

        private void StartNew()
        {
            if (_auction && (_newAuction || _lastAuction == null))
            {
                // The auction first; its results start the tournament (owner v26 #6).
                var team = _team;
                var overs = _overs;
                Close();
                TournamentAuction.Request(team, overs);
                return;
            }
            var squads = _auction && _lastAuction != null ? _lastAuction.Squads : Tournament.SeasonSquads(_data);
            if (!squads.ContainsKey(_team)) _team = squads.Keys.First();
            _t = Tournament.Create(_data, squads, _team, _overs, _auction ? "auction" : "season", DateTime.Now.Ticks & 0x7FFFFFFF,
                _auction && _lastAuction != null ? _lastAuction.Id : "");
            _t.Save();
            ShowTable();
        }
    }
}
