using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// A tied full match goes to a Super Over: the card, the user picks his three
    /// batters and his bowler (the AI its own), then one over each through the
    /// usual innings flows — the side that batted second bats first — and the
    /// result (on boundaries if it's tied again).
    /// </summary>
    public sealed partial class MatchDirector
    {
        private SuperOver? _superOver;

        /// <summary>From the end of a match (Chase or MatchEnd): a tie to settle, or the Super Over just finished.</summary>
        private bool SuperOverNext => SuperOver.Needed(_match) || (_superOver != null && _match == _superOver.Match);

        private IEnumerator SuperOverOrEnd()
        {
            if (_superOver != null && _match == _superOver.Match) yield return SuperOverEnd();
            else yield return SuperOverStart();
        }

        private IEnumerator SuperOverStart()
        {
            var main = _match;
            var userFirst = main.Batting != main.UserTeam;
            _hud.SetPlaying(false);
            _bowlUi?.Hide();
            Camera.Show(MatchCamera.Mode.Wide);
            _comm.Say("close", important: true);
            Sfx.Cheer(0.8f);
            _startPressed = false;
            _preMatch = true;
            _hud.ShowSuperOverCard(main, _bat, _bowl, userFirst);
            var until = Time.time + (Autoplay ? 2f : 3600f);
            while (!_startPressed && Time.time < until) yield return null;
            _preMatch = false;
            _hud.HideCard();

            // The user's three and his bowler (the countdown takes the best).
            var xi = main.UserTeam.XI.ToList();
            var batters = xi.OrderByDescending(p => p.Batting).Take(SuperOver.Batters).ToList();
            var bowler = SuperOver.AiBowler(main.UserTeam);
            if (!Autoplay)
            {
                if (_pre == null) _pre = PreMatchUi.Create();
                List<Player>? two = null, third = null;
                yield return _pre.Pick(_bat, xi, 2, batters.Take(2).ToList(), _ => null, PickSecs(PickClock.Striker), l => two = l, perPick: true); // owner v26 #8: 10 s a place
                var rest = xi.Where(p => two == null || !two.Contains(p)).ToList();
                yield return _pre.Pick(_bat, rest, 1, new[] { rest.OrderByDescending(p => p.Batting).First() }, _ => null, PickSecs(PickClock.NewBatter), l => third = l);
                _pre.ClearAll();
                if (two != null && two.Count == 2 && third != null && third.Count == 1) batters = two.Concat(third).ToList();
                var options = xi.Select(p => new BowlerOption { Player = p, MaxOvers = 1, Allowed = true }).OrderByDescending(o => o.Player.Bowling).ToList();
                Player? chosen = null;
                yield return _hud.PickBowler(options, _bat, PickSecs(PickClock.Bowler), p => chosen = p); // owner v26 #8: 10 s
                if (chosen != null) bowler = chosen;
            }
            _superOver = new SuperOver(main, System.DateTime.Now.Ticks & 0x7FFFFFFF, batters, bowler);
            _match = _superOver.Match;
            Recast();
            _hud.EventGraphic("SUPER OVER", $"{BattingSide.ShortName} bat first", UiKit.Gold, 2.4f);
            yield return new WaitForSeconds(Secs(1.5f));
            if (_match.UserBatting) StartCoroutine(Chase());
            else StartCoroutine(SuperOverBowlingFirst());
        }

        /// <summary>The user bowls the Super Over's first over, then chases (Chase ends in SuperOverEnd).</summary>
        private IEnumerator SuperOverBowlingFirst()
        {
            yield return BowlingInnings();
            yield return InningsBreak();
            StartCoroutine(Chase());
        }

        private IEnumerator SuperOverEnd()
        {
            var so = _superOver!;
            _hud.SetPlaying(false);
            _bowlUi?.Hide();
            yield return Finale();
            Camera.Show(MatchCamera.Mode.Wide);
            var won = so.Winner == so.Main.UserTeam;
            _comm.Say(won ? "won" : "lost", important: true);
            if (won)
            {
                Sfx.Cheer(1f);
                Sfx.Applause(1f);
            }
            _hud.ShowSuperOverResult(so, _bat, _bowl);
        }
    }
}
