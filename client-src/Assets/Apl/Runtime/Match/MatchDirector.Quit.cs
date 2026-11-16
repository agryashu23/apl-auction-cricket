using Apl.Client.Ui;
using UnityEngine.SceneManagement;

namespace Apl.Client.Match
{
    /// <summary>
    /// Quitting a match (owner v26 #21): every way out to the menu — the pause menu's QUIT TO MENU,
    /// the result card's and the after-match page's MENU, the Super Over card's MENU (all the HUD's
    /// MenuPressed) — asks "QUIT THE MATCH?" with QUIT / CANCEL first (Ui/ConfirmDialog.cs).
    /// CANCEL leaves everything as it was (the pause menu stays up, still paused).
    /// </summary>
    public sealed partial class MatchDirector
    {
        private void ConfirmQuit()
        {
            var detail = _match != null && !_match.Finished
                ? "The match stops here and you go back to the main menu."
                : "You'll go back to the main menu.";
            ConfirmDialog.Show("QUIT THE MATCH?", detail, "QUIT", "CANCEL", () => SceneManager.LoadScene(0));
        }
    }
}
