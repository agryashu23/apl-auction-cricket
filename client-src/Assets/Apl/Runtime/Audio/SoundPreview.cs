using System;
using System.Collections;
using Apl.Client.Ui;
using UnityEngine;

namespace Apl.Client.Audio
{
    /// <summary>The sample a channel plays when its slider is let go (paused listener or not).</summary>
    internal sealed class SoundPreview : MonoBehaviour
    {
        private AudioSource _source = null!;
        private Coroutine? _stop;

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            // The pause menu pauses the listener; the sample still has to be heard.
            _source.ignoreListenerPause = true;
            _source.spatialBlend = 0f;
        }

        public void Sample(AudioPrefs.Channel c)
        {
            var (path, level, seconds) = c switch
            {
                AudioPrefs.Channel.Music => ("Music/anthem", AudioPrefs.Music * 0.6f, 2.5f),
                AudioPrefs.Channel.Voice => ("Sfx/calls/arjun_call_come", AudioPrefs.Voice, 2f),
                AudioPrefs.Channel.Crowd => ("Sfx/cheer", AudioPrefs.Crowd * 0.6f, 2.2f),
                _ => ("Sfx/bat/stroke_1", AudioPrefs.Effects, 1.2f),
            };
            var clip = Resources.Load<AudioClip>(path);
            if (clip == null || level <= 0f) return;
            if (_stop != null) StopCoroutine(_stop);
            _source.Stop();
            _source.clip = clip;
            _source.volume = level;
            // The anthem from its chorus, not its silent first beat.
            _source.time = c == AudioPrefs.Channel.Music ? Mathf.Min(clip.length * 0.3f, Mathf.Max(0f, clip.length - seconds)) : 0f;
            _source.Play();
            SoundLog.Played("settings/sample", clip, level);
            _stop = StartCoroutine(FadeOut(seconds));
        }

        private IEnumerator FadeOut(float after)
        {
            yield return new WaitForSecondsRealtime(after);
            var v = _source.volume;
            for (var t = 0f; t < 0.4f; t += Time.unscaledDeltaTime)
            {
                _source.volume = Mathf.Lerp(v, 0f, t / 0.4f);
                yield return null;
            }
            _source.Stop();
        }
    }
}
