using System;

namespace Apl.Core.Play
{
    /// <summary>Where the batting side stands, for the AI batter's choices.</summary>
    public readonly struct Situation
    {
        public Situation(int runs, int wickets, int ballsLeft, int overs, int? target, int batterBalls, bool freeHit, int phase, int maxWickets = 10)
        {
            Runs = runs;
            Wickets = wickets;
            BallsLeft = ballsLeft;
            Overs = overs;
            Target = target;
            BatterBalls = batterBalls;
            FreeHit = freeHit;
            Phase = phase;
            MaxWickets = maxWickets;
        }

        public int Runs { get; }
        public int Wickets { get; }
        public int BallsLeft { get; }
        public int Overs { get; }

        /// <summary>Null batting first.</summary>
        public int? Target { get; }

        /// <summary>Balls the striker has faced (he plays himself in).</summary>
        public int BatterBalls { get; }
        public bool FreeHit { get; }
        public int Phase { get; }
        public int MaxWickets { get; }

        public double RequiredRate => Target is int t && BallsLeft > 0 ? Math.Max(0, t - Runs) * 6.0 / BallsLeft : 0;
    }

    /// <summary>
    /// The AI batter (the user's bowling innings): reads the ball, picks a shot
    /// and a gap to suit it and the game, and times it as well as his batting
    /// allows. Aggression rises with the required rate (or the phase batting
    /// first), falls with wickets down, and is total on a free hit. Variations
    /// he doesn't read (a slower ball, a wrong'un) throw his timing.
    /// </summary>
    public static class BatterBrain
    {
        /// <summary>0 (blocking it out) … 1 (swinging at everything).</summary>
        public static double Aggression(in Situation s)
        {
            if (s.FreeHit) return 1;
            double a;
            if (s.Target != null)
            {
                var rrr = s.RequiredRate;
                a = DartMath.Clamp((rrr - 5.5) / 7.0, 0.05, 1);
                if (s.BallsLeft <= 12) a = Math.Max(a, DartMath.Clamp((rrr - 4) / 6, 0, 1));
            }
            else
            {
                a = s.Phase switch { 0 => 0.26, 1 => 0.2, _ => 0.6 };
                // The last six overs are for hitting, whatever the match length (a 2-over game
                // batted at powerplay caution scored 8 an over and almost never lost a wicket).
                if (s.BallsLeft <= 36) a = Math.Max(a, 0.5 + 0.35 * (1 - s.BallsLeft / 36.0));
            }
            var inHand = s.MaxWickets - s.Wickets;
            if (inHand <= 3 && (s.Target == null || s.RequiredRate < 12)) a *= 0.7;
            else if (inHand >= 7 && s.Phase == 2) a = Math.Min(1, a + 0.1);
            if (s.BatterBalls < 4) a *= 0.8;
            return DartMath.Clamp(a, 0.05, 1);
        }

        public static ShotInput Choose(Player batter, Player bowler, Trajectory t, Field field, in Situation s, Rng rng)
        {
            var d = t.Delivery;
            var a = Aggression(s);
            var skill = DartMath.Clamp((batter.Batting - 25) / 70.0, 0, 1);
            var hitting = t.HittingStumps();

            // Owner v26 #18: a clearly wide ball (past the wide guideline, either side) is left alone —
            // the umpire calls it, the run's added, and it's bowled again. He doesn't chase it.
            if (d.Wide) return ShotInput.Leave;

            // Leave the ones outside off he needn't play (not when he's after runs).
            if (!hitting && (d.Line == Line.WideOff || d.Line == Line.OutsideOff) && rng.Chance(0.5 * (1 - a) * (0.6 + 0.4 * skill))) return ShotInput.Leave;

            // The shot: in the air with the rate (and to balls in his arc), soft hands to the good ones.
            var inArc = d.FullToss || d.Length == Length.Full || (d.Length == Length.Short && !d.IsSpin);
            var pLoft = 0.03 + 0.45 * a + (inArc ? 0.1 : 0) - (d.Length == Length.Yorker ? 0.15 : 0);
            var pPush = (0.42 - 0.36 * a) * (d.Length == Length.Good || d.Length == Length.Yorker ? 1.3 : 0.8);
            var roll = rng.NextDouble();
            var type = roll < pLoft ? ShotType.Loft : roll < pLoft + pPush ? ShotType.Push : ShotType.Stroke;

            // Where: the best direction for the ball, away from the fielders (a lesser batter misjudges it).
            var zone = Zone.StraightOff;
            var best = double.MinValue;
            foreach (Zone z in Enum.GetValues(typeof(Zone)))
            {
                var score = Batting.Suitability(d, type, z) * Gap(field, z, type) + Bowling.Normal(rng) * 0.12 * (1.2 - skill);
                if (score <= best) continue;
                best = score;
                zone = z;
            }

            // When: closer to the ideal the better he is; tougher against what he doesn't pick.
            // Round 13: swinging hard he is less sure of his timing (plain 3-over slogs lost 1 wicket an innings; real ones lose 2+).
            var sigma = 1.4 - 0.62 * skill + 0.35 * Math.Max(0, a - 0.45);
            // A variation well released (the user's PERFECT / GOOD release) is harder to pick (final rating 2026-11-14).
            var read = rng.Chance(0.35 + 0.5 * skill - 0.15 * Batting.ReleaseSting(d));
            var bias = 0.0;
            if (d.Variation == Variation.Slower && !read) bias = d.IsSpin ? -0.6 : -1.6;
            if (!read && (d.Variation == Variation.Wrong || d.Variation == Variation.Carrom || d.Variation == Variation.ArmBall || d.Variation == Variation.Flipper)) sigma += 0.7;
            if ((d.Variation == Variation.OffCutter || d.Variation == Variation.LegCutter) && !read) bias = -0.7;
            if (d.Length == Length.Yorker && hitting) sigma *= 1.25;
            if (d.FullToss && !d.Beamer) sigma *= 0.8;
            // Swinging hard he commits early.
            if (type == ShotType.Loft) sigma *= 1.1;
            var window = Batting.Window(batter, d);
            var tap = Batting.IdealTap(t) + (bias + Bowling.Normal(rng) * sigma) * window;
            return new ShotInput(type, zone, tap);
        }

        /// <summary>How open the zone is for this shot: a man on the rope there (lofted) or in the ring (along the ground) halves it.</summary>
        private static double Gap(Field field, Zone z, ShotType type)
        {
            var angle = Field.ZoneAngle(z);
            var g = 1.0;
            foreach (var f in field.Fielders)
            {
                var diff = Math.Abs(((f.AngleDeg - angle + 540) % 360) - 180);
                var deep = Field.OutsideRing(f);
                if (type == ShotType.Loft ? deep && diff < 22 : !deep && diff < 16) g *= 0.6;
                else if (type != ShotType.Loft && deep && diff < 18) g *= 0.85;
            }
            return g;
        }
    }
}
