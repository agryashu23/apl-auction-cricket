using System.Collections.Generic;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The ground's two big screens come alive (rating r7: "always the same static
    /// logo"): a world-space panel just in front of each LED face cycles the live
    /// score and the chase (MatchDirector.ScreenScore) with the APL artwork
    /// behind, and flashes FOUR / SIX / WICKET in the batting side's colour when
    /// the crowd goes up (StadiumBuilder.CrowdCheer → <see cref="Event"/>).
    /// </summary>
    public sealed class StadiumBigScreen : MonoBehaviour
    {
        private static StadiumBigScreen? _instance;

        private readonly List<(GameObject panel, Text score, Text sub, Image stripe, Text flash, RectTransform flashBox)> _faces =
            new List<(GameObject, Text, Text, Image, Text, RectTransform)>();

        private MatchDirector? _director;
        private float _flashUntil, _flashStart, _next;
        private bool _showScore = true;

        /// <summary>Adds a screen face: its centre, the way it faces (toward the middle), its size in metres.</summary>
        public void Face(Vector3 centre, Vector3 inward, float width, float height)
        {
            var go = new GameObject("BigScreenFeed", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            const float px = 1000f;
            rt.sizeDelta = new Vector2(px, px * height / width);
            rt.localScale = Vector3.one * (width / px);
            // A canvas shows its front toward -forward: face it at the middle, a hair proud of the LEDs.
            rt.SetPositionAndRotation(centre + inward * 0.12f, Quaternion.LookRotation(-inward, Vector3.up));

            var panel = UiKit.Stretch(rt, "Score", new Color(0.03f, 0.04f, 0.12f, 1f)); // opaque: the art ghosted through (r8 `41`)
            // Round 12 (audio-modes): every line of text in the LOWER half of the face — the top edge of the
            // gameplay and broadcast shots cut the upper half ("WICKET!" half-read, r11 `50`) — and the
            // live feed (BigScreenFeed: the broadcast picture, LIVE / REPLAY) in the upper half.
            var stripe = UiKit.Fill(UiKit.Strip(panel, 0f, 0.05f), Color.white);
            var score = UiKit.Label(UiKit.Strip(panel, 0.27f, 0.5f), "", 110, TextAnchor.MiddleCenter);
            var sub = UiKit.Label(UiKit.Strip(panel, 0.06f, 0.27f), "", 70, TextAnchor.MiddleCenter, new Color(1f, 0.82f, 0.3f));
            BigScreenFeed.Attach(panel, new Vector2(0.18f, 0.53f), new Vector2(0.82f, 0.98f));
            var flashBox = UiKit.Stretch(rt, "Flash", new Color(0.03f, 0.04f, 0.12f, 1f));
            var flash = UiKit.Label(UiKit.Strip(flashBox, 0.04f, 0.52f), "", 200, TextAnchor.MiddleCenter);
            flashBox.gameObject.SetActive(false);
            panel.gameObject.SetActive(false);
            _faces.Add((panel.gameObject, score, sub, stripe, flash, flashBox));
        }

        private void Awake() => _instance = this;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>A big moment on the screens for a few seconds: "SIX!", "FOUR!", "WICKET!".</summary>
        public static void Event(string text, Color colour)
        {
            if (_instance == null) return;
            _instance._flashStart = Time.time;
            _instance._flashUntil = Time.time + 4f;
            foreach (var f in _instance._faces)
            {
                f.flash.text = text;
                f.flash.color = colour;
                f.flashBox.gameObject.SetActive(true);
            }
        }

        private void Update()
        {
            var flashing = Time.time < _flashUntil;
            foreach (var f in _faces)
            {
                f.flashBox.gameObject.SetActive(flashing);
                if (flashing) f.flashBox.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin((Time.time - _flashStart) * 9f));
            }
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            if (_director == null) _director = FindFirstObjectByType<MatchDirector>();
            var s = _director != null ? _director.ScreenScore() : null;
            // Mostly the score: twelve seconds of it, three of the artwork.
            _showScore = s != null && Mathf.Repeat(Time.time, 15f) < 12f;
            foreach (var f in _faces)
            {
                f.panel.SetActive(_showScore && !flashing);
                if (s == null) continue;
                f.score.text = s.Value.score;
                f.sub.text = s.Value.sub;
                f.stripe.color = UiKit.ParseHex(s.Value.teamColour);
            }
        }
    }
}
