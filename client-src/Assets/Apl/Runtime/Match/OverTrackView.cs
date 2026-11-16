using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>One delivery's tracked flight for the over's tracking view: world points (release → bounce → bat / pad), where it pitched, its colour.</summary>
    public sealed class OverBall
    {
        public readonly List<Vector3> Path = new List<Vector3>();
        public Vector3 Bounce;
        public bool FullToss;
        public Color Colour;
        public string Length = "", Outcome = "";
    }

    /// <summary>
    /// OVER IN TRACKING (owner v26 #20, revised: "record it and show all balls at end of the over"):
    /// every delivery of the over drawn together on the ground — each flight a thin tube in its
    /// outcome's colour, a disc where it pitched — grown one after another by <see cref="Grow"/>.
    /// A fixed pool (12 lines, 12 discs, built once) so showing it allocates nothing.
    /// </summary>
    public sealed class OverTrackView : MonoBehaviour
    {
        public const int Max = 12;
        private readonly LineRenderer[] _lines = new LineRenderer[Max];
        private readonly Transform[] _spots = new Transform[Max];
        private readonly Material[] _spotMats = new Material[Max];
        private Transform _lane = null!;
        private readonly List<OverBall> _balls = new List<OverBall>();
        private Material _flat = null!;

        public static OverTrackView Create()
        {
            var go = new GameObject("OverTracking");
            var v = go.AddComponent<OverTrackView>();
            v.Build();
            go.SetActive(false);
            return v;
        }

        private void Build()
        {
            _flat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit"));
            // The stumps' lane down the pitch, faint, so line and length read against it.
            var lane = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(lane.GetComponent<Collider>());
            lane.name = "Lane";
            lane.transform.SetParent(transform, false);
            var lr0 = lane.GetComponent<Renderer>();
            lr0.material = new Material(_flat) { color = new Color(0.3f, 0.55f, 1f, 0.22f) };
            lr0.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr0.receiveShadows = false;
            _lane = lane.transform;
            for (var i = 0; i < Max; i++)
            {
                var go = new GameObject("Flight" + i);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.material = _flat;
                lr.widthMultiplier = 0.06f;
                lr.numCapVertices = 4;
                lr.numCornerVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.positionCount = 0;
                _lines[i] = lr;
                var spot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(spot.GetComponent<Collider>());
                spot.name = "Pitched" + i;
                spot.transform.SetParent(transform, false);
                var r = spot.GetComponent<Renderer>();
                _spotMats[i] = r.material = new Material(_flat);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                spot.transform.localScale = new Vector3(0.2f, 0.004f, 0.2f);
                _spots[i] = spot.transform;
            }
        }

        /// <summary>Lays the over out (nothing drawn until <see cref="Grow"/>).</summary>
        public void Show(IReadOnlyList<OverBall> balls)
        {
            gameObject.SetActive(true);
            _balls.Clear();
            for (var i = 0; i < balls.Count && i < Max; i++) _balls.Add(balls[i]);
            _lane.position = new Vector3(0, 0.005f, 0);
            _lane.localScale = new Vector3(0.228f, 0.003f, GroundSpace.HalfPitch * 2);
            for (var i = 0; i < Max; i++)
            {
                var on = i < _balls.Count;
                _lines[i].positionCount = 0;
                _lines[i].gameObject.SetActive(on);
                _spots[i].gameObject.SetActive(false);
                if (!on) continue;
                var b = _balls[i];
                _lines[i].startColor = UiKitColour(b.Colour, 0.95f);
                _lines[i].endColor = UiKitColour(b.Colour, 0.95f);
                _spots[i].position = new Vector3(b.Bounce.x, 0.012f + i * 0.001f, b.Bounce.z);
                _spotMats[i].color = b.Colour;
            }
            Grow(0);
        }

        private static Color UiKitColour(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>0–1: the deliveries draw in one after another (each over 1 / count of the time), each disc once its ball has pitched.</summary>
        public void Grow(float k)
        {
            var n = _balls.Count;
            if (n == 0) return;
            for (var i = 0; i < n; i++)
            {
                var part = Mathf.Clamp01(k * n - i);
                var pts = _balls[i].Path;
                var count = Mathf.Clamp(Mathf.CeilToInt(pts.Count * part), 0, pts.Count);
                var lr = _lines[i];
                if (lr.positionCount != count)
                {
                    lr.positionCount = count;
                    for (var j = 0; j < count; j++) lr.SetPosition(j, pts[j]);
                }
                _spots[i].gameObject.SetActive(!_balls[i].FullToss && part >= 0.55f);
            }
        }

        public void Hide()
        {
            _balls.Clear();
            gameObject.SetActive(false);
        }
    }
}
