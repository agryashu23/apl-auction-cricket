using System;
using System.Collections.Generic;
using System.Linq;

namespace Apl.Core.Play
{
    /// <summary>A bowler card for the over: how many he's bowled, and whether he may bowl this one.</summary>
    public sealed class BowlerOption
    {
        public Player Player = null!;
        public int OversBowled, MaxOvers;
        public ChaseBowler? Figures;

        /// <summary>He bowled the last over, or he's bowled his quota.</summary>
        public bool BowledLast, Allowed;
    }

    /// <summary>
    /// The full match (both innings ball by ball, the user batting or bowling),
    /// the user's bowling (the bowler for each over, the delivery, the field),
    /// the AI batter, and the reworks of a ball already in the book: the user's
    /// running and his reviews.
    /// </summary>
    public sealed partial class QuickMatch
    {
        /// <summary>
        /// A full match of <paramref name="overs"/> overs a side: the toss decides
        /// <paramref name="userBatsFirst"/>. The first innings has no target; call
        /// <see cref="StartSecondInnings"/> when it's <see cref="Finished"/>.
        /// </summary>
        public static QuickMatch Full(TeamSheet user, TeamSheet opponent, int overs, long seed, Venue? venue, bool userBatsFirst)
        {
            var m = userBatsFirst
                ? new QuickMatch(user, opponent, overs, seed, venue, user)
                : new QuickMatch(opponent, user, overs, seed, venue, user);
            m.Chasing = false;
            m.TwoInnings = true;
            return m;
        }

        /// <summary>The user's side, and the other one.</summary>
        public TeamSheet UserTeam { get; }
        public TeamSheet Opponent => UserTeam == Batting ? Bowling : Batting;

        /// <summary>The user bats this innings (else he bowls and the AI bats).</summary>
        public bool UserBatting => UserTeam == Batting;

        /// <summary>Both innings are played ball by ball (else the quick chase of a simulated total).</summary>
        public bool TwoInnings { get; private set; }
        public int InningsNumber { get; private set; } = 1;

        /// <summary>The match is over (the chase's innings is finished).</summary>
        public bool MatchOver => Chasing && Finished;

        /// <summary>The next ball is a free hit (after a no-ball).</summary>
        public bool FreeHit { get; private set; }

        /// <summary>The user's field when he bowls (Auto: the captain's own for the phase).</summary>
        public FieldPreset Preset { get; set; } = FieldPreset.Auto;

        /// <summary>The first innings' book, kept at the break (null until then).</summary>
        public List<ChaseBatter>? FirstOrder { get; private set; }
        public List<BallResult>? FirstLog { get; private set; }
        public List<ChaseBowler>? FirstBowling { get; private set; }

        /// <summary>
        /// The break: the first innings goes into the book as <see cref="FirstInnings"/>,
        /// the sides swap, and the chase begins (target = their total + 1).
        /// </summary>
        public void StartSecondInnings()
        {
            if (!TwoInnings || InningsNumber != 1) throw new InvalidOperationException("Not a full match's first innings");
            _first = Summary();
            FirstOrder = Order;
            FirstLog = Log;
            FirstBowling = _bowlers.Values.ToList();
            (Batting, Bowling) = (Bowling, Batting);
            Order = Cricket.BattingOrder(Batting.XI).Select(p => new ChaseBatter(p)).ToList();
            Order[0].Batted = Order[1].Batted = true;
            Striker = 0;
            NonStriker = 1;
            _nextIn = 2;
            Runs = Wickets = Extras = LegalBalls = 0;
            Log = new List<BallResult>();
            _bowlers = new Dictionary<string, ChaseBowler>();
            _plan = global::Apl.Core.Play.Bowling.PlanOvers(Bowling.XI, Overs);
            _chosen.Clear();
            _pending = null;
            _pendingTrajectory = null;
            _memo = null;
            FreeHit = false;
            BattingReviews = FieldingReviews = 1;
            Preset = FieldPreset.Auto;
            ResetBreaks();
            Chasing = true;
            InningsNumber = 2;
            SuperOverSetUp();
        }

