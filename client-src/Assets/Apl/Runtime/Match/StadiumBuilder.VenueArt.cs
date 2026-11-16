using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Round 10: each ground's identity inside the gameplay frame (rating r9: "from the
    /// gameplay camera all 13 are one image"). The broadcast and batting cameras see the
    /// outfield, the rope boards, the far sight screen and the lower tiers up to the first
    /// fascia — so those carry the venue: its own mowing pattern and green, its name board
    /// (Ui/Venues/&lt;id&gt;_board, tools/venue_art.py) every fourth rope board and round the
    /// fascia, its sight screen (Ui/Venues/&lt;id&gt;_screen) and its accent on the tier walls.
    /// </summary>
    public static partial class StadiumBuilder
    {
        private static Texture2D? VenueTex(string kind) => Resources.Load<Texture2D>($"Ui/Venues/{_v.Id}_{kind}");

        /// <summary>The fascia strip: the venue's board and the APL cover side by side (one texture, so one material).</summary>
        private static Texture Fascia(Texture? cover)
        {
            var board = VenueTex("board");
            if (board == null || cover == null) return cover != null ? cover : Texture2D.whiteTexture;
            var rt = new RenderTexture(1280, 248, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Fascia_" + _v.Id,
                useMipMap = true,
                autoGenerateMips = true,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = 4,
            };
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, 1280, 248, 0);
            GL.Clear(true, true, Color.black);
            Graphics.DrawTexture(new Rect(0, 0, 640, 248), board);
            Graphics.DrawTexture(new Rect(640, 0, 640, 248), cover);
            GL.PopMatrix();
            RenderTexture.active = was;
            return rt;
        }

        /// <summary>
        /// The outfield for this ground: its green, mown in its pattern (stripes, wide stripes,
        /// checks, diagonals, rings round the square, a cross-cut), with clumps and blades.
        /// The texture spans the 200 m outfield plane; the pitch runs along v.
        /// </summary>
        private static Texture2D Grass(StadiumLook look)
        {
            const int n = 1024;
            const float metres = 200f;
            var key = "grass_" + look.Id;
            if (TexCache.TryGetValue(key, out var cached) && cached != null) return cached;
            // Noise from two small tiles (cheap at load): fine blades and broad clumps.
            var fine = new float[256 * 256];
            for (var i = 0; i < fine.Length; i++) fine[i] = Hash(i % 256, i / 256) * 0.1f + Mathf.PerlinNoise(i % 256 * 0.15f, i / 256 * 0.15f) * 0.08f;
            var broad = new float[128 * 128];
            for (var i = 0; i < broad.Length; i++) broad[i] = Mathf.PerlinNoise(i % 128 * 0.2f + 3, i / 128 * 0.2f) * 0.12f - 0.06f;
            var px = new Color[n * n];
            var light = look.Grass * 1.16f;
            var dark = look.Grass * 0.9f;
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var mx = (x / (float)n - 0.5f) * metres;
                var my = (y / (float)n - 0.5f) * metres;
                var band = look.Mow switch
                {
                    Mow.WideStripes => Mathf.FloorToInt(mx / 12f) & 1,
                    Mow.Checks => (Mathf.FloorToInt(mx / 9f) + Mathf.FloorToInt(my / 9f)) & 1,
                    Mow.Diagonal => Mathf.FloorToInt((mx + my) * 0.7071f / 8f) & 1,
                    Mow.Rings => Mathf.FloorToInt(Mathf.Sqrt(mx * mx + my * my) / 7f) & 1,
                    Mow.Cross => (Mathf.FloorToInt((mx + my) * 0.7071f / 9f) + Mathf.FloorToInt((mx - my) * 0.7071f / 9f)) & 1,
                    _ => Mathf.FloorToInt(mx / 7f) & 1,
                };
                var c = band == 0 ? dark : light;
                var k = 0.92f + fine[(y & 255) * 256 + (x & 255)] + broad[(y >> 3 & 127) * 128 + (x >> 3 & 127)];
                px[y * n + x] = new Color(c.r * k, c.g * k, c.b * k, 1);
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = key, wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
            tex.SetPixels(px);
            tex.Apply(true, true);
            TexCache[key] = tex;
            return tex;
        }
    }
}
