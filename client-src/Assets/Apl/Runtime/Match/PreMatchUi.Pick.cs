using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Every "pick players" screen in the card kit — the openers (striker then
    /// non-striker, f43–48), SELECT BATTER, the impact player's picks, the Super
    /// Over's batters: big cards in up to two rows on a glass panel, a tap lifts
    /// one with its glow and tick and tag, the countdown ring autoselects, the
    /// CONFIRM pill (or a moment's pause once the pick is full) goes on.
    /// </summary>
    public sealed partial class PreMatchUi
    {
        /// <summary>
        /// Pick <paramref name="count"/> of <paramref name="players"/>. <paramref name="note"/> greys a card
        /// and says why (out, at the crease); unpicked places fill from <paramref name="defaults"/> when the
        /// clock runs out. <paramref name="heading"/> / <paramref name="tag"/>: this screen's title and the
        /// picked cards' label (else the openers' / next batter's).
        /// </summary>
        public IEnumerator Pick(Franchise team, IReadOnlyList<Player> players, int count, IReadOnlyList<Player> defaults,
            Func<Player, string?> note, float seconds, Action<List<Player>> done, string? heading = null, string? tag = null, bool preselect = false,
            bool perPick = false, bool realtime = false)
        {
            // perPick (owner v26 #8): the clock is per place — the striker's seconds, then the non-striker's afresh.
            // realtime: the clock runs while time is stopped (the in-match impact pick holds the game).
            var page = NewPage(new Color(0.01f, 0.012f, 0.03f, 0.8f));
            // preselect: the defaults start picked, so CONFIRM is there at once (the impact subs: no clock, round 10).
            var picked = preselect ? defaults.Where(d => note(d) == null && players.Any(p => p.Id == d.Id)).Take(count).ToList() : new List<Player>();
            string[] tags = tag != null ? new[] { tag } : count == 2 ? new[] { "STRIKER", "NON-STRIKER" } : new[] { "IN NEXT" };
            string Title() => heading != null ? heading : count == 1 ? "SELECT BATTER" : picked.Count == 0 ? "CHOOSE A STRIKER & NON-STRIKER"
                : picked.Count == 1 ? "CHOOSE NON-STRIKER" : "STRIKER & NON-STRIKER";
            var header = PanelKit.Header(page, Title(), count > 1 ? $"Tap {count} cards" : "Tap a card", team, null);
            var titleText = header.Title;
            var subText = header.Subtitle;
            var panel = PanelKit.GlassPanel(page, "Picks", new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(1820, 680));
            // (Round 13: 680 tall, raised: the cards fit inside it, and CONFIRM / the ring have their own row under it —
            // the bottom row used to spill past the panel and under CONFIRM, r12 P05.)
            // Near-opaque, and the IMPACT PLAYER button put away while the pick is up (v8 f267: "IMPACT P" showed through).
            panel.GetComponent<UnityEngine.UI.Image>().color = UiKit.WithAlpha(PanelKit.Glass, 0.96f);
            var impactWas = _impact != null && _impact.activeSelf;
            if (_impact != null) _impact.SetActive(false);

            // Up to six a row, two rows; cards as big as the row allows (never under 120 px to tap).
            var shown = players.Take(12).ToList();
            var top = shown.Count <= 6 ? shown.Count : (shown.Count + 1) / 2;
            var perRow = Math.Max(top, shown.Count - top);
            var w = Mathf.Clamp(1700f / Math.Max(1, perRow) - 24f, 150f, 250f);
            var size = new Vector2(w, w * 300f / 220f);
            // Two rows in 680: each card at most 290 tall (with room for the lift and the tags), one row at most 560.
            var maxH = shown.Count > 6 ? 290f : 560f;
            if (size.y > maxH) size = new Vector2(maxH * 220f / 300f, maxH);
            var bright = PlayerCardKit.Bright(UiKit.ParseHex(team.PrimaryColor));
            var cards = new Dictionary<string, PlayerCard>();
            void Refresh()
            {
                titleText.text = Title();
                subText.text = picked.Count == 0 ? (count > 1 ? $"Tap {count} cards" : "Tap a card") : string.Join("   ·   ", picked.Select(p => p.Name.ToUpperInvariant()));
                foreach (var kv in cards)
                {
                    var i = picked.FindIndex(p => p.Id == kv.Key);
                    kv.Value.Selected = i >= 0;
                    kv.Value.Tag(i >= 0 ? tags[Math.Min(i, tags.Length - 1)] : null, bright);
                }
            }
            for (var i = 0; i < shown.Count; i++)
            {
                var p = shown[i];
                var row = i < top ? 0 : 1;
                var inRow = row == 0 ? top : shown.Count - top;
                var col = row == 0 ? i : i - top;
                var y = shown.Count <= 6 ? -10f : 150f - row * 318f;
                var card = PlayerCardKit.Card(panel, p, team, new Vector2(0.5f, 0.5f), new Vector2((col - (inRow - 1) / 2f) * (size.x + 24), y), size, delay: 0.04f * i);
                // Ghosted in from the first frame: the panel never shows empty while they stagger in.
                // Full strength from the first frame (r11 f06: ghosted cards read as greyed-out / disabled).
                card.Motion.FromAlpha = 1f;
                cards[p.Id] = card;
                var why = note(p);
                if (why != null)
                {
                    card.Note(why);
                    continue;
                }
                var player = p;
                card.OnTap(() =>
                {
                    var at = picked.FindIndex(x => x.Id == player.Id);
                    if (at >= 0) picked.RemoveAt(at);
                    else if (picked.Count < count) picked.Add(player);
                    Refresh();
                });
            }
            Refresh();
            if (Debug.isDebugBuild) Debug.Log($"[APL prematch] pick '{Title()}': {cards.Count} cards of {players.Count}, {count} to pick, {seconds:0.0} s");
            var go = false;
            var confirm = PanelKit.Pill(page, "Confirm", new Vector2(1, 0), new Vector2(-200, 80), new Vector2(320, 120), "CONFIRM ►", bright, () => go = true, 40);
            var ring = PanelKit.Countdown(page, new Vector2(0, 0), new Vector2(110, 84), 120);
            // The clock on screen whenever there is one (owner v26 #8: the impact pick has 20 s now), and what happens at 0 beside it.
            var caption = PlayerCardKit.Text(page, new Vector2(0, 0), new Vector2(0, 0), new Vector2(186, 50), new Vector2(640, 118), "", 26, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            if (seconds > 600) ring.Hide();
            var full = -1f;
            Skipped = false;
            var left = seconds;
            var placed = picked.Count;
            var timedOut = false;
            while (true)
            {
                if (perPick && picked.Count != placed)
                {
                    // A new place to fill: its own seconds.
                    if (picked.Count > placed && picked.Count < count) left = seconds;
                    placed = picked.Count;
                }
                if (seconds <= 600)
                {
                    ring.Show(left, seconds);
                    var place = count == 2 && tag == null ? (picked.Count == 0 ? "STRIKER" : "NON-STRIKER") : "";
                    caption.text = picked.Count >= count && !preselect ? "" : $"AUTO-PICK {place}{(place.Length > 0 ? " " : "")}IN {Mathf.CeilToInt(Mathf.Max(0, left))} s";
                }
                confirm.transform.parent.gameObject.SetActive(picked.Count == count);
                // A full pick goes on by itself after a moment (tap a card again to change it); not a pre-filled one.
                if (picked.Count < count) full = -1f;
                else if (full < 0) full = Time.unscaledTime;
                if (go || Skipped || (!preselect && full >= 0 && Time.unscaledTime - full > 1.4f && seconds < 600)) break;
                if (left <= 0)
                {
                    timedOut = true;
                    break;
                }
                yield return null;
                left -= realtime ? Time.unscaledDeltaTime : Time.deltaTime;
            }
            if (timedOut) Debug.Log($"[APL timers] '{Title()}' timed out after {seconds:0.#} s{(perPick ? " a place" : "")}: auto-pick");
            foreach (var d in defaults)
            {
                if (picked.Count >= count) break;
                if (picked.All(p => p.Id != d.Id) && note(d) == null) picked.Add(d);
            }
            done(picked);
            if (impactWas && _impact != null) _impact.SetActive(true);
            ClearPage();
        }
    }
}