        /// <summary>This innings in the season engine's book (for the cards).</summary>
        public Innings Summary()
        {
            var inn = new Innings(Batting.TeamId, Bowling.TeamId, Order.Select(b => new BatterLine(b.Player)
            {
                Runs = b.Runs, Balls = b.Balls, Fours = b.Fours, Sixes = b.Sixes, Batted = b.Batted, Dismissal = b.HowOut,
            }).ToList())
            {
                Runs = Runs, Wickets = Wickets, LegalBalls = LegalBalls, Extras = Extras,
            };
            foreach (var f in _bowlers.Values) inn.Bowling.Add(new BowlerLine(f.Player) { Balls = f.Balls, Runs = f.Runs, Wickets = f.Wickets, Dots = f.Dots, Wides = f.Wides, NoBalls = f.NoBalls });
            return inn;
        }

        /// <summary>Who won the match (null: not over, or tied).</summary>
        public TeamSheet? Winner => !MatchOver ? null : Won ? Batting : Runs == Target - 1 ? null : Bowling;
        public bool UserWon => Winner == UserTeam;

        /// <summary>"BLR won by 12 runs" / "MBH won by 3 wickets (4 balls left)" / "Match tied" — by team id.</summary>
        public string MatchResultText()
        {
            if (!MatchOver) return "";
            var w = Winner;
            if (w == null) return "Match tied";
            if (w == Batting)
            {
                var wl = Order.Count - 1 - Wickets;
                return $"{w.TeamId} won by {wl} {(wl == 1 ? "wicket" : "wickets")}" + (BallsLeft > 0 ? $" ({BallsLeft} {(BallsLeft == 1 ? "ball" : "balls")} left)" : "");
            }
            var by = Target - 1 - Runs;
            return $"{w.TeamId} won by {by} {(by == 1 ? "run" : "runs")}";
        }

        // ------------------------------------------------------------ the bowlers

        private readonly Dictionary<int, Player> _chosen = new Dictionary<int, Player>();

        /// <summary>Overs one bowler may bowl in this match (a fifth, rounded up).</summary>
        public int MaxOversPerBowler => global::Apl.Core.Play.Bowling.MaxOvers(Overs);

        private int OversBy(Player p, int beforeOver)
        {
            var seq = Sequence(beforeOver);
            var n = 0;
            for (var o = 0; o < beforeOver; o++) if (seq[o].Id == p.Id) n++;
            return n;
        }

        /// <summary>The bowler of <paramref name="over"/>: the user's pick, else the plan's — or, when the user's picks have used the plan's man up, the best one still allowed.</summary>
        private Player BowlerFor(int over)
        {
            over = DartMath.Clamp(over, 0, Overs - 1);
            return Sequence(over + 1)[over];
        }

        /// <summary>Who bowls overs 0 … <paramref name="count"/> − 1, worked through in order.</summary>
        private List<Player> Sequence(int count)
        {
            var seq = new List<Player>();
            var used = new Dictionary<string, int>();
            for (var o = 0; o < count; o++)
            {
                Player? pick;
                if (!_chosen.TryGetValue(o, out pick))
                {
                    var last = o > 0 ? seq[o - 1] : null;
                    bool Ok(Player x) => x != last && (used.TryGetValue(x.Id, out var n) ? n : 0) < MaxOversPerBowler;
                    var planned = _plan[Math.Min(o, _plan.Count - 1)];
                    pick = _chosen.Count == 0 || Ok(planned) ? planned
                        : Bowling.XI.Where(Ok).OrderByDescending(x => x.IsBowlingOption).ThenByDescending(x => x.Bowling).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? planned;
                }
                seq.Add(pick);
                used[pick.Id] = (used.TryGetValue(pick.Id, out var u) ? u : 0) + 1;
            }
            return seq;
        }

