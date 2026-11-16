using System;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// Phone-shape captures on a PC (UI/UX dev, rater r1 §4/§10: the "phone" capture was 16:9).
    /// <c>-aplaspect 20:9</c> opens a 1920 × 864 window (exactly 20:9 = 2.222, the 2400 × 1080
    /// phone shape); <c>-aplaspect 16:9</c> a 1920 × 1080 one; any <c>W:H</c> works, 1920 wide.
    /// <c>-aplaspectwidth N</c> changes the width (e.g. 2400 for a 2400 × 1080 window on a big screen).
    ///
    /// The layout math this exercises: every UiKit canvas has AspectFit, which at 20:9 matches
    /// the HEIGHT (1080 reference) so the canvas is 2400 × 1080 reference px; centre-anchored
    /// screens keep their 1920 layout in the middle with 240 px spare each side, corner-anchored
    /// chips move out to the corners, and the home grid (MainMenu.PageSize) reflows to the full
    /// width. Logged as "[APL aspect] …" at boot so a capture log proves the shape.
    /// </summary>
    public static class AspectFlag
    {
        /// <summary>The forced aspect (w/h), or null. The window resize lands a frame late, so layout code
        /// that measures once at build time (the home grid) asks <see cref="Aspect"/> instead of Screen.</summary>
        public static float? Forced { get; private set; }

        /// <summary>The screen's aspect, or the forced one while a -aplaspect window is still resizing.</summary>
        public static float Aspect => Forced ?? (Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            if (Application.isMobilePlatform) return;
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-aplaspect");
            if (i < 0 || i + 1 >= args.Length) return;
            var parts = args[i + 1].Split(':', 'x', 'X');
            if (parts.Length != 2 || !float.TryParse(parts[0], out var aw) || !float.TryParse(parts[1], out var ah) || aw <= 0 || ah <= 0)
            {
                Debug.LogWarning($"[APL aspect] can't read '{args[i + 1]}' (use 20:9 or 16:9)");
                return;
            }
            var width = 1920;
            var wi = Array.IndexOf(args, "-aplaspectwidth");
            if (wi >= 0 && wi + 1 < args.Length && int.TryParse(args[wi + 1], out var w) && w >= 640) width = w;
            var height = Mathf.RoundToInt(width * ah / aw);
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            var aspect = aw / ah;
            Forced = aspect;
            var canvas = aspect >= 16f / 9f ? new Vector2(1080f * aspect, 1080f) : new Vector2(1920f, 1920f / aspect);
            Debug.Log($"[APL aspect] forced {args[i + 1]}: window {width}x{height} (aspect {aspect:0.000}); UI canvas {canvas.x:0}x{canvas.y:0} reference px (AspectFit matches {(aspect >= 16f / 9f ? "height" : "width")})");
        }
    }
}
