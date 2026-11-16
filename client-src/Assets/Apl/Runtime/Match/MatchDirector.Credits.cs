using Apl.Core.Play;

namespace Apl.Client.Match
{
    /// <summary>
    /// Who effected a dismissal, by name and position, for the wicket graphic (round 11: run outs read
    /// "slip" while the keeper broke the stumps). The engine's scorecard line names them the same way
    /// (QuickMatch.DismissalText); this is the on-screen form: "KOKK (COVER) → †SAMMSUN".
    /// </summary>
    public sealed partial class MatchDirector
    {
        private string FielderCredit(BallResult r)
        {
            if (r.Fielder.Length == 0 || !(r.Caught || r.RunOut || r.HowOut == "stumped")) return "";
            var bowler = _match.CurrentBowlerOf(r);
            var who = _match.PlayerAt(r.Fielder, bowler);
            var name = who?.Name ?? r.Fielder;
            var pos = r.Fielder == "keeper" ? "keeper" : r.Fielder == "bowler" ? "bowler" : r.Fielder;
            var credit = $"{name} ({pos})";
            if (r.RunOut)
            {
                // Names only on a run out (r11: "(SLIP)" beside the thrower read oddly), as the scorecard has them.
                credit = name;
                if (r.DirectHit) return (credit + " · direct hit").ToUpperInvariant();
                var breaker = r.RunOutAtKeeperEnd ? "†" + _match.FieldingKeeper.Name : bowler.Name;
                if (who == null || breaker.TrimStart('†') != who.Name) credit += " → " + breaker;
            }
            return credit.ToUpperInvariant();
        }
    }
}
