using System.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The boundary: a padded cushion on the venue's rope line instead of a
    /// thin white cord — a squashed tube in APL navy and white blocks with
    /// gold seams, like the sponsor cushions on a T20 ground.
    /// </summary>
    public static partial class StadiumBuilder
    {
        private static void Cushion(Transform root, Mats m)
        {
            var k = new MeshKit();
            const int sides = 9;
            const float r = 0.11f, squash = 0.85f;
            // A half-buried tube: from the ground on the field side, over the top, down behind.
            var profile = Enumerable.Range(0, sides + 1).Select(i =>
            {
                var a = Mathf.PI * i / sides;
                return new Vector2(r - r * Mathf.Cos(a), 0.01f + r * squash * Mathf.Sin(a));
            }).ToArray();
            Ring(k, 0, _ => profile, null, 3f);
            k.Spawn("Rope", root, m.Lit(Color.white, CushionWrap(), 0.35f, emission: 0.12f));
        }

        /// <summary>Navy and white along the rope (u), a gold seam between them.</summary>
        private static Texture2D CushionWrap() =>
            Tex("cushion_wrap", 64, 4, (x, y) =>
            {
                if (x % 32 < 2) return new Color(0.95f, 0.72f, 0.18f);
                return x < 32 ? new Color(0.1f, 0.12f, 0.38f) : new Color(0.94f, 0.94f, 0.94f);
            });
    }
}
