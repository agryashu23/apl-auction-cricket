using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using Newtonsoft.Json;
using UnityEngine;

namespace Apl.Client.Menu
{
    /// <summary>
    /// A league on the auction squads (or last season's): every side plays
    /// every other once, then the top four play off (Qualifier 1, Eliminator,
    /// Qualifier 2, Final). The user's matches are played on the ground; the
    /// rest are simulated on the season engine's odds at the same length.
    /// Saved after every match (persistentDataPath/tournament.json).
    /// </summary>
    public sealed class Tournament
    {
        /// <summary>One side's innings as the table needs it.</summary>
        public sealed class Score
        {
            public string Team = "";
            public int Runs, Wickets, Balls;
            public bool AllOut;
            public override string ToString() => AllOut ? $"{Runs}" : $"{Runs}/{Wickets}";
        }

        public sealed class Fixture
        {
            public int Number;
            /// <summary>League, Qualifier 1, Eliminator, Qualifier 2, Final.</summary>
            public string Stage = "League";
            /// <summary>A is at home.</summary>
            public string A = "", B = "", Venue = "";
            public bool Played;
            public string Winner = "", Margin = "";
            public Score? First, Second;

            public bool Involves(string team) => A == team || B == team;
            public string Other(string team) => A == team ? B : A;
            [JsonIgnore] public bool League => Stage == "League";
        }

        /// <summary>A player's tournament so far (the intro cards' numbers).</summary>
        public sealed class Line
        {
            public string Team = "";
            public int Matches, Innings, NotOuts, Runs, Balls, High, Fours, Sixes;
            public int BallsBowled, Conceded, Wickets, BestWickets, BestRuns;
            public double Average => Innings - NotOuts <= 0 ? Runs : Runs / (double)(Innings - NotOuts);
            public double StrikeRate => Balls == 0 ? 0 : Runs * 100.0 / Balls;
            public double Economy => BallsBowled == 0 ? 0 : Conceded * 6.0 / BallsBowled;
        }

        public sealed class Row
        {
            public string Team = "";
            public int Played, Won, Lost, Points, RunsFor, BallsFaced, RunsAgainst, BallsBowled;
            public double NetRunRate => BallsFaced == 0 || BallsBowled == 0 ? 0 : RunsFor * 6.0 / BallsFaced - RunsAgainst * 6.0 / BallsBowled;
        }

        public string UserTeam = "";
        public int Overs = 5;
        /// <summary>"auction" (the squads you just bought) or "season" (last season's).</summary>
        public string Source = "season";
        public long Seed;
        public int Simulated;
        public Dictionary<string, List<string>> Squads = new Dictionary<string, List<string>>();
        public List<Fixture> Fixtures = new List<Fixture>();
        public Dictionary<string, Line> Stats = new Dictionary<string, Line>();
        public string Champion = "";

        /// <summary>The champions page has been shown for this tournament (it opens once, on the way back from the last match).</summary>
        public bool ChampionShown;

        /// <summary>The last simulated match's stars, for the SIM MATCH result card (not saved).</summary>
        [JsonIgnore] public string LastTopBat = "", LastTopBowl = "";

        /// <summary>The auction the squads came from ("solo:&lt;seed&gt;", "room:&lt;code&gt;:&lt;seed&gt;"; "" for last season's).</summary>
        public string AuctionId = "";

        /// <summary>
        /// On a ROOM (multiplayer) auction's squads (owner, auction-r1 #4 decision): its players play their
        /// matches, so the hub hides SIM MATCH / SIM TO MY NEXT MATCH / SIM REST OF SEASON while they're in.
        /// </summary>
        [JsonIgnore]
        public bool FromRoom => AuctionId.StartsWith("room:", StringComparison.Ordinal);

        // ------------------------------------------------------------- set up

        /// <summary>A new league: <paramref name="squads"/> by team (player ids), fixtures drawn.</summary>
        /// <param name="auctionId">The auction behind <paramref name="squads"/> (see <see cref="AuctionId"/>).</param>
        public static Tournament Create(Dataset data, Dictionary<string, List<string>> squads, string userTeam, int overs, string source, long seed, string auctionId = "")
        {
            var t = new Tournament { UserTeam = userTeam, Overs = overs, Source = source, Seed = seed, Squads = squads, AuctionId = auctionId ?? "" };
            if (source == "auction") t.VerifyAuctionSquads(data);
            var rng = new Rng(seed);
            var teams = data.Franchises.Select(f => f.Id).Where(squads.ContainsKey).ToList();
            foreach (var round in Rounds(teams, rng))
            {
                // Your match closes each round, so "next match" plays the others first.
                foreach (var (a, b) in round.OrderBy(p => p.Item1 == userTeam || p.Item2 == userTeam ? 1 : 0))
                {
                    t.Fixtures.Add(new Fixture { Number = t.Fixtures.Count + 1, A = a, B = b, Venue = Apl.Core.Play.Venue.HomeOf(a).Id });
                }
            }
            return t;
        }

