using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Owner v26 report (screenshot 248): the scoreboard strip and the field radar (mini-map) stayed up through the
    /// victory celebration. A clean feed for the finale: everything the HUD is showing when it starts is put away
    /// (the score strip, the radar, the play controls, anything else on its canvas), and put back as it was after.
    /// Pieces the finale itself puts up later (the event graphic, the player-of-the-match card, the skip tap) are made
    /// after this and stay. Nested calls count.
    /// </summary>
    public sealed partial class MatchHud
    {
        private readonly List<GameObject> _cleanHidden = new List<GameObject>();
        private int _clean;

        public void CleanFeed(bool on)
        {
            if (on)
            {
                if (_clean++ > 0 || _root == null) return;
                _cleanHidden.Clear();
                foreach (Transform c in _root)
                {
                    if (!c.gameObject.activeSelf) continue;
                    c.gameObject.SetActive(false);
                    _cleanHidden.Add(c.gameObject);
                }
                Debug.Log($"[APL victory] HUD clean feed: {_cleanHidden.Count} pieces hidden");
                return;
            }
            if (_clean == 0 || --_clean > 0) return;
            foreach (var go in _cleanHidden) if (go != null) go.SetActive(true);
            _cleanHidden.Clear();
        }
    }
}
