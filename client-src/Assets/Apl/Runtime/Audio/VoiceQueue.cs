using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Audio
{
    /// <summary>
    /// Speaks queued lines back to back on one AudioSource per voice, so the
    /// auctioneer and assistant can overlap but never talk over themselves.
    /// A new bid interrupts a stale amount call.
    /// </summary>
    public sealed class VoiceQueue : MonoBehaviour
    {
        private readonly Queue<VoiceLine> _queue = new Queue<VoiceLine>();
        private AudioSource _source = null!;
        private float _nextAt;
        private float _level = 1f;

        public VoiceBank Bank { get; set; } = new VoiceBank();

        /// <summary>True while a line is playing or waiting.</summary>
        public bool Busy => _source.isPlaying || _queue.Count > 0 || Time.time < _nextAt;

        /// <summary>Lines waiting behind the one playing.</summary>
        public int Queued => _queue.Count;

        /// <summary>A line is coming out of this queue right now.</summary>
        public bool Speaking => _source != null && _source.isPlaying;

        /// <summary>
        /// The other commentator (round 10): this queue never talks over him. The lead
        /// ("comm") holds its next line while the partner is mid-line; the partner ("co")
        /// waits until the lead is quiet with nothing queued — so they take turns.
        /// </summary>
        public VoiceQueue? Partner;

        /// <summary>The channel's name in the sound log ("comm", "co", "hall").</summary>
        public string Channel = "";

        /// <summary>Who this channel's voice is, for the sound log ("Arjun", "Kiara").</summary>
        public string Speaker = "";

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
        }

        public void Say(params VoiceLine[] lines)
        {
            foreach (var l in lines) _queue.Enqueue(l);
        }

        public void Say(string voice, params string[] keys)
        {
            foreach (var k in keys) _queue.Enqueue(new VoiceLine(voice, k));
        }

        private static bool IsCall(string key) =>
            key == "going_once" || key == "going_twice" || key == "sold" || key == "unsold";

        /// <summary>Wait <paramref name="seconds"/> before the next line (e.g. for the gavel).</summary>
        public void Hold(float seconds) => _nextAt = Mathf.Max(_nextAt, Time.time + seconds);

        /// <summary>Drop what's waiting, but let the line being said finish.</summary>
        public void DropQueued() => _queue.Clear();

        /// <summary>Drop what's queued and cut the current line.</summary>
        public void Interrupt()
        {
            _queue.Clear();
            _source.Stop();
            _nextAt = 0;
        }

        private void Update()
        {
            // VOICE off mutes but keeps the timing (the show waits on the lines).
            _source.mute = !AudioPrefs.VoiceOn;
            if (_source.isPlaying)
            {
                _source.volume = _level * AudioPrefs.Voice;
                return;
            }
            if (Time.time < _nextAt || _queue.Count == 0) return;
            if (Partner != null && (Channel == "co" ? Partner.Busy : Partner.Speaking)) return;
            var line = _queue.Dequeue();
            var clip = Bank.Get(line.Voice, line.Key);
            if (clip == null) return;
            _source.clip = clip;
            // The calls ring out over the room; ordinary talk sits a little lower.
            _level = IsCall(line.Key) ? 1f : 0.78f;
            _source.volume = _level * AudioPrefs.Voice;
            _source.Play();
            SoundLog.Played("voice/" + (Channel.Length > 0 ? Channel : line.Voice) + (Speaker.Length > 0 ? "/" + Speaker : ""), clip, _source.volume);
            _nextAt = Time.time + clip.length + line.GapAfter;
        }
    }
}
