using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The timing ring: from release a ring round the ball closes in on it and
    /// meets it at the perfect moment to tap — green while a tap would be GOOD,
    /// bright at PERFECT, red once it's too late. On the tap it holds and flashes
    /// in the colour the tap earned. (The meter on the right says the same thing;
    /// this puts it where the player is looking: on the ball.)
    /// </summary>
    public sealed partial class BallView
    {
        /// <summary>The ring starts this many drawn-ball sizes across and closes to <see cref="RingClosed"/>.</summary>
        public const float RingOpen = 9f, RingClosed = 1.7f;

        private static readonly Color RingWait = new Color(1f, 1f, 1f, 0.75f);
        private static readonly Color RingGood = new Color(0.35f, 1f, 0.45f, 0.95f);
        private static readonly Color RingPerfect = new Color(1f, 0.92f, 0.45f, 1f);
        private static readonly Color RingLate = new Color(1f, 0.3f, 0.25f, 0.9f);

        private Transform _ring = null!;
        private Material _ringMat = null!;
        private float _ringFrom = -1f, _tapAt, _good, _perfect, _tappedAt = -1f;
        private Color _tapColor;

        private void MakeRing()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = "TimingRing";
            _ringMat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit")) { mainTexture = RingTex(), color = RingWait };
            var r = quad.GetComponent<Renderer>();
            r.material = _ringMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _ring = quad.transform;
            quad.SetActive(false);
        }

        /// <summary>
        /// Start the ring now: it closes on the ball at <paramref name="tapAt"/> (Time.time of
        /// the perfect tap); <paramref name="good"/> and <paramref name="perfect"/> are the
        /// grades' half-windows in real seconds.
        /// </summary>
        public void TimeRing(float tapAt, float good, float perfect)
        {
            _ringFrom = Time.time;
            _tapAt = Mathf.Max(tapAt, _ringFrom + 0.05f);
            _good = good;
            _perfect = perfect;
            _tappedAt = -1f;
        }

        /// <summary>He's tapped: the ring holds and flashes how it was timed, then goes.</summary>
        public void RingTapped()
        {
            if (_ringFrom < 0 || _tappedAt >= 0) return;
            _tappedAt = Time.time;
            var off = Mathf.Abs(_tappedAt - _tapAt);
            _tapColor = off <= _perfect ? RingPerfect : off <= _good ? RingGood : RingLate;
        }

        public void StopRing()
        {
            _ringFrom = -1f;
            HideRing();
        }

        private void HideRing()
        {
            if (_ring != null && _ring.gameObject.activeSelf) _ring.gameObject.SetActive(false);
        }

        private void DestroyRing()
        {
            if (_ring != null) Destroy(_ring.gameObject);
        }

        private void Ring(Camera? cam)
        {
            if (_ringFrom < 0 || cam == null || !gameObject.activeInHierarchy)
            {
                HideRing();
                return;
            }
            var now = Time.time;
            float k;
            Color c;
            if (_tappedAt >= 0)
            {
                // Held where it was when he tapped, flashing its verdict, gone in 0.35 s.
                var since = (now - _tappedAt) / 0.35f;
                if (since >= 1f)
                {
                    StopRing();
                    return;
                }
                k = Progress(_tappedAt);
                c = _tapColor;
                c.a *= 1f - since;
            }
            else
            {
                k = Progress(now);
                var off = now - _tapAt;
                c = Mathf.Abs(off) <= _perfect ? RingPerfect : Mathf.Abs(off) <= _good ? RingGood : off > 0 ? RingLate : RingWait;
                // Past the good window: it's gone (nothing to aim at any more).
                if (off > _good * 2.5f)
                {
                    StopRing();
                    return;
                }
            }
            if (!_ring.gameObject.activeSelf) _ring.gameObject.SetActive(true);
            var size = _drawn * Mathf.Lerp(RingOpen, RingClosed, k);
            // Just in front of the ball, facing the camera.
            var toCam = (cam.transform.position - transform.position).normalized;
            _ring.SetPositionAndRotation(transform.position + toCam * (_drawn * 0.6f), cam.transform.rotation);
            _ring.localScale = Vector3.one * size;
            _ringMat.color = c;
        }

        /// <summary>0 at the start, 1 at the perfect tap (eased so it visibly speeds up as it closes).</summary>
        private float Progress(float at)
        {
            var u = Mathf.Clamp01((at - _ringFrom) / Mathf.Max(0.05f, _tapAt - _ringFrom));
            return u * u;
        }

        /// <summary>A crisp white ring (outer 100%, inner 78% of the radius).</summary>
        private static Texture2D RingTex()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "ball_timing_ring", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                var a = Mathf.Clamp01((1f - d) * 40f) * Mathf.Clamp01((d - 0.78f) * 40f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>A soft dot (the shadow).</summary>
        private static Texture2D Soft()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "ball_shadow", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                var a = Mathf.Clamp01(1f - d);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * (3f - 2f * a) * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
