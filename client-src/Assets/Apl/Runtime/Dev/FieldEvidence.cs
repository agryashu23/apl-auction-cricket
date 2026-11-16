using System;
using System.Collections;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// Area 3 fielding evidence: TEST RUNS ONLY. Every call does nothing outside a <c>-aplshots</c> run.
    /// The developers call <see cref="Moment"/> from their log points (build\review\deep\fielding_testplan.md §3);
    /// it takes the game's own frame as an AutoShot moment, <c>NN_moment_&lt;kind&gt;_&lt;ball&gt;.png</c>, and with
    /// <c>-aplfieldcloseup</c> the two close-ups (FieldCloseUp). The ball tag is <c>i&lt;innings&gt;_b&lt;overs&gt;</c>,
    /// the same ball as the <c>[APL runout]</c> lines. Agreed kinds:
    /// <list type="bullet">
    /// <item><see cref="Gather"/>: dev 1, the hands meet the ball (pick-up / stop that becomes a throw); focus the ball, line from the target.</item>
    /// <item><see cref="Set"/>: dev 1, 0.1 s before the release; focus the ball in his hand, line from the target.</item>
    /// <item><see cref="Release"/>: dev 1, the release frame (where <c>[APL throw]</c> is logged); focus the ball, line from the target.</item>
    /// <item><see cref="Taken"/>: dev 1/2, the receiver or keeper has the throw; focus the ball, line from the thrower.</item>
    /// <item><see cref="Stop"/>: dev 2, a ground stop (hands, not shins); focus the ball, line from the batter.</item>
    /// <item><see cref="TvSideOn"/>, <see cref="TvStumpCam"/>, <see cref="ReplayKey"/>: the replays (screen only, already hooked).</item>
    /// </list>
    /// Catches keep the existing <c>catch_hands</c> / <c>catch_held</c> moments (the close-ups hang off those).
    /// </summary>
    public static class FieldEvidence
    {
        public const string Gather = "gather", Set = "set", Release = "release", Taken = "taken", Stop = "stop",
            TvSideOn = "tv_sideon", TvStumpCam = "tv_stumpcam", ReplayKey = "replay_key";

        /// <summary>A -aplshots run is filming (cheap: callers may skip their own work when false).</summary>
        public static bool Active => AutoShot.Active;

        /// <summary>
        /// The game's frame now as a moment, plus (-aplfieldcloseup) a side and a front close-up of
        /// <paramref name="focus"/>, square to the line from <paramref name="lineFrom"/> (null: the ball's own motion).
        /// </summary>
        public static void Moment(string kind, Vector3 focus, Vector3? lineFrom = null)
        {
            if (!AutoShot.Active) return;
            var tag = Tag(kind);
            AutoShot.Moment(tag);
            FieldCloseUp.Explicit(kind);
            FieldCloseUp.Capture(tag, () => focus, lineFrom, 0f);
        }

        /// <summary>The game's frame as a moment <paramref name="realDelay"/> real seconds from now (works through a frozen replay).</summary>
        public static void Screen(string kind, float realDelay = 0f)
        {
            if (!AutoShot.Active) return;
            var tag = Tag(kind);
            if (realDelay <= 0f)
            {
                AutoShot.Moment(tag);
                return;
            }
            FieldCloseUp.Host()?.StartCoroutine(Later(tag, realDelay));
        }

        private static IEnumerator Later(string tag, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            AutoShot.Moment(tag);
        }

        private static Match.MatchDirector? _director;

        /// <summary><paramref name="kind"/> with this ball's tag (<c>release_i1_b2.5</c>).</summary>
        internal static string Tag(string kind)
        {
            if (_director == null) _director = UnityEngine.Object.FindAnyObjectByType<Match.MatchDirector>();
            var ball = _director != null ? _director.EvidenceBall : "";
            return ball.Length > 0 ? $"{kind}_{ball}" : kind;
        }
    }
}
