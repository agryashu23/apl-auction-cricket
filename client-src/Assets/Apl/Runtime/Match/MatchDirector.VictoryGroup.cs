using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Owner v26 report (screenshot 248, the trophy lift at Seaface): two winners stood INSIDE each other — the player
    /// of the match was posed at the presentation spot (Present) for his card and never cleared, then the trophy
    /// lifter was made on the very same spot. Now the field is cleared before the lift, and up to four of the winning
    /// XI gather round the captain: each on his own slot of a loose semicircle behind him (1.8 m out, 50° apart:
    /// 1.5 m between neighbours), arriving on foot from 5 m further out along his own line (no two paths cross; the
    /// shared Strides walk, feet locked — no sliding), turning to the camera and applauding. A guard holds any
    /// arriver whose hips come within 0.6 m of another body's. "[APL victory] N players placed, min gap X m".
    /// </summary>
    public sealed partial class MatchDirector
    {
        private const float VictoryRadius = 1.8f, VictoryGapMin = 0.6f;
        private static readonly float[] VictoryAngles = { -75f, -25f, 25f, 75f };

        /// <summary>The bodies round the trophy now (the guard and the gap measure read it).</summary>
        private readonly List<Animator> _victory = new List<Animator>();

        /// <summary>The team-mates in, round <paramref name="holder"/> (standing at PresentAt facing +x).</summary>
        private IEnumerator VictoryGroup(Animator holder, IReadOnlyList<Player> xi, Player captain)
        {
            _victory.Clear();
            _victory.Add(holder);
            var potm = PlayerOfTheMatch()?.Player;
            // The player of the match first (if he's a winner), then the rest of the XI in batting order.
            var mates = xi.Where(p => p.Id != captain.Id).OrderByDescending(p => potm != null && p.Id == potm.Id).ToList();
            var bodies = new List<Animator>();
            foreach (var p in mates)
            {
                if (bodies.Count >= VictoryAngles.Length) break;
                var b = BodyOf(p);
                if (b == null || b == holder || bodies.Contains(b)) continue;
                bodies.Add(b);
            }
            var back = -Vector3.right; // behind him: he faces +x at PresentAt
            var walks = new List<Coroutine>();
            for (var i = 0; i < bodies.Count; i++)
            {
                var a = bodies[i];
                var dir = Quaternion.Euler(0, VictoryAngles[i], 0) * back;
                var slot = PresentAt + dir * VictoryRadius;
                var from = slot + dir * 5f;
                var rig = a.GetComponent<BatterRig>();
                if (rig != null) rig.enabled = false;
                a.gameObject.SetActive(true);
                PlainKit(a);
                a.transform.SetPositionAndRotation(from, Quaternion.LookRotation(-dir));
                _victory.Add(a);
                var (clip, natural, _) = Strides.PickGait(a, VictoryPace);
                var face = (Vector3.right + (PresentAt - slot).normalized * 0.4f).normalized;
                walks.Add(StartCoroutine(VictoryWalk(a, slot, clip, natural, face, 0.15f * i)));
            }
            Debug.Log($"[APL victory] {bodies.Count} team-mates walking in round {holder.name}");
            // Arrived (or held by the guard), then on film and in the log.
            for (var t = 0f; t < 4f && _victory.Skip(1).Any(Strides.Busy); t += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.4f);
            Apl.Client.Dev.AutoShot.Moment("victory_celebration");
            Debug.Log($"[APL victory] {_victory.Count} players placed, min gap {MinVictoryGap():0.00} m (slots {VictoryRadius:0.0} m out, guard {VictoryGapMin:0.0} m)");
        }

        /// <summary>The walk-in's pace (m/s). Deep area 1 (reviewer RC10): 2.4 put the 5 m walk-in on the JOG loop, eased to
        /// 0.36-0.42x playback by the end — a slow-motion jog; 1.8 is a brisk walk on the walk loop.</summary>
        private static float VictoryPace => SlideFix.Round1 ? 1.8f : 2.4f;

        private IEnumerator VictoryWalk(Animator a, Vector3 slot, string clip, float natural, Vector3 face, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            Body.Rebase(a, turn: false);
            SlideScene.Tag(a, "victory group walking in");
            yield return Strides.Walk(a, slot, VictoryPace, clip, natural, () => TooClose(a));
            if (a == null) yield break;
            if (SlideFix.Round1 && Body.Has(a, "applause_loop"))
            {
                // Deep area 1 (reviewer RC10: a hard Body.Play with a facing — the pose popped and the body swung round in one
                // frame): the walk fades into the applause where he stands, at the slow playback the walk ended on, then he
                // turns to the trophy stepping (TurnStep) while he claps.
                a.SetBool(Animator.StringToHash("Mirror"), false);
                a.speed = Mathf.Min(1f, a.speed);
                a.CrossFadeInFixedTime("applause_loop", 0.3f, 0, 0f);
                yield return new WaitForSeconds(0.3f);
                if (a == null) yield break;
                a.speed = 1f;
                yield return TurnStep.Turn(a, Quaternion.LookRotation(face), 0.45f, clips: false);
                yield break;
            }
            if (Body.Has(a, "applause_loop")) Body.Play(a, "applause_loop", face);
            else a.transform.rotation = Quaternion.LookRotation(face);
        }

        /// <summary>The guard: another body's hips within <see cref="VictoryGapMin"/> of his — he stops where he is.</summary>
        private bool TooClose(Animator a)
        {
            var me = Hips(a).position;
            foreach (var o in _victory)
            {
                if (o == null || o == a || !o.gameObject.activeInHierarchy) continue;
                var d = Hips(o).position - me;
                d.y = 0;
                if (d.magnitude < VictoryGapMin)
                {
                    Debug.LogWarning($"[APL victory] {a.name} held {d.magnitude:0.00} m from {o.name}");
                    return true;
                }
            }
            return false;
        }

        /// <summary>The closest any two of them stand (hips, on the ground).</summary>
        private float MinVictoryGap()
        {
            var min = float.MaxValue;
            for (var i = 0; i < _victory.Count; i++)
            for (var j = i + 1; j < _victory.Count; j++)
            {
                if (_victory[i] == null || _victory[j] == null) continue;
                var d = Hips(_victory[i]).position - Hips(_victory[j]).position;
                d.y = 0;
                min = Mathf.Min(min, d.magnitude);
            }
            return min == float.MaxValue ? 0f : min;
        }
    }
}
