using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Hall
{
    /// <summary>
    /// Show lighting that keeps the hall alive: sweeping spotlight beams,
    /// a chase light along the stage edge, crest-cycling LED pillars beside
    /// the stage, a shine passing over the LED wall, and confetti bursts.
    /// </summary>
    public sealed class StageFx : MonoBehaviour
    {
        /// <summary>Beam pivots (at the lamp); each sweeps on its own phase.</summary>
        public List<Transform> Beams = new List<Transform>();
        public List<Renderer> BeamRenderers = new List<Renderer>();

        /// <summary>Stage-edge light segments, lit in a running pattern.</summary>
        public List<Renderer> Chase = new List<Renderer>();

        /// <summary>LED pillars beside the stage that cycle through the crests.</summary>
        public List<Renderer> Pillars = new List<Renderer>();
        public List<Texture2D> Crests = new List<Texture2D>();

        /// <summary>A soft diagonal band that slides across the LED wall now and then.</summary>
        public Transform? Shine;
        public float ShineFromX = -8f, ShineToX = 8f;

        public ParticleSystem? Confetti;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int Tint = Shader.PropertyToID("_Color");
        private readonly List<Quaternion> _rest = new List<Quaternion>();
        private MaterialPropertyBlock _block = null!;
        private float _shineAt;
        private int _crest;
        private float _crestAt;

        private static readonly Color[] BeamColours =
        {
            new Color(1f, 0.82f, 0.45f, 0.2f), new Color(0.75f, 0.85f, 1f, 0.17f), new Color(0.35f, 0.55f, 1f, 0.2f),
        };

        private void Start()
        {
            _block = new MaterialPropertyBlock();
            foreach (var b in Beams) _rest.Add(b.localRotation);
            _shineAt = Time.time + 4f;
            SetPillars();
        }

        /// <summary>Confetti over the stage (ceremony finale, big buys, the end).</summary>
        public void Burst(int count = 220)
        {
            if (Confetti != null) Confetti.Emit(count);
        }

        private void Update()
        {
            var t = Time.time;

            // Beams sweep slowly side to side and nod, each on its own phase, colours drifting.
            for (var i = 0; i < Beams.Count; i++)
            {
                var yaw = Mathf.Sin(t * 0.35f + i * 1.7f) * 28f;
                var pitch = Mathf.Sin(t * 0.23f + i * 2.3f) * 12f;
                Beams[i].localRotation = _rest[i] * Quaternion.Euler(pitch, yaw, 0);
                if (i < BeamRenderers.Count)
                {
                    var phase = (t * 0.06f + i * 0.33f) % 1f * BeamColours.Length;
                    var a = BeamColours[(int)phase % BeamColours.Length];
                    var b = BeamColours[((int)phase + 1) % BeamColours.Length];
                    _block.Clear();
                    _block.SetColor(Tint, Color.Lerp(a, b, phase - (int)phase));
                    BeamRenderers[i].SetPropertyBlock(_block);
                }
            }

            // Chase light: a bright run travelling along the stage edge.
            for (var i = 0; i < Chase.Count; i++)
            {
                var d = Mathf.Repeat(t * 6f - i, Chase.Count);
                var k = Mathf.Clamp01(1f - d / 4f);
                _block.Clear();
                _block.SetColor(EmissionColor, new Color(1f, 0.78f, 0.35f) * (0.6f + 3.2f * k));
                Chase[i].SetPropertyBlock(_block);
            }

            // Crest pillars: next franchise every few seconds.
            if (Crests.Count > 0 && t >= _crestAt)
            {
                _crestAt = t + 3.2f;
                _crest = (_crest + 1) % Crests.Count;
                SetPillars();
            }

            // Shine sweep across the LED wall every ~9 s.
            if (Shine != null)
            {
                var since = t - _shineAt;
                if (since > 9f) _shineAt = t;
                var p = Mathf.Clamp01(since / 1.6f);
                var pos = Shine.localPosition;
                pos.x = Mathf.Lerp(ShineFromX, ShineToX, p);
                Shine.localPosition = pos;
                Shine.gameObject.SetActive(since < 1.6f);
            }
        }

        private void SetPillars()
        {
            if (Crests.Count == 0) return;
            for (var i = 0; i < Pillars.Count; i++)
            {
                var tex = Crests[(_crest + i * (Crests.Count / 2)) % Crests.Count];
                _block.Clear();
                _block.SetTexture(BaseMap, tex);
                _block.SetTexture("_EmissionMap", tex);
                Pillars[i].SetPropertyBlock(_block);
            }
        }
    }
}
