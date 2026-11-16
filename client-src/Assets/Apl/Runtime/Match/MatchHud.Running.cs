using System;
using Apl.Client.Ui;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Manual running (the user batting, RUNNING: MANUAL): three chips bottom
    /// right under the thumb — RUN (go for one more), CANCEL (don't go / turn back
    /// / stop at the next crease), DIVE (lit on the final approach) — and the call
    /// in big letters above them: "1 RUN · SAFE", "GOING FOR 2 · RISKY", "BACK!",
    /// "DIVE!". Keys: R = run, X = cancel, D = dive.
    /// </summary>
    public sealed partial class MatchHud
    {
        public event Action? RunPressed, CancelPressed, DivePressed;

        private GameObject? _runRoot;
        private UnityEngine.UI.Text _runHint = null!;
        private HudChip _runChip = null!, _diveChip = null!;
        private CanvasGroup _diveGroup = null!;

        private static readonly Color RunGreen = new Color(0.3f, 0.85f, 0.45f), CancelRed = new Color(0.95f, 0.35f, 0.3f), DiveGold = new Color(0.98f, 0.78f, 0.25f);

        private bool _runHidPlay;

        public void ShowRunControls(bool on)
        {
            if (_runRoot == null) BuildRunControls();
            _runRoot!.SetActive(on);
            // While they run only RUN / CANCEL / DIVE: the shot buttons, aim stick and swipe pad go.
            if (on)
            {
                _runHidPlay = _play.activeSelf;
                _play.SetActive(false);
            }
            else if (_runHidPlay)
            {
                _runHidPlay = false;
                _play.SetActive(RoleAllowsPlay("run controls off"));
            }
            if (on) SetDive(false, false);
        }

        private void BuildRunControls()
        {
            var root = UiKit.Box(_root, "RunControls", new Vector2(1, 0), new Vector2(-260, 330), new Vector2(480, 330), new Color(0, 0, 0, 0));
            root.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            _runRoot = root.gameObject;
            var size = new Vector2(210, 100);
            _runChip = HudChip.Create(root, "Run", new Vector2(0.5f, 0.5f), new Vector2(115, 40), size, "RUN", RunGreen, 34, () => RunPressed?.Invoke());
            HudChip.Create(root, "Cancel", new Vector2(0.5f, 0.5f), new Vector2(115, -75), size, "CANCEL", CancelRed, 30, () => CancelPressed?.Invoke());
            _diveChip = HudChip.Create(root, "Dive", new Vector2(0.5f, 0.5f), new Vector2(-115, -75), size, "DIVE", DiveGold, 34, () =>
            {
                if (_diveGroup.interactable) DivePressed?.Invoke();
            });
            _diveGroup = _diveChip.gameObject.AddComponent<CanvasGroup>();
            _runHint = UiKit.LabelAt(root, new Vector2(0.5f, 1), new Vector2(0, 10), new Vector2(560, 56), "", 38, TextAnchor.MiddleCenter, UiKit.Gold);
            _runHint.resizeTextForBestFit = false;
        }

        /// <summary>The call over the chips: <paramref name="going"/> runs against <paramref name="safe"/>, tinting RUN by the risk of one more.</summary>
        public void RunHint(int going, int safe, double nextRisk)
        {
            if (_runRoot == null) return;
            var label = going == 0 ? "NO RUN" : going == 1 ? "1 RUN" : $"{going} RUNS";
            var risky = going > safe;
            RunCall(risky ? $"GOING FOR {going} · RISKY" : $"{label} · SAFE", risky ? UiKit.Bad : UiKit.Good);
            _runChip.Selected = nextRisk < 0.15;
        }

        /// <summary>A call in big letters ("BACK!", "DIVE!").</summary>
        public void RunCall(string text, Color color)
        {
            if (_runRoot == null) return;
            _runHint.text = text;
            _runHint.color = color;
        }

        /// <summary>DIVE lit (the final approach); <paramref name="prompt"/>: a dive would save him — it glows and the call says so.</summary>
        public void SetDive(bool lit, bool prompt)
        {
            if (_runRoot == null) return;
            _diveGroup.alpha = lit ? 1f : 0.3f;
            _diveGroup.interactable = lit;
            _diveChip.Selected = lit && prompt;
            if (lit && prompt) RunCall("DIVE!", DiveGold);
        }

        /// <summary>Keyboard RUN / CANCEL / DIVE (desktop), polled by the director while the batters run.</summary>
        public static bool RunKey => Input.GetKeyDown(KeyCode.R);
        public static bool CancelKey => Input.GetKeyDown(KeyCode.X);
        public static bool DiveKey => Input.GetKeyDown(KeyCode.D);
    }
}
