using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Hall
{
    /// <summary>
    /// Mid-auction overview: your squad, any franchise's squad, every sale,
    /// players still to come, and the unsold. Built fresh each time it opens
    /// (or a tab changes), from the engine's current state.
    /// </summary>
    public sealed class AuctionHub : MonoBehaviour
    {
        public event Action? Closed;

        private AuctionEngine _engine = null!;
        private Dataset _data = null!;
        private string _me = "";
        private Text _status = null!;
        private RectTransform _root = null!, _body = null!;
        private readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();
        private string _tab = "mine";
        private string _team = "";

        private static readonly Color RowA = new Color(0.09f, 0.11f, 0.18f, 0.96f);
        private static readonly Color RowB = new Color(0.06f, 0.08f, 0.14f, 0.96f);

        public static AuctionHub Create(Dataset data)
        {
            var canvas = UiKit.Canvas("AuctionHub", 25);
            var hub = canvas.gameObject.AddComponent<AuctionHub>();
            hub._data = data;
            hub._root = UiKit.Stretch((RectTransform)canvas.transform, "Dim", new Color(0.02f, 0.03f, 0.07f, 1f));
            hub.Build();
            canvas.gameObject.SetActive(false);
            return hub;
        }

        public bool IsOpen => gameObject.activeSelf;

        /// <summary><paramref name="paused"/>: solo auctions stop while the hub is open.</summary>
        public void Open(AuctionEngine engine, string me, bool paused)
        {
            _engine = engine;
            _me = me;
            _status.text = paused ? "AUCTION PAUSED" : "LIVE · THE ROOM KEEPS BIDDING";
            _status.color = paused ? UiKit.Dim : UiKit.Bad;
            if (_team.Length == 0) _team = me;
            gameObject.SetActive(true);
            Show(_tab);
        }

        public void Close()
        {
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private Franchise Team(string id) => _data.Franchises.First(f => f.Id == id);

        private void Build()
        {
            UiKit.LabelAt(_root, new Vector2(0.5f, 1), new Vector2(0, -52), new Vector2(1200, 60), "AUCTION HUB", 48, TextAnchor.MiddleCenter, UiKit.Gold);
            var tabs = new[] { ("mine", "MY SQUAD"), ("teams", "TEAMS"), ("sold", "SOLD"), ("remaining", "REMAINING"), ("unsold", "UNSOLD") };
            for (var i = 0; i < tabs.Length; i++)
            {
                var (id, label) = tabs[i];
                _tabs[id] = UiKit.Btn(_root, "HubTab_" + id, new Vector2(0.5f, 1), new Vector2((i - 2) * 330, -132), new Vector2(310, 68), label, UiKit.Muted, () => Show(id), 26);
            }
            var body = new GameObject("Body", typeof(RectTransform));
            _body = (RectTransform)body.transform;
            _body.SetParent(_root, false);
            _body.anchorMin = new Vector2(0, 0);
            _body.anchorMax = new Vector2(1, 1);
            _body.offsetMin = new Vector2(110, 140);
            _body.offsetMax = new Vector2(-110, -180);
            _status = UiKit.LabelAt(_root, new Vector2(1, 1), new Vector2(-230, -52), new Vector2(400, 40), "", 24, TextAnchor.MiddleRight, UiKit.Dim);
            UiKit.Btn(_root, "HubClose", new Vector2(0.5f, 0), new Vector2(0, 66), new Vector2(460, 84), "BACK TO AUCTION ►", UiKit.Accent, Close, 30);
        }

        private void Show(string tab)
        {
            _tab = tab;
            foreach (var kv in _tabs)
            {
                var on = kv.Key == tab;
                kv.Value.GetComponent<Image>().color = on ? UiKit.Gold : UiKit.Muted;
                kv.Value.GetComponentInChildren<Text>().color = on ? UiKit.Ink : Color.white;
            }
            UiKit.Clear(_body);
            switch (tab)
            {
                case "mine": Squad(_me, top: 0); break;
                case "teams": Teams(); break;
                case "sold": Sold(); break;
                case "remaining": Remaining(); break;
                default: Unsold(); break;
            }
        }

        // ---------------------------------------------------------------- tabs

        private void Squad(string teamId, float top)
        {
            var t = _engine.Teams[teamId];
            var f = _engine.Format;
            var need = Math.Max(0, f.MinSquad - t.Size);
            var summary = $"{Team(teamId).Name.ToUpperInvariant()}   ·   PURSE LEFT {Money.Format(t.Purse)}   ·   {t.Size}/{f.MaxSquad} PLAYERS" +
                          $"   ·   OVERSEAS {t.OverseasCount}/{f.MaxOverseas}   ·   RTM {t.RtmCards}" + (need > 0 ? $"   ·   NEEDS {need} MORE" : "");
            UiKit.LabelAt(_body, new Vector2(0.5f, 1), new Vector2(0, -top - 22), new Vector2(1700, 40), summary + (teamId == _me ? "   ·   YOU" : ""), 26, TextAnchor.MiddleCenter, teamId == _me ? YouMark.Lime : Color.white);
            var list = List(top + 52);
            Header(list, ("PLAYER", 560), ("ROLE", 260), ("ORIGIN", 260), ("HOW", 260), ("PRICE", 300));
            var picks = t.Squad.OrderByDescending(p => p.Price).ToList();
            if (picks.Count == 0) Empty(list, "No players yet.");
            for (var i = 0; i < picks.Count; i++)
            {
                var p = picks[i];
                var how = p.Kind switch
                {
                    PurchaseKind.Retained => "RETAINED",
                    PurchaseKind.Rtm => "RTM",
                    PurchaseKind.AutoFill => "AUTO-FILL",
                    _ => "AUCTION",
                };
                Row(list, i, p.Player, (Origin(p.Player), 260), (how, 260), (Money.Format(p.Price), 300));
            }
        }

        private void Teams()
        {
            // Crest picker across the top, then the chosen team's squad.
            var n = _data.Franchises.Count;
            for (var i = 0; i < n; i++)
            {
                var f = _data.Franchises[i];
                var on = f.Id == _team;
                var x = (i - (n - 1) / 2f) * 150;
                var colour = UiKit.WithAlpha(UiKit.ParseHex(f.PrimaryColor), 0.85f);
                var tile = UiKit.Box(_body, "Pick_" + f.Id, new Vector2(0.5f, 1), new Vector2(x, -52), new Vector2(136, 96), colour);
                var id = f.Id;
                tile.gameObject.AddComponent<Button>().onClick.AddListener(() => { _team = id; Show("teams"); });
                UiKit.Crest(tile, f.Id, new Vector2(0.5f, 0.5f), new Vector2(0, 10), 62);
                UiKit.LabelAt(tile, new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(130, 24), f.ShortName, 20, TextAnchor.MiddleCenter, colour.grayscale > 0.55f ? UiKit.Ink : Color.white);
                // Yours always wears the YOU mark; the one being shown gets a plain white frame.
                if (f.Id == _me) YouMark.Add(tile, "YOU", on ? 7 : 4, tagSize: 16);
                else if (on) YouMark.Add(tile, "", 5, Color.white);
            }
            Squad(_team, top: 112);
        }

        private void Sold()
        {
            var sold = _engine.Results.Where(r => r.Sold).Reverse().ToList();
            UiKit.LabelAt(_body, new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(1700, 40),
                $"{sold.Count} SOLD · {Money.Format(sold.Sum(r => r.Price ?? 0))} SPENT · LATEST FIRST", 26, TextAnchor.MiddleCenter);
            var list = List(52);
            Header(list, ("PLAYER", 560), ("ROLE", 260), ("LOT", 160), ("SOLD TO", 360), ("PRICE", 300));
            if (sold.Count == 0) Empty(list, "Nothing sold yet.");
            for (var i = 0; i < sold.Count; i++)
            {
                var r = sold[i];
                var row = Row(list, i, r.Player, ("#" + r.LotNumber, 160), ("", 360), (Money.Format(r.Price!.Value) + (r.ViaRtm ? "  RTM" : ""), 300));
                // Buyer: crest + short name in the SOLD TO column.
                var x = 560 + 260 + 160;
                UiKit.Crest(row, r.TeamId!, new Vector2(0, 0.5f), new Vector2(x + 34, 0), 40);
                UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(x + 200, 0), new Vector2(280, 40), Team(r.TeamId!).ShortName + (r.TeamId == _me ? " · YOU" : ""), 24, TextAnchor.MiddleLeft, r.TeamId == _me ? YouMark.Lime : Color.white);
            }
        }

        /// <summary>Sets the viewer opened or closed by tapping their header (by set code); others follow the default.</summary>
        private readonly Dictionary<string, bool> _setOpen = new Dictionary<string, bool>();

        /// <summary>
        /// The auction list in the order it will run, IPL style: the CURRENT set first (open,
        /// highlighted; the player on the block, then the rest of the set in the order they come
        /// up), then the upcoming sets in order (the next one open, later ones folded: tap a header
        /// to open it), then the unsold pool the accelerated round draws from.
        /// </summary>
        private void Remaining()
        {
            var lot = _engine.CurrentLot;
            var upcoming = _engine.Upcoming;
            var currentSet = _engine.CurrentSet;
            UiKit.LabelAt(_body, new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(1700, 40),
                $"{upcoming.Count} STILL TO COME" + (_engine.InAcceleratedRound ? " · ACCELERATED ROUND" : "") + " · IN AUCTION ORDER · TAP A SET TO OPEN IT", 26, TextAnchor.MiddleCenter);
            var list = List(52);

            // Consecutive runs of one set in the queue (IPL sets run once each; a room's classic
            // order may rotate a role set more than once, so each run is its own block).
            var blocks = new List<(string Code, List<Player> Players)>();
            foreach (var p in upcoming)
            {
                var code = _engine.InAcceleratedRound ? _engine.AcceleratedSet : _engine.SetOf(p);
                if (blocks.Count == 0 || blocks[blocks.Count - 1].Code != code) blocks.Add((code, new List<Player>()));
                blocks[blocks.Count - 1].Players.Add(p);
            }

            Debug.Log($"[APL sets] hub REMAINING: now {currentSet ?? "-"}{(lot != null ? $" (on the block {lot.Player.Id})" : "")}; then " +
                      string.Join(" ", blocks.Select(b => $"{b.Code}({b.Players.Count})")) + $"; unsold pool {_engine.UnsoldPool.Count}");
            var i = 0;
            var pos = 0;
            var shown = 0;
            // The current set: the man on the block, then the rest of his set.
            if (lot != null || (currentSet != null && blocks.Count > 0 && blocks[0].Code == currentSet))
            {
                var code = currentSet ?? (blocks.Count > 0 ? blocks[0].Code : "");
                var rest = blocks.Count > 0 && blocks[0].Code == code ? blocks[0].Players : new List<Player>();
                if (blocks.Count > 0 && blocks[0].Code == code) blocks.RemoveAt(0);
                var count = rest.Count + (lot != null ? 1 : 0);
                Section(list, $"NOW  ·  {SetTitle(code)}  ·  {count} PLAYER{(count == 1 ? "" : "S")}", code, current: true, open: true);
                if (lot != null)
                {
                    var row = Row(list, i++, lot.Player, (Origin(lot.Player), 260), ("ON THE BLOCK", 260), (Money.Format(lot.Player.BasePrice), 300));
                    row.GetComponent<Image>().color = new Color(0.95f, 0.76f, 0.31f, 0.34f);
                }
                foreach (var p in rest) Row(list, i++, p, (Origin(p), 260), (Position(++pos), 260), (Money.Format(p.BasePrice), 300));
                shown++;
            }
            foreach (var (code, players) in blocks)
            {
                // The next set opens by default; later ones fold to their header.
                var open = _setOpen.TryGetValue(code, out var o) ? o : shown <= 1;
                Section(list, $"{(shown == 1 ? "NEXT  ·  " : "")}{SetTitle(code)}  ·  {players.Count}", code, current: false, open: open);
                foreach (var p in players)
                {
                    pos++;
                    if (open) Row(list, i++, p, (Origin(p), 260), (Position(pos), 260), (Money.Format(p.BasePrice), 300));
                }
                shown++;
            }

            // The unsold so far: some come back in the accelerated round.
            var pool = _engine.InAcceleratedRound ? new List<Player>() : _engine.UnsoldPool.ToList();
            if (pool.Count > 0)
            {
                const string code = "unsold-pool";
                var open = _setOpen.TryGetValue(code, out var o) && o;
                Section(list, $"ACCELERATED ROUND POOL  ·  UNSOLD SO FAR, MAY RETURN  ·  {pool.Count}", code, current: false, open: open);
                if (open)
                {
                    foreach (var p in pool.OrderByDescending(p => p.Impact).ThenBy(p => p.Name))
                    {
                        Row(list, i++, p, (Origin(p), 260), ("UNSOLD", 260), (Money.Format(p.BasePrice), 300));
                    }
                }
            }
            if (i == 0 && shown == 0) Empty(list, "No players left in this round.");
        }

        private static string Position(int n) => n == 1 ? "NEXT UP" : $"IN {n} LOTS";

        /// <summary>"BA1 · CAPPED BATTERS 1" for an IPL set, "BATTERS" for a room's.</summary>
        private static string SetTitle(string code)
        {
            var label = AuctionSets.Label(code);
            return code.Length > 0 && char.IsDigit(code[code.Length - 1]) ? $"{code}  ·  {label}" : label;
        }

        /// <summary>A set header: tap to open / fold it (the current set stays open).</summary>
        private void Section(RectTransform list, string title, string code, bool current, bool open)
        {
            var row = UiKit.ListRow(list, current ? 54 : 46, current ? new Color(0.95f, 0.76f, 0.31f, 0.42f) : new Color(0.95f, 0.76f, 0.31f, 0.16f));
            UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(24 + 600, 0), new Vector2(1200, 40), (current ? "" : open ? "▼  " : "►  ") + title, current ? 28 : 24, TextAnchor.MiddleLeft, current ? Color.white : UiKit.Gold);
            if (current) return;
            row.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                _setOpen[code] = !open;
                Show("remaining");
            });
        }

        private void Unsold()
        {
            var unsold = _engine.Results.Where(r => !r.Sold).ToList();
            UiKit.LabelAt(_body, new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(1700, 40),
                $"{unsold.Count} UNSOLD" + (_engine.InAcceleratedRound ? "" : " · SOME MAY RETURN IN THE ACCELERATED ROUND"), 26, TextAnchor.MiddleCenter);
            var list = List(52);
            Header(list, ("PLAYER", 560), ("ROLE", 260), ("ORIGIN", 260), ("LOT", 260), ("BASE", 300));
            if (unsold.Count == 0) Empty(list, "No unsold players yet.");
            for (var i = 0; i < unsold.Count; i++)
            {
                var r = unsold[i];
                Row(list, i, r.Player, (Origin(r.Player), 260), ("#" + r.LotNumber, 260), (Money.Format(r.Player.BasePrice), 300));
            }
        }

        // ------------------------------------------------------------- pieces

        private RectTransform List(float top)
        {
            var area = _body.rect;
            var height = Mathf.Max(200, (area.height > 0 ? area.height : 760) - top);
            return UiKit.ScrollList(_body, new Vector2(0.5f, 1), new Vector2(0, -top - height / 2), new Vector2(1700, height));
        }

        private static void Header(RectTransform list, params (string text, float width)[] cols)
        {
            var row = UiKit.ListRow(list, 40, new Color(0, 0, 0, 0));
            var x = 0f;
            foreach (var (text, width) in cols)
            {
                UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(x + 24 + (width - 24) / 2, 0), new Vector2(width - 24, 34), text, 20, TextAnchor.MiddleLeft, UiKit.Dim);
                x += width;
            }
        }

        private static void Empty(RectTransform list, string text)
        {
            var row = UiKit.ListRow(list, 80, new Color(0, 0, 0, 0));
            UiKit.Label(row, text, 26, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
        }

        /// <summary>Portrait + name + role, then the extra columns.</summary>
        private RectTransform Row(RectTransform list, int i, Player p, params (string text, float width)[] extra)
        {
            var row = UiKit.ListRow(list, 56, i % 2 == 0 ? RowA : RowB);
            var face = new GameObject("Face", typeof(RectTransform), typeof(RawImage));
            var frt = (RectTransform)face.transform;
            frt.SetParent(row, false);
            frt.anchorMin = frt.anchorMax = new Vector2(0, 0.5f);
            frt.anchoredPosition = new Vector2(34, 0);
            frt.sizeDelta = new Vector2(46, 46);
            var img = face.GetComponent<RawImage>();
            img.texture = Portraits.For(p, 96);
            img.raycastTarget = false;
            UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(70 + 240, 0), new Vector2(480, 44), p.Name + (p.Capped ? "" : "  (U)"), 26, TextAnchor.MiddleLeft);
            UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(560 + 130, 0), new Vector2(236, 44), HallHud.RoleLabel(p), 22, TextAnchor.MiddleLeft, UiKit.Dim);
            var x = 560f + 260f;
            foreach (var (text, width) in extra)
            {
                if (text.Length > 0)
                {
                    UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(x + 24 + (width - 24) / 2, 0), new Vector2(width - 24, 44), text, 24, TextAnchor.MiddleLeft);
                }
                x += width;
            }
            return row;
        }

        private static string Origin(Player p) => p.Overseas ? p.Country.ToUpperInvariant() : "INDIA";

        private static string SetName(string set) => set switch
        {
            "marquee" => "MARQUEE",
            "batters" => "BATTERS",
            "bowlers" => "BOWLERS",
            "allRounders" => "ALL-ROUNDERS",
            "wicketKeepers" => "KEEPERS",
            "uncapped" => "UNCAPPED",
            _ => set.ToUpperInvariant(),
        };
    }
}
