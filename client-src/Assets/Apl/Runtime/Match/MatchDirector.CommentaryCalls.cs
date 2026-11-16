using System.Linq;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The commentary box reading the game (owner's list, item 21): who's on
    /// (for the name slots), the chase in numbers at the business end ("they need
    /// 12 off 6"), the dots piling up, the first boundary of an over, a batter
    /// nearing fifty, a stand building, edges, drops, no-balls, a maiden or a big
    /// over — on top of the per-ball lines the director already says.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private int _dotRun, _overRuns, _partnershipCalled;
        private bool _overBoundary;
        private readonly System.Collections.Generic.HashSet<string> _nearFifty = new System.Collections.Generic.HashSet<string>();

        /// <summary>A ball starts: names for the slots, the clock, and the target when it's tight.</summary>
        private void CommentaryBallStarts()
        {
            _comm.Batter = _match.OnStrike.Player;
            _comm.Bowler = _match.CurrentBowler;
            _comm.NextBall();
            BoxBallStarts();
            if (_match.BallInOver == 0)
            {
                _overRuns = 0;
                _overBoundary = false;
            }
            if (_match.FreeHit) _comm.Say("free_hit", 0.8f);
            // On a hat-trick: the last two legal balls of this over both took wickets (not run outs).
            if (_match.BallInOver >= 2 && LastLegal(0) is BallResult a && LastLegal(1) is BallResult b && a.Wicket && b.Wicket && !a.RunOut && !b.RunOut)
                _comm.Say("hat_trick_ball", important: true);
            if (!_match.Chasing || _match.Finished) return;
            var balls = _match.BallsLeft;
            // The last ball with it still on: its own call (else the numbers below).
            if (balls == 1 && _match.RunsNeeded <= 6 && _comm.TrySay("last_ball", important: true)) return;
            // The first ball of each of the last three overs, and now and then in the last two.
            var say = balls <= 18 && _match.BallInOver == 0 || balls <= 12 && Random.value < 0.25f;
            if (say && _match.RunsNeeded <= 60) _comm.SayNeed(_match.RunsNeeded, balls);
        }

        /// <summary>After a ball (and its own line): what the situation says.</summary>
        private void CommentaryAfter(BallResult r)
        {
            _overRuns += r.Runs;
            BoxAfter(r);
            if (r.NoBall) _comm.Say("no_ball", 0.9f);
            if (r.Dropped.Length > 0) _comm.Say("dropped", important: true);
            else if (r.Edge && !r.Wicket && r.Runs > 0) _comm.Say("edge", 0.6f);
            if (r.Legal && r.Runs == 0 && !r.Wicket) _dotRun++;
            else _dotRun = 0;
            if (_dotRun >= 3 && _dotRun % 2 == 1) _comm.Say("pressure_building", 0.7f);
            // More two-voice exchanges (round 13): a run of dots, a fifty (the maiden's is below).
            if (_dotRun == 4) StartExchange("pressure", 0.45f);
            foreach (var b in _match.Order)
                if (b.Runs >= 50 && b.HowOut == null && _fiftyTalk.Add(b.Player.Id)) StartExchange("milestone", 0.7f);
            if (r.Four || r.Six)
            {
                if (!_overBoundary && _match.BallInOver != 1) _comm.Say("first_boundary_over", 0.45f);
                _overBoundary = true;
            }
            var me = _facing == null ? null : _match.Order.FirstOrDefault(b => b.Player.Id == _facing.Id);
            if (me != null && me.HowOut == null && me.Runs >= 44 && me.Runs < 50 && _nearFifty.Add(me.Player.Id)) _comm.Say("milestone_near", 0.8f);
            // A hundred (the director's Milestone says the fifty; the hundred is the box's own).
            foreach (var bat in _match.Order)
                if (bat.Runs >= 100 && _centuries.Add(bat.Player.Id)) StartCoroutine(After(2.2f, () => _comm.Say("hundred", important: true)));
            // The last over: every boundary and wicket is the game; two in two is a story.
            var lastOver = _match.Chasing && !_match.Finished && _match.BallsLeft + (r.Legal ? 1 : 0) <= 6;
            if (lastOver && (r.Four || r.Six)) StartCoroutine(After(1.8f, () => _comm.Say("last_over_boundary", 0.8f)));
            if (r.Wicket && LastLegal(1) is BallResult prev && prev.Wicket) StartCoroutine(After(2.6f, () => _comm.Say("two_in_two", 0.9f)));
            else if (r.Wicket && lastOver) StartCoroutine(After(2.6f, () => _comm.Say("last_over_wicket", 0.9f)));
            if (r.Wicket) _partnershipCalled = 0;
            else
            {
                var stand = 0;
                for (var i = _match.Log.Count - 1; i >= 0 && !_match.Log[i].Wicket; i--) stand += _match.Log[i].Runs;
                var mark = stand >= 50 ? 2 : stand >= 30 ? 1 : 0;
                if (mark > _partnershipCalled)
                {
                    _partnershipCalled = mark;
                    _comm.Say("partnership", 0.8f);
                }
            }
            // The over's done: a maiden, or a big one.
            if (r.Legal && _match.BallInOver == 0 && _match.LegalBalls > 0)
            {
                if (_overRuns == 0)
                {
                    _comm.Say("maiden", important: true);
                    StartExchange("maiden", 0.6f);
                }
                else if (_overRuns >= 15) _comm.Say("big_over", 0.9f);
            }
        }

        /// <summary>The wicket's line by how he went (hit wicket and caught behind have their own, where generated).</summary>
        private string WicketLine(BallResult r) => r.HowOut switch
        {
            "bowled" => "bowled",
            "lbw" => "lbw",
            "run out" => "run_out",
            "stumped" => "stumped",
            "hit wicket" => _comm.First("hit_wicket", "out"),
            "caught behind" => _comm.First("caught_behind", "caught"),
            _ => r.Caught ? "caught" : "out",
        };

        /// <summary>
        /// The six's call by how it was hit (round 12): off an edge "six_edge"; middled and
        /// fast off the bat (perfect timing, quality 0.85+, or 34 m/s+) "six_huge"; else "six".
        /// Falls back to "six" until the lines are installed.
        /// </summary>
        private string SixLine(BallResult r) =>
            r.Edge ? _comm.First("six_edge", "six")
            : (r.Timing == Timing.Perfect && r.Quality >= 0.85) || r.ExitSpeed >= 34 ? _comm.First("six_huge", "six")
            : "six";

        /// <summary>The four's call: edged "four_edge"; hammered (32 m/s+) "four_cracking"; soft but timed (under 24 m/s) "four_placed"; else "four".</summary>
        private string FourLine(BallResult r) =>
            r.Edge ? _comm.First("four_edge", "four")
            : r.ExitSpeed >= 32 ? _comm.First("four_cracking", "four")
            : r.ExitSpeed < 24 && r.Timing == Timing.Perfect ? _comm.First("four_placed", "four")
            : "four";

        /// <summary>The result's call for the user: "won", "lost" — or "tied" (round 13: a tie got a "lost" line; until the "tied" clips are made, a tie is simply not called).</summary>
        private string ResultLine() =>
            _match.MatchOver && _match.Winner == null ? "tied" : _match.UserWon ? "won" : "lost";

        private readonly System.Collections.Generic.HashSet<string> _fiftyTalk = new System.Collections.Generic.HashSet<string>();

        private readonly System.Collections.Generic.HashSet<string> _centuries = new System.Collections.Generic.HashSet<string>();

        /// <summary>The <paramref name="back"/>-th most recent legal ball in this innings' log (0 = the last), or null.</summary>
        private BallResult? LastLegal(int back)
        {
            for (var i = _match.Log.Count - 1; i >= 0; i--)
            {
                if (!_match.Log[i].Legal) continue;
                if (back-- == 0) return _match.Log[i];
            }
            return null;
        }
    }
}
