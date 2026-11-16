using System;
using System.Collections;
using Apl.Client.Ui;
using UnityEngine;

namespace Apl.Client.Audio
{
    /// <summary>
    /// SOUND (owner v26 #4): five sliders — MASTER, MUSIC, COMMENTARY &amp; VOICES, EFFECTS,
    /// CROWD — each with its % readout. Dragging is a live preview (the scene's sounds read the
    /// levels every frame); lifting the finger saves the level and plays a short sample on that
    /// channel (a bat crack, the crowd's cheer, a call, a bar of the anthem), even under the
    /// pause menu's paused listener. Opened from SETTINGS, the home pop-up and the pause menu;
    /// its own canvas over whatever is up. Public entry: <see cref="Open"/>.
    /// </summary>
    public static class SoundSettings
    {
        private static readonly (AudioPrefs.Channel c, string label, string note)[] Rows =
        {
            (AudioPrefs.Channel.Master, "MASTER", "Everything at once"),
            (AudioPrefs.Channel.Music, "MUSIC", "Anthem, menus, ground music"),
            (AudioPrefs.Channel.Voice, "COMMENTARY & VOICES", "Commentary, auctioneer, PA"),
            (AudioPrefs.Channel.Effects, "EFFECTS", "Bat, ball, stumps, calls"),
            (AudioPrefs.Channel.Crowd, "CROWD", "The bed, roars, applause, chants"),
        };

        /// <summary>The pop-up over everything (sorting order 80); <paramref name="closed"/> when it goes.</summary>
        public static void Open(Action? closed = null)
        {
            var stale = GameObject.Find("SoundSettings");
            if (stale != null) UnityEngine.Object.Destroy(stale);
            var canvas = UiKit.Canvas("SoundSettings", 80);
            var root = (RectTransform)canvas.transform;
            var preview = canvas.gameObject.AddComponent<SoundPreview>();
            // Opaque (round 10): nothing behind shows through.
            var dim = UiKit.Stretch(root, "Page", new Color(0.02f, 0.03f, 0.07f, 1f));
            const float panelW = 1200, panelH = 900;
            var panel = PanelKit.GlassPanel(dim, "Sound", new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(panelW, panelH));
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(44, -100), new Vector2(-240, -24), "SOUND", 52, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(230, -100), new Vector2(-240, -24), "Drag to set · lift to hear it", 24, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
            PanelKit.Pill(panel, "Close", new Vector2(1, 1), new Vector2(-110, -70), new Vector2(160, 120), "X", new Color(0.12f, 0.14f, 0.22f, 0.95f), () =>
            {
                UnityEngine.Object.Destroy(canvas.gameObject);
                closed?.Invoke();
            }, 40);
            // Each row: the name and what it covers on the left, the slider and its % on the right.
            const float rowH = 140, top = -200, labelW = 360, trackW = 520;
            for (var i = 0; i < Rows.Length; i++)
            {
                var (c, label, note) = Rows[i];
                var channel = c;
                var y = top - i * rowH;
                if (i > 0)
                    PlayerCardKit.Shape(panel, "Rule" + i, CardSprites.Round(1), new Color(1, 1, 1, 0.06f), new Vector2(0, y + rowH / 2), new Vector2(panelW - 80, 2), new Vector2(0.5f, 1));
                PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, y - 4), new Vector2(44 + labelW, y + 40), label, 36, TextAnchor.MiddleLeft, Color.white, condensed: true);
                PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, y - 40), new Vector2(44 + labelW, y - 6), note, 21, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
                var slider = VolumeSlider.Build(panel, "Slider_" + channel, new Vector2(44 + labelW + 30 + trackW / 2 + 30, y), trackW, AudioPrefs.Level(channel));
                slider.Changed = v => AudioPrefs.SetLevel(channel, v, false);
                slider.Released = v =>
                {
                    AudioPrefs.SetLevel(channel, v, true);
                    preview.Sample(channel);
                };
            }
        }
    }
}
