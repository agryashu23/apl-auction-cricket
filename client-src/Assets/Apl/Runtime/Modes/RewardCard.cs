using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Modes
{
    /// <summary>
    /// The rewards screen: its own opaque full-screen page, never over play — opened
    /// after the result card's CONTINUE (MatchDirector.Modes), after an auction, or on
    /// the home page for one not yet seen (<see cref="ShowPending"/>). Left: the
    /// challenge's stars and the XP with its lines; right: the level bar and the
    /// locker's new items. It plays as a reveal (RewardFx): the XP counts up, the bar
    /// fills from where it was (over the top on a level-up), the unlocks pop in.
    /// </summary>
    public sealed class RewardCard : MonoBehaviour
    {
        public static readonly Color StarOn = RewardFx.StarOn, StarOff = RewardFx.StarOff;
        public static void StarRow(RectTransform parent, Vector2 pos, int stars, float size, Vector2? anchor = null) => RewardFx.StarRow(parent, pos, stars, size, anchor);
        public static void LevelBar(RectTransform parent, Vector2 pos, float width, Vector2? anchor = null) => RewardFx.LevelBar(parent, pos, width, anchor);

        /// <summary>A reward not yet seen (the match was left before CONTINUE, or an auction's): the home page shows it.</summary>
        public static (Award award, Challenge? ch, int stars)? Pending;

        public static bool ShowPending(Action? done = null)
        {
            if (Pending is not { } p) return false;
            Show(p.award, p.ch, p.stars, null, null, done);
            return true;
        }

        private const float BarWidth = 700f;
        private static readonly Vector2 BarAt = new Vector2(0, -150);

        public static RewardCard Show(Award a, Challenge? ch, int stars, Action? retry, Action? toList, Action? done = null)
        {
            Pending = null;
            foreach (var old in FindObjectsByType<RewardCard>(FindObjectsSortMode.None)) Destroy(old.gameObject);
            var canvas = UiKit.Canvas("RewardScreen", 90);
            var card = canvas.gameObject.AddComponent<RewardCard>();
            var page = UiKit.Stretch((RectTransform)canvas.transform, "Page", new Color(0.02f, 0.03f, 0.07f, 1f));
            var title = ch != null ? ch.Title.ToUpperInvariant() : a.Lines.Any(l => l.Contains("Auction")) ? "AUCTION REWARDS" : "MATCH REWARDS";
            PlayerCardKit.Text(page, new Vector2(0, 1), new Vector2(1, 1), new Vector2(80, -170), new Vector2(-80, -60), title, 60, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            // UI/UX dev (r1 §8: "two large panels with 3 lines of text"): both panels sized to their
            // content (same height, tops level), an auction's summary under its XP lines, and today's
            // goals on the right when nothing new unlocked.
            var auction = ch == null && title == "AUCTION REWARDS" ? AuctionGoalFacts.Last : null;
            var summaryRows = auction == null ? 0 : SummaryRows(auction).Count;
            var next = NextUnlock();
            var goalsShown = a.Unlocked.Count == 0;
            var leftH = 60f + (ch != null ? 154f : 0f) + 110f + a.Lines.Count * 38f + (summaryRows > 0 ? 70f + summaryRows * 42f : 0f);
            var rightH = 250f + (a.Unlocked.Count > 0 ? 380f : (next != null ? 40f : 0f)) + (goalsShown ? 70f + 3 * 46f : 0f);
            var panelH = Mathf.Clamp(Mathf.Max(leftH, rightH, 420f), 420f, 720f);
            var panelY = 380f - panelH / 2f;
            // Left: stars, XP, the lines.
            var left = PanelKit.GlassPanel(page, "Earned", new Vector2(0.5f, 0.5f), new Vector2(-440, panelY), new Vector2(820, panelH));
            var y = -30f;
            Text Line(RectTransform p, string s, int size, Color c, float height, bool bold = true)
            {
                var t = PlayerCardKit.Text(p, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, y - height), new Vector2(-40, y), s, size, TextAnchor.MiddleLeft, c, condensed: bold, bold: bold);
                y -= height;
                return t;
            }
            var starImages = new List<Image>();
            if (ch != null)
            {
                Line(left, stars > 0 ? "CHALLENGE COMPLETE" : "NOT THIS TIME", 30, stars > 0 ? UiKit.Good : UiKit.Bad, 44);
                starImages = RewardFx.StarRow(left, new Vector2(0, y - 50), stars, 84);
                y -= 110;
            }
            var xpText = Line(left, "+0 XP", 84, UiKit.Gold, 110);
            var lines = a.Lines.Select(l => Line(left, l, 26, UiKit.Dim, 38, bold: false)).ToList();
            if (auction != null && summaryRows > 0)
            {
                // YOUR AUCTION: label | value rows on a raised strip each (UI/UX dev r1 §8).
                y -= 24;
                Line(left, "YOUR AUCTION", 26, UiKit.Gold, 46);
                foreach (var (label, value) in SummaryRows(auction))
                {
                    var row = PlayerCardKit.Shape(left, "Sum_" + label, CardSprites.Round(10), new Color(1, 1, 1, 0.05f), new Vector2(0, y - 19), new Vector2(740, 36), new Vector2(0.5f, 1));
                    var rrt = (RectTransform)row.transform;
                    PlayerCardKit.Text(rrt, Vector2.zero, Vector2.one, new Vector2(16, 0), new Vector2(-16, 0), label, 20, TextAnchor.MiddleLeft, UiKit.Dim, condensed: true);
                    var v = PlayerCardKit.Text(rrt, Vector2.zero, Vector2.one, new Vector2(200, 0), new Vector2(-16, 0), value, 22, TextAnchor.MiddleRight, Color.white, condensed: false, bold: true);
                    v.resizeTextForBestFit = true;
                    v.resizeTextMinSize = 14;
                    v.resizeTextMaxSize = 22;
                    y -= 42;
                }
            }
            // Right: the level, the bar, what came in.
            var right = PanelKit.GlassPanel(page, "Level", new Vector2(0.5f, 0.5f), new Vector2(440, panelY), new Vector2(820, panelH));
            var before = Progression.ProgressAt(Math.Max(0, Progression.Xp - a.Xp));
            var levelText = PlayerCardKit.Text(right, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -106), new Vector2(-40, -30), $"LEVEL {before.level}", 52, TextAnchor.MiddleLeft, Color.white, condensed: true);
            // The title the user wears (THE VAULT's TITLE slot), top right of the level panel — labelled, so a
            // "MATCH WINNER" title doesn't read as a match result on an auction's page (UI/UX dev r1 §8).
            var worn = PlayerCardKit.Text(right, new Vector2(0, 1), new Vector2(1, 1), new Vector2(300, -106), new Vector2(-40, -30), $"<color=#9AA3B5><size=20>TITLE</size></color>  {Progression.Equipped(Slot.Title).Name}", 30, TextAnchor.MiddleRight, UiKit.Gold, condensed: true);
            worn.supportRichText = true;
            var (fill, caption) = RewardFx.Bar(right, BarAt, BarWidth, before.into / (float)before.size, $"{before.into} / {before.size} XP", new Vector2(0.5f, 1));
            y = -230f;
            var unlocks = new List<Transform>();
            if (a.Unlocked.Count == 0 && next != null) Line(right, "Next unlock: " + next, 24, UiKit.Dim, 40, bold: false);
            if (goalsShown)
            {
                // Today's goals with ticks (the page's right half used to be empty).
                y -= 20;
                Line(right, "TODAY'S GOALS", 26, UiKit.Gold, 46);
                foreach (var (goal, progress, goalDone) in DailyGoals.Status())
                {
                    var mark = goalDone ? "<color=#59D97F>DONE</color>" : $"<color=#9AA3B5>{progress}/{goal.Target}</color>";
                    var g = Line(right, $"{mark}   {goal.Text}   <color=#F2C24F>+{goal.Xp} XP</color>", 24, goalDone ? Color.white : UiKit.Dim, 46, bold: false);
                    g.supportRichText = true;
                }
            }
            // The unlocks as cards with their picture (the painted bat, the colourway, the title plate): up to three, the rest counted.
            if (a.Unlocked.Count > 0) Line(right, a.Unlocked.Count == 1 ? "NEW IN THE VAULT" : $"NEW IN THE VAULT ({a.Unlocked.Count})", 28, UiKit.Good, 44);
            var shown = a.Unlocked.Take(3).ToList();
            for (var i = 0; i < shown.Count; i++)
            {
                var u = shown[i];
                var cardFace = PlayerCardKit.Shape(right, "Unlock_" + u.Id, CardSprites.Round(18), new Color(0.08f, 0.09f, 0.16f, 1f), new Vector2((i - (shown.Count - 1) / 2f) * 250f, -300 - 150), new Vector2(230, 300), new Vector2(0.5f, 1));
                var crt = (RectTransform)cardFace.transform;
                LockerPage.Picture(crt, u, true);
                PlayerCardKit.Text(crt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(8, 46), new Vector2(-8, 86), u.Name, 22, TextAnchor.MiddleCenter, Color.white, condensed: true);
                PlayerCardKit.Text(crt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(8, 12), new Vector2(-8, 44), LockerPage.SlotName(u.Slot), 18, TextAnchor.MiddleCenter, RewardFx.StarOn, condensed: true);
                unlocks.Add(crt);
            }
            if (a.Unlocked.Count > 3) PlayerCardKit.Text(right, new Vector2(0, 0), new Vector2(1, 0), new Vector2(40, 20), new Vector2(-40, 60), $"+{a.Unlocked.Count - 3} more in the Vault", 22, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            // The buttons, centred along the bottom.
            var buttons = new List<(string label, Color c, Action act)>();
            void Close()
            {
                Destroy(canvas.gameObject);
                done?.Invoke();
            }
            buttons.Add(("CONTINUE ►", UiKit.Accent, Close));
            if (retry != null) buttons.Add(("RETRY", new Color(0.15f, 0.55f, 0.3f), retry));
            if (toList != null) buttons.Add(("CHALLENGES", new Color(0.55f, 0.25f, 0.85f), toList));
            if (a.Unlocked.Count > 0) buttons.Add(("THE VAULT", new Color(0.85f, 0.45f, 0.1f), () => LockerPage.Open()));
            for (var i = 0; i < buttons.Count; i++)
                PanelKit.Pill(page, "Reward" + buttons[i].label, new Vector2(0.5f, 0), new Vector2((i - (buttons.Count - 1) / 2f) * 375f, 90), new Vector2(340, 120), buttons[i].label, buttons[i].c, buttons[i].act, 32);
            card.StartCoroutine(card.Reveal(page, a, before, xpText, lines, levelText, fill, caption, starImages, unlocks));
            Debug.Log($"[APL modes] reward screen: +{a.Xp} XP, level {a.LevelAfter}, unlocked {a.Unlocked.Count}{(ch != null ? $", {ch.Id} {stars}*" : "")}");
            return card;
        }

        /// <summary>The reveal: stars pop, the XP counts up line by line, the bar fills (and rolls over on a level-up), the unlocks pop in.</summary>
        private IEnumerator Reveal(RectTransform page, Award a, (int level, int into, int size) from, Text xp, List<Text> lines, Text level, RectTransform fill, Text caption, List<Image> stars, List<Transform> unlocks)
        {
            foreach (var l in lines) l.gameObject.SetActive(false);
            foreach (var u in unlocks) u.gameObject.SetActive(false);
            for (var i = 0; i < stars.Count; i++) StartCoroutine(RewardFx.Pop(stars[i].transform, 0.2f + 0.25f * i));
            for (var i = 0; i < lines.Count; i++)
            {
                yield return new WaitForSecondsRealtime(0.18f);
                lines[i].gameObject.SetActive(true);
            }
            var total = Progression.Xp;
            var start = total - a.Xp;
            var shownLevel = from.level;
            const float count = 2.4f;
            for (var e = 0f; e < count; e += Time.unscaledDeltaTime)
            {
                var now = Mathf.RoundToInt(Mathf.Lerp(start, total, Mathf.SmoothStep(0, 1, e / count)));
                var at = Progression.ProgressAt(now);
                xp.text = $"+{now - start} XP";
                // The number breathes as it climbs.
                xp.transform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(e * 18f) * (1f - e / count));
                if (at.level > shownLevel)
                {
                    shownLevel = at.level;
                    StartCoroutine(LevelUpBurst(page, at.level));
                }
                level.text = at.level > from.level ? $"LEVEL UP!  LEVEL {at.level}" : $"LEVEL {at.level}";
                level.color = at.level > from.level ? UiKit.Gold : Color.white;
                RewardFx.Set(fill, BarWidth, at.into / (float)at.size, BarAt);
                caption.text = $"{at.into} / {at.size} XP to level {at.level + 1}";
                yield return null;
            }
            xp.text = $"+{a.Xp} XP";
            xp.transform.localScale = Vector3.one;
            for (var i = 0; i < unlocks.Count; i++)
            {
                unlocks[i].gameObject.SetActive(true);
                yield return RewardFx.Pop(unlocks[i], 0.15f);
            }
        }

        /// <summary>LEVEL UP across the middle of the screen: pops in gold, holds, fades.</summary>
        private IEnumerator LevelUpBurst(RectTransform page, int level)
        {
            var glow = PlayerCardKit.Shape(page, "LevelUpGlow", CardSprites.Glow(60, 80), new Color(1f, 0.75f, 0.2f, 0.55f), new Vector2(0, 40), new Vector2(1100, 300));
            var text = PlayerCardKit.Text(page, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-560, -40), new Vector2(560, 120), $"LEVEL UP!  {level}", 110, TextAnchor.MiddleCenter, RewardFx.StarOn, condensed: true);
            Debug.Log($"[APL modes] reward reveal: LEVEL UP to {level}");
            yield return RewardFx.Pop(text.transform.parent, 0f);
            yield return new WaitForSecondsRealtime(1.2f);
            for (var e = 0f; e < 0.5f; e += Time.unscaledDeltaTime)
            {
                var k = 1f - e / 0.5f;
                text.color = new Color(text.color.r, text.color.g, text.color.b, k);
                glow.color = new Color(1f, 0.75f, 0.2f, 0.55f * k);
                yield return null;
            }
            Destroy(glow.gameObject);
            Destroy(text.transform.parent.gameObject);
        }

        /// <summary>"LEVEL 4: CARBON (BAT)" — the next thing a level brings; null when all is unlocked (the line is hidden).</summary>
        private static string? NextUnlock()
        {
            foreach (var c in Cosmetics.All.OrderBy(k => k.Level))
                if (!Progression.Unlocked(c) && c.Level > 0) return $"LEVEL {c.Level}: {c.Name} ({LockerPage.SlotName(c.Slot)}) · {c.Level - Progression.Level} to go";
            return null;
        }

        /// <summary>The auction summary rows (UI/UX dev r1 §8): only the ones with something to say.</summary>
        private static List<(string label, string value)> SummaryRows(AuctionGoalFacts.Summary s)
        {
            var rows = new List<(string, string)>
            {
                ("PLAYERS BOUGHT", $"{s.Bought}  ·  squad of {s.SquadSize}"),
                ("SPENT  ·  LEFT", $"{Apl.Core.Money.Format(s.Spent)}  ·  {Apl.Core.Money.Format(s.PurseLeft)}"),
            };
            if (s.BiggestBuy != null) rows.Add(("BIGGEST BUY", s.BiggestBuy));
            if (s.BestSteal != null) rows.Add(("BEST STEAL", s.BestSteal));
            if (s.Rank > 0) rows.Add(("AUCTION POINTS", $"{s.Points:N0} pts  ·  #{s.Rank} of {s.Teams}"));
            if (s.GoalsDone.Count > 0) rows.Add(("GOALS DONE", string.Join("  ·  ", s.GoalsDone)));
            return rows;
        }
    }
}
