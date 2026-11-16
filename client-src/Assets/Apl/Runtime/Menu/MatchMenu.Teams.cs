using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The QUICK MATCH team pick (owner v26, his 7.jpg LAYOUT in our own art — no stat bars):
    /// YOUR TEAM panel on the left and OPPONENT on the right, each with a playing-XI button,
    /// a big crest, the name and team rating, and a carousel (‹ neighbour · picked · neighbour ›)
    /// to change it; VS, the ground and the overs (‹ 20 OVERS ›) in the middle; PLAY bottom
    /// right; MATCH OPTIONS (the old setup page: ground picture, commentary, controls,
    /// difficulty) bottom centre. Neither carousel ever offers the other side's team.
    /// Button names for AutoShot: Next (= MATCH OPTIONS), Play, OversPrev / OversNext,
    /// Xi_Mine / Xi_Opp, Car_Mine_Prev / _Next, Car_Opp_Prev / _Next.
    /// </summary>
    public sealed partial class MatchMenu
    {
        private static readonly Color Ink2 = new Color(0.12f, 0.14f, 0.22f, 0.95f);

        private void ShowTeams()
        {
            var page = NewPage();
            var size = MainMenu.PageSize();
            BackButton(page, Close);
            var title = PlayerCardKit.Text(page, new Vector2(0, 1), new Vector2(0, 1), new Vector2(290, -112), new Vector2(1000, -28), "QUICK MATCH", 60, TextAnchor.MiddleLeft, Color.white, condensed: true);
            title.fontStyle = FontStyle.Italic;

            // The two panels sit at ±27% of the width: the middle widens on wide phones.
            var px = Mathf.Round(size.x * 0.27f);
            var panel = new Vector2(Mathf.Min(600f, size.x * 0.3f), 760f);
            const float py = 10f;
            SidePanel(page, false, new Vector2(-px, py), panel);
            SidePanel(page, true, new Vector2(px, py), panel);

            // The middle: VS, the ground, the overs (level with the carousels).
            var vs = PlayerCardKit.Shape(page, "Vs", CardSprites.Disc(), new Color(0.06f, 0.05f, 0.14f, 0.97f), new Vector2(0, py + 210), new Vector2(150, 150));
            PlayerCardKit.Shape((RectTransform)vs.transform, "Ring", CardSprites.Ring(0.08f), UiKit.Gold, Vector2.zero, new Vector2(150, 150));
            PlayerCardKit.Text((RectTransform)vs.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, "VS", 70, TextAnchor.MiddleCenter, Color.white, condensed: true).fontStyle = FontStyle.Italic;
            var midW = Mathf.Min(460f, px * 2f - panel.x - 40f);
            GroundChip(page, new Vector2(0, py - 20), midW);
            OversPicker(page, new Vector2(0, py - panel.y / 2f + 100f), midW);

            PanelKit.Pill(page, "Next", new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(380, 120), "MATCH OPTIONS", Ink2, ShowSetup, 34);
            PanelKit.Pill(page, "Play", new Vector2(1, 0), new Vector2(-200, 90), new Vector2(320, 130), "PLAY ►", UiKit.Accent, Play, 54);
        }

        /// <summary>The ids a side may pick from: every team but the other side's.</summary>
        private List<string> Choices(bool opp) => _data.Franchises.Select(f => f.Id).Where(id => id != (opp ? _mine : _opp)).ToList();

        private void SetSide(bool opp, string id)
        {
            if (opp) _opp = id;
            else
            {
                _mine = id;
                _venue = Venue.HomeOf(id).Id;
            }
            ShowTeams();
        }

        /// <summary>One side: heading, XI button, the crest in its glow, name and rating, the carousel.</summary>
        private void SidePanel(RectTransform page, bool opp, Vector2 pos, Vector2 size)
        {
            var id = opp ? _opp : _mine;
            var f = Team(id);
            var colour = PlayerCardKit.Bright(UiKit.ParseHex(f.PrimaryColor));
            PlayerCardKit.Shape(page, "Glow" + (opp ? "Opp" : "Mine"), CardSprites.Glow(24, 30), UiKit.WithAlpha(colour, 0.35f), pos, size + new Vector2(60, 60));
            var panel = Solid(PanelKit.GlassPanel(page, opp ? "SideOpp" : "SideMine", new Vector2(0.5f, 0.5f), pos, size));
            PlayerCardKit.Shape(panel, "Band", CardSprites.Round(6), colour, new Vector2(0, -8), new Vector2(size.x - 60, 8), new Vector2(0.5f, 1));
            var tl = new Vector2(0, 1);
            // Heading on the inner side, the XI button on the outer corner (as 7.jpg).
            PlayerCardKit.Text(panel, tl, new Vector2(1, 1), new Vector2(opp ? 36 : 130, -96), new Vector2(opp ? -130 : -36, -26), opp ? "OPPONENT" : "YOUR TEAM", 40, opp ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, opp ? colour : YouMark.Lime, condensed: true).fontStyle = FontStyle.Italic;
            XiButton(panel, new Vector2(opp ? 1 : 0, 1), new Vector2(opp ? -72 : 72, -62), id, !opp);

            // The crest on a soft disc of its colour.
            PlayerCardKit.Shape(panel, "CrestGlow", CardSprites.Glow(60, 90), UiKit.WithAlpha(colour, 0.4f), new Vector2(0, -260), new Vector2(380, 380), new Vector2(0.5f, 1));
            UiKit.Crest(panel, f.Id, new Vector2(0.5f, 1), new Vector2(0, -258), 260);
            PlayerCardKit.Text(panel, tl, new Vector2(1, 1), new Vector2(24, -470), new Vector2(-24, -404), f.Name.ToUpperInvariant(), 50, TextAnchor.MiddleCenter, Color.white, condensed: true);
            var chip = PlayerCardKit.Shape(panel, "Rating", CardSprites.Round(18), new Color(0.03f, 0.03f, 0.08f, 0.85f), new Vector2(0, -502), new Vector2(300, 46), new Vector2(0.5f, 1));
            var cr = (RectTransform)chip.transform;
            PlayerCardKit.Shape(cr, "Star", Apl.Client.Modes.RewardFx.Star, UiKit.Gold, new Vector2(26, 0), new Vector2(32, 32), new Vector2(0, 0.5f));
            PlayerCardKit.Text(cr, Vector2.zero, Vector2.one, new Vector2(48, 0), new Vector2(-12, 0), $"TEAM RATING  {Rating(f.Id)}", 28, TextAnchor.MiddleCenter, UiKit.Gold, condensed: true);
            Carousel(panel, opp, size.x);
        }

        /// <summary>‹ prev · the neighbour before · the picked one (gold ring) · the neighbour after · next ›.</summary>
        private void Carousel(RectTransform panel, bool opp, float width)
        {
            var list = Choices(opp);
            var id = opp ? _opp : _mine;
            var at = Mathf.Max(0, list.IndexOf(id));
            string Near(int by) => list[((at + by) % list.Count + list.Count) % list.Count];
            var tag = opp ? "Opp" : "Mine";
            var strip = PlayerCardKit.Shape(panel, "Carousel", CardSprites.Round(26), new Color(0.02f, 0.02f, 0.06f, 0.9f), new Vector2(0, 100), new Vector2(width - 40, 160), new Vector2(0.5f, 0));
            var sr = (RectTransform)strip.transform;
            Arrow(sr, "Car_" + tag + "_Prev", -1, () => SetSide(opp, Near(-1)));
            Arrow(sr, "Car_" + tag + "_Next", 1, () => SetSide(opp, Near(1)));
            if (list.Count > 2) Tile(sr, Near(-1), new Vector2(-130, 0), 100, false, () => SetSide(opp, Near(-1)));
            if (list.Count > 1) Tile(sr, Near(1), new Vector2(130, 0), 100, false, () => SetSide(opp, Near(1)));
            Tile(sr, id, Vector2.zero, 140, true, null);
        }

        private static void Arrow(RectTransform strip, string name, int side, Action act)
        {
            var b = PlayerCardKit.Shape(strip, name, CardSprites.Round(18), new Color(0.12f, 0.13f, 0.24f, 0.95f), new Vector2(side * -44, 0), new Vector2(64, 120), new Vector2(side < 0 ? 0 : 1, 0.5f));
            b.raycastTarget = true;
            b.gameObject.AddComponent<Button>().onClick.AddListener(() => act());
            PlayerCardKit.Text((RectTransform)b.transform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, 4), side < 0 ? "‹" : "›", 72, TextAnchor.MiddleCenter, Color.white, condensed: true);
        }

        /// <summary>A crest tile in the carousel: its colour darkened, the crest, the short name; the picked one ringed in gold.</summary>
        private void Tile(RectTransform strip, string id, Vector2 pos, float s, bool picked, Action? act)
        {
            var f = Team(id);
            var colour = UiKit.ParseHex(f.PrimaryColor);
            if (picked) PlayerCardKit.Shape(strip, "Ring_" + id, CardSprites.Round(22), UiKit.Gold, pos, new Vector2(s + 10, s + 10));
            var t = PlayerCardKit.Shape(strip, "Tab_" + id, CardSprites.Round(18), picked ? colour : Color.Lerp(colour, Color.black, 0.45f), pos, new Vector2(s, s));
            var tr = (RectTransform)t.transform;
            UiKit.Crest(tr, id, new Vector2(0.5f, 0.5f), new Vector2(0, s * 0.1f), s * 0.66f);
            PlayerCardKit.Text(tr, Vector2.zero, new Vector2(1, 0), new Vector2(2, 2), new Vector2(-2, s * 0.26f), f.ShortName, picked ? 24 : 20, TextAnchor.MiddleCenter, colour.grayscale > 0.55f && picked ? UiKit.Ink : Color.white, condensed: true);
            if (act == null) return;
            t.raycastTarget = true;
            t.gameObject.AddComponent<Button>().onClick.AddListener(() => act());
        }

        /// <summary>The playing-XI button: a round blue badge, "XI" under a player glyph.</summary>
        private void XiButton(RectTransform panel, Vector2 anchor, Vector2 pos, string teamId, bool mine)
        {
            var disc = PlayerCardKit.Shape(panel, mine ? "Xi_Mine" : "Xi_Opp", CardSprites.Disc(), new Color(0.2f, 0.45f, 0.95f), pos, new Vector2(92, 92), anchor);
            disc.raycastTarget = true;
            disc.gameObject.AddComponent<Button>().onClick.AddListener(() => OpenXi(teamId, mine));
            var dr = (RectTransform)disc.transform;
            PlayerCardKit.Shape(dr, "Ring", CardSprites.Ring(0.07f), new Color(1, 1, 1, 0.85f), Vector2.zero, new Vector2(92, 92));
            PlayerCardKit.Shape(dr, "Head", CardSprites.Disc(), Color.white, new Vector2(0, 18), new Vector2(22, 22));
            PlayerCardKit.Text(dr, Vector2.zero, Vector2.one, new Vector2(0, 8), new Vector2(0, -36), "XI", 30, TextAnchor.MiddleCenter, Color.white, condensed: true);
        }

        /// <summary>The XI button: your side's PLAYING XI picker; the opponent's XI read-only.</summary>
        private void OpenXi(string teamId, bool mine)
        {
            if (mine)
            {
                // Your side: the PLAYING XI picker (Menu/PlayingXiScreen.cs, agent D) over this page; its CONFIRM
                // stores the XI (XiChoice) for the match and comes back here, as does its BACK.
                PlayingXiScreen.OpenQuickMatch(_data, _mine, _opp, ShowTeams, ShowTeams);
                return;
            }
            // The opponent's side: its XI read-only (the AI picks it).
            var team = Team(teamId);
            var xi = QuickMatch.LastSeasonXI(_data, teamId).XI;
            var dim = PanelKit.Backdrop(_page, 0.86f);
            dim.gameObject.AddComponent<Button>(); // swallow taps on the page behind
            var panel = Solid(PanelKit.GlassPanel(dim, "XiPanel", new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(1500, 880)));
            UiKit.Crest(panel, teamId, new Vector2(0, 1), new Vector2(90, -70), 90);
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(150, -110), new Vector2(-200, -30), $"PLAYING XI  ·  {team.Name.ToUpperInvariant()}", 46, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            PanelKit.Pill(panel, "XiClose", new Vector2(1, 1), new Vector2(-110, -70), new Vector2(160, 120), "X", Ink2, () => Destroy(dim.gameObject), 40);
            var rows = new[] { 6, 5 };
            var i = 0;
            for (var r = 0; r < rows.Length && i < xi.Count; r++)
            for (var c = 0; c < rows[r] && i < xi.Count; c++, i++)
                PlayerCardKit.Card(panel, xi[i], team, new Vector2(0.5f, 0.5f), new Vector2((c - (rows[r] - 1) / 2f) * 210f, 90 - r * 300f), new Vector2(186, 254), captain: i == 0, delay: 0.03f * i).Motion.FromAlpha = 1f;
        }

        /// <summary>‹ 20 OVERS › — steps through the overs choices.</summary>
        private void OversPicker(RectTransform page, Vector2 pos, float width)
        {
            var at = Mathf.Max(0, Array.IndexOf(OverChoices, _overs));
            _overs = OverChoices[at];
            var bar = PlayerCardKit.Shape(page, "Overs", CardSprites.Round(30), new Color(0.02f, 0.02f, 0.06f, 0.92f), pos, new Vector2(width, 120));
            var br = (RectTransform)bar.transform;
            void Step(int by)
            {
                _overs = OverChoices[Mathf.Clamp(at + by, 0, OverChoices.Length - 1)];
                ShowTeams();
            }
            PlayerCardKit.Text(br, Vector2.zero, Vector2.one, new Vector2(80, 0), new Vector2(-80, 0), $"{_overs} OVERS", 46, TextAnchor.MiddleCenter, UiKit.Gold, condensed: true);
            StepButton(br, "OversPrev", -1, at > 0, () => Step(-1));
            StepButton(br, "OversNext", 1, at < OverChoices.Length - 1, () => Step(1));
        }

        /// <summary>The ground in the middle: GROUND, its name and city (HOME / AWAY), ‹ › to change.</summary>
        private void GroundChip(RectTransform page, Vector2 pos, float width)
        {
            var venue = Venue.Find(_venue) ?? Venue.All[0];
            var box = PlayerCardKit.Shape(page, "Ground", CardSprites.Round(26), new Color(0.02f, 0.02f, 0.06f, 0.85f), pos, new Vector2(width, 170));
            var br = (RectTransform)box.transform;
            PlayerCardKit.Text(br, new Vector2(0, 1), new Vector2(1, 1), new Vector2(70, -48), new Vector2(-70, -10), "GROUND", 24, TextAnchor.MiddleCenter, UiKit.Dim, condensed: true);
            PlayerCardKit.Text(br, new Vector2(0, 0), new Vector2(1, 1), new Vector2(76, 50), new Vector2(-76, -46), venue.Name.ToUpperInvariant(), 36, TextAnchor.MiddleCenter, Color.white, condensed: true);
            var tag = venue.Home == _mine ? "  ·  HOME" : venue.Home == _opp ? "  ·  AWAY" : "";
            PlayerCardKit.Text(br, new Vector2(0, 0), new Vector2(1, 0), new Vector2(76, 14), new Vector2(-76, 50), venue.City.ToUpperInvariant() + tag, 22, TextAnchor.MiddleCenter, UiKit.Gold, condensed: true);
            var index = Venue.All.ToList().IndexOf(venue);
            void Step(int by)
            {
                var n = Venue.All.Count;
                _venue = Venue.All[((index + by) % n + n) % n].Id;
                ShowTeams();
            }
            StepButton(br, "GroundPrev", -1, true, () => Step(-1));
            StepButton(br, "GroundStep", 1, true, () => Step(1));
        }

        private static void StepButton(RectTransform bar, string name, int side, bool on, Action act)
        {
            var b = PlayerCardKit.Shape(bar, name, CardSprites.Round(20), on ? new Color(0.14f, 0.15f, 0.28f, 0.95f) : new Color(0.08f, 0.08f, 0.14f, 0.6f), new Vector2(side * -40, 0), new Vector2(64, 100), new Vector2(side < 0 ? 0 : 1, 0.5f));
            PlayerCardKit.Text((RectTransform)b.transform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, 4), side < 0 ? "‹" : "›", 64, TextAnchor.MiddleCenter, on ? Color.white : new Color(1, 1, 1, 0.25f), condensed: true);
            if (!on) return;
            b.raycastTarget = true;
            b.gameObject.AddComponent<Button>().onClick.AddListener(() => act());
        }

        /// <summary>A glass panel made near-opaque so the hall behind (crests, banners) doesn't bleed through.</summary>
        private static RectTransform Solid(RectTransform panel)
        {
            panel.GetComponent<Image>().color = UiKit.WithAlpha(PanelKit.Glass, 0.96f);
            return panel;
        }
    }
}
