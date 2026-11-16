using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apl.Client.Match
{
    /// <summary>
    /// Builds one mesh out of many quads, boxes and props (the stands, the
    /// skyline, a forest) so a whole stadium is a handful of draw calls.
    /// Quads are given as seen from the front: bottom-left, top-left,
    /// top-right, bottom-right.
    /// </summary>
    public sealed class MeshKit
    {
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int>[] _tris;

        public MeshKit(int submeshes = 1)
        {
            _tris = new List<int>[submeshes];
            for (var i = 0; i < submeshes; i++) _tris[i] = new List<int>();
        }

        public int Vert(Vector3 p, Vector2 uv)
        {
            _v.Add(p);
            _uv.Add(uv);
            return _v.Count - 1;
        }

        public void Tri(int sub, int a, int b, int c)
        {
            _tris[sub].Add(a);
            _tris[sub].Add(b);
            _tris[sub].Add(c);
        }

        public void Quad(int sub, Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, Vector2? uv0 = null, Vector2? uv1 = null)
        {
            var lo = uv0 ?? Vector2.zero;
            var hi = uv1 ?? Vector2.one;
            var a = Vert(bl, new Vector2(lo.x, lo.y));
            var b = Vert(tl, new Vector2(lo.x, hi.y));
            var c = Vert(tr, new Vector2(hi.x, hi.y));
            var d = Vert(br, new Vector2(hi.x, lo.y));
            Tri(sub, a, b, c);
            Tri(sub, a, c, d);
        }

        /// <summary>A box standing on <paramref name="foot"/>, facing <paramref name="forward"/>; texture scaled per metre.</summary>
        public void Box(int sub, Vector3 foot, Vector3 size, Vector3 forward, float uvPerMetre = 0)
        {
            var f = forward.sqrMagnitude < 1e-6f ? Vector3.forward : new Vector3(forward.x, 0, forward.z).normalized;
            var r = Vector3.Cross(Vector3.up, f);
            var hx = r * size.x / 2;
            var hz = f * size.z / 2;
            var up = Vector3.up * size.y;
            Vector3 P(int sx, int sy, int sz) => foot + hx * sx + hz * sz + up * sy;
            Vector2 U(float w, float h) => uvPerMetre > 0 ? new Vector2(w * uvPerMetre, h * uvPerMetre) : Vector2.one;
            // The face towards forward, the back, both sides, the top.
            Quad(sub, P(1, 0, 1), P(1, 1, 1), P(-1, 1, 1), P(-1, 0, 1), null, U(size.x, size.y));
            Quad(sub, P(-1, 0, -1), P(-1, 1, -1), P(1, 1, -1), P(1, 0, -1), null, U(size.x, size.y));
            Quad(sub, P(1, 0, -1), P(1, 1, -1), P(1, 1, 1), P(1, 0, 1), null, U(size.z, size.y));
            Quad(sub, P(-1, 0, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, 0, -1), null, U(size.z, size.y));
            Quad(sub, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), Vector2.zero, new Vector2(0.01f, 0.01f));
        }

        /// <summary>A cone (pines, spires): <paramref name="sides"/> faces from a ring to a point.</summary>
        public void Cone(int sub, Vector3 foot, float radius, float height, int sides = 6)
        {
            var tip = Vert(foot + Vector3.up * height, new Vector2(0.5f, 1));
            var ring = new int[sides + 1];
            for (var i = 0; i <= sides; i++)
            {
                var a = i * Mathf.PI * 2 / sides;
                ring[i] = Vert(foot + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * radius, new Vector2(i / (float)sides, 0));
            }
            for (var i = 0; i < sides; i++) Tri(sub, ring[i], ring[i + 1], tip);
        }

        /// <summary>A rough ball (tree crowns, domes): an octahedron squashed to size.</summary>
        public void Blob(int sub, Vector3 centre, Vector3 size)
        {
            Vector3[] p = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            int[] faces = { 0, 4, 3, 0, 3, 5, 0, 5, 2, 0, 2, 4, 1, 3, 4, 1, 5, 3, 1, 2, 5, 1, 4, 2 };
            for (var i = 0; i < faces.Length; i += 3)
            {
                var a = Vert(centre + Vector3.Scale(p[faces[i]], size / 2), new Vector2(0.5f, 0.5f));
                var b = Vert(centre + Vector3.Scale(p[faces[i + 1]], size / 2), new Vector2(0.5f, 0.5f));
                var c = Vert(centre + Vector3.Scale(p[faces[i + 2]], size / 2), new Vector2(0.5f, 0.5f));
                Tri(sub, a, b, c);
            }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (_v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(_v);
            m.SetUVs(0, _uv);
            m.subMeshCount = _tris.Length;
            for (var i = 0; i < _tris.Length; i++) m.SetTriangles(_tris[i], i);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Puts the mesh in the scene with one material per submesh; no shadows (mobile).</summary>
        public GameObject Spawn(string name, Transform parent, params Material[] mats)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = Build(name);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        public bool Empty => _v.Count == 0;
    }
}
