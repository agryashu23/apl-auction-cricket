using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The replay tape: the last <see cref="Seconds"/> of every body on the field
    /// (where it stands and every bone's local rotation, and the positions down to
    /// the hips, which carry the clips' baked travel), the ball, the bats, the
    /// stumps and bails, sampled <see cref="Rate"/> times a second after everything
    /// else has moved them. Playback writes a moment of the tape back onto the same
    /// transforms (in between samples blended), with the game frozen behind it
    /// (time scale 0), so no gameplay runs; <see cref="End"/> puts the live frame
    /// back exactly as it was.
    /// </summary>
    [DefaultExecutionOrder(5000)]
    public sealed class ReplayRecorder : MonoBehaviour
    {
        public const float Rate = 30f, Seconds = 12f;
        private const int Capacity = (int)(Rate * Seconds) + 2;

        /// <summary>One body: its transform, its bones (rotations) and the chain down to the hips (positions too).</summary>
        private sealed class BodyTape
        {
            public Animator A = null!;
            public Transform[] Bones = null!;
            public Transform[] Chain = null!;
            public int Stride;
            public float[] Data = null!;
            public bool[] Active = null!;
            public float[] Live = null!;
            public bool LiveActive;

            /// <summary>Its renderers, and whether playback has hidden them (a body that wasn't on the field then).</summary>
            public Renderer[] Renderers = System.Array.Empty<Renderer>();
            public bool Hidden;
        }

        /// <summary>A rigid thing: the ball, a bat, a stump, a bail.</summary>
        private sealed class PropTape
        {
            public Transform T = null!;
            public float[] Data = null!;
            public bool[] Active = null!;
            public Vector3 LivePos;
            public Quaternion LiveRot;
            public bool LiveActive;
        }

        private readonly List<BodyTape> _bodies = new List<BodyTape>();
        private readonly List<PropTape> _props = new List<PropTape>();
        private readonly Dictionary<Transform, PropTape> _propOf = new Dictionary<Transform, PropTape>();
        private readonly Dictionary<Animator, BodyTape> _bodyOf = new Dictionary<Animator, BodyTape>();
        private readonly float[] _times = new float[Capacity];
        private int _head, _count;
        private float _nextSample;

        /// <summary>Playing back (the tape is being written onto the scene, not recorded).</summary>
        public bool Playing { get; private set; }

        /// <summary>The tape's moment being shown (game time it was recorded at).</summary>
        public float PlayTime { get; set; }

        public float Oldest => _count == 0 ? 0 : _times[Index(0)];
        public float Newest => _count == 0 ? 0 : _times[Index(_count - 1)];

        /// <summary>Record this body from now on (the same body twice is ignored).</summary>
        public void AddBody(Animator? a)
        {
            if (a == null || _bodyOf.ContainsKey(a)) return;
            var bones = new HashSet<Transform>();
            foreach (var smr in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (var b in smr.bones) if (b != null) bones.Add(b);
            }
            var hips = a.isHuman ? a.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips != null) bones.Add(hips);
            // The chain from the body's root down to the hips: the clips move these, not just turn them.
            var chain = new List<Transform>();
            for (var t = hips; t != null && t != a.transform; t = t.parent) chain.Add(t);
            var tape = new BodyTape
            {
                A = a,
                Bones = new List<Transform>(bones).ToArray(),
                Chain = chain.ToArray(),
            };
            tape.Stride = 7 + tape.Chain.Length * 3 + tape.Bones.Length * 4;
            tape.Data = new float[tape.Stride * Capacity];
            tape.Active = new bool[Capacity];
            tape.Live = new float[tape.Stride];
            _bodies.Add(tape);
            _bodyOf[a] = tape;
        }

        public void AddProp(Transform? t)
        {
            if (t == null || _propOf.ContainsKey(t)) return;
            var p = new PropTape { T = t, Data = new float[7 * Capacity], Active = new bool[Capacity] };
            _props.Add(p);
            _propOf[t] = p;
        }

        /// <summary>Forget everything recorded (a new innings, a scene reset).</summary>
        public void Clear()
        {
            _count = 0;
            _head = 0;
        }

        private int Index(int logical) => (_head - _count + logical + Capacity * 2) % Capacity;

        private void LateUpdate()
        {
            if (Playing)
            {
                var a = StallProbe.Start();
                Apply(PlayTime);
                StallProbe.End($"tape apply ({_bodies.Count} bodies, {_props.Count} props)", a);
                return;
            }
            if (Time.timeScale <= 0f || Time.time < _nextSample) return;
            _nextSample = Time.time + 1f / Rate * 0.98f;
            var s = StallProbe.Start();
            Sample();
            StallProbe.End($"tape sample ({_bodies.Count} bodies)", s);
        }

        private void Sample()
        {
            var i = _head;
            _times[i] = Time.time;
            foreach (var b in _bodies)
            {
                var on = b.A != null && b.A.gameObject.activeInHierarchy;
                b.Active[i] = on;
                if (on) Write(b, b.Data, i * b.Stride);
            }
            foreach (var p in _props)
            {
                var on = p.T != null && p.T.gameObject.activeInHierarchy;
                p.Active[i] = on;
                if (!on) continue;
                var o = i * 7;
                var pos = p.T.position;
                var rot = p.T.rotation;
                p.Data[o] = pos.x; p.Data[o + 1] = pos.y; p.Data[o + 2] = pos.z;
                p.Data[o + 3] = rot.x; p.Data[o + 4] = rot.y; p.Data[o + 5] = rot.z; p.Data[o + 6] = rot.w;
            }
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        private static void Write(BodyTape b, float[] into, int o)
        {
            var root = b.A.transform;
            var pos = root.position;
            var rot = root.rotation;
            into[o++] = pos.x; into[o++] = pos.y; into[o++] = pos.z;
            into[o++] = rot.x; into[o++] = rot.y; into[o++] = rot.z; into[o++] = rot.w;
            foreach (var c in b.Chain)
            {
                var lp = c.localPosition;
                into[o++] = lp.x; into[o++] = lp.y; into[o++] = lp.z;
            }
            foreach (var bone in b.Bones)
            {
                var q = bone.localRotation;
                into[o++] = q.x; into[o++] = q.y; into[o++] = q.z; into[o++] = q.w;
            }
        }

        private static void Read(BodyTape b, float[] a, int oa, float[] c, int oc, float k)
        {
            var root = b.A.transform;
            root.SetPositionAndRotation(V(a, oa, c, oc, k), Q(a, oa + 3, c, oc + 3, k));
            oa += 7;
            oc += 7;
            foreach (var t in b.Chain)
            {
                t.localPosition = V(a, oa, c, oc, k);
                oa += 3;
                oc += 3;
            }
            foreach (var bone in b.Bones)
            {
                bone.localRotation = Q(a, oa, c, oc, k);
                oa += 4;
                oc += 4;
            }
        }

        private static Vector3 V(float[] a, int oa, float[] b, int ob, float k) =>
            Vector3.LerpUnclamped(new Vector3(a[oa], a[oa + 1], a[oa + 2]), new Vector3(b[ob], b[ob + 1], b[ob + 2]), k);

        private static Quaternion Q(float[] a, int oa, float[] b, int ob, float k) =>
            Quaternion.Slerp(new Quaternion(a[oa], a[oa + 1], a[oa + 2], a[oa + 3]), new Quaternion(b[ob], b[ob + 1], b[ob + 2], b[ob + 3]), k);

        /// <summary>The two samples either side of <paramref name="time"/> and how far between them it is.</summary>
        private bool Bracket(float time, out int i0, out int i1, out float k)
        {
            i0 = i1 = 0;
            k = 0;
            if (_count == 0) return false;
            if (time <= _times[Index(0)])
            {
                i0 = i1 = Index(0);
                return true;
            }
            var last = Index(_count - 1);
            if (time >= _times[last])
            {
                i0 = i1 = last;
                return true;
            }
            // Binary search over the logical order.
            int lo = 0, hi = _count - 1;
            while (hi - lo > 1)
            {
                var mid = (lo + hi) / 2;
                if (_times[Index(mid)] <= time) lo = mid;
                else hi = mid;
            }
            i0 = Index(lo);
            i1 = Index(hi);
            var span = _times[i1] - _times[i0];
            k = span > 1e-5f ? (time - _times[i0]) / span : 0;
            return true;
        }

        /// <summary>Freeze the live frame and start writing the tape onto the scene (the bodies on the field now).</summary>
        public void Begin()
        {
            if (Playing) return;
            StallProbe.Mark($"tape begin ({_bodies.Count} bodies, {_props.Count} props, {_count} samples)");
            var t0 = StallProbe.Start();
            try { BeginLive(); }
            finally { StallProbe.End("tape begin", t0); }
        }

        private void BeginLive()
        {
            foreach (var b in _bodies)
            {
                if (b.A == null) continue;
                b.LiveActive = b.A.gameObject.activeInHierarchy;
                if (!b.LiveActive) continue;
                Write(b, b.Live, 0);
                b.Renderers = b.A.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                b.Hidden = false;
            }
            foreach (var p in _props)
            {
                if (p.T == null) continue;
                p.LiveActive = p.T.gameObject.activeSelf;
                p.LivePos = p.T.position;
                p.LiveRot = p.T.rotation;
            }
            Playing = true;
            PlayTime = Oldest;
        }

        /// <summary>Back to the live frame, exactly as it was when the replay began.</summary>
        public void End()
        {
            if (!Playing) return;
            Playing = false;
            foreach (var b in _bodies)
            {
                if (b.A == null || !b.LiveActive) continue;
                Show(b, true);
                Read(b, b.Live, 0, b.Live, 0, 0);
            }
            foreach (var p in _props)
            {
                if (p.T == null) continue;
                p.T.gameObject.SetActive(p.LiveActive);
                p.T.SetPositionAndRotation(p.LivePos, p.LiveRot);
            }
        }

        private void Apply(float time)
        {
            if (!Bracket(time, out var i0, out var i1, out var k)) return;
            foreach (var b in _bodies)
            {
                // Bodies are never switched off and on (that would restart their animators): a
                // body that wasn't on the field then is hidden; one that isn't now stays away.
                if (b.A == null || !b.LiveActive) continue;
                var on = b.Active[i0] || b.Active[i1];
                Show(b, on);
                if (!on) continue;
                // Only one side recorded (he came on or went off between samples): no blend.
                var a0 = b.Active[i0] ? i0 : i1;
                var a1 = b.Active[i1] ? i1 : i0;
                Read(b, b.Data, a0 * b.Stride, b.Data, a1 * b.Stride, k);
            }
            foreach (var p in _props)
            {
                if (p.T == null) continue;
                var on = p.Active[i0] && p.Active[i1] || (k < 0.5f ? p.Active[i0] : p.Active[i1]);
                if (p.T.gameObject.activeSelf != on) p.T.gameObject.SetActive(on);
                if (!on) continue;
                var a0 = p.Active[i0] ? i0 : i1;
                var a1 = p.Active[i1] ? i1 : i0;
                // The ball on its replay line (the engine's path, ReplayLine) wherever the line has it.
                var at = p.T == _lineProp && _line != null && _line.At(time, out var lp) ? lp : Curve(p, a0, a1, k);
                p.T.SetPositionAndRotation(at, Q(p.Data, a0 * 7 + 3, p.Data, a1 * 7 + 3, k));
            }
        }

        private static void Show(BodyTape b, bool on)
        {
            if (b.Hidden == !on) return;
            b.Hidden = !on;
            foreach (var r in b.Renderers) if (r != null) r.enabled = on;
        }

        /// <summary>The replay's ball line (ReplayLine) and the prop it drives (the ball); null: none.</summary>
        private Transform? _lineProp;
        private ReplayLine? _line;

        /// <summary>From now until cleared, <paramref name="prop"/> is played (and asked for) on <paramref name="line"/> where it has it.</summary>
        public void SetLine(Transform? prop, ReplayLine? line)
        {
            _lineProp = line != null ? prop : null;
            _line = line;
        }

        /// <summary>
        /// Where a recorded prop (the ball) was at <paramref name="time"/>; false if it wasn't in play. The ball
        /// on a replay line is where the line has it, unless <paramref name="raw"/> (the tape itself).
        /// </summary>
        public bool PropAt(Transform t, float time, out Vector3 pos, bool raw = false)
        {
            pos = Vector3.zero;
            if (!_propOf.TryGetValue(t, out var p) || !Bracket(time, out var i0, out var i1, out var k)) return false;
            if (!p.Active[i0] && !p.Active[i1]) return false;
            if (!raw && t == _lineProp && _line != null && _line.At(time, out pos)) return true;
            var a0 = p.Active[i0] ? i0 : i1;
            var a1 = p.Active[i1] ? i1 : i0;
            pos = Curve(p, a0, a1, k);
            return true;
        }

        /// <summary>
        /// A prop's position between samples <paramref name="i0"/> and <paramref name="i1"/> on a cubic through its
        /// neighbours, with the tangents taken over the samples' real times (uneven frames on a slow PC): the ball's
        /// path is smooth in place and speed, not straight pieces with a jolt at every sample (owner, round 13:
        /// the replay ball "shivering"). Falls back to a straight line at the tape's ends or where it wasn't in play.
        /// </summary>
        private Vector3 Curve(PropTape p, int i0, int i1, float k)
        {
            Vector3 P(int i) => new Vector3(p.Data[i * 7], p.Data[i * 7 + 1], p.Data[i * 7 + 2]);
            var p0 = P(i0);
            var p1 = P(i1);
            if (i0 == i1) return p0;
            var l0 = Logical(i0);
            var l1 = Logical(i1);
            var t0 = _times[i0];
            var t1 = _times[i1];
            var dt = t1 - t0;
            if (dt <= 1e-5f) return Vector3.LerpUnclamped(p0, p1, k);
            // Tangents (per second) from the samples either side, where they're there and in play.
            Vector3 Tangent(int l, int ring, Vector3 here, float tHere)
            {
                var hasPrev = l - 1 >= 0 && p.Active[Index(l - 1)];
                var hasNext = l + 1 < _count && p.Active[Index(l + 1)];
                var prev = hasPrev ? Index(l - 1) : ring;
                var next = hasNext ? Index(l + 1) : ring;
                if (prev == next) return Vector3.zero;
                var span = _times[next] - _times[prev];
                return span > 1e-5f ? (P(next) - P(prev)) / span : Vector3.zero;
            }
            var m0 = Tangent(l0, i0, p0, t0) * dt;
            var m1 = Tangent(l1, i1, p1, t1) * dt;
            // A bounce or a catch (a sharp turn): the plain line keeps the kink where it really was.
            var chord = p1 - p0;
            if (Vector3.Dot(m0, chord) < 0f || Vector3.Dot(m1, chord) < 0f) return Vector3.LerpUnclamped(p0, p1, k);
            var k2 = k * k;
            var k3 = k2 * k;
            var c = (2 * k3 - 3 * k2 + 1) * p0 + (k3 - 2 * k2 + k) * m0 + (-2 * k3 + 3 * k2) * p1 + (k3 - k2) * m1;
            // Never through the turf at a bounce.
            c.y = Mathf.Max(c.y, Mathf.Min(0.035f, Mathf.Min(p0.y, p1.y)));
            return c;
        }

        /// <summary>The logical (oldest = 0) position of a ring index.</summary>
        private int Logical(int ring) => (ring - (_head - _count) + Capacity * 2) % Capacity;

        /// <summary>Where a recorded body stood (its transform) at <paramref name="time"/>.</summary>
        public bool BodyAt(Animator a, float time, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (a == null || !_bodyOf.TryGetValue(a, out var b) || !Bracket(time, out var i0, out var i1, out var k)) return false;
            if (!b.Active[i0] && !b.Active[i1]) return false;
            var a0 = b.Active[i0] ? i0 : i1;
            var a1 = b.Active[i1] ? i1 : i0;
            pos = V(b.Data, a0 * b.Stride, b.Data, a1 * b.Stride, k);
            return true;
        }
    }
}
