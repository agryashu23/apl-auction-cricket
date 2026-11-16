using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core
{
    /// <summary>How an auction orders its lots.</summary>
    public enum LotOrder
    {
        /// <summary>The Dart engine's order (marquee, role sets rotating in tiers of 8, uncapped last). Friend rooms: the server runs it.</summary>
        Classic,

        /// <summary>IPL-style sets (solo): M1, M2, then BA1 AL1 WK1 FA1 SP1, UBA1 … USP1, BA2 …, then the accelerated round.</summary>
        IplSets,
    }

    /// <summary>One set of the auction list: a code ("BA1") and its players in the order they come up.</summary>
    public sealed class AuctionSet
    {
        public AuctionSet(string code, List<Player> players)
        {
            Code = code;
            Players = players;
        }

        public string Code { get; }
        public List<Player> Players { get; }
    }

    /// <summary>
    /// The IPL auction list, as the real mega auction runs it (IPL 2025: M1, M2 of six marquee
    /// players each; then capped batters BA1, all-rounders AL1, keepers WK1, fast bowlers FA1,
    /// spinners SP1; then the uncapped UBA1, UAL1, UWK1, UFA1, USP1; then BA2 … and so on; the
    /// accelerated round last). Players inside a category are ranked by stature (base price, then
    /// market value, then rating) so the big names come in the early sets; the draw inside a set
    /// is random, as the auctioneer's is.
    /// </summary>
    public static class AuctionSets
    {
        /// <summary>Marquee sets hold six, like the real M1 / M2.</summary>
        public const int MarqueeSize = 6;

        /// <summary>Category sets: six to eight players in the real list.</summary>
        public const int SetSize = 7;

        public const string Accelerated = "ACC";

        /// <summary>Category codes in the order each round calls them.</summary>
        public static readonly IReadOnlyList<string> Categories = new[] { "BA", "AL", "WK", "FA", "SP" };

        public static string Category(Player p) => p.Role switch
        {
            Role.Batter => "BA",
            Role.AllRounder => "AL",
            Role.WicketKeeper => "WK",
            _ => p.BowlType == BowlType.Spin ? "SP" : "FA",
        };

        /// <summary>Bigger names first: base price, then market value, then rating, then id.</summary>
        public static int ByStature(Player a, Player b)
        {
            var c = b.BasePrice.CompareTo(a.BasePrice);
            if (c != 0) return c;
            c = b.Value.CompareTo(a.Value);
            if (c != 0) return c;
            c = b.Impact.CompareTo(a.Impact);
            return c != 0 ? c : DartMath.Compare(a.Id, b.Id);
        }

        public static List<AuctionSet> Build(IReadOnlyList<Player> pool, AuctionFormat format, AuctionRules rules, Rng rng)
        {
            var marquee = pool.Where(p => p.Set == "marquee").ToList();
            marquee.Sort(ByStature);
            var others = pool.Where(p => p.Set != "marquee").ToList();

            // Capped / uncapped × category, each ranked by stature.
            var groups = new Dictionary<string, List<Player>>();
            foreach (var p in others)
            {
                var key = (p.Capped ? "" : "U") + Category(p);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Player>();
                list.Add(p);
            }
            foreach (var list in groups.Values) list.Sort(ByStature);

            // A lot cap (mini auction) keeps each group's share of the list, best first.
            if (format.MaxLots != null)
            {
                var budget = Math.Max(0, format.MaxLots.Value - marquee.Count);
                var rest = Math.Max(1, others.Count);
                foreach (var key in groups.Keys.ToList())
                {
                    var quota = DartMath.Round((double)(budget * groups[key].Count) / rest);
                    groups[key] = groups[key].Take(quota).ToList();
                }
            }

            var sets = new List<AuctionSet>();
            void Add(string code, List<Player> players)
            {
                if (players.Count == 0) return;
                var drawn = players.ToList();
                rng.Shuffle(drawn);
                sets.Add(new AuctionSet(code, drawn));
            }

            for (var i = 0; i * MarqueeSize < marquee.Count; i++)
            {
                Add("M" + (i + 1), marquee.Skip(i * MarqueeSize).Take(MarqueeSize).ToList());
            }
            var rounds = groups.Count == 0 ? 0 : groups.Values.Max(l => (l.Count + SetSize - 1) / SetSize);
            for (var r = 0; r < rounds; r++)
            {
                foreach (var prefix in new[] { "", "U" })
                {
                    foreach (var cat in Categories)
                    {
                        if (!groups.TryGetValue(prefix + cat, out var list)) continue;
                        Add(prefix + cat + (r + 1), list.Skip(r * SetSize).Take(SetSize).ToList());
                    }
                }
            }
            return sets;
        }

        /// <summary>
        /// The auctioneer's set announcement for a set code. Only the existing voice keys
        /// (set_marquee, set_batters, set_bowlers, set_allRounders, set_wicketKeepers,
        /// set_uncapped, set_accelerated): fast bowlers and spinners share "bowlers", every
        /// uncapped set says "uncapped". Classic set names pass straight through.
        /// </summary>
        public static string VoiceSet(string code)
        {
            if (code == Accelerated || code == "accelerated") return "accelerated";
            if (code.Length >= 2 && code[0] == 'M' && char.IsDigit(code[1])) return "marquee";
            if (code.Length >= 3 && code[0] == 'U' && char.IsDigit(code[code.Length - 1])) return "uncapped";
            var cat = code.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            return cat switch
            {
                "BA" => "batters",
                "AL" => "allRounders",
                "WK" => "wicketKeepers",
                "FA" => "bowlers",
                "SP" => "bowlers",
                _ => code,
            };
        }

        /// <summary>A set's name for the HUD and the hub: "MARQUEE SET 1", "CAPPED BATTERS 2", "UNCAPPED SPINNERS 1".</summary>
        public static string Label(string code)
        {
            switch (code)
            {
                case Accelerated:
                case "accelerated": return "ACCELERATED ROUND";
                case "marquee": return "MARQUEE";
                case "batters": return "BATTERS";
                case "bowlers": return "BOWLERS";
                case "allRounders": return "ALL-ROUNDERS";
                case "wicketKeepers": return "WICKETKEEPERS";
                case "uncapped": return "UNCAPPED";
            }
            var digits = new string(code.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            var head = code.Substring(0, code.Length - digits.Length);
            if (head == "M") return "MARQUEE SET " + digits;
            var uncapped = head.StartsWith("U") && head.Length == 3;
            var cat = uncapped ? head.Substring(1) : head;
            var name = cat switch
            {
                "BA" => "BATTERS",
                "AL" => "ALL-ROUNDERS",
                "WK" => "WICKETKEEPERS",
                "FA" => "FAST BOWLERS",
                "SP" => "SPINNERS",
                _ => code.ToUpperInvariant(),
            };
            return (uncapped ? "UNCAPPED " : "CAPPED ") + name + (digits.Length > 0 ? " " + digits : "");
        }
    }
}
