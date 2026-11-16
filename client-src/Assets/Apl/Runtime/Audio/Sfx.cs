using UnityEngine;

namespace Apl.Client.Audio
{
    /// <summary>
    /// Hall sound: recorded clips from Resources/Sfx (tools/fetch_sfx.sh —
    /// gavel, applause in three sizes, cheer, "ooh", room murmur), with
    /// synthesized stand-ins for any that are missing so the hall is never
    /// silent. The murmur loops under everything and can swell or settle.
    /// </summary>
    public sealed class Sfx : MonoBehaviour
    {
        /// <summary>Murmur level while the auction runs.</summary>
        public float CrowdLevel = 0.18f;

        /// <summary>Scales applause, cheers and "ooh" (kept under the voices).</summary>
        public float CrowdFxVolume = 0.45f;

        private AudioSource _oneShots = null!;
        private AudioSource _crowd = null!;
        private AudioClip _bat = null!;
        private AudioClip _gavel = null!, _gavelTap = null!, _gavelSold = null!, _paddle = null!, _tick = null!, _shutter = null!;
        private AudioSource _gavelSource = null!;
        private AudioSource? _batSource;
        private AudioSource _crowdFx = null!;
        private float _musicLevel;
        private AudioClip _applauseSmall = null!, _applause = null!, _applauseBig = null!, _cheer = null!, _ooh = null!;
        private float _crowdTarget;
        private AudioSource _music = null!;
        private float _musicTarget, _musicFadeRate = 1f;

        private void Awake()
        {
            _oneShots = gameObject.AddComponent<AudioSource>();
            _crowd = gameObject.AddComponent<AudioSource>();
            _gavel = Load("gavel") ?? Knock();
            _gavelTap = Load("gavel_tap") ?? _gavel;
            // SOLD: one big hammer beat.
            _gavelSold = Load("gavel_sold") ?? _gavelTap;
            // Its own source so the knock is never ducked under applause.
            _gavelSource = gameObject.AddComponent<AudioSource>();
            _gavelSource.priority = 0;
            _paddle = Load("paddle") ?? Swish();
            _bat = Load("bat/stroke_1") ?? BatHit();
            _tick = Load("tick") ?? Click();
            _shutter = Load("shutter") ?? Shutter();
            _applause = Load("applause") ?? NoiseBursts("applause", 2.6f, 900, 0.5f);
            _applauseBig = Load("applause_big") ?? _applause;
            _applauseSmall = Load("applause_short") ?? _applause;
            _cheer = Load("cheer") ?? _applauseBig;
            _ooh = Load("ooh") ?? _applauseSmall;
            _crowd.clip = Load("crowd") ?? Murmur();
            _crowd.loop = true;
            _crowd.volume = _crowdTarget = CrowdLevel;
            _crowd.Play();
            // Applause, cheers, the ooh and chants on their own source: the CROWD level, not EFFECTS.
            _crowdFx = gameObject.AddComponent<AudioSource>();
            AudioPrefs.Apply();
        }

        private void Update()
        {
            // The levels (AudioPrefs): EFFECTS on the one-shots, CROWD on the bed and its reactions, MUSIC on the track.
            _oneShots.volume = _gavelSource.volume = AudioPrefs.Effects;
            if (_batSource != null) _batSource.volume = AudioPrefs.Effects;
            _crowdFx.volume = AudioPrefs.Crowd;
            _crowd.volume = Mathf.MoveTowards(_crowd.volume, _crowdTarget * AudioPrefs.Crowd, Time.unscaledDeltaTime * 0.25f);
            if (_music != null && _music.clip != null)
            {
                _music.mute = !AudioPrefs.MusicOn;
                _musicLevel = Mathf.MoveTowards(_musicLevel, _musicTarget, Time.unscaledDeltaTime * _musicFadeRate);
                _music.volume = _musicLevel * AudioPrefs.Music;
                if (_musicTarget <= 0f && _musicLevel <= 0f && _music.isPlaying) _music.Stop();
            }
        }

