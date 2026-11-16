using System;
using System.Collections;
using System.Linq;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Apl.Client.Match
{
    /// <summary>
    /// The nets (PRACTICE): the match scene with no ceremony and nothing riding
    /// on it — ball after ball for as long as you like, a tip panel walking
    /// through the controls (NetsTips), EXIT to leave. Batting: the opponent's
    /// attack bowls at you; the score strip and radar are off and a finished
    /// innings simply starts again. Bowling: you bowl at their batters (the
    /// marker, the ball type, the release), over after over.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private NetsLaunch.Kind _nets;
        private NetsTips? _tips;

        private static readonly string[] BatTips =
        {
            "TIMING: the meter on the right fills as the ball comes. Tap your shot as it reaches the green band — PERFECT in the middle.",
            "AIM: push the stick (bottom left) toward the gap before you tap. With SWIPE controls (pause ► settings) you swipe the way to hit.",
            "PUSH defends and drops it for a single, STROKE hits along the ground, LOFT goes in the air — big, but it can be caught.",
            "LEAVE lets a ball go: wide of off stump, or short and climbing. Never leave one that's hitting the stumps!",
            "FOOTWORK: AUTO picks front or back foot from the length. MANUAL: FRONT FOOT to full balls, BACK FOOT to short ones.",
            "Read the ball: watch the pitch marker and the speed (top right). Spinners turn — aim with the turn. Keep going, or EXIT when you're done.",
        };

        private static readonly string[] BowlTips =
        {
            "THE MARKER: move the stick (bottom left) to set where the ball pitches. Further up is fuller, closer to you is shorter.",
            "LINE AND LENGTH: just outside off stump on a good length is the safe ball. Yorkers at the stumps for the death; a bouncer now and then.",
            "BALL TYPE: pick it on the right — swing, cutters, slower balls for a quick; off spin, leg spin, arm ball for a spinner.",
            "RELEASE: tap BOWL (with the release meter, tap as it crosses the green). A bad release strays — wider, fuller or shorter than you aimed.",
            "Mix it up: the same ball every time gets hit. Keep bowling, or EXIT when you're done.",
        };

        /// <summary>The nets in place of the ceremony (BeginPreMatch); false: it's a match.</summary>
        private bool BeginNets()
        {
            _nets = NetsLaunch.Mode;
            NetsLaunch.Mode = NetsLaunch.Kind.None;
            if (_nets == NetsLaunch.Kind.None || !_cast.Ready) return false;
            // No commentary, PA or crowd reactions in the nets; the big screen counts the session (audio-modes: MatchDirector.NetsFeel.cs).
            NetsFeelStart();
            NetsEmpty(); // characters: nobody standing round an empty practice net
            StartCoroutine(_nets == NetsLaunch.Kind.Batting ? NetsBatting() : NetsBowling());
            return true;
        }

        private void LeaveNets() => SceneManager.LoadScene(0);

        /// <summary>The score strip and the field radar off: nothing to chase in the nets.</summary>
        private void NetsHud()
        {
            HudVisible(true);
            foreach (var name in new[] { "ScoreBar", "Radar" })
            {
                var t = _hud.transform.Find(name);
                if (t != null) t.gameObject.SetActive(false);
            }
        }

        private IEnumerator NetsBatting()
        {
            _tips = NetsTips.Create("BATTING NETS", BatTips, LeaveNets);
            yield return null;
            NetsHud();
            _hud.SetPlaying(true);
            var lastOver = -1;
            while (true)
            {
                if (_match.Finished)
                {
                    // All out or the overs gone: a fresh innings, same bodies.
                    _match = new QuickMatch(SheetFor(_bat.Id), SheetFor(_bowl.Id), MatchLaunch.Overs, DateTime.Now.Ticks & 0x7FFFFFFF, _venue);
                    lastOver = -1;
                }
                if (_match.Over != lastOver)
                {
                    lastOver = _match.Over;
                    Roles();
                    PlaceField();
                }
                yield return Ball();
                NetsHud();
                _tips.Ball();
            }
        }

        private IEnumerator NetsBowling()
        {
            _tips = NetsTips.Create("BOWLING NETS", BowlTips, LeaveNets);
            NewNetsBowling();
            yield return null;
            NetsHud();
            if (_bowlUi == null)
            {
                _bowlUi = BowlingControls.Create(this);
                _bowlUi.MarkChanged += m => ShowAim(m, _match.OnStrike.Player.LeftHanded);
            }
            _bowlUi.Show();
            var lastOver = -1;
            while (true)
            {
                if (_match.Finished)
                {
                    NewNetsBowling();
                    lastOver = -1;
                    yield return null;
                }
                if (_match.Over != lastOver)
                {
                    lastOver = _match.Over;
                    // The best bowler allowed this over (no bowler cards in the nets).
                    var pick = _match.BowlerOptions().Where(o => o.Allowed).OrderByDescending(o => o.Player.Bowling).FirstOrDefault()?.Player;
                    if (pick != null) _match.SetBowler(pick);
                    Roles();
                    PlaceField();
                }
                yield return BowlBall();
                NetsHud();
                _tips.Ball();
            }
        }

        /// <summary>A full match with the user bowling first: their batters in, his side in the field, bodies in the right kits.</summary>
        private void NewNetsBowling()
        {
            _match = QuickMatch.Full(SheetFor(_bat.Id), SheetFor(_bowl.Id), MatchLaunch.Overs, DateTime.Now.Ticks & 0x7FFFFFFF, _venue, userBatsFirst: false);
            Recast();
            _striker.TakeGuard();
            PlaceOtherBatter();
        }
    }
}
