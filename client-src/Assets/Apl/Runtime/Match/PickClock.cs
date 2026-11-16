using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Core;

namespace Apl.Client.Match
{
    /// <summary>
    /// The pick screens' clocks, in every mode (owner v26 #8): the impact player 20 s; the
    /// striker, the non-striker and the bowler 10 s each (the new batter after a wicket is a
    /// striker / non-striker pick: 10 s too). At 0 the screen picks for you (the sensible
    /// default each screen names). The PLAYING XI's confirm clock (tournament / online) is 30 s.
    /// <para>
    /// Test runs (-aplmatchtest) cut every clock to a second; add <c>-aplrealtimers</c> to keep
    /// these lengths (the screens then time out and auto-pick on their own: a timer check).
    /// </para>
    /// </summary>
    public static class PickClock
    {
        public const float Impact = 20f;
        public const float Striker = 10f;
        public const float NonStriker = 10f;
        public const float NewBatter = 10f;
        public const float Bowler = 10f;
        public const float PlayingXi = 30f;

        private static bool? _real;

        /// <summary>-aplrealtimers: test runs keep the real clock lengths.</summary>
        public static bool RealInTests => _real ??= Environment.GetCommandLineArgs().Contains("-aplrealtimers");

        /// <summary>
        /// The impact player's sensible "who goes off" at the clock: the least useful of those who may
        /// go — batting, the weakest batter still to bat; fielding, the weakest bowler.
        /// </summary>
        public static Player? LeastUseful(IEnumerable<Player> eligible, bool batting) =>
            batting
                ? eligible.OrderBy(p => p.Batting).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault()
                : eligible.OrderBy(p => p.Bowling).ThenBy(p => p.Batting).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
    }
}
