using UnityEngine;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The home page's art: our own cricketers rendered from the game's cast in the user's
    /// kit (Resources/Ui/HomeArt, made by Dev/HomeArt.cs — hero_bat_&lt;team&gt; for the
    /// QUICK MATCH card, mode_&lt;pose&gt;_&lt;team&gt;, hero_&lt;team&gt;). The squad hero and MY
    /// SQUAD plate that sat on the right are gone (owner v26).
    /// </summary>
    public sealed partial class MainMenu
    {
        /// <summary>A rendered figure for <paramref name="team"/> (or MBH's, then null).</summary>
        private static Texture2D? Art(string name, string team) =>
            Resources.Load<Texture2D>($"Ui/HomeArt/{name}_{team}") ?? Resources.Load<Texture2D>($"Ui/HomeArt/{name}_MBH");
    }
}