        /// <summary>Last season's squads, as the dataset has them.</summary>
        public static Dictionary<string, List<string>> SeasonSquads(Dataset data) =>
            data.Franchises.ToDictionary(f => f.Id, f => data.Players.Where(p => p.PreviousTeam == f.Id).Select(p => p.Id).ToList());

        /// <summary>Round-robin by rounds (the circle method), each pairing's home side alternating, rounds shuffled.</summary>
        private static List<List<(string, string)>> Rounds(List<string> teams, Rng rng)
        {
            var list = teams.ToList();
            if (list.Count % 2 == 1) list.Add("");
            var n = list.Count;
            var rounds = new List<List<(string, string)>>();
            for (var r = 0; r < n - 1; r++)
            {
                var round = new List<(string, string)>();
                for (var i = 0; i < n / 2; i++)
                {
                    var a = list[i];
                    var b = list[n - 1 - i];
                    if (a.Length > 0 && b.Length > 0) round.Add((r + i) % 2 == 0 ? (a, b) : (b, a));
                }
                rng.Shuffle(round);
                rounds.Add(round);
                var last = list[n - 1];
                list.RemoveAt(n - 1);
                list.Insert(1, last);
            }
            rng.Shuffle(rounds);
            return rounds;
        }

        // ------------------------------------------------------------- squads

        /// <summary>A side's best XI from its tournament squad.</summary>
        public TeamSheet Sheet(Dataset data, string teamId)
        {
            var ids = Squads.TryGetValue(teamId, out var s) ? new HashSet<string>(s) : new HashSet<string>();
            var squad = data.Players.Where(p => ids.Contains(p.Id)).ToList();
            if (squad.Count < 11)
            {
                // Should never happen with auction squads (18+ each): say so, it means the XI isn't the auction's.
                if (Source == "auction") Debug.LogWarning($"[APL tourney] {teamId}: only {squad.Count} of its {ids.Count} auction players found: playing last season's XI (matches auction: no)");
                return QuickMatch.LastSeasonXI(data, teamId);
            }
            return new TeamSheet(teamId, Lineups.PickBestXI(squad, data.Rules.MaxOverseasInXI).XI);
        }

        // ------------------------------------------------------------- state

        [JsonIgnore]
        public Fixture? Next => Fixtures.FirstOrDefault(f => !f.Played);

        /// <summary>Your next match (null: you're out, or it's over).</summary>
        [JsonIgnore]
        public Fixture? NextForUser => Fixtures.FirstOrDefault(f => !f.Played && f.Involves(UserTeam));

        [JsonIgnore]
        public bool Finished => Champion.Length > 0;

        /// <summary>
        /// Still in it (round auction-r1 #4): a match to come, or waiting for a playoff draw (lost Qualifier 1:
        /// Qualifier 2 waits on the Eliminator; won a playoff whose next match isn't drawn yet).
        /// <see cref="NextForUser"/> alone is null while waiting, which read as "your side is out".
        /// </summary>
        [JsonIgnore]
        public bool UserStillIn
        {
            get
            {
                if (Finished) return false;
                if (NextForUser != null) return true;
                if (!LeagueDone) return false;
                if (Fixtures.All(f => f.League)) return true; // the playoffs not drawn yet
                var mine = Fixtures.Where(f => !f.League && f.Involves(UserTeam)).ToList();
                if (mine.Count == 0) return false;
                var last = mine[mine.Count - 1];
                if (!last.Played) return true;
                if (last.Winner != UserTeam) return last.Stage == "Qualifier 1";
                return last.Stage != "Final";
            }
        }

