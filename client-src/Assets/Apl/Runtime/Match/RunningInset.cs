using System;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The picture-in-picture of the batters running (owner, round 2): while
    /// they're running and the main camera is out with the ball and the fielder,
    /// a second camera films the pitch side-on — both batters, both creases —
    /// into a small render texture shown in an inset at the bottom of the screen
    /// (<see cref="MatchHud.ShowInset"/>). Mobile-cheap: 480×270, a short far
    /// plane, no shadows' worth of distance, and only on while they run.
    /// </summary>
    public sealed class RunningInset : MonoBehaviour
    {
        private const int Width = 480, Height = 270;

        private Camera _cam = null!;
        private RenderTexture _rt = null!;
        private MatchHud _hud = null!;
        private Func<bool> _active = null!;
        private Func<(Vector3 a, Vector3 b)?> _batters = null!;
        private bool _on;
        private float _offAt;
        private Vector3 _mid;

        /// <summary>
        /// The inset: on while <paramref name="active"/> says so (and a moment after),
        /// framing the two bodies <paramref name="batters"/> gives (hips, world).
        /// </summary>
        public static RunningInset Create(Camera main, MatchHud hud, Func<bool> active, Func<(Vector3 a, Vector3 b)?> batters)
        {
            var go = new GameObject("RunningInset");
            var inset = go.AddComponent<RunningInset>();
            inset._hud = hud;
            inset._active = active;
            inset._batters = batters;
            inset._rt = new RenderTexture(Width, Height, 16) { name = "RunningInset", antiAliasing = 1 };
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.targetTexture = inset._rt;
            cam.farClipPlane = 120f;
            cam.depth = main.depth - 1;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            cam.enabled = false;
            inset._cam = cam;
            return inset;
        }

        private void LateUpdate()
        {
            var want = _active();
            if (want) _offAt = Time.unscaledTime + 0.6f;
            var on = want || Time.unscaledTime < _offAt;
            var pair = on ? _batters() : null;
            if (pair == null) on = false;
            if (on != _on)
            {
                _on = on;
                _cam.enabled = on;
                _hud.ShowInset(on ? _rt : null);
                if (on) _mid = Mid(pair!.Value);
            }
            if (!on) return;
            Frame(pair!.Value);
            _hud.UpdateInset();
        }

        private static Vector3 Mid((Vector3 a, Vector3 b) p) => (p.a + p.b) * 0.5f;

        /// <summary>Side-on, square of the middle of the pitch, wide enough for both of them and a crease's worth either side.</summary>
        private void Frame((Vector3 a, Vector3 b) p)
        {
            _mid = Vector3.Lerp(_mid, Mid(p), 1 - Mathf.Exp(-Time.unscaledDeltaTime * 4f));
            var z = Mathf.Clamp(_mid.z, -5f, 5f);
            const float dist = 17f;
            var spread = Mathf.Max(Mathf.Abs(p.a.z - p.b.z), 14f) + 5f;
            var hfov = 2f * Mathf.Atan(spread * 0.5f / dist);
            var vfov = 2f * Mathf.Atan(Mathf.Tan(hfov * 0.5f) / ((float)Width / Height)) * Mathf.Rad2Deg;
            transform.position = new Vector3(dist, 3.4f, z);
            transform.LookAt(new Vector3(0, 0.8f, z));
            _cam.fieldOfView = Mathf.Clamp(vfov, 18f, 50f);
        }

        private void OnDestroy()
        {
            if (_rt != null) _rt.Release();
        }
    }
}
