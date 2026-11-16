using System;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Owner v26 #17, the slidewatch run: after the root causes (Loco's restarted fades, the clamped gaits, the
    /// batters' lane hold), what's left is the pose itself moving a planted foot — the blend of two clips at the
    /// start and stop of every run (the stance's feet don't step while the body is carried), the spin bowlers'
    /// delivery strides as authored (la_orthodox_*, up to 5 m/s), the gather-and-throw clips steered onto the
    /// ball, the non-striker's idle faded into itself at another phase. So a foot that is planted STAYS planted:
    /// each foot's planted moment is read off the animated pose (low, and not moving up or down — the rule
    /// FootLock and the meters use), and the leg is bent to keep it there (a two-bone solve on hip-knee-ankle).
    /// Runs after FootLock (-100: the walks read the animated pose as before, untouched) and the rigs (0), before
    /// the slidewatch probe (10000) — so what's measured is what's drawn. Not on a batter while his bat is on its
    /// authored path (a shot's footwork is the shot), not during a replay. Off with <c>-aplnoslidefix</c>.
    /// <para>Deep area 1, round 1 (the meter reads the CONTACT point — the lowest of ankle and toe — and FootPin held
    /// only the ankle): (1) the held point is the contact point: the toe (the ball of the foot) when it's the lower,
    /// else the ankle (a heel strike); the leg reaches for wherever the ankle must be for that point to stay, so a heel
    /// rolling up over the ball no longer pushes the toe forward. (2) A held foot doesn't swivel: the animated foot's
    /// turn about the vertical since it landed is taken back out (up to <see cref="MaxTwist"/>°), so a blend from the
    /// stance's splayed feet into a walk's straight ones, or a body turning over a planted foot, no longer swings the
    /// toe round the ankle. (3) A foot the body has left behind (more than <see cref="Reach"/> away, or twisted past
    /// what an ankle turns) is no longer slid back under him over a tenth of a second: it STEPS there, lifted, over
    /// <see cref="StepTime"/> s. (4) Turns on the spot (TurnStep): one foot is the pivot — it swivels on its ball, which
    /// stays put — and the other steps round (<see cref="BeginPivot"/>, <see cref="Swing"/>).</para>
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class FootPin : MonoBehaviour
    {
        private static GameObject? _scanner;

        /// <summary>Puts a FootPin on every player's body in the match scene (checked once a second).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_scanner != null) return;
            _scanner = new GameObject("FootPins");
            DontDestroyOnLoad(_scanner);
            _scanner.AddComponent<FootPinScanner>();
        }

        /// <summary>One leg: its bones, whether its foot is held and where.</summary>
        private sealed class Leg
        {
            public Transform? Hip, Knee, Ankle, Toe;
            public float Low = float.MaxValue, PrevY = float.MinValue;
            public bool Held;
            /// <summary>The held contact point (world; x and z are used): the toe when <see cref="ToeContact"/>, else the ankle.</summary>
            public Vector3 Anchor;
            public bool ToeContact;
            /// <summary>The foot's heading on the ground (ankle to toe, flat) when it landed.</summary>
            public Vector3 Dir0;
            /// <summary>Where the ankle was held last frame, and the twist taken out (for the ease out of a hold).</summary>
            public Vector3 HeldAnkle;
            public float HeldTwist;
            /// <summary>1 while held, easing to 0 over <see cref="Release"/> s once lifted.</summary>
            public float Weight;
            /// <summary>Pivoting (a turn on the spot): this foot swivels on its ball until then.</summary>
            public float PivotUntil = -1f;
            /// <summary>Stepping (re-planting, or the turn's free foot): from where, how long, how far up.</summary>
            public float SwingT = -1f, SwingLen, SwingLift, SwingTwist;
            public Vector3 SwingFrom;
            /// <summary>The ankle as drawn last frame (after the solve).</summary>
            public Vector3 Drawn;
        }

        private const float Band = 0.04f, Rise = 0.35f, Reach = 0.25f, Release = 0.1f;

        /// <summary>The most a held foot's turn is taken out (°); past <see cref="MaxTwist"/> + 25 it steps instead.</summary>
        private const float MaxTwist = 35f;

        /// <summary>A left-behind foot's re-plant: this long, this high at the top (m).</summary>
        private const float StepTime = 0.16f, StepLift = 0.07f;

        private Animator _a = null!;
        private BatterRig? _batter;
        private readonly Leg[] _legs = { new Leg(), new Leg() };

        /// <summary>For the log (FootPinScanner): bodies pinned, feet held, let go when lifted, re-planted by a step (too far / twisted /
        /// out of the leg's reach), turn steps, holds dropped because he was placed (a jump), and the worst drawn-ankle miss of a hold (m).</summary>
        public static int Installed, Holds, Lifted, TooFar, Twisted, TurnSteps, Overreach, Placed;

        public static float WorstMiss;

        /// <summary>For the log: held frames drawn, and how many of them missed the hold by more than 3 cm (reviewer N11).</summary>
        public static int HeldFrames, MissFrames;

        /// <summary>Where the transform was last frame, and its facing (a jump over 0.5 m or 45° in a frame is a placement).</summary>
        private Vector3 _lastPos;
        private float _lastYaw;
        private bool _seen;

        private bool _init;

        /// <summary>
        /// Iteration 7 (reviewer B-3: 17 turns ran before FootPinScanner's 1 s scan reached the body): a pin can be added on
        /// demand (<see cref="On"/>) and works from that moment — its bones are found the first time anything asks.
        /// </summary>
        public static FootPin? On(Animator? a)
        {
            if (a == null || !a.isHuman) return null;
            var p = a.GetComponent<FootPin>() ?? a.gameObject.AddComponent<FootPin>();
            p.Init();
            return p;
        }

        private void Start() => Init();

        private void Init()
        {
            if (_init) return;
            _init = true;
            Installed++;
            _a = GetComponent<Animator>();
            _batter = GetComponent<BatterRig>();
            _legs[0].Hip = _a.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _legs[0].Knee = _a.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _legs[0].Ankle = _a.GetBoneTransform(HumanBodyBones.LeftFoot);
            _legs[0].Toe = _a.GetBoneTransform(HumanBodyBones.LeftToes);
            _legs[1].Hip = _a.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _legs[1].Knee = _a.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _legs[1].Ankle = _a.GetBoneTransform(HumanBodyBones.RightFoot);
            _legs[1].Toe = _a.GetBoneTransform(HumanBodyBones.RightToes);
        }

        /// <summary>Test runs: <c>-aplnofootpin</c> turns the pins off alone (the other slide fixes stay on), to A/B them.</summary>
        public static readonly bool Off = Environment.GetCommandLineArgs().Contains("-aplnofootpin");

        /// <summary>Pinning him now (the switch on, a live body, no replay, no shot's bat path — unless a turn asked for it).</summary>
        public bool Working => SlideFix.On && !Off && _a != null && _a.isActiveAndEnabled && !Strides.IsPaused
                               && !(_batter != null && _batter.OnBatPath && Time.time >= _forceUntil);

        private float _forceUntil = -1f, _swivelUntil = -1f;

        /// <summary>
        /// Iteration 8 (reviewer A1: the batter's after-run turn swivelled with no pin — his bat path's weight was still up): a
        /// turn on the spot asks for the pin for its length, whatever the bat path says.
        /// </summary>
        public void Force(float seconds) => _forceUntil = Mathf.Max(_forceUntil, Time.time + seconds);

        /// <summary>
        /// Iteration 8 (reviewer T-1: FootPin took the turn clip's own authored pivot swivel out, then forced "twisted"
        /// re-plants): while a turn clip plays, held feet may swivel as the clip turns them (no twist taken out, no twisted
        /// re-plant); the holds themselves stay.
        /// </summary>
        public void FreeSwivel(float seconds) => _swivelUntil = Mathf.Max(_swivelUntil, Time.time + seconds);

        /// <summary>
        /// Iteration 8 (reviewer T-1: at a turn clip's end the transform is re-seated under the turned body — about 180° in one
        /// frame — and that read as a placement, the holds dropped, the feet snapped 14-29 cm): the pose drawn is the same
        /// either side of a re-seat, so the holds (world points) carry through; this tells the placement check it wasn't one.
        /// </summary>
        /// <summary>He's been placed somewhere new (a walk-out from the gate): every hold let go now, before the next pose.</summary>
        public void DropHolds()
        {
            Drop();
            _seen = false;
        }

        public void Reseated()
        {
            _lastPos = transform.position;
            _lastYaw = transform.eulerAngles.y;
            _seen = true;
        }

        /// <summary>Leg <paramref name="leg"/> (0 left, 1 right): its held contact point (world), else its foot's lowest point if it's low, else null.</summary>
        public Vector3? Contact(int leg)
        {
            Init();
            var l = _legs[leg];
            if (l.Ankle == null) return null;
            // Iteration 7 (reviewer B-1: turns swung bodies metres — a Rebase / re-centre under the hips had moved him, and the
            // hold from BEFORE it was still taken as his pivot): a contact more than 0.5 m (flat) from his hips isn't under
            // him — that hold is dropped, and the animated foot answers.
            var hips = _a != null ? _a.GetBoneTransform(HumanBodyBones.Hips) : null;
            bool Under(Vector3 p) => hips == null || Flat(p - hips.position).magnitude <= 0.5f;
            if (l.Held)
            {
                if (Under(l.Anchor)) return l.Anchor;
                // (Note 7-2, considered: easing out of a hold that's over 0.5 m away would drag the foot that far over 0.1 s —
                // a visible slide — so it's let go at once; the foot is drawn where the pose has it.)
                l.Held = false;
                l.Weight = 0f;
                StaleDrops++;
            }
            var p = l.Toe != null && l.Toe.position.y < l.Ankle.position.y ? l.Toe.position : l.Ankle.position;
            return p.y < l.Low + Band + 0.02f && Under(p) ? p : (Vector3?)null;
        }

        /// <summary>For the log: holds dropped because the body had been placed away from them (B-1).</summary>
        public static int StaleDrops;

        /// <summary>For the log (B-2): forced re-plants per clip (the state he was in), so the eager ones show.</summary>
        public static readonly System.Collections.Generic.Dictionary<string, int> ReplantsByClip = new System.Collections.Generic.Dictionary<string, int>();

        private readonly System.Collections.Generic.List<AnimatorClipInfo> _clipInfo = new System.Collections.Generic.List<AnimatorClipInfo>();

        private void CountReplant(string why)
        {
            if (!Debug.isDebugBuild || _a == null) return;
            _clipInfo.Clear();
            _a.GetCurrentAnimatorClipInfo(0, _clipInfo);
            var clip = _clipInfo.Count > 0 && _clipInfo[0].clip != null ? _clipInfo[0].clip.name : "(none)";
            var key = clip + " " + why;
            ReplantsByClip.TryGetValue(key, out var n);
            ReplantsByClip[key] = n + 1;
        }

        /// <summary>Which leg is on the <paramref name="side"/> of him (world direction): 0 left, 1 right.</summary>
        public int LegToward(Vector3 side)
        {
            var l = _legs[0].Ankle;
            var r = _legs[1].Ankle;
            if (l == null || r == null) return 1;
            return Vector3.Dot(r.position - l.position, side) >= 0 ? 1 : 0;
        }

        /// <summary>The leg pivoting now (TurnStep), if one is.</summary>
        public int? PivotingLeg => Time.time < _legs[0].PivotUntil ? 0 : Time.time < _legs[1].PivotUntil ? 1 : (int?)null;

        /// <summary>A turn on the spot: leg <paramref name="leg"/> is the pivot for <paramref name="seconds"/> (held at its ball, free to swivel).</summary>
        public void BeginPivot(int leg, float seconds)
        {
            var l = _legs[leg];
            l.PivotUntil = Time.time + seconds;
            l.SwingT = -1f;
        }

        /// <summary>Leg <paramref name="leg"/> steps: lifted from where it's drawn now and put down where the pose has it, over <paramref name="seconds"/>.</summary>
        public void Swing(int leg, float seconds, float lift)
        {
            var l = _legs[leg];
            if (l.Ankle == null) return;
            // The turn the hold had taken out of the foot eases back in over the step (no swivel pop).
            l.SwingTwist = l.Held || l.Weight > 0f ? l.HeldTwist * (l.Held ? 1f : l.Weight) : 0f;
            l.Held = false;
            l.Weight = 0f;
            l.SwingFrom = l.Drawn != Vector3.zero ? l.Drawn : l.Ankle.position;
            l.SwingT = 0f;
            l.SwingLen = Mathf.Max(0.05f, seconds);
            l.SwingLift = lift;
            TurnSteps++;
        }

        private void Drop()
        {
            foreach (var l in _legs)
            {
                l.Held = false;
                l.Weight = 0f;
                l.PrevY = float.MinValue;
                l.SwingT = -1f;
                l.PivotUntil = -1f;
                l.Drawn = Vector3.zero;
            }
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        private void LateUpdate()
        {
            var dt = Time.deltaTime;
            if (!Working || dt <= 0f)
            {
                Drop();
                _seen = false;
                return;
            }
            // Iteration 2 (reviewer F2): placed (a jump of over 0.5 m or 45° in one frame — a cut, a Rebase, a teleport): the
            // holds are dropped, not stepped across the jump (a 0.16 s "step" metres long). The feet land afresh where they are.
            var yawNow = transform.eulerAngles.y;
            if (_seen && (Flat(transform.position - _lastPos).magnitude > 0.5f || Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yawNow)) > 45f))
            {
                Drop();
                Placed++;
            }
            _seen = true;
            _lastPos = transform.position;
            _lastYaw = yawNow;
            foreach (var l in _legs)
            {
                if (l.Hip == null || l.Knee == null || l.Ankle == null) continue;
                // The animated foot: its lowest point (ankle or toe), on the ground or not.
                var ankle = l.Ankle.position;
                var toe = l.Toe != null ? l.Toe.position : ankle;
                var rot = l.Ankle.rotation;
                var toeRot = l.Toe != null ? l.Toe.rotation : Quaternion.identity;
                var y = Mathf.Min(ankle.y, toe.y);
                l.Low = Mathf.Min(y, l.Low + 0.03f * dt);
                var vy = l.PrevY > float.MinValue ? Mathf.Abs(y - l.PrevY) / dt : 0f;
                l.PrevY = y;
                var down = y < l.Low + Band && vy < Rise;

                // A step under way (a re-plant, the turn's free foot): lifted from where it was to where the pose has it.
                if (l.SwingT >= 0f)
                {
                    l.SwingT += dt;
                    var s = Mathf.Clamp01(l.SwingT / l.SwingLen);
                    var e = Mathf.SmoothStep(0f, 1f, s);
                    var at = Vector3.Lerp(l.SwingFrom, ankle, e);
                    at.y = Mathf.Lerp(l.SwingFrom.y, ankle.y, e) + l.SwingLift * Mathf.Sin(Mathf.PI * s);
                    var sq = Quaternion.AngleAxis(l.SwingTwist * (1f - e), Vector3.up);
                    Solve(l, at, sq * rot, sq * toeRot);
                    l.Drawn = l.Ankle.position;
                    if (s >= 1f)
                    {
                        // Down where the pose has it: the next frame holds it there if it's on the ground.
                        l.SwingT = -1f;
                        l.PrevY = float.MinValue;
                    }
                    continue;
                }

                var pivoting = Time.time < l.PivotUntil || Time.time < _swivelUntil;
                if (down && !l.Held && l.Weight <= 0f)
                {
                    // Landed: hold its contact point here.
                    l.Held = true;
                    Holds++;
                    l.ToeContact = SlideFix.Round1 && toe.y < ankle.y; // (-aplslideround1off: the ankle, as before)
                    l.Anchor = l.ToeContact ? toe : ankle;
                    l.Dir0 = Flat(toe - ankle).normalized;
                    l.HeldTwist = 0f;
                }
                if (l.Held)
                {
                    // Its swivel since it landed, taken back out (a pivot's isn't: it turns on its ball).
                    var now = Flat(toe - ankle);
                    var raw = l.Dir0.sqrMagnitude > 0.5f && now.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(now, l.Dir0, Vector3.up) : 0f;
                    if (pivoting)
                    {
                        l.Dir0 = now.normalized;
                        raw = 0f;
                    }
                    var twist = SlideFix.Round1 ? Mathf.Clamp(raw, -MaxTwist, MaxTwist) : 0f;
                    var q = Quaternion.AngleAxis(twist, Vector3.up);
                    var off = q * (toe - ankle);
                    off.y = 0f;
                    // The contact passing from heel to ball (or back): re-anchored where the held foot has it now, no jump.
                    var wantToe = SlideFix.Round1 && toe.y < ankle.y;
                    if (wantToe != l.ToeContact)
                    {
                        var heldAnkle = l.ToeContact ? l.Anchor - off : l.Anchor;
                        l.Anchor = wantToe ? heldAnkle + off : heldAnkle;
                        l.ToeContact = wantToe;
                    }
                    var target = l.ToeContact ? l.Anchor - off : l.Anchor;
                    target.y = ankle.y; // the pose's own height: a heel lifting stays as animated
                    var gone = Flat(ankle - target).magnitude;
                    if (!down)
                    {
                        l.Held = false;
                        Lifted++;
                    }
                    else if (!SlideFix.Round1 && gone > Reach)
                    {
                        // (-aplslideround1off: let go and eased back to the pose over a tenth of a second, as before.)
                        l.Held = false;
                        TooFar++;
                    }
                    else if (gone > Reach || (SlideFix.Round1 && (Mathf.Abs(raw) > MaxTwist + 25f || OutOfReach(l, target))))
                    {
                        // Left behind (a turn, the body moving on): it steps to where he is, not slid there. Iteration 2 (reviewer
                        // F1): also when the held point is beyond the leg's reach (the solve would lock the leg straight and the
                        // drawn foot slide short of its hold, with no re-plant).
                        l.Held = false;
                        if (gone > Reach) { TooFar++; CountReplant("too far"); }
                        else if (Mathf.Abs(raw) > MaxTwist + 25f) { Twisted++; CountReplant("twisted"); }
                        else { Overreach++; CountReplant("out of reach"); }
                        l.Drawn = new Vector3(l.HeldAnkle.x, ankle.y, l.HeldAnkle.z);
                        l.Held = true; // (so the step starts from the hold's twist)
                        Swing(l == _legs[0] ? 0 : 1, StepTime, StepLift);
                        TurnSteps--; // (counted as a re-plant, not a turn step)
                        // Its first frame now: still where it was held.
                        var tq0 = Quaternion.AngleAxis(l.SwingTwist, Vector3.up);
                        Solve(l, l.SwingFrom, tq0 * rot, tq0 * toeRot);
                        l.Drawn = l.Ankle.position;
                        continue;
                    }
                    else
                    {
                        l.HeldAnkle = target;
                        l.HeldTwist = twist;
                    }
                }
                l.Weight = l.Held ? 1f : Mathf.Max(0f, l.Weight - dt / Release);
                if (l.Weight <= 0f)
                {
                    l.Drawn = ankle;
                    continue;
                }
                // Held (or easing out of the hold once it lifts): where the held foot has the ankle, turned as held.
                var hold = new Vector3(l.HeldAnkle.x, ankle.y, l.HeldAnkle.z);
                var tq = Quaternion.AngleAxis(l.HeldTwist * l.Weight, Vector3.up);
                Solve(l, Vector3.Lerp(ankle, hold, l.Weight), tq * rot, tq * toeRot);
                l.Drawn = l.Ankle.position;
                // Log only: how far the drawn ankle misses the hold (the leg couldn't reach it).
                if (l.Held && Debug.isDebugBuild)
                {
                    var miss = Flat(l.Drawn - hold).magnitude;
                    if (miss > WorstMiss) WorstMiss = miss;
                    HeldFrames++;
                    if (miss > 0.03f) MissFrames++;
                }
            }
        }

        /// <summary>
        /// The held ankle point is out of the leg's reach: beyond 99.5 % of its length, or beyond 97 % AND 3 cm further than
        /// the animated pose already reaches (a walk's own near-straight leg at heel strike isn't an over-reach). Checked on the
        /// animated pose, before the solve.
        /// </summary>
        private static bool OutOfReach(Leg l, Vector3 target)
        {
            // Iteration 7 (reviewer B-2: 60 % of holds ended in this forced step — the old test, beyond 97 % of the leg and 3 cm
            // more than the pose reached, fired on ordinary strides, whose stance leg is near-straight at heel strike and
            // toe-off): only a hold the leg can't reach at all — more than 2 cm beyond its full length — forces a step. Short of
            // that the solve straightens the leg and the drawn foot misses by under 2 cm (counted: "held frames missing").
            var hip = l.Hip!.position;
            var leg = (l.Knee!.position - hip).magnitude + (l.Ankle!.position - l.Knee.position).magnitude;
            return (target - hip).magnitude > leg + 0.02f;
        }

        /// <summary>
        /// Bend hip and knee so the ankle reaches <paramref name="target"/>: the knee opens or closes to the
        /// hip-to-target length (law of cosines, about the leg's own bend axis), then the hip swings the leg onto
        /// it. The foot then takes <paramref name="footRot"/> (and the toe <paramref name="toeRot"/>), world rotations.
        /// </summary>
        private static void Solve(Leg l, Vector3 target, Quaternion footRot, Quaternion toeRot)
        {
            var hip = l.Hip!;
            var knee = l.Knee!;
            var ankle = l.Ankle!;
            var a = hip.position;
            var b = knee.position;
            var c = ankle.position;
            var upper = (b - a).magnitude;
            var lower = (c - b).magnitude;
            if (upper < 1e-3f || lower < 1e-3f) return;
            var want = Mathf.Clamp((target - a).magnitude, Mathf.Abs(upper - lower) + 1e-3f, upper + lower - 1e-3f);
            var axis = Vector3.Cross(a - b, c - b);
            if (axis.sqrMagnitude > 1e-8f)
            {
                var now = Vector3.Angle(a - b, c - b);
                var then = Mathf.Acos(Mathf.Clamp((upper * upper + lower * lower - want * want) / (2f * upper * lower), -1f, 1f)) * Mathf.Rad2Deg;
                // Rotating about (a-b)x(c-b) turns (a-b) toward (c-b): the same turn applied to the shin opens the knee.
                knee.rotation = Quaternion.AngleAxis(then - now, axis.normalized) * knee.rotation;
            }
            var c2 = ankle.position;
            hip.rotation = Quaternion.FromToRotation(c2 - a, target - a) * hip.rotation;
            ankle.rotation = footRot;
            if (l.Toe != null) l.Toe.rotation = toeRot;
        }

        private void OnDisable() => Drop();
    }
}
