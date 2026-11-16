using System;
using Apl.Client.Audio;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Apl.Client.Hall
{
    /// <summary>
    /// Pause overlay: resume, auction hub, sound settings (the sound panel), quit (confirmed).
    /// Solo freezes the hall (time and audio); a friend room keeps running on
    /// the server.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        public event Action? HubPressed;

        private bool _freezes;
        private Text _note = null!;

        public static PauseMenu Create()
        {
            var canvas = UiKit.Canvas("Pause", 24);
            var menu = canvas.gameObject.AddComponent<PauseMenu>();
            menu.Build(UiKit.Stretch((RectTransform)canvas.transform, "Dim", new Color(0.02f, 0.03f, 0.07f, 0.78f)));
            canvas.gameObject.SetActive(false);
            // The old all-or-nothing switch is gone; MUSIC and VOICE replace it.
            AudioListener.volume = 1f;
            return menu;
        }

        public bool IsOpen => gameObject.activeSelf;

        private void Build(RectTransform root)
        {
            var panel = UiKit.EdgedPanel(root, "PausePanel", new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(620, 690), UiKit.PanelSolid);
            UiKit.LabelAt(panel, new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(560, 64), "PAUSED", 54, TextAnchor.MiddleCenter, UiKit.Gold);
            _note = UiKit.LabelAt(panel, new Vector2(0.5f, 1), new Vector2(0, -112), new Vector2(560, 32), "", 22, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            UiKit.Btn(panel, "Resume", new Vector2(0.5f, 1), new Vector2(0, -200), new Vector2(460, 88), "RESUME ►", UiKit.Accent, Close, 34);
            UiKit.Btn(panel, "PauseHub", new Vector2(0.5f, 1), new Vector2(0, -305), new Vector2(460, 80), "AUCTION HUB", UiKit.Muted, () => HubPressed?.Invoke(), 30);
            // One SOUND SETTINGS entry (owner v26 #14: no loose MUSIC / VOICE switches; the sound panel has them all).
            UiKit.Btn(panel, "PauseSound", new Vector2(0.5f, 1), new Vector2(0, -400), new Vector2(460, 80), "SOUND SETTINGS", UiKit.Muted, () => SoundSettings.Open(), 30);
            // Asks first (owner v26 #21's confirm, Ui/ConfirmDialog.cs): a stray tap shouldn't throw an auction away.
            UiKit.Btn(panel, "Quit", new Vector2(0.5f, 1), new Vector2(0, -560), new Vector2(460, 80), "QUIT TO MENU", new Color(0.35f, 0.1f, 0.12f, 0.96f),
                () => ConfirmDialog.Show("QUIT THE AUCTION?", "You'll go back to the main menu.", "QUIT", "CANCEL", Quit), 30);
        }

        /// <param name="freezes">Solo: stop time and sound while paused.</param>
        public void Open(bool freezes)
        {
            _freezes = freezes;
            _note.text = freezes ? "The auction is on hold." : "Friend room: the auction carries on without you.";
            gameObject.SetActive(true);
            if (freezes)
            {
                Time.timeScale = 0f;
                AudioListener.pause = true;
            }
        }

        public void Close()
        {
            gameObject.SetActive(false);
            if (_freezes)
            {
                Time.timeScale = 1f;
                AudioListener.pause = false;
            }
        }

        private static void Quit()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
