using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// Over / round the wicket: a card at the left centre (the owner moved it off
    /// the top left) with two segments, OVER and ROUND, each with a little picture
    /// from behind the bowler — the three stumps and a dot for which side of them
    /// he runs in (mirrored for a left-armer). The lit segment is this ball's.
    /// Owner v26 #16: bigger (easier to hit) but faded and see-through, so it
    /// doesn't dominate the view.
    /// </summary>
    public sealed partial class BowlingControls
    {
        /// <summary>Round the wicket this ball (the director sets the captain's call; the user may flip it while aiming).</summary>
        public bool Around { get; private set; }

        private RectTransform _sideCard = null!;
        private (Image fill, Text label, RectTransform dot) _over, _round;

        private static readonly Color SegOff = new Color(0.08f, 0.1f, 0.17f, 0.32f);
        private static readonly Color SegOn = new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.5f);

        /// <summary>The whole card's opacity (it's a setting for the ball, not a headline).</summary>
        private const float SideAlpha = 0.78f;

        private void BuildSide()
        {
            _sideCard = UiKit.Box(_root, "SideCard", new Vector2(0, 0.5f), new Vector2(180, 60), new Vector2(330, 200), new Color(UiKit.Panel.r, UiKit.Panel.g, UiKit.Panel.b, 0.28f));
            _sideCard.gameObject.AddComponent<CanvasGroup>().alpha = SideAlpha;
            UiKit.LabelAt(_sideCard, new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(320, 26), "BOWL FROM", 18, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.7f)).raycastTarget = false;
            _over = Segment("OVER", new Vector2(-79, -16), false);
            _round = Segment("ROUND", new Vector2(79, -16), true);
            RefreshSide();
        }

        /// <summary>One half of the toggle: a tap picks it (aiming only).</summary>
        private (Image, Text, RectTransform) Segment(string caption, Vector2 at, bool around)
        {
            var seg = UiKit.Box(_sideCard, caption, new Vector2(0.5f, 0.5f), at, new Vector2(150, 148), SegOff);
            var fill = seg.GetComponent<Image>();
            seg.gameObject.AddComponent<OnPress>().Pressed = () =>
            {
                if (Now == Phase.Aim) SetAround(around);
            };
            // The picture: three stumps and the bowler's side of them.
            for (var i = -1; i <= 1; i++)
            {
                var stump = UiKit.Box(seg, "Stump", new Vector2(0.5f, 0.5f), new Vector2(i * 12, 18), new Vector2(5, 54), new Color(1, 1, 1, 0.85f));
                stump.GetComponent<Image>().raycastTarget = false;
            }
            var dot = UiKit.Box(seg, "Bowler", new Vector2(0.5f, 0.5f), new Vector2(-40, 10), new Vector2(22, 22), UiKit.Gold);
            dot.GetComponent<Image>().sprite = MatchHud.DiscSprite;
            dot.GetComponent<Image>().raycastTarget = false;
            var label = UiKit.LabelAt(seg, new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(146, 30), caption, 24, TextAnchor.MiddleCenter, Color.white);
            label.raycastTarget = false;
            return (fill, label, dot);
        }

        /// <summary>Over / round the wicket for this ball: the lit segment follows.</summary>
        public void SetAround(bool around)
        {
            Around = around;
            RefreshSide();
        }

        private void ShowSide(bool on)
        {
            if (_sideCard != null) _sideCard.gameObject.SetActive(on);
        }

        /// <summary>Lit segment, and the dot on the right side for this bowler's arm.</summary>
        private void RefreshSide()
        {
            if (_sideCard == null) return;
            // From behind: a right-armer comes in left of the stumps over the wicket, right of them round it.
            var left = _bowler != null && Bowling.LeftArm(_bowler);
            foreach (var (seg, around) in new[] { (_over, false), (_round, true) })
            {
                var lit = around == Around;
                seg.fill.color = lit ? SegOn : SegOff;
                seg.label.color = lit ? Color.white : new Color(1, 1, 1, 0.75f);
                var onLeft = around == left;
                seg.dot.anchoredPosition = new Vector2(onLeft ? -40 : 40, 10);
                seg.dot.GetComponent<Image>().color = lit ? UiKit.Ink : UiKit.Gold;
            }
        }
    }
}
