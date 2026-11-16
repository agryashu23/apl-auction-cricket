using System.Collections;
using System.Collections.Generic;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Ball tracking, owner v26 #20 (revised): "don't show ball tracking after every ball, record
    /// it and show all balls at the end of the over". Every delivery played (legal or not) has its
    /// flight recorded as it's booked — release → bounce → the bat / pad, ~16 points straight from
    /// the engine's Trajectory (no Hawk-Eye projection, so nothing for Drs.cs's caps to catch) — and
    /// at the over break, before the over card, OVER IN TRACKING draws them all together from the
    /// batter's end (MatchDirector.OverTracking.cs). No per-ball card any more, and no verdict panel
    /// (that's the DRS review's alone). Every ball's pitching point also goes on this over's pitch map.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>This over's pitching points: world x, distance from the batter's stumps, and how the ball went.</summary>
        private readonly List<(float x, float z, Color c)> _overPitches = new List<(float, float, Color)>();

        /// <summary>The end of every ball (both innings): note where it pitched and record its flight for the over's tracking.</summary>
        private IEnumerator AfterBall(BallResult r)
        {
            StallProbe.Mark("after ball");
            SquareLegWatching(false);
            if (r.Delivery == null || r.Trajectory == null) yield break;
            var lh = _facing?.LeftHanded ?? (_striker != null && _striker.LeftHanded);
            var at = GroundSpace.Ball(r.Delivery.BounceX, 0, r.Delivery.BounceZ, lh);
            _overPitches.Add((at.x, (float)r.Delivery.BounceZ, PitchColour(r)));
            RecordTrack(r, lh);
            var fifty = BookShot(r);
            CommentaryAfter(r);
            if (r.Runs == 0 && !r.Wicket && !r.Wide) KeeperChatter();
            if (fifty && _facing != null) yield return WagonMoment(_facing, $"{_facing.Name.ToUpperInvariant()}  ·  FIFTY");
        }

        private static string LengthName(Length l) => l switch
        {
            Length.Yorker => "YORKER",
            Length.Full => "FULL",
            Length.Good => "GOOD LENGTH",
            Length.Short => "SHORT",
            _ => "BOUNCER",
        };

        /// <summary>A pitch-map dot's colour: wicket red, boundary gold, runs white, dot grey.</summary>
        private static Color PitchColour(BallResult r) =>
            r.Wicket ? UiKit.Bad : r.Four || r.Six ? UiKit.Gold : r.Runs > 0 ? Color.white : new Color(0.45f, 0.55f, 0.75f);
    }
}
