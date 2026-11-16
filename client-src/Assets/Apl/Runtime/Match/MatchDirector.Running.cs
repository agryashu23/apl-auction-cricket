using System.Collections;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Running between the wickets (rules-bowling). The engine proposes the safe
    /// runs (<see cref="RunPlan"/>). AUTO (and the AI side) runs them by itself,
    /// with a drag of the bat or a dive when it's tight. MANUAL (the user batting,
    /// <see cref="RunningPrefs"/>): RUN / CANCEL / DIVE — the calls are in
    /// MatchDirector.RunCalls.cs. Each run is the crease-to-crease clip; the man
    /// run out reacts and walks off. <see cref="RunState"/> is what the
    /// presentation's picture-in-picture reads.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private const float CallBy = 1.6f;
        private bool _runnerBusy;
        private BallResult? _wicketShown;

        /// <summary>The batters' running right now (the presentation's picture-in-picture reads it every frame).</summary>
        public RunningState RunState { get; } = new RunningState();

        /// <summary>From Hit (and the byes): the batters set off if there's anything to run for. Returns whether they do.</summary>
        private bool StartRunning(BallResult r, out float runTime)
        {
            RunState.Manual = false;
            runTime = 2.4f;
            var single = CricketClips.Run("run_single_crease_to_crease", false);
            if (_striker.UsesClips && single != null) runTime = single.EventTime + 0.3f;
            if (r.Running == null || r.Four || r.Six || r.Caught || r.ExtrasBoundary || (r.Wicket && !r.RunOut)) return false;
            // (-aplrunmanual: a test run's user side runs MANUAL, called by the scripted player — MatchDirector.RunOutReal.)
            var manual = _match.UserBatting && (Autoplay ? RunManualBot : !RunningPrefs.Auto) && _match.Log.Count > 0 && _match.Log[_match.Log.Count - 1] == r;
            if (!manual && r.Attempted == 0) return false;
            _runnerBusy = true;
            RunState.Manual = manual;
            StartCoroutine(RunBetween(r, runTime, manual));
            return true;
        }

        /// <summary>Running lanes either side of the stumps, and the two creases they run between.</summary>
        private (Vector3 laneA, Vector3 laneB, Vector3 near, Vector3 far) Lanes() => (
            Vector3.right * (-Mathf.Sign(_nonStrikerX) * 0.75f), Vector3.right * _nonStrikerX,
            new Vector3(0, 0, -GroundSpace.HalfPitch + 1.22f), new Vector3(0, 0, GroundSpace.HalfPitch - 1.22f));

        /// <summary>
        /// The last run, from <see cref="CallBy"/> into it: the man run out stops short and
        /// walks off when the wicket's broken; the other makes his ground — with a drag of
        /// the bat when it's tight, and (AUTO) a dive when it's very tight.
        /// </summary>
        private void Finish(BallResult r, int k, (BatterRig rig, Vector3 lane, bool fromNear)[] legs, float t0, bool autoDive)
        {
            var spare = r.Running!.Spare(k);
            var (_, _, near, far) = Lanes();
            foreach (var (rig, lane, fromNear) in legs)
            {
                if (r.RunOut && (rig == legs[0].rig) == r.RunOutStriker)
                {
                    OutReaction(r, rig, t0);
                    continue;
                }
                // A dive only when the engine's race really was that tight (its no-dive margin under a quarter
                // second) — not on every run with half a second to spare, which read as "saved by a dive" when
                // the throw had looked in time (owner v26).
                var margin = r.Running.LastMargin;
                var tight = margin < 0.25 && margin > -RunPlan.DiveGain;
                var fin = (r.Dived || autoDive && tight) ? "dive_into_crease" : spare < 1.2 ? "drag_bat_into_crease" : null;
                if (fin == "dive_into_crease") _runOutDiveShown = true;
                var run = CricketClips.Run("run_single_crease_to_crease", rig.LeftHanded);
                var finClip = fin != null ? CricketClips.Run(fin, rig.LeftHanded) : null;
                if (run == null || finClip == null) continue;
                var start = (fromNear ? near : far) + lane;
                var dir = fromNear ? Vector3.forward : Vector3.back;
                StartCoroutine(After(Mathf.Max(0, run.EventTime - finClip.EventTime - CallBy), () => rig.PlayRun(finClip, start, dir)));
            }
        }

        /// <summary>The man run out: his reaction and walk-off when the wicket's broken.</summary>
        private void OutReaction(BallResult r, BatterRig rig, float t0)
        {
            RunState.RunOut = rig;
            // (The engine's own break: a direct hit's arrival, a wide throw's late take — not always the throw back plus the gather.)
            var broken = (float)(r.BrokenAt > 0 ? r.BrokenAt : r.Running!.Back + RunPlan.Gather) - (Time.time - t0);
            // Still out when the bails come off (a dive in time may have saved him since).
            // (Run-out owner: never home on screen before the bails are off — MatchDirector.RunOutHold.)
            StartCoroutine(HoldOutShort(r, rig));
            StartCoroutine(After(Mathf.Max(0.2f, broken), () =>
            {
                if (r.RunOut && _heldShort != rig) rig.React("run_out_reaction_walk_off", walkOff: true);
                else if (RunState.RunOut == rig) RunState.RunOut = null;
            }));
        }

        /// <summary>Sent back: the striker brakes and scrambles back to his crease, the non-striker to his.</summary>
        private static void PlayCancel(BatterRig fromStriker, Vector3 strikerCrease, BatterRig fromBowler, Vector3 bowlerCrease)
        {
            var a = CricketClips.Run("cancel_run_striker", fromStriker.LeftHanded);
            if (a != null) fromStriker.PlayRun(a, strikerCrease, Vector3.forward, fromStriker.transform.position.x);
            var b = CricketClips.Run("cancel_run_nonstriker", fromBowler.LeftHanded);
            if (b != null) fromBowler.PlayRun(b, bowlerCrease, Vector3.back, fromBowler.transform.position.x);
        }

        /// <summary>From Hit's end: wait for the batters; a run out the user's call brought on is shown here, and the final runs.</summary>
        private IEnumerator RunsDone(BallResult r, string batterName)
        {
            while (_runnerBusy) yield return null;
            // A close one goes upstairs first (out: then the wicket; not out: the let-off) — the wicket
            // broken on screen first, and the replay on that moment (fielding: MatchDirector.RunOutBreak).
            yield return EnsureBroken(r);
            yield return Refer(r, RunOutMoment(r));
            // Not out by a whisker, not upstairs: seen side-on at the crease (MatchDirector.RunOutSeen).
            yield return CloseRunReplay(r);
            if (r.RunOut && _wicketShown != r) yield return Wicket(r, r.OutBatter?.Name ?? batterName);
            else if (!r.Wicket)
            {
                var sub = r.TurnedBack ? "sent back" : r.Byes > 0 ? "byes" : r.LegByes > 0 ? "leg byes" : r.Dived ? "dived in" : "";
                Show(r.Runs == 0 ? "NO RUN" : r.Runs == 1 ? "1 RUN" : $"{r.Runs} RUNS", sub, Color.white, 1.2f);
            }
            _hud.ShowScore(_match, BattingSide, FieldingSide);
            // The run-out trace (match-play: MatchDirector.RunOutTrace.cs).
            TraceRunOut(r);
        }
    }
}