        /// <summary>The cards for this over's choice: everyone in the XI, best bowlers first.</summary>
        public List<BowlerOption> BowlerOptions()
        {
            var last = Over > 0 ? BowlerFor(Over - 1) : null;
            return Bowling.XI.OrderByDescending(p => p.IsBowlingOption).ThenByDescending(p => p.Bowling).Select(p =>
            {
                var bowled = OversBy(p, Over);
                _bowlers.TryGetValue(p.Id, out var fig);
                var o = new BowlerOption { Player = p, OversBowled = bowled, MaxOvers = MaxOversPerBowler, Figures = fig, BowledLast = last != null && last.Id == p.Id };
                o.Allowed = !o.BowledLast && bowled < MaxOversPerBowler;
                return o;
            }).ToList();
        }

        /// <summary>The user picks this over's bowler (only before its first ball). False if he may not bowl it.</summary>
        public bool SetBowler(Player p)
        {
            // Not once the over's begun (a wide or no-ball first counts).
            if (BallInOver != 0 || (Log.Count > 0 && !Log[Log.Count - 1].Legal && LegalBallsBefore(Log[Log.Count - 1]) == LegalBalls) || !Bowling.XI.Contains(p)) return false;
            var opt = BowlerOptions().FirstOrDefault(o => o.Player.Id == p.Id);
            if (opt == null || !opt.Allowed) return false;
            _chosen[Over] = p;
            _pending = null;
            _pendingTrajectory = null;
            return true;
        }

        private int LegalBallsBefore(BallResult r)
        {
            var n = 0;
            foreach (var x in Log)
            {
                if (x == r) return n;
                if (x.Legal) n++;
            }
            return n;
        }

        // --------------------------------------------------------- the user bowls

        /// <summary>
        /// The user's ball: <paramref name="variation"/>, aimed to pitch at
        /// (<paramref name="pitchX"/>, <paramref name="pitchZ"/>) in the striker's
        /// frame (+x his off side; z metres from his stumps), with the release
        /// meter's <paramref name="release"/> error (0 perfect, ±1 the edge of the
        /// green). Becomes the pending delivery; <see cref="PlayAi"/> plays it.
        /// </summary>
        public Trajectory Bowl(Variation variation, double pitchX, double pitchZ, double release, bool aroundWicket = false)
        {
            BackingUp();
            _pending = global::Apl.Core.Play.Bowling.Deliver(CurrentBowler, variation, pitchX, pitchZ, release, Phase, _rng, OnStrike.Player.LeftHanded, aroundWicket);
            ForcedNoBall();
            return _pendingTrajectory = new Trajectory(OnPitch(_pending));
        }

        /// <summary>
        /// Test runs only (the client's evidence nudges, never a real match): the next ball is an
        /// overstep no-ball (a free hit follows). Cleared once that ball's played.
        /// </summary>
        public bool TestNoBall { get; set; }

        /// <summary>Test runs only: any lbw decision is reviewed by whichever side may (the DRS on film).</summary>
        public bool TestAlwaysReview { get; set; }

        private void ForcedNoBall()
        {
            // The T20 bouncer limit first (Extras.cs): a third bouncer in the over is a no-ball.
            BouncerRule();
            if (!TestNoBall || _pending == null) return;
            _pending.NoBall = true;
            _pending.Wide = false;
        }

        /// <summary>The state of the game for the batter's brain.</summary>
        public Situation Situation => new Situation(Runs, Wickets, BallsLeft, Overs, Chasing ? Target : (int?)null, OnStrike.Balls, FreeHit, Phase, Order.Count - 1);

        /// <summary>How hard the batting side is going (0–1): the AI's shots and its running.</summary>
        public double Aggression() => BatterBrain.Aggression(Situation);

        /// <summary>What the AI batter does with the pending ball (the tap time tells the view when he swings).</summary>
        public ShotInput AiShot() => BatterBrain.Choose(OnStrike.Player, CurrentBowler, NextDelivery(), Field, Situation, _rng);

        /// <summary>The AI batter plays the pending ball (the user's, or the AI's own when nobody set one).</summary>
        public BallResult PlayAi() => Play(AiShot());

        // ------------------------------------------------- reworking the last ball

        /// <summary>The book before the last ball went in, so the user's call (runs, review) can redo it.</summary>
        private Memo? _memo;

