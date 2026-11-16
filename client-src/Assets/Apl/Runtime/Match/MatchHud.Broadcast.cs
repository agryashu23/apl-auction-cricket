using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The broadcast layer over the match, in the reference game's style: the big
    /// event graphic (BOWLED, CAUGHT, 4, 6) sweeping in bottom right, the shot
    /// name and its timing after contact, the ball's speed, the player card
    /// lower third (intros, the out batter walking off), the between-overs card,
    /// the full scorecard (from the pause menu), the replay camera bar, the DRS
    /// review button and the Hawk-Eye read-out. Everything here animates on real
    /// time, so it runs while a replay has the game frozen.
    /// </summary>
    public sealed partial class MatchHud
    {
        /// <summary>The viewer tapped the screen during a skippable moment (an intro, a replay, a card).</summary>
        public event Action? Skipped;

        /// <summary>The source for the full scorecard: the match and both sides (set by the director).</summary>
        public Func<(QuickMatch match, Franchise batting, Franchise bowling)?>? Scorecard;

        private RectTransform _bc = null!;
        private GameObject _tap = null!;
        private GameObject? _event, _caption, _speed, _playerCard, _overCard, _replayBar, _review, _hawk, _replayBug;
        private int _broadcasting;
        private bool _playWas;

        private static readonly Color Ribbon = new Color(0.22f, 0.07f, 0.42f, 0.94f);
        private static readonly Color Ribbon2 = new Color(0.43f, 0.16f, 0.78f, 0.95f);
        private static readonly Color Night = new Color(0.03f, 0.04f, 0.1f, 0.92f);

        /// <summary>The layer the broadcast pieces live in (made once, before any pause menu, so the menu stays on top).</summary>
        public void BroadcastInit()
        {
            if (_bc != null) return;
            _bc = UiKit.Stretch(_root, "Broadcast");
            // The tap-anywhere catcher, under the broadcast pieces' own buttons.
            var tap = UiKit.Stretch(_bc, "TapToSkip", new Color(0, 0, 0, 0));
            tap.gameObject.AddComponent<OnPress>().Pressed = () => Skipped?.Invoke();
            _tap = tap.gameObject;
            _tap.SetActive(false);
        }

        /// <summary>Taps skip what's on screen.</summary>
        public void SetSkippable(bool on)
        {
            BroadcastInit();
            _tap.SetActive(on);
            _tap.transform.SetAsFirstSibling();
        }

        /// <summary>
        /// A broadcast moment has the screen: the batting controls make way (nested
        /// calls count; the last one out puts them back as they were).
        /// </summary>
        /// <summary>A broadcast piece is on screen (replay, wicket sequence, wagon wheel…): play controls stay off.</summary>
        public bool OnAir => _broadcasting > 0;

        public void Broadcasting(bool on)
        {
            if (on)
            {
                if (_broadcasting++ == 0)
                {
                    _playWas = _play.activeSelf;
                    _play.SetActive(false);
                }
                return;
            }
            if (_broadcasting == 0) return;
            if (--_broadcasting == 0) _play.SetActive(_playWas && RoleAllowsPlay("broadcast end"));
        }

        /// <summary>Back to the plain game (a new ball): every broadcast piece away.</summary>
        public void ClearBroadcast()
        {
            foreach (var go in new[] { _event, _caption, _playerCard, _overCard, _replayBar, _review, _hawk, _replayBug })
            {
                if (go != null) Destroy(go);
            }
            _event = _caption = _playerCard = _overCard = _replayBar = _review = _hawk = _replayBug = null;
            if (_tap != null) _tap.SetActive(false);
            HideWagons();
            if (_broadcasting > 0)
            {
                _broadcasting = 0;
                _play.SetActive(_playWas && RoleAllowsPlay("broadcast cleared"));
            }
        }

        // ------------------------------------------------------------ animation

        private static float Ease(float t) => 1 - Mathf.Pow(1 - Mathf.Clamp01(t), 3);

        /// <summary>
        /// Slide <paramref name="rt"/> in from <paramref name="from"/> (offset) and fade it in,
        /// hold, then fade it out and destroy it (unless <paramref name="seconds"/> ≤ 0: it stays).
        /// </summary>
        private IEnumerator Animate(RectTransform rt, Vector2 from, float seconds, float popScale = 1f)
        {
            var group = rt.GetComponent<CanvasGroup>() ?? rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = true;
            var home = rt.anchoredPosition;
            for (var t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.22f)
            {
                if (rt == null) yield break;
                var e = Ease(t);
                rt.anchoredPosition = Vector2.Lerp(home + from, home, e);
                rt.localScale = Vector3.one * Mathf.Lerp(popScale, 1f, e);
                group.alpha = Mathf.Clamp01(t * 2.5f);
                yield return null;
            }
            if (rt == null) yield break;
            rt.anchoredPosition = home;
            rt.localScale = Vector3.one;
            group.alpha = 1;
            if (seconds <= 0) yield break;
            for (var until = Time.unscaledTime + seconds; Time.unscaledTime < until;)
            {
                if (rt == null) yield break;
                yield return null;
            }
            for (var t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.25f)
            {
                if (rt == null) yield break;
                group.alpha = 1 - t;
                yield return null;
            }
            if (rt != null) Destroy(rt.gameObject);
        }

        private RectTransform Piece(ref GameObject? slot, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            BroadcastInit();
            if (slot != null) Destroy(slot);
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_bc, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            slot = go;
            return rt;
        }

        private static RectTransform Slant(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color, float degrees = 0)
        {
            var b = UiKit.Box(parent, name, new Vector2(0.5f, 0.5f), pos, size, color);
            b.localRotation = Quaternion.Euler(0, 0, degrees);
            b.GetComponent<Image>().raycastTarget = false;
            return b;
        }

        // ------------------------------------------------------- event graphic

        /// <summary>
        /// The big graphic bottom right: a word (BOWLED, CAUGHT, LBW, RUN OUT,
        /// STUMPED, NOT OUT) or a boundary's "4" / "6" with its word underneath.
        /// </summary>
        public void EventGraphic(string big, string small, Color color, float seconds = 2.2f, int priority = EventPriority) =>
            Stage("Event:" + big, priority, () => EventGraphicNow(big, small, color, seconds), () =>
            {
                if (_event != null) Destroy(_event);
                _event = null;
            }, () => _event != null);

        private void EventGraphicNow(string big, string small, Color color, float seconds)
        {
            var number = big.Length <= 2;
            var rt = Piece(ref _event, "EventGraphic", new Vector2(1, 0), new Vector2(-460, BarHeight + 230), new Vector2(780, 300));
            // Two slashes of colour behind the word, like the reference's lightning sweep.
            Slant(rt, "Slash", new Vector2(40, -10), new Vector2(number ? 300 : 700, number ? 360 : 120), UiKit.WithAlpha(color, 0.85f), number ? -18 : 10);
            // The second slash is dark ink, not white: the white "4" / "FOUR" vanished on a white stripe.
            Slant(rt, "Slash2", new Vector2(70, -40), new Vector2(number ? 120 : 560, number ? 380 : 26), new Color(0.05f, 0.03f, 0.14f, 0.82f), number ? -18 : 10);
            var word = UiKit.Label(rt, big, number ? 280 : 120, TextAnchor.MiddleCenter, Color.white);
            word.fontStyle = FontStyle.BoldAndItalic;
            var shadow = word.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(0.05f, 0.02f, 0.15f, 0.9f);
            shadow.effectDistance = new Vector2(5, -5);
            if (small.Length > 0)
            {
                var sub = UiKit.LabelAt(rt, new Vector2(0.5f, 0), new Vector2(0, number ? -6 : 34), new Vector2(700, 50), small, 40, TextAnchor.MiddleCenter, Color.white);
                sub.fontStyle = FontStyle.BoldAndItalic;
                var edge = sub.gameObject.AddComponent<Outline>();
                edge.effectColor = new Color(0.05f, 0.02f, 0.15f, 0.95f);
                edge.effectDistance = new Vector2(3, -3);
            }
            // A short slide in (from 420 px it was caught half off the right edge: r10 f62 "RUN OUT" clipped).
            StartCoroutine(Animate(rt, new Vector2(140, -40), seconds, number ? 1.35f : 1.12f));
        }

        // -------------------------------------------------------- shot caption

        /// <summary>The named stroke card is off (owner, final rating); flip to bring it back.</summary>
        public static readonly bool ShotCaptionOn = false;

        /// <summary>After contact: the shot's name and how it was timed ("COVER DRIVE · PERFECT").</summary>
        public void ShotCaption(string shot, string grade, Color gradeColor, float seconds = 1.6f)
        {
            // Owner (final rating): "a named stroke card not required" — the card (LOFTED OFF DRIVE, LATE CUT…) is
            // off in the match HUD; the stroke names stay in the data/engine. (It also went up on the AI's strokes.)
            if (!ShotCaptionOn) return;
            if (shot.Length == 0) return;
            // 80–680 px from the left: clear of the running inset (from 684 px; r10 f60 "DAB" under it), and a short
            // slide in, so a still never catches it cut off at the edge (r10 f298 "OFTED STRAIGHT DRIVE").
            var rt = Piece(ref _caption, "ShotCaption", new Vector2(0, 0), new Vector2(380, BarHeight + 150), new Vector2(600, 120));
            Slant(rt, "Band", Vector2.zero, new Vector2(590, 92), Ribbon, 0);
            Slant(rt, "Edge", new Vector2(-291, 0), new Vector2(10, 92), UiKit.Gold, 0);
            var name = UiKit.LabelAt(rt, new Vector2(0, 0.5f), new Vector2(300, 12), new Vector2(560, 56), shot, 50, TextAnchor.MiddleLeft, Color.white);
            name.fontStyle = FontStyle.BoldAndItalic;
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 26;
            name.resizeTextMaxSize = 50;
            UiKit.LabelAt(rt, new Vector2(0, 0.5f), new Vector2(300, -28), new Vector2(560, 30), grade, 26, TextAnchor.MiddleLeft, gradeColor);
            StartCoroutine(Animate(rt, new Vector2(-90, 0), seconds));
        }

        // --------------------------------------------------------------- speed

        /// <summary>The ball's pace, bottom centre, once it's been bowled ("Speed 142.3 km/h").</summary>
        public void SpeedReadout(double kmh, float seconds = 2.6f)
        {
            // Over the running inset when it's up (it sits bottom centre too: the pill covered its label, v23 f176).
            // Not over the end-of-over card (r10 f336): its numbers are the over's story.
            if (_overCard != null) return;
            var insetUp = _inset != null && _inset.activeSelf;
            // Beside the running inset when it's up, not above it where the timing stamp lands (r11 f144, f194).
            var rt = Piece(ref _speed, "Speed", new Vector2(0.5f, 0), new Vector2(insetUp ? 530 : 0, BarHeight + 34), new Vector2(460, 54));
            Slant(rt, "Pill", Vector2.zero, new Vector2(460, 48), Night);
            Slant(rt, "Streak", new Vector2(-200, 0), new Vector2(70, 6), UiKit.WithAlpha(Ribbon2, 0.9f));
            var ball = UiKit.Box(rt, "BallIcon", new Vector2(0, 0.5f), new Vector2(60, 0), new Vector2(40, 40), Color.white);
            ball.GetComponent<Image>().sprite = Disc();
            ball.GetComponent<Image>().raycastTarget = false;
            var text = UiKit.LabelAt(rt, new Vector2(0, 0.5f), new Vector2(270, 0), new Vector2(360, 44),
                $"Speed  <color=#F2C24F>{kmh.ToString("0.0", CultureInfo.InvariantCulture)}</color> km/h", 30, TextAnchor.MiddleLeft, Color.white);
            text.supportRichText = true;
            text.fontStyle = FontStyle.BoldAndItalic;
            StartCoroutine(Animate(rt, new Vector2(0, -40), seconds));
        }

        // --------------------------------------------------------- player card

        /// <summary>
        /// The player card lower third (the reference's intro card): his picture,
        /// rating and name on a card in his side's colour, then a band with what
        /// he does and his side, and a row of numbers. Stays until
        /// <see cref="HidePlayerCard"/> when <paramref name="seconds"/> ≤ 0.
        /// </summary>
        public void ShowPlayerCard(PlayerCardInfo info, float seconds = 0)
        {
            // One card at a time: the over card and its pitch map go at once, not fading under the intro (v8 f101).
            if (_overCard != null) Destroy(_overCard);
            _overCard = null;
            HidePitchMap();
            var p = info.Player;
            var rt = Piece(ref _playerCard, "PlayerCard", new Vector2(0, 0), new Vector2(700, BarHeight + 150), new Vector2(1240, 280));
            // The card: portrait, rating, name, skills.
            var card = UiKit.Box(rt, "Card", new Vector2(0, 0.5f), new Vector2(120, 0), new Vector2(220, 280), UiKit.WithAlpha(info.Tint, 0.95f));
            card.GetComponent<Image>().raycastTarget = false;
            UiKit.Box(card, "Shade", new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(220, 100), new Color(0, 0, 0, 0.35f)).GetComponent<Image>().raycastTarget = false;
            var pic = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage));
            var picRt = (RectTransform)pic.transform;
            picRt.SetParent(card, false);
            picRt.anchorMin = picRt.anchorMax = new Vector2(0.5f, 1);
            picRt.anchoredPosition = new Vector2(0, -92);
            picRt.sizeDelta = new Vector2(176, 176);
            var img = pic.GetComponent<RawImage>();
            img.texture = Portraits.For(p, 256);
            img.raycastTarget = false;
            UiKit.LabelAt(card, new Vector2(1, 1), new Vector2(-30, -24), new Vector2(60, 40), p.Impact.ToString(), 32, TextAnchor.MiddleCenter, Color.white);
            UiKit.LabelAt(card, new Vector2(0.5f, 0), new Vector2(0, 64), new Vector2(210, 40), Surname(p.Name), 30, TextAnchor.MiddleCenter, Color.white);
            UiKit.LabelAt(card, new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(210, 30),
                $"BAT {p.Batting}   BWL {p.Bowling}   FLD {p.Fielding}", 16, TextAnchor.MiddleCenter, UiKit.Dim);

            // The band: what he does, his side; and the numbers.
            var band = UiKit.Box(rt, "Band", new Vector2(0, 0.5f), new Vector2(240 + 500, -50), new Vector2(1000, 54), Ribbon2);
            band.GetComponent<Image>().raycastTarget = false;
            UiKit.LabelAt(band, new Vector2(0, 0.5f), new Vector2(300, 0), new Vector2(570, 46), info.Role, 30, TextAnchor.MiddleLeft, Color.white);
            UiKit.LabelAt(band, new Vector2(1, 0.5f), new Vector2(-120, 0), new Vector2(220, 40), info.Team, 26, TextAnchor.MiddleCenter, UiKit.Gold);
            var grid = UiKit.Box(rt, "Stats", new Vector2(0, 0.5f), new Vector2(240 + 500, -120), new Vector2(1000, 86), Night);
            grid.GetComponent<Image>().raycastTarget = false;
            var n = Math.Max(1, info.Stats.Count);
            var w = 1000f / n;
            for (var i = 0; i < info.Stats.Count; i++)
            {
                var x = w * i + w / 2;
                UiKit.LabelAt(grid, new Vector2(0, 0.5f), new Vector2(x, 22), new Vector2(w - 8, 26), info.Stats[i].Label, 18, TextAnchor.MiddleCenter, UiKit.Dim);
                UiKit.LabelAt(grid, new Vector2(0, 0.5f), new Vector2(x, -14), new Vector2(w - 8, 40), info.Stats[i].Value, 32, TextAnchor.MiddleCenter, Color.white);
                if (i > 0) UiKit.Box(grid, "Rule", new Vector2(0, 0.5f), new Vector2(w * i, 0), new Vector2(2, 62), new Color(1, 1, 1, 0.12f)).GetComponent<Image>().raycastTarget = false;
            }
            StartCoroutine(Animate(rt, new Vector2(-160, 0), seconds));
        }

        public void HidePlayerCard()
        {
            if (_playerCard == null) return;
            // Gone at once: fading, it lay under the next ball's controls (r9 f302).
            Destroy(_playerCard);
            _playerCard = null;
        }

        private IEnumerator FadeOut(RectTransform rt)
        {
            var group = rt.GetComponent<CanvasGroup>() ?? rt.gameObject.AddComponent<CanvasGroup>();
            for (var t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.2f)
            {
                if (rt == null) yield break;
                group.alpha = 1 - t;
                yield return null;
            }
            if (rt != null) Destroy(rt.gameObject);
        }

        /// <summary>"Rogit Shaermah" → "Shaermah" (the card's big name).</summary>
        private static string Surname(string name)
        {
            var parts = name.Split(' ');
            return parts.Length < 2 ? name : parts[parts.Length - 1];
        }

        // ------------------------------------------------------------ over card

        /// <summary>
        /// The between-overs card: the score and the chase, both batters' lines, the
        /// bowler's figures, the partnership and this over ball by ball.
        /// </summary>
        public void ShowOverCard(QuickMatch m, Franchise batting, Franchise bowling, Player lastBowler, float seconds = 0)
        {
            var rt = Piece(ref _overCard, "OverCard", new Vector2(0.5f, 0), new Vector2(0, BarHeight + 230), new Vector2(1240, 380));
            var panel = UiKit.Box(rt, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240, 380), Night);
            panel.GetComponent<Image>().raycastTarget = false;
            var head = UiKit.Box(panel, "Head", new Vector2(0.5f, 1), new Vector2(0, -32), new Vector2(1240, 64), Ribbon2);
            head.GetComponent<Image>().raycastTarget = false;
            UiKit.Crest(head, batting.Id, new Vector2(0, 0.5f), new Vector2(40, 0), 54);
            UiKit.LabelAt(head, new Vector2(0, 0.5f), new Vector2(330, 0), new Vector2(520, 50), $"END OF OVER {m.Over}", 34, TextAnchor.MiddleLeft, Color.white);
            var rrr = m.BallsLeft > 0 ? m.RunsNeeded * 6.0 / m.BallsLeft : 0;
            // Batting first there's nothing to chase yet: the run rate and where it's heading (as the bar, MatchHud.ShowScore).
            var crr = m.LegalBalls > 0 ? m.Runs * 6.0 / m.LegalBalls : 0;
            var chase = !m.Chasing
                ? (m.Finished ? "INNINGS COMPLETE" : $"PROJECTED {Math.Round(m.Runs + crr * m.BallsLeft / 6.0)}")
                : m.Finished ? $"Target {m.Target}" : $"NEED {m.RunsNeeded} OFF {m.BallsLeft}  ·  RRR {rrr.ToString("0.00", CultureInfo.InvariantCulture)}";
            UiKit.LabelAt(head, new Vector2(1, 0.5f), new Vector2(-300, 0), new Vector2(560, 50), chase, 28, TextAnchor.MiddleRight, UiKit.Gold);
            UiKit.LabelAt(panel, new Vector2(0, 1), new Vector2(170, -100), new Vector2(300, 50), $"{batting.ShortName} {m.Runs}/{m.Wickets}", 40, TextAnchor.MiddleLeft, Color.white);
            var rr = m.LegalBalls > 0 ? m.Runs * 6.0 / m.LegalBalls : 0;
            UiKit.LabelAt(panel, new Vector2(0, 1), new Vector2(170, -140), new Vector2(300, 30), $"{Cricket.FormatOvers(m.LegalBalls)} ov  ·  RR {rr.ToString("0.00", CultureInfo.InvariantCulture)}", 22, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);

            // The batters.
            UiKit.LabelAt(panel, new Vector2(0, 1), new Vector2(560, -92), new Vector2(560, 26), "BATTER                 R     B    4s   6s", 18, TextAnchor.MiddleLeft, UiKit.Dim);
            var pair = new[] { m.OnStrike, m.OffStrike };
            for (var i = 0; i < 2; i++)
            {
                var b = pair[i];
                var y = -128 - i * 40;
                UiKit.LabelAt(panel, new Vector2(0, 1), new Vector2(480, y), new Vector2(400, 36), (i == 0 ? "● " : "  ") + b.Player.Name, 26, TextAnchor.MiddleLeft, Color.white);
                UiKit.LabelAt(panel, new Vector2(0, 1), new Vector2(800, y), new Vector2(260, 36), $"{b.Runs}    {b.Balls}    {b.Fours}    {b.Sixes}", 26, TextAnchor.MiddleLeft, UiKit.Gold);
            }
            // The bowler who's just finished, and the partnership.
            var fig = m.BowlingFigures.FirstOrDefault(f => f.Player.Id == lastBowler.Id);
            var maidens = Maidens(m, lastBowler);
            var figures = fig == null ? "" : $"{Cricket.FormatOvers(fig.Balls)}-{maidens}-{fig.Runs}-{fig.Wickets}";
            UiKit.LabelAt(panel, new Vector2(0, 1), new Vector2(170, -220), new Vector2(300, 26), "BOWLER  O-M-R-W", 18, TextAnchor.MiddleLeft, UiKit.Dim);
            UiKit.LabelAt(panel, new Vector2(0, 1), new Vector2(250, -254), new Vector2(460, 36), $"{lastBowler.Name}   <color=#F2C24F>{figures}</color>", 26, TextAnchor.MiddleLeft, Color.white).supportRichText = true;
            var (pRuns, pBalls) = Partnership(m);
            UiKit.LabelAt(panel, new Vector2(0, 1), new Vector2(170, -300), new Vector2(300, 26), "PARTNERSHIP", 18, TextAnchor.MiddleLeft, UiKit.Dim);
            UiKit.LabelAt(panel, new Vector2(0, 1), new Vector2(170, -332), new Vector2(300, 36), $"{pRuns} ({pBalls})", 28, TextAnchor.MiddleLeft, Color.white);

            // This over, ball by ball.
            UiKit.LabelAt(panel, new Vector2(0, 1), new Vector2(560, -220), new Vector2(560, 26), "THIS OVER", 18, TextAnchor.MiddleLeft, UiKit.Dim);
            var balls = LastOver(m);
            var x = 520f;
            var total = 0;
            foreach (var r in balls)
            {
                total += r.Runs;
                var (text, color) = Chip(r);
                var chip = UiKit.Box(panel, "Ball", new Vector2(0, 1), new Vector2(x, -272), new Vector2(62, 62), color);
                chip.GetComponent<Image>().sprite = Disc();
                chip.GetComponent<Image>().raycastTarget = false;
                UiKit.Label(chip, text, text.Length > 2 ? 19 : text.Length > 1 ? 23 : 26, TextAnchor.MiddleCenter, color.grayscale > 0.6f ? UiKit.Ink : Color.white);
                x += 74;
            }
            UiKit.LabelAt(panel, new Vector2(0, 1), new Vector2(x + 70, -272), new Vector2(160, 40), $"= {total}", 30, TextAnchor.MiddleLeft, UiKit.Gold);
            StartCoroutine(Animate(rt, new Vector2(0, -120), seconds));
        }

        public void HideOverCard()
        {
            if (_overCard == null) return;
            var rt = (RectTransform)_overCard.transform;
            _overCard = null;
            StartCoroutine(FadeOut(rt));
        }

        /// <summary>The balls of the over just finished (or the one going on).</summary>
        private static List<BallResult> LastOver(QuickMatch m)
        {
            var list = new List<BallResult>();
            var want = m.BallInOver == 0 ? 6 : m.BallInOver;
            var legal = 0;
            var i = m.Log.Count - 1;
            for (; i >= 0 && legal < want; i--)
            {
                if (m.Log[i].Legal) legal++;
                list.Insert(0, m.Log[i]);
            }
            // A no-ball or wide before the over's first legal ball is this over's too (match-play: it was dropped).
            for (; i >= 0 && !m.Log[i].Legal; i--) list.Insert(0, m.Log[i]);
            return list;
        }

        private static (string, Color) Chip(BallResult r) =>
            // Extras by name (a no-ball used to show as a plain "1"), as the scorebug's strip has them.
            r.NoBall ? (r.Wicket ? "nbW" : r.BatRuns > 0 ? $"nb{r.BatRuns}" : r.Runs > 1 ? $"nb{r.Runs - 1}" : "nb", r.Wicket ? UiKit.Bad : new Color(0.95f, 0.55f, 0.25f))
            : r.Wicket ? ("W", UiKit.Bad)
            : r.Wide ? (r.Runs > 1 ? $"wd{r.Runs - 1}" : "wd", new Color(0.45f, 0.5f, 0.65f))
            : r.Byes > 0 ? ($"{r.Byes}b", new Color(0.7f, 0.74f, 0.82f))
            : r.LegByes > 0 ? ($"{r.LegByes}lb", new Color(0.7f, 0.74f, 0.82f))
            : r.Six ? ("6", Ribbon2)
            : r.Four ? ("4", UiKit.Gold)
            : r.Runs == 0 ? ("•", new Color(0.25f, 0.28f, 0.36f))
            : (r.Runs.ToString(), new Color(0.88f, 0.9f, 0.95f));

        /// <summary>Maidens: his completed overs that cost nothing (wides count against him).</summary>
        private int Maidens(QuickMatch m, Player bowler)
        {
            // The log doesn't carry the bowler: rebuild the overs, each bowled by the plan's man.
            var maidens = 0;
            var legal = 0;
            var runs = 0;
            var over = 0;
            foreach (var r in m.Log)
            {
                runs += r.Runs;
                if (!r.Legal) continue;
                if (++legal % 6 != 0) continue;
                if (runs == 0 && BowlerOf(m, over)?.Id == bowler.Id) maidens++;
                runs = 0;
                over++;
            }
            return maidens;
        }

        private Player? BowlerOf(QuickMatch m, int over) => OverBowlers.TryGetValue(over, out var p) ? p : null;

        /// <summary>Who bowled each over (0-based), filled in by the director as each starts (the engine's plan is private).</summary>
        public readonly Dictionary<int, Player> OverBowlers = new Dictionary<int, Player>();

        /// <summary>The current pair's stand: runs and balls since the last wicket.</summary>
        private static (int runs, int balls) Partnership(QuickMatch m)
        {
            var runs = 0;
            var balls = 0;
            for (var i = m.Log.Count - 1; i >= 0; i--)
            {
                var r = m.Log[i];
                if (r.Wicket) break;
                runs += r.Runs;
                if (r.Legal) balls++;
            }
            return (runs, balls);
        }

        // ------------------------------------------------------------ scorecard

        /// <summary>Pause menu: room for the SCORECARD button under the title.</summary>
        private void PauseExtras(RectTransform panel)
        {
            var back = (RectTransform)panel.parent;
            panel.sizeDelta += new Vector2(0, 234);
            back.sizeDelta += new Vector2(0, 234);
            UiKit.Btn(panel, "Scorecard", new Vector2(0.5f, 0), new Vector2(-114, 320), new Vector2(212, 90), "SCORECARD", Ribbon2, ShowScorecard, 24);
            UiKit.Btn(panel, "WagonWheel", new Vector2(0.5f, 0), new Vector2(114, 320), new Vector2(212, 90), "WAGON WHEEL", Ribbon2, () =>
            {
                var wheels = Wagons?.Invoke();
                if (ShowWagonExplorer()) return;
                if (wheels != null && wheels.Count > 0) ShowWagons(wheels, 0);
            }, 24);
            // Ball tracking is on every ball now (owner v26 #20): no KEY BALLS / EVERY BALL toggle.
        }

        /// <summary>The current pair's wagon wheels for the pause menu (set by the director).</summary>
        public Func<IReadOnlyList<(IReadOnlyList<WagonShot> shots, bool leftHanded, string title, string line)>>? Wagons;

        // The full scorecard (ShowScorecard) is in MatchHud.Scorecard.cs (owner v26 #19: tabs).

        // --------------------------------------------------------------- replay

        /// <summary>The replay's camera choices, as in the reference (AUTO, SIDE, BOWLER, STUMP CAM, AERIAL, BOUNDARY).</summary>
        public static readonly string[] ReplayCams = { "AUTO", "SIDE", "BOWLER", "STUMP CAM", "AERIAL", "BOUNDARY" };

        /// <summary>The replay bar along the bottom, <paramref name="selected"/> lit, and the REPLAY tag top left.</summary>
        public void ShowReplayBar(int selected, Action<int> pick, Action skip)
        {
            var rt = Piece(ref _replayBar, "ReplayBar", new Vector2(0.5f, 0), new Vector2(0, BarHeight + 52), new Vector2(1580, 88));
            var bar = UiKit.Box(rt, "Bar", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1580, 84), Night);
            UiKit.LabelAt(bar, new Vector2(0, 0.5f), new Vector2(110, 0), new Vector2(200, 60), "REPLAY\nCAMERA", 20, TextAnchor.MiddleCenter, UiKit.Gold);
            for (var i = 0; i < ReplayCams.Length; i++)
            {
                var k = i;
                var on = i == selected;
                UiKit.Btn(bar, "Cam_" + ReplayCams[i], new Vector2(0, 0.5f), new Vector2(290 + i * 180, 0), new Vector2(168, 60), ReplayCams[i], on ? UiKit.Gold : Ribbon, () => pick(k), 22);
            }
            UiKit.Btn(bar, "SkipReplay", new Vector2(1, 0.5f), new Vector2(-120, 0), new Vector2(210, 60), "SKIP ►", UiKit.Accent, () => skip(), 26);
            StartCoroutine(Animate(rt, new Vector2(0, -80), 0));
            if (_replayBug == null)
            {
                var bug = Piece(ref _replayBug, "ReplayBug", new Vector2(0, 1), new Vector2(380, -60), new Vector2(240, 64));
                Slant(bug, "Tag", Vector2.zero, new Vector2(240, 58), Ribbon2, 0);
                var t = UiKit.Label(bug, "▶ REPLAY", 32, TextAnchor.MiddleCenter, Color.white);
                t.fontStyle = FontStyle.BoldAndItalic;
            }
        }

        public void HideReplayBar()
        {
            if (_replayBar != null) Destroy(_replayBar);
            if (_replayBug != null) Destroy(_replayBug);
            _replayBar = _replayBug = null;
        }

        // ------------------------------------------------------------------ DRS

        /// <summary>The batting side's REVIEW button with its countdown ring; it goes when pressed or when time's up.</summary>
        public void ShowReview(float seconds, Action pressed)
        {
            var rt = Piece(ref _review, "Review", new Vector2(1, 0), new Vector2(-220, BarHeight + 240), new Vector2(240, 240));
            var ring = UiKit.Box(rt, "Ring", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(230, 230), UiKit.Gold);
            ring.GetComponent<Image>().sprite = Disc();
            ring.GetComponent<Image>().type = Image.Type.Filled;
            ring.GetComponent<Image>().fillMethod = Image.FillMethod.Radial360;
            ring.GetComponent<Image>().raycastTarget = false;
            var disc = UiKit.Box(rt, "Disc", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(196, 196), Ribbon2);
            disc.GetComponent<Image>().sprite = Disc();
            var label = UiKit.Label(disc, "REVIEW\nDRS", 36, TextAnchor.MiddleCenter, Color.white);
            label.fontStyle = FontStyle.BoldAndItalic;
            var done = false;
            disc.gameObject.AddComponent<OnPress>().Pressed = () =>
            {
                if (done) return;
                done = true;
                HideReview();
                pressed();
            };
            StartCoroutine(Countdown(rt, ring.GetComponent<Image>(), seconds));
            StartCoroutine(Animate(rt, new Vector2(200, 0), 0, 1.4f));
        }

        private IEnumerator Countdown(RectTransform rt, Image ring, float seconds)
        {
            for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (rt == null || ring == null) yield break;
                ring.fillAmount = 1 - t / seconds;
                yield return null;
            }
            if (rt != null && _review == rt.gameObject) HideReview();
        }

        public void HideReview()
        {
            if (_review != null) Destroy(_review);
            _review = null;
        }

        /// <summary>The Hawk-Eye read-out panel, right: a title, then its rows as they're revealed.</summary>
        public void ShowHawkeye(string title)
        {
            var rt = Piece(ref _hawk, "Hawkeye", new Vector2(1, 0.5f), new Vector2(-290, 60), new Vector2(500, 470));
            UiKit.Box(rt, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500, 470), Night).GetComponent<Image>().raycastTarget = false;
            var head = UiKit.Box(rt, "Head", new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(500, 68), Ribbon2);
            head.GetComponent<Image>().raycastTarget = false;
            var t = UiKit.Label(head, title, 30, TextAnchor.MiddleCenter, Color.white);
            t.fontStyle = FontStyle.BoldAndItalic;
            StartCoroutine(Animate(rt, new Vector2(300, 0), 0));
        }

        /// <summary>Row <paramref name="i"/> (0 pitching, 1 impact, 2 wickets) of the read-out.</summary>
        public void HawkeyeRow(int i, string label, string verdict, Color color)
        {
            if (_hawk == null) return;
            var rt = (RectTransform)_hawk.transform;
            var row = UiKit.Box(rt, "Row" + i, new Vector2(0.5f, 1), new Vector2(0, -120 - i * 82), new Vector2(460, 70), new Color(1, 1, 1, 0.06f));
            row.GetComponent<Image>().raycastTarget = false;
            UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(90, 0), new Vector2(160, 50), label, 22, TextAnchor.MiddleLeft, UiKit.Dim);
            var v = UiKit.Box(row, "Verdict", new Vector2(1, 0.5f), new Vector2(-136, 0), new Vector2(252, 54), color);
            v.GetComponent<Image>().raycastTarget = false;
            UiKit.Label(v, verdict, 24, TextAnchor.MiddleCenter, color.grayscale > 0.6f ? UiKit.Ink : Color.white);
            StartCoroutine(Animate(row, new Vector2(60, 0), 0));
        }

        /// <summary>The read-out's last line: the decision.</summary>
        public void HawkeyeDecision(string text, Color color)
        {
            if (_hawk == null) return;
            var rt = (RectTransform)_hawk.transform;
            var row = UiKit.Box(rt, "Decision", new Vector2(0.5f, 0), new Vector2(0, 54), new Vector2(460, 84), color);
            row.GetComponent<Image>().raycastTarget = false;
            var t = UiKit.Label(row, text, 44, TextAnchor.MiddleCenter, color.grayscale > 0.6f ? UiKit.Ink : Color.white);
            t.fontStyle = FontStyle.BoldAndItalic;
            StartCoroutine(Animate(row, Vector2.zero, 0, 1.4f));
        }

        public void HideHawkeye()
        {
            if (_hawk != null) Destroy(_hawk);
            _hawk = null;
        }
    }
}
