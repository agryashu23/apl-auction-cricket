using System.Collections;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// (batting) The milestone's caption as a broadcast lower third (v26 celebrations review: the centre banner sat
    /// over his head and torso and hid the celebration): a strip across the bottom fifth of the screen, the
    /// FIFTY / HUNDRED and the name · runs (balls) under it. Made after any CleanFeed, so it stays up through one.
    /// </summary>
    public sealed partial class MatchHud
    {
        private GameObject? _thirdRoot;
        private Text? _thirdTitle, _thirdSub;
        private Coroutine? _thirdHide;

        public void LowerThird(string text, string sub, Color color, float seconds)
        {
            if (_root == null) return;
            if (_thirdRoot == null)
            {
                // Bottom centre: 150 px tall, its middle 125 px up (of 1080): the bottom ~19 % of the screen.
                var box = UiKit.Box(_root, "LowerThird", new Vector2(0.5f, 0f), new Vector2(0, 125), new Vector2(1180, 150), new Color(0.04f, 0.05f, 0.1f, 0.84f));
                _thirdRoot = box.gameObject;
                _thirdTitle = UiKit.Label(UiKit.Strip(box, 0.42f, 1f), "", 66, TextAnchor.MiddleCenter);
                _thirdSub = UiKit.Label(UiKit.Strip(box, 0f, 0.42f), "", 30, TextAnchor.MiddleCenter);
            }
            _thirdRoot.SetActive(true);
            _thirdRoot.transform.SetAsLastSibling();
            _thirdTitle!.text = text;
            _thirdTitle.color = color;
            _thirdSub!.text = sub;
            if (_thirdHide != null) StopCoroutine(_thirdHide);
            _thirdHide = StartCoroutine(HideThird(seconds));
        }

        public void HideLowerThird()
        {
            if (_thirdHide != null) StopCoroutine(_thirdHide);
            _thirdHide = null;
            if (_thirdRoot != null) _thirdRoot.SetActive(false);
        }

        private IEnumerator HideThird(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _thirdHide = null;
            if (_thirdRoot != null) _thirdRoot.SetActive(false);
        }
    }
}
