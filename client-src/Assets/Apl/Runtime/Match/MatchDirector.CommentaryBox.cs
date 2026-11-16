using System.Linq;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The box as two voices, and the ground around it (round 9): after the lead's
    /// call on a boundary, a wicket or now and then a dot, the second voice comes in
    /// — a stat when there is one ("three fours in the over", "his second six",
    /// "{bowler} has three"), else a line of banter; the DJ plays a sting at a boundary. (The stadium PA and
    /// its chime were removed in round 13 at the owner's request.) All of it rate-limited so it never stacks up.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private int _overFours, _overSixes;

        /// <summary>A ball starts (CommentaryBallStarts): the over's counts, a chase exchange.</summary>
        private void BoxBallStarts()
        {
            if (_match.BallInOver != 0 || _nets != NetsLaunch.Kind.None) return;
            _overFours = _overSixes = 0;
            // A tight chase late on: the two talk it over between overs.
            if (_match.Chasing && _match.BallsLeft <= 24 && _match.BallsLeft > 6 && Tension() > 0.45f) StartExchange("chase", 0.4f);
        }

        /// <summary>After the ball (CommentaryAfter): the second voice and the sting (the PA was removed in round 13).</summary>
        private void BoxAfter(BallResult r)
        {
            // (The nets are quiet: no PA, no DJ, no second voice.)
            if (_nets != NetsLaunch.Kind.None)
            {
                NetsCount(r);
                return;
            }
            if (r.Six) _overSixes++;
            else if (r.Four) _overFours++;
            var stat = Stat(r);
            if (r.Six || r.Four)
            {
                // A big six can start a two-voice exchange; otherwise a stat or banter from the second voice.
                var huge = r.Six && SixLine(r) == "six_huge";
                if (!(huge && stat == null && StartExchange("six", 0.6f)))
                    _comm.Follow(stat ?? (r.Six ? "banter_six" : "banter_four"), stat != null ? 0.9f : 0.45f);
                if (Random.value < (r.Six ? 0.7f : 0.4f)) _msfx.Sting();
            }
            else if (r.Wicket)
            {
                if (!(stat == null && StartExchange("wicket", 0.35f)))
                    _comm.Follow(stat ?? "banter_wicket", stat != null ? 0.9f : 0.45f);
            }
            else if (r.Legal && r.Runs == 0 && Random.value < 0.3f) _comm.Follow("banter_dot", 1f);
        }

        /// <summary>An exchange after the lead's call has gone (queued a beat later so it doesn't wait on the live call).</summary>
        private bool StartExchange(string topic, float chance)
        {
            if (Random.value > chance || !_comm.HasEvent("xq_" + topic)) return false;
            StartCoroutine(ExchangeWhenQuiet(topic));
            return true;
        }

        private System.Collections.IEnumerator ExchangeWhenQuiet(string topic)
        {
            // Up to 6 s for the box to fall quiet after the call, then the question and its answer.
            for (var t = 0f; t < 6f; t += 0.25f)
            {
                yield return new WaitForSeconds(0.25f);
                if (_comm.Exchange(topic, 1f)) yield break;
            }
        }

        /// <summary>The stat the ball makes, if any (most telling first).</summary>
        private string? Stat(BallResult r)
        {
            if (r.Wicket && !r.RunOut && _comm.Bowler != null)
            {
                var w = _match.BowlingFigures.FirstOrDefault(f => f.Player.Id == _comm.Bowler.Id)?.Wickets ?? 0;
                return w == 2 ? "stat_bowler_two" : w >= 3 ? "stat_bowler_three" : null;
            }
            if (!r.Four && !r.Six) return null;
            if (r.Six)
            {
                if (_overSixes == 2) return "stat_two_sixes_over";
                var me = _facing == null ? null : _match.Order.FirstOrDefault(b => b.Player.Id == _facing.Id);
                if (me != null && me.Sixes >= 2) return me.Sixes == 2 ? "stat_second_six" : me.Sixes == 3 ? "stat_third_six" : "stat_many_sixes";
            }
            else if (_overFours >= 2) return _overFours == 2 ? "stat_two_fours_over" : "stat_three_fours_over";
            return LastLegal(1) is BallResult prev && (prev.Four || prev.Six) && _match.BallInOver != 1 ? "stat_back_to_back" : null;
        }

        /// <summary>The ground's cue at a fresh match and the result (GroundMusic's watcher): the PA used to speak here — removed in round 13 at the owner's request ("airport-like"), nothing in its place.</summary>
        private void BoxMusicCue(bool matchOver)
        {
        }
    }
}
