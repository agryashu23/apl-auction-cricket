using System;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Final rating fix #3 (P156: the camera went inside the dismissed batter's helmet at the RUN OUT banner):
    /// a near-clip guard for every match shot. After the shot is placed each frame, no player's head may be
    /// closer than <see cref="HeadClear"/> to the lens (nor his chest closer than <see cref="BodyClear"/>):
    /// the camera is pulled back along its own view line — the look point stays, so the framing reads the
    /// same, only a little wider. The bodies are the director's (both sides on the field and the umpires).
    /// </summary>
    public sealed partial class MatchCamera
    {
        /// <summary>The players to keep the lens out of (set by the director; null: no guard).</summary>
        public Func<IEnumerable<Animator?>>? GuardBodies;

        public const float HeadClear = 0.6f, BodyClear = 0.45f;

        /// <summary>The furthest the guard pulls back in one frame (a shot that's really inside a crowd is left alone past this).</summary>
        private const float MaxPull = 3f;

        private readonly List<(Animator who, Transform? head, Transform? chest)> _guard = new List<(Animator, Transform?, Transform?)>();
        private readonly Dictionary<Animator, (Transform? head, Transform? chest)> _bones = new Dictionary<Animator, (Transform?, Transform?)>();
        private float _guardListAt = -99f;
        private bool _guardOn;

        /// <summary>The bodies' head and chest bones, refreshed a few times a second (walk-outs and recasts change them).</summary>
        private void GuardList()
        {
            if (Time.unscaledTime - _guardListAt < 0.25f) return;
            _guardListAt = Time.unscaledTime;
            _guard.Clear();
            if (_bones.Count > 160) _bones.Clear(); // recast bodies are destroyed later: don't keep their bones forever
            var bodies = GuardBodies?.Invoke();
            if (bodies == null) return;
            foreach (var a in bodies)
            {
                if (a == null || !a.isActiveAndEnabled) continue;
                if (!_bones.TryGetValue(a, out var b))
                {
                    Transform? head = null, chest = null;
                    if (a.isHuman)
                    {
                        head = a.GetBoneTransform(HumanBodyBones.Head);
                        chest = a.GetBoneTransform(HumanBodyBones.Chest) ?? a.GetBoneTransform(HumanBodyBones.Spine);
                    }
                    b = (head, chest);
                    _bones[a] = b;
                }
                _guard.Add((a, b.head, b.chest));
            }
        }

        /// <summary>Called after the shot is placed in LateUpdate: pull back along the view line until every head is clear.</summary>
        private void Guard()
        {
            if (GuardBodies == null) return;
            GuardList();
            if (_guard.Count == 0) return;
            var pos = transform.position;
            var fwd = transform.forward;
            var pulled = 0f;
            string? who = null;
            var closest = float.MaxValue;
            for (var pass = 0; pass < 3; pass++)
            {
                var need = 0f;
                foreach (var (a, head, chest) in _guard)
                {
                    if (a == null) continue;
                    // The head bone sits at the neck's top: the helmet's middle is ~10 cm above it.
                    if (head != null) Need(head.position + Vector3.up * 0.1f, HeadClear, a);
                    if (chest != null) Need(chest.position, BodyClear, a);
                    if (head == null && chest == null) Need(a.transform.position + Vector3.up * 1.6f, HeadClear, a);
                }
                if (need <= 0.001f) break;
                need = Mathf.Min(need, MaxPull - pulled);
                if (need <= 0.001f) break;
                pos -= fwd * need;
                pulled += need;

                void Need(Vector3 p, float clear, Animator body)
                {
                    var d = p - pos;
                    var dist = d.magnitude;
                    if (dist >= clear) return;
                    var along = Vector3.Dot(d, fwd);
                    // Behind the lens (and not on it): pulling back would only go into him.
                    if (along < -0.05f) return;
                    var perp2 = Mathf.Max(0f, d.sqrMagnitude - along * along);
                    var s = Mathf.Sqrt(Mathf.Max(0f, clear * clear - perp2)) - along;
                    s = Mathf.Max(s, clear - dist) + 0.02f;
                    if (s > need)
                    {
                        need = s;
                        who = body.name;
                    }
                    if (dist < closest) closest = dist;
                }
            }
            if (pulled <= 0.001f)
            {
                if (_guardOn) Debug.Log("[APL camguard] clear again (no head within the guard)");
                _guardOn = false;
                return;
            }
            transform.position = pos;
            // The smoothing mustn't keep driving into him: drop the velocity toward the look.
            var into = Vector3.Dot(_velocity, fwd);
            if (into > 0) _velocity -= fwd * into;
            transform.LookAt(_look);
            if (!_guardOn) Debug.Log($"[APL camguard] lens {closest:0.00} m from {who} ({Current}, hold {Hold}): pulled back {pulled:0.00} m along the view line");
            _guardOn = true;
        }
    }
}
