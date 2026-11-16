using System.Collections;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Owner v26 #18 and #24, on either side's ball:
    /// the umpire's extras calls — WIDE (arm signal, the run, bowled again), the NO BALL's reason
    /// (overstepped, above the waist, over head height, the third bouncer of the over) and the bouncer
    /// count ("1ST BOUNCER" / "2ND BOUNCER", two allowed an over in the T20 league) — each with a
    /// "[APL extras]" log line for the PM's test runs; and the settle after a dot ball while the user
    /// bats (the bowler walks back, the batter resets) so the next ball doesn't come straight at him.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>After a dot ball (or a wide / no-ball nobody ran on) while batting: this long from the ball going dead to the next one (s, tap skips).</summary>
        private const float DotSettle = 2.6f;

        /// <summary>The extras' calls for this ball (from <see cref="RulesSignals"/>, at the start of its outcome).</summary>
        private void ExtrasCalls(BallResult r)
        {
            var d = r.Delivery;
            var over = $"{_match.Over}.{_match.BallInOver}";
            if (r.Wide)
            {
                Debug.Log($"[APL extras] wide at {over}: +{r.Penalty} (total {r.Runs}), re-bowled; batter {(r.Timing == Timing.Leave ? "left it" : "missed it")}; bowler {(_match.UserBatting ? "AI" : "user")}");
                Extra(After(0.9f, () =>
                {
                    Show("WIDE", r.Runs > 1 ? $"+{r.Runs} · bowled again" : "+1 · bowled again", Color.white, 1.8f);
                    Umpire("umpire_wide");
                }));
                _comm.Say("wide", 0.8f);
            }
            if (r.NoBall)
                Debug.Log($"[APL extras] no-ball at {over}: {NoBallReason(r)}, +{r.Penalty}; free hit next; bowler {(_match.UserBatting ? "AI" : "user")}");
            if (d != null && r.BouncerNumber > 0 && !(d.BouncerNoBall))
            {
                var n = r.BouncerNumber;
                Debug.Log($"[APL extras] bouncer {n} of {QuickMatch.BouncersPerOver} allowed this over (score now at {over})");
                var title = n == 1 ? "1ST BOUNCER" : n == 2 ? "2ND BOUNCER" : $"BOUNCER {n}";
                var sub = n < QuickMatch.BouncersPerOver ? "one more allowed this over" : "that's the over's last — the next is a no-ball";
                Extra(After(1.1f, () => Show(title, sub, UiKit.Gold, 1.6f)));
            }
            if (_match.FreeHit && r.NoBall)
                Debug.Log("[APL extras] free hit next ball (only a run-out can dismiss)");
        }

        /// <summary>Why the no-ball was called, for the banner's second line.</summary>
        private static string NoBallReason(BallResult r)
        {
            var d = r.Delivery;
            if (d == null) return "no-ball";
            if (d.BouncerNoBall) return $"bouncer {r.BouncerNumber} in the over (only {QuickMatch.BouncersPerOver} allowed)";
            if (d.Beamer) return "full toss above the waist";
            if (d.HeadHigh) return "over head height";
            return "overstepped";
        }

        /// <summary>
        /// The batter's breather after a dot (owner v26 #24: the next ball came "too quickly" after a dot,
        /// when a run or a boundary gets its own few seconds): from <paramref name="deadAt"/> (the ball
        /// dead, the call made) at least <see cref="DotSettle"/> s pass before the next ball — the bowler
        /// on his walk back, the batter resetting. A tap skips it. Batting only (bowling, the user
        /// sets up his own next ball at his own pace).
        /// </summary>
        private IEnumerator DotBallSettle(BallResult r, float deadAt)
        {
            if (!_match.UserBatting || r.Wicket || _match.Finished || _nets != NetsLaunch.Kind.None) yield break;
            if (r.Runs > 0 && !r.Wide && !r.NoBall) yield break;
            var wait = DotSettle - (Time.time - deadAt);
            Debug.Log($"[APL gap] dot ball settle: {Mathf.Max(0, wait):0.0} s more ({DotSettle:0.0} s after the ball died; tap skips)");
            if (wait <= 0.05f) yield break;
            // Game time (a pause holds it); a fresh tap anywhere ends it early. Read straight off the input:
            // the HUD's skip catcher only lives inside broadcast moments, and this isn't one (the controls stay).
            var until = Time.time + wait;
            yield return null;
            while (Time.time < until)
            {
                if (!_hud.Paused && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || TouchBegan()))
                {
                    Debug.Log("[APL gap] dot ball settle skipped by tap");
                    yield break;
                }
                yield return null;
            }
        }

        private static bool TouchBegan()
        {
            for (var i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) return true;
            return false;
        }
    }
}
