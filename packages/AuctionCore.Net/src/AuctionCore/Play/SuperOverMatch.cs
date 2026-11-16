using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    /// <summary>The Super Over as a match: one over a side, the picked three batters and bowler in each innings.</summary>
    public sealed partial class QuickMatch
    {
        /// <summary>This is a Super Over (a tie in it goes to boundaries, see <see cref="SuperOver"/>).</summary>
        public bool IsSuperOver { get; private set; }

        private Dictionary<string, List<Player>>? _superOrders;
        private Dictionary<string, Player>? _superBowlers;

        internal static QuickMatch ForSuperOver(TeamSheet user, TeamSheet opp, bool userBatsFirst, long seed, Venue? venue,
            Dictionary<string, List<Player>> orders, Dictionary<string, Player> bowlers)
        {
            var m = Full(user, opp, 1, seed, venue, userBatsFirst);
            m.IsSuperOver = true;
            m._superOrders = orders;
            m._superBowlers = bowlers;
            m.SuperOverSetUp();
            return m;
        }

        /// <summary>Each innings: the side's three in the order picked, its bowler for the over.</summary>
        private void SuperOverSetUp()
        {
            if (_superOrders == null || _superBowlers == null) return;
            Order = _superOrders[Batting.TeamId].Select(p => new ChaseBatter(p)).ToList();
            Order[0].Batted = Order[1].Batted = true;
            Striker = 0;
            NonStriker = 1;
            _nextIn = 2;
            _chosen.Clear();
            _chosen[0] = _superBowlers[Bowling.TeamId];
        }
    }
}
