using System;
using System.Collections;
using System.Linq;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Deep area 1 (sliding) repros for test runs. <c>-aplfieldburst</c> (autoplay, the innings the user's side bowls):
    /// before EVERY ball the field preset changes (a cycle of very different fields), so every fielder crosses
    /// 10-40 m to his new spot on every ball — the owner's "changing field location" scenario, filmed and metered
    /// ball after ball ("[APL slideburst]" per change; the slide meter books them as "fielder field change").
    /// Never in a real match.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private static readonly bool FieldBurstOn = Environment.GetCommandLineArgs().Contains("-aplfieldburst");

        /// <summary>Fields far apart from each other, in turn.</summary>
        private static readonly FieldPreset[] BurstCycle =
        {
            FieldPreset.Attacking, FieldPreset.Defensive, FieldPreset.OffSideTrap, FieldPreset.LegSideTrap, FieldPreset.SaveOne, FieldPreset.Death,
        };

        private int _fieldBurstN;

        private IEnumerator FieldBurst()
        {
            if (!FieldBurstOn || !Autoplay || _match.UserBatting || _match.Finished) yield break;
            var next = BurstCycle[_fieldBurstN++ % BurstCycle.Length];
            if (next == _match.Preset) next = BurstCycle[_fieldBurstN++ % BurstCycle.Length];
            _match.Preset = next;
            var before = _fielders.Where(f => f.a != null && f.a.gameObject.activeSelf).Select(f => (f.a, at: Flat(f.a.transform.position))).ToList();
            var n = _fieldBurstN;
            var run = StartCoroutine(RunToNewField());
            // The first few changes on film: a side-on camera at shin height on the man with the longest way to go
            // (his feet against the turf), three frames over the first 2.4 s of his crossing.
            if (n <= 6)
            {
                yield return null;
                var who = _fielders.Where(f => f.a != null && f.a.gameObject.activeSelf && Crossing(f.a))
                    .OrderByDescending(f => _crossing.TryGetValue(f.a, out var c) ? Vector3.Distance(Flat(f.a.transform.position), c.to) : 0f)
                    .Select(f => f.a).FirstOrDefault();
                if (who != null)
                {
                    var t = who.transform;
                    Camera.Track(() =>
                    {
                        var side = Vector3.Cross(Vector3.up, Flat(t.forward)).normalized;
                        var p = t.position;
                        return (p + side * 4.2f + Vector3.up * 0.9f, p + Vector3.up * 0.45f);
                    }, fov: 34f, smooth: 0.15f, cut: true);
                    for (var k = 1; k <= 3; k++)
                    {
                        var shot = $"fieldburst{n:00}_{k}";
                        StartCoroutine(AfterReal(0.8f * k, () => Apl.Client.Dev.AutoShot.Moment(shot)));
                    }
                }
            }
            yield return run;
            Camera.Show(MatchCamera.Mode.Wide);
            var moved = before.Count(b => b.a != null && Vector3.Distance(b.at, Flat(b.a.transform.position)) > 3f);
            Debug.Log($"[APL slideburst] field change before over {_match.Over} ball {_match.BallInOver + 1}: {Field.PresetName(next)}, {moved} of {before.Count} fielders crossed over 3 m");
        }
    }
}
