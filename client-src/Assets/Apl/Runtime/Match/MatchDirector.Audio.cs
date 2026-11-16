using System.Collections;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The players' voices and the crowd, directed (owner's list, item 41): the
    /// batters call their runs as the running goes (read off <see cref="RunState"/>
    /// every frame — "yes!" / "push!" setting off, "two!" turning, "no!" / "back!"
    /// when it's called off, "wait!" while the user's deciding), the fielders'
    /// "howzat!", the keeper's chatter after a dot, the stumps and bails, and the
    /// crowd swelling on the big moments.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private MatchSfx _msfx = null!;

        private void AudioStart()
        {
            _msfx = MatchSfx.Create(Sfx);
            var ids = _data.Franchises.Select(f => f.Id).ToList();
            _msfx.Sides(ids.IndexOf(_match.Batting.TeamId), ids.IndexOf(_match.Bowling.TeamId));
            StartCoroutine(CallTheRuns());
            AudioFxStart();
        }

        /// <summary>A new innings: the sides' voices swap round with them.</summary>
        private void AudioSides()
        {
            var ids = _data.Franchises.Select(f => f.Id).ToList();
            _msfx.Sides(ids.IndexOf(_match.Batting.TeamId), ids.IndexOf(_match.Bowling.TeamId));
        }

        private Vector3 Mouth(Component? c) => c == null ? Vector3.zero : Hips(c).position + Vector3.up * 0.7f;

        /// <summary>The calling, by watching the race as it goes.</summary>
        private IEnumerator CallTheRuns()
        {
            var wasActive = false;
            var run = 0;
            var going = 0;
            var turned = false;
            var waited = 0f;
            while (true)
            {
                var st = RunState;
                if (st.Active && !wasActive)
                {
                    // Setting off: the striker calls it; a push for more than one is said so.
                    run = st.Run;
                    going = st.Going;
                    turned = false;
                    waited = 0f;
                    if (going > 0) CallBoth(going >= 2 ? "call_push" : "call_yes", "call_yes");
                }
                else if (st.Active)
                {
                    if (going == 0 && st.Going == 0 && st.Manual && (waited += Time.deltaTime) > 0.25f && waited < 1f)
                    {
                        // The user's deciding: one "wait!".
                        Call(0, "call_wait");
                        waited = 1f;
                    }
                    if (going == 0 && st.Going > 0) CallBoth(st.Going >= 2 ? "call_push" : "call_yes", "call_yes");
                    if (st.Run > run && st.Run >= 2) Call(st.Run % 2, st.Run == 2 ? "call_two" : "call_three");
                    if (st.Going < going && st.Going >= 0) Call(0, Random.value < 0.5f ? "call_no" : "call_no2");
                    if (st.TurnedBack && !turned)
                    {
                        turned = true;
                        Call(1, "call_back");
                    }
                    run = st.Run;
                    going = st.Going;
                }
                wasActive = st.Active;
                yield return null;
            }
        }

        private void Call(int which, string key)
        {
            var st = RunState;
            var who = which == 0 ? st.FromStrikerEnd ?? _striker : st.FromBowlerEnd ?? _other;
            _msfx.Say(true, which, key, Mouth(who));
        }

        /// <summary>One calls, the other answers a beat later.</summary>
        private void CallBoth(string call, string answer)
        {
            Call(0, call);
            StartCoroutine(After(0.35f, () => Call(1, answer)));
        }

        /// <summary>The fielding side goes up: the bowler and two or three more, staggered.</summary>
        private void AppealShouts()
        {
            _msfx.Say(false, 0, "appeal_" + Random.Range(1, 4), Mouth(_bowler));
            StartCoroutine(After(0.15f, () => _msfx.Say(false, 1, "appeal_" + Random.Range(1, 4), Mouth(Refs.Keeper), 0.85f)));
            StartCoroutine(After(0.3f, () => _msfx.Say(false, 0, "appeal_" + Random.Range(1, 4), new Vector3(2f, 1.5f, -12f), 0.6f)));
            _msfx.Swell(1.6f, 2f);
        }

        /// <summary>After a dot, now and then: the keeper (or a close fielder) gets behind his bowler.</summary>
        private void KeeperChatter()
        {
            if (Random.value > 0.35f || Refs.Keeper == null) return;
            var lines = new[] { "chat_bowling", "chat_well", "chat_areas", "chat_boys", "chat_tight", "chat_shot" };
            var keeper = Refs.Keeper;
            StartCoroutine(After(0.5f, () => _msfx.Say(false, 1, lines[Random.Range(0, lines.Length)], Mouth(keeper), 0.8f)));
        }
    }
}
