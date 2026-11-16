using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apl.Client.Match
{
    /// <summary>
    /// The cricket gear modelled in Blender (tools/gear/make_gear.py) on the
    /// Rocketbox bind pose — helmet, batting and keeping pads and gloves, the
    /// bat — read from Resources/Gear/gear.bytes. <see cref="Dress"/> puts a set
    /// of parts on a body as ONE skinned renderer: every vertex rides its own
    /// bone (finger rolls curl with the fingers, the pad's thigh flap with the
    /// thigh), one draw call per material slot.
    /// </summary>
    public static class GearMeshes
    {
        /// <summary>Material slots the parts are painted with.</summary>
        public const int Team = 0, White = 1, Dark = 2, Bat = 3;

        public sealed class Part
        {
            public string Name = "";
            public int Slot;

            /// <summary>Bone per bone index; null = the part's own frame (the bat).</summary>
            public HumanBodyBones?[] Bones = Array.Empty<HumanBodyBones?>();
            public Vector3[] Pos = Array.Empty<Vector3>(), Nrm = Array.Empty<Vector3>();
            public Vector2[] Uv = Array.Empty<Vector2>();
            public byte[] Bone = Array.Empty<byte>();

            /// <summary>A second bone per vertex and how much it leans on it (joints bend smoothly).</summary>
            public byte[] Bone2 = Array.Empty<byte>();
            public float[] Weight2 = Array.Empty<float>();
            public int[] Tris = Array.Empty<int>();
        }

        private static Dictionary<string, Part>? _parts;

        private static Dictionary<string, Part> Parts()
        {
            if (_parts != null) return _parts;
            _parts = new Dictionary<string, Part>();
            var asset = Resources.Load<TextAsset>("Gear/gear");
            if (asset == null)
            {
                Debug.LogWarning("[APL] Resources/Gear/gear.bytes missing: gear falls back to primitives");
                return _parts;
            }
            try
            {
                using var r = new BinaryReader(new MemoryStream(asset.bytes));
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "APLG") throw new InvalidDataException("not a gear file");
                var version = r.ReadInt32();
                if (version != 2 && version != 3) throw new InvalidDataException("gear version " + version);
                var n = r.ReadInt32();
                for (var i = 0; i < n; i++)
                {
                    var p = new Part { Name = Str(r), Slot = r.ReadInt32() };
                    p.Bones = new HumanBodyBones?[r.ReadInt32()];
                    for (var b = 0; b < p.Bones.Length; b++)
                    {
                        var name = Str(r);
                        p.Bones[b] = Enum.TryParse<HumanBodyBones>(name, out var hb) ? hb : (HumanBodyBones?)null;
                    }
                    var nv = r.ReadInt32();
                    p.Pos = new Vector3[nv];
                    p.Nrm = new Vector3[nv];
                    p.Uv = new Vector2[nv];
                    p.Bone = new byte[nv];
                    p.Bone2 = new byte[nv];
                    p.Weight2 = new float[nv];
                    for (var v = 0; v < nv; v++)
                    {
                        p.Pos[v] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        p.Nrm[v] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        p.Uv[v] = new Vector2(r.ReadSingle(), r.ReadSingle());
                        p.Bone[v] = r.ReadByte();
                        p.Bone2[v] = version >= 3 ? r.ReadByte() : p.Bone[v];
                        p.Weight2[v] = version >= 3 ? r.ReadSingle() : 0;
                    }
                    p.Tris = new int[r.ReadInt32()];
                    for (var t = 0; t < p.Tris.Length; t++) p.Tris[t] = r.ReadInt32();
                    _parts[p.Name] = p;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[APL] gear.bytes unreadable: " + e.Message);
                _parts.Clear();
            }
            return _parts;
        }

        private static string Str(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));

        public static bool Ready => Parts().Count > 0;

        public static Part? Get(string name) => Parts().TryGetValue(name, out var p) ? p : null;

        /// <summary>A plain mesh of one part in its own frame (the bat).</summary>
        public static Mesh? Rigid(string name)
        {
            var p = Get(name);
            if (p == null) return null;
            var m = new Mesh { name = name };
            m.SetVertices(p.Pos);
            m.SetNormals(p.Nrm);
            m.SetUVs(0, p.Uv);
            m.SetTriangles(p.Tris, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        private static bool _warned;

        /// <summary>A finger bone the body doesn't map falls back to its hand (the glove then rides the hand).</summary>
        private static Transform? BoneOf(Animator a, HumanBodyBones b)
        {
            var t = a.GetBoneTransform(b);
            if (t != null) return t;
            var name = b.ToString();
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning($"[APL] gear: {a.name} has no {name} mapped; that part rides the hand / stays rigid");
            }
            if (name.Contains("Thumb") || name.Contains("Index") || name.Contains("Middle") || name.Contains("Ring") || name.Contains("Little"))
            {
                return a.GetBoneTransform(name.StartsWith("Left") ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            }
            return null;
        }

        /// <summary>
        /// Dresses a body (in its bind pose, i.e. straight after it's spawned)
        /// in <paramref name="names"/>: one skinned renderer, a submesh per slot
        /// used, painted with <paramref name="slots"/>[slot]. Head-bone parts
        /// are scaled by <paramref name="headScale"/> (big hair). The renderer's
        /// object is called <paramref name="label"/>. Null if no part could be placed.
        /// </summary>
        public static SkinnedMeshRenderer? Dress(Animator a, IEnumerable<string> names, Material[] slots, Vector3 headScale, string label = "Gear", Vector3 headOffset = default)
        {
            var skin = a.GetComponentInChildren<SkinnedMeshRenderer>();
            var go = new GameObject(label);
            var gear = go.transform;
            gear.SetParent(a.transform, false);
            var body = a.transform;
            var bones = new List<Transform>();
            var boneIndex = new Dictionary<Transform, int>();
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var weights = new List<BoneWeight>();
            var bySlot = new SortedDictionary<int, List<int>>();
            foreach (var name in names)
            {
                var p = Get(name);
                if (p == null) continue;
                var map = new int[p.Bones.Length];
                var ok = true;
                for (var b = 0; b < p.Bones.Length; b++)
                {
                    var t = p.Bones[b] is HumanBodyBones hb ? BoneOf(a, hb) : null;
                    if (t == null)
                    {
                        ok = false;
                        break;
                    }
                    if (!boneIndex.TryGetValue(t, out var bi))
                    {
                        bi = bones.Count;
                        bones.Add(t);
                        boneIndex[t] = bi;
                    }
                    map[b] = bi;
                }
                if (!ok) continue;
                var start = verts.Count;
                for (var v = 0; v < p.Pos.Length; v++)
                {
                    var bi = map[p.Bone[v]];
                    var off = p.Pos[v];
                    if (p.Bones[p.Bone[v]] == HumanBodyBones.Head) off = Vector3.Scale(off, headScale) + headOffset;
                    // Offsets are in the body's frame (x right, y up, z forward), metres.
                    // (Scaled with the body: players vary ±4 % in height, ±3 % in build.)
                    var world = bones[bi].position + body.rotation * Vector3.Scale(off, body.lossyScale);
                    verts.Add(gear.InverseTransformPoint(world));
                    normals.Add(gear.InverseTransformDirection(body.rotation * p.Nrm[v]));
                    uvs.Add(p.Uv[v]);
                    var w2 = Mathf.Clamp01(p.Weight2[v]);
                    var b2 = map[p.Bone2[v]];
                    // Heavier bone first (Unity's order for blend weights).
                    weights.Add(w2 <= 0 || b2 == bi ? new BoneWeight { boneIndex0 = bi, weight0 = 1 }
                        : w2 <= 0.5f ? new BoneWeight { boneIndex0 = bi, weight0 = 1 - w2, boneIndex1 = b2, weight1 = w2 }
                        : new BoneWeight { boneIndex0 = b2, weight0 = w2, boneIndex1 = bi, weight1 = 1 - w2 });
                }
                if (!bySlot.TryGetValue(p.Slot, out var tris)) bySlot[p.Slot] = tris = new List<int>();
                foreach (var i in p.Tris) tris.Add(start + i);
            }
            if (verts.Count == 0)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }
            var mesh = new Mesh { name = "Gear_" + a.name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.boneWeights = weights.ToArray();
            mesh.subMeshCount = bySlot.Count;
            var mats = new Material[bySlot.Count];
            var s = 0;
            foreach (var kv in bySlot)
            {
                mesh.SetTriangles(kv.Value, s);
                mats[s++] = slots[Mathf.Clamp(kv.Key, 0, slots.Length - 1)];
            }
            var bind = new Matrix4x4[bones.Count];
            for (var i = 0; i < bones.Count; i++) bind[i] = bones[i].worldToLocalMatrix * gear.localToWorldMatrix;
            mesh.bindposes = bind;
            mesh.RecalculateBounds();

            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones.ToArray();
            smr.sharedMaterials = mats;
            smr.quality = SkinQuality.Bone2;
            smr.shadowCastingMode = ShadowCastingMode.On;
            // Same culling box as the body it's on (the gear never leaves it by much).
            if (skin != null && skin.rootBone != null)
            {
                smr.rootBone = skin.rootBone;
                var lb = skin.localBounds;
                lb.Expand(0.4f);
                smr.localBounds = lb;
            }
            else
            {
                smr.updateWhenOffscreen = true;
            }
            return smr;
        }
    }
}
