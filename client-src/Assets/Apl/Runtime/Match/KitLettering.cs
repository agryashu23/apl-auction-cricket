using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apl.Client.Match
{
    /// <summary>
    /// Name and squad number across a player's back, as on a broadcast kit:
    /// glyph quads from the UI font laid on the curve of the shirt (each body's
    /// back measured in tools/gear — they differ by 4 cm) and carried by the
    /// upper spine. One small mesh and draw call per player; the glyphs are
    /// re-mapped whenever the dynamic font atlas is rebuilt.
    /// </summary>
    public static class KitLettering
    {
        private const int Size = 96;
        private const FontStyle Style = FontStyle.Bold;

        /// <summary>Back of the shirt (z, metres, body frame) at the name (y 1.42) and the number (y 1.26).</summary>
        private static readonly Dictionary<string, Vector2> Backs = new Dictionary<string, Vector2>
        {
            ["Male_Adult_11"] = new Vector2(-0.123f, -0.121f), ["Male_Adult_02"] = new Vector2(-0.133f, -0.133f),
            ["Male_Adult_16"] = new Vector2(-0.128f, -0.129f), ["Male_Adult_09"] = new Vector2(-0.128f, -0.129f),
            ["Sports_Male_04"] = new Vector2(-0.108f, -0.106f), ["Sports_Male_02"] = new Vector2(-0.129f, -0.123f),
            ["Male_Adult_10"] = new Vector2(-0.132f, -0.14f), ["Male_Adult_05"] = new Vector2(-0.13f, -0.148f),
            ["Male_Adult_14"] = new Vector2(-0.113f, -0.119f), ["Male_Adult_01"] = new Vector2(-0.128f, -0.124f),
            ["Male_Adult_20"] = new Vector2(-0.123f, -0.134f), ["Male_Adult_08"] = new Vector2(-0.13f, -0.132f),
            ["Male_Adult_06"] = new Vector2(-0.128f, -0.126f), ["Male_Adult_03"] = new Vector2(-0.127f, -0.125f),
            ["Male_Adult_13"] = new Vector2(-0.136f, -0.137f), ["Male_Adult_07"] = new Vector2(-0.138f, -0.134f),
            ["Sports_Male_03"] = new Vector2(-0.131f, -0.127f), ["Wood_Male_01"] = new Vector2(-0.126f, -0.128f),
            ["Male_Adult_04"] = new Vector2(-0.141f, -0.143f), ["Male_Adult_17"] = new Vector2(-0.126f, -0.137f),
            ["Male_Adult_12"] = new Vector2(-0.127f, -0.149f), ["Male_Adult_18"] = new Vector2(-0.129f, -0.129f),
            ["Sports_Male_01"] = new Vector2(-0.105f, -0.099f), ["Business_Male_02"] = new Vector2(-0.14f, -0.13f),
            ["Business_Male_04"] = new Vector2(-0.128f, -0.132f), ["Business_Male_05"] = new Vector2(-0.135f, -0.131f),
            ["Business_Male_06"] = new Vector2(-0.127f, -0.13f), ["Business_Male_07"] = new Vector2(-0.126f, -0.137f),
        };

        private sealed class Lettering
        {
            public Mesh Mesh = null!;
            public string Glyphs = "";
        }

        private static readonly List<Lettering> All = new List<Lettering>();
        private static Material? _mat;
        private static bool _hooked;

        /// <summary>The surname as printed on a shirt.</summary>
        public static string Surname(string name)
        {
            var parts = name.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            var last = parts.Length > 0 ? parts[parts.Length - 1] : name;
            return new string(last.ToUpperInvariant().Where(c => char.IsLetter(c) || c == '-' || c == '\'').ToArray());
        }

        /// <summary>
        /// Puts <paramref name="name"/> over <paramref name="number"/> on the
        /// back of a freshly spawned body (bind pose) of Rocketbox body
        /// <paramref name="model"/>, in <paramref name="colour"/>.
        /// </summary>
        public static void Apply(Animator a, string model, string name, int number, Color colour)
        {
            var bone = a.GetBoneTransform(HumanBodyBones.UpperChest) ?? a.GetBoneTransform(HumanBodyBones.Chest) ?? a.GetBoneTransform(HumanBodyBones.Spine);
            if (bone == null) return;
            var font = UiKit.Font;
            var mat = Material();
            if (mat == null) return;
            var back = Backs.TryGetValue(model, out var b) ? b : new Vector2(-0.128f, -0.128f);
            var num = number.ToString();
            var glyphs = name + num;
            font.RequestCharactersInTexture(glyphs, Size, Style);

            var go = new GameObject("Lettering");
            go.transform.SetPositionAndRotation(bone.position, bone.rotation);
            var verts = new List<Vector3>();
            var cols = new List<Color32>();
            var tris = new List<int>();
            var c32 = (Color32)colour;
            // Name across the shoulder blades, the number big below it (cap heights, max widths in metres).
            Line(name, 1.425f, back.x, 0.052f, 0.25f);
            Line(num, 1.255f, back.y, 0.15f, 0.2f);

            void Line(string text, float y, float z, float cap, float maxWidth)
            {
                var pen = 0f;
                foreach (var ch in text) pen += font.GetCharacterInfo(ch, out var ci, Size, Style) ? ci.advance : 0;
                if (pen <= 0) return;
                var k = Mathf.Min(cap / (Size * 0.72f), maxWidth / pen);
                var x0 = -pen * k / 2;
                var cursor = 0f;
                foreach (var ch in text)
                {
                    if (!font.GetCharacterInfo(ch, out var ci, Size, Style)) continue;
                    var l = x0 + (cursor + ci.minX) * k;
                    var r = x0 + (cursor + ci.maxX) * k;
                    var bot = y - cap / 2 + ci.minY * k;
                    var top = y - cap / 2 + ci.maxY * k;
                    cursor += ci.advance;
                    // On the curve of the back, 8 mm proud of it; seen from behind (body -z), so +x reads left to right.
                    Vector3 P(float x, float yy) => a.transform.TransformPoint(new Vector3(x, yy, z + 0.9f * x * x - 0.008f));
                    var s = verts.Count;
                    verts.Add(go.transform.InverseTransformPoint(P(l, bot)));
                    verts.Add(go.transform.InverseTransformPoint(P(l, top)));
                    verts.Add(go.transform.InverseTransformPoint(P(r, top)));
                    verts.Add(go.transform.InverseTransformPoint(P(r, bot)));
                    cols.AddRange(new[] { c32, c32, c32, c32 });
                    tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                }
            }

            var mesh = new Mesh { name = "Lettering_" + name };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            var entry = new Lettering { Mesh = mesh, Glyphs = name + "\n" + num };
            Remap(entry, font);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.transform.SetParent(bone, true);
            All.Add(entry);
        }

        /// <summary>Glyph UVs from the font atlas as it is now (quads in the order they were laid).</summary>
        private static void Remap(Lettering e, Font font)
        {
            var uvs = new List<Vector2>();
            foreach (var ch in e.Glyphs)
            {
                if (ch == '\n' || !font.GetCharacterInfo(ch, out var ci, Size, Style)) continue;
                uvs.Add(ci.uvBottomLeft);
                uvs.Add(ci.uvTopLeft);
                uvs.Add(ci.uvTopRight);
                uvs.Add(ci.uvBottomRight);
            }
            if (uvs.Count == e.Mesh.vertexCount) e.Mesh.SetUVs(0, uvs);
        }

        private static Material? Material()
        {
            if (_mat != null) return _mat;
            var shader = Resources.Load<Shader>("Shaders/KitText") ?? Shader.Find("Apl/KitText");
            if (shader == null) return null;
            var font = UiKit.Font;
            _mat = new Material(shader) { name = "KitText", mainTexture = font.material.mainTexture };
            if (!_hooked)
            {
                _hooked = true;
                Font.textureRebuilt += OnRebuilt;
            }
            return _mat;
        }

        private static void OnRebuilt(Font f)
        {
            if (f != UiKit.Font) return;
            All.RemoveAll(e => e.Mesh == null);
            foreach (var e in All) f.RequestCharactersInTexture(e.Glyphs.Replace("\n", ""), Size, Style);
            if (_mat != null) _mat.mainTexture = f.material.mainTexture;
            foreach (var e in All) Remap(e, f);
        }
    }
}
