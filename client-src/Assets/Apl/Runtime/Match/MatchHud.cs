using System;
using System.Collections.Generic;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The match overlay, laid out like a TV game: a score strip along the
    /// bottom (the batting side and both batters on the left, the score and
    /// chase in the middle, the bowler and this over on the right), the field
    /// radar top left, the timing meter and pause top right, the aim stick bottom
    /// left and the shot controls bottom right, result banners in the middle,
    /// and the cards before and after the chase.
    /// </summary>
    public sealed partial class MatchHud : MonoBehaviour
    {
        /// <summary>A shot: the selected type, and the foot (null: whichever suits the length).</summary>
        public event Action<ShotType, bool?>? ShotPressed;
        public event Action? LeavePressed, SpecialPressed;
        public event Action? StartPressed, PlayAgainPressed, MenuPressed;

        /// <summary>The aim the player is holding on the stick; null = AUTO (best shot for the ball).</summary>
        public Zone? Aim { get; private set; }

        /// <summary>The game is frozen (time scale 0) behind the pause menu.</summary>
        public bool Paused { get; private set; }

        private const float BarHeight = 96f, MeterHeight = 320f;
        private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        private RectTransform _root = null!;
        private Text _score = null!, _chase = null!, _bowler = null!, _over = null!;
        private readonly Text[] _batterName = new Text[2], _batterScore = new Text[2];
        private readonly RawImage[] _batterPic = new RawImage[2];
        private readonly string[] _batterPicId = { "", "" };
        private RawImage _bowlerPic = null!, _batCrest = null!, _bowlCrest = null!;
        private string _bowlerPicId = "", _batCrestId = "", _bowlCrestId = "";
        private Text _banner = null!, _bannerSub = null!, _grade = null!;
        private GameObject _bannerRoot = null!, _play = null!, _card = null!;
        private GameObject? _pauseMenu;
        private RectTransform _meter = null!, _needle = null!, _window = null!, _radar = null!, _result = null!, _marker = null!;
        private Text _ball = null!;
        private ShotType _mode = ShotType.Stroke;
        private readonly List<RectTransform> _dots = new List<RectTransform>();
        private Text _aimLabel = null!, _offLabel = null!, _legLabel = null!;
        private bool _leftHanded;
        private float _bannerUntil;

        public static MatchHud Create()
        {
            var canvas = UiKit.Canvas("MatchHud", 10);
            var hud = canvas.gameObject.AddComponent<MatchHud>();
            hud._root = (RectTransform)canvas.transform;
            hud.Build();
            return hud;
        }

        private void Build()
        {
            BuildScoreBar();

            // The field, top left: where the nine are standing this ball.
            _radar = UiKit.Box(_root, "Radar", new Vector2(0, 1), new Vector2(122, -122), new Vector2(196, 196), new Color(0.05f, 0.25f, 0.08f, 0.62f));
            _radar.GetComponent<Image>().sprite = Disc();
            var ring = UiKit.Box(_radar, "Ring", Centre, Vector2.zero, new Vector2(196, 196), new Color(1, 1, 1, 0.8f));
            ring.GetComponent<Image>().sprite = Ring();
            ring.GetComponent<Image>().raycastTarget = false;
            UiKit.Box(_radar, "Pitch", Centre, new Vector2(0, -13), new Vector2(6, 26), new Color(0.85f, 0.75f, 0.5f, 0.95f));
            var batter = UiKit.Box(_radar, "Batter", Centre, Vector2.zero, new Vector2(11, 11), UiKit.Gold);
            batter.GetComponent<Image>().sprite = Disc();

            _play = new GameObject("PlayControls", typeof(RectTransform)).gameObject;
            var play = (RectTransform)_play.transform;
            play.SetParent(_root, false);
            play.anchorMin = Vector2.zero;
            play.anchorMax = Vector2.one;
            play.offsetMin = play.offsetMax = Vector2.zero;

            BuildTiming(play);

            // Pause, in the corner.
            UiKit.Btn(play, "Pause", new Vector2(1, 1), new Vector2(-54, -54), new Vector2(72, 72), "II", new Color(0.04f, 0.05f, 0.1f, 0.8f), () => SetPaused(true), 30);

            // Aim stick, bottom left: push the knob toward the gap, as the field looks from
            // behind the bowler (straight down the ground is toward you, at the bottom).
            var stickAt = new Vector2(200, BarHeight + 36 + 125);
            var stick = UiKit.Box(play, "AimStick", new Vector2(0, 0), stickAt, new Vector2(250, 250), new Color(0.05f, 0.25f, 0.08f, 0.5f));
            stick.GetComponent<Image>().sprite = Disc();
            var rim = UiKit.Box(stick, "Rim", Centre, Vector2.zero, new Vector2(250, 250), new Color(1, 1, 1, 0.55f));
            rim.GetComponent<Image>().sprite = Ring();
            rim.GetComponent<Image>().raycastTarget = false;
            foreach (var (text, pos) in new[] { ("FINE", new Vector2(0, 98)), ("STRAIGHT", new Vector2(0, -98)) })
            {
                UiKit.LabelAt(stick, Centre, pos, new Vector2(120, 22), text, 14, TextAnchor.MiddleCenter, UiKit.Dim);
            }
            _offLabel = UiKit.LabelAt(stick, Centre, new Vector2(-96, 0), new Vector2(60, 22), "OFF", 14, TextAnchor.MiddleCenter, UiKit.Dim);
            _legLabel = UiKit.LabelAt(stick, Centre, new Vector2(96, 0), new Vector2(60, 22), "LEG", 14, TextAnchor.MiddleCenter, UiKit.Dim);
            var knob = UiKit.Box(stick, "Knob", Centre, Vector2.zero, new Vector2(84, 84), UiKit.Gold);
            knob.GetComponent<Image>().sprite = Disc();
            knob.GetComponent<Image>().raycastTarget = false;
            var js = stick.gameObject.AddComponent<Joystick>();
            js.Knob = knob;
            js.Radius = 82f;
            js.Changed += SetAim;
            _aimLabel = UiKit.LabelAt(play, new Vector2(0, 0), stickAt + new Vector2(0, 150), new Vector2(260, 30), "AIM · AUTO", 20, TextAnchor.MiddleCenter, UiKit.Gold);

            BuildShotControls(play);

            // Result banner, centre.
            var banner = UiKit.Box(_root, "Banner", Centre, new Vector2(0, 150), new Vector2(820, 190), new Color(0.04f, 0.05f, 0.1f, 0.82f));
            _bannerRoot = banner.gameObject;
            _banner = UiKit.Label(UiKit.Strip(banner, 0.42f, 1f), "", 90, TextAnchor.MiddleCenter);
            _bannerSub = UiKit.Label(UiKit.Strip(banner, 0f, 0.42f), "", 30, TextAnchor.MiddleCenter);
            _bannerRoot.SetActive(false);
            SetAim((Vector2?)null);
        }

        /// <summary>The strip along the bottom: batting side | batters | score and chase | bowler and over | bowling side.</summary>
        private void BuildScoreBar()
        {
            var go = new GameObject("ScoreBar", typeof(RectTransform), typeof(Image));
            var bar = (RectTransform)go.transform;
            bar.SetParent(_root, false);
            bar.anchorMin = new Vector2(0, 0);
            bar.anchorMax = new Vector2(1, 0);
            bar.pivot = new Vector2(0.5f, 0);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0, BarHeight);
            go.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.1f, 0.9f);
            var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            var edgeRt = (RectTransform)edge.transform;
            edgeRt.SetParent(bar, false);
            edgeRt.anchorMin = new Vector2(0, 1);
            edgeRt.anchorMax = new Vector2(1, 1);
            edgeRt.pivot = new Vector2(0.5f, 1);
            edgeRt.anchoredPosition = Vector2.zero;
            edgeRt.sizeDelta = new Vector2(0, 3);
            edge.GetComponent<Image>().color = UiKit.Gold;
            edge.GetComponent<Image>().raycastTarget = false;

            var left = new Vector2(0, 0.5f);
            var right = new Vector2(1, 0.5f);
            _batCrest = UiKit.Crest(bar, "", left, new Vector2(54, 0), 74);
            for (var i = 0; i < 2; i++)
            {
                var x0 = 140 + i * 300;
                _batterPic[i] = UiKit.HexPortrait(bar, "Batter" + i, left, new Vector2(x0, 0), 72);
                _batterName[i] = UiKit.LabelAt(bar, left, new Vector2(x0 + 150, 17), new Vector2(220, 30), "", 22, TextAnchor.MiddleLeft);
                _batterScore[i] = UiKit.LabelAt(bar, left, new Vector2(x0 + 150, -17), new Vector2(220, 32), "", 26, TextAnchor.MiddleLeft, UiKit.Gold);
            }
            _score = UiKit.LabelAt(bar, Centre, new Vector2(0, 17), new Vector2(520, 44), "", 40, TextAnchor.MiddleCenter);
            _score.supportRichText = true;
            _chase = UiKit.LabelAt(bar, Centre, new Vector2(0, -20), new Vector2(560, 30), "", 22, TextAnchor.MiddleCenter, UiKit.Gold);

            _bowlCrest = UiKit.Crest(bar, "", right, new Vector2(-54, 0), 74);
            _bowlerPic = UiKit.HexPortrait(bar, "BowlerPic", right, new Vector2(-150, 0), 72);
            _bowler = UiKit.LabelAt(bar, right, new Vector2(-350, 17), new Vector2(300, 30), "", 22, TextAnchor.MiddleRight);
            _over = UiKit.LabelAt(bar, right, new Vector2(-350, -17), new Vector2(300, 32), "", 26, TextAnchor.MiddleRight, UiKit.Gold);
            _over.supportRichText = true;
        }

        /// <summary>
        /// Final rating fix #1 (wrong-role HUD): whether the user's side is batting now (null: no gate, e.g. nets).
        /// The batting panel (LOFT/STROKE/PUSH, aim stick, timing stamp) only ever comes on when this says so —
        /// through <see cref="SetPlaying"/> and through every "put it back as it was" restore (broadcast, running).
        /// </summary>
        public Func<bool>? UserBats;

        /// <summary>The batting panel may be on now (the role gate); logs when it keeps it off.</summary>
        private bool RoleAllowsPlay(string why)
        {
            if (UserBats == null || UserBats()) return true;
            Debug.Log($"[APL hudrole] batting panel kept OFF ({why}): the user is bowling");
            return false;
        }

        public void SetPlaying(bool on) => _play.SetActive(on && RoleAllowsPlay("SetPlaying"));

        /// <summary>The stick's sides follow the batter: off side on the left for a right-hander, mirrored for a left-hander.</summary>
        public void SetBatter(bool leftHanded)
        {
            _leftHanded = leftHanded;
            _offLabel.text = leftHanded ? "LEG" : "OFF";
            _legLabel.text = leftHanded ? "OFF" : "LEG";
        }

        public void ShowScore(QuickMatch m, Franchise batting, Franchise bowling)
        {
            Crest(_batCrest, ref _batCrestId, batting.Id);
            Crest(_bowlCrest, ref _bowlCrestId, bowling.Id);
            _score.text = $"{batting.ShortName}  {m.Runs}/{m.Wickets}  <size=24>{Cricket.FormatOvers(m.LegalBalls)} ({m.Overs})</size>";
            if (!m.Chasing)
            {
                // Batting first (a full match): the run rate and where it's heading.
                var crr = m.LegalBalls > 0 ? m.Runs * 6.0 / m.LegalBalls : 0;
                _chase.text = m.Finished ? "Innings complete" : $"CRR {crr:0.0}  ·  Projected {Math.Round(m.Runs + crr * m.BallsLeft / 6.0)}";
            }
            else if (m.Finished)
            {
                _chase.text = $"Target {m.Target}";
            }
            else
            {
                var rrr = m.BallsLeft > 0 ? m.RunsNeeded * 6.0 / m.BallsLeft : 0;
                _chase.text = $"Need {m.RunsNeeded} off {m.BallsLeft}  ·  RRR {rrr:0.0}";
            }
            var pair = new[] { m.OnStrike, m.OffStrike };
            for (var i = 0; i < 2; i++)
            {
                var b = pair[i];
                _batterName[i].text = (i == 0 ? "● " : "") + Short(b.Player.Name);
                _batterScore[i].text = $"{b.Runs} ({b.Balls})" + (i == 0 ? "*" : "");
                if (_batterPicId[i] != b.Player.Id)
                {
                    _batterPicId[i] = b.Player.Id;
                    _batterPic[i].texture = Portraits.For(b.Player, 96);
                }
            }
            var bowler = m.CurrentBowler;
            ChaseBowler? fig = null;
            foreach (var f in m.BowlingFigures) if (f.Player.Id == bowler.Id) fig = f;
            _bowler.text = fig == null
                ? $"{Short(bowler.Name)}  ·  {BowlLabel(bowler)}"
                : $"{Short(bowler.Name)}  {fig.Wickets}-{fig.Runs} ({Cricket.FormatOvers(fig.Balls)})";
            if (_bowlerPicId != bowler.Id)
            {
                _bowlerPicId = bowler.Id;
                _bowlerPic.texture = Portraits.For(bowler, 96);
            }
            // This over: one mark per ball so far.
            var marks = new List<string>();
            var log = m.Log;
            var legalInOver = 0;
            // Finished on the over's last ball: the bar keeps that over's balls (no empty "next over", match-play r11).
            var inOver = m.Finished && m.BallInOver == 0 && m.LegalBalls > 0 ? 6 : m.BallInOver;
            for (var i = log.Count - 1; i >= 0 && legalInOver < inOver; i--)
            {
                var r = log[i];
                if (r.Legal) legalInOver++;
                marks.Insert(0, r.Wide ? "wd" : r.NoBall ? (r.Wicket ? "nb<color=#EB5252>W</color>" : r.BatRuns > 0 ? $"nb{r.BatRuns}" : "nb") : r.Byes > 0 ? $"{r.Byes}b" : r.LegByes > 0 ? $"{r.LegByes}lb" : r.Wicket ? "<color=#EB5252>W</color>" : r.Six ? "<color=#F2C24F>6</color>" : r.Four ? "<color=#F2C24F>4</color>" : r.Runs == 0 ? "•" : r.Runs.ToString());
            }
            while (legalInOver++ < 6) marks.Add("<color=#5C6478>○</color>");
            _over.text = string.Join("  ", marks);
            ShowField(m.Field);
        }

        private static void Crest(RawImage img, ref string current, string teamId)
        {
            if (current == teamId) return;
            current = teamId;
            var tex = Resources.Load<Texture2D>("Crests/" + teamId);
            img.texture = tex;
            img.color = tex != null ? Color.white : new Color(1, 1, 1, 0);
        }

        /// <summary>"Rogit Shaermah" → "R. Shaermah": the strip hasn't room for full names.</summary>
        private static string Short(string name)
        {
            var parts = name.Split(' ');
            return parts.Length < 2 ? name : $"{parts[0][0]}. {parts[parts.Length - 1]}";
        }

        private static string BowlLabel(Player p) => p.BowlType == BowlType.Spin ? "SPIN" : "PACE";

        // ---------------------------------------------------------------- radar

        private const float RopePx = 88f;

        /// <summary>
        /// The nine fielders (and keeper, bowler) on the radar, scaled to this
        /// ground's rope, as the field looks from behind the bowler: the batter in
        /// the middle, straight down the ground at the bottom, off side on the
        /// left for a right-hander.
        /// </summary>
        private void ShowField(Field field)
        {
            var sign = field.LeftHanded ? 1f : -1f;
            var n = 0;
            void Dot(double angleDeg, double radius, Color color, float size)
            {
                var k = (float)Math.Min(0.96, radius / Math.Max(1, field.Rope(angleDeg))) * RopePx;
                var a = (float)(angleDeg * Math.PI / 180);
                RectTransform dot;
                if (n < _dots.Count) dot = _dots[n];
                else
                {
                    dot = UiKit.Box(_radar, "Dot", Centre, Vector2.zero, new Vector2(size, size), color);
                    var img = dot.GetComponent<Image>();
                    img.sprite = Disc();
                    img.raycastTarget = false;
                    _dots.Add(dot);
                }
                n++;
                dot.gameObject.SetActive(true);
                dot.anchoredPosition = new Vector2(sign * Mathf.Sin(a) * k, -Mathf.Cos(a) * k);
                dot.sizeDelta = new Vector2(size, size);
                dot.GetComponent<Image>().color = color;
            }
            foreach (var f in field.Fielders) Dot(f.AngleDeg, f.Radius, Color.white, 11);
            var pale = new Color(0.65f, 0.9f, 1f);
            Dot(180, 14, pale, 9);
            Dot(0, 20, pale, 9);
            for (; n < _dots.Count; n++) _dots[n].gameObject.SetActive(false);
        }

        private static Sprite? _disc, _ring;
        private static Sprite Disc() => _disc ??= Circle(64, 0f);
        private static Sprite Ring() => _ring ??= Circle(128, 0.955f);

        /// <summary>A soft-edged white disc, or a ring from <paramref name="inner"/> (0–1 of the radius) out.</summary>
        private static Sprite Circle(int size, float inner)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            var r = size / 2f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                var a = Mathf.Clamp01((1f - d) * r);
                if (inner > 0) a *= Mathf.Clamp01((d - inner) * r);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), Centre);
        }

        // ------------------------------------------------- banner (shot controls, meter: MatchHud.Batting.cs)

        /// <summary>The centre banner, through the one-card-at-a-time stage (MatchHud.Overlays.cs).</summary>
        public void Banner(string text, string sub, Color color, float seconds = 2f) =>
            Stage("Banner:" + text, BannerPriority, () => BannerNow(text, sub, color, seconds), () => _bannerRoot.SetActive(false), () => _bannerRoot.activeSelf,
                // A long notice (a challenge's goal, 3 s+) is read in full; a run result goes when the next ball comes.
                minHold: seconds >= 3f ? seconds : 0f);

        private void BannerNow(string text, string sub, Color color, float seconds)
        {
            _bannerRoot.SetActive(true);
            _banner.text = text;
            _banner.color = color;
            _bannerSub.text = sub;
            _bannerUntil = Time.time + seconds;
        }

        private void Update()
        {
            if (_bannerRoot.activeSelf && Time.time > _bannerUntil) _bannerRoot.SetActive(false);
            StageUpdate();
            // Keyboard for desktop testing: 1/2/3 = defend/ground/loft, space/B = front/back foot, L = leave, S = special, Esc = pause.
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetMode(ShotType.Push);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetMode(ShotType.Stroke);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetMode(ShotType.Loft);
            if (Input.GetKeyDown(KeyCode.Space)) ShotPressed?.Invoke(_mode, KeyFoot(false));
            if (Input.GetKeyDown(KeyCode.B)) ShotPressed?.Invoke(_mode, KeyFoot(true));
            if (Input.GetKeyDown(KeyCode.L)) LeavePressed?.Invoke();
            if (Input.GetKeyDown(KeyCode.S)) SpecialPressed?.Invoke();
            if (Input.GetKeyDown(KeyCode.Escape) && _play.activeSelf) SetPaused(!Paused);
        }

        // ---------------------------------------------------------------- pause

        /// <summary>
        /// Freeze the match (time scale 0, sound held) behind RESUME / QUIT.
        /// Everything in the match runs on scaled time, so it all waits.
        /// </summary>
        public void SetPaused(bool on)
        {
            if (Paused == on) return;
            Paused = on;
            Time.timeScale = on ? 0f : 1f;
            AudioListener.pause = on;
            if (!on)
            {
                if (_pauseMenu != null) Destroy(_pauseMenu);
                _pauseMenu = null;
                return;
            }
            // The wide three-column pause screen on its own canvas (MatchHud.PauseMenu.cs).
            BuildPauseMenu();
        }

        private void OnDestroy()
        {
            // Leaving the scene paused must not freeze the menu.
            if (!Paused) return;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        // ---------------------------------------------------------------- cards

        private RectTransform NewCard(string name, float height)
        {
            if (_card != null) Destroy(_card);
            var dim = UiKit.Stretch(_root, name, new Color(0.02f, 0.03f, 0.07f, 0.6f));
            _card = dim.gameObject;
            return UiKit.EdgedPanel(dim, name + "Panel", Centre, new Vector2(0, 20), new Vector2(1100, height), UiKit.PanelSolid, UiKit.Gold);
        }

        public void HideCard()
        {
            if (_card != null) Destroy(_card);
            _card = null!;
        }

        /// <summary>The opponent's innings and the target.</summary>
        public void ShowTarget(Dataset data, QuickMatch m, Franchise batting, Franchise bowling) => TargetScreen(m, batting, bowling); // card kit: MatchHud.BreakCard.cs

        /// <summary>The chase's result: the card-kit result screen (MatchHud.ResultCard.cs, presentation).</summary>
        public void ShowResult(QuickMatch m, Franchise batting, Franchise bowling) => ResultFor(m, batting, bowling);
    }
}
