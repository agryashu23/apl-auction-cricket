using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Client.Ui;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The broadcast direction of a ball: the camera running in with the bowler
    /// and settling into the broadcast view by the release, the ball's speed, a
    /// quick look at the shot's follow-through with its name and timing, the
    /// player cards when a batter or a new bowler starts, the between-overs card.
    /// The wicket and boundary sequences, the replays and the DRS are in
    /// <c>MatchDirector.Moments.cs</c>.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private ReplayRecorder _tape = null!;
        private ReplayPlayer _replays = null!;
        private HawkeyeView _hawkView = null!;

        /// <summary>Players whose card has been shown this match (intros once each).</summary>
        private readonly HashSet<string> _introduced = new HashSet<string>();

        /// <summary>The viewer tapped to skip the moment on screen.</summary>
        private bool _skip;

        /// <summary>The clip the batter played this ball (its name is the shot's: "05_cover_drive").</summary>
        private string? _shotName;

        /// <summary>Game time the ball was hit (or passed the bat), and the moment it was all over (landed, caught, stumps).</summary>
        private float _contactTime = -1, _eventTime = -1;

        /// <summary>Test runs film this ball from the batter's close-up: no broadcast direction.</summary>
        private bool _closeUp;

        /// <summary>Everyone the replays record (and Hawk-Eye hides).</summary>
        private readonly List<Animator> _castBodies = new List<Animator>();

        /// <summary>Once, after the cast and the HUD: the tape, Hawk-Eye, the HUD's broadcast layer.</summary>
        private void PresentationStart()
        {
            _hud.BroadcastInit();
            _hud.Skipped += () => _skip = true;
            _hud.Wagons = CurrentWagons;
            _hud.WagonBooks = WagonBooks;
            _hud.Scorecard = () => (_match, BattingSide, FieldingSide);
            _tape = gameObject.AddComponent<ReplayRecorder>();
            var bodies = new List<Animator>();
            bodies.AddRange(_side.Values);
            bodies.AddRange(_batters.Values.Select(b => b.GetComponent<Animator>()));
            if (!_cast.Ready)
            {
                bodies.AddRange(Refs.Fielders);
                bodies.AddRange(new[] { Refs.Striker, Refs.NonStriker, Refs.Bowler, Refs.Keeper }.Where(a => a != null)!);
            }
            if (Refs.Umpire != null) bodies.Add(Refs.Umpire);
            MakeSquareLeg();
            if (_squareLeg != null) bodies.Add(_squareLeg);
            foreach (var a in bodies.Where(a => a != null).Distinct())
            {
                _castBodies.Add(a);
                _tape.AddBody(a);
            }
            _tape.AddProp(_ball);
            foreach (var b in _batters.Values) _tape.AddProp(b.Bat);
            if (!_cast.Ready)
            {
                _tape.AddProp(_striker.Bat);
                _tape.AddProp(_other.Bat);
            }
            foreach (var s in Refs.Stumps) _tape.AddProp(s);
            foreach (var b in Refs.Bails) _tape.AddProp(b);
            _hawkView = HawkeyeView.Create();
            AudioStart();
            _replays = new ReplayPlayer(_tape, _hud, Camera, _ball, _trail);
            _replays.LineFor = ReplayLineNow; // the ball's line of action, from the engine's path (MatchDirector.ReplayLine.cs)
            // characters (FLD-1): walks hold while a replay is on air. (Holding on the tape's own Playing flag too
            // made it worse in the focus1 plain run — reverted.)
            Strides.Paused = () => _replays != null && _replays.Running;
            // Occluder-safe replays: the cast's hips (ReplayPlayer.Guard.cs).
            _replays.Bodies = () => _castBodies.Where(a => a != null).Select(a => Hips(a));
            // The batters running, in an inset, while the main camera is out with the ball.
            RunningInset.Create(Camera.GetComponent<Camera>(), _hud,
                // (Not over the run-out view: the main camera is already on the crease and the inset hid it — deep area 3, R2.)
                () => _runnerBusy && !_replays.Running && Time.timeScale > 0f && !Camera.StumpsOnAir,
                () => _striker != null && _other != null ? (Hips(_striker).position, Hips(_other).position) : ((Vector3, Vector3)?)null);
        }

        /// <summary>
        /// Everyone on the field now on the replay tape: the second innings' bodies (Recast makes new
        /// ones for both sides) weren't, so its replays showed no runner and no fielders moving (r12
        /// E182-184, E373-375). The tape ignores a body it already has.
        /// </summary>
        private void TapeEveryone()
        {
            if (_tape == null) return;
            var now = _side.Values.Concat(_batters.Values.Where(b => b != null).Select(b => b.GetComponent<Animator>()));
            foreach (var a in now)
            {
                if (a == null) continue;
                _tape.AddBody(a);
                if (!_castBodies.Contains(a)) _castBodies.Add(a);
            }
            foreach (var b in _batters.Values) if (b != null) _tape.AddProp(b.Bat);
        }

        // --------------------------------------------------------- the ball

        /// <summary>
        /// A ball starts: the broadcast pieces from the last one away, the cards for
        /// a new bowler or a batter who hasn't faced, then the camera behind the
        /// bowler at his mark (or the test runs' close-up).
        /// </summary>
        private IEnumerator BallOpens(Vector3 releaseAt, bool closeUp)
        {
            Camera.ResetHolds();
            // A replay or Hawk-Eye cut short last ball: its state cleared, the clock running (r12 hang).
            if (_replays.Running) _replays.Exit();
            _hud.ClockReset("the ball's opening");
            var tapeT = StallProbe.Start();
            TapeEveryone();
            StallProbe.End("tape everyone", tapeT);
            _hud.ClearBroadcast();
            _caughtAt = -1f;
            // The ball out of the close-ups: left where the last ball (or innings) ended it hung beside the
            // new batter in his intro (r9 f240-245). Parked under the ground until it's in the bowler's hand.
            if (_ball != null)
            {
                _ball.position = new Vector3(releaseAt.x, -5f, releaseAt.z);
                if (_trail != null) _trail.Clear();
            }
            // A new innings starts its own book of who bowled which over.
            if (_match.Log.Count == 0)
            {
                _hud.OverBowlers.Clear();
                _overPitches.Clear();
                _overTracks.Clear();
                AudioSides();
            }
            _hud.OverBowlers[_match.Over] = _match.CurrentBowler;
            _shotName = null;
            _facing = _match.OnStrike.Player;
            CommentaryBallStarts();
            if (_squareLegLeft != _facing.LeftHanded)
            {
                _squareLegLeft = _facing.LeftHanded;
                PlaceSquareLeg(_facing.LeftHanded);
            }
            _contactTime = _eventTime = -1;
            _closeUp = closeUp;
            if (!closeUp)
            {
                var bowler = _match.CurrentBowler;
                if (!_introduced.Contains(bowler.Id) && _bowler != null) yield return ShowIntro(bowler, _bowler.GetComponent<Animator>(), 2.4f);
                var batter = _match.OnStrike.Player;
                if (!_introduced.Contains(batter.Id) && _striker != null) yield return ShowIntro(batter, _striker.GetComponent<Animator>(), 2.4f);
            }
            if (_match.FreeHit && !closeUp)
            {
                // After a no-ball: the umpire twirls a finger, and the graphic says so.
                Umpire("umpire_free_hit");
                // The centre banner, not the big bottom-right card: that sat on the bowler's delivery chips (r10 f272, f296).
                _hud.Banner("FREE HIT", "only a run-out can get him out", UiKit.Accent, 2f);
            }
            if (closeUp) Camera.Show(MatchCamera.Mode.Batter, cut: Camera.Current != MatchCamera.Mode.Batter);
            else FollowRunUp(releaseAt, cut: true);
        }

        /// <summary>
        /// The run-up: the camera behind and above the bowler's bowling shoulder,
        /// moving in with him (his hips, so the clips' baked run is followed), and
        /// easing into the broadcast view as he nears the crease — there by the
        /// release, so the batting view is the same every ball.
        /// </summary>
        private void FollowRunUp(Vector3 releaseAt, bool cut = false)
        {
            if (_bowler == null) return;
            var hips = Hips(_bowler);
            var start = float.NaN;
            var frame0 = Time.frameCount;
            var side = releaseAt.x >= 0 ? 1f : -1f;
            var endZ = releaseAt.z + 0.6f;
            Camera.TrackLens(() =>
            {
                var h = hips.position;
                // Where the run starts is only known once the clip has put him there.
                if (float.IsNaN(start) && Time.frameCount > frame0 + 1) start = Mathf.Max(h.z, endZ + 1f);
                var k = float.IsNaN(start) ? 0f : Mathf.InverseLerp(start, endZ, h.z);
                var w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.9f, k));
                var pos = new Vector3(h.x + 0.8f * side, 3.0f, h.z + 6.5f);
                var look = new Vector3(h.x * 0.4f, 0.9f, -4f);
                return (Vector3.Lerp(pos, MatchCamera.BroadcastPos, w), Vector3.Lerp(look, MatchCamera.BroadcastLook, w), Mathf.Lerp(40f, MatchCamera.BroadcastFov, w));
            }, smooth: cut ? 0.2f : 0.14f, cut: cut);
        }

        /// <summary>Out of his hand: the broadcast view (the run-up shot is already there), and the speed once it's reached the batter.</summary>
        private void Released(Delivery d, float toBat)
        {
            if (Camera.Current == MatchCamera.Mode.Custom && !Camera.Busy) Camera.Show(MatchCamera.Mode.Broadcast);
            SquareLegWatching(true);
            var kmh = d.SpeedKmh;
            StartCoroutine(After(toBat + 0.3f, () => _hud.SpeedReadout(kmh)));
        }

        // ------------------------------------------------------------ the shot

        /// <summary>
        /// Contact: the shot's name and timing ("COVER DRIVE · PERFECT"), and — when
        /// the ball's going far enough that nothing's missed — a moment on the
        /// batter's follow-through before the ball-follow (or the fielding camera,
        /// which waits its turn behind this shot) takes over.
        /// </summary>
        private void Showcase(BallResult r, float flight, bool follow)
        {
            _contactTime = Time.time;
            // From here until it's dead, whatever plain or fielding shot is on keeps the ball in the picture.
            var ball = _ball;
            Camera.KeepInFrame = () => ball.gameObject.activeInHierarchy ? ball.position : (Vector3?)null;
            var name = BroadcastStats.ShotName(_shotName);
            if (name.Length > 0)
            {
                var grade = r.Edge ? "EDGED" : BroadcastStats.Grade(r.Timing);
                // (No ms here: the timing stamp over the batter is the one timing number — MatchHud.Batting.)
                _hud.ShotCaption(name, grade, r.Edge ? UiKit.Bad : BroadcastStats.GradeColor(r.Timing));
            }
            if (r.Caught || r.Wicket || Camera.Busy || _striker == null || _closeUp) return;
            if (r.Six || r.Launch > 20) _msfx.Swell(r.Six ? 2.0f : 1.6f, 3f);
            // Only when it's still going to be in the air afterwards: a look at the finish costs
            // nothing then; on anything along the ground the camera stays on the ball.
            if (!(r.Six || (r.Launch > 20 && flight >= 2.6f))) return;
            StartCoroutine(ShowcaseShot(follow));
        }

        private IEnumerator ShowcaseShot(bool follow)
        {
            var hips = Hips(_striker);
            var off = _striker.LeftHanded ? -1f : 1f;
            // From the off side in front of him (his face and the bat's finish), or over the keeper's shoulder.
            var behind = _visRng.Chance(0.35);
            Camera.Track(() =>
            {
                var h = hips.position;
                return behind
                    ? (new Vector3(h.x - off * 2.4f, 2.4f, h.z - 4.2f), new Vector3(h.x + off * 0.6f, 1.0f, h.z + 3f))
                    : (new Vector3(h.x + off * 3.6f, 1.45f, h.z + 2.2f), new Vector3(h.x, 1.05f, h.z));
            }, fov: 34f, smooth: 0.08f, cut: true, priority: MatchCamera.Moment);
            var mine = Camera.Shots;
            yield return new WaitForSeconds(0.6f);
            if (Camera.Shots != mine) yield break;
            Camera.Release(MatchCamera.Moment);
            // Nobody asked for the camera meanwhile: back to the ball.
            if (Camera.Shots != mine) yield break;
            Camera.Target = _ball;
            Camera.Show(follow ? MatchCamera.Mode.Follow : MatchCamera.Mode.Broadcast);
        }

        // --------------------------------------------------------- the cards

        /// <summary>
        /// A player's card on a close-up of him (the reference's intros): who he
        /// is, how he bats or bowls, his numbers. The pre-match walk-out calls this
        /// for the openers; the director itself for each new batter and each
        /// bowler's first over (once a player). Tap skips.
        /// </summary>
        public IEnumerator ShowIntro(Player player, Animator body, float seconds = 2.6f)
        {
            _introduced.Add(player.Id);
            if (body == null) yield break;
            var batting = _match.Order.Any(b => b.Player.Id == player.Id);
            var info = batting ? BroadcastStats.Batter(player, _match, BattingSide) : BroadcastStats.Bowler(player, _match, FieldingSide);
            if (!batting) CricketCast.BowlerIntroPose(body, Bowling.LeftArm(player)); // characters: upright half of the ball-polish loop
            StadiumBigScreen.Event(Surname(player.Name), UnityEngine.Color.white); // characters: his name up on the big screens
            yield return CardOn(body, info, seconds);
        }

        /// <summary>The card and the close-up, held for <paramref name="seconds"/> (or until a tap).</summary>
        private IEnumerator CardOn(Animator body, PlayerCardInfo info, float seconds, bool walking = false)
        {
            _hud.Broadcasting(true);
            _hud.SetSkippable(true);
            _skip = false;
            var hips = Hips(body);
            Camera.Track(() =>
            {
                var (f, right) = Facing(body);
                var chest = hips.position + Vector3.up * 0.35f;
                // In front of him, a little to his right, so he stands right of centre with the card on the left.
                var pos = chest + f * (walking ? 4.6f : 3.4f) + right * 0.9f + Vector3.up * 0.15f;
                return (pos, chest + right * 0.55f);
            }, fov: 30f, smooth: walking ? 0.25f : 0.15f, cut: true, priority: MatchCamera.Moment);
            StallProbe.Mark($"card on: {info.Player.Name}");
            var cardT = StallProbe.Start();
            _hud.ShowPlayerCard(info);
            StallProbe.End("player card", cardT);
            for (var until = Time.time + seconds; Time.time < until && !_skip;) yield return null;
            StallProbe.Mark("card off");
            _hud.HidePlayerCard();
            _hud.SetSkippable(false);
            _hud.Broadcasting(false);
            Camera.Release(MatchCamera.Moment);
        }

        /// <summary>Which way a body faces (square to its hips and shoulders) and its right, on the ground.</summary>
        private static (Vector3 forward, Vector3 right) Facing(Animator a)
        {
            Vector3 Across(HumanBodyBones l, HumanBodyBones r)
            {
                var lt = a.GetBoneTransform(l);
                var rt = a.GetBoneTransform(r);
                return lt != null && rt != null ? rt.position - lt.position : Vector3.zero;
            }
            var right = a.isHuman ? Across(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg) + Across(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm) : Vector3.zero;
            right.y = 0;
            if (right.sqrMagnitude < 1e-4f) right = a.transform.right;
            right.Normalize();
            return (Vector3.Cross(right, Vector3.up).normalized, right);
        }

        /// <summary>
        /// The end of an over: the broadcast card (score and chase, both batters,
        /// the bowler's figures, the partnership, this over) over the wide shot.
        /// </summary>
        private IEnumerator OverCard()
        {
            // Every ball of the over on one tracking view first (owner v26 #20: MatchDirector.OverTracking.cs).
            yield return OverTracking();
            var last = _hud.OverBowlers.TryGetValue(_match.Over - 1, out var p) ? p : _match.CurrentBowler;
            Camera.ResetHolds();
            Camera.Show(MatchCamera.Mode.Wide);
            _hud.Broadcasting(true);
            _hud.SetSkippable(true);
            _skip = false;
            _hud.ShowOverCard(_match, BattingSide, FieldingSide, last);
            if (_overPitches.Count > 0) _hud.ShowPitchMap(_overPitches.ToArray());
            _overPitches.Clear();
            for (var until = Time.time + 3.8f; Time.time < until && !_skip;) yield return null;
            _hud.HideOverCard();
            _hud.HidePitchMap();
            _hud.SetSkippable(false);
            _hud.Broadcasting(false);
        }
    }
}
