using System;

namespace Apl.Core.Play
{
    /// <summary>What goes upstairs to the TV umpire.</summary>
    public enum TvCheck { None, RunOut, Stumping, Catch }

    public sealed partial class BallResult
    {
        /// <summary>
        /// Referred to the third umpire (close run outs, stumpings, the odd low
        /// catch) and his decision — the engine decides, the presentation shows
        /// it (MatchDirector.ThirdUmpire). <see cref="Margin"/> is how close it
        /// was: seconds for a run out (+ the batter was in), metres for a
        /// stumping (+ his foot was back).
        /// </summary>
        public TvCheck Referral;
        public bool TvOut;
        public double Margin;

        /// <summary>Out to a genuine edge or a plumb lbw the umpire on the field didn't give ("caught behind", "lbw"), null if none.</summary>
        public string? NotGiven;

        /// <summary>Caught behind: the batter walked before the finger went up.</summary>
        public bool Walked;

        /// <summary>Caught by the bowler off his own bowling.</summary>
        public bool CaughtAndBowled => HowOut == "caught" && Fielder == "bowler";
    }

    /// <summary>
    /// The umpires on top of the engine's outcome: caught and bowled for a
    /// return catch, the odd genuine nick or plumb lbw the on-field umpire turns
    /// down, the batter who walks, and referrals — every stumping, the odd low
    /// catch (and now and then it's ruled not out: it bounced first).
    /// Close run outs are referred by <see cref="Running.Settle"/>.
    /// </summary>
    public static class Umpiring
    {
        /// <summary>Chances the on-field umpire turns down a genuine caught behind / lbw.</summary>
        public const double MissedEdge = 0.08, MissedLbw = 0.06;

        public static void Apply(BallResult r, Rng rng)
        {
            CaughtAndBowled(r, rng);
            if (r.HowOut == "caught behind")
            {
                if (rng.Chance(MissedEdge)) TurnDown(r, "caught behind");
                else r.Walked = rng.Chance(0.25);
            }
            else if (r.HowOut == "lbw" && r.Lbw != null && r.Lbw.Tracking.ClearlyOut(r.Lbw.ShotOffered) && rng.Chance(MissedLbw))
            {
                r.Lbw.OnFieldOut = false;
                TurnDown(r, "lbw");
            }
            if (r.HowOut == "stumped")
            {
                r.Referral = TvCheck.Stumping;
                r.TvOut = true;
                r.Margin = -rng.Range(0.02, 0.4);
            }
            else if (r.MissedStumping || (r.Delivery.IsSpin && !r.Wicket && !r.Shot.IsLeave && r.Timing == Timing.Miss && rng.Chance(0.25)))
            {
                // Bails off with his foot just back (or the keeper fumbling the take): a look upstairs, not out.
                r.Referral = TvCheck.Stumping;
                r.TvOut = false;
                r.Margin = rng.Range(0.01, 0.12);
            }
            else if (r.HowOut == "caught" && !r.CaughtAndBowled && r.Path.InAir && r.Path.Hang < 2.2 && rng.Chance(0.25))
            {
                // A low catch: did it carry?
                r.Referral = TvCheck.Catch;
                r.TvOut = !rng.Chance(0.2);
                if (!r.TvOut)
                {
                    r.Wicket = r.Caught = false;
                    r.HowOut = null;
                    r.Runs = r.Penalty;
                }
            }
        }

        /// <summary>Where the bowler's follow-through takes a return catch (m from the batter), and how far to either side he can dive for it (m).</summary>
        public const double ReturnLine = 17.5, ReturnReach = 2.2;

        /// <summary>A return catch: in the air, straight back and short, and the bowler in his follow-through gets to it.</summary>
        private static void CaughtAndBowled(BallResult r, Rng rng)
        {
            if (r.Wicket || r.Four || r.Six || !r.Path.InAir || r.Path.Carry > 22 || Math.Abs(r.ExitAngle) > 14) return;
            // Deep area 3 (Unity review b3: a C&B 3.3 m wide of the bowler — no return-catch clip or dive reaches it,
            // so the picture bent the ball onto his hands): only what passes within a dive of his follow-through line.
            if (Math.Abs(ReturnLine * Math.Tan(r.ExitAngle * Math.PI / 180)) > ReturnReach) return;
            // (Twice the odds on the narrower line: as many return catches as before, all of them reachable.)
            if (!rng.Chance(r.ExitSpeed > 30 ? 0.6 : 0.95)) return;
            r.Wicket = r.Caught = true;
            r.HowOut = "caught";
            r.Fielder = "bowler";
            r.End = (r.Path.Carry * Math.Sin(r.ExitAngle * Math.PI / 180) * 0.8, 17.5);
            r.Runs = r.Penalty;
            r.Ran = r.Attempted = 0;
            r.Running = null;
        }

        private static void TurnDown(BallResult r, string what)
        {
            r.NotGiven = what;
            r.Wicket = false;
            r.HowOut = null;
            r.Caught = false;
            r.Appeal = true;
            r.Runs = r.Penalty;
        }
    }
}
