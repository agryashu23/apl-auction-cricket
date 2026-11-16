using System.Linq;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// A dive that gets a hand to it and can't stop it: the engine palms the ball
    /// on (slower) at <see cref="Field.BallPath.DeflectAt"/>, and the fielder who
    /// went for it is shown diving across it at that moment — so a ball never runs
    /// past a fielder who could have reached it without him going for it.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The stand-in ball the beaten fielder dives at (the real one runs on).</summary>
        private Transform? _palmBall;

        private void BeatenDive(BallResult r)
        {
            var name = r.FieldPlay?.Beaten ?? "";
            var at = (float)r.Path.DeflectAt;
            var diver = NamedFielder(name);
            if (diver == null || at <= 0 || diver == _chaser || (_play != null && _play.Fielders.Contains(diver))) return;
            var b = Situation(r, KeeperTarget, false);
            b.Due = at;
            b.Dived = true;
            // A dive and no throw (the full-length stops; the sequences get up and throw).
            var dives = SeqClip.All.Where(c => c.Style == FieldStyle.Dive && c.Collects && !c.HasThrow);
            var dive = FieldPlay.PlanGround(diver, b, dives);
            if (dive == null) return;
            // The hand reaches it as it goes past: exactly the engine's moment.
            dive.EventAt = at;
            dive.JoinTime = at - (dive.Clip.EventTime - dive.JoinClip);
            _palmBall ??= new GameObject("PalmBall").transform;
            FieldRoutine(dive.Play(this, _hitAt, _palmBall, RunFor, NoThrow, false));
            if (Autoplay) Debug.Log($"[APL match] {name} dives ({dive.Clip.Name}) and is beaten at {at:0.00}s");
        }
    }
}
