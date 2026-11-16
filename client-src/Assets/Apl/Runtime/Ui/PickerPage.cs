using Apl.Core;
using UnityEngine;

namespace Apl.Client.Ui
{
    /// <summary>
    /// A picker screen of its own over everything (the HUD, the bowling controls,
    /// the radar): the dimmed backdrop, the header bar with the crests, a glass
    /// panel for the cards, the countdown ring bottom left. For the in-match
    /// pickers (bowler, field) so they look like the pre-match ones.
    /// </summary>
    public sealed class PickerPage
    {
        public RectTransform Root { get; }
        public RectTransform Panel { get; }
        public HeaderView Header { get; }
        public CountdownRing Ring { get; }
        private readonly GameObject _canvas;

        private PickerPage(GameObject canvas, RectTransform root, RectTransform panel, HeaderView header, CountdownRing ring)
        {
            _canvas = canvas;
            Root = root;
            Panel = panel;
            Header = header;
            Ring = ring;
        }

        /// <summary>The page with <paramref name="title"/> / <paramref name="subtitle"/>, the crests of <paramref name="left"/> and <paramref name="right"/>.</summary>
        public static PickerPage Open(string title, string subtitle, Franchise? left, Franchise? right, float panelHeight = 740)
        {
            var canvas = UiKit.Canvas("Picker", 40);
            var root = PanelKit.Backdrop((RectTransform)canvas.transform, 0.8f);
            var header = PanelKit.Header(root, title, subtitle, left, right);
            var panel = PanelKit.GlassPanel(root, "Cards", new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(1820, panelHeight));
            var ring = PanelKit.Countdown(root, new Vector2(0, 0), new Vector2(110, 84), 120);
            return new PickerPage(canvas.gameObject, root, panel, header, ring);
        }

        public void Close()
        {
            if (_canvas != null) Object.Destroy(_canvas);
        }
    }
}
