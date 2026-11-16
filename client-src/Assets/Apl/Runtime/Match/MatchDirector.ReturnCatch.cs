using System.Collections;
using System.Collections.Generic;
using Apl.Core.Play;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Caught and bowled. The return-catch clips (Resources/Data/cricket/after_ball)
    /// are whole actions — run-up, release, follow-through, the catch — sharing the
    /// bowling clip's run-up, so they cross-fade in from the action on the same
    /// transform. The clip whose hands come nearest the ball's path is chosen
    /// (chest high, one hand to his left, diving forward), he's eased across the
    /// last bit, and it's started so its catch frame lands as the ball arrives.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private sealed class ReturnClip
        {
            public string Name = "";
            public float Release, Catch, Length;
            public bool OneHandLeft;
        }

        private static List<ReturnClip>? _returnClips;

        private static IReadOnlyList<ReturnClip> ReturnClips
        {
            get
            {
                if (_returnClips != null) return _returnClips;
                _returnClips = new List<ReturnClip>();
                var text = Resources.Load<TextAsset>("Data/cricket/after_ball");
                if (text == null) return _returnClips;
                foreach (JObject c in (JArray)JObject.Parse(text.text)["clips"]!)
                {
                    var file = (string)c["file"]!;
                    if (!file.Contains("caught_and_bowled") || c["catch_frame"] == null) continue;
                    var name = file.EndsWith(".fbx") ? file.Substring(0, file.Length - 4) : file;
                    _returnClips.Add(new ReturnClip
                    {
                        Name = name,
                        Release = ((int?)c["release_frame"] ?? 1) / 30f - 1 / 30f,
                        Catch = ((int)c["catch_frame"]! - 1) / 30f,
                        Length = ((int?)c["frames"] ?? 200) / 30f,
                        OneHandLeft = name.Contains("one_hand_left"),
                    });
                }
                return _returnClips;
            }
        }

        /// <summary>Move a body by <paramref name="shift"/> evenly over <paramref name="seconds"/> (no one-frame jump).</summary>
        private static IEnumerator SpreadStep(Animator a, Vector3 shift, float seconds)
        {
            var done = Vector3.zero;
            for (var t = 0f; t < seconds && a != null;)
            {
                t = Mathf.Min(seconds, t + Time.deltaTime);
                var want = shift * (t / seconds);
                a.transform.position += want - done;
                done = want;
                yield return null;
            }
        }

        /// <summary>The hand that holds the return catch.</summary>
        private Transform? _returnHand;

        /// <summary>
        /// The bowler takes the return catch: picks and starts the clip; returns
        /// when (s after the hit) the ball meets his hands, or null (no clips: the
        /// plain catch fallback plays).
        /// </summary>
        private float? ReturnCatch(BallResult r, bool spin)
        {
            var a = Refs.Bowler;
            _returnHand = null;
            if (a == null || !_bowler.UsesClips) return null;
            var prefix = (_bowler.LeftArm ? "la_" : "ra_") + "caught_and_bowled_" + (spin ? "spin_" : "pace_");
            var now = Time.time - _hitAt;
            var end = r.Path.InAir ? (float)r.Path.Hang : 1.5f;
            ReturnClip? best = null;
            float bestMiss = float.MaxValue, bestT = 0;
            Vector3 bestHand = default;
            foreach (var c in ReturnClips)
            {
                if (!c.Name.StartsWith(prefix) || !Body.Has(a, c.Name)) continue;
                var (l, rt) = FieldPlay.HandsAt(a, c.Name, c.Length, c.Catch);
                var local = c.OneHandLeft ? l : (l + rt) / 2;
                var hand = a.transform.position + a.transform.rotation * local;
                // Where along the ball's way it passes nearest his hands (he needs a moment to get there).
                for (var t = now + 0.3f; t <= end; t += 0.02f)
                {
                    var p = PathAt(t);
                    var miss = Flat(p - hand).magnitude + 2f * Mathf.Abs(p.y - hand.y);
                    if (miss >= bestMiss) continue;
                    bestMiss = miss;
                    best = c;
                    bestT = t;
                    bestHand = hand;
                }
            }
            if (best == null) return null;
            // Across the last bit so his hands are on it (the cross-fade hides the step).
            var shift = Vector3.ClampMagnitude(Flat(PathAt(bestT) - bestHand), 1.2f);
            // Deep area 3 (review b2: a plan that missed by 3.66 m had the ball bent 3.5 m sideways onto his hands):
            // what's left after the step must be within his arms' reach, or no return-catch clip — he runs to it and
            // takes it with the clip for its height (Hit's plain catch, MatchDirector.CatchHands.CatchClipAt).
            var left = Vector3.Distance(PathAt(bestT), bestHand + shift);
            if (left > CatchHands.MaxReach)
            {
                if (Autoplay) Debug.Log($"[APL match] caught and bowled: no return-catch clip reaches ({best.Name} {left:0.00} m off after a {shift.magnitude:0.00} m step); he runs to it");
                return null;
            }
            _bowler.Release();
            // (Review b3: not in one frame — spread over the clip's 0.25 s cross-fade, done before his hands meet it.)
            Extra(SpreadStep(a, shift, Mathf.Clamp((bestT - now) * 0.8f, 0.05f, 0.25f)));
            a.speed = 1f;
            a.SetBool(Animator.StringToHash("Mirror"), false);
            a.CrossFadeInFixedTime(best.Name, 0.25f, 0, Mathf.Max(best.Release, best.Catch - (bestT - now)));
            // Deep area 3: his palm(s), reaching for the ball's real line over the last 0.25 s (it was the wrist bone).
            _catchTruth = OnTurf(PathAt(bestT));
            StartCatchReach(a, _hitAt + bestT, best.Name, "return", best.OneHandLeft, false);
            _returnHand = _catchReach!.CupTransform;
            if (Autoplay) Debug.Log($"[APL match] caught and bowled: {best.Name}, hands on it at {bestT:0.00}s, moved {shift.magnitude:0.00} m, miss {bestMiss:0.00}");
            return bestT;
        }
    }
}
