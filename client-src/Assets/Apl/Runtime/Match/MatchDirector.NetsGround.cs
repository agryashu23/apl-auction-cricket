using System.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The empty practice net (round 13): only the batter, the bowler and the keeper are
    /// seen — the rest of the fielding side is hidden (their bodies still play their part
    /// for the engine, unseen). The umpires stay away too.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private void NetsEmpty()
        {
            var keep = new[] { Refs.Bowler, Refs.Keeper }.Where(x => x != null).Select(x => x!.gameObject).ToList();
            if (_bowler != null) keep.Add(_bowler.gameObject);
            if (_keeper != null) keep.Add(_keeper.gameObject);
            foreach (var a in _side.Values)
            {
                if (a == null || keep.Contains(a.gameObject)) continue;
                foreach (var r in a.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }
            foreach (var u in FindObjectsByType<Animator>(FindObjectsSortMode.None))
                if (u.name.Contains("Umpire")) foreach (var r in u.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            Debug.Log("[APL] nets: the empty practice net — fielders and umpires out of sight");
        }
    }
}
