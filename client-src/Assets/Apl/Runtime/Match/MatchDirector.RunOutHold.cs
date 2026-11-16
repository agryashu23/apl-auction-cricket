using System.Collections;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Run-out owner (2026-11-13; build 4 F4 balls 1 and 1.2: given out on run 2 while the picture had him in his
    /// ground, +0.2 m). The engine's run out is decided on its clock; the picture's wicket waits for the ball to reach
    /// the stumps on screen, and when the throw comes in late the batter, running the crease-to-crease clip, makes his
    /// ground first, and the decision reads as wrong. The man the engine has run out never makes his ground on
    /// screen: if he gets within <see cref="HoldShort"/> of the line before the bails are off, he pulls up there
    /// (the run-out reaction: hand on the helmet, looking back at the stumps), and the break comes with him short.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The out batter pulls up this far (m) short of the line if the bails aren't off yet.</summary>
        private const float HoldShort = 1.3f;

        /// <summary>The batter already pulled up short this ball (his reaction is played; OutReaction skips it).</summary>
        private BatterRig? _heldShort;

        /// <summary>From OutReaction: watch the man run out until the wicket's broken; hold him short of the line.</summary>
        private IEnumerator HoldOutShort(BallResult r, BatterRig rig)
        {
            _heldShort = null;
            var keeperEnd = r.RunOutAtKeeperEnd;
            for (var until = Time.time + 12f; Time.time < until; )
            {
                if (rig == null || !r.RunOut || _wicketBroken || RunState.Dived || RunState.RunOut != rig) yield break;
                if (PastCrease(keeperEnd) > -HoldShort)
                {
                    if (rig.React("run_out_reaction_walk_off", walkOff: true))
                    {
                        _heldShort = rig;
                        if (Autoplay) Debug.Log($"[APL runout] held short: {rig.name} pulled up {-PastCrease(keeperEnd):0.0} m short of the {(keeperEnd ? "keeper's" : "bowler's")} end, the bails not yet off (picture behind the engine's {r.BrokenAt:0.00} s)");
                    }
                    yield break;
                }
                yield return null;
            }
        }
    }
}
