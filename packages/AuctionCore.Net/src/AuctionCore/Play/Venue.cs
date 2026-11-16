using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    /// <summary>
    /// A ground: its name and ends, where the rope is, how far the ball carries
    /// (altitude) and what sort of pitch it tends to produce. The rope is given
    /// from the middle of the pitch along four lines — straight (the bowler's
    /// end the batter faces), behind (the keeper's end), the right-hander's off
    /// side and leg side — and joined by quarter ellipses between them.
    /// Fictional grounds styled on the league's real venues.
    /// </summary>
    public sealed class Venue
    {
        public Venue(string id, string name, string city, string home, string endA, string endB,
            double straight, double off, double leg, double behind, double carry, double[] pitch, string character)
        {
            Id = id;
            Name = name;
            City = city;
            Home = home;
            EndA = endA;
            EndB = endB;
            Straight = straight;
            Off = off;
            Leg = leg;
            Behind = behind;
            Carry = carry;
            PitchWeights = pitch;
            Character = character;
        }

        public string Id { get; }
        public string Name { get; }
        public string City { get; }

        /// <summary>The franchise that plays here (id), for the crowd and the default pick.</summary>
        public string Home { get; }

        /// <summary>End A is behind the bowler (the broadcast end), End B behind the keeper.</summary>
        public string EndA { get; }
        public string EndB { get; }

        /// <summary>Rope distances from the middle of the pitch, metres.</summary>
        public double Straight { get; }
        public double Off { get; }
        public double Leg { get; }
        public double Behind { get; }

        /// <summary>Thin air: the ball flies this much further.</summary>
        public double Carry { get; }

        /// <summary>Chances of a balanced, flat, turning and seaming pitch.</summary>
        public double[] PitchWeights { get; }

        /// <summary>One line for the menu: size and what the pitch does.</summary>
        public string Character { get; }

        /// <summary>
        /// Rope distance from the middle along a ground direction (x across,
        /// + the right-hander's off side; z up the pitch towards End A).
        /// </summary>
        public double RopeFromCentre(double x, double z)
        {
            var len = Math.Sqrt(x * x + z * z);
            if (len < 1e-9) return Math.Min(Math.Min(Straight, Behind), Math.Min(Off, Leg));
            var a = x >= 0 ? Off : Leg;
            var b = z >= 0 ? Straight : Behind;
            var ux = x / len;
            var uz = z / len;
            return 1 / Math.Sqrt(ux * ux / (a * a) + uz * uz / (b * b));
        }

        /// <summary>Is a ground point (from the middle) over the rope?</summary>
        public bool Over(double x, double z) => x * x + z * z >= Math.Pow(RopeFromCentre(x, z), 2);

        /// <summary>
        /// Distance from the batter's stumps to the rope along a shot angle (0
        /// straight, + off side) — the batter stands half a pitch behind the
        /// middle, so the ropes behind him are closer.
        /// </summary>
        public double FromBatter(double angleDeg, bool leftHanded)
        {
            var (ux, uz) = Dir(angleDeg, leftHanded);
            double lo = 0, hi = 200;
            for (var i = 0; i < 30; i++)
            {
                var t = (lo + hi) / 2;
                if (Over(ux * t, -HalfPitch + uz * t)) hi = t;
                else lo = t;
            }
            return (lo + hi) / 2;
        }

        /// <summary>Is a batter-centred point (x across + off, f forward) over the rope?</summary>
        public bool OverFromBatter(double x, double f, bool leftHanded) => Over(leftHanded ? -x : x, -HalfPitch + f);

        private static (double x, double z) Dir(double angleDeg, bool leftHanded)
        {
            var a = angleDeg * Math.PI / 180;
            return (Math.Sin(a) * (leftHanded ? -1 : 1), Math.Cos(a));
        }

        public const double HalfPitch = 10.06;

        /// <summary>
        /// The standard oval's average rope distance over this ground's: above 1
        /// for a small ground. The simulated innings scores to suit it.
        /// </summary>
        public double SizeFactor
        {
            get
            {
                double sum = 0, std = 0;
                for (var a = 0; a < 360; a += 5)
                {
                    sum += FromBatter(a, false);
                    std += Field.Boundary(a);
                }
                return std / sum;
            }
        }

        public Pitch PickPitch(Rng rng) => (Pitch)rng.PickWeighted(PitchWeights);

        /// <summary>
        /// The ground's own pitch (owner v26 #23): the type it most often produces — green at Shivalik,
        /// a dry turner at Marina, a flat belter at Seaface. A match still varies it (<see cref="PickPitch"/>).
        /// </summary>
        public Pitch DefaultPitch
        {
            get
            {
                var best = 0;
                for (var i = 1; i < PitchWeights.Length; i++) if (PitchWeights[i] > PitchWeights[best]) best = i;
                return (Pitch)best;
            }
        }

        // Balanced, flat, turning, seaming.
        private static readonly double[] Flat = { 0.3, 0.5, 0.1, 0.1 };
        private static readonly double[] Turning = { 0.3, 0.1, 0.5, 0.1 };
        private static readonly double[] Seaming = { 0.35, 0.1, 0.1, 0.45 };
        private static readonly double[] Even = { 0.45, 0.25, 0.15, 0.15 };

        /// <summary>The ten home grounds and three second homes, biggest last.</summary>
        public static readonly IReadOnlyList<Venue> All = new List<Venue>
        {
            new Venue("seaface", "Seaface Stadium", "Mumbai", "MBH", "Pavilion End", "Station End", 65, 67, 65, 63, 1.0, Flat,
                "Small bowl by the sea. Fast outfield, big totals."),
            new Venue("marina", "Marina Oval", "Chennai", "CHC", "Club House End", "Beach Road End", 70, 66.5, 66.5, 65, 1.0, Turning,
                "Medium oval. Slow, dry pitch that turns."),
            new Venue("gardencity", "Garden City Bowl", "Bengaluru", "BLR", "Pavilion End", "Park End", 63.5, 60, 60, 58, 1.03, Flat,
                "The smallest ground, high up: the ball flies."),
            new Venue("lakeside", "Lakeside Arena", "Nava Raipur", "BLR", "Lake End", "City End", 82, 77.5, 77.5, 74, 1.0, Even,
                "One of the biggest grounds. Sixes are earned."),
            new Venue("hooghly", "Hooghly Oval", "Kolkata", "KOC", "Club House End", "River End", 74, 67, 67, 68, 1.0, Even,
                "Big historic oval, quick outfield, loud crowd."),
            new Venue("oldfort", "Old Fort Ground", "Delhi", "DLD", "Pavilion End", "Fort End", 66.5, 60.5, 60.5, 61.5, 1.0, Flat,
                "Small with short square boundaries."),
            new Venue("deccan", "Deccan Arena", "Hyderabad", "HYH", "Pavilion End", "North End", 70, 65.5, 65.5, 65, 1.0, Flat,
                "Medium ground on a flat, true pitch."),
            new Venue("sabarmati", "Sabarmati Mega Bowl", "Ahmedabad", "GJG", "River End", "City End", 77.5, 72.5, 72.5, 70, 1.0, Even,
                "The biggest bowl in the world. Needs real power."),
            new Venue("awadh", "Awadh Arena", "Lucknow", "LKL", "Pavilion End", "Gomti End", 76.5, 72, 72, 70, 1.0, Turning,
                "Big ground, slow pitch, spin later on."),
            new Venue("shivalik", "Shivalik Park", "New Chandigarh", "PJP", "Pavilion End", "Hills End", 73.5, 70, 70, 68, 1.0, Seaming,
                "Medium-large, pace and bounce."),
            new Venue("dhauladhar", "Dhauladhar Heights", "Dharamshala", "PJP", "Valley End", "Mountain End", 69, 65, 65, 63, 1.05, Seaming,
                "High in the hills: thin air, seam movement."),
            new Venue("pinkcity", "Pink City Oval", "Jaipur", "RJR", "Palace End", "Fort End", 73.5, 68, 68, 66.5, 1.0, Turning,
                "Medium-large; the pitch slows and spins."),
            new Venue("brahmaputra", "Brahmaputra Park", "Guwahati", "RJR", "River End", "Hills End", 70, 65.5, 65.5, 64.5, 1.0, Flat,
                "Batting-friendly, humid, early dew."),
        };

        public static Venue? Find(string? id) => All.FirstOrDefault(v => v.Id == id);

        /// <summary>A franchise's main home ground.</summary>
        public static Venue HomeOf(string franchiseId) => All.FirstOrDefault(v => v.Home == franchiseId) ?? All[0];
    }
}
