using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// What the ball is doing, readable at a glance: the seam (the leather
    /// texture's equator, plus a raised ring) spins in its own plane — upright
    /// and backspinning for pace (tilted toward slip or leg when it swings),
    /// rolled over on a sideways axis for spin — a faint 0.2 s ghost of the
    /// curve when it swings, and at the bounce a puff of dust kicked the way it
    /// deviates. After the hit it just tumbles end over end. Set per delivery
    /// by <see cref="BallView.Release(Delivery, bool)"/>.
    /// </summary>
    public sealed class BallSpin : MonoBehaviour
    {
        /// <summary>Spin rates (turns a second) slowed to what reads on screen.</summary>
        private const float PaceTurns = 4.5f, SpinTurns = 6.5f, TumbleTurns = 3f;

        private Delivery? _d;
        private bool _lh, _bounced, _hit;
        private float _angle, _since;
        private Vector3 _last, _vel;
        private BallCues _cues = null!;
        private BallView? _view;

        public static BallSpin On(GameObject ball)
        {
            var s = ball.GetComponent<BallSpin>();
            if (s != null) return s;
            s = ball.AddComponent<BallSpin>();
            s._cues = BallCues.Make(ball.transform);
            Seam(ball.transform);
            return s;
        }

        /// <summary>A new delivery leaves the hand.</summary>
        public void Bowl(Delivery d, bool leftHanded)
        {
            _d = d;
            _lh = leftHanded;
            _bounced = _hit = false;
            _since = 0;
            _last = transform.position;
            _vel = Vector3.zero;
            _cues.Clear();
        }

        private void LateUpdate()
        {
            var dt = Time.deltaTime;
            if (dt <= 0 || _d == null) return;
            if (_view == null) _view = GetComponent<BallView>();
            if (_view != null && _view.InHand)
            {
                // Carried in the run-up: still, seam as he holds it; the next delivery resets the rest.
                _last = transform.position;
                return;
            }
            _since += dt;
            var v = (transform.position - _last) / dt;
            _last = transform.position;
            if (v.sqrMagnitude < 0.01f) return;
            // Bounce: falling, then rising, near the ground. A hit: the direction turns sharply.
            if (!_bounced && !_hit && _vel.y < -0.5f && v.y > 0.2f && transform.position.y < 0.35f)
            {
                _bounced = true;
                _cues.Bounce(transform.position, v, (float)(_lh ? -_d.Deviation : _d.Deviation), _d.IsSpin || System.Math.Abs(_d.Deviation) > 0.015);
            }
            else if (!_hit && _vel.sqrMagnitude > 1 && Vector3.Dot(v.normalized, _vel.normalized) < 0.3f) _hit = true;
            _vel = v;

            var dir = v.normalized;
            var flat = new Vector3(dir.x, 0, dir.z).normalized;
            Vector3 axis;
            float turns;
            if (_hit || flat.sqrMagnitude < 0.5f)
            {
                axis = Vector3.Cross(Vector3.up, flat.sqrMagnitude > 0.5f ? flat : Vector3.forward).normalized;  // end over end
                turns = TumbleTurns;
            }
            else if (_d.IsSpin)
            {
                // Rolled over: the axis lies near the line of flight, turned toward the side it spins from.
                var off = _d.Kind == DeliveryKind.OffSpin ? 1f : -1f;
                if (_d.LeftArm) off = -off;
                axis = Quaternion.AngleAxis(40f * off, Vector3.up) * flat;
                turns = SpinTurns;
            }
            else
            {
                // Seam up, backspinning; angled toward where it swings.
                var tilt = Mathf.Clamp((float)_d.Swing * 80f, -22f, 22f) * (_lh ? -1 : 1);
                axis = Quaternion.AngleAxis(tilt, Vector3.up) * Vector3.Cross(flat, Vector3.up).normalized;
                turns = PaceTurns;
            }
            _angle = (_angle + turns * 360f * dt) % 360f;
            // The sphere's seam is its local equator: local up = the spin axis, then turn about it.
            transform.rotation = Quaternion.AngleAxis(_angle, axis) * Quaternion.FromToRotation(Vector3.up, axis);

            // The swing ghost: only a real swinger, in the air, before the bounce.
            var swinging = !_d.IsSpin && System.Math.Abs(_d.Swing) > 0.1 && !_bounced && !_hit;
            _cues.Ghost(swinging ? transform.position : (Vector3?)null, dt);
        }

        /// <summary>A raised seam ring round the equator (a thin torus on the unit sphere), stitch-grey.</summary>
        private static void Seam(Transform ball)
        {
            const int around = 32, tube = 4;
            const float r = 0.5f, t = 0.018f;
            var v = new Vector3[around * tube];
            var tris = new int[around * tube * 6];
            for (var i = 0; i < around; i++)
            for (var j = 0; j < tube; j++)
            {
                var a = i * Mathf.PI * 2 / around;
                var b = j * Mathf.PI * 2 / tube;
                var rr = r + t * Mathf.Cos(b);
                v[i * tube + j] = new Vector3(rr * Mathf.Cos(a), t * Mathf.Sin(b), rr * Mathf.Sin(a));
                var k = (i * tube + j) * 6;
                int i1 = (i + 1) % around, j1 = (j + 1) % tube;
                tris[k] = i * tube + j; tris[k + 1] = i * tube + j1; tris[k + 2] = i1 * tube + j1;
                tris[k + 3] = i * tube + j; tris[k + 4] = i1 * tube + j1; tris[k + 5] = i1 * tube + j;
            }
            var mesh = new Mesh { name = "BallSeam", vertices = v, triangles = tris };
            mesh.RecalculateNormals();
            var go = new GameObject("Seam");
            go.transform.SetParent(ball, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { color = new Color(0.62f, 0.64f, 0.68f) };
            m.SetColor("_BaseColor", new Color(0.62f, 0.64f, 0.68f));
            m.SetFloat("_Smoothness", 0.3f);
            m.SetFloat("_Cull", 0f);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
