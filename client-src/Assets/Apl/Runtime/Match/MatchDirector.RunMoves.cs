using System.Collections;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>The running calls' moves: the keys, sending them back, the dive.</summary>
    public sealed partial class MatchDirector
    {
        /// <summary>R = run, X = cancel, D = dive (desktop).</summary>
        private static void Keys(ref bool run, ref bool cancel, ref bool dive)
        {
            run |= MatchHud.RunKey;
            cancel |= MatchHud.CancelKey;
            dive |= MatchHud.DiveKey;
        }

        /// <summary>
        /// CANCEL early in run <paramref name="k"/>: both brake, turn and scramble back
        /// to the creases they left (the cancel clips); the engine re-settles it (the
        /// run doesn't count) and the man the throw beats home is run out.
        /// </summary>
        private IEnumerator TurnBack(BallResult r, int k, float secondsIn, (BatterRig rig, Vector3 lane, bool fromNear)[] legs, Vector3 near, Vector3 far, float t0)
        {
            var st = RunState;
            st.TurnedBack = true;
            st.DiveLit = st.DiveMatters = false;
            _hud.SetDive(false, false);
            _hud.RunCall("BACK!", UiKit.Bad);
            _match.TurnBack(k, secondsIn);
            // Who left the keeper's end on this run, and who the bowler's.
            var (fromKeeper, keeperLane) = legs[0].fromNear ? (legs[0].rig, legs[0].lane) : (legs[1].rig, legs[1].lane);
            var (fromBowler, bowlerLane) = legs[0].fromNear ? (legs[1].rig, legs[1].lane) : (legs[0].rig, legs[0].lane);
            if (fromKeeper.UsesClips) PlayCancel(fromKeeper, near + keeperLane, fromBowler, far + bowlerLane);
            if (r.RunOut) OutReaction(r, r.RunOutStriker ? legs[0].rig : legs[1].rig, t0);
            // Home again: as long back as they went, and the turn.
            var home = Time.time + secondsIn + (float)RunPlan.TurnTime + 0.3f;
            while (Time.time < home) yield return null;
        }

        /// <summary>
        /// DIVE: the batter heading for the end the throw's going to (else the striker)
        /// launches so the dive lands on the line at the crease — or now, if it's
        /// already that late.
        /// </summary>
        private void DiveNow(BallResult r, (BatterRig rig, Vector3 lane, bool fromNear)[] legs, Vector3 near, Vector3 far, float legStart)
        {
            var toKeeperEnd = r.Running?.ToKeeperEnd ?? true;
            // Heading for the keeper's end: the one who set off from the bowler's.
            var who = legs[0].fromNear != toKeeperEnd ? legs[0] : legs[1];
            var run = CricketClips.Run("run_single_crease_to_crease", who.rig.LeftHanded);
            var dive = CricketClips.Run("dive_into_crease", who.rig.LeftHanded);
            if (run == null || dive == null) return;
            var start = (who.fromNear ? near : far) + who.lane;
            var dir = who.fromNear ? Vector3.forward : Vector3.back;
            var at = legStart + run.EventTime - dive.EventTime;
            var rig = who.rig;
            StartCoroutine(After(Mathf.Max(0f, at - Time.time), () => rig.PlayRun(dive, start, dir)));
        }
    }
}
