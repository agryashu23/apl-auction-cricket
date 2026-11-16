using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Menu;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The impact player's screens (the rule is the engine's, ImpactPlayer.cs):
    /// after the squads, pick up to four substitutes from your bench (the best
    /// four by default; the opponent's are its best four); then, whenever a
    /// substitution can be made — an over break, straight after a wicket —
    /// IMPACT PLAYER ► is on the left: who comes on, who goes off.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private List<Player>? _mySubs, _theirSubs;

        /// <summary>The match the substitutes were named in (the toss can start a new one).</summary>
        private QuickMatch? _subsNamed;
        private bool _subbing;

        /// <summary>A side's squad: the tournament's, else last season's.</summary>
        private List<Player> SquadFor(string teamId)
        {
            var t = TournamentLaunch.Active;
            if (t != null && t.Squads.TryGetValue(teamId, out var ids))
            {
                var set = new HashSet<string>(ids);
                return _data.Players.Where(p => set.Contains(p.Id)).ToList();
            }
            return _data.Players.Where(p => p.PreviousTeam == teamId).ToList();
        }

        /// <summary>Your four from the bench (the squads screen's sequel); theirs the best four.</summary>
        private IEnumerator PickSubstitutes()
        {
            var mine = SheetFor(_bat.Id);
            var bench = SquadFor(_bat.Id).Where(p => !mine.XI.Any(x => x.Id == p.Id)).OrderByDescending(p => p.Impact).Take(12).ToList();
            _theirSubs = QuickMatch.DefaultSubstitutes(SquadFor(_bowl.Id), SheetFor(_bowl.Id));
            _mySubs = QuickMatch.DefaultSubstitutes(bench, mine);
            if (Autoplay || Debug.isDebugBuild) Debug.Log($"[APL prematch] impact bench {bench.Count} (squad {SquadFor(_bat.Id).Count}, XI {mine.XI.Count})");
            // No bench (a squad of eleven): no screen.
            if (bench.Count == 0) yield break;
            var count = Mathf.Min(QuickMatch.MaxSubstitutes, bench.Count);
            yield return _pre.Pick(_bat, bench, count, _mySubs, _ => null, PickSecs(PickClock.Impact), l => _mySubs = l, "IMPACT PLAYERS · PICK YOUR SUBSTITUTES", "SUB", preselect: true); // owner v26 #8: 20 s, the best four at 0
        }

        /// <summary>
        /// The IMPACT PLAYER button is only offered at an over break or after a wicket — not
        /// while a ball is live (the engine's window stays open through the last ball's play).
        /// </summary>
        private bool _impactOffer;

        /// <summary>A broadcast piece has the screen (the bowling controls step aside for it).</summary>
        public bool OnAir => _hud != null && _hud.OnAir;

        /// <summary>Names the substitutes in whichever match is on, and offers the swap when it's allowed.</summary>
        private IEnumerator KeepImpact()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.25f);
                if (_match == null) continue;
                if (_subsNamed != _match && (_mySubs != null || _theirSubs != null))
                {
                    _subsNamed = _match;
                    if (_mySubs != null && _match.Substitutes(_bat.Id).Count == 0) _match.NameSubstitutes(_bat.Id, _mySubs);
                    if (_theirSubs != null && _match.Substitutes(_bowl.Id).Count == 0) _match.NameSubstitutes(_bowl.Id, _theirSubs);
                }
                if (_pre == null) continue;
                var can = _impactOffer && !_hud.OnAir && !_replays.Running && !_preMatch && !_subbing && _nets == NetsLaunch.Kind.None && _match.CanSubstitute(_bat.Id) && !_hud.Paused;
                _pre.ImpactButton(can, () => StartCoroutine(Substitution()));
            }
        }

        /// <summary>The impact pick's frozen clock is held on purpose (the HUD's watchdog leaves it be) while the pick is up.</summary>
        private IEnumerator HoldClockWhileSubbing()
        {
            while (_subbing)
            {
                _hud.ClockHeld();
                yield return null;
            }
        }

        /// <summary>The game held (time stopped) while you choose who comes on and who goes off.</summary>
        private IEnumerator Substitution()
        {
            if (_subbing) yield break;
            _subbing = true;
            _pre.ImpactButton(false, () => { });
            var was = Time.timeScale;
            Time.timeScale = 0f;
            StartCoroutine(HoldClockWhileSubbing());
            var subs = _match.Substitutes(_bat.Id).ToList();
            Player? on = null, off = null;
            // Time is stopped: the screens wait for a pick (their clocks don't run).
            yield return _pre.Pick(_bat, subs, 1, subs.Take(1).ToList(), _ => null, PickSecs(PickClock.Impact), l => on = l.FirstOrDefault(), "IMPACT PLAYER · WHO COMES ON", "ON", realtime: true); // owner v26 #8: 20 s
            var side = _match.Batting.TeamId == _bat.Id ? _match.Batting : _match.Bowling;
            var batting = side == _match.Batting;
            string? Why(Player p)
            {
                if (batting && (_match.OnStrike.Player == p || _match.OffStrike.Player == p)) return "BATTING";
                if (batting && _match.Order.Any(b => b.Player == p && b.HowOut != null)) return "OUT";
                if (!batting && _match.CurrentBowler == p && _match.BallInOver != 0) return "BOWLING";
                return null;
            }
            var xi = side.XI.ToList();
            // At the clock the least useful of those who may go (PickClock.LeastUseful), not simply the first listed.
            var first = PickClock.LeastUseful(xi.Where(p => Why(p) == null && (!batting || !_match.Order.Any(b => b.Player == p && b.Batted))), batting)
                        ?? xi.FirstOrDefault(p => Why(p) == null);
            if (on != null && first != null)
                yield return _pre.Pick(_bat, xi, 1, new[] { first }, Why, PickSecs(PickClock.Impact), l => off = l.FirstOrDefault(), $"▲ {on.Name.ToUpperInvariant()} · WHO GOES OFF?", "OFF", realtime: true);
            Time.timeScale = was <= 0f ? 1f : was;
            if (on != null && off != null && _match.Substitute(_bat.Id, off, on))
            {
                Recast();
                _hud.Banner("IMPACT PLAYER", $"▲ {on.Name}   ▼ {off.Name}", UiKit.Gold, 2.5f);
            }
            else if (on != null && off != null) _hud.Banner("NOT NOW", "the next over break", UiKit.Dim, 2f);
            _subbing = false;
        }
    }
}
