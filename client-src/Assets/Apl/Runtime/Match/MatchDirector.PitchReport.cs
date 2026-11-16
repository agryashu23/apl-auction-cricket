using System;
using System.Collections;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Owner v26 #23: the match's pitch type (QuickMatch.Pitch: the ground's weights, varied per match;
    /// Venue.DefaultPitch is the ground's usual one) dresses the strip (StadiumBuilder.Pitch.cs), plays
    /// in the engine (PitchPlay.cs: seam and bounce on a green top, turn on a dry one, little on a
    /// belter) and is shown before the toss: a PITCH REPORT shot looking down the strip with its
    /// caption. Test runs: <c>-aplpitch green|dry|flat|balanced</c> forces it for every match.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>-aplpitch: force the pitch (test runs); read before the first match is made.</summary>
        private static void ApplyPitchFlag()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-aplpitch");
            if (i < 0 || i + 1 >= args.Length) return;
            QuickMatch.ForcePitch = args[i + 1].ToLowerInvariant() switch
            {
                "green" or "seam" or "seaming" => Pitch.SeamFriendly,
                "dry" or "spin" or "turning" => Pitch.SpinFriendly,
                "flat" => Pitch.Flat,
                _ => Pitch.Balanced,
            };
            Debug.Log($"[APL pitch] -aplpitch: every match on a {QuickMatch.ForcePitch} pitch");
        }

        /// <summary>The match is made (or remade): the strip in its pitch's look.</summary>
        private void ShowPitchType()
        {
            if (_match == null) return;
            if (StadiumBuilder.PitchShown != _match.Pitch || !_pitchDressed) StadiumBuilder.PitchFor(_match.Pitch);
            _pitchDressed = true;
            Debug.Log($"[APL pitch] {_venue?.Name}: {_match.Pitch} (the ground's usual: {_venue?.DefaultPitch})");
        }

        private bool _pitchDressed;

        /// <summary>Before the full match replaces the quick chase made at Start: keep the pitch the toss was called on.</summary>
        private void CarryPitch()
        {
            if (_match != null) QuickMatch.NextPitch = _match.Pitch;
        }

        /// <summary>
        /// The pitch report before the coin goes up: looking down the strip from behind the bowler's
        /// stumps, a little above, with what it is and what it'll do. SKIP moves on.
        /// </summary>
        private IEnumerator PitchReport()
        {
            if (_match == null) yield break;
            var (name, note) = PitchEffect.Describe(_match.Pitch);
            Camera.Track(() => (new Vector3(0.6f, 3.1f, GroundSpace.HalfPitch + 4.2f), new Vector3(0, 0, -2.5f)), fov: 38f, smooth: 0.25f, cut: true);
            _pre.Caption($"PITCH REPORT  ·  {name}  ·  {note.ToUpperInvariant()}");
            StartCoroutine(After(0.6f, () => Apl.Client.Dev.AutoShot.Moment("pitch_report")));
            Debug.Log($"[APL pitch] pitch report: {name} ({note})");
            yield return Hold(Secs(2.6f));
            _pre.Caption(null);
        }
    }
}
