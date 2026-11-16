using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>The captains' intro card before the toss (bottom left, over his close-up), in the card kit.</summary>
    public sealed partial class PreMatchUi
    {
        private GameObject? _captain;

        /// <summary>His card and a glass strip: CAPTAIN · team, his name big, his role and style.</summary>
        public void ShowCaptain(Player p, Franchise team, bool mine)
        {
            HideCaptain();
            var root = (RectTransform)new GameObject("CaptainIntro", typeof(RectTransform)).transform;
            root.SetParent(_root, false);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.sizeDelta = Vector2.zero;
            _captain = root.gameObject;
            var strip = PanelKit.GlassPanel(root, "Strip", Vector2.zero, new Vector2(700, 190), new Vector2(760, 170));
            var colour = PlayerCardKit.Bright(UiKit.ParseHex(team.PrimaryColor));
            PlayerCardKit.Shape(strip, "Band", CardSprites.Round(6), colour, new Vector2(30, 0), new Vector2(8, 130), new Vector2(0, 0.5f));
            PlayerCardKit.Text(strip, new Vector2(0, 0.66f), new Vector2(1, 1), new Vector2(150, 0), new Vector2(-20, -12),
                $"CAPTAIN  ·  {team.Name.ToUpperInvariant()}", 24, TextAnchor.MiddleLeft, mine ? YouMark.Lime : colour, condensed: true);
            PlayerCardKit.Text(strip, new Vector2(0, 0.28f), new Vector2(1, 0.7f), new Vector2(150, 0), new Vector2(-20, 0), p.Name.ToUpperInvariant(), 52, TextAnchor.MiddleLeft, Color.white, condensed: true);
            PlayerCardKit.Text(strip, new Vector2(0, 0), new Vector2(1, 0.3f), new Vector2(150, 10), new Vector2(-20, 0),
                PreMatchCard.Description(p).ToUpperInvariant(), 20, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
            // Your captain carries your title (THE VAULT's TITLE slot, owner v26 #11: every Vault item shows in the game).
            if (mine) PlayerCardKit.Text(strip, new Vector2(0, 0), new Vector2(1, 0.3f), new Vector2(150, 10), new Vector2(-130, 0),
                Apl.Client.Modes.Progression.Equipped(Apl.Client.Modes.Slot.Title).Name, 22, TextAnchor.MiddleRight, UiKit.Gold, condensed: true);
            PlayerCardKit.Card(root, p, team, Vector2.zero, new Vector2(190, 230), new Vector2(240, 327), captain: true);
            UiKit.Crest(strip, team.Id, new Vector2(1, 0.5f), new Vector2(-70, 0), 96);
        }

        public void HideCaptain()
        {
            if (_captain != null) Destroy(_captain);
            _captain = null;
        }
    }
}
