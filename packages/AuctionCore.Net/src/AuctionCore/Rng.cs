using System;
using System.Collections.Generic;

namespace Apl.Core
{
    /// <summary>
    /// Deterministic random numbers (mulberry32) — bit-identical to the Dart
    /// <c>Rng</c>, so the same seed gives the same auction on phone and server.
    /// </summary>
    public sealed class Rng
    {
        private uint _state;

        public Rng(long seed)
        {
            _state = (uint)(seed & 0xFFFFFFFFL);
        }

        public uint NextUint32()
        {
            unchecked
            {
                _state += 0x6D2B79F5u;
                uint t = _state;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return t ^ (t >> 14);
            }
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => NextUint32() / 4294967296.0;

        /// <summary>Uniform in [0, max).</summary>
        public int NextInt(int max)
        {
            if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max), "must be positive");
            return (int)Math.Floor(NextDouble() * max);
        }

        public bool Chance(double p) => NextDouble() < p;

        public double Range(double min, double max) => min + (max - min) * NextDouble();

        public T Pick<T>(IReadOnlyList<T> items) => items[NextInt(items.Count)];

        /// <summary>Index into <paramref name="weights"/>, in proportion to each weight.</summary>
        public int PickWeighted(IReadOnlyList<double> weights)
        {
            var total = 0.0;
            for (var i = 0; i < weights.Count; i++) total += weights[i];
            var roll = NextDouble() * total;
            for (var i = 0; i < weights.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0) return i;
            }
            return weights.Count - 1;
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = NextInt(i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }

    /// <summary>Dart's number semantics where they differ from C#'s defaults.</summary>
    public static class DartMath
    {
        /// <summary>Dart <c>double.round()</c>: halves round away from zero (C# defaults to even).</summary>
        public static int Round(double x) => (int)Math.Round(x, MidpointRounding.AwayFromZero);

        public static int Floor(double x) => (int)Math.Floor(x);

        public static int Ceil(double x) => (int)Math.Ceiling(x);

        public static int Clamp(int x, int lo, int hi) => x < lo ? lo : (x > hi ? hi : x);

        public static double Clamp(double x, double lo, double hi) => x < lo ? lo : (x > hi ? hi : x);

        /// <summary>Dart <c>String.compareTo</c>: ordinal UTF-16 code units.</summary>
        public static int Compare(string a, string b) => Math.Sign(string.CompareOrdinal(a, b));
    }
}
