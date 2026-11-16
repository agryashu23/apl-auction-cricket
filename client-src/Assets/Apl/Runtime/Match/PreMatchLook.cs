using System;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Where a ceremony body looks (the toss party, round 7: they all looked at
    /// their feet): Unity's look-at IK turns the head (and a little of the chest)
    /// to <see cref="At"/> — the presenter, the other captain, the coin, the
    /// camera — blending in and out, so heads come up level whatever the clip
    /// does. <see cref="Nod"/> adds a listener's small nods. Removed after the
    /// ceremony (it never outlives its body's part in it).
    /// </summary>
    public sealed class PreMatchLook : MonoBehaviour
    {
        /// <summary>The point to look at (null: let the clip have the head back).</summary>
        public Func<Vector3>? At;

        /// <summary>Nodding along (a listener).</summary>
        public bool Nod;

        private Animator _a = null!;
        private float _weight;
        private Vector3 _point;

        private void Awake() => _a = GetComponent<Animator>();

        private void OnDisable() => Destroy(this);

        private void Update()
        {
            var want = At != null ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, want, Time.deltaTime * 2.5f);
            if (At != null)
            {
                var p = At();
                // A nod: the point dips and comes back up, about once a second and a half.
                if (Nod) p += Vector3.down * (0.07f * Mathf.Max(0, Mathf.Sin(Time.time * 4.2f)));
                _point = _weight <= 0.01f ? p : Vector3.Lerp(_point, p, 1 - Mathf.Exp(-Time.deltaTime * 8f));
            }
        }

        private void OnAnimatorIK(int layer)
        {
            if (_weight <= 0.001f) return;
            // Light now the clips are made looking level at eye height (motion-other, round 7): the clip's own
            // glances and nods show through; this only turns the head the last bit to whoever he faces.
            _a.SetLookAtWeight(_weight, 0.05f, 0.55f, 0.9f, 0.5f);
            _a.SetLookAtPosition(_point);
        }

        /// <summary>Have <paramref name="a"/> look at <paramref name="at"/> (null: stop), nodding if <paramref name="nod"/>.</summary>
        public static void Set(Animator? a, Func<Vector3>? at, bool nod = false)
        {
            if (a == null) return;
            var look = a.GetComponent<PreMatchLook>();
            if (look == null)
            {
                if (at == null) return;
                look = a.gameObject.AddComponent<PreMatchLook>();
            }
            look.At = at;
            look.Nod = nod;
        }

        /// <summary>His eyes (a little above the head bone), for others to look at.</summary>
        public static Func<Vector3> Eyes(Animator? a)
        {
            var head = a != null ? a.GetBoneTransform(HumanBodyBones.Head) : null;
            var fallback = a != null ? a.transform.position + Vector3.up * 1.65f : Vector3.up * 1.65f;
            return () => head != null ? head.position + Vector3.up * 0.07f : fallback;
        }

        /// <summary>Takes the look off <paramref name="a"/> at once (his part's over).</summary>
        public static void Clear(Animator? a)
        {
            if (a == null) return;
            foreach (var l in a.GetComponents<PreMatchLook>()) Destroy(l);
        }
    }
}
