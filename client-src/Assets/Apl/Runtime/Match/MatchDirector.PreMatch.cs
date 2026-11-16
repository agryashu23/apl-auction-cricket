using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Apl.Client.Menu;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Apl.Client.Match
{
    /// <summary>
    /// Everything before the first ball, as the reference plays it: both XIs,
    /// the match card over the two captains, the toss at the centre circle
    /// (MatchDirector.PreMatchToss.cs), pick your openers, the walk-out and the
    /// intros (MatchDirector.PreMatchWalk.cs); "SELECT BATTER" after each
    /// wicket; and a tournament match's result into the table.
    /// <para>
    /// For the two-innings flow (rules-bowling): the toss leaves
    /// <see cref="UserBatsFirst"/>. Who bats first then starts through
    /// <c>StartUserBowlingInnings()</c> / <c>StartUserBattingFirst()</c> when the
    /// director has them (found by name, so neither side waits on the other);
    /// until then the opponent's innings is simulated and the user chases.
    /// </para>
    /// </summary>
    public sealed partial class MatchDirector
    {
        private PreMatchUi _pre = null!;

        /// <summary>The toss's outcome: the user's side bats first.</summary>
        public bool UserBatsFirst { get; private set; }

        /// <summary>Who won the toss (a franchise id; empty before it).</summary>
        public string TossWinnerId { get; private set; } = "";

        /// <summary>The ceremony is on: START on the target card carries it on instead of starting the chase.</summary>
        private bool _preMatch, _startPressed;

        /// <summary>Test runs (-aplmatchtest) go straight to the chase unless -aplprematch asks for the ceremony too.</summary>
        /// <remarks>(-aplrunoutreal plays the real game's flow too: the toss and both innings, the user bowling one —
        /// the quick chase films only the user batting, never the AI side running. Run-out owner, 2026-11-13.)</remarks>
        private bool PlayPreMatch => _cast.Ready && (!Autoplay || Environment.GetCommandLineArgs().Contains("-aplprematch") || RunOutReal);

        /// <summary>Countdowns: the reference's lengths, a second each in test runs.</summary>
        private float Secs(float s) => Autoplay ? Mathf.Min(s, 1f) : s;

        /// <summary>A pick screen's clock (PickClock, owner v26 #8): a second in test runs unless -aplrealtimers.</summary>
        private float PickSecs(float s) => Autoplay && !PickClock.RealInTests ? Mathf.Min(s, 1f) : s;

        /// <summary>This side's XI: the tournament's squad when it's a tournament match, else last season's.</summary>
        // The opponent at the chosen difficulty (audio-modes: Modes/Difficulty.cs; the user's side unchanged).
        // The user's side: the XI he confirmed on the PLAYING XI screen (owner v26 #7, Match/XiChoice.cs) when it still fits.
        private TeamSheet SheetFor(string teamId) => Apl.Client.Modes.Difficulty.Sheet(
            (teamId == _bat.Id ? XiChoice.SheetFor(_data, teamId, SquadFor(teamId)) : null)
            ?? TournamentLaunch.Sheet(_data, teamId) ?? QuickMatch.LastSeasonXI(_data, teamId), teamId != _bat.Id);

        /// <summary>START on the HUD's target card.</summary>
        private void OnStartPressed()
        {
            if (_preMatch)
            {
                _startPressed = true;
                return;
            }
            StartCoroutine(Chase());
        }

        /// <summary>The end of Start(): the ceremony, or (older builds, test runs) the target card as before.</summary>
        private void BeginPreMatch()
        {
            // PRACTICE: the nets instead of a match (nothing saved, no ceremony).
            if (BeginNets()) return;
            // A challenge's banner and stars, and the career XP when it's over (audio-modes: MatchDirector.Modes.cs).
            ModesStart();
            StartCoroutine(WatchForResult());
            StartCoroutine(KeepSaving());
            StartCoroutine(KeepImpact());
            if (_resumed != null)
            {
                StartCoroutine(ResumeFlow());
                return;
            }
            // A new match replaces whatever was saved.
            MatchResume.Delete();
            if (!PlayPreMatch)
            {
                _hud.ShowTarget(_data, _match, _bat, _bowl);
                if (Autoplay) StartCoroutine(AutoStart());
                return;
            }
            StartCoroutine(PreMatch());
        }

        private IEnumerator PreMatch()
        {
            _preMatch = true;
            _pre = PreMatchUi.Create();
            _hud.SetPlaying(false);
            // No radar or score strip over the squads and the toss.
            HudVisible(false);
            // One frame for the batters' rigs to start (their Start puts them at the crease).
            yield return null;
            var mine = _bat;
            var opp = _bowl;
            var myXI = MatchXI(mine.Id);
            var oppXI = MatchXI(opp.Id);
            var myCap = Captain(myXI);
            var oppCap = Captain(oppXI);
            ClearField();
            var title = TournamentLaunch.Title() ?? "QUICK PLAY";

            // 1. The squads, over a slow pan of the ground.
            OrbitGround();
            yield return _pre.Squads(title + " · SQUADS", mine, myXI, myCap, opp, oppXI, oppCap, Autoplay ? 2.5f : 20f); // owner (round 13): keep the auto-advance, 20 s
            // The impact player: your substitutes from the bench.
            yield return PickSubstitutes();

            // 2. The toss beside the pitch, the two captains in plain team kit (the owner cut the
            // QUICK PLAY / VS card with the strength bars: MatchCard() is no longer in the flow).
            var capA = Stage(myCap);
            var capB = Stage(oppCap);
            var staged = true;
            StartCoroutine(KeepKit(capA, () => staged));
            StartCoroutine(KeepKit(capB, () => staged));
            yield return Toss(myCap, capA, oppCap, capB);
            staged = false;
            Unstage(capA);
            Unstage(capB);

            // 4. Who bats first: the two-innings flow when it's there, else they bat (simulated) and you chase.
            var hook = UserBatsFirst ? "StartUserBattingFirst" : "StartUserBowlingInnings";
            if (HasHook(hook))
            {
                _pre.ClearAll();
                _preMatch = false;
                HudVisible(true);
                RunHook(hook);
                yield break;
            }
            yield return TargetCard();
            yield return ChooseOpeners();
            yield return WalkOut();
            yield return Intros();
            _pre.ClearAll();
            _preMatch = false;
            HudVisible(true);
            StartCoroutine(Chase());
        }

        /// <summary>Their innings, simulated while the toss was on: the card, until START (or a few seconds in test runs).</summary>
        private IEnumerator TargetCard()
        {
            Camera.Show(MatchCamera.Mode.Wide);
            _startPressed = false;
            HudVisible(true);
            _hud.ShowTarget(_data, _match, _bat, _bowl);
            var until = Time.time + (Autoplay ? 1.5f : 3600f);
            while (!_startPressed && Time.time < until) yield return null;
            _hud.HideCard();
            HudVisible(false);
        }

        /// <summary>The match HUD (radar, score strip, cards) on or off: off for the ceremonies, on for the cards and the play.</summary>
        private void HudVisible(bool on)
        {
            var c = _hud != null ? _hud.GetComponent<Canvas>() : null;
            if (c != null) c.enabled = on;
        }

        // ------------------------------------------------------------ hooks

        /// <summary>The director has <paramref name="name"/>() (another partial's flow).</summary>
        private bool HasHook(string name) => GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null;

        private void RunHook(string name)
        {
            var m = GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            var r = m?.Invoke(this, null);
            if (r is IEnumerator routine) StartCoroutine(routine);
        }

        // ----------------------------------------------------------- the sides

        /// <summary>A side's XI as this match has it (the batting order for the side in).</summary>
        private List<Player> MatchXI(string teamId)
        {
            if (_match.Batting.TeamId == teamId) return _match.Order.Select(b => b.Player).ToList();
            if (_match.Bowling.TeamId == teamId) return _match.Bowling.XI.ToList();
            return SheetFor(teamId).XI.ToList();
        }

        /// <summary>The captain: the biggest name in the XI (the data has no captaincy).</summary>
        private static Player Captain(IReadOnlyList<Player> xi) =>
            xi.OrderByDescending(p => p.StarPower).ThenByDescending(p => p.Impact).ThenBy(p => p.Id, StringComparer.Ordinal).First();

        /// <summary>This player's body on the ground (batting side or fielding side), or null.</summary>
        private Animator? BodyOf(Player p)
        {
            if (_batters.TryGetValue(p.Id, out var rig) && rig != null) return rig.GetComponent<Animator>();
            return _side.TryGetValue(p.Id, out var a) ? a : null;
        }

        /// <summary>The field empty for the ceremony (the umpire stays at his end).</summary>
        private void ClearField()
        {
            foreach (var a in _side.Values) a.gameObject.SetActive(false);
            foreach (var b in _batters.Values) b.gameObject.SetActive(false);
        }

        /// <summary>Strength bars for the match card, 0–100, from the XIs (as the ratings weigh them).</summary>
        private static (string, int, int)[] Strengths(IReadOnlyList<Player> a, IReadOnlyList<Player> b)
        {
            int Field(IReadOnlyList<Player> xi) => Mathf.RoundToInt((float)xi.Average(p => p.Role == Role.WicketKeeper ? (p.Fielding + p.Keeping) / 2.0 : p.Fielding));
            return new (string, int, int)[]
            {
                ("BATTING", Mathf.RoundToInt((float)Lineups.BattingStrength(a)), Mathf.RoundToInt((float)Lineups.BattingStrength(b))),
                ("BOWLING", Mathf.RoundToInt((float)Lineups.BowlingStrength(a)), Mathf.RoundToInt((float)Lineups.BowlingStrength(b))),
                ("FIELDING", Field(a), Field(b)),
            };
        }

        private static string PitchName(Pitch p) => p switch
        {
            Pitch.Flat => "Flat · batting pitch",
            Pitch.SpinFriendly => "Dry · turning pitch",
            Pitch.SeamFriendly => "Green · seaming pitch",
            _ => "Balanced pitch",
        };

        // --------------------------------------------------------- the clips

        private static List<string>? _clipFiles;

        /// <summary>Every clip the packs list (Resources/Data/cricket/*.json "file" entries, minus ".fbx").</summary>
        private static List<string> ClipFiles()
        {
            if (_clipFiles != null) return _clipFiles;
            _clipFiles = new List<string>();
            foreach (var t in Resources.LoadAll<TextAsset>("Data/cricket"))
            {
                foreach (System.Text.RegularExpressions.Match m in Regex.Matches(t.text, "\"file\"\\s*:\\s*\"([^\"]+?)\\.fbx\"")) _clipFiles.Add(m.Groups[1].Value);
            }
            return _clipFiles;
        }

        /// <summary>
        /// The first clip on <paramref name="a"/> whose name has every one of a set of
        /// words (sets tried in order: the motion packs' new clips by what they are),
        /// else <paramref name="fallback"/> if he has it, else null.
        /// </summary>
        private static string? ClipFor(Animator a, string? fallback, params string[][] wordSets)
        {
            foreach (var words in wordSets)
            {
                var name = ClipFiles().FirstOrDefault(n => words.All(w => n.Contains(w)) && Body.Has(a, n));
                if (name != null) return name;
            }
            return fallback != null && Body.Has(a, fallback) ? fallback : null;
        }

        /// <summary>Standing about (captains, presenter, players waiting): an idle the packs have, else the plain stand.</summary>
        private static void Idle(Animator a, params string[][] prefer)
        {
            var clip = ClipFor(a, "Stand", prefer.Concat(new[] { new[] { "intro", "idle" }, new[] { "captain", "idle" } }).ToArray()) ?? "Ready";
            a.speed = 1f;
            a.SetBool(Animator.StringToHash("Mirror"), false);
            if (Body.Has(a, clip)) a.CrossFadeInFixedTime(clip, 0.25f, 0, UnityEngine.Random.value * 0.5f);
        }

        // --------------------------------------------------------- the camera

        /// <summary>A slow pan round the ground from high in the stands (behind the squad screen).</summary>
        private void OrbitGround()
        {
            var t0 = Time.time;
            Camera.Track(() =>
            {
                var a = (Time.time - t0) * 0.06f + 2.4f;
                return (new Vector3(Mathf.Sin(a) * 58f, 22f, Mathf.Cos(a) * 58f), new Vector3(0, 0, 0));
            }, fov: 45f, smooth: 0.6f, cut: true);
        }

        /// <summary>Stand <paramref name="a"/> at <paramref name="at"/> facing <paramref name="face"/>, idling.</summary>
        private static void Place(Animator? a, Vector3 at, Vector3 face)
        {
            // Squared to the way his body faces in the idle (the clips aren't all made facing the same way).
            Stand(a, at, face, "Stand");
        }

        /// <summary>
        /// A captain out for the ceremony: his match body, without the bat and the
        /// batting / keeping gear (and his rig off, so nothing puts him back at the crease).
        /// </summary>
        private Animator? Stage(Player p)
        {
            var a = BodyOf(p);
            if (a == null) return null;
            a.gameObject.SetActive(true);
            foreach (var rig in a.GetComponents<MonoBehaviour>())
            {
                if (rig is BatterRig || rig is BowlerRig || rig is KeeperRig) rig.enabled = false;
            }
            Gear(a, false);
            _cast.CaptainCap(a, true); // characters: match kit for the ceremony — the team cap, not bare or a helmet
            return a;
        }

        /// <summary>Back to his part in the match (hidden until the walk-out).</summary>
        private void Unstage(Animator? a)
        {
            if (a == null) return;
            // The handshake's IK on his right hand goes with the ceremony, whatever cut it short.
            foreach (var h in a.GetComponents<PreMatchHandshake>()) Destroy(h);
            PreMatchLook.Clear(a);
            _cast.CaptainCap(a, false);
            Gear(a, true);
            foreach (var rig in a.GetComponents<MonoBehaviour>())
            {
                if (rig is BatterRig || rig is BowlerRig || rig is KeeperRig) rig.enabled = true;
            }
            a.gameObject.SetActive(false);
        }

        /// <summary>
        /// A body in a ceremony after the match (player of the match, the captain with the
        /// trophy, the handshake line): his team kit only — no helmet, pads, gloves or bat —
        /// the same as the captains at the toss.
        /// </summary>
        private static void PlainKit(Animator a)
        {
            Gear(a, false);
            var rig = a.GetComponent<BatterRig>();
            if (rig != null && rig.Bat != null) rig.Bat.gameObject.SetActive(false);
        }

        /// <summary>What the ceremony switched off on each staged body, to switch back on after.</summary>
        private static readonly Dictionary<Animator, List<Renderer>> GearOff = new Dictionary<Animator, List<Renderer>>();

        /// <summary>
        /// Helmet, pads and gloves off (the toss is in plain team kit) or back on:
        /// the skinned gear groups (CricketCast.ShowGear, which also brings the hair
        /// back) and anything else hung on him — the older code-built helmet, pads
        /// and glove blocks are plain mesh renderers, and the bodies themselves have
        /// none. Caps and hats stay.
        /// </summary>
        private static void Gear(Animator a, bool on)
        {
            if (on)
            {
                if (GearOff.TryGetValue(a, out var off)) foreach (var r in off) if (r != null) r.enabled = true;
                GearOff.Remove(a);
                CricketCast.ShowGear(a, true);
                return;
            }
            if (!GearOff.TryGetValue(a, out var list)) GearOff[a] = list = new List<Renderer>();
            foreach (var r in a.GetComponentsInChildren<Renderer>(true))
            {
                var n = r.gameObject.name;
                var gear = r is MeshRenderer || n.StartsWith(CricketCast.HelmetGear) || n.StartsWith(CricketCast.PadGear) || n == "Helmet" || n == "Pad" || n == "Gear";
                if (!gear || n.StartsWith(CricketCast.CapGear) || n.StartsWith(CricketCast.HatGear) || !r.enabled) continue;
                r.enabled = false;
                list.Add(r);
            }
            CricketCast.ShowGear(a, false);
        }

        /// <summary>While the captains are out for the ceremony, nothing puts the gear back on them.</summary>
        private IEnumerator KeepKit(Animator? a, Func<bool> staged)
        {
            while (a != null && staged())
            {
                if (a.GetComponentsInChildren<Renderer>().Any(r => r.enabled && (r is MeshRenderer || r.name.StartsWith(CricketCast.HelmetGear) || r.name.StartsWith(CricketCast.PadGear))
                                                                && !r.name.StartsWith(CricketCast.CapGear) && !r.name.StartsWith(CricketCast.HatGear)))
                    Gear(a, false);
                yield return new WaitForSeconds(0.1f);
            }
        }

        // ---------------------------------------------------------- tournament

        /// <summary>A tournament match: once it's over, the result goes in the table and the result card gets CONTINUE.</summary>
        private IEnumerator WatchForResult()
        {
            if (TournamentLaunch.Active == null) yield break;
            while (!_match.Finished || !_match.Chasing) yield return null;
            // A tie goes to a Super Over (MatchDirector.SuperOver.cs): the table waits for it. The main
            // match's innings are the scores and the stats; the Super Over only says who won.
            var main = _match;
            string? winner = null;
            if (SuperOver.Needed(main))
            {
                while (_superOver == null || _superOver.Main != main || !_superOver.Over) yield return null;
                winner = _superOver.Winner?.TeamId;
            }
            TournamentLaunch.Record(_data, main, winner);
            // The finale and the result card first.
            yield return new WaitForSeconds(Autoplay ? 1f : 5.5f);
            if (_pre == null) _pre = PreMatchUi.Create();
            _pre.Continue("CONTINUE TOURNAMENT ►", () =>
            {
                TournamentLaunch.ReturnToHub = true;
                SceneManager.LoadScene(0);
            });
        }
    }
}
