using System;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Apl.Client.Hall
{
    /// <summary>
    /// The overlay, laid out like the reference: balance, player card,
    /// current bid, bidding team badge, RTM indicator, timer, and the
    /// Skip / BID / HOLD buttons. Built in code so it needs no prefabs.
    /// </summary>
    // partial: the bid-cluster layout and the shared style pass live in HallHud.Layout.cs (UI/UX dev, r1).
    public sealed partial class HallHud : MonoBehaviour
    {
        private static readonly Color Panel = UiKit.Panel;
        private static readonly Color Gold = UiKit.Gold;
        private static readonly Color Accent = UiKit.Accent;

        private Text _balance = null!, _cardName = null!, _cardBase = null!;
        private Text _bidLabel = null!, _bidAmount = null!, _badgeTitle = null!;
        private Text _lot = null!, _timerText = null!, _rtm = null!, _bidButtonText = null!, _banner = null!, _bannerSub = null!;
        private Image _badge = null!, _timerFill = null!;
        private RectTransform _badgeRt = null!, _cardFace = null!;
        private GameObject _bannerRoot = null!, _rtmPrompt = null!;
        private Text _rtmPromptText = null!;
        private Text _balanceSub = null!;
        private Button _ffButton = null!;
        private Image _rtmBox = null!;
        private bool _rtmEligible;
        private GameObject _teamsTab = null!, _teamsPanel = null!;
        private Text _teamsTabText = null!;
        private bool _teamsOpen;
        private readonly System.Collections.Generic.Dictionary<string, (Image row, Text purse, Text squad)> _teamRows =
            new System.Collections.Generic.Dictionary<string, (Image, Text, Text)>();
        private Text[] _cardValues = Array.Empty<Text>();
        private Text _cardStats = null!;
        private RawImage _portrait = null!, _badgeCrest = null!;
        private Button _bidButton = null!, _skipButton = null!, _passButton = null!;

        // The SOLD / UNSOLD card.
        private Image _cardStripe = null!;
        private Text _cardTeam = null!, _cardPrice = null!, _cardPlayer = null!;
        private RawImage _cardPortrait = null!, _cardCrest = null!;
        private float _bannerAt = -1;

        public event Action? BidPressed;
        public event Action? SkipPressed;
        public event Action? PassPressed;
        public event Action? FastForwardPressed;
        public event Action<bool>? RtmAnswered;
        public event Action? CeremonySkipPressed;
        public event Action? RulesSkipPressed;
        public event Action? HubPressed;
        public event Action? PausePressed;

        /// <summary>SIMULATE (solo): finish the rest of the auction at once.</summary>
        public event Action? SimulatePressed;
        private Button _simulateButton = null!;

        private readonly System.Collections.Generic.List<GameObject> _auctionPanels = new System.Collections.Generic.List<GameObject>();
        private GameObject _ceremonySkip = null!;
        private GameObject _rulesRoot = null!;
        private Text _rulesText = null!;

        /// <summary>Ceremony on: hide the auction panels, show SKIP CEREMONY.</summary>
        public void ShowCeremony(bool on)
        {
            SetAuctionPanels(!on);
            _simulateButton.gameObject.SetActive(!on && _simulateAllowed);
            _ceremonySkip.SetActive(on);
        }

        /// <summary>
        /// The rules card before the first lot: rules up to <paramref name="current"/>
        /// are shown, the one being read is gold, later ones stay dim.
        /// Pass null to hide it.
        /// </summary>
        public void ShowRules(System.Collections.Generic.IReadOnlyList<string>? rules, int current = 0)
        {
            var on = rules != null;
            SetAuctionPanels(!on);
            _simulateButton.gameObject.SetActive(!on && _simulateAllowed);
            _rulesRoot.SetActive(on);
            if (rules == null) return;
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < rules.Count; i++)
            {
                var color = i == current ? "#F2C24F" : i < current ? "#FFFFFF" : "#5C6478";
                sb.Append("<color=").Append(color).Append(">•  ").Append(rules[i]).Append("</color>\n\n");
            }
            _rulesText.text = sb.ToString();
        }

        /// <summary>
        /// Buttons hidden for this auction (a room: FAST and SKIP; solo: nothing). The ceremony and
        /// rules cards bring the auction panels back, but never these (PM: FAST showed in rooms).
        /// </summary>
        private readonly System.Collections.Generic.HashSet<GameObject> _forcedOff = new System.Collections.Generic.HashSet<GameObject>();

        private void SetAuctionPanels(bool on)
        {
            foreach (var p in _auctionPanels) p.SetActive(on && !_forcedOff.Contains(p));
        }

        private void ForceOff(GameObject go, bool off)
        {
            if (off) _forcedOff.Add(go);
            else _forcedOff.Remove(go);
            if (off) go.SetActive(false);
        }

        public static HallHud Create()
        {
            var hud = UiKit.Canvas("Hud", 10).gameObject.AddComponent<HallHud>();
            hud.Build();
            return hud;
        }

        private void Build()
        {
            var root = (RectTransform)transform;

            // Top bar: lot + timer, RTM chip.
            _lot = Label(Box(root, "Lot", new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(760, 56), Panel), "", 26, TextAnchor.MiddleCenter);
            var timer = Box(root, "Timer", new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(96, 96), Panel);
            _timerFill = Fill(timer, Gold);
            _timerText = Label(timer, "", 40, TextAnchor.MiddleCenter);
            var rtmBox = Box(root, "Rtm", new Vector2(0, 1), new Vector2(170, -60), new Vector2(300, 70), new Color(0.12f, 0.55f, 0.56f, 0.92f));
            _rtmBox = rtmBox.GetComponent<Image>();
            _rtm = Label(rtmBox, "RTM CARDS  1/1", 24, TextAnchor.MiddleCenter);
            var tab = Button(root, "TeamsTab", new Vector2(0, 1), new Vector2(170, -134), new Vector2(300, 54), "TEAMS ▼", new Color(0.12f, 0.14f, 0.2f, 0.94f), ToggleTeams);
            _teamsTab = tab.gameObject;
            _teamsTabText = tab.GetComponentInChildren<Text>();
            _teamsTabText.fontSize = 26;

            // Bottom strip (reference layout): balance | player card | portrait over
            // current bid | bidding team.
            var bal = UiKit.EdgedPanel(root, "Balance", new Vector2(0.5f, 0), new Vector2(-770, 205), new Vector2(290, 124), Panel);
            UiKit.LabelAt(bal, new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(270, 28), "BALANCE", 20, TextAnchor.MiddleCenter, UiKit.Dim);
            _balance = UiKit.LabelAt(bal, new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(270, 48), "—", 40, TextAnchor.MiddleCenter);
            _balanceSub = UiKit.LabelAt(bal, new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(270, 28), "", 20, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);

            // The card is a row taller than its neighbours: name, skills, his last real season, base price.
            var card = UiKit.EdgedPanel(root, "Card", new Vector2(0.5f, 0), new Vector2(-355, 218), new Vector2(490, 176), Panel);
            var nameBar = UiKit.Box(card, "NameBar", new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(490, 48), new Color(0.1f, 0.13f, 0.22f, 1f));
            _cardName = Label(nameBar, "", 32, TextAnchor.MiddleCenter);
            string[] heads = { "BAT", "BOWL", "ORIGIN", "ROLE" };
            float[] widths = { 0.18f, 0.18f, 0.28f, 0.36f };
            _cardValues = new Text[heads.Length];
            var x0 = 0f;
            for (var i = 0; i < heads.Length; i++)
            {
                var col = new GameObject("Col_" + heads[i], typeof(RectTransform));
                var rt = (RectTransform)col.transform;
                rt.SetParent(card, false);
                rt.anchorMin = new Vector2(x0, 0.39f);
                rt.anchorMax = new Vector2(x0 + widths[i], 0.73f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                UiKit.Label(UiKit.Strip(rt, 0.52f, 1f), heads[i], 18, TextAnchor.MiddleCenter, UiKit.Dim);
                _cardValues[i] = UiKit.Label(UiKit.Strip(rt, 0f, 0.55f), "", 24, TextAnchor.MiddleCenter, new Color(0.38f, 0.86f, 0.96f));
                x0 += widths[i];
            }
            _cardStats = UiKit.Label(UiKit.Strip(card, 0.2f, 0.39f), "", 20, TextAnchor.MiddleCenter, new Color(0.95f, 0.82f, 0.45f));
            _cardBase = Label(UiKit.Strip(card, 0.01f, 0.21f), "", 20, TextAnchor.MiddleCenter);

            // UI/UX dev (r1 §5): a lit head-shot card, not the cartoon hexagon; placed by ApplyAuctionLayout.
            _portrait = UiKit.CardPortrait(root, "Portrait", new Vector2(0.5f, 0), new Vector2(40, 158), new Vector2(240, 280));
            var bid = UiKit.EdgedPanel(root, "CurrentBid", new Vector2(0.5f, 0), new Vector2(40, 158), new Vector2(270, 96), Accent, new Color(0.55f, 0.2f, 0.05f));
            _bidLabel = Label(UiKit.Strip(bid, 0.6f, 1f), "BASE PRICE", 22, TextAnchor.MiddleCenter);
            _bidAmount = Label(UiKit.Strip(bid, 0f, 0.62f), "", 42, TextAnchor.MiddleCenter);
            // The auctioneer's call over the CURRENT BID label (solo-auction dev, rater r1 #9): GOING ONCE… /
            // GOING TWICE… / SOLD!, set with his calls (HallDirector.CallGoing). No numeric countdown.
            var call = UiKit.Strip(bid, 0.58f, 1f);
            call.name = "CallChip";
            call.offsetMin = new Vector2(4, 0);
            call.offsetMax = new Vector2(-4, -4);
            _callChip = call.gameObject.AddComponent<Image>();
            _callChip.raycastTarget = false;
            _callText = Label(call, "", 24, TextAnchor.MiddleCenter);
            call.gameObject.SetActive(false);

            var badge = UiKit.EdgedPanel(root, "Bidder", new Vector2(0.5f, 0), new Vector2(390, 205), new Vector2(270, 150), new Color(0.16f, 0.36f, 0.66f, 0.95f));
            _badge = badge.GetComponent<Image>();
            _badgeRt = badge;
            _badgeCrest = UiKit.Crest(badge, "", new Vector2(0.5f, 1), new Vector2(0, -52), 84);
            _badgeTitle = Label(UiKit.Strip(badge, 0.02f, 0.3f), "", 26, TextAnchor.MiddleCenter);

            Button(root, "Pause", new Vector2(1, 1), new Vector2(-74, -58), new Vector2(104, 84), "II", new Color(0.12f, 0.14f, 0.2f, 0.96f), () => PausePressed?.Invoke());
            // SIMULATE (solo): top right beside pause, gold so it reads as the way out of a long auction.
            _simulateButton = Button(root, "Simulate", new Vector2(1, 1), new Vector2(-300, -58), new Vector2(320, 84), "SIMULATE ►►|", Gold, () => SimulatePressed?.Invoke());
            _simulateButton.GetComponentInChildren<Text>().color = UiKit.Ink;

            // Buttons: Auction Hub | Skip | Hold-to-fast-forward | Bid, with Pass above Bid.
            Button(root, "Hub", new Vector2(0.5f, 0), new Vector2(-770, 60), new Vector2(290, 80), "AUCTION HUB", new Color(0.12f, 0.14f, 0.2f, 0.96f), () => HubPressed?.Invoke());
            _passButton = Button(root, "Pass", new Vector2(0.5f, 0), new Vector2(660, 162), new Vector2(220, 74), "PASS", new Color(0.30f, 0.12f, 0.12f, 0.96f), () => PassPressed?.Invoke());
            _skipButton = Button(root, "Skip", new Vector2(0.5f, 0), new Vector2(-250, 60), new Vector2(260, 80), "SKIP", new Color(0.12f, 0.14f, 0.2f, 0.96f), () => SkipPressed?.Invoke());
            _ffButton = Button(root, "Hold", new Vector2(0.5f, 0), new Vector2(110, 60), new Vector2(230, 80), "►► FAST", new Color(0.12f, 0.14f, 0.2f, 0.96f), () => FastForwardPressed?.Invoke());
            _bidButton = Button(root, "Bid", new Vector2(0.5f, 0), new Vector2(560, 62), new Vector2(420, 92), "BID", Accent, () => BidPressed?.Invoke());
            _bidButtonText = _bidButton.GetComponentInChildren<Text>();

            foreach (var name in new[] { "Lot", "Timer", "Rtm", "Balance", "Card", "Portrait", "CurrentBid", "Bidder", "Hub", "Pass", "Skip", "Bid", "Hold", "Pause", "TeamsTab" })
            {
                _auctionPanels.Add(root.Find(name).gameObject);
            }
            _ceremonySkip = Button(root, "SkipCeremony", new Vector2(1, 0), new Vector2(-250, 80), new Vector2(420, 90), "SKIP CEREMONY ►", new Color(0.16f, 0.18f, 0.24f, 0.95f), () => CeremonySkipPressed?.Invoke()).gameObject;
            _ceremonySkip.SetActive(false);

            // Rules card, read out by the auctioneer before the first lot.
            var rules = Box(root, "Rules", new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(1360, 720), new Color(0.04f, 0.05f, 0.1f, 0.97f));
            _rulesRoot = rules.gameObject;
            Label(Strip(rules, 0.87f, 0.98f), "AUCTION RULES", 46, TextAnchor.MiddleCenter).color = Gold;
            _rulesText = Label(Strip(rules, 0.17f, 0.86f), "", 32, TextAnchor.UpperLeft);
            _rulesText.supportRichText = true;
            // Shrink to fit rather than lose the last rule (both 16:9 and ~2.2:1).
            _rulesText.resizeTextForBestFit = true;
            _rulesText.resizeTextMinSize = 20;
            _rulesText.resizeTextMaxSize = 32;
            _rulesText.fontStyle = FontStyle.Normal;
            _rulesText.rectTransform.offsetMin = new Vector2(70, 0);
            _rulesText.rectTransform.offsetMax = new Vector2(-70, 0);
            Button(rules, "SkipRules", new Vector2(0.5f, 0), new Vector2(0, 66), new Vector2(380, 84), "SKIP RULES ►", new Color(0.16f, 0.18f, 0.24f, 0.95f), () => RulesSkipPressed?.Invoke());
            _rulesRoot.SetActive(false);

            // SOLD / UNSOLD: a lower-third strip sitting just above the bottom HUD band, so the
            // faces in the hero shots (auctioneer, buyers' table) stay clear above it.
            var banner = UiKit.EdgedPanel(root, "Banner", new Vector2(0.5f, 0), new Vector2(0, 405), new Vector2(1180, 170), UiKit.PanelSolid, Gold);
            // EdgedPanel returns the face; show/hide (and pop) the whole card, edge included.
            _bannerRoot = banner.parent.gameObject;
            _cardFace = banner;
            var stripe = Box(banner, "Stripe", new Vector2(0, 0.5f), new Vector2(130, 0), new Vector2(260, 170), Gold);
            _cardStripe = stripe.GetComponent<Image>();
            _banner = Label(stripe, "", 54, TextAnchor.MiddleCenter);
            _cardPortrait = UiKit.CardPortrait(banner, "SoldPortrait", new Vector2(0, 0.5f), new Vector2(360, 0), new Vector2(132, 152));  // UI/UX dev r1 §5
            // Columns: stripe 0-260 | portrait 285-435 | name/team 455-815 | price 830-1030 | crest.
            _cardPlayer = UiKit.LabelAt(banner, new Vector2(0, 0.5f), new Vector2(635, 30), new Vector2(360, 52), "", 38, TextAnchor.MiddleLeft);
            _cardPlayer.resizeTextForBestFit = true;
            _cardPlayer.resizeTextMinSize = 20;
            _cardPlayer.resizeTextMaxSize = 38;
            _cardPlayer.horizontalOverflow = HorizontalWrapMode.Wrap;
            _cardTeam = UiKit.LabelAt(banner, new Vector2(0, 0.5f), new Vector2(635, -26), new Vector2(360, 38), "", 25, TextAnchor.MiddleLeft, UiKit.Dim);
            _cardTeam.resizeTextForBestFit = true;
            _cardTeam.resizeTextMinSize = 16;
            _cardTeam.resizeTextMaxSize = 25;
            _cardPrice = UiKit.LabelAt(banner, new Vector2(1, 0.5f), new Vector2(-250, 0), new Vector2(200, 84), "", 54, TextAnchor.MiddleCenter, Gold);
            _cardPrice.resizeTextForBestFit = true;
            _cardPrice.resizeTextMinSize = 30;
            _cardPrice.resizeTextMaxSize = 54;
            _cardCrest = UiKit.Crest(banner, "", new Vector2(1, 0.5f), new Vector2(-85, 0), 124);
            _bannerSub = _cardTeam;
            _bannerRoot.SetActive(false);

            // RTM prompt (for the human's own card).
            // A lower third like the SOLD strip, so the table deciding stays in view above it.
            var prompt = Box(root, "RtmPrompt", new Vector2(0.5f, 0), new Vector2(0, 405), new Vector2(1300, 170), new Color(0.05f, 0.07f, 0.13f, 0.97f));
            _rtmPrompt = prompt.gameObject;
            _rtmPromptText = UiKit.LabelAt(prompt, new Vector2(0, 0.5f), new Vector2(400, 0), new Vector2(760, 156), "", 28, TextAnchor.MiddleCenter);
            Button(prompt, "No", new Vector2(1, 0.5f), new Vector2(-400, 0), new Vector2(230, 90), "LET HIM GO", new Color(0.25f, 0.27f, 0.33f), () => RtmAnswered?.Invoke(false));
            Button(prompt, "Yes", new Vector2(1, 0.5f), new Vector2(-145, 0), new Vector2(230, 90), "USE RTM", Gold, () => RtmAnswered?.Invoke(true));
            _rtmPrompt.SetActive(false);

            // UI/UX dev (r1 §4 #3, §4 LOW see-through): one aligned bid cluster and opaque panels (HallHud.Layout.cs).
            ApplyAuctionLayout(root);
        }

        // --------------------------------------------------------------- updates

        public void ShowLot(Lot lot, string setLabel, int lotsLeft)
        {
            var p = lot.Player;
            _lot.text = $"LOT {lot.Number} · {setLabel.ToUpperInvariant()} · {lotsLeft} TO GO";
            _cardName.text = p.Name.ToUpperInvariant();
            _cardValues[0].text = p.Batting.ToString();
            _cardValues[1].text = p.Bowling.ToString();
            _cardValues[2].text = p.Overseas ? p.Country.ToUpperInvariant() : "INDIA";
            _cardValues[3].text = RoleLabel(p);
            _cardStats.text = StatLine(p);
            _cardBase.text = $"BASE PRICE   {Money.Format(p.BasePrice)}{(p.Capped ? "" : "   ·   UNCAPPED")}";
            _portrait.texture = Portraits.For(p);
            _bidLabel.text = "BASE PRICE";
            _bidAmount.text = Money.Format(p.BasePrice);
            _badgeTitle.text = "NO BIDS YET";
            _thinkingRestore = null;
            _badgeCrest.color = new Color(1, 1, 1, 0);
            _badge.color = new Color(0.13f, 0.16f, 0.26f, 1f);  // opaque (UI/UX dev r1)
            YouMark.Remove(_badgeRt);
            _bannerRoot.SetActive(false);
            SetBidPanels(true);
        }

        private void SetBidPanels(bool on)
        {
            var root = (RectTransform)transform;
            foreach (var name in new[] { "Portrait", "CurrentBid" })
            {
                var t = root.Find(name);
                if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
            }
        }

        /// <summary>His last real season for the card: "IPL 2026 · 16 M · 776 RUNS · SR 237" (batters),
        /// wickets and economy for bowlers, runs and wickets for all-rounders.</summary>
        public static string StatLine(Player p)
        {
            var r = p.Recent;
            if (r == null || r.Matches == 0) return "NO RECENT T20 RECORD";
            var head = $"{r.Label}  ·  {r.Matches} M";  // "IPL 2026", "T20s 2025-26" (not "T20S")
            return p.Role switch
            {
                Role.Bowler => $"{head}  ·  {Count(r.Wickets, "WKT")}  ·  ECON {r.Economy:0.0}",
                Role.AllRounder => r.Wickets > 0
                    ? $"{head}  ·  {Count(r.Runs, "RUN")}  ·  {Count(r.Wickets, "WKT")}"
                    : $"{head}  ·  {Count(r.Runs, "RUN")}  ·  SR {r.StrikeRate:0}",
                _ => $"{head}  ·  {Count(r.Runs, "RUN")}  ·  SR {r.StrikeRate:0}",
            };
        }

        private static string Count(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "S")}";

        public static string RoleLabel(Player p) => p.Role switch
        {
            Role.Batter => "BATTER",
            Role.Bowler => p.BowlType == BowlType.Spin ? "SPINNER" : "FAST BOWLER",
            Role.AllRounder => "ALL-ROUNDER",
            _ => "KEEPER",
        };

        private string? _thinkingRestore;

        /// <summary>A team talking it over on camera: the bidder badge names them (text only).</summary>
        public void ShowThinking(Franchise team)
        {
            _thinkingRestore ??= _badgeTitle.text;
            _badgeTitle.text = team.ShortName + " THINKING\u2026";
        }

        /// <summary>Back to the leader's line, if the badge is still on a thinking team.</summary>
        public void EndThinking()
        {
            if (_thinkingRestore == null) return;
            _badgeTitle.text = _thinkingRestore;
            _thinkingRestore = null;
        }

        public void ShowBid(int amount, Franchise team, bool mine)
        {
            _thinkingRestore = null;
            _bidLabel.text = "CURRENT BID";
            _bidAmount.text = Money.Format(amount);
            _badgeTitle.text = mine ? "YOUR BID" : "BID BY " + team.ShortName;
            var crest = Resources.Load<Texture2D>("Crests/" + team.Id);
            _badgeCrest.texture = crest;
            _badgeCrest.color = crest != null ? Color.white : new Color(1, 1, 1, 0);
            _badge.color = WithAlpha(ParseHex(team.PrimaryColor), 1f);  // opaque (UI/UX dev r1)
            // Your own bid wears your mark (the team colour alone could be anyone's).
            if (mine) YouMark.Add(_badgeRt, "YOU", 6);
            else YouMark.Remove(_badgeRt);
        }

        /// <summary>The SOLD / UNSOLD card; <paramref name="mine"/>: you bought him (the card wears your mark).</summary>
        public void ShowResult(LotResult r, Franchise? buyer, bool mine = false)
        {
            // The strip carries the portrait and price: the HUD's own portrait and bid panel step aside.
            SetBidPanels(false);
            if (mine && r.Sold) YouMark.Add(_cardFace, "YOURS", 8, tagSize: 30);
            else YouMark.Remove(_cardFace);
            _bannerRoot.SetActive(true);
            _bannerAt = Time.unscaledTime;
            _cardPortrait.texture = Portraits.For(r.Player);
            _cardPlayer.text = r.Player.Name.ToUpperInvariant();
            if (r.Sold && buyer != null)
            {
                var team = ParseHex(buyer.PrimaryColor);
                _cardStripe.color = team;
                _banner.text = r.ViaRtm ? "RTM!  SOLD" : "SOLD";
                _banner.color = team.grayscale > 0.6f ? UiKit.Ink : Color.white;
                _cardTeam.text = mine ? "TO YOU · " + buyer.Name.ToUpperInvariant() : "TO " + buyer.Name.ToUpperInvariant();
                _cardPrice.text = Money.Format(r.Price!.Value);
                var crest = Resources.Load<Texture2D>("Crests/" + buyer.Id);
                _cardCrest.texture = crest;
                _cardCrest.color = crest != null ? Color.white : new Color(1, 1, 1, 0);
                _bidLabel.text = "SOLD AT";
            }
            else
            {
                _cardStripe.color = new Color(0.38f, 0.40f, 0.46f);
                _banner.text = "UNSOLD";
                _banner.color = Color.white;
                _cardTeam.text = "NO BIDS";
                _cardPrice.text = "—";
                _cardCrest.color = new Color(1, 1, 1, 0);
            }
        }

        private void PopCard()
        {
            // The result card pops in.
            if (_bannerAt < 0) return;
            var k = Mathf.Clamp01((Time.unscaledTime - _bannerAt) / 0.28f);
            var s = k < 1f ? Mathf.LerpUnclamped(0.7f, 1f, 1f - Mathf.Pow(1f - k, 3f) * Mathf.Cos(k * 6f)) : 1f;
            _bannerRoot.transform.localScale = Vector3.one * s;
            if (k >= 1f) _bannerAt = -1;
        }

        /// <summary>The countdown box is off by request; the auctioneer's calls carry the clock.</summary>
        public bool TimerVisible = false;

        public void ShowTimer(int leftMs, int totalMs, bool visible)
        {
            visible &= TimerVisible;
            _timerFill.transform.parent.gameObject.SetActive(visible);
            if (!visible) return;
            _timerFill.fillAmount = totalMs == 0 ? 0 : leftMs / (float)totalMs;
            _timerText.text = Mathf.CeilToInt(leftMs / 1000f).ToString();
            _timerFill.color = _timerFill.fillAmount > 0.5f ? new Color(0.3f, 0.85f, 0.5f) : _timerFill.fillAmount > 0.25f ? Gold : new Color(0.92f, 0.32f, 0.32f);
        }

        public void ShowMine(TeamState me, int maxSquad, int maxOverseas)
        {
            _balance.text = Money.Format(me.Purse);
            _balanceSub.text = $"{me.Size}/{maxSquad} PLAYERS · OS {me.OverseasCount}/{maxOverseas}";
        }

        /// <summary>The expandable purse list under the TEAMS tab (built on first open).</summary>
        public void ShowTeams(Dataset data, AuctionEngine engine, string me, string? leaderId)
        {
            var visible = _teamsOpen && _teamsTab.activeSelf;
            if (_teamsPanel == null)
            {
                if (!visible) return;
                BuildTeamsPanel(data, me);
            }
            _teamsPanel.SetActive(visible);
            if (!visible) return;
            foreach (var kv in _teamRows)
            {
                if (!engine.Teams.TryGetValue(kv.Key, out var t)) continue;
                var (row, purse, squad) = kv.Value;
                purse.text = Money.Format(t.Purse);
                squad.text = $"{t.Size}/{engine.Format.MaxSquad} · OS {t.OverseasCount}";
                // The bidder lit in his own colour; you are the outlined row (see BuildTeamsPanel).
                row.color = kv.Key == leaderId && data.Franchises.Find(f => f.Id == kv.Key) is Franchise lead
                    ? WithAlpha(ParseHex(lead.PrimaryColor), 0.55f)
                    : new Color(1, 1, 1, 0.04f);
            }
        }

        private void ToggleTeams()
        {
            _teamsOpen = !_teamsOpen;
            _teamsTabText.text = _teamsOpen ? "TEAMS ▲" : "TEAMS ▼";
            if (_teamsPanel != null) _teamsPanel.SetActive(_teamsOpen);
        }

        private void BuildTeamsPanel(Dataset data, string me)
        {
            var root = (RectTransform)transform;
            const float rowH = 44, w = 460;
            var h = 16 + data.Franchises.Count * rowH;
            var panel = Box(root, "TeamsPanel", new Vector2(0, 1), new Vector2(20 + w / 2, -168 - h / 2), new Vector2(w, h), new Color(0.04f, 0.05f, 0.1f, 0.92f));
            _teamsPanel = panel.gameObject;
            for (var i = 0; i < data.Franchises.Count; i++)
            {
                var f = data.Franchises[i];
                var row = Box(panel, "Row_" + f.Id, new Vector2(0.5f, 1), new Vector2(0, -8 - rowH / 2 - i * rowH), new Vector2(w - 12, rowH - 4), new Color(1, 1, 1, 0.04f));
                UiKit.Crest(row, f.Id, new Vector2(0, 0.5f), new Vector2(24, 0), 34);
                UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(48 + 45, 0), new Vector2(90, 36), f.ShortName, 22, TextAnchor.MiddleLeft, f.Id == me ? YouMark.Lime : Color.white);
                if (f.Id == me) YouMark.Add(row, "", 2);
                var purse = UiKit.LabelAt(row, new Vector2(0, 0.5f), new Vector2(140 + 70, 0), new Vector2(140, 36), "", 22, TextAnchor.MiddleRight);
                var squad = UiKit.LabelAt(row, new Vector2(1, 0.5f), new Vector2(-80, 0), new Vector2(150, 36), "", 18, TextAnchor.MiddleRight, UiKit.Dim, bold: false);
                _teamRows[f.Id] = (row.GetComponent<Image>(), purse, squad);
            }
        }

        /// <summary>Cards left; when the player on the block is one we can match, the chip says so.</summary>
        public void ShowRtm(int left, int total, bool eligible)
        {
            _rtmEligible = eligible;
            _rtm.text = eligible ? $"RIGHT TO MATCH\nAVAILABLE · {left}/{total}" : $"RTM CARDS  {left}/{total}";
            _rtm.fontSize = eligible ? 22 : 24;
            if (!eligible)
            {
                _rtmBox.color = left > 0 ? new Color(0.1f, 0.3f, 0.32f, 0.9f) : new Color(0.15f, 0.15f, 0.18f, 0.85f);
                _rtmBox.transform.localScale = Vector3.one;
            }
        }

        private Image _callChip = null!;
        private Text _callText = null!;
        private float _callAt = -1;

        /// <summary>
        /// The call chip on the CURRENT BID box: "GOING ONCE…" (gold), "GOING TWICE…" (orange),
        /// "SOLD!" (green) / "UNSOLD" (grey), "RIGHT TO MATCH?" (teal); null hides it.
        /// It pops in and pulses so the hammer's approach reads with the voice muted.
        /// </summary>
        public void ShowCall(string? call)
        {
            var go = _callChip.gameObject;
            if (call == null)
            {
                if (go.activeSelf) go.SetActive(false);
                _callAt = -1;
                return;
            }
            if (go.activeSelf && _callText.text == call) return;
            _callText.text = call;
            _callChip.color = call.StartsWith("GOING ONCE") ? Gold
                : call.StartsWith("GOING TWICE") ? new Color(0.95f, 0.45f, 0.12f, 1f)
                : call.StartsWith("SOLD") ? new Color(0.22f, 0.7f, 0.36f, 1f)
                : call.StartsWith("RIGHT") ? new Color(0.12f, 0.6f, 0.6f, 1f)
                : new Color(0.38f, 0.40f, 0.46f, 1f);
            _callText.color = call.StartsWith("GOING ONCE") ? UiKit.Ink : Color.white;
            go.SetActive(true);
            _callAt = Time.unscaledTime;
            Debug.Log($"[APL hud] call chip: {call}");
        }

        private void PulseCall()
        {
            if (_callAt < 0) return;
            var e = Time.unscaledTime - _callAt;
            // Pop in over 0.2 s, then a gentle pulse while the call stands.
            var k = e < 0.2f ? Mathf.Lerp(1.35f, 1f, e / 0.2f) : 1f + 0.05f * Mathf.Sin(e * 9f);
            _callChip.transform.localScale = new Vector3(k, k, 1);
        }

        private void Update()
        {
            PopCard();
            PulseCall();
            if (!_rtmEligible) return;
            var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            _rtmBox.color = Color.Lerp(new Color(0.12f, 0.55f, 0.56f, 0.95f), new Color(0.2f, 0.85f, 0.8f, 1f), pulse);
            var k = 1f + 0.04f * pulse;
            _rtmBox.transform.localScale = new Vector3(k, k, 1);
        }

        /// <summary>FAST is a toggle (tap on, tap off); hidden where the server sets the pace.</summary>
        public void SetFastForward(bool on, bool available)
        {
            ForceOff(_ffButton.gameObject, !available);
            _ffButton.gameObject.SetActive(available);
            _ffButton.GetComponent<Image>().color = on ? Gold : new Color(0.12f, 0.14f, 0.2f, 0.96f);
            var t = _ffButton.GetComponentInChildren<Text>();
            t.text = on ? "►► FAST: ON" : "►► FAST";
            t.color = on ? UiKit.Ink : Color.white;
        }

        public void SetBidButton(string label, bool enabled)
        {
            // UI/UX dev (r1 §4): PASSED shows once (on PASS); BID then says what happens next, greyed.
            if (_passedShown && label == "PASSED") label = "OUT OF THIS LOT";
            _bidButtonText.text = label;
            _bidButton.interactable = enabled;
            StyleActionButtons();
        }

        public void SetSkipEnabled(bool enabled)
        {
            _skipButton.interactable = enabled;
            _passButton.interactable = enabled;
        }

        // ------------------------------------------------------------ room seat

        /// <summary>TAKE BACK CONTROL (room): the bot stops bidding for you.</summary>
        public event Action? TakeBackPressed;
        private GameObject? _botPanel;

        /// <summary>The HUD's root, for the room's own panels (host panel, seat strip) to hang off.</summary>
        public RectTransform Root => (RectTransform)transform;

        /// <summary>
        /// Room: a bot is bidding for you (SIMULATE, or after a drop-out). BID and PASS give way to
        /// "BOT IS BIDDING FOR YOU" and TAKE BACK CONTROL in the same spot; SIMULATE hides.
        /// </summary>
        public void ShowBotControl(bool on)
        {
            if (_botPanel == null)
            {
                if (!on) return;
                var root = (RectTransform)transform;
                var panel = UiKit.EdgedPanel(root, "BotControl", new Vector2(0.5f, 0), new Vector2(560, 112), new Vector2(460, 196), UiKit.PanelSolid, Gold);
                _botPanel = panel.parent.gameObject;
                UiKit.LabelAt(panel, new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(440, 40), "BOT IS BIDDING FOR YOU", 28, TextAnchor.MiddleCenter, Gold);
                UiKit.LabelAt(panel, new Vector2(0.5f, 1), new Vector2(0, -68), new Vector2(440, 28), "It plays your team until you take over.", 19, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
                Button(panel, "TakeBack", new Vector2(0.5f, 0), new Vector2(0, 52), new Vector2(400, 80), "TAKE BACK CONTROL", Accent, () => TakeBackPressed?.Invoke());
            }
            if (_botPanel.activeSelf != on) _botPanel.SetActive(on);
            foreach (var b in new[] { _bidButton.gameObject, _passButton.gameObject })
            {
                var show = !on && !_forcedOff.Contains(b);
                if (b.activeSelf != show) b.SetActive(show);
            }
            if (on && _simulateButton.gameObject.activeSelf) _simulateButton.gameObject.SetActive(false);
            else if (!on && _simulateAllowed && !_simulateButton.gameObject.activeSelf && _bidButton.gameObject.activeInHierarchy) _simulateButton.gameObject.SetActive(true);
        }

        /// <summary>SIMULATE is solo only (a room's server sets the pace); hidden once the auction is over.</summary>
        public void ShowSimulate(bool on)
        {
            _simulateAllowed = on;
            if (_simulateButton.gameObject.activeSelf != on) _simulateButton.gameObject.SetActive(on);
        }

        private bool _simulateAllowed = true;

        /// <summary>PASS is solo only: in a room the SKIP button already passes.</summary>
        public void ShowPass(bool on)
        {
            ForceOff(_passButton.gameObject, !on);
            _passButton.gameObject.SetActive(on);
        }

        /// <summary>SKIP is solo only: hidden (not disabled) in a room.</summary>
        public void ShowSkip(bool on)
        {
            ForceOff(_skipButton.gameObject, !on);
            _skipButton.gameObject.SetActive(on);
        }

        public void SetPassLabel(string label)
        {
            _passButton.GetComponentInChildren<Text>().text = label;
            // UI/UX dev (r1 §4): remember PASSED so BID doesn't repeat it (SetBidButton).
            _passedShown = label == "PASSED";
            if (_passedShown && _bidButtonText.text == "PASSED") _bidButtonText.text = "OUT OF THIS LOT";
            StyleActionButtons();
        }

        public void SetSkipLabel(string label) => _skipButton.GetComponentInChildren<Text>().text = label;

        public void ShowRtmPrompt(string text, bool on)
        {
            _rtmPrompt.SetActive(on);
            if (on) _rtmPromptText.text = text;
        }

        // --------------------------------------------------------------- helpers

        private static Color WithAlpha(Color c, float a) => UiKit.WithAlpha(c, a);

        public static Color ParseHex(string hex) => UiKit.ParseHex(hex);

        private static RectTransform Box(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color) =>
            UiKit.Box(parent, name, anchor, pos, size, color);

        private static RectTransform Strip(RectTransform parent, float yMin, float yMax) => UiKit.Strip(parent, yMin, yMax);

        private static Text Label(RectTransform parent, string text, int size, TextAnchor anchor) => UiKit.Label(parent, text, size, anchor);

        private static Image Fill(RectTransform parent, Color color) => UiKit.Fill(parent, color);

        private static Button Button(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, string label, Color color, Action? onClick) =>
            UiKit.Btn(parent, name, anchor, pos, size, label, color, onClick);

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action) => UiKit.AddTrigger(trigger, type, action);
    }
}
