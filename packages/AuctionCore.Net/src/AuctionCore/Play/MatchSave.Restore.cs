using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    public sealed partial class QuickMatch
    {
        /// <summary>
        /// A saved match back as it was at the end of its last ball, the dice
        /// starting afresh from <paramref name="seed"/>. Throws if the save names
        /// players this data doesn't have (the caller drops such a save).
        /// </summary>
        public static QuickMatch Restore(MatchSave s, Dataset data, long seed) => new QuickMatch(s, data, seed);

        private QuickMatch(MatchSave s, Dataset data, long seed)
        {
            var players = data.Players.ToDictionary(p => p.Id);
            Player P(string id) => players.TryGetValue(id, out var p) ? p : throw new ArgumentException("Unknown player " + id);
            _rng = new Rng(seed);
            Overs = s.Overs;
            Venue = Venue.Find(s.Venue);
            Pitch = (Pitch)s.Pitch;
            Batting = new TeamSheet(s.BattingTeam, s.BattingXI.Select(P).ToList());
            Bowling = new TeamSheet(s.BowlingTeam, s.BowlingXI.Select(P).ToList());
            UserTeam = s.UserTeam == Batting.TeamId ? Batting : Bowling;
            TwoInnings = s.TwoInnings;
            Chasing = s.Chasing;
            InningsNumber = s.InningsNumber;
            if (s.First != null) _first = InningsOf(s.First, P);
            Order = s.Order.Select(b => BatterOf(b, P)).ToList();
            Striker = s.Striker;
            NonStriker = s.NonStriker;
            _nextIn = s.NextIn;
            Runs = s.Runs;
            Wickets = s.Wickets;
            Extras = s.Extras;
            LegalBalls = s.LegalBalls;
            _bowlers = s.Bowlers.ToDictionary(b => b.Id, b => BowlerOf(b, P));
            Log = s.Log.Select(b => BallOf(b, P)).ToList();
            _plan = s.Plan.Select(P).ToList();
            foreach (var kv in s.Chosen) _chosen[kv.Key] = P(kv.Value);
            FreeHit = s.FreeHit;
            Preset = (FieldPreset)s.Preset;
            BattingReviews = s.BattingReviews;
            FieldingReviews = s.FieldingReviews;
            FirstOrder = s.FirstOrder?.Select(b => BatterOf(b, P)).ToList();
            FirstBowling = s.FirstBowling?.Select(b => BowlerOf(b, P)).ToList();
            FirstLog = s.FirstLog?.Select(b => BallOf(b, P)).ToList();
            foreach (var kv in s.Substitutes) _subs[kv.Key] = kv.Value.Select(P).ToList();
            foreach (var kv in s.Impact) if (kv.Value.Length == 2) _impact[kv.Key] = (P(kv.Value[0]), P(kv.Value[1]));
        }

        private static ChaseBatter BatterOf(BatterSave b, Func<string, Player> p) =>
            new ChaseBatter(p(b.Id)) { Runs = b.Runs, Balls = b.Balls, Fours = b.Fours, Sixes = b.Sixes, Batted = b.Batted, HowOut = b.HowOut };

        private static ChaseBowler BowlerOf(BowlerSave b, Func<string, Player> p) =>
            new ChaseBowler(p(b.Id)) { Balls = b.Balls, Runs = b.Runs, Wickets = b.Wickets, Wides = b.Wides, NoBalls = b.NoBalls, Dots = b.Dots };

        /// <summary>A ball back in the book: the score's facts only (no flight to replay).</summary>
        private static BallResult BallOf(BallSave b, Func<string, Player> p) => new BallResult
        {
            Runs = b.Runs, Byes = b.Byes, LegByes = b.LegByes, Penalty = b.Penalty, Ran = b.Ran, BouncerNumber = b.Bouncer,
            Wide = b.Wide, NoBall = b.NoBall, Wicket = b.Wicket, Four = b.Four, Six = b.Six, ExtrasBoundary = b.ExtrasBoundary, RunOut = b.RunOut,
            HowOut = b.HowOut, Fielder = b.Fielder, OutBatter = b.OutBatter != null ? p(b.OutBatter) : null,
        };

        private static Innings InningsOf(InningsSave s, Func<string, Player> p)
        {
            var inn = new Innings(s.Batting, s.Bowling, s.Batters.Select(b =>
                new BatterLine(p(b.Id)) { Runs = b.Runs, Balls = b.Balls, Fours = b.Fours, Sixes = b.Sixes, Batted = b.Batted, Dismissal = b.HowOut }).ToList())
            {
                Runs = s.Runs, Wickets = s.Wickets, LegalBalls = s.LegalBalls, Extras = s.Extras,
            };
            inn.Bowling.AddRange(s.Bowlers.Select(b => new BowlerLine(p(b.Id)) { Balls = b.Balls, Runs = b.Runs, Wickets = b.Wickets, Wides = b.Wides, NoBalls = b.NoBalls, Dots = b.Dots }));
            return inn;
        }
    }
}
