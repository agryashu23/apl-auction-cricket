using System.Collections;
using System.Collections.Generic;
using Apl.Client.Audio;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The match's own sounds (owner's list, item 41): the stumps hit and the bails
    /// clattering (made in code), the players' voices on the field — the batters'
    /// running calls, appeals, the keeper's chatter — from Resources/Sfx/calls
    /// (tools/gen_match_voices.py: Polly, six English voices, two per side so the
    /// batters differ), played where the speaker stands, and the crowd swelling
    /// for the big moments.
    /// </summary>
    public sealed class MatchSfx : MonoBehaviour
    {
        /// <summary>The voice pairs; each side gets one pair (never the other side's).</summary>
        private static readonly string[][] Pairs = { new[] { "arjun", "stephen" }, new[] { "brian", "matthew" }, new[] { "russell", "curtis" } };

        private readonly Dictionary<string, AudioClip?> _clips = new Dictionary<string, AudioClip?>();
        private readonly List<AudioSource> _sources = new List<AudioSource>();
        private AudioClip _stumps = null!, _bails = null!;
        private Sfx _sfx = null!;
        private Coroutine? _swell;
        private string[] _batting = Pairs[0], _fielding = Pairs[1];

        public static MatchSfx Create(Sfx sfx)
        {
            var go = new GameObject("MatchSfx");
            var m = go.AddComponent<MatchSfx>();
            m._sfx = sfx;
            m._stumps = StumpSounds.Stumps();
            m._bails = StumpSounds.Bails();
            sfx.UseStadiumBeds();
            return m;
        }

        /// <summary>Each side's pair of voices, by franchise (index in the dataset), the two never the same.</summary>
        public void Sides(int battingIndex, int fieldingIndex)
        {
            var a = Mathf.Abs(battingIndex) % Pairs.Length;
            var b = Mathf.Abs(fieldingIndex) % Pairs.Length;
            if (b == a) b = (a + 1) % Pairs.Length;
            _batting = Pairs[a];
            _fielding = Pairs[b];
        }

        /// <summary>A free source, placed at <paramref name="at"/>: mostly flat so the far end is still heard, a touch of place.</summary>
        private AudioSource Source(Vector3 at)
        {
            AudioSource? s = null;
            foreach (var x in _sources) if (!x.isPlaying) s = x;
            if (s == null)
            {
                var go = new GameObject("Voice");
                go.transform.SetParent(transform, false);
                s = go.AddComponent<AudioSource>();
                s.spatialBlend = 0.35f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.maxDistance = 140f;
                _sources.Add(s);
            }
            s.transform.position = at;
            // (A thud may have bent the pitch on this one; voices play true.)
            s.pitch = 1f;
            s.volume = AudioPrefs.Effects;
            return s;
        }

        private AudioClip? Clip(string voice, string key)
        {
            var path = $"Sfx/calls/{voice}_{key}";
            if (!_clips.TryGetValue(path, out var c)) _clips[path] = c = Resources.Load<AudioClip>(path);
            return c;
        }

        /// <summary>A line from a batter (<paramref name="which"/> 0 striker, 1 non-striker) or a fielder.</summary>
        public void Say(bool batting, int which, string key, Vector3 at, float volume = 1f)
        {
            var pair = batting ? _batting : _fielding;
            var clip = Clip(pair[Mathf.Abs(which) % 2], key);
            if (clip == null) return;
            Source(at).PlayOneShot(clip, volume);
            SoundLog.Played("call/" + key, clip, volume, at);
        }

        /// <summary>
        /// The stumps struck (bowled, run out, stumped), then the bails coming down:
        /// a recorded take (Sfx/match/stumps_*, bails_*) over the synthesised crack.
        /// </summary>
        public void StumpsHit(Vector3 at, float strength = 1f)
        {
            var take = Sfx.MatchClip("stumps");
            Source(at).PlayOneShot(_stumps, Mathf.Lerp(0.6f, 1f, strength) * (take != null ? 0.45f : 1f));
            if (take != null) Source(at).PlayOneShot(take, Mathf.Lerp(0.7f, 1f, strength));
            SoundLog.Played("field/stumps", take ?? _stumps, Mathf.Lerp(0.6f, 1f, strength), at);
            var bails = Sfx.MatchClip("bails");
            // The bails fly the instant the stumps go (round 10: 0.32 s apart read as two events).
            StartCoroutine(Later(0.06f, () =>
            {
                Source(at).PlayOneShot(bails ?? _bails, 0.8f);
                SoundLog.Played("field/bails", bails ?? _bails, 0.8f, at);
            }));
        }

        private float _stingFree;

        /// <summary>The DJ's sting at a boundary (a few seconds of the ground's music), on the MUSIC level; not twice inside 10 s.</summary>
        public void Sting(float volume = 0.5f)
        {
            if (Time.time < _stingFree || !AudioPrefs.MusicOn) return;
            var clip = Sfx.MatchClip("sting");
            if (clip == null) return;
            _stingFree = Time.time + 10f;
            var s = gameObject.AddComponent<AudioSource>();
            s.spatialBlend = 0f;
            s.volume = AudioPrefs.Music;
            s.PlayOneShot(clip, volume);
            SoundLog.Played("music/sting", clip, volume);
            Destroy(s, clip.length + 0.5f);
        }

        /// <summary>A recorded match sound from a pool (pad, glove, bounce, rope) where it happened.</summary>
        public void Thud(string pool, Vector3 at, float volume)
        {
            var clip = Sfx.MatchClip(pool);
            if (clip == null || volume <= 0.01f) return;
            var s = Source(at);
            s.pitch = Random.Range(0.94f, 1.06f);
            s.PlayOneShot(clip, Mathf.Clamp01(volume));
            SoundLog.Played("field/" + pool, clip, Mathf.Clamp01(volume), at);
        }

        /// <summary>
        /// The crowd between the moments (× its level), set by how tight the game is
        /// (MatchDirector.AudioFx); a swell settles back to it, not to a fixed level.
        /// </summary>
        public float Base { get; private set; } = 1.2f;

        /// <summary>The roar layer between the moments (0–1), with <see cref="Base"/>.</summary>
        private float _baseRoar;

        public void SetBase(float level, float roar)
        {
            Base = level;
            _baseRoar = roar;
            if (_swell == null)
            {
                _sfx.SetCrowd(Base);
                _sfx.SetRoar(_baseRoar);
            }
        }

        /// <summary>The crowd rises to <paramref name="peak"/> (× its level), the roar with it, and settles over <paramref name="seconds"/>.</summary>
        public void Swell(float peak, float seconds)
        {
            if (_swell != null) StopCoroutine(_swell);
            _swell = StartCoroutine(SwellRoutine(peak, seconds));
        }

        private IEnumerator SwellRoutine(float peak, float seconds)
        {
            _sfx.SetCrowd(peak);
            // 1.6 (an appeal) brings some of it up; 2.3 (a six) all of it.
            _sfx.SetRoar(Mathf.Max(_baseRoar, Mathf.InverseLerp(1.3f, 2.3f, peak)));
            yield return new WaitForSecondsRealtime(seconds * 0.5f);
            _sfx.SetCrowd(Mathf.Lerp(peak, Base, 0.5f));
            _sfx.SetRoar(Mathf.Max(_baseRoar, Mathf.InverseLerp(1.3f, 2.3f, peak) * 0.4f));
            yield return new WaitForSecondsRealtime(seconds * 0.5f);
            _sfx.SetCrowd(Base);
            _sfx.SetRoar(_baseRoar);
            _swell = null;
        }

        private static IEnumerator Later(float seconds, System.Action act)
        {
            yield return new WaitForSeconds(seconds);
            act();
        }
    }
}
