using System.Collections;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>What the on-field umpire sends upstairs.</summary>
    public enum Referral { RunOut, Stumping, Catch }

    /// <summary>
    /// The TV umpire, broadcast side: "REFERRED TO THIRD UMPIRE", the replay of
    /// the moment from the angles that settle it (side-on and stump cam at the
    /// crease for a run-out or stumping, the catch close-up from two sides), a
    /// "checking…" hold on the frozen frame, then the big-screen OUT / NOT OUT.
    /// The decision is the engine's; this only shows it.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The ball last sent upstairs (its wicket sequence skips the replay: the referral was it).</summary>
        private BallResult? _referred;

        /// <summary>
        /// The referral of this ball (rules call it, and wait on it, once the play
        /// is dead): <paramref name="decision"/> true = OUT. <paramref name="moment"/>
        /// is the game time (<c>Time.time</c>) the bails came off / the ball was
        /// caught — the replay slows down and freezes there; &lt; 0 = 0.8 s ago.
        /// Skippable with a tap (straight to the verdict).
        /// </summary>
        public IEnumerator ThirdUmpire(Referral kind, BallResult r, bool decision, float moment = -1f)
        {
            if (moment < 0) moment = Time.time - 0.8f;
            _referred = r;
            yield return ThirdUmpireShow(kind, r, decision, moment);
        }
    }
}
