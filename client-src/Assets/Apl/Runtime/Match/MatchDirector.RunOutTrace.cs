using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Run-out trace (owner v26: "an easy run out, still the batter reached the crease with a dive"): every
    /// ball they ran on with a chance in it (the ball back within a second of the batter's ground) gets one
    /// line — who was running, when he grounds his bat, when the ball's at the stumps and when the bails
    /// come off, who threw it from where, who collected, the engine's verdict and whether the picture dived —
    /// and each innings ends with a summary: "[APL runout] chances N, out M, safe-by-dive K, direct hits D".
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>A dive was played on screen this ball (set by Finish).</summary>
        private bool _runOutDiveShown;

        private int _roChances, _roOut, _roDived, _roDirect, _roHits, _roEasySafe, _roEasy, _roClose, _roTv, _roHeld, _roInnings = -1;

        /// <summary>Run-out owner (2026-11-14): raced balls, extra runs chanced (more than the safe ones) and run out on, sharp-throw run outs, close calls given safe (&lt; 0.3 s).</summary>
        private int _roRaced, _roExtra, _roExtraOut, _roSharp, _roCloseSafe;

        /// <summary>
        /// The owner's easy run out (run-out owner, 2026-11-13): the ball in the hands over the stumps (or a shy there)
        /// with the batter still ~1.5 m short — 0.22 s at a sprint. The engine gives every one out (RunPlan.Gather 0.15).
        /// </summary>
        private const double OwnerEasy = 0.22;

        /// <summary>A run that close (s between ball and bat) is a chance worth logging.</summary>
        private const double ChanceWindow = 1.0;

        /// <summary>After the runs are settled (RunsDone): the trace line, and the summary at an innings' end.</summary>
        private void TraceRunOut(BallResult r)
        {
            if (_roInnings != _match.InningsNumber)
            {
                _roInnings = _match.InningsNumber;
                _roChances = _roOut = _roDived = _roDirect = _roHits = _roEasySafe = _roEasy = _roClose = _roTv = _roHeld = 0;
                _roRaced = _roExtra = _roExtraOut = _roSharp = _roCloseSafe = 0;
            }
            var p = r.Running;
            if (p != null && (r.Attempted > 0 || r.TurnedBack))
            {
                var k = System.Math.Max(1, r.Attempted);
                var tRun = RunPlan.ArrivesAt(k);
                // (Deep area 3: the engine's own race as it fell — the ball in hand at the stumps, the bat in.)
                var raced = !double.IsNaN(r.BallIn);
                if (raced) tRun = r.BatIn;
                var tBreak = r.RunOut && r.BrokenAt > 0 ? r.BrokenAt : raced ? r.BallIn + (r.DirectHit ? 0 : RunPlan.Gather) : p.Back + (r.DirectHit ? 0 : RunPlan.Gather);
                var chance = r.RunOut || System.Math.Abs(tBreak - tRun) < ChanceWindow;
                // How much the ball (in the hands over the stumps, or a shy getting there) beat the bat by; the owner's
                // easy one (≥ 0.22 s, ~1.5 m short) given not out is an easy chance missed.
                var beat = raced ? r.BatIn - r.BallIn : double.NaN;
                var easy = beat >= OwnerEasy;
                var easySafe = !r.RunOut && easy;
                // Run-out owner (2026-11-14): every raced ball counted, and one short line each ("[APL runout] ran"), so the
                // risky runs the AI chances and how they end can be counted beside the chances.
                var extra = r.Attempted > p.Safe;
                _roRaced++;
                if (extra) _roExtra++;
                if (extra && r.RunOut) _roExtraOut++;
                if (r.RunOut && p.LastThrowSharp) _roSharp++;
                if (!r.RunOut && System.Math.Abs(tBreak - tRun) < 0.3) _roCloseSafe++;
                Debug.Log($"[APL runout] ran ball {Cricket.FormatOvers(_match.LegalBalls)}: side {RunSideLabel}, went for {r.Attempted} (safe {p.Safe}{(extra ? ", EXTRA chanced" : "")}), ran {r.Ran}{(r.RunOut ? ", RUN OUT" : "")}, sharp throw {(p.LastThrowSharp ? "Y" : "N")}, margin {(p.LastMargin == double.MaxValue ? "-" : p.LastMargin.ToString("0.00"))}");
                if (chance)
                {
                    _roChances++;
                    if (r.RunOut) _roOut++;
                    if (r.RunOut && r.DirectHit) _roDirect++;
                    if (r.DirectHit) _roHits++;
                    if (easySafe) _roEasySafe++;
                    if (easy) _roEasy++;
                    if (r.RunOut || System.Math.Abs(tBreak - tRun) < 0.3) _roClose++;
                    if (r.Referral == TvCheck.RunOut) _roTv++;
                    if (r.RunOut && _heldShort != null) _roHeld++;
                    if (!r.RunOut && (r.Dived || _runOutDiveShown)) _roDived++;
                    var thrower = _match.PlayerAt(p.Fielder)?.Name ?? p.Fielder;
                    var collector = r.DirectHit ? "direct hit" : p.ToKeeperEnd ? "keeper" : "bowler";
                    var runner = r.RunOut ? r.OutBatter?.Name : (_match.OnStrike.Player.Name + "/" + _match.OffStrike.Player.Name);
                    Debug.Log($"[APL runout] ball {Cricket.FormatOvers(_match.LegalBalls)}: runner {runner} needs t_run={tRun:0.00}s to the crease (run {k}{(r.TurnedBack ? ", sent back" : "")}), ball at stumps t_ball={p.Back:0.00}s (thrower {thrower} ({p.Fielder}), throw dist {p.ThrowDistance:0} m, collector {collector}, break {tBreak:0.00}s), engine {(r.RunOut ? "OUT" : "SAFE")}, visual dive={(_runOutDiveShown ? "Y" : "N")} margin={(p.LastMargin == double.MaxValue ? "-" : p.LastMargin.ToString("0.00"))}"
                        + $", stumps hit {(r.DirectHit ? "Y" : "N")}, shied {(r.Shied ? "Y" : "N")}, visible: {(_roVisible.Length > 0 ? _roVisible : "no break on screen")}, replay {_roReplay}, easy {(easy ? "Y" : "N")}, easy-safe {(easySafe ? "Y" : "N")} (ball beat bat by {(raced ? beat.ToString("0.00") : "-")} s)"
                        + $", side {RunSideLabel}, went for {r.Attempted} (safe {p.Safe}), held short {(_heldShort != null ? "Y" : "N")}, sharp throw {(p.LastThrowSharp ? "Y" : "N")}");
                }
            }
            _runOutDiveShown = false;
            _heldShort = null;
            if (_match.Finished) Debug.Log($"[APL runout] innings {_match.InningsNumber}: chances {_roChances}, out {_roOut}, safe-by-dive {_roDived}, direct hits {_roDirect}, stumps hit {_roHits}, easy-safe {_roEasySafe}"
                + $", easy {_roEasy}, close (< 0.3 s) {_roClose}, close-safe {_roCloseSafe}, tv {_roTv}, held short {_roHeld}, raced {_roRaced}, extra chanced {_roExtra} (run out {_roExtraOut}), sharp-throw run outs {_roSharp}, overs {_match.Overs}, side {RunSideLabel}{(RunOutReal ? " (real running, -aplrunoutreal)" : "")}");
        }
    }
}
