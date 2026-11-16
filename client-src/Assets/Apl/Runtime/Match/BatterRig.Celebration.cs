using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The milestone celebrations' props (owner delivery 2026-11-12, celebrations.json): the bat let go at
    /// bat_release_frame falls (a toss carries on with the hand's speed), lands and topples flat, and goes back
    /// into his hand when he's done (from where he ends up: the somersault carries him ~4.5 m, so he walks back
    /// to it first — the shared stepping walk); the helmet goes into the top hand between helmet_off_frame and
    /// helmet_on_frame (the old hundred's leap too). Anything that takes him over (StopSettling: the next ball,
    /// a run, another clip) puts the bat back in his hand and the helmet back on his head at once.
    /// </summary>
    public sealed partial class BatterRig
    {
        /// <summary>A celebration clip's length and events (1-based frames, 30 fps; 0 = none).</summary>
        public sealed class CelebrationInfo
        {
            public int Frames, Release, HelmetOff, HelmetOn, Key;
            public bool Travels;
            public float Seconds => (Frames - 1) / 30f;
            public float KeySeconds => Mathf.Max(0, Key - 1) / 30f;
        }

        public static readonly Dictionary<string, CelebrationInfo> Celebrations = new Dictionary<string, CelebrationInfo>
        {
            ["bat_fifty_raise_bat"] = new CelebrationInfo { Frames = 120, Key = 35 },
            ["bat_hundred_leap_helmet_off"] = new CelebrationInfo { Frames = 180, HelmetOff = 56, HelmetOn = 154, Key = 90 },
            ["fifty_signature_pose"] = new CelebrationInfo { Frames = 140, Key = 60 },
            ["century_bat_drop"] = new CelebrationInfo { Frames = 120, Release = 63, Key = 72 },
            ["century_leap_and_chin_swipe"] = new CelebrationInfo { Frames = 150, Key = 79 },
            ["century_leap_and_kisses"] = new CelebrationInfo { Frames = 140, Key = 68 },
            ["century_meditation_pose"] = new CelebrationInfo { Frames = 180, Key = 95 },
            ["century_roar_helmet_off_bow"] = new CelebrationInfo { Frames = 210, HelmetOff = 84, HelmetOn = 186, Key = 120 },
            ["century_somersault"] = new CelebrationInfo { Frames = 150, Release = 21, Key = 66, Travels = true },
        };

        private Coroutine? _props;
        private bool _batLoose, _propsWalking;
        private Vector3 _batVel, _batPrev;
        private (Vector3 pos, Quaternion rot, float t0)? _batBack;
        private readonly List<(Renderer r, GameObject? clone, Transform? parent, Vector3 lp, Quaternion lr)> _helmetOff = new List<(Renderer, GameObject?, Transform?, Vector3, Quaternion)>();

        /// <summary>
        /// A milestone celebration with its props: <see cref="Celebrate(string, Vector3?)"/>, then the bat and helmet
        /// events off its clock. Seconds until he's done (the clip, the walk back to his bat), or −1 if it isn't there.
        /// </summary>
        public float CelebrateMilestone(string stem, Vector3? towards)
        {
            if (!Celebrate(stem, towards)) return -1f;
            if (!Celebrations.TryGetValue(stem, out var info)) return 3.6f;
            if (info.Release == 0 && info.HelmetOff == 0) return info.Seconds;
            _props = StartCoroutine(PropsRoutine((LeftHanded ? "lhb_" : "rhb_") + stem, info));
            return info.Seconds + (info.Travels ? 4.5f : info.Release > 0 ? 0.6f : 0f);
        }

        private IEnumerator PropsRoutine(string state, CelebrationInfo info)
        {
            var hash = Animator.StringToHash(state);
            var start = transform.position;
            float Clock()
            {
                var cur = _animator.GetCurrentAnimatorStateInfo(0);
                if (cur.shortNameHash == hash) return cur.normalizedTime * info.Seconds;
                if (_animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).shortNameHash == hash) return _animator.GetNextAnimatorStateInfo(0).normalizedTime * info.Seconds;
                return -1f;
            }
            var events = new List<(float at, System.Action act)>();
            if (info.Release > 0) events.Add(((info.Release - 1) / 30f, LetGoBat));
            if (info.HelmetOff > 0) events.Add(((info.HelmetOff - 1) / 30f, HelmetToHand));
            if (info.HelmetOn > 0) events.Add(((info.HelmetOn - 1) / 30f, HelmetOnHead));
            events.Sort((a, b) => a.at.CompareTo(b.at));
            var next = 0;
            yield return null;
            while (next < events.Count)
            {
                var t = Clock();
                if (t < 0f) break;
                while (next < events.Count && t >= events[next].at) events[next++].act();
                yield return null;
            }
            // The clip's over (or something else took over his animator): helmet back on whatever happened.
            HelmetOnHead();
            if (!_batLoose) yield break;
            while (Clock() >= 0f) yield return null;
            yield return new WaitForSeconds(0.5f);
            // Back to his bat (the somersault: metres away), stepping; then it's in his hand again.
            var bat = new Vector3(_bat.position.x, 0, _bat.position.z);
            var body = HipsGround();
            var gap = (bat - body).magnitude;
            if (gap > 0.9f && Body.Has(_animator, WalkLoop))
            {
                _carry = true;
                _free = false;
                var to = bat + (body - bat).normalized * 0.55f;
                Debug.Log($"[APL celebrate] {name} walks {gap - 0.55f:0.0} m back to his bat");
                _propsWalking = true;
                SlideScene.Tag(_animator, "batter milestone walk-back");
                yield return Strides.Walk(_animator, to, WalkInPace, WalkLoop, 1.31f);
                _propsWalking = false;
                // Deep area 1: the walk fades out at the slow playback it ended on (at 1x the fading walk stepped at full
                // speed under a body that had stopped — the walk-back treadmill), back to 1x once the idle's in.
                _animator.speed = SlideFix.Round1 ? Mathf.Min(1f, _animator.speed) : 1f;
                _animator.SetBool(MirrorParam, false);
                FadeTo(NonStrikerIdle, 0.3f, Random.value, to, transform.rotation);
                if (SlideFix.Round1)
                {
                    yield return new WaitForSeconds(0.35f);
                    _animator.speed = 1f;
                }
            }
            BatBackToHand(0.35f);
            _props = null;
        }

        /// <summary>The bat leaves his hand: on with the hand's speed, falling.</summary>
        private void LetGoBat()
        {
            if (_batLoose || _bat == null) return;
            _batLoose = true;
            _batBack = null;
            _batVel = Vector3.ClampMagnitude(_batVel, 6f);
            Debug.Log($"[APL celebrate] {name} lets go of the bat at {_bat.position:0.00}, {_batVel.magnitude:0.0} m/s");
        }

        private void BatBackToHand(float seconds)
        {
            if (!_batLoose) return;
            _batLoose = false;
            _batBack = (_bat.position, _bat.rotation, Time.time + 0f);
            _batBackFor = Mathf.Max(0.01f, seconds);
        }

        private float _batBackFor = 0.35f;

        /// <summary>
        /// From LateUpdate, before the bat is placed: true while it's loose (falling or on the ground — not placed in his
        /// hand); on its way back to the hand it's placed as usual and blended from where it lay.
        /// </summary>
        private bool LooseBat()
        {
            if (_bat == null) return false;
            if (_batLoose)
            {
                var dt = Time.deltaTime;
                var len = 0.85f;
                var down = _bat.rotation * Vector3.down;
                var toe = _bat.position + down * len;
                if (Mathf.Min(_bat.position.y, toe.y) > 0.04f)
                {
                    _batVel += Physics.gravity * dt;
                    _batVel *= 1f - 0.4f * dt;
                    _bat.position += _batVel * dt;
                }
                else
                {
                    // Down: it topples flat (blade along the ground, face up), the end that landed stays put.
                    _batVel = Vector3.zero;
                    var h = new Vector3(down.x, 0, down.z);
                    if (h.sqrMagnitude < 1e-4f) h = transform.right;
                    h.Normalize();
                    var landed = toe.y <= _bat.position.y ? toe : _bat.position;
                    var flat = Quaternion.LookRotation(Vector3.up, -h);
                    var rot = Quaternion.RotateTowards(_bat.rotation, flat, 220f * dt);
                    var grip = toe.y <= _bat.position.y ? landed - (rot * Vector3.down) * len : landed;
                    grip.y = Mathf.Max(grip.y, 0.04f);
                    _bat.SetPositionAndRotation(grip, rot);
                }
                _batPrev = _bat.position;
                return true;
            }
            if (_batBack is (Vector3 p, Quaternion r, float t0))
            {
                PlaceBat();
                var k = Mathf.SmoothStep(0, 1, (Time.time - t0) / _batBackFor);
                _bat.SetPositionAndRotation(Vector3.Lerp(p, _bat.position, k), Quaternion.Slerp(r, _bat.rotation, k));
                if (k >= 1f) _batBack = null;
                _batPrev = _bat.position;
                return true;
            }
            return false;
        }

        /// <summary>After the bat's placed each frame: its speed (a toss carries on at the hand's speed when it's let go).</summary>
        private void NoteBat()
        {
            if (_bat == null) return;
            if (Time.deltaTime > 0f) _batVel = (_bat.position - _batPrev) / Time.deltaTime;
            _batPrev = _bat.position;
        }

        /// <summary>The helmet off his head into his top hand: a skinned helmet is copied as it is now (and hidden), a plain one moved.</summary>
        private void HelmetToHand()
        {
            if (_helmetOff.Count > 0) return;
            var hand = _animator.GetBoneTransform(LeftHanded ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (hand == null) return;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r is SkinnedMeshRenderer smr && smr.name == CricketCast.HelmetGear)
                {
                    var mesh = new Mesh();
                    smr.BakeMesh(mesh, true);
                    var go = new GameObject("HelmetInHand");
                    go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                    go.transform.SetParent(hand, true);
                    smr.enabled = false;
                    _helmetOff.Add((r, go, null, Vector3.zero, Quaternion.identity));
                }
                else if (r is MeshRenderer && r.gameObject.name == "Helmet")
                {
                    var t = r.transform;
                    _helmetOff.Add((r, null, t.parent, t.localPosition, t.localRotation));
                    t.SetParent(hand, true);
                }
            }
            if (_helmetOff.Count > 0)
            {
                CricketCast.RefreshHair(_animator);
                Debug.Log($"[APL celebrate] {name}: helmet off into the top hand ({_helmetOff.Count} part(s))");
            }
        }

        /// <summary>The helmet back on his head (or anything that takes over: at once).</summary>
        private void HelmetOnHead()
        {
            if (_helmetOff.Count == 0) return;
            foreach (var (r, clone, parent, lp, lr) in _helmetOff)
            {
                if (clone != null)
                {
                    var mf = clone.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
                    Destroy(clone);
                }
                if (r == null) continue;
                if (parent != null)
                {
                    r.transform.SetParent(parent, false);
                    r.transform.localPosition = lp;
                    r.transform.localRotation = lr;
                }
                else r.enabled = true;
            }
            _helmetOff.Clear();
            CricketCast.RefreshHair(_animator);
        }

        /// <summary>Taken over (StopSettling): the props put right at once — the bat in his hand, the helmet on.</summary>
        private void StopProps()
        {
            if (_props != null) StopCoroutine(_props);
            _props = null;
            if (_propsWalking)
            {
                _propsWalking = false;
                var feet = GetComponent<FootLock>();
                if (feet != null && feet.Active) feet.End();
                _animator.speed = 1f;
            }
            _batLoose = false;
            _batBack = null;
            HelmetOnHead();
        }
    }
}
