using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Modes
{
    /// <summary>
    /// What a finished auction did for the human team (UI/UX dev, rater r1 §8/§10): the facts the
    /// auction daily goals count (DailyGoals.ApplyAuction), and a short summary the rewards page
    /// shows so it isn't two near-empty panels (RewardCard). Read only: engine results and the
    /// deterministic AuctionAwards scores; nothing in the engine changes.
    /// </summary>
    public static class AuctionGoalFacts
    {
        /// <summary>The last auction's summary for the rewards page, or null.</summary>
        public sealed class Summary
        {
            public string TeamId = "";
            public int Bought, Spent, PurseLeft, SquadSize, Steals, Rank, Teams, Points;
            public string? BiggestBuy, BestSteal;
            public readonly List<string> GoalsDone = new List<string>();
        }

        public static Summary? Last;

        public static DailyGoals.AuctionFacts From(Dataset data, AuctionEngine engine, string me, bool room)
        {
            var f = new DailyGoals.AuctionFacts { Room = room, Mega = engine.Format.Id == "mega" };
            var s = new Summary { TeamId = me, Teams = engine.Teams.Count };
            foreach (var r in engine.Results)
            {
                if (r.TeamId != me || r.Price == null) continue;
                f.Bought++;
                if (r.ViaRtm) f.RtmUsed++;
                else if (r.Price.Value <= r.Player.BasePrice) f.BoughtAtBase++;
                // A war: a rival was in it (outbid) and it went for ₹10 Cr (1000 L) or more.
                if (r.Price.Value >= 1000 && r.OutbidTeamId != null && r.BidCount >= 3) f.WarsOver10Cr++;
            }
            if (engine.Teams.TryGetValue(me, out var team))
            {
                f.PurseLeftLakhs = team.Purse;
                s.Spent = team.Spent;
                s.PurseLeft = team.Purse;
                s.SquadSize = team.Size;
                var big = team.Squad.Where(p => p.Kind == PurchaseKind.Auction || p.Kind == PurchaseKind.Rtm).OrderByDescending(p => p.Price).FirstOrDefault();
                if (big != null) s.BiggestBuy = $"{big.Player.Name} · {Money.Format(big.Price)}";
            }
            s.Bought = f.Bought;
            try
            {
                var report = AuctionAwards.Compute(engine, data.Rules);
                var mine = report.For(me);
                f.Place = mine.Rank;
                f.Steals = mine.StealCount;
                s.Rank = mine.Rank;
                s.Points = mine.Total;
                s.Steals = mine.StealCount;
                var steal = mine.StealList.OrderByDescending(x => x.Worth - x.Buy.Price).FirstOrDefault();
                if (steal.Buy != null) s.BestSteal = $"{steal.Buy.Player.Name} · {Money.Format(steal.Buy.Price)} (worth {Money.Format(steal.Worth)})";
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL goals] no auction scores: {ex.Message}");
            }
            Last = s;
            Debug.Log($"[APL goals] auction facts {me}: bought {f.Bought}, at base {f.BoughtAtBase}, wars>10Cr {f.WarsOver10Cr}, rtm {f.RtmUsed}, steals {f.Steals}, place #{f.Place}, room {f.Room}");
            return f;
        }

        /// <summary>The facts into today's goals: the XP lines for goals completed (also kept on <see cref="Last"/>).</summary>
        public static List<(int xp, string line)> Apply(Dataset data, AuctionEngine engine, string me)
        {
            try
            {
                var room = UnityEngine.Object.FindAnyObjectByType<Hall.HallDirector>()?.Room != null;
                var done = DailyGoals.ApplyAuction(From(data, engine, me, room));
                if (Last != null) Last.GoalsDone.AddRange(done.Select(d => d.line.Replace("Daily goal: ", "")));
                return done;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL goals] auction goals skipped: {ex.Message}");
                return new List<(int, string)>();
            }
        }
    }
}
