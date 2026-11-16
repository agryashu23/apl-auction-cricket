using System.Collections;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The running calls. MANUAL (owner, 2026-11-13: "RUN, CANCEL, DIVE available immediately after hitting the
    /// ball, until the ball is thrown back to the bowler or keeper; on overthrows they can run"):
    /// - the chips show the moment the ball is hit and stay until the ball's dead — back in the hands of the man
    ///   over the stumps (<see cref="BallBack"/>), or a backstop well after the engine's throw — or he's run out;
    /// - RUN goes for one more: before they go, during a run, or standing at the crease after one while the ball's
    ///   still live (the engine's race starts that run as late as he called it: QuickMatch.SettleRuns' hold);
    ///   after a shy that ran away (<see cref="BallResult.Overthrow"/>), RUN takes the overthrow;
    /// - CANCEL before they go is no run, early in a run (before <see cref="BackBefore"/> of it) turns them back
    ///   (<see cref="QuickMatch.TurnBack"/> — the run doesn't count, the throw can still beat one home), later it
    ///   stops them at the next crease;
    /// - DIVE is lit whenever they're running; diving makes that crease the last and re-settles the race with the
    ///   dive's gain — "DIVE!" when it would save him.
    /// AUTO / the AI: the engine's call (an overthrow run included), the dive or drag automatic.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>CANCEL before this share of a run turns them back; DIVE lit from this far into a run until this long before the crease (s).</summary>
        private const float BackBefore = 0.45f, DiveFrom = 0.3f, DiveUntil = 0.1f;

        /// <summary>MANUAL: the backstop on the live ball if it's never seen back in the hands (s after the engine's throw back; and at most).</summary>
        private const float LiveAfterBack = 2.5f, LiveMax = 12f;

        /// <summary>The throw this ball is back in the hands over the stumps (the ball's dead for running).</summary>
        private bool BallBack(float since) => _throwStart >= since && _throwHome && _ballPhase == "held";

        private IEnumerator RunBetween(BallResult r, float runTime, bool manual)
        {
            var plan = r.Running!;
            var t0 = Time.time;
            // MANUAL: nobody moves until RUN (the engine's own call is only the AI's / AUTO's). AUTO / the AI also run an
            // overthrow the engine gave them.
            var target = manual ? 0 : r.Ran > r.Attempted && !r.RunOut ? r.Ran : r.Attempted;
            var settled = !manual;
            var striker = _striker;
            var other = _other;
            var st = RunState;
            st.Active = true;
            st.Ball = r;
            st.Run = 0;
            st.Going = target;
            st.Safe = plan.Safe;
            st.StartedAt = t0;
            st.RunTime = runTime;
            st.FromStrikerEnd = striker;
            st.FromBowlerEnd = other;
            st.RunOut = null;
            st.TurnedBack = st.Dived = st.DiveLit = st.DiveMatters = false;
            bool run = false, cancel = false, dive = false;
            void OnRun() => run = true;
            void OnCancel() => cancel = true;
            void OnDive() => dive = true;
            var hideWhy = "running over";
            var liveUntil = t0 + Mathf.Min(LiveMax, Mathf.Max(4f, (float)plan.Back + LiveAfterBack));
            bool Live() => !BallBack(t0) && Time.time < liveUntil;
            if (manual)
            {
                _hud.ShowRunControls(true);
                _hud.RunPressed += OnRun;
                _hud.CancelPressed += OnCancel;
                _hud.DivePressed += OnDive;
                Debug.Log($"[APL runbtn] shown at t={Time.time - _hitAt:0.00}s after the hit (ball {_match.Over}.{_match.BallInOver}, engine throw back {plan.Back:0.00}s, safe {plan.Safe})");
                if (Autoplay) BotPlan(plan);
            }
            var (laneA, laneB, near, far) = Lanes();
            // The engine's extras on the player's calls: the late start, the wait at the crease before the last run, the overthrow.
            var late = 0f;
            var hold = 0f;
            var overthrowTaken = false;
            var noMore = false;
            try
            {
                // AUTO: a beat to call it before they go. MANUAL: they wait at the crease for RUN while the ball's live;
                // a late call costs them the time they stood there.
                while (manual ? Live() : Time.time - t0 < 0.35f)
                {
                    if (manual)
                    {
                        Keys(ref run, ref cancel, ref dive);
                        if (Autoplay) BotPress(Time.time - t0, target, false, false, ref run, ref dive);
                        if (run) target = Mathf.Min(4, target + 1);
                        var called = cancel;
                        if (cancel) target = 0;
                        run = cancel = dive = false;
                        if (target == 0) _hud.RunCall("RUN?", UiKit.Gold);
                        else _hud.RunHint(target, plan.Safe, plan.Risk(target + 1));
                        if (called || (target > 0 && Time.time - t0 >= 0.35f)) break;
                    }
                    st.Going = target;
                    yield return null;
                }
                if (manual) late = Mathf.Max(0f, Time.time - t0 - 0.35f);
                if (target == 0)
                {
                    if (manual)
                    {
                        _match.SettleRuns(0);
                        hideWhy = BallBack(t0) ? "ball back in the hands, no run called" : "no run called (backstop)";
                    }
                    // (MANUAL: they never moved — nothing to send back.)
                    if (!manual && plan.Safe > 0 && striker.UsesClips) PlayCancel(striker, near + laneA, other, far + laneB);
                    yield break;
                }
                var k = 0;
                while (true)
                {
                    if (k < target)
                    {
                        k++;
                        var leg = Time.time;
                        st.Run = k;
                        st.Dived = false;
                        // A leg run on the overthrow the engine has already counted: nothing more to settle.
                        var overthrowLeg = overthrowTaken && k == target;
                        var legs = new[] { (striker, laneA, k % 2 == 1), (other, laneB, k % 2 == 0) };
                        foreach (var (rig, lane, fromNear) in legs)
                        {
                            var clip = CricketClips.Run("run_single_crease_to_crease", rig.LeftHanded);
                            if (clip != null) rig.PlayRun(clip, (fromNear ? near : far) + lane, fromNear ? Vector3.forward : Vector3.back, k == 1 ? rig.transform.position.x : (float?)null);
                        }
                        var finished = false;
                        while (Time.time - leg < runTime)
                        {
                            var into = Time.time - leg;
                            var last = target <= k;
                            if (manual)
                            {
                                Keys(ref run, ref cancel, ref dive);
                                if (Autoplay) BotPress(Time.time - t0, target, st.DiveLit && last, st.DiveMatters, ref run, ref dive);
                                // One more while the ball's live (the engine's race decides it); after an overthrow, no more.
                                if (run && !overthrowLeg && !noMore && Live())
                                {
                                    if (settled && r.Overthrow && !overthrowTaken) TakeOverthrow(k);
                                    else if (!settled || !r.RunOut)
                                    {
                                        target = Mathf.Min(4, target + 1);
                                        settled = false;
                                    }
                                }
                                if (cancel && !st.Dived && !overthrowLeg)
                                {
                                    if (into < runTime * BackBefore)
                                    {
                                        hideWhy = "sent back";
                                        yield return TurnBack(r, k, into * (float)RunPlan.RunTime / runTime, legs, near, far, t0);
                                        yield break;
                                    }
                                    if (!settled) target = k;
                                }
                                run = cancel = false;
                            }
                            last = target <= k;
                            if (!settled && into >= CallBy && last)
                            {
                                // (The player's RUN is run as called — never quietly held back: deep area 3, Running.Commit.)
                                _match.SettleRuns(k, dive: st.Dived, late: late, playerCalled: manual, hold: hold, overthrowCalls: manual ? 0 : -1);
                                settled = true;
                            }
                            if (!finished && into >= CallBy && last)
                            {
                                finished = true;
                                Finish(r, k, legs, t0, autoDive: !manual);
                            }
                            // DIVE: lit whenever they're running (diving in makes this crease the last).
                            st.DiveLit = manual && !st.Dived && !overthrowLeg && into >= DiveFrom && into <= runTime - DiveUntil;
                            st.DiveMatters = st.DiveLit && last && settled && r.RunOut && plan.DiveMatters;
                            if (manual) _hud.SetDive(st.DiveLit, st.DiveMatters);
                            if (dive && st.DiveLit)
                            {
                                st.Dived = true;
                                if (!last)
                                {
                                    // He dives for this crease: they stop here.
                                    target = k;
                                    last = true;
                                }
                                if (settled) _match.SettleRuns(k, dive: true, late: late, playerCalled: manual, hold: hold, overthrowCalls: 0);
                                if (!r.RunOut) st.RunOut = null;
                                DiveNow(r, legs, near, far, leg);
                                _hud.RunCall("DIVE!", UiKit.Gold);
                            }
                            dive = false;
                            if (manual && !st.DiveMatters && !st.Dived && !overthrowLeg) _hud.RunHint(target, plan.Safe, plan.Risk(target + 1));
                            st.Going = target;
                            yield return null;
                        }
                        if (overthrowLeg) noMore = true;
                        if (r.RunOut && target <= k)
                        {
                            hideWhy = "run out";
                            break;
                        }
                        continue;
                    }
                    if (!manual) break;
                    // Made their ground, the ball still live: RUN for another (or the overthrow), until the ball's back.
                    st.DiveLit = st.DiveMatters = false;
                    _hud.SetDive(false, false);
                    var stood = Time.time;
                    var again = false;
                    while (Live() && !noMore)
                    {
                        Keys(ref run, ref cancel, ref dive);
                        if (Autoplay) BotPress(Time.time - t0, target, false, false, ref run, ref dive);
                        if (Autoplay && r.Overthrow && !overthrowTaken && BotTakesOverthrow(Time.time - stood)) run = true;
                        if (run)
                        {
                            if (r.Overthrow && !overthrowTaken) TakeOverthrow(k);
                            else
                            {
                                hold += Time.time - stood;
                                target = Mathf.Min(4, k + 1);
                                settled = false;
                            }
                            again = target > k;
                        }
                        run = cancel = dive = false;
                        if (again) break;
                        _hud.RunCall(r.Overthrow && !overthrowTaken ? "OVERTHROW · RUN!" : "RUN?", UiKit.Gold);
                        yield return null;
                    }
                    if (!again)
                    {
                        hideWhy = noMore ? "overthrow run made" : BallBack(t0) ? "ball back in the hands" : "ball live backstop";
                        break;
                    }
                }

                void TakeOverthrow(int made)
                {
                    // The shy missed and ran away: the overthrow's run, called — the engine counts it (it's a safe one).
                    overthrowTaken = true;
                    _match.SettleRuns(made, dive: false, late: late, playerCalled: true, hold: hold, overthrowCalls: 1);
                    target = made + 1;
                    _hud.RunCall("OVERTHROW!", UiKit.Good);
                    Debug.Log($"[APL runbtn] overthrow run called at t={Time.time - _hitAt:0.00}s after {made} run(s): {r.Runs} now");
                }
            }
            finally
            {
                if (manual)
                {
                    _hud.RunPressed -= OnRun;
                    _hud.CancelPressed -= OnCancel;
                    _hud.DivePressed -= OnDive;
                    _hud.ShowRunControls(false);
                    Debug.Log($"[APL runbtn] hidden at t={Time.time - _hitAt:0.00}s after the hit ({hideWhy}); ran {r.Ran}{(r.RunOut ? ", run out" : "")}{(r.Overthrow ? (overthrowTaken ? ", overthrow taken" : ", overthrow not taken") : "")}, ball back {(BallBack(t0) ? "Y" : "N")}");
                }
                _runnerBusy = false;
                st.Active = st.DiveLit = st.DiveMatters = false;
            }
        }
    }
}