        /// <summary>How the user's side finished: CHAMPIONS / RUNNERS-UP / OUT IN THE ELIMINATOR / 6TH IN THE LEAGUE.</summary>
        public string UserFinish()
        {
            if (Champion.Length > 0 && Champion == UserTeam) return "CHAMPIONS";
            var final = Fixtures.FirstOrDefault(f => f.Stage == "Final");
            if (final != null && final.Played && final.Involves(UserTeam)) return "RUNNERS-UP";
            var lost = Fixtures.LastOrDefault(f => !f.League && f.Played && f.Involves(UserTeam) && f.Winner != UserTeam);
            if (lost != null) return "OUT IN THE " + lost.Stage.ToUpperInvariant();
            var rank = Table().FindIndex(r => r.Team == UserTeam) + 1;
            return $"{Ordinal(rank)} IN THE LEAGUE";
        }

        public static string Ordinal(int n)
        {
            var tens = n % 100;
            if (tens >= 11 && tens <= 13) return n + "TH";
            return n + (n % 10 == 1 ? "ST" : n % 10 == 2 ? "ND" : n % 10 == 3 ? "RD" : "TH");
        }

        [JsonIgnore]
        public int LeagueCount => Fixtures.Count(f => f.League);

        [JsonIgnore]
        public bool LeagueDone => Fixtures.Where(f => f.League).All(f => f.Played);

        /// <summary>A side's league matches in all (played or not): 9 in a ten-team league.</summary>
        public int LeagueMatchesOf(string team) => Fixtures.Count(f => f.League && f.Involves(team));

        /// <summary>
        /// The table's "Q" (owner v26 #9, decision (a)): <paramref name="rank"/> (1-based, in <see cref="Table"/>'s
        /// order) in the top four AND more than half of its league matches won (the engine's rule).
        /// </summary>
        public bool Qualified(Row r, int rank) => LeagueTable.Qualified(rank, r.Won, LeagueMatchesOf(r.Team));

        /// <summary>The league table: points, then net run rate, then wins.</summary>
        public List<Row> Table()
        {
            var rows = Squads.Keys.ToDictionary(id => id, id => new Row { Team = id });
            foreach (var f in Fixtures.Where(f => f.League && f.Played && f.First != null && f.Second != null))
            {
                foreach (var (bat, bowl) in new[] { (f.First!, f.Second!), (f.Second!, f.First!) })
                {
                    if (!rows.TryGetValue(bat.Team, out var r)) continue;
                    // All out counts as the full quota faced (the NRR rule).
                    var balls = bat.AllOut ? Overs * 6 : bat.Balls;
                    r.RunsFor += bat.Runs;
                    r.BallsFaced += balls;
                    if (rows.TryGetValue(bowl.Team, out var o))
                    {
                        o.RunsAgainst += bat.Runs;
                        o.BallsBowled += balls;
                    }
                }
                if (rows.TryGetValue(f.Winner, out var w))
                {
                    w.Played++;
                    w.Won++;
                    w.Points += 2;
                }
                if (rows.TryGetValue(f.Other(f.Winner), out var l))
                {
                    l.Played++;
                    l.Lost++;
                }
            }
            return rows.Values
                .OrderByDescending(r => r.Points)
                .ThenByDescending(r => r.NetRunRate)
                .ThenByDescending(r => r.Won)
                .ThenBy(r => r.Team, StringComparer.Ordinal)
                .ToList();
        }

        // ------------------------------------------------------------ results

        /// <summary>Simulate every match before your next one (all of them once you're out).</summary>
        public void SimulateUntilUser(Dataset data)
        {
            while (Next is Fixture f && !f.Involves(UserTeam) && !Finished) Simulate(data, f);
        }

        /// <summary>
        /// Every match left, the user's too, through the playoffs to the champions (SIM REST OF SEASON).
        /// <paramref name="userSheet"/>: the XI the user last confirmed (null: the auto-picked XI).
        /// </summary>
        public void SimulateToEnd(Dataset data, TeamSheet? userSheet = null)
        {
            var guard = 0;
            while (!Finished && Next is Fixture f && guard++ < 200) Simulate(data, f, userSheet);
        }