        private sealed class Memo
        {
            public int Runs, Wickets, Extras, LegalBalls, Striker, NonStriker, NextIn, BatReviews, FieldReviews, LogCount;
            public bool FreeHit;
            public (ChaseBatter b, int runs, int balls, int fours, int sixes, bool batted, string? how)[] Batters = null!;
            public (ChaseBowler f, int balls, int runs, int wickets, int wides, int noBalls, int dots)[] Bowlers = null!;
        }

        private Memo Snapshot() => new Memo
        {
            Runs = Runs, Wickets = Wickets, Extras = Extras, LegalBalls = LegalBalls, Striker = Striker, NonStriker = NonStriker,
            NextIn = _nextIn, BatReviews = BattingReviews, FieldReviews = FieldingReviews, LogCount = Log.Count, FreeHit = FreeHit,
            Batters = Order.Select(b => (b, b.Runs, b.Balls, b.Fours, b.Sixes, b.Batted, b.HowOut)).ToArray(),
            Bowlers = _bowlers.Values.Select(f => (f, f.Balls, f.Runs, f.Wickets, f.Wides, f.NoBalls, f.Dots)).ToArray(),
        };

        private void Restore(Memo m)
        {
            Runs = m.Runs; Wickets = m.Wickets; Extras = m.Extras; LegalBalls = m.LegalBalls;
            Striker = m.Striker; NonStriker = m.NonStriker; _nextIn = m.NextIn;
            BattingReviews = m.BatReviews; FieldingReviews = m.FieldReviews; FreeHit = m.FreeHit;
            Log.RemoveRange(m.LogCount, Log.Count - m.LogCount);
            foreach (var x in m.Batters)
            {
                x.b.Runs = x.runs; x.b.Balls = x.balls; x.b.Fours = x.fours; x.b.Sixes = x.sixes; x.b.Batted = x.batted; x.b.HowOut = x.how;
            }
            var keep = new HashSet<ChaseBowler>(m.Bowlers.Select(x => x.f));
            foreach (var id in _bowlers.Where(kv => !keep.Contains(kv.Value)).Select(kv => kv.Key).ToList()) _bowlers.Remove(id);
            foreach (var x in m.Bowlers)
            {
                x.f.Balls = x.balls; x.f.Runs = x.runs; x.f.Wickets = x.wickets; x.f.Wides = x.wides; x.f.NoBalls = x.noBalls; x.f.Dots = x.dots;
            }
        }

        /// <summary>The last ball, if it can still be reworked (nothing bowled since).</summary>
        private BallResult? Reworkable => _memo != null && _pending == null && Log.Count == _memo.LogCount + 1 ? Log[Log.Count - 1] : null;

        /// <summary>
        /// The user's running on the last ball: they went for <paramref name="attempted"/>
        /// runs (0 = stayed / sent back). Re-settles the race (a run out if the throw
        /// beats them) and the book. Returns the reworked ball, or null if there was
        /// nothing to run for.
        /// </summary>
        /// <param name="playerCalled">The player's own RUN (MANUAL): run as called — never quietly held back by the
        /// "ball's already in" rule (<see cref="Running.Commit"/>), which is for the AI's and the test runs' calls.</param>
        /// <param name="hold">The player called the last run after standing at the crease this long (s; the ball still live).</param>
        /// <param name="overthrowCalls">−1: an overthrow's run is taken automatically; else the player's call on it (0 / 1).</param>
        public BallResult? SettleRuns(int attempted, bool dive = false, double late = 0, bool playerCalled = false, double hold = 0, int overthrowCalls = -1) =>
            Rework(r => global::Apl.Core.Play.Running.Settle(r, attempted, _rng, dive, late, refuse: !playerCalled, hold: hold, overthrowCalls: overthrowCalls));

        /// <summary>
        /// CANCEL early in run <paramref name="run"/> (<paramref name="secondsIn"/> s
        /// after setting off on it): they turn back to their creases; the run doesn't
        /// count and the throw can still beat one of them home.
        /// </summary>
        public BallResult? TurnBack(int run, double secondsIn) =>
            Rework(r => global::Apl.Core.Play.Running.TurnBack(r, run, secondsIn, _rng));

