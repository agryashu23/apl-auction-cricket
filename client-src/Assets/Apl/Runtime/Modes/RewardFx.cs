using System.Collections;
using System.Collections.Generic;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Modes
{
    /// <summary>
    /// The rewards screen's pictures and motion (round 10): a clean five-pointed star,
    /// the XP bar, and the reveal — the XP counting up, the bar filling from where it was
    /// (to the top and over for a level-up, with a flash), then each unlock popping in.
    /// </summary>
    public static class RewardFx
    {
        public static readonly Color StarOn = new Color(1f, 0.8f, 0.2f), StarOff = new Color(1f, 1f, 1f, 0.16f);

        private static Sprite? _star;

        /// <summary>A five-pointed star (straight edges, 4× supersampled), white on clear.</summary>
        public static Sprite Star
        {
            get
            {
                if (_star != null) return _star;
                const int n = 96;
                var pts = new Vector2[10];
                for (var i = 0; i < 10; i++)
                {
                    var a = Mathf.PI / 2 + i * Mathf.PI / 5;
                    var r = i % 2 == 0 ? 0.96f : 0.4f;
                    pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a) - 0.06f) * r;
                }
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "star" };
                var px = new Color32[n * n];
                for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var hits = 0;
                    for (var s = 0; s < 16; s++)
                    {
                        var p = new Vector2(x + (s % 4 + 0.5f) / 4f - n / 2f, y + (s / 4 + 0.5f) / 4f - n / 2f) / (n / 2f);
                        if (Inside(p, pts)) hits++;
                    }
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(hits * 255 / 16));
                }
                tex.SetPixels32(px);
                tex.Apply();
                return _star = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            }
        }

        private static bool Inside(Vector2 p, Vector2[] poly)
        {
            var inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        /// <summary>Three stars, the earned ones gold.</summary>
        public static List<Image> StarRow(RectTransform parent, Vector2 pos, int stars, float size, Vector2? anchor = null)
        {
            var list = new List<Image>();
            for (var i = 0; i < 3; i++)
                list.Add(PlayerCardKit.Shape(parent, "Star" + i, Star, i < stars ? StarOn : StarOff, pos + new Vector2((i - 1) * size * 1.1f, 0), new Vector2(size, size), anchor ?? new Vector2(0.5f, 1)));
            return list;
        }

        /// <summary>An XP bar at <paramref name="k"/> (0–1); returns its fill and its caption for animating.</summary>
        public static (RectTransform fill, Text caption) Bar(RectTransform parent, Vector2 pos, float width, float k, string caption, Vector2? anchor = null)
        {
            var a = anchor ?? new Vector2(0.5f, 1);
            PlayerCardKit.Shape(parent, "XpTrack", CardSprites.Round(10), new Color(1, 1, 1, 0.12f), pos, new Vector2(width, 24), a);
            var fill = (RectTransform)PlayerCardKit.Shape(parent, "XpFill", CardSprites.Round(10), UiKit.Gold, pos, new Vector2(width, 24), a).transform;
            Set(fill, width, k, pos);
            var text = PlayerCardKit.Text(parent, a, a, pos + new Vector2(-width / 2, -44), pos + new Vector2(width / 2, -16), caption, 20, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            return (fill, text);
        }

        public static void Set(RectTransform fill, float width, float k, Vector2 centre)
        {
            k = Mathf.Clamp01(k);
            fill.sizeDelta = new Vector2(Mathf.Max(1f, width * k), fill.sizeDelta.y);
            fill.anchoredPosition = centre + new Vector2(-width * (1 - k) / 2, 0);
            fill.gameObject.SetActive(k > 0.005f);
        }

        /// <summary>The static bar for the career column and the home chip: XP into the current level.</summary>
        public static void LevelBar(RectTransform parent, Vector2 pos, float width, Vector2? anchor = null)
        {
            var (into, size) = Progression.LevelProgress();
            Bar(parent, pos, width, into / (float)Mathf.Max(1, size), $"{into} / {size} XP to level {Progression.Level + 1}", anchor);
        }

        /// <summary>A quick pop-in: from small and clear to full size.</summary>
        public static IEnumerator Pop(Transform t, float delay)
        {
            t.localScale = Vector3.zero;
            yield return new WaitForSecondsRealtime(delay);
            for (var e = 0f; e < 0.28f; e += Time.unscaledDeltaTime)
            {
                var k = e / 0.28f;
                t.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(k * Mathf.PI)) * Mathf.Min(1f, k * 1.6f);
                yield return null;
            }
            t.localScale = Vector3.one;
        }
    }
}