        /// <summary>
        /// A match on the season engine's odds, at this tournament's length; both XIs from the tournament
        /// squads (<see cref="Sheet"/>), or the user's own confirmed XI when <paramref name="userSheet"/> is
        /// that side's. The user's simulated matches log the same squad check as the played ones.
        /// </summary>
        public void Simulate(Dataset data, Fixture f, TeamSheet? userSheet = null)
        {
            var rng = new Rng(Seed * 31 + f.Number * 7919 + Simulated++);
            var a = userSheet != null && userSheet.TeamId == f.A ? userSheet : Sheet(data, f.A);
            var b = userSheet != null && userSheet.TeamId == f.B ? userSheet : Sheet(data, f.B);
            if (f.Involves(UserTeam))
            {
                bool FromSquad(TeamSheet s) => Squads.TryGetValue(s.TeamId, out var ids) && s.XI.Count == 11 && s.XI.All(p => ids.Contains(p.Id));
                var mine = f.A == UserTeam ? a : b;
                var opp = f.A == UserTeam ? b : a;
                Debug.Log($"[APL tourney] sim match {f.Number} ({Source} squads): {UserTeam} XI from squad: {(FromSquad(mine) ? "yes" : "no")} ({(ReferenceEquals(mine, userSheet) ? "chosen XI" : "auto XI")}); " +
                          $"{opp.TeamId} XI from squad: {(FromSquad(opp) ? "yes" : "no")}");
            }
            var venue = Apl.Core.Play.Venue.Find(f.Venue) ?? Apl.Core.Play.Venue.HomeOf(f.A);
            var pitch = venue.PickPitch(rng);
            var sim = new MatchSimulator(new Rng(rng.NextInt(int.MaxValue)));
            var tossA = rng.Chance(0.5);
            var batFirstA = rng.Chance(0.45) ? tossA : !tossA;
            var first = batFirstA ? a : b;
            var second = batFirstA ? b : a;
            var one = sim.SimulateInnings(first, second, pitch, Overs, Apl.Core.Play.Bowling.PlanOvers(second.XI, Overs), null, venue.SizeFactor);
            var two = sim.SimulateInnings(second, first, pitch, Overs, Apl.Core.Play.Bowling.PlanOvers(first.XI, Overs), one.Runs + 1, venue.SizeFactor);
            Record(f, ScoreOf(one), ScoreOf(two), second.XI.Count, rng);
            var bat = one.Batting.Concat(two.Batting).Where(x => x.Batted).OrderByDescending(x => x.Runs).ThenBy(x => x.Balls).FirstOrDefault();
            LastTopBat = bat != null ? $"{bat.Player.Name} {bat.Runs}{(bat.Out ? "" : "*")} ({bat.Balls})" : "";
            var bowl = one.Bowling.Concat(two.Bowling).Where(x => x.Balls > 0).OrderByDescending(x => x.Wickets).ThenBy(x => x.Runs).FirstOrDefault();
            LastTopBowl = bowl != null ? $"{bowl.Player.Name} {bowl.Wickets}/{bowl.Runs} ({bowl.Overs} ov)" : "";
            AddStats(one, first.XI);
            AddStats(two, second.XI);
            Advance(data);
        }

        /// <summary>Your match, as it was played on the ground (they batted first, you chased).</summary>
        public void RecordUserMatch(Dataset data, int number, QuickMatch m, string? superOverWinner = null)
        {
            var f = Fixtures.FirstOrDefault(x => x.Number == number);
            if (f == null || f.Played) return;
            var first = ScoreOf(m.FirstInnings);
            var second = new Score
            {
                Team = m.Batting.TeamId, Runs = m.Runs, Wickets = m.Wickets, Balls = m.LegalBalls,
                AllOut = m.Wickets >= m.Order.Count - 1,
            };
            Record(f, first, second, m.Order.Count, new Rng(Seed + number), superOverWinner);
            AddStats(m.FirstInnings, m.Bowling.XI);
            // The chase, from the match's own lines.
            foreach (var p in m.Batting.XI) Of(p, m.Batting.TeamId).Matches++;
            foreach (var b in m.Order.Where(b => b.Batted))
            {
                var s = Of(b.Player, m.Batting.TeamId);
                s.Innings++;
                if (b.HowOut == null) s.NotOuts++;
                s.Runs += b.Runs;
                s.Balls += b.Balls;
                s.Fours += b.Fours;
                s.Sixes += b.Sixes;
                s.High = Math.Max(s.High, b.Runs);
            }
            foreach (var w in m.BowlingFigures) Bowled(Of(w.Player, m.Bowling.TeamId), w.Balls, w.Runs, w.Wickets);
            Advance(data);
        }

        private static Score ScoreOf(Innings inn) => new Score
        {
            Team = inn.BattingTeamId, Runs = inn.Runs, Wickets = inn.Wickets, Balls = inn.LegalBalls, AllOut = inn.AllOut,
        };

