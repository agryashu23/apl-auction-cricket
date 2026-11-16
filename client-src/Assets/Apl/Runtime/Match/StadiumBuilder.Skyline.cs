using System;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Round 9, the city beyond the stands (rating r8: "untextured boxes with window
    /// dots"): towers in three facade styles (strip windows, curtain glass, older
    /// punched windows), dark roof caps with plant rooms and red aviation lights on the
    /// tall ones, and a far skyline silhouette ring for depth.
    /// </summary>
    public static partial class StadiumBuilder
    {
        /// <summary>One building: a facade box in one of three styles, a roof cap, maybe a plant room and a red light.</summary>
        private static void Tower(MeshKit facades, MeshKit caps, MeshKit reds, Vector3 foot, Vector3 size, Vector3 facing, int style, float r)
        {
            facades.Box(style, foot, size, facing, 1 / 24f);
            caps.Box(0, foot + Vector3.up * size.y, new Vector3(size.x + 0.6f, 1.2f, size.z + 0.6f), facing);
            if (r < 0.45f) caps.Box(0, foot + Vector3.up * (size.y + 1.2f), new Vector3(size.x * 0.4f, 3.5f, size.z * 0.4f), facing);
            if (size.y > 55) reds.Box(0, foot + Vector3.up * (size.y + (r < 0.45f ? 4.7f : 1.2f)), new Vector3(1.2f, 1.2f, 1.2f), facing);
        }

        private static void SpawnCity(Transform root, Mats m, MeshKit facades, MeshKit caps, MeshKit reds)
        {
            facades.Spawn("Skyline", root,
                m.Glow(new Color(0.7f, 0.72f, 0.8f), 0.75f, CityFacade(0)),
                m.Glow(new Color(0.6f, 0.68f, 0.8f), 0.75f, CityFacade(1)),
                m.Glow(new Color(0.75f, 0.7f, 0.65f), 0.75f, CityFacade(2)));
            caps.Spawn("SkylineRoofs", root, m.Lit(new Color(0.07f, 0.07f, 0.09f), smooth: 0.1f));
            if (!reds.Empty) reds.Spawn("AviationLights", root, m.Glow(new Color(1f, 0.12f, 0.08f), 4f));
        }

        /// <summary>
        /// 24 m of facade (8 floors): style 0 strip windows on dark spandrels, 1 curtain glass
        /// with a sky reflection, 2 punched windows in pale stone. About a third of rooms lit.
        /// </summary>
        private static Texture2D CityFacade(int style) =>
            Tex("city_" + style, 128, 128, (x, y) =>
            {
                var floor = y / 16;
                var fy = y % 16;
                var bay = x / (style == 2 ? 16 : 8);
                var bx = x % (style == 2 ? 16 : 8);
                var lit = Hash(bay + style * 31, floor) < 0.32f;
                var warm = Hash(floor + 5, bay) < 0.65f;
                var room = lit ? (warm ? new Color(1f, 0.82f, 0.52f) : new Color(0.78f, 0.88f, 1f)) * (0.75f + 0.25f * Hash(bay, floor + 9)) : new Color(0.08f, 0.1f, 0.14f);
                switch (style)
                {
                    case 0:
                        if (fy < 6) return new Color(0.16f, 0.16f, 0.18f);          // spandrel
                        return bx == 0 ? new Color(0.1f, 0.1f, 0.12f) : room;
                    case 1:
                        if (fy == 0 || bx == 0) return new Color(0.2f, 0.22f, 0.26f); // mullions
                        var sky = new Color(0.12f, 0.16f, 0.26f) * (0.8f + 0.4f * y / 128f);
                        return lit ? Color.Lerp(sky, room, 0.75f) : sky;
                    default:
                        var stone = new Color(0.45f, 0.42f, 0.38f);
                        return fy > 4 && fy < 13 && bx > 4 && bx < 12 ? room : stone;
                }
            });

        /// <summary>The far skyline: a silhouette ring 1.3 km out (alpha cut), tiny lights in it.</summary>
        private static void FarSkyline(Transform root, Mats m, StadiumLook look, Func<float, bool> skip)
        {
            var ring = new MeshKit();
            const float r = 1300, h = 150;
            for (var a = 0f; a < 360; a += 4)
            {
                if (skip(a + 2)) continue;
                ring.Quad(0, Far(a, r, -1), Far(a, r, h), Far(a + 4, r, h), Far(a + 4, r, -1), new Vector2(a / 40f, 0), new Vector2((a + 4) / 40f, 1));
            }
            if (!ring.Empty) ring.Spawn("FarSkyline", root, m.Cutout(FarCity(), 0.55f));
        }

        /// <summary>Building silhouettes against nothing: dark blue-grey, specks of light, clear above the roofline.</summary>
        private static Texture2D FarCity() =>
            Tex("far_city", 256, 64, (x, y) =>
            {
                var block = x / 6;
                var height = 10 + (int)(Hash(block, 3) * Hash(block, 7) * 50) + (Hash(block / 3, 11) < 0.15f ? 8 : 0);
                if (y > height) return new Color(0, 0, 0, 0);
                var light = Hash(x, y) < 0.06f && y < height - 1;
                return light ? new Color(1f, 0.85f, 0.6f, 1) : new Color(0.06f, 0.07f, 0.1f, 1);
            });
    }
}
