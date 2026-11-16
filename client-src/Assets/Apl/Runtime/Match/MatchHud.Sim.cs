using System;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The simulate options (focus batch 1, match-play; owner v26 #10 moved them all here from the
    /// tournament screen): the pause menu's MATCH page has a SIMULATE row — THIS OVER, TO LAST 2 OVERS,
    /// THIS INNINGS, and WHOLE MATCH in a two-innings match's first innings — and the HUD has a SIM OVER
    /// chip during the over break. The director does the work (MatchDirector.Sim.cs); this only offers the choices.
    /// </summary>
    public sealed partial class MatchHud
    {
        /// <summary>0: the rest of this over; 1: the rest of the innings; 2: up to the last two overs; 3: the rest of the match.</summary>
        public event Action<int>? SimPressed;

        /// <summary>Which sims make sense now (the director answers: e.g. not to "last 2" inside them).</summary>
        public Func<(bool over, bool innings, bool key)>? SimOptions;

        /// <summary>Whether WHOLE MATCH is offered (a two-innings match, still in its first innings).</summary>
        public Func<bool>? SimMatchOption;

        private HudChip? _simChip;

        /// <summary>The MATCH page's SIMULATE heading and row, from <paramref name="y"/> (top-anchored) down.</summary>
        private void SimRow(RectTransform panel, float y)
        {
            var o = SimOptions?.Invoke();
            if (o == null || !(o.Value.over || o.Value.innings || o.Value.key)) return;
            var items = new System.Collections.Generic.List<(string label, int kind)>();
            if (o.Value.over) items.Add(("THIS\nOVER", 0));
            if (o.Value.key) items.Add(("TO LAST\n2 OVERS", 2));
            if (o.Value.innings) items.Add(("THIS\nINNINGS", 1));
            if (o.Value.innings && SimMatchOption != null && SimMatchOption()) items.Add(("WHOLE\nMATCH", 3));
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(80, y - 30), new Vector2(-80, y + 40), "SIMULATE", 34, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            y -= 120f;
            const float rowW = 1080f, gap = 20f;
            var w = (rowW - gap * (items.Count - 1)) / items.Count;
            for (var i = 0; i < items.Count; i++)
            {
                var (label, kind) = items[i];
                PanelKit.Pill(panel, "Sim_" + kind, new Vector2(0.5f, 1), new Vector2(-rowW / 2 + w / 2 + i * (w + gap), y), new Vector2(w, 130), label, new Color(0.15f, 0.42f, 0.6f, 0.97f), () =>
                {
                    SetPaused(false);
                    SimPressed?.Invoke(kind);
                }, 28);
            }
        }

        /// <summary>The over break's SIM OVER chip (bottom right, above the score bar), on or off.</summary>
        public void ShowSimChip(bool on)
        {
            if (on && _simChip == null)
            {
                _simChip = HudChip.Create(_root, "SimOver", new Vector2(1, 0), new Vector2(-190, 250), new Vector2(300, 84), "SIM NEXT OVER ▸", UiKit.Gold, 26, () =>
                {
                    SimPressed?.Invoke(0);
                    ShowSimChip(false);
                });
            }
            if (_simChip != null) _simChip.gameObject.SetActive(on);
        }
    }
}
