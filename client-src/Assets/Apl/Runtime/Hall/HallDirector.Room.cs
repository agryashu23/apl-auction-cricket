using System.Collections.Generic;
using System.Linq;
using Apl.Client.Online;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Hall
{
    /// <summary>
    /// The hall in a friend room (protocol v2, docs/ROOMS_PROTOCOL.md). No FAST, no SKIP. PASS
    /// takes only your own team out of this lot (your controls say PASSED; the lot carries on
    /// for everyone else until the server closes it). SIMULATE hands your team to a bot for the
    /// rest of the auction: your controls say BOT IS BIDDING FOR YOU with TAKE BACK CONTROL.
    /// Everyone sees the seat strip (passed / bot / offline badges); the host also gets the host
    /// panel. The bid timer is shown from the server's clock and stands still while paused.
    /// Toasts for refused actions and room news; RECONNECTING… while the socket is down.
    /// </summary>
    public sealed partial class HallDirector
    {
        /// <summary>The friend room this hall replays (null = solo). Set before Begin.</summary>
        public RoomClient? Room;

        private RoomSeatStrip? _seatStrip;
        private HostPanel? _hostPanel;
        private GameObject? _pausedBanner;
        private bool _wasAutopilot;
        private readonly List<RoomSeatStrip.Chip> _chips = new List<RoomSeatStrip.Chip>();

        /// <summary>The host panel (null for everyone else); test runs open it.</summary>
        public HostPanel? HostControls => _hostPanel;

        /// <summary>The seat strip (test runs log its chips).</summary>
        public RoomSeatStrip? SeatStrip => _seatStrip;

        private void BeginRoom()
        {
            var room = Room!;
            _seatStrip = RoomSeatStrip.Create(_hud.Root, _data);
            // Toasts: a narrow strip right under the seat strip (126-170 px down), clear of the
            // purse board, the bid table and the bid controls.
            RoomToast.Place(RoomToast.Slot.Hall);
            // The countdown is on in rooms (the host sets it; it runs from the server's clock), under the toast strip.
            _hud.TimerVisible = true;
            var timer = _hud.Root.Find("Timer") as RectTransform;
            if (timer != null) timer.anchoredPosition = new Vector2(0, -TimerY);
            // The HUD's top-right "II" opens this phone's menu only; in a room it never stops the
            // clock (the host's PAUSE ALL does), so it says MENU and there's one pause control.
            if (_hud.Root.Find("Pause") is Transform menu && menu.GetComponentInChildren<Text>() is Text menuText)
            {
                menuText.text = "MENU";
                menuText.fontSize = 24;
            }
            _hud.TakeBackPressed += () =>
            {
                Debug.Log("[APL room] take back control");
                room.TakeBack();
            };
            room.Error += OnRoomError;
            room.Notice += OnRoomNotice;
            room.Rebuilt += OnRoomRebuilt;
            Debug.Log($"[APL room] hall up: room {room.Code}, team {_me}, host {room.IsHost}, timer {room.BidTimerSec}s (no FAST / SKIP buttons)");
        }

        private void OnDestroy()
        {
            if (Room == null) return;
            Room.Error -= OnRoomError;
            Room.Notice -= OnRoomNotice;
            Room.Rebuilt -= OnRoomRebuilt;
            RoomToast.SetReconnecting(false);
            RoomToast.Place(RoomToast.Slot.Top);
        }

        /// <summary>The room's bid timer centre, px from the top: under the toast strip (RoomToast.Slot.Hall ends at 170).</summary>
        private const float TimerY = 224;

        private void OnRoomError(string code, string message)
        {
            if (code == "desync" || code == "slowDown") return;
            // The results are up: only a kick still matters.
            if (_resultsUp && code != "kicked") return;
            if (code == "staleAmount") message = StaleBidText();
            RoomToast.Show(message);
            if (code == "kicked")
            {
                // Removed by the host: back to the menu after the toast has been read.
                Invoke(nameof(BackToMenu), 3.5f);
            }
        }

        private void BackToMenu() => UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);

        private void OnRoomNotice(string kind, string text, Newtonsoft.Json.Linq.JObject m)
        {
            // Over the results only the host change is worth a toast.
            if (_resultsUp && kind != "youAreHost" && kind != "hostChanged") return;
            RoomToast.Show(text, bad: false);
        }

        /// <summary>
        /// Our bid lost a race (server staleAmount): say who got in first and at what price. BID
        /// already shows the next amount (it is recomputed from the replica every frame), so the
        /// toast tells you that you can go again straight away.
        /// </summary>
        private string StaleBidText()
        {
            var engine = _driver.Engine;
            var lot = engine.CurrentLot;
            if (lot?.LeaderId is string leader && leader != _me && lot.CurrentBid is int amount)
            {
                var who = _data.Franchises.FirstOrDefault(f => f.Id == leader)?.ShortName ?? leader;
                var again = engine.CheckBid(_me) == null ? $": BID {Money.Format(engine.NextBidAmount)} to go again" : "";
                Debug.Log($"[APL room] stale bid: {who} bid {amount} first (next {engine.NextBidAmount})");
                return $"{who} bid {Money.Format(amount)} first{again}.";
            }
            return RoomClient.Describe("staleAmount");
        }

        /// <summary>
        /// A fresh <c>start</c> (rejoin, host undo, everyone simulated): the engine was rebuilt from
        /// the log. Redraw from its state without animating the gap; finish if it is over.
        /// </summary>
        private void OnRoomRebuilt(string reason)
        {
            if (!_auctionStarted || _resultsUp) return;
            Debug.Log($"[APL room] hall redraw after start ({reason})");
            var engine = _driver.Engine;
            _hud.ShowRtmPrompt("", false);
            CancelInvoke(nameof(ClearCard));
            if (engine.IsFinished)
            {
                if (!_finishing) OnEvent(new AuctionFinished());
                return;
            }
            if (engine.CurrentLot is Lot lot)
            {
                _bidsThisLot = lot.CurrentBid != null ? 1 : 0;
                _hud.ShowLot(lot, SetLabel(_driver.Engine.CurrentSet ?? lot.Player.Set), engine.LotsRemaining);
                if (lot.LeaderId is string leader && lot.CurrentBid is int amount)
                {
                    _hud.ShowBid(amount, _data.Franchises.First(f => f.Id == leader), leader == _me);
                }
            }
            else
            {
                Cast.ClearCard();
            }
        }

        /// <summary>Per frame: seat strip, bot control, host panel, paused banner, reconnect pill.</summary>
        private void RoomTick()
        {
            var room = Room!;
            var engine = _driver.Engine;
            var lot = engine.CurrentLot;
            _chips.Clear();
            foreach (var s in room.Seats)
            {
                if (s.Team == null) continue;
                _chips.Add(new RoomSeatStrip.Chip
                {
                    TeamId = s.Team,
                    Name = s.Name,
                    State = s.State,
                    Passed = lot != null && (room.PassedThisLot.Contains(s.Team) || lot.Passed.Contains(s.Team)),
                    Me = s.Id == room.PlayerId,
                    Host = s.Host || (string?)room.Room?["host"] == s.Id,
                    Leading = lot?.LeaderId == s.Team,
                });
            }
            _seatStrip?.Show(_chips);

            // Our team on a bot (SIMULATE or after a drop-out): BOT IS BIDDING FOR YOU + TAKE BACK CONTROL.
            var autopilot = room.MyTeamOnAutopilot && _driver.Phase != LivePhase.Finished;
            if (autopilot != _wasAutopilot)
            {
                _wasAutopilot = autopilot;
                Debug.Log($"[APL room] my team {(autopilot ? "on autopilot: BOT IS BIDDING FOR YOU shown" : "back under my control")}");
            }
            // The autopilot answers our RTM offers: no prompt while it plays.
            if (autopilot && _driver.PendingRtm?.TeamId == _me) _hud.ShowRtmPrompt("", false);
            // The HUD hides BID / PASS / SIMULATE; our own panel takes exactly their place.
            _hud.ShowBotControl(autopilot);
            ShowRoomBotPanel(autopilot);

            // Host panel: only the host, created when host passes to us, gone when it leaves us.
            if (room.IsHost && _hostPanel == null && !_resultsUp)
            {
                _hostPanel = HostPanel.Create(_hud.Root, room, _data);
                Debug.Log("[APL room] host panel added");
            }

            var paused = room.Paused;
            if (paused && _pausedBanner == null)
            {
                // Over the (stopped) bid timer, under the toast strip: the toast saying who paused stays readable.
                var box = UiKit.Box(_hud.Root, "PausedBanner", new Vector2(0.5f, 1), new Vector2(0, -TimerY), new Vector2(560, 100), new Color(0.05f, 0.07f, 0.13f, 0.98f));
                YouMark.Add(box, "", 3, UiKit.Gold);
                UiKit.Label(box, "II  PAUSED BY THE HOST", 30, TextAnchor.MiddleCenter, UiKit.Gold);
                _pausedBanner = box.gameObject;
            }
            if (_pausedBanner != null && _pausedBanner.activeSelf != paused) _pausedBanner.SetActive(paused);
            RoomToast.SetReconnecting(room.Reconnecting);
        }

        /// <summary>BID / PASS in a room: PASSED once you pass this lot; nothing while a bot plays your team.</summary>
        private void RoomBidControls(AuctionEngine engine, Lot? lot)
        {
            var room = Room!;
            var passed = lot != null && (room.PassedThisLot.Contains(_me) || lot.Passed.Contains(_me));
            var bidding = _driver.Phase == LivePhase.Bidding && lot != null;
            var reason = bidding ? engine.CheckBid(_me) : null;
            string label;
            // PASSED shows once, on PASS; BID (greyed) says what comes next.
            if (room.Paused && lot != null) label = "PAUSED";
            else if (passed) label = "NEXT PLAYER…";
            else if (!bidding) label = "WAIT…";
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
                    BidRejection.Passed => "NEXT PLAYER…",
                    _ => "WAIT…",
                };
            }
            _hud.SetBidButton(label, bidding && reason == null && !passed && !room.Paused);
            _hud.SetPassLabel(passed ? "PASSED" : "PASS");
            // PASS while the lot is open (reveal counts once bidding opens); never as the leader.
            _hud.SetSkipEnabled(!passed && !room.Paused && lot != null && lot.LeaderId != _me &&
                                (_driver.Phase == LivePhase.Bidding || _driver.Phase == LivePhase.Revealing));
        }

        // ------------------------------------------------------- bot panel

        private RectTransform? _botPanel;
        private Vector2 _botPanelScreen;

        /// <summary>The room's BOT IS BIDDING FOR YOU panel (test runs log where it sits).</summary>
        public RectTransform? RoomBotPanel => _botPanel;

        /// <summary>
        /// BOT IS BIDDING FOR YOU + TAKE BACK CONTROL, laid exactly over the bid cluster (BID and
        /// PASS, which the HUD hides meanwhile) and trimmed so it never covers the bidder box, the
        /// current bid or the player card. Measured from the live HUD each time it opens, so it
        /// follows the cluster wherever the HUD puts it. The HUD's own bot panel stays invisible.
        /// </summary>
        private void ShowRoomBotPanel(bool on)
        {
            // The HUD's panel (made by ShowBotControl) is kept for its show/hide of BID / PASS but never seen.
            if (_hud.Root.Find("BotControl") is Transform hudPanel && hudPanel.GetComponent<CanvasGroup>() == null)
            {
                var group = hudPanel.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            var screen = new Vector2(Screen.width, Screen.height);
            if (on && (_botPanel == null || screen != _botPanelScreen))
            {
                _botPanelScreen = screen;
                BuildRoomBotPanel();
            }
            if (_botPanel != null && _botPanel.gameObject.activeSelf != on) _botPanel.gameObject.SetActive(on);
        }

        private void BuildRoomBotPanel()
        {
            if (_botPanel != null) Destroy(_botPanel.gameObject);
            var root = _hud.Root;
            var area = BidClusterArea(root);
            var go = new GameObject("RoomBotControl", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = area.center;
            rt.sizeDelta = area.size;
            go.GetComponent<Image>().color = UiKit.Gold;
            // The face, inset 3 px: a gold rim like the HUD's edged panels.
            var face = UiKit.Box(rt, "Face", new Vector2(0.5f, 0.5f), Vector2.zero, area.size - new Vector2(6, 6), UiKit.PanelSolid);
            var titleH = Mathf.Clamp(area.height * 0.32f, 28f, 44f);
            var title = UiKit.LabelAt(face, new Vector2(0.5f, 1), new Vector2(0, -titleH / 2 - 2), new Vector2(area.width - 24, titleH), "BOT IS BIDDING FOR YOU", 26, TextAnchor.MiddleCenter, UiKit.Gold);
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 14;
            title.resizeTextMaxSize = 26;
            var btnH = Mathf.Max(44f, area.height - titleH - 18);
            var take = UiKit.Btn(face, "TakeBack", new Vector2(0.5f, 0), new Vector2(0, btnH / 2 + 8), new Vector2(area.width - 30, btnH), "TAKE BACK CONTROL", UiKit.Accent, () =>
            {
                Debug.Log("[APL room] take back control");
                Room?.TakeBack();
            }, 30);
            var label = take.GetComponentInChildren<Text>();
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 16;
            label.resizeTextMaxSize = 30;
            _botPanel = rt;
            Debug.Log($"[APL room] bot panel over the bid cluster: centre {area.center.x:0},{area.center.y:0} size {area.width:0}x{area.height:0} (HUD-centre px)");
        }

        /// <summary>
        /// Where BID and PASS sit, in HUD-centre coordinates, minus anything else it would cover
        /// (bidder box, current bid, card, portrait): what's above it lowers the top, what's beside it
        /// moves the side in.
        /// </summary>
        private static Rect BidClusterArea(RectTransform root)
        {
            Rect? cluster = null;
            foreach (var name in new[] { "Bid", "Pass" })
            {
                if (LocalRect(root, name) is Rect r) cluster = cluster is Rect c ? Rect.MinMaxRect(Mathf.Min(c.xMin, r.xMin), Mathf.Min(c.yMin, r.yMin), Mathf.Max(c.xMax, r.xMax), Mathf.Max(c.yMax, r.yMax)) : r;
            }
            // No cluster found (HUD reworked): the old spot, bottom right of centre.
            var area = cluster ?? new Rect(330, -526, 460, 196);
            const float gap = 6;
            foreach (var name in new[] { "Bidder", "CurrentBid", "Card", "Portrait", "Balance" })
            {
                if (!(LocalRect(root, name) is Rect o) || !o.Overlaps(area)) continue;
                // Mostly above us: lower our top (keep at least 90 px for title + button).
                if (o.yMin - area.yMin >= 90) area.yMax = o.yMin - gap;
                // Beside us: pull that side in.
                else if (o.center.x < area.center.x) area.xMin = o.xMax + gap;
                else area.xMax = o.xMin - gap;
            }
            if (area.width < 200 || area.height < 90) area = cluster ?? area;
            return area;
        }

        /// <summary>A HUD element's rect in the HUD root's local space (works while it is hidden); edged panels include their edge.</summary>
        private static Rect? LocalRect(RectTransform root, string name)
        {
            if (!(root.Find(name) is RectTransform t)) return null;
            var corners = new Vector3[4];
            t.GetWorldCorners(corners);
            var a = root.InverseTransformPoint(corners[0]);
            var b = root.InverseTransformPoint(corners[2]);
            var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            // An edged panel's face sits 6 px up-left of its named edge.
            if (t.Find("Face") != null) r.xMin -= 6;
            if (t.Find("Face") != null) r.yMax += 6;
            return r;
        }

        /// <summary>PASS: out of this lot only. The lot goes on for everyone else; the server decides when it closes.</summary>
        private void RoomPass()
        {
            var room = Room!;
            var lot = _driver.Engine.CurrentLot;
            if (lot == null || room.PassedThisLot.Contains(_me)) return;
            Debug.Log($"[APL room] PASS lot {lot.Number} ({lot.Player.Id}): my controls show PASSED, the lot carries on for the others");
            room.PassLot();
        }

        /// <summary>SIMULATE in a room: a bot bids for your team for the rest of the auction (you can take it back).</summary>
        private void AskRoomSimulate()
        {
            var room = Room!;
            if (room.MyTeamOnAutopilot || _driver.Phase == LivePhase.Finished || ConfirmDialog.Open) return;
            var me = _driver.Engine.Teams[_me];
            var f = _driver.Engine.Format;
            ConfirmDialog.Show(
                "LET A BOT BID FOR YOU?",
                $"A bot will bid for {_data.Franchises.First(x => x.Id == _me).ShortName} for the rest of the auction: it fills your squad to {f.MinSquad} and your role gaps within your {Money.Format(me.Purse)} purse. The auction carries on for everyone. You can TAKE BACK CONTROL at any time.",
                "SIMULATE",
                "CANCEL",
                () =>
                {
                    Debug.Log("[APL room] SIMULATE confirmed: a bot bids for my team");
                    room.Simulate();
                });
        }
    }
}
