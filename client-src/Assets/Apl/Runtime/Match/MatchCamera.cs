using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The match's camera: the broadcast view from behind the bowler's arm (high
    /// over the bowler's stumps, the pitch running up the frame, as in the
    /// reference game) while he bowls, then it follows the ball when it's hit, a
    /// wide shot of the ground for the cards, and close-ups for wickets and
    /// milestones. Anyone can put it on a shot of their own (<see cref="Track"/>);
    /// a shot asked for with a priority holds the camera until it's released, and
    /// lower requests made meanwhile wait their turn (the last one is applied on
    /// <see cref="Release"/>), so the fielding camera, the broadcast moments and
    /// the replays don't fight over it. It moves last in the frame (after the
    /// bodies, the bats and the replay tape), so the shots see this frame's poses.
    /// </summary>
    [DefaultExecutionOrder(6000)]
    public sealed partial class MatchCamera : MonoBehaviour
    {
        /// <summary>Player: a close-up of whoever <see cref="Target"/> is (bowler, batter) celebrating.</summary>
        /// <summary>Custom: a shot defined by whoever called <see cref="Track"/> (fielding, replays, ceremonies).</summary>
        public enum Mode { Broadcast, Follow, Wide, Batter, Player, Custom }

        /// <summary>
        /// Who may take the camera from whom: the plain match flow (0), the fielding
        /// camera during the field play, a broadcast moment (shot showcase, umpire,
        /// celebrations), a replay. A request below the current <see cref="Hold"/> waits.
        /// </summary>
        public const int Play = 0, Field = 1, Moment = 2, Replay = 3;

        public Mode Current { get; private set; } = Mode.Wide;
        public Transform? Target;

        /// <summary>The striker's hand: the close-up stands on his off side, so it sees his face, not his back.</summary>
        public bool LeftHanded;

        /// <summary>The priority of the shot that has the camera now (0: nobody's holding it).</summary>
        public int Hold { get; private set; }

        /// <summary>Somebody (the fielding camera, a moment, a replay) holds the camera.</summary>
        public bool Busy => Hold > Play;

        /// <summary>Counts every shot the camera takes (a caller can tell whether anyone took it since).</summary>
        public int Shots { get; private set; }

        /// <summary>Smooth on real time, not game time (replays run with the game frozen).</summary>
        public bool Unscaled;

        /// <summary>The Custom shot: where the camera goes, what it looks at, its lens, how snappy it follows.</summary>
        private System.Func<(Vector3 pos, Vector3 look)>? _custom;
        private float _customFov = 40f, _customSmooth = 0.25f;

        /// <summary>The latest request that came in below the hold, applied when it's released.</summary>
        private System.Action? _pending;

        /// <summary>
        /// A shot of the caller's own: <paramref name="shot"/> is asked every frame for the
        /// camera's position and look point (so it can follow a fielder, a ball, a replay).
        /// With a <paramref name="priority"/> above <see cref="Play"/> it holds the camera
        /// until <see cref="Release"/>; below the current hold it waits for it.
        /// </summary>
        public void Track(System.Func<(Vector3 pos, Vector3 look)> shot, float fov = 40f, float smooth = 0.25f, bool cut = false, int priority = Play)
        {
            if (Defer(priority, () => Track(shot, fov, smooth, cut, priority))) return;
            _custom = shot;
            _customFov = fov;
            _customSmooth = smooth;
            Apply(Mode.Custom, cut, priority);
        }

        /// <summary>A shot of the caller's own whose lens is asked every frame too (a zoom, a dolly-zoom).</summary>
        public void TrackLens(System.Func<(Vector3 pos, Vector3 look, float fov)> shot, float smooth = 0.25f, bool cut = false, int priority = Play)
        {
            if (Defer(priority, () => TrackLens(shot, smooth, cut, priority))) return;
            _custom = () =>
            {
                var (p, l, f) = shot();
                _customFov = f;
                return (p, l);
            };
            _customFov = shot().fov;
            _customSmooth = smooth;
            Apply(Mode.Custom, cut, priority);
        }

        /// <summary>A built-in shot (waits if somebody holds the camera above the plain match flow).</summary>
        public void Show(Mode mode, bool cut = false) => Show(mode, cut, Play);

        public void Show(Mode mode, bool cut, int priority)
        {
            if (Defer(priority, () => Show(mode, cut, priority))) return;
            Apply(mode, cut, priority);
        }

        /// <summary>
        /// The holder (at <paramref name="priority"/> or above) lets go: whatever was asked
        /// for meanwhile takes the camera now, else it stays on its shot, free.
        /// </summary>
        public void Release(int priority)
        {
            if (priority < Hold) return;
            Hold = Play;
            var next = _pending;
            _pending = null;
            next?.Invoke();
        }

        /// <summary>A new ball: nobody holds the camera, nothing is waiting.</summary>
        public void ResetHolds()
        {
            KeepInFrame = null;
            Hold = Play;
            _pending = null;
        }

        /// <summary>Below the hold: remember it for later (the latest asked wins) and say so.</summary>
        private bool Defer(int priority, System.Action request)
        {
            if (priority >= Hold) return false;
            _pending = request;
            return true;
        }

        private Vector3 _velocity;
        private Vector3 _look;
        private Camera? _cam;

        private void Awake() => _cam = GetComponent<Camera>();

        /// <summary>The broadcast view: high over the bowler's stumps, looking down the pitch (the reference's gameplay camera).</summary>
        public static readonly Vector3 BroadcastPos = new Vector3(0.3f, 8.5f, 27f), BroadcastLook = new Vector3(0, 0.2f, -1.5f);
        public const float BroadcastFov = 26f;

        /// <summary>Lens per shot.</summary>
        private float Fov => Current switch
        {
            Mode.Broadcast => BroadcastFov,
            Mode.Batter => 30f,
            Mode.Player => 32f,
            Mode.Follow => 42f,
            Mode.Custom => _customFov,
            _ => 40f,
        };

        private void Apply(Mode mode, bool cut, int priority)
        {
            Current = mode;
            Hold = priority;
            Shots++;
            if (!cut) return;
            var (pos, look) = Goal();
            transform.position = pos;
            _look = look;
            _velocity = Vector3.zero;
            transform.LookAt(_look);
            if (_cam != null) _cam.fieldOfView = Fov;
        }

        private (Vector3 pos, Vector3 look) Goal()
        {
            switch (Current)
            {
                case Mode.Broadcast:
                    // From behind the bowler's arm: the batter in the top third, the bowler's
                    // crease near the bottom, the umpire in the foreground.
                    return (BroadcastPos, BroadcastLook);
                case Mode.Batter:
                    return (new Vector3(LeftHanded ? -4.5f : 4.5f, 1.9f, -4.5f), new Vector3(0, 1.1f, -10f));
                case Mode.Follow:
                    if (Target == null) goto default;
                    // High behind the batter, chasing the ball's line.
                    var t = Target.position;
                    var from = new Vector3(t.x * 0.35f, Mathf.Max(9f, t.y + 6f), Mathf.Min(-16f, t.z - 24f));
                    return (from, t);
                case Mode.Custom:
                    if (_custom == null) goto default;
                    return _custom();
                case Mode.Player:
                    if (Target == null) goto default;
                    // From the middle of the pitch's side, at chest height: he's facing roughly this way.
                    var c = Target.position;
                    var toward = c.z > 0 ? -1f : 1f;
                    return (new Vector3(c.x + 3.2f, Mathf.Max(1.6f, c.y + 0.9f), c.z + 6.5f * toward), c + Vector3.up * 0.25f);
                default:
                    return (new Vector3(0, 26f, -62f), new Vector3(0, 0, 4f));
            }
        }

        private void LateUpdate()
        {
            var dt = Unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            var (pos, look) = Goal();
            var smooth = Current == Mode.Follow ? 0.18f : Current == Mode.Custom ? _customSmooth : 0.35f;
            if (smooth <= 0.001f)
            {
                // A locked shot (replays, cut-ins): exactly where it's asked to be.
                transform.position = pos;
                _velocity = Vector3.zero;
                _look = look;
                transform.LookAt(_look);
                if (_cam != null) _cam.fieldOfView = Fov;
                Guard(); // final rating fix #3: never inside a player's head (MatchCamera.Guard)
                return;
            }
            transform.position = Vector3.SmoothDamp(transform.position, pos, ref _velocity, smooth, Mathf.Infinity, Mathf.Max(dt, 1e-5f));
            _look = Vector3.Lerp(_look, look, 1 - Mathf.Exp(-dt * (Current == Mode.Follow ? 10f : 5f)));
            transform.LookAt(_look);
            if (_cam != null) _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, Fov, 1 - Mathf.Exp(-dt * 6f));
            Keep(dt);
            Guard(); // final rating fix #3: never inside a player's head (MatchCamera.Guard)
        }
    }
}
