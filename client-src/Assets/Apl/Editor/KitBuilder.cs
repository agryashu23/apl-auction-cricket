using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Apl.Client.EditorTools
{
    /// <summary>
    /// Team kits for the cricketers: for each male Rocketbox body, a kit mask
    /// over its body texture — shirt (R), sleeves (G), trousers (B), shoes (A) —
    /// found from which bone each triangle follows, so the match can repaint any
    /// body in any team's colours (Apl/KitBlit). Also measures each body's skin
    /// tone (from the hands) on the players' 1–6 scale, so a player gets a body
    /// that looks like him. Writes Generated/Kits/*_kit.png and
    /// Resources/Data/kit_cast.json.
    /// </summary>
    public static class KitBuilder
    {
        /// <summary>
        /// Male bodies whose clothes read as a kit once repainted. Left out (seen in the
        /// line-up test): long coats and robes, headscarves and caps, a beret, a holster,
        /// work gloves, a skin-tight suit; the business suits stay in the hall.
        /// </summary>
        public static readonly string[] Cast =
        {
            "Male_Adult_01", "Male_Adult_02", "Male_Adult_03", "Male_Adult_04", "Male_Adult_05", "Male_Adult_06", "Male_Adult_07",
            "Male_Adult_08", "Male_Adult_09", "Male_Adult_10", "Male_Adult_11", "Male_Adult_12", "Male_Adult_13", "Male_Adult_14",
            "Male_Adult_16", "Male_Adult_17", "Male_Adult_18", "Male_Adult_20",
            "Sports_Male_02", "Sports_Male_03", "Sports_Male_04", "Wood_Male_01",
            // Round 5 (line-up build/gear/lineup_candidates.png): short hair, no built-in headwear,
            // shirts that repaint cleanly. Sports_Male_01's bare torso takes the kit as a fitted top.
            "Sports_Male_01", "Business_Male_02", "Business_Male_04", "Business_Male_05", "Business_Male_06", "Business_Male_07",
        };

        /// <summary>
        /// Cheerleaders (StadiumBuilder platforms): kit masks only — they are NOT in
        /// the catalog, so they can never be picked as players. GroundBuilder hands
        /// them to the match separately (GroundRefs.CheerModels).
        /// </summary>
        public static readonly string[] Cheer = { "Female_Adult_01", "Female_Adult_02" };

        public const string Out = "Assets/Apl/Generated/Kits";
        public const string Catalog = "Assets/Resources/Data/kit_cast.json";
        private const int Size = 512;

        // Mask channels.
        private const int Shirt = 0, Sleeve = 1, Trousers = 2, Shoes = 3, Keep = -1, Skin = -2, Forearm = -3;

        private static readonly Color[] Tones =
        {
            new Color(0.96f, 0.82f, 0.72f), new Color(0.91f, 0.73f, 0.60f), new Color(0.80f, 0.60f, 0.45f),
            new Color(0.68f, 0.48f, 0.33f), new Color(0.50f, 0.33f, 0.22f), new Color(0.36f, 0.23f, 0.15f),
        };

        [MenuItem("APL/Build Kit Masks")]
        public static void Build()
        {
            Directory.CreateDirectory(Out);
            var measured = new List<(string model, Color skin)>();
            foreach (var model in Cast)
            {
                var path = $"Assets/ThirdParty/Rocketbox/{model}/Export/{model}.fbx";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogWarning($"[APL] kit: missing {path}");
                    continue;
                }
                var result = Mask(model, prefab);
                if (result == null) continue;
                var (mask, skin) = result.Value;
                var file = $"{Out}/{model}_kit.png";
                File.WriteAllBytes(file, mask.EncodeToPNG());
                Object.DestroyImmediate(mask);
                AssetDatabase.ImportAsset(file);
                var importer = (TextureImporter)AssetImporter.GetAtPath(file);
                importer.sRGBTexture = false;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = false;
                importer.SaveAndReimport();
                measured.Add((model, skin));
            }
            foreach (var model in Cheer)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ThirdParty/Rocketbox/{model}/Export/{model}.fbx");
                var result = prefab != null ? Mask(model, prefab) : null;
                if (result == null) continue;
                var file = $"{Out}/{model}_kit.png";
                File.WriteAllBytes(file, result.Value.mask.EncodeToPNG());
                Object.DestroyImmediate(result.Value.mask);
                AssetDatabase.ImportAsset(file);
                var importer = (TextureImporter)AssetImporter.GetAtPath(file);
                importer.sRGBTexture = false;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = false;
                importer.SaveAndReimport();
            }
            // Tones by rank: the texture's hands read darker than faces, so place each body
            // among the others (lightest share = tone 1 ...) rather than on absolute colour.
            var ranked = measured.OrderByDescending(m => m.skin.grayscale).ToList();
            float[] share = { 0.22f, 0.2f, 0.26f, 0.2f, 0.07f, 0.05f };
            var entries = new List<string>();
            for (var i = 0; i < ranked.Count; i++)
            {
                var q = (i + 0.5f) / ranked.Count;
                var tone = 1;
                var acc = share[0];
                while (tone < 6 && q > acc) acc += share[tone++];
                var (model, skin) = ranked[i];
                entries.Add($"{{\"model\":\"{model}\",\"tone\":{tone},\"skin\":\"{ColorUtility.ToHtmlStringRGB(skin)}\"}}");
                Debug.Log($"[APL] kit {model}: skin #{ColorUtility.ToHtmlStringRGB(skin)} tone {tone}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Catalog)!);
            File.WriteAllText(Catalog, "{\"cast\":[\n" + string.Join(",\n", entries) + "\n]}\n");
            AssetDatabase.ImportAsset(Catalog);
            Debug.Log($"[APL] kit masks: {entries.Count} bodies");
        }

        /// <summary>Nearest of the players' six skin tones (by brightness, then colour).</summary>
        private static int ToneOf(Color skin)
        {
            var best = 0;
            var bestD = float.MaxValue;
            for (var i = 0; i < Tones.Length; i++)
            {
                var d = Mathf.Abs(skin.grayscale - Tones[i].grayscale) * 3 + Vector3.Distance((Vector4)skin, (Vector4)Tones[i]);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best + 1;
        }

        private static int Region(string bone)
        {
            if (bone.Contains("Finger") || bone.Contains(" Hand")) return Skin;
            if (bone.Contains("Forearm")) return Forearm;
            if (bone.Contains("UpperArm")) return Sleeve;
            if (bone.Contains("Spine") || bone.Contains("Clavicle")) return Shirt;
            if (bone.Contains("Pelvis") || bone.Contains("Thigh") || bone.Contains("Calf")) return Trousers;
            if (bone.Contains("Foot") || bone.Contains("Toe")) return Shoes;
            return Keep;
        }

        private static (Texture2D mask, Color skin)? Mask(string model, GameObject prefab)
        {
            var smrs = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var body = smrs.Select(r => (r, i: System.Array.FindIndex(r.sharedMaterials, m => m != null && m.name.ToLowerInvariant().Contains("body"))))
                .Where(x => x.i >= 0).OrderByDescending(x => x.r.sharedMesh != null ? x.r.sharedMesh.vertexCount : 0).FirstOrDefault();
            if (body.r == null || body.r.sharedMesh == null)
            {
                Debug.LogWarning($"[APL] kit {model}: no body mesh");
                return null;
            }
            var mesh = body.r.sharedMesh;
            var bones = body.r.bones;
            var weights = mesh.boneWeights;
            var uvs = mesh.uv;
            var tris = mesh.GetTriangles(body.i);
            var tex = ReadBody(body.r.sharedMaterials[body.i]);
            int VertexRegion(int v)
            {
                var w = weights[v];
                var b = w.weight0 >= w.weight1 ? w.boneIndex0 : w.boneIndex1;
                return b >= 0 && b < bones.Length && bones[b] != null ? Region(bones[b].name) : Keep;
            }
            // Each vertex's weight per region (its bone weights summed by region), so a
            // pixel can take whichever region dominates *there* — smooth hems and cuffs.
            const int regionCount = 7;
            int Slot(int region) => region + 3;
            var vw = new float[mesh.vertexCount, regionCount];
            for (var v = 0; v < mesh.vertexCount; v++)
            {
                var w = weights[v];
                void AddW(int bone, float weight)
                {
                    if (weight <= 0 || bone < 0 || bone >= bones.Length || bones[bone] == null) return;
                    vw[v, Slot(Region(bones[bone].name))] += weight;
                }
                AddW(w.boneIndex0, w.weight0);
                AddW(w.boneIndex1, w.weight1);
                AddW(w.boneIndex2, w.weight2);
                AddW(w.boneIndex3, w.weight3);
            }

            // Skin colour: the hands' texels.
            var skinSum = Vector4.zero;
            var skinN = 0;
            var regions = new int[tris.Length / 3];
            for (var t = 0; t < tris.Length; t += 3)
            {
                var r0 = VertexRegion(tris[t]);
                var r1 = VertexRegion(tris[t + 1]);
                var r2 = VertexRegion(tris[t + 2]);
                var region = r1 == r2 ? r1 : r0;
                regions[t / 3] = region;
                if (region == Skin && tex != null)
                {
                    var c = (uvs[tris[t]] + uvs[tris[t + 1]] + uvs[tris[t + 2]]) / 3;
                    skinSum += (Vector4)tex.GetPixelBilinear(c.x, c.y);
                    skinN++;
                }
            }
            var skin = skinN > 0 ? (Color)(skinSum / skinN) : new Color(0.7f, 0.5f, 0.38f);

            // The shoulder seam: a plane across the arm just outside each shoulder joint (bind pose),
            // so shirt and sleeve meet in a clean line — bone-weight dominance left ragged blotches there.
            var verts = mesh.vertices;
            var lat = Vector3.right;
            var mid = Vector3.zero;
            var seam = float.MaxValue;
            var arms = Enumerable.Range(0, bones.Length).Where(b => bones[b] != null && bones[b].name.Contains("UpperArm")).ToArray();
            if (arms.Length == 2 && mesh.bindposes.Length == bones.Length)
            {
                var a0 = mesh.bindposes[arms[0]].inverse.MultiplyPoint3x4(Vector3.zero);
                var a1 = mesh.bindposes[arms[1]].inverse.MultiplyPoint3x4(Vector3.zero);
                lat = (a1 - a0).normalized;
                mid = (a0 + a1) / 2;
                seam = Vector3.Distance(a0, a1) / 2 + 0.02f;
            }
            float Out(int v) => Mathf.Abs(Vector3.Dot(verts[v] - mid, lat));
            var px = new Color32[Size * Size];
            for (var t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c3 = tris[t + 2];
                Raster(uvs[a], uvs[b], uvs[c3], (x, y, w0, w1, w2) =>
                {
                    var region = Keep;
                    var top = -1f;
                    for (var k = 0; k < regionCount; k++)
                    {
                        var wk = vw[a, k] * w0 + vw[b, k] * w1 + vw[c3, k] * w2;
                        if (wk <= top) continue;
                        top = wk;
                        region = k - 3;
                    }
                    if (region == Keep || region == Skin) return;
                    if (region == Shirt || region == Sleeve)
                    {
                        var o = Out(a) * w0 + Out(b) * w1 + Out(c3) * w2;
                        region = o > seam ? Sleeve : Shirt;
                    }
                    var channel = region;
                    if (region == Forearm)
                    {
                        // Bare forearms stay bare (short sleeves); covered ones become sleeves.
                        if (tex != null && IsSkin(tex.GetPixelBilinear((x + 0.5f) / Size, (y + 0.5f) / Size), skin)) return;
                        channel = Sleeve;
                    }
                    var c = px[y * Size + x];
                    switch (channel)
                    {
                        case Shirt: c.r = 255; break;
                        case Sleeve: c.g = 255; break;
                        case Trousers: c.b = 255; break;
                        case Shoes: c.a = 255; break;
                    }
                    px[y * Size + x] = c;
                });
            }
            Majority(px, 2);
            Dilate(px, 2);
            if (tex != null) Object.DestroyImmediate(tex);
            var mask = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            mask.SetPixels32(px);
            mask.Apply();
            return (mask, skin);
        }

        private static bool IsSkin(Color c, Color skin) => Vector3.Distance(new Vector3(c.r, c.g, c.b), new Vector3(skin.r, skin.g, skin.b)) < 0.14f;

        /// <summary>The body's colour texture, read back at mask size (textures aren't CPU-readable).</summary>
        private static Texture2D? ReadBody(Material m)
        {
            var src = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
            if (src == null) src = m.mainTexture;
            if (src == null) return null;
            var rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        private static void Raster(Vector2 a, Vector2 b, Vector2 c, System.Action<int, int, float, float, float> plot)
        {
            Vector2 P(Vector2 uv) => new Vector2(Mathf.Repeat(uv.x, 1f) * Size, Mathf.Repeat(uv.y, 1f) * Size);
            var p0 = P(a);
            var p1 = P(b);
            var p2 = P(c);
            var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))));
            var maxX = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))));
            var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))));
            var maxY = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))));
            var area = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
            if (Mathf.Abs(area) < 1e-6f) return;
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var w0 = ((p1.x - p.x) * (p2.y - p.y) - (p2.x - p.x) * (p1.y - p.y)) / area;
                var w1 = ((p2.x - p.x) * (p0.y - p.y) - (p0.x - p.x) * (p2.y - p.y)) / area;
                var w2 = 1 - w0 - w1;
                if (w0 >= -0.01f && w1 >= -0.01f && w2 >= -0.01f) plot(x, y, Mathf.Clamp01(w0), Mathf.Clamp01(w1), Mathf.Clamp01(w2));
            }
        }

        /// <summary>Smooth ragged edges (the skin test on forearms): each texel takes its neighbourhood's majority.</summary>
        private static void Majority(Color32[] px, int radius)
        {
            var copy = (Color32[])px.Clone();
            var counts = new int[5];
            for (var y = radius; y < Size - radius; y++)
            for (var x = radius; x < Size - radius; x++)
            {
                System.Array.Clear(counts, 0, 5);
                for (var dy = -radius; dy <= radius; dy++)
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var c = copy[(y + dy) * Size + x + dx];
                    counts[c.r != 0 ? 1 : c.g != 0 ? 2 : c.b != 0 ? 3 : c.a != 0 ? 4 : 0]++;
                }
                var best = 0;
                for (var k = 1; k < 5; k++) if (counts[k] > counts[best]) best = k;
                var self = copy[y * Size + x];
                var selfK = self.r != 0 ? 1 : self.g != 0 ? 2 : self.b != 0 ? 3 : self.a != 0 ? 4 : 0;
                // Only between painted regions and bare skin/sleeve edges; leave UV gutters alone.
                if (best == selfK || (selfK == 0 && counts[0] > counts[best] / 2)) continue;
                px[y * Size + x] = best switch
                {
                    1 => new Color32(255, 0, 0, 0),
                    2 => new Color32(0, 255, 0, 0),
                    3 => new Color32(0, 0, 255, 0),
                    4 => new Color32(0, 0, 0, 255),
                    _ => new Color32(0, 0, 0, 0),
                };
            }
        }

        /// <summary>Grow each painted region a couple of texels so UV seams don't show the old colour.</summary>
        private static void Dilate(Color32[] px, int steps)
        {
            for (var s = 0; s < steps; s++)
            {
                var copy = (Color32[])px.Clone();
                for (var y = 1; y < Size - 1; y++)
                for (var x = 1; x < Size - 1; x++)
                {
                    var i = y * Size + x;
                    var c = copy[i];
                    if (c.r != 0 || c.g != 0 || c.b != 0 || c.a != 0) continue;
                    foreach (var n in new[] { i - 1, i + 1, i - Size, i + Size })
                    {
                        var o = copy[n];
                        if (o.r == 0 && o.g == 0 && o.b == 0 && o.a == 0) continue;
                        px[i] = o;
                        break;
                    }
                }
            }
        }
    }
}
