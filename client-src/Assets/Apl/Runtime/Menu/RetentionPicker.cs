using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// Last season's squad to tick keeps on, with the cost and purse left underneath. Updates in
    /// place (no rebuild), so the list keeps its scroll position. Shared by the solo menu and
    /// friend rooms.
    /// Rater r1 #13: the list showed 7 of 25 rows with no scroll cue, and the AI's pre-picked keeps
    /// weren't explained. Now two compact columns (best first, down the left then the right) that
    /// fit a whole squad of 25; a line above says what the gold rows are; the totals strip counts
    /// roles and overseas; and if a squad ever overflows, a "SCROLL FOR MORE" cue sits at the foot.
    /// </summary>
    public sealed class RetentionPicker
    {
        private readonly Dataset _data;
        private readonly string _teamId;
        private readonly AuctionFormat _format;
        private readonly List<Player> _picks;
        private readonly int _max;
        private readonly List<(Player p, Image bg, Text name, Text info, Text price)> _rows = new List<(Player, Image, Text, Text, Text)>();
        private readonly Text _summary;

        /// <summary>Called after every change, with the problem (null = allowed).</summary>
        public event Action<string?>? Changed;

        public string? Problem => Retention.Problem(_teamId, _picks, _data.Rules, _format);

        /// <summary>The list's box: under the page header (sub line ends ~160 px down), above the totals strip.</summary>
        private const float ListTop = 200f, ListH = 640f, ListW = 1560f, Spacing = 4f, Pad = 16f;

        public RetentionPicker(RectTransform page, Dataset data, string teamId, AuctionFormat format, List<Player> picks)
        {
            _data = data;
            _teamId = teamId;
            _format = format;
            _picks = picks;
            _max = data.Rules.RetentionFor(format).MaxPlayers;

            var candidates = data.Players.Where(p => p.PreviousTeam == teamId).ToList();
            candidates.Sort(Retention.ByValue);

            // What the gold rows mean: the AI's suggestion (mini auction) or nothing kept yet.
            var hint = picks.Count > 0
                ? $"GOLD = KEPT. These {picks.Count} are SUGGESTED KEEPS (the AI's picks for your squad). Tap a player to release or keep him."
                : $"Tap a player to keep him (gold). Best value first. Up to {_max}.";
            var hintText = UiKit.LabelAt(page, new Vector2(0.5f, 1), new Vector2(0, -ListTop + 18), new Vector2(ListW, 30), hint, 22, TextAnchor.MiddleCenter, UiKit.Gold, bold: false);
            hintText.name = "RetentionHint";

            // Two columns, filled top to bottom: the whole squad on one screen.
            var rows = Mathf.Max(1, Mathf.CeilToInt(candidates.Count / 2f));
            var rowH = Mathf.Clamp(Mathf.Floor((ListH - Pad - Spacing * (rows - 1)) / rows), 40f, 72f);
            var list = UiKit.ScrollList(page, new Vector2(0.5f, 1), new Vector2(0, -ListTop - ListH / 2), new Vector2(ListW, ListH));
            var lines = new List<RectTransform>();
            for (var r = 0; r < rows; r++) lines.Add(UiKit.ListRow(list, rowH, new Color(0, 0, 0, 0)));
            var big = rowH >= 56f;
            for (var i = 0; i < candidates.Count; i++)
            {
                var p = candidates[i];
                var line = lines[i % rows];
                var col = i / rows;
                var cell = new GameObject("Keep_" + p.Id, typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)cell.transform;
                rt.SetParent(line, false);
                rt.anchorMin = new Vector2(col == 0 ? 0f : 0.5f, 0f);
                rt.anchorMax = new Vector2(col == 0 ? 0.5f : 1f, 1f);
                rt.offsetMin = new Vector2(col == 0 ? 0f : 4f, 0f);
                rt.offsetMax = new Vector2(col == 0 ? -4f : 0f, 0f);
                var player = p;
                cell.AddComponent<Button>().onClick.AddListener(() => Toggle(player));
                var name = UiKit.LabelAt(rt, new Vector2(0, 0.5f), new Vector2(16 + 260, big ? 12 : 9), new Vector2(520, big ? 34 : 26), p.Name, big ? 28 : 23, TextAnchor.MiddleLeft);
                var info = UiKit.LabelAt(rt, new Vector2(0, 0.5f), new Vector2(16 + 260, big ? -16 : -11), new Vector2(520, big ? 24 : 20),
                    $"{MainMenu.RoleName(p)} · BAT {p.Batting} · BOWL {p.Bowling} · {(p.Overseas ? p.Country.ToUpperInvariant() + " (OS)" : "India")}{(p.Capped ? "" : " · UNCAPPED")}",
                    big ? 19 : 16, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
                var price = UiKit.LabelAt(rt, new Vector2(1, 0.5f), new Vector2(-110, 0), new Vector2(200, rowH - 6), "", big ? 28 : 24, TextAnchor.MiddleRight);
                _rows.Add((p, cell.GetComponent<Image>(), name, info, price));
            }
            // A taller squad than the box (not in today's data): say there's more below.
            var contentH = Pad + rows * rowH + Spacing * (rows - 1);
            if (contentH > ListH + 1f)
            {
                var cue = UiKit.Box(page, "ScrollCue", new Vector2(0.5f, 1), new Vector2(0, -ListTop - ListH + 16), new Vector2(360, 32), new Color(0.04f, 0.05f, 0.1f, 0.92f));
                UiKit.Label(cue, "▼ SCROLL FOR MORE", 20, TextAnchor.MiddleCenter, UiKit.Gold);
            }
            Debug.Log($"[APL retention] {teamId} {format.Id}: {candidates.Count} players in 2 columns x {rows} rows of {rowH:0} px ({(contentH > ListH + 1f ? "scrolls" : "all on screen")}), {picks.Count} suggested keeps");

            // A dark backing strip: without it the hall behind (a table's name plate) reads through the totals.
            var strip = UiKit.Box(page, "SummaryStrip", new Vector2(0.5f, 0), new Vector2(0, 205), new Vector2(ListW, 52), new Color(0.04f, 0.05f, 0.1f, 0.92f));
            _summary = UiKit.LabelAt(strip, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ListW - 20, 40), "", 26, TextAnchor.MiddleCenter);
            _summary.resizeTextForBestFit = true;
            _summary.resizeTextMinSize = 16;
            _summary.resizeTextMaxSize = 26;
        }

        /// <summary>Call once the owner has subscribed to Changed.</summary>
        public void Refresh()
        {
            var prices = Retention.Prices(_picks, _data.Rules, _format).ToDictionary(kv => kv.Key.Id, kv => kv.Value);
            // In a mini auction every player has his own price: show it before he's picked.
            var atValue = _data.Rules.RetentionFor(_format).AtValue;
            foreach (var (p, bg, name, info, price) in _rows)
            {
                var kept = _picks.Contains(p);
                bg.color = kept ? UiKit.Gold : UiKit.Muted;
                name.color = kept ? UiKit.Ink : Color.white;
                info.color = kept ? UiKit.Ink : UiKit.Dim;
                price.color = kept ? UiKit.Ink : Color.white;
                price.text = kept && prices.TryGetValue(p.Id, out var cost) ? Money.Format(cost)
                    : atValue ? Money.Format(Retention.Prices(new[] { p }, _data.Rules, _format)[0].Value)
                    : "KEEP";
            }
            var spent = prices.Values.Sum();
            var problem = Problem;
            int Count(Role r) => _picks.Count(x => x.Role == r);
            var mix = $"BAT {Count(Role.Batter)} · WK {_picks.Count(x => x.Role != Role.Batter && x.Role != Role.Bowler && x.Role != Role.AllRounder)} · AR {Count(Role.AllRounder)} · BOWL {Count(Role.Bowler)} · OVERSEAS {_picks.Count(x => x.Overseas)}";
            _summary.text = problem ?? $"KEPT {_picks.Count}/{_max}  ({mix})  ·  COST {Money.Format(spent)}  ·  PURSE LEFT {Money.Format(_format.Purse - spent)}";
            _summary.color = problem == null ? Color.white : UiKit.Bad;
            Changed?.Invoke(problem);
        }

        private void Toggle(Player p)
        {
            if (_picks.Contains(p)) _picks.Remove(p);
            else if (_picks.Count < _max) _picks.Add(p);
            Refresh();
        }
    }
}
