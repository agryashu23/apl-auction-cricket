using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Modes
{
    /// <summary>
    /// The modes' way in from the home page (one hook call in MainMenu.Home): under
    /// the mode cards, a CHALLENGES pill with the stars won and a LEVEL chip with the
    /// XP bar — both open the challenges page (its left column is the career).
    /// </summary>
    public static class ModesEntry
    {
        public static void AddToHome(RectTransform page, Dataset data)
        {
            PanelKit.Pill(page, "Challenges", new Vector2(0.5f, 0), new Vector2(-640, 80), new Vector2(560, 120),
                $"CHALLENGES  ·  {Progression.TotalStars}/{Challenges.MaxStars} STARS", new Color(0.85f, 0.45f, 0.1f), () => ChallengesPage.Open(data), 32);
            var chip = PanelKit.GlassPanel(page, "Level", new Vector2(0.5f, 0), new Vector2(-150, 80), new Vector2(380, 120));
            PlayerCardKit.Text(chip, new Vector2(0, 0.45f), new Vector2(1, 1), new Vector2(20, 0), new Vector2(-20, -6), $"LEVEL {Progression.Level}  ·  {Difficulty.Label(Difficulty.Current)}", 30, TextAnchor.MiddleCenter, UiKit.Gold, condensed: true);
            var (into, size) = Progression.LevelProgress();
            PlayerCardKit.Shape(chip, "Track", CardSprites.Round(8), new Color(1, 1, 1, 0.12f), new Vector2(0, 30), new Vector2(320, 14), new Vector2(0.5f, 0));
            var k = Mathf.Clamp01(into / (float)Mathf.Max(1, size));
            if (k > 0.01f) PlayerCardKit.Shape(chip, "Fill", CardSprites.Round(8), UiKit.Gold, new Vector2(-160 * (1 - k), 30), new Vector2(320 * k, 14), new Vector2(0.5f, 0));
            var hit = chip.gameObject.AddComponent<UnityEngine.UI.Button>();
            chip.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            hit.onClick.AddListener(() => ChallengesPage.Open(data));
        }

        /// <summary>Back on the home page: the challenges page after a challenge's CHALLENGES button, and any reward not yet seen.</summary>
        public static void ReturnIfAsked(Dataset data)
        {
            if (ChallengeLaunch.ReturnToList)
            {
                ChallengeLaunch.ReturnToList = false;
                ChallengesPage.Open(data);
            }
            // A reward the match was left before seeing (MENU before CONTINUE), or the auction's: on its own screen.
            RewardCard.ShowPending();
        }
    }
}
