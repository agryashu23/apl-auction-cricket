using System;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The batting controls and the timing meter. On a phone every shot is one
    /// touch, read on touch-down (a Button fires on lift, ~0.1 s late), on HUD
    /// chips (<see cref="HudChip"/>): with AUTO footwork (the default) PUSH /
    /// STROKE / LOFT play that shot at once, the foot read from the length; with
    /// MANUAL footwork the type is picked first and FRONT FOOT / BACK FOOT play
    /// it; LEAVE and SPECIAL either way. LEGACY SWIPE (a setting) drops the stick
    /// and the foot buttons: the type is picked on three chips and a swipe toward
    /// where he wants to hit is the shot. The aim (stick or swipe) is drawn on the
    /// field radar (gold: held, faint: AUTO's choice for this ball; white after
    /// the shot: where it actually went). The meter's zones are the engine's grades.
    /// </summary>
    public sealed partial class MatchHud
    {
        private RectTransform _autoShots = null!, _manualShots = null!, _swipeShots = null!;

        /// <summary>Footwork before the ball: 0 charge, −1 / +1 a step to the screen's left / right.</summary>
        public event Action<int>? FootworkPressed;

        /// <summary>Swipe mode: offered every swipe first; true if the director took it as footwork (before release).</summary>
        public Func<Vector2, bool>? SwipeFootwork;

        private static readonly Color FootColor = new Color(0.95f, 0.85f, 0.35f);
        private SwipePad _swipePad = null!;
        private Text _swipeHint = null!;
        private readonly System.Collections.Generic.List<(ShotType type, HudChip chip)> _modeChips = new System.Collections.Generic.List<(ShotType, HudChip)>();
        private RectTransform _aimRay = null!, _shotRay = null!, _perfectZone = null!, _earlyZone = null!;
        private Delivery? _delivery;
        private TimingStamp _stamp = null!;
        private readonly System.Collections.Generic.List<Text> _timingLabels = new System.Collections.Generic.List<Text>();

        /// <summary>The chips' colours: each shot type keeps its colour on every layout.</summary>
        private static readonly Color PushColor = new Color(0.35f, 0.6f, 1f);
        private static readonly Color StrokeColor = new Color(0.3f, 0.85f, 0.45f);
        private static readonly Color LoftColor = new Color(0.98f, 0.55f, 0.2f);
        private static readonly Color SpecialColor = new Color(0.72f, 0.48f, 0.95f);
        private static readonly Color LeaveColor = new Color(0.72f, 0.75f, 0.82f);

        /// <summary>
        /// Top right: how the last shot was timed (a marker on EARLY | PERFECT | LATE)
        /// and the ball's pace once it's bowled; down the right edge the live meter —
        /// the needle climbs from release to contact; tap in the green (bright band:
        /// perfect; amber: early / late, still bat on ball).
        /// </summary>
        private void BuildTiming(RectTransform play)
        {
            _result = UiKit.Box(play, "Result", new Vector2(1, 1), new Vector2(-340, -58), new Vector2(440, 26), new Color(0, 0, 0, 0.6f));
            var red = new Color(0.85f, 0.25f, 0.2f);
            var amber = new Color(0.95f, 0.75f, 0.2f);
            var x = 0f;
            // ±3.5 windows across the bar, as the grades: very early | early | good | late | very late.
            foreach (var (w, c) in new[] { (94f, red), (63f, amber), (126f, new Color(0.3f, 0.8f, 0.4f)), (63f, amber), (94f, red) })
            {
                UiKit.Box(_result, "Seg", new Vector2(0, 0.5f), new Vector2(x + w / 2, 0), new Vector2(w - 2, 18), c).GetComponent<Image>().raycastTarget = false;
                x += w;
            }
            UiKit.Box(_result, "Perfect", Centre, Vector2.zero, new Vector2(44, 18), new Color(0.55f, 1f, 0.6f)).GetComponent<Image>().raycastTarget = false;
            _marker = UiKit.Box(_result, "Marker", new Vector2(0, 0.5f), new Vector2(220, 22), new Vector2(15, 15), Color.white);
            _marker.localRotation = Quaternion.Euler(0, 0, 45);
            _marker.gameObject.SetActive(false);
            _timingLabels.Add(UiKit.LabelAt(play, new Vector2(1, 1), new Vector2(-500, -86), new Vector2(120, 22), "EARLY", 15, TextAnchor.MiddleCenter, UiKit.Dim));
            _timingLabels.Add(UiKit.LabelAt(play, new Vector2(1, 1), new Vector2(-340, -86), new Vector2(120, 22), "PERFECT", 15, TextAnchor.MiddleCenter, UiKit.Dim));
            _timingLabels.Add(UiKit.LabelAt(play, new Vector2(1, 1), new Vector2(-180, -86), new Vector2(120, 22), "LATE", 15, TextAnchor.MiddleCenter, UiKit.Dim));
            _grade = UiKit.LabelAt(play, new Vector2(1, 1), new Vector2(-340, -118), new Vector2(560, 36), "", 26, TextAnchor.MiddleCenter, UiKit.Gold);
            _stamp = TimingStamp.Create(play);
            // Round 9 (PM): ONE timing readout -- the stamp over the batter (the banner keeps the shot's name).
            // The top-right EARLY | PERFECT | LATE bar and its grade line read a second number (and sat under
            // SKIP / pause): built still, never shown.
            _result.gameObject.SetActive(false);
            _grade.gameObject.SetActive(false);
            foreach (var l in _timingLabels) l.gameObject.SetActive(false);
            // The ball: pace (real km/h, however fast the flight is shown) or spin, once it's on its way.
            var chip = UiKit.Box(play, "Ball", new Vector2(1, 1), new Vector2(-660, -58), new Vector2(170, 34), new Color(0, 0, 0, 0.6f));
            _ball = UiKit.Label(chip, "", 20, TextAnchor.MiddleCenter, UiKit.Gold);
            // The live meter down the right edge.
            _meter = UiKit.Box(play, "Meter", new Vector2(1, 0.5f), new Vector2(-40, 50), new Vector2(26, MeterHeight), new Color(0, 0, 0, 0.6f));
            _earlyZone = UiKit.Box(_meter, "EarlyLate", new Vector2(0.5f, 0), Vector2.zero, new Vector2(26, 80), new Color(0.95f, 0.75f, 0.2f, 0.45f));
            _window = UiKit.Box(_meter, "Window", new Vector2(0.5f, 0), Vector2.zero, new Vector2(26, 40), new Color(0.3f, 0.9f, 0.4f, 0.85f));
            _perfectZone = UiKit.Box(_meter, "PerfectZone", new Vector2(0.5f, 0), Vector2.zero, new Vector2(26, 14), new Color(0.75f, 1f, 0.75f, 0.95f));
            _needle = UiKit.Box(_meter, "Needle", new Vector2(0.5f, 0), Vector2.zero, new Vector2(48, 6), Color.white);
            foreach (var rt in new[] { _earlyZone, _window, _perfectZone, _needle }) rt.GetComponent<Image>().raycastTarget = false;
        }

        /// <summary>
        /// The three layouts of shot controls (one shown, per the settings) and the radar's
        /// direction rays. Chips, bottom right, in two columns within the right thumb's reach;
        /// positions are reference px from the bottom-right corner.
        /// </summary>
        private void BuildShotControls(RectTransform play)
        {
            var shot = new Vector2(200, 96);
            var side = new Vector2(180, 80);
            var pick = new Vector2(140, 64);
            _autoShots = Group(play, "AutoShots");
            Chip(_autoShots, "Tap_Loft", new Vector2(-230, 470), shot, "LOFT", LoftColor, () => ShotPressed?.Invoke(ShotType.Loft, null), 28);
            Chip(_autoShots, "Tap_Stroke", new Vector2(-230, 355), shot, "STROKE", StrokeColor, () => ShotPressed?.Invoke(ShotType.Stroke, null), 28);
            Chip(_autoShots, "Tap_Push", new Vector2(-230, 240), shot, "PUSH", PushColor, () => ShotPressed?.Invoke(ShotType.Push, null), 28);
            Chip(_autoShots, "Tap_Special", new Vector2(-455, 470), side, "SPECIAL", SpecialColor, () => SpecialPressed?.Invoke(), 22);
            Chip(_autoShots, "Tap_Leave", new Vector2(-455, 240), side, "LEAVE", LeaveColor, () => LeavePressed?.Invoke(), 24);

            _manualShots = Group(play, "ManualShots");
            TypePicker(_manualShots, 600f, pick);
            Chip(_manualShots, "Tap_Back", new Vector2(-230, 355), shot, "BACK FOOT", PushColor, () => ShotPressed?.Invoke(_mode, true), 24);
            Chip(_manualShots, "Tap_Front", new Vector2(-230, 240), shot, "FRONT FOOT", StrokeColor, () => ShotPressed?.Invoke(_mode, false), 24);
            Chip(_manualShots, "Tap_Special", new Vector2(-455, 355), side, "SPECIAL", SpecialColor, () => SpecialPressed?.Invoke(), 22);
            Chip(_manualShots, "Tap_Leave", new Vector2(-455, 240), side, "LEAVE", LeaveColor, () => LeavePressed?.Invoke(), 24);

            // LEGACY SWIPE: swipe where to hit, over most of the screen (not the top strip, the radar
            // corner or the score bar); the type stays picked on the chips; LEAVE is a small chip.
            _swipeShots = Group(play, "SwipeShots");
            _swipePad = SwipePad.Create(_swipeShots, "SwipePad", new Vector2(0.24f, 0.11f), new Vector2(0.95f, 0.84f));
            _swipePad.Swiped += OnSwipe;
            TypePicker(_swipeShots, 170f, pick);
            Chip(_swipeShots, "Tap_Leave", new Vector2(-110, 170), new Vector2(130, 64), "LEAVE", LeaveColor, () => LeavePressed?.Invoke(), 22);
            _swipeHint = UiKit.LabelAt(_swipeShots, new Vector2(1, 0), new Vector2(-405, 225), new Vector2(460, 28), "SWIPE TO PLAY", 18, TextAnchor.MiddleCenter, UiKit.Dim);
            _swipeHint.raycastTarget = false;

            // Footwork before the ball (buttons layouts): ◄ CHARGE ► in a row over the shot chips.
            foreach (var group in new[] { _autoShots, _manualShots })
            {
                Chip(group, "Foot_Left", new Vector2(-560, 690), new Vector2(96, 64), "◄", FootColor, () => FootworkPressed?.Invoke(-1), 26);
                Chip(group, "Foot_Charge", new Vector2(-405, 690), new Vector2(190, 64), "CHARGE", FootColor, () => FootworkPressed?.Invoke(0), 22);
                Chip(group, "Foot_Right", new Vector2(-250, 690), new Vector2(96, 64), "►", FootColor, () => FootworkPressed?.Invoke(1), 26);
            }
            _swipeHint.text = "SWIPE TO PLAY · BEFORE RELEASE: ↑ CHARGE  ◄ ► ACROSS";

            SetMode(ShotType.Stroke);
            // On the radar: where the stick (or swipe) is aiming, or AUTO's pick; and where the shot went.
            _shotRay = Ray("ShotRay", new Color(1f, 1f, 1f, 0.95f), 4f);
            _aimRay = Ray("AimRay", UiKit.Gold, 6f);
            ApplyFootwork();
        }

        private RectTransform Group(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static HudChip Chip(RectTransform parent, string name, Vector2 pos, Vector2 size, string label, Color color, Action onPress, int fontSize) =>
            HudChip.Create(parent, name, new Vector2(1, 0), pos, size, label, color, fontSize, onPress);

        /// <summary>PUSH / STROKE / LOFT as a selector row at height <paramref name="y"/> (it stays picked between balls).</summary>
        private void TypePicker(RectTransform parent, float y, Vector2 size)
        {
            foreach (var (type, x, label, color) in new[] { (ShotType.Push, -555f, "PUSH", PushColor), (ShotType.Stroke, -405f, "STROKE", StrokeColor), (ShotType.Loft, -255f, "LOFT", LoftColor) })
            {
                var t = type;
                _modeChips.Add((type, Chip(parent, "Mode_" + type, new Vector2(x, y), size, label, color, () => SetMode(t), 22)));
            }
        }

        /// <summary>The shot type the MANUAL taps and the swipes play.</summary>
        public ShotType Mode => _mode;

        private void SetMode(ShotType type)
        {
            _mode = type;
            foreach (var (t, chip) in _modeChips) chip.Selected = t == type;
            ShowAim();
        }

        /// <summary>Show the layout for the settings (swipe, or the buttons with AUTO / MANUAL footwork).</summary>
        private void ApplyFootwork()
        {
            var swipe = BattingPrefs.Swipe;
            var auto = BattingPrefs.AutoFootwork;
            _autoShots.gameObject.SetActive(!swipe && auto);
            _manualShots.gameObject.SetActive(!swipe && !auto);
            _swipeShots.gameObject.SetActive(swipe);
            // No stick in swipe mode (the swipe aims); the aim it last set clears with it.
            var stick = _play.transform.Find("AimStick");
            if (stick != null) stick.gameObject.SetActive(!swipe);
            _aimLabel.gameObject.SetActive(!swipe);
            SetAim((Vector2?)null);
        }

        /// <summary>The foot for a keyboard tap: AUTO footwork (and swipe) read it from the length.</summary>
        private static bool? KeyFoot(bool back) => BattingPrefs.AutoFootwork || BattingPrefs.Swipe ? (bool?)null : back;

        /// <summary>
        /// A swipe: its direction is the aim — screen-up is straight back past the bowler,
        /// sideways as the radar has it (a right-hander's off side to the left, mirrored for a
        /// left-hander), down is behind square — and the swipe itself is the shot, now.
        /// </summary>
        private void OnSwipe(Vector2 dir)
        {
            // Before release a swipe is footwork (up: charge; sideways: across), if the director takes it.
            if (SwipeFootwork?.Invoke(dir) == true) return;
            var angle = Mathf.Atan2((_leftHanded ? 1 : -1) * dir.x, dir.y) * Mathf.Rad2Deg;
            Aim = NearestZone(angle);
            _aimLabel.text = "AIM · " + Label(Aim.Value);
            ShowAim();
            ShotPressed?.Invoke(_mode, null);
        }

        private static Zone NearestZone(float angle)
        {
            var best = Zone.StraightOff;
            var bestDiff = 999.0;
            foreach (Zone z in Enum.GetValues(typeof(Zone)))
            {
                var diff = Math.Abs(Mathf.DeltaAngle(angle, (float)Field.ZoneAngle(z)));
                if (diff >= bestDiff) continue;
                bestDiff = diff;
                best = z;
            }
            return best;
        }

        // ------------------------------------------------------------------ aim

        /// <summary>
        /// The stick's direction as the field looks from behind the bowler (down =
        /// straight, left = this batter's off side) to the nearest scoring zone;
        /// let go or barely pushed = AUTO.
        /// </summary>
        private void SetAim(Vector2? stick)
        {
            if (stick == null || stick.Value.magnitude < 0.3f)
            {
                Aim = null;
                _aimLabel.text = "AIM · AUTO";
                ShowAim();
                return;
            }
            var v = stick.Value;
            var angle = Mathf.Atan2((_leftHanded ? 1 : -1) * v.x, -v.y) * Mathf.Rad2Deg;
            Zone best = Zone.StraightOff;
            var bestDiff = 999.0;
            foreach (Zone z in Enum.GetValues(typeof(Zone)))
            {
                var diff = Math.Abs(Mathf.DeltaAngle(angle, (float)Field.ZoneAngle(z)));
                if (diff >= bestDiff) continue;
                bestDiff = diff;
                best = z;
            }
            Aim = best;
            _aimLabel.text = "AIM · " + Label(best);
            ShowAim();
        }

        private static string Label(Zone z) => z switch
        {
            Zone.StraightOff => "LONG OFF",
            Zone.Cover => "COVER",
            Zone.Point => "POINT",
            Zone.ThirdMan => "3RD MAN",
            Zone.FineLeg => "FINE LEG",
            Zone.Square => "SQUARE",
            Zone.MidWicket => "MID-WKT",
            _ => "LONG ON",
        };

        /// <summary>A ray from the batter's dot on the radar.</summary>
        private RectTransform Ray(string name, Color color, float width)
        {
            var rt = UiKit.Box(_radar, name, Centre, Vector2.zero, new Vector2(width, RopePx), color);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.GetComponent<Image>().raycastTarget = false;
            rt.gameObject.SetActive(false);
            return rt;
        }

        /// <summary>Point a radar ray at <paramref name="angleDeg"/> (engine angle: 0 straight, + off side), <paramref name="length"/> of the rope.</summary>
        private void PointRay(RectTransform ray, double angleDeg, float length)
        {
            // The radar has straight down the ground at the bottom, the off side left for a right-hander.
            var sign = _leftHanded ? 1f : -1f;
            var a = (float)(angleDeg * Math.PI / 180);
            var dir = new Vector2(sign * Mathf.Sin(a), -Mathf.Cos(a));
            ray.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(-dir.x, dir.y) * Mathf.Rad2Deg);
            ray.sizeDelta = new Vector2(ray.sizeDelta.x, RopePx * Mathf.Clamp(length, 0.15f, 1f));
            ray.gameObject.SetActive(true);
        }

        /// <summary>The aim on the radar: the held zone in gold; on AUTO, faintly, the zone AUTO would pick for this ball.</summary>
        private void ShowAim()
        {
            if (_aimRay == null) return;
            var img = _aimRay.GetComponent<Image>();
            if (Aim is Zone z)
            {
                img.color = UiKit.Gold;
                PointRay(_aimRay, Field.ZoneAngle(z), 0.9f);
                return;
            }
            if (_delivery == null)
            {
                _aimRay.gameObject.SetActive(false);
                return;
            }
            img.color = UiKit.WithAlpha(UiKit.Gold, 0.4f);
            var type = BattingPrefs.AutoFootwork ? ShotType.Stroke : _mode;
            PointRay(_aimRay, Field.ZoneAngle(MatchDirector.BestZone(_delivery, type)), 0.75f);
        }

        /// <summary>After the shot: where it went (white ray to where it finished); null clears it.</summary>
        public void ShowShot(BallResult? r)
        {
            if (_shotRay == null) return;
            if (r == null || r.ExitSpeed <= 0 || r.Wide)
            {
                _shotRay.gameObject.SetActive(false);
                return;
            }
            var (x, f) = r.End;
            var reach = r.Six || r.Four ? 1f : (float)(Math.Sqrt(x * x + f * f) / 70.0);
            PointRay(_shotRay, r.ExitAngle, reach);
        }

        // ---------------------------------------------------------------- meter

        /// <summary>
        /// The meter for this ball: where the good window sits (0–1 of the flight to
        /// contact) and its half-width; the perfect band (±0.35) and the early / late
        /// band (±2) are the engine's own grade lines, so what's green is what scores.
        /// </summary>
        public void ShowMeter(float windowCentre, float windowHalf)
        {
            var h = _meter.sizeDelta.y;
            void Band(RectTransform rt, float half, float min)
            {
                var size = Mathf.Max(min, half * 2 * h);
                // Anchored at the bottom, pivot in the middle: centred on the ideal tap.
                rt.anchoredPosition = new Vector2(0, windowCentre * h);
                rt.sizeDelta = new Vector2(26, size);
            }
            Band(_earlyZone, windowHalf * 2f, 20);
            Band(_window, windowHalf, 10);
            Band(_perfectZone, windowHalf * 0.35f, 4);
            _needle.anchoredPosition = Vector2.zero;
            _grade.text = "";
            _stamp.Clear();
            _marker.gameObject.SetActive(false);
            _meterCentre = windowCentre;
            _meterHalf = windowHalf;
            MoveNeedle(0f);
        }

        private float _meterCentre, _meterHalf;
        private static readonly Color WindowIdle = new Color(0.3f, 0.9f, 0.4f, 0.55f), WindowLive = new Color(0.4f, 1f, 0.5f, 1f);
        private static readonly Color PerfectIdle = new Color(0.75f, 1f, 0.75f, 0.6f), PerfectLive = new Color(1f, 0.95f, 0.5f, 1f);

        /// <summary>
        /// The needle (0–1 of the flight to contact). The green lights up while a tap now
        /// would be GOOD, and the perfect band flashes gold — with a "NOW" pulse of the
        /// needle — at the perfect moment, the same moment the ring closes on the ball.
        /// </summary>
        public void MoveNeedle(float progress)
        {
            var h = _meter.sizeDelta.y;
            _needle.anchoredPosition = new Vector2(0, Mathf.Clamp01(progress) * h);
            var off = Mathf.Abs(progress - _meterCentre);
            var good = _meterHalf > 0 && off <= _meterHalf;
            var perfect = _meterHalf > 0 && off <= _meterHalf * 0.35f;
            _window.GetComponent<Image>().color = good ? WindowLive : WindowIdle;
            _perfectZone.GetComponent<Image>().color = perfect ? PerfectLive : PerfectIdle;
            _needle.sizeDelta = new Vector2(perfect ? 64 : good ? 54 : 48, perfect ? 10 : 6);
            _needle.GetComponent<Image>().color = perfect ? PerfectLive : Color.white;
        }

        /// <summary>What's coming: pace in km/h (the real speed) or spin (blank between balls).</summary>
        public void ShowBall(Delivery? d)
        {
            _ball.text = d == null ? "" : d.IsSpin ? "SPIN" : $"{d.SpeedKmh:0} km/h";
            _delivery = d;
            ShowAim();
        }

        /// <summary>
        /// The grade, the marker on EARLY | PERFECT | LATE, and how far off the tap was in real ms (<paramref name="pace"/>:
        /// trajectory s per real s); and the same grade stamped big over the batter (<paramref name="batter"/>: where he stands).
        /// </summary>
        public void ShowGrade(BallResult r, double pace = 1, Vector3? batter = null)
        {
            ShowGrade(r.Timing);
            if (r.Timing == Timing.Leave || r.Wide) return;
            // Owner (final rating): no ms number anywhere — the grade, which way a GOOD tap leaned, and where it went
            // ("GOOD" / "LATE  →  COVER"; "PERFECT" / "→  MID-OFF"). EARLY / LATE grades already say which way.
            var ms = (int)Math.Round(r.TimingError / Math.Max(0.1, pace) * 1000);
            var lean = r.Timing == Timing.Good && ms != 0 ? (ms < 0 ? "EARLY" : "LATE") : "";
            var word = _grade.text;
            _stamp.Anchor = batter;
            var dir = r.ExitSpeed > 0 && r.Timing != Timing.Miss ? "→  " + Region(r.ExitAngle) : "";
            var detail = lean.Length > 0 && dir.Length > 0 ? lean + "  " + dir : lean + dir;
            _stamp.Show(word, r.Timing == Timing.Miss ? "" : detail, TimingStamp.ColorOf(word));
            if (lean.Length > 0) _grade.text += "  ·  " + lean;
            if (r.Window <= 0) return;
            // ±3.5 windows across the bar (the grade thresholds: perfect 0.35, good 1, early/late 2, very 3.5).
            var e = Mathf.Clamp((float)(r.TimingError / r.Window), -3.5f, 3.5f);
            _marker.anchoredPosition = new Vector2((0.5f + e / 7f) * _result.sizeDelta.x, 22);
            _marker.gameObject.SetActive(true);
        }

        /// <summary>
        /// The shot controls (chips, swipe pad, aim stick) on or off: off from contact until the next
        /// ball, so they don't sit over the batter in the side views while the ball's in the field
        /// (round 8 rating); the RUN controls are separate and stay.
        /// </summary>
        public void ShotControls(bool on)
        {
            var stick = _play != null ? _play.transform.Find("AimStick") : null;
            foreach (var t in new[] { _autoShots, _manualShots, _swipeShots, stick as RectTransform })
            {
                if (t == null) continue;
                var g = t.GetComponent<CanvasGroup>();
                if (g == null) g = t.gameObject.AddComponent<CanvasGroup>();
                g.alpha = on ? 1f : 0f;
                g.blocksRaycasts = on;
                g.interactable = on;
            }
        }

        /// <summary>Where the ball went, by name (engine exit angle: 0 straight past the bowler, + off side).</summary>
        private static string Region(double angle)
        {
            var a = Math.Abs(angle);
            var off = angle >= 0;
            return a < 15 ? "STRAIGHT" : a < 40 ? (off ? "MID-OFF" : "MID-ON") : a < 72 ? (off ? "COVER" : "MIDWICKET")
                : a < 110 ? (off ? "POINT" : "SQUARE LEG") : (off ? "THIRD MAN" : "FINE LEG");
        }

        public void ShowGrade(Timing t) => _grade.text = t switch
        {
            Timing.Perfect => "PERFECT",
            Timing.Good => "GOOD",
            Timing.Early => "EARLY",
            Timing.Late => "LATE",
            Timing.VeryEarly => "TOO EARLY",
            Timing.VeryLate => "TOO LATE",
            Timing.Miss => "MISSED",
            _ => "LEFT",
        };

        // ------------------------------------------------------------- settings

        /// <summary>The old pause panel's batting rows (unused since the round-7 pause menu): CONTROLS, FOOTWORK, RUNNING, HAPTICS.</summary>
        private void BattingSettings(RectTransform panel)
        {
            const float grow = 400f;
            panel.sizeDelta += new Vector2(0, grow);
            if (panel.parent is RectTransform back && back != _pauseMenu?.transform) back.sizeDelta += new Vector2(0, grow);
            var top = panel.sizeDelta.y;
            // Under the PAUSED title: CONTROLS (buttons / swipe), FOOTWORK.
            var controlsY = top - 150f;
            var footY = top - 310f;
            var runY = top - 390f;
            var buzzY = top - 470f;
            UiKit.LabelAt(panel, new Vector2(0.5f, 0), new Vector2(-200, controlsY), new Vector2(200, 40), "CONTROLS", 22, TextAnchor.MiddleLeft, UiKit.Dim);
            Button buttonsBtn = null!, swipeBtn = null!;
            void PaintControls()
            {
                Lit(buttonsBtn, !BattingPrefs.Swipe);
                Lit(swipeBtn, BattingPrefs.Swipe);
                ApplyFootwork();
            }
            buttonsBtn = UiKit.Btn(panel, "Controls_Buttons", new Vector2(0.5f, 0), new Vector2(40, controlsY), new Vector2(170, 60), "BUTTONS", UiKit.Muted, () =>
            {
                BattingPrefs.Swipe = false;
                PaintControls();
            }, 20);
            swipeBtn = UiKit.Btn(panel, "Controls_Swipe", new Vector2(0.5f, 0), new Vector2(220, controlsY), new Vector2(170, 60), "SWIPE", UiKit.Muted, () =>
            {
                BattingPrefs.Swipe = true;
                PaintControls();
            }, 20);
            PaintControls();
            // BALL SPEED is gone (owner v26 #5): DIFFICULTY sets it (BattingPrefs.Pace).
            UiKit.LabelAt(panel, new Vector2(0.5f, 0), new Vector2(-200, footY), new Vector2(200, 40), "FOOTWORK", 22, TextAnchor.MiddleLeft, UiKit.Dim);
            Button autoBtn = null!, manualBtn = null!;
            void PaintFoot()
            {
                Lit(autoBtn, BattingPrefs.AutoFootwork);
                Lit(manualBtn, !BattingPrefs.AutoFootwork);
                ApplyFootwork();
            }
            autoBtn = UiKit.Btn(panel, "Foot_Auto", new Vector2(0.5f, 0), new Vector2(40, footY), new Vector2(170, 60), "AUTO", UiKit.Muted, () =>
            {
                BattingPrefs.AutoFootwork = true;
                PaintFoot();
            }, 20);
            manualBtn = UiKit.Btn(panel, "Foot_Manual", new Vector2(0.5f, 0), new Vector2(220, footY), new Vector2(170, 60), "MANUAL", UiKit.Muted, () =>
            {
                BattingPrefs.AutoFootwork = false;
                PaintFoot();
            }, 20);
            PaintFoot();
            // RUNNING (rules-bowling): MANUAL — RUN / CANCEL / DIVE — or AUTO, the safe runs by themselves.
            UiKit.LabelAt(panel, new Vector2(0.5f, 0), new Vector2(-200, runY), new Vector2(200, 40), "RUNNING", 22, TextAnchor.MiddleLeft, UiKit.Dim);
            Button runManual = null!, runAuto = null!;
            void PaintRun()
            {
                Lit(runManual, !RunningPrefs.Auto);
                Lit(runAuto, RunningPrefs.Auto);
            }
            runManual = UiKit.Btn(panel, "Run_Manual", new Vector2(0.5f, 0), new Vector2(40, runY), new Vector2(170, 60), "MANUAL", UiKit.Muted, () =>
            {
                RunningPrefs.Auto = false;
                PaintRun();
            }, 20);
            runAuto = UiKit.Btn(panel, "Run_Auto", new Vector2(0.5f, 0), new Vector2(220, runY), new Vector2(170, 60), "AUTO", UiKit.Muted, () =>
            {
                RunningPrefs.Auto = true;
                PaintRun();
            }, 20);
            PaintRun();
            // HAPTICS: a buzz on hits (by timing), wickets and sixes.
            UiKit.LabelAt(panel, new Vector2(0.5f, 0), new Vector2(-200, buzzY), new Vector2(200, 40), "HAPTICS", 22, TextAnchor.MiddleLeft, UiKit.Dim);
            Button buzzOn = null!, buzzOff = null!;
            void PaintBuzz()
            {
                Lit(buzzOn, BattingPrefs.Haptics);
                Lit(buzzOff, !BattingPrefs.Haptics);
            }
            buzzOn = UiKit.Btn(panel, "Haptics_On", new Vector2(0.5f, 0), new Vector2(40, buzzY), new Vector2(170, 60), "ON", UiKit.Muted, () =>
            {
                BattingPrefs.Haptics = true;
                PaintBuzz();
                Haptics.Pulse(Haptics.GoodMs, 170);
            }, 20);
            buzzOff = UiKit.Btn(panel, "Haptics_Off", new Vector2(0.5f, 0), new Vector2(220, buzzY), new Vector2(170, 60), "OFF", UiKit.Muted, () =>
            {
                BattingPrefs.Haptics = false;
                PaintBuzz();
            }, 20);
            PaintBuzz();
        }

        private static void Lit(Button b, bool on)
        {
            b.GetComponent<Image>().color = on ? UiKit.Gold : UiKit.Muted;
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.color = on ? UiKit.Ink : Color.white;
        }
    }
}
