using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Match;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// PLAYING XI (owner v26 #7), before every match the user plays: the auto-picked XI
    /// pre-filled on the left, the rest of the squad (the bench) on the right. Tap a player
    /// in the XI and one on the bench (either order) to swap them; the rules the auto-pick
    /// keeps are kept here too (the engine's <see cref="PlayingXi"/>: overseas cap, a keeper,
    /// five bowling options) and a swap that breaks one says why. AUTO XI puts the auto-pick
    /// back; CONFIRM ► plays it. Tournament (and online) matches give 30 s: at 0 the XI as
    /// it stands is confirmed.
    /// <para>
    /// Entry points: <see cref="OpenQuickMatch"/> (Quick Match: last season's squad, no clock)
    /// and <see cref="Open"/> (any squad, optional clock). Both store the confirmed XI in
    /// <see cref="XiChoice"/>, which the ground's MatchDirector plays for the user's side.
    /// </para>
    /// </summary>
    public sealed class PlayingXiScreen : MonoBehaviour
    {
        /// <summary>Tournament / online: the confirm clock (owner v26 #7).</summary>
        public const float TimedSeconds = PickClock.PlayingXi;

        private const string CanvasName = "PlayingXi";
        private const float RowH = 54;

        private Dataset _data = null!;
        private Franchise _team = null!;
        private Franchise? _opp;
        private List<Player> _squad = new List<Player>(), _auto = new List<Player>(), _xi = new List<Player>();
        private int _maxOverseas;
        private float _seconds, _left;
        private bool _done;
        private Action<List<Player>> _confirmed = _ => { };
        private Action? _back;

        private RectTransform _root = null!, _xiBox = null!, _benchList = null!;
        private Text _status = null!, _message = null!, _ringCaption = null!;
        private CountdownRing? _ring;
        private string? _selXi, _selBench;
        private float _messageUntil;

        /// <summary>The XI as it stands (tests and the capture run read it).</summary>
        public IReadOnlyList<Player> CurrentXI => _xi;
        public IReadOnlyList<Player> Bench => _squad.Where(p => _xi.All(x => x.Id != p.Id)).OrderByDescending(PlayerCardKit.Rating).ToList();
        public float SecondsLeft => _left;

        // ------------------------------------------------------------ entry points

        /// <summary>
        /// Quick Match: <paramref name="myTeam"/>'s last-season squad v <paramref name="opponent"/>, no clock.
        /// CONFIRM stores the XI (<see cref="XiChoice"/>) and calls <paramref name="onConfirmed"/> (load the
        /// ground there); BACK (shown when <paramref name="onBack"/> is given) closes the screen and calls it.
        /// </summary>
        public static PlayingXiScreen OpenQuickMatch(Dataset data, string myTeam, string opponent, Action onConfirmed, Action? onBack = null)
        {
            var squad = data.Players.Where(p => p.PreviousTeam == myTeam).ToList();
            var auto = QuickMatch.LastSeasonXI(data, myTeam).XI;
            return Open(data, myTeam, opponent, squad, auto, 0f, _ => onConfirmed(), onBack);
        }

        /// <summary>
        /// The screen over whatever is open: <paramref name="squad"/> to choose from, <paramref name="autoXI"/>
        /// pre-filled (null: the engine's auto-pick), <paramref name="seconds"/> &gt; 0 for a confirm clock that
        /// confirms the current XI at 0. The confirmed XI goes into <see cref="XiChoice"/> before
        /// <paramref name="confirmed"/> is called.
        /// </summary>
        public static PlayingXiScreen Open(Dataset data, string myTeam, string? opponent, IReadOnlyList<Player> squad, IReadOnlyList<Player>? autoXI,
            float seconds, Action<List<Player>> confirmed, Action? onBack = null)
        {
            var old = GameObject.Find(CanvasName);
            if (old != null) Destroy(old);
            var canvas = UiKit.Canvas(CanvasName, 60);
            var s = canvas.gameObject.AddComponent<PlayingXiScreen>();
            s._data = data;
            s._team = data.Franchises.First(f => f.Id == myTeam);
            s._opp = opponent != null ? data.Franchises.FirstOrDefault(f => f.Id == opponent) : null;
            s._squad = squad.ToList();
            s._maxOverseas = data.Rules.MaxOverseasInXI;
            s._auto = (autoXI ?? PlayingXi.Auto(s._squad, s._maxOverseas).XI).ToList();
            // The last XI confirmed for this side, when it still fits this squad (the auto-pick otherwise).
            var kept = XiChoice.SheetFor(data, myTeam, s._squad);
            s._xi = (kept?.XI ?? s._auto).ToList();
            s._seconds = s._left = seconds;
            s._confirmed = confirmed;
            s._back = onBack;
            s._root = (RectTransform)canvas.transform;
            s.Build();
            Debug.Log($"[APL xi] PLAYING XI open: {myTeam} v {opponent}, squad {s._squad.Count}, clock {(seconds > 0 ? seconds.ToString("0") + " s" : "none")}");
            return s;
        }

        // ------------------------------------------------------------------ page

        private void Build()
        {
            // Opaque (PM v26 check: the home page showed through at 0.94).
            var page = PanelKit.Backdrop(_root, 1f);
            page.gameObject.AddComponent<Button>(); // swallow taps on whatever is behind
            var header = PanelKit.Header(page, "PLAYING XI", "Tap a player in your XI, then one on the bench, to swap them", _team, _opp);

            // Left: the XI. Right: the bench.
            var bright = PlayerCardKit.Bright(UiKit.ParseHex(_team.PrimaryColor));
            _xiBox = PanelKit.GlassPanel(page, "XiPanel", new Vector2(0.5f, 0.5f), new Vector2(-400, 43), new Vector2(1020, 710));
            PlayerCardKit.Text(_xiBox, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -46), new Vector2(-24, -6), "YOUR XI · BATTING ORDER", 26, TextAnchor.MiddleLeft, bright, condensed: true);
            var bench = PanelKit.GlassPanel(page, "BenchPanel", new Vector2(0.5f, 0.5f), new Vector2(540, 43), new Vector2(780, 710));
            PlayerCardKit.Text(bench, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -46), new Vector2(-24, -6), "BENCH · tap to bring in", 26, TextAnchor.MiddleLeft, UiKit.Dim, condensed: true);
            _benchList = UiKit.ScrollList(bench, new Vector2(0.5f, 0.5f), new Vector2(0, -26), new Vector2(750, 646));

            // Bottom bar, in two bands so nothing overlaps at 16:9 or ~2.2:1: the swap note and the rules line in
            // their own strip between the panels (bottom at y 228) and the buttons (y 20..140); the clock bottom left,
            // AUTO XI and CONFIRM bottom right.
            _message = PlayerCardKit.Text(page, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-760, 188), new Vector2(760, 224), "", 26, TextAnchor.MiddleCenter, UiKit.Bad);
            _status = PlayerCardKit.Text(page, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-760, 148), new Vector2(760, 184), "", 24, TextAnchor.MiddleCenter, Color.white, bold: false);
            foreach (var line in new[] { _message, _status })
            {
                line.horizontalOverflow = HorizontalWrapMode.Wrap;
                line.verticalOverflow = VerticalWrapMode.Overflow;
                line.resizeTextForBestFit = true;
                line.resizeTextMaxSize = line.fontSize;
                line.resizeTextMinSize = 16;
            }

            PanelKit.Pill(page, "XiConfirm", new Vector2(1, 0), new Vector2(-210, 80), new Vector2(340, 120), "CONFIRM ►", UiKit.Accent, Confirm, 40);
            PanelKit.Pill(page, "XiAuto", new Vector2(1, 0), new Vector2(-570, 80), new Vector2(300, 120), "AUTO XI", UiKit.Muted, () =>
            {
                _xi = _auto.ToList();
                _selXi = _selBench = null;
                Say("AUTO XI BACK IN", UiKit.Good);
                Refresh();
            }, 34);
            if (_back != null)
                UiKit.Btn(page, "XiBack", new Vector2(0, 1), new Vector2(150, -70), new Vector2(200, 80), "◄ BACK", UiKit.Muted, () =>
                {
                    _done = true;
                    Destroy(gameObject);
                    _back?.Invoke();
                }, 30);

            // The clock (tournament / online): bottom left, with what happens at 0 spelled out beside it.
            _ringCaption = PlayerCardKit.Text(page, new Vector2(0, 0), new Vector2(0, 0), new Vector2(196, 45), new Vector2(620, 115), "", 26, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            if (_seconds > 0) _ring = PanelKit.Countdown(page, new Vector2(0, 0), new Vector2(110, 80), 120);
            Refresh();
        }

        private void Refresh()
        {
            UiKit.Clear(_xiBox.Find("Rows") ?? NewRows());
            var rows = (RectTransform)_xiBox.Find("Rows");
            // Numbered in the order the match bats them (auction-r1 #20: it listed keeper 1, bowlers 2-6;
            // the match itself bats in Cricket.BattingOrder, Match.cs), so 1-11 really is the batting order.
            var shown = Cricket.BattingOrder(_xi);
            for (var i = 0; i < shown.Count; i++)
            {
                var p = shown[i];
                var row = PlayerRow(rows, p, i + 1, _selXi == p.Id, () => TapXi(p));
                row.anchorMin = row.anchorMax = new Vector2(0.5f, 1);
                row.pivot = new Vector2(0.5f, 1);
                row.anchoredPosition = new Vector2(0, -i * (RowH + 4));
                row.sizeDelta = new Vector2(980, RowH);
            }
            UiKit.Clear(_benchList);
            var bench = Bench;
            foreach (var p in bench)
            {
                var holder = UiKit.ListRow(_benchList, RowH, new Color(0, 0, 0, 0));
                var row = PlayerRow(holder, p, 0, _selBench == p.Id, () => TapBench(p));
                row.anchorMin = Vector2.zero;
                row.anchorMax = Vector2.one;
                row.offsetMin = row.offsetMax = Vector2.zero;
            }
            if (bench.Count == 0)
            {
                var holder = UiKit.ListRow(_benchList, RowH * 2, new Color(0, 0, 0, 0));
                PlayerCardKit.Text(holder, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, "No one on the bench: the squad is eleven.", 24, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            }

            // The rules as they stand.
            var cap = PlayingXi.OverseasCap(_squad, _maxOverseas);
            var ovs = _xi.Count(p => p.Overseas);
            var keepers = _xi.Count(p => p.IsKeeper);
            var options = _xi.Count(p => p.IsBowlingOption);
            var need = PlayingXi.BowlingOptionsNeeded(_squad);
            string Mark(bool ok) => ok ? "<color=#4CD980>OK</color>" : "<color=#EB5252>NO</color>";
            _status.supportRichText = true;
            _status.text = $"OVERSEAS {ovs}/{cap} {Mark(ovs <= cap)}     KEEPER {Mark(keepers > 0 || !_squad.Any(p => p.IsKeeper))}     BOWLING OPTIONS {options} (need {need}) {Mark(options >= need)}     RATING {PlayerCardKit.TeamRating(_xi)}";
        }

        private RectTransform NewRows()
        {
            var rows = new GameObject("Rows", typeof(RectTransform));
            var rt = (RectTransform)rows.transform;
            rt.SetParent(_xiBox, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(0, 10);
            rt.offsetMax = new Vector2(0, -52);
            return rt;
        }

        /// <summary>One player: number, role glyph, name, role tag, OVERSEAS, rating; lit when selected.</summary>
        private RectTransform PlayerRow(RectTransform parent, Player p, int number, bool selected, Action tap)
        {
            var bright = PlayerCardKit.Bright(UiKit.ParseHex(_team.PrimaryColor));
            var colour = selected ? UiKit.WithAlpha(bright, 0.55f) : new Color(0.08f, 0.1f, 0.17f, 0.95f);
            var row = UiKit.Box(parent, "XiRow_" + p.Id, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100, RowH), colour);
            row.gameObject.AddComponent<Button>().onClick.AddListener(() => tap());
            var x = 18f;
            if (number > 0)
            {
                PlayerCardKit.Text(row, new Vector2(0, 0), new Vector2(0, 1), new Vector2(x, 0), new Vector2(x + 40, 0), number.ToString(), 26, TextAnchor.MiddleCenter, UiKit.Dim, condensed: true);
                x += 46;
            }
            var icon = PlayerCardKit.Shape(row, "Role", RoleIcons.For(p.Role), Color.white, new Vector2(x + 22, 0), new Vector2(40, 40), new Vector2(0, 0.5f));
            icon.raycastTarget = false;
            x += 56;
            PlayerCardKit.Text(row, new Vector2(0, 0), new Vector2(1, 1), new Vector2(x, 0), new Vector2(-300, 0), p.Name.ToUpperInvariant(), 28, TextAnchor.MiddleLeft, Color.white, condensed: true);
            // Right side: role tag, OVERSEAS, rating.
            Chip(row, new Vector2(-250, 0), new Vector2(90, 36), PlayingXi.RoleTag(p), RoleColour(p));
            if (p.Overseas) Chip(row, new Vector2(-150, 0), new Vector2(84, 36), "OVS", new Color(0.36f, 0.62f, 0.98f));
            PlayerCardKit.Text(row, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-90, 0), new Vector2(-16, 0), PlayerCardKit.Rating(p).ToString(), 30, TextAnchor.MiddleRight, UiKit.Gold, condensed: true);
            return row;
        }

        private static void Chip(RectTransform row, Vector2 pos, Vector2 size, string text, Color colour)
        {
            var chip = PlayerCardKit.Shape(row, "Chip_" + text, CardSprites.Round(14), colour, pos, size, new Vector2(1, 0.5f));
            chip.raycastTarget = false;
            PlayerCardKit.Text((RectTransform)chip.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, text, 20, TextAnchor.MiddleCenter, UiKit.Ink, condensed: true);
        }

        private static Color RoleColour(Player p) => p.Role switch
        {
            Role.Batter => new Color(0.98f, 0.78f, 0.3f),
            Role.Bowler => new Color(0.4f, 0.85f, 0.55f),
            Role.AllRounder => new Color(0.75f, 0.6f, 0.98f),
            _ => new Color(0.95f, 0.55f, 0.35f),
        };

        // ------------------------------------------------------------- swapping

        /// <summary>A player in the XI tapped: chosen to go out (or swapped with the chosen bench player).</summary>
        public void TapXi(Player p)
        {
            if (_selBench != null)
            {
                TrySwap(p, _squad.First(x => x.Id == _selBench));
                return;
            }
            _selXi = _selXi == p.Id ? null : p.Id;
            if (_selXi != null) Say($"{p.Name.ToUpperInvariant()} OUT · NOW TAP A BENCH PLAYER", UiKit.Gold);
            Refresh();
        }

        /// <summary>A bench player tapped: chosen to come in (or swapped with the chosen XI player).</summary>
        public void TapBench(Player p)
        {
            if (_selXi != null)
            {
                TrySwap(_xi.First(x => x.Id == _selXi), p);
                return;
            }
            _selBench = _selBench == p.Id ? null : p.Id;
            if (_selBench != null) Say($"{p.Name.ToUpperInvariant()} IN · NOW TAP WHO HE REPLACES", UiKit.Gold);
            Refresh();
        }

        private void TrySwap(Player outgoing, Player incoming)
        {
            _selXi = _selBench = null;
            var xi = PlayingXi.Swap(_xi, outgoing, incoming, _squad, _maxOverseas, out var problem);
            if (xi == null) Say(problem ?? "CAN'T MAKE THAT SWAP", UiKit.Bad);
            else
            {
                _xi = xi;
                Say($"▲ {incoming.Name.ToUpperInvariant()}   ▼ {outgoing.Name.ToUpperInvariant()}", UiKit.Good);
            }
            Refresh();
        }

        private void Say(string text, Color colour)
        {
            _message.text = text;
            _message.color = colour;
            _messageUntil = Time.unscaledTime + 3.5f;
        }

        // -------------------------------------------------------------- confirm

        public void Confirm()
        {
            if (_done) return;
            _done = true;
            var xi = _xi.ToList();
            XiChoice.Set(_team.Id, xi);
            Debug.Log($"[APL xi] confirmed {_team.Id}: {string.Join(", ", xi.Select(p => p.Id))}" + (_seconds > 0 && _left <= 0 ? " (clock ran out)" : ""));
            Destroy(gameObject);
            _confirmed(xi);
        }

        private void Update()
        {
            if (_done) return;
            if (_message != null && _message.text.Length > 0 && Time.unscaledTime > _messageUntil) _message.text = "";
            if (_ring == null) return;
            _left -= Time.unscaledDeltaTime;
            _ring.Show(_left, _seconds);
            _ringCaption.text = $"AUTO-CONFIRM IN {Mathf.CeilToInt(Mathf.Max(0, _left))} s";
            if (_left <= 0) Confirm();
        }
    }
}