        private void Record(Fixture f, Score first, Score second, int sideSize, Rng rng, string? superOverWinner = null)
        {
            f.Played = true;
            f.First = first;
            f.Second = second;
            if (second.Runs > first.Runs)
            {
                f.Winner = second.Team;
                var wl = sideSize - 1 - second.Wickets;
                var left = Overs * 6 - second.Balls;
                f.Margin = $"by {wl} {(wl == 1 ? "wicket" : "wickets")}" + (left > 0 ? $" ({left} {(left == 1 ? "ball" : "balls")} left)" : "");
            }
            else if (first.Runs > second.Runs)
            {
                f.Winner = first.Team;
                var by = first.Runs - second.Runs;
                f.Margin = $"by {by} {(by == 1 ? "run" : "runs")}";
            }
            else
            {
                // Level: the Super Over settles it — the one played on the ground when there was one
                // (two points to its winner), else drawn at random for a simulated match.
                f.Winner = superOverWinner == first.Team || superOverWinner == second.Team ? superOverWinner! : rng.Chance(0.5) ? first.Team : second.Team;
                f.Margin = "in the Super Over";
            }
        }

        private Line Of(Player p, string team)
        {
            if (!Stats.TryGetValue(p.Id, out var s)) Stats[p.Id] = s = new Line { Team = team, BestRuns = 999 };
            return s;
        }

        private static void Bowled(Line s, int balls, int runs, int wickets)
        {
            if (balls <= 0) return;
            s.BallsBowled += balls;
            s.Conceded += runs;
            s.Wickets += wickets;
            if (wickets > s.BestWickets || (wickets == s.BestWickets && runs < s.BestRuns))
            {
                s.BestWickets = wickets;
                s.BestRuns = runs;
            }
        }

        private void AddStats(Innings inn, IReadOnlyList<Player> batting)
        {
            foreach (var p in batting) Of(p, inn.BattingTeamId).Matches++;
            foreach (var b in inn.Batting.Where(b => b.Batted))
            {
                var s = Of(b.Player, inn.BattingTeamId);
                s.Innings++;
                if (!b.Out) s.NotOuts++;
                s.Runs += b.Runs;
                s.Balls += b.Balls;
                s.Fours += b.Fours;
                s.Sixes += b.Sixes;
                s.High = Math.Max(s.High, b.Runs);
            }
            foreach (var w in inn.Bowling) Bowled(Of(w.Player, inn.BowlingTeamId), w.Balls, w.Runs, w.Wickets);
        }

        /// <summary>The league done: the playoffs are drawn as each round's results come in.</summary>
        private void Advance(Dataset data)
        {
            if (!LeagueDone || Finished) return;
            Fixture? Stage(string name) => Fixtures.FirstOrDefault(f => f.Stage == name);
            Fixture Add(string stage, string a, string b) => AddFixture(stage, a, b);
            var q1 = Stage("Qualifier 1");
            if (q1 == null)
            {
                var top = Table().Take(4).Select(r => r.Team).ToList();
                if (top.Count < 4) return;
                Add("Qualifier 1", top[0], top[1]);
                Add("Eliminator", top[2], top[3]);
                return;
            }
            var elim = Stage("Eliminator")!;
            var q2 = Stage("Qualifier 2");
            if (q2 == null)
            {
                if (q1.Played && elim.Played) Add("Qualifier 2", q1.Other(q1.Winner), elim.Winner);
                return;
            }
            var final = Stage("Final");
            if (final == null)
            {
                if (q2.Played) Add("Final", q1.Winner, q2.Winner);
                return;
            }
            if (final.Played) Champion = final.Winner;
        }

        private Fixture AddFixture(string stage, string a, string b)
        {
            // Playoffs at the higher seed's ground.
            var f = new Fixture { Number = Fixtures.Count + 1, Stage = stage, A = a, B = b, Venue = Apl.Core.Play.Venue.HomeOf(a).Id };
            Fixtures.Add(f);
            return f;
        }

        // ------------------------------------------------------------ saving

        private static string FilePath => Path.Combine(Application.persistentDataPath, "tournament.json");

