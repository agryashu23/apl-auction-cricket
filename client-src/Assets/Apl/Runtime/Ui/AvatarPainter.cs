using System;
using UnityEngine;

namespace Apl.Client.Ui
{
    /// <summary>Broad appearance traits for one player (data/source/players_look.csv).</summary>
    [Serializable]
    public sealed class Look
    {
        public int skin = 3;
        public string hair = "short", hair_color = "black", beard = "none", age = "prime", extra = "none";
    }

    /// <summary>
    /// Paints an illustrated head-and-shoulders avatar from a <see cref="Look"/>:
    /// flat shapes with soft shading, drawn with signed distances so every edge
    /// is antialiased at any size. Same look + id = same picture.
    /// </summary>
    public static class AvatarPainter
    {
        private static readonly Color[] Skins =
        {
            new Color(0.96f, 0.82f, 0.72f), new Color(0.91f, 0.73f, 0.60f), new Color(0.80f, 0.60f, 0.45f),
            new Color(0.68f, 0.48f, 0.33f), new Color(0.50f, 0.33f, 0.22f), new Color(0.36f, 0.23f, 0.15f),
        };

        private static Color HairColor(string c) => c switch
        {
            "darkbrown" => new Color(0.20f, 0.13f, 0.08f),
            "brown" => new Color(0.36f, 0.24f, 0.14f),
            "blond" => new Color(0.80f, 0.66f, 0.40f),
            "ginger" => new Color(0.68f, 0.33f, 0.14f),
            "grey" => new Color(0.66f, 0.66f, 0.68f),
            "salt" => new Color(0.42f, 0.41f, 0.42f),
            _ => new Color(0.08f, 0.07f, 0.07f),
        };

        private sealed class Canvas
        {
            public readonly int Size;
            public readonly Color[] Px;

            /// <summary>Shape space shown per canvas width (smaller = closer crop), centred on (CX, CY).</summary>
            public float Zoom = 0.68f;
            public float CX = 0.5f, CY = 0.47f;

            public Canvas(int size)
            {
                Size = size;
                Px = new Color[size * size];
            }

            /// <summary>Blend <paramref name="color"/> where <paramref name="sd"/> (signed distance in UV units) is inside.</summary>
            public void Fill(Func<float, float, float> sd, Color color, float x0 = 0, float y0 = 0, float x1 = 1, float y1 = 1, float alpha = 1f)
            {
                var n = Size;
                var z = Zoom;
                // Shape-space bounds -> pixel bounds.
                float Px0(float sx) => (0.5f + (sx - CX) / z) * n;
                float Py0(float sy) => (0.5f + (sy - CY) / z) * n;
                int ix0 = Mathf.Clamp((int)Px0(x0) - 1, 0, n - 1), ix1 = Mathf.Clamp((int)Px0(x1) + 1, 0, n - 1);
                int iy0 = Mathf.Clamp((int)Py0(y0) - 1, 0, n - 1), iy1 = Mathf.Clamp((int)Py0(y1) + 1, 0, n - 1);
                for (var y = iy0; y <= iy1; y++)
                {
                    var v = CY + ((y + 0.5f) / n - 0.5f) * z;
                    for (var x = ix0; x <= ix1; x++)
                    {
                        var u = CX + ((x + 0.5f) / n - 0.5f) * z;
                        var a = Mathf.Clamp01(0.5f - sd(u, v) / z * n) * alpha;
                        if (a <= 0) continue;
                        var i = y * n + x;
                        Px[i] = Color.Lerp(Px[i], color, a);
                    }
                }
            }
        }

        // Signed-distance helpers (approximate, good enough for soft edges).
        private static float Ellipse(float u, float v, float cx, float cy, float rx, float ry)
        {
            var dx = (u - cx) / rx;
            var dy = (v - cy) / ry;
            return (Mathf.Sqrt(dx * dx + dy * dy) - 1f) * Mathf.Min(rx, ry);
        }

