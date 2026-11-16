using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    /// <summary>
    /// An AI bowler's choices: what to bowl for the phase and the moment, and how
    /// well it comes out. Better bowlers hit their length and line more often;
    /// poor ones spray it (and bowl wides, and overstep). The user's deliveries
    /// (<see cref="Deliver"/>) come out of the same physics from where he put the
    /// marker and how well he timed the release.
    /// </summary>
    public static partial class Bowling
    {
        /// <summary>Pitching distance ranges (m from the batter's stumps) for each length.</summary>
        public static (double near, double far) Range(Length l, bool spin) => (l, spin) switch
        {
            (Length.Yorker, _) => (0.4, 1.6),
            (Length.Full, false) => (2.0, 4.0),
            (Length.Good, false) => (4.0, 7.0),
            (Length.Short, false) => (7.0, 9.5),
            (Length.Bouncer, false) => (9.5, 11.5),
            (Length.Full, true) => (1.6, 3.5),
            (Length.Good, true) => (3.5, 5.5),
            (Length.Short, true) => (5.5, 7.5),
            _ => (7.5, 9.0),
        };

        /// <summary>Lateral position at the batter (m, + = off side) aimed for on each line.</summary>
        public static double LineX(Line l) => l switch
        {
            Line.WideOff => 0.95,
            Line.OutsideOff => 0.42,
            Line.Off => 0.12,
            Line.Middle => 0.0,
            Line.Leg => -0.12,
            _ => -0.5,
        };

        /// <summary>Classify where a ball actually ended up.</summary>
        public static Length LengthOf(double bounceZ, bool spin)
        {
            foreach (var l in new[] { Length.Yorker, Length.Full, Length.Good, Length.Short })
            {
                if (bounceZ < Range(l, spin).far) return l;
            }
            return Length.Bouncer;
        }

        public static Line LineOf(double x) =>
            x > 0.89 ? Line.WideOff : x > 0.26 ? Line.OutsideOff : x > 0.06 ? Line.Off : x > -0.06 ? Line.Middle : x > -0.4 ? Line.Leg : Line.WideLeg;

        // ------------------------------------------------------------ the bowler

        /// <summary>Spinners are finger spinners (<see cref="DeliveryKind.OffSpin"/>: off-break, or left-arm orthodox) or wrist spinners (leg-spin, or left-arm wrist), fixed per player.</summary>
        public static DeliveryKind KindFor(Player bowler)
        {
            if (bowler.BowlType != BowlType.Spin) return DeliveryKind.Pace;
            // The dataset's researched style first; the old parity hash only when it's missing.
            if (bowler.BowlStyle == "wrist") return DeliveryKind.LegSpin;
            if (bowler.BowlStyle == "finger") return DeliveryKind.OffSpin;
            var h = 0;
            foreach (var c in bowler.Id) h = h * 31 + c;
            return (h & 1) == 0 ? DeliveryKind.OffSpin : DeliveryKind.LegSpin;
        }

        /// <summary>
        /// Bowls left-arm: the dataset's bowlArm ("L"/"R"); where that's missing, about
        /// one bowler in four is left-arm, fixed per player (a stable hash of his id).
        /// </summary>
        public static bool LeftArm(Player bowler) =>
            bowler.BowlArm == "L" || (bowler.BowlArm != "R" && Stable(bowler.Id + "#arm") % 100 < 25);

        /// <summary>"Left-arm orthodox", "Right-arm fast", … for cards and commentary.</summary>
        public static string StyleName(Player bowler)
        {
            var arm = LeftArm(bowler) ? "Left-arm" : "Right-arm";
            return KindFor(bowler) switch
            {
                DeliveryKind.OffSpin => LeftArm(bowler) ? "Left-arm orthodox" : "Right-arm off-spin",
                DeliveryKind.LegSpin => LeftArm(bowler) ? "Left-arm wrist spin" : "Right-arm leg-spin",
                _ => $"{arm} {PaceName(PaceOf(bowler)).ToLowerInvariant()}",
            };
        }

        /// <summary>What this bowler can bowl (the stock ball first).</summary>
        public static IReadOnlyList<Variation> Variations(Player bowler) => KindFor(bowler) switch
        {
            // Left-arm orthodox: no doosra or carrom ball — the arm ball, the quicker one, the flighted one.
            DeliveryKind.OffSpin when LeftArm(bowler) => new[] { Variation.Stock, Variation.ArmBall, Variation.TopSpinner, Variation.Slower },
            DeliveryKind.OffSpin => new[] { Variation.Stock, Variation.ArmBall, Variation.Carrom, Variation.Wrong },
            DeliveryKind.LegSpin => new[] { Variation.Stock, Variation.Wrong, Variation.TopSpinner, Variation.Flipper },
            // Types only: a yorker or a bouncer is a length (the marker), not a ball type.
            _ => new[] { Variation.Stock, Variation.Inswinger, Variation.Outswinger, Variation.Slower, Variation.OffCutter },
        };

        /// <summary>"Googly" from a wrist spinner, "Doosra" from a finger spinner, and so on.</summary>
        public static string VariationName(Player bowler, Variation v) => v switch
        {
            // The stock turn by its name (left-arm finger spin turns away from a right-hander: "Stock", not an off-break).
            Variation.Stock => KindFor(bowler) switch { DeliveryKind.OffSpin => LeftArm(bowler) ? "Stock" : "Off-break", DeliveryKind.LegSpin => "Leg-break", _ => "Stock" },
            Variation.Wrong => KindFor(bowler) == DeliveryKind.LegSpin ? "Googly" : "Doosra",
            Variation.Slower => bowler.BowlType == BowlType.Spin ? "Flighted" : "Slower",
            Variation.TopSpinner when KindFor(bowler) == DeliveryKind.OffSpin => "Quicker",
            Variation.OffCutter => "Cutter",
            Variation.Inswinger => "Inswing",
            Variation.Outswinger => "Outswing",
            Variation.LegCutter => "Leg-cutter",
            Variation.ArmBall => "Arm ball",
            Variation.TopSpinner => "Top-spin",
            Variation.Carrom => "Carrom ball",
            _ => v.ToString(),
        };

        /// <summary>
        /// Release point across (batter-frame x). In the bowler's frame (+ = his
        /// left, a right-hander's off side) right-arm over the wicket lets go just
        /// left of the stumps and round the wicket well out to the right; left-arm
        /// is the mirror. A left-handed batter's frame is mirrored again.
        /// </summary>
        public static double ReleaseX(bool spin, bool leftArm, bool around, bool leftHandedBatter)
        {
            var x = around ? -(spin ? 0.5 : 0.65) : (spin ? 0.3 : 0.45);
            if (leftArm) x = -x;
            return leftHandedBatter ? -x : x;
        }

        /// <summary>
        /// Which way the stock ball turns, in the batter's frame (+ towards his off
        /// side): an off-break (right-arm finger spin) comes into a right-hander,
        /// a leg-break and left-arm orthodox go away from him, left-arm wrist spin
        /// comes in. Everything reverses for a left-hander.
        /// </summary>
        public static double TurnSign(DeliveryKind kind, bool leftArm, bool leftHandedBatter)
        {
            var s = kind == DeliveryKind.OffSpin ? -1.0 : 1.0;
            if (leftArm) s = -s;
            return leftHandedBatter ? -s : s;
        }

        /// <summary>How often he goes round the wicket: more to left-handers, and off-spinners to them most.</summary>
        public static double AroundOdds(DeliveryKind kind, bool leftArm, bool leftHandedBatter)
        {
            // Bowling "into" the batter's pads from wide of the crease: right-arm to a
            // left-hander, left-arm to a right-hander.
            var angleIn = leftArm != leftHandedBatter;
            if (kind == DeliveryKind.Pace) return angleIn ? 0.3 : 0.06;
            return angleIn ? 0.5 : 0.1;
        }

        // --------------------------------------------------------------- the AI

        /// <param name="phase">0 powerplay, 1 middle, 2 death.</param>
        /// <param name="leftHandedBatter">The striker's hand (the release side and turn are worked out for him).</param>
        public static Delivery Bowl(Player bowler, int phase, Rng rng, bool leftHandedBatter = false)
        {
            var kind = KindFor(bowler);
            var spin = kind != DeliveryKind.Pace;
            var skill = Math.Max(30, bowler.Bowling);
            var leftArm = LeftArm(bowler);
            var around = rng.Chance(AroundOdds(kind, leftArm, leftHandedBatter));

            // What he means to bowl.
            double[] lengthOdds = spin
                ? new[] { 0.04, 0.30, 0.52, 0.14, 0.0 }
                : phase switch
                {
                    0 => new[] { 0.05, 0.27, 0.45, 0.15, 0.08 },
                    1 => new[] { 0.06, 0.22, 0.48, 0.18, 0.06 },
                    _ => new[] { 0.38, 0.24, 0.16, 0.12, 0.10 },
                };
            var intendedLength = (Length)rng.PickWeighted(lengthOdds);
            var intendedLine = (Line)(1 + rng.PickWeighted(new[] { 0.25, 0.45, 0.2, 0.1 }));
            var variation = PickVariation(kind, intendedLength, phase, rng, leftArm);

            // How it comes out: error shrinks with skill.
            var miss = 1 - (skill - 30) / 80.0;
            var (near, far) = Range(intendedLength, spin);
            var bounceZ = rng.Range(near, far) + Normal(rng) * 1.3 * miss;
            bounceZ = DartMath.Clamp(bounceZ, 0.3, 13.5);
            var atBatter = LineX(intendedLine) + Normal(rng) * (0.06 + 0.32 * miss);
            // Owner v26 #18 ("extras are missing"): the odd one gets away from him — down the leg side,
            // or a wide yorker / slower ball past the off-side guideline. About 3 wides an innings in a
            // T20 (a real side's 4–6 counts the ones that are chased), more from a wayward bowler.
            if (rng.Chance(0.012 + 0.03 * miss)) atBatter = rng.Chance(0.55) ? -rng.Range(0.52, 0.8) : rng.Range(0.97, 1.25);

            var d = new Delivery
            {
                Kind = kind,
                SpeedKmh = StockSpeed(bowler, rng) + VariationPace(variation, rng, spin) + LengthPace(intendedLength, spin),
                ReleaseX = ReleaseX(spin, leftArm, around, leftHandedBatter),
                ReleaseY = spin ? 1.95 : 2.15,
                ReleaseZ = 19.0,
                BounceZ = bounceZ,
                LeftArm = leftArm,
                AroundWicket = around,
                Variation = variation,
            };
            Move(d, phase, skill, leftHandedBatter, rng);
            // A swinging stock ball is an in/outswinger as far as anyone watching can tell.
            if (!spin && variation == Variation.Stock && Math.Abs(d.Swing) > 0.12) d.Variation = d.Swing > 0 ? Variation.Outswinger : Variation.Inswinger;
            // Pick the bounce point so the ball reaches the batter (0.9 m out) where he
            // aimed, allowing for its line carrying on after pitching and for the turn.
            var d1 = d.ReleaseZ - bounceZ;
            var t2 = Math.Max(0, bounceZ - 0.9);
            var turnShare = Math.Min(1, t2 / Math.Max(0.5, bounceZ));
            d.BounceX = (atBatter - d.Deviation * turnShare + d.ReleaseX * t2 / d1) / (1 + t2 / d1);
            Finish(d, skill, 0, rng);
            return d;
        }


        /// <summary>km/h on top of (or off) his stock pace.</summary>
        private static double VariationPace(Variation v, Rng rng, bool spin = false) => v switch
        {
            Variation.Bouncer => 3,
            Variation.Yorker => -2,
            // A spinner's slower ball is the flighted one: a few km/h, not a pacer's 20.
            Variation.Slower => spin ? -rng.Range(6, 10) : -rng.Range(17, 25),
            Variation.TopSpinner when spin => rng.Range(5, 8),
            Variation.OffCutter or Variation.LegCutter => -rng.Range(6, 11),
            Variation.ArmBall => 5,
            Variation.Flipper => 6,
            Variation.TopSpinner => 2,
            Variation.Carrom => 3,
            _ => 0,
        };

        private static Variation PickVariation(DeliveryKind kind, Length intended, int phase, Rng rng, bool leftArm = false)
        {
            var roll = rng.NextDouble();
            switch (kind)
            {
                case DeliveryKind.Pace:
                    var slower = phase == 2 ? 0.16 : 0.06;
                    if (roll < slower) return Variation.Slower;
                    if (roll < slower + 0.06) return roll < slower + 0.03 ? Variation.OffCutter : Variation.LegCutter;
                    return Variation.Stock;
                case DeliveryKind.LegSpin:
                    return roll < 0.12 ? Variation.Wrong : roll < 0.17 ? Variation.Flipper : roll < 0.23 ? Variation.TopSpinner : Variation.Stock;
                case DeliveryKind.OffSpin when leftArm:
                    return roll < 0.1 ? Variation.ArmBall : roll < 0.16 ? Variation.TopSpinner : roll < 0.24 ? Variation.Slower : Variation.Stock;
                default:
                    return roll < 0.1 ? Variation.ArmBall : roll < 0.14 ? Variation.Wrong : roll < 0.18 ? Variation.Carrom : Variation.Stock;
            }
        }

        /// <summary>
        /// Movement: swing early in the innings (or the swing he asked for), turn
        /// for spinners (the way his arm and wrist send it, the wrong'un the
        /// other way), a little seam for pace, the cutters' grip.
        /// </summary>
        private static void Move(Delivery d, int phase, int skill, bool leftHandedBatter, Rng rng)
        {
            var s = 0.6 + 0.6 * (skill - 40) / 55.0;
            if (d.IsSpin)
            {
                var sign = TurnSign(d.Kind, d.LeftArm, leftHandedBatter);
                var turn = rng.Range(0.12, 0.45) * (0.7 + 0.6 * (skill - 40) / 55.0);
                switch (d.Variation)
                {
                    case Variation.Wrong: d.Deviation = -sign * turn * 0.8; break;
                    case Variation.Carrom: d.Deviation = -sign * turn * 0.55; break;
                    case Variation.ArmBall:
                        // Holds its line and drifts on with the arm.
                        d.Deviation = sign * 0.02;
                        d.Swing = -sign * rng.Range(0.04, 0.1);
                        break;
                    case Variation.Flipper:
                        d.Deviation = sign * 0.04;
                        d.BounceFactor = 0.6;
                        break;
                    case Variation.Slower:
                        // Flighted: slower through the air, dips and turns a touch more.
                        d.Deviation = sign * turn * 1.15;
                        break;
                    case Variation.TopSpinner:
                        d.Deviation = sign * 0.05;
                        d.BounceFactor = 1.25;
                        break;
                    default: d.Deviation = sign * turn; break;
                }
                return;
            }
            var swingAmount = phase == 0 ? 0.28 : 0.12;
            switch (d.Variation)
            {
                case Variation.Inswinger: d.Swing = -rng.Range(0.6, 1.1) * 0.28 * s; break;
                case Variation.Outswinger: d.Swing = rng.Range(0.6, 1.1) * 0.28 * s; break;
                default: d.Swing = rng.Range(-1, 1) * swingAmount * s; break;
            }
            // Cutters grip and move off the seam like a slow off- or leg-break from his hand.
            var cut = d.Variation == Variation.OffCutter ? DeliveryKind.OffSpin : d.Variation == Variation.LegCutter ? DeliveryKind.LegSpin : (DeliveryKind?)null;
            d.Deviation = cut is DeliveryKind k
                ? TurnSign(k, d.LeftArm, leftHandedBatter) * rng.Range(0.06, 0.14) * s
                : rng.Range(-0.06, 0.06);
        }

        /// <summary>
        /// Length, line and the umpire's calls: wide, and the no-balls — the
        /// odd overstep (more from quick and wayward bowlers, and from a
        /// strained, late release), and now and then a full toss that slips out
        /// above the waist (a beamer).
        /// </summary>
        private static void Finish(Delivery d, int skill, double lateRelease, Rng rng)
        {
            var spin = d.IsSpin;
            var skill01 = DartMath.Clamp((skill - 30) / 65.0, 0, 1);
            var overstep = spin ? 0.001 + 0.002 * (1 - skill01) : 0.003 + 0.008 * (1 - skill01) + Math.Max(0, d.SpeedKmh - 140) * 0.0003;
            overstep *= 1 + 3 * Math.Max(0, lateRelease - 1);
            var beamer = spin ? 0.0004 : d.BounceZ < 1.6 || d.Variation == Variation.Slower ? 0.004 : 0.0008;
            if (lateRelease > 1.5) beamer += 0.03;
            if (rng.Chance(beamer))
            {
                // It slips: a full toss arriving between the waist and the chest.
                d.Beamer = true;
                d.BounceZ = FullTossBounce(d, rng.Range(1.05, 1.45));
                d.BounceX = (d.BounceX + d.ReleaseX) / 2;
                d.Deviation = 0;
            }
            // A pace bouncer climbs (it's banged in): the short ones get up towards the head.
            if (!spin && !d.FullToss && LengthOf(d.BounceZ, false) == Length.Bouncer) d.BounceFactor *= BouncerLift;
            var trajectory = new Trajectory(d);
            var arrive = trajectory.At(trajectory.TimeAt(0.9));
            d.Length = LengthOf(d.BounceZ, spin);
            d.Line = LineOf(arrive.x);
            // Over the batter's head as he stands at the crease: no-ball (and a free hit).
            d.HeadHigh = !spin && !d.FullToss && trajectory.At(trajectory.TimeAt(0.3)).y > HeadHeight;
            d.NoBall = d.Beamer || d.HeadHigh || rng.Chance(overstep);
            // A no-ball is called first: it isn't a wide as well.
            d.Wide = !d.NoBall && (arrive.x > 0.89 || arrive.x < -0.45);
        }

        /// <summary>Where a full toss's parabola "lands" (behind the stumps) for it to reach the batter at <paramref name="height"/> m.</summary>
        public static double FullTossBounce(Delivery d, double height)
        {
            double lo = -60, hi = 0.3;
            for (var i = 0; i < 40; i++)
            {
                var mid = (lo + hi) / 2;
                var probe = new Delivery { Kind = d.Kind, SpeedKmh = d.SpeedKmh, ReleaseX = d.ReleaseX, ReleaseY = d.ReleaseY, ReleaseZ = d.ReleaseZ, BounceX = d.BounceX, BounceZ = mid };
                var t = new Trajectory(probe);
                if (t.At(t.TimeAt(1.0)).y > height) lo = mid;
                else hi = mid;
            }
            return (lo + hi) / 2;
        }

        // ------------------------------------------------------------- the user

        /// <summary>
        /// The user's ball: <paramref name="variation"/> from <paramref name="bowler"/>,
        /// aimed to pitch at (<paramref name="pitchX"/>, <paramref name="pitchZ"/>)
        /// (batter frame: across, + off side; metres up the pitch from his stumps).
        /// <paramref name="release"/> is the meter's error: 0 a perfect release, ±1
        /// the edge of the green, beyond that a bad one (− early: it goes fuller and
        /// floats; + late: dragged down shorter, and a strained stride oversteps).
        /// The bowler's skill sets the scatter round the mark; the release widens it
        /// and takes pace off.
        /// </summary>
        public static Delivery Deliver(Player bowler, Variation variation, double pitchX, double pitchZ, double release, int phase, Rng rng,
            bool leftHandedBatter = false, bool around = false)
        {
            var kind = KindFor(bowler);
            var spin = kind != DeliveryKind.Pace;
            var skill = Math.Max(30, bowler.Bowling);
            if (!Variations(bowler).Contains(variation)) variation = Variation.Stock;
            var miss = 1 - (skill - 30) / 80.0;
            var e = DartMath.Clamp(release, -3, 3);
            // A perfect release lands it close to the mark; off the green it sprays.
            var spread = Math.Abs(e) <= PerfectRelease ? 0.6 : 1 + 1.6 * (Math.Abs(e) - PerfectRelease);
            var bounceZ = pitchZ + e * 0.9 + Normal(rng) * (0.25 + 0.7 * miss) * spread;
            // A missed yorker is a low full toss; nothing lands past the bowler's half.
            bounceZ = DartMath.Clamp(bounceZ, -2.0, 13.5);
            var bounceX = pitchX + Normal(rng) * (0.03 + 0.1 * miss) * spread;
            var speed = StockSpeed(bowler, rng) + VariationPace(variation, rng, spin) + LengthPace(LengthOf(pitchZ, spin), spin);
            speed *= 1 - 0.03 * Math.Min(2, Math.Max(0, Math.Abs(e) - 0.35));
            var d = new Delivery
            {
                Kind = kind,
                SpeedKmh = speed,
                ReleaseX = ReleaseX(spin, LeftArm(bowler), around, leftHandedBatter),
                ReleaseY = spin ? 1.95 : 2.15,
                ReleaseZ = 19.0,
                BounceX = bounceX,
                BounceZ = bounceZ,
                LeftArm = LeftArm(bowler),
                AroundWicket = around,
                Variation = variation,
                Release = ReleaseGrade(e),
            };
            Move(d, phase, skill, leftHandedBatter, rng);
            // Off the green, the wrist and fingers aren't right: swing and turn fall away.
            var keep = MovementKept(e);
            d.Swing *= keep;
            d.Deviation *= keep;
            Finish(d, skill, Math.Abs(e) > 1 && e > 0 ? e : 0, rng);
            // Tapped in the red: he's overstepped — a no-ball, always (the free hit follows).
            if (ReleaseNoBall(e))
            {
                d.NoBall = true;
                d.Wide = false;
            }
            return d;
        }

        /// <summary>The marker's default spot for a variation (batter frame): stock on a good length outside off.</summary>
        public static (double x, double z) DefaultMark(Player bowler, Variation v)
        {
            var spin = bowler.BowlType == BowlType.Spin;
            return v switch
            {
                Variation.Yorker => (0.05, 0.9),
                Variation.Bouncer => (0.15, 10.5),
                Variation.Slower => (0.15, 4.5),
                _ => spin ? (0.15, 4.2) : (0.2, 5.8),
            };
        }

        /// <summary>Standard normal from two uniforms (Box–Muller), on the shared RNG.</summary>
        public static double Normal(Rng rng)
        {
            var u1 = Math.Max(1e-9, rng.NextDouble());
            var u2 = rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        /// <summary>A hash that's the same on every run and platform (string.GetHashCode isn't).</summary>
        internal static int Stable(string s)
        {
            var h = 7;
            foreach (var ch in s) h = (h * 31 + ch) & 0x7fffffff;
            return h;
        }

        /// <summary>
        /// Who bowls each of <paramref name="overs"/> overs: the season planner's
        /// shape (pace up front and at the death, spin in the middle), at most
        /// overs / 5 each (rounded up), never two in a row.
        /// </summary>
        public static List<Player> PlanOvers(IReadOnlyList<Player> xi, int overs)
        {
            if (overs >= 20) return Cricket.PlanOvers(xi);
            var options = xi.Where(p => p.IsBowlingOption).OrderByDescending(p => p.Bowling).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
            if (options.Count < 2) options = xi.OrderByDescending(p => p.Bowling).Take(5).ToList();
            var max = MaxOvers(overs);
            var used = options.ToDictionary(p => p.Id, _ => 0);
            var plan = new List<Player>();
            for (var over = 0; over < overs; over++)
            {
                var phase = Cricket.PhaseOf(over * 20 / overs);
                Player? last = plan.Count > 0 ? plan[plan.Count - 1] : null;
                var pick = options
                    .Where(p => used[p.Id] < max && p != last)
                    .OrderByDescending(p => p.Bowling + Cricket.PhaseBonus(p, phase))
                    .ThenBy(p => p.Id, StringComparer.Ordinal)
                    .FirstOrDefault() ?? options.First(p => p != last);
                used[pick.Id]++;
                plan.Add(pick);
            }
            return plan;
        }

        /// <summary>Overs one bowler may bowl: a fifth of the innings, rounded up.</summary>
        public static int MaxOvers(int overs) => Math.Max(1, (int)Math.Ceiling(overs / 5.0));
    }
}
