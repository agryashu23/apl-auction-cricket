using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// "SELECT BOWLER" at the start of each over (the user bowling), in the card
    /// kit like every player screen: a card per bowling option with his overs of
    /// the quota and his figures on its tag, greyed with the reason when he can't
    /// bowl it (the last over's man, or his quota bowled). Tap one; the best one
    /// allowed is picked when the ring runs out.
    /// </summary>
    public sealed partial class MatchHud
    {
        public IEnumerator PickBowler(List<BowlerOption> options, Franchise side, float seconds, Action<Player?> picked)
        {
            var shown = options.Where(o => o.Player.IsBowlingOption || o.OversBowled > 0).Take(8).ToList();
            if (shown.Count(o => o.Allowed) == 0) shown = options.Where(o => o.Allowed).Take(8).ToList();
            var page = PickerPage.Open("SELECT BOWLER", "Tap a card", side, null);
            var bright = PlayerCardKit.Bright(UiKit.ParseHex(side.PrimaryColor));
            var top = shown.Count <= 5 ? shown.Count : (shown.Count + 1) / 2;
            var perRow = Math.Max(top, shown.Count - top);
            var w = Mathf.Clamp(1700f / Math.Max(1, perRow) - 28f, 160f, 250f);
            if (shown.Count > 5) w = Mathf.Min(w, 230f);
            var size = new Vector2(w, w * 300f / 220f);
            Player? chosen = null;
            var cards = new List<(PlayerCard card, Player player)>();
            for (var i = 0; i < shown.Count; i++)
            {
                var o = shown[i];
                var row = i < top ? 0 : 1;
                var inRow = row == 0 ? top : shown.Count - top;
                var col = row == 0 ? i : i - top;
                var y = shown.Count <= 5 ? 0f : 184f - row * 368f;
                var card = PlayerCardKit.Card(page.Panel, o.Player, side, new Vector2(0.5f, 0.5f), new Vector2((col - (inRow - 1) / 2f) * (size.x + 28), y), size, delay: 0.04f * i);
                var fig = o.Figures;
                card.Tag($"{o.OversBowled}/{o.MaxOvers} OV" + (fig != null ? $"  {fig.Wickets}-{fig.Runs}" : ""), o.Allowed ? bright : new Color(0.5f, 0.52f, 0.58f));
                if (!o.Allowed)
                {
                    card.Note(o.BowledLast ? "BOWLED LAST OVER" : "QUOTA BOWLED");
                    continue;
                }
                var player = o.Player;
                cards.Add((card, player));
                card.OnTap(() =>
                {
                    if (chosen != null) return;
                    chosen = player;
                    foreach (var (c, p) in cards) c.Selected = p == player;
                    page.Header.Subtitle.text = player.Name.ToUpperInvariant() + "  ·  " + Bowling.StyleName(player).ToUpperInvariant();
                });
            }
            var until = Time.unscaledTime + seconds;
            var pickedAt = -1f;
            // What the clock does at 0, beside the ring (owner v26 #8).
            var caption = PlayerCardKit.Text(page.Root, new Vector2(0, 0), new Vector2(0, 0), new Vector2(186, 50), new Vector2(640, 118), "", 26, TextAnchor.MiddleLeft, UiKit.Gold, condensed: true);
            while (Time.unscaledTime < until)
            {
                page.Ring.Show(until - Time.unscaledTime, seconds);
                caption.text = chosen != null ? "" : $"AUTO-PICK BOWLER IN {Mathf.CeilToInt(until - Time.unscaledTime)} s";
                // A moment to see the pick lift before the page goes.
                if (chosen != null && pickedAt < 0) pickedAt = Time.unscaledTime;
                if (pickedAt >= 0 && Time.unscaledTime - pickedAt > 0.45f) break;
                yield return null;
            }
            page.Close();
            picked(chosen);
        }
    }
}
