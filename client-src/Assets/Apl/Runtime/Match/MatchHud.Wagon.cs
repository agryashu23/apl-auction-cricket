using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>One scoring shot: direction (deg, 0 straight, + off side), reach (0–1 of the rope), runs, his hand, the innings.</summary>
    public readonly struct WagonShot
    {
        public readonly float Angle, Reach;
        public readonly int Runs, Innings;
        public readonly bool LeftHanded;

        public WagonShot(float angle, float reach, int runs, bool leftHanded = false, int innings = 1)
        {
            Angle = angle;
            Reach = reach;
            Runs = runs;
            LeftHanded = leftHanded;
            Innings = innings;
        }
    }

    /// <summary>
    /// The wagon wheel (owner v26 #20: "looks bad", remade): the ground seen from high behind
    /// the batter's end in perspective (MatchHud.WagonGround.cs: grass, square, pitch, 30-yard
    /// circle, rope, boards, stands), each scoring shot a coloured run line from the crease —
    /// along the grass for the ground shots, a lofted arc with its shadow for a six, a dot where
    /// a boundary landed — drawing in one after another; the runs in each of eight regions
    /// (STRAIGHT, COVERS, POINT, THIRD MAN, BEHIND, FINE LEG, SQUARE LEG, MIDWICKET) on chips on
    /// the grass, the best region gold; under it the colour key (1s 2s 3s 4s 6s, how many and
    /// their runs) and the OFF / LEG split. Each shot is drawn for its own batter's hand (off
    /// side right for a right-hander), so a whole side's shots sit on one ground.
    /// Filters by batter and innings: MatchHud.WagonExplorer.cs.
    /// </summary>
    public sealed partial class MatchHud
    {
        private GameObject? _wagon;

        public static Color WagonColour(int runs) => runs switch
        {
            1 => Color.white,
            2 => new Color(0.4f, 0.85f, 1f),
            3 => new Color(0.45f, 0.95f, 0.5f),
            4 => UiKit.Gold,
            _ => new Color(0.85f, 0.45f, 1f),
        };

        /// <summary>The eight regions, right-hander's view, clockwise from straight (45° each).</summary>
        private static readonly string[] WagonRegions = { "STRAIGHT", "COVERS", "POINT", "THIRD MAN", "BEHIND", "FINE LEG", "SQUARE LEG", "MIDWICKET" };

        /// <summary>Where a shot points on the drawn ground (x right, y up the pitch), unit length.</summary>
        private static Vector2 WagonDir(WagonShot s)
        {
            var a = s.Angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a) * (s.LeftHanded ? -1 : 1), Mathf.Cos(a));
        }

        /// <summary>The shot's region on screen (0 straight, clockwise).</summary>
        private static int WagonRegion(WagonShot s)
        {
            var d = WagonDir(s);
            var deg = Mathf.Repeat(Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + 22.5f, 360f);
            return Mathf.Min(7, (int)(deg / 45f));
        }

        /// <summary>
        /// One wheel: the ground <paramref name="size"/> px wide, its title over it and the key and
        /// <paramref name="line"/> under it, centred at <paramref name="pos"/> of <paramref name="parent"/>.
        /// </summary>
        public RectTransform WagonWheel(RectTransform parent, Vector2 anchor, Vector2 pos, float size, IReadOnlyList<WagonShot> shots, bool leftHanded, string title, string line, bool animate = true)
        {
            var groundH = size / WagonAspect;
            const float top = 74f, bottom = 176f;
            var box = (RectTransform)PlayerCardKit.Shape(parent, "Wagon", CardSprites.Round(24), Night, pos, new Vector2(size + 40, groundH + top + bottom), anchor).transform;
            PlayerCardKit.Text(box, new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -top + 4), new Vector2(-20, -10), title, 38, TextAnchor.MiddleCenter, Color.white, condensed: true);
            var same = shots.Count == 0 || AllSameHand(shots);
            var hand = shots.Count > 0 && same ? shots[0].LeftHanded : leftHanded;
            var ground = WagonGround(box, size, new Vector2(0, (groundH + top + bottom) / 2 - top - groundH / 2), hand, same);
            var paths = new List<List<(RectTransform rt, float len)>>();
            // Ground shots first, the boundaries over them, the sixes' arcs on top.
            foreach (var s in shots.OrderBy(x => x.Runs))
            {
                var dir = WagonDir(s);
                var colour = WagonColour(s.Runs);
                var segs = new List<(RectTransform, float)>();
                if (s.Runs == 6)
                {
                    // In the air: an arc over the ground to a landing in the stands, its shadow on the grass.
                    var land = dir * 1.13f;
                    Segment(ground, WagonProject(Vector2.zero, size), WagonProject(land, size), 2f, new Color(0, 0, 0, 0.35f));
                    var prev = WagonProject(Vector2.zero, size, 0.02f);
                    const int n = 12;
                    for (var k = 1; k <= n; k++)
                    {
                        var u = k / (float)n;
                        var next = WagonProject(land * u, size, 0.02f + 0.42f * 4f * u * (1f - u));
                        segs.Add(Segment(ground, prev, next, 5f, colour, animate));
                        prev = next;
                    }
                    Dot(ground, WagonProject(land, size), 14f, colour);
                }
                else
                {
                    var end = dir * (s.Runs == 4 ? 0.995f : Mathf.Clamp(s.Reach, 0.08f, 0.97f));
                    segs.Add(Segment(ground, WagonProject(Vector2.zero, size), WagonProject(end, size), s.Runs == 4 ? 4.5f : 3f, UiKit.WithAlpha(colour, s.Runs == 4 ? 1f : 0.9f), animate));
                    if (s.Runs == 4) Dot(ground, WagonProject(end, size), 12f, colour);
                }
                paths.Add(segs);
            }
            RegionTotals(ground, shots, size, same, hand);
            Legend(box, shots, size);
            PlayerCardKit.Text(box, new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 8), new Vector2(-20, 52), line, 30, TextAnchor.MiddleCenter, UiKit.Gold, condensed: true);
            if (animate && paths.Count > 0) StartCoroutine(DrawIn(paths));
            return box;
        }

        /// <summary>A straight run line from <paramref name="a"/> to <paramref name="b"/> (zero length to start when it will grow).</summary>
        private static (RectTransform rt, float len) Segment(RectTransform parent, Vector2 a, Vector2 b, float width, Color colour, bool grow = false)
        {
            var d = b - a;
            var len = d.magnitude + width * 0.5f;
            var seg = UiKit.Box(parent, "Shot", new Vector2(0.5f, 0.5f), a, new Vector2(width, grow ? 0f : len), colour);
            seg.pivot = new Vector2(0.5f, 0);
            seg.anchoredPosition = a;
            seg.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg);
            seg.GetComponent<Image>().raycastTarget = false;
            return (seg, len);
        }

        private static void Dot(RectTransform parent, Vector2 at, float size, Color colour)
        {
            var ring = UiKit.Box(parent, "LandedRim", new Vector2(0.5f, 0.5f), at, new Vector2(size + 6, size + 6), new Color(0, 0, 0, 0.6f));
            ring.GetComponent<Image>().sprite = Disc();
            ring.GetComponent<Image>().raycastTarget = false;
            var dot = UiKit.Box(parent, "Landed", new Vector2(0.5f, 0.5f), at, new Vector2(size, size), colour);
            dot.GetComponent<Image>().sprite = Disc();
            dot.GetComponent<Image>().raycastTarget = false;
        }

        private static bool AllSameHand(IReadOnlyList<WagonShot> shots)
        {
            foreach (var s in shots) if (s.LeftHanded != shots[0].LeftHanded) return false;
            return true;
        }

        /// <summary>The shots grow out of the crease one after another, a six's arc segment by segment (real time: it runs paused too).</summary>
        private static IEnumerator DrawIn(List<List<(RectTransform rt, float len)>> paths)
        {
            var t0 = Time.unscaledTime;
            var step = Mathf.Min(0.06f, 1.2f / paths.Count);
            const float each = 0.3f;
            while (true)
            {
                var done = true;
                for (var i = 0; i < paths.Count; i++)
                {
                    var segs = paths[i];
                    if (segs.Count == 0) continue;
                    var k = Mathf.Clamp01((Time.unscaledTime - t0 - i * step) / each);
                    if (k < 1f) done = false;
                    var eased = 1f - Mathf.Pow(1f - k, 3f);
                    for (var j = 0; j < segs.Count; j++)
                    {
                        var (rt, len) = segs[j];
                        if (rt == null) yield break;
                        var part = Mathf.Clamp01(eased * segs.Count - j);
                        rt.sizeDelta = new Vector2(rt.sizeDelta.x, len * part);
                    }
                }
                if (done) yield break;
                yield return null;
            }
        }

        /// <summary>
        /// The runs in each region on chips on the grass (the region's name when the shots are one
        /// hand's, so COVERS is where the cover drives went), the best region gold.
        /// </summary>
        private static void RegionTotals(RectTransform ground, IReadOnlyList<WagonShot> shots, float size, bool named, bool leftHanded)
        {
            var runs = new int[8];
            foreach (var s in shots) runs[WagonRegion(s)] += s.Runs;
            var best = 0;
            for (var z = 1; z < 8; z++) if (runs[z] > runs[best]) best = z;
            var scale = Mathf.Max(0.75f, size / 760f);
            for (var z = 0; z < 8; z++)
            {
                var e = (z * 45f + 22.5f) * Mathf.Deg2Rad;
                // The region's edges, faint lines from the 30-yard circle to the rope.
                var edge = new Vector2(Mathf.Sin(e), Mathf.Cos(e));
                Segment(ground, WagonProject(edge * 0.44f, size), WagonProject(edge * 0.99f, size), 2f, new Color(1, 1, 1, 0.13f));
                if (runs[z] == 0) continue;
                var a = z * 45f * Mathf.Deg2Rad;
                var at = WagonProject(new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * 0.72f, size);
                var isBest = z == best;
                var name = named ? WagonRegions[leftHanded ? (8 - z) % 8 : z] : "";
                var chipW = (named ? 130f : 70f) * scale;
                var chipH = (named ? 64f : 44f) * scale;
                var chip = PlayerCardKit.Shape(ground, "Region" + z, CardSprites.Round(12), isBest ? UiKit.WithAlpha(UiKit.Gold, 0.95f) : new Color(0.02f, 0.03f, 0.07f, 0.8f), at, new Vector2(chipW, chipH));
                var rt = (RectTransform)chip.transform;
                var ink = isBest ? UiKit.Ink : Color.white;
                if (named)
                {
                    PlayerCardKit.Text(rt, new Vector2(0, 0.56f), Vector2.one, new Vector2(4, 0), new Vector2(-4, -2), name, Mathf.RoundToInt(18 * scale), TextAnchor.MiddleCenter, isBest ? UiKit.Ink : UiKit.Dim, condensed: true);
                    PlayerCardKit.Text(rt, Vector2.zero, new Vector2(1, 0.6f), new Vector2(4, 2), new Vector2(-4, 0), runs[z].ToString(), Mathf.RoundToInt(30 * scale), TextAnchor.MiddleCenter, ink, condensed: true);
                }
                else PlayerCardKit.Text(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, runs[z].ToString(), Mathf.RoundToInt(28 * scale), TextAnchor.MiddleCenter, ink, condensed: true);
            }
        }

        /// <summary>
        /// Under the ground: the colour key with how many of each and their runs ("4s  3 · 12"),
        /// then the OFF / LEG split of the runs as a bar.
        /// </summary>
        private static void Legend(RectTransform box, IReadOnlyList<WagonShot> shots, float size)
        {
            var kinds = new[] { 1, 2, 3, 4, 6 };
            var w = Mathf.Min(170f, (size + 10) / kinds.Length);
            for (var i = 0; i < kinds.Length; i++)
            {
                var n = shots.Count(s => s.Runs == kinds[i]);
                var x = (i - (kinds.Length - 1) / 2f) * w;
                var dot = UiKit.Box(box, "Key" + kinds[i], new Vector2(0.5f, 0), new Vector2(x - w * 0.38f, 138), new Vector2(20, 20), WagonColour(kinds[i]));
                dot.GetComponent<Image>().sprite = Disc();
                dot.GetComponent<Image>().raycastTarget = false;
                PlayerCardKit.Text(box, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(x - w * 0.38f + 16, 122), new Vector2(x + w * 0.5f, 154),
                    $"{kinds[i]}s  {n}" + (n > 0 ? $" · {n * kinds[i]}" : ""), 24, TextAnchor.MiddleLeft, n > 0 ? Color.white : UiKit.Dim, condensed: true);
            }
            // OFF / LEG: by each shot's own hand (a left-hander's off side is on the left of the ground).
            int off = 0, leg = 0;
            foreach (var s in shots)
            {
                var side = Mathf.Sin(s.Angle * Mathf.Deg2Rad);
                if (side > 0.05f) off += s.Runs;
                else if (side < -0.05f) leg += s.Runs;
            }
            var barW = Mathf.Min(520f, size - 220f);
            const float y = 78f;
            PlayerCardKit.Shape(box, "SplitBack", CardSprites.Round(8), new Color(1, 1, 1, 0.1f), new Vector2(0, y), new Vector2(barW, 16), new Vector2(0.5f, 0));
            if (off > 0)
            {
                var offW = Mathf.Max(16f, barW * off / Mathf.Max(1, off + leg));
                PlayerCardKit.Shape(box, "SplitOff", CardSprites.Round(8), new Color(0.35f, 0.65f, 1f), new Vector2(-barW / 2 + offW / 2, y), new Vector2(offW, 16), new Vector2(0.5f, 0));
            }
            PlayerCardKit.Text(box, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-barW / 2 - 160, y - 18), new Vector2(-barW / 2 - 12, y + 18), $"OFF {off}", 24, TextAnchor.MiddleRight, new Color(0.55f, 0.78f, 1f), condensed: true);
            PlayerCardKit.Text(box, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(barW / 2 + 12, y - 18), new Vector2(barW / 2 + 160, y + 18), $"{leg} LEG", 24, TextAnchor.MiddleLeft, UiKit.Dim, condensed: true);
        }
    }
}
