using System;
using Apl.Client.Match;
using Apl.Client.Modes;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The home page's header and right column (owner v26, his 1.png layout in our brand):
    /// the side's crest, its name, the LVL chip and XP bar on the left; on the right the
    /// career record (wins · played), the LOCKER and SETTINGS icons (RESUME beside them when
    /// a match is saved) — no HOME icon on home (owner: it did nothing there). Then DAILY
    /// GOALS (a list with bars and XP) and CHALLENGES (stars, cleared). Read-only use of
    /// audio-modes' ModesInfo.
    /// </summary>
    public sealed partial class MainMenu
    {
        private static readonly Color Brand = new Color(0.45f, 0.25f, 1f), Cyan = new Color(0.2f, 0.75f, 1f);
        private static readonly Color CardFill = new Color(0.08f, 0.07f, 0.2f, 0.97f);
        private static readonly Color XpGreen = new Color(0.45f, 0.9f, 0.4f);

        private void HomeTop(RectTransform page, Franchise team, Vector2 size, float m)
        {
            // Left: crest, side, LVL n and the XP bar.
            const float cy = 88f;
            UiKit.Crest(page, team.Id, TopLeft, new Vector2(m + 54, -cy), 108);
            var name = TextAt(page, m + 124, 34, 640, 60, team.Name.ToUpperInvariant(), 52, TextAnchor.MiddleLeft, Color.white);
            name.fontStyle = FontStyle.Italic;
            var lvl = PlayerCardKit.Shape(page, "Level", CardSprites.Round(10), UiKit.Gold, At(m + 126, 104, 96, 38), new Vector2(96, 38), TopLeft);
            PlayerCardKit.Text((RectTransform)lvl.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, $"LVL {ModesInfo.Level}", 26, TextAnchor.MiddleCenter, UiKit.Ink, condensed: true).fontStyle = FontStyle.Italic;
            BarAt(page, m + 240, 116, 300, ModesInfo.XpFraction, UiKit.Gold);
            TextAt(page, m + 556, 102, 220, 40, $"{ModesInfo.XpInLevel} / {ModesInfo.XpLevelSize} XP", 26, TextAnchor.MiddleLeft, UiKit.Dim);

            // Right, from the edge in: SETTINGS, LOCKER, the record, RESUME.
            const float icon = 92f, gap = 16f;
            var x = size.x - m - icon;
            IconButton(page, "Settings", MenuIcons.For("gear"), x, cy - icon / 2f, icon, ShowSettings);
            x -= icon + gap;
            IconButton(page, "Locker", RoleIcons.For("bat"), x, cy - icon / 2f, icon, () => ModesInfo.OpenLocker());
            const float recW = 300f;
            x -= recW + gap;
            var rec = HomeCard(page, "Record", x, cy - icon / 2f, recW, icon, CardFill, UiKit.WithAlpha(Brand, 0.5f));
            var t = PlayerCardKit.Text(rec, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-12, 0),
                $"<size=46>{ModesInfo.Wins}</size> wins  ·  <size=46>{ModesInfo.Matches}</size> played", 28, TextAnchor.MiddleCenter, Color.white, condensed: true);
            t.supportRichText = true;
            t.resizeTextForBestFit = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (MatchResume.Load(_data) is MatchResume.Record saved)
            {
                const float rw = 430f;
                x -= rw + gap;
                var b = PanelKit.Pill(page, "Resume", TopLeft, new Vector2(x + rw / 2f, -cy), new Vector2(rw, 104), "RESUME ►  " + MatchResume.Describe(saved), new Color(0.15f, 0.6f, 0.3f), () => MatchResume.Launch(_data, saved), 26);
                b.transition = Selectable.Transition.None;
            }
        }

        /// <summary>A square icon button in the header, (x, y) its top left.</summary>
        private static void IconButton(RectTransform page, string name, Sprite icon, float x, float y, float s, Action act)
        {
            var face = HomeCard(page, name, x, y, s, s, CardFill, UiKit.WithAlpha(Brand, 0.5f));
            face.GetComponent<Image>().raycastTarget = true;
            face.gameObject.AddComponent<Button>().onClick.AddListener(() => act());
            PlayerCardKit.Shape(face, "Icon", icon, Color.white, Vector2.zero, new Vector2(s * 0.58f, s * 0.58f));
        }

        /// <summary>A rounded progress bar, (x, y) the top left of its track, 14 px tall.</summary>
        private static void BarAt(RectTransform r, float x, float y, float width, float fill, Color colour, float height = 14f)
        {
            PlayerCardKit.Shape(r, "BarBack", CardSprites.Round(6), new Color(0.35f, 0.33f, 0.75f, 0.35f), At(x, y, width, height), new Vector2(width, height), TopLeft);
            var f = Mathf.Clamp01(fill);
            if (f <= 0) return;
            var fw = Mathf.Max(height, width * f);
            PlayerCardKit.Shape(r, "BarFill", CardSprites.Round(6), colour, At(x, y, fw, height), new Vector2(fw, height), TopLeft);
        }

        /// <summary>DAILY GOALS: today's goals as a list — the goal, its XP, a bar and the count (DONE in green).</summary>
        private void DailyGoalsCard(RectTransform page, float x, float y, float w, float h)
        {
            var goals = ModesInfo.Goals;
            var card = HomeCard(page, "DailyGoals", x, y, w, h, CardFill, UiKit.WithAlpha(Cyan, 0.35f));
            var title = TextAt(card, 32, 26, w * 0.62f, 62, "DAILY GOALS", 50, TextAnchor.MiddleLeft, Color.white);
            title.fontStyle = FontStyle.Italic;
            TextAt(card, w * 0.5f, 34, w * 0.5f - 32, 48, $"{ModesInfo.GoalsDone} of {goals.Count} done", 26, TextAnchor.MiddleRight, UiKit.Dim, condensed: false, bold: false);
            if (goals.Count == 0) return;
            const float top = 112f;
            var rowH = (h - top - 24f) / Mathf.Max(3, goals.Count);
            for (var i = 0; i < goals.Count && i < 4; i++)
            {
                var g = goals[i];
                var ry = top + i * rowH;
                TextAt(card, 32, ry, w - 64 - 130, 44, g.Text, 28, TextAnchor.MiddleLeft, Color.white, condensed: false, bold: false);
                TextAt(card, w - 32 - 130, ry, 130, 44, $"+{g.Xp} XP", 30, TextAnchor.MiddleRight, g.Done ? XpGreen : UiKit.Gold);
                var frac = g.Done ? 1f : g.Progress / (float)Mathf.Max(1, g.Target);
                BarAt(card, 32, ry + 62, w - 64 - 100, frac, g.Done ? XpGreen : UiKit.Gold);
                TextAt(card, w - 32 - 90, ry + 50, 90, 38, g.Done ? "DONE" : $"{g.Progress}/{g.Target}", 26, TextAnchor.MiddleRight, g.Done ? XpGreen : UiKit.Dim);
            }
        }

        /// <summary>CHALLENGES: the star, the name, ›; stars and difficulty, cleared of all, the stars bar. Tap to open.</summary>
        private void ChallengesCard(RectTransform page, float x, float y, float w, float h)
        {
            var card = HomeCard(page, "Challenges", x, y, w, h, CardFill, UiKit.WithAlpha(UiKit.Gold, 0.4f));
            card.GetComponent<Image>().raycastTarget = true;
            card.gameObject.AddComponent<Button>().onClick.AddListener(() => ModesInfo.OpenChallenges(_data));
            PlayerCardKit.Shape(card, "Star", RewardFx.Star, UiKit.Gold, At(30, 34, 62, 62), new Vector2(62, 62), TopLeft);
            var title = TextAt(card, 108, 30, w - 200, 70, "CHALLENGES", 50, TextAnchor.MiddleLeft, Color.white);
            title.fontStyle = FontStyle.Italic;
            TextAt(card, w - 80, 30, 50, 70, "›", 72, TextAnchor.MiddleCenter, UiKit.Dim);
            TextAt(card, 32, h - 104, w * 0.62f, 40, $"{ModesInfo.Stars} of {ModesInfo.MaxStars} stars  ·  {ModesInfo.Difficulty}", 27, TextAnchor.MiddleLeft, Color.white, condensed: false, bold: false);
            TextAt(card, w * 0.55f, h - 104, w * 0.45f - 32, 40, $"{ModesInfo.ChallengesCleared}/{ModesInfo.ChallengesCount} cleared", 27, TextAnchor.MiddleRight, UiKit.Dim, condensed: false, bold: false);
            BarAt(card, 32, h - 48, w - 64, ModesInfo.Stars / (float)Mathf.Max(1, ModesInfo.MaxStars), UiKit.Gold);
        }
    }
}
