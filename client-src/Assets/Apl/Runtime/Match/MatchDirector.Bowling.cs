using System;
using System.Collections;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The full two-innings match (rules-bowling): the toss's hooks
    /// (<see cref="StartUserBowlingInnings"/>, <see cref="StartUserBattingFirst"/>,
    /// found by name from MatchDirector.PreMatch.cs), the innings break with the
    /// target card, and the user's bowling innings over by over (the ball itself:
    /// MatchDirector.BowlingBall.cs). <c>_bat</c> / <c>_bowl</c> stay the user's
    /// side and the opponent; who's batting now is <c>BattingSide</c>.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private BowlingControls? _bowlUi;

        /// <summary>The toss: the opponent bats, the user bowls first (then chases).</summary>
        public IEnumerator StartUserBowlingInnings()
        {
            NewFullMatch(userBatsFirst: false);
            Recast();
            yield return WalkOut();
            yield return Intros();
            yield return BowlingInnings();
            yield return InningsBreak();
            yield return ChooseOpeners();
            yield return WalkOut();
            yield return Intros();
            StartCoroutine(Chase());
        }

        /// <summary>The toss: the user bats first (no target); the break and his bowling follow from <see cref="Chase"/>'s end.</summary>
        public IEnumerator StartUserBattingFirst()
        {
            NewFullMatch(userBatsFirst: true);
            yield return ChooseOpeners();
            yield return WalkOut();
            yield return Intros();
            StartCoroutine(Chase());
        }

        /// <summary>
        /// Called when the user's first innings (batting) is done: the break, the
        /// sides swap, and he bowls at the target; then the result.
        /// </summary>
        private IEnumerator SecondInningsBowling()
        {
            yield return InningsBreak();
            yield return WalkOut();
            yield return Intros();
            yield return BowlingInnings();
            yield return MatchEnd();
        }

        private void NewFullMatch(bool userBatsFirst)
        {
            var seed = DateTime.Now.Ticks & 0x7FFFFFFF;
            CarryPitch(); // the toss was called on this pitch: the full match keeps it (MatchDirector.PitchReport.cs)
            _match = QuickMatch.Full(SheetFor(_bat.Id), SheetFor(_bowl.Id), MatchLaunch.Overs, seed, _venue, userBatsFirst);
            // Timeout, impact substitutes, the pause menu's match row (MatchDirector.Breaks.cs).
            WireRules();
            _hud.HideCard();
            // The bar on the new match at once (the openers' intro shows it): not the quick chase made at Start.
            _hud.ShowScore(_match, BattingSide, FieldingSide);
        }

        /// <summary>
        /// The innings break: the first innings' card and the target, then the
        /// engine swaps the sides and the bodies are recast in the right kits.
        /// </summary>
        private IEnumerator InningsBreak()
        {
            _impactOffer = false;
            _hud.SetPlaying(false);
            _bowlUi?.Hide();
            yield return new WaitForSeconds(Autoplay ? 0.5f : 1.5f);
            Camera.Show(MatchCamera.Mode.Wide);
            var first = _match.Summary();
            var bat = BattingSide;
            var field = FieldingSide;
            _match.StartSecondInnings();
            AiImpactAtBreak();
            _comm.Say("over", 0.8f);
            _startPressed = false;
            // START on the card only flags it (as during the ceremony), it doesn't start a chase itself.
            _preMatch = true;
            _hud.ShowInningsBreak(first, bat, field, _match, BattingSide);
            var until = Time.time + (Autoplay ? 2f : 3600f);
            while (!_startPressed && Time.time < until) yield return null;
            _preMatch = false;
            _hud.HideCard();
            Recast();
            ParkBall();
            // The second innings on the bar before the walk-out and intros (not "Innings complete").
            _hud.ShowScore(_match, BattingSide, FieldingSide);
        }

        /// <summary>
        /// The ball off the field between innings: whatever held it (a keeper's or fielder's hand now
        /// retired with his side) lets go, and it's hidden till the next ball's bowler picks it up
        /// (r9 shots 240–245: it hung in the air by the batter through the second innings' intros).
        /// </summary>
        private void ParkBall()
        {
            foreach (var c in _extras) if (c != null) StopCoroutine(c);
            _extras.Clear();
            StopAllCrossing();
            _trail.emitting = false;
            _trail.Clear();
            _ballView.Hide();
            _pitchMarker.Emphasis(1f);
            _pitchMarker.AimStyle(false);
        }

        private static void Retire(GameObject go)
        {
            go.SetActive(false);
            Destroy(go, 20f);
        }

        /// <summary>The bodies for this innings: the fielding XI in its kit (any can bowl), the batting order in theirs.</summary>
        private void Recast()
        {
            if (!_cast.Ready) return;
            // Hidden now, destroyed later: walk-outs, fielding moves and celebrations still in
            // flight hold these bodies and must find them there, not destroyed under them.
            foreach (var a in _side.Values) if (a != null) Retire(a.gameObject);
            foreach (var b in _batters.Values) if (b != null) Retire(b.gameObject);
            _batters.Clear();
            _fielders.Clear();
            var field = FieldingSide;
            var bat = BattingSide;
            _side = _cast.Side(_match.Bowling.XI, field, "Field");
            foreach (var a in _side.Values) a.gameObject.AddComponent<BowlerRig>();
            _cast.KeeperGear(_side[Keeper().Id], field);
            foreach (var kv in _cast.Side(_match.Order.Select(b => b.Player), bat, "Bat"))
            {
                _cast.BatterGear(kv.Value, bat);
                _batters[kv.Key] = kv.Value.gameObject.AddComponent<BatterRig>();
                kv.Value.gameObject.SetActive(false);
            }
            Roles();
            PlaceField();
            Batters();
        }

        /// <summary>The user's bowling innings: a bowler and a field each over, then ball by ball.</summary>
        private IEnumerator BowlingInnings()
        {
            _hud.SetPlaying(false);
            if (_bowlUi == null)
            {
                _bowlUi = BowlingControls.Create(this);
                _bowlUi.MarkChanged += m => ShowAim(m, _match.OnStrike.Player.LeftHanded);
                _bowlUi.FieldPressed += () => _fieldRequested = true;
            }
            _bowlUi.Show();
            // The field is the captain's own for the phase (the owner wants bowling kept simple).
            _match.Preset = FieldPreset.Auto;
            var lastOver = -1;
            while (!_match.Finished)
            {
                if (_match.Over != lastOver)
                {
                    if (lastOver >= 0) yield return SimChipBreak(OverBreak());
                    lastOver = _match.Over;
                    yield return ChooseBowler();
                    Roles();
                    PlaceField();
                    // The field for the over (the presets screen; the countdown keeps the last one).
                    yield return FieldScreen();
                    // Test runs: the field screen on film once, and a field change (MatchDirector.Fields).
                    yield return AutoFieldScreen();
                }
                var simFrom = _match.LegalBalls;
                yield return SimIfAsked();
                if (_match.LegalBalls != simFrom || _match.Finished)
                {
                    _bowlUi.Show();
                    continue;
                }
                yield return FieldBurst(); // -aplfieldburst (deep area 1 test runs): a new field every ball
                yield return BowlBall();
                RestoreWickets();
                // Deep area 1 (iteration 8): the other side's new batter walks out too (MatchDirector.PreMatchBatters).
                if (_match.Incoming != null) yield return NewBatter();
                // A wicket: the user's impact substitution, if he asked for one.
                yield return ImpactIfAsked();
            }
            _bowlUi.Hide();
            yield return new WaitForSeconds(1.2f);
        }

        /// <summary>The bowler cards for this over (the best one allowed by default; auto in test runs).</summary>
        private IEnumerator ChooseBowler()
        {
            var options = _match.BowlerOptions();
            // The default is the captain's plan for the over (what the bar already names), else the best allowed.
            var planned = _match.CurrentBowler;
            var pick = options.Any(o => o.Allowed && o.Player.Id == planned.Id) ? planned
                : options.Where(o => o.Allowed).OrderByDescending(o => o.Player.Bowling).FirstOrDefault()?.Player;
            // Test runs with -aplnudges: the second over goes to a spinner (a stumping to film).
            if (Nudges && _match.Over == 1) pick = options.FirstOrDefault(o => o.Allowed && o.Player.BowlType == BowlType.Spin)?.Player ?? pick;
            if (!Autoplay)
            {
                Player? chosen = null;
                yield return _hud.PickBowler(options, FieldingSide, PickSecs(PickClock.Bowler), p => chosen = p); // owner v26 #8: 10 s, the planned / best allowed at 0
                if (chosen != null) pick = chosen;
            }
            if (pick != null) _match.SetBowler(pick);
            _hud.ShowScore(_match, BattingSide, FieldingSide);
        }

        /// <summary>The last ball of the chase the user bowled at: the winners celebrate, the result card.</summary>
        private IEnumerator MatchEnd()
        {
            if (SuperOverNext)
            {
                yield return SuperOverOrEnd();
                yield break;
            }
            _hud.SetPlaying(false);
            yield return Finale();
            Camera.Show(MatchCamera.Mode.Wide);
            _comm.Say(ResultLine(), important: true); // (a tie has its own call: audio-modes, CommentaryCalls)
            if (_match.UserWon)
            {
                Sfx.Cheer(1f);
                Sfx.Applause(1f);
            }
            _hud.ShowMatchResult(_match, _bat, _bowl);
        }
    }
}
