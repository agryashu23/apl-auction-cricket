using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Apl.Core
{
    /// <summary>One team's end-of-auction score (docs/ROOMS_PROTOCOL.md §8).</summary>
    public sealed class TeamScore
    {
        public string TeamId = "";
        public int Rank;
        public int Total;

        /// <summary>The squad rating's overall (0–100) and grade, as the results screen shows.</summary>
        public int Overall;
        public string Grade = "";

        // Points per category.
        public int Squad, Balance, Value, Steals, StarBuy, PurseUse, Roles, Overseas;

        // The numbers behind them.
        public int Spent, BuysSpent, PurseLeft, Purse, SquadSize, OverseasCount, MaxOverseas, StealCount, RoleShort, BalanceScore;
        public double RatingPerCr;
        public Purchase? BiggestBuy;
        public readonly List<(Purchase Buy, int Worth)> StealList = new List<(Purchase, int)>();
    }

    public sealed class AuctionAward
    {
        public string Id = "", Title = "", TeamId = "", Detail = "";
        public string? PlayerId, PlayerName;
        public int? Price, Worth;
    }

    public sealed class AwardsReport
    {
        /// <summary>In rank order (1 = winner).</summary>
        public List<TeamScore> Standings = new List<TeamScore>();
        public List<AuctionAward> Awards = new List<AuctionAward>();
        public string WinnerId => Standings[0].TeamId;

        public TeamScore For(string teamId) => Standings.First(s => s.TeamId == teamId);

        /// <summary>
        /// The "results" payload. <paramref name="owner"/> gives, per team, who held it in a room:
        /// (playerId, name, control) or null for a team no human held (control "ai").
        /// </summary>
        public JObject ToJson(Func<string, (string Id, string Name, string Control)?>? owner = null)
        {
            string? topHuman = null;
            var standings = new JArray();
            foreach (var s in Standings)
            {
                var o = owner?.Invoke(s.TeamId);
                if (o != null && topHuman == null) topHuman = s.TeamId;
                var biggest = s.BiggestBuy == null
                    ? JValue.CreateNull()
                    : (JToken)new JObject { ["player"] = s.BiggestBuy.Player.Id, ["name"] = s.BiggestBuy.Player.Name, ["price"] = s.BiggestBuy.Price };
                standings.Add(new JObject
                {
                    ["rank"] = s.Rank,
                    ["team"] = s.TeamId,
                    ["owner"] = o == null ? JValue.CreateNull() : new JValue(o.Value.Id),
                    ["name"] = o == null ? JValue.CreateNull() : new JValue(o.Value.Name),
                    ["control"] = o == null ? "ai" : o.Value.Control,
                    ["total"] = s.Total,
                    ["overall"] = s.Overall,
                    ["grade"] = s.Grade,
                    ["points"] = new JObject
                    {
                        ["squad"] = s.Squad,
                        ["balance"] = s.Balance,
                        ["value"] = s.Value,
                        ["steals"] = s.Steals,
                        ["starBuy"] = s.StarBuy,
                        ["purse"] = s.PurseUse,
                        ["roles"] = s.Roles,
                        ["overseas"] = s.Overseas,
                    },
                    ["stats"] = new JObject
                    {
                        ["spent"] = s.Spent,
                        ["buysSpent"] = s.BuysSpent,
                        ["purseLeft"] = s.PurseLeft,
                        ["purse"] = s.Purse,
                        ["squadSize"] = s.SquadSize,
                        ["overseas"] = s.OverseasCount,
                        ["maxOverseas"] = s.MaxOverseas,
                        ["ratingPerCr"] = Math.Round(s.RatingPerCr, 3),
                        ["steals"] = s.StealCount,
                        ["roleShort"] = s.RoleShort,
                        ["biggestBuy"] = biggest,
                    },
                });
            }
            var awards = new JArray();
            foreach (var a in Awards)
            {
                var j = new JObject { ["id"] = a.Id, ["title"] = a.Title, ["team"] = a.TeamId, ["detail"] = a.Detail };
                if (a.PlayerId != null)
                {
                    j["player"] = a.PlayerId;
                    j["playerName"] = a.PlayerName;
                }
                if (a.Price != null) j["price"] = a.Price.Value;
                if (a.Worth != null) j["worth"] = a.Worth.Value;
                awards.Add(j);
            }
            return new JObject
            {
                ["version"] = AuctionAwards.Version,
                ["winner"] = WinnerId,
                ["topHuman"] = topHuman == null ? JValue.CreateNull() : new JValue(topHuman),
                ["standings"] = standings,
                ["awards"] = awards,
                ["scoring"] = new JArray(AuctionAwards.ScoringRules),
            };
        }
    }

    /// <summary>
    /// End-of-auction rewards: points per team, a winner and awards. Pure and deterministic (same
    /// squads, same numbers), so rooms and solo show the same thing. Every rule is in ScoringRules.
    /// </summary>
    public static class AuctionAwards
    {
        /// <summary>2: value up to 150 (was 100), steals up to 90 (was 60): the round-1 rater found the
        /// top three a point apart (600/599/598) with the squad rating deciding nearly everything.</summary>
        public const int Version = 2;

        public const int SquadFactor = 4;
        public const int ValueMax = 150;
        public const int StealPoints = 15, StealCap = 90;
        public const double StealShare = 0.6;
        public const int StealMinWorth = 100;
        public const int StarFrom = 60, StarCap = 30;
        public const int PurseMax = 25;
        public const double PurseTarget = 0.9;
        public const int RolesMax = 25, RolePenalty = 5;
        public const int OverseasMax = 15;

        public static readonly IReadOnlyList<string> ScoringRules = new[]
        {
            "squad = squad rating overall (0-100) x 4 (max 400)",
            "balance = the rating's balance score: keepers, bowling options, batters, a left-hander, squad size (max 100)",
            "value = overall / crores spent on the squad, scaled so the room's best = 150",
            "steals = 15 per steal, max 90; a steal is a buy worth 1 Cr+ bought at 60% of its worth or less (worth = reputation price)",
            "starBuy = the highest-priced buy's impact - 60, 0 to 30",
            "purse = 25 x min(1, spent / 90% of the purse)",
            "roles = 25 - 5 per player short of the role targets (keepers, batters, all-rounders, bowlers), min 0",
            "overseas = 15 x min(1, overseas players / 4)",
            "total = the sum; rank by total, then overall, then team id; rank 1 wins",
            "buys = auction and RTM purchases (not retained or auto-filled players)",
        };

        public static bool IsBuy(Purchase p) => p.Kind == PurchaseKind.Auction || p.Kind == PurchaseKind.Rtm;

        public static int Worth(Player p, AuctionFormat format) => DartMath.Round(Market.ReputationPrice(p, format));

        public static AwardsReport Compute(AuctionEngine engine, AuctionRules rules)
        {
            var format = engine.Format;
            var teams = engine.Teams.Values.ToList();
            var ratings = Ratings.Rate(teams, rules.MaxOverseasInXI, format.MinSquad - 11, rules.RatingFor(format))
                .ToDictionary(r => r.TeamId);
            var targets = Market.SquadTargets(format);
            var roles = new[] { Role.WicketKeeper, Role.Batter, Role.AllRounder, Role.Bowler };
            var overseasFull = Math.Max(1, rules.MaxOverseasInXI);

            var scores = new List<TeamScore>();
            foreach (var t in teams)
            {
                var r = ratings[t.Id];
                var buys = t.Squad.Where(IsBuy).ToList();
                var s = new TeamScore
                {
                    TeamId = t.Id,
                    Overall = r.Overall,
                    Grade = r.Grade,
                    BalanceScore = r.Balance,
                    Spent = t.Spent,
                    BuysSpent = buys.Sum(b => b.Price),
                    PurseLeft = t.Purse,
                    Purse = format.Purse,
                    SquadSize = t.Size,
                    OverseasCount = t.OverseasCount,
                    MaxOverseas = format.MaxOverseas,
                };
                s.RatingPerCr = r.Overall / Math.Max(1.0, t.Spent / (double)Money.LakhsPerCrore);

                foreach (var b in buys)
                {
                    var worth = Worth(b.Player, format);
                    if (worth >= StealMinWorth && b.Price <= worth * StealShare) s.StealList.Add((b, worth));
                }
                s.StealCount = s.StealList.Count;
                s.BiggestBuy = buys
                    .OrderByDescending(b => b.Price)
                    .ThenByDescending(b => b.Player.Impact)
                    .ThenBy(b => b.Player.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                s.RoleShort = roles.Sum(role => Math.Max(0, targets.ForRole(role) - t.CountRole(role)));

                s.Squad = r.Overall * SquadFactor;
                s.Balance = r.Balance;
                s.Steals = Math.Min(StealCap, StealPoints * s.StealCount);
                s.StarBuy = s.BiggestBuy == null ? 0 : DartMath.Clamp(s.BiggestBuy.Player.Impact - StarFrom, 0, StarCap);
                s.PurseUse = DartMath.Round(PurseMax * Math.Min(1.0, t.Spent / (PurseTarget * format.Purse)));
                s.Roles = Math.Max(0, RolesMax - RolePenalty * s.RoleShort);
                s.Overseas = DartMath.Round(OverseasMax * Math.Min(1.0, t.OverseasCount / (double)overseasFull));
                scores.Add(s);
            }
            var bestValue = scores.Max(s => s.RatingPerCr);
            foreach (var s in scores)
            {
                s.Value = bestValue <= 0 ? 0 : DartMath.Round(ValueMax * s.RatingPerCr / bestValue);
                s.Total = s.Squad + s.Balance + s.Value + s.Steals + s.StarBuy + s.PurseUse + s.Roles + s.Overseas;
            }
            scores.Sort((a, b) =>
            {
                var c = b.Total.CompareTo(a.Total);
                if (c != 0) return c;
                c = b.Overall.CompareTo(a.Overall);
                return c != 0 ? c : DartMath.Compare(a.TeamId, b.TeamId);
            });
            for (var i = 0; i < scores.Count; i++) scores[i].Rank = i + 1;

            var report = new AwardsReport { Standings = scores };
            // Each award: the best by its measure; a tie goes to the better rank (scores is in rank order).
            TeamScore Best(Func<TeamScore, double> measure) => scores.Aggregate((best, s) => measure(s) > measure(best) ? s : best);

            var squad = Best(s => s.Overall);
            report.Awards.Add(new AuctionAward { Id = "bestSquad", Title = "Best Squad", TeamId = squad.TeamId, Detail = $"Rated {squad.Overall} ({squad.Grade})" });

            (TeamScore Team, Purchase Buy, int Worth)? steal = null;
            foreach (var s in scores)
            {
                foreach (var (buy, worth) in s.StealList)
                {
                    var margin = worth - buy.Price;
                    if (steal == null || margin > steal.Value.Worth - steal.Value.Buy.Price) steal = (s, buy, worth);
                }
            }
            if (steal != null)
            {
                var (team, buy, worth) = steal.Value;
                report.Awards.Add(new AuctionAward
                {
                    Id = "steal",
                    Title = "Steal of the Auction",
                    TeamId = team.TeamId,
                    PlayerId = buy.Player.Id,
                    PlayerName = buy.Player.Name,
                    Price = buy.Price,
                    Worth = worth,
                    Detail = $"{buy.Player.Name} for {Money.Format(buy.Price)} (worth {Money.Format(worth)})",
                });
            }

            var spender = Best(s => s.BuysSpent);
            if (spender.BuysSpent > 0)
            {
                report.Awards.Add(new AuctionAward { Id = "bigSpender", Title = "Big Spender", TeamId = spender.TeamId, Detail = $"Spent {Money.Format(spender.BuysSpent)} at the auction" });
            }

            var biggest = Best(s => s.BiggestBuy?.Price ?? 0);
            if (biggest.BiggestBuy != null)
            {
                var b = biggest.BiggestBuy;
                report.Awards.Add(new AuctionAward
                {
                    Id = "biggestBuy",
                    Title = "Biggest Buy",
                    TeamId = biggest.TeamId,
                    PlayerId = b.Player.Id,
                    PlayerName = b.Player.Name,
                    Price = b.Price,
                    Detail = $"{b.Player.Name} for {Money.Format(b.Price)}",
                });
            }

            var balanced = Best(s => s.Balance + s.Roles);
            report.Awards.Add(new AuctionAward { Id = "mostBalanced", Title = "Most Balanced", TeamId = balanced.TeamId, Detail = $"Balance {balanced.Balance}, roles {balanced.Roles}/{RolesMax}" });

            var value = Best(s => s.RatingPerCr);
            report.Awards.Add(new AuctionAward
            {
                Id = "valueKing",
                Title = "Value King",
                TeamId = value.TeamId,
                Detail = value.RatingPerCr.ToString("0.00", CultureInfo.InvariantCulture) + " rating points per crore",
            });
            return report;
        }
    }
}
