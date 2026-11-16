using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The user's batting choices: the opening pair before the first ball
    /// (f43–48), and "SELECT BATTER" after each wicket — next in the order by
    /// default — with the new man's walk out from the pavilion (the entry's
    /// <see cref="BatterEntry"/>). His intro card comes with his first ball.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The new batter this wicket has already been chosen for (he can wait at the far end for balls).</summary>
        private string _sentIn = "";

        /// <summary>The batting side picks its opening pair (the user's side only; the AI keeps its order).</summary>
        private IEnumerator ChooseOpeners()
        {
            if (_match.Batting.TeamId != _bat.Id || _match.Log.Count > 0) yield break;
            var order = _match.Order.Select(b => b.Player).ToList();
            List<Player>? chosen = null;
            yield return _pre.Pick(_bat, order, 2, order.Take(2).ToList(), _ => null, PickSecs(PickClock.Striker), l => chosen = l, perPick: true); // owner v26 #8: 10 s striker, 10 s non-striker
            if (chosen != null && chosen.Count == 2 && chosen[0].Id != chosen[1].Id) _match.SetOpeners(chosen[0], chosen[1]);
        }

        /// <summary>Set while a new batter walks out (the walk's slide-meter label: "batter walk-out (new batter)").</summary>
        private bool _newBatterWalking;

        /// <summary>
        /// The other side's new batter (no SELECT BATTER: the AI's order): out from the gate to his end — the striker to his
        /// guard, the non-striker to his place — on the walk-out the openers use (BatterEntry: SKIP fast-forwards it, the camera
        /// with him), then straight into his stance (no guard ritual, to keep the pace of play). The clock is reset as for the
        /// user's own new batter.
        /// </summary>
        private IEnumerator OtherSideNewBatter(Apl.Core.Play.ChaseBatter incoming)
        {
            if (_pre == null) _pre = PreMatchUi.Create();
            _sentIn = incoming.Player.Id;
            _hud.ClockReset("the new batter's walk-in");
            _hud.SetPlaying(false);
            StallProbe.Mark($"new batter (other side): {incoming.Player.Name}");
            Batters();
            yield return null;
            if (_batters.TryGetValue(incoming.Player.Id, out var rig))
            {
                var striking = _match.OnStrike == incoming;
                if (striking) rig.TakeGuard();
                else rig.NonStriker(new Vector3(_nonStrikerX, 0, GroundSpace.HalfPitch - 1.3f));
                var spot = (rig, rig.transform.position, rig.transform.rotation);
                HudVisible(false);
                _newBatterWalking = true;
                try
                {
                    yield return BatterEntry(new[] { spot }, $"{incoming.Player.Name} walks out", Secs(2.5f));
                }
                finally
                {
                    _newBatterWalking = false;
                }
                if (striking) rig.TakeGuard();
                else rig.NonStriker(spot.Item2);
            }
            HudVisible(true);
            // Final rating fix #1: this is the OTHER side's batter — the user is bowling, so the batting panel stays
            // off (it used to come on here and sat under the delivery wheel for the rest of the innings, M275–M664).
            HudRole($"other side's new batter {incoming.Player.Name} in");
        }

        /// <summary>
        /// Final rating fix #1: the batting panel on only when the user's side is batting (off while he bowls).
        /// Every "the panel comes back" point in the match flow goes through here, with a [APL hudrole] line.
        /// </summary>
        private void HudRole(string why)
        {
            var userBats = _match.UserBatting;
            _hud.SetPlaying(userBats);
            Debug.Log($"[APL hudrole] {why}: user {(userBats ? "BATTING -> batting panel ON" : "BOWLING -> batting panel OFF")} (innings {_match.InningsNumber}, {_match.Runs}/{_match.Wickets})");
        }

        /// <summary>Final rating fix #1: the HUD's own role gate (MatchHud.UserBats) — nets are left ungated.</summary>
        private void WireHudRole() => _hud.UserBats = () => _nets != NetsLaunch.Kind.None || _match == null || _match.UserBatting;

        private IEnumerator NewBatter()
        {
            var incoming = _match.Incoming;
            if (incoming == null || incoming.Player.Id == _sentIn || _match.Finished || !PlayPreMatch) yield break;
            // The fielders walk back to their marks while the new man walks out (fielding: MatchDirector.FieldMoves PreSetField).
            PreSetField();
            if (_match.Batting.TeamId != _bat.Id)
            {
                // Deep area 1 (iteration 8: the owner's "batsman coming to the ground" — the other side's new batter was placed at
                // the crease, never walked): he walks out from the pavilion gate too, the same stepping walk as the openers.
                if (SlideFix.Round1) yield return OtherSideNewBatter(incoming);
                yield break;
            }
            if (_pre == null) _pre = PreMatchUi.Create();
            _hud.ClockReset("the new batter's walk-in");
            _hud.SetPlaying(false);
            var order = _match.Order.Select(b => b.Player).ToList();
            string? Note(Player p)
            {
                var b = _match.Order.First(x => x.Player.Id == p.Id);
                if (b.HowOut != null) return $"OUT\n{b.Runs} ({b.Balls})";
                return b.Batted && b != incoming ? $"BATTING\n{b.Runs} ({b.Balls})" : null;
            }
            Player? pick = null;
            StallProbe.Mark("new batter: select batter");
            yield return _pre.Pick(_bat, order, 1, new[] { incoming.Player }, Note, PickSecs(PickClock.NewBatter), l => pick = l.FirstOrDefault()); // owner v26 #8: 10 s (next in the order at 0)
            if (pick != null) _match.SendIn(pick);
            var chosen = _match.Incoming ?? incoming;
            _sentIn = chosen.Player.Id;

            // Out from the pavilion to his end: the crease if he's on strike, else the bowler's end.
            StallProbe.Mark($"new batter: {chosen.Player.Name} (Batters)");
            var batT = StallProbe.Start();
            Batters();
            StallProbe.End("new batter Batters()", batT);
            yield return null;
            if (_batters.TryGetValue(chosen.Player.Id, out var rig))
            {
                var striking = _match.OnStrike == chosen;
                if (striking) rig.TakeGuard();
                else rig.NonStriker(new Vector3(_nonStrikerX, 0, GroundSpace.HalfPitch - 1.3f));
                var spot = (rig, rig.transform.position, rig.transform.rotation);
                HudVisible(false);
                _newBatterWalking = true;
                try
                {
                    yield return BatterEntry(new[] { spot }, $"{chosen.Player.Name} walks out", Secs(3f));
                }
                finally
                {
                    _newBatterWalking = false;
                }
                if (striking) yield return TakeGuardRoutine(rig);
                else rig.NonStriker(spot.Item2);
            }
            HudVisible(true);
            HudRole($"user's new batter {chosen.Player.Name} in");
        }
    }
}
