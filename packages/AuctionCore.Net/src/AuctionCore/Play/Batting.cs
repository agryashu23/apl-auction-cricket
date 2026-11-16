using System;

namespace Apl.Core.Play
{
    /// <summary>
    /// Push: soft hands, guiding the ball into a gap for a run or two (low risk,
    /// rarely a boundary). Stroke: a full-blooded shot along the ground — timed,
    /// it beats the ring and runs to the rope. Loft: in the air, over the top.
    /// </summary>
    public enum ShotType { Push, Stroke, Loft }

    public enum Timing { Leave, Miss, VeryEarly, Early, Good, Perfect, Late, VeryLate }

    /// <summary>What the batter did: a shot type, a direction, and when he tapped (s after release).</summary>
    public sealed class ShotInput
    {
        public static readonly ShotInput Leave = new ShotInput(ShotType.Push, Zone.StraightOff, double.NaN);

        public ShotInput(ShotType type, Zone direction, double tapTime, bool? backFoot = null, bool special = false)
        {
            Type = type;
            Direction = direction;
            TapTime = tapTime;
            BackFoot = backFoot;
            Special = special;
        }

        public ShotType Type { get; }
        public Zone Direction { get; }
        public double TapTime { get; }

        /// <summary>The foot he chose (null: whichever suits the length), and a trick shot (scoop, ramp, switch hit).</summary>
        public bool? BackFoot { get; }
        public bool Special { get; }
        public bool IsLeave => double.IsNaN(TapTime);

        /// <summary>
        /// The exact line he's playing along (degrees, + off side), when the view knows it
        /// better than the zone — the authored direction of the shot it's going to show — so
        /// a timed ball leaves along the bat's swing. Null: the zone's own angle.
        /// </summary>
        public double? AimDeg { get; set; }

        /// <summary>
        /// Footwork before the ball: down the track (he meets it <see cref="Batting.ChargeDistance"/>
        /// further up, a length fuller — and out of his ground if he misses), and across: +1
        /// to the off side (the ball's then a line further to leg), −1 away to leg (room through the off side).
        /// </summary>
        public bool Charged { get; set; }
        public int Across { get; set; }
    }

    /// <summary>Everything about one ball, for the score and for the view to replay.</summary>
    public sealed partial class BallResult
    {
        public Delivery Delivery = null!;
        public Trajectory Trajectory = null!;
        public ShotInput Shot = null!;
        public Timing Timing;

        /// <summary>Tap error in seconds (+ late), and the window it was judged against.</summary>
        public double TimingError, Window;

        /// <summary>0–1: how well it came off the bat.</summary>
        public double Quality;

        public int Runs;
        public bool Wide, Wicket, Four, Six, Edge, Caught;

        /// <summary>"bowled", "lbw", "caught", "caught behind" — null if not out.</summary>
        public string? HowOut;
        public string Fielder = "";

        /// <summary>Off the bat: direction (deg, 0 = straight, + off side), speed (m/s), launch (deg, 0 = along the ground).</summary>
        public double ExitAngle, ExitSpeed, Launch;

        /// <summary>Where the ball ended up (batter-centred x, forward) and when (s after contact).</summary>
        public (double x, double f) End;
        public double EndTime;

        /// <summary>The ball's flight and roll after the hit (for the view); who dropped it; a fumble gave a run.</summary>
        public Field.BallPath Path;
        public string Dropped = "";
        public bool Fumbled;
    }

    /// <summary>
    /// The batting side of a ball: whether the tap was on time, how well the shot
    /// suited the delivery, and what came of it in the field.
    /// </summary>
    public static partial class Batting
    {
        /// <summary>Seconds from the tap to the bat meeting the ball (the swing).</summary>
        public const double SwingTime = 0.16;

