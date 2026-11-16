using System;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Evidence nudges for test runs, only with <c>-aplnudges</c> on top of the autoplay
    /// (never in a real match; a plain <c>-aplmatchtest</c> run plays straight, for honest
    /// totals). In the innings the user's side bowls, the run is steered so a short capture
    /// films what the engine makes rare: a no-ball and the free hit, a six, an lbw that's
    /// reviewed (DRS), a stumping off the spinner, two catches, and (once an innings) a risky
    /// extra run for a take-and-break run out; owner v26 #18: a wide outside off (left by the
    /// AI batter, called, bowled again) and an over with three bouncers (1st, 2nd, then the
    /// no-ball and free hit). Each outcome is still the engine's honest
    /// result for the ball and shot the nudge sets up; each gets an AutoShot moment.
    /// </summary>
    public sealed partial class MatchDirector
    {
        public enum Evidence { None, NoBall, Six, Lbw, Stumping, Catch, Wide, Bouncer }

        private bool? _nudges;

        /// <summary>The evidence nudges are on (autoplay and -aplnudges).</summary>
        private bool Nudges => _nudges ??= Autoplay && Environment.GetCommandLineArgs().Contains("-aplnudges");

        private Evidence _evidence;
        private int _sixTries, _lbwTries, _stumpTries, _evidenceInnings = -1;
        private int _runOutNudged, _wideTries, _bouncerTries;

        /// <summary>What this ball of the user's bowling is set up to show (None: bowled as the autoplay likes).</summary>
        private Evidence PlanEvidence(Player bowler)
        {
            if (CatchLoop) return Evidence.Catch; // -aplcatchloop: every ball a catch chance (MatchDirector.CatchLoop)
            if (!Nudges) return Evidence.None;
            if (_evidenceInnings != _match.InningsNumber)
            {
                _evidenceInnings = _match.InningsNumber;
                _sixTries = _lbwTries = _stumpTries = _wideTries = _bouncerTries = 0;
            }
            _match.TestAlwaysReview = true;
            var log = _match.Log;
            var n = _match.LegalBalls;
            if (n >= 1 && !log.Any(b => b.NoBall) && !_match.FreeHit) return Evidence.NoBall;
            // Owner v26 #18: a pace over with three bouncers — once one's in, keep banging them in until the third (a no-ball).
            if (bowler.BowlType != BowlType.Spin && !_match.FreeHit && !log.Any(b => b.Delivery != null && b.Delivery.BouncerNoBall) && _bouncerTries < 9
                && (_match.BouncersThisOver > 0 || n >= 3 && _match.BallInOver <= 1)) { _bouncerTries++; return Evidence.Bouncer; }
            if (n >= 2 && !log.Any(b => b.Wide) && _wideTries < 3) { _wideTries++; return Evidence.Wide; }
            if (n >= 2 && !log.Any(b => b.Six) && _sixTries < 3) { _sixTries++; return Evidence.Six; }
            if (bowler.BowlType == BowlType.Spin && !log.Any(b => b.HowOut == "stumped") && _stumpTries < 3) { _stumpTries++; return Evidence.Stumping; }
            if (n >= 4 && !log.Any(b => b.Lbw != null && b.Lbw.Reviewed) && _lbwTries < 4) { _lbwTries++; return Evidence.Lbw; }
            if (log.Count(b => b.Caught) < 2 && _match.BallInOver % 2 == 0) return Evidence.Catch;
            return Evidence.None;
        }

        /// <summary>The aim for the evidence ball (batter frame: x across, + off; z from his stumps); null: the autoplay's own.</summary>
        private static Vector2? EvidenceMark(Evidence e, bool spin) => e switch
        {
            Evidence.Six => new Vector2(0.1f, spin ? 2.6f : 2.2f),
            Evidence.Lbw => new Vector2(0.02f, 2.2f),
            Evidence.Stumping => new Vector2(0.55f, 3.4f),
            // Well past the off-side wide guideline (0.89 m at the batter); a bouncer dug in.
            Evidence.Wide => new Vector2(1.1f, 4.5f),
            Evidence.Bouncer => new Vector2(0.15f, 10.8f),
            _ => (Vector2?)null,
        };

        /// <summary>The AI batter's shot for the evidence ball (the engine settles what comes of it).</summary>
        private ShotInput EvidenceShot(ShotInput shot, Trajectory t, double window)
        {
            switch (_evidence)
            {
                case Evidence.Six: return new ShotInput(ShotType.Loft, Zone.StraightOn, Batting.IdealTap(t));
                // Shouldering arms to a full straight one: lbw out on the field about half the time, and reviewed (TotalsTests).
                case Evidence.Lbw: return ShotInput.Leave;
                case Evidence.Stumping: return new ShotInput(ShotType.Loft, Zone.StraightOff, Batting.IdealTap(t, true) + 4.2 * window) { Charged = true }; // a clean miss: stumped ~3 in 4 (TotalsTests)
                case Evidence.Catch:
                    // Slogged a touch late in the air, in a random direction: about one in three is caught (ring and rope).
                    if (CatchLoop) return CatchLoopShot(t, window, shot); // -aplcatchloop: varied zone, lateness, loft or flat
                    return shot.IsLeave ? shot : new ShotInput(ShotType.Loft, (Zone)_autoRng.NextInt(8), Batting.IdealTap(t) + 1.2 * window);
                default: return shot;
            }
        }

        /// <summary>After the evidence ball: a shot of what it showed.</summary>
        private void EvidenceMoment(BallResult r)
        {
            CatchLoopCount(r); // -aplcatchloop only (MatchDirector.CatchLoop)
            if (!Nudges) return;
            if (r.NoBall) Extra(After(0.4f, () => Apl.Client.Dev.AutoShot.Moment(r.Delivery != null && r.Delivery.BouncerNoBall ? "bouncer_no_ball" : "no_ball")));
            if (r.Wide) Extra(After(1.2f, () => Apl.Client.Dev.AutoShot.Moment("wide")));
            if (r.BouncerNumber > 0 && r.Delivery != null && !r.Delivery.BouncerNoBall) Extra(After(1.3f, () => Apl.Client.Dev.AutoShot.Moment($"bouncer_{r.BouncerNumber}")));
            if (r.Six) Extra(After(1.2f, () => Apl.Client.Dev.AutoShot.Moment("six")));
            if (r.Lbw != null && r.Lbw.Reviewed) Debug.Log($"[APL match] evidence: lbw reviewed ({(r.Lbw.Overturned ? "overturned" : "stands")})");
            if (r.HowOut == "stumped") Debug.Log("[APL match] evidence: stumped");
            if (_evidence != Evidence.None) Debug.Log($"[APL match] evidence ball {_evidence}: {(r.Wicket ? r.HowOut : r.Runs + " run(s)")}{(r.NoBall ? " (no-ball)" : "")}");
        }

        /// <summary>After <paramref name="seconds"/> real seconds (through a timeScale-0 review), then the action.</summary>
        private static System.Collections.IEnumerator AfterReal(float seconds, Action act)
        {
            yield return new WaitForSecondsRealtime(seconds);
            act();
        }

        private bool _suicideDone;

        /// <summary>
        /// Test runs with -aplnudges: once a match, when the user's side is batting and a single is safe, it
        /// runs a suicidal second against a throw that will be in well ahead (risk 85%+) — so the evidence
        /// shows a take-and-break run out from the user's own running (moment_bails_off, then the TV umpire).
        /// </summary>
        private void AutoSuicideNudge(BallResult r)
        {
            if (!Nudges || _suicideDone || !_match.UserBatting || _match.Finished) return;
            var plan = r.Running;
            if (plan == null || r.Wicket || r.Four || r.Six || r.Caught || plan.Safe < 1 || r.Attempted > plan.Safe) return;
            var risk = plan.Risk(plan.Safe + 1);
            if (risk < 0.85) return;
            _suicideDone = true;
            _runOutNudged = _match.InningsNumber;
            _match.SettleRuns(plan.Safe + 1);
            Debug.Log($"[APL match] test nudge (user side): a suicidal {plan.Safe + 1} (risk {risk:0.00}): {(r.RunOut ? (r.DirectHit ? "run out, direct hit" : "run out, taken and broken") : "made it")}");
        }

        /// <summary>From Hit, before anything's planned on the result: once an innings, the batters chance a risky extra run.</summary>
        private void AutoRunOutNudge(BallResult r)
        {
            if (!Nudges || _runOutNudged == _match.InningsNumber || _match.Finished) return;
            var plan = r.Running;
            if (plan == null || r.Wicket || r.Four || r.Six || r.Caught || r.Attempted > plan.Safe) return;
            var risk = plan.Risk(plan.Safe + 1);
            if (risk < 0.3 || risk > 0.97) return;
            _runOutNudged = _match.InningsNumber;
            var before = r.Runs;
            _match.SettleRuns(plan.Safe + 1);
            Debug.Log($"[APL match] test nudge: went for {plan.Safe + 1} (risk {risk:0.00}): {(r.RunOut ? "run out" : $"made it, {before} -> {r.Runs} runs")}");
        }
    }
}