        private BallResult? Rework(Action<BallResult> settle)
        {
            var r = Reworkable;
            if (r?.Running == null) return null;
            var bowler = BowlerOfLastBall();
            Restore(_memo!);
            UndoReprieve(r);
            settle(r);
            Apply(r, bowler);
            return r;
        }

        // --------------------------------------------------------------- reviews

        /// <summary>Reviews left this innings: the batting side's, the fielding side's.</summary>
        public int BattingReviews { get; private set; } = 1;
        public int FieldingReviews { get; private set; } = 1;

        /// <summary>
        /// The user may review the last ball: an lbw given against his batter, or
        /// (bowling) one turned down — while he has a review left.
        /// </summary>
        public bool CanReview()
        {
            var r = Reworkable;
            if (r?.Lbw == null || r.Lbw.Reviewed || r.NoBall || r.FreeHit) return false;
            return UserBatting ? r.Lbw.OnFieldOut && BattingReviews > 0 : !r.Lbw.OnFieldOut && r.Lbw.Appealed && FieldingReviews > 0;
        }

        /// <summary>
        /// The user's review of the last ball: the third umpire's verdict on the
        /// tracking, the decision changed if it's overturned, the review lost
        /// unless it was upheld on umpire's call. Returns the ball (see its
        /// <see cref="BallResult.Lbw"/>), or null if there's nothing to review.
        /// </summary>
        public BallResult? Review()
        {
            if (!CanReview()) return null;
            var r = Reworkable!;
            var bowler = BowlerOfLastBall();
            Restore(_memo!);
            UndoReprieve(r);
            ReviewOn(r, UserBatting);
            Apply(r, bowler);
            return r;
        }

        /// <summary>The AI sides' reviews, decided as the ball's played.</summary>
        private void AiReview(BallResult r)
        {
            if (r.Lbw == null || r.NoBall || r.FreeHit) return;
            // The AI batting side, given out (the user bowling).
            if (!UserBatting && r.Lbw.OnFieldOut && BattingReviews > 0 && (TestAlwaysReview || r.Lbw.AiWantsReview(true, _rng, OnStrike.Runs >= 25 ? 0.2 : 0)))
            {
                ReviewOn(r, true);
            }
            // The AI fielding side, turned down (the user batting).
            else if (UserBatting && !r.Lbw.OnFieldOut && r.Lbw.Appealed && FieldingReviews > 0 && (TestAlwaysReview || r.Lbw.AiWantsReview(false, _rng)))
            {
                ReviewOn(r, false);
            }
        }

        private void ReviewOn(BallResult r, bool byBatting)
        {
            r.Lbw!.Review(byBatting);
            if (r.Lbw.ReviewLost)
            {
                if (byBatting) BattingReviews--;
                else FieldingReviews--;
            }
            if (r.Lbw.Out && !r.Wicket)
            {
                r.Wicket = true;
                r.HowOut = "lbw";
                r.Runs = r.Penalty;
                r.Ran = r.LegByes = 0;
                r.Running = null;
            }
            else if (!r.Lbw.Out && r.Wicket && r.HowOut == "lbw")
            {
                r.Wicket = false;
                r.HowOut = null;
            }
        }

        /// <summary>A ball coming back out of the book: the free-hit reprieve is the innings' to apply again.</summary>
        private static void UndoReprieve(BallResult r)
        {
            if (r.Reprieve == null) return;
            r.Wicket = true;
            r.HowOut = r.Reprieve;
            r.Reprieve = null;
        }

        /// <summary>
        /// Who bowled <paramref name="r"/> (a ball in this innings' log): the over it was bowled in.
        /// The match view credits fielders against him, not against the next over's man.
        /// </summary>
        public Player CurrentBowlerOf(BallResult r) => BowlerFor(Math.Min(LegalBallsBefore(r), Math.Max(0, Overs * 6 - 1)) / 6);

        private Player BowlerOfLastBall()
        {
            // Restoring rewinds the balls bowled: the last ball's over is the over before it went in.
            return BowlerFor(_memo!.LegalBalls / 6);
        }
    }
}
