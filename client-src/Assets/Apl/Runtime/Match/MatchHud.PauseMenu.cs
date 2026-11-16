using System;
using System.Collections.Generic;
using Apl.Client.Menu;
using Apl.Client.Ui;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The pause menu (owner v26 #14: "too cluttered"): a list on its own canvas over everything —
    /// SOUND SETTINGS (the sound panel), GAMEPLAY SETTINGS (controls, difficulty, footwork,
    /// running, haptics: Menu/GameSettings.cs, the same page the home gear opens), MATCH
    /// (scorecard, wagon wheel, field / timeout / impact sub when on offer, and the SIMULATE
    /// choices: MatchHud.Sim.cs), then RESUME and QUIT TO MENU (which asks first: owner v26 #21,
    /// the director's MenuPressed handler). Each sub-page has ◄ BACK to the list. The HUD
    /// beneath is hidden while it's up (PauseCover). Button names (Pause_Sound, Pause_Gameplay,
    /// Pause_Match, Back, Resume, QuitToMenu) are what the test runs click.
    /// </summary>
    public sealed partial class MatchHud
    {
        /// <summary>The page currently on the pause canvas (rebuilt on each move between pages).</summary>
        private RectTransform? _pausePage;

        private void BuildPauseMenu()
        {
            var canvas = UiKit.Canvas("PauseMenu", 45);
            _pauseMenu = canvas.gameObject;
            // The HUD under it is switched off while paused (PauseCover), and the match is dimmed hard,
            // so nothing (joystick, shot buttons, timing bar) shows through the glass.
            canvas.gameObject.AddComponent<PauseCover>();
            PauseList();
        }

        /// <summary>Clears the pause canvas for a page: the dim backdrop and the PAUSED header.</summary>
        private RectTransform PausePage(string subtitle)
        {
            if (_pausePage != null) Destroy(_pausePage.gameObject);
            _pausePage = PanelKit.Backdrop((RectTransform)_pauseMenu!.transform, 0.9f);
            PanelKit.Header(_pausePage, "PAUSED", subtitle, null, null);
            return _pausePage;
        }

        /// <summary>The top list: SOUND / GAMEPLAY / MATCH, a gap, RESUME / QUIT TO MENU.</summary>
        private void PauseList()
        {
            if (_pauseMenu == null) return;
            var page = PausePage("The match is on hold");
            GameSettings.ListPanel(page, new (string, string, Color, Action)?[]
            {
                ("Pause_Sound", "SOUND SETTINGS", GameSettings.ListFill, () => Apl.Client.Audio.SoundSettings.Open()),
                ("Pause_Gameplay", "GAMEPLAY SETTINGS", GameSettings.ListFill, PauseGameplay),
                ("Pause_Match", "MATCH", GameSettings.ListFill, PauseMatch),
                null,
                ("Resume", "RESUME ►", UiKit.Accent, () => SetPaused(false)),
                // Stays paused under the "Quit the match?" question; CANCEL leaves the menu as it was.
                ("QuitToMenu", "QUIT TO MENU", new Color(0.35f, 0.1f, 0.12f, 0.96f), () => MenuPressed?.Invoke()),
            });
        }

        private void PauseGameplay()
        {
            if (_pauseMenu == null) return;
            var page = PausePage("Gameplay settings");
            GameSettings.GameplayPage(page, true, PauseList, ApplyFootwork);
        }

        /// <summary>MATCH: SCORECARD and WAGON WHEEL, the rules row when on offer, then SIMULATE.</summary>
        private void PauseMatch()
        {
            if (_pauseMenu == null) return;
            var page = PausePage("Match");
            var panel = GameSettings.SubPanel(page, "MATCH", new Vector2(1240, 860), PauseList);
            var y = -200f;
            PanelKit.Pill(panel, "Tool_SCORECARD", new Vector2(0.5f, 1), new Vector2(-280, y), new Vector2(520, 124), "SCORECARD", Ribbon2, ShowScorecard, 36);
            PanelKit.Pill(panel, "Tool_WAGON WHEEL", new Vector2(0.5f, 1), new Vector2(280, y), new Vector2(520, 124), "WAGON WHEEL", Ribbon2, () =>
            {
                var wheels = Wagons?.Invoke();
                if (ShowWagonExplorer()) return;
                if (wheels != null && wheels.Count > 0) ShowWagons(wheels, 0);
            }, 36);
            y -= 160f;
            var o = RulesOptions?.Invoke();
            if (o != null)
            {
                var rules = new List<(string, Action?)>();
                if (o.Value.field) rules.Add(("FIELD", FieldMenu));
                if (o.Value.timeout) rules.Add(("TIMEOUT", TimeoutMenu));
                if (o.Value.impact) rules.Add(("IMPACT SUB", ImpactMenu));
                if (rules.Count > 0)
                {
                    const float rowW = 1080f, gap = 20f;
                    var w = (rowW - gap * (rules.Count - 1)) / rules.Count;
                    for (var i = 0; i < rules.Count; i++)
                    {
                        var (label, act) = rules[i];
                        PanelKit.Pill(panel, "Rules_" + label, new Vector2(0.5f, 1), new Vector2(-rowW / 2 + w / 2 + i * (w + gap), y), new Vector2(w, 124), label, UiKit.Accent, () =>
                        {
                            SetPaused(false);
                            act?.Invoke();
                        }, 32);
                    }
                    y -= 160f;
                }
            }
            SimRow(panel, y);
        }
    }
}
