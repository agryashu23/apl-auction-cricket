using System;
using Apl.Client.Audio;
using Apl.Client.Match;
using Apl.Client.Modes;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The organised settings (owner v26 #14): one list — SOUND SETTINGS (the sound panel,
    /// Audio/SoundSettings.cs) and GAMEPLAY SETTINGS (controls, difficulty, footwork, running,
    /// haptics) — each sub-page with BACK. The home page's gear opens <see cref="Open"/>; the
    /// match's pause menu (Match/MatchHud.PauseMenu.cs) builds the same list with MATCH, RESUME
    /// and QUIT added, and the same <see cref="GameplayPage"/>. Nothing is set anywhere else
    /// (BALL SPEED is part of DIFFICULTY since owner v26 #5).
    /// </summary>
    public static class GameSettings
    {
        private const string CanvasName = "GameSettings";

        public static readonly Color ListFill = new Color(0.14f, 0.17f, 0.27f, 0.97f);
        public static readonly Color BackFill = new Color(0.12f, 0.14f, 0.22f, 0.95f);

        /// <summary>The home page's settings, on their own opaque canvas (sorting order 70); <paramref name="closed"/> when it goes.</summary>
        public static void Open(Action? closed = null)
        {
            var stale = GameObject.Find(CanvasName);
            if (stale != null) UnityEngine.Object.Destroy(stale);
            var canvas = UiKit.Canvas(CanvasName, 70);
            var root = (RectTransform)canvas.transform;
            RectTransform? page = null;
            void Close()
            {
                UnityEngine.Object.Destroy(canvas.gameObject);
                closed?.Invoke();
            }
            void Show(Action<RectTransform> build)
            {
                if (page != null) UnityEngine.Object.Destroy(page.gameObject);
                page = UiKit.Stretch(root, "Page", new Color(0.02f, 0.03f, 0.07f, 1f));
                build(page);
            }
            void Main() => Show(p =>
            {
                PanelKit.Header(p, "SETTINGS", "Sound and gameplay", null, null);
                ListPanel(p, new (string, string, Color, Action)?[]
                {
                    ("Settings_Sound", "SOUND SETTINGS", ListFill, () => SoundSettings.Open()),
                    ("Settings_Gameplay", "GAMEPLAY SETTINGS", ListFill, () => Show(g => GameplayPage(g, false, Main, null))),
                    ("Back", "◄ BACK", BackFill, Close),
                });
                // Credits (CC BY 4.0 asks for one; client-assets/sfx/CREDITS.md).
                PlayerCardKit.Text(p, new Vector2(0, 0), new Vector2(1, 0), new Vector2(40, 12), new Vector2(-40, 44),
                    "CREDITS  ·  Bat sound: \"Willow Bat Knock\" by BudgetPixel AI (budgetpixel.com), CC BY 4.0", 18, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            });
            Main();
        }

        /// <summary>
        /// A centred glass list of big buttons, top to bottom; a null entry leaves a gap.
        /// Each item: its GameObject name (test runs click by it), label, fill, action.
        /// </summary>
        public static RectTransform ListPanel(RectTransform parent, (string name, string label, Color fill, Action act)?[] items)
        {
            const float itemH = 124f, step = 148f, gap = 40f;
            var count = 0;
            var gaps = 0;
            foreach (var it in items)
            {
                if (it == null) gaps++;
                else count++;
            }
            var h = 80f + count * step + gaps * gap;
            var panel = PanelKit.GlassPanel(parent, "SettingsList", new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(820, h));
            var y = -40f - itemH / 2;
            foreach (var it in items)
            {
                if (it == null)
                {
                    y -= gap;
                    continue;
                }
                var (name, label, fill, act) = it.Value;
                PanelKit.Pill(panel, name, new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(700, itemH), label, fill, act, 40);
                y -= step;
            }
            return panel;
        }

        /// <summary>A sub-page's frame: a wide glass panel with its gold title and a ◄ BACK pill (top right).</summary>
        public static RectTransform SubPanel(RectTransform parent, string title, Vector2 size, Action back)
        {
            var panel = PanelKit.GlassPanel(parent, "Sub_" + title, new Vector2(0.5f, 0.5f), new Vector2(0, -60), size);
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -100), new Vector2(-300, -24), title, 48, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            PanelKit.Pill(panel, "Back", new Vector2(1, 1), new Vector2(-150, -74), new Vector2(240, 120), "◄ BACK", BackFill, back, 34);
            return panel;
        }

        private static RectTransform Column(RectTransform panel, string name, float x, float width)
        {
            var col = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            col.SetParent(panel, false);
            col.anchorMin = col.anchorMax = new Vector2(0.5f, 1);
            col.pivot = new Vector2(0.5f, 1);
            col.anchoredPosition = new Vector2(x, -150);
            col.sizeDelta = new Vector2(width, 640);
            return col;
        }

        /// <summary>
        /// GAMEPLAY SETTINGS: CONTROLS, FOOTWORK, RUNNING on the left; DIFFICULTY (what it does
        /// under it, ball speed included) and HAPTICS on the right. <paramref name="inMatch"/> adds
        /// that the opposition's strength changes from the next match; <paramref name="controlsChanged"/>
        /// lets the match HUD re-lay its buttons at once.
        /// </summary>
        public static RectTransform GameplayPage(RectTransform parent, bool inMatch, Action back, Action? controlsChanged)
        {
            var panel = SubPanel(parent, "GAMEPLAY SETTINGS", new Vector2(1600, 860), back);
            var left = Column(panel, "Left", -390, 740);
            var right = Column(panel, "Right", 390, 740);
            var y = 0f;
            MenuChoice.Row(left, ref y, "CONTROLS", new[] { "BUTTONS", "SWIPE" }, () => BattingPrefs.Swipe ? 1 : 0, i =>
            {
                BattingPrefs.Swipe = i == 1;
                controlsChanged?.Invoke();
            });
            MenuChoice.Row(left, ref y, "FOOTWORK", new[] { "AUTO", "MANUAL" }, () => BattingPrefs.AutoFootwork ? 0 : 1, i =>
            {
                BattingPrefs.AutoFootwork = i == 0;
                controlsChanged?.Invoke();
            });
            MenuChoice.Row(left, ref y, "RUNNING", new[] { "MANUAL", "AUTO" }, () => RunningPrefs.Auto ? 1 : 0, i => RunningPrefs.Auto = i == 1);
            y = 0f;
            Text? note = null;
            string Note() => Difficulty.Describe(Difficulty.Current) + (inMatch ? "\nBall speed and timing change now; the opposition's strength from the next match." : "");
            MenuChoice.Row(right, ref y, "DIFFICULTY", Difficulty.Labels, () => (int)Difficulty.Current, i =>
            {
                Difficulty.Current = (DifficultyLevel)i;
                if (note != null) note.text = Note();
            });
            note = PlayerCardKit.Text(right, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, y - 100), new Vector2(-30, y + 10), Note(), 22, TextAnchor.UpperLeft, UiKit.Dim, bold: false);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            y -= 120f;
            MenuChoice.Row(right, ref y, "HAPTICS", new[] { "ON", "OFF" }, () => BattingPrefs.Haptics ? 0 : 1, i =>
            {
                BattingPrefs.Haptics = i == 0;
                if (i == 0) Haptics.Pulse(Haptics.GoodMs, 170);
            });
            return panel;
        }
    }
}
