using System.Collections;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The toss (f16–42), laid out as the ceremony clips are made (ceremony.json
    /// "toss_layout"): the two captains side by side facing the presenter, who
    /// faces them. The opponent's captain flips (captain_toss_flip), yours watches
    /// (captain_toss_watch) and you make the call; the coin goes up off his thumb
    /// and lands at their feet; then the winner's moment
    /// (MatchDirector.PreMatchTossTalk.cs). Props: MatchDirector.PreMatchTossProps.cs.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The flipper's spot (the opponent's captain), and the way both captains face (toward the presenter).</summary>
        private static readonly Vector3 TossAt = new Vector3(7f, 0, 0.5f);
        private static readonly Vector3 TossFwd = Vector3.left;

        /// <summary>The flipper's right: yours stands <see cref="CaptainGap"/> to his left.</summary>
        private static Vector3 TossRight => Vector3.Cross(Vector3.up, TossFwd);

        /// <summary>Between the captains (round 8: 1 m had them standing inside each other on the wide shot).</summary>
        private const float CaptainGap = 1.7f;

        private static Vector3 Mine => TossAt - TossRight * CaptainGap;
        private static Vector3 PresenterAt => TossAt - TossRight * (CaptainGap / 2f) + TossFwd * 1.6f;
        private static Vector3 TossMid => TossAt - TossRight * (CaptainGap / 2f) + TossFwd * 0.8f;

        /// <summary>
        /// The wide toss shot: from the front, 60° round toward the flipper's side and a little above,
        /// where the presenter and both captains stand clear of each other across the frame (worked
        /// out from their spots) and the coin's arc, up to its apex, stays in.
        /// </summary>
        private static (Vector3 pos, Vector3 look) TossWide()
        {
            var d = (TossFwd * 0.5f + TossRight * 0.866f).normalized;
            return (TossMid + d * 7.2f + Vector3.up * 2.8f, TossMid + Vector3.up * 1.25f);
        }

        private IEnumerator Toss(Player myCap, Animator? capA, Player oppCap, Animator? capB)
        {
            var presenter = Presenter(capA, capB);
            var mat = TossMat(TossMid + TossFwd * 0.1f);
            if (presenter != null) Mic(presenter);
            var canBat = HasHook("StartUserBattingFirst");
            _pre.ShowSkip(true);

            // Everyone in place, facing the right way: the captains at the presenter, he at them.
            // Waiting, relaxed (MatchDirector.PreMatchIdles.cs); into the flip clips just before the call.
            Idle(capA, Mine, TossFwd, CaptainRelaxed, fallback: "captain_toss_watch");
            Idle(capB, TossAt, TossFwd, CaptainRelaxed, fallback: "captain_toss_flip");
            if (presenter != null) Stand(presenter, PresenterAt, -TossFwd, ClipFor(presenter, "Stand", new[] { "presenter_talk_loop" }));
            _micTarget = presenter != null ? presenter.GetBoneTransform(HumanBodyBones.Head) : null;

            // Who's tossing: each captain on a close-up with his card (MatchDirector.PreMatchTossIntro.cs).
            yield return CaptainIntros(myCap, capA, oppCap, capB, presenter);
            // What they're tossing for: the pitch report (owner v26 #23, MatchDirector.PitchReport.cs).
            yield return PitchReport();
            // Heads up (round 7: they looked at their feet): the captains at the presenter, he at the two of them.
            PreMatchLook.Set(capA, PreMatchLook.Eyes(presenter));
            PreMatchLook.Set(capB, PreMatchLook.Eyes(presenter));
            var eyesA = PreMatchLook.Eyes(capA);
            var eyesB = PreMatchLook.Eyes(capB);
            PreMatchLook.Set(presenter, () => (eyesA() + eyesB()) / 2f);
            // They listen to his welcome, nodding along.
            Blend(capA, CaptainListen);
            Blend(capB, CaptainListen);

            // One wide shot from a little above, held through the welcome, the call and the flip:
            // the presenter, both captains, the mat and the coin's whole arc in frame.
            Camera.Track(TossWide, fov: 42f, smooth: 0.3f, cut: true);
            Say(TossKey("toss_welcome", 2));
            _pre.Caption("THE TOSS");
            yield return Hold(Secs(2.6f));
            _pre.Caption(null);

            // The coin on the flipper's thumb, your call: both captains settle into their flip clips' first frame.
            yield return IntoFlip(capA, capB);
            var coin = Coin();
            var thumb = capB != null ? capB.GetBoneTransform(HumanBodyBones.RightHand) : null;
            var holding = true;
            StartCoroutine(HoldCoin(coin, thumb, () => holding));
            bool? call = null;
            _pre.ShowSkip(false);
            yield return _pre.Call(Secs(10f), h => call = h);
            _pre.ShowSkip(true);
            // "Heads is the call." — over the flip.
            Say(call == true ? "toss_call_heads_1" : "toss_call_tails_1");

            // The flip (same wide shot): both clips run from frame 1, the coin leaves his hand at their release frame.
            var heads = Random.value < 0.5f;
            // Every eye on the coin while it's up.
            var coinAt = coin;
            foreach (var who in new[] { capA, capB, presenter }) PreMatchLook.Set(who, () => coinAt != null ? coinAt.position : TossMid);
            Restart(capB, "captain_toss_flip");
            Restart(capA, "captain_toss_watch");
            yield return new WaitForSeconds(CoinRelease);
            holding = false;
            yield return FlipCoin(coin, heads);
            // "It's heads!" as it lands, before who won.
            var landed = Say(heads ? "toss_landed_heads_1" : "toss_landed_tails_1");
            yield return _pre.Flash(heads ? "HEADS" : "TAILS", UiKit.Gold, 1.1f);
            yield return WaitVoice(landed, Secs(1.6f));
            var userWins = call == heads;
            TossWinnerId = (userWins ? _bat : _bowl).Id;
            // Back up to the presenter; he turns to the winner.
            PreMatchLook.Set(capA, PreMatchLook.Eyes(presenter));
            PreMatchLook.Set(capB, PreMatchLook.Eyes(presenter));
            PreMatchLook.Set(presenter, PreMatchLook.Eyes(userWins ? capA : capB));
            Blend(capA, CaptainListen);
            Blend(capB, CaptainListen);
            Destroy(coin.gameObject);

            // The winner's moment: the camera to him, the presenter's line, his choice at the mic.
            var batFirst = false;
            yield return Winner(userWins ? capA : capB, userWins ? capB : capA, presenter, userWins ? _bat : _bowl, userWins, canBat, b => batFirst = b);
            UserBatsFirst = userWins ? batFirst : !batFirst;
            // The captains shake hands, the banner still up.
            _pre.ResetSkip();
            yield return Shake(userWins ? capA : capB, userWins ? capB : capA, presenter);
            _pre.HideBanner();
            _pre.ShowSkip(false);
            _micTarget = null;
            PreMatchLook.Clear(capA);
            PreMatchLook.Clear(capB);
            if (presenter != null) Destroy(presenter.gameObject);
            Destroy(mat);
        }

        /// <summary>Wait, or until SKIP.</summary>
        private IEnumerator Hold(float seconds)
        {
            for (var until = Time.time + seconds; Time.time < until && !_pre.Skipped;) yield return null;
        }

        /// <summary>
        /// Stand <paramref name="a"/> at <paramref name="at"/> in <paramref name="clip"/>'s first frame (a one-off is
        /// held there, a loop plays), turned so his body — whatever way the clip was made — faces <paramref name="face"/>.
        /// </summary>
        private static void Stand(Animator? a, Vector3 at, Vector3 face, string? clip)
        {
            if (a == null) return;
            a.transform.position = at;
            a.transform.rotation = Quaternion.LookRotation(new Vector3(face.x, 0, face.z));
            a.SetBool(Animator.StringToHash("Mirror"), false);
            var name = clip != null && Body.Has(a, clip) ? clip : Body.Has(a, "Stand") ? "Stand" : null;
            if (name == null) return;
            a.speed = name.EndsWith("_loop") || name == "Stand" ? 1f : 0f;
            a.Play(name, 0, 0f);
            Square(a, face);
        }

        /// <summary>A held clip from its first frame, now at speed.</summary>
        private static void Restart(Animator? a, string clip)
        {
            if (a == null || !Body.Has(a, clip)) return;
            a.speed = 1f;
            a.Play(clip, 0, 0f);
        }

        /// <summary>
        /// Turn him so his body (hips and shoulders, as the pose has them) faces
        /// <paramref name="face"/>: the pose is evaluated now, so there's no visible snap.
        /// </summary>
        private static void Square(Animator a, Vector3 face)
        {
            face.y = 0;
            if (face.sqrMagnitude < 1e-4f) return;
            var speed = a.speed;
            a.speed = 1f;
            a.Update(0f);
            a.speed = speed;
            var (fwd, _) = Facing(a);
            a.transform.rotation = Quaternion.Euler(0, Vector3.SignedAngle(fwd, face, Vector3.up), 0) * a.transform.rotation;
        }
    }
}
