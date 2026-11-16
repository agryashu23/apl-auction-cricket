using System.Collections.Generic;
using System.Linq;
using Apl.Client.Audio;
using Apl.Client.Hall;
using Apl.Client.Menu;
using Apl.Client.Online;
using Apl.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Apl.Client
{
    /// <summary>
    /// Game flow in the hall scene: menu (team / format / retention) →
    /// ceremony + auction → results and season → play again.
    /// </summary>
    public sealed class AplBoot : MonoBehaviour
    {
        /// <summary>
        /// Pacing for the 3D hall (see SPEC §11b). The reveal was 11 s with BID locked (rater r1 #2:
        /// ~22 min of waiting per auction); now 4.5 s, and the player's own first bid ends it at once
        /// (HallDirector.HumanBidPressed). The card read-out still plays in full: AI bids that land
        /// while the auctioneer reads hold their amount call until he's done (HallDirector.OnBid).
        /// </summary>
        public int RevealMs = 4500;
        public int ResultMs = 5500;
        public double AiThinkScale = 3.6;

        /// <summary>AI holders of an RTM card answer after the auctioneer has asked.</summary>
        public int AiRtmThinkMs = 6500;

        private Dataset _data = null!;
        private HallDirector _director = null!;
        private RoomClient? _client;

        private void Start()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            var json = Resources.Load<TextAsset>("Data/apl_dataset");
            _data = Dataset.FromJson(json.text);
            _director = FindAnyObjectByType<HallDirector>();
            var bank = new VoiceBank();
            _director.Auctioneer.Bank = bank;
            _director.Assistant.Bank = bank;
            _director.Camera.Show("wide");
            _director.Camera.StartCinematic();
            // The anthem plays over the menus, and fades as the auction begins.
            _director.Sfx.PlayMusic(Sfx.MenuTrack());

            ShowMenu();
        }

        private void ShowMenu()
        {
            var menu = MainMenu.Create(_data);
            menu.StartPressed += StartGame;
            menu.ResumePressed += ResumeGame;
            menu.OnlinePressed += OpenOnline;
            menu.MatchPressed += () =>
            {
                var picker = MatchMenu.Create(_data);
                picker.Back += ShowMenu;
            };
        }

        private void OpenOnline()
        {
            _client ??= RoomClient.Create(_data);
            var lobby = OnlineLobby.Create(_data, _client);
            lobby.Back += ShowMenu;
            lobby.Started += StartOnline;
        }

        /// <summary>
        /// A friend room's auction: the server runs it (and its clock is already
        /// going), so no ceremony or briefing; the hall replays the server's log.
        /// </summary>
        private void StartOnline(RoomClient client, string myTeam)
        {
            var auction = client.Auction!;
            _director.PlayCeremony = false;
            _director.PlayBriefing = false;
            _director.Sfx.StopMusic();
            _director.Room = client;
            _director.Finished += () =>
            {
                _director.Sfx.PlayMusic("anthem", 0.45f);
                // Career XP for the auction and its season (audio-modes: Modes/AuctionRewards.cs; shown on the home page next).
                Apl.Client.Modes.AuctionRewards.Record(_data, auction.Engine, myTeam, auction.Setup!.SeasonSeed);
                StartCoroutine(RoomResults(client, auction, myTeam));
            };
            _director.Begin(auction, _data, myTeam);
        }

        /// <summary>
        /// The room's results screen with its rewards (podium, standings, awards): the server's
        /// <c>results</c> usually beat the closing line; if not, wait a few seconds for them, then
        /// fall back to the same numbers worked out here (AuctionAwards is deterministic).
        /// </summary>
        private System.Collections.IEnumerator RoomResults(RoomClient client, RemoteAuction auction, string myTeam)
        {
            var until = Time.unscaledTime + 6f;
            while (client.Rewards == null && Time.unscaledTime < until) yield return null;
            var rewards = client.Rewards ?? LocalRewards(auction.Engine, t =>
            {
                var seat = client.Seats.FirstOrDefault(s => s.Team == t);
                return seat == null ? ((string, string, string)?)null : (seat.Id, seat.Name, seat.Control);
            });
            if (rewards != null)
            {
                var mine = rewards.For(myTeam);
                RoomCareer.Record($"room:{client.Code}:{auction.Setup!.Seed}", (mine != null ? rewards.Place(mine).Place : 0), mine?.Points ?? 0, room: true);
            }
            var results = ResultsScreen.Create(_data, auction.Engine, myTeam, auction.Setup!.SeasonSeed, rewards, $"room:{client.Code}:{auction.Setup!.Seed}");
            results.PlayAgainPressed += () =>
            {
                client.Leave();
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            };
        }

        /// <summary>The end-of-auction rewards worked out on this phone (solo, or a room whose results never came).</summary>
        private RoomRewards? LocalRewards(AuctionEngine engine, System.Func<string, (string Id, string Name, string Control)?> owner)
        {
            try
            {
                return RoomRewards.FromJson(AuctionAwards.Compute(engine, _data.Rules).ToJson(owner));
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[APL room] no rewards: {ex.Message}");
                return null;
            }
        }

        private void StartGame(GameSetup setup)
        {
            var rules = _data.Rules;
            var seed = System.DateTime.Now.Ticks & 0x7FFFFFFF;
            Dictionary<string, List<Player>>? retentions = null;
            if (setup.Retention)
            {
                retentions = new Dictionary<string, List<Player>>();
                foreach (var f in _data.Franchises)
                {
                    retentions[f.Id] = f.Id == setup.Team.Id
                        ? new List<Player>(setup.Retained)
                        : Ai.ChooseRetentions(f.Id, _data.Players, rules, setup.Format);
                }
            }
            // Everything needed to rebuild this auction, saved after every lot.
            var roomSetup = new RoomSetup
            {
                Seed = seed,
                SeasonSeed = seed * 7919 + 13,
                FormatId = setup.Format.Id,
                Retention = setup.Retention,
                DataVersion = _data.Version,
                // Solo: IPL-style sets and the 28 Cr cap (rooms keep the server's order and cap).
                Solo = true,
            };
            if (retentions != null)
            {
                foreach (var f in _data.Franchises) roomSetup.Retentions[f.Id] = retentions[f.Id].Select(p => p.Id).ToList();
                AuctionLog.Retentions(retentions, rules, setup.Format, setup.Team.Id);
            }
            // Asked for from TOURNAMENT → AUCTION (owner v26 #6): the tournament starts from its results.
            TournamentAuction.Bind(seed, setup.Team.Id);
            var save = SoloSave.Begin(roomSetup, setup.Team.Id);
            Play(roomSetup.BuildEngine(_data), save, fresh: true);
        }

        /// <summary>Carry on a saved solo auction from its last finished lot.</summary>
        private void ResumeGame(SoloSave save)
        {
            Play(save.Rebuild(_data), save, fresh: false);
        }

        private void Play(AuctionEngine engine, SoloSave save, bool fresh)
        {
            _director.Sfx.StopMusic();
            var team = save.TeamId;
            var humans = new[] { team };
            var live = new LiveAuction(
                engine,
                // A resumed auction gets fresh AI minds: only the results so far carry over.
                Ai.CreateBidders(engine, save.Setup.Seed * 31 + 7 + save.LotsDone, humans),
                humans,
                revealMs: RevealMs,
                resultMs: ResultMs,
                aiRtmThinkMs: AiRtmThinkMs,
                aiThinkScale: AiThinkScale);
            live.OnEvent += save.Record;
            if (!fresh)
            {
                // Straight back to the next lot.
                _director.PlayCeremony = false;
                _director.PlayBriefing = false;
            }

            _director.Finished += () =>
            {
                SoloSave.Delete();
                _director.Sfx.PlayMusic("anthem", 0.45f);
                // Career XP for the auction and its season (audio-modes: Modes/AuctionRewards.cs; shown on the home page next).
                Apl.Client.Modes.AuctionRewards.Record(_data, engine, team, save.Setup.SeasonSeed);
                // The same rewards as a room (podium, standings, awards), worked out here; lifetime totals count it.
                var rewards = LocalRewards(engine, t => t == team ? ("solo", "You", "human") : ((string, string, string)?)null);
                if (rewards != null)
                {
                    var mine = rewards.For(team);
                    RoomCareer.Record($"solo:{save.Setup.Seed}", (mine != null ? rewards.Place(mine).Place : 0), mine?.Points ?? 0, room: false);
                }
                var results = ResultsScreen.Create(_data, engine, team, save.Setup.SeasonSeed, rewards, $"solo:{save.Setup.Seed}");
                results.PlayAgainPressed += () => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                // A tournament's auction: START TOURNAMENT ► on these squads (owner v26 #6, Menu/ResultsScreen.Tournament.cs).
                if (TournamentAuction.OversFor(save.Setup.Seed, team) is int overs) results.OfferTournament(overs, save.Setup.Seed);
            };
            _director.Begin(live, _data, team);
        }
    }
}
