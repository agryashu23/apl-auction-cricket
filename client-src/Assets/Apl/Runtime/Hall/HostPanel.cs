using System.Linq;
using Apl.Client.Online;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Hall
{
    /// <summary>
    /// The room host's controls during the auction, on the right of the hall HUD under SIMULATE
    /// and pause: a HOST tab that opens a two-page card. CONTROLS: pause / resume, the bid timer,
    /// lock the room, force-close the lot (SOLD to the leader / UNSOLD), undo the last sale (when
    /// the server offers it) and end the auction. PLAYERS: everyone with their connection
    /// (online / bot / offline) and MAKE HOST / KICK. Only the host ever has one; it lives on the
    /// HUD canvas, so it hides with the HUD under the hub and pause pages, and it stops above the
    /// SOLD strip and the bid controls (bottom edge 580 px from the top of a 1080 screen).
    /// </summary>
    public sealed class HostPanel : MonoBehaviour
    {
        private RoomClient _client = null!;
        private Dataset _data = null!;
        private RectTransform _root = null!, _card = null!, _body = null!;
        private Text _tabText = null!;
        private bool _open;
        private string _page = "controls";
        private string _drawn = "";
        private float _drawAt;

        public const float Width = 440, Top = -170, Height = 410;

        /// <summary>The card is open (test runs read this).</summary>
        public bool IsOpen => _open;

        public static HostPanel Create(RectTransform hudRoot, RoomClient client, Dataset data)
        {
            var go = new GameObject("HostPanel", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(hudRoot, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var panel = go.AddComponent<HostPanel>();
            panel._client = client;
            panel._data = data;
            panel._root = rt;
            panel.Build();
            return panel;
        }

        private void Build()
        {
            var tab = UiKit.Btn(_root, "HostTab", new Vector2(1, 1), new Vector2(-20 - Width / 2, -134), new Vector2(Width, 54), "HOST CONTROLS ▼", UiKit.Gold, Toggle, 24);
            _tabText = tab.GetComponentInChildren<Text>();
            _tabText.color = UiKit.Ink;
            _card = UiKit.EdgedPanel(_root, "HostCard", new Vector2(1, 1), new Vector2(-20 - Width / 2, Top - Height / 2), new Vector2(Width, Height), UiKit.PanelSolid, UiKit.Gold);
            UiKit.Btn(_card, "HostPage_controls", new Vector2(0.5f, 1), new Vector2(-Width / 4, -30), new Vector2(Width / 2 - 14, 46), "CONTROLS", UiKit.Muted, () => Page("controls"), 22);
            UiKit.Btn(_card, "HostPage_players", new Vector2(0.5f, 1), new Vector2(Width / 4, -30), new Vector2(Width / 2 - 14, 46), "PLAYERS", UiKit.Muted, () => Page("players"), 22);
            var body = new GameObject("Body", typeof(RectTransform));
            _body = (RectTransform)body.transform;
            _body.SetParent(_card, false);
            _body.anchorMin = Vector2.zero;
            _body.anchorMax = Vector2.one;
            _body.offsetMin = new Vector2(10, 10);
            _body.offsetMax = new Vector2(-10, -60);
            _card.parent.gameObject.SetActive(false);
        }

        public void Toggle() => SetOpen(!_open);

        public void SetOpen(bool open)
        {
            _open = open;
            _tabText.text = open ? "HOST CONTROLS ▲" : "HOST CONTROLS ▼";
            _card.parent.gameObject.SetActive(open);
            _drawn = "";
            Debug.Log($"[APL room] host panel {(open ? "open" : "closed")} ({_page})");
        }

        public void Page(string page)
        {
            _page = page;
            _drawn = "";
        }

        private void Update()
        {
            // Not host any more (transferred): the panel goes.
            if (!_client.IsHost)
            {
                Destroy(gameObject);
                return;
            }
            if (!_open || Time.unscaledTime < _drawAt) return;
            _drawAt = Time.unscaledTime + 0.25f;
            var key = _page + "|" + _client.Paused + "|" + _client.Locked + "|" + _client.BidTimerSec + "|" + _client.Connected + "|" + _client.LotOpen + "|" + _client.CanUndo + "|" +
                      string.Join(",", _client.Seats.Select(s => $"{s.Id}:{s.Team}:{s.State}:{s.Host}"));
            if (key == _drawn) return;
            _drawn = key;
            Draw();
        }

        private void Draw()
        {
            UiKit.Clear(_body);
            foreach (var p in new[] { "controls", "players" })
            {
                var b = _card.Find("HostPage_" + p);
                if (b == null) continue;
                b.GetComponent<Image>().color = p == _page ? UiKit.Gold : UiKit.Muted;
                b.GetComponentInChildren<Text>().color = p == _page ? UiKit.Ink : Color.white;
            }
            if (_page == "players") Players();
            else Controls();
        }

        private const float BtnH = 58, Gap = 10;
        private float Y(int row) => -BtnH / 2 - row * (BtnH + Gap);

        private Button Half(string name, int row, bool left, string label, Color colour, System.Action act, bool enabled = true)
        {
            var w = (Width - 20 - Gap) / 2;
            var b = UiKit.Btn(_body, name, new Vector2(0.5f, 1), new Vector2(left ? -(w + Gap) / 2 : (w + Gap) / 2, Y(row)), new Vector2(w, BtnH), label, colour, act, 22);
            b.interactable = enabled;
            return b;
        }

        private void Controls()
        {
            var paused = _client.Paused;
            // "… ALL": this stops the clock for the whole room (the HUD's top-right button is only this phone's MENU).
            Half("HostPause", 0, true, paused ? "► RESUME ALL" : "II PAUSE ALL", paused ? UiKit.Good : UiKit.Muted, () =>
            {
                Debug.Log($"[APL room] host {(paused ? "resume" : "pause")}");
                _client.HostPause(!paused);
            });
            var locked = _client.Locked;
            Half("HostLock", 0, false, locked ? "UNLOCK ROOM" : "LOCK ROOM", locked ? UiKit.Gold : UiKit.Muted, () =>
            {
                Debug.Log($"[APL room] host {(locked ? "unlock" : "lock")} room");
                _client.HostLock(!locked);
            });
            if (locked) _body.Find("HostLock").GetComponentInChildren<Text>().color = UiKit.Ink;

            // Bid timer: 10 / 15 / 20 s (applies from the next bid).
            var timerRow = UiKit.Box(_body, "HostTimer", new Vector2(0.5f, 1), new Vector2(0, Y(1)), new Vector2(Width - 20, BtnH), new Color(0.09f, 0.11f, 0.18f, 0.95f));
            UiKit.LabelAt(timerRow, new Vector2(0, 0.5f), new Vector2(75, 0), new Vector2(140, 40), "BID TIMER", 20, TextAnchor.MiddleLeft, UiKit.Dim);
            var sec = _client.BidTimerSec;
            var choices = RoomClient.TimerChoices;
            for (var i = 0; i < choices.Length; i++)
            {
                var s = choices[i];
                var on = s == sec;
                var b = UiKit.Btn(timerRow, "HostTimer_" + s, new Vector2(1, 0.5f), new Vector2(-48 - (choices.Length - 1 - i) * 82, 0), new Vector2(74, 44), s + "s", on ? UiKit.Gold : UiKit.Muted, () => SetTimer(s), 22);
                if (on) b.GetComponentInChildren<Text>().color = UiKit.Ink;
            }

            var lot = _client.LotOpen;
            Half("HostSold", 2, true, "SOLD NOW", new Color(0.16f, 0.45f, 0.26f, 0.97f), () => Confirm("SELL THIS PLAYER NOW?", "The lot closes at once: SOLD to the highest bidder (UNSOLD if nobody has bid).", "SELL", () => _client.HostForceClose(true)), lot);
            Half("HostUnsold", 2, false, "UNSOLD", new Color(0.4f, 0.16f, 0.16f, 0.97f), () => Confirm("CLOSE AS UNSOLD?", "The player goes unsold, whatever the bids.", "UNSOLD", () => _client.HostForceClose(false)), lot);
            var undo = _client.SupportsUndo;
            if (undo)
            {
                Half("HostUndo", 3, true, "UNDO LAST SALE", UiKit.Muted, () => Confirm("UNDO THE LAST SALE?", "The last player goes back on the block and the money goes back to the buyer.", "UNDO", () => _client.HostUndo()), _client.CanUndo);
            }
            var end = UiKit.Btn(_body, "HostEnd", new Vector2(0.5f, 1), new Vector2(undo ? (Width - 20 - Gap) / 4 + Gap / 2 : 0, Y(3)), new Vector2(undo ? (Width - 20 - Gap) / 2 : Width - 20, BtnH),
                "END AUCTION", new Color(0.62f, 0.14f, 0.14f, 0.98f), () => Confirm("END THE AUCTION NOW?", EndDetail, "END IT", () => _client.HostEndAuction()), 22);
            end.interactable = _client.Phase == "auction";
            UiKit.LabelAt(_body, new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(Width - 20, 30),
                paused ? "PAUSED · the clock is stopped for everyone" : $"ROOM {_client.Code} · {(locked ? "locked" : "open to join")}", 18, TextAnchor.MiddleCenter, paused ? UiKit.Gold : UiKit.Dim, bold: false);
        }

        /// <summary>
        /// What END AUCTION really does (server RoomAuction.End): every team, yours and your friends'
        /// included, goes to a bot, and the bots bid out every remaining lot at once.
        /// </summary>
        public const string EndDetail = "Nobody bids by hand any more: bots take over every team, yours included, and finish the rest of the auction at once. Then the results come up for everyone.";

        private void SetTimer(int seconds)
        {
            seconds = Mathf.Clamp(seconds, RoomClient.MinBidTimer, RoomClient.MaxBidTimer);
            Debug.Log($"[APL room] host bid timer {seconds}s");
            _client.HostBidTimer(seconds);
        }

        private void Confirm(string question, string detail, string ok, System.Action act)
        {
            ConfirmDialog.Show(question, detail, ok, "CANCEL", () =>
            {
                Debug.Log($"[APL room] host: {question.ToLowerInvariant()} -> {ok}");
                act();
            });
        }

        private void Players()
        {
            var seats = _client.Seats.ToList();
            var rowH = Mathf.Min(48f, (Height - 80) / Mathf.Max(1, seats.Count));
            for (var i = 0; i < seats.Count; i++)
            {
                var s = seats[i];
                var me = s.Id == _client.PlayerId;
                var row = UiKit.Box(_body, "HostSeat_" + i, new Vector2(0.5f, 1), new Vector2(0, -rowH / 2 - i * rowH), new Vector2(Width - 20, rowH - 4),
                    me ? new Color(0.95f, 0.76f, 0.31f, 0.2f) : new Color(0.09f, 0.11f, 0.18f, 0.95f));
                if (s.Team != null) UiKit.Crest(row, s.Team, new Vector2(0, 0.5f), new Vector2(22, 0), rowH - 12);
                var name = UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(46 + 75, 0), new Vector2(150, rowH - 8), s.Name + (s.Host ? " (HOST)" : ""), 18, TextAnchor.MiddleLeft);
                name.resizeTextForBestFit = true;
                name.resizeTextMinSize = 11;
                name.resizeTextMaxSize = 18;
                var (state, colour) = StateLabel(s);
                var chip = UiKit.Box(row, "State", new Vector2(0, 0.5f), new Vector2(240, 0), new Vector2(66, 24), colour);
                UiKit.Label(chip, state, 13, TextAnchor.MiddleCenter, UiKit.Ink);
                if (me) continue;
                var id = s.Id;
                var who = s.Name;
                UiKit.Btn(row, "MakeHost_" + i, new Vector2(1, 0.5f), new Vector2(-120, 0), new Vector2(66, rowH - 12), "HOST", UiKit.Muted,
                    () => Confirm($"MAKE {who.ToUpperInvariant()} HOST?", "You lose the host controls; they get them.", "MAKE HOST", () => _client.HostTransfer(id)), 15);
                UiKit.Btn(row, "Kick_" + i, new Vector2(1, 0.5f), new Vector2(-40, 0), new Vector2(66, rowH - 12), "KICK", new Color(0.45f, 0.12f, 0.12f, 0.96f),
                    () => Confirm($"KICK {who.ToUpperInvariant()}?", "They leave the room; a bot plays their team from here.", "KICK", () => _client.Kick(id)), 15);
            }
        }

        public static (string, Color) StateLabel(RoomSeat s) => s.State switch
        {
            SeatState.Disconnected => ("OFFLINE", UiKit.Bad),
            SeatState.Bot => ("BOT", new Color(0.96f, 0.62f, 0.12f)),
            _ => ("ONLINE", UiKit.Good),
        };
    }
}
