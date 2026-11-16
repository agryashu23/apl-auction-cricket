using System.Collections.Generic;

namespace Apl.Core.Play
{
    /// <summary>
    /// Simulate (focus batch 1, owner: "a 20-over match takes an hour"): the rest of this over, the rest
    /// of the innings, or up to the last two overs, played at once by the engine — both sides by the AI
    /// (the user's bowler is the captain's plan for each over, his field the one set). Every ball goes
    /// through <see cref="PlayAi"/>, so the score, the batters' and bowlers' figures, the log (over cards,
    /// wagon wheels) and the free-hit and review rules are exactly as if it had been played.
    /// </summary>
    public sealed partial class QuickMatch
    {
        /// <summary>The rest of the current over (all of it, before its first ball). Returns the balls played.</summary>
        public List<BallResult> SimulateOver()
        {
            var balls = new List<BallResult>();
            var over = Over;
            while (!Finished && Over == over) balls.Add(SimBall());
            return balls;
        }

        /// <summary>The rest of the innings.</summary>
        public List<BallResult> SimulateInnings()
        {
            var balls = new List<BallResult>();
            while (!Finished) balls.Add(SimBall());
            return balls;
        }

        /// <summary>Up to the start of over <paramref name="over"/> (0-based), e.g. Overs − 2 for the last two.</summary>
        public List<BallResult> SimulateTo(int over)
        {
            var balls = new List<BallResult>();
            while (!Finished && Over < over) balls.Add(SimBall());
            return balls;
        }

        /// <summary>One ball, the AI on both sides (a user-set delivery that wasn't played is dropped first).</summary>
        private BallResult SimBall()
        {
            _pending = null;
            _pendingTrajectory = null;
            TestNoBall = false;
            return PlayAi();
        }
    }
}
