using System;
using System.Collections.Generic;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The delivery wheel (owner v26 #16: the 2-column chip grid was "a bad view"): the bowler's ball types
    /// on a ring of round buttons, bottom right, with a hub in the middle that says PICK A BALL until one is
    /// chosen and then names it, and the over's bouncer count for a pace bowler (two allowed, owner v26 #18).
    /// Nothing is pre-selected (owner v26 #15: a stray tap bowled a STOCK ball by default) — a ball type is
    /// bowled only when its button is deliberately tapped (pressed AND released on it).
    /// </summary>
    public sealed partial class BowlingControls
    {
        /// <summary>The ring's radius and a button's size (canvas units, 1920 x 1080 reference).</summary>
        private const float WheelRadius = 128f, SlotSize = 118f, HubSize = 108f;

        private Text? _hubLabel, _hubBouncers;
        private int _bouncersUsed;

        /// <summary>The ring behind the buttons and the hub (built once with the controls).</summary>
        private void BuildWheel()
        {
            var ring = UiKit.Box(_typesBox, "Ring", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(WheelRadius * 2 + SlotSize + 16, WheelRadius * 2 + SlotSize + 16), new Color(0.03f, 0.05f, 0.1f, 0.28f));
            ring.GetComponent<Image>().sprite = MatchHud.DiscSprite;
            ring.GetComponent<Image>().raycastTarget = false;
            var hub = UiKit.Box(_typesBox, "Hub", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(HubSize, HubSize), new Color(0.03f, 0.04f, 0.09f, 0.72f));
            hub.GetComponent<Image>().sprite = MatchHud.DiscSprite;
            hub.GetComponent<Image>().raycastTarget = false;
            _hubLabel = UiKit.LabelAt(hub, new Vector2(0.5f, 0.5f), new Vector2(0, 8), new Vector2(HubSize - 10, 56), "PICK A\nBALL", 17, TextAnchor.MiddleCenter, Color.white);
            _hubLabel.raycastTarget = false;
            _hubBouncers = UiKit.LabelAt(hub, new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(HubSize - 8, 22), "", 13, TextAnchor.MiddleCenter, UiKit.Dim);
            _hubBouncers.raycastTarget = false;
        }

        /// <summary>This bowler's ball types round the ring, the first at the top, clockwise.</summary>
        private void FillWheel(Player bowler, IReadOnlyList<Variation> vars)
        {
            foreach (var (_, old) in _types) Destroy(old.gameObject);
            _types.Clear();
            for (var i = 0; i < vars.Count; i++)
            {
                var v = vars[i];
                var a = (90f - i * 360f / vars.Count) * Mathf.Deg2Rad;
                var at = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * WheelRadius;
                var name = Bowling.VariationName(bowler, v).ToUpperInvariant();
                var slot = WheelSlot.Create(_typesBox, "Ball_" + v, at, SlotSize, name.Replace(' ', '\n'), TypeColor, () => Pick(v));
                _types.Add((v, slot));
            }
            HubText();
        }

        /// <summary>Two-ball allowance for bouncers this over (pace only): the hub shows how many are gone.</summary>
        public void SetBouncers(int used)
        {
            _bouncersUsed = used;
            HubText();
        }

        private void HubText()
        {
            if (_hubLabel == null || _hubBouncers == null) return;
            _hubLabel.text = _picked ? Bowling.VariationName(_bowler!, Variation).ToUpperInvariant().Replace(' ', '\n') : "PICK A\nBALL";
            _hubLabel.color = _picked ? UiKit.Gold : Color.white;
            var pace = _bowler != null && _bowler.BowlType != BowlType.Spin;
            _hubBouncers.text = pace ? $"BOUNCERS {Mathf.Min(_bouncersUsed, QuickMatch.BouncersPerOver)}/{QuickMatch.BouncersPerOver}" : "";
            _hubBouncers.color = _bouncersUsed >= QuickMatch.BouncersPerOver ? UiKit.Bad : UiKit.Dim;
        }
    }
}
