using System;
using System.Collections;
using System.IO;
using System.Linq;
using Apl.Client.Match;
using Apl.Client.Menu;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Dev
{
    /// <summary>
    /// Owner v26 items 6, 7 and 9 in one test run: <c>APL.exe -aplxishots &lt;dir&gt;</c> (from the hall).
    /// <list type="number">
    /// <item>PLAYING XI (Quick Match entry, no clock): open, a legal swap, a refused swap, CONFIRM → XiChoice.</item>
    /// <item>PLAYING XI with the 30 s clock (tournament / online): shots at 30 and ~25 s, then it confirms itself at 0.</item>
    /// <item>TOURNAMENT → AUCTION: the request opens the auction setup; an instant AI auction (Quick format) is
    /// settled, the results screen offers START TOURNAMENT ►, which starts the league on those squads → the table.</item>
    /// <item>The table's Q: the league simulated half-way and to the end; a Q only past half the league matches in wins.</item>
    /// </list>
    /// Writes xishot.txt (every check as "CHECK name: ok|FAIL …", then "DONE fails=N"). It replaces the saved
    /// tournament (tournament.json) with the test one.
    /// <para>The pick timers (striker / non-striker / bowler 10 s, impact 20 s) are checked on the ground:
    /// <c>-aplmatchtest -aplprematch -aplrealtimers</c> (Player.log "[APL timers] … timed out after 10 s: auto-pick").</para>
    /// </summary>
    public sealed class XiShot : MonoBehaviour
    {
        private string _dir = "";
        private int _shot, _fails;
        private StreamWriter _log = null!;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-aplxishots");
            if (i < 0 || i + 1 >= args.Length || FindAnyObjectByType<XiShot>() != null) return;
            var go = new GameObject("XiShot");
            DontDestroyOnLoad(go);
            go.AddComponent<XiShot>()._dir = args[i + 1];
        }

        private void Start()
        {
            Directory.CreateDirectory(_dir);
            Application.runInBackground = true;
            _log = new StreamWriter(Path.Combine(_dir, "xishot.txt")) { AutoFlush = true };
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type == LogType.Exception || type == LogType.Error || message.StartsWith("[APL xi]") || message.StartsWith("[APL tourney]"))
                    _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] {type}: {message}");
            };
            StartCoroutine(Run());
        }

        private IEnumerator Shot(string name, float after = 1f)
        {
            yield return new WaitForSecondsRealtime(after);
            var file = Path.Combine(_dir, $"{++_shot:000}_{name}.png");
            ScreenCapture.CaptureScreenshot(file);
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] shot {file}");
            yield return null;
        }

        private void Check(string name, bool ok, string detail = "")
        {
            if (!ok) _fails++;
            _log.WriteLine($"CHECK {name}: {(ok ? "ok" : "FAIL")} {detail}");
        }

        private static Dataset? _data;
        private static Dataset Data => _data ??= Dataset.FromJson(Resources.Load<TextAsset>("Data/apl_dataset").text);

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(3f);
            yield return XiQuick();
            yield return XiTimed();
            yield return AuctionPath();
            _log.WriteLine($"DONE fails={_fails} shots={_shot}");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(_fails == 0 ? 0 : 2);
        }

        // ------------------------------------------------------------- 1. quick

        private IEnumerator XiQuick()
        {
            XiChoice.Clear();
            var confirmed = false;
            var screen = PlayingXiScreen.OpenQuickMatch(Data, "BLR", "MBH", () => confirmed = true, () => _log.WriteLine("back pressed"));
            yield return Shot("xi_quick_open", 1.5f);
            var squad = Data.Players.Where(p => p.PreviousTeam == "BLR").ToList();
            Check("xi auto = last season's XI", screen.CurrentXI.Select(p => p.Id).SequenceEqual(QuickMatch.LastSeasonXI(Data, "BLR").XI.Select(p => p.Id)));
            // A legal swap: a local batter out, a local non-keeper in.
            var outgoing = screen.CurrentXI.FirstOrDefault(p => !p.Overseas && p.Role == Role.Batter && !p.IsKeeper);
            var incoming = screen.Bench.FirstOrDefault(p => !p.Overseas && !p.IsKeeper);
            if (outgoing != null && incoming != null)
            {
                screen.TapXi(outgoing);
                yield return Shot("xi_quick_selected", 0.6f);
                screen.TapBench(incoming);
                Check("xi legal swap", screen.CurrentXI.Any(p => p.Id == incoming.Id) && screen.CurrentXI.All(p => p.Id != outgoing.Id), $"{outgoing.Id} -> {incoming.Id}");
                yield return Shot("xi_quick_swapped", 0.6f);
            }
            else Check("xi legal swap", false, "no local batter / bench player to swap");
            // A refused swap: the keeper out for a non-keeper (when he's the only one), or an overseas too many.
            var keeper = screen.CurrentXI.Where(p => p.IsKeeper).ToList();
            var nonKeeper = screen.Bench.FirstOrDefault(p => !p.IsKeeper);
            if (keeper.Count == 1 && nonKeeper != null)
            {
                var before = screen.CurrentXI.Select(p => p.Id).ToList();
                screen.TapXi(keeper[0]);
                screen.TapBench(nonKeeper);
                Check("xi refused swap (keeper)", screen.CurrentXI.Select(p => p.Id).SequenceEqual(before));
                yield return Shot("xi_quick_refused", 0.4f);
            }
            var chosen = screen.CurrentXI.Select(p => p.Id).ToList();
            screen.Confirm();
            yield return null;
            Check("xi confirmed", confirmed && XiChoice.TeamId == "BLR" && XiChoice.Ids != null && XiChoice.Ids.SequenceEqual(chosen));
            var sheet = XiChoice.SheetFor(Data, "BLR", squad);
            Check("xi resolves for the ground", sheet != null && sheet.XI.Select(p => p.Id).SequenceEqual(chosen));
        }

        // ------------------------------------------------------------- 2. timed

        private IEnumerator XiTimed()
        {
            var confirmedAt = -1f;
            var squad = Data.Players.Where(p => p.PreviousTeam == "MBH").ToList();
            var start = Time.realtimeSinceStartup;
            var screen = PlayingXiScreen.Open(Data, "MBH", "BLR", squad, null, PlayingXiScreen.TimedSeconds, _ => confirmedAt = Time.realtimeSinceStartup);
            yield return Shot("xi_timed_30", 0.5f);
            yield return Shot("xi_timed_25", 4.5f);
            Check("xi clock running", screen != null && screen.SecondsLeft < 26f && screen.SecondsLeft > 20f, $"left {(screen != null ? screen.SecondsLeft : -1):0.0}");
            while (confirmedAt < 0 && Time.realtimeSinceStartup - start < 40f) yield return null;
            var took = confirmedAt - start;
            Check("xi auto-confirm at 0", confirmedAt > 0 && took > 29f && took < 32f, $"after {took:0.0} s");
            Check("xi auto-confirmed XI stored", XiChoice.TeamId == "MBH" && XiChoice.SheetFor(Data, "MBH", squad) != null);
        }

        // ------------------------------------------------------- 3. auction path

        private IEnumerator AuctionPath()
        {
            // The hub's AUCTION → NEW AUCTION FIRST → GO TO THE AUCTION: the menu opens the auction setup.
            TournamentHub.Open(Data, "CHC", 2);
            yield return null;
            // A tournament already saved opens on its table: NEW for the setup.
            if (Find("HubNew") != null) Click("HubNew");
            yield return Shot("hub_setup", 1f);
            Click("SrcAuction");
            yield return Shot("hub_setup_auction", 0.8f);
            Click("Overs_2");
            Click("Start");
            yield return Shot("auction_setup_from_tournament", 1.2f);
            Check("auction requested", TournamentAuction.PendingTeam != null, TournamentAuction.PendingTeam ?? "none");
            Check("auction setup opened", FindAnyObjectByType<MainMenu>() != null && Find("Next") != null);

            // The auction itself, settled between AI teams in an instant (Quick format), bound like AplBoot.StartGame.
            const long seed = 20261112;
            var team = TournamentAuction.PendingTeam ?? "CHC";
            TournamentAuction.Bind(seed, team);
            foreach (var m in FindObjectsByType<MainMenu>(FindObjectsSortMode.None)) Destroy(m.gameObject);
            var format = Data.Rules.Format("standard");
            var engine = new AuctionEngine(Data.Rules, format, Data.Franchises, Data.Players, seed, null);
            Ai.RunAuction(engine, Ai.CreateBidders(engine, seed * 31));
            var results = ResultsScreen.Create(Data, engine, team, seed * 7919 + 13);
            var overs = TournamentAuction.OversFor(seed, team);
            Check("auction bound to the tournament", overs == 2, $"overs {overs}");
            if (overs is int o) results.OfferTournament(o, seed);
            yield return Shot("results_start_tournament", 1.5f);
            Click("Tournament");
            yield return Shot("tourney_table_auction", 1.5f);
            var t = Tournament.Load(Data);
            var same = t != null && engine.Teams.Values.All(x => t.Squads.TryGetValue(x.Id, out var s) && s.SequenceEqual(x.Players.Select(p => p.Id)));
            Check("tournament on the auction squads", t != null && t.Source == "auction" && t.UserTeam == team && t.Overs == 2 && same,
                t == null ? "no tournament" : $"source {t.Source}, team {t.UserTeam}, overs {t.Overs}, fixtures {t.Fixtures.Count}");
            if (t == null) yield break;
            var sheet = t.Sheet(Data, team);
            Check("tournament XI from the auction squad", sheet.XI.All(p => t.Squads[team].Contains(p.Id)));

            // 4. The Q: none at the start; half-way and at the end only past half the league in wins.
            Check("no Q before a ball", t.Table().Select((r, i) => t.Qualified(r, i + 1)).All(q => !q));
            var half = t.LeagueCount / 2;
            while (t.Fixtures.Count(f => f.League && f.Played) < half && t.Next is Tournament.Fixture f1) t.Simulate(Data, f1);
            LogQ(t, "half-way");
            while (!t.LeagueDone && t.Next is Tournament.Fixture f2) t.Simulate(Data, f2);
            LogQ(t, "league done");
            t.Save();
            foreach (var h in FindObjectsByType<TournamentHub>(FindObjectsSortMode.None)) Destroy(h.gameObject);
            TournamentHub.Open(Data, team, 2);
            yield return Shot("tourney_table_q", 1.5f);
        }

        private void LogQ(Tournament t, string when)
        {
            var table = t.Table();
            for (var i = 0; i < table.Count; i++)
            {
                var r = table[i];
                var games = t.LeagueMatchesOf(r.Team);
                var q = t.Qualified(r, i + 1);
                _log.WriteLine($"  {when}: #{i + 1} {r.Team} P{r.Played} W{r.Won} of {games} → {(q ? "Q" : "-")}");
                // Top four AND more than half won (owner decision (a)).
                if (q != (i < 4 && r.Won * 2 > games)) Check($"Q rule {r.Team} {when}", false);
            }
            Check($"Q rule {when}", true);
        }

        private static Button? Find(string name) => FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == name && b.interactable);

        private void Click(string name)
        {
            var b = Find(name);
            _log.WriteLine($"[{Time.realtimeSinceStartup:0.0}s] click {name}: {(b != null ? "ok" : "MISSING")}");
            b?.onClick.Invoke();
        }
    }
}
