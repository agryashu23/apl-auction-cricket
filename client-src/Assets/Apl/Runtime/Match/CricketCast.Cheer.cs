using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Cheerleaders: the separate female bodies (GroundRefs.CheerModels, never in
    /// the players' cast) in a team's colours — the top in the primary, the
    /// trim in the secondary, white leggings and shoes.
    /// </summary>
    public sealed partial class CricketCast
    {
        public int Cheerleaders => _refs.CheerModels.Length;

        /// <summary>Cheerleader <paramref name="index"/> (wrapping) in <paramref name="team"/>'s colours; null if none were built.</summary>
        public Animator? Cheerleader(int index, Franchise team)
        {
            if (_refs.CheerModels.Length == 0 || _refs.KitBlit == null) return null;
            var i = ((index % _refs.CheerModels.Length) + _refs.CheerModels.Length) % _refs.CheerModels.Length;
            var mask = i < _refs.CheerKits.Length ? _refs.CheerKits[i] : null;
            var a = UiKit.ParseHex(team.PrimaryColor);
            var b = UiKit.ParseHex(team.SecondaryColor);
            var kit = new[] { a, b, new Color(0.95f, 0.95f, 0.95f), new Color(0.96f, 0.96f, 0.96f) };
            return SpawnFrom(_refs.CheerModels[i], mask, -1 - i, kit, $"Cheer_{team.Id}_{index}");
        }
    }
}
