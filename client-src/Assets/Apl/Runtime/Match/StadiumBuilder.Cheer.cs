using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The cheerleaders' platforms: two LED-faced stages between the boards and
    /// the stands either side of the sight screen behind the batter (in the
    /// broadcast shot), their tops listed in <see cref="CheerSpots"/> for
    /// <see cref="CheerSquad"/>.
    /// </summary>
    public static partial class StadiumBuilder
    {
        private static readonly float[] CheerBearings = { 148f, 212f };

        /// <summary>Platform tops (stand point) and the way to face (toward the middle).</summary>
        public static readonly List<(Vector3 at, Vector3 facing)> CheerSpots = new List<(Vector3, Vector3)>();

        private static void CheerPlatforms(Transform root, Mats m)
        {
            CheerSpots.Clear();
            var body = new MeshKit();
            var face = new MeshKit();
            const float w = 3.4f, h = 1.25f, d = 2.2f, off = 6.2f;
            foreach (var a in CheerBearings)
            {
                var inward = -Dir(a);
                var foot = At(a, off, 0);
                body.Box(0, foot, new Vector3(w, h, d), inward);
                // The LED front, a hair proud of the box, facing the field.
                var right = -Vector3.Cross(Vector3.up, inward) * (w / 2 - 0.05f);
                var c = foot + inward * (d / 2 + 0.01f) + Vector3.up * (h / 2);
                var u = Vector3.up * (h / 2 - 0.08f);
                face.Quad(0, c - right - u, c - right + u, c + right + u, c + right - u);
                CheerSpots.Add((foot + Vector3.up * h, inward));
            }
            body.Spawn("CheerPlatforms", root, m.Lit(new Color(0.08f, 0.08f, 0.1f), smooth: 0.4f));
            face.Spawn("CheerPlatformLeds", root, m.Glow(Color.white, 1.1f, _cover));
        }
    }
}