        private static float Box(float u, float v, float cx, float cy, float hx, float hy, float r)
        {
            var qx = Mathf.Abs(u - cx) - hx + r;
            var qy = Mathf.Abs(v - cy) - hy + r;
            var ox = Mathf.Max(qx, 0);
            var oy = Mathf.Max(qy, 0);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }

        /// <summary>
        /// Studio lighting over the flat painting (UI/UX dev, rater r1 §5), so the head-shot reads
        /// as a lit 3D render beside the hall's people rather than a flat cartoon: the head and
        /// the shoulders get rounded normals (ellipsoids), a warm key light from up-left with a
        /// soft highlight, a cool rim light down the right edge, contact shadow under the jaw,
        /// and a touch of grain.
        /// </summary>
        private static void Light(Canvas c, float hx, float hy, float hrx, float hry, int seed)
        {
            var n = c.Size;
            var key = new Vector3(-0.5f, 0.62f, 0.62f).normalized;
            var rimDir = new Vector3(0.9f, 0.15f, -0.2f).normalized;
            var keyTint = new Color(1.0f, 0.96f, 0.9f);
            var rimTint = new Color(0.55f, 0.72f, 1f);
            var rng = new System.Random(seed);
            for (var y = 0; y < n; y++)
            {
                var v = c.CY + ((y + 0.5f) / n - 0.5f) * c.Zoom;
                for (var x = 0; x < n; x++)
                {
                    var u = c.CX + ((x + 0.5f) / n - 0.5f) * c.Zoom;
                    var i = y * n + x;
                    var p = c.Px[i];
                    // The head (with its hair) as an ellipsoid; the shoulders as a wider one.
                    Vector3? normal = null;
                    var hdx = (u - hx) / (hrx + 0.03f);
                    var hdy = (v - hy - 0.02f) / (hry + 0.05f);
                    var he = hdx * hdx + hdy * hdy;
                    if (he < 1f) normal = new Vector3(hdx, hdy, Mathf.Sqrt(1f - he));
                    else if (v < 0.36f)
                    {
                        var sdx = (u - 0.5f) / 0.5f;
                        var sdy = (v - 0.02f) / 0.33f;
                        var se = sdx * sdx + sdy * sdy;
                        if (se < 1f) normal = new Vector3(sdx * 0.9f, sdy * 0.6f, Mathf.Sqrt(Mathf.Max(0.05f, 1f - se)));
                    }
                    if (normal is Vector3 nn)
                    {
                        nn.Normalize();
                        var diffuse = Mathf.Max(0f, Vector3.Dot(nn, key));
                        var shade = 0.58f + 0.62f * diffuse;
                        var spec = Mathf.Pow(diffuse, 14f) * 0.16f;
                        var rim = Mathf.Pow(1f - Mathf.Clamp01(nn.z), 2.2f) * Mathf.Max(0f, Vector3.Dot(nn, rimDir)) * 0.55f;
                        p = new Color(p.r * shade * keyTint.r, p.g * shade * keyTint.g, p.b * shade * keyTint.b, 1f)
                            + new Color(spec, spec, spec, 0f) + rimTint * rim * 0.6f;
                        // Contact shadow on the neck and collar just under the jaw.
                        var under = (hy - hry) - v;
                        if (he >= 1f && under > 0f && under < 0.08f && Mathf.Abs(u - hx) < hrx)
                            p *= Mathf.Lerp(0.62f, 1f, under / 0.08f);
                    }
                    // Fine grain, as a photo has.
                    var g = ((float)rng.NextDouble() - 0.5f) * 0.03f;
                    p = new Color(Mathf.Clamp01(p.r + g), Mathf.Clamp01(p.g + g), Mathf.Clamp01(p.b + g), 1f);
                    c.Px[i] = p;
                }
            }
        }

        private static float Union(float a, float b) => Mathf.Min(a, b);
        private static float Intersect(float a, float b) => Mathf.Max(a, b);
        private static float Subtract(float a, float b) => Mathf.Max(a, -b);