        /// <summary>Where bat meets ball: forward to full balls, back to short ones.</summary>
        public static double ContactZ(Length l) => l switch
        {
            Length.Yorker => 1.25,
            Length.Full => 1.75,
            Length.Good => 1.4,
            _ => 0.75,
        };

        /// <summary>The moment to tap for a perfect contact.</summary>
        public static double IdealTap(Trajectory t) => t.TimeAt(ContactZ(t.Delivery.Length)) - SwingTime;

        /// <summary>The moment to tap when he's charged (he meets it <see cref="ChargeDistance"/> further up the pitch, sooner).</summary>
        public static double IdealTap(Trajectory t, bool charged) => charged ? t.TimeAt(ContactZ(t.Delivery.Length) + ChargeDistance) - SwingTime : IdealTap(t);

        /// <summary>
        /// Half-width (s) of the "good" timing window: wider for better batters,
        /// narrower against real pace and big turn.
        /// </summary>
        /// <summary>An average batter's "good" half-window (s): ±80 ms, perfect inside ±28 ms.</summary>
        public const double WindowBase = 0.08;

        /// <summary>
        /// How far (deg, at quality 1) a Stroke is steered away from the nearest ring
        /// fielder, so a timed drive beats the ring for four most of the time (a Push
        /// places it with soft hands, 14°).
        /// </summary>
        public const double StrokeSteer = 9;

        /// <summary>Driving at a moving ball finds the edge more often than a push or a swing over it (the risk a timed Stroke carries).</summary>
        public const double StrokeBeaten = 2.0;

        /// <summary>A timed push that's cut off inside the ring still brings a quick single this often (soft hands, they set off at once).</summary>
        public const double PushSingle = 0.7;

        /// <summary>A mistimed Stroke (quality under 0.45: early or late) balloons in the air this often — the risk in driving off the wrong timing.</summary>
        public const double StrokeBalloon = 0.4;

        public static double Window(Player batter, Delivery d)
        {
            var skill = DartMath.Clamp(0.65 + 0.7 * (batter.Batting - 40) / 55.0, 0.55, 1.35);
            var pace = d.IsSpin ? 1.0 - Math.Min(0.25, Math.Abs(d.Deviation) * 0.5) : DartMath.Clamp(135 / d.SpeedKmh, 0.85, 1.15);
            return WindowBase * skill * pace;
        }

        public static Timing Grade(double error, double window)
        {
            var e = Math.Abs(error) / window;
            if (e <= 0.35) return Timing.Perfect;
            if (e <= 1.0) return Timing.Good;
            if (e <= 2.0) return error < 0 ? Timing.Early : Timing.Late;
            if (e <= 3.5) return error < 0 ? Timing.VeryEarly : Timing.VeryLate;
            return Timing.Miss;
        }

        /// <summary>Extra exit speed off the middle (m/s): a PERFECT stroke beats the ring and runs to the rope, a GOOD one often.</summary>
        public const double PerfectBonus = 4.5, GoodBonus = 2;

        private static double TimedBonus(Timing t) => t == Timing.Perfect ? PerfectBonus : t == Timing.Good ? GoodBonus : 0;

        private static double TimingScore(Timing t) => t switch
        {
            Timing.Perfect => 1.0,
            Timing.Good => 0.75,
            Timing.Early or Timing.Late => 0.4,
            Timing.VeryEarly or Timing.VeryLate => 0.12,
            _ => 0,
        };

        private static readonly double[,] LengthFit =
        {
            // StraightOff, Cover, Point, ThirdMan, FineLeg, Square, MidWicket, StraightOn
            { 0.75, 0.40, 0.20, 0.50, 0.60, 0.30, 0.45, 0.75 }, // Yorker
            { 1.00, 0.95, 0.50, 0.30, 0.50, 0.50, 0.90, 1.00 }, // Full
            { 0.75, 0.85, 0.65, 0.55, 0.60, 0.60, 0.75, 0.65 }, // Good
            { 0.25, 0.50, 1.00, 0.70, 0.75, 1.00, 0.85, 0.25 }, // Short
            { 0.10, 0.25, 0.60, 0.85, 0.90, 0.85, 0.40, 0.10 }, // Bouncer
        };

