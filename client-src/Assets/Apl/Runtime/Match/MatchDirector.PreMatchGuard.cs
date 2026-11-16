using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The last moments of the entry: a fielder's warm-up as he reaches his
    /// place, the striker taking guard (rhb_/lhb_take_guard: the mark scratched,
    /// a settle, into the stance) and a look round the field; then the intros.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private static List<string>? _warmUps;

        /// <summary>The motion pack's warm-ups (jumps, stretches, arm swings…), found by name.</summary>
        /// <remarks>Deep area 1 (slidewatch: "fielder lhb_warmup_shadow_shots" 0.33-3.3 m/s, "lhb_warmup_jump_and_tap"): the
        /// batters' ritual warm-ups (rhb_/lhb_, bat in hand, made in the batting frame at the crease) matched the name
        /// search too and played on fielders' bodies, with no bat, their feet stepping about a spot the batting frame put
        /// elsewhere. A fielder's warm-up is a fielder's clip: no batter (rhb_/lhb_) or bowler-hand (ra_/la_) clips.</remarks>
        private static List<string> WarmUps(Animator a) => _warmUps ??= ClipFiles()
            .Where(n => !SlideFix.Round1 || (!n.StartsWith("rhb_") && !n.StartsWith("lhb_") && !n.StartsWith("ra_") && !n.StartsWith("la_")))
            .Where(n => !n.StartsWith("catch") && !n.Contains("celebrat") &&
                        new[] { "warm", "stretch", "jump", "arm_swing", "arm_circle", "lunge", "high_knee" }.Any(n.Contains))
            .Where(n => Body.Has(a, n)).Distinct().ToList();

        /// <summary>He's at his place: a warm-up (now and then twice, or none), then the ready stance.</summary>
        private IEnumerator WarmUp(Animator a)
        {
            var clips = WarmUps(a);
            var times = clips.Count == 0 ? 0 : _visRng.Chance(0.25) ? 2 : _visRng.Chance(0.85) ? 1 : 0;
            var home = a.transform.position;
            var rot = a.transform.rotation;
            // Round 10c (characters): a fielder who got to his spot in the first walk was still warming up
            // when the next walk moved him on — this put him back on his old spot and into "Ready"
            // mid-stride (the r10 log's "walk put back" lines: Ready and the warm-up states), so his
            // feet glided. A walk starting (FootLock active) ends the warm-up where it is.
            bool Walking() => a == null || (a.GetComponent<FootLock>() is FootLock f && f.Active);
            for (var i = 0; i < times && a != null && a.gameObject.activeInHierarchy && !Walking(); i++)
            {
                var clip = clips[_visRng.NextInt(clips.Count)];
                a.speed = 1f;
                a.Play(clip, 0, 0f);
                yield return null;
                var length = Mathf.Clamp(a.GetCurrentAnimatorStateInfo(0).length, 0.5f, 6f);
                var until = Time.time + length * (clip.EndsWith("_loop") ? 2f : 1f);
                while (Time.time < until)
                {
                    if (Walking()) yield break;
                    yield return null;
                }
                // A jump or a stretch can wander a little: back to his spot.
                if (a != null) a.transform.SetPositionAndRotation(home, rot);
            }
            if (Walking()) yield break;
            if (a != null && a.gameObject.activeInHierarchy && Body.Has(a, "Ready")) a.CrossFadeInFixedTime("Ready", 0.3f, 0, Random.value);
        }

        /// <summary>
        /// Taking guard: the clip from the stance's spot (it ends in the stance),
        /// a little quicker than life; a look round the field when the pack has one;
        /// then the guard loop. From the batter's close-up camera.
        /// </summary>
        private IEnumerator TakeGuardRoutine(BatterRig rig)
        {
            StallProbe.Mark($"take guard: {rig.name}");
            rig.TakeGuard();
            var a = rig.GetComponent<Animator>();
            var clip = (rig.LeftHanded ? "lhb_" : "rhb_") + "take_guard";
            if (!Body.Has(a, clip)) yield break;
            Camera.LeftHanded = rig.LeftHanded;
            Camera.Show(MatchCamera.Mode.Batter, cut: true);
            _pre.ResetSkip();
            a.SetBool(Animator.StringToHash("Mirror"), false);
            // A couple of shadow shots first (the ceremony pack's warm-up), then he takes guard.
            var swings = (rig.LeftHanded ? "lhb_" : "rhb_") + "warmup_shadow_shots";
            if (Body.Has(a, swings))
            {
                a.speed = 1.4f;
                a.Play(swings, 0, 0.05f);
                yield return Hold(Secs(3.2f));
                rig.TakeGuard();
                a.SetBool(Animator.StringToHash("Mirror"), false);
            }
            // The round-3 clip (180 frames): guard from the umpire, the mark scratched, a look round the
            // field, then up into the ready stance — its last frame IS the stance idle's first, so the cut
            // to the guard loop is seamless. A touch quicker than life.
            a.speed = 1.2f;
            a.Play(clip, 0, 0f);
            yield return Hold(180f / 30f / 1.2f);
            a.speed = 1f;
            rig.TakeGuard();
        }

        /// <summary>Both openers and the opening bowler, each on his close-up with his card (the presentation's ShowIntro).</summary>
        private IEnumerator Intros()
        {
            if (_striker != null) yield return ShowIntro(_match.OnStrike.Player, _striker.GetComponent<Animator>(), Secs(2.6f));
            if (_other != null) yield return ShowIntro(_match.OffStrike.Player, _other.GetComponent<Animator>(), Secs(2.2f));
            if (_bowler == null) yield break;
            // The bowler at his mark, polishing the ball, while his card is up.
            var b = _bowler.GetComponent<Animator>();
            var polish = (Bowling.LeftArm(_match.CurrentBowler) ? "la_" : "ra_") + "intro_polish_ball_loop";
            var home = _bowler.transform.position;
            var rot = _bowler.transform.rotation;
            if (Body.Has(b, polish)) b.CrossFadeInFixedTime(polish, 0.25f);
            yield return ShowIntro(_match.CurrentBowler, b, Secs(2.4f));
            _bowler.transform.SetPositionAndRotation(home, rot);
            _bowler.Mark(_match.CurrentBowler.BowlType == Apl.Core.BowlType.Spin);
        }
    }
}
