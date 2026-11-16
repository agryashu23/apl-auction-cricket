using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Modes
{
    /// <summary>
    /// The challenges page's left column (owner v26 #12): two tabs, CHALLENGE and CAREER.
    /// CHALLENGE shows the selected card in full — BAT / BOWL, title, the situation, the
    /// brief, the three star conditions (earned ones lit), the XP on offer at the current
    /// difficulty, the best result so far and the next star unlock — with the PLAY button
    /// that starts it. CAREER is the old career panel (CareerPanel), a little smaller.
    /// </summary>
    public sealed partial class ChallengesPage
    {
        private readonly Dictionary<string, (Image rim, int stars, Vector2 size)> _rims = new Dictionary<string, (Image, int, Vector2)>();
        private Challenge? _selected;
        private bool _careerTab;
        private RectTransform _detailHost = null!, _careerHost = null!;
        private Image _tabChallenge = null!, _tabCareer = null!;

        private static readonly Color TabOff = new Color(0.12f, 0.14f, 0.22f, 0.95f);

        /// <summary>The one selected (for the dev shots and the page's own use).</summary>
        public Challenge? Selected => _selected;

        private static Color RimFor(bool selected, int stars) =>
            selected ? UiKit.Gold : stars == 3 ? UiKit.WithAlpha(RewardCard.StarOn, 0.8f) : new Color(1, 1, 1, 0.12f);

        private Color RimColour(string id, int stars) => RimFor(_selected?.Id == id, stars);

        private void BuildLeft(RectTransform bg)
        {
            _rims.Clear();
            // Something is always selected: the first challenge not yet three-starred.
            _selected ??= Challenges.All.FirstOrDefault(c => Progression.StarsFor(c.Id) < 3) ?? Challenges.All[0];
            // The tabs across the top of the column.
            _tabChallenge = Tab(bg, "Tab_Challenge", "CHALLENGE", 40 + 136, () => ShowTab(false));
            _tabCareer = Tab(bg, "Tab_Career", $"CAREER · LV {Progression.Level}", 40 + 140 + 280, () => ShowTab(true));
            _detailHost = Host(bg, "DetailHost");
            _careerHost = Host(bg, "CareerHost");
            // The career panel was laid out for the whole column (top at -150): 90% of it, under the tabs.
            _careerHost.localScale = new Vector3(0.9f, 0.9f, 1f);
            _careerHost.offsetMin = _careerHost.offsetMax = new Vector2(4, -95);
            CareerPanel.Build(_careerHost, Build);
            BuildDetail();
            ShowTab(_careerTab);
        }

        private static RectTransform Host(RectTransform parent, string name)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0, 1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static Image Tab(RectTransform parent, string name, string label, float x, System.Action act)
        {
            var img = PlayerCardKit.Shape(parent, name, CardSprites.Round(18), TabOff, new Vector2(x, -184), new Vector2(272, 80), new Vector2(0, 1));
            img.raycastTarget = true;
            img.gameObject.AddComponent<Button>().onClick.AddListener(() => act());
            PlayerCardKit.Text((RectTransform)img.transform, Vector2.zero, Vector2.one, new Vector2(8, 0), new Vector2(-8, 0), label, 30, TextAnchor.MiddleCenter, Color.white, condensed: true);
            return img;
        }

        private void ShowTab(bool career)
        {
            _careerTab = career;
            _careerHost.gameObject.SetActive(career);
            _detailHost.gameObject.SetActive(!career);
            // The difficulty may have changed on the career tab: the XP on offer follows it.
            if (!career) BuildDetail();
            foreach (var (img, on) in new[] { (_tabChallenge, !career), (_tabCareer, career) })
            {
                img.color = on ? UiKit.Gold : TabOff;
                img.GetComponentInChildren<Text>().color = on ? UiKit.Ink : Color.white;
            }
        }

        /// <summary>A card was tapped: it's the selected one now (its rim gold), shown on the left.</summary>
        private void Select(Challenge ch)
        {
            _selected = ch;
            foreach (var kv in _rims)
            {
                var on = kv.Key == ch.Id;
                kv.Value.rim.color = RimFor(on, kv.Value.stars);
                ((RectTransform)kv.Value.rim.transform).sizeDelta = kv.Value.size + (on ? new Vector2(12, 12) : new Vector2(4, 4));
            }
            ShowTab(false);
            Debug.Log($"[APL modes] challenge selected {ch.Id}");
        }

        private void BuildDetail()
        {
            // Destroy waits for the frame's end: hide the old panel now so the two never draw together.
            foreach (Transform c in _detailHost) c.gameObject.SetActive(false);
            UiKit.Clear(_detailHost);
            var ch = _selected;
            if (ch == null) return;
            const float w = 560, h = 740;
            var panel = PanelKit.GlassPanel(_detailHost, "ChallengeDetail", new Vector2(0, 1), new Vector2(40 + w / 2, -230 - h / 2), new Vector2(w, h));
            var stars = Progression.StarsFor(ch.Id);
            var colour = ch.UserBats ? new Color(0.15f, 0.55f, 0.3f) : new Color(0.15f, 0.4f, 0.85f);
            Text Line(float top, float height, string s, int size, Color c, bool condensed = false, bool bold = false, TextAnchor align = TextAnchor.MiddleLeft)
            {
                var t = PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, top - height), new Vector2(-28, top), s, size, align, c, condensed, bold);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                return t;
            }
            // BAT / BOWL and its number.
            var tag = PlayerCardKit.Shape(panel, "Tag", CardSprites.Round(14), colour, new Vector2(0, -44), new Vector2(w - 32, 48), new Vector2(0.5f, 1));
            var index = System.Array.IndexOf(Challenges.All, ch) + 1;
            PlayerCardKit.Text((RectTransform)tag.transform, Vector2.zero, Vector2.one, new Vector2(16, 0), new Vector2(-16, 0), ch.UserBats ? "YOU BAT" : "YOU BOWL", 26, TextAnchor.MiddleLeft, Color.white, condensed: true);
            PlayerCardKit.Text((RectTransform)tag.transform, Vector2.zero, Vector2.one, new Vector2(16, 0), new Vector2(-16, 0), $"CHALLENGE {index} OF {Challenges.All.Length}", 22, TextAnchor.MiddleRight, Color.white, condensed: true);
            var y = -78f;
            Line(y, 58, ch.Title.ToUpperInvariant(), 46, Color.white, condensed: true, bold: true);
            y -= 60;
            Line(y, 34, ch.Situation, 24, UiKit.Gold, condensed: true, bold: true);
            y -= 38;
            Line(y, 58, ch.Brief, 21, UiKit.Dim).alignment = TextAnchor.UpperLeft;
            y -= 60;
            var my = Team(ChallengeLaunch.MyTeam)?.ShortName ?? ChallengeLaunch.MyTeam;
            var opp = Team(ChallengeLaunch.Opponent)?.ShortName ?? ChallengeLaunch.Opponent;
            Line(y, 28, $"{my} v {opp} · the last overs of a 20-over game", 19, UiKit.Dim);
            y -= 40;
            // STARS: the best so far, then the three conditions, the earned ones lit.
            PlayerCardKit.Shape(panel, "RuleStars", CardSprites.Round(1), new Color(1, 1, 1, 0.08f), new Vector2(0, y), new Vector2(w - 56, 2), new Vector2(0.5f, 1));
            y -= 8;
            Line(y, 38, "STARS", 28, Color.white, condensed: true, bold: true);
            Line(y, 38, stars == 0 ? "BEST: NOT WON YET" : $"BEST: {stars} / 3", 22, stars == 3 ? RewardCard.StarOn : UiKit.Dim, condensed: true, align: TextAnchor.MiddleRight);
            RewardCard.StarRow(panel, new Vector2(0, y - 19), stars, 26, new Vector2(0.5f, 1));
            y -= 44;
            var conditions = new[] { "Win it", ch.TwoStar, ch.ThreeStar };
            for (var i = 0; i < 3; i++)
            {
                var earned = stars > i;
                var disc = PlayerCardKit.Shape(panel, "Cond" + (i + 1), CardSprites.Disc(), earned ? RewardCard.StarOn : new Color(1, 1, 1, 0.12f), new Vector2(48, y - 21), new Vector2(34, 34), new Vector2(0, 1));
                PlayerCardKit.Text((RectTransform)disc.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, (i + 1).ToString(), 20, TextAnchor.MiddleCenter, earned ? UiKit.Ink : Color.white, condensed: true);
                var t = PlayerCardKit.Text(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(80, y - 42), new Vector2(-28, y), (earned ? "DONE  " : "") + conditions[i], 21, TextAnchor.MiddleLeft, earned ? RewardCard.StarOn : Color.white, bold: false);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                y -= 42;
            }
            y -= 10;
            // REWARD: the XP the director awards a challenge (Progression.Record), at this difficulty.
            PlayerCardKit.Shape(panel, "RuleReward", CardSprites.Round(1), new Color(1, 1, 1, 0.08f), new Vector2(0, y), new Vector2(w - 56, 2), new Vector2(0.5f, 1));
            y -= 8;
            var scale = Difficulty.XpScale(Difficulty.Current);
            var left = Mathf.RoundToInt((20 + 30 + (3 - stars) * 40) * scale);
            Line(y, 36, "REWARD", 28, Color.white, condensed: true, bold: true);
            Line(y, 36, $"UP TO +{left} XP", 24, UiKit.Gold, condensed: true, bold: true, align: TextAnchor.MiddleRight);
            y -= 38;
            var scaleNote = Mathf.Approximately(scale, 1f) ? "" : $" · x{scale:0.##} on {Difficulty.Label(Difficulty.Current)}";
            Line(y, 28, $"+20 XP to play · +30 for the win · +40 per new star{scaleNote}", 18, UiKit.Dim);
            y -= 28;
            var next = Cosmetics.All.Where(c => c.Level == 0 && c.Stars > Progression.TotalStars).OrderBy(c => c.Stars).FirstOrDefault();
            Line(y, 28, next != null
                ? $"Next star unlock: {next.Name} ({next.Slot.ToString().ToLowerInvariant()}) at {next.Stars} stars · you have {Progression.TotalStars}"
                : $"Every star unlock is yours · {Progression.TotalStars} / {Challenges.MaxStars} stars", 18, UiKit.Dim);
            // PLAY.
            var label = stars == 0 ? "PLAY ►" : stars == 3 ? "PLAY AGAIN ►" : "PLAY FOR MORE STARS ►";
            PanelKit.Pill(panel, "ChallengePlay", new Vector2(0.5f, 0), new Vector2(0, 76), new Vector2(w - 60, 120), label, UiKit.Accent, () =>
            {
                Debug.Log($"[APL modes] challenge play {ch.Id}");
                if (!ChallengeLaunch.Play(_data, ch))
                    Line(-566, 30, "COULDN'T START", 26, UiKit.Bad, condensed: true, bold: true, align: TextAnchor.MiddleCenter);
            }, 40);
        }
    }
}
