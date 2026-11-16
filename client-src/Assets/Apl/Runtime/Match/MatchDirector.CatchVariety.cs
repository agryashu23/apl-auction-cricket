using System.Collections;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// More kinds of catch: the boundary catch completed by a team-mate (taken at
    /// the rope, lobbed in before he crosses, caught inside), the dropped catch that
    /// pops out of the hands and falls, and the edge that flies to slip instead of the
    /// keeper. (Running, diving and skied catches are chosen in FieldPlay.PlanCatch.)
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>When the catch went down through his hands (the ball pops out from there), else ≤ 0.</summary>
        private float _dropAt = -1f;

        /// <summary>Who completed a boundary catch passed in to him (the celebration's catcher), if anyone.</summary>
        private Animator? _relayCatcher;

        /// <summary>The ball popping up out of the hands it went through, then falling (added to its height).</summary>
        private float DropPop(float t)
        {
            if (_dropAt <= 0 || t <= _dropAt) return 0f;
            var tau = t - _dropAt;
            return Mathf.Max(0f, 2.4f * tau - 4.9f * tau * tau);
        }

        /// <summary>
        /// A boundary catch (the leap, the toss before he crosses): now and then a team-mate
        /// close enough comes round inside the rope and the toss goes to him instead.
        /// </summary>
        private void RelayCatch(FieldPlay play)
        {
            _relayCatcher = null;
            if (float.IsNaN(play.Clip.TossAt) || _visRng.NextDouble() < 0.4) return;
            var tossAt = play.HitTime(play.Clip.TossAt);
            var inside = Vector3.MoveTowards(Flat(play.BallAt), Vector3.zero, 5f);
            Animator? mate = null;
            var best = float.MaxValue;
            foreach (var (a, _) in _fielders)
            {
                if (a == play.Fielder || !a.gameObject.activeSelf) continue;
                var d = Vector3.Distance(Flat(a.transform.position), inside);
                if (d < best && SprintTime(d) < tossAt - 0.3f)
                {
                    best = d;
                    mate = a;
                }
            }
            if (mate == null) return;
            var spot = inside;
            var face = Flat(play.BallAt - spot);
            var caughtAt = tossAt + 0.9f;
            play.TossCaughtAt = caughtAt;
            play.TossHand = mate.GetBoneTransform(HumanBodyBones.RightHand);
            // The reverse-cup catch above his head, facing the man passing it in.
            play.TossTo = () => spot + face.normalized * 0.4f + Vector3.up * 1.75f;
            _relayCatcher = mate;
            var receiver = mate;
            FieldRoutine(RelayMate(receiver, spot, face, tossAt - 0.25f - (Time.time - _hitAt), caughtAt));
        }

        private IEnumerator RelayMate(Animator mate, Vector3 spot, Vector3 face, float by, float caughtAt)
        {
            Body.Rebase(mate);
            yield return RunTo(mate, spot, Mathf.Max(0.5f, by));
            yield return Face(mate, spot + face, 0.25f);
            var clip = CricketClips.FieldClipNamed("catch_02_reverse_cup_catch");
            // Event() lands the catch frame 0.12 s after it's called.
            while (Time.time - _hitAt < caughtAt - 0.12f) yield return null;
            Event(mate, clip);
        }

        /// <summary>
        /// An edge to slip (the engine's catch behind by the slip): off the bat's edge
        /// at the slip's hands, his catch clip timed so the hands meet it — a dive if it's
        /// wide of him. False (the keeper takes it) if there's no slip in the field.
        /// </summary>
        private bool SlipCatch(BallResult r, out IEnumerator? routine)
        {
            routine = null;
            if (!(r.Wicket && r.HowOut == "caught behind" && r.Fielder == "slip")) return false;
            var slip = NamedFielder("slip");
            if (slip == null) return false;
            routine = SlipTakes(r, slip);
            return true;
        }

        private IEnumerator SlipTakes(BallResult r, Animator slip)
        {
            // Wide of him or straight at him; low, at the chest, or diving.
            var side = _visRng.NextDouble();
            var name = side < 0.3 ? "catch_01_cup_catch" : side < 0.55 ? "catch_02_reverse_cup_catch" : side < 0.78 ? "catch_05_diving_left" : "catch_06_diving_right";
            var clip = SeqClip.Named(name);
            if (clip == null || !FieldPlay.Has(slip, clip)) clip = SeqClip.Named("catch_01_cup_catch");
            var from = _ball.position;
            var (l, rt) = FieldPlay.HandsAt(slip, clip!.Name, clip.Length, clip.EventTime);
            var local = clip.LeftHand ? l : clip.RightHand ? rt : (l + rt) / 2;
            var to = slip.transform.position + slip.transform.rotation * local;
            var flight = Mathf.Clamp(Vector3.Distance(from, to) / 26f, 0.25f, 0.8f);
            slip.speed = 1f;
            slip.CrossFadeInFixedTime(clip.Name, 0.06f, 0, Mathf.Max(0, clip.EventTime - flight));
            _trail.Clear();
            _trail.emitting = true;
            var hand = slip.GetBoneTransform(clip.LeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            for (var t = 0f; t < flight; t += Time.deltaTime)
            {
                var p = Vector3.Lerp(from, to, t / flight);
                p.y += 0.6f * 4 * (t / flight) * (1 - t / flight);
                // Onto his hands as it arrives (MatchDirector.CatchCheck).
                _ball.position = OntoHand(p, hand, flight - t);
                yield return null;
            }
            _trail.emitting = false;
            if (Autoplay && hand != null) Debug.Log($"[APL match] slip catch {clip.Name}: ball-to-hand at the catch frame {Vector3.Distance(to, hand.position):0.00} m");
            // In his hands until the celebration flings it up (BallInTheAir, 0.6 s into the wicket).
            if (hand != null) Extra(Hold(hand, 0.6f));
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>
        /// The keeper's take past the bat: a stumping (out, or one the TV umpire will look at)
        /// is the stumping clip, the bails whipped off a beat after the take; anything else
        /// the plain take. Returns the clip played, or null.
        /// </summary>
        private string? KeeperTakes(BallResult r, Vector3 ball, float inSeconds, bool appeal)
        {
            if (_keeper == null) return null;
            // The gloves reach for it and hold it in the cup; test runs log the take (deep area 3: MatchDirector.CatchHands).
            WireKeeperTakes();
            _keeperDelivery = true;
            // Caught behind: the keeper's take is a CATCH (logged and judged as one — review r1 F6).
            _keeperCatch = r.Wicket && (r.HowOut == "caught behind" || r.Caught && r.Fielder == "keeper");
            try
            {
                if ((r.Wicket && r.HowOut == "stumped") || r.Referral == TvCheck.Stumping)
                {
                    var clip = _keeper.Stumping(ball, inSeconds);
                    if (clip != null)
                    {
                        Extra(After(inSeconds + 0.25f, () => BreakWicket(true, Vector3.back)));
                        return clip;
                    }
                }
                return _keeper.TakeBall(ball, inSeconds, appeal);
            }
            finally
            {
                _keeperDelivery = false;
            }
        }

        /// <summary>
        /// The keeper stands up to the spinners and to the medium pacers (as a stumping
        /// threat, the engine's call: Bowling.PaceOf), back to the quicks.
        /// </summary>
        private static bool KeeperUp(Apl.Core.Player bowler) =>
            bowler.BowlType == Apl.Core.BowlType.Spin || Bowling.PaceOf(bowler) is PaceClass.Medium or PaceClass.SlowMedium;
    }
}
