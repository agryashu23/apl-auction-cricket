using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The square-leg umpire (owner's list, item 31): the second umpire, the same
    /// body and hat as the bowler's-end one, standing level with the batter's
    /// popping crease on the striker's leg side, idling; he gives the run-outs at
    /// the keeper's end and the stumpings (the batter's end is his).
    /// </summary>
    public sealed partial class MatchDirector
    {
        private Animator? _squareLeg;
        private bool? _squareLegLeft;
        private const string SquareLegIdle = "umpire_idle_square_leg_loop";

        /// <summary>Square of the wicket: metres out to the leg side, on the popping crease.</summary>
        private const float SquareLegOut = 19f;

        private void MakeSquareLeg()
        {
            if (!_cast.Ready) return;
            _squareLeg = _cast.Umpire();
            _squareLeg.name = "SquareLegUmpire";
            PlaceSquareLeg(_match.OnStrike.Player.LeftHanded);
        }

        /// <summary>To the striker's leg side (it changes with a left-hander), facing the pitch, idling.</summary>
        private void PlaceSquareLeg(bool leftHanded)
        {
            var u = _squareLeg;
            if (u == null) return;
            // A right-hander's off side is world +x: his leg side is −x.
            var leg = leftHanded ? 1f : -1f;
            var at = new Vector3(leg * SquareLegOut, 0, -GroundSpace.HalfPitch + 1.22f);
            u.transform.SetPositionAndRotation(at, Quaternion.LookRotation(new Vector3(-leg, 0, 0)));
            var state = Body.Has(u, SquareLegIdle) ? SquareLegIdle : "umpire_idle_bowlers_end_loop";
            if (Body.Has(u, state)) u.Play(state, 0, Random.value);
        }

        /// <summary>A signal from the square-leg umpire, then back to his idle.</summary>
        private void SquareLegSignal(string clip)
        {
            var u = _squareLeg;
            if (u == null || !CricketClips.HasUmpire(clip) || !Body.Has(u, clip)) return;
            u.CrossFadeInFixedTime(clip, 0.2f);
            StartCoroutine(After(3.2f, () =>
            {
                if (u != null && Body.Has(u, SquareLegIdle)) u.CrossFadeInFixedTime(SquareLegIdle, 0.3f);
            }));
        }

        private const string SquareLegWatch = "umpire_square_leg_watch_loop";

        /// <summary>Crouched a little and watching the ball once it's bowled; back to his idle after it.</summary>
        private void SquareLegWatching(bool on)
        {
            var u = _squareLeg;
            if (u == null) return;
            var clip = on && Body.Has(u, SquareLegWatch) ? SquareLegWatch : SquareLegIdle;
            if (Body.Has(u, clip) && u.GetCurrentAnimatorStateInfo(0).shortNameHash != Animator.StringToHash(clip)) u.CrossFadeInFixedTime(clip, 0.3f);
        }

        /// <summary>A dismissal at the batter's end that's the square-leg umpire's to give: stumped, run out there.</summary>
        private static bool SquareLegsCall(BallResult r) => r.HowOut == "stumped" || (r.RunOut && r.RunOutAtKeeperEnd);

        /// <summary>The umpire who gives this one.</summary>
        private Animator? UmpireOf(BallResult r) => SquareLegsCall(r) && _squareLeg != null ? _squareLeg : Refs.Umpire;

        /// <summary>The finger, from whichever umpire's decision it is.</summary>
        private void SignalOut(BallResult r)
        {
            if (SquareLegsCall(r) && _squareLeg != null) SquareLegSignal("umpire_out");
            else Umpire("umpire_out");
        }
    }
}
