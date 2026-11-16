using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The run out, seen: on a close throw the camera goes side-on at the crease
    /// it's coming to (the ball flying in, the batter racing for the line); the
    /// throw hits the stumps (a direct hit) or the keeper / bowler gathers and
    /// whips the bails off — at the engine's moment, which for MANUAL running may
    /// only be known once the last run's settled, so the ball waits in the
    /// gloves at the stumps till then. Stumps rattle and the bails fly at either end.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>
        /// Final rating fix #3 (P156: inside the run-out batter's helmet at the RUN OUT banner): the camera's
        /// near-clip guard keeps the lens out of these bodies — both sides on the field and both umpires
        /// (MatchCamera.Guard: [APL camguard] in the log when it pulls back).
        /// </summary>
        private void WireCamGuard()
        {
            if (Camera == null) return;
            Camera.GuardBodies = CamGuardBodies;
            Debug.Log("[APL camguard] wired: heads >= " + MatchCamera.HeadClear + " m, chests >= " + MatchCamera.BodyClear + " m from the lens");
        }

        private IEnumerable<Animator?> CamGuardBodies()
        {
            foreach (var a in _side.Values) yield return a;
            foreach (var b in _batters.Values) if (b != null) yield return b.GetComponent<Animator>();
            yield return Refs.Umpire;
            yield return _squareLeg;
        }

        private sealed class Wicket3
        {
            public readonly List<(Transform t, Vector3 pos, Quaternion rot)> Stumps = new List<(Transform, Vector3, Quaternion)>();
            public readonly List<(Transform t, Vector3 pos, Quaternion rot)> Bails = new List<(Transform, Vector3, Quaternion)>();

            /// <summary>Broken this ball (the bails are off; the next ball stands it up again).</summary>
            public bool Broken;
        }

        /// <summary>The two sets of stumps (keeper's end, bowler's end), found in the ground once.</summary>
        private Wicket3? _keeperWicket, _bowlerWicket;

        /// <summary>The wicket's been broken for this ball's run out (the director waits on it).</summary>
        private bool _wicketBroken;

        private Wicket3 WicketAt(bool keeperEnd)
        {
            if (_keeperWicket == null)
            {
                _keeperWicket = new Wicket3();
                _bowlerWicket = new Wicket3();
                var ground = GameObject.Find("Ground");
                if (ground != null)
                {
                    foreach (var t in ground.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name != "Stump" && t.name != "Bail") continue;
                        var set = t.position.z < 0 ? _keeperWicket : _bowlerWicket!;
                        (t.name == "Stump" ? set.Stumps : set.Bails).Add((t, t.position, t.rotation));
                    }
                }
            }
            return keeperEnd ? _keeperWicket : _bowlerWicket!;
        }

        /// <summary>Both wickets standing again (the next ball).</summary>
        private void RestoreWickets()
        {
            // Every ball: the keeper's takes reach and log (review b3 — it was wired only from a delivery take, so a match
            // whose keeper only ever took throws, or the next innings' keeper, never logged: MatchDirector.CatchHands).
            WireKeeperTakes();
            _wicketBroken = false;
            _brokenAt = -1f;
            _watchingEnd = null;
            _roVisible = "";
            _roReplay = "none";
            if (_keeperWicket == null) return;
            foreach (var w in new[] { _keeperWicket, _bowlerWicket! })
            {
                w.Broken = false;
                foreach (var (t, pos, rot) in w.Stumps.Concat(w.Bails))
                {
                    if (t != null) t.SetPositionAndRotation(pos, rot);
                }
            }
        }

        /// <summary>The wicket at this end broken by a ball (or gloves) coming from <paramref name="dir"/>: stumps rattle, bails fly.</summary>
        private void BreakWicket(bool keeperEnd, Vector3 dir)
        {
            var w = WicketAt(keeperEnd);
            _wicketBroken = true;
            // Once per wicket a ball: a second break would fling the bails up off the turf again.
            if (w.Broken) return;
            w.Broken = true;
            _brokenAt = Time.time;
            // The live camera on the end being broken, whatever the throw's path did (r11 f47: it sat at the
            // bowler's end while the bails came off at the other).
            if (!Camera.StumpsOnAir || _watchingEnd != keeperEnd) WatchStumps(keeperEnd, _ball.position, cut: true);
            // Test runs: a shot as the bails come off (the ball in the keeper's / bowler's hand on a take-and-break).
            // (A beat after the break, bails in the air: on the break frame itself the camera hadn't cut to the stumps
            // yet — F4 frame 141 was grass only.)
            Extra(After(0.08f, () => Apl.Client.Dev.AutoShot.Moment("bails_off")));
            // Test runs: was the runner and the crease in this frame (MatchDirector.RunOutSeen)?
            NoteRunOutFrame(keeperEnd);
            // The stumps and the bails (presentation's MatchSfx).
            _msfx.StumpsHit(new Vector3(0, 0.4f, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch));
            FieldRoutine(Shatter(w, Flat(dir).sqrMagnitude > 0.01f ? Flat(dir).normalized : (keeperEnd ? Vector3.back : Vector3.forward)));
        }

        private IEnumerator Shatter(Wicket3 w, Vector3 dir)
        {
            var axis = Vector3.Cross(Vector3.up, dir);
            var bails = new List<(Transform t, Vector3 v, Vector3 spin)>();
            foreach (var (t, _, _) in w.Bails)
            {
                if (t == null) continue;
                var v = dir * Random.Range(2.5f, 4.5f) + Vector3.up * Random.Range(2.5f, 4f) + axis * Random.Range(-1.2f, 1.2f);
                bails.Add((t, v, new Vector3(Random.Range(-900f, 900f), Random.Range(-400f, 400f), Random.Range(-900f, 900f))));
            }
            var lean = new List<float>();
            for (var i = 0; i < w.Stumps.Count; i++) lean.Add(Random.Range(10f, 24f) * (i == 1 ? 1.2f : 0.8f));
            // Until the stumps have settled and every bail is lying on the turf (never left in the air).
            for (var t = 0f; t < 1.4f || t < 4f && bails.Exists(x => x.t != null && (x.t.position.y > 0.021f || x.v != Vector3.zero)); t += Time.deltaTime)
            {
                // Stumps: knocked back about their base, a damped rattle.
                for (var i = 0; i < w.Stumps.Count && t < 1.4f; i++)
                {
                    var (s, pos, rot) = w.Stumps[i];
                    if (s == null) continue;
                    var angle = lean[i] * (1 - Mathf.Exp(-t * 18f)) + 4f * Mathf.Sin(t * 40f) * Mathf.Exp(-t * 6f);
                    var foot = pos - Vector3.up * s.lossyScale.y;
                    var q = Quaternion.AngleAxis(angle, axis);
                    s.SetPositionAndRotation(foot + q * (pos - foot), q * rot);
                }
                // Bails: up and away, spinning, then lying on the turf.
                for (var i = 0; i < bails.Count; i++)
                {
                    var (b, v, spin) = bails[i];
                    if (b == null || b.position.y <= 0.02f && v.y <= 0) continue;
                    v += Physics.gravity * Time.deltaTime;
                    var p = b.position + v * Time.deltaTime;
                    if (p.y < 0.02f)
                    {
                        p.y = 0.02f;
                        v = new Vector3(v.x * 0.3f, Mathf.Abs(v.y) * 0.2f, v.z * 0.3f);
                        if (v.y < 0.4f)
                        {
                            // At rest: lying flat on the grass.
                            v = Vector3.zero;
                            b.rotation = Quaternion.Euler(0, b.eulerAngles.y, 0);
                        }
                    }
                    b.position = p;
                    if (v != Vector3.zero) b.Rotate(spin * Time.deltaTime, Space.World);
                    bails[i] = (b, v, spin);
                }
                yield return null;
            }
        }

        /// <summary>
        /// How far (m) the batter racing to that end is past its popping crease (negative: short of it),
        /// by his hips — his bat and front foot are a little further on.
        /// </summary>
        private float PastCrease(bool keeperEnd)
        {
            var hips = BatterFor(keeperEnd);
            if (hips == null) return 0f;
            var line = GroundSpace.HalfPitch - 1.22f;
            return keeperEnd ? -line - hips.Value.z : hips.Value.z - line;
        }

        /// <summary>The batter racing to <paramref name="keeperEnd"/>'s crease (the one run out, if known).</summary>
        private Vector3? BatterFor(bool keeperEnd)
        {
            if (RunState.RunOut != null) return Hips(RunState.RunOut).position;
            // Deep area 3 (F4 frame 164: the camera framed the batter LEAVING that crease while the one run out was
            // 16 m short of it): the engine's out batter, else the one heading for that end on the last run.
            // (The rigs actually running this ball — RunState — not the id map: Unity review r1 measured a rig 17 m off.)
            var r = _hitResult;
            var st = RunState;
            var fromStriker = st.Ball == r && st.FromStrikerEnd != null ? st.FromStrikerEnd : _striker;
            var fromBowler = st.Ball == r && st.FromBowlerEnd != null ? st.FromBowlerEnd : _other;
            if (r != null && r.RunOut && fromStriker != null && fromBowler != null)
                return Hips(r.RunOutStriker ? fromStriker : fromBowler).position;
            if (r?.Running != null && (r.Attempted > 0 || r.TurnedBack) && fromStriker != null && fromBowler != null)
            {
                // On an odd run the striker makes for the bowler's end. Sent back on run k (Attempted = k − 1), each
                // makes for the end he left — the striker for the bowler's end again when Attempted is odd.
                var strikerToBowler = r.Attempted % 2 == 1;
                return Hips(strikerToBowler == keeperEnd ? fromBowler : fromStriker).position;
            }
            var crease = new Vector3(0, 0, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch);
            var a = Hips(_striker).position;
            var b = Hips(_other).position;
            return Vector3.Distance(Flat(a), crease) < Vector3.Distance(Flat(b), crease) ? a : b;
        }

        /// <summary>Side-on at the crease the throw's coming to, from the side away from the thrower.</summary>
        private void WatchStumps(bool keeperEnd, Vector3 thrower, bool cut = false)
        {
            var end = keeperEnd;
            _watchingEnd = keeperEnd;
            Camera.StumpsShot(() => _ball.position, new Vector3(0, 0, keeperEnd ? -GroundSpace.HalfPitch : GroundSpace.HalfPitch),
                () => BatterFor(end), thrower.x >= 0 ? -1f : 1f, cut);
        }

        /// <summary>Which end the stumps view is on (null: none yet this ball).</summary>
        private bool? _watchingEnd;
    }
}
