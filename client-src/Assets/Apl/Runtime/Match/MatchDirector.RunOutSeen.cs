using System.Collections;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Deep area 3, developer 2 (owner: "run-out misses too much, I don't even see it"): every run-out chance
    /// is seen. When the wicket's broken the test runs check the live frame (the runner, the popping crease,
    /// no inset over it) for the trace line; a run out given on the field gets its side-on wicket replay
    /// (WicketReplay), a sub-0.1 s call goes upstairs (ThirdUmpireShow), and a let-off by under 0.3 s that
    /// didn't go upstairs gets a short side-on replay at the crease ("just in") — it used to pass unseen.
    /// <para>Referral rule (PM decision D1; engine <see cref="Running.Refer"/> = 0.02 s): within 0.02 s either way →
    /// the third umpire; out by more → given on the field, then the side-on wicket replay at the broken end; not out
    /// by up to 0.3 s → the close-call replay, no referral; by more → nothing extra.</para>
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>A let-off this close (s, the engine's no-dive margin) gets the side-on replay.</summary>
        private const double CloseReplayMargin = 0.3;

        /// <summary>This ball's run-out evidence for the trace: what the live frame showed as the bails came off, and which replay ran.</summary>
        private string _roVisible = "", _roReplay = "none";

        /// <summary>Just after the bails come off (BreakWicket): were the runner and the crease in the live frame, the inset off?</summary>
        private void NoteRunOutFrame(bool keeperEnd)
        {
            if (!Autoplay) return;
            // At the break itself (not 0.1 s on, after the reaction has started): both batters' hips from that crease
            // (+ past it, − short), named by who faced — the review asked for both to check the out-batter mapping.
            float From(BatterRig? rig)
            {
                if (rig == null) return float.NaN;
                var z = Hips(rig).position.z;
                var line = GroundSpace.HalfPitch - 1.22f;
                return keeperEnd ? -line - z : z - line;
            }
            var st = RunState;
            var both = st.Ball == _hitResult
                ? $"striker {From(st.FromStrikerEnd):+0.0;-0.0} m, non-striker {From(st.FromBowlerEnd):+0.0;-0.0} m from that crease"
                : "batters not running";
            var pastAtBreak = PastCrease(keeperEnd);
            Extra(After(0.1f, () =>
            {
                var cam = ViewCamera;
                if (cam == null) return;
                bool In(Vector3 w)
                {
                    var v = cam.WorldToViewportPoint(w);
                    return v.z > 0f && v.x > 0.02f && v.x < 0.98f && v.y > 0.02f && v.y < 0.98f;
                }
                var sign = keeperEnd ? -1f : 1f;
                var creaseZ = sign * (GroundSpace.HalfPitch - 1.22f);
                var crease = In(new Vector3(-0.6f, 0.02f, creaseZ)) && In(new Vector3(0.6f, 0.02f, creaseZ));
                var runner = BatterFor(keeperEnd) is Vector3 h && In(h) && In(new Vector3(h.x, 0.05f, h.z));
                var insetOff = !_runnerBusy || Camera.StumpsOnAir;
                // The picture at the break (F4 frame 164 read "in" while the engine had him 16 m short): the runner's
                // hips past (+) or short (−) of the crease, and the man breaking it — how far from the stumps, ball in hand.
                var past = pastAtBreak;
                var stumps = new Vector3(0, 0, sign * GroundSpace.HalfPitch);
                var collector = keeperEnd ? Refs.Keeper : _receiver != null ? _receiver : Refs.Bowler;
                var held = "N";
                var near = -1f;
                if (collector != null)
                {
                    near = Vector3.Distance(Flat(collector.transform.position), stumps);
                    var cup = Mathf.Min(Vector3.Distance(_ball.position, HandPoint.Cup(collector)), Vector3.Distance(_ball.position, HandPoint.Palm(collector, false)));
                    held = cup < 0.2f ? "Y" : "N";
                }
                var direct = _hitResult != null && _hitResult.DirectHit;
                _roVisible = $"runner-in-frame {(runner ? "Y" : "N")} crease-in-frame {(crease ? "Y" : "N")} inset-off {(insetOff ? "Y" : "N")}"
                    + $"; runner {past:+0.0;-0.0} m from the crease (picture, PastCrease; {both}), collector {(collector != null ? collector.name : "-")} {near:0.0} m from the stumps, ball in hand {(direct ? "direct hit" : held)}";
            }));
        }

        /// <summary>
        /// +1 or −1: the side of the pitch (x) a side-on camera <paramref name="dist"/> m out should stand to see
        /// <paramref name="aim"/> with no fielder in the way — the +x side unless someone's body is in its line of sight
        /// and the other side is clearer. (The man breaking the wicket, at the aim itself, doesn't count.)
        /// </summary>
        private float ClearSide(Vector3 aim, float dist)
        {
            float Blocked(float side)
            {
                var cam = new Vector3(side * dist, 1.0f, aim.z);
                var worst = 0f;
                foreach (var (a, _) in _fielders)
                {
                    if (a == null || !a.gameObject.activeInHierarchy) continue;
                    var h = Hips(a).position;
                    var ab = aim - cam;
                    var k = Mathf.Clamp01(Vector3.Dot(h - cam, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    if (k > 0.8f) continue; // at the stumps: in the shot, not in the way
                    var off = Vector3.Distance(new Vector3(h.x, 0, h.z), new Vector3(cam.x + ab.x * k, 0, cam.z + ab.z * k));
                    if (off < 0.8f) worst = Mathf.Max(worst, 1f - off / 0.8f);
                }
                return worst;
            }
            var plus = Blocked(1f);
            return plus > 0f && Blocked(-1f) < plus ? -1f : 1f;
        }

        /// <summary>A let-off by a whisker that didn't go upstairs: side-on at the crease he just made, slowed through it.</summary>
        private IEnumerator CloseRunReplay(BallResult r)
        {
            var p = r.Running;
            if (p == null || r.RunOut || r.Referral == TvCheck.RunOut || r.Attempted == 0 || double.IsNaN(r.BatIn)) yield break;
            if (p.LastMargin == double.MaxValue || p.LastMargin < 0 || p.LastMargin >= CloseReplayMargin) yield break;
            if (_closeUp || _replays == null || !_replays.Enter()) yield break;
            _roReplay = "close";
            var keeperEnd = p.ToKeeperEnd;
            var moment = _hitAt + (float)r.BatIn;
            var (oldest, newest) = _replays.Span;
            moment = Mathf.Clamp(moment, oldest + 1.4f, newest - 0.2f);
            var (sideOn, _) = CreaseAngles(keeperEnd);
            yield return _replays.Segment(moment - 1.4f, Mathf.Min(newest, moment + 0.4f), moment, sideOn, 0.35f);
            if (!_replays.Skipped)
            {
                // (Test runs: a shot on the frozen moment — the bat and the line — 0.3 s real time into the hold, ball-tagged. Review R5.)
                Apl.Client.Dev.FieldEvidence.Screen(Apl.Client.Dev.FieldEvidence.ReplayKey, 0.3f);
                yield return _replays.Hold(moment, 0.6f, sideOn);
            }
            _replays.Exit();
            _replays.ClearSkip();
            _skip = false;
            Show("SAFE", "just in", UiKit.Good, 1.0f);
        }
    }
}
