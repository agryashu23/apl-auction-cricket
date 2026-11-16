using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The batter and his bat. With the batting clips (Cricket controller) he
    /// takes guard in the stance, shapes up as the ball is bowled, and plays
    /// the chosen shot timed so its contact frame lands as bat meets ball, and
    /// settles back into the stance when the clip is done. The bat is laid along
    /// his two hands every frame (the clips lock both hands to the handle, so
    /// that line survives retargeting where a hand's rotation doesn't), its face
    /// toward the bowler in the stance and toward the shot once he plays.
    /// Without the clips, a swing drawn in code with hand IK.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed partial class BatterRig : MonoBehaviour
    {
        /// <summary>
        /// Which hand he bats with — set for both batters, the non-striker too (his
        /// idle, backing-up and running clips are per hand, and so is the bat's socket).
        /// </summary>
        public bool LeftHanded
        {
            get => _leftHanded;
            set
            {
                _leftHanded = value;
                _socket = value ? _socketLeft : _socketRight;
            }
        }

        private bool _leftHanded;

        private static readonly int StanceState = Animator.StringToHash("Stance");
        private static readonly int StanceLeft = Animator.StringToHash("StanceLeft");
        private static readonly int StanceRight = Animator.StringToHash("StanceRight");
        private static readonly int StanceLhb = Animator.StringToHash("StanceLhb");
        private static readonly int MirrorParam = Animator.StringToHash("Mirror");

        private Animator _animator = null!;
        private Transform _bat = null!;
        private Vector3 _grip, _batDir = Vector3.down;
        /// <summary>Where the bat's face points (ground direction): the bowler, then the shot.</summary>
        private Vector3 _face = Vector3.forward;
        private Coroutine? _settle, _after;

        /// <summary>The bat's place in the bottom hand (rotation, handle-top offset), from the pack's reference rig.</summary>
        private static readonly Dictionary<string, (Quaternion q, Vector3 v)> _sockets = new Dictionary<string, (Quaternion q, Vector3 v)>();
        private (Quaternion q, Vector3 v)? _socket, _socketRight, _socketLeft;

        /// <summary>Running: the lane (x) his hips are held to, eased in from where he stood.</summary>
        private (float from, float to, float t0)? _lane;

        /// <summary>The middle of the blade — where the ball is at contact.</summary>
        public Vector3 BatMiddle => _grip + _batDir * 0.5f;

        /// <summary>Where the bat is to be moved toward the ball for the meeting (world offset; zero the rest of the time).</summary>
        public Vector3 BatNudge { get; set; }

        /// <summary>The meeting offset drawn this frame (eased toward <see cref="BatNudge"/>, at most 3 m/s).</summary>
        public Vector3 BatNudgeNow => _nudge;

        private Vector3 _nudge;

        /// <summary>The bat object (test logging).</summary>
        public Transform Bat => _bat;
        private float _ik = 1f;
        private bool _carry;
        /// <summary>Celebrating: the bat in the bottom hand only, the top hand off the handle.</summary>
        private bool _free;
        private Coroutine? _swing;
        private Vector3 _home;

        /// <summary>The batting clips are there (else the code-drawn swing).</summary>
        public bool UsesClips { get; private set; }

        /// <summary>Left-handed shots of their own ("lhb_" clips); else left-handers mirror the right-handed set.</summary>
        private bool _nativeLeft, _guardLoop;

        /// <summary>
        /// The right-handed stance loop is the left-handed one mirrored (no "00_stance_idle_loop"
        /// yet): a mirrored clip has the bowler on the batter's other side, so for it he's turned
        /// round (<see cref="_flipped"/>) and turned back the moment anything else plays.
        /// </summary>
        private bool _mirroredGuard, _flipped;

        /// <summary>A right-handed clip's name for this batter.</summary>
        private string Named(string clip) => LeftHanded && _nativeLeft ? "lhb_" + clip : clip;

        private void Mirror() => _animator.SetBool(MirrorParam, LeftHanded && !_nativeLeft);

        private static Vector3 Up => Vector3.forward;
        private Vector3 Off => LeftHanded ? Vector3.left : Vector3.right;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _bat = MakeBat().transform;
            BuildTrackMap();
            UsesClips = _animator.runtimeAnimatorController != null && _animator.HasState(0, StanceState);
            _nativeLeft = UsesClips && _animator.HasState(0, Animator.StringToHash("lhb_02_straight_drive"));
            _guardLoop = UsesClips && _animator.HasState(0, StanceRight);
            _mirroredGuard = _guardLoop && !_animator.HasState(0, Animator.StringToHash("00_stance_idle_loop"));
        }

        /// <summary>The way to face for the guard state: the mirrored loop turns him round.</summary>
        private Quaternion GuardRotation(int guard) =>
            guard == StanceRight && _mirroredGuard ? Facing * Quaternion.Euler(0, 180f, 0) : Facing;

        /// <summary>Back to the clips' own facing before any of them plays; true if he was turned round (so no cross-fade).</summary>
        private bool Unflip()
        {
            if (!_flipped) return false;
            _flipped = false;
            transform.rotation = Facing;
            return true;
        }

        /// <summary>Into the guard state: turned round for the mirrored loop (a cut, since the pose faces the other way).</summary>
        private void PlayGuard(float fade)
        {
            var guard = GuardState();
            var rotation = GuardRotation(guard);
            var flip = guard == StanceRight && _mirroredGuard;
            transform.rotation = rotation;
            if (flip || fade <= 0f)
            {
                _flipped = flip;
                _animator.Play(guard, 0, GuardTime(guard));
            }
            else _animator.CrossFadeInFixedTime(guard, fade, 0, GuardTime(guard));
        }

        /// <summary>
        /// The stance to wait in: this hand's idle loop when the pack has one,
        /// else frame 1 of the forward defence — the very pose every shot clip
        /// starts from, so nothing jumps when he plays.
        /// </summary>
        private int GuardState()
        {
            if (_guardLoop && _animator.HasState(0, LeftHanded ? StanceLeft : StanceRight)) return LeftHanded ? StanceLeft : StanceRight;
            return LeftHanded && _nativeLeft && _animator.HasState(0, StanceLhb) ? StanceLhb : StanceState;
        }

        /// <summary>
        /// Something already placed him before his first frame (the openers' walk-out wakes the rig and sets him
        /// jogging from the gate): Start must not then take guard — that put him at the crease, 63 m away, and the
        /// walk's teleport guard dragged him back (Strides, r11 log ×4) — nor leave his animator in a stance.
        /// </summary>
        private bool _told;

        private void Start()
        {
            var told = _told;
            var before = _animator != null ? _animator.GetCurrentAnimatorStateInfo(0) : default;
            if (UsesClips)
            {
                // The FBX import lands the clips' bowler direction at −X: a quarter turn puts it down the
                // pitch (+Z), for either hand. (Measuring it from the straight drive's hands gave 59°–94°
                // from body to body — retargeting noise — so the exact mapping is used instead.)
                _facingRight = _facingLeft = Quaternion.Euler(0, 90f, 0);
                MeasureFacing(false);
                MeasureFacing(true);
                _guardTime ??= MeasureGuardTime();
                // Both hands' sockets: who's left-handed is only known once he's in (and the
                // non-striker's hand used never to be set, so a left-hander could run with his
                // right hand's socket on his left hand — the bat out at an angle).
                _socketRight = MeasureSocket(false);
                _socketLeft = MeasureSocket(true);
                _socket = LeftHanded ? _socketLeft : _socketRight;
                // The measurements leave the animator in the other hand's straight drive at contact; the guard
                // below cross-fades OUT of that, so for its fade the bat was drawn from that track (8 m off his
                // hands on a new batter — the batting capture's grip log). Start the guard from a plain stance.
                var still = LeftHanded ? "StanceLeft" : "StanceRight";
                if (told && before.fullPathHash != 0)
                {
                    // Back to what he was doing (the walk-out's loop, mid-stride), not a stance.
                    _animator.Play(before.fullPathHash, 0, before.normalizedTime);
                    _animator.Update(0f);
                }
                else if (Body.Has(_animator, still))
                {
                    _animator.Play(still, 0, 0f);
                    _animator.Update(0f);
                }
                Debug.Log($"[APL] batter facing: right-handed yaw {_facingRight.eulerAngles.y:0}°, left-handed yaw {_facingLeft.eulerAngles.y:0}°; guard pose at {_guardTime:0.00}s of the forward defence; socket {(_socket != null ? "yes" : "no")}");
            }
            if (!told) TakeGuard();
        }

        /// <summary>
        /// Seconds into the forward defence held as the stance when there's no idle
        /// loop: frame 1 has the bat grounded and the body tall, which from the
        /// broadcast camera reads as a man holding a stick; a little way into the
        /// backlift — bat up, weight set, before the stride — reads as a batter
        /// waiting. Found from the clip itself (where the hands have risen 40% of
        /// the way to the top of the backlift), once for every body.
        /// </summary>
        private static float? _guardTime;

        private float MeasureGuardTime()
        {
            var clip = CricketClips.Find("01_forward_defence");
            if (clip == null) return 0f;
            var pos = transform.position;
            var rot = transform.rotation;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _animator.SetBool(MirrorParam, false);
            float Hands(float t)
            {
                _animator.Play(clip.Name, 0, t / Mathf.Max(0.01f, clip.Length));
                _animator.Update(0f);
                var l = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
                var r = _animator.GetBoneTransform(HumanBodyBones.RightHand);
                return l != null && r != null ? (l.position.y + r.position.y) / 2 : 0f;
            }
            const float step = 1f / 30f;
            var h0 = Hands(0f);
            var peak = h0;
            for (var t = step; t < clip.ContactTime; t += step) peak = Mathf.Max(peak, Hands(t));
            var want = h0 + 0.12f * (peak - h0);
            var guard = 0f;
            for (var t = step; t < clip.ContactTime; t += step)
            {
                if (Hands(t) < want) continue;
                guard = t;
                break;
            }
            transform.SetPositionAndRotation(pos, rot);
            return guard;
        }

        /// <summary>The bottom hand grips this far below the top of the handle (the packs' reference rig).</summary>
        private const float GripFromTop = 0.16f;

        // ------------------------------------------------------------- bat track
        // The bat as the generator authored it, frame by frame, and both hands IK'd
        // onto its handle. Unity's humanoid retargeting drops the hands' twist, so a
        // bat socketed to the hand bone wanders from pose to pose (and through the
        // body); the authored path doesn't. Clips without a track (running,
        // reactions, celebrations) fall back to the socket.

        /// <summary>Animator states that carry a bat path: state hash → track, frozen at frame 0 (the Stance states), loops.</summary>
        private readonly Dictionary<int, (BatTrack track, bool frozen, bool loop)> _tracks = new Dictionary<int, (BatTrack, bool, bool)>();
        /// <summary>
        /// This frame's bat from the tracks, in the world, and how much of the pose it
        /// owns (1: all of it; through a fade between a state with a track and one
        /// without — a run into the non-striker's idle — the fade's weight, the rest
        /// going to the hand socket). Worked out in the IK pass, used in LateUpdate.
        /// </summary>
        private BatPose _track;
        private float _trackWeight;

        /// <summary>The bat on a track, in the world: top of the handle, axis down the blade, face normal, and both hands' IK goals.</summary>
        private struct BatPose
        {
            public Vector3 Top, Axis, Face, BottomHand, TopHand;
            /// <summary>Wrist toward the knuckles, as the generator gripped it (across the handle, 19° down it).</summary>
            public Vector3 Fingers;
            public bool TopOn;
        }

        /// <summary>
        /// Where a state's clip origin is in the world. Normally the transform; while
        /// <see cref="Glide"/> moves the transform under a fade, the state fading out
        /// still sits where the transform was and the one fading in where it's going.
        /// </summary>
        private (Vector3 pos, Quaternion rot)? _fromFrame, _toFrame;

        private void BuildTrackMap()
        {
            _tracks.Clear();
            void Add(string state, string clip, bool frozen = false)
            {
                var t = CricketClips.BatTrackFor(clip);
                if (t != null && t.Frames > 0) _tracks[Animator.StringToHash(state)] = (t, frozen, clip.EndsWith("_loop"));
            }
            foreach (var s in CricketClips.Shots)
            {
                Add(s.Name, s.Name);
                Add("lhb_" + s.Name, s.Name);
            }
            Add("00_stance_idle_loop", "00_stance_idle_loop");
            Add("lhb_00_stance_idle_loop", "00_stance_idle_loop");
            Add("StanceRight", "00_stance_idle_loop");
            Add("StanceLeft", "00_stance_idle_loop");
            Add("Stance", "01_forward_defence", frozen: true);
            Add("StanceLhb", "01_forward_defence", frozen: true);
            // The ceremony's bat-holding clips (walk-outs, guard, intro, warm-ups, settles): tracked as rhb_;
            // a left-hander plays the lhb_ clip and the track is mirrored like every other.
            foreach (var c in new[] { "take_guard", "walk_out_bat_carried_loop", "walk_out_bat_tucked_loop", "walk_out_helmet_in_hand_loop", "put_helmet_on", "intro_idle_loop", "settle_after_run_single", "settle_after_dive", "settle_after_drag", "warmup_shadow_shots", "warmup_bat_stretch", "warmup_jump_and_tap" })
            {
                Add("rhb_" + c, "rhb_" + c);
                Add("lhb_" + c, "rhb_" + c);
            }
            foreach (var extra in new[] { "75_non_striker_idle_loop", "76_backing_up", "77_backing_up_ready_loop", "78_play_and_miss_off", "79_inside_edge_onto_pad", "80_bat_pad_pop", "81_chopped_on", "82_charge_down_the_track", "83_move_across_off", "84_move_to_leg_side" })
            {
                Add(extra, extra);
                Add("lhb_" + extra, extra);
            }
        }

        /// <summary>The track under the animator state, interpolated to its time, placed in the world from <paramref name="frame"/>.</summary>
        private bool Sample(AnimatorStateInfo info, (Vector3 pos, Quaternion rot) frame, out BatPose pose)
        {
            pose = default;
            if (!_tracks.TryGetValue(info.shortNameHash, out var e)) return false;
            var n = e.track.Frames;
            var nt = e.frozen ? 0f : e.loop ? Mathf.Repeat(info.normalizedTime, 1f) : Mathf.Clamp01(info.normalizedTime);
            var f = nt * (n - 1);
            var i = Mathf.Clamp(Mathf.FloorToInt(f), 0, n - 1);
            var j = Mathf.Min(i + 1, n - 1);
            var k = f - i;
            var grip = Vector3.Lerp(e.track.Grip[i], e.track.Grip[j], k);
            var a = Vector3.Lerp(e.track.Axis[i], e.track.Axis[j], k).normalized;
            var nrm = Vector3.Lerp(e.track.Face[i], e.track.Face[j], k).normalized;
            // Both hands onto the handle as the generator placed them: the wrist 7.5 cm back along
            // the fingers from its grip point, a little either side of the handle.
            var across = Vector3.Cross(a, nrm);
            var fingers = (across + a * 0.35f).normalized;
            var handleTop = grip - a * 0.17f;
            pose.Top = ToWorld(handleTop, true, frame);
            pose.Axis = ToWorld(a, false, frame).normalized;
            pose.Face = ToWorld(nrm, false, frame).normalized;
            pose.BottomHand = ToWorld(grip - fingers * 0.075f - nrm * 0.03f, true, frame);
            pose.TopHand = ToWorld(handleTop + a * 0.065f - fingers * 0.075f + nrm * 0.03f, true, frame);
            pose.Fingers = ToWorld(fingers, false, frame).normalized;
            pose.TopOn = e.track.TopHand[i];
            return true;
        }

        /// <summary>
        /// The bat this frame from the tracks and the share of the pose they own: the
        /// current state's, blended with the next state's through a fade; a fade to or
        /// from a state without a track hands over to the socket with the fade's weight
        /// (it used to snap straight to the track, which sat where the transform was
        /// while the body was still metres away finishing its run).
        /// </summary>
        private float TrackPose(out BatPose pose)
        {
            var here = (transform.position, transform.rotation);
            var has = Sample(_animator.GetCurrentAnimatorStateInfo(0), _fromFrame ?? here, out pose);
            if (!_animator.IsInTransition(0)) return has ? 1f : 0f;
            var w = Mathf.Clamp01(_animator.GetAnimatorTransitionInfo(0).normalizedTime);
            var next = Sample(_animator.GetNextAnimatorStateInfo(0), _toFrame ?? here, out var p2);
            if (!next) return has ? 1f - w : 0f;
            if (!has)
            {
                pose = p2;
                return w;
            }
            pose.Top = Vector3.Lerp(pose.Top, p2.Top, w);
            pose.Axis = Vector3.Slerp(pose.Axis, p2.Axis, w).normalized;
            pose.Face = Vector3.Slerp(pose.Face, p2.Face, w).normalized;
            pose.BottomHand = Vector3.Lerp(pose.BottomHand, p2.BottomHand, w);
            pose.TopHand = Vector3.Lerp(pose.TopHand, p2.TopHand, w);
            pose.Fingers = Vector3.Slerp(pose.Fingers, p2.Fingers, w).normalized;
            pose.TopOn = w < 0.5f ? pose.TopOn : p2.TopOn;
            return 1f;
        }

        /// <summary>
        /// Where the middle of the bat will be at <paramref name="clipTime"/> (s) of <paramref name="clip"/>, from its
        /// track, with him where he stands now (the meeting aims the bat at the ball's line from this:
        /// MatchDirector.Meet.cs). Null when the clip has no track.
        /// </summary>
        public Vector3? TrackMiddleAt(string clip, float clipTime)
        {
            if (!_tracks.TryGetValue(Animator.StringToHash(Named(clip)), out var e)) return null;
            var n = e.track.Frames;
            var f = Mathf.Clamp(clipTime * 30f, 0f, n - 1);
            var i = Mathf.Clamp(Mathf.FloorToInt(f), 0, n - 1);
            var j = Mathf.Min(i + 1, n - 1);
            var a = Vector3.Lerp(e.track.Axis[i], e.track.Axis[j], f - i).normalized;
            var grip = Vector3.Lerp(e.track.Grip[i], e.track.Grip[j], f - i);
            var here = (transform.position, transform.rotation);
            return ToWorld(grip - a * 0.17f, true, here) + ToWorld(a, false, here).normalized * 0.5f;
        }

        /// <summary>
        /// Generator frame → world through a clip origin (the batter's transform). The
        /// FBX import lands the generator's (x, y, z) at (−x, z, −y) in the clip's own
        /// frame; a left-hander's clips are the mirror, y → −y.
        /// </summary>
        private Vector3 ToWorld(Vector3 g, bool point, (Vector3 pos, Quaternion rot) frame)
        {
            var local = Vector3.Scale(new Vector3(-g.x, g.z, LeftHanded ? g.y : -g.y), transform.lossyScale);
            return point ? frame.pos + frame.rot * local : frame.rot * local;
        }

        /// <summary>
        /// Where the bat sits in the bottom hand, from the pack's own numbers: at a
        /// shot's contact frame the pack says where the middle of the bat is
        /// (sweet_spot_at_contact_m), and the bottom hand is on the handle, so the
        /// line from this body's hand to that spot is the bat — face to the bowler
        /// for the straight drive. Measured once per body and hand, on this body:
        /// the hands' bone axes differ between skeletons, so a reference rig's grip
        /// doesn't carry across (tried; the bat stood out over the shoulder).
        /// </summary>
        private (Quaternion q, Vector3 v)? MeasureSocket(bool left)
        {
            var clip = CricketClips.Find("02_straight_drive");
            if (clip == null || clip.Sweet == Vector3.zero) return null;
            var key = (_animator.avatar != null ? _animator.avatar.name : name) + (left ? "/L" : "/R");
            if (_sockets.TryGetValue(key, out var known)) return known;
            (Quaternion q, Vector3 v)? socket = null;
            var pos = transform.position;
            var rot = transform.rotation;
            // The batter's frame: between his feet on the crease, +Z at the bowler, +X his off side.
            transform.SetPositionAndRotation(Vector3.zero, left ? _facingLeft : _facingRight);
            _animator.SetBool(MirrorParam, left && !_nativeLeft);
            _animator.Play(left && _nativeLeft ? "lhb_" + clip.Name : clip.Name, 0, clip.ContactTime / Mathf.Max(0.01f, clip.Length));
            _animator.Update(0f);
            var hand = _animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (hand != null)
            {
                var sweet = new Vector3((left ? -1 : 1) * clip.Sweet.x, clip.Sweet.y, clip.Sweet.z);
                var blade = (sweet - hand.position).normalized;
                var face = Vector3.ProjectOnPlane(Vector3.forward, blade);
                if (face.sqrMagnitude > 0.01f)
                {
                    var batRot = Quaternion.LookRotation(face.normalized, -blade);
                    var top = hand.position - blade * GripFromTop;
                    var inv = Quaternion.Inverse(hand.rotation);
                    socket = (inv * batRot, inv * (top - hand.position));
                    _sockets[key] = socket.Value;
                    Debug.Log($"[APL] bat socket {key}: hand at contact {hand.position:0.00}, sweet spot {sweet:0.00}, blade {blade:0.00}");
                }
            }
            transform.SetPositionAndRotation(pos, rot);
            _animator.SetBool(MirrorParam, LeftHanded && !_nativeLeft);
            return socket;
        }

        /// <summary>Where in the stance state to hold: the loop anywhere, the frozen defence at the guard pose.</summary>
        private float GuardTime(int state) => state == StanceState || state == StanceLhb
            ? (_guardTime ?? 0f) / Mathf.Max(0.01f, CricketClips.Find("01_forward_defence")?.Length ?? 3f)
            : Random.value;

        /// <summary>Which way the clips face in the world, per hand (the mirrored set faces the other way).</summary>
        private Quaternion _facingRight = Quaternion.LookRotation(Vector3.right), _facingLeft = Quaternion.LookRotation(Vector3.left);
        private Quaternion Facing => LeftHanded ? _facingLeft : _facingRight;

        /// <summary>
        /// The clips' frame isn't documented beyond "origin between the feet", so
        /// find which way they face: play the forward defence to its contact frame
        /// and see where the bat's middle is — the pack's notes put it 0.615 m toward
        /// the bowler and 0.276 m to the off side — then turn the body so that
        /// lands down the pitch. (Facing the clips' forward at the off side, as
        /// before, had the batter standing front-on.)
        /// </summary>
        private Quaternion MeasureFacing(bool left)
        {
            var fallback = Quaternion.LookRotation(left ? Vector3.left : Vector3.right);
            var clip = CricketClips.Find("01_forward_defence");
            if (clip == null) return fallback;
            var pos = transform.position;
            var rot = transform.rotation;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _animator.SetBool(MirrorParam, left && !_nativeLeft);
            _animator.Play(left && _nativeLeft ? "lhb_" + clip.Name : clip.Name, 0, clip.ContactTime / clip.Length);
            _animator.Update(0f);
            var top = _animator.GetBoneTransform(left ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            var bottom = _animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var middle = Vector3.zero;
            if (top != null && bottom != null)
            {
                var along = bottom.position - top.position;
                if (along.sqrMagnitude > 0.0004f) middle = (top.position + bottom.position) / 2 + along.normalized * 0.4f;
            }
            transform.SetPositionAndRotation(pos, rot);
            var measured = new Vector3(middle.x, 0, middle.z);
            if (measured.sqrMagnitude < 0.01f) return fallback;
            var expected = new Vector3((left ? -1 : 1) * 0.276f, 0, 0.615f);
            return Quaternion.Euler(0, Vector3.SignedAngle(measured, expected, Vector3.up), 0);
        }

        /// <summary>Back in the stance at the crease, side-on, facing the off side.</summary>
        public void TakeGuard()
        {
            _told = true;
            _pickedUp = false;
            ResetFootwork();
            ShowBat();
            _gripLogUntil = Time.time + 7f;
            // A toss handshake's IK helper left on him (the captain) would take his right hand off the bat.
            foreach (var h in GetComponents<PreMatchHandshake>()) Destroy(h);
            StopReach();
            if (_swing != null) StopCoroutine(_swing);
            _swing = null;
            StopSettling();
            _carry = false;
            _free = false;
            _face = Vector3.forward;
            _ik = UsesClips ? 0f : 1f;
            if (UsesClips)
            {
                // Clip origin: between the feet, GuardBack behind the popping crease (v26 #25: on the line, the
                // stance had the front foot 0.24 m over it — "standing outside the crease"), feet just to leg of middle.
                _home = GuardSpot;
                Mirror();
                transform.position = _home;
                PlayGuard(0f);
                // Any stance (mocap, left-handed, mirrored): his front toe put at the line, measured (BatterRig.Crease.cs).
                FitGuardToCrease();
                LogGuardSoon();
                return;
            }
            _home = new Vector3(LeftHanded ? 0.32f : -0.32f, 0, -GroundSpace.HalfPitch + 0.95f);
            Pose(StanceGrip(), StanceDir());
            transform.position = _home;
            transform.rotation = Quaternion.LookRotation(Off, Vector3.up);
        }

        // ------------------------------------------------------------ shot timing
        // The ball is bowled: he presses (a front-foot trigger to a full ball, back and
        // across to a short one — the first frames of the forward or back-foot defence,
        // which every shot clip shares) and lifts the bat, arriving at the top of the
        // backlift as the tap's moment comes, and waits there. The tap then joins the
        // chosen shot from its own backlift, the downswing sped up to land the contact
        // frame on the ball, and from contact on the clip runs at its own speed: no
        // lunge that snaps into a pull at the last moment.

        /// <summary>
        /// Furthest into the defence the shape-up goes before waiting (clip s): the trigger done
        /// and the bat on its way up, the stride not yet taken. (0.62 s held him with the front
        /// foot 27 cm in the air mid-stride — the "fencing lunge" while he waited for the ball;
        /// the shot's own clip takes the stride.)
        /// </summary>
        public const float ShapeHold = 0.43f;

        /// <summary>The shape-up's pace is fitted to the ball within these, then it creeps on at <see cref="HoverSpeed"/> while he waits.</summary>
        public const float ShapeMinSpeed = 0.7f, ShapeMaxSpeed = 1.6f, HoverSpeed = 0.05f, ShapeBlend = 0.18f;

        /// <summary>
        /// A shot joins its clip at least this long (clip s) before contact — from its
        /// backlift, never later — and blends in over <see cref="ShotBlend"/> s; the
        /// downswing runs between <see cref="MinSwingSpeed"/> and <see cref="MaxSwingSpeed"/>
        /// times the clip's own speed to land contact on time (later entry if even the
        /// fastest can't make it, from earlier if the tap was very early).
        /// </summary>
        public const float ShotLead = 0.35f, ShotBlend = 0.14f, MinSwingSpeed = 0.75f, MaxSwingSpeed = 1.6f;   // round 10: 2.0 made the downswing a 0.13 s wrist flick

        private Coroutine? _clock;

        /// <summary>Top of each clip's backlift (s): where the bat's toe is highest before contact, from its bat track.</summary>
        private static readonly Dictionary<string, float> _backlift = new Dictionary<string, float>();

        private static float BackliftTop(ShotClip clip)
        {
            if (_backlift.TryGetValue(clip.Name, out var known)) return known;
            var top = clip.ContactTime - ShotLead;
            var track = CricketClips.BatTrackFor(clip.Name);
            if (track != null && track.Frames > 0)
            {
                var best = float.MinValue;
                var last = Mathf.Min(track.Frames - 1, Mathf.FloorToInt((clip.ContactTime - 0.15f) * 30f));
                for (var i = 9; i <= last; i++)
                {
                    // Generator frame: Z up; the toe ~0.68 m down the blade from the bottom hand.
                    var toe = track.Grip[i].z + track.Axis[i].z * 0.68f;
                    if (toe <= best) continue;
                    best = toe;
                    top = i / 30f;
                }
            }
            return _backlift[clip.Name] = top;
        }

        /// <summary>
        /// Drive the animator's clock through one state: <paramref name="speed"/> until the
        /// state's clip time reaches <paramref name="until"/>, then <paramref name="after"/>.
        /// Ends (speed 1) as soon as anything else plays.
        /// </summary>
        private void Clock(string state, float length, float speed, float until, float after)
        {
            StopClock();
            _animator.speed = speed;
            _clock = StartCoroutine(ClockRoutine(Animator.StringToHash(state), length, until, after));
        }

        private void StopClock()
        {
            if (_clock != null) StopCoroutine(_clock);
            _clock = null;
            _animator.speed = 1f;
        }

        private IEnumerator ClockRoutine(int hash, float length, float until, float after)
        {
            yield return null;
            while (true)
            {
                var info = _animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).shortNameHash == hash
                    ? _animator.GetNextAnimatorStateInfo(0)
                    : _animator.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash != hash) break;
                if (info.normalizedTime * length >= until)
                {
                    if (after == 1f) break;
                    // Hovering: slowed until something else plays, then back to normal speed.
                    _animator.speed = after;
                }
                yield return null;
            }
            _clock = null;
            _animator.speed = 1f;
        }

        /// <summary>
        /// The ball's on its way: press forward (<paramref name="backFoot"/> false) or back
        /// and across, bat lifting, so the top of the backlift comes in
        /// <paramref name="tapIn"/> s (the perfect tap's moment); then wait there.
        /// </summary>
        /// <summary>He picked the bat up in the bowler's delivery stride (<see cref="PickUp"/>); cleared at the guard.</summary>
        private bool _pickedUp;

        /// <summary>
        /// Round 11 (the owner's reference: the bat comes up in the trigger, BEFORE the ball is out, whatever the
        /// tap): called as the bowler gathers, so the bat is at the top of the backlift by the release —
        /// <paramref name="secondsToTop"/> from now — and waits there; the shot (or <see cref="ShapeUp"/> at the
        /// release) takes it from the top. Not while his footwork (charge, move across) is under way.
        /// </summary>
        public void PickUp(float secondsToTop)
        {
            if (!UsesClips || _pickedUp || Charged || Across != 0) return;
            ShapeUp(Mathf.Max(0.2f, secondsToTop), false);
            _pickedUp = true;
        }

        public void ShapeUp(float tapIn, bool backFoot)
        {
            if (!UsesClips) return;
            CommitFootwork(false);
            var defence = CricketClips.Find(backFoot ? "32_back_foot_defence" : "01_forward_defence") ?? CricketClips.Find("01_forward_defence");
            if (defence == null) return;
            Mirror();
            var hold = Mathf.Min(ShapeHold, BackliftTop(defence));
            if (_pickedUp)
            {
                // Already up (PickUp, in the bowler's delivery stride): stay up. Same clip — keep waiting at the
                // top; the back-foot one for a short ball — straight onto ITS top, no second pick-up from the ground.
                _pickedUp = false;
                var want = Animator.StringToHash(Named(defence.Name));
                if (_animator.GetCurrentAnimatorStateInfo(0).shortNameHash == want ||
                    (_animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).shortNameHash == want)) return;
                Clock(Named(defence.Name), defence.Length, 1f, hold, HoverSpeed);
                _animator.CrossFadeInFixedTime(Named(defence.Name), ShapeBlend, 0, hold);
                return;
            }
            var speed = Mathf.Clamp(hold / Mathf.Max(0.05f, tapIn), ShapeMinSpeed, ShapeMaxSpeed);
            var at = Mathf.Max(0f, hold - tapIn * speed);
            // Settle back if nothing follows (every ball ends in a shot or a leave, but just in case).
            SettleAfter(4f);
            // Clock first and the offset in animator time (see PlayTimed).
            Clock(Named(defence.Name), defence.Length, speed, hold, HoverSpeed);
            if (Unflip()) _animator.Play(Named(defence.Name), 0, at / Mathf.Max(0.01f, defence.Length));
            else _animator.CrossFadeInFixedTime(Named(defence.Name), ShapeBlend * speed, 0, at / speed);
        }

        /// <summary>No shot: shoulder arms, the bat up and away as the ball goes by in <paramref name="contactIn"/> s.</summary>
        public void Leave(float contactIn)
        {
            if (!UsesClips) return;
            var leave = CricketClips.Find("33_leave");
            if (leave != null) PlayTimed(leave, contactIn);
        }

        /// <summary>
        /// Play <paramref name="clip"/> so its contact frame comes in <paramref name="contactIn"/> s,
        /// the bat's face turned toward <paramref name="face"/> (where the shot goes).
        /// </summary>
        public void Play(ShotClip clip, float contactIn, Vector3? face = null)
        {
            if (face is Vector3 f && f.sqrMagnitude > 0.01f) _face = new Vector3(f.x, 0, f.z).normalized;
            PlayTimed(clip, contactIn);
        }

        /// <summary>Test films: where the animator is against <paramref name="clip"/> (its own seconds), mid-transition too.</summary>
        public string DebugClock(string clip)
        {
            var h = Animator.StringToHash(Named(clip));
            var cur = _animator.GetCurrentAnimatorStateInfo(0);
            var next = _animator.GetNextAnimatorStateInfo(0);
            string S(AnimatorStateInfo i) => i.shortNameHash == h ? $"shot@{i.normalizedTime * StateLength(clip):0.000}s" : $"other({i.shortNameHash})@{i.normalizedTime:0.00}";
            var tr = _animator.IsInTransition(0) ? $" -> {S(next)} w{_animator.GetAnimatorTransitionInfo(0).normalizedTime:0.00}" : "";
            return $"{S(cur)}{tr} speed {_animator.speed:0.00}";
        }

        private Coroutine? _reach;

        /// <summary>
        /// Across to the ball's line during the swing: the whole body slides
        /// <paramref name="by"/> (m, world) over <paramref name="seconds"/>, eased, so
        /// the clip's bat meets the ball where it actually is.
        /// </summary>
        public void Reach(Vector3 by, float seconds)
        {
            StopReach();
            if (by.sqrMagnitude > 0.0001f) _reach = StartCoroutine(ReachRoutine(by, Mathf.Max(0.05f, seconds)));
        }

        private void StopReach()
        {
            if (_reach != null) StopCoroutine(_reach);
            _reach = null;
        }

        private IEnumerator ReachRoutine(Vector3 by, float seconds)
        {
            var done = Vector3.zero;
            for (var e = 0f; e < seconds;)
            {
                e += Time.deltaTime;
                var want = by * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / seconds));
                transform.position += want - done;
                done = want;
                yield return null;
            }
            _reach = null;
        }

        private void PlayTimed(ShotClip clip, float contactIn)
        {
            CommitFootwork(clip.Family == "down the track");
            Mirror();
            contactIn = Mathf.Max(0.03f, contactIn);
            var entry = Mathf.Min(BackliftTop(clip), clip.ContactTime - ShotLead);
            var speed = (clip.ContactTime - entry) / contactIn;
            if (speed > MaxSwingSpeed)
            {
                speed = MaxSwingSpeed;
                entry = clip.ContactTime - contactIn * speed;
            }
            else if (speed < MinSwingSpeed)
            {
                speed = MinSwingSpeed;
                entry = Mathf.Max(0f, clip.ContactTime - contactIn * speed);
            }
            // The blend (real s) is done well before contact, so the bat meets the ball on the clip's own path.
            var blend = Mathf.Min(ShotBlend, contactIn * 0.7f);
            var name = Named(clip.Name);
            SettleAfter((clip.ContactTime - entry) / speed + (clip.Length - clip.ContactTime));
            // The clock (animator speed) first: Unity scales a cross-fade's time offset by the
            // animator's speed, so the offset is given in animator time (entry / speed). Given in clip
            // seconds, a 1.5x swing joined at contact itself and the bat met the ball's spot 0.3 s early.
            Clock(name, clip.Length, speed, clip.ContactTime, 1f);
            if (Unflip()) _animator.Play(name, 0, entry / Mathf.Max(0.01f, clip.Length));
            else _animator.CrossFadeInFixedTime(name, blend * speed, 0, entry / speed);
        }

        /// <summary>Once the clip has run its course, ease back into the stance (unless he's off running).</summary>
        private void SettleAfter(float seconds)
        {
            StopSettling();
            _settle = StartCoroutine(SettleRoutine(Mathf.Max(0.1f, seconds)));
        }

        private void StopSettling()
        {
            if (_settle != null) StopCoroutine(_settle);
            _settle = null;
            if (_after != null) StopCoroutine(_after);
            _after = null;
            _lane = null;
            _along = null;
            StopGlide();
            StopClock();
            StopWalkIn();
            StopProps();
        }

        private IEnumerator SettleRoutine(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _settle = null;
            if (_carry || !UsesClips) yield break;
            _free = false;
            _face = Vector3.forward;
            // Out of his ground or off his guard (a charge, a reach, the shot's stride): he walks back (BatterRig.Crease.cs).
            if (WalkBackToGuard()) yield break;
            PlayGuard(0.5f);
        }

        // ------------------------------------------------------- fades that move him
        // Every clip's pose is relative to the transform, and the clips carry their
        // travel in the pose (a run ends 18 m from where it started). Moving the
        // transform and cross-fading drags the old pose along with it: the body flew
        // up the pitch while the bat (on the new clip's track) was already at the
        // other end — the "bat left behind, batter comes around". So a fade that
        // moves him glides the transform in step with the blend weight, which keeps
        // the blended body where it was (or walks it to the new spot), and a jump
        // too big for that is a cut.

        private Coroutine? _glide;

        /// <summary>Where his body is, on the ground (the hips, from the bones).</summary>
        private Vector3 HipsGround()
        {
            var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            var p = hips != null ? hips.position : transform.position;
            return new Vector3(p.x, 0, p.z);
        }

        private void StopGlide()
        {
            if (_glide != null) StopCoroutine(_glide);
            _glide = null;
            if (_runTurn != null) StopCoroutine(_runTurn); // (deep area 1: the turn after a run is a placement too)
            _runTurn = null;
            _fromFrame = _toFrame = null;
        }

        /// <summary>
        /// Fade into <paramref name="state"/> (authored at its origin, like the idle loops)
        /// with his body moving from where it is now to <paramref name="goal"/>, facing
        /// <paramref name="facing"/> — no jump however far the old pose was from the transform.
        /// </summary>
        private void FadeTo(string state, float fade, float normalizedAt, Vector3 goal, Quaternion facing)
        {
            StopGlide();
            // Owner v26 #17 (slidewatch: "75_non_striker_idle_loop>75_non_striker_idle_loop", up to 4.3 m/s): fading a loop
            // into ITSELF at a random phase blends two different moments of it — the planted feet slide between
            // them. Into the loop he's already in, the fade keeps the phase he's at (the two poses agree throughout).
            if (SlideFix.On)
            {
                // (Already fading into it — round 3, still 0.48 m/s mean: the phase the fade is bringing in.)
                var cur = _animator.IsInTransition(0) ? _animator.GetNextAnimatorStateInfo(0) : _animator.GetCurrentAnimatorStateInfo(0);
                if (cur.shortNameHash == Animator.StringToHash(state)) normalizedAt = Mathf.Repeat(cur.normalizedTime, 1f);
            }
            var hips = HipsGround();
            var p0 = transform.position;
            var r0 = transform.rotation;
            // The old pose's body, in its own frame (the clip's travel).
            var offset = Quaternion.Inverse(r0) * (hips - new Vector3(p0.x, 0, p0.z));
            offset.y = 0;
            _fromFrame = (p0, r0);
            _toFrame = (new Vector3(goal.x, p0.y, goal.z), facing);
            _animator.CrossFadeInFixedTime(state, fade, 0, normalizedAt * StateLength(state));
            _glide = StartCoroutine(GlideRoutine(hips, new Vector3(goal.x, 0, goal.z), offset, p0.y, r0, facing, Mathf.Max(0.01f, fade)));
        }

        private IEnumerator GlideRoutine(Vector3 from, Vector3 to, Vector3 offset, float y, Quaternion r0, Quaternion r1, float fade)
        {
            // The animator blends the two poses' bodies linearly with the fade's weight, in the
            // transform's frame: put the transform where that blend lands on the moving body.
            // (The first step runs now, before this frame's animator update that starts the fade.)
            Vector3? placed = null;
            for (var elapsed = 0f; ;)
            {
                // Round 12: someone else has moved him since the last step (the walk-out: Carry() faded him into the
                // idle where he stood — at the crease — then the walk put him at the gate, and this glide dragged him
                // 63 m back every frame: the Strides "moved by something else" line). Their move wins; the glide stops.
                if (placed is Vector3 was && (new Vector3(transform.position.x, 0, transform.position.z) - was).magnitude > 0.5f) break;
                elapsed += Time.deltaTime * Mathf.Max(0.01f, _animator.speed);
                var w = Mathf.Clamp01(elapsed / fade);
                var rot = Quaternion.Slerp(r0, r1, w);
                var body = Vector3.Lerp(from, to, w);
                var p = body - rot * (offset * (1 - w));
                transform.SetPositionAndRotation(new Vector3(p.x, y, p.z), rot);
                placed = new Vector3(p.x, 0, p.z);
                if (w >= 1f) break;
                yield return null;
            }
            _glide = null;
            _fromFrame = _toFrame = null;
        }

        /// <summary>A state's clip length (s): the shots from the batting list, the non-striker's clips by their frame counts.</summary>
        private static float StateLength(string state)
        {
            var stem = state.StartsWith("lhb_") ? state.Substring(4) : state;
            return stem switch
            {
                "75_non_striker_idle_loop" => 150 / 30f,
                "76_backing_up" => 90 / 30f,
                "77_backing_up_ready_loop" => 120 / 30f,
                "82_charge_down_the_track" => 36 / 30f,
                "83_move_across_off" or "84_move_to_leg_side" => 26 / 30f,
                _ => CricketClips.Find(stem)?.Length ?? 3f,
            };
        }

        /// <summary>Running between the wickets (or walking off): sprint, bat in the bottom hand.</summary>
        public void Run(bool running)
        {
            StopSettling();
            Unflip();
            _carry = true;
            _free = false;
            _ik = 0f;
            if (!running)
            {
                IdleInPlace();
                return;
            }
            if (UsesClips) _animator.CrossFadeInFixedTime("run_03_sprint_loop", 0.15f);
        }

        /// <summary>A stride or two out of his ground when the run starts: the clip is shifted along the run (z), the shift worked off by the time he grounds the bat.</summary>
        private (float fromZ, float toZ, float t0, float seconds)? _along;

        /// <summary>
        /// Run a clip from this crease towards the other (its run-up is baked into
        /// the pose: the body travels, the object stays). Running clips come in
        /// right- and left-handed versions, so no mirroring. From <paramref name="fromX"/>
        /// (where he's standing — the first run) he drifts into his lane over the first
        /// strides instead of jumping there, and starts from as far down the pitch as
        /// he is (a backing-up non-striker, a striker at the end of his stride). When
        /// the clip has played out he pulls up into the idle at that end, facing his
        /// partner (he used to freeze in the pull-up pose).
        /// </summary>
        public void PlayRun(RunClip clip, Vector3 crease, Vector3 towards, float? fromX = null)
        {
            _told = true;
            _backUpFrom = null;
            ShowBat();
            StopSettling();
            var cut = _flipped;
            _flipped = false;
            _carry = true;
            _free = false;
            _ik = 0f;
            var dir = new Vector3(towards.x, 0, towards.z).normalized;
            var hips = HipsGround();
            var ahead = Vector3.Dot(hips - new Vector3(crease.x, 0, crease.z), dir);
            // (Behind it too: the striker's guard is GuardBack inside his crease — the run starts from there.)
            var start = fromX != null && Mathf.Abs(ahead) > 0.1f && ahead > -0.6f && ahead < 2.5f ? ahead : 0f;
            // Where the old pose would be drawn once the transform has moved: too far from where
            // it is, and a fade would fling him across the ground — cut instead.
            var oldLocal = Quaternion.Inverse(transform.rotation) * (hips - transform.position);
            transform.position = crease + dir * start;
            // The running clips share the batting clips' frame (they start from the stance and run
            // toward the bowler's end): the stance facing, turned to run the way he's going.
            transform.rotation = UsesClips
                ? Quaternion.Euler(0, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0) * Facing
                : Quaternion.LookRotation(dir, Vector3.up);
            var shown = transform.position + transform.rotation * oldLocal;
            cut |= new Vector3(shown.x - hips.x, 0, shown.z - hips.z).magnitude > 0.6f;
            _animator.SetBool(MirrorParam, false);
            _animator.speed = 1f;
            if (cut) _animator.Play(clip.Name, 0, 0f);
            else _animator.CrossFadeInFixedTime(clip.Name, 0.1f, 0, 0f);
            _lane = (fromX ?? crease.x, crease.x, Time.time);
            _along = start != 0f ? (transform.position.z, crease.z, Time.time, Mathf.Max(0.6f, clip.EventTime)) : null;
            if (UsesClips) AfterClip(clip.Name, SettleAfterRun);
        }

        /// <summary>
        /// The run's done: into the non-striker's idle where he pulled up, turned to face
        /// down the pitch at his partner (at the far end that's back toward the batting end).
        /// </summary>
        private void SettleAfterRun()
        {
            _lane = null;
            _along = null;
            _carry = true;
            _free = false;
            _ik = 0f;
            var hips = HipsGround();
            var facing = hips.z > 0 ? Quaternion.Euler(0, 180f, 0) * Facing : Facing;
            if (Body.Has(_animator, NonStrikerIdle))
            {
                _animator.SetBool(MirrorParam, false);
                if (SlideFix.Round1)
                {
                    SettleAndTurnAfterRun(hips, facing);
                    return;
                }
                FadeTo(NonStrikerIdle, 0.45f, Random.value, hips, facing);
                // Pulled up short of the line (the dive) or off his place: he walks in (BatterRig.Crease.cs).
                WalkInAfterRun();
                return;
            }
            Body.Rebase(_animator, turn: false);
            transform.rotation = facing;
            _animator.Play("Stand", 0, 0f);
        }

        /// <summary>Carry the bat in one hand, where he stands (after a run, walking off).</summary>
        public void Carry()
        {
            _told = true;
            StopSettling();
            Unflip();
            IdleInPlace();
        }

        /// <summary>The non-striker's idle loop (the pack's, when it's there), per hand.</summary>
        private string NonStrikerIdle => (LeftHanded ? "lhb_" : "") + "75_non_striker_idle_loop";

        /// <summary>
        /// Waiting with the bat in the bottom hand, where he stands and as he faces:
        /// the non-striker's loop (bat toe on the ground, weight shifting) when the
        /// pack has it, else the plain stand.
        /// </summary>
        private void IdleInPlace()
        {
            _carry = true;
            _free = false;
            _ik = 0f;
            if (!UsesClips) return;
            if (Body.Has(_animator, NonStrikerIdle))
            {
                _animator.SetBool(MirrorParam, false);
                FadeTo(NonStrikerIdle, 0.3f, Random.value, HipsGround(), transform.rotation);
            }
            else _animator.CrossFadeInFixedTime("Stand", 0.2f);
        }

        /// <summary>
        /// At the non-striker's end: the idle loop faces down the pitch at the
        /// striker (the batting clips' frame turned round), the plain stand just
        /// looks that way. From close by (where his run ended) he shuffles into
        /// place; from further, a cut. He always waits INSIDE his crease: the spot is
        /// pushed back to <see cref="InsideCrease"/> behind the line (callers used to pass
        /// a spot 8 cm in front of it, with the idle loop's bat toe on the line — out of
        /// his ground every ball).
        /// </summary>
        public void NonStriker(Vector3 at)
        {
            _told = true;
            if (at.z > 0) at.z = Mathf.Max(at.z, FarCrease + InsideCrease);
            _backUpFrom = null;
            _gripLogUntil = 0f;
            ShowBat();
            StopSettling();
            Unflip();
            _carry = true;
            _free = false;
            _ik = 0f;
            _animator.speed = 1f;
            var idle = UsesClips && Body.Has(_animator, NonStrikerIdle);
            var facing = idle ? Quaternion.Euler(0, 180f, 0) * Facing : Quaternion.LookRotation(Vector3.back);
            if (!idle)
            {
                transform.SetPositionAndRotation(at, facing);
                if (UsesClips) _animator.CrossFadeInFixedTime("Stand", 0.2f);
                return;
            }
            _animator.SetBool(MirrorParam, false);
            // (2026-11-16: 3.5 m, was 2.5 — crossing to the other side of the stumps is up to 3 m now, 1.0–1.5 m wide
            // either side: walked, not cut.)
            var near = Vector3.Distance(HipsGround(), new Vector3(at.x, 0, at.z)) < 3.5f;
            if (near && isActiveAndEnabled)
            {
                // A step or more: walked, not glided (BatterRig.Crease.cs).
                if (WalkToNonStrikerSpot(at)) return;
                FadeTo(NonStrikerIdle, 0.5f, Random.value, at, facing);
                return;
            }
            transform.SetPositionAndRotation(at, facing);
            _animator.Play(NonStrikerIdle, 0, Random.value);
        }

        /// <summary>
        /// The non-striker backs up as the bowler runs in: a few steps down the pitch
        /// into a ready crouch (the clip holds it until the ball is played or he runs).
        /// </summary>
        public void BackUp()
        {
            var clip = (LeftHanded ? "lhb_" : "") + "76_backing_up";
            if (!UsesClips || !Body.Has(_animator, clip)) return;
            // Still crossing to his side of the stumps (the run-up's wait gave up): let him finish — backing up from
            // mid-crossing left him in the bowler's path (owner 2026-11-16, round the wicket).
            if (CrossingToPlace) return;
            StopSettling();
            Unflip();
            _carry = true;
            _free = false;
            _ik = 0f;
            transform.rotation = Quaternion.Euler(0, 180f, 0) * Facing;
            // Where he backs up from (his place inside the crease): the walk home ends here.
            _backUpFrom = (transform.position, transform.rotation);
            _animator.SetBool(MirrorParam, false);
            _animator.CrossFadeInFixedTime(clip, 0.25f, 0, 0f);
            // Then the ready crouch's own loop (when the pack has it), so he never holds a frozen
            // last frame: the walk's travel is in the pose, so the transform is first brought under
            // his hips — the loop is authored at the origin — keeping the facing.
            var ready = (LeftHanded ? "lhb_" : "") + "77_backing_up_ready_loop";
            if (Body.Has(_animator, ready))
            {
                AfterClip(clip, () => FadeTo(ready, 0.2f, 0f, HipsGround(), transform.rotation));
            }
        }

        // ------------------------------------------------------- non-striker home
        // Backing up takes him a metre or so out of his ground; when the ball's been
        // played and nobody's running he goes straight back: the backing-up walk played
        // in reverse (short steps back, still facing the striker, bat in hand) to where
        // he started, bat grounded behind the line, then the idle loop — home within
        // ~1.4 s of the ball's moment, well before any throw can come to his end.

        /// <summary>The popping crease at the non-striker's end (z), and how far behind it he waits (the idle loop's bat toe lands ~0.1 m further back).</summary>
        public static float FarCrease => GroundSpace.HalfPitch - 1.22f;
        public const float InsideCrease = 0.45f;

        /// <summary>He sets off home this long after the ball's moment (a run takes over if one starts), walking back at this multiple of the backing-up's own pace.</summary>
        public const float ReturnDelay = 0.25f, ReturnRate = 2.6f;

        private (Vector3 pos, Quaternion rot)? _backUpFrom;

        /// <summary>
        /// The ball's been played: unless a run takes him (the running clips replace this),
        /// walk back into the crease. Does nothing if he never backed up.
        /// </summary>
        public void HomeAfterBall()
        {
            if (!UsesClips || !isActiveAndEnabled || _backUpFrom == null) return;
            StopSettling();
            _settle = StartCoroutine(HomeRoutine());
        }

        private IEnumerator HomeRoutine()
        {
            yield return new WaitForSeconds(ReturnDelay);
            var backing = (LeftHanded ? "lhb_" : "") + "76_backing_up";
            var ready = Animator.StringToHash((LeftHanded ? "lhb_" : "") + "77_backing_up_ready_loop");
            var info = _animator.GetCurrentAnimatorStateInfo(0);
            var next = _animator.IsInTransition(0) ? _animator.GetNextAnimatorStateInfo(0).shortNameHash : 0;
            float t;
            if (info.shortNameHash == ready || next == ready)
            {
                // At the end of the walk out: back onto its last frame, from where it started (the ready
                // crouch begins in that pose, so the cut doesn't show).
                StopGlide();
                transform.SetPositionAndRotation(_backUpFrom!.Value.pos, _backUpFrom.Value.rot);
                t = 1f;
            }
            else if (info.shortNameHash == Animator.StringToHash(backing)) t = Mathf.Clamp01(info.normalizedTime);
            else
            {
                // Running, celebrating, or never left: nothing to walk back.
                _settle = null;
                yield break;
            }
            var length = StateLength(backing);
            _animator.speed = 0f;
            while (t > 0f)
            {
                t = Mathf.Max(0f, t - Time.deltaTime * ReturnRate / length);
                _animator.Play(backing, 0, t);
                yield return null;
            }
            _animator.speed = 1f;
            _backUpFrom = null;
            _settle = null;
            // Home: bat grounded behind the line, waiting (the backing-up starts from this idle's pose).
            _animator.CrossFadeInFixedTime(NonStrikerIdle, 0.25f, 0, Random.value * StateLength(NonStrikerIdle));
        }

        /// <summary>Once <paramref name="clip"/> has played through (no loop), do <paramref name="then"/> — so nobody freezes on a last frame.</summary>
        private void AfterClip(string clip, System.Action then)
        {
            if (_after != null) StopCoroutine(_after);
            _after = StartCoroutine(AfterClipRoutine(clip, then));
        }

        private IEnumerator AfterClipRoutine(string clip, System.Action then)
        {
            var hash = Animator.StringToHash(clip);
            yield return null;
            while (true)
            {
                // Through a cross-fade the clip is the *next* state, not the current one: this
                // used to see the old state on the first frame and fire at once (the backing-up
                // walk was skipped straight into its ready loop).
                var info = _animator.GetCurrentAnimatorStateInfo(0);
                var fadingIn = _animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).shortNameHash == hash;
                if (!fadingIn && (info.shortNameHash != hash || info.normalizedTime >= 0.97f)) break;
                yield return null;
            }
            _after = null;
            then();
        }

        /// <summary>
        /// Test runs (-aplgallery): hold <paramref name="clip"/> at <paramref name="normalized"/>
        /// (0–1) at the crease, facing as the clips do, so it can be photographed.
        /// </summary>
        // The bat is its own object in the scene (placed every frame, not parented, so the
        // hand's lost twist can't drag it): it comes and goes with him.
        private void OnEnable()
        {
            if (_bat != null) _bat.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (_bat != null) _bat.gameObject.SetActive(false);
            ForgetRunning();
            StopProps();
        }

        private void OnDestroy()
        {
            if (_bat != null) Destroy(_bat.gameObject);
        }

        public void GalleryPose(string clip, float normalized)
        {
            if (_swing != null) StopCoroutine(_swing);
            _swing = null;
            StopSettling();
            _carry = false;
            _free = false;
            _flipped = false;
            _ik = 0f;
            _face = Vector3.forward;
            transform.SetPositionAndRotation(_home, Facing);
            _animator.SetBool(MirrorParam, LeftHanded && !_nativeLeft);
            _animator.speed = 0f;
            _animator.Play(clip, 0, Mathf.Clamp01(normalized));
            _animator.Update(0f);
        }

        /// <summary>
        /// A milestone from wherever he is (often the far end after a run): the
        /// rhb_/lhb_ celebration ("bat_fifty_raise_bat", "bat_acknowledge_applause",
        /// "bat_winning_runs_celebration"), bat in the bottom hand.
        /// </summary>
        public bool Celebrate(string what) => Celebrate(what, null);

        /// <summary>
        /// The celebration with his chest turned to <paramref name="towards"/> (world; the milestone turns to the
        /// dressing room, then to the crowd). At the crease (null) the clips face as the stance does — chest to the
        /// off side, +x for a right-hander — so the turn is that much more.
        /// </summary>
        public bool Celebrate(string what, Vector3? towards)
        {
            var clipFacing = Facing * Vector3.forward;
            if (towards is Vector3 to && new Vector3(to.x, 0, to.z).sqrMagnitude > 1e-4f)
            {
                var chest = LeftHanded ? Vector3.left : Vector3.right;
                clipFacing = Quaternion.Euler(0, Vector3.SignedAngle(chest, new Vector3(to.x, 0, to.z), Vector3.up), 0) * clipFacing;
            }
            var name = (LeftHanded ? "lhb_" : "rhb_") + what;
            if (!UsesClips || !Body.Has(_animator, name)) return false;
            if (_swing != null) StopCoroutine(_swing);
            _swing = null;
            StopSettling();
            _carry = false;
            _free = true;
            _flipped = false;
            // The clips start from the stance, facing as at the crease (or turned); when it's done, he waits with the bat.
            if (SlideFix.Round1)
            {
                // Deep area 1, iteration 8 (reviewer: the hard cut from his walk into bat_acknowledge_applause popped a foot
                // 49 cm): faded in over 0.3 s with his body kept where it is (FadeTo), turning to the clip's facing as it fades;
                // FootPin holds his feet through it.
                _animator.SetBool(MirrorParam, false);
                _animator.speed = 1f;
                FadeTo(name, 0.3f, 0f, HipsGround(), Quaternion.LookRotation(new Vector3(clipFacing.x, 0, clipFacing.z)));
            }
            else if (!Body.Play(_animator, name, clipFacing)) return false;
            AfterClip(name, IdleInPlace);
            return true;
        }

        /// <summary>
        /// A reaction (beaten, struck on the pad or body, dismissed) joined at the
        /// clip's event frame — the ball's moment is now — then back to the
        /// stance; a dismissal instead ends walking off: the clip's own exit and
        /// then the dejected loop toward the pavilion (behind square, leg side).
        /// </summary>
        public bool React(string what, bool walkOff = false)
        {
            var clip = CricketClips.Reaction(what, LeftHanded);
            if (!UsesClips || clip == null || !Body.Has(_animator, clip.Name)) return false;
            if (_swing != null) StopCoroutine(_swing);
            _swing = null;
            StopSettling();
            // Deep area 3 (dev 2): mid-run the running clip carries his body in the pose while the transform stays at
            // the crease he left — reacting there snapped a run-out batter ~17 m back to it at the break. Rebase to
            // where his body is and cut (a fade would drag him across the gap).
            var running = _carry;
            if (running) Body.Rebase(_animator, false);
            _carry = false;
            _free = true;
            _animator.SetBool(MirrorParam, false);
            _animator.speed = 1f;
            if (Unflip() | running) _animator.Play(clip.Name, 0, clip.EventTime / Mathf.Max(0.01f, clip.Length));
            else _animator.CrossFadeInFixedTime(clip.Name, 0.12f, 0, clip.EventTime / Mathf.Max(0.01f, clip.Length));
            var remaining = Mathf.Max(0.1f, clip.Length - clip.EventTime);
            if (walkOff) _settle = StartCoroutine(WalkOffRoutine(remaining));
            else SettleAfter(remaining);
            return true;
        }

        private IEnumerator WalkOffRoutine(float after)
        {
            yield return new WaitForSeconds(after);
            _settle = null;
            var loop = CricketClips.Reaction("walk_off_dejected_loop", LeftHanded);
            var away = (Vector3.back - Off).normalized;
            if (loop == null || !Body.Play(_animator, loop.Name, away)) yield break;
            StallProbe.Mark($"walk-off: {name}");
            SlideScene.Tag(_animator, "batter dismissed, walking off");
            // The shared walk (Strides): his body faces the way he walks, whichever way the loop was made.
            yield return Strides.Along(_animator, away, 0.54f, () => false, loop.Name);
        }

        // ---------------------------------------------------- code-drawn swing (no clips)

        private Vector3 StanceGrip() => _home + Off * 0.18f + Vector3.up * 0.88f + Up * 0.05f;
        private Vector3 StanceDir() => (Vector3.down + Off * 0.25f - Up * 0.25f).normalized;

        /// <param name="direction">Where the shot is going (ground vector).</param>
        /// <param name="contactIn">Seconds from now until bat meets ball.</param>
        public void Swing(Apl.Core.Play.ShotType type, Vector3 direction, float contactIn, bool frontFoot)
        {
            if (_swing != null) StopCoroutine(_swing);
            _swing = StartCoroutine(SwingRoutine(type, direction.normalized, Mathf.Max(0.08f, contactIn), frontFoot));
        }

        private IEnumerator SwingRoutine(Apl.Core.Play.ShotType type, Vector3 dir, float down, bool frontFoot)
        {
            var defend = type == Apl.Core.Play.ShotType.Push;
            var loft = type == Apl.Core.Play.ShotType.Loft;
            var square = Mathf.Abs(Vector3.Dot(dir, Off)) > 0.75f;
            var step = frontFoot ? Up * 0.45f : -Up * 0.25f;

            var backGrip = _home + Off * 0.05f + Vector3.up * 1.25f - Up * 0.15f;
            var backDir = (Vector3.up * 0.6f - Up * 0.8f + Off * 0.1f).normalized;
            var hitGrip = _home + step + Off * 0.42f + Vector3.up * (square ? 1.0f : 0.82f) + dir * 0.15f;
            var hitDir = square ? (Vector3.Cross(Vector3.up, dir) * (Vector3.Dot(dir, Off) > 0 ? -1 : 1) + Vector3.down * 0.25f).normalized
                : (Vector3.down + dir * 0.2f).normalized;
            var endGrip = defend ? hitGrip : _home + step + Off * 0.15f + Vector3.up * (loft ? 1.6f : 1.35f) + dir * 0.25f;
            var endDir = defend ? hitDir : (Vector3.up * (loft ? 0.9f : 0.5f) + dir * 0.5f - Off * 0.3f).normalized;

            var lift = Mathf.Min(0.12f, down * 0.4f);
            yield return Move(StanceGrip(), StanceDir(), backGrip, backDir, lift, _home, _home);
            yield return Move(backGrip, backDir, hitGrip, hitDir, down - lift, _home, _home + step);
            yield return Move(hitGrip, hitDir, endGrip, endDir, defend ? 0.1f : 0.32f, _home + step, _home + step);
            yield return new WaitForSeconds(0.55f);
            yield return Move(endGrip, endDir, StanceGrip(), StanceDir(), 0.4f, _home + step, _home);
            _swing = null;
        }

        private IEnumerator Move(Vector3 g0, Vector3 d0, Vector3 g1, Vector3 d1, float seconds, Vector3 p0, Vector3 p1)
        {
            for (var t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, seconds))
            {
                var k = Mathf.SmoothStep(0, 1, t);
                transform.position = Vector3.Lerp(p0, p1, k);
                Pose(Vector3.Lerp(g0, g1, k), Vector3.Slerp(d0, d1, k));
                yield return null;
            }
            transform.position = p1;
            Pose(g1, d1);
        }

        private void Pose(Vector3 grip, Vector3 dir)
        {
            _grip = grip;
            _batDir = dir;
        }

        // ------------------------------------------------------------- the bat

        private void LateUpdate()
        {
            // A celebration's bat let go (falling, on the ground, on its way back): BatterRig.Celebration.cs.
            if (LooseBat()) return;
            PlaceBat();
            KeepBatWithHim();
            NoteBat();
        }

        private void PlaceBat()
        {
            // Owner v26 #17: the run's corrections move him only while both feet are off the ground (BatterRig.RunLane.cs).
            if (SlideFix.On && (_along != null || _lane != null)) RunCorrections();
            else if (_along is (float fromZ, float toZ, float z0, float seconds))
            {
                // Worked off gently (no change at first, while he pivots), never faster than ~0.7 m/s.
                var k = Mathf.SmoothStep(0, 1, (Time.time - z0) / seconds);
                transform.position = new Vector3(transform.position.x, transform.position.y, Mathf.Lerp(fromZ, toZ, k));
                if (k >= 1f) _along = null;
            }
            if (!SlideFix.On && _lane is (float from, float to, float t0))
            {
                // Held to his lane: the run clips wander sideways a little, and he eases in from where he stood.
                var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null)
                {
                    var x = Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, (Time.time - t0) / 0.45f));
                    transform.position += new Vector3(x - hips.position.x, 0, 0);
                }
            }
            var top = _animator.GetBoneTransform(LeftHanded ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            var bottom = _animator.GetBoneTransform(LeftHanded ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (UsesClips && _trackWeight > 0f)
            {
                // The authored bat path: the handle top, the blade down the axis, the face as the clip has it —
                // handed over to (or from) the hand socket through a fade with a state that has no track.
                var up = Vector3.ProjectOnPlane(_track.Face, _track.Axis);
                var batTop = _track.Top;
                var rot = up.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(up.normalized, -_track.Axis) : Quaternion.FromToRotation(Vector3.down, _track.Axis);
                var bottomLow = _animator.GetBoneTransform(BottomBone(1));
                if (_trackWeight < 0.999f && bottom != null && bottomLow != null && PalmGrip && PalmHold(bottomLow, bottom, !LeftHanded, out var held, out var heldRot))
                {
                    // Round 14: the one-handed end of the fade is the bat in his palm (BatterRig.HandModel.cs).
                    batTop = Vector3.Lerp(held, batTop, _trackWeight);
                    rot = Quaternion.Slerp(heldRot, rot, _trackWeight);
                }
                else if (_trackWeight < 0.999f && bottom != null && _socket is (Quaternion hq, Vector3 hv))
                {
                    batTop = Vector3.Lerp(bottom.position + bottom.rotation * hv, batTop, _trackWeight);
                    rot = Quaternion.Slerp(bottom.rotation * hq, rot, _trackWeight);
                }
                // The last few frames to contact: the bat a little toward the ball (MatchDirector.Meet.cs), hands with it.
                _nudge = Vector3.MoveTowards(_nudge, BatNudge, 3f * Time.deltaTime);
                batTop += _nudge;
                // Both wrists onto it, and the bat onto the bottom wrist where the arm can't reach (BatterRig.Grip.cs).
                Grip(ref batTop, rot);
                _legClear = Quaternion.identity;
                LogGrip();
                _grip = batTop;
                _batDir = rot * Vector3.down;
                _bat.SetPositionAndRotation(batTop, rot);
                return;
            }
            if (UsesClips && bottom != null && _socket is (Quaternion sq, Vector3 sv))
            {
                // The pack's socket: the bat fixed in the bottom hand however he holds it (shots, runs, celebrations).
                var batRot = bottom.rotation * sq;
                _grip = bottom.position + bottom.rotation * sv;
                // Round 14: the handle in his fingers, across the palm, measured off this hand now (BatterRig.HandModel.cs).
                var low1 = _animator.GetBoneTransform(BottomBone(1));
                if (PalmGrip && low1 != null && PalmHold(low1, bottom, !LeftHanded, out var palmTop, out var palmRot))
                {
                    _grip = palmTop;
                    batRot = palmRot;
                }
                // Off the pads (BatterRig.Grip.cs), turned about the hand so the handle stays in it.
                var cleared = ClearOfLegs(_grip, batRot);
                _grip = bottom.position + cleared * Quaternion.Inverse(batRot) * (_grip - bottom.position);
                batRot = cleared;
                _batDir = batRot * Vector3.down;
                _bat.SetPositionAndRotation(_grip, batRot);
                GripError = (0f, -1f, 0f, -1f, 0f);
                LogGrip();
                return;
            }
            if (_carry || (UsesClips && (top == null || bottom == null)))
            {
                // Carried in the bottom hand, blade down and a little forward.
                var hand = bottom ?? top;
                if (hand == null) return;
                if (PalmGrip && UsesClips && bottom != null && PalmInHand(bottom)) return;
                _bat.position = hand.position;
                _bat.rotation = Quaternion.FromToRotation(Vector3.down, (Vector3.down + transform.forward * 0.4f).normalized);
                return;
            }
            if (UsesClips)
            {
                var along = bottom!.position - top!.position;
                // A free top hand (celebrating, clutching the ribs, walking off): the bat hangs from the bottom hand.
                if (_free && along.magnitude > 0.3f)
                {
                    if (PalmGrip && PalmInHand(bottom)) return;
                    _bat.position = bottom.position;
                    _bat.rotation = Quaternion.FromToRotation(Vector3.down, (Vector3.down + transform.forward * 0.4f).normalized);
                    return;
                }
                // Along the hands: top of the handle just above the top hand, blade on past the bottom hand.
                // The hands never swap ends of the handle: a near-reversal is a crossing frame, keep the old line.
                if (along.sqrMagnitude > 0.0009f && Vector3.Dot(along, _batDir) > -0.2f * along.magnitude) _batDir = along.normalized;
                _grip = top.position - _batDir * 0.05f;
            }
            _bat.position = _grip;
            var face = Vector3.ProjectOnPlane(UsesClips ? _face : Vector3.forward, _batDir);
            _bat.rotation = face.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(face.normalized, -_batDir)
                : Quaternion.FromToRotation(Vector3.down, _batDir);
        }

        private void OnAnimatorIK(int layer)
        {
            if (UsesClips)
            {
                // Our people's arms aren't the clips' rig, so the hands drift apart on the
                // handle: keep the clip's bat angle but put the top hand a handle's length
                // above the bottom one.
                var topGoal = LeftHanded ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
                var bottomGoal = LeftHanded ? AvatarIKGoal.LeftHand : AvatarIKGoal.RightHand;
                // A clip with a bat track says itself which hand is on the handle (the non-striker's
                // loop has the top hand free); without one, a carried bat means no hand IK at all.
                _trackWeight = _flipped ? 0f : TrackPose(out _track);
                if (_trackWeight > 0f)
                {
                    _animator.SetIKPositionWeight(bottomGoal, _trackWeight);
                    _animator.SetIKPosition(bottomGoal, _track.BottomHand);
                    var topWeight = _track.TopOn ? _trackWeight : 0f;
                    _animator.SetIKPositionWeight(topGoal, topWeight);
                    if (topWeight > 0f) _animator.SetIKPosition(topGoal, _track.TopHand);
                    return;
                }
                if (_carry || _free)
                {
                    _animator.SetIKPositionWeight(topGoal, 0f);
                    _animator.SetIKPositionWeight(bottomGoal, 0f);
                    return;
                }
                _animator.SetIKPositionWeight(bottomGoal, 0f);
                var topNow = _animator.GetIKPosition(topGoal);
                var bottomNow = _animator.GetIKPosition(bottomGoal);
                // Along the hands' own line (last frame's bat when they overlap); with the socket, down the handle itself.
                var line = bottomNow - topNow;
                if (_socket is (Quaternion sq, Vector3 _))
                {
                    var bottomBone = _animator.GetBoneTransform(bottomGoal == AvatarIKGoal.LeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                    if (bottomBone != null) line = (bottomBone.rotation * sq) * Vector3.down;
                }
                if (line.sqrMagnitude < 0.0004f) line = _batDir;
                if (line.sqrMagnitude < 0.0004f) return;
                _animator.SetIKPositionWeight(topGoal, 1f);
                _animator.SetIKPosition(topGoal, bottomNow - line.normalized * 0.11f);
                return;
            }
            var w = _ik;
            var top = _grip;
            var bottom = _grip + _batDir * 0.11f;
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, w);
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, w);
            _animator.SetIKPosition(LeftHanded ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand, top);
            _animator.SetIKPosition(LeftHanded ? AvatarIKGoal.LeftHand : AvatarIKGoal.RightHand, bottom);
        }

        /// <summary>A bat from primitives: grip at the origin, blade down local -Y, face toward local +Z.</summary>
        private static GameObject MakeBat()
        {
            return BatMesh.Create();
        }

        private static void Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            r.material = new Material(shader) { color = color };
            r.material.SetColor("_BaseColor", color);
        }
    }
}
