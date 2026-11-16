using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The ball's movement cues (<see cref="BallSpin"/> drives them): a faint
    /// ghost of the last 0.2 s of a swinging ball's path — thin, tapering,
    /// barely there, never a streak — and a puff of pitch dust at the bounce,
    /// sprayed along the ball's new line and kicked sideways the way it seams
    /// or turns. A dozen particles; one line renderer.
    /// </summary>
    public sealed class BallCues
    {
        private const float GhostSeconds = 0.2f, GhostWidth = 0.02f, GhostAlpha = 0.16f;

        private LineRenderer _line = null!;
        private ParticleSystem _dust = null!;
        private readonly List<(Vector3 p, float t)> _pts = new List<(Vector3, float)>();
        private float _clock;

        public static BallCues Make(Transform ball)
        {
            var c = new BallCues();
            var sprite = Shader.Find("Sprites/Default");
            var ghost = new GameObject("BallGhost");
            c._line = ghost.AddComponent<LineRenderer>();
            c._line.useWorldSpace = true;
            c._line.material = new Material(sprite);
            c._line.widthCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, GhostWidth));
            c._line.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                alphaKeys = new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(GhostAlpha, 1) },
            };
            c._line.numCapVertices = 2;
            c._line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            c._line.positionCount = 0;

            var puff = new GameObject("BallDust");
            c._dust = puff.AddComponent<ParticleSystem>();
            c._dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = c._dust.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
            main.gravityModifier = 0.35f;
            main.maxParticles = 40;
            var emission = c._dust.emission;
            emission.enabled = false;
            var shape = c._dust.shape;
            shape.enabled = false;
            var size = c._dust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(1, 1.6f)));
            var colour = c._dust.colorOverLifetime;
            colour.enabled = true;
            colour.color = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                alphaKeys = new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) },
            };
            var pr = puff.GetComponent<ParticleSystemRenderer>();
            pr.material = new Material(sprite) { mainTexture = SoftDot() };
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return c;
        }

        public void Clear()
        {
            _pts.Clear();
            _line.positionCount = 0;
        }

        /// <summary>Each frame: the ball's position while it's swinging, else null (the ghost runs out).</summary>
        public void Ghost(Vector3? at, float dt)
        {
            _clock += dt;
            if (at.HasValue) _pts.Add((at.Value, _clock));
            _pts.RemoveAll(p => _clock - p.t > GhostSeconds);
            _line.positionCount = _pts.Count;
            for (var i = 0; i < _pts.Count; i++) _line.SetPosition(i, _pts[i].p);
        }

        /// <summary>
        /// The bounce at <paramref name="at"/>, leaving along <paramref name="velocity"/>;
        /// <paramref name="deviation"/> (m, + = toward world +x) is how far it moves off
        /// the pitch; a seamer or a spinner kicks the dust that way.
        /// </summary>
        public void Bounce(Vector3 at, Vector3 velocity, float deviation, bool kicks)
        {
            var flat = new Vector3(velocity.x, 0, velocity.z);
            var dir = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, dir) * Mathf.Sign(Vector3.Dot(Vector3.Cross(Vector3.up, dir), Vector3.right) * deviation);
            var kick = kicks ? Mathf.Clamp(Mathf.Abs(deviation) * 9f, 0.5f, 2.2f) : 0f;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (var i = 0; i < 14; i++)
            {
                var r = Random.insideUnitSphere;
                ep.position = new Vector3(at.x, 0.03f, at.z) + new Vector3(r.x, 0, r.z) * 0.06f;
                ep.velocity = dir * Random.Range(0.8f, 2f) + Vector3.up * Random.Range(0.4f, 1.1f) + side * (kick * Random.Range(0.4f, 1f)) + r * 0.25f;
                ep.startColor = new Color(0.78f, 0.67f, 0.48f, Random.Range(0.45f, 0.75f));
                _dust.Emit(ep, 1);
            }
        }

        private static Texture2D? _dot;

        private static Texture2D SoftDot()
        {
            if (_dot != null) return _dot;
            const int n = 32;
            _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                _dot.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1 - d) * Mathf.Clamp01(1 - d)));
            }
            _dot.Apply();
            return _dot;
        }
    }
}
