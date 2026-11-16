using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The nets feel like nets (round 11, audio-modes): no commentary box, no PA, no
    /// chants, applause or roar — just a thin murmur and the ball — and the ground's big
    /// screen keeps the session's count ("NETS · BALL 14 · 3 FOURS · 1 SIX · 0 OUT")
    /// instead of a scorebug. The session counts every ball faced or bowled.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private int _netBalls, _netFours, _netSixes, _netOuts, _netRuns;

        /// <summary>From BeginNets: quiet the match furniture.</summary>
        private void NetsFeelStart()
        {
            _comm.Muted = true;
            // Both voices (round 12: the start line, on the second voice's channel, still got out).
            _comm.Silence();
            Sfx.CrowdFxVolume = 0f;
            Sfx.CrowdLevel = 0.05f;
            Sfx.SetCrowd(1f);
            Sfx.SetRoar(0f);
            Debug.Log("[APL modes] nets: commentary, PA and crowd reactions off");
        }

        /// <summary>A ball in the nets (BoxAfter): the session's count.</summary>
        private void NetsCount(BallResult r)
        {
            if (r.Legal) _netBalls++;
            _netRuns += r.BatRuns;
            if (r.Six) _netSixes++;
            else if (r.Four) _netFours++;
            if (r.Wicket) _netOuts++;
        }

        /// <summary>The big screen in the nets: the session, not a score.</summary>
        private (string score, string sub, string teamColour) NetsScreen() =>
            (_nets == NetsLaunch.Kind.Batting ? "BATTING NETS" : "BOWLING NETS",
             $"BALL {_netBalls}  ·  {_netRuns} RUNS  ·  {_netFours} FOURS  ·  {_netSixes} SIXES  ·  {_netOuts} OUT",
             "#1F7A8C");
    }
}
