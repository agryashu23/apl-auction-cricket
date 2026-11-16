using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Audio;
using Apl.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Match commentary, in English (Resources/Data/commentary.json,
    /// clips in Voice/comm_en, tools/gen_commentary.py): a random take on
    /// each event, none heard again within <see cref="Fresh"/> balls (the oldest
    /// first if they're all recent). Lines with a name slot say the batter's or
    /// the bowler's name (its own clip) at the start or the end. Big moments cut
    /// in; small ones only speak when the box is quiet, and not every time.
    /// </summary>
    public sealed class Commentary
    {
        /// <summary>A line isn't used again until this many balls have gone.</summary>
        private const int Fresh = 10;

        /// <summary>The commentary's clips and lines: English, the only language (owner, focus batch 1).</summary>
        public const string Language = "en";

        /// <summary>Who's batting and bowling this ball (for the name slots); set by the director.</summary>
        public Player? Batter, Bowler;

        private readonly VoiceQueue _voice;
        private readonly Dictionary<string, string[]> _events = new Dictionary<string, string[]>();
        private readonly Dictionary<string, (string slot, string at)> _named = new Dictionary<string, (string, string)>();
        private readonly Dictionary<string, int> _usedAt = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _uses = new Dictionary<string, int>();
        private readonly HashSet<string> _colour = new HashSet<string>();
        private readonly Dictionary<string, string> _lastOf = new Dictionary<string, string>();

        /// <summary>
        /// The second commentator's own channel (round 10): his lines queue here, start only
        /// when the lead has finished, and the lead waits for him in turn (VoiceQueue.Partner),
        /// so the two trade lines and neither is ever cut off.
        /// </summary>
        private readonly VoiceQueue _co;
        private readonly Dictionary<string, bool> _have = new Dictionary<string, bool>();
        private readonly string _folder;
        private readonly System.Random _rng = new System.Random();
        private int _ball;

        public Commentary(VoiceQueue voice)
        {
            _voice = voice;
            _co = new GameObject("CoCommentator").AddComponent<VoiceQueue>();
            _co.Bank = voice.Bank;
            _co.Channel = "co";
            _co.Partner = voice;
            voice.Channel = "comm";
            voice.Speaker = "Arjun";
            _co.Speaker = "Kiara";
            voice.Partner = _co;
            var lang = Language;
            _folder = "comm_" + lang;
            var text = Resources.Load<TextAsset>("Data/commentary");
            if (text == null) return;
            var root = JObject.Parse(text.text);
            if (root["events"]?[lang] is JObject events)
            {
                foreach (var kv in events) _events[kv.Key] = ((JArray)kv.Value!).Select(k => (string)k!).ToArray();
            }
            // Who says each line (tools/tag_roles_r11.py): "colour" lines are the second commentator's.
            if (root["roles"]?[lang] is JObject roles)
                foreach (var kv in roles) if ((string?)kv.Value == "colour") _colour.Add(kv.Key);
            if (root["named"]?[lang] is JObject named)
            {
                foreach (var kv in named) _named[kv.Key] = ((string?)kv.Value?["slot"] ?? "bat", (string?)kv.Value?["at"] ?? "start");
            }
        }

        /// <summary>A ball's been bowled (the freshness clock).</summary>
        public void NextBall() => _ball++;

        /// <summary>The clip is there (names and numbers are looked up before they're promised).</summary>
        private bool Has(string key)
        {
            if (!_have.TryGetValue(key, out var ok)) _have[key] = ok = Resources.Load<AudioClip>($"Voice/{_folder}/{key}") != null;
            return ok;
        }

        private string? NameKey(string slot)
        {
            var p = slot == "bowl" ? Bowler : Batter;
            return p == null ? null : "name_" + p.Id;
        }

        /// <summary>
        /// Say something about <paramref name="ev"/>: always if
        /// <paramref name="important"/> (cutting off whatever's being said),
        /// otherwise with <paramref name="chance"/> and only into silence.
        /// </summary>
        public void Say(string ev, float chance = 1f, bool important = false) => TrySay(ev, chance, important);

        /// <summary>The box has lines for <paramref name="ev"/> (a newer event may not be generated yet).</summary>
        public bool HasEvent(string ev) => _events.TryGetValue(ev, out var keys) && keys.Length > 0;

        /// <summary>The first of <paramref name="events"/> the box has lines for, else the last one named.</summary>
        public string First(params string[] events) => events.FirstOrDefault(HasEvent) ?? events[events.Length - 1];

        /// <summary><see cref="Say"/>, saying whether a line went out.</summary>
        public bool TrySay(string ev, float chance = 1f, bool important = false) => Speak(ev, chance, important, false);

        /// <summary>
        /// The second voice coming in after the line being said (round 9: banter and stats):
        /// queued behind it rather than waiting for silence; with <paramref name="chance"/>,
        /// and not if the box already has two lines waiting.
        /// </summary>
        public bool Follow(string ev, float chance = 1f) => Speak(ev, chance, false, true);

        /// <summary>The box is off (the nets): nothing is said.</summary>
        public bool Muted;

        /// <summary>Both voices stop and nothing queued is said (the nets).</summary>
        public void Silence()
        {
            _voice.Interrupt();
            _co.Interrupt();
        }

        /// <summary>
        /// A two-voice exchange (round 12): the lead asks (xq_&lt;topic&gt;_N) and the second voice
        /// answers (xa_&lt;topic&gt;_N, the same N) — the answer queued on his channel, so it comes
        /// the moment the question ends. With <paramref name="chance"/>, into quiet only.
        /// </summary>
        public bool Exchange(string topic, float chance)
        {
            if (Muted || _voice.Busy || _co.Busy || _rng.NextDouble() > chance) return false;
            if (!_events.TryGetValue("xq_" + topic, out var qs) || !_events.TryGetValue("xa_" + topic, out var answers)) return false;
            var n = Math.Min(qs.Length, answers.Length);
            if (n == 0) return false;
            // The pair said least, never the last one again.
            _lastOf.TryGetValue("x_" + topic, out var last);
            var order = Enumerable.Range(0, n).Where(i => qs[i] != last).OrderBy(i => _uses.TryGetValue(qs[i], out var u) ? u : 0).ThenBy(_ => _rng.Next()).ToList();
            if (order.Count == 0) order.Add(0);
            var k = order[0];
            _uses[qs[k]] = (_uses.TryGetValue(qs[k], out var used) ? used : 0) + 1;
            _lastOf["x_" + topic] = qs[k];
            SoundLog.Said("comm/exchange_" + topic, qs[k] + "+" + answers[k]);
            _voice.Say(_folder, qs[k]);
            _co.Say(_folder, answers[k]);
            return true;
        }

        private bool Speak(string ev, float chance, bool important, bool follow)
        {
            if (Muted) return false;
            if (!_events.TryGetValue(ev, out var keys) || keys.Length == 0) return false;
            if (follow && (_co.Queued > 1 || _rng.NextDouble() > chance)) return false;
            if (!important && !follow && (_voice.Busy || _co.Busy || _rng.NextDouble() > chance)) return false;
            // Lines that can be said now (a name slot needs that name's clip).
            var sayable = keys.Where(k => !_named.TryGetValue(k, out var n) || (NameKey(n.slot) is string nk && Has(nk))).ToList();
            if (sayable.Count == 0) return false;
            // A shuffle bag per event over the match (round 10): the least-said lines first, never the
            // event's last line twice running, so all 15 FOUR lines go round before any comes back.
            var least = sayable.Min(k => _uses.TryGetValue(k, out var n) ? n : 0);
            _lastOf.TryGetValue(ev, out var last);
            var bag = sayable.Where(k => (_uses.TryGetValue(k, out var n) ? n : 0) == least && k != last).ToList();
            if (bag.Count == 0) bag = sayable.Where(k => k != last).ToList();
            if (bag.Count == 0) bag = sayable;
            var pick = bag[_rng.Next(bag.Count)];
            _uses[pick] = (_uses.TryGetValue(pick, out var used) ? used : 0) + 1;
            _lastOf[ev] = pick;
            _usedAt[pick] = _ball;
            // The lead's lines on his channel, the colour man's on his (round 11: two voices, taking turns).
            var colour = follow || _colour.Contains(pick);
            var voice = colour ? _co : _voice;
            SoundLog.Said("comm/" + ev, pick);
            // A big moment: the lead cuts in; the second voice finishes his line but drops any stale banter.
            if (important)
            {
                _voice.Interrupt();
                // The second voice finishes his line and keeps what's queued (round 12: queue, never drop),
                // unless he's the one with the big call.
                if (colour) _co.Interrupt();
            }
            if (_named.TryGetValue(pick, out var slot) && NameKey(slot.slot) is string name)
            {
                // Start: the name, then the line; end: the line, then the name; middle: the line's two halves round it.
                if (slot.at == "start") voice.Say(_folder, name, pick);
                else if (slot.at == "mid" && Has(pick + "_b")) voice.Say(_folder, pick, name, pick + "_b");
                else voice.Say(_folder, pick, name);
                return true;
            }
            voice.Say(_folder, pick);
            return true;
        }

        private int _needForm = -1;

        /// <summary>
        /// The chase in numbers, in four phrasings that take turns (round 10): "They need 12
        /// off 6 balls", "12 needed from 6 balls", "6 balls left, 12 to get", "12 runs
        /// required, 6 balls remaining". False if it can't be said.
        /// </summary>
        public bool SayNeed(int runs, int balls, bool important = false)
        {
            if (Muted || runs < 1 || balls < 1 || runs > 60 || balls > 60 || !Has("num_" + runs) || !Has("num_" + balls)) return false;
            if (!important && _voice.Busy) return false;
            var one = balls == 1;
            string R = "num_" + runs, B = "num_" + balls;
            var forms = new List<string[]>
            {
                new[] { "frag_need_1", R, "frag_off_1", B, one ? "frag_ball_1" : "frag_balls_1" },
                new[] { R, "frag_needed_from_1", B, one ? "frag_ball_1" : "frag_balls_1" },
                new[] { B, one ? "frag_ball_left_1" : "frag_balls_left_1", R, "frag_to_get_1" },
            };
            if (runs > 1) forms.Add(new[] { R, "frag_runs_required_1", B, one ? "frag_ball_remaining_1" : "frag_balls_remaining_1" });
            var sayable = forms.Select((f, i) => (f, i)).Where(x => x.f.All(Has)).ToList();
            if (sayable.Count == 0) return false;
            // The next phrasing after the last one used.
            var pick = sayable.FirstOrDefault(x => x.i > _needForm);
            if (pick.f == null) pick = sayable[0];
            _needForm = pick.i;
            SoundLog.Said("comm/need", string.Join("+", pick.f));
            if (important) _voice.Interrupt();
            _voice.Say(_folder, pick.f);
            return true;
        }    }
}
