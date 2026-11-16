using System.Collections;
using System.Linq;
using Apl.Core;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Field rework evidence (TEST RUNS ONLY, <c>-aplshots &lt;dir&gt; -aplfieldmap</c>): once, at the field screen of
    /// the user's second bowling over, every named field (AUTO first) is set in turn; the men walk to it, then a
    /// straight-down field map (<see cref="Apl.Client.Dev.FieldMap"/>) and a moment of the game's own view are taken,
    /// and each man's spot is logged as <c>[APL fieldmap]</c> (metres from the bat and from the pitch). The field the
    /// over started with comes back after.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private bool _fieldMapToured;

        private IEnumerator FieldMapTour()
        {
            if (!Apl.Client.Dev.FieldMap.On || !Autoplay || _fieldMapToured || _match.UserBatting) yield break;
            _fieldMapToured = true;
            var was = _match.Preset;
            var spin = _match.CurrentBowler.BowlType == BowlType.Spin;
            var lh = _match.OnStrike.Player.LeftHanded;
            foreach (var p in Field.Presets)
            {
                _match.Preset = p;
                yield return RunToNewField();
                yield return new WaitForSeconds(0.4f);
                var field = _match.Field;
                var tag = p.ToString().ToLowerInvariant() + (spin ? "_spin" : "_pace") + "_ph" + _match.Phase;
                Debug.Log($"[APL fieldmap] {tag} ({Field.PresetName(p)}): " + string.Join(", ", field.Fielders.Select(f =>
                    $"{f.Name} {f.AngleDeg:0}° {f.Radius:0.0} m (pitch {Field.FromPitch(f.AngleDeg, f.Radius):0.0}{(Field.OutsideRing(f) ? ", out" : "")})")));
                Apl.Client.Dev.FieldMap.Capture(tag, _fielders.Where(x => x.a != null && x.a.gameObject.activeSelf).Select(x => x.home).ToList(), lh);
                Apl.Client.Dev.AutoShot.Moment("fieldmap_view_" + tag);
                yield return new WaitForSeconds(0.3f);
            }
            _match.Preset = was;
            yield return RunToNewField();
        }
    }
}
