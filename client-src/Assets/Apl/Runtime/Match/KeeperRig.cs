using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The wicketkeeper (Resources/Data/cricket/keeper.json): stands up to the
    /// stumps for spin and back for pace, and takes each ball with the clip
    /// whose gloves land nearest where the ball actually arrives — sampled on
    /// this body, so the clip's left/right never has to be trusted — shuffled
    /// across the last few centimetres and timed so the gloves close on it.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class KeeperRig : MonoBehaviour
    {
        private sealed class Take
        {
            public string Name = "", Type = "";
            public float EventTime, Length;
            public bool Up, Dive, Appeal;
        }

        private const float Fps = 30f;
        private static List<Take>? _takes;
        private static float _returnRelease = 19 / Fps;

        private Animator _animator = null!;
        private readonly Dictionary<string, Vector3> _gloves = new Dictionary<string, Vector3>();

        public bool UsesClips { get; private set; }

        /// <summary>Standing up to the stumps (spin) or back (pace).</summary>
        public bool StandingUp { get; private set; }

        /// <summary>
        /// Where the ball sits in his gloves: the palm cup (HandPoint, kept after the animation and the hand IK
        /// by CatchHands) — it was the right WRIST bone, a ball held against his forearm (deep area 3).
        /// </summary>
        public Transform? Gloves => CatchHands.For(_animator).CupTransform;

        /// <summary>Every take (clip, where the ball arrives, in how long): the director reaches the gloves for it and logs it.</summary>
        public System.Action<string, Vector3, float>? OnTake;

        private static IReadOnlyList<Take> Takes
        {
            get
            {
                if (_takes != null) return _takes;
                _takes = new List<Take>();
                var text = Resources.Load<TextAsset>("Data/cricket/keeper");
                if (text == null) return _takes;
                foreach (JObject c in (JArray)JObject.Parse(text.text)["clips"]!)
                {
                    var file = (string)c["file"]!;
                    var name = file.Substring(0, file.Length - 4);
                    var type = (string?)c["type"] ?? "";
                    if (name == "wk_return_ball_to_bowler") _returnRelease = (((int?)c["release_frame"] ?? 20) - 1) / Fps;
                    // The run-out takes (a throw in, the bails off) are kept apart for RunOutTake.
                    if (c["event_frame"] != null && (name.Contains("runout_take") || name.Contains("stumping")))
                    {
                        (name.Contains("stumping") ? StumpingTakes : RunOutTakes).Add(new Take { Name = name, Type = type, EventTime = ((int)c["event_frame"]! - 1) / Fps, Length = (int)c["frames"]! / Fps, Up = true });
                        continue;
                    }
                    // Plain takes only: stumpings, run-outs and the fumble need a wicket or byes behind them.
                    if (c["event_frame"] == null || name.Contains("stumping") || name.Contains("runout") || name.Contains("fumble")) continue;
                    _takes.Add(new Take
                    {
                        Name = name,
                        Type = type,
                        EventTime = ((int)c["event_frame"]! - 1) / Fps,
                        Length = (int)c["frames"]! / Fps,
                        Up = type.Contains("standing up"),
                        Dive = type.Contains("diving"),
                        Appeal = type.Contains("appeal"),
                    });
                }
                return _takes;
            }
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            UsesClips = _animator.runtimeAnimatorController != null && _animator.HasState(0, Animator.StringToHash("wk_stance_back_loop"));
        }

        /// <summary>Test runs log every take's shuffle ("[APL keeper] take shift …"); the director sets it.</summary>
        public static bool LogShifts;

        /// <summary>How much of a take's miss (m) the hand IK (CatchHands, up to 0.65 m) takes up without moving his feet.</summary>
        public const float ShiftFree = 0.15f;

        /// <summary>The shuffle across to a take still to make (world, m), and how fast (m/s).</summary>
        private Vector3 _shiftLeft;
        private float _shiftRate;

        /// <summary>
        /// Deep area 3 (sliding report: a take moved him up to 0.5 m in ONE frame, a pop): the shuffle across is
        /// spread over ~0.15 s (done before the take frame); the hand IK (CatchHands) keeps the gloves on the
        /// ball's line whatever is left.
        /// </summary>
        private void Shift(Vector3 world, float inSeconds, string clip)
        {
            // (Sliding verdict A4: the shuffle still slid his planted feet — wk_take_up_offside 0.24 m/s. The gloves'
            // IK covers the first ShiftFree m of a miss; only what's beyond it moves his body.)
            var miss = world.magnitude;
            world = miss > ShiftFree ? world * ((miss - ShiftFree) / miss) : Vector3.zero;
            var over = Mathf.Clamp(inSeconds * 0.8f, 0.05f, 0.15f);
            _moveUntil = -1f;
            _shiftLeft += world;
            _shiftRate = _shiftLeft.magnitude / over;
            if (LogShifts) Debug.Log($"[APL keeper] take shift {world.magnitude:0.00} m over {over:0.00} s ({clip}; miss {miss:0.00} m, the gloves' IK takes {Mathf.Min(miss, ShiftFree):0.00})");
        }

        private void Update()
        {
            if (_moveUntil > 0f)
            {
                // The read: eased across (added as a delta, so a clip's own root motion is kept on top).
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - _moveFrom) / Mathf.Max(0.01f, _moveUntil - _moveFrom)));
                transform.position += _moveBy * (k - _moveDone);
                _moveDone = k;
                if (k >= 1f) _moveUntil = -1f;
            }
            if (_shiftRate <= 0f) return;
            var step = Vector3.ClampMagnitude(_shiftLeft, _shiftRate * Time.deltaTime);
            transform.position += step;
            _shiftLeft -= step;
            if (_shiftLeft.sqrMagnitude < 1e-6f) _shiftRate = 0f;
        }

        /// <summary>Into position for the next ball: at the stumps for spin, a few strides back for pace.</summary>
        public void Stance(bool spin)
        {
            _shiftLeft = Vector3.zero;
            _shiftRate = 0f;
            _moveUntil = -1f;
            _releaseAt = -1f;
            StandingUp = spin;
            _animator.speed = 1f;
            // Standing up: the stumps 0.62 m in front and 0.28 m to his right (the clips' setup).
            var pos = spin ? new Vector3(-0.28f, 0, -GroundSpace.HalfPitch - 0.62f) : new Vector3(0, 0, -GroundSpace.HalfPitch - 4.5f);
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.forward));
            if (UsesClips) _animator.Play(spin ? "wk_stance_up_loop" : "wk_stance_back_loop", 0, Random.value);
            // Every take's gloves sampled now, in his stance (never mid-take, where sampling would cut a cross-fade).
            if (UsesClips)
                foreach (var t in Takes)
                    if (_animator.HasState(0, Animator.StringToHash(t.Name))) Glove(t);
        }

        /// <summary>He's run up to the stumps to take a throw.</summary>
        public void UpToStumps() => StandingUp = true;

        /// <summary>
        /// Take the ball arriving at <paramref name="ball"/> (world) in
        /// <paramref name="inSeconds"/>. <paramref name="appeal"/> asks for the
        /// take-and-appeal when it fits. Returns the clip played, or null (no clips).
        /// </summary>
        public string? TakeBall(Vector3 ball, float inSeconds, bool appeal = false)
        {
            if (!UsesClips) return null;
            var local = transform.InverseTransformPoint(ball);
            Take? best = null;
            var bestScore = float.MaxValue;
            foreach (var t in Takes)
            {
                if (!_animator.HasState(0, Animator.StringToHash(t.Name))) continue;
                if (t.Up != StandingUp && !t.Dive) continue;
                var g = Glove(t);
                var score = Vector3.Distance(g, local);
                if (t.Dive && Mathf.Abs(local.x) < 1.3f) score += 1f;
                if (t.Appeal) score += appeal ? -0.25f : 0.6f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            if (best == null) return null;
            // Shuffle across so the gloves meet it (feet, not hands, make up the difference).
            var miss = local - Glove(best);
            var shift = new Vector3(Mathf.Clamp(miss.x, -0.6f, 0.6f), 0, Mathf.Clamp(miss.z, -0.4f, 0.4f));
            Shift(transform.rotation * shift, inSeconds, best.Name);
            _animator.speed = 1f;
            _animator.CrossFadeInFixedTime(best.Name, 0.08f, 0, Mathf.Max(0, best.EventTime - inSeconds));
            OnTake?.Invoke(best.Name, ball, inSeconds);
            return best.Name;
        }

        /// <summary>
        /// The ball bursts out of his gloves (a fumbled throw, or byes): the
        /// missed-take clip timed so the gloves meet it in <paramref name="inSeconds"/>,
        /// then he turns and chases it. False if the clip isn't there.
        /// </summary>
        public bool Fumble(Vector3 ball, float inSeconds)
        {
            const string clip = "wk_fumble_turn_chase_byes";
            if (!UsesClips || !_animator.HasState(0, Animator.StringToHash(clip))) return false;
            var face = ball - transform.position;
            face.y = 0;
            if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(face);
            _animator.speed = 1f;
            // The miss is on frame 31, like the takes.
            _animator.CrossFadeInFixedTime(clip, 0.08f, 0, Mathf.Max(0, 30 / Fps - inSeconds));
            return true;
        }

        private static readonly List<Take> RunOutTakes = new List<Take>(), StumpingTakes = new List<Take>();

        /// <summary>
        /// A stumping: standing up, he takes it (off or leg side, whichever clip's gloves land
        /// nearest the ball) and whips the gloves onto the bails. Returns the clip, or null.
        /// </summary>
        public string? Stumping(Vector3 ball, float inSeconds)
        {
            if (!UsesClips) return null;
            _ = Takes;
            var local = transform.InverseTransformPoint(ball);
            Take? best = null;
            var bestScore = float.MaxValue;
            foreach (var t in StumpingTakes)
            {
                if (!_animator.HasState(0, Animator.StringToHash(t.Name))) continue;
                var score = Vector3.Distance(Glove(t), local);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            if (best == null) return null;
            var miss = local - Glove(best);
            Shift(transform.rotation * new Vector3(Mathf.Clamp(miss.x, -0.5f, 0.5f), 0, Mathf.Clamp(miss.z, -0.3f, 0.3f)), inSeconds, best.Name);
            _animator.speed = 1f;
            _animator.CrossFadeInFixedTime(best.Name, 0.08f, 0, Mathf.Max(0, best.EventTime - inSeconds));
            OnTake?.Invoke(best.Name, ball, inSeconds);
            return best.Name;
        }

        /// <summary>
        /// A run out at his end: up at the stumps facing down the pitch, he takes
        /// the throw with the run-out clip (from in front, the off or the leg side)
        /// whose gloves land nearest it and whips the bails off. Falls back to a
        /// plain take. Returns the clip played, or null.
        /// </summary>
        public string? RunOutTake(Vector3 ball, float inSeconds)
        {
            if (!UsesClips) return null;
            _ = Takes;
            transform.rotation = Quaternion.LookRotation(Vector3.forward);
            var local = transform.InverseTransformPoint(ball);
            Take? best = null;
            var bestScore = float.MaxValue;
            foreach (var t in RunOutTakes)
            {
                if (!_animator.HasState(0, Animator.StringToHash(t.Name))) continue;
                var score = Vector3.Distance(Glove(t), local);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            if (best == null) return TakeBall(ball, inSeconds);
            var miss = local - Glove(best);
            Shift(transform.rotation * new Vector3(Mathf.Clamp(miss.x, -0.5f, 0.5f), 0, Mathf.Clamp(miss.z, -0.3f, 0.3f)), inSeconds, best.Name);
            _animator.speed = 1f;
            _animator.CrossFadeInFixedTime(best.Name, 0.08f, 0, Mathf.Max(0, best.EventTime - inSeconds));
            OnTake?.Invoke(best.Name, ball, inSeconds);
            return best.Name;
        }

        /// <summary>Lob it back to the bowler; returns seconds until it leaves his hand.</summary>
        public float ReturnBall()
        {
            if (!UsesClips || !_animator.HasState(0, Animator.StringToHash("wk_return_ball_to_bowler"))) return 0;
            _animator.CrossFadeInFixedTime("wk_return_ball_to_bowler", 0.15f);
            _releaseAt = Time.time + _returnRelease;
            return _returnRelease;
        }

        /// <summary>The lob back to the bowler has left his gloves (the take's cup hold lets go then).</summary>
        public bool Released => _releaseAt > 0f && Time.time >= _releaseAt;
        private float _releaseAt = -1f;

        // ------------------------------------------------------------ the delivery's take (MatchDirector.KeeperRead)

        /// <summary>The read's move: by how much (world), from / until when (s), and how much is done (0–1).</summary>
        private Vector3 _moveBy;
        private float _moveFrom, _moveUntil = -1f, _moveDone;

        /// <summary>
        /// Keeper's read (owner: "the ball's going somewhere, then suddenly the keeper has it"): his feet take him
        /// to <paramref name="body"/> (only across and back — never up), eased over <paramref name="seconds"/>.
        /// Re-targeting mid-move starts from where he is.
        /// </summary>
        public void MoveTo(Vector3 body, float seconds)
        {
            _shiftLeft = Vector3.zero;
            _shiftRate = 0f;
            var by = body - transform.position;
            by.y = 0f;
            _moveBy = by;
            _moveDone = 0f;
            _moveFrom = Time.time;
            _moveUntil = Time.time + Mathf.Max(0.05f, seconds);
        }

        /// <summary>
        /// The take for a delivery arriving at <paramref name="ball"/> (world, the ball's own flight there), with his
        /// body at <paramref name="from"/> and up to <paramref name="budget"/> m of sideways move in time: the clip
        /// whose gloves (sampled on this body at its take frame) need the least sideways move and arm, at the ball's
        /// height — low, waist, chest, head, off / leg side, the dive only when nothing else reaches.
        /// <paramref name="body"/>: where his feet must take him; <paramref name="glove"/>: his gloves there at the take.
        /// </summary>
        public string? PlanDelivery(Vector3 ball, Vector3 from, float budget, bool appeal, out Vector3 body, out Vector3 glove)
        {
            body = from;
            glove = ball;
            if (!UsesClips) return null;
            var rot = transform.rotation;
            // (Unscaled, as Glove() samples it: his transform at the origin, unrotated.)
            var local = Quaternion.Inverse(rot) * (ball - from);
            Take? best = null;
            var bestScore = float.MaxValue;
            var bestMove = 0f;
            foreach (var t in Takes)
            {
                if (!_animator.HasState(0, Animator.StringToHash(t.Name))) continue;
                if (t.Up != StandingUp && !t.Dive) continue;
                var g = Glove(t);
                var dx = local.x - g.x;
                var move = Mathf.Clamp(dx, -budget, budget);
                // Within a hand's width the arms take it: no shuffle for that.
                if (Mathf.Abs(dx) < 0.1f) move = 0f;
                var armX = dx - move;
                var dy = local.y - g.y;
                var score = Mathf.Sqrt(armX * armX + dy * dy) + 0.3f * Mathf.Abs(move);
                // The dive only for a ball his feet can't get him to.
                if (t.Dive && Mathf.Abs(local.x) < Mathf.Min(1.3f, budget + 0.5f)) score += 1f;
                if (t.Appeal) score += appeal ? -0.25f : 0.6f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = t;
                    bestMove = move;
                }
            }
            if (best == null) return null;
            body = from + rot * new Vector3(bestMove, 0f, 0f);
            glove = body + rot * Glove(best);
            return best.Name;
        }

        /// <summary>The take clip, timed so its take frame lands in <paramref name="inSeconds"/>.</summary>
        public void FireTake(string clip, float inSeconds)
        {
            var ev = 30 / Fps;
            foreach (var t in Takes)
                if (t.Name == clip) ev = t.EventTime;
            _animator.speed = 1f;
            _animator.CrossFadeInFixedTime(clip, Mathf.Min(0.08f, Mathf.Max(0.02f, inSeconds * 0.5f)), 0, Mathf.Max(0, ev - inSeconds));
        }

        /// <summary>Where his gloves are on the take frame, in his own frame (sampled once per clip).</summary>
        private Vector3 Glove(Take t)
        {
            if (_gloves.TryGetValue(t.Name, out var g)) return g;
            var pos = transform.position;
            var rot = transform.rotation;
            var state = _animator.GetCurrentAnimatorStateInfo(0);
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var culling = _animator.cullingMode;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // The take frame as a share of the clip's real length (the takes then play in real seconds).
            _animator.Play(t.Name, 0, 0f);
            _animator.Update(0f);
            var len = _animator.GetCurrentAnimatorStateInfo(0).length;
            _animator.Play(t.Name, 0, t.EventTime / (len > 0.05f ? len : Mathf.Max(0.01f, t.Length)));
            _animator.Update(0f);
            _animator.cullingMode = culling;
            var l = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var r = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            // The palm cup (where the ball is held), not between the wrists.
            g = l != null && r != null ? HandPoint.Cup(_animator) : new Vector3(0, 0.9f, 0.5f);
            transform.SetPositionAndRotation(pos, rot);
            _animator.Play(state.shortNameHash, 0, state.normalizedTime);
            _animator.Update(0f);
            _gloves[t.Name] = g;
            return g;
        }
    }
}
