using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The release meter: a bar up the right that fills through the delivery stride, in coloured zones
    /// (round 13, owner): dark for well early (a floated, loose ball), ORANGE either side of the GREEN
    /// (a weaker ball: less pace and movement, a looser line), GREEN with the PERFECT stripe in its middle,
    /// and RED at the top — he's overstepped: a NO-BALL. A tap anywhere releases; the needle stops dead
    /// where the tap landed and stays there. Then the grade on its plate (PERFECT … LATE, NO BALL).
    /// </summary>
    public sealed partial class BowlingControls
    {
        private const float MeterHeight = 360f, MeterWidth = 46f;
        private RectTransform _meter = null!, _needle = null!;
        private RectTransform _early = null!, _orangeEarly = null!, _band = null!, _perfect = null!, _orangeLate = null!, _red = null!;
        private GameObject _catcher = null!;
        private Text _grade = null!, _meterHint = null!;
        /// <summary>The grade's dark plate: read on grass, crowd or sky alike.</summary>
        private RectTransform _gradePlate = null!;
        private float _centre, _half;
        private bool _hintShown;

        private static readonly Color Dark = new Color(0.32f, 0.2f, 0.12f), Orange = new Color(1f, 0.55f, 0.08f),
            Green = new Color(0.15f, 0.85f, 0.3f), Perfect = new Color(0.75f, 1f, 0.75f), Red = new Color(0.95f, 0.12f, 0.12f);

        private void BuildMeter()
        {
            // Under everything else on this canvas: a tap on the stick or a chip isn't a release.
            var catcher = UiKit.Stretch(_root, "TapToRelease", new Color(1, 1, 1, 0.001f));
            catcher.SetAsFirstSibling();
            catcher.gameObject.AddComponent<OnPress>().Pressed = Tap;
            _catcher = catcher.gameObject;
            // A white frame round the bar, so the zones read against any background.
            var frame = UiKit.Box(_root, "ReleaseFrame", new Vector2(1, 0.5f), new Vector2(-60, 175), new Vector2(MeterWidth + 8, MeterHeight + 8), new Color(1, 1, 1, 0.9f));
            frame.GetComponent<Image>().raycastTarget = false;
            _meter = UiKit.Box(frame, "ReleaseMeter", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MeterWidth, MeterHeight), new Color(0.05f, 0.05f, 0.08f, 1f));
            _meter.GetComponent<Image>().raycastTarget = false;
            _early = Zone("Early", Dark);
            _orangeEarly = Zone("OrangeEarly", Orange);
            _band = Zone("Green", Green);
            _perfect = Zone("Perfect", Perfect);
            _orangeLate = Zone("OrangeLate", Orange);
            _red = Zone("NoBall", Red);
            UiKit.LabelAt(_red, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MeterWidth, 20), "NB", 15, TextAnchor.MiddleCenter, Color.white).raycastTarget = false;
            // The needle: a white bar with a dark edge, wider than the meter so it reads over any zone.
            var edge = UiKit.Box(_meter, "NeedleEdge", new Vector2(0.5f, 0), Vector2.zero, new Vector2(MeterWidth + 32, 12), new Color(0, 0, 0, 0.9f));
            UiKit.Box(edge, "Needle", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MeterWidth + 28, 6), Color.white).GetComponent<Image>().raycastTarget = false;
            edge.GetComponent<Image>().raycastTarget = false;
            _needle = edge;
            UiKit.LabelAt(frame, new Vector2(0.5f, 1), new Vector2(0, 26), new Vector2(160, 26), "RELEASE", 18, TextAnchor.MiddleCenter, Color.white);
            _meterHint = UiKit.LabelAt(_root, new Vector2(1, 0.5f), new Vector2(-200, 175), new Vector2(220, 60), "TAP IN THE\nGREEN", 22, TextAnchor.MiddleRight, Green);
            _meterHint.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);
            _meterHint.gameObject.SetActive(false);
            _gradePlate = UiKit.Box(_root, "GradePlate", new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(520, 76), new Color(0.03f, 0.04f, 0.09f, 0.88f));
            _gradePlate.GetComponent<Image>().raycastTarget = false;
            _grade = UiKit.LabelAt(_gradePlate, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 60), "", 44, TextAnchor.MiddleCenter, UiKit.Gold);
            _grade.raycastTarget = false;
            _grade.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);
            _gradePlate.gameObject.SetActive(false);
            frame.gameObject.SetActive(false);
            _meterFrame = frame;
            ShowMeterPhase(false);
        }

        private RectTransform _meterFrame = null!;

        private RectTransform Zone(string name, Color c)
        {
            var z = UiKit.Box(_meter, name, new Vector2(0.5f, 0), Vector2.zero, new Vector2(MeterWidth, 1), c);
            z.GetComponent<Image>().raycastTarget = false;
            return z;
        }

        /// <summary>A zone from release error <paramref name="e0"/> to <paramref name="e1"/> (clamped to the bar).</summary>
        private void Place(RectTransform z, double e0, double e1)
        {
            var a = Mathf.Clamp01(Share(e0));
            var b = Mathf.Clamp01(Share(e1));
            z.anchoredPosition = new Vector2(0, a * MeterHeight);
            z.sizeDelta = new Vector2(MeterWidth, Mathf.Max(0, (b - a) * MeterHeight));
        }

        /// <summary>Where on the bar (0 bottom, 1 top) a release error falls.</summary>
        private float Share(double e) => _centre + (float)e * _half;

        private void ShowMeterPhase(bool on)
        {
            if (_meter == null) return;
            _catcher.SetActive(on);
            if (on) _meterFrame.gameObject.SetActive(true);
            _meterHint.gameObject.SetActive(on && !_hintShown);
            if (on) _hintShown = true;
        }

        /// <summary>
        /// The delivery stride: the bar fills; <paramref name="centre"/> and <paramref name="half"/> (shares of
        /// the bar) are the green's middle and half-width — one unit of release error.
        /// </summary>
        public void StartMeter(float centre, float half)
        {
            _centre = centre;
            _half = half;
            Place(_early, -99, -Bowling.OrangeEdge);
            Place(_orangeEarly, -Bowling.OrangeEdge, -1);
            Place(_band, -1, 1);
            Place(_perfect, -Bowling.PerfectRelease, Bowling.PerfectRelease);
            Place(_orangeLate, 1, Bowling.NoBallRelease);
            Place(_red, Bowling.NoBallRelease, 99);
            _needle.anchoredPosition = Vector2.zero;
            _grade.text = "";
            _gradePlate.gameObject.SetActive(false);
            SetPhase(Phase.Meter);
        }

        /// <summary>The needle climbs — until the tap, where it stops dead and stays (every tap, owner r10/r13).</summary>
        public void MoveNeedle(float share)
        {
            if (!float.IsNaN(TapAt) || Now != Phase.Meter) return;
            _needle.anchoredPosition = new Vector2(0, Mathf.Clamp01(share) * MeterHeight);
        }

        /// <summary>The needle set at the release that was graded (the tap's own spot), and held there.</summary>
        public void FreezeNeedle(double e) => _needle.anchoredPosition = new Vector2(0, Mathf.Clamp01(Share(e)) * MeterHeight);

        /// <summary>The meter's done (released or not); its bar stays a moment with the needle where it stopped.</summary>
        public void EndMeter()
        {
            if (Now == Phase.Meter) SetPhase(Phase.Done);
            _catcher.SetActive(false);
            _meterHint.gameObject.SetActive(false);
        }

        /// <summary>How the release went: the zone the needle stopped in, graded like a shot (red: NO BALL).</summary>
        public void ShowRelease(double e)
        {
            FreezeNeedle(e);
            var g = Bowling.ReleaseGrade(e);
            var noBall = Bowling.ReleaseNoBall(e);
            _grade.text = noBall ? "NO BALL — OVERSTEPPED" : g switch
            {
                Timing.Perfect => "PERFECT RELEASE",
                Timing.Good => "GOOD RELEASE",
                Timing.Early => "EARLY",
                Timing.Late => "LATE",
                Timing.VeryEarly => "TOO EARLY",
                _ => "TOO LATE",
            };
            // Bright grades on the dark plate, in the zone's own colour.
            _grade.color = noBall ? new Color(1f, 0.3f, 0.3f) : g == Timing.Perfect ? new Color(0.45f, 1f, 0.55f) : g == Timing.Good ? new Color(0.3f, 0.95f, 0.45f) : new Color(1f, 0.62f, 0.2f);
            _gradePlate.gameObject.SetActive(true);
            // Read once, then gone (it used to sit over the replays and the wagon wheel).
            _gradeUntil = Time.unscaledTime + 2.2f;
        }

        private float _gradeUntil;

        /// <summary>Between balls: the meter and the grade off the screen.</summary>
        public void ClearMeter()
        {
            _meterFrame.gameObject.SetActive(false);
            _grade.text = "";
            _gradePlate.gameObject.SetActive(false);
        }
    }
}
