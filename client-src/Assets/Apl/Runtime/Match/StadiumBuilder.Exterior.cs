using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The stands' outside wall (seen in aerial shots and the Match Setup preview):
    /// concrete fins, concourse levels with lit openings, a dark plinth, and an LED
    /// ribbon in the home side's colour under the parapet — it was a plain grey
    /// cylinder (rating r7, stadiums.png).
    /// </summary>
    public static partial class StadiumBuilder
    {
        private static void Exterior(Transform root, Mats m, StadiumLook look, Color home, float at, float height)
        {
            var wall = new MeshKit(2);
            // v runs top (0) to bottom (1) of the wall; one texture tile every 12 m round.
            Ring(wall, 0, _ => new[] { new Vector2(at, height), new Vector2(at, 0) }, Gone, 12);
            Ring(wall, 1, _ => new[] { new Vector2(at + 0.08f, height - 0.6f), new Vector2(at + 0.08f, height - 2f) }, Gone, 6);
            wall.Spawn("Exterior", root, m.Glow(look.Concrete * 0.8f, 0.12f, Facade()), m.Glow(home, 1.8f));
            // The precinct round it: paving lit in pools by the street lamps (aerials no longer float in the dark).
            var plaza = new MeshKit();
            Ring(plaza, 0, _ => new[] { new Vector2(at, 0.03f), new Vector2(at + 30, 0.03f) }, null, 15, flat: true);
            plaza.Spawn("Plaza", root, m.Glow(new Color(0.45f, 0.44f, 0.42f), 0.08f, Paving(), 0.1f));
        }

        /// <summary>15 m of paving: slabs, and a warm pool of lamplight in the middle of each tile.</summary>
        private static Texture2D Paving() =>
            Tex("paving", 64, 64, (x, y) =>
            {
                var dx = (x - 32) / 32f;
                var dy = (y - 32) / 32f;
                var pool = Mathf.Clamp01(1 - (dx * dx + dy * dy) * 1.4f);
                var slab = x % 8 == 0 || y % 8 == 0 ? 0.8f : 1f;
                return Color.Lerp(new Color(0.3f, 0.3f, 0.32f), new Color(0.8f, 0.68f, 0.5f), pool * 0.45f) * slab;
            });

        /// <summary>One 12 m bay of the outside wall: fins, four concourse bands with lit openings, the plinth.</summary>
        private static Texture2D Facade() =>
            Tex("facade", 64, 128, (x, y) =>
            {
                var v = y / 127f;                         // 0 = top of the wall
                var fin = x % 16 < 2;
                if (v > 0.93f) return new Color(0.28f, 0.28f, 0.3f);           // plinth
                if (v < 0.1f) return fin ? new Color(1f, 1f, 1f) : new Color(0.86f, 0.86f, 0.88f); // under the ribbon
                var band = (v - 0.1f) / 0.83f * 4;            // four concourse levels
                var inBand = band - Mathf.Floor(band);
                if (fin) return new Color(1f, 1f, 1f);
                if (inBand > 0.18f && inBand < 0.62f)
                {
                    // An opening: warm-lit concourse, a few people walking past.
                    var person = Hash(x / 3, (int)band) < 0.18f && inBand > 0.3f;
                    return person ? new Color(0.25f, 0.2f, 0.18f) : new Color(1.4f, 1.15f, 0.75f);
                }
                return new Color(0.78f, 0.78f, 0.8f) * (inBand < 0.18f ? 0.85f : 1f);
            });
    }
}
