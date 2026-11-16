using System.Collections.Generic;
using Apl.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace Apl.Client.Ui
{
    /// <summary>
    /// Player pictures for the HUD and lists. A real portrait comes from
    /// Resources/Portraits/&lt;player id&gt; when one exists; otherwise a
    /// cricketer silhouette stands in. Also draws the hexagon shapes the
    /// portrait frame is cut from, so the HUD needs no art files.
    /// </summary>
    public static class Portraits
    {
        private static readonly Dictionary<string, Texture2D?> Cache = new Dictionary<string, Texture2D?>();
        private static Dictionary<string, Look>? _looks;

        /// <summary>Appearance notes per player id (Resources/Data/player_looks.json, tools/gen_looks.py).</summary>
        public static Dictionary<string, Look> Looks
        {
            get
            {
                if (_looks != null) return _looks;
                var json = Resources.Load<TextAsset>("Data/player_looks");
                _looks = json == null ? new Dictionary<string, Look>() : JsonConvert.DeserializeObject<Dictionary<string, Look>>(json.text) ?? new Dictionary<string, Look>();
                return _looks;
            }
        }
        private static Texture2D? _silhouette;
        private static readonly Dictionary<int, Sprite> Hexes = new Dictionary<int, Sprite>();

        /// <summary>
        /// This player's picture: a real portrait if one ships, else the painted
        /// avatar from their appearance notes, else the silhouette.
        /// <paramref name="size"/> is the avatar resolution (lists use small ones).
        /// </summary>
        public static Texture2D For(Player p, int size = 256)
        {
            var key = p.Id + "@" + size;
            if (!Cache.TryGetValue(key, out var tex))
            {
                tex = Resources.Load<Texture2D>("Portraits/" + p.Id);
                if (tex == null && Looks.TryGetValue(p.Id, out var look)) tex = AvatarPainter.Paint(look, p.Id, size);
                Cache[key] = tex;
            }
            return tex != null ? tex : Silhouette;
        }

        /// <summary>Head and shoulders on a dark gradient.</summary>
        public static Texture2D Silhouette => _silhouette ??= DrawSilhouette(256);

        /// <summary>A pointy-top hexagon sprite (white, antialiased) for masks and frames.</summary>
        public static Sprite Hex(int size = 256)
        {
            if (Hexes.TryGetValue(size, out var sprite)) return sprite;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            var c = (size - 1) / 2f;
            var r = size / 2f - 1.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Signed distance to a pointy-top hexagon of circumradius r:
                    // vertical side edges, slanted edges with normals at ±60°.
                    var dx = Mathf.Abs(x - c);
                    var dy = Mathf.Abs(y - c);
                    var d = Mathf.Max(dx, dx * 0.5f + dy * 0.8660254f) - r * 0.8660254f;
                    var a = Mathf.Clamp01(0.5f - d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
            Hexes[size] = sprite;
            return sprite;
        }

        private static Texture2D DrawSilhouette(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            var top = new Color(0.16f, 0.22f, 0.36f);
            var bottom = new Color(0.05f, 0.07f, 0.13f);
            var figure = new Color(0.55f, 0.62f, 0.74f);
            var shade = new Color(0.40f, 0.47f, 0.60f);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var u = x / (float)size;
                    var v = y / (float)size;
                    var bg = Color.Lerp(bottom, top, v);
                    // Head, neck, shoulders as soft-edged shapes (v = 0 is the bottom).
                    var head = Vector2.Distance(new Vector2(u, v * 1.08f), new Vector2(0.5f, 0.66f)) - 0.17f;
                    var neck = Mathf.Max(Mathf.Abs(u - 0.5f) - 0.075f, Mathf.Abs(v - 0.43f) - 0.08f);
                    var su = (u - 0.5f) / 0.42f;
                    var sv = (v - 0.04f) / 0.36f;
                    var shoulders = Mathf.Sqrt(su * su + sv * sv) - 1f;
                    shoulders = Mathf.Max(shoulders * 0.36f, -v);
                    var d = Mathf.Min(head, Mathf.Min(neck, shoulders));
                    var a = Mathf.Clamp01(0.5f - d * size);
                    var body = Color.Lerp(shade, figure, Mathf.Clamp01(v * 1.6f));
                    px[y * size + x] = Color.Lerp(bg, body, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
