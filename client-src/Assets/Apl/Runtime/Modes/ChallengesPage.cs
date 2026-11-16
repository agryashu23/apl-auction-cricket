using System;
using Apl.Client.Menu;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Modes
{
    /// <summary>
    /// CHALLENGES (from the home page): twelve death-overs scenarios as cards — the
    /// situation, a line, BAT or BOWL, the stars earned and what the next two need.
    /// Owner v26 #12: tapping a card SELECTS it (gold rim) and shows it in the left
    /// column (ChallengesPage.Detail.cs: goal, star conditions, reward, best result,
    /// PLAY); PLAY starts it. The left column's CAREER tab is the old career panel
    /// (level, XP, difficulty, goals, locker).
    /// </summary>
    public sealed partial class ChallengesPage : MonoBehaviour
    {
        private Dataset _data = null!;
        private RectTransform _root = null!;

        public static ChallengesPage Open(Dataset data)
        {
            // One challenges page at a time (a second open replaces the first, never stacks on it).
            foreach (var old in FindObjectsByType<ChallengesPage>(FindObjectsSortMode.None)) Destroy(old.gameObject);
            var canvas = UiKit.Canvas("Challenges", 40);
            var page = canvas.gameObject.AddComponent<ChallengesPage>();
            page._data = data;
            page._root = (RectTransform)canvas.transform;
            var (mine, opp) = MatchMenu.SavedTeams(data);
            ChallengeLaunch.MyTeam = mine;
            ChallengeLaunch.Opponent = opp;
            page.Build();
            return page;
        }

        private void Build()
        {
            // The old page goes at once (Destroy waits for the frame's end: both would draw).
            foreach (Transform c in _root) c.gameObject.SetActive(false);
            UiKit.Clear(_root);
            var bg = UiKit.Stretch(_root, "Page", new Color(0.02f, 0.03f, 0.07f, 1f));
            PlayerCardKit.Text(bg, new Vector2(0, 1), new Vector2(1, 1), new Vector2(60, -110), new Vector2(-60, -20), "CHALLENGES", 60, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            var my = Team(ChallengeLaunch.MyTeam);
            var opp = Team(ChallengeLaunch.Opponent);
            PlayerCardKit.Text(bg, new Vector2(0, 1), new Vector2(1, 1), new Vector2(420, -110), new Vector2(-560, -20),
                $"{my?.ShortName ?? ChallengeLaunch.MyTeam} v {opp?.ShortName ?? ChallengeLaunch.Opponent}  ·  the last overs of a 20-over game  ·  STARS {Progression.TotalStars} / {Challenges.MaxStars}",
                26, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
            PanelKit.Pill(bg, "Back", new Vector2(1, 1), new Vector2(-150, -70), new Vector2(240, 120), "◄ BACK", new Color(0.12f, 0.14f, 0.22f, 0.95f), () => Destroy(gameObject), 30);
            BuildLeft(bg);
            // The cards: four across, three down, right of the career panel.
            const float w = 300, h = 262, gap = 14;
            for (var i = 0; i < Challenges.All.Length; i++)
            {
                var col = i % 4;
                var row = i / 4;
                var pos = new Vector2(-350 + col * (w + gap) + w / 2, -170 - row * (h + gap) - h / 2);
                Card(bg, Challenges.All[i], new Vector2(0.5f, 1), pos, new Vector2(w, h));
            }
        }

        private Franchise? Team(string id) => _data.Franchises.Find(f => f.Id == id);

        private void Card(RectTransform parent, Challenge ch, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var stars = Progression.StarsFor(ch.Id);
            var colour = ch.UserBats ? new Color(0.15f, 0.55f, 0.3f) : new Color(0.15f, 0.4f, 0.85f);
            // The rim: gold-bright and thicker when selected, star-gold when all three are won.
            var rim = PlayerCardKit.Shape(parent, "Rim_" + ch.Id, CardSprites.Round(24), RimColour(ch.Id, stars), pos, size + new Vector2(4, 4), anchor);
            _rims[ch.Id] = (rim, stars, size);
            if (ch.Id == _selected?.Id) ((RectTransform)rim.transform).sizeDelta = size + new Vector2(12, 12);
            var face = PlayerCardKit.Shape(parent, "Card_" + ch.Id, CardSprites.Round(20), new Color(0.06f, 0.07f, 0.13f, 0.97f), pos, size, anchor);
            face.raycastTarget = true;
            var rt = (RectTransform)face.transform;
            face.gameObject.AddComponent<Button>().onClick.AddListener(() => Select(ch));
            // The BAT / BOWL tag in its colour across the top.
            var tag = PlayerCardKit.Shape(rt, "Tag", CardSprites.Round(14), colour, new Vector2(0, -22), new Vector2(size.x - 16, 36), new Vector2(0.5f, 1));
            PlayerCardKit.Text((RectTransform)tag.transform, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-12, 0), ch.UserBats ? "YOU BAT" : "YOU BOWL", 20, TextAnchor.MiddleLeft, Color.white, condensed: true);
            PlayerCardKit.Text((RectTransform)tag.transform, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-12, 0), $"{ch.Need} OFF {ch.Balls}", 20, TextAnchor.MiddleRight, Color.white, condensed: true);
            PlayerCardKit.Text(rt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -96), new Vector2(-14, -46), ch.Title.ToUpperInvariant(), 30, TextAnchor.MiddleLeft, Color.white, condensed: true);
            var brief = PlayerCardKit.Text(rt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -150), new Vector2(-14, -96), ch.Brief, 19, TextAnchor.UpperLeft, UiKit.Dim, bold: false);
            brief.horizontalOverflow = HorizontalWrapMode.Wrap;
            RewardCard.StarRow(rt, new Vector2(-size.x / 2 + 70, 74), stars, 34, new Vector2(0.5f, 0));
            // What the stars want: the next one not yet earned.
            var next = stars == 0 ? "1 STAR: win it" : stars == 1 ? "2 STARS: " + ch.TwoStar : stars == 2 ? "3 STARS: " + ch.ThreeStar : "All three stars!";
            var want = PlayerCardKit.Text(rt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(14, 8), new Vector2(-14, 50), next, 18, TextAnchor.MiddleLeft, stars == 3 ? RewardCard.StarOn : UiKit.Dim, bold: false);
            want.horizontalOverflow = HorizontalWrapMode.Wrap;
        }
    }
}
