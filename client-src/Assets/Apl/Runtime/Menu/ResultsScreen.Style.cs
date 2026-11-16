using System.Linq;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The results screen's look, layered over whatever each tab built (UI/UX dev, rater r1 §8:
    /// make the podium / standings / awards rich and readable; the points are bunched together).
    /// Runs after every tab is drawn (ResultsScreen.Show), so the tabs' content and the solo
    /// developer's rank labels stay exactly as they are; this only adds and restyles:
    ///  - every text-bearing panel and table row opaque (UiTheme: linear-space see-through);
    ///  - PODIUM: a light column rising behind each block in the team's colour, a gold floor glow,
    ///    a gold cap on each block, and the MARGIN on the block ("+19 PTS AHEAD" / "19 BEHIND 1ST"),
    ///    so 600 / 599 / 598 reads as a race, not three equal numbers;
    ///  - STANDINGS: a points bar on every row scaled from the field's lowest to its highest score
    ///    (a 1-point gap is visible) and the gap to the leader in numbers;
    ///  - AWARDS: the bought player's head-shot beside the crest, a glow under the crest.
    /// </summary>
    public sealed partial class ResultsScreen
    {
        /// <summary>
        /// The NEWEST child called <paramref name="name"/>: UiKit.Clear destroys the old tab at the end
        /// of the frame, so the old tab's objects are still there (and first) when this pass runs.
        /// </summary>
        private static RectTransform? Newest(Transform parent, string name)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var c = parent.GetChild(i);
                if (c.name == name) return c as RectTransform;
            }
            return null;
        }

        private void StyleTab(string tab)
        {
            OpaquePanels();
            if (_rewards == null) return;
            switch (tab)
            {
                case "podium": StylePodium(); break;
                case "standings": StyleStandings(); break;
                case "awards": StyleAwards(); break;
            }
        }

        /// <summary>Near-opaque faces and rows to fully opaque; the selected (gold-tint) row to an opaque gold-blend.</summary>
        private void OpaquePanels()
        {
            foreach (var img in _body.GetComponentsInChildren<Image>(true))
            {
                var c = img.color;
                if (c.a >= 0.8f && c.a < 1f) img.color = new Color(c.r, c.g, c.b, 1f);
                else if (img.name == "Row" && c.a > 0.2f && c.a < 0.4f) img.color = Color.Lerp(UiTheme.RowA, new Color(c.r, c.g, c.b, 1f), 0.4f);
                else if (img.name == "Total" && c.a < 0.3f) img.color = Color.Lerp(UiTheme.Panel, new Color(c.r, c.g, c.b, 1f), 0.25f);
            }
        }

        private void StylePodium()
        {
            var r = _rewards!;
            var top = r.Standings.Take(3).ToList();
            if (top.Count == 0) return;
            // Floor glow under all three blocks.
            var floor = PlayerCardKit.Shape(_body, "PodiumFloor", CardSprites.Glow(40, 80), new Color(0.95f, 0.72f, 0.25f, 0.18f), new Vector2(0, 62), new Vector2(1500, 90), new Vector2(0.5f, 0));
            floor.transform.SetAsFirstSibling();
            for (var i = 0; i < top.Count; i++)
            {
                if (!(Newest(_body, "Podium_" + (i + 1)) is RectTransform back)) continue;
                var team = Team(top[i].TeamId);
                var colour = PlayerCardKit.Bright(UiKit.ParseHex(team.PrimaryColor));
                var x = back.anchoredPosition.x - 6;
                var h = back.sizeDelta.y;
                // The light column: bright at the block's top, fading up behind the crest.
                var beam = PlayerCardKit.Shape(_body, "Beam_" + (i + 1), CardSprites.FadeUp(), UiKit.WithAlpha(colour, i == 0 ? 0.38f : 0.24f), new Vector2(x, 60 + h + 230), new Vector2(340, 460), new Vector2(0.5f, 0));
                beam.transform.SetSiblingIndex(1);
                // Gold cap along the block's top edge.
                if (back.Find("Face") is RectTransform face)
                {
                    PlayerCardKit.Shape(face, "Cap", CardSprites.Round(2), i == 0 ? UiKit.Gold : new Color(0.85f, 0.87f, 0.92f), new Vector2(0, -3), new Vector2(face.sizeDelta.x, 6), new Vector2(0.5f, 1));
                    // The margin, mid-block, where there's room (the 3rd block is short).
                    if (h >= 200 && top.Count > 1)
                    {
                        var gap = i == 0 ? top[0].Points - top[1].Points : top[0].Points - top[i].Points;
                        var text = i == 0 ? (gap > 0 ? $"+{gap:N0} PTS AHEAD" : "LEVEL ON POINTS") : (gap > 0 ? $"{gap:N0} BEHIND 1ST" : "LEVEL WITH 1ST");
                        UiTheme.Chip(face, "Margin", new Vector2(0.5f, 0.5f), new Vector2(0, i == 0 ? 10 : -2), new Vector2(250, 40), text, new Color(0, 0, 0, 0.45f), Color.white);
                    }
                }
            }
        }

        private void StyleStandings()
        {
            var r = _rewards!;
            if (r.Standings.Count == 0) return;
            var max = r.Standings.Max(s => s.Points);
            var min = r.Standings.Min(s => s.Points);
            // Scale from a little under the lowest score, so the bars spread the field.
            var floor = min - Mathf.Max(5, (max - min) / 4);
            var span = Mathf.Max(1, max - floor);
            if (!(Newest(_body, "Standings") is RectTransform table)) return;
            foreach (var s in r.Standings)
            {
                if (!(table.Find("Standing_" + s.TeamId) is RectTransform row)) continue;
                // Inside the PLAYER column's right half (row: 1000 wide; PLAYER spans 360-800).
                const float x0 = 600f, w = 150f;
                var frac = (s.Points - floor) / (float)span;
                var colour = PlayerCardKit.Bright(UiKit.ParseHex(Team(s.TeamId).PrimaryColor));
                PlayerCardKit.Shape(row, "PtsTrack", CardSprites.Round(5), new Color(1, 1, 1, 0.08f), new Vector2(x0 + w / 2f, 0), new Vector2(w, 12), new Vector2(0, 0.5f));
                var fw = Mathf.Max(12f, w * frac);
                PlayerCardKit.Shape(row, "PtsBar", CardSprites.Round(5), colour, new Vector2(x0 + fw / 2f, 0), new Vector2(fw, 12), new Vector2(0, 0.5f));
                var gap = max - s.Points;
                UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(x0 + w + 30, 0), new Vector2(56, 34), gap == 0 ? "LEAD" : "−" + gap.ToString("N0"), 18, TextAnchor.MiddleLeft, gap == 0 ? UiKit.Gold : UiKit.Dim, bold: true);
            }
        }

        private void StyleAwards()
        {
            var awards = _rewards!.Awards;
            for (var i = 0; i < awards.Count && i < 8; i++)
            {
                var a = awards[i];
                var name = "Award_" + (a.Id.Length > 0 ? a.Id : i.ToString());
                if (!(Newest(_body, name) is RectTransform back) || !(back.Find("Face") is RectTransform card)) continue;
                var crest = card.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("Crest_")) as RectTransform;
                var player = a.PlayerId != null ? _data.Players.FirstOrDefault(p => p.Id == a.PlayerId) : null;
                if (crest != null)
                {
                    var glow = PlayerCardKit.Shape(card, "CrestGlow", CardSprites.Glow(30, 40), new Color(0.95f, 0.76f, 0.31f, 0.18f), crest.anchoredPosition, new Vector2(150, 150), new Vector2(0.5f, 1));
                    glow.transform.SetSiblingIndex(crest.GetSiblingIndex());
                    if (player != null)
                    {
                        // Crest left, the player's head-shot right.
                        crest.anchoredPosition = new Vector2(-80, crest.anchoredPosition.y);
                        glow.rectTransform.anchoredPosition = crest.anchoredPosition;
                        var pic = UiKit.CardPortrait(card, "AwardPortrait", new Vector2(0.5f, 1), new Vector2(80, crest.anchoredPosition.y), new Vector2(96, 112));
                        pic.texture = Portraits.For(player, 128);
                    }
                }
            }
        }
    }
}
