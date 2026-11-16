using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The pre-match screens over the ground, as the reference plays them:
    /// both XIs (squad VS), the match card over the two captains, the toss
    /// calls, pick your openers / the next batter, the intro cards, captions.
    /// Every timed screen counts down and has SKIP; every choice autoselects.
    /// The match director runs them (MatchDirector.PreMatch.cs) and moves the
    /// people and the camera behind them.
    /// </summary>
    public sealed partial class PreMatchUi : MonoBehaviour
    {
        private RectTransform _root = null!;
        private RectTransform? _page, _intro, _caption, _banner;
        private GameObject? _skip;

        /// <summary>SKIP pressed (or the screen tapped) on the current timed screen.</summary>
        public bool Skipped { get; private set; }

        private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
        private static readonly Color Dark = new Color(0.02f, 0.03f, 0.07f, 0.78f);
        private static readonly Color Violet = new Color(0.2f, 0.08f, 0.42f, 0.97f);

        public static PreMatchUi Create()
        {
            var canvas = UiKit.Canvas("PreMatch", 15);
            var ui = canvas.gameObject.AddComponent<PreMatchUi>();
            ui._root = (RectTransform)canvas.transform;
            return ui;
        }

        private RectTransform NewPage(Color? dim)
        {
            ClearPage();
            _page = UiKit.Stretch(_root, "Page", dim ?? new Color(0, 0, 0, 0));
            // A see-through page lets taps through (to the HUD's cards below).
            _page.GetComponent<Image>().raycastTarget = dim != null;
            // A full screen (squads, pick your batters): the cards are the only thing lit — every
            // other overlay (radar, score strip, broadcast bits, controls) is off until it closes.
            if (dim != null) HideOthers(true);
            return _page;
        }

        public void ClearPage()
        {
            if (_page != null) Destroy(_page.gameObject);
            _page = null;
            HideOthers(false);
        }

        private readonly System.Collections.Generic.List<Canvas> _hidden = new System.Collections.Generic.List<Canvas>();

        private void HideOthers(bool hide)
        {
            if (!hide)
            {
                foreach (var c in _hidden) if (c != null) c.enabled = true;
                _hidden.Clear();
                return;
            }
            var mine = GetComponent<Canvas>();
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c == mine || !c.enabled || !c.isRootCanvas || c.renderMode == RenderMode.WorldSpace || _hidden.Contains(c)) continue;
                c.enabled = false;
                _hidden.Add(c);
            }
        }

        /// <summary>Everything off (the match is about to start).</summary>
        public void ClearAll()
        {
            ClearPage();
            HideIntro();
            Caption(null);
            HideBanner();
            ShowSkip(false);
        }

        /// <summary>A SKIP ► in the corner for the cinematic parts (walk-out, intros).</summary>
        public void ShowSkip(bool on)
        {
            if (!on)
            {
                if (_skip != null) Destroy(_skip);
                _skip = null;
                return;
            }
            Skipped = false;
            if (_skip != null) return;
            _skip = UiKit.Btn(_root, "SkipIntro", new Vector2(1, 1), new Vector2(-150, -60), new Vector2(240, 76), "SKIP ►", new Color(0.04f, 0.05f, 0.1f, 0.85f), () => Skipped = true, 28).gameObject;
        }

        public void ResetSkip() => Skipped = false;

        /// <summary>
        /// Wait <paramref name="seconds"/> (or until SKIP / <paramref name="done"/>), writing
        /// <paramref name="text"/>(seconds left) into <paramref name="label"/> as it goes.
        /// </summary>
        private IEnumerator Count(float seconds, Text? label, Func<int, string>? text, Func<bool>? done = null)
        {
            Skipped = false;
            var end = Time.time + seconds;
            while (Time.time < end && !Skipped && (done == null || !done()))
            {
                if (label != null && text != null) label.text = text(Mathf.CeilToInt(end - Time.time));
                yield return null;
            }
        }

        /// <summary>The page itself skips when tapped (squads, match card).</summary>
        private void TapToSkip(RectTransform page) => page.gameObject.AddComponent<Button>().onClick.AddListener(() => Skipped = true);

        private Button SkipButton(RectTransform page, string label = "SKIP ►") =>
            UiKit.Btn(page, "Skip", new Vector2(1, 0), new Vector2(-170, 70), new Vector2(260, 84), label, UiKit.Accent, () => Skipped = true, 30);

        // --------------------------------------------------------------- toss

        /// <summary>"Make your call": HEADS / TAILS, autoselected when the time runs out (f21–23).</summary>
        public IEnumerator Call(float seconds, Action<bool> heads)
        {
            var page = NewPage(null);
            bool? pick = null;
            // The card kit (round 8: it was a flat box in the plain font).
            var panel = PanelKit.GlassPanel(page, "Call", new Vector2(1, 0.5f), new Vector2(-260, 20), new Vector2(440, 420));
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -82), new Vector2(-24, -20), "MAKE YOUR CALL", 40, TextAnchor.MiddleCenter, UiKit.Gold, condensed: true);
            PanelKit.Pill(panel, "Heads", Centre, new Vector2(0, 20), new Vector2(370, 120), "HEADS", UiKit.Accent, () => pick = true, 46);
            PanelKit.Pill(panel, "Tails", Centre, new Vector2(0, -130), new Vector2(370, 120), "TAILS", new Color(0.15f, 0.42f, 0.9f), () => pick = false, 46);
            var auto = AutoLabel(page);
            yield return Count(seconds, auto, n => $"Autoselect in {n}", () => pick != null);
            heads(pick ?? UnityEngine.Random.value < 0.5f);
            ClearPage();
        }

        /// <summary>You won the toss: BAT or BOWL first (<paramref name="canBat"/> false: batting first isn't playable yet).</summary>
        public IEnumerator BatOrBowl(float seconds, bool suggestBat, bool canBat, Action<bool> batFirst)
        {
            var page = NewPage(null);
            bool? pick = null;
            var panel = PanelKit.GlassPanel(page, "Choose", new Vector2(1, 0.5f), new Vector2(-260, 20), new Vector2(440, 460));
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -74), new Vector2(-24, -18), "YOU WON THE TOSS", 40, TextAnchor.MiddleCenter, UiKit.Gold, condensed: true);
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -112), new Vector2(-24, -74), "CHOOSE TO", 26, TextAnchor.MiddleCenter, UiKit.Dim, condensed: true);
            var bat = PanelKit.Pill(panel, "Bat", Centre, new Vector2(0, 0), new Vector2(370, 120), canBat ? "BAT" : "BAT  (SOON)", new Color(0.15f, 0.6f, 0.3f), () => pick = true, 46);
            bat.interactable = canBat;
            PanelKit.Pill(panel, "Bowl", Centre, new Vector2(0, -150), new Vector2(370, 120), "BOWL", new Color(0.15f, 0.42f, 0.9f), () => pick = false, 46);
            var auto = AutoLabel(page);
            yield return Count(seconds, auto, n => $"Autoselect in {n}", () => pick != null);
            batFirst(pick ?? (suggestBat && canBat));
            ClearPage();
        }

        private static Text AutoLabel(RectTransform page)
        {
            var chip = PlayerCardKit.Shape(page, "Auto", CardSprites.Round(16), new Color(0.03f, 0.035f, 0.08f, 0.9f), new Vector2(-210, 50), new Vector2(340, 56), new Vector2(1, 0));
            return PlayerCardKit.Text((RectTransform)chip.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, "", 28, TextAnchor.MiddleCenter, Color.white, condensed: true);
        }

        /// <summary>A two-line banner bottom right: "MBH won the toss" / "and chose to <b>bowl</b> first" (f27–42).</summary>
        public void Banner(string line, string sub, Franchise? team = null)
        {
            HideBanner();
            _banner = UiKit.EdgedPanel(_root, "TossBanner", new Vector2(1, 0), new Vector2(-380, 190), new Vector2(640, 150), new Color(0.03f, 0.03f, 0.08f, 0.9f), team != null ? UiKit.ParseHex(team.PrimaryColor) : UiKit.Accent);
            if (team != null) UiKit.Crest(_banner, team.Id, new Vector2(0, 0.5f), new Vector2(80, 0), 104);
            var x = team != null ? 60f : 0f;
            // The APL condensed type, like the cards; one line sits in the middle.
            var top = UiKit.LabelAt(_banner, Centre, new Vector2(x, sub.Length > 0 ? 26 : 0), new Vector2(500, 56), line, 44, TextAnchor.MiddleCenter, Color.white);
            top.font = PlayerCardKit.Condensed;
            var under = UiKit.LabelAt(_banner, Centre, new Vector2(x, -30), new Vector2(500, 44), sub, 32, TextAnchor.MiddleCenter, UiKit.Gold);
            under.font = PlayerCardKit.Condensed;
            under.supportRichText = true;
        }

        public void HideBanner()
        {
            if (_banner != null) Destroy(_banner.parent.gameObject);
            _banner = null;
        }

        /// <summary>A big word in the middle for a moment ("HEADS!").</summary>
        public IEnumerator Flash(string text, Color color, float seconds)
        {
            var label = UiKit.LabelAt(_root, Centre, new Vector2(0, 260), new Vector2(900, 140), text, 110, TextAnchor.MiddleCenter, color);
            label.gameObject.AddComponent<Outline>().effectDistance = new Vector2(4, -4);
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                var k = Mathf.Clamp01(t / 0.25f);
                label.transform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, k);
                label.color = UiKit.WithAlpha(color, t > seconds - 0.3f ? (seconds - t) / 0.3f : k);
                yield return null;
            }
            Destroy(label.transform.parent.gameObject);
        }

        // ------------------------------------------------------------- intros

        /// <summary>
        /// The intro card bottom left over the close-up (f61–84): his card, then a
        /// strip with what he does and his numbers (<paramref name="stats"/>: heading, value).
        /// </summary>
        public void Intro(Player p, Franchise team, bool mine, IReadOnlyList<(string head, string value)> stats)
        {
            HideIntro();
            var root = new GameObject("Intro", typeof(RectTransform));
            _intro = (RectTransform)root.transform;
            _intro.SetParent(_root, false);
            _intro.anchorMin = _intro.anchorMax = new Vector2(0, 0);
            _intro.sizeDelta = Vector2.zero;
            PreMatchCard.Build(_intro, p, team, new Vector2(0, 0), new Vector2(190, 236), 1.15f);
            var strip = UiKit.Box(_intro, "Strip", new Vector2(0, 0), new Vector2(330 + 420, 186), new Vector2(840, 120), new Color(0.06f, 0.04f, 0.16f, 0.92f));
            var head = UiKit.Box(strip, "Head", new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(840, 40), new Color(0.42f, 0.16f, 0.78f, 0.95f));
            UiKit.LabelAt(head, new Vector2(0, 0.5f), new Vector2(230, 0), new Vector2(420, 34), PreMatchCard.Description(p), 24, TextAnchor.MiddleLeft, Color.white);
            UiKit.LabelAt(head, new Vector2(1, 0.5f), new Vector2(-110, 0), new Vector2(200, 34), mine ? "MY TEAM" : team.ShortName, 22, TextAnchor.MiddleRight, mine ? YouMark.Lime : Color.white);
            UiKit.Crest(head, team.Id, new Vector2(1, 0.5f), new Vector2(-240, 0), 34);
            var w = 840f / Math.Max(1, stats.Count);
            for (var i = 0; i < stats.Count; i++)
            {
                var x = -420 + w * (i + 0.5f);
                UiKit.LabelAt(strip, Centre, new Vector2(x, -4), new Vector2(w - 6, 24), stats[i].head, 16, TextAnchor.MiddleCenter, UiKit.Dim);
                UiKit.LabelAt(strip, Centre, new Vector2(x, -36), new Vector2(w - 6, 36), stats[i].value, 30, TextAnchor.MiddleCenter, Color.white);
            }
        }

        public void HideIntro()
        {
            if (_intro != null) Destroy(_intro.gameObject);
            _intro = null;
        }

        /// <summary>A lower-third line ("The Bengaluru Royals take the field"); null hides it.</summary>
        public void Caption(string? text)
        {
            if (_caption != null) Destroy(_caption.gameObject);
            _caption = null;
            if (text == null) return;
            // In the card kit (round 8; it was a flat box in the plain font): rounded dark glass, a gold
            // edge on the left, the APL condensed type.
            var bar = PlayerCardKit.Shape(_root, "Caption", CardSprites.Round(18), new Color(0.03f, 0.035f, 0.08f, 0.9f), new Vector2(0, 124), new Vector2(1180, 76), new Vector2(0.5f, 0));
            _caption = (RectTransform)bar.transform;
            PlayerCardKit.Shape(_caption, "Edge", CardSprites.Round(4), UiKit.Gold, new Vector2(14, 0), new Vector2(8, 52), new Vector2(0, 0.5f));
            PlayerCardKit.Shape(_caption, "Sheen", CardSprites.Round(2), new Color(1, 1, 1, 0.12f), new Vector2(0, -3), new Vector2(1140, 2), new Vector2(0.5f, 1));
            var line = PlayerCardKit.Text(_caption, Vector2.zero, Vector2.one, new Vector2(40, 0), new Vector2(-24, 0), text, 38, TextAnchor.MiddleCenter, Color.white, condensed: true);
            line.resizeTextForBestFit = true;
            line.resizeTextMinSize = 22;
            line.resizeTextMaxSize = 38;
        }

        // --------------------------------------------------------- tournament

        /// <summary>A button under the result card (tournament matches: back to the table).</summary>
        private GameObject? _impact;

        /// <summary>IMPACT PLAYER ► on the left while a substitution can be made (over breaks, after a wicket).</summary>
        public void ImpactButton(bool on, Action tap)
        {
            if (!on)
            {
                if (_impact != null) Destroy(_impact);
                _impact = null;
                return;
            }
            if (_impact != null) return;
            _impact = UiKit.Btn(_root, "ImpactSub", new Vector2(0, 0.5f), new Vector2(170, 120), new Vector2(300, 96), "IMPACT PLAYER ►", new Color(0.55f, 0.2f, 0.75f, 0.96f), () => tap(), 26).gameObject;
        }

        public void Continue(string label, Action act)
        {
            // Over the result card, blocking its PLAY AGAIN (this fixture is in the table now).
            var page = NewPage(new Color(0, 0, 0, 0.01f));
            UiKit.Btn(page, "Continue", new Vector2(0.5f, 0), new Vector2(0, 120), new Vector2(640, 104), label, UiKit.Accent, act, 36);
        }
    }
}
