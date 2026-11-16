using System.Linq;
using Apl.Core;

namespace AuctionCore.Tests
{
    /// <summary>
    /// Named bowler picks for tests, so a dataset change can't silently swap in a different kind of
    /// bowler (v7 made the top-rated pacer a medium-pacer, Bhuvneshwar, whose style caps his pace class).
    /// Ties go to the lower id, so the pick is stable.
    /// </summary>
    internal static class TestBowlers
    {
        /// <summary>The best-rated pace bowler of any style (for tests where the speed class doesn't matter).</summary>
        public static Player TopPacer(Dataset data) =>
            data.Players.Where(p => p.BowlType == BowlType.Pace)
                .OrderByDescending(p => p.Bowling).ThenBy(p => p.Id, System.StringComparer.Ordinal).First();

        /// <summary>The best-rated genuinely fast bowler: a pacer whose researched style isn't medium or fast-medium.</summary>
        public static Player TopFastBowler(Dataset data) =>
            data.Players.Where(p => p.BowlType == BowlType.Pace && p.BowlStyle != "medium" && p.BowlStyle != "fast-medium")
                .OrderByDescending(p => p.Bowling).ThenBy(p => p.Id, System.StringComparer.Ordinal).First();
    }
}
