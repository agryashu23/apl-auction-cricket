using System;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Menu
{
    /// <summary>What the player picked before the auction.</summary>
    public sealed class GameSetup
    {
        public Franchise Team = null!;
        public AuctionFormat Format = null!;
        public bool Retention;

        /// <summary>The human's keeps (retention auctions only).</summary>
        public List<Player> Retained = new List<Player>();
    }

    /// <summary>
    /// Pre-game flow over the live hall: title → franchise → format and
    /// auction type → (retention picks) → start.
    /// </summary>
    public sealed partial class MainMenu : MonoBehaviour
    {
        /// <summary>Candidates shown on the retention page (best first).</summary>

        public event Action<GameSetup>? StartPressed;

        /// <summary>Carry on the saved solo auction instead.</summary>
        public event Action<SoloSave>? ResumePressed;
        public event Action? OnlinePressed;

        /// <summary>Quick match: pick two teams, bat the chase.</summary>
        public event Action? MatchPressed;

        private Dataset _data = null!;
        private RectTransform _root = null!;
        private RectTransform _page = null!;
        private readonly GameSetup _setup = new GameSetup();

        /// <summary>Back from a tournament match: the tournament hub again.</summary>
        private void Start()
        {
            // Back from a challenge's CHALLENGES button: the challenges page again (audio-modes: Modes/ModesEntry.cs).
            Apl.Client.Modes.ModesEntry.ReturnIfAsked(_data);
            // Back from a tournament match: straight to its table, over the home page.
            if (!TournamentLaunch.ReturnToHub) return;
            TournamentLaunch.ReturnToHub = false;
            OpenTournament();
        }

        public static MainMenu Create(Dataset data)
        {
            var canvas = UiKit.Canvas("Menu", 20);
            var menu = canvas.gameObject.AddComponent<MainMenu>();
            menu._data = data;
            menu._root = (RectTransform)canvas.transform;
            menu._setup.Format = data.Rules.FormatList.FirstOrDefault(f => f.Id == "standard") ?? data.Rules.FormatList[0];
            menu.ShowTitle();
            return menu;
        }

        private RectTransform NewPage(float dim)
        {
            if (_page != null) Destroy(_page.gameObject);
            _playButton = null;
            _ticker = null;
            _page = UiKit.Stretch(_root, "Page", new Color(0.02f, 0.03f, 0.07f, dim));
            return _page;
        }

        private Text Header(RectTransform page, string text, string sub = "")
        {
            var title = UiKit.LabelAt(page, new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(1400, 80), text, 56, TextAnchor.MiddleCenter, UiKit.Gold);
            if (sub.Length > 0) UiKit.LabelAt(page, new Vector2(0.5f, 1), new Vector2(0, -140), new Vector2(1400, 44), sub, 28, TextAnchor.MiddleCenter, UiKit.Dim, bold: false);
            return title;
        }

        private void Back(RectTransform page, Action action) =>
            UiKit.Btn(page, "Back", new Vector2(0, 1), new Vector2(150, -80), new Vector2(200, 80), "◄ BACK", UiKit.Muted, action, 30);

        // ------------------------------------------------------------- title

        private RectTransform? _playButton;
        private RectTransform? _ticker;

        private void Update()
        {
            // UI/UX dev (r1 see-through audit): each new page's faces made opaque once (MainMenu.HomeAuctionCards.cs).
            SolidifyPage();
            if (_glow != null)
            {
                // The team colour behind MY TEAM breathes.
                var k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 1.4f);
                var c = _glow.color;
                c.a = 0.25f + 0.2f * k;
                _glow.color = c;
                _glow.rectTransform.localScale = Vector3.one * (1f + 0.04f * k);
            }
            if (_playButton != null)
            {
                var pulse = 1f + 0.035f * Mathf.Sin(Time.unscaledTime * 3.2f);
                _playButton.localScale = new Vector3(pulse, pulse, 1);
            }
            if (_ticker != null)
            {
                // One franchise cycle is 10 × 133 px; wrap seamlessly after it.
                var span = _data.Franchises.Count * 133f;
                var x = -(Time.unscaledTime * 45f % span);
                _ticker.anchoredPosition = new Vector2(x, 0);
            }
        }

        /// <summary>A vertical colour ramp across part of the screen (yMin..yMax, bottom→top colours).</summary>
        private static void Gradient(RectTransform page, float yMin, float yMax, Color bottom, Color top)
        {
            var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < 64; y++) tex.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
            tex.Apply();
            var go = new GameObject("Gradient", typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform)go.transform;
            rt.SetParent(page, false);
            rt.anchorMin = new Vector2(0, yMin);
            rt.anchorMax = new Vector2(1, yMax);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
        }

        // ------------------------------------------------------------- teams

        private void ShowTeams()
        {
            var page = NewPage(0.55f);
            Header(page, "PICK YOUR FRANCHISE", "You sit at the front table. Nine AI franchises bid against you.");
            Back(page, ShowTitle);
            const float w = 330, h = 330, gap = 24;
            for (var i = 0; i < _data.Franchises.Count; i++)
            {
                var f = _data.Franchises[i];
                var col = i % 5;
                var row = i / 5;
                var pos = new Vector2((col - 2) * (w + gap), 130 - row * (h + gap) - 60);
                var color = UiKit.WithAlpha(UiKit.ParseHex(f.PrimaryColor), 0.9f);
                var tile = UiKit.Box(page, "Team_" + f.Id, new Vector2(0.5f, 0.5f), pos, new Vector2(w, h), color);
                var button = tile.gameObject.AddComponent<Button>();
                var team = f;
                button.onClick.AddListener(() =>
                {
                    _setup.Team = team;
                    _setup.Retained.Clear();
                    // MINI AUCTION from the home page: nothing left to choose (it always starts from
                    // retention), so straight to the keeps (rater r1 #10: the format was asked twice).
                    if (_formatFromHome && _setup.Format.RetentionOnly)
                    {
                        _setup.Retention = true;
                        AskResume(ShowRetention);
                    }
                    else ShowOptions();
                });
                UiKit.Crest(tile, f.Id, new Vector2(0.5f, 1), new Vector2(0, -110), 190);
                var light = color.grayscale > 0.55f;
                UiKit.LabelAt(tile, new Vector2(0.5f, 0), new Vector2(0, 74), new Vector2(w - 20, 44), f.ShortName, 40, TextAnchor.MiddleCenter, light ? UiKit.Ink : Color.white);
                UiKit.LabelAt(tile, new Vector2(0.5f, 0), new Vector2(0, 32), new Vector2(w - 20, 36), f.Name, 26, TextAnchor.MiddleCenter, light ? UiKit.Ink : Color.white, bold: false);
            }
        }

        // ----------------------------------------------------------- options

        private void ShowOptions()
        {
            var page = NewPage(0.6f);
            Header(page, _setup.Team.Name.ToUpperInvariant(), "Choose how big the auction is and how it starts.");
            Back(page, ShowTeams);
            UiKit.Crest(page, _setup.Team.Id, new Vector2(1, 1), new Vector2(-130, -110), 150);

            UiKit.LabelAt(page, new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(1400, 44), "FORMAT", 30, TextAnchor.MiddleCenter, UiKit.Dim);
            var formats = _data.Rules.FormatList;
            if (_formatFromHome)
            {
                // Picked on the home page (MEGA AUCTION ►): shown, not asked again; CHANGE reopens the choice.
                UiKit.EdgedPanel(page, "FormatChosen", new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(900, 130), UiKit.PanelSolid, UiKit.Gold);
                UiKit.LabelAt(page, new Vector2(0.5f, 0.5f), new Vector2(-60, 150), new Vector2(700, 120),
                    $"{FormatName(_setup.Format)}\n<size=24>{FormatBlurb(_data, _setup.Format)}</size>", 40, TextAnchor.MiddleCenter, UiKit.Gold).supportRichText = true;
                UiKit.Btn(page, "FormatChange", new Vector2(0.5f, 0.5f), new Vector2(345, 150), new Vector2(170, 70), "CHANGE", UiKit.Muted, () =>
                {
                    _formatFromHome = false;
                    ShowOptions();
                }, 24);
            }
            else
            {
                for (var i = 0; i < formats.Count; i++)
                {
                    var f = formats[i];
                    var selected = f.Id == _setup.Format.Id;
                    var x = (i - (formats.Count - 1) / 2f) * 470;
                    var b = UiKit.Btn(page, "Format_" + f.Id, new Vector2(0.5f, 0.5f), new Vector2(x, 150), new Vector2(440, 130),
                        "", selected ? UiKit.Gold : UiKit.Muted, () => SelectFormat(f));
                    var text = b.GetComponentInChildren<Text>();
                    text.text = $"{FormatName(f)}\n<size=24>{FormatBlurb(_data, f)}</size>";
                    text.supportRichText = true;
                }
            }

            UiKit.LabelAt(page, new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(1400, 44), "AUCTION", 30, TextAnchor.MiddleCenter, UiKit.Dim);
            var keep = _data.Rules.RetentionFor(_setup.Format).MaxPlayers;
            if (_setup.Format.RetentionOnly)
            {
                // A mini auction always starts from last season's squads.
                _setup.Retention = true;
                // On a panel, so it reads over the bright hall behind the menu.
                UiKit.EdgedPanel(page, "MiniAuction", new Vector2(0.5f, 0.5f), new Vector2(0, -80), new Vector2(1360, 140), UiKit.PanelSolid, UiKit.Gold);
                UiKit.LabelAt(page, new Vector2(0.5f, 0.5f), new Vector2(0, -80), new Vector2(1300, 110),
                    $"MINI AUCTION\n<size=26>Keep up to {keep} of last season's squad at their current price, then fill the gaps.</size>",
                    38, TextAnchor.MiddleCenter, UiKit.Gold).supportRichText = true;
            }
            else
            {
                TypeButton(page, new Vector2(-240, -80), false, "FRESH AUCTION", "Every player goes under the hammer");
                TypeButton(page, new Vector2(240, -80), true, "RETENTION", $"Keep up to {keep} of last season's squad");
            }

            var next = _setup.Retention ? "PICK RETENTIONS ►" : "START AUCTION ►";
            UiKit.Btn(page, "Next", new Vector2(0.5f, 0), new Vector2(0, 110), new Vector2(620, 110), next, UiKit.Accent, () => AskResume(() =>
            {
                if (_setup.Retention) ShowRetention();
                else Begin();
            }), 40);
        }

        /// <summary>
        /// A solo auction was left unfinished: RESUME it, or START NEW (which
        /// throws the save away) and carry on with <paramref name="startNew"/>.
        /// </summary>
        private void AskResume(Action startNew)
        {
            var save = SoloSave.Load(_data);
            if (save == null)
            {
                startNew();
                return;
            }
            var overlay = UiKit.Stretch(_page, "ResumePrompt", new Color(0.02f, 0.03f, 0.07f, 0.88f));
            overlay.gameObject.AddComponent<Button>(); // swallow taps on the page behind
            var panel = UiKit.EdgedPanel(overlay, "ResumePanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980, 470), UiKit.PanelSolid, UiKit.Gold);
            var team = _data.Franchises.FirstOrDefault(f => f.Id == save.TeamId);
            var format = _data.Rules.FormatList.First(f => f.Id == save.Setup.FormatId);
            var same = save.TeamId == _setup.Team.Id && save.Setup.FormatId == _setup.Format.Id;
            UiKit.LabelAt(panel, new Vector2(0.5f, 1), new Vector2(0, -64), new Vector2(900, 64), "UNFINISHED AUCTION", 50, TextAnchor.MiddleCenter, UiKit.Gold);
            if (team != null) UiKit.Crest(panel, team.Id, new Vector2(0.5f, 1), new Vector2(-330, -180), 120);
            UiKit.LabelAt(panel, new Vector2(0.5f, 1), new Vector2(70, -160), new Vector2(700, 48),
                $"{team?.Name.ToUpperInvariant() ?? save.TeamId} · {FormatName(format)}", 36, TextAnchor.MiddleLeft);
            UiKit.LabelAt(panel, new Vector2(0.5f, 1), new Vector2(70, -205), new Vector2(700, 36),
                $"{save.LotsDone} {(save.LotsDone == 1 ? "lot" : "lots")} done · saved {Ago(save.SavedAt)}", 26, TextAnchor.MiddleLeft, UiKit.Dim, bold: false);
            UiKit.LabelAt(panel, new Vector2(0.5f, 1), new Vector2(0, -275), new Vector2(880, 60),
                same ? "Pick up where you left off, or start this auction again."
                     : "That's a different auction from the one you just picked. Starting new replaces it.",
                24, TextAnchor.MiddleCenter, Color.white, bold: false);
            UiKit.Btn(panel, "StartNew", new Vector2(0.5f, 0), new Vector2(-230, 80), new Vector2(400, 100), "START NEW", UiKit.Muted, () =>
            {
                SoloSave.Delete();
                Destroy(overlay.gameObject);
                startNew();
            }, 34);
            UiKit.Btn(panel, "Resume", new Vector2(0.5f, 0), new Vector2(230, 80), new Vector2(400, 100), "RESUME ►", UiKit.Accent, () =>
            {
                ResumePressed?.Invoke(save);
                Destroy(gameObject);
            }, 38);
        }

        private static string Ago(DateTime utc)
        {
            var span = DateTime.UtcNow - utc;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours} h ago";
            return $"{(int)span.TotalDays} days ago";
        }

        private void SelectFormat(AuctionFormat f)
        {
            if (f.Id != _setup.Format.Id) _setup.Retained.Clear();
            _setup.Format = f;
            if (!f.RetentionOnly) _setup.Retention = false;
            ShowOptions();
        }

        /// <summary>
        /// The one name a format goes by everywhere (rater r1 #10): MINI AUCTION (the dataset's
        /// "Standard", retention only) and MEGA AUCTION — the same words as the home buttons and the rooms.
        /// </summary>
        public static string FormatName(AuctionFormat f) => OnlineLobby.FormatTitle(f);

        /// <summary>The format came from the home page's MEGA / MINI AUCTION buttons: not asked again.</summary>
        private bool _formatFromHome;

        /// <summary>One line under a format's name: purse and how it starts.</summary>
        public static string FormatBlurb(Dataset data, AuctionFormat f) =>
            $"{Money.Format(f.Purse)} purse · " + (f.RetentionOnly
                ? $"keep up to {data.Rules.RetentionFor(f).MaxPlayers}"
                : $"fresh or keep {data.Rules.RetentionFor(f).MaxPlayers}");

        private void TypeButton(RectTransform page, Vector2 pos, bool retention, string title, string sub)
        {
            var selected = _setup.Retention == retention;
            var b = UiKit.Btn(page, title, new Vector2(0.5f, 0.5f), pos, new Vector2(440, 130), "", selected ? UiKit.Gold : UiKit.Muted,
                () => { _setup.Retention = retention; ShowOptions(); });
            var text = b.GetComponentInChildren<Text>();
            text.supportRichText = true;
            text.text = $"{title}\n<size=24>{sub}</size>";
        }

        // --------------------------------------------------------- retention

        private void ShowRetention()
        {
            var page = NewPage(0.7f);
            var rules = _data.Rules;
            var retention = rules.RetentionFor(_setup.Format);
            // A mini auction starts from the AI's picks: most people tweak, not build.
            if (_setup.Retained.Count == 0 && _setup.Format.RetentionOnly)
                _setup.Retained.AddRange(Ai.ChooseRetentions(_setup.Team.Id, _data.Players, rules, _setup.Format));
            Header(page, $"{FormatName(_setup.Format)} · RETAIN UP TO {retention.MaxPlayers}", retention.AtValue
                ? $"Last season's {_setup.Team.ShortName} squad. Keeps cost their current price."
                : $"Last season's {_setup.Team.ShortName} squad. Capped keeps cost the slab prices, best first.");
            // MINI from the home page skipped the options page: BACK goes to the franchise pick.
            Back(page, _formatFromHome && _setup.Format.RetentionOnly ? (Action)ShowTeams : ShowOptions);

            var picker = new RetentionPicker(page, _data, _setup.Team.Id, _setup.Format, _setup.Retained);
            var start = UiKit.Btn(page, "Start", new Vector2(0.5f, 0), new Vector2(0, 110), new Vector2(620, 110), "", UiKit.Accent, Begin, 40);
            var label = start.GetComponentInChildren<Text>();
            picker.Changed += problem =>
            {
                start.interactable = problem == null;
                label.text = _setup.Retained.Count == 0 ? "START WITH NO KEEPS ►" : "START AUCTION ►";
            };
            picker.Refresh();
        }

        private void Begin()
        {
            if (!_setup.Retention) _setup.Retained.Clear();
            StartPressed?.Invoke(_setup);
            Destroy(gameObject);
        }

        public static string RoleName(Player p) => p.Role switch
        {
            Role.Batter => "Batter",
            Role.Bowler => p.BowlType == BowlType.Spin ? "Spinner" : "Fast bowler",
            Role.AllRounder => "All-rounder",
            _ => "Wicketkeeper",
        };
    }
}