        private static bool OffSide(Zone z) => z == Zone.StraightOff || z == Zone.Cover || z == Zone.Point || z == Zone.ThirdMan;

        private static double LineFit(Line l, Zone z)
        {
            double[] off = { 1.0, 1.0, 0.9, 0.7, 0.4, 0.2 };
            double[] leg = { 0.2, 0.45, 0.65, 0.85, 1.0, 1.0 };
            return (OffSide(z) ? off : leg)[(int)l];
        }

        /// <summary>0–1: how well a shot in this direction suits the ball.</summary>
        public static double Suitability(Delivery d, ShotType type, Zone z) => Suitability(d.Length, d.Line, d.IsSpin, type, z);

        /// <summary>The same, for the ball as he meets it after charging or moving across (<see cref="EffectiveLength"/>, <see cref="EffectiveLine"/>).</summary>
        public static double Suitability(Delivery d, ShotInput shot, Zone z) => Suitability(EffectiveLength(d, shot), EffectiveLine(d, shot), d.IsSpin, shot.Type, z);

        private static double Suitability(Length length, Line line, bool spin, ShotType type, Zone z)
        {
            var s = LengthFit[(int)length, (int)z] * LineFit(line, z);
            if (type == ShotType.Loft && length == Length.Yorker) s *= 0.45;
            if (type == ShotType.Loft && length == Length.Good && !spin) s *= 0.8;
            // Soft hands go with most balls: a push is placement, not power.
            if (type == ShotType.Push) s = 0.55 + 0.45 * s;
            return s;
        }

        /// <summary>How far down the pitch a charge takes him before the ball arrives (m): he meets it this much earlier.</summary>
        public const double ChargeDistance = 1.6;

        /// <summary>
        /// The length as he meets it: down the track, a good ball is a half-volley (full),
        /// a short one good, a yorker a full toss (full); otherwise as bowled.
        /// </summary>
        public static Length EffectiveLength(Delivery d, ShotInput shot)
        {
            if (!shot.Charged) return d.Length;
            return d.Length switch
            {
                Length.Yorker => Length.Full,
                Length.Full => Length.Full,
                Length.Good => Length.Full,
                Length.Short => Length.Good,
                _ => Length.Short,
            };
        }

        /// <summary>The line as he meets it: across to the off side, the ball's a line further to his legs; away to leg, a line further off (room to free the arms).</summary>
        public static Line EffectiveLine(Delivery d, ShotInput shot) =>
            (Line)DartMath.Clamp((int)d.Line + Math.Sign(shot.Across), (int)Line.WideOff, (int)Line.WideLeg);

        /// <summary>
        /// A pushed ball is steered into the gap: the exit angle moves away from
        /// the nearest ring fielder, up to <paramref name="steer"/> degrees.
        /// </summary>
        private static double FindGap(Field field, double angleDeg, double steer)
        {
            Fielder? nearest = null;
            var nearestDiff = double.MaxValue;
            foreach (var f in field.Fielders)
            {
                if (Field.OutsideRing(f)) continue;
                var diff = ((f.AngleDeg - angleDeg + 540) % 360) - 180;
                if (Math.Abs(diff) >= Math.Abs(nearestDiff)) continue;
                nearestDiff = diff;
                nearest = f;
            }
            if (nearest == null || Math.Abs(nearestDiff) > 25) return angleDeg;
            // Away from him, more the closer he stands to the line.
            var away = nearestDiff >= 0 ? -1 : 1;
            return angleDeg + away * steer * (1 - Math.Abs(nearestDiff) / 25);
        }

