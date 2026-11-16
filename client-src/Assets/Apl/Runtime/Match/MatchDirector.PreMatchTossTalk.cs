using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The toss winner's moment: the camera turns to him, the presenter says who
    /// won, the winner chooses (you with BAT / BOWL; the AI by the pitch and its
    /// side), steps forward to the presenter and says it into the mic — the
    /// voices are Polly lines (tools/gen_toss_voice.py) in the commentary
    /// language, in Voice/comm_en as toss_*.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private IEnumerator Winner(Animator? cap, Animator? loser, Animator? presenter, Franchise team, bool user, bool canBat, Action<bool> decided)
        {
            // Just the two of them: the loser walks off out of shot (the cut hides it). Laid out as the
            // mic clips are made (ceremony.json captain_speak_into_mic): the presenter 1.3 m in front of
            // the winner and 0.3 m to his right, facing him; the clip leans him 0.45 m in to the mic.
            if (loser != null) loser.gameObject.SetActive(false);
            var at = cap != null ? cap.transform.position : TossAt;
            var host = at + TossFwd * 1.3f + TossRight * 0.3f;
            var speak = cap != null && Body.Has(cap, "captain_speak_into_mic") ? "captain_speak_into_mic" : null;
            // He listens to the presenter (MatchDirector.PreMatchIdles.cs) until he answers at the mic.
            Idle(cap, at, TossFwd, CaptainListen, fallback: speak ?? "Stand");
            if (presenter != null) Stand(presenter, host, -TossFwd, ClipFor(presenter, "Stand", new[] { "presenter_talk_loop" }));
            var pair = (at + TossFwd * 0.45f + host) / 2f;
            Camera.Track(() => (pair + TossRight * 3.4f + Vector3.up * 1.65f, pair + Vector3.up * 1.4f), fov: 36f, smooth: 0.3f, cut: true);
            yield return WaitVoice(Say(TossKey($"toss_won_{team.Id}", 1)), Secs(2.6f));
            Sfx.Applause(0.35f);

            // His call: the presenter (done asking) listens for it (MatchDirector.PreMatchIdles.cs).
            if (presenter != null) StartCoroutine(BlendLater(presenter, "presenter_talk_loop", PresenterListen, user ? 1.6f : 0.2f));
            bool bat;
            if (user)
            {
                _pre.Banner("YOU WON THE TOSS", "", team);
                Say(TossKey("toss_ask", 2));
                var suggest = ChoosesToBat(SheetFor(_bat.Id).XI, SheetFor(_bowl.Id).XI);
                var pick = suggest && canBat;
                _pre.ShowSkip(false);
                yield return _pre.BatOrBowl(Secs(8f), suggest, canBat, b => pick = b);
                _pre.ShowSkip(true);
                bat = pick;
            }
            else
            {
                _pre.Banner($"{team.ShortName} WON THE TOSS", "", team);
                yield return Hold(Secs(0.8f));
                // Until batting first is playable they bat, and you chase.
                bat = !canBat || ChoosesToBat(SheetFor(_bowl.Id).XI, SheetFor(_bat.Id).XI);
            }
            decided(bat);

            // The mic held out between them; he leans in and says it (no handshake: hands that don't meet read wrong).
            if (presenter != null && Body.Has(presenter, "presenter_hold_mic_out_loop")) Blend(presenter, "presenter_hold_mic_out_loop", 0.3f);
            else if (presenter != null) Stand(presenter, host, -TossFwd, ClipFor(presenter, "Stand", new[] { "presenter_talk_loop" }));
            // The mic's head toward his mouth (the clips put the presenter's hand where he leans to).
            _micTarget = cap != null ? cap.GetBoneTransform(HumanBodyBones.Head) : null;
            var clip = speak ?? (bat ? "captain_toss_win_choose_bat" : "captain_toss_win_choose_bowl");
            if (cap != null)
            {
                // From his listening loop straight into the mic clip (both made facing the presenter's way).
                if (clip == speak && Body.Has(cap, CaptainListen)) Blend(cap, clip, 0.25f);
                else
                {
                    Stand(cap, at, TossFwd, clip);
                    Restart(cap, clip);
                }
                // The winner speaks to the presenter; the presenter listens, nodding along.
                PreMatchLook.Set(cap, PreMatchLook.Eyes(presenter));
                PreMatchLook.Set(presenter, PreMatchLook.Eyes(cap), nod: true);
            }
            _pre.Banner(user ? "YOU WON THE TOSS" : $"{team.ShortName} WON THE TOSS", $"and chose to <color=#FFFFFF>{(bat ? "BAT" : "BOWL")}</color> first", team);
            // He speaks from the clip's first talking frame (34 of 191).
            yield return Hold(speak != null ? 34f / 30f : 0.3f);
            yield return WaitVoice(Say(TossKey(bat ? "toss_bat" : "toss_bowl", 3)), Secs(4.5f));
            yield return Hold(Secs(1f));
            _micTarget = null;
        }

        // ------------------------------------------------------------- voices

        /// <summary>One of <paramref name="variants"/> takes of a toss line ("toss_bat" → "toss_bat_2"); a full key as is.</summary>
        private static string TossKey(string key, int variants) => $"{key}_{UnityEngine.Random.Range(1, variants + 1)}";

        /// <summary>Say a toss line in the commentary language (cutting off the commentary); false if it isn't there.</summary>
        private bool Say(string key)
        {
            const string folder = "comm_" + Commentary.Language;
            if (Voice == null || Voice.Bank.Get(folder, key) == null) return false;
            Voice.Interrupt();
            Voice.Say(folder, key);
            return true;
        }

        /// <summary>Until the line's been said (at least a moment, at most <paramref name="max"/> s), or SKIP.</summary>
        private IEnumerator WaitVoice(bool speaking, float max)
        {
            var until = Time.time + max;
            var least = Time.time + (speaking ? 0.4f : max * 0.6f);
            while (Time.time < until && !_pre.Skipped && (Time.time < least || (speaking && Voice.Busy))) yield return null;
        }

        /// <summary>
        /// The toss winner's call (the AI's, and the user's suggestion): a short game
        /// favours chasing; a flat deck says bat, a turner with good spinners says bat
        /// (it'll grip later), a green one says bowl; a side stronger with the bat than
        /// the ball leans to batting.
        /// </summary>
        private bool ChoosesToBat(IReadOnlyList<Player> own, IReadOnlyList<Player> other)
        {
            var spin = own.Where(p => p.IsBowlingOption && p.BowlType == BowlType.Spin).Select(p => p.Bowling).DefaultIfEmpty(0).Max();
            var s = -0.3;
            s += _match.Pitch switch
            {
                Pitch.Flat => 0.45,
                Pitch.SpinFriendly => spin >= 70 ? 0.8 : 0.2,
                Pitch.SeamFriendly => -0.6,
                _ => 0,
            };
            s += (Lineups.BattingStrength(own) - Lineups.BowlingStrength(own)) / 20.0;
            s += (Lineups.BowlingStrength(other) - Lineups.BattingStrength(other)) / 40.0;
            return s > 0;
        }
    }
}
