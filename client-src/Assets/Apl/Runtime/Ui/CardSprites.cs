using System;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Ui
{
    /// <summary>
    /// The shapes the card and panel kit is drawn with, made in code once
    /// (antialiased, white, tinted by the Image): rounded rectangles (9-sliced,
    /// so any size keeps its corners), a soft glow, a vertical fade, a ring, a
    /// tick, a disc.
    /// </summary>
    public static class CardSprites
    {
        private static readonly Dictionary<string, Sprite> Made = new Dictionary<string, Sprite>();

        /// <summary>A rounded rectangle with <paramref name="radius"/> px corners (sliced).</summary>
        public static Sprite Round(int radius = 18)
        {
            var size = radius * 2 + 4;
            return Get("round" + radius, size, size, (x, y) =>
            {
                var d = RoundBox(x, y, size, size, radius);
                return Mathf.Clamp01(0.5f - d);
            }, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
        }

        /// <summary>The outline of a rounded rectangle, <paramref name="thickness"/> px wide (sliced).</summary>
        public static Sprite RoundOutline(int radius = 18, int thickness = 4)
        {
            var size = radius * 2 + 4;
            return Get($"outline{radius}_{thickness}", size, size, (x, y) =>
            {
                var d = RoundBox(x, y, size, size, radius);
                return Mathf.Clamp01(0.5f - (Mathf.Abs(d + thickness / 2f) - thickness / 2f));
            }, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
        }

        /// <summary>A glow outside a rounded rectangle only (clear inside, so it can sit over a tile), fading over <paramref name="spread"/> px.</summary>
        public static Sprite OuterGlow(int radius = 12, int spread = 18)
        {
            var size = (radius + spread) * 2 + 4;
            return Get($"outer{radius}_{spread}", size, size, (x, y) =>
            {
                var d = RoundBox(x, y, size, size, radius + spread) + spread;
                if (d < 0) return 0f;
                var k = Mathf.Clamp01(1f - d / spread);
                return k * k;
            }, new Vector4(radius + spread + 1, radius + spread + 1, radius + spread + 1, radius + spread + 1));
        }

        /// <summary>A soft glow round a rounded rectangle (sliced): solid inside, fading over <paramref name="spread"/> px.</summary>
        public static Sprite Glow(int radius = 18, int spread = 24)
        {
            var size = (radius + spread) * 2 + 4;
            return Get($"glow{radius}_{spread}", size, size, (x, y) =>
            {
                var d = RoundBox(x, y, size, size, radius + spread) + spread;
                var k = Mathf.Clamp01(1f - d / spread);
                return k * k;
            }, new Vector4(radius + spread + 1, radius + spread + 1, radius + spread + 1, radius + spread + 1));
        }

        /// <summary>Opaque at the bottom fading to clear at the top (stretch it over a picture for the name band).</summary>
        public static Sprite FadeUp() => Get("fadeup", 4, 128, (x, y) => Mathf.SmoothStep(1f, 0f, y / 127f), Vector4.zero);

        /// <summary>A ring (countdown, selection), <paramref name="thickness"/> of the radius.</summary>
        public static Sprite Ring(float thickness = 0.14f)
        {
            const int n = 128;
            return Get("ring" + thickness, n, n, (x, y) =>
            {
                var r = Vector2.Distance(new Vector2(x, y), new Vector2(n / 2f, n / 2f));
                var outer = n / 2f - 1.5f;
                var inner = outer * (1 - thickness);
                return Mathf.Clamp01(outer - r + 0.5f) * Mathf.Clamp01(r - inner + 0.5f);
            }, Vector4.zero);
        }

        public static Sprite Disc() => Get("disc", 64, 64, (x, y) => Mathf.Clamp01(31.5f - Vector2.Distance(new Vector2(x, y), new Vector2(32, 32)) + 0.5f), Vector4.zero);

        /// <summary>A check mark.</summary>
        public static Sprite Tick()
        {
            const int n = 64;
            var a = new Vector2(14, 34);
            var b = new Vector2(27, 20);
            var c = new Vector2(51, 46);
            return Get("tick", n, n, (x, y) =>
            {
                var p = new Vector2(x, y);
                var d = Mathf.Min(Segment(p, a, b), Segment(p, b, c)) - 4.5f;
                return Mathf.Clamp01(0.5f - d);
            }, Vector4.zero);
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Signed distance (px) to a w×h rectangle with corner radius r (pixel centres).</summary>
        private static float RoundBox(int x, int y, int w, int h, int r)
        {
            var px = Mathf.Abs(x + 0.5f - w / 2f) - (w / 2f - r - 1);
            var py = Mathf.Abs(y + 0.5f - h / 2f) - (h / 2f - r - 1);
            var outside = new Vector2(Mathf.Max(px, 0), Mathf.Max(py, 0)).magnitude;
            return outside + Mathf.Min(Mathf.Max(px, py), 0) - r;
        }

        private static Sprite Get(string key, int w, int h, Func<int, int, float> alpha, Vector4 border)
        {
            if (Made.TryGetValue(key, out var s) && s != null) return s;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "kit_" + key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x, y)) * 255));
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, border);
            Made[key] = s;
            return s;
        }
    }
}
