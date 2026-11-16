using System;
using System.Collections.Generic;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The wagon wheel's ground (owner v26 #20: "looks bad"): the ground seen from a camera
    /// high behind the batter's end, tilted ~40° off top-down, in true perspective — painted
    /// once into a texture (mowing stripes, grain, the lighter square, the pitch with its
    /// creases, the dashed 30-yard circle, the rope, the boundary boards and the stands), and
    /// a projection (<see cref="WagonProject"/>) that puts any ground point (rope radius 1,
    /// batter at the origin, y up the pitch, h above the grass) onto it, so the shots drawn
    /// over it sit on the same ground. Also the panels the wheels are shown on.
    /// </summary>
    public sealed partial class MatchHud
    {
        // The camera: tilt from straight down, distance (rope radii); the texture's window on the projected plane.
        private const float WagonTilt = 40f * Mathf.Deg2Rad, WagonDist = 3f;
        private const float WagonMinX = -1.25f, WagonMaxX = 1.25f, WagonMinY = -1.12f, WagonMaxY = 0.86f;
        private const int WagonTexW = 640;
        private static readonly int WagonTexH = Mathf.RoundToInt(WagonTexW * (WagonMaxY - WagonMinY) / (WagonMaxX - WagonMinX));
        private static Texture2D? _wagonGround;

        /// <summary>Width / height of the drawn ground.</summary>
        public static float WagonAspect => (WagonMaxX - WagonMinX) / (WagonMaxY - WagonMinY);

        /// <summary>A ground point (x right, y up the pitch, h up; rope radius 1) on the projected plane.</summary>
        private static Vector2 WagonPlane(float x, float y, float h = 0f)
        {
            float s = Mathf.Sin(WagonTilt), c = Mathf.Cos(WagonTilt);
            var z = y * s - h * c + WagonDist;
            return new Vector2(x, y * c + h * s) * (WagonDist / z);
        }

        /// <summary>
        /// A ground point in the ground image's local pixels (its centre the origin) for an image
        /// <paramref name="width"/> px wide.
        /// </summary>
        private static Vector2 WagonProject(Vector2 ground, float width, float h = 0f)
        {
            var p = WagonPlane(ground.x, ground.y, h);
            var scale = width / (WagonMaxX - WagonMinX);
            return new Vector2(p.x - (WagonMinX + WagonMaxX) / 2f, p.y - (WagonMinY + WagonMaxY) / 2f) * scale;
        }

        /// <summary>The ground image, <paramref name="width"/> px wide, centred at <paramref name="pos"/> of <paramref name="box"/>.</summary>
        private static RectTransform WagonGround(RectTransform box, float width, Vector2 pos, bool leftHanded, bool sides)
        {
            var go = new GameObject("Ground", typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform)go.transform;
            rt.SetParent(box, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, width / WagonAspect);
            var raw = go.GetComponent<RawImage>();
            raw.texture = GroundTexture();
            raw.raycastTarget = false;
            if (sides)
            {
                // OFF / LEG on the grass either side of the square.
                var offRight = !leftHanded;
                foreach (var (label, right) in new[] { ("OFF", offRight), ("LEG", !offRight) })
                {
                    var at = WagonProject(new Vector2(right ? 0.62f : -0.62f, -0.05f), width);
                    PlayerCardKit.Text(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), at - new Vector2(60, 18), at + new Vector2(60, 18), label, 24, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.4f), condensed: true);
                }
            }
            return rt;
        }

        // ------------------------------------------------------------ the painted ground

        private static float Hash(int x, int y)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        private static Color32 C32(Color c) => new Color32((byte)(Mathf.Clamp01(c.r) * 255), (byte)(Mathf.Clamp01(c.g) * 255), (byte)(Mathf.Clamp01(c.b) * 255), 255);

        /// <summary>Painted once (about 0.3 M pixels, a few tens of ms), then cached for every wheel.</summary>
        private static Texture2D GroundTexture()
        {
            if (_wagonGround != null) return _wagonGround;
            int w = WagonTexW, h = WagonTexH;
            var px = new Color32[w * h];
            float s = Mathf.Sin(WagonTilt), c = Mathf.Cos(WagonTilt), d = WagonDist;
            var grassA = new Color(0.13f, 0.47f, 0.19f);
            var grassB = new Color(0.17f, 0.55f, 0.23f);
            var square = new Color(0.30f, 0.60f, 0.27f);
            var pitch = new Color(0.82f, 0.71f, 0.48f);
            var night = new Color(0.02f, 0.03f, 0.07f);
            var boards = new[] { new Color(0.16f, 0.12f, 0.45f), new Color(0.75f, 0.2f, 0.25f), new Color(0.1f, 0.35f, 0.6f), new Color(0.85f, 0.55f, 0.1f) };
            for (var j = 0; j < h; j++)
            {
                var sy = Mathf.Lerp(WagonMinY, WagonMaxY, (j + 0.5f) / h);
                // The plane under this row (ground y), from the inverse of the projection.
                var den = d * c - sy * s;
                var gy = den > 0.05f ? sy * d / den : 99f;
                var zc = gy * s + d;
                // Depth light: the far side a touch darker, the near side brighter.
                var shade = Mathf.Lerp(1.08f, 0.82f, Mathf.InverseLerp(-1f, 1f, gy));
                for (var i = 0; i < w; i++)
                {
                    var sx = Mathf.Lerp(WagonMinX, WagonMaxX, (i + 0.5f) / w);
                    var gx = sx * zc / d;
                    var r = Mathf.Sqrt(gx * gx + gy * gy);
                    var n = Hash(i, j);
                    Color col;
                    if (gy > 50f || r > 1.32f)
                    {
                        col = night;
                    }
                    else if (r > 1.045f)
                    {
                        // The stands: rows of seats with a speckled crowd, darker up the tiers.
                        var tier = Mathf.InverseLerp(1.045f, 1.32f, r);
                        var row = Mathf.Repeat(r * 90f, 1f) < 0.3f ? 0.75f : 1f;
                        var crowd = n > 0.55f ? Color.Lerp(new Color(0.55f, 0.5f, 0.6f), new Color(0.9f, 0.75f, 0.35f), Hash(j, i)) : new Color(0.2f, 0.22f, 0.32f);
                        col = Color.Lerp(crowd * row, night, tier * 0.85f);
                    }
                    else if (r > 1.012f)
                    {
                        // The boundary boards: coloured panels round the rope.
                        var ang = Mathf.Atan2(gx, gy) / (Mathf.PI * 2f) + 0.5f;
                        col = boards[(int)(ang * 36f) % boards.Length] * 0.9f;
                    }
                    else if (r > 0.993f)
                    {
                        col = new Color(0.97f, 0.95f, 0.88f);
                    }
                    else
                    {
                        // Grass: mowing stripes across the ground, grain, a soft vignette to the rope.
                        var band = Mathf.FloorToInt((gy + 2f) / 0.12f) % 2 == 0;
                        col = band ? grassA : grassB;
                        col *= 0.94f + n * 0.1f;
                        col *= Mathf.Lerp(1.04f, 0.9f, r * r);
                        // The square, the pitch and its creases (batter's end at the origin).
                        if (Mathf.Abs(gx) < 0.13f && gy > -0.12f && gy < 0.42f) col = Color.Lerp(col, square, 0.35f);
                        if (Mathf.Abs(gx) < 0.034f && gy > -0.035f && gy < 0.315f)
                        {
                            col = pitch * (0.92f + n * 0.1f);
                            // Scuffed ends where the bowlers land.
                            if (gy < 0.06f || gy > 0.24f) col *= 0.9f;
                        }
                        if ((Mathf.Abs(gy) < 0.005f || Mathf.Abs(gy - 0.28f) < 0.005f) && Mathf.Abs(gx) < 0.06f) col = Color.white;
                        if (Mathf.Abs(gx) < 0.006f && (Mathf.Abs(gy + 0.005f) < 0.012f || Mathf.Abs(gy - 0.285f) < 0.012f)) col = new Color(0.95f, 0.9f, 0.75f);
                        // The 30-yard circle, dashed.
                        if (Mathf.Abs(r - 0.42f) < 0.005f && Mathf.Repeat(Mathf.Atan2(gx, gy) * 18f / Mathf.PI, 1f) < 0.55f) col = Color.Lerp(col, Color.white, 0.75f);
                    }
                    if (r <= 1.045f) col *= shade;
                    px[j * w + i] = C32(col);
                }
            }
            _wagonGround = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "wagon_ground" };
            _wagonGround.SetPixels32(px);
            _wagonGround.Apply(false, true);
            return _wagonGround;
        }

        /// <summary>
        /// The wheels on their own panel (one batter after his innings or a fifty); taps / CLOSE
        /// dismiss; <paramref name="seconds"/> ≤ 0 stays. Over the pause menu: an opaque page with BACK.
        /// </summary>
        public void ShowWagons(IReadOnlyList<(IReadOnlyList<WagonShot> shots, bool leftHanded, string title, string line)> wheels, float seconds, Action? closed = null)
        {
            var parent = _pauseMenu != null ? (RectTransform)_pauseMenu.transform : null;
            RectTransform rt;
            if (parent != null)
            {
                if (_wagon != null) Destroy(_wagon);
                rt = UiKit.Stretch(parent, "WagonWheels", new Color(0.02f, 0.03f, 0.07f, 0.97f));
                _wagon = rt.gameObject;
            }
            else rt = Piece(ref _wagon, "WagonWheels", new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1400, 860));
            var size = wheels.Count > 1 ? 580f : 760f;
            for (var i = 0; i < wheels.Count; i++)
            {
                var x = wheels.Count == 1 ? 0 : (i == 0 ? -330f : 330f);
                var w = wheels[i];
                WagonWheel(rt, new Vector2(0.5f, 0.5f), new Vector2(x, 30), size, w.shots, w.leftHanded, w.title, w.line);
            }
            if (parent != null)
            {
                PanelKit.Pill(rt, "CloseWagon", new Vector2(0.5f, 0), new Vector2(0, 80), new Vector2(360, 120), "BACK", UiKit.Accent, () =>
                {
                    if (_wagon != null) Destroy(_wagon);
                    _wagon = null;
                    closed?.Invoke();
                }, 36);
                return;
            }
            StartCoroutine(Animate(rt, new Vector2(0, -60), seconds));
        }

        public void HideWagons()
        {
            if (_wagon != null) Destroy(_wagon);
            _wagon = null;
        }
    }
}
