using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>The user's impact substitutes, named by the squad screen (player ids; null: the best four left out).</summary>
    public static class ImpactPicks
    {
        public static List<string>? UserSubIds;
    }

    /// <summary>
    /// The breaks in play (rules-bowling): the strategic timeout (the card, both
    /// sides in a huddle) at an over break inside its window — called from the
    /// pause menu, or at the window's last over — and the impact substitution,
    /// the user's at an over break or wicket after he asks for it, the AI's at
    /// the innings break.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private bool _timeoutRequested, _impactRequested, _rulesWired;

        /// <summary>The pause menu's FIELD / TIMEOUT / IMPACT SUB, and both sides' substitutes.</summary>
        private void WireRules()
        {
            if (!_rulesWired)
            {
                _rulesWired = true;
                _hud.RulesOptions = () => (!_match.UserBatting && _match.TwoInnings,
                    _match.TimeoutOpen && !_timeoutRequested,
                    _match.Substitutes(_bat.Id).Count > 0 && _match.ImpactMade(_bat.Id) == null && !_impactRequested);
                _hud.FieldMenu += () => _fieldRequested = true;
                _hud.TimeoutMenu += () =>
                {
                    _timeoutRequested = true;
                    Show("STRATEGIC TIMEOUT", "at the end of the over", Color.white, 1.6f);
                };
                _hud.ImpactMenu += () =>
                {
                    _impactRequested = true;
                    Show("IMPACT PLAYER", "at the next over break or wicket", Color.white, 1.6f);
                };
            }
            List<Player> Squad(string id) => _data.Players.Where(p => p.PreviousTeam == id).ToList();
            var mine = ImpactPicks.UserSubIds?.Select(id => _data.Players.FirstOrDefault(p => p.Id == id)).Where(p => p != null).Select(p => p!).ToList();
            var user = _match.UserTeam;
            _match.NameSubstitutes(user.TeamId, mine != null && mine.Count > 0 ? mine : QuickMatch.DefaultSubstitutes(Squad(user.TeamId), user));
            _match.NameSubstitutes(_match.Opponent.TeamId, QuickMatch.DefaultSubstitutes(Squad(_match.Opponent.TeamId), _match.Opponent));
        }

        /// <summary>At an over break: the timeout if it's called (or due), then a substitution asked for.</summary>
        private IEnumerator BreakRules()
        {
            if (_match.CanTakeTimeout && (_timeoutRequested || _match.TimeoutDue) && _match.TakeTimeout())
            {
                _timeoutRequested = false;
                yield return StrategicTimeout();
            }
            yield return ImpactIfAsked();
        }

        /// <summary>The user's substitution, at the first over break or wicket after he asked.</summary>
        private IEnumerator ImpactIfAsked()
        {
            if (!_impactRequested || !_match.CanSubstitute(_bat.Id)) yield break;
            _impactRequested = false;
            if (_pre == null) _pre = PreMatchUi.Create();
            var side = _match.UserTeam;
            bool AtCrease(Player p) => _match.Batting == side && (_match.OnStrike.Player == p || _match.OffStrike.Player == p);
            var xi = side.XI.ToList();
            Player? off = null, on = null;
            // owner v26 #8: 20 s each; at 0 the least useful who may go off, then the best substitute.
            var offDefault = PickClock.LeastUseful(xi.Where(p => !AtCrease(p)), _match.Batting == side) ?? xi.Last();
            yield return _pre.Pick(_bat, xi, 1, new[] { offDefault }, p => AtCrease(p) ? "AT THE\nCREASE" : null, PickSecs(PickClock.Impact), l => off = l.FirstOrDefault());
            var subs = _match.Substitutes(side.TeamId).ToList();
            if (off != null && !AtCrease(off) && subs.Count > 0) yield return _pre.Pick(_bat, subs, 1, new[] { subs[0] }, _ => null, PickSecs(PickClock.Impact), l => on = l.FirstOrDefault());
            _pre.ClearAll();
            if (off == null || on == null || !_match.Substitute(side.TeamId, off, on)) yield break;
            _hud.Banner("IMPACT PLAYER", $"▲ {on.Name}   ▼ {off.Name}", UiKit.Gold, 2.4f);
            Recast();
            yield return new WaitForSeconds(Secs(2.2f));
        }

        /// <summary>The AI's substitution at the innings break, announced.</summary>
        private void AiImpactAtBreak()
        {
            var opp = _match.Opponent.TeamId;
            if (!_match.AiImpact(opp)) return;
            var (off, on) = _match.ImpactMade(opp)!.Value;
            _hud.Banner("IMPACT PLAYER", $"▲ {on.Name}   ▼ {off.Name}", UiKit.Gold, 2.6f);
        }

        /// <summary>STRATEGIC TIMEOUT: the card, the fielding side in a huddle by the pitch, the batters together mid-pitch.</summary>
        private IEnumerator StrategicTimeout()
        {
            _hud.EventGraphic("STRATEGIC TIMEOUT", $"{BattingSide.ShortName} {_match.Runs}/{_match.Wickets} after {_match.Over} overs", UiKit.Accent, 3f);
            _comm.Say("over", 0.8f);
            var centre = new Vector3(9f, 0, 1f);
            var bodies = _side.Values.Where(a => a != null && a.gameObject.activeSelf).ToList();
            for (var i = 0; i < bodies.Count; i++)
            {
                var ang = i * Mathf.PI * 2 / bodies.Count;
                var spot = centre + new Vector3(Mathf.Sin(ang), 0, Mathf.Cos(ang)) * 2.2f;
                // Field rework (PM, 2026-11-16: "Field_r152 moved 12 m during a run-up"): the huddle walk is a field routine,
                // so the next ball's ResetFielders stops it and walks him back on a tracked crossing the bowler waits for —
                // an untracked huddle walk used to carry on through the next run-up.
                FieldRoutine(JoinHuddle(bodies[i], spot, centre));
            }
            foreach (var (rig, x) in new[] { (_striker, -0.5f), (_other, 0.5f) })
            {
                if (rig == null) continue;
                StartCoroutine(RunTo(rig.GetComponent<Animator>(), new Vector3(x, 0, 0), 3f));
            }
            Camera.Track(() => (centre + new Vector3(-9f, 6f, -12f), centre + Vector3.up), fov: 38f, smooth: 0.5f);
            yield return new WaitForSeconds(Secs(5f));
            Camera.Show(MatchCamera.Mode.Wide);
        }

        private IEnumerator JoinHuddle(Animator a, Vector3 spot, Vector3 centre)
        {
            StopCross(a); // one mover per body: a walk back to his mark (PreSetField) gives way to the huddle
            SlideScene.Tag(a, "strategic timeout huddle");
            yield return RunTo(a, spot, Mathf.Max(1.5f, Vector3.Distance(Flat(a.transform.position), spot) / 5f), betweenBalls: true);
            Body.Play(a, "team_huddle_loop", centre - spot);
        }
    }
}
