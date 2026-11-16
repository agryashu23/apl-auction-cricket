using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;

namespace Apl.Client.Match
{
    /// <summary>
    /// The playing XI the user confirmed on the PLAYING XI screen (Menu/PlayingXiScreen.cs),
    /// carried across the scene load like <see cref="MatchLaunch"/>. The ground's
    /// <c>SheetFor</c> plays it for the user's side when it still fits that side's squad and
    /// the rules (<see cref="PlayingXi.Resolve"/>); otherwise the auto-picked XI plays as before.
    /// </summary>
    public static class XiChoice
    {
        public static string TeamId { get; private set; } = "";
        public static List<string>? Ids { get; private set; }

        public static void Set(string teamId, IEnumerable<Player> xi)
        {
            TeamId = teamId;
            Ids = xi.Select(p => p.Id).ToList();
        }

        /// <summary>No choice: the auto-picked XI (a challenge, a resumed match, a launch that skips the screen).</summary>
        public static void Clear()
        {
            TeamId = "";
            Ids = null;
        }

        /// <summary>The chosen XI for <paramref name="teamId"/> out of <paramref name="squad"/>, or null.</summary>
        public static TeamSheet? SheetFor(Dataset data, string teamId, IReadOnlyList<Player> squad) =>
            teamId.Length > 0 && teamId == TeamId ? PlayingXi.Resolve(teamId, squad, Ids, data.Rules.MaxOverseasInXI) : null;
    }
}