        /// <summary>
        /// How far (deg) a shot may go from where it was aimed: timed (good / perfect)
        /// within <see cref="AimTimed"/>; early or late within <see cref="AimOff"/>, and
        /// only to the side the timing sends it (early: leg side, late: off side). The view
        /// plays the shot he asked for, so the ball has to leave in that shot's direction;
        /// only a mistimed (very early / late) or edged ball flies off-line.
        /// </summary>
        public const double AimTimed = 10, AimOff = 25;

        /// <summary>
        /// Where an edge goes: carried to the keeper / slip (pace, spin), played on to the
        /// stumps (when it was hitting them), or ballooned into the ring off a leading / top
        /// edge; the rest runs away fine. Tuned so caught behind is ~12–15% of dismissals,
        /// as in T20, rather than the ~30% it was.
        /// </summary>
        public const double EdgeBehindPace = 0.16, EdgeBehindSpin = 0.08, EdgePlayedOn = 0.18, EdgeSkied = 0.26;

        /// <summary>A loft's exit pace by its timing (carry goes with its square): PERFECT the full carry, GOOD ~4% less (and a touch flatter: <see cref="GoodLoftFlatter"/>).</summary>
        public static double LoftPace(Timing t) => t switch
        {
            Timing.Perfect => 1.0,
            Timing.Good => 0.98,
            _ => 0.92,
        };

        /// <summary>
        /// How much the user's release costs the batter (final rating 2026-11-14: "a perfect release should
        /// cost the batter more"): 1 for a PERFECT release, less for GOOD, below 0 for a loose (early / late)
        /// one, and a variation well released is harder to read still. 0 for the AI's own balls.
        /// </summary>
        public static double ReleaseSting(Delivery d)
        {
            if (d.Release == null) return 0;
            var s = d.Release.Value switch
            {
                Timing.Perfect => 1.0,
                Timing.Good => 0.45,
                Timing.Early or Timing.Late => -0.3,
                _ => -0.5,
            };
            if (s > 0 && d.Variation != Variation.Stock) s += 0.3;
            return s;
        }

        /// <summary>Contact lost to a stinging release (quality × (1 − this × sting)), and the extra chance of being beaten by its movement (× (1 + this × sting)).</summary>
        public const double ReleaseQualityCost = 0.06, ReleaseBeaten = 0.3;

        /// <summary>Degrees flatter a GOOD loft leaves the bat than a PERFECT one of the same quality would.</summary>
        public const double GoodLoftFlatter = 2;

        /// <summary>A mistimed loft (quality under 0.45) goes along the ground this often instead of in the air.</summary>
        public const double MistimedLoftGround = 0.3;

        /// <summary>
        /// Beaten by a straight one, by the tap's grade: GOOD now and then, EARLY / LATE often, VERY off
        /// it more often still (PERFECT almost never). Tuned so bowled + lbw are ~20–25% of T20 dismissals.
        /// </summary>
        public const double StraightBeatenPerfect = 0.002, StraightBeatenGood = 0.011, StraightBeatenOff = 0.035, StraightBeatenVery = 0.07;

        private static double StraightBeaten(Delivery d, ShotInput shot, Timing t, double movement, double matchup, double sting)
        {
            var p = t switch
            {
                Timing.Perfect => StraightBeatenPerfect,
                Timing.Good => StraightBeatenGood,
                Timing.Early or Timing.Late => StraightBeatenOff,
                _ => StraightBeatenVery,
            };
            // Across the line (a loft) is the riskiest; soft hands down the line the safest.
            p *= shot.Type == ShotType.Loft ? 1.4 : shot.Type == ShotType.Push ? 0.6 : 1.0;
            // Full and straight (the yorker above all) beats the bat; a short one is over the stumps or easily seen.
            p *= d.Length switch { Length.Yorker => 1.3, Length.Full => 1.15, Length.Good => 1.0, Length.Short => 0.45, _ => 0.15 };
            if (d.FullToss) p *= 0.3;
            // Down the track he's meeting it on the full: less to beat him with.
            if (shot.Charged) p *= 0.5;
            // Late movement, the better bowler, a good release.
            p *= 1 + DartMath.Clamp(movement / 0.3, 0, 1) * 0.6;
            p *= DartMath.Clamp((1.35 - matchup) / 0.35, 0.4, 1.7);
            p *= Math.Max(0.6, 1 + 0.15 * sting);
            return DartMath.Clamp(p, 0, 0.5);
        }

