using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Audio;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Apl.Client.Match
{
    /// <summary>What the match menu picked; read when the ground scene opens.</summary>
    public static class MatchLaunch
    {
        public static string MyTeam = "BLR", Opponent = "MBH";
        public static int Overs = 2;

        /// <summary>The ground (Venue id); empty = your team's home.</summary>
        public static string VenueId = "";
        public const string GroundScene = "Ground";
    }

    /// <summary>
    /// Runs a quick match on the ground: the opponent's simulated innings, then
    /// the human's chase ball by ball — run-up, flight, the tap, the outcome
    /// played out (ball, fielders, running, stumps), score, next ball.
    /// </summary>
    public sealed partial class MatchDirector : MonoBehaviour
    {
        public GroundRefs Refs = null!;
        public MatchCamera Camera = null!;
        public Sfx Sfx = null!;
        public VoiceQueue Voice = null!;

        /// <summary>Test runs (-aplmatchtest): the director plays the shots itself.</summary>
        public bool Autoplay;

        private Dataset _data = null!;
        private Commentary _comm = null!;
        private Venue _venue = null!;
        private QuickMatch _match = null!;
        private MatchHud _hud = null!;
        private Franchise _bat = null!, _bowl = null!;
        private BatterRig _striker = null!, _other = null!;
        private BowlerRig _bowler = null!;
        private Transform _ball = null!;
        private TrailRenderer _trail = null!;
        /// <summary>The blue marker on the pitch where this ball will land.</summary>
        private Transform _marker = null!;
        private readonly List<(Animator a, Vector3 home)> _fielders = new List<(Animator, Vector3)>();
        private readonly Vector3[] _stumpHome = new Vector3[3];
        private readonly Quaternion[] _stumpRot = new Quaternion[3];

        // The ball in play.
        private Trajectory? _flight;
        private float _released = -1;
        private double? _tap;
        private ShotType _tapType;
        private bool? _tapBack;
        private bool _tapSpecial, _left;
        /// <summary>The ball's outcome, settled at the tap so the shot clip can match it.</summary>
        private BallResult? _result;
        private Zone _tapZone;
        private double _contactAt;
        private Rng _autoRng = new Rng(1);

        /// <summary>Picks among similar clips (shot variety, bowling variants).</summary>
        private Rng _visRng = new Rng(2);
        private bool _leaveShown;

        private static readonly int Walking = Animator.StringToHash("Walking");

        private void Start()
        {
            Application.targetFrameRate = 60;
            _data = Dataset.FromJson(Resources.Load<TextAsset>("Data/apl_dataset").text);
            Voice.Bank = new VoiceBank();
            Autoplay |= Environment.GetCommandLineArgs().Contains("-aplmatchtest");
            _bat = _data.Franchises.First(f => f.Id == MatchLaunch.MyTeam);
            _bowl = _data.Franchises.First(f => f.Id == MatchLaunch.Opponent);
            var seed = Apl.Client.Dev.TestSeed.Or(DateTime.Now.Ticks & 0x7FFFFFFF); // -aplseed N, test runs only (Dev/TestSeed.cs)
            var venue = _venue = Venue.Find(MatchLaunch.VenueId) ?? Venue.HomeOf(_bat.Id);
            _comm = new Commentary(Voice);
            // Practice nets: an empty net on a practice ground, not the stadium (owner, round 13).
            if (NetsLaunch.Mode != NetsLaunch.Kind.None) StadiumBuilder.BuildNets(venue, Refs);
            else StadiumBuilder.Build(venue, StadiumLook.For(venue.Id), Refs, _data, _bat, _bowl);
            // A saved match the menu resumed (MatchDirector.Resume.cs), else a new one.
            ApplyPitchFlag();
            _match = ResumedMatch() ?? new QuickMatch(SheetFor(_bat.Id), SheetFor(_bowl.Id), MatchLaunch.Overs, seed, venue);
            // The strip in this match's pitch type (owner v26 #23, MatchDirector.PitchReport.cs).
            if (NetsLaunch.Mode == NetsLaunch.Kind.None) ShowPitchType();
            _autoRng = new Rng(seed + 5);
            _visRng = new Rng(seed + 9);

            _cast = new CricketCast(Refs);
            if (_cast.Ready && NetsLaunch.Mode == NetsLaunch.Kind.None) CheerSquad.Create(_cast, _bat, _bowl);
            if (_cast.Ready)
            {
                MakeCast();
            }
            else
            {
                // No kit bodies (older build): the scene's stand-ins play every part.
                _striker = Refs.Striker!.gameObject.AddComponent<BatterRig>();
                _other = Refs.NonStriker!.gameObject.AddComponent<BatterRig>();
                _bowler = Refs.Bowler!.gameObject.AddComponent<BowlerRig>();
                foreach (var f in Refs.Fielders) _fielders.Add((f, f.transform.position));
                if (Refs.Keeper != null) _keeper = Refs.Keeper.gameObject.AddComponent<KeeperRig>();
            }
            for (var i = 0; i < Refs.Stumps.Length && i < 3; i++)
            {
                _stumpHome[i] = Refs.Stumps[i].position;
                _stumpRot[i] = Refs.Stumps[i].rotation;
            }
            _ball = MakeBall();
            _marker = MakeMarker();
            Umpire("umpire_idle_bowlers_end_loop");
            if (_cast.Ready)
            {
                // Everyone in place for the opening shot.
                Roles();
                PlaceField();
                Batters();
                _striker.TakeGuard();
                PlaceOtherBatter();
                _bowler.Mark(_match.CurrentBowler.BowlType == BowlType.Spin);
            }

            _hud = MatchHud.Create();
            WireHudRole(); // final rating fix #1: the batting panel only while the user bats (MatchDirector.PreMatchBatters)
            WireCamGuard(); // final rating fix #3: the camera never inside a player's head (MatchDirector.RunOutView)
            _hud.ShotPressed += (type, back) => OnShot(type, back, false);
            _hud.LeavePressed += OnLeave;
            _hud.SpecialPressed += () => OnShot(ShotType.Loft, null, true);
            _hud.StartPressed += OnStartPressed;
            _hud.PlayAgainPressed += () => SceneManager.LoadScene(MatchLaunch.GroundScene);
            // Every MENU / QUIT TO MENU in the match asks first (owner v26 #21: MatchDirector.Quit.cs).
            _hud.MenuPressed += ConfirmQuit;
            // Test runs go past the result card to the rewards by themselves (MatchHud.ResultCard.cs).
            _hud.AutoContinue = () => Autoplay;
            StallProbe.On |= Autoplay;
            StallWatch.Ensure(); // test runs: the stall watcher thread (TEC-1)
            // An abandoned freeze restarted by the HUD's watchdog: the replay it left on is closed too (TEC-1).
            _hud.ClockRestarted += () => { if (_replays != null && _replays.Running) _replays.Exit(); };
            // One Player of the Match for the finale and the result card.
            _hud.PlayerOfMatchSource = () => PlayerOfTheMatch()?.Player;
            _hud.SetPlaying(false);
            _hud.ShowScore(_match, _bat, _bowl);
            PresentationStart();

            Camera.Show(MatchCamera.Mode.Wide, cut: true);
            Sfx.SetCrowd(1.2f);
            _comm.Say("start", important: true);
            // The toss, squads, walk-out (MatchDirector.PreMatch.cs) — or the target card straight away.
            BeginPreMatch();
        }

        private IEnumerator AutoStart()
        {
            yield return new WaitForSeconds(2.5f);
            if (!_chasing) StartCoroutine(Chase());
        }

        private bool _chasing;

        private IEnumerator Chase()
        {
            if (_chasing) yield break;
            _chasing = true;
            _hud.HideCard();
            HudRole("chase starts"); // final rating fix #1 (was SetPlaying(true): MatchDirector.PreMatchBatters)
            Roles();
            PlaceField();
            var lastOver = -1;
            while (!_match.Finished)
            {
                if (_match.Over != lastOver)
                {
                    if (lastOver >= 0) yield return SimChipBreak(OverBreak());
                    lastOver = _match.Over;
                    Roles();
                    PlaceField();
                    // The AI captain's bowling change / field change, named (match-play: MatchDirector.Captaincy.cs).
                    AnnounceAiCaptaincy();
                }
                // SIM OVER / INNINGS / TO LAST 2 (match-play: MatchDirector.Sim.cs): if one ran, back round the loop.
                var simFrom = _match.LegalBalls;
                yield return SimIfAsked();
                if (_match.LegalBalls != simFrom || _match.Finished) continue;
                yield return Ball();
                // A wicket: the user picks who comes in (MatchDirector.PreMatchWalk.cs).
                // Both wickets stood up again before anyone walks in (match-play: the broken stumps
                // used to lie there through the new batter's walk-out and guard).
                RestoreWickets();
                if (_match.Incoming != null) yield return NewBatter();
                yield return ImpactIfAsked();
            }
            yield return new WaitForSeconds(1.2f);
            _hud.SetPlaying(false);
            _chasing = false;
            // A full match's first innings: the break, then the user bowls (MatchDirector.Bowling.cs).
            if (_match.TwoInnings && !_match.Chasing)
            {
                yield return SecondInningsBowling();
                yield break;
            }
            // A tie: the Super Over (or this was it) — MatchDirector.SuperOver.cs.
            if (SuperOverNext)
            {
                yield return SuperOverOrEnd();
                yield break;
            }
            yield return Finale();
            Camera.Show(MatchCamera.Mode.Wide);
            _comm.Say(ResultLine(), important: true); // (a tie has its own call: audio-modes, CommentaryCalls)
            if (_match.Won)
            {
                Sfx.Cheer(1f);
                Sfx.Applause(1f);
            }
            _hud.ShowResult(_match, _bat, _bowl);
        }

        private IEnumerator OverBreak()
        {
            _impactOffer = true;
            // (Fielding dev, final rating 2026-11-14: the fielders walk back to their marks through the over break.)
            PreSetField();
            // How the chase stands, from the box; the odd chant from the stands.
            var rate = _match.BallsLeft == 0 ? 99 : _match.RunsNeeded * 6.0 / _match.BallsLeft;
            if (_match.BallsLeft <= 6) _comm.Say("last_over", important: true);
            else if (_match.RunsNeeded <= _match.BallsLeft * 0.9) _comm.Say("cruising", 0.7f);
            else if (_match.BallsLeft <= 18 && rate <= 14) _comm.Say("close", 0.8f);
            else if (rate > 11) _comm.Say("pressure", 0.8f);
            else _comm.Say("over", 0.6f);
            if (UnityEngine.Random.value < 0.4f) Sfx.Chant(UnityEngine.Random.value < 0.5f ? "apl" : _venue.Home.ToLowerInvariant(), 0.7f);
            yield return OverCard();
            // The strategic timeout and an impact substitution (MatchDirector.Breaks.cs).
            yield return BreakRules();
        }

        // A ball (run-up, flight, the tap, the shot): MatchDirector.Batting.cs.

        // ------------------------------------------------------------ outcome

        private IEnumerator Outcome(BallResult r, double since, bool leftHanded, string batterName)
        {
            // The non-striker, backed up out of his ground, walks straight back in (a run takes him over) —
            // except when he's run out backing up (a fingertip deflection onto the bowler's stumps): he stays out.
            if (!(r.RunOut && !r.RunOutAtKeeperEnd && !r.RunOutStriker && r.Running == null)) _other.HomeAfterBall();
            // Where the ball was drawn as the outcome took over (the pad / stumps snap check: KeeperRead.LogPadStump).
            var entry = _ball.position;
            var entryT = since;
            // No-ball, byes, leg byes, a free-hit reprieve (MatchDirector.BowlingFlight.cs).
            RulesSignals(r);
            // Not the keeper's ball (hit, the slip's, bowled, off the pad): his read lets go (MatchDirector.KeeperRead).
            if (_kr != null && !KeepersBall(r)) KeeperOff(_kr);
            var hit = r.ExitSpeed > 0 && !(r.Wicket && !r.Caught && !r.RunOut) && !r.Wide && r.Timing != Timing.Leave && r.Timing != Timing.Miss;
            // The batter's reaction from the ball's moment: beaten or struck, or out and walking.
            var react = r.Wicket || !hit ? BatterReaction(r) : 0f;
            // The knock (middled / plain / a nick), the buzz, a middled hit's flash and slow-mo (MatchDirector.HitFeel.cs).
            ContactFeel(r, hit);
            // The pad's thud when that's what it hit (audio: MatchDirector.AudioFx.cs).
            ImpactSounds(r, hit);
            if (!hit)
            {
                // An edge to slip: his catch, not the keeper's (fielding: MatchDirector.CatchVariety).
                if (SlipCatch(r, out var slipTakes))
                {
                    yield return slipTakes;
                    yield return Refer(r);
                    yield return Wicket(r, batterName, NamedFielder("slip"), true);
                    yield return AfterBall(r);
                    yield break;
                }
                // Past the bat: into the keeper's gloves, or into the stumps.
                var bowled = r.Wicket && r.HowOut == "bowled";
                // (match-sim, final rating 2026-11-14: natural lbw) Struck on the pad (no leg byes run): the ball stops at
                // the pad and drops there — it used to fly on through his legs into the keeper's gloves, lbw given or not.
                var padStop = r.PadHit && r.Lbw != null && r.LegByes == 0 && !r.Wide;
                var keeperZ = _keeper != null && _keeper.UsesClips ? _keeper.transform.position.z + 0.55 + GroundSpace.HalfPitch : -2.2;
                var end = padStop ? r.Lbw!.Tracking.ImpactTime : r.Trajectory.TimeAt(bowled ? 0 : keeperZ);
                string? take = null;
                if (!bowled && !padStop && _keeper != null)
                {
                    // The take the keeper read off the flight (MatchDirector.KeeperRead): the ball flies its own line
                    // into his gloves at the take point, timed there — no fixed spot, no snap.
                    if (_keeper.UsesClips) take = KeeperOnBall(r, since, leftHanded, ref end);
                    else
                    {
                        var at = r.Trajectory.At(end);
                        var beaten = !r.Wicket && !r.Wide && r.Timing != Timing.Leave;
                        // (A stumping is the stumping take, the bails off: fielding's MatchDirector.CatchVariety.)
                        take = KeeperTakes(r, GroundSpace.Ball(at.x, Math.Max(0.15, at.y), at.z, leftHanded), (float)((end - since) / _pace), beaten && UnityEngine.Random.value < 0.3f);
                    }
                }
                // Chopped on: off the inside of the bat into the stumps, not along its own line.
                if (bowled && r.Edge)
                {
                    yield return ChoppedOn(leftHanded);
                    since = end;
                }
                while (since < end)
                {
                    since = FlightNow;
                    SetBall(r.Trajectory, KeeperFlight(Math.Min(since, end)), leftHanded);
                    yield return null;
                }
                if (padStop || bowled && !r.Edge) LogPadStump(r, padStop, end, entry, entryT, leftHanded);
                // (In his cup from the frame it gets there: KeeperRead.)
                if (take != null && !bowled && !padStop) KeeperFlight(end);
                var beatHim = _kr != null && _kr.Miss;
                if (padStop) Extra(PadDrop(r, end, leftHanded));
                // Into the gloves (audio: MatchDirector.AudioFx.cs).
                if (take != null && !bowled && !beatHim) GloveSound();
                var appealed = (take != null && take.Contains("appeal")) || (padStop && r.Appeal);
                if (beatHim) { }
                else if (take != null && !r.Wicket) _keeperPlay = StartCoroutine(KeeperHolds(appealed));
                else if (take != null && _keeper!.Gloves != null) Extra(Hold(_keeper.Gloves, 3.5f));
                else if (!bowled && !padStop && Refs.Keeper != null) Event(Refs.Keeper, CricketClips.FieldClipNamed("catch_01_cup_catch"));
                // Byes and leg byes: the keeper fumbles it (or it's off the pad) and they run (MatchDirector.Running.cs).
                if (r.KeeperFumble && Refs.Keeper != null) Body.Play(Refs.Keeper, "wk_fumble_turn_chase_byes");
                if (r.ExtrasBoundary) Show($"{r.Byes} BYES", "past the keeper to the rope", Color.white, 1.6f);
                if (!r.Wicket && (r.Byes > 0 || r.LegByes > 0) && !r.ExtrasBoundary && StartRunning(r, out _))
                {
                    yield return RunsDone(r, batterName);
                    yield return AfterBall(r);
                    yield break;
                }
                // A stumping (or a let-off) goes to the TV umpire first (MatchDirector.Referrals.cs).
                yield return Refer(r);
                if (r.Wicket) yield return Wicket(r, batterName);
                // A wide: the umpire's call, the banner and the line are ExtrasCalls' (MatchDirector.Extras.cs),
                // so it's called even when they run byes off it.
                else if (r.Wide) { }
                else
                {
                    Show(r.Timing == Timing.Leave ? "LEFT ALONE" : "BEATEN", "no run", UiKit.Dim, 1.2f);
                    _comm.Say(r.Timing == Timing.Leave ? "left" : "beaten", r.Timing == Timing.Leave ? 0.7f : 0.85f);
                }
                var deadAt = Time.time;
                yield return new WaitForSeconds(r.Wicket ? 0.3f : Mathf.Max(appealed ? 2.8f : 1.0f, react));
                // A shout turned down: Hawk-Eye (or, bowling, the user's review — which can give it).
                var reviewedOut = false;
                if (!r.Wicket) yield return NotOutShout(r, o => reviewedOut = o);
                if (reviewedOut) yield return Wicket(r, batterName);
                // Ball tracking (every ball: MatchDirector.Tracking.cs), and the over's pitch map (presentation).
                yield return AfterBall(r);
                // Batting: a breather before the next ball after a dot (owner v26 #24; MatchDirector.Extras.cs).
                if (!reviewedOut) yield return DotBallSettle(r, deadAt);
                yield break;
            }

            yield return Hit(r, leftHanded, batterName);
            yield return AfterBall(r);
        }

        private static readonly string[] Celebrations =
        {
            "bowler_celebration_fist_pump_roar", "bowler_celebration_point_to_sky",
            "bowler_celebration_knee_slide", "bowler_celebration_aeroplane",
        };

        /// <summary>
        /// Out: lbw and caught behind are appealed for (bowler, close fielders,
        /// keeper) before the finger goes up; then the bowler celebrates, a catcher
        /// throws the ball up, and the fielders come in to him.
        /// <paramref name="catcher"/> is busy with his catch; <paramref name="ballUp"/>:
        /// he caught it where he stood and throws it up.
        /// </summary>
        private IEnumerator Wicket(BallResult r, string batterName, Animator? catcher = null, bool ballUp = false)
        {
            _wicketShown = r;
            // Final rating fix #4 (HUD dev): the bar shows the wicket the moment it's given (it lagged 10–15 s, until
            // the replay and walk-off were over). Not while the batting side can still review an lbw: the review's
            // own refresh (OfferReview) has the last word there.
            if (!(r.HowOut == "lbw" && _match.UserBatting && _match.CanReview()))
            {
                _hud.ShowScore(_match, BattingSide, FieldingSide);
                Debug.Log($"[APL hudrole] scorebug on the wicket ({r.HowOut}): {_match.Runs}/{_match.Wickets}");
            }
            StallProbe.Mark($"wicket: {r.HowOut}{(r.RunOut ? (r.DirectHit ? " (direct hit)" : " (taken and broken)") : "")}");
            _impactOffer = true;
            Haptics.Wicket();
            _hud.ShowRunControls(false);
            // The stumps go at the batting end: bowled, stumped, hit wicket, a run out there.
            if (r.HowOut == "bowled" || r.HowOut == "stumped" || r.HowOut == "hit wicket" || (r.RunOut && r.RunOutAtKeeperEnd)) StartCoroutine(Stumps());
            var where = r.Caught && r.Fielder.Length > 0 && r.HowOut == "caught" ? $" at {r.Fielder}" : "";
            var appeal = r.HowOut == "lbw" || r.HowOut == "caught behind";
            if (appeal)
            {
                var lbw = r.HowOut == "lbw";
                _bowler.Act(lbw ? (UnityEngine.Random.value < 0.3f ? "bowler_appeal_on_knees" : "bowler_appeal_lbw") : "bowler_appeal_caught_behind", Vector3.back);
                Appeal();
                // The finger goes up when the lbw clip's batter looks for it (frame 75 of 162, 1.47 s after the pad).
                Extra(After(1.45f, () => Umpire("umpire_out")));
            }
            // The finger goes up for a catch; bowled needs no signal.
            else if (r.HowOut != "bowled") Extra(After(UmpireCue, () => SignalOut(r)));
            Extra(After(appeal ? 2.1f : 1.4f, CelebrateWicket));
            if (ballUp && catcher != null && catcher != Refs.Bowler && catcher != Refs.Keeper) Extra(BallInTheAir(catcher));
            Extra(After(appeal ? 2.4f : 1.2f, () => { if (!CelebrateCatch(catcher)) Congratulate(catcher); }));
            _comm.Say(WicketLine(r), important: true);
            CrowdChant(batting: false, 0.7f);
            StadiumBuilder.Flash(250);
            StadiumBuilder.CrowdCheer(0.9f, FieldingSide.Id);
            StadiumBuilder.CrowdWicketBanners(FieldingSide.Id);
            if (!_match.Finished) StartCoroutine(After(3.2f, () => _comm.Say("new_batter", 0.6f)));
            Sfx.Ooh(0.9f);
            Sfx.Cheer(0.6f);
            // The ground goes up, and settles back to the game's mood (audio: MatchSfx.Swell).
            _msfx.Swell(2.2f, 4f);
            // The broadcast's wicket sequence: umpire, graphic, celebration, replay, walk-off — with the
            // batting buttons off the screen throughout (they sat over the celebrations).
            _hud.Broadcasting(true);
            try { yield return WicketDirection(r, catcher, appeal); }
            finally { _hud.Broadcasting(false); }
        }

        /// <summary>
        /// What the batter does when the ball gets past him (the user's reaction
        /// clips, joined at their event frame): out — the dismissal's own
        /// reaction and walk-off; otherwise beaten, or now and then struck on the
        /// pad, ribs or helmet (looks only: the engine scored it a dot). Returns
        /// seconds the scene wants before the next ball.
        /// </summary>
        private float BatterReaction(BallResult r)
        {
            if (!_striker.UsesClips || r.Timing == Timing.Leave) return 0f;
            if (r.Wicket)
            {
                var clip = r.HowOut switch
                {
                    "bowled" => "bowled_reaction_walk_off",
                    "lbw" => "lbw_given_walk_off",
                    "caught behind" => "outside_edge_reaction",
                    "stumped" => "turn_and_start_walk_off",
                    "hit wicket" => "hit_wicket_walk_off",
                    "run out" => "run_out_reaction_walk_off",
                    _ => "caught_reaction_walk_off",
                };
                // Run out: the man who's out (the non-striker, now and then) walks; RunBetween plays it.
                if (r.RunOut) return 0f;
                // A nick: he walks at once, or stands his ground until the finger goes up (~1.5 s).
                if (r.HowOut == "caught behind" && !r.Walked)
                {
                    var rig = _striker;
                    Extra(After(1.55f, () => rig.React(clip, walkOff: true)));
                    return 0f;
                }
                _striker.React(clip, walkOff: true);
                return 0f;
            }
            if (r.Wide) return 0f;
            var d = r.Delivery;
            var roll = UnityEngine.Random.value;
            // On the pad only when the engine says so (its lbw shout and any leg byes go with it).
            // Off the pad with no shout, playing a shot: often an inside edge onto it (the nick sounds in ContactFeel).
            var insideEdge = r.PadHit && !r.Appeal && !r.Shot.IsLeave && roll < 0.45f;
            var what = insideEdge ? "inside_edge_onto_pad"
                : r.PadHit ? (d.Length >= Length.Short ? "hit_on_pad_back_foot" : "hit_on_pad_front_foot")
                : d.Length == Length.Bouncer && roll < 0.45f ? (roll < 0.18f ? "hit_on_helmet" : "hit_on_body_ribs")
                : r.Shot.Type == ShotType.Push ? "beaten_forward_defence" : "play_and_miss_drive";
            _insideEdge = insideEdge && _striker.React(what);
            if (insideEdge && !_insideEdge) what = d.Length >= Length.Short ? "hit_on_pad_back_foot" : "hit_on_pad_front_foot";
            if (!_insideEdge && !_striker.React(what)) return 0f;
            if (r.PadHit)
            {
                // Nowhere near: no shout. A shout: the bowler goes up, the umpire says no (Hawk-Eye and
                // any review follow in the presentation's NotOutShout).
                if (!r.Appeal) return 1.4f;
                // Struck on the pad: a shout, and the umpire shakes his head.
                Extra(After(0.4f, () => _bowler.Act("bowler_appeal_lbw", Vector3.back)));
                Extra(After(1.3f, () => Umpire("umpire_not_out")));
                return 2.4f;
            }
            if (what == "hit_on_helmet" || what == "hit_on_body_ribs" || what == "hit_on_glove")
            {
                Sfx.Ooh(0.7f);
                return what == "hit_on_helmet" ? 3.6f : 2.4f;
            }
            return 1.4f;
        }

        private void Show(string text, string sub, Color color, float seconds = 1.8f) => _hud.Banner(text, sub, color, seconds);

        /// <summary>The home crowd gets going when their side does something; otherwise the neutral "A-P-L!".</summary>
        private void CrowdChant(bool batting, float chance)
        {
            if (UnityEngine.Random.value > chance) return;
            // The side that did it (whichever innings it is); away fans get their own chant half the time (audio-modes, round 10).
            var side = batting ? BattingSide.Id : FieldingSide.Id;
            Sfx.Chant(side == _venue.Home || UnityEngine.Random.value < 0.5f ? side.ToLowerInvariant() : "apl", side == _venue.Home ? 0.8f : 0.6f);
        }

        private readonly HashSet<string> _fifties = new HashSet<string>();
        private readonly HashSet<string> _hundreds = new HashSet<string>();
        private (BatterRig rig, string clip)? _salute;

        /// <summary>Fifty (or a hundred) up for either batter.</summary>
        private void Milestone()
        {
            // Each fifty-run level crossed, once (batting: MatchDirector.Milestone.cs).
            MilestoneLevels();
        }

        /// <summary>The milestone's bat raised to the dressing room, after the running.</summary>
        private IEnumerator Salute()
        {
            var s = _salute;
            _salute = null;
            if (s == null || _match.Finished || !s.Value.rig.gameObject.activeInHierarchy) yield break;
            // The batter's celebration (batting: MatchDirector.Milestone.cs).
            yield return Celebration(s.Value.rig, s.Value.clip);
        }

        /// <summary>The last ball: the batters celebrate a win; a defence, the bowler (unless a wicket just did).</summary>
        private IEnumerator Finale()
        {
            // Fireworks, confetti and the crowd for the winners (once; nothing on a tie).
            if (_match.Winner != null) WinFx.Play(Side(_match.Winner.TeamId, _bat));
            Transform? focus = null;
            if (_match.Won)
            {
                // The user's striker celebrates his own way (THE VAULT's CELEBRATION, Modes/Celebrations.cs).
                var win = Apl.Client.Modes.Locker.CelebrationClip(BattingSide.Id, "bat_winning_runs_celebration");
                // (With its bat and helmet events: BatterRig.Celebration.cs.)
                if (_striker.CelebrateMilestone(win, null) >= 0f || _striker.Celebrate("bat_winning_runs_celebration")) focus = Hips(_striker);
                var other = _other;
                StartCoroutine(After(0.4f, () => other.Celebrate("bat_winning_runs_celebration")));
            }
            else if (_match.Log.Count == 0 || !_match.Log[_match.Log.Count - 1].Wicket)
            {
                if (_bowler.Act(Celebrations[UnityEngine.Random.Range(0, Celebrations.Length)])) focus = Hips(_bowler);
                Congratulate(null);
            }
            if (focus != null)
            {
                Camera.Target = focus;
                Camera.Show(MatchCamera.Mode.Player);
                yield return new WaitForSeconds(3.6f);
            }
            // Fireworks, the handshake line, player of the match, the trophy (presentation).
            yield return WinningMoment();
        }

        // ------------------------------------------------- appeals and celebrations

        /// <summary>Wicket-time routines, stopped when the next ball starts.</summary>
        private readonly List<Coroutine> _extras = new List<Coroutine>();

        private void Extra(IEnumerator routine) => _extras.Add(StartCoroutine(routine));

        private static Transform Hips(Component c)
        {
            var a = c.GetComponent<Animator>();
            var hips = a != null ? a.GetBoneTransform(HumanBodyBones.Hips) : null;
            return hips != null ? hips : c.transform;
        }

        /// <summary>The close fielders and the keeper go up with the bowler.</summary>
        private void Appeal()
        {
            var field = _match.Field.Fielders;
            var stumps = new Vector3(0, 0, -GroundSpace.HalfPitch);
            for (var i = 0; i < _fielders.Count && i < field.Count; i++)
            {
                var a = _fielders[i].a;
                if (!a.gameObject.activeSelf || Vector3.Distance(Flat(a.transform.position), stumps) > 26f) continue;
                var clip = field[i].Name == "slip" ? "slip_appeal_from_crouch" : "fielder_appeal_standing";
                Extra(After(UnityEngine.Random.Range(0.05f, 0.25f), () => Body.Play(a, clip)));
            }
            // The keeper once the ball's in his gloves.
            var keeper = Refs.Keeper;
            if (keeper != null) Extra(After(0.35f, () => Body.Play(keeper, "fielder_appeal_standing")));
            // "Howzat!" (presentation).
            AppealShouts();
        }

        private void CelebrateWicket()
        {
            var clip = Celebrations[UnityEngine.Random.Range(0, Celebrations.Length)];
            // The ones that run off (aeroplane, knee slide) head out square, not at the batter.
            var runsOff = clip.Contains("aeroplane") || clip.Contains("knee_slide");
            _bowler.Act(clip, runsOff ? (UnityEngine.Random.value < 0.5f ? Vector3.left : Vector3.right) : (Vector3?)null);
        }

        /// <summary>The near fielders run in to the bowler and applaud him; the far ones applaud where they are.</summary>
        private void Congratulate(Animator? busy)
        {
            var at = Flat(Hips(_bowler).position);
            var near = 0;
            var everyone = new List<Animator>(Refs.Fielders);
            if (Refs.Keeper != null) everyone.Add(Refs.Keeper);
            foreach (var a in everyone)
            {
                if (a == null || a == busy || !a.gameObject.activeSelf) continue;
                if (near < 5 && Vector3.Distance(Flat(a.transform.position), at) < 32f)
                {
                    var angle = (near * 72 + 20) * Mathf.Deg2Rad;
                    near++;
                    Extra(JoinIn(a, at + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 1.7f, at));
                }
                else Body.Play(a, "applause_loop");
            }
        }

        private IEnumerator JoinIn(Animator a, Vector3 spot, Vector3 bowler)
        {
            Body.Rebase(a);
            SlideScene.Tag(a, "fielder celebrating (run to the bowler)");
            yield return RunTo(a, spot, Mathf.Max(1.2f, Vector3.Distance(Flat(a.transform.position), spot) / 5f), betweenBalls: true);
            Body.Play(a, "applause_loop", bowler - spot);
        }

        /// <summary>After a catch: he flings the ball high in the air (frame 19, 9 m/s up) and lets it fall.</summary>
        private IEnumerator BallInTheAir(Animator catcher)
        {
            yield return new WaitForSeconds(0.6f);
            if (!Body.Play(catcher, "catch_celebration_ball_in_the_air")) yield break;
            var hand = catcher.GetBoneTransform(HumanBodyBones.RightHand);
            for (var until = Time.time + 18 / 30f; Time.time < until;)
            {
                if (hand != null) _ball.position = hand.position;
                yield return null;
            }
            var v = catcher.transform.rotation * new Vector3(0, 9f, 0.3f);
            var p = _ball.position;
            while (p.y > 0.04f || v.y > 0)
            {
                v += Physics.gravity * Time.deltaTime;
                p += v * Time.deltaTime;
                _ball.position = p;
                yield return null;
            }
        }

        /// <summary>The ball stays in his hand (the keeper's gloves) for a while.</summary>
        private IEnumerator Hold(Transform hand, float seconds)
        {
            for (var until = Time.time + seconds; Time.time < until;)
            {
                _ball.position = hand.position;
                yield return null;
            }
        }

        /// <summary>
        /// The batting end's wicket broken (bowled, chopped on, hit wicket, stumped, run out there):
        /// stumps rattle, the bails fly and fall to the turf (fielding's BreakWicket — once a ball,
        /// so a wicket the field play already broke isn't broken again).
        /// </summary>
        private IEnumerator Stumps()
        {
            BreakWicket(true, Vector3.back);
            yield break;
        }

        private static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0, p.z);

        private static IEnumerator After(float seconds, Action act)
        {
            yield return new WaitForSeconds(seconds);
            act();
        }

        /// <summary>The batters run; on an odd number they finish at the other end.</summary>
        private IEnumerator Run(int runs, float runTime)
        {
            if (_striker.UsesClips && CricketClips.Run("run_single_crease_to_crease", false) != null)
            {
                yield return RunWithClips(runs, runTime);
                yield break;
            }
            var a = _striker.GetComponent<Animator>();
            var b = _other.GetComponent<Animator>();
            yield return new WaitForSeconds(0.35f);
            _striker.Run(true);
            _other.Run(true);
            var endA = new Vector3(_striker.transform.position.x, 0, -GroundSpace.HalfPitch + 0.9f);
            var endB = new Vector3(_other.transform.position.x, 0, GroundSpace.HalfPitch - 0.9f);
            for (var i = 0; i < runs; i++)
            {
                var (fa, ta) = i % 2 == 0 ? (endA, endB) : (endB, endA);
                var (fb, tb) = i % 2 == 0 ? (endB, endA) : (endA, endB);
                a.transform.rotation = Quaternion.LookRotation(ta - fa);
                b.transform.rotation = Quaternion.LookRotation(tb - fb);
                for (var t = 0f; t < 1f; t += Time.deltaTime / 2.4f)
                {
                    a.transform.position = Vector3.Lerp(fa, ta, t) + Vector3.right * 0.6f;
                    b.transform.position = Vector3.Lerp(fb, tb, t) - Vector3.right * 0.6f;
                    yield return null;
                }
            }
            _striker.Run(false);
            _other.Run(false);
            // Whoever ended up at the batting end is the striker's model now.
            if (runs % 2 == 1) (_striker, _other) = (_other, _striker);
        }

        /// <summary>
        /// Crease to crease with the running clips: each run starts on a popping
        /// crease facing the other end; the last run of two or more is sometimes
        /// a dive to beat the throw.
        /// </summary>
        private IEnumerator RunWithClips(int runs, float runTime)
        {
            yield return new WaitForSeconds(0.3f);
            var near = new Vector3(0, 0, -GroundSpace.HalfPitch + 1.22f);
            var far = new Vector3(0, 0, GroundSpace.HalfPitch - 1.22f);
            // Running lanes on the pitch, either side of the stumps: the non-striker
            // keeps the side he was waiting on, the striker takes the other.
            var laneA = Vector3.right * (-Mathf.Sign(_nonStrikerX) * 0.75f);
            var laneB = Vector3.right * _nonStrikerX;
            for (var i = 0; i < runs; i++)
            {
                var last = i == runs - 1;
                var dive = last && runs >= 2 && _visRng.Chance(0.35);
                foreach (var (rig, lane, fromNear) in new[] { (_striker, laneA, i % 2 == 0), (_other, laneB, i % 2 == 1) })
                {
                    var clip = CricketClips.Run("run_single_crease_to_crease", rig.LeftHanded);
                    if (clip == null) continue;
                    var start = (fromNear ? near : far) + lane;
                    // The first run starts from where he stands (the crease, or his shot's finish).
                    rig.PlayRun(clip, start, fromNear ? Vector3.forward : Vector3.back, i == 0 ? rig.transform.position.x : (float?)null);
                    // A dive covers only the last strides: switch into it so it lands on the line.
                    var diveClip = dive ? CricketClips.Run("dive_into_crease", rig.LeftHanded) : null;
                    if (diveClip != null) StartCoroutine(After(Mathf.Max(0, clip.EventTime - diveClip.EventTime), () => rig.PlayRun(diveClip, start, fromNear ? Vector3.forward : Vector3.back)));
                }
                yield return new WaitForSeconds(runTime);
                if (Autoplay)
                {
                    var hips = _striker.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips);
                    if (hips != null) Debug.Log($"[APL match] run {i + 1}: striker hips at x {hips.position.x:0.00} z {hips.position.z:0.00} (lanes {laneA.x:0.00} / {laneB.x:0.00}, pitch ±1.52)");
                }
            }
            // Whoever ended up at the batting end is the striker's model now.
            if (runs % 2 == 1) (_striker, _other) = (_other, _striker);
        }

        /// <summary>Test runs: what the batter's animator is playing, and how far apart his hands are.</summary>
        private void LogBatter(string when)
        {
            var a = _striker.GetComponent<Animator>();
            var info = a.GetCurrentAnimatorStateInfo(0);
            var names = new[] { "Stance", "StanceLhb", "StanceLeft", "StanceRight", "Ready", "Stand", "01_forward_defence" };
            var name = names.FirstOrDefault(n => Animator.StringToHash(n) == info.shortNameHash) ?? info.shortNameHash.ToString();
            var l = a.GetBoneTransform(HumanBodyBones.LeftHand);
            var r = a.GetBoneTransform(HumanBodyBones.RightHand);
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            var bat = _striker.Bat;
            Debug.Log($"[APL match] {when}: state {name} t={info.normalizedTime:0.000} speed={info.speed} clips={_striker.UsesClips} hands {(l != null && r != null ? Vector3.Distance(l.position, r.position) : -1):0.00} m, hips y {(hips != null ? hips.position.y : -1):0.00}, facing {_striker.transform.rotation.eulerAngles.y:0}°, bat {(bat != null && hips != null ? (bat.position - hips.position).ToString("0.00") : "?")} from hips, down {(bat != null ? (bat.rotation * Vector3.down).ToString("0.00") : "?")}");
        }

        /// <summary>The umpire: idle, watching, or a signal (then back to idle).</summary>
        private void Umpire(string clip)
        {
            var u = Refs.Umpire;
            if (u == null || !CricketClips.HasUmpire(clip) || !u.HasState(0, Animator.StringToHash(clip))) return;
            u.CrossFadeInFixedTime(clip, 0.2f);
            if (clip.EndsWith("_loop")) return;
            StartCoroutine(After(3.2f, () =>
            {
                if (u.HasState(0, Animator.StringToHash("umpire_idle_bowlers_end_loop"))) u.CrossFadeInFixedTime("umpire_idle_bowlers_end_loop", 0.3f);
            }));
        }

        // ---------------------------------------------------------------- cast

        private CricketCast _cast = null!;
        private KeeperRig? _keeper;
        /// <summary>Which side of the stumps the non-striker waits (and runs) on: away from the bowler's run.</summary>
        private float _nonStrikerX = -0.9f;
        private Coroutine? _keeperPlay;
        private Dictionary<string, Animator> _side = new Dictionary<string, Animator>();
        private readonly Dictionary<string, BatterRig> _batters = new Dictionary<string, BatterRig>();

        /// <summary>
        /// The two sides in their kits and the umpire, in place of the scene's
        /// stand-ins: the whole fielding XI (any of them can be asked to bowl)
        /// and the whole batting order (two out at a time).
        /// </summary>
        private void MakeCast()
        {
            _side = _cast.Side(_match.Bowling.XI, _bowl, "Field");
            foreach (var a in _side.Values) a.gameObject.AddComponent<BowlerRig>();
            _cast.KeeperGear(_side[Keeper().Id], _bowl);
            foreach (var kv in _cast.Side(_match.Order.Select(b => b.Player), _bat, "Bat"))
            {
                _cast.BatterGear(kv.Value, _bat);
                _batters[kv.Key] = kv.Value.gameObject.AddComponent<BatterRig>();
                kv.Value.gameObject.SetActive(false);
            }
            var umpire = _cast.Umpire();
            if (Refs.Umpire != null)
            {
                umpire.transform.SetPositionAndRotation(Refs.Umpire.transform.position, Refs.Umpire.transform.rotation);
                Destroy(Refs.Umpire.gameObject);
            }
            Refs.Umpire = umpire;
            foreach (var a in new[] { Refs.Striker, Refs.NonStriker, Refs.Bowler, Refs.Keeper }.Concat(Refs.Fielders))
            {
                if (a != null) Destroy(a.gameObject);
            }
            Refs.Fielders = Array.Empty<Animator>();
        }

        private Player Keeper() => _match.Bowling.XI.FirstOrDefault(p => p.IsKeeper) ?? _match.Bowling.XI.Last();

        /// <summary>True once Start has built the HUD (dev captures wait on it before posing).</summary>
        public bool Ready => _hud != null;

        /// <summary>Test runs (-aplgallery): the striker alone at the crease, in his kit, for posing clips.</summary>
        public BatterRig GalleryBatter(bool leftHanded)
        {
            StopAllCoroutines();
            _hud.HideCard();
            _hud.SetPlaying(false);
            Roles();
            PlaceField();
            Batters();
            _striker.LeftHanded = leftHanded;
            Camera.LeftHanded = leftHanded;
            _striker.TakeGuard();
            _other.gameObject.SetActive(false);
            return _striker;
        }

        /// <summary>This over's bowler, the keeper, and the other nine in the field.</summary>
        private void Roles()
        {
            if (!_cast.Ready) return;
            var bowler = _match.CurrentBowler;
            var keeper = Keeper();
            Refs.Bowler = _side[bowler.Id];
            _bowler = Refs.Bowler.GetComponent<BowlerRig>();
            // Pace by bowler: how hard he runs in (rules-bowling).
            _bowler.RunUpScale = (float)Bowling.RunUpPace(Bowling.PaceOf(bowler));
            Refs.Keeper = _side[keeper.Id];
            _keeper = Refs.Keeper.GetComponent<KeeperRig>() ?? Refs.Keeper.gameObject.AddComponent<KeeperRig>();
            _keeper.Stance(KeeperUp(bowler));
            CricketCast.KeeperHeadgear(Refs.Keeper, bowler.BowlType == BowlType.Spin);
            // The bowler bowls bareheaded; whoever bowled last over gets his cap or hat back.
            foreach (var body in _side.Values) CricketCast.Bareheaded(body, body == Refs.Bowler);
            _fielders.Clear();
            foreach (var p in _match.Bowling.XI)
            {
                if (p.Id == bowler.Id || p.Id == keeper.Id) continue;
                _fielders.Add((_side[p.Id], _side[p.Id].transform.position));
            }
            Refs.Fielders = _fielders.Select(f => f.a).ToArray();
        }

        /// <summary>The two batters in, as the engine has them: on strike at the batting end.</summary>
        private void Batters()
        {
            if (!_cast.Ready) return;
            var on = _batters[_match.OnStrike.Player.Id];
            var off = _batters[_match.OffStrike.Player.Id];
            foreach (var b in _batters.Values) b.gameObject.SetActive(b == on || b == off);
            _striker = on;
            _other = off;
            // Both hands, the non-striker's too (his clips and bat socket are per hand).
            on.LeftHanded = _match.OnStrike.Player.LeftHanded;
            off.LeftHanded = _match.OffStrike.Player.LeftHanded;
        }

        // --------------------------------------------------------------- setup

        private void PlaceField()
        {
            // Who stands where is the engine's share-out (match-play: MatchDirector.FieldMoves SyncFieldBodies);
            // a man walking to his spot is left to his walk (one mover per body).
            var placeTimer = System.Diagnostics.Stopwatch.StartNew();
            var wasOn = SyncFieldBodies();
            var field = _match.Field.Fielders;
            var lh = _match.OnStrike.Player.LeftHanded;
            for (var i = 0; i < _fielders.Count; i++)
            {
                var a = _fielders[i].a;
                if (i >= field.Count)
                {
                    a.gameObject.SetActive(false);
                    continue;
                }
                a.gameObject.SetActive(true);
                if (Crossing(a)) continue;
                var (x, f) = field[i].Position;
                var p = GroundSpace.Field(x, f, lh);
                // On the field already and his new spot's a way off (the old bowler back in the field): he walks.
                if (WalkToNewSpot(a, wasOn, p)) continue;
                Loco(a, false);
                Crouch(i, a);
                a.transform.position = p;
                var toBat = new Vector3(0, 0, -GroundSpace.HalfPitch) - p;
                a.transform.rotation = Quaternion.LookRotation(new Vector3(toBat.x, 0, toBat.z));
                _fielders[i] = (a, p);
            }
            for (var i = 0; i < Refs.Stumps.Length && i < 3; i++)
            {
                Refs.Stumps[i].position = _stumpHome[i];
                Refs.Stumps[i].rotation = _stumpRot[i];
            }
            SlowLog("PlaceField", placeTimer);
        }

        /// <summary>Slips wait crouched; everyone else in the ready stance.</summary>
        private void Crouch(int i, Animator a)
        {
            var field = _match.Field.Fielders;
            if (i < field.Count && field[i].Name == "slip" && Body.Has(a, "slip_stance_loop")) a.CrossFadeInFixedTime("slip_stance_loop", 0.1f, 0, UnityEngine.Random.value);
        }

        private void PlaceOtherBatter()
        {
            // Reset everyone after the last ball (fielders back, stumps up, non-striker at his end).
            if (_keeperPlay != null) StopCoroutine(_keeperPlay);
            _keeperPlay = null;
            foreach (var c in _extras) if (c != null) StopCoroutine(c);
            _extras.Clear();
            // Fielding: everyone to where he waits for the run-up (MatchDirector.FieldMoves).
            ResetFielders();
            // The last ball's runs card is done (match-play: MatchHud.Bowling ClearRunCard).
            if (_hud != null) _hud.ClearRunCard(); // Start() places the batters before the HUD exists
            for (var i = 0; i < Refs.Stumps.Length && i < 3; i++)
            {
                Refs.Stumps[i].position = _stumpHome[i];
                Refs.Stumps[i].rotation = _stumpRot[i];
            }
            // Bails in the grooves: 0.72 m up from the turf. (_stumpHome is the stump's
            // centre, 0.355 m up — adding 0.72 to it floated them a foot above the stumps.)
            for (var i = 0; i < Refs.Bails.Length; i++)
            {
                var mid = _stumpHome[1];
                Refs.Bails[i].position = new Vector3(mid.x + (i == 0 ? -0.055f : 0.055f), 0.72f, mid.z);
                Refs.Bails[i].rotation = Quaternion.identity;
            }
            if (_keeper != null && _keeper.UsesClips) _keeper.Stance(KeeperUp(_match.CurrentBowler));
            // Test runs: up to spin / back to pace, as placed (match-play; the rater saw the same depth for both).
            if (Autoplay && Refs.Keeper != null) Debug.Log($"[APL match] keeper {(KeeperUp(_match.CurrentBowler) ? "UP" : "back")} for {_match.CurrentBowler.Name}: z {Refs.Keeper.transform.position.z + GroundSpace.HalfPitch:0.00} m behind the stumps line, clips {_keeper?.UsesClips}");
            else if (Refs.Keeper != null)
            {
                Loco(Refs.Keeper, false);
                Refs.Keeper.transform.SetPositionAndRotation(new Vector3(0, 0, -GroundSpace.HalfPitch - 2.6f), Quaternion.LookRotation(Vector3.forward));
            }
            _other.NonStriker(new Vector3(_nonStrikerX, 0, GroundSpace.HalfPitch - 1.3f));
        }
    }
}
