using Apl.Core;

namespace Apl.Client.Match
{
    /// <summary>What the ground's big screens show (StadiumBigScreen polls it): the score and the chase.</summary>
    public sealed partial class MatchDirector
    {
        /// <summary>Two lines for the big screen — "CHC 16/0 (1.4)", "NEED 30 OFF 10" / "SAMMSUN 10* (3)" / "BHUMRAH BOWLING" — or null before the match.</summary>
        public (string score, string sub, string teamColour)? ScreenScore()
        {
            if (_match == null) return null;
            // The nets: the session's count, not a scorebug (audio-modes: MatchDirector.NetsFeel.cs).
            if (_nets != NetsLaunch.Kind.None) return NetsScreen();
            var side = BattingSide;
            var score = $"{side.ShortName} {_match.Runs}/{_match.Wickets}  ({_match.Over}.{_match.BallInOver})";
            var chase = _match.Chasing && _match.Target > 0
                ? (_match.Won ? "TARGET REACHED" : $"NEED {_match.RunsNeeded} OFF {_match.BallsLeft}")
                : $"{_match.Overs} OVERS";
            // The second line turns over every four seconds: the chase, the batter on strike, the bowler.
            var on = _match.OnStrike;
            var sub = ((int)(UnityEngine.Time.time / 4f) % 3) switch
            {
                1 => $"{Surname(on.Player.Name)} {on.Runs}* ({on.Balls})",
                2 => $"{Surname(_match.CurrentBowler.Name)} BOWLING",
                _ => chase,
            };
            return (score, sub, side.PrimaryColor);
        }

        private static string Surname(string name) => (name.Contains(' ') ? name.Substring(name.LastIndexOf(' ') + 1) : name).ToUpperInvariant();
    }
}
