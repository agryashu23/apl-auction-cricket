using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Apl.Client.Online
{
    /// <summary>
    /// The player's lifetime auction totals, kept on this device only (like SoloSave):
    /// auctions played, wins, total points and best finish. Each finished auction is counted
    /// once (its id is remembered), so a rejoin or a second results screen never double-counts.
    /// </summary>
    public sealed class RoomCareer
    {
        public int Played, Wins, TotalPoints;

        /// <summary>Best finishing place (1 = won); 0 = none yet.</summary>
        public int BestRank;

        /// <summary>Room auctions only (the rest were solo).</summary>
        public int RoomPlayed;

        private readonly List<string> _counted = new List<string>();

        private static string FilePath => Path.Combine(Application.persistentDataPath, "auction_career.json");

        private static RoomCareer? _cache;

        public static RoomCareer Load()
        {
            if (_cache != null) return _cache;
            var c = new RoomCareer();
            try
            {
                if (File.Exists(FilePath))
                {
                    var j = JObject.Parse(File.ReadAllText(FilePath));
                    c.Played = (int?)j["played"] ?? 0;
                    c.Wins = (int?)j["wins"] ?? 0;
                    c.TotalPoints = (int?)j["points"] ?? 0;
                    c.BestRank = (int?)j["bestRank"] ?? 0;
                    c.RoomPlayed = (int?)j["roomPlayed"] ?? 0;
                    if (j["counted"] is JArray ids) c._counted.AddRange(ids.Select(x => (string?)x ?? "").Where(x => x.Length > 0));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL room] ignoring a broken career file: {ex.Message}");
            }
            return _cache = c;
        }

        /// <summary>Counts one finished auction (once per <paramref name="auctionId"/>). True if it was new.</summary>
        public static bool Record(string auctionId, int rank, int points, bool room)
        {
            var c = Load();
            if (auctionId.Length > 0 && c._counted.Contains(auctionId)) return false;
            c.Played++;
            if (room) c.RoomPlayed++;
            if (rank == 1) c.Wins++;
            c.TotalPoints += Math.Max(0, points);
            if (rank > 0 && (c.BestRank == 0 || rank < c.BestRank)) c.BestRank = rank;
            if (auctionId.Length > 0)
            {
                c._counted.Add(auctionId);
                // Only the recent ids matter (a rejoin comes within minutes).
                if (c._counted.Count > 50) c._counted.RemoveRange(0, c._counted.Count - 50);
            }
            c.Write();
            Debug.Log($"[APL room] career: +1 auction (rank {rank}, {points} pts) -> played {c.Played}, wins {c.Wins}, points {c.TotalPoints}, best #{c.BestRank}");
            return true;
        }

        /// <summary>"12 AUCTIONS · 3 WINS · 4,210 PTS · BEST #1".</summary>
        public string Line() =>
            Played == 0
                ? "NO AUCTIONS YET"
                : $"{Played} AUCTION{(Played == 1 ? "" : "S")}  ·  {Wins} WIN{(Wins == 1 ? "" : "S")}  ·  {TotalPoints:N0} PTS  ·  BEST {(BestRank > 0 ? "#" + BestRank : "—")}";

        private void Write()
        {
            var json = new JObject
            {
                ["played"] = Played,
                ["wins"] = Wins,
                ["points"] = TotalPoints,
                ["bestRank"] = BestRank,
                ["roomPlayed"] = RoomPlayed,
                ["counted"] = new JArray(_counted),
            };
            try
            {
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json.ToString(Newtonsoft.Json.Formatting.None));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL room] could not save the career totals: {ex.Message}");
            }
        }
    }
}
