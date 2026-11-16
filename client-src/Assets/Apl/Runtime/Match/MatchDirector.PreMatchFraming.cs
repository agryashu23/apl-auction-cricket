using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Ceremony framing (round 8): a two-shot that keeps two people whole in the
    /// frame however far apart they walk — the camera stands off along
    /// <c>dir</c> from their midpoint, as far back as their spread (across the
    /// view) needs at the lens's width, plus their spread in depth so the nearer
    /// one isn't cut off. The openers' walk-out shots use it so they never show
    /// empty grass or a batter sliced at the frame edge (v23 rating f23/24/149/150).
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>Track <paramref name="a"/> and <paramref name="b"/> together from the <paramref name="dir"/> side.</summary>
        private void TwoShot(Transform a, Transform b, Vector3 dir, float fov, bool cut, float height = 1.7f, float minDist = 5.5f, float smooth = 0.35f)
        {
            Camera.Track(() => Frame2(a.position, b.position, dir, fov, height, minDist), fov: fov, smooth: smooth, cut: cut);
        }

        /// <summary>The camera (pos, look) that holds both points, whole bodies, from the <paramref name="dir"/> side.</summary>
        private static (Vector3 pos, Vector3 look) Frame2(Vector3 pa, Vector3 pb, Vector3 dir, float fov, float height, float minDist)
        {
            dir.y = 0;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.right;
            var mid = (pa + pb) / 2f;
            var ground = new Vector3(mid.x, 0, mid.z);
            var side = Vector3.Cross(Vector3.up, dir);
            var gap = pa - pb;
            // Half their spread across the view, plus a body's width and some air either side.
            var span = Mathf.Abs(Vector3.Dot(gap, side)) / 2f + 1.4f;
            var depth = Mathf.Abs(Vector3.Dot(gap, dir)) / 2f;
            var aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            var tanH = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * aspect;
            var dist = Mathf.Max(minDist, span / Mathf.Max(0.1f, tanH) + depth);
            return (ground + dir * dist + Vector3.up * height, ground + Vector3.up * 1.0f);
        }
    }
}
