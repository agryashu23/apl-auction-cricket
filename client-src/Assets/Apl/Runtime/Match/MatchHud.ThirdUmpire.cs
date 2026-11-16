using System.Collections;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The third umpire's screens: the "REFERRED TO THIRD UMPIRE" strip across the
    /// top while the replays roll (with the angle on air, "CHECKING…" on the
    /// frozen frame), then the big-screen panel — DECISION PENDING flashing, and
    /// OUT or NOT OUT.
    /// </summary>
    public sealed partial class MatchHud
    {
        private GameObject? _referral, _bigScreen;
        private Text? _referralSub;

        /// <summary>The strip across the top: what's being checked.</summary>
        public void ShowReferral(string check)
        {
            var rt = Piece(ref _referral, "Referral", new Vector2(0.5f, 1), new Vector2(0, -90), new Vector2(1100, 132));
            Slant(rt, "Back", Vector2.zero, new Vector2(1100, 120), Night);
            Slant(rt, "Edge", new Vector2(0, -60), new Vector2(1100, 6), UiKit.Gold);
            var tv = UiKit.Box(rt, "TV", new Vector2(0, 0.5f), new Vector2(80, 0), new Vector2(110, 90), Ribbon2);
            tv.GetComponent<Image>().raycastTarget = false;
            UiKit.Label(tv, "TV", 44, TextAnchor.MiddleCenter, Color.white).fontStyle = FontStyle.BoldAndItalic;
            var title = UiKit.LabelAt(rt, new Vector2(0.5f, 0.5f), new Vector2(60, 22), new Vector2(900, 54), "REFERRED TO THIRD UMPIRE", 44, TextAnchor.MiddleCenter, Color.white);
            title.fontStyle = FontStyle.BoldAndItalic;
            _referralSub = UiKit.LabelAt(rt, new Vector2(0.5f, 0.5f), new Vector2(60, -30), new Vector2(900, 40), check, 30, TextAnchor.MiddleCenter, UiKit.Gold);
            StartCoroutine(Animate(rt, new Vector2(0, 140), 0));
        }

        /// <summary>The strip's second line: the check and the angle ("RUN OUT · SIDE-ON", "CHECKING…").</summary>
        public void ReferralStatus(string text)
        {
            if (_referralSub != null) _referralSub.text = text;
        }

        public void HideReferral()
        {
            if (_referral != null) Destroy(_referral);
            if (_bigScreen != null) Destroy(_bigScreen);
            _referral = _bigScreen = null;
            _referralSub = null;
        }

        /// <summary>The big screen: DECISION PENDING, flashing, for <paramref name="seconds"/>; then the verdict, held.</summary>
        public IEnumerator BigScreen(bool isOut, float seconds = 1.6f)
        {
            var rt = Piece(ref _bigScreen, "BigScreen", new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1200, 420));
            var frame = Slant(rt, "Frame", Vector2.zero, new Vector2(1200, 420), UiKit.Gold);
            var face = Slant(rt, "Face", Vector2.zero, new Vector2(1176, 396), Night);
            var word = UiKit.Label(face, "DECISION\nPENDING", 120, TextAnchor.MiddleCenter, Color.white);
            word.fontStyle = FontStyle.BoldAndItalic;
            StartCoroutine(Animate(rt, Vector2.zero, 0, 0.6f));
            var faceImg = face.GetComponent<Image>();
            for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (rt == null) yield break;
                ClockHeld(); // the referral's frozen frame is still on
                // The pending flash: the panel pulses between night and purple.
                faceImg.color = Color.Lerp(Night, Ribbon2, 0.5f + 0.5f * Mathf.Sin(t * 9f));
                yield return null;
            }
            if (rt == null) yield break;
            var c = isOut ? UiKit.Bad : UiKit.Good;
            faceImg.color = UiKit.WithAlpha(c, 0.95f);
            frame.GetComponent<Image>().color = Color.white;
            word.text = isOut ? "OUT" : "NOT OUT";
            word.fontSize = 230;
            word.resizeTextMaxSize = 230;
            rt.localScale = Vector3.one * 1.15f;
            for (var t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.2f)
            {
                if (rt == null) yield break;
                ClockHeld();
                rt.localScale = Vector3.one * Mathf.Lerp(1.15f, 1f, t);
                yield return null;
            }
        }
    }
}
