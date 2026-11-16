using System;
using System.Collections.Generic;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Ui
{
    /// <summary>
    /// White role glyphs for the cards, drawn from distance functions: a bat
    /// (blade and handle, tilted), a ball with its seam, both for an all-rounder,
    /// a keeper's glove.
    /// </summary>
    public static class RoleIcons
    {
        private static readonly Dictionary<string, Sprite> Made = new Dictionary<string, Sprite>();

        public static Sprite For(Role role) => For(role switch
        {
            Role.Batter => "bat",
            Role.Bowler => "ball",
            Role.AllRounder => "both",
            _ => "gloves",
        });

        public static Sprite For(string key)
        {
            if (Made.TryGetValue(key, out var s) && s != null) return s;
            Func<float, float, float> sdf = key switch
            {
                "bat" => Bat,
                "ball" => (x, y) => Ball(x, y, 0.5f, 0.5f, 0.36f),
                "both" => (x, y) => Mathf.Min(Bat(x * 1.1f - 0.08f, y * 1.1f - 0.02f), Ball(x, y, 0.77f, 0.23f, 0.17f)),
                _ => Glove,
            };
            const int n = 96;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "role_" + key, wrapMode = TextureWrapMode.Clamp };
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

        private static float Bat(float x, float y)
        {
            // Turned about the centre: the blade along v, the handle above it.
            var c = Mathf.Cos(0.6f);
            var sn = Mathf.Sin(0.6f);
            var u = (x - 0.5f) * c - (y - 0.5f) * sn;
            var v = (x - 0.5f) * sn + (y - 0.5f) * c;
            var blade = Mathf.Max(Mathf.Abs(u) - 0.09f, Mathf.Abs(v + 0.1f) - 0.3f);
            var handle = Mathf.Max(Mathf.Abs(u) - 0.035f, Mathf.Abs(v - 0.3f) - 0.12f);
            return Mathf.Min(blade, handle);
        }

        private static float Ball(float x, float y, float cx, float cy, float r)
        {
            var d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) - r;
            // The seam: a thin ring cut out across the middle.
            var seam = Mathf.Abs(Vector2.Distance(new Vector2(x, y), new Vector2(cx - r * 1.1f, cy)) - r * 1.25f) - r * 0.08f;
            return Mathf.Max(d, -seam);
        }

        private static float Glove(float x, float y)
        {
            var palm = Mathf.Max(Mathf.Abs(x - 0.5f) - 0.22f, Mathf.Abs(y - 0.4f) - 0.2f) - 0.04f;
            var fingers = Mathf.Max(Mathf.Abs(x - 0.5f) - 0.2f, Mathf.Abs(y - 0.68f) - 0.12f);
            var gaps = Mathf.Min(Mathf.Abs(x - 0.4f), Mathf.Min(Mathf.Abs(x - 0.5f), Mathf.Abs(x - 0.6f))) - 0.012f;
            fingers = y > 0.6f ? Mathf.Max(fingers, -gaps) : fingers;
            var thumb = Vector2.Distance(new Vector2(x, y), new Vector2(0.2f, 0.5f)) - 0.09f;
            return Mathf.Min(Mathf.Min(palm, fingers), thumb);
        }
    }
}
