using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Broadcast stumps: the scene's primitive cylinders and cubes keep their
    /// transforms (the director knocks them about) but get turned stumps with
    /// a bevelled dome, in APL navy with the gold band and the name down the
    /// front, and bails with a barrel and spigots.
    /// </summary>
    public static partial class StadiumBuilder
    {
        private static Mesh? _stump, _bail;

        private static void Stumps(Transform root, Mats m)
        {
            var ground = GameObject.Find("Ground");
            if (ground == null) return;
            var wood = m.Lit(Color.white, StumpWrap(), 0.45f);
            if (_stump == null) _stump = Lathe("stump", new[] { (0.5f, -1f), (0.5f, 0.92f), (0.48f, 0.955f), (0.4f, 0.982f), (0.22f, 0.997f), (0f, 1f) }, 16, false);
            if (_bail == null) _bail = Lathe("bail", new[] { (0f, -0.5f), (0.26f, -0.49f), (0.28f, -0.32f), (0.45f, -0.3f), (0.5f, -0.2f), (0.5f, 0.2f), (0.45f, 0.3f), (0.28f, 0.32f), (0.26f, 0.49f), (0f, 0.5f) }, 10, true);
            foreach (var mf in ground.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name == "Stump")
                {
                    mf.sharedMesh = _stump;
                    mf.GetComponent<MeshRenderer>().sharedMaterial = wood;
                }
                else if (mf.name == "Bail")
                {
                    mf.sharedMesh = _bail;
                }
            }
        }

        /// <summary>
        /// A turned shape in the primitive's unit space: (radius, height) rows,
        /// round the y axis — or, for a bail, along x (the cube's long side).
        /// U runs round from the front (+z), V up.
        /// </summary>
        private static Mesh Lathe(string name, (float r, float h)[] rows, int sides, bool alongX)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (var i = 0; i < rows.Length; i++)
            for (var s = 0; s <= sides; s++)
            {
                var a = s * Mathf.PI * 2 / sides;
                var (r, h) = rows[i];
                var p = alongX ? new Vector3(h, r * Mathf.Cos(a), r * Mathf.Sin(a)) : new Vector3(r * Mathf.Sin(a), h, r * Mathf.Cos(a));
                v.Add(p);
                uv.Add(new Vector2(s / (float)sides, (h + (alongX ? 0.5f : 1f)) / (alongX ? 1f : 2f)));
            }
            var w = sides + 1;
            for (var i = 0; i + 1 < rows.Length; i++)
            for (var s = 0; s < sides; s++)
            {
                var a = i * w + s;
                var b = a + w;
                // Outward faces (Unity's clockwise front): round, then up the axis.
                t.AddRange(new[] { a, a + 1, b + 1, a, b + 1, b });
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static readonly string[] Apl =
        {
            "01110", "10001", "10001", "11111", "10001", "10001", "10001",
            "11110", "10001", "10001", "11110", "10000", "10000", "10000",
            "10000", "10000", "10000", "10000", "10000", "10000", "11111",
        };

        /// <summary>Navy stump wrap: a white foot, gold band under the top, "APL" stacked down the front and back.</summary>
        private static Texture2D StumpWrap() =>
            Tex("stump_wrap", 64, 256, (x, y) =>
            {
                var navy = new Color(0.1f, 0.12f, 0.36f);
                if (y < 18) return new Color(0.92f, 0.92f, 0.9f);
                if (y > 236 && y < 244) return new Color(0.95f, 0.72f, 0.18f);
                if (y >= 244) return navy * 1.2f;
                // Letters 2 px per cell, centred on u = 0 and u = 0.5 (front and back).
                var u = x % 32;
                var gx = 4 - (u + 5) % 32 / 2;  // seen from outside, +u runs to the viewer's left
                var row = (int)((200 - y) / 2f);
                var letter = row / 9;
                var gy = row % 9;
                // Above y = 200 the row goes negative (and C# division truncates toward zero), so check it first.
                if (y <= 200 && gx >= 0 && letter >= 0 && letter < 3 && gy < 7 && Apl[letter * 7 + gy][gx] == '1') return Color.white;
                return navy * (0.9f + 0.1f * Mathf.Sin(x * 0.4f));
            });
    }
}
