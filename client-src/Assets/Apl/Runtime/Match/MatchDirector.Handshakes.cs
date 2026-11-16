using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The handshake line at the end (owner's list, item 22): the two sides in
    /// single file walking past each other down the middle, 1 m apart, right
    /// hands meeting. motion-other's <see cref="LineWalk"/> is a walk made in
    /// place at 0.98 m/s with one shake per step, so the files are carried at
    /// that pace and every pair shakes as it passes; without it, the plain walk.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private const string LineWalk = "handshake_line_walk_loop", PlainWalk = "run_01_walk_in_loop";

        /// <summary>The line clip's own pace (m/s) and the files' gap: the clip's hands meet 0.5 m to each side.</summary>
        private const float LinePace = 0.98f, LineGap = 1.4f, LineX = 0.5f;

        /// <summary>Both XIs file past each other; the camera side-on where they meet. Tap skips.</summary>
        private IEnumerator HandshakeLine(float seconds)
        {
            if (!_cast.Ready) yield break;
            foreach (var c in _extras) if (c != null) StopCoroutine(c);
            _extras.Clear();
            var a = _side.Values.Where(x => x != null).ToList();
            var b = _batters.Values.Select(x => x.GetComponent<Animator>()).Where(x => x != null).ToList();
            var line = a.Count > 0 && Body.Has(a[0], LineWalk);
            var walkers = new List<Coroutine>();
            // The not-out pair keep their bats, carried at the side; everyone else's goes (v9 f315: the batting
            // side walked the line in their stance, bats up — the batting rig's own pose and IK held them).
            var notOut = new HashSet<string>(_match.Order.Where(o => o.Batted && o.HowOut == null).Select(o => o.Player.Id));
            var carriers = new HashSet<Animator>(_batters.Where(kv => notOut.Contains(kv.Key) && kv.Value != null).Select(kv => kv.Value.GetComponent<Animator>()));
            // One file comes down from the bowler's end on the +x side, the other up from the keeper's.
            void File(List<Animator> side, float x, float dir)
            {
                for (var i = 0; i < side.Count; i++)
                {
                    var body = side[i];
                    body.gameObject.SetActive(true);
                    PlainKit(body);
                    body.speed = 1f;
                    var rig = body.GetComponent<BatterRig>();
                    var carry = rig != null && carriers.Contains(body);
                    // A batting rig off: its stance, hand IK and bat stop with it (OnDisable hides the bat).
                    if (rig != null && !carry) rig.enabled = false;
                    var start = new Vector3(x, 0, -dir * (1.2f + i * LineGap));
                    body.transform.SetPositionAndRotation(start, Quaternion.LookRotation(new Vector3(0, 0, dir)));
                    var end = start + new Vector3(0, 0, dir * 40f);
                    string clip;
                    float clipSpeed, pace;
                    if (carry)
                    {
                        // The walk-out's carry: bat in the bottom hand at his side.
                        rig!.Carry();
                        var g = BatGait(body, rig.LeftHanded);
                        (clip, clipSpeed, pace) = (g.Clip, g.Speed, 1.2f);
                    }
                    else if (Body.Has(body, LineWalk)) (clip, clipSpeed, pace) = (LineWalk, LinePace, LinePace);
                    else (clip, clipSpeed, pace) = (PlainWalk, 1.5f, 1.2f);
                    SlideScene.Tag(body, "handshake line walk", 1f);
                    walkers.Add(StartCoroutine(Strides.Walk(body, end, pace, clip, clipSpeed)));
                }
            }
            File(a, LineX, -1f);
            File(b, -LineX, 1f);
            Camera.Track(() => (new Vector3(7.5f, 1.6f, 0f), new Vector3(0, 1.1f, 0f)), fov: 46f, smooth: 0.3f, cut: true, priority: MatchCamera.Moment);
            _hud.SetSkippable(true);
            _skip = false;
            for (var until = Time.time + seconds; Time.time < until && !_skip;)
            {
                LineHands(a, b);
                yield return null;
            }
            foreach (var w in walkers) if (w != null) StopCoroutine(w);
            // Stood still where they stopped, not walking on the spot behind the next shots.
            foreach (var body in a.Concat(b))
            {
                if (body == null) continue;
                body.speed = 1f;
                var rig = body.GetComponent<BatterRig>();
                if (rig != null && rig.enabled) rig.Carry();
                else if (Body.Has(body, "Stand")) body.CrossFadeInFixedTime("Stand", 0.3f);
            }
            _hud.SetSkippable(false);
            Camera.Release(MatchCamera.Moment);
            _skip = false;
        }

        /// <summary>
        /// Hands that really meet (r10 `399`-`405`: the files walked past with no contact): each man
        /// and the nearest of the other file, while they're within 0.8 m along the line, pull their
        /// right hands to the point between them (PreMatchHandshake), strongest as they draw level.
        /// </summary>
        private static void LineHands(List<Animator> a, List<Animator> b)
        {
            foreach (var x in a)
            {
                if (x == null) continue;
                Animator? best = null;
                var gap = 0.8f;
                foreach (var y in b)
                {
                    if (y == null) continue;
                    var dz = Mathf.Abs(x.transform.position.z - y.transform.position.z);
                    if (dz >= gap) continue;
                    gap = dz;
                    best = y;
                }
                var hx = x.GetComponent<PreMatchHandshake>();
                if (best == null)
                {
                    if (hx != null) hx.Weight = 0f;
                    continue;
                }
                var hy = best.GetComponent<PreMatchHandshake>();
                if (hx == null) hx = x.gameObject.AddComponent<PreMatchHandshake>();
                if (hy == null) hy = best.gameObject.AddComponent<PreMatchHandshake>();
                hx.Other = hy;
                hy.Other = hx;
                var w = Mathf.SmoothStep(0f, 1f, 1f - gap / 0.8f);
                hx.Weight = hy.Weight = w;
            }
        }
    }
}
