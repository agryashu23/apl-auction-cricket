using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    /// <summary>
    /// Where the eight scoring areas are, as angles from straight back past the
    /// bowler (0°), positive towards the batter's off side.
    /// </summary>
    public enum Zone { StraightOff, Cover, Point, ThirdMan, FineLeg, Square, MidWicket, StraightOn }

    public sealed class Fielder
    {
        public Fielder(string name, double angleDeg, double radius)
        {
            Name = name;
            AngleDeg = angleDeg;
            Radius = radius;
        }

        public string Name { get; }
        public double AngleDeg { get; }
        public double Radius { get; }

        /// <summary>Batter-centred ground position: x across (+ off side), f forward (towards the bowler).</summary>
        public (double x, double f) Position => (Radius * Math.Sin(AngleDeg * Math.PI / 180), Radius * Math.Cos(AngleDeg * Math.PI / 180));
    }

    /// <summary>
    /// The fielding side's nine fielders (plus bowler and keeper), and what
    /// happens to a ball hit into them: cut off for runs, a boundary, or a catch.
    /// </summary>
    public sealed partial class Field
    {
        public const double InnerRing = 27.4;
        private const double FielderSpeed = 7.0, Reaction = 0.3, Reach = 1.6, Pickup = 0.55, ThrowSpeed = 28, RunTime = 3.3;

        /// <summary>Past this distance (m) from the pitch a fielder's gather-and-turn costs up to <see cref="DeepTurn"/> s more before the throw.</summary>
        private const double DeepFrom = 30, DeepTurn = 0.9;

        /// <summary>Under a high ball fielders read it late and run less far (eyes on the ball).</summary>
        private const double CatchSpeed = 5.0, CatchReaction = 0.7;

        public IReadOnlyList<Fielder> Fielders { get; }

        /// <summary>The ground (null: the standard oval) and which way the batter stands.</summary>
        public Venue? Venue { get; }
        public bool LeftHanded { get; }

        private Field(IReadOnlyList<Fielder> fielders, Venue? venue, bool leftHanded)
        {
            Venue = venue;
            LeftHanded = leftHanded;
            // Deep fielders keep the same gap to this ground's rope as on the standard oval.
            Fielders = venue == null
                ? fielders
                : fielders.Select(f => !OutsideRing(f) ? f
                    : new Fielder(f.Name, f.AngleDeg, Math.Max(30, venue.FromBatter(f.AngleDeg, leftHanded) - (Boundary(f.AngleDeg) - f.Radius)))).ToList();
        }

        /// <summary>The pitch: the striker's stumps (f 0) to the bowler's (f 20.12).</summary>
        public const double PitchLength = 20.12;

        /// <summary>
        /// Where a ring fielder stands (field rework, owner: "fielders very close to the pitch"): this far from the
        /// pitch, a couple of steps inside the 30-yard line, so mid-off and mid-on are out on the circle at the
        /// bowler's end, not beside his stumps.
        /// </summary>
        public const double RingGap = 23;

        /// <summary>
        /// Metres from the pitch (the line between the two sets of stumps) of a man at this angle and distance
        /// from the batter. The 30-yard ring is the set of points <see cref="InnerRing"/> from it: a half circle
        /// round each set of stumps joined by straight lines.
        /// </summary>
        public static double FromPitch(double angleDeg, double radius)
        {
            var x = radius * Math.Sin(angleDeg * Math.PI / 180);
            var f = radius * Math.Cos(angleDeg * Math.PI / 180);
            var along = f < 0 ? f : f > PitchLength ? f - PitchLength : 0;
            return Math.Sqrt(x * x + along * along);
        }

        /// <summary>Outside the 30-yard ring (the fielding restrictions count these).</summary>
        public static bool OutsideRing(Fielder f) => FromPitch(f.AngleDeg, f.Radius) > InnerRing + 0.01;

        /// <summary>The distance from the batter, along <paramref name="angleDeg"/>, of the spot <paramref name="gap"/> m from the pitch.</summary>
        public static double RingRadius(double angleDeg, double gap = RingGap)
        {
            double lo = 0, hi = 120;
            for (var i = 0; i < 50; i++)
            {
                var mid = (lo + hi) / 2;
                if (FromPitch(angleDeg, mid) < gap) lo = mid;
                else hi = mid;
            }
            return Math.Round((lo + hi) / 2, 1);
        }

        /// <summary>
        /// How far a throw from (x, f) (batter frame, m) has to go: to the keeper at the striker's stumps, or
        /// (<paramref name="bowlersEnd"/>) to the bowler's stumps 20.12 m up the pitch.
        /// </summary>
        public static double ThrowDistance(double x, double f, bool bowlersEnd) =>
            Math.Max(1, Math.Sqrt(x * x + (bowlersEnd ? (f - PitchLength) * (f - PitchLength) : f * f)));

        /// <summary>A fielder's throw from his own spot to that end.</summary>
        public static double ThrowDistance(Fielder fielder, bool bowlersEnd) => ThrowDistance(fielder.Position.x, fielder.Position.f, bowlersEnd);

        /// <summary>A ring fielder: on the circle, <paramref name="gap"/> m from the pitch.</summary>
        public static Fielder Ring(string name, double angleDeg, double gap = RingGap) => new Fielder(name, angleDeg, RingRadius(angleDeg, gap));

        public static double ZoneAngle(Zone z) => z switch
        {
            Zone.StraightOff => 12,
            Zone.Cover => 50,
            Zone.Point => 92,
            Zone.ThirdMan => 138,
            Zone.FineLeg => -140,
            Zone.Square => -92,
            Zone.MidWicket => -52,
            _ => -12,
        };

        /// <summary>Distance to the rope: longest straight, shortest behind square.</summary>
        public static double Boundary(double angleDeg) => 66 + 9 * Math.Cos(angleDeg * Math.PI / 180);

        /// <summary>Distance to the rope from the batter along a shot angle, on this ground.</summary>
        public double Rope(double angleDeg) => Venue?.FromBatter(angleDeg, LeftHanded) ?? Boundary(angleDeg);

        private bool Over(double x, double f) =>
            Venue?.OverFromBatter(x, f, LeftHanded) ?? Math.Sqrt(x * x + f * f) >= Boundary(Math.Atan2(x, f) * 180 / Math.PI);

        /// <summary>
        /// The field for a ball: a named <paramref name="preset"/> (the user's pick when he bowls, the AI captain's when
        /// he bats), or Auto — the researched field for this kind of bowler and the phase (<see cref="PlanFor"/>).
        /// A left-hander's field is the mirror image (<see cref="LeftHanded"/>). The close catchers stand by the keeper:
        /// level with him when he's back, beside him when he's up (<paramref name="keeperUp"/>, default: up to spin).
        /// </summary>
        public static Field For(int phase, bool spin, Venue? venue = null, bool leftHanded = false, FieldPreset preset = FieldPreset.Auto,
            bool? keeperUp = null, FieldBowler? bowler = null)
        {
            var up = keeperUp ?? spin;
            var plan = preset != FieldPreset.Auto ? preset : PlanFor(bowler ?? (spin ? FieldBowler.OffSpin : FieldBowler.Pace), phase);
            return new Field(Preset(plan, phase, spin, up), venue, leftHanded) { KeeperUp = up, Plan = plan };
        }

        /// <summary>The named field this is (Auto resolved to the captain's plan it stands for).</summary>
        public FieldPreset Plan { get; private set; }

        /// <summary>
        /// The ball's way after the hit exactly as the engine played it, so the view
        /// can fly it there: in the air along the shot's line at carry / hang m/s under
        /// a parabola that comes down at the hang time, then along the ground from
        /// where it landed (or from the bat) slowing at <see cref="Decel"/> until it stops.
        /// </summary>
        public readonly struct BallPath
        {
            public BallPath(double angleDeg, double hang, double carry, (double x, double f) rollFrom, double rollSpeed, double rollAt, double decel = Outfield.Standard, double skim = 0,
                double deflectAt = 0, double deflectSpeed = 0)
            {
                Skim = skim;
                DeflectAt = deflectAt;
                DeflectSpeed = deflectSpeed;
                AngleDeg = angleDeg;
                Hang = hang;
                Carry = carry;
                RollFrom = rollFrom;
                RollSpeed = rollSpeed;
                RollAt = rollAt;
                Decel = decel > 0 ? decel : Outfield.Standard;
            }

            /// <summary>How fast the outfield slows it (m/s²): a fast T20 outfield lets a ball in a gap race to the rope.</summary>
            public double Decel { get; }

            /// <summary>
            /// Along the ground off the bat: the pace it skims at, low, to its first bounce
            /// (<see cref="RollAt"/> s, at <see cref="RollFrom"/>) before the turf takes some
            /// of it (0: none — it rolls from the start, or it landed from the air).
            /// </summary>
            public double Skim { get; }

            /// <summary>A diving fielder got a hand to it but couldn't stop it: from then (s after the hit, 0: never) it runs on, same line, at this pace.</summary>
            public double DeflectAt { get; }
            public double DeflectSpeed { get; }

            /// <summary>This path with the ball palmed on at <paramref name="at"/>, keeping <paramref name="keep"/> of its pace.</summary>
            public BallPath Deflected(double at, double keep) =>
                new BallPath(AngleDeg, Hang, Carry, RollFrom, RollSpeed, RollAt, Decel, Skim, at, SpeedAt(at) * keep);

            /// <summary>Its pace along the ground (or across it, in the air) at <paramref name="t"/>.</summary>
            public double SpeedAt(double t)
            {
                if (Hang > 0 && (t < Hang || RollSpeed <= 0)) return Carry / Hang;
                if (DeflectAt > 0 && t > DeflectAt) return Math.Max(0, DeflectSpeed - Slow * (t - DeflectAt));
                if (Hang <= 0 && Skim > 0 && t < RollAt) return Skim;
                return Math.Max(0, RollSpeed - Slow * (t - RollAt));
            }

            public double AngleDeg { get; }

            /// <summary>Seconds in the air (0: along the ground) and how far it carries.</summary>
            public double Hang { get; }
            public double Carry { get; }

            /// <summary>Where, how fast and when (s after the hit) it runs along the ground; speed 0: it doesn't (a six).</summary>
            public (double x, double f) RollFrom { get; }
            public double RollSpeed { get; }
            public double RollAt { get; }

            public bool InAir => Hang > 0;

            /// <summary>Highest point of the flight above the line from the bat to where it lands.</summary>
            public double Apex => Trajectory.Gravity * Hang * Hang / 8;

            /// <summary>Ground position (batter-centred) and height above that line, <paramref name="t"/> s after the hit.</summary>
            public (double x, double f, double h) At(double t)
            {
                if (DeflectAt > 0 && t > DeflectAt)
                {
                    var (bx, bf, _) = Plain(DeflectAt);
                    var tau = DartMath.Clamp(t - DeflectAt, 0, DeflectSpeed / Slow);
                    var run = DeflectSpeed * tau - 0.5 * Slow * tau * tau;
                    return (bx + Math.Sin(AngleDeg * Math.PI / 180) * run, bf + Math.Cos(AngleDeg * Math.PI / 180) * run, 0);
                }
                return Plain(t);
            }

            private (double x, double f, double h) Plain(double t)
            {
                var ux = Math.Sin(AngleDeg * Math.PI / 180);
                var uf = Math.Cos(AngleDeg * Math.PI / 180);
                if (Hang > 0 && (t < Hang || RollSpeed <= 0))
                {
                    // A six carries on down into the crowd past the hang time.
                    var d = Carry * t / Hang;
                    return (ux * d, uf * d, 0.5 * Trajectory.Gravity * t * (Hang - t));
                }
                if (Hang <= 0 && Skim > 0 && t < RollAt)
                {
                    var back = Skim * (RollAt - Math.Max(0, t));
                    return (RollFrom.x - ux * back, RollFrom.f - uf * back, 0);
                }
                var tt = DartMath.Clamp(t - RollAt, 0, RollSpeed / Slow);
                var s = RollSpeed * tt - 0.5 * Slow * tt * tt;
                return (RollFrom.x + ux * s, RollFrom.f + uf * s, 0);
            }

            /// <summary>Seconds after the hit it stops rolling.</summary>
            public double StopsAt => DeflectAt > 0 ? DeflectAt + DeflectSpeed / Slow : RollSpeed > 0 ? RollAt + RollSpeed / Slow : Hang;

            /// <summary>The deceleration, the standard outfield's on a default path.</summary>
            private double Slow => Decel > 0 ? Decel : Outfield.Standard;
        }

        /// <summary>Result of a ball in the field.</summary>
        public readonly struct Play
        {
            public Play(int runs, bool boundary, bool caught, string fielder, (double x, double f) end, double time, BallPath path = default, string dropped = "", bool fumbled = false,
                double back = 0, double throwDistance = 0, bool dived = false, string beaten = "")
            {
                Beaten = beaten;
                Dived = dived;
                Runs = runs;
                Boundary = boundary;
                Caught = caught;
                Fielder = fielder;
                End = end;
                Time = time;
                Path = path;
                Dropped = dropped;
                Fumbled = fumbled;
                Back = back;
                ThrowDistance = throwDistance;
            }

            /// <summary>Fielded: seconds after the hit the throw gets back to the stumps, and how far it was thrown (the race between the wickets).</summary>
            public double Back { get; }
            public double ThrowDistance { get; }

            /// <summary>How the ball got to <see cref="End"/> (for the view).</summary>
            public BallPath Path { get; }

            /// <summary>Who put the catch down (empty: it wasn't a chance).</summary>
            public string Dropped { get; }

            /// <summary>The odd fumble gave the batters one more.</summary>
            public bool Fumbled { get; }

            /// <summary>He stopped it with a dive.</summary>
            public bool Dived { get; }

            /// <summary>Who dived and got a hand to it but couldn't stop it (the path is palmed on, slower, from <see cref="BallPath.DeflectAt"/>).</summary>
            public string Beaten { get; }

            public int Runs { get; }
            public bool Boundary { get; }
            public bool Caught { get; }
            public string Fielder { get; }

            /// <summary>Where the ball finished (fielded, rope, or the catch).</summary>
            public (double x, double f) End { get; }

            /// <summary>Seconds after the hit it got there.</summary>
            public double Time { get; }
        }

        /// <summary>
        /// A ball along the ground at <paramref name="speed"/> m/s from <paramref name="start"/>
        /// (null: off the bat, losing pace into the turf), already <paramref name="t0"/> s after
        /// the hit: four if it reaches the rope first, else whoever first gets his hands to it
        /// or dives and stops it (<see cref="Outfield"/>).
        /// </summary>
        public Play Ground(double angleDeg, double speed, Rng rng, (double x, double f)? start = null, double t0 = 0)
        {
            var (sx, sf) = start ?? (0, 0);
            // Off the bat it skims at full pace to its first bounce, then the turf takes some of it.
            var skim = start == null ? speed : 0;
            var skimFor = start == null ? Outfield.SkimTime : 0;
            if (start == null)
            {
                speed *= Outfield.TurfFor(skim);
                sx += Math.Sin(angleDeg * Math.PI / 180) * skim * skimFor;
                sf += Math.Cos(angleDeg * Math.PI / 180) * skim * skimFor;
                t0 = skimFor;
            }
            var decel = Outfield.DecelFor(Venue);
            var path = new BallPath(angleDeg, 0, 0, (sx, sf), speed, t0, decel, skim);
            t0 = start == null ? 0 : t0;
            const double dt = 0.02;
            var dived = new bool[Fielders.Count];
            var beaten = "";
            for (var t = 0.0; ; t += dt)
            {
                var now = t0 + t;
                var (px, pf, _) = path.At(now);
                var v = path.SpeedAt(now);
                var run = Outfield.Run(now);
                // Every fielder near its line has a go before it can reach the rope.
                for (var i = 0; i < Fielders.Count; i++)
                {
                    var (fx, ff) = Fielders[i].Position;
                    var dist = Math.Sqrt((fx - px) * (fx - px) + (ff - pf) * (ff - pf));
                    if (dist <= run + Outfield.Hands) return Fielded(i, px, pf, now, Outfield.Pickup, path, rng, false, beaten);
                    // A dive: at a ball going past within a dive's length that he won't get to on his feet.
                    // (Field rework: the slips and close catchers now stand a few metres from the bat, at the keeper's depth.
                    // A ball on them before he can react goes past unless it hits his hands — no full-length dive in 0.15 s.)
                    if (dived[i] || now < Outfield.Reaction || dist > run + Outfield.Dive || OnHisFeet(path, now, fx, ff)) continue;
                    dived[i] = true;
                    if (rng.Chance(Outfield.DiveStops(dist - run - Outfield.Hands, v, Fielding))) return Fielded(i, px, pf, now, Outfield.DiveRecover, path, rng, true, beaten);
                    // Beaten: a hand to it, and it runs on slower (the first one's palm-off is the one that shows).
                    if (beaten.Length > 0) continue;
                    beaten = Fielders[i].Name;
                    path = path.Deflected(now, Outfield.Deflect);
                }
                if (Over(px, pf)) return new Play(4, true, false, "", (px, pf), now, path, beaten: beaten);
                if (t > 40) return new Play(1, false, false, "", (px, pf), now, path, back: now + RunTime, throwDistance: Math.Sqrt(px * px + pf * pf), beaten: beaten);
            }
        }

        /// <summary>He can still get his hands to it running, within the next second or so.</summary>
        private static bool OnHisFeet(BallPath path, double now, double fx, double ff)
        {
            for (var t = now + 0.04; t < now + 1.2; t += 0.04)
            {
                var (px, pf, _) = path.At(t);
                if (Math.Sqrt((fx - px) * (fx - px) + (ff - pf) * (ff - pf)) <= Outfield.Run(t) + Outfield.Hands) return true;
            }
            return false;
        }

        /// <summary>Fielded at (x, f) at <paramref name="time"/>: the batters run while it comes back (the odd fumble gives one more).</summary>
        private Play Fielded(int i, double x, double f, double time, double pickup, BallPath path, Rng rng, bool dived, string beaten = "")
        {
            var throwDist = Math.Sqrt(x * x + f * f);
            // In the deep he's gathered it on the run and must stop, turn and set for the throw (focus batch 1:
            // with the throw as quick as a ring fielder's, a ball into the deep was a single, never the two).
            // Measured from the pitch (field rework): a ring man out at mid-off is 40 m from the bat but in the ring.
            var outFrom = FromPitch(Math.Atan2(x, f) * 180 / Math.PI, throwDist);
            var deepTurn = outFrom > DeepFrom ? DeepTurn * Math.Min(1, (outFrom - DeepFrom) / 15) : 0;
            var back = time + pickup + deepTurn + throwDist / ThrowSpeed;
            var fumbled = rng.Chance(0.04);
            if (fumbled) back += RunTime;
            // The runs they can safely make are judged off the quicker end (field rework): mid-off, mid-on and cover
            // on the ring are far nearer the bowler's stumps than the keeper's. Back and ThrowDistance stay the
            // keeper's end (RunPlan.For picks the end the throw actually goes to from them).
            var quickest = back - Math.Max(0, (throwDist - ThrowDistance(x, f, true)) / ThrowSpeed - RunPlan.BowlerEndExtra);
            var runs = DartMath.Clamp(DartMath.Floor((quickest - RunPlan.Start) / RunTime), 0, 3);
            return new Play(runs, false, false, Fielders[i].Name, (x, f), time, path, fumbled: fumbled, back: back, throwDistance: throwDist, dived: dived, beaten: beaten);
        }

        /// <summary>
        /// The odds a catch goes down: <paramref name="ran"/> of the <paramref name="reach"/> he could
        /// cover (his stretch), how hard it's hit and how flat (short hang); a better fielding side drops fewer.
        /// </summary>
        public double DropChance(double ran, double reach, double speed, double hang)
        {
            var stretch = DartMath.Clamp(ran / Math.Max(0.5, reach), 0, 1);
            var odds = 0.02 + 0.14 * stretch * stretch + (speed > 34 ? 0.04 : 0) + (hang < 1.6 ? 0.03 : 0) - (Fielding - 70) * 0.0025;
            return DartMath.Clamp(odds, 0.01, 0.35);
        }

        /// <summary>A ball in the air: six over the rope, a catch, or it lands and runs on.</summary>
        public Play Air(double angleDeg, double speed, double launchDeg, Rng rng)
        {
            var th = launchDeg * Math.PI / 180;
            var hang = 2 * speed * Math.Sin(th) / Trajectory.Gravity * 0.92;
            var carry = speed * Math.Cos(th) * hang * 0.88 * (Venue?.Carry ?? 1);
            var boundary = Rope(angleDeg);
            var ux = Math.Sin(angleDeg * Math.PI / 180);
            var uf = Math.Cos(angleDeg * Math.PI / 180);
            if (carry >= boundary) return new Play(6, true, false, "", (ux * boundary, uf * boundary), hang * boundary / carry, new BallPath(angleDeg, hang, carry, (ux * carry, uf * carry), 0, hang));

            var landing = (ux * carry, uf * carry);
            // Whoever can get under it in time has a go.
            Fielder? best = null;
            var bestDist = double.MaxValue;
            foreach (var f in Fielders)
            {
                var (fx, ff) = f.Position;
                var dist = Math.Sqrt((fx - landing.Item1) * (fx - landing.Item1) + (ff - landing.Item2) * (ff - landing.Item2));
                if (dist <= Reach + CatchSpeed * Math.Max(0, hang - CatchReaction) && dist < bestDist)
                {
                    best = f;
                    bestDist = dist;
                }
            }
            if (best != null)
            {
                // Held, mostly: about one chance in ten goes down in a T20 — almost never a dolly,
                // more often at full stretch, off a rasping hit or a flat one he barely sees.
                if (!rng.Chance(DropChance(bestDist, Reach + CatchSpeed * Math.Max(0, hang - CatchReaction), speed, hang))) return new Play(0, false, true, best.Name, landing, hang, new BallPath(angleDeg, hang, carry, landing, speed * 0.32, hang, Outfield.DecelFor(Venue)));
            }
            // Lands safe and runs on (slower, after a bounce) — or goes down.
            var g = Ground(angleDeg, speed * 0.32, rng, landing, hang);
            return new Play(g.Runs, g.Boundary, false, g.Fielder, g.End, g.Time, new BallPath(angleDeg, hang, carry, landing, speed * 0.32, hang, Outfield.DecelFor(Venue), 0, g.Path.DeflectAt, g.Path.DeflectSpeed),
                best?.Name ?? "", g.Fumbled, g.Back, g.ThrowDistance, g.Dived, g.Beaten);
        }
    }
}
