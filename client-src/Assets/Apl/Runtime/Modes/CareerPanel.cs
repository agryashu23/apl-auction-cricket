using System;
using Apl.Client.Menu;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Modes
{
    /// <summary>
    /// The career column on the challenges page: the level and its XP bar, matches and
    /// wins, the DIFFICULTY choice (it applies to every match), today's goals and
    /// what the locker has equipped, with the way into it (LockerPage).
    /// </summary>
    public static class CareerPanel
    {
        public static void Build(RectTransform page, Action rebuild)
        {
            var panel = PanelKit.GlassPanel(page, "Career", new Vector2(0, 1), new Vector2(320, -560), new Vector2(560, 820));
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -80), new Vector2(-30, -20), $"LEVEL {Progression.Level}", 52, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -80), new Vector2(-30, -20), $"{Progression.Matches} played · {Progression.Wins} won", 22, TextAnchor.MiddleRight, UiKit.Dim, bold: false);
            RewardCard.LevelBar(panel, new Vector2(0, -110), 500);
            // DIFFICULTY: the settings row, with what the chosen one does under it.
            var rows = (RectTransform)new GameObject("Rows", typeof(RectTransform)).transform;
            rows.SetParent(panel, false);
            rows.anchorMin = rows.anchorMax = new Vector2(0.5f, 1);
            rows.pivot = new Vector2(0.5f, 1);
            rows.anchoredPosition = new Vector2(0, -170);
            rows.sizeDelta = new Vector2(560, 160);
            var y = 0f;
            Text? note = null;
            MenuChoice.Row(rows, ref y, "DIFFICULTY", Difficulty.Labels, () => (int)Difficulty.Current, i =>
            {
                Difficulty.Current = (DifficultyLevel)i;
                if (note != null) note.text = Difficulty.Describe(Difficulty.Current);
            });
            note = PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -340), new Vector2(-30, -300), Difficulty.Describe(Difficulty.Current), 20, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            // Today's goals, then THE VAULT's summary and the way in.
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -400), new Vector2(-30, -355), "TODAY'S GOALS", 28, TextAnchor.MiddleLeft, Color.white, condensed: true);
            var y2 = -400f;
            foreach (var (text, done) in DailyGoals.Lines())
            {
                PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, y2 - 36), new Vector2(-30, y2), (done ? "DONE  " : "") + text, 20, TextAnchor.MiddleLeft, done ? UiKit.Good : UiKit.Dim, bold: false);
                y2 -= 36;
            }
            y2 -= 14;
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, y2 - 40), new Vector2(-30, y2), "THE VAULT", 28, TextAnchor.MiddleLeft, Color.white, condensed: true);
            y2 -= 40;
            var unlocked = 0;
            foreach (var x in Cosmetics.All) if (Progression.Unlocked(x)) unlocked++;
            var line = $"{Progression.Equipped(Slot.Title).Name} · Bat {Progression.Equipped(Slot.Bat).Name} · Helmet {Progression.Equipped(Slot.Helmet).Name} · Pads {Progression.Equipped(Slot.Pads).Name} · Celebration {Progression.Equipped(Slot.Celebration).Name} · {unlocked}/{Cosmetics.All.Length} unlocked";
            var summary = PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, y2 - 80), new Vector2(-30, y2), line, 19, TextAnchor.UpperLeft, UiKit.Dim, bold: false);
            summary.horizontalOverflow = HorizontalWrapMode.Wrap;
            PanelKit.Pill(panel, "Locker", new Vector2(0.5f, 0), new Vector2(0, 80), new Vector2(480, 120), "OPEN THE VAULT ►", new Color(0.85f, 0.45f, 0.1f), () => LockerPage.Open(rebuild), 32);
        }
    }
}