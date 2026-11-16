using System;
using System.Linq;
using Apl.Client.Audio;
using Apl.Client.Match;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The MATCH SETUP page: the ground on the left — its picture (Resources/Ui/
    /// Stadiums/&lt;venue&gt;), name, city, character, ◄ ► to change — and on the
    /// right OVERS, DIFFICULTY and SOUND &amp; GAMEPLAY SETTINGS (Menu/GameSettings.cs); then PLAY.
    /// </summary>
    public sealed partial class MatchMenu
    {
        private void ShowSetup()
        {
            var page = NewPage();
            PanelKit.Header(page, "MATCH SETUP", $"{Team(_mine).Name.ToUpperInvariant()}  v  {Team(_opp).Name.ToUpperInvariant()}", Team(_mine), Team(_opp));
            BackButton(page, ShowTeams);
            GroundPanel(page);
            var col = Solid(PanelKit.GlassPanel(page, "Options", new Vector2(0.5f, 0.5f), new Vector2(520, 30), new Vector2(760, 740)));
            var y = -50f;
            MenuChoice.Row(col, ref y, "OVERS", OverChoices.Select(o => o.ToString()).ToArray(), () => Array.IndexOf(OverChoices, _overs), i => _overs = OverChoices[i]);
            // AI strength, timing window and ball speed (Modes/Difficulty.cs), here too so it's set per match.
            MenuChoice.Row(col, ref y, "DIFFICULTY", Apl.Client.Modes.Difficulty.Labels, () => (int)Apl.Client.Modes.Difficulty.Current, i => Apl.Client.Modes.Difficulty.Current = (Apl.Client.Modes.DifficultyLevel)i);
            // Everything else in the one organised place (owner v26 #14: no settings scattered over screens).
            PanelKit.Pill(col, "SetupSettings", new Vector2(0.5f, 1), new Vector2(0, y - 80), new Vector2(640, 120), "SOUND & GAMEPLAY SETTINGS ►", GameSettings.ListFill,
                () => GameSettings.Open(ShowSetup), 30);
            PanelKit.Pill(page, "Play", new Vector2(1, 0), new Vector2(-190, 90), new Vector2(300, 130), "PLAY ►", UiKit.Accent, Play, 50);
            // The bat sound's CC BY credit is in SETTINGS' credits line now (MainMenu.HomePopups.cs), not on this screen.
        }

        /// <summary>The ground: its picture (or its home side's colours and crest), name, city, home / away, character.</summary>
        private void GroundPanel(RectTransform page)
        {
            var venue = Venue.Find(_venue) ?? Venue.All[0];
            var panel = Solid(PanelKit.GlassPanel(page, "Ground", new Vector2(0.5f, 0.5f), new Vector2(-370, 30), new Vector2(940, 740)));
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -70), new Vector2(-30, -16), "GROUND", 30, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            // The picture, 16:9, in a rounded frame.
            var frame = PlayerCardKit.Shape(panel, "Frame", CardSprites.Round(18), Color.white, new Vector2(0, 40), new Vector2(672, 378));
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var pic = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
            var pr = (RectTransform)pic.transform;
            pr.SetParent(frame.transform, false);
            pr.anchorMin = Vector2.zero;
            pr.anchorMax = Vector2.one;
            pr.offsetMin = pr.offsetMax = Vector2.zero;
            var raw = pic.GetComponent<RawImage>();
            raw.raycastTarget = false;
            var tex = Resources.Load<Texture2D>("Ui/Stadiums/" + venue.Id);
            if (tex != null)
            {
                raw.texture = tex;
                // Cropped in on the bowl: the renders' empty night sky round the edges read as a black void.
                raw.uvRect = new Rect(0.1f, 0.12f, 0.8f, 0.8f);
            }
            else
            {
                // No picture yet: the home side's colour with its crest.
                var home = _data.Franchises.FirstOrDefault(f => f.Id == venue.Home);
                raw.color = home != null ? Color.Lerp(UiKit.ParseHex(home.PrimaryColor), Color.black, 0.45f) : new Color(0.1f, 0.25f, 0.12f);
                if (home != null) UiKit.Crest(pr, home.Id, new Vector2(0.5f, 0.5f), Vector2.zero, 200);
            }
            var tag = venue.Home == _mine ? "  ·  HOME" : venue.Home == _opp ? "  ·  AWAY" : "";
            PlayerCardKit.Text(panel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(150, 92), new Vector2(-150, 150), venue.Name.ToUpperInvariant(), 44, TextAnchor.MiddleCenter, Color.white, condensed: true);
            PlayerCardKit.Text(panel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(150, 56), new Vector2(-150, 92), venue.City.ToUpperInvariant() + tag, 24, TextAnchor.MiddleCenter, UiKit.Gold, condensed: true);
            PlayerCardKit.Text(panel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(150, 18), new Vector2(-150, 56), venue.Character, 20, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            var index = Venue.All.ToList().IndexOf(venue);
            void Step(int by)
            {
                var n = Venue.All.Count;
                _venue = Venue.All[((index + by) % n + n) % n].Id;
                ShowSetup();
            }
            PanelKit.Pill(panel, "Prev", new Vector2(0, 0), new Vector2(80, 90), new Vector2(120, 120), "◄", new Color(0.12f, 0.14f, 0.22f, 0.95f), () => Step(-1), 44);
            PanelKit.Pill(panel, "Next", new Vector2(1, 0), new Vector2(-80, 90), new Vector2(120, 120), "►", new Color(0.12f, 0.14f, 0.22f, 0.95f), () => Step(1), 44);
        }
    }
}
