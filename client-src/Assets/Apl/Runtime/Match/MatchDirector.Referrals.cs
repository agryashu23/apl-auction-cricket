using System.Collections;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The umpires' side of a ball (rules-bowling): the engine refers close run
    /// outs, stumpings and the odd low catch (<see cref="BallResult.Referral"/>,
    /// its decision <see cref="BallResult.TvOut"/>), and the presentation's
    /// <see cref="ThirdUmpire"/> shows it — called once the play is dead, before
    /// the wicket sequence (or the let-off). An appeal the on-field umpire turns
    /// down for a genuine nick (<see cref="BallResult.NotGiven"/>) gets its shout.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private BallResult? _tvReferred;

        /// <summary>Upstairs, if the engine referred this ball (once per ball).</summary>
        private IEnumerator Refer(BallResult r, float moment = -1f)
        {
            if (r.Referral == TvCheck.None || _tvReferred == r) yield break;
            _tvReferred = r;
            StallProbe.Mark($"refer: {r.Referral}");
            var kind = r.Referral == TvCheck.RunOut ? Referral.RunOut : r.Referral == TvCheck.Stumping ? Referral.Stumping : Referral.Catch;
            yield return ThirdUmpire(kind, r, r.TvOut, moment);
            // (The big OUT / NOT OUT is the presentation's.)
            if (!r.TvOut) Sfx.Applause(0.4f);
        }

        /// <summary>The keeper and bowler go up for a nick the umpire doesn't see.</summary>
        private void TurnedDown(BallResult r)
        {
            if (r.NotGiven != "caught behind") return;
            Extra(After(0.3f, () => _bowler.Act("bowler_appeal_caught_behind", Vector3.back)));
            Appeal();
            Extra(After(1.3f, () => Umpire("umpire_not_out")));
            Extra(After(1.9f, () => _bowler.Act("appeal_turned_down_hands_on_head")));
            Extra(After(1.5f, () => Show("NOT OUT", "the umpire's unmoved", UiKit.Dim, 1.6f)));
        }
    }
}
