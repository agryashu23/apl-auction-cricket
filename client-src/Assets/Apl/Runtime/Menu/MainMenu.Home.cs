using System;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>
    /// The home page, auction first (owner, 2026-11: APL is an auction game first): a header row
    /// (crest, side, LVL and XP on the left; record, locker and settings on the right:
    /// MainMenu.HomeTop.cs), then three aligned columns on one grid — THE AUCTION as the hero
    /// card on the left (solo against nine AI franchises: MEGA AUCTION / MINI AUCTION, your
    /// auction career under the title, OUR batter rendered in the user's kit), MULTIPLAYER
    /// (rooms: CREATE / JOIN) and AUCTION ► TOURNAMENT stacked in the middle with PRACTICE NETS /
    /// QUICK MATCH as a slim row under them (match play is the side option now), DAILY GOALS and
    /// CHALLENGES on the right (MainMenu.HomeTop.cs). The grid is laid out
    /// from the canvas's real size (AspectFit: 1080 tall, 1920 to ~2400 wide), so it fills
    /// a 16:9 screen and a 2.2:1 phone alike. Names kept for AutoShot: Mode_&lt;TITLE&gt; (the
    /// card's button is its child), Settings, Locker, Challenges, Resume.
    /// </summary>
    public sealed partial class MainMenu
    {
        private Image? _glow;

        /// <summary>The page grid: outer margin, gap between cards, header height.</summary>
        private const float HomeGap = 28f, HomeTopH = 168f, HomeBottom = 40f;

        /// <summary>The canvas size in reference pixels (AspectFit: never under 1920 × 1080 either way).</summary>
        internal static Vector2 PageSize()
        {
            // UI/UX dev (r1 #6): the -aplaspect forced shape from frame one (the window resize lands a frame late).
            var a = Apl.Client.Dev.AspectFlag.Aspect;
            return a >= 16f / 9f ? new Vector2(1080f * a, 1080f) : new Vector2(1920f, 1920f / a);
        }

        /// <summary>Side margin: a little more on wide phones (rounded corners, the camera cut-out).</summary>
        private static float Margin(Vector2 size) => size.x > 2000f ? 72f : 48f;

        /// <summary>The centre of a box given by its top-left corner, for a top-left anchor.</summary>
        private static Vector2 At(float x, float y, float w, float h) => new Vector2(x + w / 2f, -(y + h / 2f));

        private static readonly Vector2 TopLeft = new Vector2(0, 1);

        private void ShowTitle()
        {
            // The hall well dimmed behind the page: the cards carry the screen, not the banners.
            var page = NewPage(0.7f);
            Gradient(page, 0f, 1f, new Color(0.06f, 0.03f, 0.18f, 0.9f), new Color(0.02f, 0.02f, 0.08f, 0.82f));
            PlayerCardKit.Shape(page, "BloomA", CardSprites.Glow(60, 200), UiKit.WithAlpha(Brand, 0.2f), new Vector2(-620, -300), new Vector2(1300, 900));
            PlayerCardKit.Shape(page, "BloomB", CardSprites.Glow(60, 200), UiKit.WithAlpha(Cyan, 0.1f), new Vector2(760, 380), new Vector2(1200, 700));
            var (mine, opp) = MatchMenu.SavedTeams(_data);
            var team = System.Linq.Enumerable.FirstOrDefault(_data.Franchises, f => f.Id == mine) ?? _data.Franchises[0];

            var size = PageSize();
            var m = Margin(size);
            HomeTop(page, team, size, m);

            // Three columns: 42% / 27% / 31% of the width between the margins.
            var top = HomeTopH;
            var h = size.y - top - HomeBottom;
            var avail = size.x - 2 * m - 2 * HomeGap;
            var wa = Mathf.Round(avail * 0.42f);
            var wb = Mathf.Round(avail * 0.27f);
            var wc = avail - wa - wb;
            var xa = m;
            var xb = xa + wa + HomeGap;
            var xc = xb + wb + HomeGap;

            AuctionHero(page, team, xa, top, wa, h);
            Debug.Log($"[APL home] auction-first home {size.x:0}x{size.y:0}: THE AUCTION hero {wa:0} wide (MEGA / MINI), MULTIPLAYER + AUCTION ► TOURNAMENT {wb:0} wide, NETS | QUICK MATCH slim row");

            // Middle: MULTIPLAYER (rooms) and AUCTION ► TOURNAMENT, then NETS | QUICK MATCH as a slim row.
            const float slim = 150f;
            var big = (h - slim - 2 * HomeGap) / 2f;
            var online = ModeTile(page, "PLAY ONLINE", "Auction rooms with friends", MenuIcons.For("online"), new Color(0.35f, 0.55f, 1f), xb, top, wb, big,
                () => OpenOnline(null), "MULTIPLAYER");
            InnerPills(online, wb, ("RoomCreate", "CREATE", UiKit.Accent, () => OpenOnline("create")), ("RoomJoin", "JOIN", new Color(0.2f, 0.42f, 0.9f), () => OpenOnline("home")));
            var tourney = ModeTile(page, "TOURNAMENT", "Bid for your squad, then play the league with it", MenuIcons.For("trophy"), new Color(0.6f, 0.4f, 1f), xb, top + big + HomeGap, wb, big,
                OpenAuctionTournament, "AUCTION ► TOURNAMENT");
            // UI/UX dev (r1 §10): fill the card's empty middle with the AUCTION → XI → LEAGUE path (MainMenu.HomeAuctionCards.cs).
            TourneyFlow(tourney, wb, big);
            var half = (wb - HomeGap * 0.6f) / 2f;
            ModeTile(page, "PRACTICE NETS", "Bat or bowl", MenuIcons.For("nets"), new Color(0.2f, 0.75f, 0.9f), xb, top + 2 * (big + HomeGap), half, slim, ShowNets, "NETS");
            ModeTile(page, "QUICK MATCH", "Play a match", MenuIcons.For("bat"), Brand, xb + wb - half, top + 2 * (big + HomeGap), half, slim,
                () => { MatchPressed?.Invoke(); Destroy(gameObject); }, "MATCH");

            // Right: DAILY GOALS (auction goals now) over a slim MATCH CHALLENGES row level with NETS | MATCH.
            // UI/UX dev (r1 §10): the goals were all match goals on an auction-first home (MainMenu.HomeAuctionCards.cs).
            AuctionGoalsCard(page, xc, top, wc, h - slim - HomeGap);
            MatchChallengesRow(page, xc, top + h - slim, wc, slim);
        }

        /// <summary>
        /// A rounded card at (x, y) from the page's top left: a soft shadow, a thin rim in
        /// <paramref name="edge"/>, the face in <paramref name="fill"/> with a faint sheen on top.
        /// Returns the face (children placed from ITS top left).
        /// </summary>
        private static RectTransform HomeCard(RectTransform parent, string name, float x, float y, float w, float h, Color fill, Color edge)
        {
            var pos = At(x, y, w, h);
            PlayerCardKit.Shape(parent, name + "Shadow", CardSprites.Glow(26, 18), new Color(0, 0, 0, 0.45f), pos + new Vector2(0, -8), new Vector2(w + 30, h + 30), TopLeft);
            PlayerCardKit.Shape(parent, name + "Rim", CardSprites.Round(30), edge, pos, new Vector2(w + 4, h + 4), TopLeft);
            var face = PlayerCardKit.Shape(parent, name, CardSprites.Round(28), fill, pos, new Vector2(w, h), TopLeft);
            var rt = (RectTransform)face.transform;
            var sheen = PlayerCardKit.Shape(rt, "Sheen", CardSprites.FadeUp(), new Color(1, 1, 1, 0.045f), new Vector2(0, -h * 0.2f), new Vector2(w - 24, h * 0.4f), new Vector2(0.5f, 1));
            sheen.rectTransform.localRotation = Quaternion.Euler(180, 0, 0);
            return rt;
        }

        /// <summary>Text in a box at (x, y) from <paramref name="r"/>'s top left.</summary>
        private static Text TextAt(RectTransform r, float x, float y, float w, float h, string s, int size, TextAnchor align, Color colour, bool condensed = true, bool bold = true)
        {
            var t = PlayerCardKit.Text(r, TopLeft, TopLeft, new Vector2(x, -(y + h)), new Vector2(x + w, -y), s, size, align, colour, condensed, bold);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        /// <summary>A clickable card: a holder named Mode_&lt;title&gt; with the card's face (the button) in it, as AutoShot looks for.</summary>
        private static (RectTransform face, Button button) ModeCardFace(RectTransform page, string title, float x, float y, float w, float h, Color fill, Color edge)
        {
            var holder = UiKit.Stretch(page, "Mode_" + title);
            var face = HomeCard(holder, "Face", x, y, w, h, fill, edge);
            face.GetComponent<Image>().raycastTarget = true;
            var b = face.gameObject.AddComponent<Button>();
            var colours = b.colors;
            colours.pressedColor = new Color(0.8f, 0.8f, 0.9f);
            b.colors = colours;
            return (face, b);
        }

        /// <summary>
        /// THE AUCTION, the hero: the name big on the left, what it is under it, your auction career
        /// (RoomCareer: auctions, wins, best finish); our batter (the render, lit, in the side's kit) on
        /// the right in his team's glow over a faint gavel; MEGA AUCTION and MINI AUCTION across the
        /// bottom, each straight to the franchise pick with that format chosen. The card opens the pick.
        /// </summary>
        private void AuctionHero(RectTransform page, Franchise team, float x, float y, float w, float h)
        {
            var (face, button) = ModeCardFace(page, "THE AUCTION", x, y, w, h, new Color(0.18f, 0.08f, 0.22f, 0.97f), UiKit.WithAlpha(UiKit.Accent, 0.8f));
            // The card itself (not MEGA / MINI): the options page asks the format (solo-auction dev, r1 #10).
            button.onClick.AddListener(() => { _formatFromHome = false; ShowTeams(); });
            var wash = PlayerCardKit.Shape(face, "Wash", CardSprites.FadeUp(), UiKit.WithAlpha(UiKit.Accent, 0.2f), new Vector2(0, h * 0.3f), new Vector2(w - 8, h * 0.6f), new Vector2(0.5f, 0));
            wash.type = Image.Type.Simple;

            // The batter's stage: the right ~58% of the card, above the two format buttons.
            const float barH = 128f, pad = 40f;
            var stageX = w * 0.40f;
            var stageW = w - stageX - 16f;
            var stageY = 20f;
            var stageH = h - barH - pad - 30f - stageY;
            var colour = PlayerCardKit.Bright(UiKit.ParseHex(team.PrimaryColor));
            var centre = At(stageX, stageY, stageW, stageH);
            _glow = PlayerCardKit.Shape(face, "TeamGlow", CardSprites.Glow(60, 140), UiKit.WithAlpha(colour, 0.35f), centre, new Vector2(stageW * 1.05f, stageH * 1.05f), TopLeft);
            var ring = Mathf.Min(stageW, stageH) * 0.86f;
            PlayerCardKit.Shape(face, "Ring", CardSprites.Ring(0.018f), UiKit.WithAlpha(Color.white, 0.16f), centre + new Vector2(0, 10), new Vector2(ring, ring), TopLeft);
            // The gavel as a faint mark behind him: this card is the auction.
            var gs = Mathf.Min(stageW, stageH) * 0.5f;
            var gavel = PlayerCardKit.Shape(face, "Gavel", MenuIcons.For("gavel"), UiKit.WithAlpha(UiKit.Gold, 0.12f), At(w - gs - 24, 28, gs, gs), new Vector2(gs, gs), TopLeft);
            gavel.preserveAspect = true;
            var tex = Art("hero_bat", team.Id) ?? Art("mode_bat", team.Id);
            if (!HeroFigure(face, tex, stageX, stageY, stageW, stageH))
            {
                var xi = Apl.Core.Play.QuickMatch.LastSeasonXI(_data, team.Id).XI;
                Player? who = null;
                foreach (var p in xi) if (who == null || p.StarPower > who.StarPower) who = p;
                if (who != null) PlayerCardKit.Card(face, who, team, TopLeft, centre, new Vector2(240, 326)).Motion.FromAlpha = 1f;
            }

            // The words, over the art's left edge.
            var title = TextAt(face, 44, 36, w * 0.5f, 260, "THE\nAUCTION", 128, TextAnchor.UpperLeft, Color.white);
            title.fontStyle = FontStyle.Italic;
            title.lineSpacing = 0.82f;
            title.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3, -4);
            TextAt(face, 46, 306, w * 0.42f, 84, "Build your squad against\nnine AI franchises", 30, TextAnchor.UpperLeft, UiKit.Dim, condensed: false, bold: false);

            // Your auction career (solo and rooms, kept on this phone).
            var c = Online.RoomCareer.Load();
            var chipW = Mathf.Min(w * 0.5f, 470f);  // UI/UX dev r1: wider, so the line fits on one row
            var chip = PlayerCardKit.Shape(face, "Career", CardSprites.Round(20), new Color(0.04f, 0.03f, 0.12f, 0.75f), At(44, 410, chipW, 72), new Vector2(chipW, 72), TopLeft);
            var cr = (RectTransform)chip.transform;
            // UI/UX dev (r1 §10): "1 WINS" and "#1" wrapping — singular/plural, and one line that shrinks to fit.
            var careerText = c.Played == 0 ? "YOUR FIRST AUCTION AWAITS"
                : $"{c.Played} AUCTION{(c.Played == 1 ? "" : "S")}  ·  {c.Wins} WIN{(c.Wins == 1 ? "" : "S")}  ·  BEST #{c.BestRank}";
            // One text line tall (18 px insets): best fit shrinks the words instead of wrapping "#1" (UI/UX dev r1).
            var ct = PlayerCardKit.Text(cr, Vector2.zero, Vector2.one, new Vector2(14, 16), new Vector2(-14, -16), careerText, 30, TextAnchor.MiddleCenter, UiKit.Gold, condensed: true);
            ct.resizeTextForBestFit = true;
            ct.resizeTextMinSize = 16;
            ct.resizeTextMaxSize = 26;

            // MEGA | MINI across the bottom (MEGA pulses: Update).
            var bw = (w - 2 * pad - 24) / 2f;
            var mega = PanelKit.Pill(face, "SoloMega", TopLeft, At(pad, h - pad - barH, bw, barH), new Vector2(bw, barH), "MEGA AUCTION ►", new Color(1f, 0.78f, 0.2f), () => SoloAuction("mega"), 40);
            mega.GetComponentInChildren<Text>().fontStyle = FontStyle.Italic;
            var mini = PanelKit.Pill(face, "SoloMini", TopLeft, At(pad + bw + 24, h - pad - barH, bw, barH), new Vector2(bw, barH), "MINI AUCTION ►", UiKit.Accent, () => SoloAuction("standard"), 40);
            mini.GetComponentInChildren<Text>().fontStyle = FontStyle.Italic;
            _playButton = (RectTransform)mega.transform;
        }

        /// <summary>A solo auction in this format: straight to the franchise pick (the options page then has it chosen).</summary>
        private void SoloAuction(string formatId)
        {
            var f = System.Linq.Enumerable.FirstOrDefault(_data.Rules.FormatList, x => x.Id == formatId) ?? _data.Rules.FormatList[0];
            _setup.Format = f;
            _setup.Retention = f.RetentionOnly;
            // Chosen here: the setup doesn't ask the format again (MainMenu.ShowOptions / ShowTeams).
            _formatFromHome = true;
            _setup.Retained.Clear();
            Debug.Log($"[APL home] solo auction: {f.Id}");
            ShowTeams();
        }

        /// <summary>MULTIPLAYER: the online page, opened on CREATE (the new-room page) or its home page (join by code).</summary>
        private void OpenOnline(string? view)
        {
            OnlineLobby.StartView = view;
            Debug.Log($"[APL home] multiplayer: {view ?? "home"}");
            OnlinePressed?.Invoke();
            Destroy(gameObject);
        }

        /// <summary>AUCTION ► TOURNAMENT: the tournament hub with AUCTION and NEW AUCTION FIRST chosen.</summary>
        private void OpenAuctionTournament()
        {
            var (mine, _) = MatchMenu.SavedTeams(_data);
            Debug.Log("[APL home] auction then tournament");
            TournamentHub.Open(_data, mine, PlayerPrefs.GetInt(MatchMenu.OversKey, 5), auctionFirst: true);
        }

        /// <summary>Two small buttons along the top of a big mode card, right of its icon tile (in place of its ›).</summary>
        private static void InnerPills(RectTransform face, float w, params (string name, string label, Color fill, Action act)[] pills)
        {
            var arrow = face.Find("Arrow");
            if (arrow != null) arrow.gameObject.SetActive(false);
            const float x0 = 150f, gap = 14f, ph = 70f;
            var pw = (w - x0 - 28f - gap * (pills.Length - 1)) / pills.Length;
            for (var i = 0; i < pills.Length; i++)
            {
                var (name, label, fill, act) = pills[i];
                var x = x0 + i * (pw + gap);
                PanelKit.Pill(face, name, TopLeft, At(x, 45, pw, ph), new Vector2(pw, ph), label, fill, act, 30);
            }
        }

        /// <summary>
        /// The render in a stage box, kept to its own shape, a little larger than the box (the
        /// renders carry a clear margin round the figure, so the figure itself fits), with a
        /// soft shadow at his feet. False if there's no render.
        /// </summary>
        private static bool HeroFigure(RectTransform r, Texture2D? tex, float x, float y, float w, float h)
        {
            if (tex == null) return false;
            const float over = 1.28f;
            var k = Mathf.Min(w / tex.width, h / tex.height) * over;
            var fw = tex.width * k;
            var fh = tex.height * k;
            // Bottom of the texture a little under the stage (its lower ~20% is clear floor).
            var bottom = y + h + fh * 0.12f;
            var cx = x + w / 2f;
            // The renders stand his feet ~22% up the frame.
            var feet = bottom - fh * 0.22f;
            PlayerCardKit.Shape(r, "FeetShadow", CardSprites.Disc(), new Color(0, 0, 0, 0.4f), new Vector2(cx, -feet), new Vector2(fh * 0.34f, fh * 0.05f), TopLeft);
            var go = new GameObject("Figure", typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform)go.transform;
            rt.SetParent(r, false);
            rt.anchorMin = rt.anchorMax = TopLeft;
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(cx, -bottom);
            rt.sizeDelta = new Vector2(fw, fh);
            var raw = go.GetComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;
            return true;
        }

        /// <summary>
        /// A mode card (1.png's middle column): an icon tile top left, › top right, the icon
        /// again as a faint watermark, the name big at the bottom and a line under it. A slim
        /// one (under 200 tall) is a row: icon, name, line. <paramref name="shown"/>: a shorter name to
        /// show than the card's own (AutoShot finds it as Mode_&lt;title&gt;).
        /// </summary>
        private static RectTransform ModeTile(RectTransform page, string title, string sub, Sprite icon, Color tint, float x, float y, float w, float h, Action go, string? shown = null)
        {
            var (face, button) = ModeCardFace(page, title, x, y, w, h, new Color(0.08f, 0.07f, 0.2f, 0.97f), UiKit.WithAlpha(tint, 0.45f));
            button.onClick.AddListener(() => go());
            var tileFill = Color.Lerp(tint, new Color(0.08f, 0.07f, 0.2f), 0.62f);
            var bright = PlayerCardKit.Bright(tint);
            if (h < 200f)
            {
                const float t = 76f;
                var tile = PlayerCardKit.Shape(face, "IconTile", CardSprites.Round(20), tileFill, At(22, (h - t) / 2f, t, t), new Vector2(t, t), TopLeft);
                PlayerCardKit.Shape((RectTransform)tile.transform, "Icon", icon, bright, Vector2.zero, new Vector2(52, 52));
                var name = TextAt(face, 112, h / 2f - 46, w - 124, 50, shown ?? title, 36, TextAnchor.LowerLeft, Color.white);
                name.fontStyle = FontStyle.Italic;
                TextAt(face, 113, h / 2f + 8, w - 124, 34, sub, 21, TextAnchor.UpperLeft, UiKit.Dim, condensed: false, bold: false);
                return face;
            }
            var ms = h * 0.74f;
            var mark = PlayerCardKit.Shape(face, "Watermark", icon, UiKit.WithAlpha(bright, 0.07f), At(w - ms - 20, h * 0.2f, ms, ms), new Vector2(ms, ms), TopLeft);
            mark.preserveAspect = true;
            const float s = 96f;
            var big = PlayerCardKit.Shape(face, "IconTile", CardSprites.Round(24), tileFill, At(32, 32, s, s), new Vector2(s, s), TopLeft);
            PlayerCardKit.Shape((RectTransform)big.transform, "Icon", icon, bright, Vector2.zero, new Vector2(64, 64));
            TextAt(face, w - 80, 30, 50, 70, "›", 72, TextAnchor.MiddleCenter, UiKit.Dim).name = "Arrow";
            var label = TextAt(face, 32, h - 140, w - 64, 76, shown ?? title, 64, TextAnchor.LowerLeft, Color.white);
            label.fontStyle = FontStyle.Italic;
            // Long names (AUCTION ► TOURNAMENT) shrink to the card rather than wrap.
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 30;
            label.resizeTextMaxSize = 64;
            TextAt(face, 34, h - 62, w - 64, 36, sub, 26, TextAnchor.UpperLeft, UiKit.Dim, condensed: false, bold: false);
            return face;
        }
    }
}
