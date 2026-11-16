using System.Collections;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Between balls (focus batch 1, BAT-2: "the same static crouch on every ball"): before most balls he does
    /// something at the crease — re-marks his guard (looking up at the umpire and round the field), stretches with
    /// the bat, shadows a shot, a little jump and bat tap — picked at random, never the same one twice running,
    /// then eases into his stance in good time. The pick-up in the bowler's stride (PickUp) follows from there.
    /// </summary>
    public sealed partial class BatterRig
    {
        private static readonly string[] Rituals = { "take_guard", "warmup_bat_stretch", "warmup_shadow_shots", "warmup_jump_and_tap" };
        private string? _lastRitual;
        private Coroutine? _ritual;

        /// <summary>The ritual clips' lengths (ceremony pack frames / 30).</summary>
        private static float RitualLength(string r) => r switch
        {
            "take_guard" => 180 / 30f,
            "warmup_bat_stretch" => 176 / 30f,
            "warmup_jump_and_tap" => 116 / 30f,
            "warmup_shadow_shots" => 318 / 30f,
            _ => 3f,
        };

        /// <summary>
        /// A ritual for at most <paramref name="maxSeconds"/> (the clips run 4–10 s: a long one is cut there, easing
        /// into the stance); 0 if none, or by chance he just waits. Returns the seconds it will take.
        /// </summary>
        public float Ritual(float maxSeconds, float chance = 0.8f)
        {
            if (!UsesClips || maxSeconds < 1.5f || Random.value > chance) return 0f;
            var hand = LeftHanded ? "lhb_" : "rhb_";
            var options = new System.Collections.Generic.List<string>();
            foreach (var r in Rituals)
            {
                if (r != _lastRitual && Body.Has(_animator, hand + r)) options.Add(r);
            }
            if (options.Count == 0) return 0f;
            var pick = options[Random.Range(0, options.Count)];
            _lastRitual = pick;
            var clip = hand + pick;
            StopSettling();
            _carry = false;
            _free = false;
            transform.SetPositionAndRotation(_home, Facing);
            if (!Body.Play(_animator, clip, Facing * Vector3.forward)) return 0f;
            if (_ritual != null) StopCoroutine(_ritual);
            var seconds = Mathf.Min(maxSeconds, RitualLength(pick));
            _ritual = StartCoroutine(BackToStance(Animator.StringToHash(clip), Time.time + seconds));
            return seconds;
        }

        /// <summary>When the ritual ends or its time is up (and only if nothing else — the pick-up — has taken him), into the stance.</summary>
        private IEnumerator BackToStance(int hash, float until)
        {
            yield return null;
            while (true)
            {
                var info = _animator.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash != hash) break;
                if (info.normalizedTime >= 0.95f || Time.time >= until)
                {
                    transform.SetPositionAndRotation(_home, Facing);
                    PlayGuard(0.45f);
                    break;
                }
                yield return null;
            }
            _ritual = null;
        }
    }
}
