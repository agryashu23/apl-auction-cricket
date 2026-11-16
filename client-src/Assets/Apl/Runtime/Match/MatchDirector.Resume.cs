using System;
using System.Collections;
using Apl.Client.Menu;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Resume a match after the app's been closed: the book is saved whenever
    /// a ball's been settled (MatchResume), and RESUME MATCH comes back here at
    /// the next ball — no ceremony — in whichever innings it was, batting or bowling.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>This scene is a resumed match.</summary>
        private MatchResume.Record? _resumed;

        /// <summary>The saved match the menu asked for (called in Start, in place of a new one); null for a new match.</summary>
        private QuickMatch? ResumedMatch()
        {
            var r = MatchResume.Pending;
            MatchResume.Pending = null;
            if (r == null) return null;
            try
            {
                var m = QuickMatch.Restore(r.Match, _data, DateTime.Now.Ticks & 0x7FFFFFFF);
                _resumed = r;
                UserBatsFirst = r.UserBatsFirst;
                TossWinnerId = r.TossWinner;
                return m;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL] the saved match wouldn't load: {ex.Message}");
                MatchResume.Delete();
                return null;
            }
        }

        /// <summary>Straight back in at the next ball, in the innings the save was in.</summary>
        private IEnumerator ResumeFlow()
        {
            _pre = PreMatchUi.Create();
            // The bodies were made for the user batting: in his bowling innings, recast in the right kits.
            if (_match.Batting.TeamId != _bat.Id) Recast();
            yield return null;
            _striker.TakeGuard();
            PlaceOtherBatter();
            HudVisible(true);
            _hud.ShowScore(_match, BattingSide, FieldingSide);
            Camera.Show(MatchCamera.Mode.Wide, cut: true);
            // A challenge (audio-modes: MatchDirector.Modes.cs) says what it is, and holds a moment longer.
            var banner = ChallengeBanner() ?? ("MATCH RESUMED", $"{BattingSide.ShortName} {_match.Runs}/{_match.Wickets} ({Cricket.FormatOvers(_match.LegalBalls)})");
            _hud.Banner(banner.title, banner.sub, UiKit.Gold, _challenge != null ? 3.5f : 2.5f);
            yield return new WaitForSeconds(Autoplay ? 0.5f : _challenge != null ? 3.2f : 2.2f);
            if (_match.UserBatting)
            {
                // The chase loop carries a full match's first innings on into the break and the bowling.
                StartCoroutine(Chase());
                yield break;
            }
            yield return BowlingInnings();
            if (_match.TwoInnings && !_match.Chasing)
            {
                yield return InningsBreak();
                yield return ChooseOpeners();
                yield return WalkOut();
                yield return Intros();
                StartCoroutine(Chase());
            }
            else yield return MatchEnd();
        }

        /// <summary>
        /// Saves the book once a ball's settled (nothing has changed for a moment —
        /// the user's running and reviews rework the last ball after it's played);
        /// the save goes when the match is over.
        /// </summary>
        private IEnumerator KeepSaving()
        {
            var last = "";
            var changed = -1f;
            // A challenge is never written over the user's real match in progress (audio-modes).
            if (NoResumeSave) yield break;
            while (true)
            {
                yield return new WaitForSeconds(0.5f);
                if (_match == null) continue;
                if (_match.MatchOver || (!_match.TwoInnings && _match.Finished))
                {
                    MatchResume.Delete();
                    yield break;
                }
                // Nothing played yet: nothing worth resuming (the ceremony runs again).
                if (_match.Log.Count == 0 && _match.InningsNumber == 1) continue;
                var key = $"{_match.InningsNumber}:{_match.Log.Count}:{_match.Runs}:{_match.Wickets}:{_match.LegalBalls}:{_match.Striker}:{_match.NonStriker}";
                if (key != last)
                {
                    last = key;
                    changed = Time.time;
                    continue;
                }
                if (changed < 0 || Time.time - changed < 1.5f) continue;
                changed = -1f;
                MatchResume.Write(new MatchResume.Record
                {
                    MyTeam = _bat.Id,
                    Opponent = _bowl.Id,
                    VenueId = _venue.Id,
                    Overs = _match.Overs,
                    TournamentFixture = TournamentLaunch.Active != null ? TournamentLaunch.Fixture : 0,
                    UserBatsFirst = UserBatsFirst,
                    TossWinner = TossWinnerId,
                    Match = _match.Save(),
                });
            }
        }
    }
}
