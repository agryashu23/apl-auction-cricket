using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>One batter's innings for the wagon explorer: who, which side, which innings, his shots and his line.</summary>
    public sealed class WagonBook
    {
        public string Id = "", Name = "", Team = "", Line = "";
        public bool LeftHanded;
        public int Innings = 1;
        public List<WagonShot> Shots = new List<WagonShot>();
    }

    /// <summary>
    /// The wagon wheel explorer (pause menu → WAGON WHEEL; the scorecard's): the wheel on the
    /// left (the perspective ground, MatchHud.Wagon.cs), and on the right the INNINGS tabs and the batters of that innings — ALL (the
    /// whole side's shots on one ground) or one man — tapping one redraws the wheel.
    /// </summary>
    public sealed partial class MatchHud
    {
        /// <summary>Every batter's shots so far, both innings (the director's book: MatchDirector.Wagon.cs).</summary>
        public Func<IReadOnlyList<WagonBook>>? WagonBooks;

        /// <summary>The explorer over the pause menu (or the HUD); false if there's nothing to show.</summary>
        public bool ShowWagonExplorer(Action? closed = null)
        {
            var books = WagonBooks?.Invoke();
            if (books == null || books.Count == 0) return false;
            var innings = books.Select(b => b.Innings).Distinct().OrderBy(i => i).ToList();
            var inns = innings[innings.Count - 1];
            string? who = null;
            var parent = _pauseMenu != null ? (RectTransform)_pauseMenu.transform : _root;
            if (_wagon != null) Destroy(_wagon);
            var page = UiKit.Stretch(parent, "WagonExplorer", new Color(0.02f, 0.03f, 0.07f, 0.98f));
            _wagon = page.gameObject;
            RectTransform? wheel = null, side = null;

            void Draw()
            {
                if (wheel != null) Destroy(wheel.gameObject);
                if (side != null) Destroy(side.gameObject);
                var these = books.Where(b => b.Innings == inns).ToList();
                var pick = these.FirstOrDefault(b => b.Id == who);
                var shots = pick != null ? pick.Shots : these.SelectMany(b => b.Shots).ToList();
                var team = these.Count > 0 ? these[0].Team : "";
                var title = pick != null ? pick.Name.ToUpperInvariant() : $"{team}  ·  {(inns == 1 ? "1ST" : "2ND")} INNINGS";
                var line = pick != null ? pick.Line : $"{shots.Sum(s => s.Runs)} RUNS IN SHOTS  ·  {shots.Count(s => s.Runs == 4)}×4  {shots.Count(s => s.Runs == 6)}×6";
                wheel = WagonWheel(page, new Vector2(0.5f, 0.5f), new Vector2(-340, 20), 800f, shots, pick?.LeftHanded ?? false, title, line);
                side = Filters(page, innings, inns, these, who, i => { inns = i; who = null; Draw(); }, id => { who = id; Draw(); });
            }
            Draw();
            PanelKit.Pill(page, "CloseWagon", new Vector2(0.5f, 0), new Vector2(560, 80), new Vector2(360, 120), "BACK", UiKit.Accent, () =>
            {
                if (_wagon != null) Destroy(_wagon);
                _wagon = null;
                closed?.Invoke();
            }, 36);
            return true;
        }

        /// <summary>The right-hand column: INNINGS tabs, then ALL and each batter of that innings, the chosen one gold.</summary>
        private static RectTransform Filters(RectTransform page, List<int> innings, int inns, List<WagonBook> these, string? who, Action<int> pickInnings, Action<string?> pickBatter)
        {
            var col = PanelKit.GlassPanel(page, "WagonFilters", new Vector2(0.5f, 0.5f), new Vector2(560, 60), new Vector2(640, 760));
            col.GetComponent<UnityEngine.UI.Image>().color = UiKit.WithAlpha(PanelKit.Glass, 0.96f);
            PlayerCardKit.Text(col, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -66), new Vector2(-30, -16), "INNINGS", 28, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            var dark = new Color(0.12f, 0.14f, 0.22f, 0.95f);
            for (var i = 0; i < 2; i++)
            {
                var n = i + 1;
                var has = innings.Contains(n);
                var b = PanelKit.Pill(col, "Inns" + n, new Vector2(0.5f, 1), new Vector2(-145 + i * 290, -130), new Vector2(270, 100), n == 1 ? "1ST INNINGS" : "2ND INNINGS", n == inns ? UiKit.Gold : dark, () => { if (has) pickInnings(n); }, 28);
                b.interactable = has;
            }
            PlayerCardKit.Text(col, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -240), new Vector2(-30, -190), "BATTER", 28, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            var rows = new List<(string? id, string label)> { (null, "ALL") };
            rows.AddRange(these.Select(b => ((string?)b.Id, ShortName(b.Name))));
            for (var i = 0; i < rows.Count && i < 12; i++)
            {
                var (id, label) = rows[i];
                var x = i % 2 == 0 ? -150f : 150f;
                var y = -300f - (i / 2) * 76f;
                var on = id == who;
                var chip = PlayerCardKit.Shape(col, "Batter" + i, CardSprites.Round(18), on ? UiKit.Gold : dark, new Vector2(x, y), new Vector2(280, 64), new Vector2(0.5f, 1));
                chip.raycastTarget = true;
                PlayerCardKit.Text((RectTransform)chip.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, label, 26, TextAnchor.MiddleCenter, on ? UiKit.Ink : Color.white, condensed: true);
                chip.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => pickBatter(id));
            }
            return col;
        }

        /// <summary>"Rohit Varma" → "R. VARMA" (fits a chip).</summary>
        private static string ShortName(string name)
        {
            var (first, last) = PlayerCardKit.Split(name);
            return (first.Length > 0 ? first[0] + ". " : "") + last.ToUpperInvariant();
        }
    }
}
