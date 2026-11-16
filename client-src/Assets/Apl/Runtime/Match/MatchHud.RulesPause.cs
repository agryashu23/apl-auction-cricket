using System;
using System.Collections.Generic;
using Apl.Client.Ui;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The pause menu's match row (rules-bowling): FIELD (bowling), STRATEGIC
    /// TIMEOUT (in its overs window), IMPACT PLAYER (while the side's
    /// substitution is unused) — each only when it's on offer. Pressing one
    /// resumes the game; the director does it at the next chance (the next ball,
    /// the next over break, the next over break or wicket).
    /// </summary>
    public sealed partial class MatchHud
    {
        /// <summary>What's on offer now (set by the director).</summary>
        public Func<(bool field, bool timeout, bool impact)>? RulesOptions;

        public event Action? FieldMenu, TimeoutMenu, ImpactMenu;

        private void RulesPauseRow(RectTransform panel)
        {
            var o = RulesOptions?.Invoke();
            if (o == null) return;
            var buttons = new List<(string label, Action act)>();
            if (o.Value.field) buttons.Add(("FIELD", () => FieldMenu?.Invoke()));
            if (o.Value.timeout) buttons.Add(("TIMEOUT", () => TimeoutMenu?.Invoke()));
            if (o.Value.impact) buttons.Add(("IMPACT SUB", () => ImpactMenu?.Invoke()));
            if (buttons.Count == 0) return;
            const float grow = 110f;
            var top = panel.sizeDelta.y;
            panel.sizeDelta += new Vector2(0, grow);
            if (panel.parent is RectTransform back && back != _pauseMenu?.transform) back.sizeDelta += new Vector2(0, grow);
            var w = 560f / buttons.Count;
            for (var i = 0; i < buttons.Count; i++)
            {
                var (label, act) = buttons[i];
                UiKit.Btn(panel, "Rules_" + label, new Vector2(0.5f, 0), new Vector2(-280 + w * (i + 0.5f), top - 20), new Vector2(w - 16, 76), label, UiKit.Accent, () =>
                {
                    SetPaused(false);
                    act();
                }, 22);
            }
        }
    }
}
