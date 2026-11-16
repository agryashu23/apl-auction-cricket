using Apl.Client.Ui;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The AI captain made visible (match-play): when the user bats, each new over
    /// names the bowling change and any change of field (QuickMatch.FieldPresetNow
    /// is the captain's pick for the over), so his captaincy reads on screen.
    /// </summary>
    public sealed partial class MatchDirector
    {
        private FieldPreset? _aiFieldShown;
        private string? _aiBowlerShown;

        /// <summary>At the start of an over the user bats in: "BOWLING CHANGE" / "FIELD CHANGE" when something changed.</summary>
        private void AnnounceAiCaptaincy()
        {
            if (!_match.UserBatting) return;
            var preset = _match.FieldPresetNow;
            var bowler = _match.CurrentBowler;
            var newBowler = _aiBowlerShown != null && _aiBowlerShown != bowler.Id;
            var newField = _aiFieldShown != null && _aiFieldShown != preset;
            // The first over of the innings just sets the baseline (the intros have named the bowler).
            if (_aiBowlerShown == null || _aiFieldShown == null || _match.LegalBalls == 0)
            {
                _aiBowlerShown = bowler.Id;
                _aiFieldShown = preset;
                return;
            }
            _aiBowlerShown = bowler.Id;
            _aiFieldShown = preset;
            var fieldName = preset == FieldPreset.Auto ? "STANDARD FIELD" : Field.PresetName(preset) + " FIELD";
            if (newBowler && newField) _hud.Banner("BOWLING CHANGE", $"{bowler.Name} · {Bowling.StyleName(bowler)} · {fieldName}", UiKit.Gold, 2.4f);
            else if (newBowler) _hud.Banner("BOWLING CHANGE", $"{bowler.Name} · {Bowling.StyleName(bowler)}", UiKit.Gold, 2.2f);
            else if (newField) _hud.Banner("FIELD CHANGE", fieldName, UiKit.Gold, 2.2f);
        }
    }
}
