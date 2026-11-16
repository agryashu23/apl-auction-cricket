using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Owner v26 #25 (batting): per ball, before anyone is placed for the next one, where both batters
    /// ended the last — "[APL crease] striker NAME settled at z=… (crease z=…) inside|OUTSIDE" — so a test
    /// run shows whether the walks in (BatterRig.Crease.cs) got them behind the line in time.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private void LogCrease()
        {
            if (_match.LegalBalls == 0 && _match.Log.Count == 0) return;
            foreach (var (rig, who) in new[] { (_striker, "striker"), (_other, "non-striker") })
                if (rig != null && rig.gameObject.activeInHierarchy && rig.UsesClips)
                    Debug.Log(rig.CreaseLine($"{who} {rig.name}"));
        }
    }
}
