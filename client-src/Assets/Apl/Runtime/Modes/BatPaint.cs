using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Modes
{
    /// <summary>
    /// A bat design painted onto the bat's texture (Resources/Gear/bat: willow, two
    /// sticker panels, a toe band, the grip). The sticker panels and band get the
    /// design's art — the classic graphic recoloured, stripes, chevrons, flames or a
    /// split — keeping the white APL lettering; the blade gets its finish (willow,
    /// carbon, ice lacquer, gold leaf) keeping the grain. The grip is left alone.
    /// Read back through a render texture (the asset isn't CPU-readable); one
    /// painted copy per design.
    /// </summary>
    public static class BatPaint
    {
        private static readonly Dictionary<string, Texture2D> Painted = new Dictionary<string, Texture2D>();
        private static readonly Color Navy = new Color(0.078f, 0.118f, 0.322f), Red = new Color(0.83f, 0.165f, 0.165f), Gold = new Color(0.91f, 0.69f, 0.13f);

        public static Texture2D? Source => Resources.Load<Texture2D>("Gear/bat");

        /// <summary>The bat's texture in design <paramref name="d"/> (the default design: the texture itself).</summary>
        public static Texture2D? For(Cosmetic d)
        {
            var src = Source;
            if (src == null || d.Default) return src;
            if (Painted.TryGetValue(d.Id, out var done) && done != null) return done;
            return Painted[d.Id] = Paint(src, d);
        }

        private static Color32[] Read(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tmp = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            tmp.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tmp.GetPixels32();
            Object.Destroy(tmp);
            return px;
        }

        /// <summary>Which of the sticker's colours a pixel is (0 navy panel, 1 red bars, 2 gold stripe, 3 white lettering), or -1.</summary>
        private static int Class(Color c)
        {
            if (c.b > 0.15f && c.b > c.r + 0.08f && c.b > c.g + 0.04f && c.maxColorComponent < 0.55f) return 0;
            if (c.r > 0.55f && c.g < 0.32f && c.b < 0.32f) return 1;
            if (c.r > 0.72f && c.g > 0.48f && c.b < 0.3f && c.r - c.b > 0.5f) return 2;
            if (c.r > 0.85f && c.g > 0.85f && c.b > 0.85f) return 3;
            return -1;
        }

        private static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static Texture2D Paint(Texture2D src, Cosmetic d)
        {
            int w = src.width, h = src.height;
            var px = Read(src);
            var grip = (int)(h * 0.76f);
            // The sticker regions' boxes: the two panels (left / right half, upper blade) and the toe band.
            var boxes = new[] { new RectInt(w, h, -w, -h), new RectInt(w, h, -w, -h), new RectInt(w, h, -w, -h) };
            var cls = new int[px.Length];
            for (var y = 0; y < grip; y++)
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                cls[i] = Class(px[i]);
                if (cls[i] < 0 || cls[i] == 3) continue;
                var b = y > h * 0.35f ? (x < w / 2 ? 0 : 1) : 2;
                boxes[b] = Grow(boxes[b], x, y);
            }
            for (var y = 0; y < grip; y++)
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                Color c = px[i];
                var box = -1;
                for (var b = 0; b < 3; b++) if (boxes[b].width > 0 && boxes[b].Contains(new Vector2Int(x, y))) box = b;
                if (box < 0)
                {
                    px[i] = Finish(c, d.Finish);
                    continue;
                }
                // The lettering stays white.
                if (cls[i] == 3 && box < 2) continue;
                var r = boxes[box];
                var u = (x - r.xMin + 0.5f) / Mathf.Max(1, r.width);
                var v = (y - r.yMin + 0.5f) / Mathf.Max(1, r.height);
                var n = Art(d, cls[i], c, u, v);
                n.a = c.a;
                px[i] = n;
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "bat_" + d.Id, wrapMode = src.wrapMode, filterMode = src.filterMode };
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        private static RectInt Grow(RectInt r, int x, int y)
        {
            if (r.width < 0) return new RectInt(x, y, 1, 1);
            var xMin = Mathf.Min(r.xMin, x);
            var yMin = Mathf.Min(r.yMin, y);
            return new RectInt(xMin, yMin, Mathf.Max(r.xMax, x + 1) - xMin, Mathf.Max(r.yMax, y + 1) - yMin);
        }

        /// <summary>The sticker art at (u, v) in its box; <paramref name="cls"/> is what the classic graphic had there.</summary>
        private static Color Art(Cosmetic d, int cls, Color was, float u, float v)
        {
            var edge = u < 0.07f || u > 0.93f || v < 0.05f || v > 0.95f;
            switch (d.Pattern)
            {
                case BatPattern.Classic:
                    var (from, to) = cls == 1 ? (Red, d.B) : cls == 2 ? (Gold, d.C) : (Navy, d.A);
                    return to * Mathf.Clamp(Lum(was) / Mathf.Max(0.04f, Lum(from)), 0.4f, 1.6f);
                case BatPattern.Stripes:
                    return edge ? d.C : Mathf.FloorToInt((u * 0.8f + v) * 7f) % 2 == 0 ? d.A : d.B;
                case BatPattern.Chevron:
                    return edge ? d.C : Mathf.FloorToInt((v + Mathf.Abs(u - 0.5f) * 0.9f) * 6f) % 2 == 0 ? d.A : d.B;
                case BatPattern.Flame:
                    var tongue = 0.42f + 0.18f * Mathf.Sin(u * 19f + 1.3f) * Mathf.Sin(u * 7f);
                    return v < tongue ? Color.Lerp(d.C, d.B, v / Mathf.Max(0.05f, tongue)) : d.A;
                default:
                    return Mathf.Abs(u - 0.5f) < 0.06f ? d.C : u < 0.5f ? d.A : d.B;
            }
        }

        /// <summary>The blade's finish over the willow, keeping its grain (brightness).</summary>
        private static Color32 Finish(Color c, BatFinish f)
        {
            var l = Lum(c);
            Color n = f switch
            {
                BatFinish.Carbon => new Color(0.16f, 0.16f, 0.18f) * (0.6f + 0.6f * l),
                BatFinish.Ice => Color.Lerp(new Color(0.96f, 0.97f, 1f), c, 0.2f),
                BatFinish.Gold => new Color(1f, 0.78f, 0.3f) * (0.55f + 0.6f * l),
                _ => c,
            };
            n.a = c.a;
            return n;
        }

        /// <summary>A small picture of the design for the locker: the texture's upper blade with a sticker panel.</summary>
        public static Rect PreviewUv => new Rect(0f, 0.3f, 0.5f, 0.46f);
    }
}
