using System;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Menu
{
    /// <summary>
    /// White glyphs for the home page's mode cards, drawn from distance functions
    /// like the role icons: an auction gavel on its block, practice nets (three
    /// stumps inside a net cage), and online (a globe with two signal arcs).
    /// </summary>
    public static class MenuIcons
    {
        private static readonly Dictionary<string, Sprite> Made = new Dictionary<string, Sprite>();

        public static Sprite For(string key)
        {
            if (Made.TryGetValue(key, out var s) && s != null) return s;
            Func<float, float, float> sdf = key switch
            {
                "gavel" => Gavel,
                "nets" => Nets,
                "home" => House,
                "gear" => Gear,
                "trophy" => Trophy,
                "bat" => Bat,
                _ => Online,
            };
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "menu_" + key, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var a = Mathf.Clamp01(0.5f - sdf((x + 0.5f) / n, (y + 0.5f) / n) * n);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Made[key] = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }

        private static float Box(float x, float y, float cx, float cy, float hw, float hh) =>
            Mathf.Max(Mathf.Abs(x - cx) - hw, Mathf.Abs(y - cy) - hh);

        /// <summary>A house: a pitched roof over a box with a door cut out.</summary>
        private static float House(float x, float y)
        {
            var box = Box(x, y, 0.5f, 0.36f, 0.24f, 0.18f);
            // The roof: a triangle as the intersection of two slanted half-planes and a floor.
            var roof = Mathf.Max(Mathf.Max((x - 0.5f) * 0.8f + (y - 0.86f) * 0.6f, -(x - 0.5f) * 0.8f + (y - 0.86f) * 0.6f), 0.5f - y);
            var door = Box(x, y, 0.5f, 0.27f, 0.06f, 0.1f);
            return Mathf.Max(Mathf.Min(box, roof), -door);
        }

        /// <summary>A cog: a ring with eight teeth and a hole.</summary>
        private static float Gear(float x, float y)
        {
            var p = new Vector2(x - 0.5f, y - 0.5f);
            var r = p.magnitude;
            var a = Mathf.Atan2(p.y, p.x);
            var tooth = Mathf.Abs(Mathf.Repeat(a * 8f / (2f * Mathf.PI) + 0.5f, 1f) - 0.5f) < 0.22f ? 0.4f : 0.31f;
            var outer = r - tooth;
            var hole = 0.13f - r;
            return Mathf.Max(outer, hole);
        }

        /// <summary>A gavel raised over its sound block: the head across, the handle down-right.</summary>
        /// <summary>A bat (blade and handle, leaning 40°) and a ball: QUICK MATCH.</summary>
        private static float Bat(float x, float y)
        {
            const float cx = 0.46f, cy = 0.5f;
            var c = Mathf.Cos(0.7f);
            var sn = Mathf.Sin(0.7f);
            var u = (x - cx) * c - (y - cy) * sn;
            var v = (x - cx) * sn + (y - cy) * c;
            var blade = Box(u, v, 0, -0.1f, 0.075f, 0.24f) - 0.02f;
            var handle = Box(u, v, 0, 0.24f, 0.028f, 0.12f);
            var ball = Vector2.Distance(new Vector2(x, y), new Vector2(0.78f, 0.24f)) - 0.075f;
            return Mathf.Min(Mathf.Min(blade, handle), ball);
        }

        private static float Gavel(float x, float y)
        {
            // The head and handle are turned 35° about the head's centre.
            const float cx = 0.42f, cy = 0.62f;
            var c = Mathf.Cos(-0.6f);
            var sn = Mathf.Sin(-0.6f);
            var u = (x - cx) * c - (y - cy) * sn;
            var v = (x - cx) * sn + (y - cy) * c;
            var head = Box(u, v, 0, 0, 0.1f, 0.2f) - 0.02f;
            // The bands near each end of the head.
            var bands = Mathf.Min(Mathf.Abs(v - 0.13f), Mathf.Abs(v + 0.13f)) - 0.012f;
            head = Mathf.Max(head, -bands);
            var handle = Box(u, v, 0.27f, 0, 0.2f, 0.035f);
            var block = Box(x, y, 0.62f, 0.16f, 0.22f, 0.05f) - 0.015f;
            var top = Box(x, y, 0.62f, 0.24f, 0.15f, 0.03f);
            return Mathf.Min(Mathf.Min(head, handle), Mathf.Min(block, top));
        }

        /// <summary>A cup on a stem and base, a handle each side (the home page's TOURNAMENT card, owner v26).</summary>
        private static float Trophy(float x, float y)
        {
            var p = new Vector2(x, y);
            var bowl = Mathf.Max(Mathf.Min(Box(x, y, 0.5f, 0.74f, 0.2f, 0.1f), Vector2.Distance(p, new Vector2(0.5f, 0.64f)) - 0.2f), y - 0.84f);
            float Handle(float cx, float side) => Mathf.Max(Mathf.Abs(Vector2.Distance(p, new Vector2(cx, 0.7f)) - 0.085f) - 0.024f, side * (x - cx));
            var handles = Mathf.Min(Handle(0.31f, 1f), Handle(0.69f, -1f));
            var stem = Box(x, y, 0.5f, 0.36f, 0.04f, 0.08f);
            var plinth = Box(x, y, 0.5f, 0.26f, 0.1f, 0.025f);
            var foot = Box(x, y, 0.5f, 0.17f, 0.17f, 0.045f) - 0.01f;
            return Mathf.Min(Mathf.Min(bowl, handles), Mathf.Min(stem, Mathf.Min(plinth, foot)));
        }

        /// <summary>Three stumps with bails inside an arched net cage drawn as a grid.</summary>
        private static float Nets(float x, float y)
        {
            var stumps = Mathf.Min(Box(x, y, 0.39f, 0.38f, 0.024f, 0.2f), Mathf.Min(Box(x, y, 0.5f, 0.38f, 0.024f, 0.2f), Box(x, y, 0.61f, 0.38f, 0.024f, 0.2f)));
            var bails = Box(x, y, 0.5f, 0.6f, 0.13f, 0.012f);
            // The cage: an outline frame with a thin mesh inside it.
            var outer = Box(x, y, 0.5f, 0.5f, 0.42f, 0.4f);
            var frame = Mathf.Abs(outer) - 0.02f;
            var mesh = Mathf.Min(Mathf.Abs(Mathf.Repeat(x - 0.08f, 0.12f) - 0.06f), Mathf.Abs(Mathf.Repeat(y - 0.1f, 0.12f) - 0.06f)) - 0.004f;
            mesh = Mathf.Max(mesh, outer);
            // Keep the mesh off the stumps so they read.
            mesh = Mathf.Max(mesh, -(Mathf.Min(stumps, bails) - 0.015f));
            return Mathf.Min(Mathf.Min(stumps, bails), Mathf.Min(frame, mesh));
        }

        /// <summary>A globe (rim, equator, a meridian) with two signal arcs at its top right.</summary>
        private static float Online(float x, float y)
        {
            var p = new Vector2(x, y);
            var centre = new Vector2(0.4f, 0.4f);
            const float r = 0.26f;
            var d = Vector2.Distance(p, centre);
            var rim = Mathf.Abs(d - r) - 0.025f;
            var inside = d - r;
            var equator = Mathf.Max(Mathf.Abs(y - centre.y) - 0.018f, inside);
            // A meridian: an ellipse half as wide as the globe.
            var e = new Vector2((x - centre.x) * 2.2f, y - centre.y).magnitude;
            var meridian = Mathf.Max(Mathf.Abs(e - r) / 2.2f - 0.016f, inside);
            var axis = Mathf.Max(Mathf.Abs(x - centre.x) - 0.018f, inside);
            var globe = Mathf.Min(Mathf.Min(rim, equator), Mathf.Min(meridian, axis));
            // The signal: two arcs around the top-right corner, only in that quarter.
            var corner = new Vector2(0.62f, 0.62f);
            var q = p - corner;
            var dc = q.magnitude;
            var arcs = Mathf.Min(Mathf.Abs(dc - 0.14f), Mathf.Abs(dc - 0.25f)) - 0.025f;
            arcs = Mathf.Max(arcs, Mathf.Max(-q.x, -q.y) + 0.02f);
            var dot = Vector2.Distance(p, corner + new Vector2(0.03f, 0.03f)) - 0.04f;
            return Mathf.Min(globe, Mathf.Min(arcs, dot));
        }
    }
}
