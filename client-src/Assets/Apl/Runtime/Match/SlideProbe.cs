using System;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// LOG ONLY (owner v26 #17): one body's feet for <see cref="SlideWatch"/>. Runs after everything that
    /// moves him this frame (FootLock -100, the rigs' LateUpdates at 0), so it sees what's drawn.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class SlideProbe : MonoBehaviour
    {
        private Animator _a = null!;
        private Transform?[] _pts = new Transform?[0];
        private int _idx = -1;
        private Vector3 _last;
        private float _low = float.MaxValue, _prevY = float.MinValue;
        private bool _planted;
        private float _secs, _dist, _turn;
        private string _state = "";
        /// <summary>Deep area 1: the scenario a mover tagged him with when the plant began (null: guessed from role and clip).</summary>
        private string? _scene;
        private bool _walk, _feet;
        private float _yaw;
        private readonly List<AnimatorClipInfo> _clips = new List<AnimatorClipInfo>();

        private void Start()
        {
            _a = GetComponent<Animator>();
            _pts = new[]
            {
                _a.GetBoneTransform(HumanBodyBones.LeftFoot), _a.GetBoneTransform(HumanBodyBones.LeftToes),
                _a.GetBoneTransform(HumanBodyBones.RightFoot), _a.GetBoneTransform(HumanBodyBones.RightToes),
            };
            _yaw = transform.eulerAngles.y;
        }

        /// <summary>Who he is now (a body can be a fielder one over and the bowler the next).</summary>
        /// <remarks>PM's v26 run: every fielder's body carries an enabled BowlerRig (any of them may bowl), so the
        /// rig can't say who's bowling — the director's refs do (the bowler of this over, the keeper, the umpire).</remarks>
        private string Role()
        {
            if (_director == null) _director = FindAnyObjectByType<MatchDirector>();
            var refs = _director != null ? _director.Refs : null;
            if (refs != null && refs.Bowler == _a) return "bowler";
            if (refs != null && refs.Keeper == _a) return "keeper";
            if (refs != null && refs.Umpire == _a) return "umpire";
            if (GetComponent<BatterRig>() != null) return "batter";
            if (GetComponent<KeeperRig>() != null) return "keeper";
            if (name.StartsWith("Field")) return "fielder";
            return name.IndexOf("ump", StringComparison.OrdinalIgnoreCase) >= 0 ? "umpire" : "other";
        }

        private MatchDirector? _director;
        private static int _treadLines;

        private string Top(bool next)
        {
            _clips.Clear();
            if (next) _a.GetNextAnimatorClipInfo(0, _clips);
            else _a.GetCurrentAnimatorClipInfo(0, _clips);
            var best = "";
            var w = -1f;
            foreach (var c in _clips)
            {
                if (c.clip == null || c.weight <= w) continue;
                w = c.weight;
                best = c.clip.name;
            }
            return best.Length > 0 ? best : "(none)";
        }

        private string State() => _a.IsInTransition(0) ? Top(false) + ">" + Top(true) : Top(false);

        private void Close()
        {
            if (_planted && _secs >= 0.08f)
            {
                var tags = (_walk ? " +walk" : "") + (_feet ? " +feet" : "") + (_turn / Mathf.Max(_secs, 1e-3f) > 90f ? " +turn" : "");
                var role = Role();
                SlideWatch.Book(role, _state + tags, name + (_scene != null ? " [" + _scene + "]" : ""), _dist / _secs, _secs);
                var sceneName = _scene ?? SlideScene.Guess(role, _state + tags);
                SlideWatch.BookScene(sceneName, role + " " + _state + tags, _dist / _secs);
                // Iteration 7 (reviewer A6, log only): a gait loop stepping with nothing moving him — who last touched him.
                if (sceneName == "fielder gait with no mover" && _dist / _secs > 0.15f && _treadLines++ < 60)
                    Debug.Log($"[APL slidewatch] TREADMILL {name} {_state}: {_dist / _secs:0.00} m/s over {_secs:0.00} s; last calls: {AnimTrace.RecentNotes(_a)}");
            }
            _planted = false;
            _secs = _dist = _turn = 0f;
        }

        private void LateUpdate()
        {
            if (_a == null || !_a.isActiveAndEnabled) return;
            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (Strides.IsPaused)
            {
                Close();
                _idx = -1;
                return;
            }
            var idx = -1;
            var y = float.MaxValue;
            for (var i = 0; i < _pts.Length; i++)
            {
                var p = _pts[i];
                if (p == null || p.position.y >= y) continue;
                y = p.position.y;
                idx = i;
            }
            if (idx < 0) return;
            var pos = _pts[idx]!.position;
            _low = Mathf.Min(pos.y, _low + 0.03f * dt);
            var vy = _prevY > float.MinValue && idx == _idx ? Mathf.Abs(pos.y - _prevY) / dt : 0f;
            _prevY = pos.y;
            // The same "on the ground" as FootLock and Strides' meter: low, and not rising or falling.
            var down = pos.y < _low + 0.04f && vy < 0.35f;
            var yaw = transform.eulerAngles.y;
            var turned = Mathf.Abs(Mathf.DeltaAngle(_yaw, yaw));
            _yaw = yaw;
            if (down && idx == _idx)
            {
                var d = pos - _last;
                d.y = 0;
                var m = d.magnitude;
                if (m > 0.5f)
                {
                    // A cut, not a slide (deep area 1: still counted per scenario, a placement the eye may see).
                    SlideWatch.BookJump(SlideScene.Of(_a) ?? SlideScene.Guess(Role(), State()), m);
                    Close();
                }
                else if (m > Mathf.Max(0.12f, 4f * dt))
                {
                    // Faster than any foot slides (4 m/s): somebody placed him this frame.
                    SlideWatch.BookPop(Role(), State(), name, m);
                    SlideWatch.BookScenePop(SlideScene.Of(_a) ?? SlideScene.Guess(Role(), State()), m);
                    Close();
                }
                else
                {
                    if (!_planted)
                    {
                        _planted = true;
                        _state = State();
                        _walk = Strides.Busy(_a);
                        _feet = Strides.FeetDriven(_a);
                        _scene = SlideScene.Of(_a);
                    }
                    _secs += dt;
                    _dist += m;
                    _turn += turned;
                }
            }
            else Close();
            _idx = down ? idx : -1;
            _last = pos;
        }

        private void OnDisable()
        {
            Close();
            _idx = -1;
        }
    }
}
