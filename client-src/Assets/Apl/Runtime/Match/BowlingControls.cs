using System;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The user's bowling controls: the left stick moves the pitch marker (line
    /// and length), a button per ball type on the delivery wheel picks the ball AND
    /// starts the run-up (no BOWL button, and no default ball: nothing goes until a
    /// type is deliberately tapped); in the delivery stride a meter fills — tap
    /// anywhere in the green for the ball as planned (BowlingControls.Meter.cs).
    /// Between balls: FIELD (the field screen). In the run-up, when the
    /// non-striker's backing up too far: RUN HIM OUT. Keys: arrows move the
    /// marker, 1–7 pick a ball, Space taps the release.
    /// </summary>
    public sealed partial class BowlingControls : MonoBehaviour
    {
        public enum Phase { Aim, RunUp, Meter, Done }

        private RectTransform _root = null!;
        private MatchDirector _director = null!;

        public Phase Now { get; private set; } = Phase.Done;

        /// <summary>This ball's type, and the pitch mark (batter frame: x across, + his off side; z m from his stumps).</summary>
        public Variation Variation { get; private set; } = Variation.Stock;
        public Vector2 Mark { get; private set; } = new Vector2(0.2f, 5.8f);

        /// <summary>The ball type's been picked: the run-up's on. When the release was tapped (Time.time; NaN: not yet).</summary>
        public bool Started => Now >= Phase.RunUp;
        public float TapAt { get; private set; } = float.NaN;

        /// <summary>RUN HIM OUT was pressed in this run-up.</summary>
        public bool MankadPressed { get; private set; }

        public event Action<Vector2>? MarkChanged;
        public event Action? FieldPressed;

        private bool _leftHanded;
        private Vector2? _stick;

        public static BowlingControls Create(MatchDirector director)
        {
            var canvas = UiKit.Canvas("BowlingControls", 12);
            var c = canvas.gameObject.AddComponent<BowlingControls>();
            c._director = director;
            c._root = (RectTransform)canvas.transform;
            c.BuildAim();
            c.BuildButtons();
            c.BuildMeter();
            // Nothing live until the first ball's aim (the stick sat over the intro card's portrait).
            c.SetPhase(Phase.Done);
            c.Hide();
            return c;
        }

        private CanvasGroup? _group;

        /// <summary>Off the screen (and not catching taps) while a replay, wagon wheel or wicket sequence is on.</summary>
        private void LateUpdate()
        {
            if (_group == null) _group = _root.gameObject.AddComponent<CanvasGroup>();
            // Never hidden (or deaf) during the release meter: a broadcast graphic mustn't eat the tap.
            var air = _director.OnAir && Now != Phase.Meter;
            _group.alpha = air ? 0f : 1f;
            _group.blocksRaycasts = !air;
            if (_gradeUntil > 0 && Time.unscaledTime > _gradeUntil)
            {
                _gradeUntil = 0;
                _grade.text = "";
                _gradePlate.gameObject.SetActive(false);
            }
        }

        public void Show() => _root.gameObject.SetActive(true);
        public void Hide() => _root.gameObject.SetActive(false);

        /// <summary>A new ball: aiming again.</summary>
        public void ResetBall()
        {
            TapAt = float.NaN;
            MankadPressed = false;
            _dragFrom = null;
            ClearPick();
            SetPhase(Phase.Aim);
        }

        /// <summary>The run-up started without a pick (test runs only: a real ball needs a type tapped, owner v26 #15).</summary>
        public void StartRunUp() => SetPhase(Phase.RunUp);

        public void SetBatter(bool leftHanded) => _leftHanded = leftHanded;

        /// <summary>Aim at (x across, z from the batter's stumps), kept on a sensible line and length; the marker glides there.</summary>
        public void SetMark(Vector2 mark) => Mark = Clamp(mark);

        private void Update()
        {
            if (!_root.gameObject.activeInHierarchy) return;
            // The stick (or the arrows) through the dead zone and curve (BowlingControls.Aim.cs).
            var v = Vector2.zero;
            if (Now == Phase.Aim || Now == Phase.RunUp)
            {
                v = _stick ?? Vector2.zero;
                if (Input.GetKey(KeyCode.LeftArrow)) v.x = -1;
                if (Input.GetKey(KeyCode.RightArrow)) v.x = 1;
                if (Input.GetKey(KeyCode.UpArrow)) v.y = 1;
                if (Input.GetKey(KeyCode.DownArrow)) v.y = -1;
            }
            AimUpdate(v);
            ShowUpdate();
            if (Now == Phase.Aim)
            {
                for (var i = 0; i < _types.Count && i < 9; i++) if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Pick(_types[i].v);
            }
            // The release is read straight off the input, not through a UI raycast: an overlay on another
            // canvas (or this one hidden for a broadcast moment) used to swallow some taps, and the needle ran on.
            if (Now == Phase.Meter && (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0) || TouchBegan())) Tap();
        }

        private static bool TouchBegan()
        {
            for (var i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) return true;
            return false;
        }

        /// <summary>The release tapped (anywhere on the screen during the meter).</summary>
        private void Tap()
        {
            if (Now != Phase.Meter || !float.IsNaN(TapAt)) return;
            TapAt = Time.time;
            SetPhase(Phase.Done);
        }
    }
}
