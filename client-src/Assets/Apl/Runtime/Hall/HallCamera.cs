using System;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Hall
{
    /// <summary>A named camera position: where it sits and what it looks at.</summary>
    [System.Serializable]
    public sealed class Shot
    {
        public string Name = "";
        public Vector3 Position;
        public Vector3 LookAt;

        /// <summary>Vertical field of view, authored for a 16:9 screen.</summary>
        public float FieldOfView = 40f;

        /// <summary>
        /// Keep the 16:9 width on every screen (wides and the stage: nothing new
        /// creeps in at the edges on 2.2:1, nothing is lost on 4:3). Otherwise the
        /// height is kept (faces stay the same size), widening only on screens
        /// narrower than 16:9.
        /// </summary>
        public bool FitWidth;
    }

    /// <summary>
    /// Broadcast-style camera: cuts or glides between shots (wide hall, stage,
    /// auctioneer, each team's table), with a slight handheld drift so static
    /// shots feel alive. No Cinemachine dependency on purpose.
    /// Every change of shot is logged as "[APL hallcam] shot reason".
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class HallCamera : MonoBehaviour
    {
        public List<Shot> Shots = new List<Shot>();
        public float GlideTime = 0.6f;
        public float DriftAmount = 0.03f;

        /// <summary>The 16:9 every shot is authored at.</summary>
        private const float AuthoredAspect = 16f / 9f;

        /// <summary>FitWidth keeps the 16:9 width only up to 2:1; wider screens keep the 2:1 height
        /// (past that, holding the width squeezes the top and bottom under the HUD).</summary>
        private const float MaxFitAspect = 2f;

        private string? _pendingShotEvent;
        private float _pendingAt;

        private Camera _camera = null!;
        private Shot? _target;
        private Vector3 _velocity;
        private Vector3 _lookVelocity;
        private Vector3 _look;
        private float _fovVelocity;

        public string Current => _target?.Name ?? "";

        /// <summary>Game time of the last change of shot.</summary>
        public float ShownAt { get; private set; } = -100f;

        /// <summary>Raised on every change of shot (the shot's name).</summary>
        public event Action<string>? ShotChanged;

        /// <summary>Title-screen move: a slow swing around the hall, facing the stage.</summary>
        public bool Cinematic { get; private set; }
        private float _cinematicStart;

        public void StartCinematic()
        {
            Cinematic = true;
            _cinematicStart = Time.unscaledTime;
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        public bool Has(string name) => Shots.Exists(s => s.Name == name);

        /// <summary>Hard cut (new subject) or glide (same area). Re-showing the current shot is a no-op.</summary>
        public void Show(string name, bool cut = true, string reason = "")
        {
            var shot = Shots.Find(s => s.Name == name);
            if (shot == null) return;
            if (!Cinematic && _target == shot) return;
            Cinematic = false;
            _target = shot;
            ShownAt = Time.time;
            Debug.Log($"[APL hallcam] t={Time.time:0.00} {name} {(string.IsNullOrEmpty(reason) ? "-" : reason)} {(cut ? "cut" : "glide")} fov {FovFor(shot):0.0} aspect {_camera.aspect:0.00}");
            if (cut)
            {
                transform.position = shot.Position;
                _look = shot.LookAt;
                transform.LookAt(_look);
                _camera.fieldOfView = FovFor(shot);
                _velocity = _lookVelocity = Vector3.zero;
                _pendingShotEvent = null;
                ShotChanged?.Invoke(name);
                return;
            }
            // A glide: what the new shot hides (or shows) changes when the move is over, not while
            // the old framing is still on screen.
            _pendingShotEvent = name;
            _pendingAt = Time.time + GlideTime * 1.3f;
        }

        /// <summary>The vertical FOV that frames <paramref name="shot"/> on this screen (see <see cref="Shot.FitWidth"/>).</summary>
        public float FovFor(Shot shot)
        {
            var aspect = Mathf.Max(0.5f, _camera.aspect);
            var halfWidth = Mathf.Tan(shot.FieldOfView * 0.5f * Mathf.Deg2Rad) * AuthoredAspect;
            var keepWidth = 2f * Mathf.Atan(halfWidth / aspect) * Mathf.Rad2Deg;
            if (shot.FitWidth) return 2f * Mathf.Atan(halfWidth / Mathf.Min(aspect, MaxFitAspect)) * Mathf.Rad2Deg;
            return aspect < AuthoredAspect ? Mathf.Max(shot.FieldOfView, keepWidth) : shot.FieldOfView;
        }

        private void LateUpdate()
        {
            if (_pendingShotEvent != null && Time.time >= _pendingAt)
            {
                var done = _pendingShotEvent;
                _pendingShotEvent = null;
                ShotChanged?.Invoke(done);
            }
            if (Cinematic)
            {
                var c = Time.unscaledTime - _cinematicStart;
                var a = Mathf.Sin(c * 0.07f) * 0.75f;
                var pos = new Vector3(Mathf.Sin(a) * 12f, 4.4f + Mathf.Sin(c * 0.11f) * 0.9f, 6f - Mathf.Cos(a) * 12f);
                transform.position = Vector3.Lerp(transform.position, pos, Mathf.Clamp01(Time.unscaledDeltaTime * 1.5f));
                transform.LookAt(new Vector3(0, 3.1f, 10f));
                _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, 52f, Time.unscaledDeltaTime);
                return;
            }
            if (_target == null) return;
            var t = Time.time;
            var drift = new Vector3(Mathf.PerlinNoise(t * 0.3f, 0) - 0.5f, Mathf.PerlinNoise(0, t * 0.3f) - 0.5f, 0) * DriftAmount;
            transform.position = Vector3.SmoothDamp(transform.position, _target.Position + drift, ref _velocity, GlideTime);
            _look = Vector3.SmoothDamp(_look, _target.LookAt, ref _lookVelocity, GlideTime * 0.8f);
            transform.LookAt(_look);
            _camera.fieldOfView = Mathf.SmoothDamp(_camera.fieldOfView, FovFor(_target), ref _fovVelocity, GlideTime);
        }
    }
}
