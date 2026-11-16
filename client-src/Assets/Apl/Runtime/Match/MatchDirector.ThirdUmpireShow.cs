using System;
using System.Collections;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    public sealed partial class MatchDirector
    {
        private static readonly float PoppingCrease = GroundSpace.HalfPitch - 1.22f;

        /// <summary>
        /// The referral: the strip, two replay angles of the moment slowed through
        /// it, the frozen frame "CHECKING…", then the big screen. All on the frozen
        /// replay (no gameplay runs); a tap jumps to the verdict.
        /// </summary>
        private IEnumerator ThirdUmpireShow(Referral kind, BallResult r, bool decision, float moment)
        {
            if (_closeUp || !_replays.Enter())
            {
                _hud.EventGraphic(decision ? "OUT" : "NOT OUT", "THIRD UMPIRE", decision ? UiKit.Bad : UiKit.Good, 2f);
                yield return new WaitForSeconds(2f);
                yield break;
            }
            var check = kind == Referral.RunOut ? "RUN OUT" : kind == Referral.Stumping ? "STUMPING" : "CATCH";
            if (kind == Referral.RunOut) _roReplay = "tv";
            StallProbe.Mark($"third umpire: {check}");
            _hud.ShowReferral(check);
            var (a, b) = kind == Referral.Catch ? CatchAngles(moment) : CreaseAngles(kind == Referral.Stumping || r.RunOutAtKeeperEnd);
            _hud.ReferralStatus($"{check}  ·  {(kind == Referral.Catch ? "CLOSE-UP" : "SIDE-ON")}");
            yield return _replays.Segment(moment - 1.6f, moment + 0.5f, moment, a, 0.3f);
            if (!_replays.Skipped)
            {
                _hud.ReferralStatus($"{check}  ·  {(kind == Referral.Catch ? "REVERSE ANGLE" : "STUMP CAM")}");
                Apl.Client.Dev.FieldEvidence.Screen(Apl.Client.Dev.FieldEvidence.TvStumpCam, 1.95f); // test runs: at the moment (0.3 s at 1x, 0.5 s at 0.3x)
                yield return _replays.Segment(moment - 0.8f, moment + 0.4f, moment, b, 0.2f);
            }
            if (!_replays.Skipped)
            {
                _hud.ReferralStatus("CHECKING…");
                Apl.Client.Dev.FieldEvidence.Screen(Apl.Client.Dev.FieldEvidence.TvSideOn, 0.3f); // test runs: the frozen side-on frame
                yield return _replays.Hold(moment, 1.4f, a);
            }
            // The verdict, on the frozen frame.
            var skipped = _replays.Skipped;
            StallProbe.Mark($"third umpire: big screen ({(decision ? "out" : "not out")})");
            _replays.ClearSkip();
            yield return _hud.BigScreen(decision, skipped ? 0.5f : 1.6f);
            // At the batter's end the square-leg umpire passes it on.
            if (!decision && kind != Referral.Catch && (kind == Referral.Stumping || r.RunOutAtKeeperEnd)) SquareLegSignal("umpire_not_out");
            for (var waited = 0f; waited < 1.3f && !_replays.Skipped; waited += Time.unscaledDeltaTime)
            {
                _hud.ClockHeld();
                yield return null;
            }
            StallProbe.Mark("third umpire: exit");
            _hud.HideReferral();
            _replays.Exit();
            _skip = false;
        }

        /// <summary>Run-out / stumping: side-on along the popping crease, and front-on at the stumps (the bat, the bails).</summary>
        private (Func<float, (Vector3, Vector3, float)>, Func<float, (Vector3, Vector3, float)>) CreaseAngles(bool keeperEnd)
        {
            var sign = keeperEnd ? -1f : 1f;
            var crease = sign * PoppingCrease;
            var stumps = sign * GroundSpace.HalfPitch;
            var side = Refs.Umpire != null && Refs.Umpire.transform.position.x > 0 ? -1f : 1f;
            // Square on the line, and wide enough for the runner coming in as well as the line (r11 f360-366: only
            // the keeper and stumps were in it, so in or out couldn't be judged). His body replays the tape.
            var end = keeperEnd;
            (Vector3, Vector3, float) SideOn(float _)
            {
                var aim = new Vector3(0, 0.45f, crease);
                var fov = 28f;
                if (BatterFor(end) is Vector3 runner)
                {
                    var gap = runner.z - crease;
                    aim.z = crease + gap * 0.5f * Mathf.Clamp01(Mathf.Abs(gap) / 5f);
                    var half = Mathf.Atan((Mathf.Max(Mathf.Abs(runner.z - aim.z), Mathf.Abs(stumps - aim.z)) + 1.2f) / 8.5f);
                    fov = Mathf.Clamp(2f * Mathf.Atan(Mathf.Tan(half) / 1.6f) * Mathf.Rad2Deg, 28f, 60f);
                }
                return (new Vector3(side * 8.5f, 1.0f, crease), aim, fov);
            }
            (Vector3, Vector3, float) StumpCam(float _) => (new Vector3(side * 1.1f, 0.7f, crease - sign * 4.2f), new Vector3(0, 0.35f, stumps), 32f);
            return (SideOn, StumpCam);
        }

        /// <summary>A catch: close on the ball at the moment it was taken, from inside the ground and from the side.</summary>
        private (Func<float, (Vector3, Vector3, float)>, Func<float, (Vector3, Vector3, float)>) CatchAngles(float moment)
        {
            var at = _replays.BallAt(moment) ?? _ball.position;
            var outward = new Vector3(at.x, 0, at.z);
            outward = outward.sqrMagnitude > 1f ? outward.normalized : Vector3.forward;
            var across = new Vector3(outward.z, 0, -outward.x);
            // The look follows the ball in while it's on the tape, then holds the catch point.
            Vector3 Look(float t)
            {
                var l = _replays.BallAt(t) is Vector3 p && Vector3.Distance(p, at) < 6f ? Vector3.Lerp(at, p, 0.6f) : at;
                l.y = Mathf.Min(l.y, Mathf.Max(at.y, 1f) + 1.8f);
                return l;
            }
            (Vector3, Vector3, float) Inside(float t) => (at - outward * 4.6f + across * 1.4f + Vector3.up * 0.7f, Look(t), 30f);
            (Vector3, Vector3, float) Reverse(float t) => (at + across * 4.8f + Vector3.up * 0.9f, Look(t), 30f);
            return (Inside, Reverse);
        }
    }
}
