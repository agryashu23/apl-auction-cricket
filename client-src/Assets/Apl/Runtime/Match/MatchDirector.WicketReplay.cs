using System.Collections;
using System.Linq;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Wicket replays that show the decision (r9 rating: run-outs and catches replayed only
    /// the stroke). Two passes on the tape: the stroke side-on, then the decisive moment —
    /// a catch from the batter's side of the catcher, ending on his hands; a run-out or a
    /// stumping square on to the broken stumps, along the crease, with the throw coming in
    /// — slowed through it, held on the frame. Other wickets keep the ordinary replay.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>When the ball went into the catcher's hands this ball (MatchDirector.CatchClean.cs), else −1.</summary>
        private float _caughtAt = -1f;

        private IEnumerator WicketReplay(BallResult r, bool hit)
        {
            var runOut = r.HowOut == "run out" || r.HowOut == "stumped";
            if (!r.Caught && !runOut)
            {
                yield return ReplayMoment(r, hit);
                yield break;
            }
            StallProbe.Mark($"wicket replay: enter ({(runOut ? "run-out" : "catch")})");
            var enterT = StallProbe.Start();
            var entered = !_closeUp && _released >= 0 && _replays.Enter();
            StallProbe.End("wicket replay enter", enterT);
            if (!entered) yield break;
            if (r.HowOut == "run out" && _roReplay == "none") _roReplay = "wicket";
            var lh = _striker != null && _striker.LeftHanded;
            var off = lh ? -1f : 1f;
            var atBat = RealAt(r.Trajectory.TimeAt(Batting.ContactZ(r.Delivery.Length)));
            var focus = hit && _contactTime > 0 ? _contactTime : atBat;
            var (oldest, newest) = _replays.Span;
            var end = Mathf.Min(newest, _eventTime > 0 ? _eventTime + 0.4f : newest);
            // A run-out's moment is when the wicket was broken (MatchDirector.RunOutView.cs), not when the
            // umpire's call came: the replay ends just after it, so the bat and the line are both there.
            if (runOut && _brokenAt > 0 && _brokenAt > oldest) end = Mathf.Min(newest, _brokenAt + 0.7f);
            // A catch ends just after the ball is in his hands (r11 f282-284 showed only the edge).
            var caught = r.Caught && _caughtAt > oldest;
            if (caught) end = Mathf.Min(newest, _caughtAt + 0.6f);
            var stumps = new Vector3(0, 0, -GroundSpace.HalfPitch);

            // 1. The stroke (a stumping's is the decision itself: straight to pass 2). A run out goes straight to the
            // broken end too (deep area 3, F4 168–170: the stroke pass at the striker's end read as the wrong end).
            if (r.HowOut != "stumped" && !runOut)
            {
                var strokeEnd = Mathf.Min(focus + 0.9f, end);
                yield return _replays.Segment(Mathf.Max(oldest, _released - 0.4f), strokeEnd, focus,
                    _ => (stumps + new Vector3(off * 6.5f, 1.4f, 1.6f), stumps + new Vector3(0, 0.9f, 0.4f), 30f), 0.4f);
            }

            StallProbe.Mark("wicket replay: decision pass");
            // 2. The decision: where the ball was just before the end (the hands, the stumps).
            var key = runOut && _brokenAt > 0 && _brokenAt > oldest ? _brokenAt : caught ? _caughtAt : end - 0.45f;
            var at = _replays.BallAt(key) ?? stumps;
            // The batter run out (his body replays the tape too): the run-out lens keeps him in shot.
            var outId = r.OutBatter?.Id ?? _match.Order.LastOrDefault(o => o.HowOut != null)?.Player.Id;
            // Keyed to his BAT (the thing that has to be over the line), else his hips.
            var runner = outId != null && _batters.TryGetValue(outId, out var outRig) && outRig != null ? (outRig.Bat != null ? outRig.Bat : Hips(outRig)) : null;
            // (Deep area 3, Unity review r1: the run-out man is the rig that RAN this ball — RunState — not the id map.)
            var st = RunState;
            if (r.RunOut && st.Ball == r && (r.RunOutStriker ? st.FromStrikerEnd : st.FromBowlerEnd) is BatterRig ranRig && ranRig != null)
                runner = ranRig.Bat != null ? ranRig.Bat : Hips(ranRig);
            // The end he was run out at: the engine's word, else where the ball broke the wicket.
            var outAtKeeperEnd = r.HowOut == "stumped" || (r.RunOut ? r.RunOutAtKeeperEnd : at.z < 0);
            float? decisionSide = null;
            (Vector3, Vector3, float) Decision(float t)
            {
                if (runOut)
                {
                    // Square on the popping crease at the broken end, low: the line, the bat sliding in and the
                    // stumps a stride behind it all in one frame (r10: the stumps and the keeper only).
                    var z = outAtKeeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch;
                    var creaseZ = z + (z < 0 ? 1.22f : -1.22f);
                    var mid = (creaseZ + z) / 2f;
                    var aim = new Vector3(0, 0.45f, Mathf.Lerp(creaseZ, mid, 0.3f));
                    var fov = 32f;
                    var dist = 7.5f;
                    // The runner in it too (r11 f360-366: only the keeper and stumps were): still square on the line,
                    // but turned toward him and wide enough that his bat (and the line) are both in the frame.
                    if (runner != null)
                    {
                        var rz = runner.position.z;
                        var gap = Mathf.Abs(rz - creaseZ);
                        // (Deep area 3, review r1: never more than 4 m off the line — the crease stays in the middle third.)
                        aim.z = Mathf.MoveTowards(creaseZ, Mathf.Lerp(aim.z, (rz + creaseZ) / 2f, Mathf.Clamp01(gap / 6f)), 4f);
                        var need = Mathf.Max(Mathf.Abs(rz - aim.z), Mathf.Abs(z - aim.z)) + 1.2f;
                        var half = Mathf.Atan(need / 7.5f);
                        // (At most 46°: a man yards short is plainly out; the crease and bat must stay readable — review r1.)
                        fov = Mathf.Clamp(2f * Mathf.Atan(Mathf.Tan(half) / 1.6f) * Mathf.Rad2Deg, 30f, 46f);
                        // Square on to the aim, pulled back (not widened past 46°) until the span fits across the frame.
                        dist = Mathf.Clamp(need / (Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 16f / 9f), 7.5f, 16f);
                    }
                    // (The side with no fielder standing in the line of sight — review b3: replay 180 blocked by one.)
                    // (Chosen once for the replay: never a flip of sides mid-shot.)
                    var sideX = decisionSide ??= ClearSide(aim, dist);
                    return (new Vector3(sideX * dist, 0.7f + (dist - 7.5f) * 0.06f, aim.z), aim, fov);
                }
                // The catch: 7 m back toward the pitch from his hands and a little aside, on his front.
                var flat = new Vector3(at.x, 0, at.z);
                var back = (Vector3.zero - flat).sqrMagnitude > 1f ? (Vector3.zero - flat).normalized : Vector3.forward;
                var side = Vector3.Cross(Vector3.up, back);
                var ball = _replays.BallAt(t);
                var look = Vector3.Lerp(ball ?? at, at, 0.6f);
                // Never up into the night sky after a skyer (r12 P155): the aim stays within 1.8 m above his hands,
                // so the catcher and the ground stay in the frame and the ball drops into it.
                look.y = Mathf.Min(look.y, Mathf.Max(at.y, 1f) + 1.8f);
                return (at + back * 7f + side * 2.2f + Vector3.up * 1.3f, look, 34f);
            }
            if (!_replays.Skipped)
            {
                var from = Mathf.Max(focus + 0.9f, end - (runOut ? 2.4f : 2.0f));
                if (r.HowOut == "stumped") from = Mathf.Max(oldest, focus - 0.6f);
                // A quick catch (behind, the slips) still gets its second angle: from a second before the take.
                if (caught) from = Mathf.Max(oldest, Mathf.Min(from, key - 1.0f));
                yield return _replays.Segment(from, end, key, Decision, 0.3f);
            }
            // The freeze on the moment: a run-out held longer (the bat and the line are what decide it).
            if (!_replays.Skipped) Apl.Client.Dev.FieldEvidence.Screen(Apl.Client.Dev.FieldEvidence.ReplayKey, 0.3f); // test runs: the frozen key frame
            if (!_replays.Skipped) yield return _replays.Hold(key + 0.02f, runOut ? 1.6f : caught ? 1.3f : 0.9f, Decision);
            StallProbe.Mark("wicket replay: exit");
            _replays.Exit();
            _skip = false;
        }
    }
}
