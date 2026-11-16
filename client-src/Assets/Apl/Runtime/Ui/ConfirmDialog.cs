using System;
using UnityEngine;

namespace Apl.Client.Ui
{
    /// <summary>
    /// A yes / no question over everything (owner v26 #21: "Quit the match?" QUIT / CANCEL):
    /// its own canvas (sorting order 95, above the pause menu and the sound panel), the screen
    /// dimmed behind it so nothing else can be pressed, a glass card with the question, one
    /// line under it, and two big pills — the action (red) and CANCEL. Works while the match
    /// is paused (UI runs on unscaled time). Only one is up at a time.
    /// Buttons are named "ConfirmOk" / "ConfirmCancel" for the test runs.
    /// </summary>
    public static class ConfirmDialog
    {
        private const string Name = "ConfirmDialog";

        /// <summary>True while a dialog is up.</summary>
        public static bool Open => GameObject.Find(Name) != null;

        public static void Show(string question, string detail, string ok, string cancel, Action onOk, Action? onCancel = null)
        {
            var stale = GameObject.Find(Name);
            if (stale != null) UnityEngine.Object.Destroy(stale);
            var canvas = UiKit.Canvas(Name, 95);
            var go = canvas.gameObject;
            var dim = PanelKit.Backdrop((RectTransform)canvas.transform, 0.75f);
            var card = PanelKit.GlassPanel(dim, "Card", new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(980, 520));
            // Opaque (UI/UX dev r1 audit): the question must not have the hall reading through it.
            card.GetComponent<UnityEngine.UI.Image>().color = UiTheme.Panel;
            PlayerCardKit.Text(card, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -170), new Vector2(-40, -50), question, 64, TextAnchor.MiddleCenter, Color.white, condensed: true);
            if (detail.Length > 0)
            {
                var t = PlayerCardKit.Text(card, new Vector2(0, 1), new Vector2(1, 1), new Vector2(60, -260), new Vector2(-60, -180), detail, 28, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
                t.horizontalOverflow = UnityEngine.HorizontalWrapMode.Wrap;
            }
            var done = false;
            void Close()
            {
                done = true;
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            PanelKit.Pill(card, "ConfirmOk", new Vector2(0.5f, 0), new Vector2(-220, 120), new Vector2(380, 130), ok, new Color(0.78f, 0.2f, 0.2f, 0.98f), () =>
            {
                if (done) return;
                Close();
                onOk();
            }, 44);
            PanelKit.Pill(card, "ConfirmCancel", new Vector2(0.5f, 0), new Vector2(220, 120), new Vector2(380, 130), cancel, new Color(0.12f, 0.14f, 0.22f, 0.98f), () =>
            {
                if (done) return;
                Close();
                onCancel?.Invoke();
            }, 44);
        }
    }
}
