using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Per-player variety on top of the body pick, all from the player's id and his
    /// portrait look (Portraits.Looks) so a player looks the same every match and
    /// like his card: height (quicks tall, keepers compact, ±5 % within that),
    /// build (width / depth ±5 %), hair colour from the portrait (black, brown,
    /// blond, ginger, salt-and-pepper, greying for seniors), buzz cuts and bald
    /// heads with the hair cards off. Players are dressed in an order fixed by
    /// their ids too, so the body each gets — and his shirt number — doesn't
    /// depend on the batting order.
    /// </summary>
    public sealed partial class CricketCast
    {
        /// <summary>The Rocketbox hair cards' typical colour (sRGB): what the dye is divided by.</summary>
        private static readonly Color CardBrown = new Color(0.3f, 0.22f, 0.16f);

        /// <summary>A stable hash of a player's id (not string.GetHashCode, which can change between runs).</summary>
        private static int Seed(Player p, int salt) => Mathf.Abs(p.Id.Aggregate(salt * 7919 + 17, (h, c) => h * 31 + c));

        /// <summary>The side in the order its bodies are chosen: fixed by id.</summary>
        private static IEnumerable<Player> StableOrder(IEnumerable<Player> players) => players.OrderBy(p => Seed(p, 3)).ThenBy(p => p.Id);

        /// <summary>Height by role: fast bowlers tall, keepers compact, the rest in between.</summary>
        private static float RoleHeight(Player p) =>
            p.Role == Role.WicketKeeper ? 0.965f : p.BowlType == BowlType.Pace && p.Role != Role.Batter ? 1.03f : 1f;

        /// <summary>Height, build and hair for this player (on a freshly spawned body, before gear).</summary>
        private static void Looks(Animator body, Player p)
        {
            var h = RoleHeight(p) * (0.955f + 0.09f * (Seed(p, 5) % 1000) / 999f);
            var w = 0.95f + 0.1f * (Seed(p, 11) % 1000) / 999f;
            body.transform.localScale = new Vector3(h * w, h, h * Mathf.Lerp(1f, w, 0.7f));
            Portraits.Looks.TryGetValue(p.Id, out var look);
            // The hair cards to the card's colour: the dye (CricketCast.Heads.HairDye) over the cards' own
            // mid-brown, as a multiplier (r9 `374`: ×1.6 on brown still read brown for a blond card).
            var dye = look != null && HairDye.TryGetValue(look.hair_color, out var d0) ? d0 : HairDye["black"];
            var tint = new Color(dye.r / CardBrown.r, dye.g / CardBrown.g, dye.b / CardBrown.b);
            // Seniors with dark hair go grey at the temples: lift it toward grey.
            if (look != null && look.age == "senior" && look.hair_color != "salt" && look.hair_color != "grey")
                tint = Color.Lerp(tint, new Color(HairDye["salt"].r / CardBrown.r, HairDye["salt"].g / CardBrown.g, HairDye["salt"].b / CardBrown.b), 0.45f);
            var shaved = look != null && (look.hair == "buzz" || look.hair == "bald");
            foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.name.ToLowerInvariant().Contains("opacity") || !m.HasProperty("_BaseColor")) continue;
                    var c = m.GetColor("_BaseColor");
                    // HDR multipliers (blond, grey) brighten the dark cards; alpha 0 = no hair cards (Hair() keeps it).
                    m.SetColor("_BaseColor", new Color(Mathf.Clamp(c.r * tint.r, 0.15f, 3.5f), Mathf.Clamp(c.g * tint.g, 0.15f, 3.5f), Mathf.Clamp(c.b * tint.b, 0.15f, 3.5f), shaved ? 0 : c.a));
                    if (shaved)
                    {
                        m.SetFloat("_AlphaClip", 1);
                        m.EnableKeyword("_ALPHATEST_ON");
                    }
                }
            }
        }
    }
}
