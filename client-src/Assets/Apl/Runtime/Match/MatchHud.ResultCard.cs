using System;
using System.Collections;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The result screen in the card kit (round 8): a banner in the winner's
    /// colours with the crest, the side's name and the margin; both innings'
    /// scores (the winner's lit gold); three tiles — PLAYER OF THE MATCH, TOP
    /// BATTER, TOP BOWLER — each with the player's card and figures; PLAY AGAIN
    /// and MENU. Used by the quick chase, the full match (ShowResult /
    /// ShowMatchResult hook here).
    /// </summary>
    public sealed partial class MatchHud
    {
        /// <summary>The director's Player of the Match (MatchDirector.PlayerOfMatch.cs): one choice for finale and card.</summary>
        public Func<Apl.Core.Player?>? PlayerOfMatchSource;

        /// <summary>The result screen's CONTINUE: the reward screen may come up now (MatchDirector.Modes.cs waits on it).</summary>
        public event Action? ContinuePressed;

        /// <summary>CONTINUE has been pressed on the result (or the super over's result): rewards may show.</summary>
        public bool ResultContinued { get; private set; }

        /// <summary>Test runs (the director's Autoplay): go on by themselves a few seconds after the result.</summary>
        public Func<bool>? AutoContinue;

        private IEnumerator ContinueLater(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            ContinueResult();
        }

        /// <summary>
        /// Off the result to the after-match page: an opaque screen of its own (the reward card is drawn
        /// over it by the modes code), with PLAY AGAIN and MENU along the foot once the rewards are closed.
        /// </summary>
        private void ContinueResult()
        {
            if (ResultContinued) return;
            ResultContinued = true;
            ClearStage();
            if (_card != null) Destroy(_card);
            var page = UiKit.Stretch(_root, "AfterMatch", new Color(0.015f, 0.018f, 0.045f, 1f));
            _card = page.gameObject;
            PlayerCardKit.Shape(page, "Glow", CardSprites.Glow(60, 160), UiKit.WithAlpha(UiKit.Gold, 0.12f), new Vector2(0, 60), new Vector2(1400, 700));
            PlayerCardKit.Text(page, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-600, -150), new Vector2(600, -60), "AFTER THE MATCH", 56, TextAnchor.MiddleCenter, UiKit.Gold, condensed: true);
            PanelKit.Pill(page, "PlayAgain", Centre, new Vector2(-230, -420), new Vector2(400, 120), "PLAY AGAIN ►", UiKit.Accent, () => PlayAgainPressed?.Invoke(), 40);
            PanelKit.Pill(page, "MatchMenu", Centre, new Vector2(230, -420), new Vector2(400, 120), "MENU", new Color(0.12f, 0.14f, 0.22f, 0.95f), () => MenuPressed?.Invoke(), 40);
            ContinuePressed?.Invoke();
        }

        /// <summary>The result screen for a finished chase or full match (sides <paramref name="a"/> and <paramref name="b"/> in any order).</summary>
        private void ResultFor(QuickMatch m, Franchise a, Franchise b)
        {
            var w = m.Winner;
            var text = m.MatchResultText();
            // "CHC won by 10 wickets (4 balls left)" → "won by 10 wickets (4 balls left)".
            var margin = w != null && text.StartsWith(w.TeamId + " ") ? text.Substring(w.TeamId.Length + 1) : text;
            if (w == null && margin.Length == 0) margin = "Match tied";
            ResultScreen(m, a, b, w?.TeamId, margin, w == null ? (bool?)null : m.UserWon);
        }

        /// <param name="winnerId">The winning side, or null for a tie.</param>
        /// <param name="margin">"WON BY 10 WICKETS (4 BALLS LEFT)" style, without the side's name.</param>
        /// <param name="userWon">From the user's side when one side is the user's (a VICTORY / DEFEAT tag), else null.</param>
        private void ResultScreen(QuickMatch m, Franchise a, Franchise b, string? winnerId, string margin, bool? userWon)
        {
            if (_card != null) Destroy(_card);
            // The finale's graphics are done with: nothing else on screen with the result.
            ClearStage();
            var page = UiKit.Stretch(_root, "ResultScreen", new Color(0.01f, 0.012f, 0.03f, 0.88f));
            _card = page.gameObject;
            Franchise Side(string id) => id == a.Id ? a : b;
            var winner = winnerId != null ? Side(winnerId) : null;

            // The banner.
            var kit = winner != null ? PlayerCardKit.Bright(UiKit.ParseHex(winner.PrimaryColor)) : UiKit.Gold;
            var banner = PlayerCardKit.Shape(page, "Banner", CardSprites.Round(24), Color.Lerp(kit, Color.black, 0.45f), new Vector2(0, 330), new Vector2(1560, 200));
            PlayerCardKit.Shape((RectTransform)banner.transform, "Band", CardSprites.Round(4), kit, new Vector2(0, 4), new Vector2(1500, 6), new Vector2(0.5f, 0));
            if (winner != null) UiKit.Crest(page, winner.Id, Centre, new Vector2(-660, 330), 170);
            var title = winner != null ? winner.Name.ToUpperInvariant() : "MATCH TIED";
            PlayerCardKit.Text(page, Centre, Centre, new Vector2(-540, 336), new Vector2(560, 424), title, 76, TextAnchor.MiddleLeft, Color.white, condensed: true);
            PlayerCardKit.Text(page, Centre, Centre, new Vector2(-540, 252), new Vector2(560, 330), margin.ToUpperInvariant(), 40, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            if (userWon != null)
            {
                var tag = PlayerCardKit.Shape(page, "Verdict", CardSprites.Round(20), userWon.Value ? UiKit.Gold : new Color(0.14f, 0.16f, 0.25f, 0.95f), new Vector2(630, 330), new Vector2(240, 84));
                PlayerCardKit.Text((RectTransform)tag.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, userWon.Value ? "VICTORY" : "DEFEAT", 42, TextAnchor.MiddleCenter, userWon.Value ? UiKit.Ink : Color.white, condensed: true);
            }

            // Both innings.
            var first = m.FirstInnings;
            ScorePanel(page, Side(first.BattingTeamId), $"{first.Runs}/{first.Wickets}", first.Overs, -400, first.BattingTeamId == winnerId);
            ScorePanel(page, Side(m.Batting.TeamId), $"{m.Runs}/{m.Wickets}", Cricket.FormatOvers(m.LegalBalls), 400, m.Batting.TeamId == winnerId);

            // The players of the day.
            var all = Performers(m);
            // The director's Player of the Match (the one the finale crowned), else this card's own pick.
            var crowned = PlayerOfMatchSource?.Invoke();
            var potm = (crowned != null ? all.Find(x => x.Player.Id == crowned.Id) : null) ?? PlayerOfTheMatch(all, winnerId);
            var bat = TopBatter(all);
            var bowl = TopBowler(all);
            StarTile(page, "PLAYER OF THE MATCH", potm, potm?.Figures, -520, Side, true);
            StarTile(page, "TOP BATTER", bat, bat?.BatLine, 0, Side, false);
            StarTile(page, "TOP BOWLER", bowl, bowl?.BowlLine, 520, Side, false);

            // CONTINUE goes on to the rewards (its own screen, never over the match); PLAY AGAIN / MENU skip them.
            PanelKit.Pill(page, "Continue", Centre, new Vector2(0, -400), new Vector2(480, 120), "CONTINUE ►", UiKit.Accent, ContinueResult, 44);
            PanelKit.Pill(page, "PlayAgain", Centre, new Vector2(-500, -400), new Vector2(360, 120), "PLAY AGAIN", new Color(0.12f, 0.14f, 0.22f, 0.95f), () => PlayAgainPressed?.Invoke(), 36);
            PanelKit.Pill(page, "MatchMenu", Centre, new Vector2(500, -400), new Vector2(360, 120), "MENU", new Color(0.12f, 0.14f, 0.22f, 0.95f), () => MenuPressed?.Invoke(), 36);
            // Test runs: AutoShot presses CONTINUE itself after its result shot; this is the fallback.
            if (AutoContinue != null && AutoContinue()) StartCoroutine(ContinueLater(8f));
            if (Debug.isDebugBuild) Debug.Log($"[APL result] {title} {margin}; POTM {potm?.Player.Name} {potm?.Figures}");
        }

        /// <summary>One innings: crest, the side's short name, the score big (gold for the winners), the overs.</summary>
        private static void ScorePanel(RectTransform page, Franchise team, string score, string overs, float x, bool won)
        {
            var panel = PanelKit.GlassPanel(page, "Score_" + team.Id, Centre, new Vector2(x, 110), new Vector2(740, 190));
            if (won) PlayerCardKit.Shape(panel, "WinRim", CardSprites.Round(24), UiKit.WithAlpha(UiKit.Gold, 0.9f), new Vector2(0, -6), new Vector2(680, 6), new Vector2(0.5f, 1));
            UiKit.Crest(panel, team.Id, new Vector2(0, 0.5f), new Vector2(95, 0), 120);
            PlayerCardKit.Text(panel, new Vector2(0, 0), new Vector2(0, 1), new Vector2(175, 0), new Vector2(420, 0), team.ShortName, 52, TextAnchor.MiddleLeft, Color.white, condensed: true);
            PlayerCardKit.Text(panel, new Vector2(1, 0.3f), new Vector2(1, 1), new Vector2(-330, 0), new Vector2(-30, -10), score, 84, TextAnchor.MiddleRight, won ? UiKit.Gold : Color.white, condensed: true);
            PlayerCardKit.Text(panel, new Vector2(1, 0), new Vector2(1, 0.32f), new Vector2(-330, 10), new Vector2(-30, 0), $"{overs} OVERS", 24, TextAnchor.MiddleRight, UiKit.Dim, condensed: true);
        }

        /// <summary>A tile for one of the day's best: heading, the player's card, his name and figures.</summary>
        private static void StarTile(RectTransform page, string heading, Performer? who, string? figures, float x, System.Func<string, Franchise> side, bool lead)
        {
            var tile = PanelKit.GlassPanel(page, "Star_" + heading, Centre, new Vector2(x, -150), new Vector2(480, 250));
            PlayerCardKit.Text(tile, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -50), new Vector2(-24, -10), heading, 28, TextAnchor.MiddleLeft, lead ? UiKit.Gold : UiKit.Dim, condensed: true);
            if (who == null)
            {
                PlayerCardKit.Text(tile, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, "—", 40, TextAnchor.MiddleCenter, UiKit.Dim, condensed: true);
                return;
            }
            var team = side(who.TeamId);
            var card = PlayerCardKit.Card(tile, who.Player, team, new Vector2(0, 0.5f), new Vector2(96, -22), new Vector2(132, 180), delay: lead ? 0.1f : 0.25f);
            // Fully drawn from the start (r9 f443: the fade-in read as half-faded portraits).
            card.Motion.FromAlpha = 1f;
            var (firstName, last) = PlayerCardKit.Split(who.Player.Name);
            PlayerCardKit.Text(tile, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(180, 18), new Vector2(-16, 46), firstName.ToUpperInvariant(), 22, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
            PlayerCardKit.Text(tile, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(180, -26), new Vector2(-16, 20), last.ToUpperInvariant(), 40, TextAnchor.MiddleLeft, Color.white, condensed: true);
            PlayerCardKit.Text(tile, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(180, -70), new Vector2(-16, -28), figures ?? "", 30, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            PlayerCardKit.Text(tile, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(180, -104), new Vector2(-16, -72), team.ShortName, 22, TextAnchor.MiddleLeft, UiKit.Dim, condensed: true);
        }
    }
}
