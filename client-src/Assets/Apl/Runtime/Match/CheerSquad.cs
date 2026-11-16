using System.Collections;
using System.Collections.Generic;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Two cheerleaders on each platform (StadiumBuilder.CheerSpots), one platform
    /// in each side's colours: a light idle sway all match, a dance on a four or a
    /// six (StadiumBuilder.CrowdCheer calls <see cref="Dance"/>). Dances are the
    /// motion pack's cheer_dance_* clips when installed, else the applause loop.
    /// </summary>
    public sealed class CheerSquad : MonoBehaviour
    {
        private static readonly string[] Dances = { "cheer_dance_1", "cheer_dance_2", "cheer_dance_3", "applause_loop", "victory" };
        private static readonly string[] Idles = { "cheer_idle_loop", "stand_idle" };

        public static CheerSquad? Instance { get; private set; }

        private readonly List<Animator> _girls = new List<Animator>();
        private Coroutine? _dance;

        /// <summary>The squad for this match (none if the stadium has no platforms or no cheer bodies were built).</summary>
        public static CheerSquad? Create(CricketCast cast, Franchise a, Franchise b)
        {
            if (StadiumBuilder.CheerSpots.Count == 0 || cast.Cheerleaders == 0) return null;
            var squad = new GameObject("CheerSquad").AddComponent<CheerSquad>();
            Instance = squad;
            var n = 0;
            for (var s = 0; s < StadiumBuilder.CheerSpots.Count; s++)
            {
                var (at, facing) = StadiumBuilder.CheerSpots[s];
                var right = Vector3.Cross(Vector3.up, facing).normalized;
                foreach (var side in new[] { -0.7f, 0.7f })
                {
                    var girl = cast.Cheerleader(n++, s % 2 == 0 ? a : b);
                    if (girl == null) continue;
                    girl.transform.SetPositionAndRotation(at + right * side, Quaternion.LookRotation(facing));
                    squad._girls.Add(girl);
                    Play(girl, Idles, Random.Range(0f, 1f));
                }
            }
            return squad;
        }

        /// <summary>Into a dance for a few seconds (longer for a big moment), then back to the sway.</summary>
        public void Dance(float intensity)
        {
            if (_dance != null) StopCoroutine(_dance);
            _dance = StartCoroutine(Dancing(2.5f + 3f * Mathf.Clamp01(intensity)));
        }

        private IEnumerator Dancing(float seconds)
        {
            for (var i = 0; i < _girls.Count; i++)
            {
                if (_girls[i] == null) continue;
                // Pairs in step, the two platforms a beat apart.
                var start = new List<string>(Dances);
                var pick = (i / 2 + (int)(Time.time * 0.37f)) % 3;
                start.Insert(0, Dances[pick]);
                Play(_girls[i], start.ToArray(), (i / 2) * 0.12f);
            }
            yield return new WaitForSeconds(seconds);
            foreach (var g in _girls) if (g != null) Play(g, Idles, Random.Range(0f, 1f));
            _dance = null;
        }

        /// <summary>The first of <paramref name="states"/> the controller has, from <paramref name="at"/> (0–1).</summary>
        private static void Play(Animator a, string[] states, float at)
        {
            foreach (var s in states)
            {
                var hash = Animator.StringToHash(s);
                if (!a.HasState(0, hash)) continue;
                a.CrossFadeInFixedTime(hash, 0.25f, 0, at);
                return;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