        private static double KeepToAim(double angleDeg, double aim, Timing t)
        {
            var off = ((angleDeg - aim + 540) % 360) - 180;
            switch (t)
            {
                case Timing.Perfect:
                case Timing.Good:
                    return aim + DartMath.Clamp(off, -AimTimed, AimTimed);
                case Timing.Early:
                    return aim + DartMath.Clamp(off, -AimOff, AimTimed);
                case Timing.Late:
                    return aim + DartMath.Clamp(off, -AimTimed, AimOff);
                default:
                    return angleDeg;
            }
        }

        /// <summary>The ball off the bat (or past it); the extras, the running and the free-hit rules go on top in <c>Resolve</c> (Rules.cs).</summary>
        private static BallResult ResolveCore(Player batter, Player bowler, Delivery d, Trajectory traj, ShotInput shot, Field field, Rng rng)
        {
            var r = new BallResult { Delivery = d, Trajectory = traj, Shot = shot, Window = Window(batter, d) };
            var hitting = traj.HittingStumps();

            if (shot.IsLeave)
            {
                r.Timing = Timing.Leave;
                if (d.Wide)
                {
                    r.Wide = true;
                    r.Runs = 1;
                }
                else Missed(r, batter, d, traj, shot, field, hitting, rng);
                return r;
            }

            r.TimingError = shot.TapTime - IdealTap(traj, shot.Charged);
            r.Timing = Grade(r.TimingError, r.Window);
            var timing = TimingScore(r.Timing);
            // The ball as he meets it: charged, it's fuller; moved across, its line has changed.
            var fit = Suitability(d, shot, shot.Direction);
            var met = EffectiveLength(d, shot);
            // Footwork he chose: back to a full ball, or forward to a short one, cramps the shot.
            if (!shot.Charged && shot.BackFoot is bool back && met != Length.Good && back != (met >= Length.Short)) fit *= 0.5;
            // Trick shots: more can go wrong.
            if (shot.Special) fit *= 0.8;
            var matchup = DartMath.Clamp(1 + (batter.Batting - bowler.Bowling) / 150.0, 0.78, 1.22);
            // The user's release (final rating 2026-11-14, owner-approved): a PERFECT / GOOD one is harder to middle.
            var sting = ReleaseSting(d);
            r.Quality = DartMath.Clamp(timing * (0.45 + 0.55 * fit) * matchup * (1 - ReleaseQualityCost * sting), 0, 1);

            // Swung and missed (or nowhere near it).
            if (r.Timing == Timing.Miss || (r.Quality < 0.12 && rng.Chance(0.6)))
            {
                if (d.Wide)
                {
                    r.Wide = true;
                    r.Runs = 1;
                    // Down the leg side to a spinner, and he's out of his ground: stumped off a wide.
                    Stumping(r, batter, d, shot, field, rng);
                }
                else Missed(r, batter, d, traj, shot, field, hitting, rng);
                return r;
            }

            // Deep in his crease to a short one, he treads on (or clips) his stumps.
            if (HitWicket(d, shot, rng))
            {
                Out(r, "hit wicket");
                return r;
            }

            var movement = d.IsSpin ? Math.Abs(d.Deviation) * 0.6 : Math.Abs(d.Swing) * 0.5 + Math.Abs(d.Deviation) * 2;

            // Beaten by a straight one (final rating 2026-11-14: 7 of 7 natural wickets were caught, "why not
            // LBW, it should be"): against a ball that's hitting, anything short of a PERFECT tap now and then
            // misses it — through the gate (bowled) or onto the pad in line (lbw, the umpire's call on the
            // tracking: Missed / LbwCase.Judge).
            if (hitting && !d.Wide && rng.Chance(StraightBeaten(d, shot, r.Timing, movement, matchup, sting)))
            {
                r.Quality = 0;
                Missed(r, batter, d, traj, shot, field, hitting, rng);
                return r;
            }

            // Movement finds the edge even of a well-timed shot now and then (more for
            // lesser batters, and when it moves a lot late).
            var beaten = DartMath.Clamp(movement / 0.3, 0, 1) * 0.12 * (1.35 - matchup) * (shot.Type == ShotType.Push ? 0.5 : shot.Type == ShotType.Stroke ? StrokeBeaten : 1);
            beaten *= Math.Max(0.4, 1 + ReleaseBeaten * sting);
            if (d.Line <= Line.Off && rng.Chance(beaten)) r.Quality = Math.Min(r.Quality, 0.2);

            // Thin contact: an edge.
            if (r.Quality < (shot.Type == ShotType.Push ? 0.22 : 0.3))
            {
                r.Edge = true;
                var behindChance = d.IsSpin ? EdgeBehindSpin : EdgeBehindPace;
                if (shot.Type == ShotType.Push) behindChance *= 0.6;
                // Top edges off a big swing often fly over the keeper.
                if (shot.Type == ShotType.Loft) behindChance *= 0.75;
                if (rng.Chance(behindChance))
                {
                    r.Caught = true;
                    r.Fielder = rng.Chance(0.7) ? "keeper" : "slip";
                    Out(r, "caught behind");
                    return r;
                }
                // An inside edge onto the stumps: played on.
                if (hitting && rng.Chance(EdgePlayedOn))
                {
                    Out(r, "bowled");
                    return r;
                }
                // A leading edge or a top edge, up in the air into the ring: a mis-hit catch (or it drops safe).
                if (rng.Chance(shot.Type == ShotType.Push ? EdgeSkied * 0.5 : EdgeSkied))
                {
                    r.ExitAngle = OffSide(shot.Direction) ? rng.Range(45, 110) : rng.Range(-150, -60);
                    r.ExitSpeed = rng.Range(12, 19);
                    r.Launch = rng.Range(38, 62);
                    return InField(r, field.Air(r.ExitAngle, r.ExitSpeed, r.Launch, rng));
                }
                r.ExitAngle = OffSide(shot.Direction) ? rng.Range(125, 160) : rng.Range(-160, -125);
                r.ExitSpeed = rng.Range(9, 18);
                return InField(r, field.Ground(r.ExitAngle, r.ExitSpeed, rng));
            }

            // Early swings drag it round to the leg side, late ones push it to the off side
            // (a push, played late and soft, drifts less).
            var drift = DartMath.Clamp(r.TimingError / r.Window, -3, 3) * (shot.Type == ShotType.Push ? 5 : 9);
            var spread = (1 - r.Quality) * (shot.Type == ShotType.Push ? 12 : 22);
            var aim = shot.AimDeg ?? Field.ZoneAngle(shot.Direction);
            r.ExitAngle = KeepToAim(aim + drift + Bowling.Normal(rng) * spread, aim, r.Timing);
            var skill = (batter.Batting - 40) / 55.0;

            switch (shot.Type)
            {
                case ShotType.Push:
                {
                    // Guided into the gap with soft hands: a run or two, and the batters set
                    // off at once — the odd quick single comes even when the ball's cut off.
                    r.ExitAngle = KeepToAim(FindGap(field, r.ExitAngle, 14 * r.Quality), aim, r.Timing);
                    r.ExitSpeed = 8 + 10 * r.Quality * rng.Range(0.85, 1.1);
                    var play = field.Ground(r.ExitAngle, r.ExitSpeed, rng);
                    InField(r, play);
                    if (!r.Wicket && r.Runs == 0 && !play.Boundary && r.Quality > 0.45 && rng.Chance(PushSingle))
                    {
                        var (ex, ef) = r.End;
                        if (Math.Sqrt(ex * ex + ef * ef) > 9) r.Runs = 1;
                    }
                    return r;
                }
                case ShotType.Stroke:
                {
                    // Hit with intent: timed, it goes through the ring and on to the rope.
                    var power = r.Quality * (0.9 + 0.3 * skill) * rng.Range(0.88, 1.08);
                    // Round 12 (owner: PERFECT drives went for 0-2): the middle of the bat on a timed
                    // stroke races away — a timing bonus on top of the quality-scaled pace.
                    r.ExitSpeed = 12 + 26 * power + TimedBonus(r.Timing);
                    // Timed, it's placed: threaded between the ring fielders rather than
                    // drilled at them (without this a perfect drive found mid-off).
                    r.ExitAngle = KeepToAim(FindGap(field, r.ExitAngle, StrokeSteer * r.Quality), aim, r.Timing);
                    // A mistimed drive can still balloon in the air.
                    if (r.Quality < 0.45 && rng.Chance(StrokeBalloon))
                    {
                        r.Launch = rng.Range(18, 35);
                        return InField(r, field.Air(r.ExitAngle, r.ExitSpeed * 0.8, r.Launch, rng));
                    }
                    return InField(r, field.Ground(r.ExitAngle, r.ExitSpeed, rng));
                }
                default:
                {
                    var power = Math.Pow(r.Quality, 1.25) * (0.85 + 0.3 * skill) * rng.Range(0.76, 1.02);
                    // Final rating 2026-11-14 (six-heavy, owner: "approx same sixes and fours"): only a PERFECT loft
                    // reliably clears the rope; a GOOD one falls shorter (a catch in the deep, or a bounce for four).
                    r.ExitSpeed = (13 + 27 * power) * LoftPace(r.Timing);
                    // Sweet spot: flat and far. Mishit: skied.
                    r.Launch = 27 + (1 - r.Quality) * 28 + rng.Range(-4, 4);
                    // A GOOD (not PERFECT) loft comes off flatter: short of the rope it's more often one bounce
                    // for four than a skier into the deep fielder's hands.
                    if (r.Timing == Timing.Good) r.Launch -= GoodLoftFlatter;
                    // A mistimed slog (early / late) is often dragged or bottom-edged along the ground rather than
                    // skied (final rating 2026-11-14: keeps the wicket rate at T20 par now that straight balls bowl
                    // and trap batters lbw — the mix shifts from catches, it doesn't just add wickets).
                    if (r.Quality < 0.45 && rng.Chance(MistimedLoftGround))
                    {
                        r.Launch = rng.Range(2, 8);
                        return InField(r, field.Ground(r.ExitAngle, r.ExitSpeed * 0.75, rng));
                    }
                    return InField(r, field.Air(r.ExitAngle, r.ExitSpeed, r.Launch, rng));
                }
            }
        }

        private static BallResult InField(BallResult r, Field.Play p)
        {
            r.End = p.End;
            r.EndTime = p.Time;
            r.Fielder = p.Fielder;
            r.Path = p.Path;
            r.Dropped = p.Dropped;
            r.Fumbled = p.Fumbled;
            r.FieldPlay = p;
            if (p.Caught)
            {
                r.Caught = true;
                Out(r, "caught");
                return r;
            }
            r.Runs = p.Runs;
            r.Four = p.Boundary && p.Runs == 4;
            r.Six = p.Boundary && p.Runs == 6;
            return r;
        }

        private static void Out(BallResult r, string how)
        {
            r.Wicket = true;
            r.HowOut = how;
            r.Runs = 0;
        }
    }
}
