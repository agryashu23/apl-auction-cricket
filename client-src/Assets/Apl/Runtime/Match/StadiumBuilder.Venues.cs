using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Per-venue bowl shapes (round 8: "all 13 grounds share one bowl"): grounds
    /// with <see cref="StadiumLook.OpenCorners"/> have their stands stop short of
    /// the four corners, where the floodlight towers stand on grass banks — the
    /// small hill grounds read nothing like the big concrete bowls.
    /// </summary>
    public static partial class StadiumBuilder
    {
        private static StadiumLook _look = new StadiumLook();

        private static readonly float[] Corners = { 45, 135, 225, 315 };

        private static bool InCorner(float a)
        {
            if (!_look.OpenCorners) return false;
            foreach (var c in Corners) if (Gap(a, c) < _look.CornerHalf) return true;
            return false;
        }

        /// <summary>No stand here: the pavilion has its own building, open corners have banks.</summary>
        private static bool Gone(float a) => InPavilion(a) || InCorner(a);

        /// <summary>Grass banks in the open corners, rising from the rope wall to behind the towers, end walls on the stands.</summary>
        private static void CornerBanks(Transform root, Mats m, float back, float top)
        {
            if (!_look.OpenCorners) return;
            var bank = new MeshKit();
            var ends = new MeshKit();
            bool NotCorner(float a) => !InCorner(a);
            Ring(bank, 0, _ => new[] { new Vector2(9, 0.2f), new Vector2(back * 0.6f, 3.5f), new Vector2(back + 18, 7f) }, NotCorner, 6, flat: true);
            foreach (var c in Corners)
            foreach (var s in new[] { -1f, 1f })
            {
                // Close the stand's open end: a wall the stand's depth and height.
                var edge = c + s * _look.CornerHalf;
                ends.Box(0, At(edge, (9 + back) / 2, 0), new Vector3(0.8f, top + 2.4f, back - 9), Dir(edge), 1 / 4f);
            }
            bank.Spawn("CornerBanks", root, m.Lit(new Color(0.16f, 0.3f, 0.14f), Grass(), 0.1f));
            ends.Spawn("StandEnds", root, m.Lit(Color.white, Cladding(), 0.35f, emission: 0.18f));
        }

        /// <summary>Rough bank grass: mottled greens.</summary>
        private static Texture2D Grass() =>
            Tex("bank_grass", 32, 32, (x, y) => Color.white * (0.8f + 0.35f * Hash(x, y)));
    }
}
