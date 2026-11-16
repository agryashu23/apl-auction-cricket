using System.Collections;
using System.Collections.Generic;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Match
{
    /// <summary>
    /// The squads screen (f1–9) in the card kit: both XIs on glass panels in two
    /// rows, cards staggering in, a VS badge between them, the header with both
    /// crests and team ratings, a countdown ring; tap anywhere or SKIP.
    /// </summary>
    public sealed partial class PreMatchUi
    {
        public IEnumerator Squads(string title, Franchise mine, IReadOnlyList<Player> myXI, Player? myCaptain,
            Franchise opp, IReadOnlyList<Player> oppXI, Player? oppCaptain, float seconds)
        {
            var page = NewPage(new Color(0.01f, 0.012f, 0.03f, 0.8f));
            TapToSkip(page);
            PanelKit.Header(page, title, $"MY TEAM  ·  TR {PlayerCardKit.TeamRating(myXI)}        OPPONENT  ·  TR {PlayerCardKit.TeamRating(oppXI)}", mine, opp);
            SquadSide(page, mine, myXI, myCaptain, -1, "MY TEAM");
            SquadSide(page, opp, oppXI, oppCaptain, 1, "OPPONENT");
            VsBadge(page, new Vector2(0, -20));
            var ring = PanelKit.Countdown(page, new Vector2(0.5f, 0), new Vector2(0, 86), 120);
            PanelKit.Pill(page, "Skip", new Vector2(1, 0), new Vector2(-180, 80), new Vector2(280, 120), "SKIP ►", UiKit.Accent, () => Skipped = true, 40);
            if (Debug.isDebugBuild) Debug.Log($"[APL prematch] squads screen: {myXI.Count} v {oppXI.Count}, {seconds:0.0} s");
            yield return Clock(seconds, ring);
            ClearPage();
        }

        /// <summary>One side: its glass panel, its name in its colour, eleven cards in rows of six and five.</summary>
        private static void SquadSide(RectTransform page, Franchise team, IReadOnlyList<Player> xi, Player? captain, int side, string heading)
        {
            var x = side * 482f;
            // Sized to the two rows of cards plus the balance bar (a 720 panel left its lower third empty).
            var panel = PanelKit.GlassPanel(page, "Side_" + team.Id, new Vector2(0.5f, 0.5f), new Vector2(x, -20), new Vector2(950, 650));
            var colour = PlayerCardKit.Bright(UiKit.ParseHex(team.PrimaryColor));
            PlayerCardKit.Shape(panel, "Band", CardSprites.Round(6), colour, new Vector2(0, -6), new Vector2(950 - 60, 6), new Vector2(0.5f, 1));
            PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -62), new Vector2(-30, -14),
                $"{heading}  ·  {team.Name.ToUpperInvariant()}", 30, side < 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, side < 0 ? YouMark.Lime : colour, condensed: true);
            var rows = new[] { 6, 5 };
            var size = new Vector2(148, 202);
            var i = 0;
            for (var r = 0; r < rows.Length && i < xi.Count; r++)
            {
                for (var c = 0; c < rows[r] && i < xi.Count; c++, i++)
                {
                    var pos = new Vector2((c - (rows[r] - 1) / 2f) * 154f, 128 - r * 242f);
                    // Nearest the middle first, so the two sides fill in towards each other.
                    var order = side < 0 ? rows[r] - 1 - c : c;
                    PlayerCardKit.Card(panel, xi[i], team, new Vector2(0.5f, 0.5f), pos, size, captain != null && xi[i].Id == captain.Id, 0.05f + r * 0.18f + order * 0.05f);
                }
            }
            Balance(panel, xi, colour);
        }

        /// <summary>The XI's make-up along the panel's foot: batters, all-rounders, bowlers, keeper, and its rating.</summary>
        private static void Balance(RectTransform panel, IReadOnlyList<Player> xi, Color colour)
        {
            int Count(Role r) { var n = 0; foreach (var p in xi) if (p.Role == r) n++; return n; }
            var bar = PlayerCardKit.Shape(panel, "Balance", CardSprites.Round(16), new Color(0.03f, 0.035f, 0.07f, 0.9f), new Vector2(0, 48), new Vector2(890, 64), new Vector2(0.5f, 0));
            var br = (RectTransform)bar.transform;
            var items = new (string key, string label, int n)[]
            {
                ("bat", "BAT", Count(Role.Batter)), ("both", "AR", Count(Role.AllRounder)),
                ("ball", "BOWL", Count(Role.Bowler)), ("gloves", "WK", Count(Role.WicketKeeper)),
            };
            for (var k = 0; k < items.Length; k++)
            {
                var cx = -330f + k * 170f;
                PlayerCardKit.Shape(br, "Icon_" + items[k].key, RoleIcons.For(items[k].key), colour, new Vector2(cx - 40, 0), new Vector2(40, 40));
                PlayerCardKit.Text(br, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(cx - 14, 0), new Vector2(cx + 80, 0), $"{items[k].n} {items[k].label}", 28, TextAnchor.MiddleLeft, Color.white, condensed: true);
            }
            PlayerCardKit.Text(br, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-200, 0), new Vector2(-24, 0), $"TR {PlayerCardKit.TeamRating(xi)}", 32, TextAnchor.MiddleRight, UiKit.Gold, condensed: true);
        }

        /// <summary>The VS between the panels: a disc with a ring, the letters in condensed type.</summary>
        private static void VsBadge(RectTransform parent, Vector2 pos)
        {
            PlayerCardKit.Shape(parent, "VsGlow", CardSprites.Glow(40, 30), new Color(0.55f, 0.3f, 1f, 0.55f), pos, new Vector2(200, 200));
            var disc = PlayerCardKit.Shape(parent, "Vs", CardSprites.Disc(), new Color(0.06f, 0.05f, 0.14f, 0.97f), pos, new Vector2(132, 132));
            PlayerCardKit.Shape((RectTransform)disc.transform, "VsRing", CardSprites.Ring(0.08f), UiKit.Gold, Vector2.zero, new Vector2(132, 132));
            var vs = PlayerCardKit.Text((RectTransform)disc.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, "VS", 62, TextAnchor.MiddleCenter, Color.white, condensed: true);
            vs.fontStyle = FontStyle.Italic;
        }

        /// <summary>The countdown ring until the time's up, SKIP or <paramref name="done"/>.</summary>
        private IEnumerator Clock(float seconds, CountdownRing ring, System.Func<bool>? done = null)
        {
            Skipped = false;
            var end = Time.time + seconds;
            while (Time.time < end && !Skipped && (done == null || !done()))
            {
                ring.Show(end - Time.time, seconds);
                yield return null;
            }
        }
    }
}
