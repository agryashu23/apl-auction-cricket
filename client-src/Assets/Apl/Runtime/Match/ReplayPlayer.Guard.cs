using System;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Occluder-safe replays (r9 rating f123–124: two seconds of a fielder's back): every
    /// replay lens is checked each frame against the players' bodies — one standing within
    /// a body's width of the line from the camera to what it looks at, and close to the
    /// camera, blocks it. A blocked angle is mirrored across what it looks at (the same
    /// shot from the other side) for the rest of the pass; if that's blocked too, it
    /// rises over them.
    /// </summary>
    public sealed partial class ReplayPlayer
    {
        /// <summary>The players' hips, asked each frame (the director's cast).</summary>
        public Func<IEnumerable<Transform>>? Bodies;

        /// <summary>This pass's fix: 0 as is, 1 mirrored, 2 raised.</summary>
        private int _dodge;

        /// <summary>Forget the last pass's fix (a new pass, a new camera).</summary>
        private void ResetGuard() => _dodge = 0;

        private (Vector3 pos, Vector3 look, float fov) Guard((Vector3 pos, Vector3 look, float fov) lens)
        {
            var t0 = StallProbe.Start();
            try { return GuardLens(lens); }
            finally { StallProbe.End("replay lens guard", t0); }
        }

        private (Vector3 pos, Vector3 look, float fov) GuardLens((Vector3 pos, Vector3 look, float fov) lens)
        {
            // No angle aims into the empty sky after a skyer (r12 P155): at most 5 m up; the ball drops into the frame.
            if (lens.look.y > 5f) lens.look.y = 5f;
            if (_dodge == 0 && Blocked(lens.pos, lens.look)) _dodge = 1;
            if (_dodge == 1)
            {
                var m = Mirror(lens);
                if (!Blocked(m.pos, m.look)) return m;
                _dodge = 2;
            }
            if (_dodge == 2) return (lens.pos + Vector3.up * 6f, lens.look, lens.fov);
            return lens;
        }

        private static (Vector3 pos, Vector3 look, float fov) Mirror((Vector3 pos, Vector3 look, float fov) l) =>
            (new Vector3(2f * l.look.x - l.pos.x, l.pos.y, l.pos.z), l.look, l.fov);

        /// <summary>A body between the camera and its subject, near enough the camera to fill a good part of the frame.</summary>
        private bool Blocked(Vector3 cam, Vector3 look)
        {
            if (Bodies == null) return false;
            var ray = look - cam;
            var len = ray.magnitude;
            if (len < 1f) return false;
            var dir = ray / len;
            foreach (var hips in Bodies())
            {
                if (hips == null || !hips.gameObject.activeInHierarchy) continue;
                // The body as a post from the knees to the head.
                var c = hips.position + Vector3.up * 0.3f;
                var along = Vector3.Dot(c - cam, dir);
                // In front of the camera, short of the subject (not the subject himself), and close.
                if (along < 0.3f || along > len - 1.2f || along > 12f) continue;
                var off = c - (cam + dir * along);
                off.y *= 0.45f;
                // A body near the camera fills a big part of the frame even off the line (r10 f280: the bowler's head
                // took a third of it): the nearer he is, the wider the berth.
                if (off.magnitude < Mathf.Lerp(1.0f, 0.55f, Mathf.InverseLerp(2f, 10f, along))) return true;
            }
            return false;
        }
    }
}
