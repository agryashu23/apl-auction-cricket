using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The bowler's follow-through and walk back to his mark go round the non-striker, not through
    /// him (v23 shot 67): his turn-and-walk clip drifts across the crease, so while
    /// he's near the bowler's end and nobody's running, any step that would put him
    /// inside the non-striker's space is turned into a sidestep round him.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>Hip to hip on the ground, the closest the two may come (m).</summary>
        private const float PersonalSpace = 0.85f;

        private void LateUpdate()
        {
            if (_bowler == null || _other == null || _runnerBusy) return;
            if (_replays != null && _replays.Running) return;
            if (!_bowler.gameObject.activeInHierarchy || !_other.gameObject.activeInHierarchy) return;
            // On his follow-through and walk back (r8 shot 200: the follow-through ran into him too) —
            // not the run-up, a Mankad, or the bowler fielding off his own bowling.
            var anim = _bowler.GetComponent<Animator>();
            if (anim == null) return;
            var followThrough = _released >= 0 && Time.time - _released < 3f && _chaser != Refs.Bowler;
            if (!followThrough && !WalkingBack(anim)) return;
            var bowler = _bowler.transform;
            // Only at the bowler's end (not a bowler fielding off his own bowling down the pitch).
            if (bowler.position.z < GroundSpace.HalfPitch - 4f) return;
            var a = Hips(anim).position;
            var b = Hips(_other.GetComponent<Animator>()).position;
            var gap = new Vector3(a.x - b.x, 0, a.z - b.z);
            var d = gap.magnitude;
            // Deep area 1 (reviewer RC6: the whole overlap, up to 0.85 m, was pushed in ONE frame — both planted feet
            // jumped): the sidestep starts earlier (within PersonalSpace + 0.6 m) and goes at most 1.2 m/s (3 m/s if he's
            // really inside the space), so it reads as a step aside that FootPin carries, not a shove.
            if (SlideFix.Round1)
            {
                // Iteration 7 (reviewer A5: 200 pops up to 23 cm from the push): walking back on his feet, his walk steers round
                // the non-striker (BowlerRig.AvoidOnWalkBack) — no push at all; on the follow-through (the clip carries him) the
                // push is capped at 2 cm a frame.
                if (_bowler.WalkingBackOnFeet)
                {
                    if (d < 3f) _bowler.AvoidOnWalkBack(new Vector3(b.x, 0f, b.z));
                    return;
                }
                const float early = 0.6f;
                if (d >= PersonalSpace + early) return;
                var awayR = d > 0.05f ? gap / d : new Vector3(-Mathf.Sign(_nonStrikerX), 0, 0);
                if (Mathf.Abs(awayR.x) < 0.5f) awayR = new Vector3(-Mathf.Sign(_nonStrikerX), 0, awayR.z * 0.5f).normalized;
                var most = Mathf.Min((d < PersonalSpace * 0.7f ? 3f : 1.2f) * Time.deltaTime, 0.02f);
                var want = PersonalSpace + early - d;
                _bowler.transform.position += awayR * Mathf.Min(want, most);
                return;
            }
            if (d >= PersonalSpace) return;
            // Out sideways (away from the non-striker's side of the stumps), so he walks round him.
            var away = d > 0.05f ? gap / d : new Vector3(-Mathf.Sign(_nonStrikerX), 0, 0);
            if (Mathf.Abs(away.x) < 0.5f) away = new Vector3(-Mathf.Sign(_nonStrikerX), 0, away.z * 0.5f).normalized;
            bowler.position += away * (PersonalSpace - d);
        }

        private static readonly int WalkBackRa = Animator.StringToHash("ra_turn_and_walk_back_to_mark");
        private static readonly int WalkBackLa = Animator.StringToHash("la_turn_and_walk_back_to_mark");

        // (Deep area 1, iteration 5: the owner's turn into the walk back and the walk-back loops the bowler walks on now.)
        private static readonly int WalkBackTurn = Animator.StringToHash("bowler_walkback_turn_180");
        private static readonly int WalkBackLoopRa = Animator.StringToHash("ra_walk_back_loop");
        private static readonly int WalkBackLoopLa = Animator.StringToHash("la_walk_back_loop");

        private static bool WalkingBack(Animator a)
        {
            bool Is(AnimatorStateInfo i) => i.shortNameHash == WalkBackRa || i.shortNameHash == WalkBackLa
                                             || i.shortNameHash == WalkBackTurn || i.shortNameHash == WalkBackLoopRa || i.shortNameHash == WalkBackLoopLa;
            return Is(a.GetCurrentAnimatorStateInfo(0)) || a.IsInTransition(0) && Is(a.GetNextAnimatorStateInfo(0));
        }
    }
}
