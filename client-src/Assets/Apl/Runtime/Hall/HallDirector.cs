using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Audio;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Hall
{
    /// <summary>
    /// Runs the show: drives the auction (solo LiveAuction or a room replica)
    /// in real time and turns each event into camera cuts, character
    /// animation, voice lines and HUD updates.
    /// </summary>
    public sealed partial class HallDirector : MonoBehaviour
    {
        public HallCamera Camera = null!;
        public StageCast Cast = null!;
        public VoiceQueue Auctioneer = null!;
        public VoiceQueue Assistant = null!;
        public Sfx Sfx = null!;
        public Ceremony? Ceremony;
        public StageFx? Fx;

        /// <summary>Play the team walk-on before the first lot (players can skip it).</summary>
        public bool PlayCeremony = true;

        /// <summary>The auctioneer reads the rules (~30 s, skippable) before the first lot.</summary>
        public bool PlayBriefing = true;

        /// <summary>Raised once the last lot is closed and the closing line is said.</summary>
        public event Action? Finished;

        private readonly Dictionary<string, TeamTable> _tables = new Dictionary<string, TeamTable>();
        private IAuctionDriver _driver = null!;
        private Dataset _data = null!;
        private string _me = "";
        private HallHud _hud = null!;
        private string? _lastSet;
        private string? _lastVoiceSet;

        /// <summary>The results screen is up: the purse boards stay off.</summary>
        private bool _resultsUp;
        private int _bidsThisLot;
        private string? _goingCalled;
        private int _loudestBid;
        private float _returnToStageAt = -1;

        /// <summary>Seconds without a bid before the camera leaves the bidding table for the hall.</summary>
        private const float QuietBeforeWide = 2.5f;

        /// <summary>No shot shorter than this: no cut to the hall or to a conferring table if a bid or call follows sooner.</summary>
        private const float MinShot = 0.8f;

        /// <summary>
        /// Seconds (game time) to the next thing that will cut the camera: the next scheduled AI bid
        /// (solo) or the auctioneer's next call (the bid timer's call marks), whichever is sooner.
        /// </summary>
        private float SecondsToNextEvent()
        {
            var next = float.MaxValue;
            if (_driver is LiveAuction live)
            {
                foreach (var (_, dueAt) in live.ScheduledBids) next = Mathf.Min(next, (dueAt - live.NowMs) / 1000f);
            }
            if (_driver.Phase == LivePhase.Bidding && _bidsThisLot > 0)
            {
                float left = _driver.BidTimeLeftMs, total = _driver.BidTimerTotalMs;
                var marks = _skipThrough ? SkipMarks : CallMarks;
                foreach (var m in marks)
                {
                    if (left <= total * m) continue;
                    next = Mathf.Min(next, (left - total * m) / 1000f);
                    break;
                }
            }
            return next;
        }

        /// <summary>Seconds (game time) to the next scheduled AI bid (solo only; infinite otherwise).</summary>
        private float SecondsToNextBid()
        {
            var next = float.MaxValue;
            if (_driver is LiveAuction live)
            {
                foreach (var (_, dueAt) in live.ScheduledBids) next = Mathf.Min(next, (dueAt - live.NowMs) / 1000f);
            }
            return next;
        }

        /// <summary>The bid-timer fractions where CallGoing speaks (0 = the hammer).</summary>
        private static readonly float[] CallMarks = { 0.5f, 0.35f, 0.15f, 0f };
        private static readonly float[] SkipMarks = { 0.85f, 0.62f, 0.35f, 0.15f, 0f };

        /// <summary>The player skipped (or passed) this lot: the AI bidding is settled.</summary>
        private bool _skipThrough;

        /// <summary>Bids are arriving all at once (a settle): no paddles or calls for each.</summary>
        private bool _racing;

        /// <summary>AI bids already considered for a "talk it over first" moment, this lot.</summary>
        private readonly HashSet<string> _conferChecked = new HashSet<string>();
        private int _lastBidFrame = -1;
        private bool _anySaid;
        private string? _rtmOfferTeam;
        private readonly HashSet<string> _biddersThisLot = new HashSet<string>();

        public void Begin(IAuctionDriver driver, Dataset data, string humanTeamId)
        {
            _driver = driver;
            _data = data;
            _me = humanTeamId;
            foreach (var t in FindObjectsByType<TeamTable>(FindObjectsSortMode.None)) _tables[t.TeamId] = t;
            MoveToFront(_me);
            if (_tables.TryGetValue(_me, out var mine)) mine.SetHighlight(true);
            Camera.ShotChanged += OnShot;

            _hud = HallHud.Create();
            _hud.BidPressed += HumanBidPressed;
            _hud.SkipPressed += SkipLot;
            _hud.FastForwardPressed += () =>
            {
                _driver.HoldingFastForward = !_driver.HoldingFastForward;
                _hud.SetFastForward(_driver.HoldingFastForward, _driver.CanHoldFastForward);
            };
            _hud.SetFastForward(false, _driver.CanHoldFastForward);
            _hud.SetSkipLabel("SKIP ►►");
            _hud.PassPressed += PassLot;
            _hud.ShowPass(true);
            // A room has no SKIP and no FAST at all (hidden, not disabled): the server sets the pace.
            if (Room != null) _hud.ShowSkip(false);
            // SIMULATE: solo finishes the auction at once; a room hands your team to a bot (HallDirector.Simulate.cs).
            _hud.SimulatePressed += AskSimulate;
            _hud.ShowSimulate(true);
            _hud.RtmAnswered += use =>
            {
                _hud.ShowRtmPrompt("", false);
                _driver.HumanRtm(_me, use);
            };

            _driver.OnEvent += OnEvent;
            // Room: seat strip, host panel, toasts, timer (HallDirector.Room.cs).
            if (Room != null) BeginRoom();
            _hud.RulesSkipPressed += () =>
            {
                _skipRules = true;
                RememberSkip(SkipRulesKey);
            };
            _hub = AuctionHub.Create(_data);
            _hud.HubPressed += () =>
            {
                // Solo stops the clock while you browse; a room can't wait for one phone.
                _hubPaused = _driver is LiveAuction;
                _driver.HoldingFastForward = false;
                _hub.Open(_driver.Engine, _me, _hubPaused);
                _hud.gameObject.SetActive(false);
            };
            // The auction HUD (lot bar, card, BID / PASS, SIMULATE) hides while the hub is up (PM r15: it showed through).
            _hub.Closed += () =>
            {
                _hubPaused = false;
                if (_driver.Phase != LivePhase.Finished) _hud.gameObject.SetActive(true);
            };
            _pause = PauseMenu.Create();
            _hud.PausePressed += () => _pause.Open(freezes: _driver is LiveAuction);
            _pause.HubPressed += () =>
            {
                _pause.Close();
                _hubPaused = _driver is LiveAuction;
                _driver.HoldingFastForward = false;
                _hub.Open(_driver.Engine, _me, _hubPaused);
                _hud.gameObject.SetActive(false);
            };
            Camera.Show("wide", reason: "open");
            // Skipped once, skipped for good (rater r1 #28: 45 s before lot 1 on every auction).
            if (PlayCeremony && PlayerPrefs.GetInt(SkipCeremonyKey, 0) == 1)
            {
                PlayCeremony = false;
                Debug.Log("[APL hall] ceremony off: you skipped it before (" + SkipCeremonyKey + ")");
            }
            if (PlayBriefing && PlayerPrefs.GetInt(SkipRulesKey, 0) == 1)
            {
                PlayBriefing = false;
                Debug.Log("[APL hall] rules briefing off: you skipped it before (" + SkipRulesKey + ")");
            }
            if (PlayCeremony && Ceremony != null)
            {
                // The ceremony opens with the owners' welcome.
                _hud.ShowCeremony(true);
                _hud.CeremonySkipPressed += Ceremony.Skip;
                _hud.CeremonySkipPressed += () => RememberSkip(SkipCeremonyKey);
                StartCoroutine(Ceremony.Run(_data.Franchises, _tables, _me, AfterCeremony));
            }
            else
            {
                Auctioneer.Say("arjun", "welcome");
                Sfx.Applause(0.8f);
                AfterCeremony();
            }
        }

        /// <summary>PlayerPrefs: the player skipped the walk-on / the rules once, so later solo auctions go straight in.</summary>
        public const string SkipCeremonyKey = "apl.skip.ceremony", SkipRulesKey = "apl.skip.rules";

        private static void RememberSkip(string key)
        {
            if (PlayerPrefs.GetInt(key, 0) == 1) return;
            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();
            Debug.Log("[APL hall] remembered: " + key);
        }

        private bool _auctionStarted;
        private bool _finishing;
        private bool _skipRules;
        private AuctionHub _hub = null!;
        private PauseMenu _pause = null!;
        private bool _hubPaused;

        private void AfterCeremony()
        {
            _hud.ShowCeremony(false);
            if (PlayBriefing) StartCoroutine(Briefing());
            else StartAuction();
        }

        /// <summary>
        /// The auctioneer walks through the rules for this format — purse,
        /// squad limits, auto-fill, retention, RTM, the going-once calls —
        /// while the card highlights each one.
        /// </summary>
        private IEnumerator Briefing()
        {
            var f = _driver.Engine.Format;
            var retained = _driver.Engine.Teams.Values.Any(t => t.Squad.Any(p => p.Kind == PurchaseKind.Retained));
            var steps = new List<(string key, string text)>
            {
                ("rules_purse_" + f.Id, $"Fixed purse: <b>{Money.Format(f.Purse)}</b> per franchise. Once it's spent, it's gone."),
                ("rules_squad_" + f.Id, $"Squad of <b>{f.MinSquad} to {f.MaxSquad}</b> players, at most <b>{f.MaxOverseas} overseas</b>."),
                ("rules_fill", "Short of the minimum? Empty slots are filled with base-price players."),
            };
            if (retained) steps.Add(("rules_retention", "Retained players are already paid for from your purse."));
            steps.Add(("rules_rtm", $"<b>{_driver.Engine.Rules.RtmCards} Right to Match</b> card: take back a player from last season at the final bid."));
            steps.Add(("rules_bidding", "Going once... going twice... <b>SOLD!</b>"));
            steps.Add(("rules_go", "Tap <b>BID</b> to raise, <b>PASS</b> to let a player go, <b>SKIP</b> to jump to the final calls, <b>►►</b> to fast-forward."));
            var texts = steps.Select(st => st.text).ToList();

            _skipRules = false;
            Camera.Show("stage", reason: "rules");
            Sfx.SetCrowd(0.6f);
            Auctioneer.Say("arjun", "rules_intro");
            Cast.Announce();
            _hud.ShowRules(texts, -1);
            while (Auctioneer.Busy && !_skipRules) yield return null;
            for (var i = 0; i < steps.Count && !_skipRules; i++)
            {
                _hud.ShowRules(texts, i);
                if (i == 2) Camera.Show("auctioneer", cut: false, reason: "rules");
                Auctioneer.Say("arjun", steps[i].key);
                if (i % 2 == 0) Cast.Announce();
                while (Auctioneer.Busy && !_skipRules) yield return null;
            }
            if (_skipRules) Auctioneer.Interrupt();
            _hud.ShowRules(null);
            Sfx.SetCrowd(1f);
            Sfx.Applause(0.9f);
            StartAuction();
        }

        private PurseBoard? _purseBoard;
        private float _boardAt;

        private void StartAuction()
        {
            // Purse boards on the LED wall, either side of the crest.
            var wall = Ceremony != null && Ceremony.LedWall != null ? Ceremony.LedWall.bounds.center : new Vector3(0, 3.4f, 11.6f);
            _purseBoard = PurseBoard.Create(_data, _me, new Vector3(0, wall.y - 0.1f, wall.z - 0.12f), 4.9f);
            OnShot(Camera.Current);
            _auctionStarted = true;
            _driver.Start();
            // Joining a room mid-lot: show the player already on the block.
            if (!(_driver is LiveAuction) && _driver.Engine.CurrentLot is Lot current) OnReveal(current);
        }

        private void Update()
        {
            if (_driver == null || !_auctionStarted || _hubPaused) return;
            // Cap a frame's step (a hitch shouldn't skip a lot), scaled when time is sped up.
            var ms = (float)Mathf.Clamp(Mathf.RoundToInt(Time.deltaTime * 1000), 0, Mathf.RoundToInt(200 * Mathf.Max(1f, Time.timeScale)));
            // A room the host paused: every countdown stands still until the server resumes.
            if (Room == null || !Room.Paused) _driver.Advance(Mathf.Max(1, Mathf.RoundToInt(ms)));
            UpdateHud();
            if (Room != null) RoomTick();
            CallGoing();
            FinishCardRead();
            ConferBeforeBids();
            // Back to the hall only if it can hold the screen: not when a bid or a call is due within
            // MinShot (it would flash for a fraction of a second, then cut away).
            if (_returnToStageAt > 0 && Time.time >= _returnToStageAt && SecondsToNextEvent() >= MinShot)
            {
                // Back to the hall; close-ups of the auctioneer are only for his calls.
                _returnToStageAt = -1;
                Camera.Show("wide", reason: "rest");
                ChatterInTheHall();
            }
        }

        // ------------------------------------------------------------- events

        private void OnEvent(LiveEvent e)
        {
            switch (e)
            {
                case LotRevealed r: OnReveal(r.Lot); break;
                case BidPlaced b: OnBid(b.TeamId, b.Amount); break;
                case RtmOffered o: OnRtmOffer(o.Offer); break;
                case LotClosed c: OnClosed(c.Result); break;
                case AuctionFinished _:
                    // Once (a room can also finish through a rebuilt log: HallDirector.Room.cs).
                    if (_finishing) break;
                    _finishing = true;
                    _hud.ShowSimulate(false);
                    Camera.Show("wide", reason: "finished");
                    Auctioneer.Say("arjun", "auction_over");
                    Sfx.Applause(1f);
                    Sfx.Cheer(0.8f);
                    Fx?.Burst(320);
                    Invoke(nameof(RaiseFinished), 4.5f);
                    break;
            }
        }

        /// <summary>
        /// BID (rater r1 #2): during a solo reveal the player's own opening bid ends the reveal at
        /// once — the engine is stepped exactly to the end of its Revealing phase (the same
        /// Revealing → Bidding move the clock would make), then the bid goes in as usual.
        /// Rooms keep the server's timing.
        /// </summary>
        private void HumanBidPressed()
        {
            if (Room == null && _driver is LiveAuction live && live.Phase == LivePhase.Revealing)
            {
                for (var guard = 0; live.Phase == LivePhase.Revealing && guard < 4; guard++)
                {
                    var ms = Math.Max(1, (int)Math.Ceiling(live.PhaseTimeLeftMs / Math.Max(1.0, live.Speed)));
                    live.Advance(ms);
                }
                Debug.Log($"[APL auction] reveal ended early by your bid (lot {live.Engine.CurrentLot?.Number})");
            }
            _driver.HumanBid(_me);
        }

        /// <summary>
        /// The auctioneer is still reading the card (name, role, country, base price): bids that land
        /// now don't cut him off; the latest amount is called once he's done (the reveal is short now).
        /// </summary>
        private bool _readingCard;
        private bool _cardHandedOver;
        private int _heldAmountCall;
        private string? _heldAmountTeam;

        private void FinishCardRead()
        {
            if (!_readingCard || !_cardHandedOver || Auctioneer.Busy) return;
            _readingCard = false;
            if (_heldAmountCall <= 0 || _driver.Phase != LivePhase.Bidding) return;
            Auctioneer.Say("arjun", Auctioneer.Bank.AmountKeys(_heldAmountCall).ToArray());
            if (_heldAmountTeam != null) Auctioneer.Say("arjun", "with_team", "team_" + _heldAmountTeam);
            _heldAmountCall = 0;
            _heldAmountTeam = null;
        }

        private void OnReveal(Lot lot)
        {
            _readingCard = true;
            _cardHandedOver = false;
            _heldAmountCall = 0;
            _heldAmountTeam = null;
            _hud.ShowCall(null);
            _bidsThisLot = 0;
            _goingCalled = null;
            _loudestBid = 0;
            _skipThrough = false;
            _anySaid = false;
            _rtmOfferTeam = null;
            _conferChecked.Clear();
            Sfx.SetCrowd(1f);
            _biddersThisLot.Clear();
            var p = lot.Player;
            // IPL sets (solo: M1, BA1, UFA2, ACC …) or the data's set names (rooms); the auctioneer
            // announces a set only when its voice line changes (FA1 → SP1 are both "bowlers").
            var set = _driver.Engine.CurrentSet ?? p.Set;
            if (set != _lastSet)
            {
                var voice = AuctionSets.VoiceSet(set);
                if (voice != _lastVoiceSet) Auctioneer.Say("arjun", "set_" + voice);
                Debug.Log($"[APL sets] lot {lot.Number}: set {set} '{AuctionSets.Label(set)}' (voice set_{voice}{(voice == _lastVoiceSet ? ", not repeated" : "")}); {_driver.Engine.Upcoming.Count(u => _driver.Engine.SetOf(u) == set)} more in this set, {_driver.Engine.LotsRemaining} lots to go");
                _lastVoiceSet = voice;
                _lastSet = set;
            }

            // Assistant draws the card, walks it to the podium, and the
            // auctioneer reads it out once it's in his hand.
            Camera.Show("stage", reason: $"reveal lot {lot.Number} {p.Id}");
            Assistant.Say("kajal", lot.Number == 1 ? "first_player" : "next_player");
            var lines = new List<string> { "name_" + p.Id, "role_" + p.Role.Code(), "from_" + p.Country.Replace(' ', '_'), "base_price" };
            lines.AddRange(Auctioneer.Bank.AmountKeys(p.BasePrice));
            Cast.DeliverCard(() =>
            {
                Cast.Announce();
                Auctioneer.Say("arjun", lines.ToArray());
                _cardHandedOver = true;
                Camera.Show("auctioneer", cut: false, reason: "reads-card");
                _returnToStageAt = Time.time + 4f;
            });
            _hud.ShowLot(lot, SetLabel(set), _driver.Engine.LotsRemaining);
            AuctionLog.Revealed(lot);
        }

        private void ClearCard() => Cast.ClearCard();

        private float _pendingApplause, _pendingCheer;
        private string _soldTo = "";

        private void ShowBuyer()
        {
            if (BuyersShot(_soldTo) is string shot) Camera.Show(shot, reason: "sold-buyer " + _soldTo);
        }

        /// <summary>
        /// The winners after the hammer: the team's table shot, a little higher and tighter, so the
        /// side members sit above the SOLD strip or out at the frame edges instead of behind it
        /// (no paddle goes up here, so the headroom the bid shot keeps isn't needed). Derived at
        /// run time from the table shot (MoveToFront has already swapped those).
        /// </summary>
        private string? BuyersShot(string teamId)
        {
            var name = "buyers_" + teamId;
            if (Camera.Has(name)) return name;
            var table = Camera.Shots.Find(s => s.Name == "table_" + teamId);
            if (table == null) return null;
            Camera.Shots.Add(new Shot
            {
                Name = name,
                Position = table.Position + new Vector3(0, 0.2f, -0.4f),
                LookAt = table.LookAt + new Vector3(0, -0.2f, 0),
                FieldOfView = 32,
            });
            return name;
        }

        private void SoldApplause()
        {
            Sfx.Applause(_pendingApplause);
            if (_pendingCheer > 0) Sfx.Cheer(_pendingCheer);
        }

        /// <summary>SKIP: out of this lot, and (solo) fast through to the closing calls.</summary>
        /// <summary>
        /// SKIP (solo): race through the AI back-and-forth to where the bidding
        /// settles, then carry on at normal pace — you can still bid, and the
        /// closing calls come as usual. In a room the server sets the pace, so
        /// the button is PASS there.
        /// </summary>
        private void SkipLot()
        {
            if (!(_driver is LiveAuction live))
            {
                _driver.HumanPass(_me);
                return;
            }
            // The AI teams bid it out at once; then the closing calls, and only
            // you can still raise.
            Settle(live, closeNow: false);
            var lot = _driver.Engine.CurrentLot;
            if (lot?.LeaderId is string leader)
            {
                Auctioneer.Say("arjun", Auctioneer.Bank.AmountKeys(lot.CurrentBid ?? lot.Player.BasePrice).ToArray());
                Auctioneer.Say("arjun", "with_team", "team_" + leader);
                if (Camera.Has("table_" + leader)) Camera.Show("table_" + leader, reason: "skip-leader " + leader);
                _returnToStageAt = Time.time + 1.8f;
            }
            else
            {
                Camera.Show("wide", reason: "skip-no-bid");
            }
        }

        /// <summary>
        /// PASS (solo): you're out of this player. The AI teams settle him at
        /// once and the lot ends straight away — SOLD (or unsold) as usual.
        /// </summary>
        private void PassLot()
        {
            if (Room != null)
            {
                RoomPass();
                return;
            }
            if (!(_driver is LiveAuction live)) return;
            // Passing as the only human settles the lot inside the engine: keep it quiet.
            _racing = true;
            try
            {
                live.HumanPass(_me);
            }
            finally
            {
                _racing = false;
            }
            Settle(live, closeNow: true);
        }

        private void Settle(LiveAuction live, bool closeNow)
        {
            _skipThrough = true;
            Auctioneer.Interrupt();
            // The read-out is cut short by SKIP / PASS: nothing held for after it.
            _readingCard = false;
            _heldAmountCall = 0;
            _returnToStageAt = -1;
            _racing = true;
            try
            {
                live.SettleAi(closeNow);
            }
            finally
            {
                _racing = false;
            }
        }

        private void RaiseFinished()
        {
            _hud.gameObject.SetActive(false);
            // The LED purse boards would show through the results (PM r15): off for good.
            _resultsUp = true;
            _purseBoard?.SetVisible(false);
            Finished?.Invoke();
        }

        /// <summary>
        /// The hall is built with one team front and centre; swap the human's
        /// table (and its camera shot) into that spot.
        /// </summary>
        private void MoveToFront(string teamId)
        {
            if (!_tables.TryGetValue(teamId, out var mine) || _tables.Count < 2) return;
            var front = _tables.Values
                .OrderBy(t => Mathf.Abs(t.transform.position.x))
                .ThenByDescending(t => t.transform.position.z)
                .First();
            if (front == mine) return;
            var spot = front.transform.position;
            front.transform.position = mine.transform.position;
            mine.transform.position = spot;
            var a = Camera.Shots.Find(s => s.Name == "table_" + teamId);
            var b = Camera.Shots.Find(s => s.Name == "table_" + front.TeamId);
            if (a != null && b != null)
            {
                a.Name = "table_" + front.TeamId;
                b.Name = "table_" + teamId;
            }
        }


        private void OnBid(string teamId, int amount)
        {
            // Bidding after SKIP reopens the lot: normal calls again.
            if (teamId == _me && !_racing) _skipThrough = false;
            // A room settled a lot everyone passed on: its bids land in one burst.
            // Animate the first; the rest only move the board.
            var burst = Time.frameCount == _lastBidFrame;
            _lastBidFrame = Time.frameCount;
            _bidsThisLot++;
            _biddersThisLot.Add(teamId);
            _goingCalled = null;
            // A new bid clears the GOING ONCE / TWICE chip (rater r1 #9).
            _hud.ShowCall(null);
            var team = _data.Franchises.First(f => f.Id == teamId);
            if (_racing || burst)
            {
                _hud.ShowBid(amount, team, teamId == _me);
                return;
            }
            if (_tables.TryGetValue(teamId, out var table)) table.Bid();
            Sfx.Paddle();
            // The room warms up as a lot gets contested, and reacts to big money.
            Sfx.SetCrowd(1f + Mathf.Min(_bidsThisLot, 10) * 0.07f);
            var milestone = amount >= 2000 ? 3 : amount >= 1000 ? 2 : amount >= 500 ? 1 : 0;
            if (milestone > _loudestBid)
            {
                _loudestBid = milestone;
                if (milestone >= 2) Sfx.Ooh(milestone == 3 ? 0.9f : 0.65f);
            }

            // Every bid cuts to the bidding table (paddle up), ours included but shorter.
            if (Camera.Has("table_" + teamId))
            {
                Camera.Show("table_" + teamId, reason: $"bid {teamId} {amount}");
                // In a bidding war the next bid cuts table to table; the hall comes back only
                // after a quiet spell (a 0.4 s flash of the wide between bids reads as a jump).
                _returnToStageAt = Time.time + QuietBeforeWide;
            }
            Cast.PointAtBidder();

            _hud.ShowBid(amount, team, teamId == _me);
            if (_readingCard)
            {
                // Still reading the card (short reveal): hold the amount call until he's done (FinishCardRead).
                _heldAmountCall = amount;
                _heldAmountTeam = _bidsThisLot == 1 || _bidsThisLot % 3 == 1 ? teamId : _heldAmountTeam;
                return;
            }
            // Fresh amount replaces a stale call; name the team now and then.
            Auctioneer.Interrupt();
            Auctioneer.Say("arjun", Auctioneer.Bank.AmountKeys(amount).ToArray());
            if (_bidsThisLot % 3 == 1) Auctioneer.Say("arjun", "with_team", "team_" + teamId);
        }

        private void OnRtmOffer(RtmOffer offer)
        {
            _rtmOfferTeam = offer.TeamId;
            _returnToStageAt = -1;
            _hud.ShowCall("RIGHT TO MATCH?");
            // The previous team huddles over the card before answering.
            if (_tables.TryGetValue(offer.TeamId, out var huddle)) huddle.Confer(4.5f);
            Auctioneer.Interrupt();
            Auctioneer.Hold(0.6f);
            Auctioneer.Say("arjun", "rtm_ask_" + offer.TeamId);
            if (Camera.Has("table_" + offer.TeamId)) Camera.Show("table_" + offer.TeamId, reason: "rtm-ask " + offer.TeamId);
            if (offer.TeamId == _me)
            {
                var winner = _data.Franchises.First(f => f.Id == offer.WinnerId);
                _hud.ShowRtmPrompt($"RIGHT TO MATCH\n{_driver.Engine.CurrentLot!.Player.Name} went to {winner.ShortName} for {Money.Format(offer.Price)}.\nUse your RTM card to take him at that price?", true);
            }
        }

        private void OnClosed(LotResult r)
        {
            _returnToStageAt = -1;
            _hud.ShowRtmPrompt("", false);
            // The last call on the chip: SOLD (the strip takes over a beat later) or UNSOLD.
            _hud.ShowCall(r.TeamId != null ? "SOLD!" : "UNSOLD");
            _readingCard = false;
            _heldAmountCall = 0;
            var hadOffer = _rtmOfferTeam;
            Sfx.SetCrowd(1f);
            Auctioneer.Interrupt();
            var buyer = r.TeamId == null ? null : _data.Franchises.First(f => f.Id == r.TeamId);
            AuctionLog.Closed(r, _driver.Engine);
            Debug.Log(buyer == null
                ? $"[APL auction] UNSOLD {r.Player.Id} - 0 base {r.Player.BasePrice} value {r.Player.Value} impact {r.Player.Impact}"
                : $"[APL auction] SOLD {r.Player.Id} {buyer.Id} {r.Price} base {r.Player.BasePrice} value {r.Player.Value} impact {r.Player.Impact}{(r.ViaRtm ? " rtm" : "")}");
            if (buyer == null)
            {
                Invoke(nameof(ClearCard), 1.8f);
                _hud.ShowResult(r, null);
                Auctioneer.Say("arjun", "unsold");
                Camera.Show("auctioneer", cut: false, reason: "unsold " + r.Player.Id);
                return;
            }
            if (hadOffer != null)
            {
                // The table answers first (RTM card up, or the "no"); the SOLD
                // card and the hammer follow a beat later.
                if (Camera.Has("table_" + hadOffer)) Camera.Show("table_" + hadOffer, reason: "rtm-answer " + hadOffer);
                if (r.ViaRtm)
                {
                    if (_tables.TryGetValue(hadOffer, out var rtmTable)) rtmTable.UseRtm();
                    Assistant.Say("kajal", "rtm_used_" + buyer.Id);
                    Sfx.Ooh(0.8f);
                }
                else
                {
                    if (_tables.TryGetValue(hadOffer, out var declined)) declined.DeclineRtm();
                    Auctioneer.Say("arjun", "rtm_declined");
                }
                StartCoroutine(AnnounceSale(r, buyer, delay: 1.2f, afterRtm: true));
                return;
            }
            // The hammer: camera on the auctioneer, wind-up, one big beat on the block.
            Camera.Show("auctioneer", reason: "hammer " + r.Player.Id);
            Cast.StrikeGavel(Sfx.GavelHit, 1);
            // The SOLD strip lands just after the rap (the hit is ~0.33 s into the strike).
            StartCoroutine(AnnounceSale(r, buyer, delay: 0.45f, afterRtm: false));
        }

        /// <summary>The SOLD card, the call, the applause and the buyers' table.</summary>
        private IEnumerator AnnounceSale(LotResult r, Franchise buyer, float delay, bool afterRtm)
        {
            if (delay > 0) yield return new WaitForSeconds(delay);
            if (afterRtm) Sfx.GavelHit();
            Invoke(nameof(ClearCard), 1.8f);
            Auctioneer.Hold(afterRtm ? 0.2f : 0.75f);
            _hud.ShowResult(r, buyer, buyer.Id == _me);
            var lines = new List<string> { "sold", "sold_to", "team_" + buyer.Id, "for" };
            lines.AddRange(Auctioneer.Bank.AmountKeys(r.Price!.Value));
            Auctioneer.Say("arjun", lines.ToArray());
            var price = r.Price.Value;
            _pendingApplause = price >= 1000 ? 1f : price >= 300 ? 0.6f : 0.35f;
            _pendingCheer = buyer.Id == _me ? 1f : price >= 1500 ? 0.7f : 0f;
            if (price >= 1500) Fx?.Burst(140);
            Invoke(nameof(SoldApplause), 0.8f);
            // After the hammer, the winners.
            _soldTo = buyer.Id;
            Invoke(nameof(ShowBuyer), afterRtm ? 0.4f : 1.4f);
            if (_tables.TryGetValue(buyer.Id, out var winner)) winner.Won();
            foreach (var loser in _biddersThisLot.Where(t => t != buyer.Id))
            {
                if (_tables.TryGetValue(loser, out var table)) table.LostOut();
            }
            if (price >= 1000) Cast.Applaud();
        }

        // --------------------------------------------------------------- per frame

        private void CallGoing()
        {
            if (_driver.Phase != LivePhase.Bidding || _bidsThisLot == 0) return;
            var left = _driver.BidTimeLeftMs;
            var total = _driver.BidTimerTotalMs;
            if (_racing) return;
            // After SKIP the whole timer is the closing: "any more?", "I'll sell
            // to…", then going once, twice, sold.
            string? call = _skipThrough
                ? (left <= total * 0.15f ? "going_twice" : left <= total * 0.35f ? "going_once" : left <= total * 0.62f ? "no_more" : left <= total * 0.85f ? "any" : null)
                : (left <= total * 0.15f ? "going_twice" : left <= total * 0.35f ? "going_once" : left <= total * 0.5f ? "any" : null);
            if (call == null || call == _goingCalled) return;
            _goingCalled = call;
            // The call on screen too (rater r1 #9; no numeric countdown, owner's request stands):
            // a chip on the CURRENT BID box, set with the auctioneer's own call.
            if (call == "going_once") _hud.ShowCall("GOING ONCE…");
            else if (call == "going_twice") _hud.ShowCall("GOING TWICE…");
            if (call == "no_more")
            {
                var seller = _driver.Engine.CurrentLot?.LeaderId;
                if (seller == null) return;
                Auctioneer.Say("arjun", "no_more_bids", "team_" + seller);
                Cast.Announce();
                if (SecondsToNextBid() >= MinShot) Camera.Show("auctioneer", reason: "no-more-bids");
                _returnToStageAt = Time.time + 2.6f;
                return;
            }
            if (call == "any")
            {
                if (_anySaid || (!_skipThrough && UnityEngine.Random.value < 0.4f)) return;
                _anySaid = true;
                // The room's gone quiet: the auctioneer fishes for one more bid, on camera.
                var leader = _driver.Engine.CurrentLot?.LeaderId;
                if (leader != null && !_skipThrough && UnityEngine.Random.value < 0.35f)
                {
                    Auctioneer.Say("arjun", "with_team", "team_" + leader);
                }
                Auctioneer.Say("arjun", "any_" + UnityEngine.Random.Range(1, 7));
                Cast.PointAtBidder();
                if (SecondsToNextBid() >= MinShot) Camera.Show("auctioneer", reason: "any-more");
                _returnToStageAt = Time.time + 2.4f;
                return;
            }
            // The hammer only comes down on SOLD.
            Auctioneer.Say("arjun", call);
            Cast.Announce();
            // A bid landing within MinShot cuts to its table instead (no 0.3 s flash of him).
            if (SecondsToNextBid() >= MinShot) Camera.Show("auctioneer", reason: call);
            // Going once -> going twice -> SOLD stays on him; a new bid cuts to its table.
            _returnToStageAt = -1;
            // The room hushes for the final call.
            if (call == "going_twice") Sfx.SetCrowd(0.55f);
        }

        /// <summary>
        /// Now and then an AI team talks it over before its bid lands: the
        /// camera finds the table, two of them confer, then the paddle goes up.
        /// Solo only (a room's bids aren't known in advance).
        /// </summary>
        private void ConferBeforeBids()
        {
            if (!(_driver is LiveAuction live) || _racing || _skipThrough || _driver.Phase != LivePhase.Bidding) return;
            foreach (var (teamId, dueAt) in live.ScheduledBids)
            {
                var ahead = dueAt - live.NowMs;
                if (ahead < 1600 || ahead > 3200) continue;
                if (!_conferChecked.Add(teamId + ":" + dueAt)) continue;
                if (UnityEngine.Random.value > 0.3f || !_tables.TryGetValue(teamId, out var table)) continue;
                table.Confer(ahead / 1000f);
                // Only cut to them from the hall view, not over the auctioneer's calls.
                // Not straight after a cut either (the wide would flash for a fraction of a second).
                if (_returnToStageAt < 0 && Time.time - Camera.ShownAt >= MinShot && Camera.Has("table_" + teamId) && OtherEventsClear(teamId, dueAt))
                {
                    Camera.Show("table_" + teamId, reason: "confer " + teamId);
                    // The badge still says who leads; over this table it read as the wrong team.
                    _hud.ShowThinking(_data.Franchises.First(f => f.Id == teamId));
                    _returnToStageAt = Time.time + ahead / 1000f + 1.6f;
                }
            }
        }

        /// <summary>
        /// Per shot: the LED purse boards only in the shots that hold them whole (wide, stage),
        /// and your table's floor ring off in the table close-ups (it flares white at the frame edge).
        /// </summary>
        private void OnShot(string shot)
        {
            _purseBoard?.SetVisible(!_resultsUp && (shot == "wide" || shot == "stage"));
            // Your floor ring marks your table in the hall view only: from any closer shot (the stage
            // shot passes over your table) it flares as a white shard at the frame edge.
            if (_tables.TryGetValue(_me, out var mine)) mine.SetHighlight(shot == "wide");
            // Table close-ups: no name plates. The table behind the bidder showed its plate over his
            // head ("HYH" above MBH) and your own plate's letters peeked between the HUD panels; the
            // HUD's BID BY badge names the team.
            var close = shot.StartsWith("table_") || shot.StartsWith("buyers_");
            // Leaving a conferring table: the badge goes back to the current bid.
            if (!shot.StartsWith("table_") && _hud != null) _hud.EndThinking();
            if (_signs == null)
            {
                _signs = new List<Renderer>();
                foreach (var t in _tables.Values)
                {
                    foreach (Transform child in t.transform)
                    {
                        if ((child.name == "Sign" || child.name == "SignText") && child.TryGetComponent<Renderer>(out var r)) _signs.Add(r);
                    }
                }
            }
            foreach (var r in _signs) r.enabled = !close;
        }

        private List<Renderer>? _signs;

        /// <summary>A confer cut only if no other bid and no call lands within a second (else it flashes).</summary>
        private bool OtherEventsClear(string teamId, int dueAt)
        {
            if (!(_driver is LiveAuction live)) return false;
            foreach (var (t, at) in live.ScheduledBids)
            {
                if (t == teamId && at == dueAt) continue;
                if ((at - live.NowMs) / 1000f < 1f) return false;
            }
            var calls = SecondsToNextEvent();
            // SecondsToNextEvent includes this team's own bid (1.6 s+ away), so a sooner value is a call.
            return calls >= 1f;
        }

        /// <summary>Back on the wide shot after a bid: a table or two chat among themselves.</summary>
        private void ChatterInTheHall()
        {
            if (_driver.Phase != LivePhase.Bidding) return;
            var leader = _driver.Engine.CurrentLot?.LeaderId;
            foreach (var t in _tables.Values)
            {
                if (t.TeamId != leader && t.TeamId != _me && UnityEngine.Random.value < 0.18f) t.Confer(UnityEngine.Random.Range(1.8f, 3f));
            }
        }

        private void UpdateHud()
        {
            var engine = _driver.Engine;
            var me = engine.Teams[_me];
            _hud.ShowMine(me, engine.Format.MaxSquad, engine.Format.MaxOverseas);
            // Purses: LED boards and the HUD tab, a few times a second.
            if (Time.unscaledTime >= _boardAt)
            {
                _boardAt = Time.unscaledTime + 0.25f;
                var leader = engine.CurrentLot?.LeaderId;
                _purseBoard?.Refresh(engine, leader);
                _hud.ShowTeams(_data, engine, _me, leader);
            }
            var onBlock = engine.CurrentLot;
            _hud.ShowRtm(me.RtmCards, engine.Rules.RtmCards,
                eligible: onBlock != null && me.RtmCards > 0 && onBlock.Player.PreviousTeam == _me && _driver.Phase != LivePhase.Result);
            _hud.ShowTimer(_driver.BidTimeLeftMs, _driver.BidTimerTotalMs, _driver.Phase == LivePhase.Bidding);
            var lot = engine.CurrentLot;
            if (Room != null)
            {
                RoomBidControls(engine, lot);
                return;
            }
            // Solo: BID is live from the reveal on — the player's opening bid ends the reveal (HumanBidPressed).
            var bidding = (_driver.Phase == LivePhase.Bidding || (_driver.Phase == LivePhase.Revealing && _driver is LiveAuction)) && lot != null;
            var reason = bidding ? engine.CheckBid(_me) : null;
            string label;
            if (!bidding) label = "WAIT…";
            else if (reason == null) label = "BID " + Money.Format(engine.NextBidAmount);
            else
            {
                label = reason switch
                {
                    BidRejection.AlreadyLeading => "YOU LEAD",
                    BidRejection.SquadFull => "SQUAD FULL",
                    BidRejection.OverseasLimit => "OVERSEAS FULL",
                    BidRejection.CannotAfford => "CAN'T AFFORD",
                    BidRejection.PriceCap => "MAX " + Money.Format(engine.MaxPrice ?? 0),
                    BidRejection.Passed => "SKIPPED",
                    _ => "WAIT…",
                };
            }
            _hud.SetBidButton(label, bidding && reason == null);
            _hud.SetSkipEnabled(!_skipThrough && lot != null && lot.LeaderId != _me && !lot.Passed.Contains(_me) &&
                                (_driver.Phase == LivePhase.Bidding || _driver.Phase == LivePhase.Revealing));
        }

        /// <summary>The HUD's set name: "Capped batters 1 (BA1)" for an IPL set, "Batters" for a room's.</summary>
        private static string SetLabel(string set)
        {
            var label = AuctionSets.Label(set);
            var named = label.Length > 1 ? label.Substring(0, 1) + label.Substring(1).ToLowerInvariant() : label;
            return set.Length > 0 && char.IsDigit(set[set.Length - 1]) ? $"{named} ({set})" : named;
        }
    }
}
