using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Modes
{
    /// <summary>
    /// Career XP for an auction (round 9): finishing it, where your squad ranks, and
    /// how your season goes — the same season the results screen simulates (same
    /// squads, same seed; read only, the engine is untouched). Shown on the rewards
    /// screen the next time the home page comes up (RewardCard.Pending).
    /// </summary>
    public static class AuctionRewards
    {
        public static Award? Record(Dataset data, AuctionEngine engine, string myTeam, long seasonSeed)
        {
            try
            {
                var ratings = Ratings.Rate(engine.Teams.Values, data.Rules.MaxOverseasInXI, engine.Format.MinSquad - 11, data.Rules.RatingFor(engine.Format));
                var rank = ratings.FirstOrDefault(r => r.TeamId == myTeam)?.Rank ?? 99;
                var squads = new OrderedMap<string, List<Player>>();
                foreach (var t in engine.Teams.Values) squads[t.Id] = t.Players;
                var season = Seasons.Simulate(squads, seasonSeed, data.Rules.MaxOverseasInXI);
                var place = season.Table.FindIndex(r => r.TeamId == myTeam) + 1;
                var items = new List<(int, string)> { (120, "Auction completed") };
                // Each line names its rank (rater r1 #3; solo-auction dev): squad rating vs the season sim.
                if (rank == 1) items.Add((100, "Squad rating rank #1 (best squad)"));
                else if (rank <= 3) items.Add((50, $"Squad rating rank #{rank}"));
                if (season.ChampionId == myTeam) items.Add((200, "Season sim: champions!"));
                else if (season.RunnerUpId == myTeam) items.Add((100, "Season sim: runners-up"));
                else if (place > 0 && place <= 4) items.Add((50, $"Season sim: playoffs (position #{place})"));
                // UI/UX dev (r1 §10): today's AUCTION goals this auction completed, paid with it (Modes/AuctionGoalFacts.cs).
                items.AddRange(AuctionGoalFacts.Apply(data, engine, myTeam));
                var award = Progression.RecordEvent(items.ToArray());
                RewardCard.Pending = (award, null, 0);
                Debug.Log($"[APL modes] auction over: {myTeam} rank #{rank}, table #{place}, champion {season.ChampionId}: +{award.Xp} XP (level {award.LevelBefore}->{award.LevelAfter})");
                return award;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL modes] no auction XP: {ex.Message}");
                return null;
            }
        }
    }
}
