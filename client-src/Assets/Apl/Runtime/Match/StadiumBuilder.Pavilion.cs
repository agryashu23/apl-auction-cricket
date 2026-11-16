using System;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The pavilion (bearing 270 ± 15, square of the wicket): a members' stand
    /// full of fans in front, a glazed lounge behind it, a balcony with the
    /// dressing rooms recessed above, and a cantilevered roof with a light strip
    /// over the seats. Round 8: it was a 20 m wall of lit/unlit window squares
    /// that read as an office block on every walk-out and square-on replay
    /// (rating r7, `118`).
    /// </summary>
    public static partial class StadiumBuilder
    {
        private const float MemFront = 8.6f, MemBack = 20f, MemFloor = 1.2f, MemRise = 7f;

        private static Color _homeSeat = new Color(0.5f, 0.1f, 0.1f);

        private static bool OutsidePavilion(float a) => !InPavilion(a);

        /// <summary>Behind the gate (the walk-outs start there): the players' tunnel, under the front rows.</summary>
        private static bool InGate(float a) => Gap(a, PavilionAt) < 1.9f;

        private static bool OffSeats(float a) => !InPavilion(a) || InGate(a);

        /// <summary>The members' seating and its fans (before the tiers spawn the crowd).</summary>
        private static void PavilionStand(Transform root, Mats m, StadiumLook look)
        {
            var seats = new MeshKit();
            var wall = new MeshKit();
            Ring(wall, 0, _ => new[] { new Vector2(8, 0), new Vector2(8, MemFloor), new Vector2(MemFront, MemFloor) }, OffSeats, 8);
            Ring(seats, 0, _ => new[] { new Vector2(MemFront, MemFloor), new Vector2(MemBack, MemFloor + MemRise) }, OffSeats, 16);
            CrowdTier(5, MemFront, MemFloor, MemBack - MemFront, MemRise, OffSeats, 0.92f, look.Id.Length * 97 + 5);
            // The players' tunnel: under the front rows behind the gate — the seats there start
            // 3.4 m back, over a dark mouth with a white lintel.
            const float tunnel = 3.4f;
            var lintel = MemFloor + MemRise * tunnel / (MemBack - MemFront);
            Ring(seats, 0, _ => new[] { new Vector2(MemFront + tunnel, lintel), new Vector2(MemBack, MemFloor + MemRise) }, a => !InGate(a), 16);
            // Fans on the rows over the tunnel too (r9 `226`: empty blue rows round the mouth).
            CrowdTier(6, MemFront + tunnel, lintel, MemBack - MemFront - tunnel, MemFloor + MemRise - lintel, a => !InGate(a), 0.92f, look.Id.Length * 89 + 11);
            var across = Vector3.Cross(Vector3.up, Dir(PavilionAt));
            foreach (var side in new[] { -1f, 1f })
                wall.Box(0, At(PavilionAt, 8 + tunnel / 2, 0) + across * side * 2.5f, new Vector3(0.5f, lintel, tunnel + 0.6f), Dir(PavilionAt));
            wall.Box(0, At(PavilionAt, 8 + 0.3f, lintel - 0.5f), new Vector3(5.5f, 0.6f, 0.6f), Dir(PavilionAt));
            var dark = new MeshKit();
            dark.Box(0, At(PavilionAt, 8 + tunnel + 0.1f, 0), new Vector3(4.6f, lintel, 0.2f), Dir(PavilionAt));
            dark.Spawn("PlayersTunnel", root, m.Lit(new Color(0.03f, 0.035f, 0.045f), smooth: 0.1f));
            var mat = m.Lit(Color.white, Seats(_homeSeat), 0.05f, emission: 0.3f);
            mat.SetTextureScale("_BaseMap", new Vector2(1, Mathf.RoundToInt((MemBack - MemFront) / RowDepth)));
            seats.Spawn("MembersSeats", root, mat);
            // Clad, not bare concrete (rating r8 `19`, `154`): navy panels with the APL gold line.
            wall.Spawn("MembersWall", root, m.Lit(Color.white, Cladding(), 0.35f, emission: 0.18f));
        }

        private static void Pavilion(Transform root, Mats m, StadiumLook look, float back, float top)
        {
            var h = Mathf.Max(19, top + 2);
            var glass = new MeshKit();
            var stone = new MeshKit(2);
            var rail = new MeshKit();
            var leds = new MeshKit();
            var deep = Mathf.Max(back, 26);
            var y0 = MemFloor + MemRise;          // the lounge floor, at the top of the members' seats
            var y1 = y0 + (h - y0) * 0.48f;       // the balcony
            // Behind the top row: a parapet, the lounge's glass, the balcony slab and its glass rail.
            Ring(stone, 0, _ => new[] { new Vector2(MemBack, y0), new Vector2(MemBack, y0 + 0.9f) }, OutsidePavilion, 8);
            Ring(glass, 0, _ => new[] { new Vector2(MemBack + 0.6f, y0 + 0.9f), new Vector2(MemBack + 0.6f, y1) }, OutsidePavilion, 9);
            Ring(stone, 0, _ => new[] { new Vector2(MemBack + 0.6f, y1), new Vector2(MemBack - 1.2f, y1), new Vector2(MemBack - 1.2f, y1 + 0.4f), new Vector2(MemBack + 2.4f, y1 + 0.4f) }, OutsidePavilion, 8);
            Ring(rail, 0, _ => new[] { new Vector2(MemBack - 1.2f, y1 + 0.4f), new Vector2(MemBack - 1.2f, y1 + 1.4f) }, OutsidePavilion, 4);
            // The dressing rooms, set back under the roof.
            Ring(glass, 0, _ => new[] { new Vector2(MemBack + 2.4f, y1 + 0.4f), new Vector2(MemBack + 2.4f, h) }, OutsidePavilion, 9);
            Ring(stone, 0, _ => new[] { new Vector2(MemBack + 2.4f, h), new Vector2(deep, h), new Vector2(deep, 0) }, OutsidePavilion, 8);
            if (look.HillPavilion)
            {
                Ring(stone, 1, _ => new[] { new Vector2(MemBack, h), new Vector2((MemBack + deep) / 2, h + 8), new Vector2(deep + 1, h) }, OutsidePavilion, 8);
            }
            else
            {
                // A thin cantilever rising toward the field, over the members' seats, lights along its lip.
                Ring(stone, 0, _ => new[] { new Vector2(deep + 1, h + 1), new Vector2(MemFront + 1.5f, h + 3.2f), new Vector2(MemFront + 1.5f, h + 2.4f) }, OutsidePavilion, 8);
                Ring(leds, 0, _ => new[] { new Vector2(MemFront + 1.7f, h + 2.35f), new Vector2(MemFront + 3.6f, h + 2.45f) }, OutsidePavilion, 4);
            }
            // End walls where it meets the stands.
            // Clad, like the members' wall (r9 `226`: a bare grey slab at the edge of the walk-out shot).
            var ends = new MeshKit();
            foreach (var edge in new[] { PavilionAt - PavilionHalf, PavilionAt + PavilionHalf })
            {
                ends.Box(0, At(edge, (8 + deep) / 2, 0), new Vector3(1.2f, h + 1.2f, deep - 8), Dir(edge), 1 / 4f);
            }
            ends.Spawn("PavilionEnds", root, m.Lit(Color.white, Cladding(), 0.35f, emission: 0.18f));
            var face = look.Arches ? m.Lit(look.Concrete, Arches(), 0.2f, emission: 0.25f) : m.Glow(Color.white, 0.6f, PavilionGlass(), 0.85f);
            glass.Spawn("PavilionFace", root, face);
            rail.Spawn("PavilionRail", root, m.Lit(new Color(0.55f, 0.65f, 0.72f), smooth: 0.9f, metal: 0.3f));
            if (!leds.Empty) leds.Spawn("PavilionLights", root, m.Glow(new Color(1f, 0.96f, 0.88f), 3.5f));
            var stoneMat = m.Lit(look.HillPavilion ? look.Concrete : Color.Lerp(look.Concrete, Color.white, 0.3f), smooth: 0.3f);
            stone.Spawn("Pavilion", root, stoneMat, m.Lit(new Color(0.55f, 0.16f, 0.12f), smooth: 0.2f));
        }

        /// <summary>Wall cladding, 4 m a tile: navy panels with joints, a gold line along the top, a lighter skirting.</summary>
        private static Texture2D Cladding() =>
            Tex("cladding", 64, 32, (x, y) =>
            {
                if (y > 27 && y < 30) return new Color(0.95f, 0.72f, 0.2f);
                if (y >= 30) return new Color(0.12f, 0.14f, 0.3f);
                if (y < 3) return new Color(0.55f, 0.56f, 0.6f);
                var joint = x % 16 == 0 || y == 15;
                return joint ? new Color(0.06f, 0.07f, 0.16f) : new Color(0.14f, 0.17f, 0.38f) * (0.92f + 0.08f * Hash(x / 16, y / 15));
            });

        /// <summary>
        /// Tinted curtain glass: tall panes on slim mullions, a warm lounge glow low in
        /// each pane, people's shapes against it here and there, sky reflected above.
        /// Even lighting on purpose — the old lit/unlit window grid read as offices.
        /// </summary>
        private static Texture2D PavilionGlass() =>
            Tex("pavilion_glass", 128, 64, (x, y) =>
            {
                var px = x % 16;
                var pane = x / 16;
                if (px == 0 || y < 2 || y > 61) return new Color(0.16f, 0.17f, 0.2f);
                var v = y / 63f;
                var warm = Color.Lerp(new Color(1f, 0.78f, 0.5f), new Color(0.45f, 0.4f, 0.38f), Mathf.Clamp01((v - 0.15f) / 0.6f));
                var glass = Color.Lerp(warm, new Color(0.22f, 0.32f, 0.42f), Mathf.Clamp01((v - 0.35f) * 1.6f));
                // Heads and shoulders against the glow in a few panes.
                var cx = px - 8f + (Hash(pane, 3) - 0.5f) * 6;
                var head = (cx * cx) / 6f + (y - 26) * (y - 26) / 9f < 1;
                var body = Mathf.Abs(cx) < 3.2f && y >= 8 && y < 24;
                if (Hash(pane, 9) < 0.55f && (head || body)) return glass * 0.35f;
                // A diagonal sheen.
                var sheen = Mathf.Repeat((x + y * 0.6f) / 40f, 1f) < 0.12f ? 1.15f : 1f;
                return glass * sheen;
            });
    }
}
