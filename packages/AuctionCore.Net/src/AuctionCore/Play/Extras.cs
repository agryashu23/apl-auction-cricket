using System;

namespace Apl.Core.Play
{
    public static partial class Bowling
    {
        /// <summary>
        /// A bouncer for the over's count (owner v26 #18, the T20 league rule): a pace ball dug in on the
        /// bouncer length that pitches (a full toss isn't one). One already over head height is its own
        /// no-ball and doesn't use up the allowance; nor does a spinner's long hop.
        /// </summary>
        public static bool CountsAsBouncer(Delivery d) =>
            !d.IsSpin && !d.FullToss && !d.Beamer && !d.HeadHigh && d.Length == Length.Bouncer;
    }

    /// <summary>
    /// Extras on top of the laws in Rules.cs (owner v26 #18): the bouncer limit — two an over, the third
    /// a no-ball (and the free hit follows) — and the extras broken down for the scorecard.
    /// </summary>
    public sealed partial class QuickMatch
    {
        /// <summary>Bouncers allowed in an over; the next one is called a no-ball.</summary>
        public const int BouncersPerOver = 2;

        /// <summary>Bouncers bowled so far in the over in progress (wides and no-balls in it included).</summary>
        public int BouncersThisOver
        {
            get
            {
                var n = 0;
                var legal = 0;
                for (var i = Log.Count - 1; i >= 0; i--)
                {
                    var r = Log[i];
                    if (r.Legal)
                    {
                        // Back past this over's legal balls: the last over's.
                        if (legal == BallInOver) break;
                        legal++;
                    }
                    if (r.BouncerNumber > 0) n++;
                }
                return n;
            }
        }

        /// <summary>The pending ball's place in the bouncer count, and the no-ball once the over's two are gone.</summary>
        private void BouncerRule()
        {
            var d = _pending;
            if (d == null) return;
            d.BouncerNumber = 0;
            d.BouncerNoBall = false;
            if (!global::Apl.Core.Play.Bowling.CountsAsBouncer(d)) return;
            d.BouncerNumber = BouncersThisOver + 1;
            if (d.BouncerNumber <= BouncersPerOver || d.NoBall) return;
            d.NoBall = true;
            d.Wide = false;
            d.BouncerNoBall = true;
        }

        /// <summary>
        /// The AI bowler knows the rule: with the over's two bouncers gone he usually bowls something else
        /// (now and then one still gets away from him — and it's called).
        /// </summary>
        private void AiBouncerDiscipline()
        {
            for (var tries = 0; tries < 4 && _pending != null; tries++)
            {
                if (!global::Apl.Core.Play.Bowling.CountsAsBouncer(_pending) || BouncersThisOver < BouncersPerOver) return;
                if (_rng.Chance(0.15)) return;
                _pending = global::Apl.Core.Play.Bowling.Bowl(CurrentBowler, Phase, _rng, OnStrike.Player.LeftHanded);
            }
        }

        /// <summary>This innings' extras by kind: wides, no-balls (the penalty runs), byes, leg byes.</summary>
        public (int wides, int noBalls, int byes, int legByes) ExtrasBreakdown()
        {
            int wd = 0, nb = 0, b = 0, lb = 0;
            foreach (var r in Log)
            {
                // (The parts add up to the innings' Extras: runs run off a wide are booked as byes.)
                if (r.Wide) wd += r.Penalty;
                else if (r.NoBall) nb += r.Penalty;
                b += r.Byes;
                lb += r.LegByes;
            }
            return (wd, nb, b, lb);
        }

        /// <summary>"Extras 7 (wd 3, nb 2, b 1, lb 1)" — the scorecard's line (only the kinds there were).</summary>
        public string ExtrasText()
        {
            var (wd, nb, b, lb) = ExtrasBreakdown();
            var parts = new System.Collections.Generic.List<string>();
            if (wd > 0) parts.Add($"wd {wd}");
            if (nb > 0) parts.Add($"nb {nb}");
            if (b > 0) parts.Add($"b {b}");
            if (lb > 0) parts.Add($"lb {lb}");
            return parts.Count == 0 ? $"Extras {Extras}" : $"Extras {Extras} ({string.Join(", ", parts)})";
        }
    }
}
