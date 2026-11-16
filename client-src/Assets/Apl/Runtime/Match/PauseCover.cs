using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// On the pause menu's canvas: while it lives, every other screen-space canvas
    /// under it (the batting / bowling HUD, the scorebug, the nets tips) is switched
    /// off, so the joystick, shot buttons and timing bar don't show through the
    /// glass columns; they come back exactly as they were when the menu closes.
    /// </summary>
    public sealed class PauseCover : MonoBehaviour
    {
        private readonly List<Canvas> _hidden = new List<Canvas>();

        private void Start()
        {
            var mine = GetComponent<Canvas>();
            var order = mine != null ? mine.sortingOrder : int.MaxValue;
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c == mine || !c.enabled || c.renderMode == RenderMode.WorldSpace) continue;
                if (c.transform.IsChildOf(transform)) continue;
                // Only what sits under the pause menu; pop-ups opened over it stay.
                if (c.rootCanvas.sortingOrder >= order) continue;
                c.enabled = false;
                _hidden.Add(c);
            }
        }

        private void OnDestroy()
        {
            foreach (var c in _hidden)
            {
                if (c != null) c.enabled = true;
            }
            _hidden.Clear();
        }
    }
}
