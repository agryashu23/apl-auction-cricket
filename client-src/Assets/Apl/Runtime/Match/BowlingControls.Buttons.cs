using System.Collections.Generic;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>The stick (bottom left), the ball types on the delivery wheel (each one bowls it: BowlingControls.Wheel.cs), FIELD, RUN HIM OUT, the hint.</summary>
    public sealed partial class BowlingControls
    {
        private RectTransform _typesBox = null!;
        private readonly List<(Variation v, WheelSlot chip)> _types = new List<(Variation, WheelSlot)>();

        /// <summary>A ball type has been deliberately picked for this ball (none is pre-selected: owner v26 #15).</summary>
        private bool _picked;
        private Text _hint = null!, _ballLabel = null!, _stickLabel = null!;
        private HudChip _field = null!, _mankad = null!;
        private GameObject _stickGo = null!;
        private bool _mankadOn;
        private Player? _bowler;

        private static readonly Color TypeColor = new Color(0.98f, 0.62f, 0.2f);

        private void BuildButtons()
        {
            var at = new Vector2(200, 260);
            var stick = UiKit.Box(_root, "AimStick", new Vector2(0, 0), at, new Vector2(260, 260), new Color(0.05f, 0.25f, 0.08f, 0.5f));
            stick.GetComponent<Image>().sprite = MatchHud.DiscSprite;
            // The knob in the marker's blue (owner v26 #15: one plain blue for the aim).
            var knob = UiKit.Box(stick, "Knob", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96, 96), new Color(0.2f, 0.55f, 1f));
            knob.GetComponent<Image>().sprite = MatchHud.DiscSprite;
            knob.GetComponent<Image>().raycastTarget = false;
            var js = stick.gameObject.AddComponent<Joystick>();
            js.Knob = knob;
            js.Radius = 86f;
            js.Changed += v => _stick = v;
            _stickGo = stick.gameObject;
            _stickLabel = UiKit.LabelAt(_root, new Vector2(0, 0), at + new Vector2(0, 160), new Vector2(300, 30), "MOVE THE MARKER", 20, TextAnchor.MiddleCenter, Color.white);
            _stickLabel.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);

            // The delivery wheel, bottom right (its centre clear above RUN HIM OUT; BowlingControls.Wheel.cs).
            _typesBox = UiKit.Box(_root, "BallTypes", new Vector2(1, 0), new Vector2(-240, 400), new Vector2(420, 420), new Color(0, 0, 0, 0));
            _typesBox.GetComponent<Image>().raycastTarget = false;
            BuildWheel();
            _ballLabel = UiKit.LabelAt(_root, new Vector2(1, 0), new Vector2(-240, 625), new Vector2(460, 34), "", 22, TextAnchor.MiddleCenter, UiKit.Gold);
            _ballLabel.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);
            // Above the score bar and the speed pill (which come up after the release).
            _hint = UiKit.LabelAt(_root, new Vector2(0.5f, 0), new Vector2(0, 205), new Vector2(900, 40), "", 26, TextAnchor.MiddleCenter, Color.white);
            _hint.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.85f);
            // Over or round the wicket: the toggle card at the left centre (BowlingControls.Side.cs).
            BuildSide();
            _field = HudChip.Create(_root, "Field", new Vector2(0, 1), new Vector2(122, -260), new Vector2(210, 64), "FIELD", UiKit.Gold, 24, () => FieldPressed?.Invoke());
            _mankad = HudChip.Create(_root, "Mankad", new Vector2(1, 0), new Vector2(-250, 160), new Vector2(300, 90), "RUN HIM OUT", UiKit.Bad, 26, () =>
            {
                if (_mankadOn && Now == Phase.RunUp) MankadPressed = true;
            });
            _mankad.gameObject.SetActive(false);
            UiKit.Btn(_root, "Pause", new Vector2(1, 1), new Vector2(-54, -54), new Vector2(72, 72), "II", new Color(0.04f, 0.05f, 0.1f, 0.8f), () => _director.PauseFromControls(), 30);
        }

        /// <summary>This over's bowler: a wheel button per ball he can bowl — tapping one bowls it.</summary>
        public void SetBowler(Player bowler)
        {
            if (_bowler == bowler) return;
            _bowler = bowler;
            FillWheel(bowler, Bowling.Variations(bowler));
            // "Right-arm express" already names the pace; a spinner's style doesn't.
            var style = Bowling.StyleName(bowler);
            var pace = Bowling.PaceName(Bowling.PaceOf(bowler));
            _ballLabel.text = (style.IndexOf(pace, System.StringComparison.OrdinalIgnoreCase) >= 0 ? style : $"{style}  ·  {pace}").ToUpperInvariant();
            // Nothing lit: no default ball (owner v26 #15).
            Highlight(null);
            RefreshSide();
        }

        /// <summary>A ball type tapped while aiming: that's the ball, and he runs in.</summary>
        private void Pick(Variation v)
        {
            if (Now != Phase.Aim) return;
            Variation = v;
            _picked = true;
            Highlight(v);
            HubText();
            Debug.Log($"[APL bowling] delivery picked: {v} (aim x {Mark.x:0.00}, z {Mark.y:0.00})");
            SetPhase(Phase.RunUp);
        }

        /// <summary>Test runs: bowl this type (as if its chip were tapped).</summary>
        public void AutoPick(Variation v) => Pick(v);

        private void Highlight(Variation? v)
        {
            foreach (var (t, chip) in _types) chip.Selected = v != null && t == v.Value;
        }

        /// <summary>A new ball: no type chosen, nothing lit, the hub asks for one.</summary>
        private void ClearPick()
        {
            _picked = false;
            Variation = Variation.Stock;
            Highlight(null);
            HubText();
        }

        /// <summary>
        /// Once he's running in, the chosen type stays lit as SELECTED and the others go dim and dead
        /// (the owner: keep the choice on screen, don't hide it); all live again for the next ball.
        /// </summary>
        private void LockTypes(bool locked)
        {
            foreach (var (t, chip) in _types)
            {
                var g = chip.GetComponent<CanvasGroup>() ?? chip.gameObject.AddComponent<CanvasGroup>();
                var chosen = t == Variation;
                g.alpha = locked && !chosen ? 0.3f : 1f;
                g.interactable = g.blocksRaycasts = !locked;
            }
        }

        /// <summary>RUN HIM OUT on offer this run-up (the non-striker's leaving too early).</summary>
        public void SetMankad(bool on)
        {
            _mankadOn = on;
            _mankad.gameObject.SetActive(on && Now == Phase.RunUp);
        }

        /// <summary>What's on screen in each phase.</summary>
        private void SetPhase(Phase p)
        {
            Now = p;
            // The types stay up until the ball's bowled: live while aiming, then the choice locked in.
            _typesBox.gameObject.SetActive(p != Phase.Done);
            LockTypes(p != Phase.Aim);
            _field.gameObject.SetActive(p == Phase.Aim);
            ShowSide(p == Phase.Aim);
            // Safe zones: the stick and its label only while it steers (the shot banner and the
            // speed pill take the bottom left after the release); the bowler's style only with the chips.
            var steering = p == Phase.Aim || p == Phase.RunUp;
            _stickGo.SetActive(steering);
            _stickLabel.gameObject.SetActive(steering);
            _ballLabel.gameObject.SetActive(p == Phase.Aim);
            _mankad.gameObject.SetActive(_mankadOn && p == Phase.RunUp);
            if (p == Phase.Aim) _stick = null;
            // No floating instructions over the play (the owner: they drew on top of things).
            _hint.text = "";
            ShowMeterPhase(p == Phase.Meter);
        }
    }
}
