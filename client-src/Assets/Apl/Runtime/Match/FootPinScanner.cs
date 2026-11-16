using System;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Adds a FootPin to every player body in the match scene (owner v26 #17), and logs that it did:
    /// "[APL footpin] installed on N bodies" whenever the count grows (and once at boot, with the switch's state),
    /// then every 30 s how many feet were held and why they were let go — proof it runs in a player build.
    /// </summary>
    internal sealed class FootPinScanner : MonoBehaviour
    {
        private float _next, _report;
        private int _logged = -1;

        private void Start() => Debug.Log($"[APL footpin] scanner up (slide fixes {(SlideFix.On ? "on" : "OFF: -aplnoslidefix, no pins")}{(FootPin.Off ? "; pins OFF: -aplnofootpin" : "")})");

        private void Update()
        {
            if (!SlideFix.On) return;
            if (Time.unscaledTime >= _report)
            {
                if (_report > 0 && FootPin.Installed > 0)
                    Debug.Log($"[APL footpin] {FootPin.Installed} bodies; feet held {FootPin.Holds}, let go on lifting {FootPin.Lifted}, let go too far (>25 cm) {FootPin.TooFar}, twisted {FootPin.Twisted}, out of reach {FootPin.Overreach} (all three re-planted by a lifted step), dropped on a placement {FootPin.Placed}, worst drawn-ankle miss {FootPin.WorstMiss * 100:0} cm, held frames missing by > 3 cm {FootPin.MissFrames} of {FootPin.HeldFrames}, stale holds dropped {FootPin.StaleDrops}, stale pivots refused {TurnStep.StalePivots}, in-play turn steps {TurnStep.PlaySteps}; forced re-plants by clip: {string.Join(", ", FootPin.ReplantsByClip.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + " " + kv.Value))}, turn steps {FootPin.TurnSteps}; turns on the spot {TurnStep.Turns} ({TurnStep.ByClip} by clip)");
                _report = Time.unscaledTime + 30f;
            }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            if (FindAnyObjectByType<MatchDirector>() == null) return;
            var added = 0;
            foreach (var a in FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                if (a == null || !a.isHuman || a.GetComponent<FootPin>() != null) continue;
                var player = a.GetComponent<BatterRig>() != null || a.GetComponent<BowlerRig>() != null || a.GetComponent<KeeperRig>() != null
                             || a.name.StartsWith("Field") || a.name.StartsWith("Bat");
                if (!player) continue;
                a.gameObject.AddComponent<FootPin>();
                added++;
            }
            if (added > 0 && _logged != added)
            {
                _logged = added;
                Debug.Log($"[APL footpin] installed on {added} more bodies ({FootPin.Installed} running so far)");
            }
        }
    }
}
