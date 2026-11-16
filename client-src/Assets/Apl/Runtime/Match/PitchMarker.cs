using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The blue ring where the ball will pitch: a translucent ring with a spot in
    /// the middle. It comes up early — fading in while the bowler is still about
    /// 1.5 s from letting go (the engine has the ball's bounce point before the
    /// run-up starts) — so the batter can read the length, and goes when the
    /// ball lands.
    /// </summary>
    public sealed class PitchMarker : MonoBehaviour
    {
        private (Material m, float alpha)[] _parts = System.Array.Empty<(Material, float)>();
        private float _k, _target, _rate = 4f;

        public static PitchMarker Create()
        {
            var go = new GameObject("PitchMarker");
            var marker = go.AddComponent<PitchMarker>();
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var parts = new System.Collections.Generic.List<(Material, float)>();
            // (The dark outline under the ring only shows in the aim style: match-play's bowling marker.)
            var order = 0;
            foreach (var (name, radius, alpha) in new[] { ("Outline", 0.5f, 0.8f), ("Ring", 0.42f, 0.38f), ("Spot", 0.15f, 0.85f) })
            {
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(disc.GetComponent<Collider>());
                disc.name = name;
                disc.transform.SetParent(go.transform, false);
                disc.transform.localScale = new Vector3(radius * 2, name == "Outline" ? 0.002f : 0.003f, radius * 2);
                var r = disc.GetComponent<Renderer>();
                var m = new Material(shader) { color = new Color(0.25f, 0.6f, 1f, 0f) };
                // Drawn in a fixed order, outline first (round 13: the three transparent discs sorted at
                // random, and the dark outline drawn OVER the yellow ring made it a dark mustard).
                m.renderQueue = 3100 + order++;
                r.material = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                parts.Add((m, alpha));
            }
            marker._parts = parts.ToArray();
            go.SetActive(false);
            return marker;
        }

        /// <summary>
        /// Drawn this many times its size (match-play: the user's aim marker is read from the broadcast
        /// camera, 20 m away — r10 found it hard to see; 1 for the batter's read of the length).
        /// </summary>
        public void Emphasis(float scale)
        {
            _emphasis = scale;
            Apply();
        }

        private float _emphasis = 1f;
        private bool _aim;

        /// <summary>
        /// The bowling aim's look. Owner v26 #15: "too big and multi-coloured" — now one plain blue: a
        /// stronger blue ring and a solid blue spot, no dark outline and no yellow (round 12's pale blue
        /// read poorly on beige, so it's a deeper, brighter blue than the batter's soft marker).
        /// Off: the batter's soft blue marker.
        /// </summary>
        public void AimStyle(bool on)
        {
            _aim = on;
            Apply();
        }

        /// <summary>Fade in at <paramref name="at"/> over <paramref name="fade"/> s.</summary>
        public void Show(Vector3 at, float fade)
        {
            transform.position = at;
            if (!gameObject.activeSelf)
            {
                _k = 0f;
                gameObject.SetActive(true);
            }
            _target = 1f;
            _rate = 1f / Mathf.Max(0.01f, fade);
            Apply();
        }

        /// <summary>Fade out over <paramref name="fade"/> s (0: gone now).</summary>
        public void Hide(float fade)
        {
            _target = 0f;
            _rate = 1f / Mathf.Max(0.01f, fade);
            if (fade <= 0f || !gameObject.activeSelf)
            {
                _k = 0f;
                gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            _k = Mathf.MoveTowards(_k, _target, _rate * Time.deltaTime);
            Apply();
            if (_k <= 0f && _target <= 0f) gameObject.SetActive(false);
        }

        private void Apply()
        {
            // Opens out a little as it fades in.
            transform.localScale = Vector3.one * _emphasis * (0.75f + 0.25f * Mathf.SmoothStep(0, 1, _k));
            for (var i = 0; i < _parts.Length; i++)
            {
                var (m, alpha) = _parts[i];
                // Unlit (Sprites/Default); the aim's blue a touch over 1 so the tonemapper can't dull it.
                var c = i == 0 ? Color.clear
                    : !_aim ? new Color(0.25f, 0.6f, 1f)
                    : new Color(0.15f, 0.55f, 1.35f);
                var a = i == 0 ? 0f : _aim ? (i == 1 ? 0.55f : 0.95f) : alpha;
                m.color = new Color(c.r, c.g, c.b, a * _k);
            }
        }
    }
}
