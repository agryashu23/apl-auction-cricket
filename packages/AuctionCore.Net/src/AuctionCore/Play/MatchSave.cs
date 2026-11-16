using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    /// <summary>
    /// A match in progress, as the book stands at the end of a ball: enough to
    /// carry it on after the app's been closed (MatchSave.Restore.cs). The ball
    /// in hand isn't kept (it's bowled afresh), and the dice start again from a
    /// new seed; everything that's been scored stays exactly as it was.
    /// Plain fields, player ids for players, so any JSON writer can keep it.
    /// </summary>
    public sealed class MatchSave
    {
        public int Version = 1;
        public string UserTeam = "", BattingTeam = "", BowlingTeam = "";
        public List<string> BattingXI = new List<string>(), BowlingXI = new List<string>();
        public int Overs;
        public string? Venue;
        public int Pitch;
        public bool TwoInnings, Chasing;
        public int InningsNumber;

        /// <summary>The first innings as the book has it (the simulated one in a quick chase); null while it's on.</summary>
        public InningsSave? First;

        public List<BatterSave> Order = new List<BatterSave>();
        public int Striker, NonStriker, NextIn;
        public int Runs, Wickets, Extras, LegalBalls;
        public List<BowlerSave> Bowlers = new List<BowlerSave>();
        public List<BallSave> Log = new List<BallSave>();

        /// <summary>The bowling plan (a bowler's id per over) and the overs the user chose a bowler for.</summary>
        public List<string> Plan = new List<string>();
        public Dictionary<int, string> Chosen = new Dictionary<int, string>();
        public bool FreeHit;
        public int Preset;
        public int BattingReviews = 1, FieldingReviews = 1;

        /// <summary>A full match after the break: the first innings' own book.</summary>
        public List<BatterSave>? FirstOrder;
        public List<BowlerSave>? FirstBowling;
        public List<BallSave>? FirstLog;

        /// <summary>The impact player (ImpactPlayer.cs): each side's named substitutes, and the swap made (off, on).</summary>
        public Dictionary<string, List<string>> Substitutes = new Dictionary<string, List<string>>();
        public Dictionary<string, string[]> Impact = new Dictionary<string, string[]>();
    }

    public sealed class BatterSave
    {
        public string Id = "";
        public int Runs, Balls, Fours, Sixes;
        public bool Batted;
        public string? HowOut;
    }

    public sealed class BowlerSave
    {
        public string Id = "";
        public int Balls, Runs, Wickets, Wides, NoBalls, Dots;
    }

    /// <summary>One ball, as the score strip, the over card and the scorecard read it.</summary>
    public sealed class BallSave
    {
        public int Runs, Byes, LegByes, Penalty, Ran, Bouncer;
        public bool Wide, NoBall, Wicket, Four, Six, ExtrasBoundary, RunOut;
        public string? HowOut;
        public string Fielder = "";
        public string? OutBatter;
    }

    public sealed class InningsSave
    {
        public string Batting = "", Bowling = "";
        public int Runs, Wickets, LegalBalls, Extras;
        public List<BatterSave> Batters = new List<BatterSave>();
        public List<BowlerSave> Bowlers = new List<BowlerSave>();
    }

    public sealed partial class QuickMatch
    {
        /// <summary>The book as it stands (call it between balls).</summary>
        public MatchSave Save() => new MatchSave
        {
            UserTeam = UserTeam.TeamId,
            BattingTeam = Batting.TeamId,
            BowlingTeam = Bowling.TeamId,
            BattingXI = Batting.XI.Select(p => p.Id).ToList(),
            BowlingXI = Bowling.XI.Select(p => p.Id).ToList(),
            Overs = Overs,
            Venue = Venue?.Id,
            Pitch = (int)Pitch,
            TwoInnings = TwoInnings,
            Chasing = Chasing,
            InningsNumber = InningsNumber,
            First = _first == null ? null : SaveOf(_first),
            Order = Order.Select(SaveOf).ToList(),
            Striker = Striker,
            NonStriker = NonStriker,
            NextIn = _nextIn,
            Runs = Runs,
            Wickets = Wickets,
            Extras = Extras,
            LegalBalls = LegalBalls,
            Bowlers = _bowlers.Values.Select(SaveOf).ToList(),
            Log = Log.Select(SaveOf).ToList(),
            Plan = _plan.Select(p => p.Id).ToList(),
            Chosen = _chosen.ToDictionary(kv => kv.Key, kv => kv.Value.Id),
            FreeHit = FreeHit,
            Preset = (int)Preset,
            BattingReviews = BattingReviews,
            FieldingReviews = FieldingReviews,
            FirstOrder = FirstOrder?.Select(SaveOf).ToList(),
            FirstBowling = FirstBowling?.Select(SaveOf).ToList(),
            FirstLog = FirstLog?.Select(SaveOf).ToList(),
            Substitutes = _subs.ToDictionary(kv => kv.Key, kv => kv.Value.Select(p => p.Id).ToList()),
            Impact = _impact.ToDictionary(kv => kv.Key, kv => new[] { kv.Value.off.Id, kv.Value.on.Id }),
        };

        private static BatterSave SaveOf(ChaseBatter b) => new BatterSave { Id = b.Player.Id, Runs = b.Runs, Balls = b.Balls, Fours = b.Fours, Sixes = b.Sixes, Batted = b.Batted, HowOut = b.HowOut };

        private static BowlerSave SaveOf(ChaseBowler b) => new BowlerSave { Id = b.Player.Id, Balls = b.Balls, Runs = b.Runs, Wickets = b.Wickets, Wides = b.Wides, NoBalls = b.NoBalls, Dots = b.Dots };

        private static BallSave SaveOf(BallResult r) => new BallSave
        {
            Runs = r.Runs, Byes = r.Byes, LegByes = r.LegByes, Penalty = r.Penalty, Ran = r.Ran, Bouncer = r.BouncerNumber,
            Wide = r.Wide, NoBall = r.NoBall, Wicket = r.Wicket, Four = r.Four, Six = r.Six, ExtrasBoundary = r.ExtrasBoundary, RunOut = r.RunOut,
            HowOut = r.HowOut, Fielder = r.Fielder, OutBatter = r.OutBatter?.Id,
        };

        private static InningsSave SaveOf(Innings i) => new InningsSave
        {
            Batting = i.BattingTeamId, Bowling = i.BowlingTeamId, Runs = i.Runs, Wickets = i.Wickets, LegalBalls = i.LegalBalls, Extras = i.Extras,
            Batters = i.Batting.Select(b => new BatterSave { Id = b.Player.Id, Runs = b.Runs, Balls = b.Balls, Fours = b.Fours, Sixes = b.Sixes, Batted = b.Batted, HowOut = b.Dismissal }).ToList(),
            Bowlers = i.Bowling.Select(b => new BowlerSave { Id = b.Player.Id, Balls = b.Balls, Runs = b.Runs, Wickets = b.Wickets, Wides = b.Wides, NoBalls = b.NoBalls, Dots = b.Dots }).ToList(),
        };
    }
}
