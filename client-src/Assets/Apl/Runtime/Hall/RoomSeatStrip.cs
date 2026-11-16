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
    /// Room only: a slim row of chips under the lot bar, one per person in the room — crest,
    /// team and name — with a badge for who has PASSED this lot, whose team a BOT is playing
    /// (simulating or dropped out), who is OFFLINE, and who is leading, and a HOST tag. The badges
    /// stack in a column inside each chip's right edge (status on top, HOST below), so they never
    /// overlap each other or stick out of the chip. Everyone sees it; it sits between the RTM chip
    /// (left) and SIMULATE / pause (right), clear of the bid controls; room toasts go right under it.
    /// </summary>
    public sealed class RoomSeatStrip : MonoBehaviour
    {
        /// <summary>What one chip shows.</summary>
        public struct Chip
        {
            public string TeamId, Name;
            public SeatState State;
            public bool Passed, Me, Host, Leading;
        }

        private Dataset _data = null!;
        private RectTransform _row = null!;
        private string _drawn = "";

        /// <summary>Widest the row may be (RTM chip on the left, SIMULATE + pause on the right).</summary>
        private const float MaxWidth = 980f;

        public static RoomSeatStrip Create(RectTransform hudRoot, Dataset data)
        {
            var go = new GameObject("RoomSeats", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(hudRoot, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1);
            rt.pivot = new Vector2(0.5f, 1);
            // 66 px down: 14 px of headroom under the lot bar (ends 52 px down); chips end at 122.
            rt.anchoredPosition = new Vector2(0, -66);
            rt.sizeDelta = new Vector2(MaxWidth, ChipH);
            var strip = go.AddComponent<RoomSeatStrip>();
            strip._data = data;
            strip._row = rt;
            return strip;
        }

        /// <summary>The last drawn chips, one line each (test runs log it).</summary>
        public string Describe() => _drawn.Replace("|", "  ");

        /// <summary>Redraws only when something changed (called every frame by the hall).</summary>
        public void Show(IReadOnlyList<Chip> chips)
        {
            var key = string.Join("|", chips.Select(c => $"{c.TeamId}:{c.Name}:{c.State}:{c.Passed}:{c.Host}:{c.Leading}"));
            if (key == _drawn) return;
            _drawn = key;
            UiKit.Clear(_row);
            if (chips.Count == 0) return;
            var w = Mathf.Min(ChipMax, MaxWidth / chips.Count);
            // Every chip keeps the same badge column when any chip has a badge, so names line up.
            var anyBadge = chips.Any(c => c.Host || Status(c).Tag.Length > 0);
            var bw = anyBadge ? Mathf.Clamp(w * 0.32f, 40f, 58f) : 0f;
            for (var i = 0; i < chips.Count; i++)
            {
                var c = chips[i];
                var team = _data.Franchises.FirstOrDefault(f => f.Id == c.TeamId);
                var colour = team != null ? UiKit.ParseHex(team.PrimaryColor) : Color.gray;
                var x = (i - (chips.Count - 1) / 2f) * w;
                var faded = c.Passed || c.State == SeatState.Disconnected;
                var chip = UiKit.Box(_row, "Seat_" + c.TeamId, new Vector2(0.5f, 1), new Vector2(x, -ChipH / 2), new Vector2(w - 8, ChipH),
                    c.Leading ? UiKit.WithAlpha(colour, 0.95f) : new Color(0.05f, 0.07f, 0.13f, faded ? 0.7f : 0.92f));
                // A team-colour bar on the left edge.
                UiKit.Box(chip, "Bar", new Vector2(0, 0.5f), new Vector2(3, 0), new Vector2(6, ChipH), colour);
                var crest = UiKit.Crest(chip, c.TeamId, new Vector2(0, 0.5f), new Vector2(30, 0), 40);
                if (faded) crest.color = new Color(1, 1, 1, 0.45f);
                var ink = c.Leading && colour.grayscale > 0.55f ? UiKit.Ink : Color.white;
                // Text between the crest and the badge column.
                var textW = Mathf.Max(24f, w - 8 - 54 - (bw > 0 ? bw + 6 : 4));
                var label = UiKit.LabelAt(chip, new Vector2(0, 1), new Vector2(54 + textW / 2, -16), new Vector2(textW, 24), team?.ShortName ?? c.TeamId, 18, TextAnchor.MiddleLeft, faded ? UiKit.Dim : ink);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 12;
                label.resizeTextMaxSize = 18;
                var name = UiKit.LabelAt(chip, new Vector2(0, 0), new Vector2(54 + textW / 2, 16), new Vector2(textW, 22), c.Me ? "YOU" : c.Name, 15, TextAnchor.MiddleLeft, c.Me ? YouMark.Lime : UiKit.Dim, bold: c.Me);
                name.resizeTextForBestFit = true;
                name.resizeTextMinSize = 10;
                name.resizeTextMaxSize = 15;
                // Badge column inside the chip's right edge: the status on top (one at a time, most
                // telling first), HOST underneath. Nothing sticks out of the chip, so nothing clips.
                var (tag, fill) = Status(c);
                if (tag.Length > 0) Badge(chip, "Badge_" + tag, tag, fill, bw, top: true, tag == "PASSED" || tag == "OFFLINE" ? Color.white : UiKit.Ink);
                if (c.Host) Badge(chip, "HostTag", "HOST", UiKit.Gold, bw, top: false, UiKit.Ink);
                if (c.Me) YouMark.Add(chip, "", 2);
            }
        }

        private const float ChipH = 56, BadgeH = 20, ChipMax = 200;

        private static (string Tag, Color Fill) Status(Chip c) =>
            c.State == SeatState.Disconnected ? ("OFFLINE", UiKit.Bad)
            : c.State == SeatState.Bot ? ("BOT", new Color(0.96f, 0.62f, 0.12f))
            : c.Passed ? ("PASSED", new Color(0.42f, 0.45f, 0.52f))
            : c.Leading ? ("LEADS", UiKit.Good)
            : ("", Color.clear);

        private static void Badge(RectTransform chip, string name, string tag, Color fill, float width, bool top, Color ink)
        {
            // 6 px in from the right edge; 6 px from the top (status) or the bottom (HOST).
            var badge = UiKit.Box(chip, name, new Vector2(1, top ? 1 : 0), new Vector2(-6 - width / 2, top ? -6 - BadgeH / 2 : 6 + BadgeH / 2), new Vector2(width, BadgeH), fill);
            var t = UiKit.Label(badge, tag, 13, TextAnchor.MiddleCenter, ink);
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 8;
            t.resizeTextMaxSize = 13;
        }
    }
}
