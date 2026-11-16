using System.Collections;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The non-striker run out at the bowler's end — the engine's call (rules-bowling),
    /// the view's here. A straight drive the bowler gets a fingertip to in his
    /// follow-through, deflected onto the stumps with the non-striker backed up out of
    /// his ground; or the bowler, in his delivery stride, breaking the stumps with the
    /// ball in his hand as the non-striker leaves early. Both side-on at the bowler's
    /// stumps (the run-out camera), the wicket broken for the third umpire.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>
        /// The engine's deflection run out: out at the bowler's end with nothing run
        /// (no race between the wickets), off a hit straight back.
        /// </summary>
        private static bool BowlerEndDeflection(BallResult r) =>
            r.RunOut && !r.RunOutAtKeeperEnd && r.Running == null && System.Math.Abs(r.ExitAngle) < 25;

        /// <summary>
        /// The fingertip: the bowler reaches for the return catch (the one-handed return-catch
        /// clip, timed so his hand meets the ball), it brushes his fingers and goes on into the
        /// stumps at his end. Hit plays this instead of the field play; the caller refers and
        /// gives the wicket.
        /// </summary>
        private IEnumerator DeflectionOntoStumps(BallResult r)
        {
            var stumps = new Vector3(0, 0, GroundSpace.HalfPitch);
            WatchStumps(false, new Vector3(_hitLeft ? 6f : -6f, 0, 0));
            var touch = ReturnCatch(r, r.Delivery.IsSpin) ?? 0.45f;
            while (Time.time - _hitAt < touch)
            {
                _ball.position = OnTurf(PathAt(Time.time - _hitAt));
                yield return null;
            }
            // Off his fingertips, on into the stumps.
            var from = _ball.position;
            var to = stumps + new Vector3(0.03f, 0.4f, 0);
            for (var t = 0f; t < 0.22f; t += Time.deltaTime)
            {
                _ball.position = Vector3.Lerp(from, to, t / 0.22f);
                yield return null;
            }
            BreakWicket(false, Flat(to - from));
            yield return Roll(to, Flat(to - from).normalized, 3f, 1f);
            yield return new WaitForSeconds(0.8f);
            Camera.EndFielding(false);
        }

        /// <summary>
        /// Backing up too far: the bowler stops in his delivery stride and breaks the stumps
        /// at his end with the ball in his hand (the rules-bowling flow calls this instead of
        /// the delivery when the engine says so), then appeals.
        /// </summary>
        private IEnumerator BreakNonStrikerEnd()
        {
            var a = Refs.Bowler;
            if (a == null) yield break;
            _bowler.Release();
            WatchStumps(false, new Vector3(-6f, 0, 0));
            var hand = a.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand != null) Extra(Hold(hand, 3f));
            // The arm comes down onto the bails.
            Event(a, CricketClips.FieldClipNamed("throw_19_underarm_throw"));
            yield return new WaitForSeconds(0.2f);
            BreakWicket(false, Vector3.back);
            yield return new WaitForSeconds(0.5f);
            Body.Play(a, "fielder_appeal_standing");
            yield return new WaitForSeconds(1.0f);
            Camera.EndFielding(false);
        }

        // ------------------------------------------------------- the non-striker's side of the stumps
        // Owner (2026-11-16): "the bowler bowling from round the wicket, the batsman stays there, so it collides".
        // Two causes: (1) he always stood 0.9 m wide, but a round-the-wicket clip is placed so its hand meets the
        // engine's release point (spin 0.5 m wide) while its own hand is ~0.97 m wide, so the whole run-up shifts
        // ~0.47 m toward him and the bowler's body comes down x≈0, his front arm into the non-striker; (2) when the
        // side switched mid-over his walk across was cut off by BackUp (0.4 s into the run-up, StopSettling stops
        // the walk), leaving him mid-crossing in the bowler's path. Now his spot is worked out per ball from the
        // bowler's actual corridor (the clip sampled, cached per clip), he walks there and the run-up waits for
        // him, and every ball logs the closest the two came: "[APL nonstriker] ...".

        /// <summary>Dev (-aplroundwicket): every bowler comes round the wicket (the AI's balls and the user's).</summary>
        public static readonly bool RoundWicketDev = System.Environment.GetCommandLineArgs().Contains("-aplroundwicket");

        /// <summary>His spot: at least this wide of the middle stump, at most this (m); his hips at least this clear of the bowler's.</summary>
        private const float NsMinWide = 1.0f, NsMaxWide = 1.5f, NsClear = 1.05f;

        /// <summary>Only where the bowler passes within this of the non-striker's crease (z, m) does his line matter.</summary>
        private const float NsZoneHalf = 3.0f;

        /// <summary>Per clip: the bowler's hips across (x, relative to his transform facing the batter) near the non-striker.</summary>
        private readonly System.Collections.Generic.Dictionary<string, Vector2> _corridors = new System.Collections.Generic.Dictionary<string, Vector2>();

        /// <summary>-aplroundwicket: the AI's ball comes from round the wicket (side and release point; dev only).</summary>
        private static void ForceRoundWicket(Delivery d, bool leftHandedBatter)
        {
            if (!RoundWicketDev || d.AroundWicket) return;
            d.AroundWicket = true;
            d.ReleaseX = Bowling.ReleaseX(d.IsSpin, d.LeftArm, true, leftHandedBatter);
        }

        /// <summary>
        /// Where the non-striker stands across (world x) for this ball: on the other side of the stumps from the
        /// bowler's line (the release side: RA over / LA round on +x, RA round / LA over on −x — the same for either
        /// batter's hand, the engine's frame being mirrored back), 1.0–1.5 m wide, clear of the bowler's hips by
        /// <see cref="NsClear"/> where the clip lets him.
        /// </summary>
        private float NonStrikerSpotX(Delivery d, Vector3 releaseAt)
        {
            var side = releaseAt.x >= 0 ? -1f : 1f;
            var wide = 1.1f;
            var clip = _bowler != null && _bowler.UsesClips ? CricketClips.PickBowl(_match.CurrentBowler, d, new Rng(1)) : null;
            var c = clip != null ? BowlerCorridor(clip, releaseAt) : (Vector2?)null;
            if (c != null)
            {
                // The bowler's hips at their nearest to his side of the stumps.
                var edge = side > 0 ? c.Value.y : -c.Value.x;
                wide = Mathf.Clamp(edge + NsClear, NsMinWide, NsMaxWide);
            }
            return side * wide;
        }

        /// <summary>The bowler's hips across (world x: min, max) near the non-striker's crease, for <paramref name="clip"/> placed to release at <paramref name="releaseAt"/>.</summary>
        private Vector2? BowlerCorridor(BowlClip clip, Vector3 releaseAt)
        {
            var a = Refs.Bowler;
            if (a == null) return null;
            var hand = _bowler.HandAtRelease(clip);
            var origin = new Vector3(releaseAt.x - hand.x, 0, releaseAt.z - hand.z);
            if (!_corridors.TryGetValue(clip.Name, out var rel))
            {
                var hips = a.GetBoneTransform(HumanBodyBones.Hips);
                if (hips == null) return null;
                // Sampled as HandAtRelease measures the hand: at the origin facing the batter, then put back.
                var t = a.transform;
                var (pos, rot) = (t.position, t.rotation);
                var info = a.GetCurrentAnimatorStateInfo(0);
                t.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(Vector3.back, Vector3.up));
                var nsZ = BatterRig.FarCrease + BatterRig.InsideCrease;
                float lo = float.MaxValue, hi = float.MinValue;
                var length = Mathf.Max(0.01f, clip.Length);
                for (var s = Mathf.Max(0f, clip.ReleaseTime - 4f); s <= Mathf.Min(clip.Length, clip.ReleaseTime + 0.6f); s += 0.05f)
                {
                    a.Play(clip.Name, 0, s / length);
                    a.Update(0f);
                    var z = origin.z + hips.position.z;
                    if (Mathf.Abs(z - nsZ) > NsZoneHalf) continue;
                    lo = Mathf.Min(lo, hips.position.x);
                    hi = Mathf.Max(hi, hips.position.x);
                }
                t.SetPositionAndRotation(pos, rot);
                a.Play(info.shortNameHash, 0, info.normalizedTime);
                a.Update(0f);
                if (lo > hi) return null;
                rel = new Vector2(lo, hi);
                _corridors[clip.Name] = rel;
                Debug.Log($"[APL nonstriker] corridor {clip.Name}: hips x {lo:+0.00;-0.00}..{hi:+0.00;-0.00} m off his mark near the crease");
            }
            return new Vector2(origin.x + rel.x, origin.x + rel.y);
        }

        /// <summary>The run-up waits (up to 3 s) for the non-striker to finish crossing to his side.</summary>
        private IEnumerator NonStrikerSettled()
        {
            var from = Time.time;
            while (_other != null && _other.CrossingToPlace && Time.time - from < 3f) yield return null;
            if (Time.time - from > 0.05f) Debug.Log($"[APL nonstriker] run-up held {Time.time - from:0.00} s for the non-striker to reach his side{(_other != null && _other.CrossingToPlace ? " (gave up: still walking)" : "")}");
        }

        /// <summary>From the run-up to just past the release: the closest the bowler and the non-striker (hips, on the ground) came.</summary>
        private IEnumerator NonStrikerGapWatch(Delivery d)
        {
            var other = _other;
            var a = Refs.Bowler;
            var b = other != null ? other.GetComponent<Animator>() : null;
            var bh = a != null ? a.GetBoneTransform(HumanBodyBones.Hips) : null;
            var nh = b != null ? b.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (bh == null || nh == null) yield break;
            var min = float.MaxValue;
            var from = Time.time;
            var spot = _nonStrikerX;
            yield return null;
            while (Time.time - from < 8f && (_released < 0 || Time.time < _released + 0.5f))
            {
                if (_other != other) break;
                var g = new Vector2(bh.position.x - nh.position.x, bh.position.z - nh.position.z).magnitude;
                min = Mathf.Min(min, g);
                yield return null;
            }
            if (min == float.MaxValue) yield break;
            // Left / right as the bowler sees it running in (he faces −z: his left is +x).
            Debug.Log($"[APL nonstriker] bowler {(d.LeftArm ? "LA" : "RA")} {(d.AroundWicket ? "round" : "over")}: stands {(spot > 0 ? "left" : "right")} of stumps, min gap to bowler during delivery {min:0.00} m"
                + $" (spot x {spot:+0.00;-0.00}, non-striker {(other!.LeftHanded ? "LH" : "RH")}{(min < 0.8f ? ", TOO CLOSE" : "")})");
        }
    }
}