        /// <summary>FNV-1a: the same on every device and run (string.GetHashCode isn't).</summary>
        private static int StableHash(string s)
        {
            unchecked
            {
                var h = (int)2166136261;
                foreach (var ch in s) h = (h ^ ch) * 16777619;
                return h & 0x7FFFFFFF;
            }
        }

        public static Texture2D Paint(Look look, string id, int size = 256)
        {
            var rng = new System.Random(StableHash(id));
            float R(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

            var c = new Canvas(size);
            var skin = Skins[Mathf.Clamp(look.skin, 1, 6) - 1];
            var shade = skin * 0.82f;
            shade.a = 1;
            var hairColor = HairColor(look.hair_color);
            var senior = look.age == "senior";
            var young = look.age == "young";

            // Head geometry, with a little per-player variety.
            var hx = 0.5f;
            var hy = 0.54f;
            var hrx = R(0.155f, 0.175f);
            var hry = R(0.2f, 0.218f);
            var eyeY = hy + 0.005f;
            var eyeDx = R(0.058f, 0.066f);

            // Background: a photo-studio backdrop (UI/UX dev, rater r1 §5): charcoal blue, a soft key
            // spot up-left behind the head, darker corners (it used to be a saturated cartoon glow).
            var top = new Color(0.12f, 0.14f, 0.19f);
            var bottom = new Color(0.03f, 0.035f, 0.05f);
            for (var y = 0; y < size; y++)
            {
                var v = y / (float)size;
                for (var x = 0; x < size; x++)
                {
                    var u = x / (float)size;
                    var glow = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u, v), new Vector2(0.4f, 0.66f)) * 1.6f);
                    var corner = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.55f));
                    var vignette = 1f - 0.45f * Mathf.Clamp01(corner * corner * 2.2f);
                    c.Px[y * size + x] = (Color.Lerp(bottom, top, v) + new Color(0.22f, 0.2f, 0.18f) * glow * glow * 0.6f) * vignette;
                }
            }

            // Long hair falls behind the shoulders.
            if (look.hair == "long")
            {
                c.Fill((u, v) => Box(u, v, hx, hy - 0.05f, hrx + 0.03f, 0.2f, 0.08f), hairColor);
            }

            // Shoulders in a plain navy kit, collar trim, neck.
            var kit = new Color(0.11f, 0.17f, 0.36f);
            c.Fill((u, v) => Ellipse(u, v, 0.5f, 0.02f, 0.5f, 0.33f), kit, 0, 0, 1, 0.36f);
            c.Fill((u, v) => Ellipse(u, v, 0.5f, 0.02f, 0.5f, 0.33f), kit * 1.25f, 0, 0.2f, 1, 0.36f, alpha: 0.3f);
            c.Fill((u, v) => Box(u, v, 0.5f, 0.37f, 0.06f, 0.06f, 0.03f), shade);
            // V-neck: skin showing in a V, gold trim along both edges.
            float Vee(float u, float v, float w) => Mathf.Max(Mathf.Abs(u - 0.5f) - (v - 0.235f) * w, v - 0.355f);
            c.Fill((u, v) => Vee(u, v, 0.6f) - 0.012f, new Color(0.95f, 0.72f, 0.3f), 0.38f, 0.2f, 0.62f, 0.37f);
            c.Fill((u, v) => Vee(u, v, 0.6f), shade, 0.38f, 0.2f, 0.62f, 0.37f);

            // Ears, then the head with soft jaw shading.
            foreach (var side in new[] { -1f, 1f })
            {
                var ex = hx + side * (hrx - 0.005f);
                c.Fill((u, v) => Ellipse(u, v, ex, hy - 0.005f, 0.027f, 0.042f), shade);
            }
            c.Fill((u, v) => Ellipse(u, v, hx, hy, hrx, hry), skin);
            c.Fill((u, v) => Intersect(Ellipse(u, v, hx, hy, hrx, hry), -Ellipse(u, v, hx, hy + 0.03f, hrx * 0.92f, hry * 0.9f)), shade, alpha: 0.55f);

            // Beard (drawn on the face before features so the mouth sits on top).
            var beardColor = look.beard == "stubble" ? Color.Lerp(skin, hairColor, 0.45f) : hairColor;
            var jaw = new Func<float, float, float>((u, v) =>
            {
                var head = Ellipse(u, v, hx, hy, hrx, hry);
                var chin = Intersect(head, v - (hy - 0.082f));
                var sideburns = Intersect(Subtract(head, Ellipse(u, v, hx, hy + 0.01f, hrx * 0.8f, hry * 0.95f)), v - (hy + 0.01f));
                return Union(chin, sideburns);
            });
            switch (look.beard)
            {
                case "stubble":
                    c.Fill(jaw, beardColor, alpha: 0.4f);
                    break;
                case "short":
                    c.Fill(jaw, beardColor, alpha: 0.85f);
                    break;
                case "full":
                    c.Fill((u, v) => Union(jaw(u, v), Ellipse(u, v, hx, hy - hry + 0.015f, hrx * 0.7f, 0.045f)), beardColor, alpha: 0.95f);
                    break;
                case "goatee":
                    c.Fill((u, v) => Ellipse(u, v, hx, hy - hry + 0.03f, 0.04f, 0.035f), beardColor);
                    break;
            }
            if (look.beard != "none" && look.beard != "stubble")
            {
                // Moustache.
                c.Fill((u, v) => Ellipse(u, v, hx, hy - 0.085f, 0.05f, 0.014f), beardColor);
            }

            // Eyes, brows, nose, mouth.
            foreach (var side in new[] { -1f, 1f })
            {
                var ex = hx + side * eyeDx;
                c.Fill((u, v) => Ellipse(u, v, ex, eyeY, 0.023f, 0.0125f), new Color(0.97f, 0.96f, 0.94f));
                c.Fill((u, v) => Ellipse(u, v, ex, eyeY, 0.0105f, 0.0105f), new Color(0.16f, 0.1f, 0.06f));
                c.Fill((u, v) => Ellipse(u, v, ex + 0.004f, eyeY + 0.004f, 0.003f, 0.003f), Color.white);
                var tilt = side * R(-0.006f, 0.008f);
                c.Fill((u, v) => Box(u, v, ex, eyeY + 0.038f + (u - ex) * tilt * 4f, 0.03f, 0.0075f, 0.006f), Color.Lerp(hairColor, Color.black, 0.2f));
            }
            c.Fill((u, v) => Ellipse(u, v, hx, hy - 0.045f, 0.018f, 0.03f), shade, alpha: 0.75f);
            var smile = R(0.034f, 0.046f);
            c.Fill((u, v) => Intersect(Subtract(Ellipse(u, v, hx, hy - 0.1f, smile, 0.026f), Ellipse(u, v, hx, hy - 0.09f, smile * 0.92f, 0.02f)), v - (hy - 0.1f)),
                new Color(0.62f, 0.25f, 0.22f));

            if (senior)
            {
                // Forehead lines and a touch of grey at the temples.
                for (var k = 0; k < 2; k++)
                {
                    var ly = hy + 0.115f + k * 0.018f;
                    c.Fill((u, v) => Box(u, v, hx, ly, 0.05f, 0.0018f, 0.0018f), shade, alpha: 0.6f);
                }
            }

            // Hair.
            var cap = new Func<float, float, float>((u, v) => Intersect(Ellipse(u, v, hx, hy + 0.012f, hrx + 0.012f, hry + 0.02f), (hy + 0.09f) - v));
            switch (look.hair)
            {
                case "bald":
                    break;
                case "buzz":
                    c.Fill(cap, hairColor, alpha: 0.8f);
                    break;
                case "curly":
                    c.Fill(cap, hairColor);
                    for (var k = 0; k < 9; k++)
                    {
                        var a = Mathf.PI * (0.1f + 0.8f * k / 8f);
                        var px = hx + Mathf.Cos(a) * (hrx + 0.005f);
                        var py = hy + 0.06f + Mathf.Sin(a) * (hry * 0.85f);
                        c.Fill((u, v) => Ellipse(u, v, px, py, 0.045f, 0.04f), hairColor);
                    }
                    break;
                case "spiky":
                    c.Fill(cap, hairColor);
                    for (var k = 0; k < 5; k++)
                    {
                        var px = hx - 0.1f + k * 0.05f;
                        c.Fill((u, v) => Ellipse(u, v, px, hy + hry + 0.015f, 0.022f, 0.045f), hairColor);
                    }
                    break;
                default:
                    // short / side / quiff / long: a cap, plus a parting or a raised front.
                    c.Fill(cap, hairColor);
                    if (look.hair == "side")
                    {
                        c.Fill((u, v) => Ellipse(u, v, hx - 0.04f, hy + hry - 0.005f, 0.13f, 0.045f), hairColor);
                        c.Fill((u, v) => Box(u, v, hx - 0.07f, hy + hry - 0.0f, 0.004f, 0.03f, 0.003f), Color.Lerp(hairColor, skin, 0.35f), alpha: 0.7f);
                    }
                    else if (look.hair == "quiff")
                    {
                        c.Fill((u, v) => Ellipse(u, v, hx + 0.015f, hy + hry + 0.02f, 0.12f, 0.06f), hairColor);
                    }
                    else if (look.hair == "long")
                    {
                        foreach (var side in new[] { -1f, 1f })
                        {
                            var sx = hx + side * (hrx + 0.005f);
                            c.Fill((u, v) => Box(u, v, sx, hy + 0.0f, 0.025f, 0.11f, 0.02f), hairColor);
                        }
                    }
                    break;
            }
            if (senior && look.hair != "bald" && look.hair_color != "grey" && look.hair_color != "salt")
            {
                foreach (var side in new[] { -1f, 1f })
                {
                    var sx = hx + side * (hrx - 0.012f);
                    c.Fill((u, v) => Ellipse(u, v, sx, hy + 0.06f, 0.018f, 0.04f), HairColor("grey"), alpha: 0.65f);
                }
            }

            // Extras.
            switch (look.extra)
            {
                case "glasses":
                    foreach (var side in new[] { -1f, 1f })
                    {
                        var ex = hx + side * eyeDx;
                        c.Fill((u, v) => Subtract(Box(u, v, ex, eyeY, 0.038f, 0.026f, 0.012f), Box(u, v, ex, eyeY, 0.032f, 0.02f, 0.009f)), new Color(0.08f, 0.08f, 0.1f));
                    }
                    c.Fill((u, v) => Box(u, v, hx, eyeY + 0.006f, 0.022f, 0.003f, 0.002f), new Color(0.08f, 0.08f, 0.1f));
                    break;
                case "headband":
                    c.Fill((u, v) => Intersect(Ellipse(u, v, hx, hy, hrx + 0.01f, hry + 0.02f), Mathf.Abs(v - (hy + 0.1f)) - 0.016f), new Color(0.85f, 0.12f, 0.16f));
                    break;
                case "patka":
                    var cloth = new Color(0.12f, 0.2f, 0.5f);
                    c.Fill((u, v) => Intersect(Ellipse(u, v, hx, hy + 0.03f, hrx + 0.02f, hry + 0.03f), (hy + 0.07f) - v), cloth);
                    c.Fill((u, v) => Ellipse(u, v, hx, hy + hry + 0.05f, 0.045f, 0.04f), cloth * 1.15f);
                    break;
            }

            if (young)
            {
                // Younger players: a slightly brighter, cleaner face.
                c.Fill((u, v) => Ellipse(u, v, hx - 0.07f, hy - 0.04f, 0.03f, 0.02f), new Color(1f, 0.6f, 0.55f), alpha: 0.15f);
                c.Fill((u, v) => Ellipse(u, v, hx + 0.07f, hy - 0.04f, 0.03f, 0.02f), new Color(1f, 0.6f, 0.55f), alpha: 0.15f);
            }

            Light(c, hx, hy, hrx, hry, StableHash(id));

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Avatar_" + id };
            tex.SetPixels(c.Px);
            tex.Apply();
            return tex;
        }
    }
}
