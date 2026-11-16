using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The online lobby's see-through fix (UI/UX dev, rater r1 §6 LOW: "a DLD wall banner and a
    /// shield show behind the rows", host/room_03). Applied to the page after every redraw
    /// (OnlineLobby.Draw), without changing what the lobby builds:
    ///  - an opaque card behind the PLAYERS list (its label included);
    ///  - the franchise tiles, code card, host controls and rows fully opaque (UiTheme.Solidify).
    /// </summary>
    public static class LobbyStyle
    {
        public static void Apply(RectTransform page)
        {
            if (page == null) return;
            UiTheme.Solidify(page);
            var card = UiTheme.BackCard(page, "Member_", 12f, 56f);
            if (card != null)
            {
                // Under the "PLAYERS n/10" label as well as the rows.
                foreach (Transform t in page)
                {
                    var text = t.GetComponentInChildren<Text>();
                    if (text == null || !text.text.StartsWith("PLAYERS")) continue;
                    var at = t.GetSiblingIndex();
                    page.Find("Member_Rim")?.SetSiblingIndex(at);
                    card.SetSiblingIndex(at + 1);
                    break;
                }
            }
        }
    }
}
