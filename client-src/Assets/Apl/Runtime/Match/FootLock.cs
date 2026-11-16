using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The feet drive the walk (round 10, the owner's "sliding, not walking" for the fourth
    /// time). After every animation update the planted point — the lowest of both ankles
    /// and toes, while it is on the ground — is read off the pose in the body's own frame,
    /// and its travel under the body is the body's ground VELOCITY: speed and direction.
    /// <see cref="Strides.Walk"/> carries the body along exactly that vector and turns the
    /// body (about the planted foot) until that vector points where he's going.
    /// Round 10b: the speed alone, projected on the heading, left 0.3–0.5 m/s of sideways
    /// slide — the walk-outs step ~20° off the line their hips and shoulders face (the bat
    /// carried across the body turns the torso), so the feet went one way and the body another.
    /// Round 10c: it also MOVES the body, in the same frame it reads the pose (the animation has
    /// been evaluated before any LateUpdate), so the planted point stays exactly where it is —
    /// a frame late, through the walk coroutine, left ~0.08 m/s (walks) and ~0.7 m/s (the jog)
    /// in simulation on the source clips; same-frame, 0.06 and 0.26. It runs before the other
    /// LateUpdates (the bat is put in the hand after he's moved).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class FootLock : MonoBehaviour
    {
        private Animator _a = null!;
        private Transform?[] _points = new Transform?[0];
        private int _last = -1;
        private Vector3 _lastLocal;
        private float _low = float.MaxValue;
        private readonly float[] _prevY = { float.MinValue, float.MinValue, float.MinValue, float.MinValue };
        private int _clip;
        private string _clipName = "";
        private int _resets;

        /// <summary>The body's ground velocity as the feet have it now (world, m/s), smoothed over a few frames.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>The same, smoothed over about a stride: which way he's really going, for steering (no wobble per step).</summary>
        public Vector3 Course { get; private set; }

        /// <summary>Where the planted point is (world), if a foot is down.</summary>
        public Vector3? Planted { get; private set; }

        /// <summary>Where he's walking to (ground); he's never carried past it, and the last 0.6 m go straight at it.</summary>
        public Vector3 Target { get; set; }

        /// <summary>Per frame, for the walk-burst log: which point was planted (-1 none: 0/1 left ankle/toe, 2/3 right), its travel back under him, the step he was given.</summary>
        public int LastPoint { get; private set; } = -1;
        public Vector3 LastBack { get; private set; }
        public Vector3 LastStep { get; private set; }

        /// <summary>Walking (Begin) or not (End).</summary>
        public bool Active { get; private set; }

        /// <summary>Which walk owns him: a new walk on the same body takes over and the old one stops (two never fight over him).</summary>
        public int Owner { get; private set; }

        /// <summary>
        /// Owner v26 #17 round 4: the feet drive him whatever gait plays (MatchDirector.FeetStep: the fielders' runs, the
        /// walk-in, the coast to a stop) — no clip is held (the mover changes gait), and the body isn't "Busy" (Loco may
        /// play the loops). The clips carry no root motion (every import bakes it into the pose, AnimatorBuilder's log:
        /// ground speed 0.00 m/s for the run loops and the bowling actions), so this is the root motion: the planted foot's travel.
        /// </summary>
        public bool Free { get; private set; }

        public int BeginFree(Vector3 target)
        {
            var id = Begin("", target);
            Free = true;
            return id;
        }

        public int Begin(string clip, Vector3 target)
        {
            Free = false;
            Target = target;
            _a = GetComponent<Animator>();
            _points = new[]
            {
                _a.GetBoneTransform(HumanBodyBones.LeftFoot), _a.GetBoneTransform(HumanBodyBones.LeftToes),
                _a.GetBoneTransform(HumanBodyBones.RightFoot), _a.GetBoneTransform(HumanBodyBones.RightToes),
            };
            _clip = Animator.StringToHash(clip);
            _clipName = clip;
            _last = -1;
            _low = float.MaxValue;
            for (var i = 0; i < _prevY.Length; i++) _prevY[i] = float.MinValue;
            _resets = 0;
            // From a standstill: he goes only as fast as his feet say, from the first step.
            Velocity = Vector3.zero;
            Course = Vector3.zero;
            LastStep = Vector3.zero;
            LastPoint = -1;
            Beat = Time.frameCount;
            Planted = null;
            Active = true;
            return ++Owner;
        }

        public void End() => Active = false;

        /// <summary>The frame the walk last ticked: a walk stopped from outside (StopCoroutine) stops driving him too.</summary>
        public int Beat { get; set; }

        /// <summary>The walk has accounted for the last step (so it's counted once).</summary>
        public void ClearStep() => LastStep = Vector3.zero;

        /// <summary>The walk turned him by <paramref name="q"/>: his feet's push turns with him (no steering overshoot).</summary>
        public void Turned(Quaternion q)
        {
            Velocity = q * Velocity;
            Course = q * Course;
        }

        private Vector3 Local(Transform t) => Quaternion.Inverse(transform.rotation) * (t.position - transform.position);

        private void LateUpdate()
        {
            if (!Active || _a == null) return;
            if (Time.frameCount - Beat > 2)
            {
                // His walk was stopped without a word (the handshake line's StopCoroutine): let him be.
                Active = false;
                return;
            }
            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (Strides.IsPaused)
            {
                // The replay owns him: nothing measured or moved, and the next step starts afresh.
                _last = -1;
                return;
            }
            if (!Free) KeepOnClip();
            var best = -1;
            var y = float.MaxValue;
            for (var i = 0; i < _points.Length; i++)
            {
                var p = _points[i];
                if (p == null || p.position.y >= y) continue;
                y = p.position.y;
                best = i;
            }
            if (best < 0) return;
            // The ground line: the lowest the feet go, allowed to rise slowly.
            _low = Mathf.Min(y, _low + 0.03f * dt);
            // FLD-1: on the ground means low AND not rising or falling (a jog's or sprint's toe-off and
            // landing pass through the 4 cm band at speed — pinning those points made the body lurch).
            var vy = _prevY[best] > float.MinValue ? Mathf.Abs(y - _prevY[best]) / dt : 0f;
            for (var i = 0; i < _points.Length; i++) _prevY[i] = _points[i] != null ? _points[i]!.position.y : float.MinValue;
            var down = y < _low + 0.04f && vy < 0.35f;
            // FLD-1 (snap trace: the walk-start cross-fade from "Ready" into the jog moved the blended pose's
            // "planted" point 0.5-0.6 m a frame and carried the body with it): while the animator is blending
            // between states, the pose isn't a stride — nothing is measured, he carries on at the speed he had
            // (none at a walk's start), and the first clean frame after it starts the stride afresh.
            var blending = _a.IsInTransition(0);
            // Round 4 (slidewatch round 3: Ready>walk_in +walk still 0.68 m/s mean): a blend moves the planted foot too —
            // not only the incoming loop's stride, but the fade itself carrying the foot from one pose's place to the
            // other's. So through a blend the planted point is followed as well, as long as its frame-to-frame travel
            // is a step's (under 2x his speed + 2.5 cm a frame; the FLD-1 lurches were 50-60 cm); anything bigger
            // falls back to the stride estimate below.
            if (blending && !SlideFix.On) down = false;
            var local = Local(_points[best]!);
            // The step: exactly the planted point's travel back under him (so it stays put on the
            // ground), else — a foot changing over, a jog's flight — on at the speed he was going.
            var step = Velocity * dt;
            // Owner v26 #17 round 3 (slidewatch: Ready>walk_in +walk mean 0.88, Ready>sprint +walk 1.73): through the
            // blend nothing was measured and he went on at the speed he had — none at a walk's start — while the fading-in
            // loop already stepped under him. Now through a blend he goes exactly as fast as the walk's share of it
            // steps: its stride on this body × playback × its weight in the fade, toward where he's going.
            if (SlideFix.On && blending)
            {
                // The stride estimate, for a blend frame whose foot can't be followed (no foot down, a lurch).
                float speed;
                if (Free) speed = Strides.FeetSpeed(_a);
                else
                {
                    var t = Mathf.Clamp01(_a.GetAnimatorTransitionInfo(0).normalizedTime);
                    var w = _a.GetNextAnimatorStateInfo(0).shortNameHash == _clip ? t : _a.GetCurrentAnimatorStateInfo(0).shortNameHash == _clip ? 1f - t : 0f;
                    speed = Strides.StrideSpeed(_a, _clipName, 1.3f) * _a.speed * w;
                }
                var way = Course.sqrMagnitude > 0.04f ? Course.normalized : Free ? transform.forward : Target - transform.position;
                way.y = 0;
                step = way.sqrMagnitude > 1e-6f ? way.normalized * (speed * dt) : Vector3.zero;
                Velocity = step / dt;
            }
            LastPoint = down && best == _last ? best : -1;
            LastBack = Vector3.zero;
            if (down && best == _last)
            {
                var back = transform.rotation * (_lastLocal - local);
                if (back.magnitude > 0.5f) AnimTrace.Snap(_a, back.magnitude, _clipName); // FLD-1 log only
                back.y = 0;
                LastBack = back;
                var most = blending ? Mathf.Max(0.04f, 2f * Velocity.magnitude * dt + 0.025f) : 12f * dt;
                if (back.magnitude < most)
                {
                    step = back;
                    Velocity = back / dt;
                    Course = Vector3.Lerp(Course, Velocity, 1 - Mathf.Exp(-dt * 3f));
                }
            }
            var toGo = Target - transform.position;
            toGo.y = 0;
            var left = toGo.magnitude;
            if (left < 0.6f) step = left > 1e-4f ? toGo / left * Mathf.Min(left, step.magnitude) : Vector3.zero;
            else if (step.magnitude > left) step = toGo;
            transform.position += step;
            LastStep = step;
            Planted = down ? _points[best]!.position : (Vector3?)null;
            _last = down ? best : -1;
            _lastLocal = local;
        }

        /// <summary>
        /// The walk stays on: the animator was found reset to its default "Ready" at the start of
        /// the take-the-field walks (r10a log: state -380327253 = "Ready"), feet still while the body
        /// moved. Put back at once, every time (not after 0.3 s), and counted in the log.
        /// </summary>
        private void KeepOnClip()
        {
            var st = _a.GetCurrentAnimatorStateInfo(0);
            if (st.shortNameHash == _clip || (_a.IsInTransition(0) && _a.GetNextAnimatorStateInfo(0).shortNameHash == _clip)) return;
            if (_resets++ == 0) Debug.LogWarning($"[APL strides] {name}: walking in {_clipName} but the animator went to state {st.shortNameHash}: walk put back");
            AnimTrace.Note(_a, "FootLock put the walk back", true); // log only
            _a.CrossFadeInFixedTime(_clipName, 0.1f, 0, Random.value);
        }
    }
}
