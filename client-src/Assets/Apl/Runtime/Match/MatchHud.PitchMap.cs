using System.Collections.Generic;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The over's pitch map (with the between-overs card): the batter's half of
    /// the pitch seen from behind the bowler, his stumps at the top, the length
    /// bands marked, and a dot where each ball of the over pitched — red a
    /// wicket, gold a boundary, white runs, blue-grey a dot.
    /// </summary>
    public sealed partial class MatchHud
    {
        private GameObject? _pitchMap;

        /// <summary>Metres of pitch shown (from the batter's stumps), and the sideways stretch (the pitch is narrow).</summary>
        private const float MapMetres = 12f, MapPxPerM = 27f, MapWide = 2.4f;

        /// <summary>The map beside the over card: <paramref name="balls"/> are world x and metres from the batter's stumps.</summary>
        public void ShowPitchMap(IReadOnlyList<(float x, float z, Color c)> balls, float seconds = 0)
        {
            var h = MapMetres * MapPxPerM;
            var rt = Piece(ref _pitchMap, "PitchMap", new Vector2(1, 0), new Vector2(-190, BarHeight + 250), new Vector2(300, h + 110));
            UiKit.Box(rt, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, h + 110), Night).GetComponent<Image>().raycastTarget = false;
            UiKit.LabelAt(rt, new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(280, 36), "PITCH MAP", 26, TextAnchor.MiddleCenter, Color.white);
            var pitchW = 3.05f * MapPxPerM * MapWide * 0.5f;
            var pitch = UiKit.Box(rt, "Pitch", new Vector2(0.5f, 1), new Vector2(0, -54 - h / 2), new Vector2(pitchW, h), new Color(0.82f, 0.72f, 0.5f));
            pitch.GetComponent<Image>().raycastTarget = false;
            // Length bands (pace): yorker < 2, full 2–4, good 4–7, short 7–9.5.
            foreach (var (z, name) in new[] { (2f, "FULL"), (4f, "GOOD"), (7f, "SHORT") })
            {
                var y = -z * MapPxPerM;
                UiKit.Box(pitch, "Band", new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(pitchW, 2), new Color(1, 1, 1, 0.45f)).GetComponent<Image>().raycastTarget = false;
                UiKit.LabelAt(pitch, new Vector2(0, 1), new Vector2(-38, y - 0.9f * MapPxPerM), new Vector2(70, 22), name, 14, TextAnchor.MiddleRight, UiKit.Dim);
            }
            // The stumps and the popping crease at the top.
            UiKit.Box(pitch, "Stumps", new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(0.23f * MapPxPerM * MapWide, 6), Color.white).GetComponent<Image>().raycastTarget = false;
            UiKit.Box(pitch, "Crease", new Vector2(0.5f, 1), new Vector2(0, -1.22f * MapPxPerM), new Vector2(pitchW + 30, 2), Color.white).GetComponent<Image>().raycastTarget = false;
            foreach (var (x, z, c) in balls)
            {
                // From behind the bowler the ground's +x is on the left of the picture.
                var px = Mathf.Clamp(-x * MapPxPerM * MapWide, -130, 130);
                var py = -Mathf.Clamp(z, 0.2f, MapMetres - 0.2f) * MapPxPerM;
                var dot = UiKit.Box(pitch, "Ball", new Vector2(0.5f, 1), new Vector2(px, py), new Vector2(18, 18), c);
                dot.GetComponent<Image>().sprite = Disc();
                dot.GetComponent<Image>().raycastTarget = false;
            }
            StartCoroutine(Animate(rt, new Vector2(240, 0), seconds));
        }

        public void HidePitchMap()
        {
            if (_pitchMap != null) Destroy(_pitchMap);
            _pitchMap = null;
        }
    }
}