        public void Save()
        {
            try
            {
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(this));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL] could not save the tournament: {ex.Message}");
            }
        }

        /// <summary>The saved tournament, or null (none, unreadable, or made with players this data no longer has).</summary>
        public static Tournament? Load(Dataset data)
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var t = JsonConvert.DeserializeObject<Tournament>(File.ReadAllText(FilePath));
                if (t == null || t.Fixtures.Count == 0 || t.Squads.Count < 4) return null;
                var ids = new HashSet<string>(data.Players.Select(p => p.Id));
                if (t.Squads.Values.Any(s => s.Any(id => !ids.Contains(id)))) return null;
                return t;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL] ignoring a broken tournament save: {ex.Message}");
                return null;
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL] could not delete the tournament: {ex.Message}");
            }
        }

        // ------------------------------------------- the last auction's squads

        private static string AuctionPath => Path.Combine(Application.persistentDataPath, "auction_squads.json");

        /// <summary>Kept when an auction ends, so a tournament can be started on it later.</summary>
        public sealed class AuctionSquads
        {
            /// <summary>Which auction ("solo:&lt;seed&gt;", "room:&lt;code&gt;:&lt;seed&gt;").</summary>
            public string Id = "";
            public string Team = "";
            public long SavedAt;
            public Dictionary<string, List<string>> Squads = new Dictionary<string, List<string>>();

            /// <summary>The retained players per team (a subset of Squads; for the log's check).</summary>
            public Dictionary<string, List<string>> Retained = new Dictionary<string, List<string>>();
        }

        /// <summary>The auction just finished, as its engine left every squad (retained, bought, RTM, auto-fill): what a tournament on it must match.</summary>
        private static AuctionSquads? _lastAuction;

        public static void SaveAuction(string team, IEnumerable<TeamState> teams, string id = "")
        {
            try
            {
                var a = new AuctionSquads { Id = id, Team = team, SavedAt = DateTime.UtcNow.Ticks, Squads = teams.ToDictionary(t => t.Id, t => t.Players.Select(p => p.Id).ToList()) };
                foreach (var t in teams) a.Retained[t.Id] = t.Squad.Where(p => p.Kind == PurchaseKind.Retained).Select(p => p.Player.Id).ToList();
                _lastAuction = a;
                Debug.Log($"[APL tourney] auction {id} kept for a tournament: {string.Join(" ", a.Squads.Select(kv => kv.Key + ":" + kv.Value.Count))}");
                File.WriteAllText(AuctionPath, JsonConvert.SerializeObject(a));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL] could not keep the auction's squads: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs, per team, that the tournament's squad is exactly the auction's (same players, retained ones
        /// included) and that its XI comes from that squad:
        /// [APL tourney] squads from auction &lt;id&gt;: team X n players (matches auction: yes/no).
        /// The reference is the auction finished in this session, else the kept file.
        /// </summary>
        private void VerifyAuctionSquads(Dataset data)
        {
            var truth = _lastAuction ?? LoadAuction(data);
            var id = truth?.Id is string s && s.Length > 0 ? s : "unknown";
            foreach (var kv in Squads.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var expected = truth != null && truth.Squads.TryGetValue(kv.Key, out var e) ? e : null;
                var same = expected != null && expected.Count == kv.Value.Count && new HashSet<string>(expected).SetEquals(kv.Value);
                var squadSet = new HashSet<string>(kv.Value);
                var xi = Sheet(data, kv.Key).XI;
                var xiOk = xi.Count == 11 && xi.All(p => squadSet.Contains(p.Id));
                var kept = truth != null && truth.Retained.TryGetValue(kv.Key, out var r) ? r : new List<string>();
                var retained = kept.Count(squadSet.Contains);
                Debug.Log($"[APL tourney] squads from auction {id}: team {kv.Key} {kv.Value.Count} players (matches auction: {(same && xiOk ? "yes" : "no")}){(same ? "" : $" expected {expected?.Count.ToString() ?? "none"}")}, XI from squad: {(xiOk ? "yes" : "no")}, retained {retained}/{kept.Count} carried");
            }
        }

        public static AuctionSquads? LoadAuction(Dataset data)
        {
            try
            {
                if (!File.Exists(AuctionPath)) return null;
                var a = JsonConvert.DeserializeObject<AuctionSquads>(File.ReadAllText(AuctionPath));
                if (a == null || a.Squads.Count < 4) return null;
                var ids = new HashSet<string>(data.Players.Select(p => p.Id));
                if (a.Squads.Values.Any(s => s.Count < 11 || s.Any(id => !ids.Contains(id)))) return null;
                return a;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[APL] ignoring the saved auction squads: {ex.Message}");
                return null;
            }
        }
    }
}
