using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The running inset: the side-on picture of the batters (<see cref="RunningInset"/>)
    /// in a framed box at the bottom of the screen, ~28% of its width, with the
    /// running call on its strip — the RUN / STAY hint when the user is calling
    /// the runs, else "RUNNING".
    /// </summary>
    public sealed partial class MatchHud
    {
        private GameObject? _inset;
        private RawImage _insetPic = null!;
        private Text _insetLabel = null!;

        /// <summary>Show the inset with <paramref name="picture"/>, or take it away (null).</summary>
        public void ShowInset(Texture? picture)
        {
            if (picture == null)
            {
                if (_inset != null) _inset.SetActive(false);
                return;
            }
            if (_inset == null) BuildInset();
            // The speed pill sits where the inset goes.
            if (_speed != null) Destroy(_speed);
            _insetPic.texture = picture;
            _inset!.SetActive(true);
            _inset.transform.SetAsLastSibling();
        }

        private void BuildInset()
        {
            BroadcastInit();
            // Bottom centre, above the score strip: 540 × 304 of the 1920 × 1080 layout (~28% wide).
            var box = UiKit.Box(_bc, "RunningInset", new Vector2(0.5f, 0), new Vector2(0, BarHeight + 176), new Vector2(552, 344), UiKit.Gold);
            box.GetComponent<Image>().raycastTarget = false;
            _inset = box.gameObject;
            var pic = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform)pic.transform;
            rt.SetParent(box, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -6);
            rt.sizeDelta = new Vector2(540, 304);
            _insetPic = pic.GetComponent<RawImage>();
            _insetPic.raycastTarget = false;
            var strip = UiKit.Box(box, "Strip", new Vector2(0.5f, 0), new Vector2(0, 17), new Vector2(540, 28), Night);
            strip.GetComponent<Image>().raycastTarget = false;
            _insetLabel = UiKit.Label(strip, "RUNNING", 20, TextAnchor.MiddleCenter, Color.white);
        }

        /// <summary>Each frame the inset's on: the strip mirrors the running call.</summary>
        public void UpdateInset()
        {
            if (_inset == null || !_inset.activeSelf) return;
            var calling = _runRoot != null && _runRoot.activeSelf && _runHint != null && _runHint.text.Length > 0;
            _insetLabel.text = calling ? _runHint!.text : "BATTERS RUNNING";
            _insetLabel.color = calling ? _runHint!.color : Color.white;
        }
    }
}
