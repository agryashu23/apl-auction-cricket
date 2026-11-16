using System;
using System.Collections.Generic;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Hawk-Eye on the ground: the ball's real path as a tube from the hand to the
    /// pad, the projection on to the stumps in a paler colour, where it pitched
    /// and where it struck (discs coloured by the verdict), the line of the
    /// stumps down the pitch and the wicket zone, and a ghost ball where it would
    /// have passed the stumps. Drawn from the engine's <see cref="BallTracking"/>
    /// and grown along with <see cref="Grow"/>.
    /// </summary>
    public sealed class HawkeyeView : MonoBehaviour
    {
        private LineRenderer _path = null!, _projected = null!;
        private Transform _pitch = null!, _impact = null!, _ghost = null!, _zone = null!, _lane = null!;
        private readonly List<Vector3> _pathPts = new List<Vector3>(), _projPts = new List<Vector3>();
        private Material _flat = null!;

        public static readonly Color In = new Color(0.25f, 0.85f, 0.4f), Call = new Color(1f, 0.72f, 0.2f), Out = new Color(0.92f, 0.28f, 0.28f);

        public static HawkeyeView Create()
        {
            var go = new GameObject("Hawkeye");
            var v = go.AddComponent<HawkeyeView>();
            v.Build();
            go.SetActive(false);
            return v;
        }

        private void Build()
        {
            _flat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit"));
            _path = Line("Path", new Color(0.95f, 0.25f, 0.2f, 0.95f));
            _projected = Line("Projected", new Color(0.5f, 0.75f, 1f, 0.75f));
            _lane = Piece(PrimitiveType.Cube, "Lane", new Color(0.3f, 0.55f, 1f, 0.28f));
            _zone = Piece(PrimitiveType.Cube, "WicketZone", new Color(0.3f, 0.55f, 1f, 0.38f));
            _pitch = Piece(PrimitiveType.Cylinder, "PitchSpot", In);
            _impact = Piece(PrimitiveType.Cylinder, "ImpactSpot", In);
            _ghost = Piece(PrimitiveType.Sphere, "Ghost", new Color(1, 1, 1, 0.7f));
        }

        private LineRenderer Line(string name, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = _flat;
            lr.startColor = lr.endColor = c;
            // The tube: the ball's own width, round ends.
            lr.widthMultiplier = 0.075f;
            lr.numCapVertices = 6;
            lr.numCornerVertices = 3;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.positionCount = 0;
            return lr;
        }

        private Transform Piece(PrimitiveType type, string name, Color c)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(transform, false);
            var r = go.GetComponent<Renderer>();
            r.material = new Material(_flat) { color = c };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        private static void Tint(Transform t, Color c) => t.GetComponent<Renderer>().material.color = c;

        /// <summary>Lay out <paramref name="tr"/> for this batter's hand (nothing drawn yet: <see cref="Grow"/> draws it).</summary>
        public void Show(BallTracking tr, bool leftHanded)
        {
            gameObject.SetActive(true);
            Vector3 W((double x, double y, double z) p) => GroundSpace.Ball(p.x, p.y + 0.036, p.z, leftHanded);
            _pathPts.Clear();
            _projPts.Clear();
            // At most MaxPoints a line, and the projection stopped at the stumps (r12: an evidence ball's
            // projection came back with millions of points — a LineRenderer that size froze the game 26 minutes).
            Thin(tr.Path, _pathPts, W, null);
            Thin(tr.Projected, _projPts, W, -GroundSpace.HalfPitch - 0.5f);
            const float half = (float)BallTracking.StumpsHalfWidth;
            var stumps = new Vector3(0, 0, -GroundSpace.HalfPitch);
            _lane.position = new Vector3(0, 0.006f, 0);
            _lane.localScale = new Vector3(half * 2, 0.004f, GroundSpace.HalfPitch * 2);
            _zone.position = stumps + new Vector3(0, (float)BallTracking.StumpsHeight / 2, 0);
            _zone.localScale = new Vector3(half * 2, (float)BallTracking.StumpsHeight, 0.02f);
            _pitch.position = W((tr.PitchX, -0.03, tr.PitchZ));
            _pitch.localScale = new Vector3(0.16f, 0.004f, 0.16f);
            Tint(_pitch, Color(tr.Pitching));
            _impact.position = W((tr.ImpactX, tr.ImpactY - 0.036, tr.ImpactZ));
            _impact.rotation = Quaternion.Euler(90, 0, 0);
            _impact.localScale = new Vector3(0.16f, 0.004f, 0.16f);
            Tint(_impact, Color(tr.Impact));
            _ghost.position = W((tr.StumpsX, tr.StumpsY, 0));
            _ghost.localScale = Vector3.one * 0.075f;
            Tint(_ghost, UiColor(tr.Wickets));
            Grow(0);
        }

        /// <summary>Draw it up to <paramref name="k"/>: 0–0.6 the flight to the pad, 0.6–1 the projection to the stumps.</summary>
        public void Grow(float k)
        {
            var a = Mathf.Clamp01(k / 0.6f);
            var b = Mathf.Clamp01((k - 0.6f) / 0.4f);
            Fill(_path, _pathPts, a);
            Fill(_projected, _projPts, b);
            _pitch.gameObject.SetActive(a >= 0.45f);
            _impact.gameObject.SetActive(a >= 0.999f);
            _ghost.gameObject.SetActive(b >= 0.999f);
        }

        private static void Fill(LineRenderer lr, List<Vector3> pts, float k)
        {
            var n = Mathf.Clamp(Mathf.CeilToInt(pts.Count * k), 0, pts.Count);
            lr.positionCount = n;
            for (var i = 0; i < n; i++) lr.SetPosition(i, pts[i]);
        }

        public void Hide() => gameObject.SetActive(false);

        private const int MaxPoints = 160;

        /// <summary>
        /// Every k-th point (and the last) so there are at most <see cref="MaxPoints"/>; with
        /// <paramref name="stopZ"/>, none past it (the projection only has to reach the stumps).
        /// </summary>
        private static void Thin(List<(double x, double y, double z)> src, List<Vector3> dst, Func<(double x, double y, double z), Vector3> w, float? stopZ)
        {
            var n = src.Count;
            if (n == 0) return;
            var every = Mathf.Max(1, Mathf.CeilToInt(n / (float)MaxPoints));
            for (var i = 0; i < n; i += every)
            {
                var p = w(src[i]);
                if (!IsFinite(p)) break;
                dst.Add(p);
                if (stopZ is float z && p.z <= z) return;
            }
            var last = w(src[n - 1]);
            if (IsFinite(last) && (stopZ == null || dst.Count == 0 || dst[dst.Count - 1].z > stopZ.Value)) dst.Add(stopZ is float sz && last.z < sz ? new Vector3(last.x, last.y, sz) : last);
        }

        private static bool IsFinite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        // The verdicts' colours (green fine for out, amber umpire's call, red the batter's way).
        public static Color Color(PitchVerdict v) => v == PitchVerdict.OutsideLeg ? Out : In;
        public static Color Color(ImpactVerdict v) => v == ImpactVerdict.InLine ? In : v == ImpactVerdict.UmpiresCall ? Call : Out;
        public static Color UiColor(WicketsVerdict v) => v == WicketsVerdict.Hitting ? In : v == WicketsVerdict.UmpiresCall ? Call : Out;

        public static string Text(PitchVerdict v) => v switch
        {
            PitchVerdict.InLine => "IN LINE",
            PitchVerdict.OutsideOff => "OUTSIDE OFF",
            PitchVerdict.OutsideLeg => "OUTSIDE LEG",
            _ => "FULL TOSS",
        };

        public static string Text(ImpactVerdict v) => v switch
        {
            ImpactVerdict.InLine => "IN LINE",
            ImpactVerdict.UmpiresCall => "UMPIRE'S CALL",
            ImpactVerdict.OutsideOff => "OUTSIDE OFF",
            _ => "OUTSIDE LEG",
        };

        public static string Text(WicketsVerdict v) => v switch
        {
            WicketsVerdict.Hitting => "HITTING",
            WicketsVerdict.UmpiresCall => "UMPIRE'S CALL",
            _ => "MISSING",
        };
    }
}
