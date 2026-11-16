using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Hall
{
    /// <summary>
    /// Purse boards on the stage LED wall, one each side of the crest: every
    /// franchise's purse left and squad size, the current bidder lit in its colour, yours outlined.
    /// World-space uGUI, built in code when the auction starts.
    /// </summary>
    public sealed class PurseBoard : MonoBehaviour
    {
        private const float PixelsPerMetre = 360f;

        private readonly Dictionary<string, (Image row, Text purse, Text squad)> _rows = new Dictionary<string, (Image, Text, Text)>();
        private readonly Dictionary<string, Color> _colours = new Dictionary<string, Color>();
        private string _me = "";
        private readonly List<CanvasGroup> _groups = new List<CanvasGroup>();
        private Coroutine? _fade;

        /// <summary>The switch-off fades over this long (a hard pop reads as a glitch).</summary>
        private const float FadeSeconds = 0.2f;

        /// <summary>Two panels centred on the LED wall at <paramref name="wallCentre"/>, facing the hall.</summary>
        public static PurseBoard Create(Dataset data, string me, Vector3 wallCentre, float halfSpan)
        {
            var root = new GameObject("PurseBoard");
            var board = root.AddComponent<PurseBoard>();
            board._me = me;
            var teams = data.Franchises;
            var half = (teams.Count + 1) / 2;
            board.Panel(teams.Take(half).ToList(), wallCentre + new Vector3(-halfSpan, 0, 0), root.transform);
            board.Panel(teams.Skip(half).ToList(), wallCentre + new Vector3(halfSpan, 0, 0), root.transform);
            return board;
        }

        private void Panel(List<Franchise> teams, Vector3 centre, Transform parent)
        {
            var go = new GameObject("PursePanel", typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            _groups.Add(go.AddComponent<CanvasGroup>());
            canvas.renderMode = RenderMode.WorldSpace;
            const float w = 1500, rowH = 230;
            var h = 260 + teams.Count * rowH;
            rt.sizeDelta = new Vector2(w, h);
            rt.position = centre;
            rt.rotation = Quaternion.identity;
            rt.localScale = Vector3.one / PixelsPerMetre;

            var bg = UiKit.Stretch(rt, "Back", new Color(0.03f, 0.04f, 0.1f, 0.84f));
            UiKit.LabelAt(bg, new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(w - 80, 150), "PURSE LEFT", 120, TextAnchor.MiddleCenter, UiKit.Gold);
            for (var i = 0; i < teams.Count; i++)
            {
                var f = teams[i];
                _colours[f.Id] = UiKit.ParseHex(f.PrimaryColor);
                var row = UiKit.Box(bg, "Row_" + f.Id, new Vector2(0.5f, 1), new Vector2(0, -250 - rowH / 2 - i * rowH), new Vector2(w - 60, rowH - 20), new Color(1, 1, 1, 0.05f));
                UiKit.Crest(row, f.Id, new Vector2(0, 0.5f), new Vector2(120, 0), 180);
                UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(230 + 200, 0), new Vector2(400, 170), f.ShortName, 110, TextAnchor.MiddleLeft,
                    f.Id == _me ? YouMark.Lime : Color.white);
                // Your row: the pulsing YOU outline (not a colour a franchise could have).
                if (f.Id == _me) YouMark.Add(row, "YOU", 12, tagSize: 64);
                // Purse and squad count in their own columns (a 25-man squad reads "11/25").
                var purse = UiKit.LabelAt(row, new Vector2(1, 0.5f), new Vector2(-540, 0), new Vector2(600, 170), "", 100, TextAnchor.MiddleRight);
                var squad = UiKit.LabelAt(row, new Vector2(1, 0.5f), new Vector2(-115, 0), new Vector2(210, 150), "", 72, TextAnchor.MiddleRight, UiKit.Dim);
                _rows[f.Id] = (row.GetComponent<Image>(), purse, squad);
            }
        }

        /// <summary>
        /// Off for the close-ups (auctioneer, team tables): there the boards would only
        /// show as giant half-cut text behind the subject. The HUD's TEAMS tab still has the purses.
        /// </summary>
        public void SetVisible(bool on)
        {
            if (_fade != null)
            {
                StopCoroutine(_fade);
                _fade = null;
            }
            if (on)
            {
                if (!gameObject.activeSelf) gameObject.SetActive(true);
                foreach (var g in _groups) g.alpha = 1f;
                return;
            }
            if (gameObject.activeSelf) _fade = StartCoroutine(FadeOut());
        }

        private System.Collections.IEnumerator FadeOut()
        {
            // Game time, so it keeps pace with the auction (and with the 16x fast-forward).
            for (var t = 0f; t < FadeSeconds; t += Time.deltaTime)
            {
                foreach (var g in _groups) g.alpha = 1f - t / FadeSeconds;
                yield return null;
            }
            foreach (var g in _groups) g.alpha = 0f;
            gameObject.SetActive(false);
            _fade = null;
        }

        /// <summary>Latest purses; <paramref name="leaderId"/> (the current bid) is lit.</summary>
        public void Refresh(AuctionEngine engine, string? leaderId)
        {
            foreach (var kv in _rows)
            {
                if (!engine.Teams.TryGetValue(kv.Key, out var t)) continue;
                var (row, purse, squad) = kv.Value;
                purse.text = Money.Format(t.Purse);
                squad.text = $"{t.Size}/{engine.Format.MaxSquad}";
                // The bidder lit in his own colour; your row carries the YOU outline instead of a tint.
                row.color = kv.Key == leaderId && _colours.TryGetValue(kv.Key, out var lead) ? UiKit.WithAlpha(lead, 0.6f)
                    : new Color(1, 1, 1, 0.05f);
            }
        }
    }
}
