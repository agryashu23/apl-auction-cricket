using System;
using System.Collections.Generic;
using Apl.Client.Match;
using UnityEngine;

namespace Apl.Client.Dev
{
    /// <summary>
    /// TEST ONLY (no behaviour): one body's feet for <see cref="FootPrints"/>. Runs after everything that moves or poses
    /// him (FootLock -100, rigs 0, FootPin 500, SlideProbe 10000), so the discs show what's drawn. Per foot: a plant starts
    /// when its lowest point (ankle or toe) is down; each point is anchored the first frame it is that foot's lowest in the
    /// plant (heel strike → ankle, roll-over → toe), and the drift is the current lowest point's ground distance from its
    /// own anchor — so a heel lifting over a planted toe isn't counted, a toe or heel moving over the turf is.
    /// A one-frame jump over 0.5 m (a cut, a placement) ends the plant uncounted. Nothing during a replay.
    /// </summary>
    [DefaultExecutionOrder(10001)]
    public sealed class FootPrintProbe : MonoBehaviour
    {
        private const float Band = 0.04f, Rise = 0.35f, Cut = 0.5f, MinSecs = 0.08f;

        private sealed class Foot
        {
            public Transform? Ankle, Toe;
            public float Low = float.MaxValue, PrevY = float.MinValue;
            public bool Down, HasAnkle, HasToe, Red;
            public Vector3 AnkleAt, ToeAt, Last;
            public int LastIdx = -1;
            public float Secs, MaxCm, NowCm;
            public string State = "";
            public GameObject? Disc;
        }

        private Animator _a = null!;
        private readonly Foot[] _feet = { new Foot(), new Foot() };
        private readonly List<AnimatorClipInfo> _clips = new List<AnimatorClipInfo>();
        private MatchDirector? _director;

        /// <summary>For the burst log: this foot's drift now (cm) while planted, else -1. 0 = left, 1 = right.</summary>
        public float DriftCm(int foot) => _feet[foot].Down ? _feet[foot].NowCm : -1f;

        /// <summary>The largest drift (cm) of the current or last plant of this foot.</summary>
        public float PlantMaxCm(int foot) => _feet[foot].MaxCm;

        private void Start()
        {
            _a = GetComponent<Animator>();
            _feet[0].Ankle = _a.GetBoneTransform(HumanBodyBones.LeftFoot);
            _feet[0].Toe = _a.GetBoneTransform(HumanBodyBones.LeftToes);
            _feet[1].Ankle = _a.GetBoneTransform(HumanBodyBones.RightFoot);
            _feet[1].Toe = _a.GetBoneTransform(HumanBodyBones.RightToes);
        }

        public string Role()
        {
            if (_director == null) _director = FindAnyObjectByType<MatchDirector>();
            var refs = _director != null ? _director.Refs : null;
            if (refs != null && refs.Bowler == _a) return "bowler";
            if (refs != null && refs.Keeper == _a) return "keeper";
            if (GetComponent<BatterRig>() != null) return "batter";
            if (GetComponent<KeeperRig>() != null) return "keeper";
            return "fielder";
        }

        private string Top(bool next)
        {
            _clips.Clear();
            if (next) _a.GetNextAnimatorClipInfo(0, _clips);
            else _a.GetCurrentAnimatorClipInfo(0, _clips);
            var best = "(none)";
            var w = -1f;
            foreach (var c in _clips)
            {
                if (c.clip == null || c.weight <= w) continue;
                w = c.weight;
                best = c.clip.name;
            }
            return best;
        }

        private string State() => _a.IsInTransition(0) ? Top(false) + ">" + Top(true) : Top(false);

        private void End(Foot f, bool book)
        {
            if (f.Down && book && f.Secs >= MinSecs) FootPrints.Book(Role(), name, f.State, f.MaxCm, f.Secs);
            if (f.Disc != null) FootPrints.Release(f.Disc);
            f.Disc = null;
            f.Down = false;
            f.LastIdx = -1;
        }

        private void LateUpdate()
        {
            if (_a == null || !_a.isActiveAndEnabled) return;
            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (Strides.IsPaused)
            {
                foreach (var f in _feet) End(f, false);
                return;
            }
            foreach (var f in _feet)
            {
                if (f.Ankle == null) continue;
                var ankle = f.Ankle.position;
                var toe = f.Toe != null ? f.Toe.position : ankle;
                var idx = f.Toe != null && toe.y < ankle.y ? 1 : 0;
                var p = idx == 1 ? toe : ankle;
                f.Low = Mathf.Min(p.y, f.Low + 0.03f * dt);
                var vy = f.PrevY > float.MinValue ? Mathf.Abs(p.y - f.PrevY) / dt : 0f;
                f.PrevY = p.y;
                var down = p.y < f.Low + Band && vy < Rise;
                if (!down)
                {
                    End(f, true);
                    continue;
                }
                if (f.Down && f.LastIdx == idx && new Vector2(p.x - f.Last.x, p.z - f.Last.z).magnitude > Cut)
                {
                    // A cut or a placement, not a slide.
                    End(f, false);
                }
                if (!f.Down)
                {
                    f.Down = true;
                    f.HasAnkle = f.HasToe = f.Red = false;
                    f.Secs = f.MaxCm = f.NowCm = 0f;
                    f.State = State();
                    f.Disc = FootPrints.Take(p, Role());
                }
                Vector3 anchor;
                if (idx == 0)
                {
                    if (!f.HasAnkle)
                    {
                        f.HasAnkle = true;
                        f.AnkleAt = p;
                        FootPrints.Move(f.Disc, p);
                    }
                    anchor = f.AnkleAt;
                }
                else
                {
                    if (!f.HasToe)
                    {
                        f.HasToe = true;
                        f.ToeAt = p;
                        FootPrints.Move(f.Disc, p);
                    }
                    anchor = f.ToeAt;
                }
                f.NowCm = new Vector2(p.x - anchor.x, p.z - anchor.z).magnitude * 100f;
                f.MaxCm = Mathf.Max(f.MaxCm, f.NowCm);
                if (!f.Red && f.NowCm > FootPrints.RedCm)
                {
                    f.Red = true;
                    FootPrints.Redden(f.Disc);
                }
                f.Secs += dt;
                f.Last = p;
                f.LastIdx = idx;
            }
        }

        private void OnDisable()
        {
            foreach (var f in _feet) End(f, false);
        }
    }
}
