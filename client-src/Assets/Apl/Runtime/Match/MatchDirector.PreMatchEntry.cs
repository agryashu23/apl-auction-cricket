using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The players' entry (f49–60): the fielding side comes out of the pavilion
    /// in a loose group and spreads to its places, warming up as it gets there;
    /// the openers come out of the pavilion too, and the striker takes guard,
    /// settles and looks round the field. Then the intros (f61–84).
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The pavilion's bearing (StadiumBuilder: 270° = the ground's −x side).</summary>
        private static readonly Vector3 PavilionDir = Vector3.left;

        /// <summary>The gate: on the rope in front of the pavilion.</summary>
        private Vector3 Gate() => PavilionDir * ((float)_venue.RopeFromCentre(PavilionDir.x, PavilionDir.z) - 1.5f);

        private IEnumerator WalkOut()
        {
            var spin = _match.CurrentBowler.BowlType == BowlType.Spin;
            // The walk-out moves the fielders itself: match-play's field crossings stay out of it (one mover).
            BeginWalkOut();
            HudVisible(false);
            foreach (var a in _side.Values) a.gameObject.SetActive(true);
            Batters();
            // A frame for any rig that's just woken (its Start puts a batter at the crease).
            yield return null;
            // Everyone to his place first, to know where each is going.
            Roles();
            PlaceField();
            _bowler.Mark(spin);
            _keeper?.Stance(KeeperUp(_match.CurrentBowler));
            _striker.TakeGuard();
            PlaceOtherBatter();
            var fielding = _match.Bowling.XI.Where(p => _side.ContainsKey(p.Id)).Select(p => _side[p.Id])
                .Select(a => (a, to: a.transform.position, rot: a.transform.rotation)).ToList();
            var batting = new[] { _striker, _other }.Select(r => (r, r.transform.position, r.transform.rotation)).ToArray();
            foreach (var r in new[] { _striker, _other }) r.gameObject.SetActive(false);

            // Out of the pavilion: a loose group off the rope, from the gate's camera.
            _pre.ShowSkip(true);
            var side = _match.Bowling.TeamId == _bowl.Id ? _bowl : _bat;
            _pre.Caption($"{side.Name} take the field".ToUpperInvariant());
            var gate = Gate();
            var inward = -PavilionDir;
            var across = Vector3.Cross(Vector3.up, inward);
            var group = fielding.Select((f, i) =>
            {
                var start = gate + across * ((i % 4 - 1.5f) * 1.7f + (float)_visRng.Range(-0.4, 0.4)) + inward * (i / 4 * 1.6f + (float)_visRng.Range(0, 0.6));
                return StartWalk(f.a, start, f.to, f.rot, FieldGait(f.a), () => StartCoroutine(WarmUp(f.a)));
            }).ToList();
            Camera.Track(() =>
            {
                var c = Centroid(group);
                return (gate + inward * 15f + across * 4f + Vector3.up * 1.7f, c + Vector3.up * 1.1f);
            }, fov: 42f, smooth: 0.4f, cut: true);
            yield return Walkers(group, Secs(5f), finish: false);
            Halt(group);

            // Cut: spreading to their places (the last stretch of each one's walk), from high on the pavilion.
            _pre.ResetSkip();
            var spread = fielding.Select(f => StartWalk(f.a, Short(f.a.transform.position, f.to, 14f), f.to, f.rot, FieldGait(f.a), () => StartCoroutine(WarmUp(f.a)))).ToList();
            Camera.Track(() => (gate * 0.8f + Vector3.up * 17f, inward * 8f), fov: 52f, smooth: 0.5f, cut: true);
            yield return Walkers(spread, Secs(9f));
            yield return Hold(Secs(1.5f));

            // The openers, out of the pavilion and on to their ends; the striker takes guard.
            _pre.ResetSkip();
            // A jog, a warm-up, a glove punch at the middle, each to his end (MatchDirector.PreMatchOpeners.cs).
            yield return OpenersEntry(_striker, _other, batting[0].Item2, batting[0].Item3, batting[1].Item2, batting[1].Item3);
            PlaceField();
            _bowler.Mark(spin);
            _keeper?.Stance(KeeperUp(_match.CurrentBowler));
            PlaceOtherBatter();
            yield return TakeGuardRoutine(_striker);
            _pre.Caption(null);
            _pre.ShowSkip(false);
            HudVisible(true);
            _walkOutOn = false;
        }

        private static Vector3 Centroid(List<Walker> w)
        {
            var sum = Vector3.zero;
            foreach (var x in w) sum += x.A.transform.position;
            return w.Count == 0 ? Vector3.zero : sum / w.Count;
        }

        /// <summary>
        /// Batters out of the pavilion: a few seconds from the gate with the camera
        /// in front of them, then (a cut) the last metres to their places.
        /// </summary>
        private IEnumerator BatterEntry(IReadOnlyList<(BatterRig rig, Vector3 to, Quaternion rot)> batters, string caption, float fromGate)
        {
            StallProbe.Mark($"batter entry: {caption}");
            _pre.ShowSkip(true);
            _pre.Caption(caption.ToUpperInvariant());
            var gate = Gate();
            var across = Vector3.Cross(Vector3.up, -PavilionDir);
            var gaits = batters.Select(b => BatGait(b.rig.GetComponent<Animator>(), b.rig.LeftHanded)).ToList();
            var walking = new List<Walker>();
            for (var i = 0; i < batters.Count; i++)
            {
                var (rig, to, rot) = batters[i];
                rig.gameObject.SetActive(true);
                rig.Carry();
                var r = rig;
                walking.Add(StartWalk(rig.GetComponent<Animator>(), gate + across * (i * 1.3f - 0.65f), to, rot, gaits[i], () => r.Carry()));
            }
            var lead = Hips(batters[0].rig);
            Camera.Track(() =>
            {
                var h = lead.position;
                var heading = (batters[0].to - new Vector3(h.x, 0, h.z)).normalized;
                return (new Vector3(h.x, 0, h.z) + heading * 5.5f + Vector3.up * 1.6f, h + Vector3.up * 0.35f);
            }, fov: 38f, smooth: 0.35f, cut: true);
            yield return Walkers(walking, fromGate, finish: false);
            Halt(walking);
            _pre.ResetSkip();
            var last = new List<Walker>();
            for (var i = 0; i < batters.Count; i++)
            {
                var (rig, to, rot) = batters[i];
                var a = rig.GetComponent<Animator>();
                var r = rig;
                last.Add(StartWalk(a, Short(a.transform.position, to, 9f), to, rot, gaits[i], () => r.Carry()));
            }
            Camera.Track(() =>
            {
                var h = lead.position;
                return (new Vector3(h.x, 0, h.z) + across * 3.2f + (batters[0].to - new Vector3(h.x, 0, h.z)).normalized * 3.2f + Vector3.up * 1.5f, h + Vector3.up * 0.3f);
            }, fov: 36f, smooth: 0.3f, cut: true);
            yield return Walkers(last, Secs(7f));
            _pre.Caption(null);
            // SKIP goes with the walk-out (a new batter's left it up through the live balls after: r10 f84/88/204).
            _pre.ShowSkip(false);
        }
    }
}
