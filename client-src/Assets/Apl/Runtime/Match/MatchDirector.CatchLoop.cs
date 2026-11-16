using System;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Area 3 evidence, TEST RUNS ONLY (<c>-aplcatchloop</c> on top of <c>-aplmatchtest</c>). Every ball, in both innings,
    /// the batter (the AI's, or the autoplay's for the user side) goes aerial a touch late. Two in three tries are
    /// LOFTED: all eight zones in turn, 0.8–1.6 windows late, one in four a stroke not a loft (skiers, back-running and
    /// low catches). One in three is FLAT (build 3, for the chest / side / high bands the lofted ones never gave): a
    /// stroke mistimed just enough (0.45–0.8 windows late) to balloon flat into the ring, toward the ring zones in turn;
    /// every third flat try goes at the cordon instead (third man, 1.0–1.4 windows late: an edge for the keeper and
    /// slips). The engine still decides each one honestly (caught, dropped, short, four). Each ball logs
    /// <c>[APL catchloop] #caught of tries … [mode]</c>. Without the flag nothing here runs; a real match never changes.
    /// Also the evidence ball tag (<see cref="EvidenceBall"/>) for Dev/FieldEvidence moments.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private bool? _catchLoop;

        /// <summary>The catch loop is on (autoplay and -aplcatchloop).</summary>
        private bool CatchLoop => _catchLoop ??= Autoplay && (Environment.GetCommandLineArgs().Contains("-aplcatchloop") || CatchForce);

        private bool? _catchForce;

        /// <summary>
        /// -aplcatchforce [side|high|all] (test only, implies the catch loop): EVERY try is a flat, catchable balloon for the
        /// side and high bands. The engine's own rules (Batting.Resolve) are used, not bypassed: a stroke in the batter's
        /// best direction (the AI's chosen zone, else BestZone) graded Early/Late (1.2–1.8 windows, alternating, so the
        /// drift sends it to both sides) scores 0.4 for timing, quality ~0.3–0.45 — the "mistimed drive balloons" branch
        /// (launch 18–35°, apex well under 6 m), where the catch planner picks the one-hand (side) and jumping (high) clips;
        /// below 0.3 it's the edge branch (skied into the ring, caught behind). Which band each catch lands in is still
        /// the planner's — the mode word only labels the log.
        /// </summary>
        private bool CatchForce => _catchForce ??= Autoplay && Environment.GetCommandLineArgs().Contains("-aplcatchforce");

        private string CatchForceMode
        {
            get
            {
                var a = Environment.GetCommandLineArgs();
                var i = Array.IndexOf(a, "-aplcatchforce");
                return i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : "all";
            }
        }

        /// <summary>Its own draws, so the catch loop never shifts the autoplay's.</summary>
        private readonly Rng _loopRng = new Rng(4242);

        private int _catchTries, _catchTaken;

        /// <summary>This try is a flat one (one in three), and of those every third goes at the cordon.</summary>
        private bool CatchLoopFlat => _catchTries % 3 == 2;
        private bool CatchLoopCordon => CatchLoopFlat && (_catchTries / 3) % 3 == 2;

        /// <summary>The ring zones a flat hit is aimed at, in turn (ring fielders on both sides).</summary>
        private static readonly Zone[] FlatZones = { Zone.Cover, Zone.MidWicket, Zone.Point, Zone.Square, Zone.StraightOff, Zone.StraightOn };

        /// <summary>This try's mode, for the log.</summary>
        private string CatchLoopMode => CatchForce ? "forced " + CatchForceMode : CatchLoopCordon ? "flat cordon" : CatchLoopFlat ? "flat ring" : "lofted";

        /// <summary>How late (in windows) this ball's catch-loop shot is.</summary>
        private double CatchLoopLate() => CatchForce ? (_catchTries % 2 == 0 ? 1 : -1) * (1.2 + 0.6 * _loopRng.NextDouble())
            : CatchLoopCordon ? 1.0 + 0.4 * _loopRng.NextDouble()
            : CatchLoopFlat ? 0.45 + 0.35 * _loopRng.NextDouble()
            : 0.8 + 0.8 * _loopRng.NextDouble();

        /// <summary>Flat tries: a stroke. Lofted tries: mostly a loft, one in four a stroke.</summary>
        private ShotType CatchLoopType() => CatchForce || CatchLoopFlat ? ShotType.Stroke : _loopRng.Chance(0.25) ? ShotType.Stroke : ShotType.Loft;

        /// <summary>Lofted: all eight zones in turn. Flat: the ring zones in turn; at the cordon, third man.</summary>
        private Zone CatchLoopZone() => CatchLoopCordon ? Zone.ThirdMan : CatchLoopFlat ? FlatZones[(_catchTries / 3) % FlatZones.Length] : (Zone)(_catchTries % 8);

        /// <summary>The AI batter's catch-loop shot (MatchDirector.TestNudges.EvidenceShot).</summary>
        private ShotInput CatchLoopShot(Trajectory t, double window, ShotInput own) =>
            new ShotInput(CatchLoopType(), CatchForce ? own.Direction : CatchLoopZone(), Batting.IdealTap(t) + CatchLoopLate() * window);

        /// <summary>After each catch-loop ball: the count.</summary>
        private void CatchLoopCount(BallResult r)
        {
            if (!CatchLoop) return;
            var mode = CatchLoopMode;
            _catchTries++;
            if (r.Caught) _catchTaken++;
            Debug.Log($"[APL catchloop] #{_catchTaken} caught of {_catchTries} tries (innings {_match.InningsNumber}, {(_match.UserBatting ? "user batting" : "user fielding")}): "
                + $"{(r.Caught ? $"caught by {r.Fielder}" : r.Dropped.Length > 0 ? $"dropped by {r.Dropped}" : r.Wicket ? r.HowOut : r.Runs + " run(s)")} [{mode}]");
        }

        /// <summary>Test evidence: this ball's tag for moment file names, <c>i&lt;innings&gt;_b&lt;overs&gt;</c> (as the [APL runout] lines).</summary>
        public string EvidenceBall => _match == null ? "" : $"i{_match.InningsNumber}_b{Cricket.FormatOvers(_match.LegalBalls)}";
    }
}
