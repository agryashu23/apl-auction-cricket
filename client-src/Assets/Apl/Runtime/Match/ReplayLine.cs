using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The ball's line of action for a replay: built once when the replay starts (MatchDirector.ReplayLine.cs)
    /// from the engine's own path — the delivery's trajectory from the hand, then the flight off the bat —
    /// sampled every <see cref="Step"/> s of game time, in world space. The replay draws it (fixed vertices,
    /// growing behind the ball: ReplayPlayer.Line.cs) and the replayed ball rides on it (ReplayRecorder), so
    /// the ball and its line are one and the same and neither shivers (owner, deep areas: "the line seems to be
    /// shivering and not accurate").
    /// </summary>
    public sealed class ReplayLine
    {
        /// <summary>Game seconds between points (120 a second: a smooth curve however slow the replay runs).</summary>
        public const float Step = 1f / 120f;

        public readonly string Kind;
        public readonly List<float> Times = new List<float>();
        public readonly List<Vector3> Points = new List<Vector3>();

        /// <summary>The game time a test run takes its evidence frame at (the ball well along its line).</summary>
        public float MomentAt;

        public ReplayLine(string kind) => Kind = kind;

        public int Count => Times.Count;
        public float Start => Times.Count > 0 ? Times[0] : 0f;
        public float End => Times.Count > 0 ? Times[Times.Count - 1] : 0f;

        public void Add(float t, Vector3 p)
        {
            Times.Add(t);
            Points.Add(p);
        }

        /// <summary>How many points are at or before <paramref name="t"/>.</summary>
        public int Upto(float t)
        {
            int lo = 0, hi = Times.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (Times[mid] <= t) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>The ball on the line at <paramref name="t"/>; false outside it (or across a gap in it).</summary>
        public bool At(float t, out Vector3 p)
        {
            p = Vector3.zero;
            var n = Times.Count;
            if (n == 0 || t < Times[0] || t > Times[n - 1]) return false;
            var i = Upto(t);
            if (i >= n)
            {
                p = Points[n - 1];
                return true;
            }
            if (i == 0)
            {
                p = Points[0];
                return true;
            }
            var t0 = Times[i - 1];
            var t1 = Times[i];
            // Between the pieces (the ball onto the bat and off it, one live frame): no blend across a real gap.
            if (t1 - t0 > Step * 6f)
            {
                p = t - t0 < t1 - t ? Points[i - 1] : Points[i];
                return true;
            }
            p = Vector3.LerpUnclamped(Points[i - 1], Points[i], t1 - t0 > 1e-6f ? (t - t0) / (t1 - t0) : 0f);
            return true;
        }
    }
}