        /// <summary>
        /// Background music from Resources/Music/&lt;name&gt; (the APL anthem on the title
        /// and results screens). Fades in; does nothing if the track isn't there yet.
        /// </summary>
        public void PlayMusic(string name, float volume = 0.6f, float fadeSeconds = 2f)
        {
            if (_music == null)
            {
                _music = gameObject.AddComponent<AudioSource>();
                _music.loop = true;
                _music.playOnAwake = false;
                _music.ignoreListenerPause = false;
            }
            var clip = Resources.Load<AudioClip>("Music/" + name);
            if (clip == null) return;
            if (_music.clip != clip || !_music.isPlaying)
            {
                _music.clip = clip;
                _music.volume = _musicLevel = 0f;
                _music.Play();
                SoundLog.Played("music/play", clip, volume);
            }
            _musicTarget = volume;
            _musicFadeRate = volume / Mathf.Max(0.1f, fadeSeconds);
        }

        /// <summary>
        /// The menus' track: the APL anthem and "It Takes a Hero" (Resources/Music/menu_hero)
        /// take turns, one per launch, so the home page doesn't always open on the same tune.
        /// </summary>
        public static string MenuTrack()
        {
            const string key = "apl.menuTrack";
            var n = PlayerPrefs.GetInt(key, 0);
            PlayerPrefs.SetInt(key, n + 1);
            return n % 2 == 1 && Resources.Load<AudioClip>("Music/menu_hero") != null ? "menu_hero" : "anthem";
        }



        public void StopMusic(float fadeSeconds = 2.5f)
        {
            if (_music == null) return;
            _musicFadeRate = Mathf.Max(0.05f, _musicLevel) / Mathf.Max(0.1f, fadeSeconds);
            if (_musicTarget > 0f) SoundLog.Played("music/stop", _music.clip, 0f);
            _musicTarget = 0f;
        }

        public void Gavel() => One(_gavelSource, "hall/gavel", _gavel, 1f);
        public void GavelTap() => One(_gavelSource, "hall/gavel_tap", _gavelTap, 0.9f);

        /// <summary>The SOLD hammer: one loud beat on the strike's impact.</summary>
        public void GavelHit() => One(_gavelSource, "hall/gavel_sold", _gavelSold, 1f);
        /// <summary>Bat on ball: a sharp wooden crack, louder off the middle.</summary>
        public void Bat(float strength = 1f) => One(_oneShots, "bat/plain", _bat, Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(strength)));

        /// <summary>
        /// Bat on ball, wooden (round 13), by kind: "edge" (a thin, short, dull nick — edges and
        /// play-and-miss nicks), "stroke" (a solid crisp tock — ground strokes), "loft" (a deeper,
        /// fuller, louder crack — lofts and big hits). Older callers' "middle" / null map to stroke
        /// (loft when it's a big one). Three takes a kind, never the same twice running, a touch of
        /// pitch spread. Logged as bat/edge, bat/stroke, bat/loft.
        /// </summary>
        public void Bat(string? kind, float strength)
        {
            strength = Mathf.Clamp01(strength);
            var pool = kind == "edge" || kind == "loft" || kind == "stroke" ? kind
                : kind == "middle" && strength >= 0.9f ? "loft" : "stroke";
            var clip = BatClips.Pick(pool, Load) ?? _bat;
            // Its own source: pitch bends everything playing on a source, the applause must not wobble.
            if (_batSource == null) _batSource = gameObject.AddComponent<AudioSource>();
            _batSource.pitch = Random.Range(0.97f, 1.03f);
            var volume = pool == "edge" ? Mathf.Lerp(0.45f, 0.7f, strength) : pool == "loft" ? Mathf.Lerp(0.85f, 1f, strength) : Mathf.Lerp(0.6f, 0.9f, strength);
            One(_batSource, "bat/" + pool, clip, volume);
        }
        public void Paddle() => _oneShots.PlayOneShot(_paddle, 0.5f);
        public void Tick() => _oneShots.PlayOneShot(_tick, 0.6f);
        public void CameraShutter(float volume = 0.35f) => _oneShots.PlayOneShot(_shutter, volume);

