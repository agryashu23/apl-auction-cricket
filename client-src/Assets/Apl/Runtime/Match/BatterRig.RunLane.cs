using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Owner v26 #17, batters running between the wickets. The run clips carry him in the pose; two
    /// corrections moved the transform under them every frame: <c>_along</c> (a striker starting a stride
    /// or two down the pitch, worked off over the run) and <c>_lane</c> (held to his lane, which pinned the
    /// HIPS' x every frame — the hips sway a few cm each stride, so his planted foot was dragged sideways
    /// at the sway's speed, and the ease into the lane slid him ~0.75 m across in 0.45 s with his feet down).
    /// Now both are applied only in the frames when neither foot is on the ground (a run's flight phase: the
    /// move happens in the air, nothing slides), at most 4 m/s, against the hips' SMOOTHED x (the sway taken
    /// out). If the clip never leaves the ground (no flight), after its time is up the old way finishes it.
    /// Off with <c>-aplnoslidefix</c> (the old per-frame corrections, BatterRig.PlaceBat).
    /// </summary>
    public sealed partial class BatterRig
    {
        /// <summary>The bat is on a shot's authored path (FootPin leaves a shot's footwork alone).</summary>
        internal bool OnBatPath => UsesClips && _trackWeight > 0.01f;

        private Transform?[]? _feetPts;
        private float _runLow = float.MaxValue;
        private float _laneHips = float.NaN, _laneFor = -1f;

        /// <summary>Neither foot (ankles, toes) within 5 cm of the lowest they've been lately: he's in the air.</summary>
        private bool Airborne()
        {
            _feetPts ??= new[]
            {
                _animator.GetBoneTransform(HumanBodyBones.LeftFoot), _animator.GetBoneTransform(HumanBodyBones.LeftToes),
                _animator.GetBoneTransform(HumanBodyBones.RightFoot), _animator.GetBoneTransform(HumanBodyBones.RightToes),
            };
            var y = float.MaxValue;
            foreach (var p in _feetPts)
                if (p != null && p.position.y < y) y = p.position.y;
            if (y == float.MaxValue) return true;
            _runLow = Mathf.Min(y, _runLow + 0.03f * Time.deltaTime);
            return y > _runLow + 0.05f;
        }

        private void RunCorrections()
        {
            var dt = Time.deltaTime;
            var air = Airborne();
            var most = 4f * dt;
            if (_along is (float fromZ, float toZ, float z0, float seconds))
            {
                var late = Time.time - z0 > seconds + 0.2f;
                var want = Mathf.Lerp(fromZ, toZ, Mathf.SmoothStep(0, 1, (Time.time - z0) / seconds));
                var dz = want - transform.position.z;
                if (late) transform.position = new Vector3(transform.position.x, transform.position.y, want);
                else if (air) transform.position += new Vector3(0, 0, Mathf.Clamp(dz, -most, most));
                if (late || (Time.time - z0 >= seconds && Mathf.Abs(dz) < 0.01f)) _along = null;
            }
            if (_lane is (float from, float to, float t0))
            {
                var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips == null) return;
                if (_laneFor != t0)
                {
                    // A new run: the smoothing starts from where his hips are.
                    _laneFor = t0;
                    _laneHips = hips.position.x;
                }
                _laneHips = Mathf.Lerp(_laneHips, hips.position.x, 1 - Mathf.Exp(-dt / 0.35f));
                var x = Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, (Time.time - t0) / 0.45f));
                var dx = x - _laneHips;
                // No flight for 1.5 s (a clip that never leaves the ground): the old hold, so he still makes his lane.
                var step = air ? Mathf.Clamp(dx, -most, most) : Time.time - t0 > 1.5f ? Mathf.Clamp(dx, -0.5f * dt, 0.5f * dt) : 0f;
                if (step != 0f)
                {
                    transform.position += new Vector3(step, 0, 0);
                    _laneHips += step;
                }
            }
        }
    }
}
