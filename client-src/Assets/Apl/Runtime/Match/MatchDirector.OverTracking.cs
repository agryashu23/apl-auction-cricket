using System.Collections;
using System.Collections.Generic;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// OVER IN TRACKING (owner v26 #20, revised): at each over break, before the over card, every
    /// delivery of the over just bowled on one view from behind the batter's stumps — the flights
    /// as thin tubes and the pitch discs (OverTrackView), numbered 1, 2, 3 … in the order bowled, in
    /// their outcome's colour (dot, runs, boundary, wicket, extra), with a small colour key
    /// (MatchHud.OverTracking.cs). No verdict panel: that's the DRS review's. Tap skips; it closes
    /// itself after ~5 s. An over that was simulated (all or part) isn't shown: its balls never
    /// reached AfterBall, so the recorded count doesn't match the over's.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private readonly List<OverBall> _overTracks = new List<OverBall>();
        private OverTrackView? _overView;

        /// <summary>The outcome colours (the key's order).</summary>
        public static readonly Color TrackDot = new Color(0.55f, 0.62f, 0.78f), TrackRuns = Color.white, TrackBoundary = new Color(0.95f, 0.76f, 0.31f),
            TrackWicket = new Color(0.95f, 0.3f, 0.3f), TrackExtra = new Color(1f, 0.55f, 0.15f);

        private const int TrackPoints = 16;

        /// <summary>The ball's flight into this over's book: ~16 points on the engine's own path, release to the bat / pad.</summary>
        private void RecordTrack(BallResult r, bool leftHanded)
        {
            var t = r.Trajectory;
            var d = r.Delivery;
            var impactZ = System.Math.Max(0.35, Batting.ContactZ(d.Length) - 0.45);
            var end = t.TimeAt(impactZ);
            // A near-dead ball can take "minutes" to arrive (the Drs.cs cases): the flight stops at 3 s.
            if (double.IsNaN(end) || double.IsInfinity(end) || end <= 0) end = t.BounceTime;
            end = System.Math.Min(end, 3.0);
            var bounce = d.FullToss ? end : System.Math.Min(t.BounceTime, end);
            var b = new OverBall
            {
                Bounce = GroundSpace.Ball(d.BounceX, 0, d.BounceZ, leftHanded),
                FullToss = d.FullToss || d.BounceZ < impactZ,
                Length = LengthName(d.Length),
            };
            // Half the points to the bounce, half after it, so the kink at the bounce is exact.
            const int half = TrackPoints / 2;
            for (var i = 0; i <= half; i++) Add(b, t.At(bounce * i / half), leftHanded);
            if (end > bounce)
                for (var i = 1; i <= half; i++) Add(b, t.At(bounce + (end - bounce) * i / half), leftHanded);
            (b.Outcome, b.Colour) =
                r.Wicket ? ("OUT", TrackWicket)
                : r.Wide ? ("WIDE", TrackExtra)
                : r.NoBall ? ("NO BALL", TrackExtra)
                : r.Six ? ("SIX", TrackBoundary)
                : r.Four ? ("FOUR", TrackBoundary)
                : r.Runs > 0 ? (r.Runs == 1 ? "1 RUN" : $"{r.Runs} RUNS", TrackRuns)
                : ("DOT", TrackDot);
            _overTracks.Add(b);
        }

        private static void Add(OverBall b, (double x, double y, double z) p, bool leftHanded)
        {
            var w = GroundSpace.Ball(p.x, p.y + 0.036, p.z, leftHanded);
            if (!float.IsNaN(w.x) && !float.IsNaN(w.y) && !float.IsNaN(w.z) && !float.IsInfinity(w.z)) b.Path.Add(w);
        }

        /// <summary>How many deliveries (legal or not) the over just finished had, from the book.</summary>
        private int LastOverBalls()
        {
            var log = _match.Log;
            int i = log.Count - 1, n = 0, legal = 0;
            while (i >= 0 && legal < 6)
            {
                if (log[i].Legal) legal++;
                n++;
                i--;
            }
            // Wides / no-balls before the over's first legal ball are its too.
            while (i >= 0 && !log[i].Legal)
            {
                n++;
                i--;
            }
            return n;
        }

        /// <summary>The over break's tracking view (nothing if the over wasn't all played ball by ball).</summary>
        private IEnumerator OverTracking()
        {
            if (_overTracks.Count == 0) yield break;
            var balls = new List<OverBall>(_overTracks);
            _overTracks.Clear();
            if (balls.Count != LastOverBalls() || _match.LegalBalls % 6 != 0 || _match.LegalBalls == 0) yield break;
            if (balls.Count > OverTrackView.Max) balls.RemoveRange(0, balls.Count - OverTrackView.Max);
            _overView ??= OverTrackView.Create();
            var over = _match.LegalBalls / 6;
            var bowler = _hud.OverBowlers.TryGetValue(over - 1, out var p) ? p.Name.ToUpperInvariant() : "";
            var cam = ViewCamera;
            _hud.Broadcasting(true);
            _hud.SetSkippable(true);
            _skip = false;
            HideCast(true);
            _overView.Show(balls);
            var spots = new List<(Vector3 at, Color c)>();
            foreach (var b in balls) spots.Add((b.FullToss && b.Path.Count > 0 ? b.Path[b.Path.Count - 1] : b.Bounce, b.Colour));
            _hud.ShowOverTracking($"OVER {over} IN TRACKING", bowler);
            // Behind and above the batter's stumps, looking up the pitch: the lengths and lines read at once.
            var stumpsZ = -GroundSpace.HalfPitch;
            Camera.TrackLens(() => (new Vector3(0, 2.9f, stumpsZ - 7.5f), new Vector3(0, 0.1f, stumpsZ + 6.5f), 38f), smooth: 0f, cut: true, priority: MatchCamera.Replay);
            var t = 0f;
            var shot = false;
            try
            {
                while (!_skip && t < 5.2f)
                {
                    _hud.ClockHeld();
                    if (!_hud.Paused) t += Time.unscaledDeltaTime;
                    var k = Mathf.Clamp01(t / 2.2f);
                    _overView.Grow(k);
                    _hud.OverTrackLabels(cam, spots, Mathf.Min(spots.Count, Mathf.CeilToInt(k * spots.Count + 0.001f)));
                    if (!shot && t > 2.5f)
                    {
                        shot = true;
                        Apl.Client.Dev.AutoShot.Moment("over_tracking");
                    }
                    yield return null;
                }
            }
            finally
            {
                _overView.Hide();
                HideCast(false);
                _hud.HideOverTracking();
                _hud.SetSkippable(false);
                _hud.Broadcasting(false);
                Camera.Release(MatchCamera.Replay);
                _skip = false;
            }
        }
    }
}