        /// <summary>0–1: a polite ripple, a solid round, or the whole room.</summary>
        public void Applause(float strength)
        {
            var clip = strength >= 0.9f ? _applauseBig : strength >= 0.5f ? _applause : _applauseSmall;
            One(_crowdFx, "crowd/applause", clip, Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(strength)) * CrowdFxVolume);
        }

        /// <summary>Whoops and claps: a team walks on, a marquee buy, your signing.</summary>
        public void Cheer(float volume = 0.9f) => One(_crowdFx, "crowd/cheer", _cheer, volume * CrowdFxVolume);

        /// <summary>The room reacting to a big jump in the bidding.</summary>
        public void Ooh(float volume = 0.8f) => One(_crowdFx, "crowd/ooh", _ooh, volume * CrowdFxVolume);

        /// <summary>The stands chanting: "apl", or a home side's name (franchise id, lower case).</summary>
        public void Chant(string name, float volume = 0.8f)
        {
            var clip = Load("chant_" + name);
            if (clip != null) One(_crowdFx, "crowd/chant", clip, volume * CrowdFxVolume * 1.6f);
        }

        /// <summary>Raise or settle the murmur (1 = CrowdLevel); it eases there.</summary>
        public void SetCrowd(float relative) => _crowdTarget = CrowdLevel * relative;

        private AudioSource? _roar;
        private float _roarTarget;

        /// <summary>
        /// The ground's own beds (match only): a thousand people talking in place of the
        /// hall's murmur, and under it a looped roar that <see cref="SetRoar"/> brings up
        /// on the big moments. No-op where the clips aren't installed.
        /// </summary>
        public void UseStadiumBeds()
        {
            var bed = Load("match/crowd_bed");
            if (bed != null && _crowd.clip != bed)
            {
                _crowd.clip = bed;
                _crowd.Play();
                SoundLog.Played("crowd/bed (loop)", bed, _crowdTarget);
            }
            var roar = Load("match/crowd_roar");
            if (roar == null || _roar != null) return;
            _roar = gameObject.AddComponent<AudioSource>();
            _roar.clip = roar;
            _roar.loop = true;
            _roar.volume = 0f;
            _roar.Play();
        }

        /// <summary>The roar layer, 0 (silent) … 1 (the whole ground on its feet); it eases there.</summary>
        public void SetRoar(float level)
        {
            var target = Mathf.Clamp01(level) * CrowdFxVolume * 0.9f;
            if (Mathf.Abs(target - _roarTarget) > 0.05f) SoundLog.Played("crowd/roar", _roar != null ? _roar.clip : null, target);
            _roarTarget = target;
        }

        /// <summary>A one-shot on <paramref name="source"/>, logged (SoundLog).</summary>
        private static void One(AudioSource source, string kind, AudioClip clip, float volume)
        {
            source.PlayOneShot(clip, volume);
            SoundLog.Played(kind, clip, volume);
        }

        /// <summary>A one-shot on the effects source (MatchSfx's stings), logged.</summary>
        public void Effect(string kind, AudioClip clip, float volume) => One(_oneShots, kind, clip, volume);

        /// <summary>A one-shot on the crowd source, logged.</summary>
        public void CrowdEffect(string kind, AudioClip clip, float volume) => One(_crowdFx, kind, clip, volume);

        private void LateUpdate()
        {
            if (_roar != null) _roar.volume = Mathf.MoveTowards(_roar.volume, _roarTarget * AudioPrefs.Crowd, Time.unscaledDeltaTime * (_roarTarget > _roar.volume ? 0.9f : 0.18f));
        }

        /// <summary>A recorded match sound from Resources/Sfx/match (pad, glove, bounce, rope, stumps, bails), else null.</summary>
        public static AudioClip? MatchClip(string pool) => BatClips.Pick(pool, Load);

        private static AudioClip? Load(string name) => Resources.Load<AudioClip>("Sfx/" + name);

        // ---- synthesized stand-ins ------------------------------------------

        private const int Rate = 44100;

        private static AudioClip Make(string name, float seconds, System.Func<int, float, float> sample)
        {
            var n = (int)(Rate * seconds);
            var data = new float[n];
            for (var i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i, i / (float)Rate), -1, 1);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Two wooden knocks: low thump plus a short noisy click.</summary>
        private static AudioClip Knock()
        {
            var rng = new System.Random(1);
            return Make("gavel", 0.6f, (i, t) =>
            {
                float Hit(float start)
                {
                    var u = t - start;
                    if (u < 0) return 0;
                    var env = Mathf.Exp(-u * 38f);
                    return env * (0.7f * Mathf.Sin(2 * Mathf.PI * 180f * u) + 0.35f * (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-u * 120f));
                }
                return Hit(0f) + Hit(0.28f);
            });
        }

        /// <summary>
        /// Willow on leather: a click of noise, a hollow knock (two body modes
        /// of the blade) dying in ~60 ms, a short ring of the handle.
        /// </summary>
        private static AudioClip BatHit()
        {
            var rng = new System.Random(5);
            return Make("bat_hit", 0.35f, (i, t) =>
            {
                var click = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 900f) * 0.9f;
                var knock = (Mathf.Sin(2 * Mathf.PI * 1150f * t) * 0.55f + Mathf.Sin(2 * Mathf.PI * 620f * t) * 0.45f) * Mathf.Exp(-t * 55f);
                var ring = Mathf.Sin(2 * Mathf.PI * 2350f * t) * Mathf.Exp(-t * 30f) * 0.12f;
                return (click + knock + ring) * 0.9f;
            });
        }

        private static AudioClip Swish()
        {
            var rng = new System.Random(2);
            var last = 0f;
            return Make("paddle", 0.25f, (i, t) =>
            {
                var env = Mathf.Sin(Mathf.PI * t / 0.25f);
                last = Mathf.Lerp(last, (float)(rng.NextDouble() * 2 - 1), 0.25f);
                return last * env * 0.5f;
            });
        }

        /// <summary>Mirror slap and shutter: two short filtered clicks.</summary>
        private static AudioClip Shutter()
        {
            var rng = new System.Random(5);
            return Make("shutter", 0.12f, (i, t) =>
            {
                float Snap(float start, float decay)
                {
                    var u = t - start;
                    return u < 0 ? 0 : Mathf.Exp(-u * decay) * (float)(rng.NextDouble() * 2 - 1);
                }
                return 0.7f * Snap(0f, 160f) + 0.5f * Snap(0.055f, 220f);
            });
        }

        private static AudioClip Click() =>
            Make("tick", 0.08f, (i, t) => Mathf.Sin(2 * Mathf.PI * 1250f * t) * Mathf.Exp(-t * 60f) * 0.6f);

        /// <summary>Many short random claps.</summary>
        private static AudioClip NoiseBursts(string name, float seconds, int claps, float level)
        {
            var rng = new System.Random(3);
            var n = (int)(Rate * seconds);
            var data = new float[n];
            for (var c = 0; c < claps; c++)
            {
                var start = (int)(rng.NextDouble() * (n - 2000));
                var fade = 1f - start / (float)n;
                for (var k = 0; k < 900 && start + k < n; k++)
                {
                    data[start + k] += (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-k / 140f) * 0.12f * fade;
                }
            }
            for (var i = 0; i < n; i++) data[i] = Mathf.Clamp(data[i] * level * 3f, -1, 1);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Low, slowly breathing filtered noise: a room full of people.</summary>
        private static AudioClip Murmur()
        {
            var rng = new System.Random(4);
            var lp = 0f;
            return Make("crowd", 6f, (i, t) =>
            {
                lp = Mathf.Lerp(lp, (float)(rng.NextDouble() * 2 - 1), 0.03f);
                var swell = 0.6f + 0.4f * Mathf.Sin(t * 0.9f) * Mathf.Sin(t * 0.37f + 1f);
                // Fade the loop seam.
                var edge = Mathf.Min(1f, Mathf.Min(t, 6f - t) * 4f);
                return lp * swell * edge * 2.2f;
            });
        }
    }
}
