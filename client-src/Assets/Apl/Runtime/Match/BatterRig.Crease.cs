using System.Collections;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Owner v26 #25: "the batsman still looks like standing outside the crease — after a dive, completing a run,
    /// or even while playing (spin or something)". Root causes: (1) after a shot the stance was faded in WHERE THE
    /// TRANSFORM WAS — a charge down the track (or a reach for the ball) had moved the transform out of his ground,
    /// so he waited in his stance a metre or two up the pitch until the next ball's TakeGuard snapped him home; and a
    /// shot's own travel (the stride, the follow-through) was dragged back under the stance in the 0.5 s fade, feet
    /// sliding. (2) After a run he stood in the idle where the clip pulled up — the dive ends 0.78 m SHORT of the
    /// popping crease (running.json: end 16.9 m, crease 17.68 m), the bat over the line and the body out of his ground.
    /// (3) The non-striker's move to his place (the other side of the stumps for the next ball) was a 0.5 s glide.
    /// Now each of them WALKS (the shared Strides walk: the body carried by the planted foot, no skating): the striker
    /// back to his guard, then into the stance; a runner who pulled up out of his ground (or far from his place) to
    /// his place behind the line; the non-striker to his spot. Off with <c>-aplnowalkin</c> (the old ways).
    /// "[APL crease]" lines: each walk, and per ball where both batters ended (MatchDirector.Crease.cs).
    /// </summary>
    public sealed partial class BatterRig
    {
        public static readonly bool WalkInOn = !System.Environment.GetCommandLineArgs().Contains("-aplnowalkin");

        /// <summary>The popping crease at the striker's end (z).</summary>
        public static float NearCrease => -GroundSpace.HalfPitch + 1.22f;

        /// <summary>Further from his place than this (m, body on the ground) and he walks there; the walk's pace (m/s).</summary>
        public const float WalkInFrom = 0.3f, WalkInPace = 1.3f;

        /// <summary>His body at least this far behind the line (m) is in his ground (at guard it's <see cref="GuardBack"/>).</summary>
        public const float InsideBy = 0.15f;

        private Coroutine? _walkIn;

        private string WalkLoop => (LeftHanded ? "lhb_" : "rhb_") + "walk_out_bat_tucked_loop";

        private bool CanWalkIn => WalkInOn && UsesClips && isActiveAndEnabled && Body.Has(_animator, WalkLoop);

        /// <summary>
        /// v26 #25, the stance itself: the clip origin (between the feet) is this far BEHIND the popping crease at guard.
        /// The ready stance has the feet 0.24 m either side of the origin along the pitch (batting_v2.py READY: back foot
        /// −0.24, front +0.24, the front toe ~+0.31): at 0.30 back the front toe is at the line, the front foot's ball
        /// 6 cm behind it, the back foot 0.54 m inside — as batters stand. A front-foot stride still goes over the line.
        /// </summary>
        public const float GuardBack = 0.30f;

        /// <summary>The guard's origin in the engine's frame (m in front of the batting stumps): the popping crease less <see cref="GuardBack"/>.</summary>
        public const double GuardZ = 1.22 - GuardBack;

        /// <summary>His guard at the striker's end (the clip origin, as TakeGuard places it; less this stance's fit).</summary>
        private Vector3 GuardSpot => new Vector3(LeftHanded ? 0.26f : -0.26f, 0, NearCrease - GuardBack - _guardFit);

        /// <summary>His guard's origin in the engine's frame (m in front of the batting stumps): where the swing meets the ball from (MatchDirector.Meet).</summary>
        public double HomeZ => _home.z + GroundSpace.HalfPitch;

        /// <summary>Where the front toe (the furthest foot / toe bone toward the bowler) sits at guard: this far behind the line (m).</summary>
        public const float GuardToe = 0.03f;

        /// <summary>
        /// Per rig, per stance (state, hand, mirrored): how much further back (m, never forward — the fit only ever
        /// pushes back from <see cref="GuardBack"/>) the origin goes so the front toe is behind the line. Measured from
        /// the SETTLED stance (LogGuardRoutine: in the guard state, out of any blend, no ritual), not the instant pose
        /// (that measure read +0.22 and moved every guard forward ~0.2 m; bodies are scaled per batter, so per rig).
        /// </summary>
        private readonly System.Collections.Generic.Dictionary<string, float> _guardFits = new System.Collections.Generic.Dictionary<string, float>();
        private float _guardFit;
        private float _guardAt = -1f;

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        private string GuardKey =>$"{GuardState()}/{(LeftHanded ? "L" : "R")}/{_flipped}";

        /// <summary>The front toe's distance behind the near popping crease (m, + behind), from the bones as posed now.</summary>
        private float FrontToeDepth()
        {
            var depth = float.MaxValue;
            foreach (var b in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
            {
                var t = _animator.GetBoneTransform(b);
                if (t != null) depth = Mathf.Min(depth, NearCrease - t.position.z);
            }
            return depth;
        }

        /// <summary>Just placed at guard (TakeGuard): this stance's fit as measured on an earlier ball (none yet: 0.30 back).</summary>
        private void FitGuardToCrease()
        {
            _guardAt = Time.time;
            _guardFits.TryGetValue(GuardKey, out var fit);
            if (Mathf.Abs(fit - _guardFit) < 0.001f) return;
            _guardFit = fit;
            _home = GuardSpot;
            transform.position = _home;
        }

        /// <summary>He's in the guard state itself, fully (no blend, no ritual, no shot): the settled stance.</summary>
        private bool InGuardPose()
        {
            if (_ritual != null || _carry || _walkIn != null || _animator.IsInTransition(0)) return false;
            var h = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            return h == GuardState();
        }

        /// <summary>Each ball, once the stance has settled: re-fit if his front toe is over the line, and the "at guard" line.</summary>
        private void LogGuardSoon()
        {
            if (isActiveAndEnabled) StartCoroutine(LogGuardRoutine());
        }

        private IEnumerator LogGuardRoutine()
        {
            yield return new WaitForSeconds(0.25f);
            // Only when he's in his stance at his guard (the openers' walk-out wakes the rig and walks him on; a ritual).
            if (!InGuardPose() || Flat(HipsGround() - _home).magnitude > 0.5f) yield break;
            var toe = FrontToeDepth();
            if (toe != float.MaxValue && toe < 0f)
            {
                var key = GuardKey;
                var fit = Mathf.Clamp(_guardFit + GuardToe - toe, 0f, 0.6f);
                _guardFits[key] = fit;
                Debug.Log($"[APL crease] guard fit {name} {key}: settled front toe {toe:+0.00;-0.00} m behind the line, guard {GuardBack + fit:0.00} m back");
                // Still the moment he took guard (a quarter second on): put right now, then measured again.
                if (Time.time - _guardAt < 0.5f && Mathf.Abs(fit - _guardFit) > 0.001f)
                {
                    _guardFit = fit;
                    _home = GuardSpot;
                    transform.position = _home;
                    yield return null;
                    yield return null;
                    if (!InGuardPose()) yield break;
                }
            }
            Debug.Log(CreaseLine(name, "at guard"));
        }

        /// <summary>
        /// The non-striker is still walking to his side of the stumps (to his spot and turned to face): the run-up waits
        /// for this (MatchDirector.BowlerEnd NonStrikerSettled), and BackUp leaves him to finish it.
        /// </summary>
        public bool CrossingToPlace => _toPlace && _walkIn != null;

        private bool _toPlace;

        private void StopWalkIn()
        {
            _toPlace = false;
            if (_walkIn == null) return;
            StopCoroutine(_walkIn);
            _walkIn = null;
            // The walk's feet (FootLock) were his: let go, or they'd keep carrying him.
            var feet = GetComponent<FootLock>();
            if (feet != null && feet.Active) feet.End();
            _animator.speed = 1f;
        }

        /// <summary>After a shot, a leave or a reaction (SettleRoutine): out of his ground, or off his guard — walk back to it. False: near enough, the old fade.</summary>
        private bool WalkBackToGuard()
        {
            if (!CanWalkIn) return false;
            var body = HipsGround();
            var off = Flat(body - _home).magnitude;
            if (off < WalkInFrom) return false;
            // Up out of the shot where he finished (the fade keeps his body still), then the walk.
            _carry = true;
            _ik = 0f;
            _animator.SetBool(MirrorParam, false);
            _animator.speed = 1f;
            FadeTo(NonStrikerIdle, 0.35f, Random.value, body, Facing);
            StartWalkIn(_home, true, $"back to his guard after the shot, {off:0.00} m", 0.4f);
            return true;
        }

        /// <summary>
        /// Deep area 1 (reviewer RC2, the batter's "*run_single…&gt;*75_non_striker_idle_loop +turn" rows, 1.0-4.6 m/s): after a
        /// run the idle used to fade in with a 180° turn of the transform over 0.45 s — the planted feet spun round. Now:
        /// <list type="bullet">
        /// <item>a walk-in due (short of the line, off his place): the idle fades in where he faces, and the walk (its own
        /// stepping turn first, Strides.Walk) takes him — its end faces him;</item>
        /// <item>else, with the owner's rhb_/lhb_turn_180_after_run on his animator and a turn of 120° or more: the clip
        /// (TurnStep, handing over to the non-striker's idle under his turned body);</item>
        /// <item>else the idle fades in where he faces, then a stepping turn (TurnStep: pivot foot held, the other steps).</item>
        /// </list>
        /// Any placement (StopGlide: FadeTo, TakeGuard, NonStriker…) stops it.
        /// </summary>
        private void SettleAndTurnAfterRun(Vector3 hips, Quaternion facing)
        {
            var turn = Quaternion.Angle(transform.rotation, facing);
            var clip = (LeftHanded ? "lhb_" : "rhb_") + "turn_180_after_run";
            // (Iteration 9, reviewer note 8-1: PARKED — the clip ends 31 cm off our non-striker idle and its hand-over read
            // 1.71 m/s; the pinned stepping turn below is used until it's re-made to end on our idle.)
            if (TurnAfterRunClip && !WalkInAfterRunNeeded() && turn >= 120f && Body.Has(_animator, clip))
            {
                StopGlide();
                _runTurn = StartCoroutine(TurnStep.Turn(_animator, facing, 0.9f, null, NonStrikerIdle, clips: true));
                return;
            }
            FadeTo(NonStrikerIdle, 0.45f, Random.value, hips, transform.rotation);
            if (WalkInAfterRun()) return;
            if (turn >= 3f) _runTurn = StartCoroutine(TurnAfterFade(facing, turn));
        }

        private Coroutine? _runTurn;

        /// <summary>The owner's rhb_/lhb_turn_180_after_run is offered after a run (parked: false until it ends on our idle).</summary>
        /// <remarks>Iteration 11: back ON — build 4 showed the pinned step-turn on the batter's idle worse (RA 0.364, RC 0.489)
        /// than build 3 with the clip (crease walk 0.067 / 0.347); the clip's hand-over (2 plants at 1.71) is the lesser cost.</remarks>
        private const bool TurnAfterRunClip = true;

        private IEnumerator TurnAfterFade(Quaternion facing, float turn)
        {
            yield return new WaitForSeconds(0.45f);
            yield return TurnStep.Turn(_animator, facing, Mathf.Lerp(0.3f, 0.6f, turn / 180f));
            _runTurn = null;
        }

        /// <summary>Would <see cref="WalkInAfterRun"/> walk him (short of the line, or a step or more off his place)?</summary>
        private bool WalkInAfterRunNeeded()
        {
            if (!CanWalkIn) return false;
            var body = HipsGround();
            var far = body.z > 0;
            var depth = far ? body.z - FarCrease : NearCrease - body.z;
            var spot = far ? new Vector3((body.x < 0 ? -1 : 1) * 0.9f, 0, FarCrease + InsideCrease) : GuardSpot;
            return depth < InsideBy || Flat(body - spot).magnitude >= WalkInFrom;
        }

        /// <summary>After a run (SettleAfterRun, already fading into the idle where he pulled up): short of the line, or a step or more from his place — walk there (the near end: to his guard). True if he walks.</summary>
        private bool WalkInAfterRun()
        {
            if (!CanWalkIn) return false;
            var body = HipsGround();
            var far = body.z > 0;
            var depth = far ? body.z - FarCrease : NearCrease - body.z;
            var spot = far ? new Vector3((body.x < 0 ? -1 : 1) * 0.9f, 0, FarCrease + InsideCrease) : GuardSpot;
            var off = Flat(body - spot).magnitude;
            if (depth >= InsideBy && off < WalkInFrom) return false;
            if (!far) _home = spot;
            StartWalkIn(spot, !far, $"after the run: body {depth:+0.00;-0.00} m behind the line, {off:0.00} m from his place", 0.5f);
            return true;
        }

        /// <summary>The non-striker to his place (NonStriker): walked, when he's standing under his transform and it's a step or more.</summary>
        private bool WalkToNonStrikerSpot(Vector3 at)
        {
            if (!CanWalkIn) return false;
            var body = HipsGround();
            if (Flat(body - transform.position).magnitude > 0.3f) return false;
            var off = Flat(body - at).magnitude;
            if (off < WalkInFrom) return false;
            StartWalkIn(at, false, $"to the non-striker's place, {off:0.00} m", 0f);
            _toPlace = true;
            return true;
        }

        private void StartWalkIn(Vector3 to, bool guard, string why, float delay)
        {
            StopWalkIn();
            _walkIn = StartCoroutine(WalkInRoutine(new Vector3(to.x, 0, to.z), guard, why, delay));
        }

        private IEnumerator WalkInRoutine(Vector3 to, bool guard, string why, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            StopGlide();
            _carry = true;
            _free = false;
            _ik = 0f;
            Debug.Log($"[APL crease] {name} walks in {Flat(to - transform.position).magnitude:0.00} m ({why})");
            SlideScene.Tag(_animator, "batter crease walk (to guard / his place)");
            yield return Strides.Walk(_animator, to, WalkInPace, WalkLoop, 1.31f);
            _walkIn = null;
            // v26 #17 (slidewatch round 3: lhb_walk_out_bat_tucked_loop 1.42 m/s mean): the walk fades out at the slow
            // playback it ended on — at 1x the fading walk stepped at full speed under a body that had stopped.
            var endSpeed = _animator.speed;
            _animator.speed = SlideFix.On ? Mathf.Min(1f, endSpeed) : 1f;
            if (guard) GuardFrom(0.45f);
            else
            {
                // Facing down the pitch at the striker, bat grounded (as NonStriker places him).
                _animator.SetBool(MirrorParam, false);
                var face = Quaternion.Euler(0, 180f, 0) * Facing;
                var turn = Quaternion.Angle(transform.rotation, face);
                // (Iteration 11: iteration 9's "fade in where he faces, then step round on the idle" is reverted — build 4's
                // batter idle +turn rows regressed to 0.36-0.49; build 3's turn-in-the-fade measured better.)
                FadeTo(NonStrikerIdle, 0.4f, Random.value, to, face);
                _ = turn;
            }
            // Turned to face: a backing-up can start from here (the idle's fade is 0.4 s).
            yield return new WaitForSeconds(0.45f);
            _toPlace = false;
            yield return new WaitForSeconds(0.05f);
            _animator.speed = 1f;
            Debug.Log(CreaseLine(name, guard ? "at his guard" : "at his place"));
        }

        /// <summary>Into the stance at his guard from wherever the walk left him facing: the turn and the pose blend together (FadeTo), a cut for the mirrored loop.</summary>
        private void GuardFrom(float fade)
        {
            _carry = false;
            _free = false;
            _face = Vector3.forward;
            Mirror();
            var guard = GuardState();
            if (guard == StanceRight && _mirroredGuard)
            {
                transform.position = _home;
                PlayGuard(0f);
                return;
            }
            var state = guard == StanceLeft ? "StanceLeft" : guard == StanceRight ? "StanceRight" : guard == StanceLhb ? "StanceLhb" : "Stance";
            // The frozen defence holds its guard frame: as many seconds in as the guard pose (FadeTo offsets in the state's seconds).
            var at = guard == StanceState || guard == StanceLhb ? (_guardTime ?? 0f) / Mathf.Max(0.01f, StateLength(state)) : Random.value;
            var turn = Quaternion.Angle(transform.rotation, GuardRotation(guard));
            if (SlideFix.Round1 && turn > 20f)
            {
                // Deep area 1, iteration 8 (reviewer: "walk_out>00_stance_idle +turn" 0.9-1.15 m/s — the stance faded in while the
                // transform turned to the guard's facing, the planted feet spun round): the stance fades in where he faces, then
                // he steps round to his guard (TurnStep, pinned).
                FadeTo(state, fade, at, _home, transform.rotation);
                _runTurn = StartCoroutine(TurnAfterFade(GuardRotation(guard), turn));
                return;
            }
            FadeTo(state, fade, at, _home, GuardRotation(guard));
        }

        /// <summary>The "[APL crease]" line: where his body is against the popping crease at the end he's at, and in or out.</summary>
        public string CreaseLine(string who, string what = "settled")
        {
            var body = HipsGround();
            var far = body.z > 0;
            var crease = far ? FarCrease : NearCrease;
            var depth = far ? body.z - crease : crease - body.z;
            // His furthest foot toward the other end.
            var foot = float.MaxValue;
            foreach (var b in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
            {
                var t = _animator.GetBoneTransform(b);
                if (t != null) foot = Mathf.Min(foot, far ? t.position.z - crease : crease - t.position.z);
            }
            // At guard the front toe is the judge (at or behind the line; the body is meant to be ~0.1 m behind it); else the body.
            var inside = what == "at guard" ? foot >= 0f : depth >= InsideBy;
            var state = _walkIn != null ? "walking in" : _carry ? "bat in hand" : "stance";
            return $"[APL crease] {who} {what} at z={body.z:0.00} (crease z={crease:0.00}) {(inside ? "inside" : "OUTSIDE")}: body {depth:+0.00;-0.00} m behind the line, front foot {(foot == float.MaxValue ? 0 : foot):+0.00;-0.00}, {state}";
        }
    }
}
