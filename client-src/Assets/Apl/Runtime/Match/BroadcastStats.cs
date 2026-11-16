using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>One cell of a player card's stats row: "RUNS" / "157".</summary>
    public readonly struct CardStat
    {
        public readonly string Label, Value;

        public CardStat(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }

    /// <summary>What a player card shows: who, what he does, his numbers, whose side he's on.</summary>
    public sealed class PlayerCardInfo
    {
        public Player Player = null!;

        /// <summary>"Right Hand Batter", "Left-arm Fast" — or a dismissal ("c Mid-off b Henrey").</summary>
        public string Role = "";

        /// <summary>The side's short name and colour (the card's tint).</summary>
        public string Team = "";
        public Color Tint = UiKit.Accent;
        public List<CardStat> Stats = new List<CardStat>();
    }

    /// <summary>
    /// The words and numbers on the broadcast cards: how a player bats and bowls,
    /// his season record when the dataset carries one (a "stats" / "season" block
    /// per player), else what he's done in this match, and his skill ratings.
    /// </summary>
    public static class BroadcastStats
    {
        private static Dictionary<string, JObject>? _season;

        /// <summary>Per-player season blocks from the dataset, if it has any (none yet in v1).</summary>
        private static Dictionary<string, JObject> Season
        {
            get
            {
                if (_season != null) return _season;
                _season = new Dictionary<string, JObject>();
                try
                {
                    var text = Resources.Load<TextAsset>("Data/apl_dataset");
                    if (text != null && JObject.Parse(text.text)["players"] is JArray players)
                    {
                        foreach (var p in players.OfType<JObject>())
                        {
                            var block = p["stats"] as JObject ?? p["season"] as JObject;
                            var id = (string?)p["id"];
                            if (block != null && id != null) _season[id] = block;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[APL match] season stats unreadable: {e.Message}");
                }
                return _season;
            }
        }

        public static string BatLabel(Player p) => p.LeftHanded ? "Left Hand Batter" : "Right Hand Batter";

        /// <summary>A left-arm bowler: the engine's call (Bowling.LeftArm), the one source the actions, chips and cards share.</summary>
        public static bool LeftArm(Player p) => Bowling.LeftArm(p);

        /// <summary>
        /// "Right-arm Express", "Left-arm Orthodox", "Right-arm Leg-Spin": the engine's
        /// Bowling.StyleName, title-cased — the same words as the bowling chips' header
        /// (the card used to work out the arm from tags and disagree with them).
        /// </summary>
        public static string BowlLabel(Player p)
        {
            if (p.BowlType == BowlType.None) return "Part-time";
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Bowling.StyleName(p));
        }

        /// <summary>The card for a batter coming in: season numbers if known, else this match's.</summary>
        public static PlayerCardInfo Batter(Player p, QuickMatch m, Franchise side)
        {
            var info = Card(p, BatLabel(p), side);
            if (Season.TryGetValue(p.Id, out var s))
            {
                Add(info, s, "MATCHES", "matches");
                Add(info, s, "INNINGS", "innings");
                Add(info, s, "RUNS", "runs");
                Add(info, s, "BATTING AVG", "average", "battingAverage", "avg");
                Add(info, s, "STRIKE RATE", "strikeRate", "sr");
                Add(info, s, "BEST SCORE", "best", "highScore", "hs");
                Add(info, s, "50s", "fifties", "50s");
                Add(info, s, "100s", "hundreds", "100s");
            }
            if (info.Stats.Count == 0)
            {
                var line = m.Order.FirstOrDefault(b => b.Player.Id == p.Id);
                var runs = line?.Runs ?? 0;
                var balls = line?.Balls ?? 0;
                info.Stats.Add(new CardStat("RUNS", runs.ToString()));
                info.Stats.Add(new CardStat("BALLS", balls.ToString()));
                info.Stats.Add(new CardStat("4s", (line?.Fours ?? 0).ToString()));
                info.Stats.Add(new CardStat("6s", (line?.Sixes ?? 0).ToString()));
                info.Stats.Add(new CardStat("STRIKE RATE", balls > 0 ? (runs * 100.0 / balls).ToString("0.0", CultureInfo.InvariantCulture) : "-"));
                info.Stats.Add(new CardStat("BATTING", p.Batting.ToString()));
            }
            return info;
        }

        /// <summary>The card for a bowler starting a spell.</summary>
        public static PlayerCardInfo Bowler(Player p, QuickMatch m, Franchise side)
        {
            var info = Card(p, BowlLabel(p), side);
            if (Season.TryGetValue(p.Id, out var s))
            {
                Add(info, s, "MATCHES", "matches");
                Add(info, s, "WICKETS", "wickets");
                Add(info, s, "BOWLING AVG", "bowlingAverage", "bowlAvg");
                Add(info, s, "ECONOMY", "economy", "econ");
                Add(info, s, "BEST", "bestBowling", "bbi");
            }
            if (info.Stats.Count == 0)
            {
                var f = m.BowlingFigures.FirstOrDefault(b => b.Player.Id == p.Id);
                var balls = f?.Balls ?? 0;
                info.Stats.Add(new CardStat("OVERS", Cricket.FormatOvers(balls)));
                info.Stats.Add(new CardStat("RUNS", (f?.Runs ?? 0).ToString()));
                info.Stats.Add(new CardStat("WICKETS", (f?.Wickets ?? 0).ToString()));
                info.Stats.Add(new CardStat("ECONOMY", balls > 0 ? (f!.Runs * 6.0 / balls).ToString("0.00", CultureInfo.InvariantCulture) : "-"));
                info.Stats.Add(new CardStat("BOWLING", p.Bowling.ToString()));
            }
            return info;
        }

        /// <summary>The out batter walking off: how he went, and his innings.</summary>
        public static PlayerCardInfo Dismissed(ChaseBatter b, Franchise side)
        {
            var info = Card(b.Player, b.HowOut ?? "out", side);
            info.Stats.Add(new CardStat("RUNS", b.Runs.ToString()));
            info.Stats.Add(new CardStat("BALLS", b.Balls.ToString()));
            info.Stats.Add(new CardStat("4s", b.Fours.ToString()));
            info.Stats.Add(new CardStat("6s", b.Sixes.ToString()));
            info.Stats.Add(new CardStat("S/R", b.Balls > 0 ? (b.Runs * 100.0 / b.Balls).ToString("0.00", CultureInfo.InvariantCulture) : "0.00"));
            return info;
        }

        private static PlayerCardInfo Card(Player p, string role, Franchise side) => new PlayerCardInfo
        {
            Player = p,
            Role = role,
            Team = side.ShortName,
            Tint = UiKit.ParseHex(side.PrimaryColor),
        };

        /// <summary>The first of <paramref name="keys"/> the block has, as a cell.</summary>
        private static void Add(PlayerCardInfo info, JObject s, string label, params string[] keys)
        {
            foreach (var k in keys)
            {
                var v = s[k];
                if (v == null || v.Type == JTokenType.Null) continue;
                var text = v.Type == JTokenType.Float ? ((double)v).ToString("0.00", CultureInfo.InvariantCulture) : v.ToString();
                info.Stats.Add(new CardStat(label, text));
                return;
            }
        }

        /// <summary>"05_cover_drive" → "COVER DRIVE"; "lhb_44_lofted_pull" → "LOFTED PULL".</summary>
        public static string ShotName(string? clip)
        {
            if (string.IsNullOrEmpty(clip)) return "";
            var parts = clip!.Split('_').Where(x => x.Length > 0).ToList();
            if (parts.Count > 0 && (parts[0] == "lhb" || parts[0] == "rhb")) parts.RemoveAt(0);
            if (parts.Count > 0 && parts[0].All(char.IsDigit)) parts.RemoveAt(0);
            var name = string.Join(" ", parts).ToUpperInvariant();
            return name.Replace("DILSCOOP", "SCOOP").Replace("STEER DAB", "DAB");
        }

        public static string Grade(Timing t) => t switch
        {
            Timing.Perfect => "PERFECT",
            Timing.Good => "GOOD",
            Timing.Early => "EARLY",
            Timing.Late => "LATE",
            Timing.VeryEarly => "TOO EARLY",
            Timing.VeryLate => "TOO LATE",
            _ => "",
        };

        public static Color GradeColor(Timing t) => t switch
        {
            Timing.Perfect => new Color(0.45f, 1f, 0.55f),
            Timing.Good => new Color(0.75f, 0.95f, 0.45f),
            Timing.Early or Timing.Late => new Color(1f, 0.78f, 0.3f),
            _ => new Color(1f, 0.45f, 0.4f),
        };

        /// <summary>The wicket graphic's word for a dismissal.</summary>
        public static string Dismissal(string? howOut) => howOut switch
        {
            "bowled" => "BOWLED",
            "lbw" => "LBW",
            "caught" or "caught behind" or "caught and bowled" => "CAUGHT",
            "run out" => "RUN OUT",
            "stumped" => "STUMPED",
            "hit wicket" => "HIT WICKET",
            null => "OUT",
            _ => howOut.ToUpperInvariant(),
        };
    }
}
