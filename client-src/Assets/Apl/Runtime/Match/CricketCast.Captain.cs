using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Ceremony kit (ratings r9/r10: captains at the toss bareheaded): while a player is out
    /// for the toss, an interview or the trophy (MatchDirector.Stage) he wears his side's cap —
    /// or the team sun hat if his hair won't take one (NoCap) — unless he already has headwear.
    /// Round 12 (r11 `07`: the cap sat on the back of his head, peak behind): the cap is fitted
    /// when the body is made, in its bind pose like every other piece of gear, and only shown
    /// at the ceremony — fitted then, on a posed head, it came out tilted off the skull.
    /// </summary>
    public sealed partial class CricketCast
    {
        private readonly Dictionary<Animator, Franchise> _teamOf = new Dictionary<Animator, Franchise>();
        private readonly Dictionary<Animator, List<SkinnedMeshRenderer>> _ceremonyCaps = new Dictionary<Animator, List<SkinnedMeshRenderer>>();

        /// <summary>At spawn, before any pose: a hidden cap (or hat) on everyone who has no headwear.</summary>
        private void CeremonyCaps(Dictionary<string, Animator> side, Franchise team)
        {
            foreach (var a in side.Values)
            {
                if (a == null || Groups(a, CapGear, HatGear).Any()) continue;
                var label = Capped(a) ? CapGear : HatGear;
                if (!Wear(a, label, label == CapGear ? CapSet : HatSet, CapSlots(team))) continue;
                var added = Groups(a, label).ToList();
                foreach (var r in added) r.enabled = false;
                _ceremonyCaps[a] = added;
                Hair(a);
            }
        }

        /// <summary>The ceremony cap shown (<paramref name="on"/>) or hidden again.</summary>
        public void CaptainCap(Animator? a, bool on)
        {
            if (a == null || !_ceremonyCaps.TryGetValue(a, out var caps)) return;
            foreach (var r in caps)
            {
                if (r == null) continue;
                r.gameObject.SetActive(true);
                r.enabled = on;
            }
            Hair(a);
        }
    }
}
