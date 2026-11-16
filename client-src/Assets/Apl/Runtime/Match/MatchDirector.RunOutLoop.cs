using System;
using System.Linq;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// TEC-1 repro (test runs only: <c>-aplrunoutloop</c> on top of the autoplay): the 23.6 s freeze came
    /// once, right after a take-and-break run out, and a normal run films one run out an innings. With
    /// this flag, every ball in BOTH innings that the batters can run on, they chance one run more than
    /// is safe — the riskiest of the next three — so nearly every such ball is an engine run out with the
    /// whole package (TV umpire, wicket replay, dismissed card, wagon wheel, new batter's walk-in,
    /// the field share-out). Each is counted ("[APL runoutloop] #N …"); the stall watcher names any freeze.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private bool? _runOutLoop;

        /// <summary>The run-out loop is on (autoplay and -aplrunoutloop).</summary>
        private bool RunOutLoop => _runOutLoop ??= Autoplay && Environment.GetCommandLineArgs().Contains("-aplrunoutloop");

        private int _loopTries, _loopRunOuts;

        /// <summary>From Hit, before anything's planned on the result: a suicidal extra run, every runnable ball.</summary>
        private void RunOutLoopNudge(BallResult r)
        {
            if (!RunOutLoop || _match.Finished) return;
            var plan = r.Running;
            if (plan == null || r.Wicket || r.Four || r.Six || r.Caught || r.Attempted > plan.Safe) return;
            var go = plan.Safe + 1;
            var risk = plan.Risk(go);
            for (var k = plan.Safe + 2; k <= plan.Safe + 3 && risk < 0.9; k++)
            {
                var rk = plan.Risk(k);
                if (rk > risk)
                {
                    go = k;
                    risk = rk;
                }
            }
            _loopTries++;
            _match.SettleRuns(go);
            if (r.RunOut) _loopRunOuts++;
            StallProbe.Mark($"runoutloop: went for {go} (risk {risk:0.00}): {(r.RunOut ? "RUN OUT" : "made it")}");
            Debug.Log($"[APL runoutloop] #{_loopRunOuts} run outs of {_loopTries} tries (innings {_match.InningsNumber}, {(_match.UserBatting ? "user batting" : "user fielding")}): "
                + $"went for {go} (risk {risk:0.00}): {(r.RunOut ? (r.DirectHit ? "run out, direct hit" : "run out, taken and broken") : "made it")}");
        }
    }
}
