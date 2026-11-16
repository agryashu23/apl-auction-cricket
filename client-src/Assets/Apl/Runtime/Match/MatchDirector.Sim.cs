using System.Collections;
using System.Collections.Generic;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Simulate (focus batch 1, owner: "sim options for sure"): from the pause menu's MATCH page or the over
    /// break's chip, the rest of this over, the rest of the innings, up to the last two overs, or the whole
    /// match (this innings now, the chase as soon as it starts) are played at once by the
    /// engine (QuickMatch.SimulateOver / SimulateInnings / SimulateTo). Each simulated ball is booked as a
    /// played one would be — the over's bowler for the over cards, the wagon wheel, the commentary box's
    /// running tally — then play picks up with a "SIMULATED: 4 overs · 38/2" banner.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The sim asked for (taken at the next ball): -1 none, 0 this over, 1 the innings, 2 to the last two overs.</summary>
        private int _simAsked = -1;
        private bool _simWired;

        /// <summary>The pause menu's and the over break's sim choices, wired once.</summary>
        private void WireSim()
        {
            // Test runs: the user's side runs like the AI (tight extra runs, the odd misjudgement), so a plain capture has
            // natural run-out chances. A player's AUTO is the safe runs, never run out (owner, 2026-11-13) — and so is a
            // -aplrunoutreal run's (real running for both sides: MatchDirector.RunOutReal).
            if (_match != null) _match.UserRunsLikeAi = Autoplay && !RunOutReal;
            if (_simWired || _hud == null) return;
            _simWired = true;
            _hud.SimPressed += kind => _simAsked = kind;
            _hud.SimOptions = () => (!_match.Finished, !_match.Finished, !_match.Finished && _match.Over < _match.Overs - 2);
            // WHOLE MATCH (owner v26 #10: simulate lives in the match now, not on the tournament screen):
            // only a two-innings match's first innings needs it; in the second, THIS INNINGS ends the match.
            _hud.SimMatchOption = () => !_match.Finished && _match.TwoInnings && !_match.Chasing && _superOver == null;
        }

        /// <summary>WHOLE MATCH was asked for in the first innings: the second innings is simulated as soon as it starts.</summary>
        private bool _simRestOfMatch;

        /// <summary>The over break with the SIM NEXT OVER chip up while it runs.</summary>
        private IEnumerator SimChipBreak(IEnumerator overBreak)
        {
            WireSim();
            if (!_match.Finished) _hud.ShowSimChip(true);
            yield return overBreak;
            _hud.ShowSimChip(false);
        }

        /// <summary>Before a ball: if a sim was asked for, it's played now (true if anything was simulated).</summary>
        private IEnumerator SimIfAsked()
        {
            WireSim();
            TestSim();
            // WHOLE MATCH carried over the innings break: the chase is simulated at its first ball.
            if (_simRestOfMatch && _match.Chasing && !_match.Finished)
            {
                _simRestOfMatch = false;
                _simAsked = 1;
            }
            if (_simAsked < 0 || _match.Finished) yield break;
            var kind = _simAsked;
            _simAsked = -1;
            if (kind == 3)
            {
                // The rest of this innings now; the second one when it starts (the innings break, the
                // tournament's record and the result all run as after a played innings).
                _simRestOfMatch = _match.TwoInnings && !_match.Chasing;
                kind = 1;
            }
            _hud.ShowSimChip(false);
            // Whatever banner was up (the bowling change for the over being simulated) goes now: it named the
            // over's bowler, and the over is about to be over (r-sim run: it sat over SIMULATED and named the
            // wrong man for the bar).
            _hud.ClearStage();
            _bowlUi?.Hide();
            _hud.SetPlaying(false);
            var runs = _match.Runs;
            var wickets = _match.Wickets;
            var balls = _match.LegalBalls;
            _hud.Banner("SIMULATING…", "", UiKit.Gold, 1f);
            yield return null;
            // Ball by ball, each booked as if bowled (a frame every over so the screen keeps breathing).
            while (!_match.Finished)
            {
                if (kind == 0 && _match.LegalBalls > balls && _match.BallInOver == 0) break;
                if (kind == 2 && _match.Over >= _match.Overs - 2) break;
                var over = _match.Over;
                _hud.OverBowlers[over] = _match.CurrentBowler;
                _facing = _match.OnStrike.Player;
                var played = _match.SimulateTo(over + 1 > _match.Overs ? _match.Overs : over + 1);
                if (played.Count == 0) break;
                // The over's balls into the wagon wheels, each under the batter who faced it.
                BookSimulated(played);
                if (kind == 0) break;
                yield return null;
            }
            // Everyone and the bar on the new state; the next ball sets the field and the bowler.
            Batters();
            _hud.ShowScore(_match, BattingSide, FieldingSide);
            var simmed = _match.LegalBalls - balls;
            var ov = Cricket.FormatOvers(simmed);
            var line = $"{ov} {(ov == "1" ? "over" : "overs")} · {_match.Runs - runs}/{_match.Wickets - wickets}"; // PM: "1 over", not "1 overs"
            if (Autoplay) Debug.Log($"[APL match] simulated ({(kind == 0 ? "over" : kind == 1 ? "innings" : "to the last two")}): {line}, now {_match.Runs}/{_match.Wickets} after {Cricket.FormatOvers(_match.LegalBalls)}");
            _hud.ClearStage();
            _hud.Banner("SIMULATED", line, UiKit.Gold, 2.8f);
            StartCoroutine(AfterReal(0.5f, () => Apl.Client.Dev.AutoShot.Moment("simulated")));
            // (The batting controls only when the user's side is batting; bowling, the loop brings his own back.)
            if (!_match.Finished && _match.UserBatting) _hud.SetPlaying(true);
            // Held for its read (real time, test runs too), before the over break and the next bowler's announcement.
            yield return new WaitForSecondsRealtime(2.6f);
        }

        /// <summary>The simulated balls into the wagon wheels (each scoring shot under the batter it was credited to).</summary>
        private void BookSimulated(List<BallResult> played)
        {
            foreach (var r in played)
            {
                if (r.ExitSpeed <= 0 || r.BatRuns <= 0 || r.Facing == null) continue;
                _facing = r.Facing;
                BookShot(r);
            }
        }

        private int _simTestInnings;

        /// <summary>Test runs with -aplsim: SIM OVER is asked for at the start of each innings' second over.</summary>
        private void TestSim()
        {
            if (!Autoplay || _simTestInnings == _match.InningsNumber || _match.Over != 1 || _match.BallInOver != 0) return;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-aplsim") < 0) return;
            _simTestInnings = _match.InningsNumber;
            _simAsked = 0;
        }
    }
}
