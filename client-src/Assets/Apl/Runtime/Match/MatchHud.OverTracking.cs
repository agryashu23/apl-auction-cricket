using System.Collections.Generic;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// OVER IN TRACKING's screen parts (MatchDirector.OverTracking.cs): a small title chip top
    /// left (the over, the bowler), the colour key along the bottom, and the numbers 1, 2, 3 … on
    /// the pitch discs, kept over them each frame. No verdict panel (PITCHING / IMPACT / WICKETS is
    /// the DRS review's alone).
    /// </summary>
    public sealed partial class MatchHud
    {
        private GameObject? _overTrack, _overTrackKey, _overTrackLabels;
        private readonly List<(RectTransform rt, Image disc)> _overTrackChips = new List<(RectTransform, Image)>();

        public void ShowOverTracking(string title, string bowler)
        {
            var rt = Piece(ref _overTrack, "OverTracking", new Vector2(0, 1), new Vector2(330, -170), new Vector2(560, 104));
            var bg = UiKit.Box(rt, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 104), Night);
            bg.GetComponent<Image>().raycastTarget = false;
            var head = UiKit.Box(rt, "Head", new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(560, 56), Ribbon2);
            head.GetComponent<Image>().raycastTarget = false;
            var t = UiKit.Label(head, title, 30, TextAnchor.MiddleCenter, Color.white);
            t.fontStyle = FontStyle.BoldAndItalic;
            UiKit.LabelAt(rt, new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(520, 40), bowler.Length > 0 ? "BOWLER  " + bowler : "", 22, TextAnchor.MiddleCenter, UiKit.Dim);
            StartCoroutine(Animate(rt, new Vector2(-300, 0), 0));

            // The key: dot · runs · boundary · wicket · extra.
            var key = Piece(ref _overTrackKey, "OverTrackKey", new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(980, 60));
            UiKit.Box(key, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980, 60), Night).GetComponent<Image>().raycastTarget = false;
            var items = new[] { ("DOT", MatchDirector.TrackDot), ("RUNS", MatchDirector.TrackRuns), ("4 / 6", MatchDirector.TrackBoundary), ("WICKET", MatchDirector.TrackWicket), ("WIDE / NO BALL", MatchDirector.TrackExtra) };
            float[] widths = { 140, 150, 150, 170, 250 };
            var x = -490f + 40f;
            for (var i = 0; i < items.Length; i++)
            {
                var dot = UiKit.Box(key, "KeyDot", new Vector2(0.5f, 0.5f), new Vector2(x, 0), new Vector2(22, 22), items[i].Item2);
                dot.GetComponent<Image>().sprite = Disc();
                dot.GetComponent<Image>().raycastTarget = false;
                UiKit.LabelAt(key, new Vector2(0.5f, 0.5f), new Vector2(x + 20 + (widths[i] - 40) / 2, 0), new Vector2(widths[i] - 40, 40), items[i].Item1, 22, TextAnchor.MiddleLeft, Color.white);
                x += widths[i];
            }

            // The numbers' layer, full screen; chips made once, placed every frame.
            var labels = Piece(ref _overTrackLabels, "OverTrackLabels", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080));
            _overTrackChips.Clear();
            for (var i = 0; i < OverTrackView.Max; i++)
            {
                var chip = UiKit.Box(labels, "Ball" + (i + 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40), Color.white);
                var img = chip.GetComponent<Image>();
                img.sprite = Disc();
                img.raycastTarget = false;
                UiKit.Label(chip, (i + 1).ToString(), 24, TextAnchor.MiddleCenter, UiKit.Ink);
                chip.gameObject.SetActive(false);
                _overTrackChips.Add((chip, img));
            }
        }

        /// <summary>The first <paramref name="shown"/> numbers over their pitch discs, as <paramref name="cam"/> sees them.</summary>
        public void OverTrackLabels(Camera? cam, IReadOnlyList<(Vector3 at, Color c)> spots, int shown)
        {
            if (_overTrackLabels == null || cam == null) return;
            var layer = (RectTransform)_overTrackLabels.transform;
            for (var i = 0; i < _overTrackChips.Count; i++)
            {
                var (rt, img) = _overTrackChips[i];
                var on = i < spots.Count && i < shown;
                if (on)
                {
                    var sp = cam.WorldToScreenPoint(spots[i].at);
                    var p = Vector2.zero;
                    on = sp.z > 0 && RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, sp, null, out p);
                    if (on)
                    {
                        // Just above the disc, nudged sideways by its number so close lengths don't stack.
                        rt.anchoredPosition = p + new Vector2((i % 2 == 0 ? -1 : 1) * 16f, 30f);
                        img.color = spots[i].c;
                    }
                }
                if (rt.gameObject.activeSelf != on) rt.gameObject.SetActive(on);
            }
        }

        public void HideOverTracking()
        {
            foreach (var slot in new[] { _overTrack, _overTrackKey, _overTrackLabels })
                if (slot != null) Destroy(slot);
            _overTrack = _overTrackKey = _overTrackLabels = null;
            _overTrackChips.Clear();
        }
    }
}
