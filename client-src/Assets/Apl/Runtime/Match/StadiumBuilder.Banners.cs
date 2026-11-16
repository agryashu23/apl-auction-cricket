using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Placards in the stands: folded away in every fan chunk (kind 2 in the
    /// Apl/Crowd shader) and raised overhead and pumped up and down —
    /// <see cref="CrowdBanners"/> on a four or a six by the crowd round where the
    /// ball went ("4" / "FOUR", "6" / "SIX!"), <see cref="CrowdWicketBanners"/>
    /// right round the ground ("OUT!" / "WICKET"). White cards, team-colour
    /// borders and letters (crowd_atlas / crowd_mask).
    /// </summary>
    public static partial class StadiumBuilder
    {
        private static readonly int BannerId = Shader.PropertyToID("_AplBanner");

        /// <summary>Sheet cells (two wide): 112 "4", 114 "FOUR"; +4 cells the six's, +8 the wicket's.</summary>
        private const int PlacardFour = 112, PlacardWord = 114, PlacardStep = 4;

        /// <summary>
        /// Placards up near <paramref name="towards"/> (where the ball crossed the rope);
        /// <paramref name="teamId"/>'s fans hold most of them (as <see cref="CrowdCheer"/>).
        /// </summary>
        public static void CrowdBanners(bool six, Vector3 towards, string? teamId = null)
        {
            var bearing = Mathf.Atan2(towards.x, towards.z) / (2 * Mathf.PI) + 0.5f;
            Raise(bearing, six ? 1 : 0, teamId);
        }

        /// <summary>"OUT!" / "WICKET" placards all round the ground, mostly the fielding side's fans.</summary>
        public static void CrowdWicketBanners(string? teamId = null) => Raise(-1, 2, teamId);

        private static void Raise(float bearing, int kind, string? teamId)
        {
            Shader.SetGlobalVector(BannerId, new Vector4(1, Time.timeSinceLevelLoad, bearing, kind));
            if (teamId == null) return;
            float Joins(string id) => teamId == id ? 1f : 0.15f;
            Shader.SetGlobalVector(CheerTeamsId, new Vector4(Joins(_groupA), Joins(_groupB), 0.6f, 0));
        }

        /// <summary>A folded placard over a fan (the shader raises it); half numbers, half words.</summary>
        private static void Placard(Chunk c, Vector3 foot, Color colour, float code, float phase, float light)
        {
            var cell = Cell(phase < 0.5f ? PlacardFour : PlacardWord);
            var du = 2f / Cols;
            var dv = 1f / Rows;
            var col = (Color32)new Color(colour.r, colour.g, colour.b, code);
            var start = c.Pos.Count;
            var corners = new[] { new Vector2(-0.4f, -0.4f), new Vector2(-0.4f, 0.4f), new Vector2(0.4f, 0.4f), new Vector2(0.4f, -0.4f) };
            var uvs = new[] { cell, cell + new Vector2(0, dv), cell + new Vector2(du, dv), cell + new Vector2(du, 0) };
            for (var i = 0; i < 4; i++)
            {
                c.Pos.Add(foot);
                c.Col.Add(col);
                c.Uv.Add(uvs[i]);
                c.K.Add(new Vector4(corners[i].x, corners[i].y, phase, 2));
                // The shader adds this offset times _AplBanner.w (0 four, 1 six, 2 wicket).
                c.Alt.Add(new Vector3(PlacardStep / (float)Cols, 0, light));
            }
            c.Tris.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
    }
}
