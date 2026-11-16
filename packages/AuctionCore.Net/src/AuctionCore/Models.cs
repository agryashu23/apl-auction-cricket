using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Apl.Core
{
    public enum Role { Batter, Bowler, AllRounder, WicketKeeper }

    public enum BowlType { Pace, Spin, None }

    public static class RoleCodes
    {
        public static Role FromCode(string code) => code switch
        {
            "BAT" => Role.Batter,
            "BOWL" => Role.Bowler,
            "AR" => Role.AllRounder,
            "WK" => Role.WicketKeeper,
            _ => throw new FormatException($"Unknown role \"{code}\""),
        };

        public static string Code(this Role r) => r switch
        {
            Role.Batter => "BAT",
            Role.Bowler => "BOWL",
            Role.AllRounder => "AR",
            _ => "WK",
        };

        public static BowlType BowlTypeFromName(string? name) => name switch
        {
            "pace" => BowlType.Pace,
            "spin" => BowlType.Spin,
            _ => BowlType.None,
        };
    }

    public sealed class Player : IEquatable<Player>
    {
        public string Id = "";
        public string Name = "";
        public string Country = "";
        public bool Overseas;
        public bool Capped = true;
        public Role Role;
        public bool LeftHanded;
        public BowlType BowlType = BowlType.None;

        /// <summary>Skills, 0–100.</summary>
        public int Batting, Bowling, Fielding, Keeping;

        /// <summary>-5..+5, added to skills.</summary>
        public int Form;

        /// <summary>Lakhs.</summary>
        public int BasePrice;
        public string Set = "";
        public List<string> Tags = new List<string>();

        /// <summary>Image key on the content CDN; null = silhouette.</summary>
        public string? Image;

        /// <summary>Fame / captaincy / brand, 0–100: AI pays more, no effect on play.</summary>
        public int StarPower;

        /// <summary>Franchise id last season (null = free agent).</summary>
        public string? PreviousTeam;

        /// <summary>Market value in lakhs at a ₹120 Cr purse (0 = unknown: valued from skills).</summary>
        public int Value;

        /// <summary>Bowling arm, "L" or "R" (null = unknown: Bowling.LeftArm falls back to a stable hash).</summary>
        public string? BowlArm;

        /// <summary>"fast", "fast-medium", "medium", "finger" (off-break / left-arm orthodox) or "wrist" (leg-break / left-arm wrist); null = unknown.</summary>
        public string? BowlStyle;

        /// <summary>His most recent real T20 season line, for the auction card (null = none on record).</summary>
        public SeasonLine? Recent;

        /// <summary>How good this player is in their role, 0–100.</summary>
        public int Impact
        {
            get
            {
                var hi = Math.Max(Batting, Bowling);
                var lo = Math.Min(Batting, Bowling);
                double raw = Role switch
                {
                    Role.Batter => Batting,
                    Role.Bowler => Bowling,
                    Role.AllRounder => 0.55 * hi + 0.45 * lo + 4,
                    _ => 0.85 * Batting + 0.15 * Keeping,
                };
                return DartMath.Clamp(DartMath.Round(raw + Form), 0, 100);
            }
        }

        public bool IsBowlingOption => Role == Role.Bowler || Role == Role.AllRounder || Bowling >= 55;

        public bool IsKeeper => Role == Role.WicketKeeper || Keeping >= 75;

        public bool HasTag(string tag) => Tags.Contains(tag);

        public static Player FromJson(JObject j) => new Player
        {
            Id = (string)j["id"]!,
            Name = (string)j["name"]!,
            Country = (string)j["country"]!,
            Overseas = (bool)j["overseas"]!,
            Capped = (bool?)j["capped"] ?? true,
            Role = RoleCodes.FromCode((string)j["role"]!),
            LeftHanded = (bool?)j["leftHanded"] ?? false,
            BowlType = RoleCodes.BowlTypeFromName((string?)j["bowlType"]),
            Batting = (int)j["batting"]!,
            Bowling = (int)j["bowling"]!,
            Fielding = (int)j["fielding"]!,
            Keeping = (int?)j["keeping"] ?? 0,
            Form = (int?)j["form"] ?? 0,
            BasePrice = (int)j["basePrice"]!,
            Set = (string)j["set"]!,
            Tags = j["tags"] is JArray tags ? tags.Select(t => (string)t!).ToList() : new List<string>(),
            Image = (string?)j["image"],
            StarPower = (int?)j["starPower"] ?? 0,
            PreviousTeam = (string?)j["previousTeam"],
            Value = (int?)j["value"] ?? 0,
            BowlArm = (string?)j["bowlArm"],
            BowlStyle = (string?)j["bowlStyle"],
            Recent = j["recent"] is JObject rec ? SeasonLine.FromJson(rec) : null,
        };

        public bool Equals(Player? other) => other != null && other.Id == Id;
        public override bool Equals(object? obj) => obj is Player p && Equals(p);
        public override int GetHashCode() => Id.GetHashCode();
        public override string ToString() => $"{Name} ({Role.Code()}, {Impact})";
    }

    /// <summary>One real season's numbers (display only: no effect on play or prices).</summary>
    public sealed class SeasonLine
    {
        /// <summary>"IPL 2026", "IPL 2025" or "T20s 2025-26".</summary>
        public string Label = "";
        public int Matches, Runs, Wickets;
        public double StrikeRate, Economy;

        public static SeasonLine FromJson(JObject j) => new SeasonLine
        {
            Label = (string?)j["label"] ?? "",
            Matches = (int?)j["m"] ?? 0,
            Runs = (int?)j["runs"] ?? 0,
            StrikeRate = (double?)j["sr"] ?? 0,
            Wickets = (int?)j["wkts"] ?? 0,
            Economy = (double?)j["econ"] ?? 0,
        };
    }

    public sealed class Franchise
    {
        public string Id = "", Name = "", ShortName = "", City = "";

        /// <summary>Hex, e.g. "#1E5AA8".</summary>
        public string PrimaryColor = "", SecondaryColor = "";

        public static Franchise FromJson(JObject j) => new Franchise
        {
            Id = (string)j["id"]!,
            Name = (string)j["name"]!,
            ShortName = (string)j["shortName"]!,
            City = (string)j["city"]!,
            PrimaryColor = (string)j["primaryColor"]!,
            SecondaryColor = (string)j["secondaryColor"]!,
        };
    }

    /// <summary>While the current bid is below <see cref="Below"/>, raises go up by <see cref="Step"/>.</summary>
    public sealed class IncrementStep
    {
        public int? Below;
        public int Step;
    }

    public sealed class RatingScale
    {
        public double BattingBase = 45, BowlingBase = 50, Spread = 36;

        public static RatingScale FromJson(JObject? j) => j == null
            ? new RatingScale()
            : new RatingScale
            {
                BattingBase = (double)j["battingBase"]!,
                BowlingBase = (double)j["bowlingBase"]!,
                Spread = (double)j["spread"]!,
            };
    }

    public sealed class RetentionRules
    {
        public int MaxPlayers = 3;
        public List<int> Slabs = new List<int> { 1800, 1400, 1100 };
        public int UncappedPrice = 400;

        /// <summary>Retained players keep their current price (market value) instead of the slabs.</summary>
        public bool AtValue;

        public static RetentionRules FromJson(JObject? j) => j == null
            ? new RetentionRules()
            : new RetentionRules
            {
                MaxPlayers = (int)j["maxPlayers"]!,
                Slabs = j["slabs"] is JArray slabs ? slabs.Select(x => (int)x).ToList() : new List<int>(),
                UncappedPrice = (int?)j["uncappedPrice"] ?? 0,
                AtValue = (bool?)j["atValue"] ?? false,
            };
    }

    public sealed class AuctionFormat
    {
        public string Id = "", Label = "";

        /// <summary>Lakhs.</summary>
        public int Purse, MinSquad, MaxSquad, MaxOverseas;
        public int? MaxLots;
        public RatingScale? Rating;

        /// <summary>Overrides AuctionRules.Retention for this format.</summary>
        public RetentionRules? Retention;

        /// <summary>Always played with retentions (a mini auction): no fresh-auction option.</summary>
        public bool RetentionOnly;

        /// <summary>
        /// RTM cards per team in this format; null = AuctionRules.RtmCards. The real IPL uses RTM only
        /// at a mega auction, so data can set the mini auction's to 0 (formats.standard.rtmCards).
        /// </summary>
        public int? RtmCards;

        public static AuctionFormat FromJson(string id, JObject j) => new AuctionFormat
        {
            Id = id,
            Label = (string)j["label"]!,
            Purse = (int)j["purse"]!,
            MinSquad = (int)j["minSquad"]!,
            MaxSquad = (int)j["maxSquad"]!,
            MaxOverseas = (int)j["maxOverseas"]!,
            MaxLots = (int?)j["maxLots"],
            Rating = j["rating"] is JObject r ? RatingScale.FromJson(r) : null,
            Retention = j["retention"] is JObject ret ? RetentionRules.FromJson(ret) : null,
            RetentionOnly = (bool?)j["retentionOnly"] ?? false,
            RtmCards = (int?)j["rtmCards"],
        };
    }

    public sealed class AuctionRules
    {
        public List<IncrementStep> Increments = new List<IncrementStep>();

        /// <summary>Lakhs, highest first.</summary>
        public List<int> BasePrices = new List<int>();
        public int BidTimerSec, AcceleratedTimerSec, MaxOverseasInXI;
        public List<string> SetOrder = new List<string>();

        /// <summary>In data order.</summary>
        public List<AuctionFormat> FormatList = new List<AuctionFormat>();
        public RatingScale Rating = new RatingScale();
        public RetentionRules Retention = new RetentionRules();
        public int RtmCards = 1;

        /// <summary>No player may sell above this (lakhs). Null = no cap.</summary>
        public int? MaxPrice;

        /// <summary>The cap in friend rooms, if different (null = MaxPrice).</summary>
        public int? RoomMaxPrice;

        public AuctionFormat Format(string id) => FormatList.First(f => f.Id == id);

        public int MinBasePrice => BasePrices.Min();

        public RatingScale RatingFor(AuctionFormat format) => format.Rating ?? Rating;

        public RetentionRules RetentionFor(AuctionFormat format) => format.Retention ?? Retention;

        public int RtmCardsFor(AuctionFormat format) => format.RtmCards ?? RtmCards;

        public int IncrementFor(int currentBid)
        {
            foreach (var s in Increments)
            {
                if (s.Below == null || currentBid < s.Below.Value) return s.Step;
            }
            return Increments[Increments.Count - 1].Step;
        }

        /// <summary>The only amount a team may bid next.</summary>
        public int NextBid(int? currentBid, int basePrice) =>
            currentBid == null ? basePrice : currentBid.Value + IncrementFor(currentBid.Value);

        /// <summary>
        /// The highest price a lot opening at <paramref name="basePrice"/> can actually reach without
        /// going over <paramref name="amount"/> (base, then the increments); 0 when even the base is
        /// over it. An AI limit is always one of these, so "would pay up to" is a real bid.
        /// </summary>
        public int FloorToBid(int amount, int basePrice)
        {
            if (amount < basePrice) return 0;
            var price = basePrice;
            while (true)
            {
                var next = NextBid(price, basePrice);
                if (next > amount) return price;
                price = next;
            }
        }

        /// <summary>True when a lot opening at <paramref name="basePrice"/> can sell at exactly <paramref name="amount"/>.</summary>
        public bool IsReachableBid(int amount, int basePrice) => amount >= basePrice && FloorToBid(amount, basePrice) == amount;

        public static AuctionRules FromJson(JObject j) => new AuctionRules
        {
            Increments = ((JArray)j["increments"]!)
                .Select(s => new IncrementStep { Below = (int?)s["below"], Step = (int)s["step"]! })
                .ToList(),
            BasePrices = ((JArray)j["basePrices"]!).Select(x => (int)x).ToList(),
            BidTimerSec = (int)j["bidTimerSec"]!,
            AcceleratedTimerSec = (int)j["acceleratedTimerSec"]!,
            MaxOverseasInXI = (int)j["maxOverseasInXI"]!,
            SetOrder = ((JArray)j["setOrder"]!).Select(x => (string)x!).ToList(),
            FormatList = ((JObject)j["formats"]!).Properties()
                .Select(p => AuctionFormat.FromJson(p.Name, (JObject)p.Value))
                .ToList(),
            Rating = RatingScale.FromJson(j["rating"] as JObject),
            Retention = RetentionRules.FromJson(j["retention"] as JObject),
            RtmCards = (int?)j["rtmCards"] ?? 1,
            MaxPrice = (int?)j["maxPrice"],
            RoomMaxPrice = (int?)j["roomMaxPrice"],
        };
    }

    /// <summary>Everything the CDN publishes: league, teams, players, rules.</summary>
    public sealed class Dataset
    {
        public int Version;
        public string LeagueName = "", LeagueShortName = "";
        public List<Franchise> Franchises = new List<Franchise>();
        public List<Player> Players = new List<Player>();
        public AuctionRules Rules = new AuctionRules();

        public static Dataset FromJson(string json) => FromJson(JObject.Parse(json));

        public static Dataset FromJson(JObject j)
        {
            var league = (JObject)j["league"]!;
            return new Dataset
            {
                Version = (int)j["version"]!,
                LeagueName = (string)league["name"]!,
                LeagueShortName = (string)league["shortName"]!,
                Franchises = ((JArray)j["franchises"]!).Select(f => Franchise.FromJson((JObject)f)).ToList(),
                Players = ((JArray)j["players"]!).Select(p => Player.FromJson((JObject)p)).ToList(),
                Rules = AuctionRules.FromJson((JObject)j["rules"]!),
            };
        }

        public Player PlayerById(string id) => Players.First(p => p.Id == id);
    }
}
