using System.Collections;
using Apl.Client.Audio;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The ground's impact sounds and moods (round 8, audio): the ball off the pitch
    /// and the outfield (heard from its own path — a hard fall turning into a rise near
    /// the turf), into the boundary cushion, onto the pad, into the keeper's gloves;
    /// the crowd's level and roar following how tight the game is; and the ground's
    /// own music over the walk-out, the breaks and the result (never under play,
    /// where it would sit on the commentary).
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>A replay is on the match camera now (the big screen's feed tags it REPLAY).</summary>
        public bool ReplayOnAir => _replays != null && _replays.Running;

        /// <summary>The impact sounds and moods start with the match's other audio (AudioStart).</summary>
        private void AudioFxStart()
        {
            StartCoroutine(BallSounds());
            StartCoroutine(CrowdMood());
            StartCoroutine(GroundMusic());
        }

        /// <summary>
        /// The bat sound's kind (round 13): an edge or a play-and-miss nick "edge"; a lofted shot,
        /// a six or a big middled hit "loft"; any other stroke along the ground "stroke".
        /// </summary>
        private static string BatKind(BallResult r, bool nick) =>
            nick || r.Edge ? "edge"
            : r.Shot.Type == ShotType.Loft || r.Six || (r.Timing == Timing.Perfect && r.ExitSpeed >= 34) ? "loft"
            : "stroke";

        /// <summary>At the ball's moment (Outcome, beside ContactFeel): the pad, when it's the pad it hit.</summary>
        private void ImpactSounds(BallResult r, bool hit)
        {
            if (hit || !r.PadHit || r.Wide || _striker == null) return;
            var pads = Hips(_striker).position + Vector3.down * 0.55f;
            _msfx.Thud("pad", pads, 0.95f);
        }

        /// <summary>Past the bat and into the keeper's gloves (Outcome, when the ball arrives).</summary>
        private void GloveSound()
        {
            if (Refs.Keeper == null) return;
            _msfx.Thud("glove", Hips(Refs.Keeper).position + Vector3.up * 0.2f, Random.Range(0.8f, 1f));
        }

        /// <summary>Listens to the ball's path: a bounce on the turf, the boundary cushion.</summary>
        private IEnumerator BallSounds()
        {
            while (_ball == null) yield return null;
            var prev = _ball.position;
            var prevV = Vector3.zero;
            var beyond = false;
            var lastBounce = -1f;
            while (true)
            {
                yield return null;
                var dt = Time.deltaTime;
                if (dt <= 1e-5f || _ball == null) continue;
                var p = _ball.position;
                var v = (p - prev) / dt;
                prev = p;
                // A teleport (the ball back in the bowler's hand, a new delivery) is no movement at all.
                var jump = v.sqrMagnitude > 70f * 70f || !_ball.gameObject.activeInHierarchy;
                var outside = RopeGap(p) < 0f;
                if (!jump)
                {
                    if (prevV.y < -1.5f && v.y > 0.3f && p.y < 0.35f && Time.time - lastBounce > 0.12f)
                    {
                        lastBounce = Time.time;
                        // The delivery's bounce on the pitch carries; a dribble in the outfield barely.
                        _msfx.Thud("bounce", p, 0.15f + 0.8f * Mathf.InverseLerp(1.5f, 12f, -prevV.y));
                    }
                    var flat = new Vector2(v.x, v.z).magnitude;
                    if (outside && !beyond && p.y < 1.2f && flat > 1.5f) _msfx.Thud("rope", p, Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(2f, 20f, flat)));
                }
                beyond = outside;
                prevV = jump ? Vector3.zero : v;
            }
        }

        /// <summary>
        /// How tight it is, once a second: the crowd's level and its roar between the
        /// moments (a swell settles back to these). A close chase late on has the ground
        /// humming; a stroll or the first innings' early overs stay calm.
        /// </summary>
        private IEnumerator CrowdMood()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(1f);
                if (_match == null || _msfx == null) continue;
                _msfx.SetBase(1.1f + 0.7f * Tension(), 0.35f * Tension());
            }
        }

        /// <summary>0 (nothing on it) … 1 (the last over, either side can win).</summary>
        private float Tension()
        {
            if (_match.Finished) return 0f;
            var total = Mathf.Max(6, _match.Overs * 6);
            var late = 1f - Mathf.Clamp01(_match.BallsLeft / Mathf.Min(36f, total));
            if (!_match.Chasing) return 0.2f * late;
            var balls = _match.BallsLeft;
            var rate = balls > 0 ? _match.RunsNeeded * 6f / balls : 36f;
            // Closest at a required rate the side can just about make (~9 an over).
            var close = 1f - Mathf.Clamp01(Mathf.Abs(rate - 9f) / 9f);
            var t = close * (0.35f + 0.65f * late);
            if (balls <= 6 && _match.RunsNeeded <= 3 * balls + 2) t = Mathf.Max(t, 0.8f);
            // Down to the last pair.
            if (_match.Wickets >= _match.Order.Count - 2) t = Mathf.Max(t, 0.6f * late + 0.2f);
            return Mathf.Clamp01(t);
        }

        /// <summary>
        /// The ground's music (round 10, owner: no music under play, and the darbuka track is
        /// gone): from the first ball nothing but the crowd; the calm APL anthem, low, only at a
        /// full match's innings break and at the result. The PA's welcome before a fresh match.
        /// </summary>
        private IEnumerator GroundMusic()
        {
            var state = -1;
            while (true)
            {
                // (First a beat: the nets are only known once Start has run.)
                yield return new WaitForSecondsRealtime(0.5f);
                if (_match == null || _nets != NetsLaunch.Kind.None) continue;
                // 0 play (or a fresh match), 1 the innings break, 2 the result.
                var now = _match.MatchOver || (!_match.TwoInnings && _match.Finished) ? 2
                    : _match.Log.Count == 0 && _match.InningsNumber == 2 ? 1 : 0;
                if (now == state) continue;
                var first = state < 0;
                state = now;
                if (first || now == 2) BoxMusicCue(now == 2);
                if (now == 0) Sfx.StopMusic(1.5f);
                // At the result, after the finale and the result card have had their moment (round 12: it came in on the last ball's contact).
                else if (now == 2) StartCoroutine(After(7f, () => Sfx.PlayMusic("anthem", 0.22f, 3f)));
                else Sfx.PlayMusic("anthem", 0.16f, 3f);
            }
        }
    }
}
