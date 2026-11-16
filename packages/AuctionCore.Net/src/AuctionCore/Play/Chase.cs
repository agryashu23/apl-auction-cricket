using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    /// <summary>A batter's line in the chase.</summary>
    public sealed class ChaseBatter
    {
        public ChaseBatter(Player player) { Player = player; }

        public Player Player { get; }
        public int Runs, Balls, Fours, Sixes;
        public bool Batted;
        public string? HowOut;
    }

    /// <summary>A bowler's figures in the chase.</summary>
    public sealed class ChaseBowler
    {
        public ChaseBowler(Player player) { Player = player; }

        public Player Player { get; }
        public int Balls, Runs, Wickets, Wides, NoBalls, Dots;

        public string Overs => Cricket.FormatOvers(Balls);
    }

    /// <summary>
    /// A quick match: the opponent's innings simulated on the season engine,
    /// then the human chasing it ball by ball. The view asks for the next
    /// delivery, shows it, collects the shot and calls <see cref="Play"/>.
    /// </summary>
    public sealed partial class QuickMatch
    {
        private readonly Rng _rng;
        private List<Player> _plan;
        private Dictionary<string, ChaseBowler> _bowlers = new Dictionary<string, ChaseBowler>();
        private Delivery? _pending;
        private Trajectory? _pendingTrajectory;

        /// <summary>The sides in this innings (they swap at a full match's break, MatchFlow.cs).</summary>
        public TeamSheet Batting { get; private set; }
        public TeamSheet Bowling { get; private set; }
        public int Overs { get; }
        public Pitch Pitch { get; }

        /// <summary>The ground (null: the standard oval).</summary>
        public Venue? Venue { get; }

        /// <summary>
        /// The opponent's innings (simulated) — or, in a full match, the first
        /// innings as played (while it's on: the innings so far).
        /// </summary>
        public Innings FirstInnings => _first ?? Summary();
        private Innings? _first;

        /// <summary>There's a target (the chase; false in the first innings of a full match).</summary>
        public bool Chasing { get; private set; } = true;
        public int Target => Chasing ? FirstInnings.Runs + 1 : 0;

        public List<ChaseBatter> Order { get; private set; }
        public int Striker { get; private set; }
        public int NonStriker { get; private set; } = 1;
        private int _nextIn = 2;

        public int Runs { get; private set; }
        public int Wickets { get; private set; }
        public int Extras { get; private set; }
        public int LegalBalls { get; private set; }
        public List<BallResult> Log { get; private set; } = new List<BallResult>();

        /// <summary>The quick chase: the user (<paramref name="batting"/>) chases the opponent's simulated innings.</summary>
        public QuickMatch(TeamSheet batting, TeamSheet bowling, int overs, long seed, Venue? venue = null)
            : this(batting, bowling, overs, seed, venue, batting)
        {
            var sim = new MatchSimulator(new Rng(seed ^ 0x2545F491));
            _first = sim.SimulateInnings(bowling, batting, Pitch, overs, global::Apl.Core.Play.Bowling.PlanOvers(batting.XI, overs), ground: venue?.SizeFactor ?? 1);
        }

        /// <summary>Common set-up; a full match (<see cref="Full"/>) starts here with no target.</summary>
        private QuickMatch(TeamSheet batting, TeamSheet bowling, int overs, long seed, Venue? venue, TeamSheet user)
        {
            Venue = venue;
            _rng = new Rng(seed);
            Batting = batting;
            Bowling = bowling;
            UserTeam = user;
            Overs = overs;
            Pitch = PitchFor(venue?.PickPitch(_rng) ?? (Pitch)_rng.PickWeighted(new[] { 0.4, 0.25, 0.175, 0.175 }));
            Order = Cricket.BattingOrder(batting.XI).Select(p => new ChaseBatter(p)).ToList();
            Order[0].Batted = Order[1].Batted = true;
            _plan = global::Apl.Core.Play.Bowling.PlanOvers(bowling.XI, overs);
        }

        public int Over => LegalBalls / 6;
        public int BallInOver => LegalBalls % 6;
        public int BallsLeft => Overs * 6 - LegalBalls;
        public int RunsNeeded => Chasing ? Math.Max(0, Target - Runs) : 0;
        public bool Won => Chasing && Runs >= Target;
        public bool Finished => Won || Wickets >= Order.Count - 1 || BallsLeft <= 0;

        /// <summary>0 powerplay, 1 middle, 2 death — scaled to the match length.</summary>
        public int Phase => (int)Cricket.PhaseOf(Math.Min(19, Over * 20 / Overs));

        /// <summary>This over's bowler: the one the user picked (bowling), else the plan's (see MatchFlow.cs).</summary>
        /// <remarks>Once the innings is over it stays the man who bowled the last ball (the bar freezes on the
        /// final over: no "next over" bowler after the winning runs, round 11).</remarks>
        public Player CurrentBowler => Finished && Log.Count > 0 ? CurrentBowlerOf(Log[Log.Count - 1]) : BowlerFor(Over);
        public ChaseBatter OnStrike => Order[Striker];
        public ChaseBatter OffStrike => Order[NonStriker];

        /// <summary>The field for this ball: the user's preset when he bowls, else the captain's; with the side's fielding and keeping.</summary>
        public Field Field
        {
            get
            {
                var spin = CurrentBowler.BowlType == BowlType.Spin;
                var preset = FieldPresetNow;
                // The catchers stand by the keeper as he stands this over; AUTO is the field for this kind of bowler.
                var f = Field.For(Phase, spin, Venue, OnStrike.Player.LeftHanded, preset, KeeperUp, Field.KindOf(CurrentBowler));
                f.Fielding = Bowling.XI.Average(p => p.Fielding);
                f.Keeping = Bowling.XI.Max(p => p.Keeping);
                return f;
            }
        }

        public IReadOnlyCollection<ChaseBowler> BowlingFigures => _bowlers.Values;

        /// <summary>The ball about to be bowled (the same one until it's played): the AI bowler's, unless the user's set it with <see cref="Bowl"/>.</summary>
        public Trajectory NextDelivery()
        {
            if (_pendingTrajectory != null) return _pendingTrajectory;
            BackingUp();
            _pending = global::Apl.Core.Play.Bowling.Bowl(CurrentBowler, Phase, _rng, OnStrike.Player.LeftHanded);
            // He knows the limit: two bouncers gone, he mostly keeps the next one down (Extras.cs).
            AiBouncerDiscipline();
            ForcedNoBall();
            return _pendingTrajectory = new Trajectory(OnPitch(_pending));
        }

        /// <summary>Plays the pending delivery with the batter's input and moves the game on.</summary>
        /// <remarks>
        /// The runs are settled at once: the engine's safe runs for the user's
        /// side (his RUN / STAY reworks them with <see cref="SettleRuns"/>), the
        /// AI's own call for an AI side. An AI side reviews an lbw it doesn't like
        /// here; the user's review is <see cref="Review"/>.
        /// </remarks>
        public BallResult Play(ShotInput shot)
        {
            if (Finished) throw new InvalidOperationException("The innings is over");
            var traj = NextDelivery();
            var bowler = CurrentBowler;
            // This ball's field, kept for naming the fielders in the dismissal (a rework of the run is the same ball).
            _ballField = Field;
            FixBallSlots(_ballField);
            var result = global::Apl.Core.Play.Batting.Resolve(OnStrike.Player, bowler, _pending!, traj, shot, _ballField, _rng);
            _pending = null;
            _pendingTrajectory = null;
            TestNoBall = false;
            result.FreeHit = FreeHit;
            result.Facing = OnStrike.Player;
            if (result.Running != null)
            {
                // The user's side (owner, 2026-11-13: "AUTO running shouldn't have run outs — only MANUAL"): AUTO takes the
                // safe runs and never one the race would run him out on — one fewer until it stands (the same draws: a
                // re-settle replays the race). MANUAL's RUN / CANCEL / DIVE reworks this (SettleRuns) and can be run out.
                // The AI's side (and the test runs' autoplay, UserRunsLikeAi) runs on its own judgement: tight runs
                // chanced, misjudged now and then.
                var safeOnly = UserBatting && UserSafeRunsOnly && !UserRunsLikeAi;
                var go = safeOnly ? result.Running.Safe : global::Apl.Core.Play.Running.AiAttempt(result.Running, Aggression(), _rng);
                global::Apl.Core.Play.Running.Settle(result, go, _rng);
                while (safeOnly && result.RunOut && go > 0) global::Apl.Core.Play.Running.Settle(result, --go, _rng);
            }
            AiReview(result);
            _memo = Snapshot();
            Apply(result, bowler);
            return result;
        }

        /// <summary>The ball into the book: the free-hit reprieve, the score, the figures, who's on strike.</summary>
        private void Apply(BallResult r, Player bowler)
        {
            Log.Add(r);
            // Off a no-ball or a free hit he can only be run out.
            if (r.Wicket && (r.NoBall || r.FreeHit) && r.HowOut != "run out")
            {
                r.Reprieve = r.HowOut;
                r.Wicket = false;
                r.HowOut = null;
                if (r.Referral != TvCheck.RunOut) r.Referral = TvCheck.None;
            }
            if (!_bowlers.TryGetValue(bowler.Id, out var figures)) _bowlers[bowler.Id] = figures = new ChaseBowler(bowler);
            Runs += r.Runs;
            Extras += r.Extras;
            // Byes and leg byes aren't the bowler's.
            figures.Runs += r.Runs - r.Byes - r.LegByes;
            if (r.Wide) figures.Wides++;
            if (r.NoBall) figures.NoBalls++;
            var striker = OnStrike;
            if (r.Legal)
            {
                LegalBalls++;
                figures.Balls++;
                if (r.Runs == 0) figures.Dots++;
            }
            // A no-ball is a ball faced; a wide isn't.
            if (!r.Wide && !r.DeadBall)
            {
                striker.Balls++;
                striker.Runs += r.BatRuns;
                if (r.Four && !r.ExtrasBoundary) striker.Fours++;
                if (r.Six) striker.Sixes++;
            }

            if (r.Wicket)
            {
                Wickets++;
                if (r.HowOut != "run out") figures.Wickets++;
                var strikerOut = !r.RunOut || r.RunOutStriker;
                var outAt = strikerOut ? Striker : NonStriker;
                Order[outAt].HowOut = DismissalText(r, bowler);
                r.OutBatter = Order[outAt].Player;
                var next = _nextIn < Order.Count ? _nextIn++ : outAt;
                Order[next].Batted = true;
                if (r.RunOut)
                {
                    // The survivor made his ground at the other end; the new man takes the end the wicket fell at.
                    var survivor = strikerOut ? NonStriker : Striker;
                    if (r.RunOutAtKeeperEnd) (Striker, NonStriker) = (next, survivor);
                    else (Striker, NonStriker) = (survivor, next);
                }
                // Caught, bowled…: the new batter takes strike.
                else Striker = next;
            }
            else if (r.Ran % 2 == 1)
            {
                (Striker, NonStriker) = (NonStriker, Striker);
            }
            // A no-ball's next ball is a free hit (and stays one through a wide).
            if (r.NoBall) FreeHit = true;
            else if (r.Legal) FreeHit = false;
            // End of the over: they swap ends.
            if (r.Legal && BallInOver == 0 && !Finished) (Striker, NonStriker) = (NonStriker, Striker);
        }

        private Field? _ballField;

        /// <summary>Test runs (the client's autoplay): the user's side calls its runs as the AI does, not the safe ones.</summary>
        public bool UserRunsLikeAi { get; set; }

        /// <summary>
        /// The user's AUTO running (owner, 2026-11-13, on by default): only the safe runs, never run out — a run the race
        /// would run him out on isn't taken. Off: the user's side runs on its own judgement, as the AI's does.
        /// </summary>
        public bool UserSafeRunsOnly { get; set; } = true;

        /// <summary>
        /// The scorecard line, with the fielders by NAME (round 11: run outs read "run out (slip)" while the
        /// keeper broke the stumps). A run out credits the thrower and the man who took it and broke the
        /// wicket ("run out (Kokk/†Sammsun)"); a direct hit, the thrower alone.
        /// </summary>
        private string DismissalText(BallResult r, Player bowler)
        {
            var keeper = FieldingKeeper;
            string Who(string position) => position == "keeper" ? "†" + keeper.Name : PlayerAt(position, bowler)?.Name ?? position;
            switch (r.HowOut)
            {
                case "bowled": return $"b {bowler.Name}";
                case "lbw": return $"lbw b {bowler.Name}";
                case "caught behind": return $"c {Who(r.Fielder.Length > 0 ? r.Fielder : "keeper")} b {bowler.Name}";
                case "caught": return r.Fielder == "bowler" ? $"c & b {bowler.Name}" : $"c {Who(r.Fielder)} b {bowler.Name}";
                case "stumped": return $"st †{keeper.Name} b {bowler.Name}";
                case "hit wicket": return $"hit wicket b {bowler.Name}";
                case "run out":
                    var thrower = Who(r.Fielder.Length > 0 ? r.Fielder : "keeper");
                    var breaker = r.RunOutAtKeeperEnd ? "†" + keeper.Name : bowler.Name;
                    return r.DirectHit || thrower == breaker || r.Fielder == "bowler" ? $"run out ({thrower})" : $"run out ({thrower}/{breaker})";
                default: return r.HowOut ?? "";
            }
        }

        /// <summary>The fielding side's keeper (the gloves; the client stands the same man behind the stumps).</summary>
        public Player FieldingKeeper => Bowling.XI.FirstOrDefault(p => p.IsKeeper) ?? Bowling.XI.Last();

        /// <summary>
        /// Who stands at <paramref name="position"/> ("cover", "keeper", "bowler", …) this ball: the
        /// fielders are the XI less the bowler and keeper, in XI order, one per position in the field's
        /// order — the same placing the match view uses. Null if the field has no such position.
        /// </summary>
        public Player? PlayerAt(string position, Player? bowler = null)
        {
            bowler ??= CurrentBowler;
            var keeper = FieldingKeeper;
            if (position == "keeper") return keeper;
            if (position == "bowler") return bowler;
            var field = _ballField ?? Field;
            var i = -1;
            for (var k = 0; k < field.Fielders.Count; k++) if (field.Fielders[k].Name == position) { i = k; break; }
            if (i < 0) return null;
            var others = Bowling.XI.Where(p => p.Id != bowler.Id && p.Id != keeper.Id).ToList();
            var slot = BallSlotOf(i);
            return slot < others.Count ? others[slot] : null;
        }

        // ------------------------------------------------- the user picks his batters

        /// <summary>The batters still in the hut, next man in first.</summary>
        public IReadOnlyList<ChaseBatter> Waiting => Order.Skip(_nextIn).ToList();

        /// <summary>
        /// The batter who came in at the last wicket and hasn't faced yet (the
        /// one <see cref="SendIn"/> can swap), or null.
        /// </summary>
        public ChaseBatter? Incoming
        {
            get
            {
                if (_nextIn <= 2) return null;
                var b = Order[_nextIn - 1];
                return b.Balls == 0 && b.HowOut == null ? b : null;
            }
        }

        /// <summary>
        /// Before the first ball: <paramref name="striker"/> and <paramref name="nonStriker"/>
        /// open (moved to the top of the order, everyone else keeping theirs).
        /// </summary>
        public void SetOpeners(Player striker, Player nonStriker)
        {
            if (Log.Count > 0) throw new InvalidOperationException("The innings has started");
            var a = Order.FindIndex(b => b.Player.Id == striker.Id);
            var c = Order.FindIndex(b => b.Player.Id == nonStriker.Id);
            if (a < 0 || c < 0 || a == c) throw new ArgumentException("Two different batters from this side");
            var first = Order[a];
            var second = Order[c];
            Order.Remove(first);
            Order.Remove(second);
            Order.Insert(0, first);
            Order.Insert(1, second);
            foreach (var b in Order) b.Batted = false;
            Order[0].Batted = Order[1].Batted = true;
            Striker = 0;
            NonStriker = 1;
        }

        /// <summary>
        /// The new batter after a wicket is the user's pick: <paramref name="p"/>
        /// (one still waiting) takes the place of the man who just came in, who
        /// goes back to the hut — the batting order moves him up. False if there's
        /// no one to swap (no wicket yet, or the new man has faced).
        /// </summary>
        public bool SendIn(Player p)
        {
            var incoming = Incoming;
            if (incoming == null) return false;
            if (incoming.Player.Id == p.Id) return true;
            var j = Order.FindIndex(_nextIn, b => b.Player.Id == p.Id);
            if (j < 0) return false;
            var k = _nextIn - 1;
            var chosen = Order[j];
            Order[j] = incoming;
            Order[k] = chosen;
            incoming.Batted = false;
            chosen.Batted = true;
            return true;
        }

        /// <summary>"won by 3 wickets (4 balls left)" / "lost by 12 runs".</summary>
        public string ResultText()
        {
            if (Won)
            {
                var wl = Order.Count - 1 - Wickets;
                return $"won by {wl} {(wl == 1 ? "wicket" : "wickets")}" + (BallsLeft > 0 ? $" ({BallsLeft} {(BallsLeft == 1 ? "ball" : "balls")} left)" : "");
            }
            var short1 = Target - 1 - Runs;
            return short1 == 0 ? "tied" : $"lost by {short1} {(short1 == 1 ? "run" : "runs")}";
        }

        /// <summary>A team's XI from last season's real squad (the dataset's previous team).</summary>
        public static TeamSheet LastSeasonXI(Dataset data, string teamId)
        {
            var squad = data.Players.Where(p => p.PreviousTeam == teamId).ToList();
            var xi = Lineups.PickBestXI(squad, data.Rules.MaxOverseasInXI).XI;
            return new TeamSheet(teamId, xi);
        }
    }
}
