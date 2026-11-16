using System.Diagnostics;
using Apl.Core.Play;
using Xunit;

namespace AuctionCore.Tests
{
    /// <summary>Round 13 blocker: the field share-out must be quick and never stall, whatever it's given.</summary>
    public class AssignTests
    {
        private static double[,] Grid(int n, System.Func<int, int, double> c)
        {
            var m = new double[n, n];
            for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                m[i, j] = c(i, j);
            return m;
        }

        [Fact]
        public void Degenerate_inputs_finish_fast_and_never_hang()
        {
            var cases = new[]
            {
                Grid(9, (i, j) => 0),                                        // everyone on the same spot
                Grid(9, (i, j) => i == j ? 0 : 100),                         // each already on his spot
                Grid(9, (i, j) => double.NaN),                               // NaN everywhere
                Grid(9, (i, j) => (i + j) % 3 == 0 ? double.PositiveInfinity : i * j), // infinities
                Grid(9, (i, j) => double.MaxValue),                          // huge
                Grid(9, (i, j) => (i - j) * (i - j) * 1e7),                  // large, ordinary
            };
            QuickMatch.Assign(cases[5], 9); // warm (the JIT's first call isn't the method's speed)
            var sw = Stopwatch.StartNew();
            foreach (var c in cases)
            {
                var t = Stopwatch.StartNew();
                var r = QuickMatch.Assign(c, 9);
                Assert.True(t.Elapsed.TotalMilliseconds < 25, $"{t.Elapsed.TotalMilliseconds} ms");
                if (r == null) continue;
                // A share-out, when it gives one, is one man per position.
                Assert.Equal(9, new System.Collections.Generic.HashSet<int>(r).Count);
            }
            // The ordinary cases do give one, and the diagonal is the answer when everyone's in place.
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, QuickMatch.Assign(cases[1], 9));
            Assert.NotNull(QuickMatch.Assign(cases[5], 9));
            Assert.True(sw.Elapsed.TotalMilliseconds < 100);
        }
    }
}
