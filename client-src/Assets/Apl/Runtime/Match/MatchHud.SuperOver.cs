using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>The Super Over's ceremony card (the tie, the rules) and its result card.</summary>
    public sealed partial class MatchHud
    {
        public void ShowSuperOverCard(QuickMatch main, Franchise user, Franchise opp, bool userBatsFirst)
        {
            var card = NewCard("SuperOver", 700);
            UiKit.LabelAt(card, new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(1000, 90), "SUPER OVER", 84, TextAnchor.MiddleCenter, UiKit.Gold);
            var first = main.FirstInnings;
            UiKit.LabelAt(card, new Vector2(0.5f, 1), new Vector2(0, -160), new Vector2(1000, 44),
                $"MATCH TIED  ·  {first.Runs}/{first.Wickets} each", 34, TextAnchor.MiddleCenter, Color.white);
            UiKit.Crest(card, user.Id, new Vector2(0.5f, 0.5f), new Vector2(-330, 40), 140);
            UiKit.Crest(card, opp.Id, new Vector2(0.5f, 0.5f), new Vector2(330, 40), 140);
            UiKit.LabelAt(card, new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(300, 60), "VS", 48, TextAnchor.MiddleCenter, UiKit.Dim);
            var rules = "One over each  ·  three batters (two wickets and it's over)  ·  one bowler\n"
                + $"{(userBatsFirst ? user : opp).ShortName} bat first (they batted second)\n"
                + "Tied again: the side with more boundaries in the match and the Super Over wins";
            UiKit.LabelAt(card, new Vector2(0.5f, 0), new Vector2(0, 230), new Vector2(1040, 120), rules, 24, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            UiKit.Btn(card, "SuperOverGo", new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(560, 104), "PICK YOUR SUPER OVER ►", UiKit.Accent, () => StartPressed?.Invoke(), 34);
        }

        public void ShowSuperOverResult(SuperOver so, Franchise user, Franchise opp)
        {
            var card = NewCard("SuperOverResult", 640);
            var w = so.Winner;
            var winner = w == null ? null : w.TeamId == user.Id ? user : opp;
            if (winner != null) UiKit.Crest(card, winner.Id, new Vector2(0.5f, 1), new Vector2(0, -110), 150);
            var text = so.ResultText();
            if (winner != null) text = text.Replace(winner.Id, winner.ShortName);
            UiKit.LabelAt(card, new Vector2(0.5f, 1), new Vector2(0, -230), new Vector2(1040, 80), text.ToUpperInvariant(), 50, TextAnchor.MiddleCenter,
                w == null ? UiKit.Gold : w == so.Main.UserTeam ? UiKit.Good : UiKit.Bad);
            var m = so.Match;
            var first = m.FirstInnings;
            string Name(string id) => id == user.Id ? user.ShortName : opp.ShortName;
            var line = $"SUPER OVER  {Name(first.BattingTeamId)} {first.Runs}/{first.Wickets}  ·  {Name(m.Batting.TeamId)} {m.Runs}/{m.Wickets}";
            if (so.OnBoundaries) line += $"\nBOUNDARIES  {user.ShortName} {so.Boundaries(user.Id)}  ·  {opp.ShortName} {so.Boundaries(opp.Id)}";
            UiKit.LabelAt(card, new Vector2(0.5f, 1), new Vector2(0, -320), new Vector2(1040, 90), line, 28, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            // On to the rewards' own screen (MatchHud.ResultCard.cs), as after any result.
            PanelKit.Pill(card, "Continue", new Vector2(0.5f, 0), new Vector2(0, 205), new Vector2(480, 110), "CONTINUE ►", UiKit.Accent, ContinueResult, 40);
            if (AutoContinue != null && AutoContinue()) StartCoroutine(ContinueLater(8f));
            UiKit.Btn(card, "PlayAgain", new Vector2(0.5f, 0), new Vector2(-230, 90), new Vector2(400, 104), "PLAY AGAIN", UiKit.Accent, () => PlayAgainPressed?.Invoke(), 36);
            UiKit.Btn(card, "MatchMenu", new Vector2(0.5f, 0), new Vector2(230, 90), new Vector2(400, 104), "MENU", UiKit.Muted, () => MenuPressed?.Invoke(), 36);
        }
    }
}
