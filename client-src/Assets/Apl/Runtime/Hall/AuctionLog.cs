using System.Collections.Generic;
using System.Linq;
using System.Text;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Hall
{
    /// <summary>
    /// Auction evidence for the Unity log (PM checks), one line per lot:
    /// <c>[APL aibid]</c> each AI team's price limit for the player on the block (set once per lot by
    /// the franchise AI), <c>[APL price]</c> the sale against his market value and star price, and
    /// <c>[APL retain]</c> each franchise's keeps at the start. Silent during SIMULATE.
    /// </summary>
    public static class AuctionLog
    {
        /// <summary>SIMULATE is running: no per-lot lines (thousands of limits).</summary>
        public static bool Quiet;

        private static bool _installed;
        private static int _lot = -1;
        private static readonly Dictionary<string, int> Limits = new Dictionary<string, int>();

        private static void Install()
        {
            if (_installed) return;
            _installed = true;
            AiBidder.LimitSet += (bidder, lot, limit) =>
            {
                if (Quiet) return;
                if (lot.Number != _lot)
                {
                    _lot = lot.Number;
                    Limits.Clear();
                }
                Limits[bidder.TeamId] = limit;
            };
        }

        public static void Revealed(Lot lot)
        {
            Install();
            if (lot.Number != _lot)
            {
                _lot = lot.Number;
                Limits.Clear();
            }
        }

        public static void Closed(LotResult r, AuctionEngine engine)
        {
            if (Quiet) return;
            var p = r.Player;
            var f = engine.Format;
            if (r.LotNumber == _lot && Limits.Count > 0)
            {
                var sb = new StringBuilder($"[APL aibid] lot {r.LotNumber} {p.Id} {p.Name} ({engine.SetOf(p)}) limits:");
                foreach (var kv in Limits.OrderByDescending(kv => kv.Value))
                {
                    sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value > 0 ? Money.Format(kv.Value) : "out");
                }
                Debug.Log(sb.ToString());
            }
            var rep = Market.ReputationPrice(p, f);
            Debug.Log($"[APL price] lot {r.LotNumber} {p.Id} {p.Name}: {(r.Sold ? $"SOLD {r.TeamId} {Money.Format(r.Price!.Value)}{(r.ViaRtm ? " via RTM" : "")}" : "UNSOLD")}" +
                      $" | base {Money.Format(p.BasePrice)} value {Money.Format(DartMath.Round(Market.FairPrice(p, f)))} star price {Money.Format(DartMath.Round(rep))}" +
                      $"{(Market.IsMarqueeLot(p, f) ? " MARQUEE-LOT" : "")} star {p.StarPower} impact {p.Impact} bids {r.BidCount}");
        }

        /// <summary>Every franchise's retentions (AI picks and yours) with their cost.</summary>
        public static void Retentions(IReadOnlyDictionary<string, List<Player>> retentions, AuctionRules rules, AuctionFormat format, string you)
        {
            foreach (var kv in retentions)
            {
                var prices = Retention.Prices(kv.Value, rules, format);
                var names = string.Join(", ", prices.Select(x => $"{x.Key.Name} {Money.Format(x.Value)}"));
                Debug.Log($"[APL retain] {kv.Key}{(kv.Key == you ? " (you)" : " (AI)")}: kept {kv.Value.Count}/{rules.RetentionFor(format).MaxPlayers} for {Money.Format(prices.Sum(x => x.Value))}: {names}");
            }
        }
    }
}
