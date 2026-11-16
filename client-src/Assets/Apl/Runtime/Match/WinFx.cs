using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Apl.Client.Match
{
    /// <summary>
    /// The win moment's effects in one call: the fireworks volley over the
    /// stands (<see cref="Fireworks"/>, with its sky flash), confetti in the
    /// winners' colours over the square (<see cref="Confetti"/>), and the whole
    /// crowd up with the wave. Phone-friendly; once per match (scene).
    /// </summary>
    public static class WinFx
    {
        private static int _scene = int.MinValue;

        public static void Play(Franchise winner)
        {
            var scene = SceneManager.GetActiveScene().handle;
            if (_scene == scene) return;
            _scene = scene;
            var a = UiKit.ParseHex(winner.PrimaryColor);
            var b = UiKit.ParseHex(winner.SecondaryColor);
            var fw = Fireworks.Create();
            fw.StartCoroutine(fw.Show(12, 7f, a));
            Confetti.Drop(a, b);
            StadiumBuilder.CrowdCheer(1f, winner.Id, (winner.ShortName.Length > 0 ? winner.ShortName : winner.Name).ToUpperInvariant() + " WIN!");
        }
    }
}
