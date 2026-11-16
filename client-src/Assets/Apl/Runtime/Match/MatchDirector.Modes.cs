using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Modes;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Apl.Client.Match
{
    /// <summary>
    /// The match's part in the modes (round 8): a challenge's banner and stars, and
    /// for every match the career XP — the side's runs, sixes and wickets, the result
    /// and the difficulty — shown on a reward card once the result is in (RewardCard),
    /// with RETRY / CHALLENGES after a challenge.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The challenge this scene is playing (null: a match).</summary>
        private Challenge? _challenge;

        /// <summary>From BeginPreMatch (not the nets): take the challenge, if it is one, and wait for the result.</summary>
        private void ModesStart()
        {
            _challenge = ChallengeLaunch.Active;
            // Taken: PLAY AGAIN on the result card is a normal match again.
            ChallengeLaunch.Active = null;
            StartCoroutine(ModesWatch());
        }

        /// <summary>The resumed-match banner, or the challenge's own.</summary>
        private (string title, string sub)? ChallengeBanner() =>
            _challenge == null ? ((string, string)?)null : (_challenge.Title.ToUpperInvariant(), _challenge.Situation);

        /// <summary>A challenge is never saved over the user's real match in progress.</summary>
        private bool NoResumeSave => _challenge != null;

        private IEnumerator ModesWatch()
        {
            while (_match == null || !_match.MatchOver) yield return null;
            var main = _match;
            var user = main.UserTeam.TeamId;
            var won = main.UserWon;
            var tied = main.Winner == null;
            if (SuperOver.Needed(main))
            {
                while (_superOver == null || _superOver.Main != main || !_superOver.Over) yield return null;
                won = _superOver.Winner?.TeamId == user;
                tied = false;
            }
            // Both innings' balls, each with who batted in it.
            var innings = new List<(string batting, List<BallResult> log)> { (main.Batting.TeamId, main.Log) };
            // (The first innings' batting side is the one bowling now.)
            if (main.FirstLog != null) innings.Add((main.Bowling.TeamId, main.FirstLog));
            var mine = innings.Where(i => i.batting == user).SelectMany(i => i.log).ToList();
            var theirs = innings.Where(i => i.batting != user).SelectMany(i => i.log).ToList();
            var stats = new MatchStats
            {
                Won = won, Tied = tied, Challenge = _challenge != null,
                RunsScored = mine.Sum(b => b.Runs), Sixes = mine.Count(b => b.Six), Fours = mine.Count(b => b.Four),
                WicketsTaken = theirs.Count(b => b.Wicket),
            };
            var stars = 0;
            if (_challenge != null)
            {
                var userBats = main.UserBatting;
                var outcome = new ChallengeOutcome
                {
                    Won = won,
                    BallsLeft = main.BallsLeft,
                    Wickets = main.Log.Count(b => b.Wicket),
                    Runs = main.Log.Sum(b => b.Runs),
                    Sixes = userBats ? main.Log.Count(b => b.Six) : 0,
                };
                stats.Stars = stars = _challenge.Stars(outcome);
                stats.StarsBefore = Progression.StarsFor(_challenge.Id);
            }
            var award = Progression.Record(stats, _challenge?.Id);
            // Kept until it's seen: left before CONTINUE, the home page shows it (ModesEntry).
            RewardCard.Pending = (award, _challenge, stars);
            Debug.Log($"[APL modes] match over: won {won}, +{award.Xp} XP (level {award.LevelBefore}->{award.LevelAfter}){(_challenge != null ? $", challenge {_challenge.Id} {stars}*" : "")}");
            // Its own screen, after the result: only once CONTINUE on the result card is pressed (presentation's
            // MatchHud.ResultCard.cs; Autoplay presses it itself) — never over the last ball or the finale.
            while (!_hud.ResultContinued) yield return null;
            var ch = _challenge;
            // A tournament fixture: the rewards screen's CONTINUE goes back to the table (round 13: it
            // stayed on the rewards, and the tournament never moved on). The result is already in the
            // table (WatchForResult records it as the match ends).
            System.Action? done = Apl.Client.Menu.TournamentLaunch.Active != null && ch == null
                ? () =>
                {
                    Apl.Client.Menu.TournamentLaunch.ReturnToHub = true;
                    SceneManager.LoadScene(0);
                }
                : (System.Action?)null;
            RewardCard.Show(award, ch, stars,
                ch == null ? (System.Action?)null : () => ChallengeLaunch.Play(_data, ch),
                ch == null ? (System.Action?)null : () =>
                {
                    ChallengeLaunch.ReturnToList = true;
                    SceneManager.LoadScene(0);
                }, done);
        }
    }
}
