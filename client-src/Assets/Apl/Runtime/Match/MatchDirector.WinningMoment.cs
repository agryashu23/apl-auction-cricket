using System.Collections;
using System.Linq;
using Apl.Client.Ui;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The end of the match (owner's list, item 22), after the winning moment's
    /// fireworks and confetti (<see cref="WinFx"/>, at the top of Finale): the
    /// winners' graphic on a slow aerial turn of the ground under the fireworks,
    /// the handshake line, the player of the match on his close-up with his card,
    /// and the winning captain lifting the trophy. Tap skips a part.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private IEnumerator WinningMoment()
        {
            if (_closeUp) yield break;
            var winner = _match.Winner;
            var side = winner != null ? Side(winner.TeamId, _bat) : null;
            var colour = side != null ? UiKit.ParseHex(side.PrimaryColor) : Color.white;
            // Owner v26 report (screenshot 248): no scoreboard strip or radar over the celebration (MatchHud.CleanFeed.cs).
            _hud.CleanFeed(true);
            _hud.Broadcasting(true);
            _hud.SetSkippable(true);
            _skip = false;

            // The winning moment: the ground from the air, turning, under WinFx's fireworks.
            Sfx.Cheer(1f);
            Sfx.Applause(1f);
            Sfx.SetCrowd(1.8f);
            StadiumBuilder.Flash(400);
            _hud.EventGraphic(side != null ? side.ShortName : "TIED", side != null ? "WIN THE MATCH" : "", side != null ? colour : UiKit.Gold, 4f, MatchHud.FinalePriority);
            var t0 = Time.time;
            Camera.Track(() =>
            {
                var a = (Time.time - t0) * 0.12f + 0.6f;
                return (new Vector3(Mathf.Sin(a) * 72f, 38f, Mathf.Cos(a) * 72f), new Vector3(0, 6f, 0));
            }, fov: 50f, smooth: 0.2f, cut: true, priority: MatchCamera.Moment);
            yield return Beat(t0, 4.5f);
            _skip = false;

            yield return HandshakeLine(9f);

            // Player of the match.
            // The one Player of the Match: this cut-scene and the result card both ask PlayerOfTheMatch() (round 11).
            var potm = PlayerOfTheMatch();
            if (potm != null && BodyOf(potm.Player) is Animator star)
            {
                // Him alone in the shot (r12 `P393`: two handshake-line players filled the foreground).
                ClearField();
                star.gameObject.SetActive(true);
                PlainKit(star);
                // On his own spot, clear of the handshake line and the stumps (v8 f322: a head filled the shot).
                Present(star);
                yield return CardOn(star, potm, 3.6f);
            }

            // The captain lifts the trophy — in the WINNERS' kit (r11 `492`-`495`: the body found for him was
            // painted in the losers' colours). Characters: a fresh body for him, made in his side's kit
            // with his face and cap; the match body he had stays hidden.
            // Owner v26 report (screenshot 248): the player of the match was left standing on this spot and the lifter made
            // inside him. Clear the field first; then his team-mates gather round him, each on his own spot (MatchDirector.VictoryGroup.cs).
            ClearField();
            if (winner != null && side != null && TrophyCaptain(Captain(winner.XI), side) is Animator cap)
            {
                // The same clear spot (v8 f324: the low shot had the stumps' blocky tops filling it).
                Present(cap);
                var group = StartCoroutine(VictoryGroup(cap, winner.XI, Captain(winner.XI)));
                yield return TrophyLift(cap, colour);
                StopCoroutine(group);
                Debug.Log($"[APL victory] end of the lift: {_victory.Count} players, min gap {MinVictoryGap():0.00} m");
            }
            _hud.SetSkippable(false);
            _hud.Broadcasting(false);
            _hud.CleanFeed(false);
            Camera.Release(MatchCamera.Moment);
            Sfx.SetCrowd(1.2f);
        }

        /// <summary>
        /// Where the player of the match and the trophy lift are shot: square of the pitch, 8.5 m off the
        /// handshake files (x ±0.5) and level with neither set of stumps (z ±10), facing away from the
        /// square so the camera in front of him has only stands behind it and nobody between.
        /// </summary>
        private static readonly Vector3 PresentAt = new Vector3(9f, 0, -2f);

        private static void Present(Animator a)
        {
            // His batting rig off (its stance and bat would take the pose back), then the plain stand.
            var rig = a.GetComponent<BatterRig>();
            if (rig != null) rig.enabled = false;
            Stand(a, PresentAt, Vector3.right, null);
        }

        /// <summary>The trophy held between both hands, upright, for <paramref name="seconds"/>.</summary>
        private static IEnumerator HoldBetween(Transform trophy, Transform right, Transform left, float seconds)
        {
            for (var t = 0f; t < seconds && trophy != null && right != null && left != null; t += Time.deltaTime)
            {
                // Hands on the handles (0.42 m up the trophy, 0.26 m apart — trophy_raise holds them 0.3 m apart),
                // not under its base: r11 `494` had it floating over his fists. Turned so the handles face his hands.
                var across = right.position - left.position;
                across.y = 0;
                var rot = across.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(Vector3.Cross(across.normalized, Vector3.up), Vector3.up) : Quaternion.identity;
                trophy.SetPositionAndRotation((right.position + left.position) / 2f - Vector3.up * 0.42f, rot);
                yield return new WaitForEndOfFrame();
            }
        }

        /// <summary>The trophy in his right hand, raised overhead; the camera low in front of him.</summary>
        private IEnumerator TrophyLift(Animator cap, Color colour)
        {
            var hand = cap.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand == null) yield break;
            var trophy = Trophy.Make();
            var left = cap.GetBoneTransform(HumanBodyBones.LeftHand);
            if (Body.Has(cap, "trophy_raise") && left != null)
            {
                // Two hands round it (trophy_raise: hands 0.3 m apart, palms in): it sits between them, upright.
                Body.Play(cap, "trophy_raise");
                StartCoroutine(HoldBetween(trophy.transform, hand, left, 5.5f));
            }
            else
            {
                // One hand: gripped round the stem (0.2 m up), not balanced on the fist.
                trophy.transform.SetParent(hand, false);
                trophy.transform.localPosition = Vector3.zero;
                trophy.transform.position = hand.position - Vector3.up * 0.2f;
                trophy.transform.rotation = Quaternion.identity;
                Body.Play(cap, Body.Has(cap, "rhb_bat_fifty_raise_bat") ? "rhb_bat_fifty_raise_bat" : "bowler_celebration_point_to_sky");
            }
            // The batter's bat out of his hands for this.
            var rig = cap.GetComponent<BatterRig>();
            if (rig != null) rig.Bat.gameObject.SetActive(false);
            Sfx.Cheer(1f);
            var hips = Hips(cap);
            var cup = trophy.transform;
            Camera.Track(() =>
            {
                var (f, right) = Facing(cap);
                var c = hips.position;
                // Low in front, a little to his right, looking up between his chest and the trophy: the trophy is in
                // the frame against the night sky and the floodlights, not his head over the big screen (r10 f408-411).
                var top = cup != null ? cup.position : c + Vector3.up * 1.6f;
                var look = Vector3.Lerp(c + Vector3.up * 0.4f, top, 0.55f);
                // Chest height and wider to his side (r12 P397: from 0.45 m the trophy sat over his face): the
                // trophy above the head and the face under it both read.
                return (new Vector3(c.x, 1.25f, c.z) + f * 4.8f + right * 2.2f, look);
            }, fov: 48f, smooth: 0.3f, cut: true, priority: MatchCamera.Moment);
            _skip = false;
            yield return Beat(Time.time, 5f);
            _skip = false;
        }
    }
}
