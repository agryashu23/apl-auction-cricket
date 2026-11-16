using System.Linq;
using Apl.Client.Match;
using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>The hub's main page: points table, fixtures and results, the next match.</summary>
    public sealed partial class TournamentHub
    {
        private const float RowH = 56;

        private void ShowTable()
        {
            var t = _t!;
            var page = NewPage();
            var source = t.Source == "auction" ? "AUCTION SQUADS" : "LAST SEASON'S SQUADS";
            UiKit.LabelAt(page, new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(1500, 64), "TOURNAMENT", 50, TextAnchor.MiddleCenter, UiKit.Gold);
            UiKit.LabelAt(page, new Vector2(0.5f, 1), new Vector2(0, -108), new Vector2(1500, 34), $"{source} · {t.Overs} OVERS A SIDE · {t.Fixtures.Count(f => f.Played)} OF {t.Fixtures.Count} PLAYED", 24, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            UiKit.Btn(page, "HubBack", new Vector2(0, 1), new Vector2(150, -70), new Vector2(200, 80), "◄ BACK", UiKit.Muted, Close, 30);
            UiKit.Btn(page, "HubNew", new Vector2(1, 1), new Vector2(-170, -70), new Vector2(260, 80), "NEW", UiKit.Muted, ShowSetup, 30);

            PointsTable(page, t);
            Fixtures(page, t);
            NextPanel(page, t);
        }

        private void PointsTable(RectTransform page, Tournament t)
        {
            const float width = 900;
            var box = UiKit.Box(page, "Table", new Vector2(0, 1), new Vector2(70 + width / 2, -470), new Vector2(width, 640), new Color(0, 0, 0, 0.25f));
            var cols = new (float w, string text)[] { (60, "#"), (60, ""), (300, "TEAM"), (80, "P"), (80, "W"), (80, "L"), (100, "PTS"), (140, "NRR") };
            Row(box, -26, width, new Color(0, 0, 0, 0), UiKit.Dim, 22, cols);
            var table = t.Table();
            for (var i = 0; i < table.Count; i++)
            {
                var r = table[i];
                var mine = r.Team == t.UserTeam;
                var y = -26 - (i + 1) * RowH;
                var colour = i % 2 == 0 ? new Color(0.09f, 0.11f, 0.18f, 0.95f) : new Color(0.06f, 0.08f, 0.14f, 0.95f);
                var row = Row(box, y, width - 12, colour, Color.white, 26,
                    (60, (i + 1).ToString()), (60, ""), (300, Team(r.Team).ShortName + (t.Qualified(r, i + 1) ? "  · Q" : "") + (mine ? "  · YOU" : "")),
                    (80, r.Played.ToString()), (80, r.Won.ToString()), (80, r.Lost.ToString()), (100, r.Points.ToString()),
                    (140, r.NetRunRate.ToString("+0.000;-0.000")));
                UiKit.Crest(row, r.Team, new Vector2(0, 0.5f), new Vector2(90, 0), RowH - 12);
                if (mine) YouMark.Add(row, "", 3);
                // The playoff line under the fourth.
                if (i == 3) UiKit.Box(box, "Cut", new Vector2(0.5f, 1), new Vector2(0, y - RowH / 2), new Vector2(width - 12, 3), UiKit.WithAlpha(UiKit.Gold, 0.7f));
            }
        }

        private static RectTransform Row(RectTransform parent, float y, float width, Color background, Color ink, int size, params (float w, string text)[] cols)
        {
            var row = UiKit.Box(parent, "Row", new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(width, RowH - 6), background);
            var x = -width / 2;
            foreach (var (w, text) in cols)
            {
                UiKit.LabelAt(row, Centre, new Vector2(x + w / 2, 0), new Vector2(w - 12, RowH - 10), text, size, w >= 300 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, ink);
                x += w;
            }
            return row;
        }

        /// <summary>Every fixture: results with the scores, then what's to come; yours outlined.</summary>
        private void Fixtures(RectTransform page, Tournament t)
        {
            UiKit.LabelAt(page, new Vector2(1, 1), new Vector2(-450, -175), new Vector2(820, 34), "FIXTURES & RESULTS", 24, TextAnchor.MiddleCenter, UiKit.Dim);
            var list = UiKit.ScrollList(page, new Vector2(1, 1), new Vector2(-450, -500), new Vector2(820, 600));
            var next = t.Next;
            foreach (var f in t.Fixtures)
            {
                var mine = f.Involves(t.UserTeam);
                var row = UiKit.ListRow(list, 74, f == next ? new Color(0.2f, 0.16f, 0.32f, 0.95f) : new Color(0.07f, 0.09f, 0.15f, 0.95f));
                var stage = f.League ? $"M{f.Number}" : f.Stage.ToUpperInvariant();
                UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(80, 14), new Vector2(140, 28), stage, 20, TextAnchor.MiddleCenter, f.League ? UiKit.Dim : UiKit.Gold);
                UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(80, -16), new Vector2(140, 24), Venue.Find(f.Venue)?.City ?? "", 16, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
                UiKit.Crest(row, f.A, new Vector2(0, 0.5f), new Vector2(190, 0), 46);
                UiKit.Crest(row, f.B, new Vector2(0, 0.5f), new Vector2(390, 0), 46);
                UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(290, 0), new Vector2(150, 34), $"{Team(f.A).ShortName}  v  {Team(f.B).ShortName}", 22, TextAnchor.MiddleCenter, mine ? YouMark.Lime : Color.white);
                var result = !f.Played ? (f == next ? "NEXT" : "")
                    : $"{Team(f.First!.Team).ShortName} {f.First}  ·  {Team(f.Second!.Team).ShortName} {f.Second}\n<color=#F2C24F>{Team(f.Winner).ShortName} won {f.Margin}</color>";
                var text = UiKit.LabelAt(row, new Vector2(1, 0.5f), new Vector2(-190, 0), new Vector2(360, 64), result, 19, TextAnchor.MiddleCenter, Color.white, bold: false);
                text.supportRichText = true;
                if (mine) YouMark.Add(row, "", 3);
            }
            // Scrolled to where the season is (auction-r1: after a few rounds NEXT sat below the fold): the last
            // result just above NEXT, or the playoffs once it's over. Rows 74 + 5 apart, 8 padding, 600 visible.
            var at = next != null ? t.Fixtures.IndexOf(next) - 1 : t.Fixtures.Count - 1;
            var max = Mathf.Max(0f, 16 + t.Fixtures.Count * 79 - 5 - 600);
            list.anchoredPosition = new Vector2(list.anchoredPosition.x, Mathf.Clamp(at * 79f, 0f, max));
        }

        // The bottom band (under the table and the fixtures, which end ~280 px up): a line about your next
        // match at y ~200, the action buttons in one row at y 80.
        private const float InfoY = 222, ButtonsY = 80;
        private static readonly Vector2 ButtonSize = new Vector2(430, 104);
        private static float ButtonX(int i) => -675 + i * 450;

        /// <summary>
        /// Bottom: your next match with PLAY MATCH / SIM MATCH / SIM TO MY NEXT MATCH / SIM REST OF SEASON
        /// (round auction-r1 #4: an auction-first game mustn't make you play every match; the in-match
        /// pause > SIMULATE stays too); waiting for a playoff draw; knocked out; or the champions.
        /// </summary>
        private void NextPanel(RectTransform page, Tournament t)
        {
            var bottom = new Vector2(0.5f, 0);
            if (t.Finished)
            {
                var champ = Team(t.Champion);
                var won = t.Champion == t.UserTeam;
                UiKit.Crest(page, champ.Id, bottom, new Vector2(-800, 140), 120);
                UiKit.LabelAt(page, bottom, new Vector2(-260, InfoY - 10), new Vector2(900, 56), won ? "CHAMPIONS! YOUR SIDE WON THE TOURNAMENT" : $"{champ.Name.ToUpperInvariant()} ARE THE CHAMPIONS", 36, TextAnchor.MiddleLeft, won ? YouMark.Lime : UiKit.Gold);
                UiKit.LabelAt(page, bottom, new Vector2(-260, InfoY - 52), new Vector2(900, 32), $"YOUR SIDE: {t.UserFinish()}", 24, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
                UiKit.Btn(page, "ChampView", bottom, new Vector2(ButtonX(2), ButtonsY), ButtonSize, "VIEW CHAMPIONS", UiKit.Muted, ShowChampions, 30);
                UiKit.Btn(page, "Again", bottom, new Vector2(ButtonX(3), ButtonsY), ButtonSize, "NEW TOURNAMENT ►", UiKit.Accent, ShowSetup, 30);
                return;
            }
            var mine = t.NextForUser;
            if (mine == null && !t.UserStillIn)
            {
                // Knocked out: the rest plays out without you, one match at a time or all at once.
                UiKit.LabelAt(page, bottom, new Vector2(-260, InfoY), new Vector2(1300, 50), "YOUR SIDE IS OUT · the rest of the playoffs are simulated", 30, TextAnchor.MiddleCenter, UiKit.Dim);
                UiKit.Btn(page, "SimNext", bottom, new Vector2(ButtonX(2), ButtonsY), ButtonSize, "SIM NEXT MATCH", UiKit.Muted, () => SimOne(t), 30);
                UiKit.Btn(page, "SimAll", bottom, new Vector2(ButtonX(3), ButtonsY), ButtonSize, "SIM REST OF SEASON ►", UiKit.Accent, () => SimRest(t, confirm: false), 30);
                return;
            }
            var waiting = mine == null;
            var others = waiting ? t.Fixtures.Count(f => !f.Played) : t.Fixtures.Count(f => !f.Played && f.Number < mine!.Number && !f.Involves(t.UserTeam));
            if (mine != null)
            {
                var opp = Team(mine.Other(t.UserTeam));
                var stage = mine.League ? $"MATCH {mine.Number} OF {t.LeagueCount}" : mine.Stage.ToUpperInvariant();
                UiKit.Crest(page, opp.Id, bottom, new Vector2(-860, InfoY - 16), 90);
                UiKit.LabelAt(page, bottom, new Vector2(-470, InfoY + 6), new Vector2(680, 44), $"NEXT: v {opp.Name.ToUpperInvariant()}", 32, TextAnchor.MiddleLeft, Color.white);
                UiKit.LabelAt(page, bottom, new Vector2(-470, InfoY - 34), new Vector2(680, 30),
                    $"{stage} · {Venue.Find(mine.Venue)?.Name ?? ""}" + (others > 0 ? $" · {others} other {(others == 1 ? "match" : "matches")} first" : ""), 22, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
            }
            else
            {
                // Lost Qualifier 1 (Qualifier 2 waits on the Eliminator), or won a playoff whose next match isn't drawn yet.
                var next = t.Next;
                UiKit.LabelAt(page, bottom, new Vector2(-470, InfoY + 6), new Vector2(760, 44), "YOU'RE STILL IN · WAITING FOR YOUR OPPONENT", 30, TextAnchor.MiddleLeft, YouMark.Lime);
                UiKit.LabelAt(page, bottom, new Vector2(-470, InfoY - 34), new Vector2(760, 30),
                    next != null ? $"{next.Stage.ToUpperInvariant()} first: {Team(next.A).ShortName} v {Team(next.B).ShortName}" : "", 22, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
            }
            // A ROOM auction's tournament (owner decision on #4): its players play their matches, no sims while
            // they're in; PLAY MATCH alone (it plays the others first, so it also gets a waiting side to its match).
            Debug.Log($"[APL tourney] sim buttons: {(t.FromRoom ? "hidden" : "shown")} ({(t.FromRoom ? "room" : "solo")})");
            if (t.FromRoom)
            {
                UiKit.LabelAt(page, bottom, new Vector2(480, InfoY - 14), new Vector2(880, 30),
                    "ROOM TOURNAMENT · you play your matches", 22, TextAnchor.MiddleRight, UiKit.Dim, bold: false);
                UiKit.Btn(page, "PlayNext", bottom, new Vector2(ButtonX(3) - 20, ButtonsY), new Vector2(470, ButtonSize.y), "PLAY MATCH ►", UiKit.Accent, () => Play(t), 40);
                return;
            }
            var squads = t.Source == "auction" ? "your auction squad" : "last season's squad";
            UiKit.LabelAt(page, bottom, new Vector2(480, InfoY - 14), new Vector2(880, 30),
                $"SIM: an instant result, your XI from {squads}", 22, TextAnchor.MiddleRight, UiKit.Dim, bold: false);

            // PLAY (the ground; the in-match pause > SIMULATE still works, Match/MatchHud.Sim.cs) and the three sims.
            var play = UiKit.Btn(page, "PlayNext", bottom, new Vector2(ButtonX(0), ButtonsY), ButtonSize, "PLAY MATCH ►", UiKit.Accent, () => Play(t), 36);
            var simMine = UiKit.Btn(page, "SimMatch", bottom, new Vector2(ButtonX(1), ButtonsY), ButtonSize, "SIM MATCH", UiKit.Gold, () => SimMine(t), 32);
            var simTo = UiKit.Btn(page, "SimToNext", bottom, new Vector2(ButtonX(2), ButtonsY), ButtonSize, "SIM TO MY NEXT MATCH", UiKit.Muted, () => SimToMine(t), 28);
            UiKit.Btn(page, "SimRest", bottom, new Vector2(ButtonX(3), ButtonsY), ButtonSize, "SIM REST OF SEASON", UiKit.Muted, () => SimRest(t, confirm: true), 28);
            simMine.GetComponentInChildren<Text>().color = UiKit.Ink;
            if (waiting)
            {
                // Nothing of yours to play until the draw: only the sims that get you there.
                play.interactable = false;
                simMine.interactable = false;
            }
            if (others == 0) simTo.interactable = false;
            foreach (var b in new[] { play, simMine, simTo })
                if (!b.interactable) b.GetComponent<Image>().color = new Color(0.16f, 0.18f, 0.24f, 0.6f);
        }

        /// <summary>The matches before yours are simulated, then yours opens on the ground (they bat first, you chase).</summary>
        private void Play(Tournament t)
        {
            t.SimulateUntilUser(_data);
            t.Save();
            var f = t.NextForUser;
            if (f == null)
            {
                ShowTable();
                return;
            }
            TournamentLaunch.Active = t;
            TournamentLaunch.Fixture = f.Number;
            MatchLaunch.MyTeam = t.UserTeam;
            MatchLaunch.Opponent = f.Other(t.UserTeam);
            MatchLaunch.Overs = t.Overs;
            MatchLaunch.VenueId = f.Venue;
            // The PLAYING XI first, 30 s to confirm (owner v26 #7: TournamentHub.Xi.cs), then the ground.
            PickXiThenPlay(t, f);
        }
    }
}
